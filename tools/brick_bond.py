"""煉瓦タイルの面を、実物の張り方・1 枚ずつの色・目地で組み直す (#109)。

葛飾キャンパスの外壁は二丁掛の割肌タイルを芋張り（縦目地が上下の段でそろう）にしたもので、
タイルは 1 枚ずつ色が違い（赤茶・橙・灰・薄灰・焦げ茶）、目地は細く暗い線に見える。
ambientCG の煉瓦素材はどれも馬張りで、目地は明るいモルタルなので、そのままでは合わない。

ここでは素材の写真から**煉瓦 1 枚ずつの肌（明暗の粒）だけ**を切り出し、
格子に並べ直して、1 枚ずつ palette の色を掛け、細い目地を暗く塗る。
平均色の合わせ込み（retint）は fetch_textures.py が最後に行う。

素材の格子は Displacement（高いほど手前）から測る。目地は奥にあるので暗い。
段の数と 1 段の枚数は素材ごとに決まっている（Bricks092 は 18 段 × 5 枚）ので、
測るのは位相（どこから始まるか）と目地の幅だけでよい。
"""

from __future__ import annotations

import hashlib

import numpy as np
from PIL import Image

# 位相を探す刻み（px）。素材の段のピッチは整数にならない（1024 / 18 = 56.9 px）。
PHASE_STEP = 0.25

# 目地の幅を測るときの、谷の底から山までの何割を目地とみなすか。
JOINT_LEVEL = 0.5

# 目地から内側へさらに削る画素数。目地の縁のぼけたモルタルを煉瓦に持ち込まない。
EDGE_GUARD = 2

# 組むときの拡大率。格子を 2 倍で描いてから 2×2 の平均で縮め、目地の縁をなめらかにする。
SUPERSAMPLE = 2


def to_linear(srgb: np.ndarray) -> np.ndarray:
    return np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)


def to_srgb(linear: np.ndarray) -> np.ndarray:
    linear = np.clip(linear, 0.0, 1.0)
    return np.where(linear <= 0.0031308, linear * 12.92, 1.055 * linear ** (1 / 2.4) - 0.055)


def hex_linear(hex_text: str) -> np.ndarray:
    text = hex_text.lstrip("#")
    if len(text) != 6:
        raise ValueError(f"色は RRGGBB で書く: {hex_text}")

    return to_linear(np.array([int(text[i : i + 2], 16) for i in (0, 2, 4)], dtype=np.float64) / 255)


# ---------------------------------------------------------------------------
# 素材の格子を測る
# ---------------------------------------------------------------------------


def fold_phase(profile: np.ndarray, count: int) -> tuple[float, float]:
    """周期 len/count の繰り返しの谷（目地の中心）の位相と、谷の幅（px）を返す。

    profile は 1 周分（端がつながっている）。位相 φ ごとに φ + k·pitch の値を平均し、
    最も低い φ を谷とする。幅は、谷の底と山の中間より低い位相の長さ。
    """
    length = len(profile)
    pitch = length / count
    phases = np.arange(0.0, pitch, PHASE_STEP)
    positions = (phases[:, None] + np.arange(count)[None, :] * pitch) % length
    lower = np.floor(positions).astype(int)
    frac = positions - lower
    values = profile[lower] * (1 - frac) + profile[(lower + 1) % length] * frac
    folded = values.mean(axis=1)

    bottom, top = folded.min(), folded.max()
    if top - bottom <= 1e-9:
        raise ValueError("目地の谷が見つからない（凹凸が平ら）")

    level = bottom + (top - bottom) * JOINT_LEVEL
    width = float((folded < level).sum()) * PHASE_STEP
    return float(phases[int(np.argmin(folded))]), width


def find_grid(height: np.ndarray, courses: int, per_course: int) -> dict:
    """凹凸マップ（2 次元、高いほど手前）から格子を測る。

    返す dict: pitch_y / pitch_x（px）、row_phase（横目地の中心の y）、
    col_phases（段ごとの縦目地の中心の x）、joint_y / joint_x（目地の幅、px）。
    """
    rows, cols = height.shape
    row_phase, joint_y = fold_phase(height.mean(axis=1), courses)
    pitch_y = rows / courses
    pitch_x = cols / per_course

    col_phases: list[float] = []
    joint_x = 0.0
    for course in range(courses):
        # 段の中ほど 6 割だけで縦目地を探す。横目地の行が混ざると谷が浅くなる。
        top = row_phase + course * pitch_y + pitch_y * 0.2
        band = [int(y) % rows for y in np.arange(top, top + pitch_y * 0.6)]
        phase, width = fold_phase(height[band].mean(axis=0), per_course)
        col_phases.append(phase)
        joint_x = max(joint_x, width)

    return {
        "pitch_y": pitch_y,
        "pitch_x": pitch_x,
        "row_phase": row_phase,
        "col_phases": col_phases,
        "joint_y": joint_y,
        "joint_x": joint_x,
    }


def cut_bricks(color: np.ndarray, grid: dict, courses: int, per_course: int) -> list[np.ndarray]:
    """素材の写真から煉瓦 1 枚ずつの内側（目地を除いた矩形）を切り出す。端をまたぐ煉瓦もつなぐ。"""
    rows, cols = color.shape[:2]
    inset_y = grid["joint_y"] / 2 + EDGE_GUARD
    inset_x = grid["joint_x"] / 2 + EDGE_GUARD
    height = int(grid["pitch_y"] - inset_y * 2)
    width = int(grid["pitch_x"] - inset_x * 2)
    if height < 4 or width < 4:
        raise ValueError("目地が太すぎて煉瓦の内側が残らない")

    bricks = []
    for course in range(courses):
        y0 = grid["row_phase"] + course * grid["pitch_y"] + inset_y
        ys = (np.floor(y0).astype(int) + np.arange(height)) % rows
        for index in range(per_course):
            x0 = grid["col_phases"][course] + index * grid["pitch_x"] + inset_x
            xs = (np.floor(x0).astype(int) + np.arange(width)) % cols
            bricks.append(color[np.ix_(ys, xs)])

    return bricks


# ---------------------------------------------------------------------------
# 並べ直す
# ---------------------------------------------------------------------------


def grain(brick: np.ndarray, strength: float) -> np.ndarray:
    """煉瓦 1 枚の明暗の粒（平均 1 の倍率、線形）。strength は写真の粒を残す割合。"""
    luminance = to_linear(brick / 255.0) @ np.array([0.2126, 0.7152, 0.0722])
    mean = luminance.mean()
    if mean <= 0:
        return np.ones_like(luminance)

    return 1.0 + (luminance / mean - 1.0) * strength


def seeded_rng(name: str) -> np.random.Generator:
    """素材名から決まる乱数。同じ指定なら誰が何度焼いても同じ画像になる。"""
    return np.random.default_rng(int.from_bytes(hashlib.sha256(name.encode("utf-8")).digest()[:8], "little"))


def deal_colors(rng: np.random.Generator, weights: np.ndarray, rows: int, cols: int, block: int) -> np.ndarray:
    """煉瓦ごとに palette の番号を配る。block 段ずつ、割合どおりの枚数を混ぜて配る。

    1 枚ずつ独立に引くと、同じ色が固まる場所がたまたまでき、タイルを並べたときに
    その固まりが周期として目に付く。block 段ごとに枚数を割合に合わせれば、固まりは
    実物の程度（縦に 2〜3 段続く程度）に収まる。
    """
    dealt = np.empty((rows, cols), dtype=int)
    for top in range(0, rows, block):
        count = min(block, rows - top) * cols
        exact = weights * count
        counts = np.floor(exact).astype(int)
        # 端数は、切り捨てた量の大きい色から 1 枚ずつ足す
        for extra in np.argsort(-(exact - counts))[: count - counts.sum()]:
            counts[extra] += 1

        deck = np.repeat(np.arange(len(weights)), counts)
        rng.shuffle(deck)
        dealt[top : top + count // cols] = deck.reshape(-1, cols)

    return dealt


def resize_brick(brick: np.ndarray, width: int, height: int, flip: bool) -> np.ndarray:
    image = Image.fromarray(brick.astype(np.uint8))
    if flip:
        image = image.transpose(Image.FLIP_LEFT_RIGHT)

    return np.asarray(image.resize((width, height), Image.LANCZOS), dtype=np.float64)


def compose(color: Image.Image, height: Image.Image, spec: dict, size: int, name: str) -> Image.Image:
    """素材の写真と凹凸マップから、芋張りの煉瓦の面を size px 四方で組む。

    spec（surfaces.json の "bricks"）:
      source_grid: [段の数, 1 段の枚数]（素材の格子）
      grid: [段の数, 1 段の枚数]（組む格子。1 枚の縦横比は size/段 と size/枚 で決まる）
      joint: 目地の幅（段のピッチに対する割合）
      grain: 写真の粒を残す割合
      palette: [{"hex": ..., "weight": ...}, ...]（1 枚ずつの色。平均は retint が合わせる）
      joint_ratio: 目地の明るさ（palette の加重平均に対する倍率、線形）
    """
    source_courses, source_per_course = spec["source_grid"]
    courses, per_course = spec["grid"]
    source = np.asarray(color.convert("RGB"), dtype=np.float64)
    grid = find_grid(np.asarray(height.convert("L"), dtype=np.float64), source_courses, source_per_course)
    bricks = cut_bricks(source, grid, source_courses, source_per_course)

    palette = np.array([hex_linear(entry["hex"]) for entry in spec["palette"]])
    weights = np.array([float(entry["weight"]) for entry in spec["palette"]])
    if weights.min() < 0 or weights.sum() <= 0:
        raise ValueError("palette の weight は 0 以上で、合計が正でないといけない")

    weights = weights / weights.sum()
    joint_color = (palette * weights[:, None]).sum(axis=0) * float(spec["joint_ratio"])

    rng = seeded_rng(name)
    dealt = deal_colors(rng, weights, courses, per_course, int(spec.get("block", 2)))

    side = size * SUPERSAMPLE
    pitch_y = side / courses
    pitch_x = side / per_course
    joint = max(1, round(pitch_y * float(spec["joint"])))
    canvas = np.empty((side, side, 3), dtype=np.float64)
    canvas[:] = joint_color

    for course in range(courses):
        # 目地を格子の線の上に置く。タイルの端がちょうど目地の中心になり、並べてもつながる。
        y0 = round(course * pitch_y + joint / 2)
        y1 = round((course + 1) * pitch_y - joint / 2)
        for index in range(per_course):
            x0 = round(index * pitch_x + joint / 2)
            x1 = round((index + 1) * pitch_x - joint / 2)
            piece = resize_brick(bricks[rng.integers(len(bricks))], x1 - x0, y1 - y0, bool(rng.integers(2)))
            canvas[y0:y1, x0:x1] = grain(piece, float(spec["grain"]))[..., None] * palette[dealt[course, index]]

    small = canvas.reshape(size, SUPERSAMPLE, size, SUPERSAMPLE, 3).mean(axis=(1, 3))
    return Image.fromarray(np.round(to_srgb(small) * 255).astype(np.uint8), "RGB")

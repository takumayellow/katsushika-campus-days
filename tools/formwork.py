"""打放しコンクリートの壁の面を、型枠の割付けから手続きで描く (#423)。

講義棟の打放しの壁は、平らな 1 色のままだと塗装した壁と見分けが付かない。打放しらしさは
型枠の跡で決まる。合板の型枠を外した面には、

- 型枠のパネルどうしの継ぎ目（目地）が細く暗い線で残り、
- 型枠を留めていたセパレータの穴（P コンの跡）が、パネル 1 枚に 2 段ずつ小さく暗い丸で並び、
- パネルごとに打ち上がりの明るさが少しずつ違い、面の中にも控えめなムラと気泡が出る。

ここではそれを素材写真に頼らず、割付けの寸法（mm）からそのまま描く。1 枚の画像が何 cm 四方かは
surfaces.json の tile_cm で、Blender が壁にメートル単位の UV を書くので、目地と穴は実寸で並ぶ。

描くのは明るさの倍率だけで、色は付けない。平均色は fetch_textures.retint が宣言 hex に
チャンネルごとのゲインで合わせる（線形の倍率なので、明るさの比はそのまま残る）。

どの要素もタイルの端で切れないように描く。ムラは FFT で作るので周期的になり、目地・穴・気泡は
周期的な距離で描く。目地の位置は割付けの半ピッチずらしてタイルの端から離す。
"""

from __future__ import annotations

import numpy as np
from PIL import Image

from brick_bond import SUPERSAMPLE, seeded_rng, to_srgb

# 色を付ける前の明るさ（線形）。retint があとで宣言 hex へ合わせるので、ゲインが 1 付近で済む
# 中ほどの値にしておく。白飛びも黒つぶれもしない。
BASE_LINEAR = 0.4


def periodic_noise(rng: np.random.Generator, side: int, scale_px: float) -> np.ndarray:
    """周期的なムラ（平均 0・標準偏差 1）。白色雑音をガウスで低域に絞る。

    FFT の上で絞るので、出来上がりは上下・左右につながっている（タイルの継ぎ目が出ない）。
    scale_px はムラの粒の大きさ（ガウスの標準偏差、画素）。
    """
    noise = rng.standard_normal((side, side))
    freq = np.fft.fftfreq(side)
    radius2 = freq[:, None] ** 2 + freq[None, :] ** 2
    smooth = np.real(np.fft.ifft2(np.fft.fft2(noise) * np.exp(-2.0 * (np.pi * scale_px) ** 2 * radius2)))
    std = smooth.std()
    return smooth / std if std > 1e-12 else smooth


def wrapped_distance(coords: np.ndarray, start: float, pitch: float) -> np.ndarray:
    """coords から、start + k·pitch に並ぶ線までの最短距離（mm）。

    pitch がタイルの大きさを割り切ること（check_layout が確かめる）で、タイルの端をまたいでも
    間隔が崩れない。
    """
    offset = np.mod(coords - start, pitch)
    return np.minimum(offset, pitch - offset)


def coverage(distance: np.ndarray, half_width: float) -> np.ndarray:
    """線や丸の中なら 1、外なら 0。縁は 1 px で滑らかにつなぐ（縮めたときに段が出ない）。"""
    return np.clip(half_width - distance + 0.5, 0.0, 1.0)


def check_layout(spec: dict, tile_mm: float) -> tuple[float, float, float]:
    """割付けがタイルの大きさで割り切れることを確かめ、(パネル幅, パネル高さ, 穴のピッチ) を返す。"""
    panel_w, panel_h = (float(v) for v in spec["panel_mm"])
    pitch = float(spec["pcon_pitch_mm"])
    for what, length, unit in (
        ("パネルの幅", tile_mm, panel_w),
        ("パネルの高さ", tile_mm, panel_h),
        ("P コンの横の間隔", panel_w, pitch),
        ("P コンの縦の間隔", panel_h, pitch),
    ):
        count = length / unit
        if unit <= 0 or abs(count - round(count)) > 1e-6 or round(count) < 1:
            raise ValueError(f"{what} {unit} mm が {length} mm を割り切らない（タイルの端で模様が切れる）")

    return panel_w, panel_h, pitch


def compose(spec: dict, size: int, tile_cm: float, name: str) -> Image.Image:
    """型枠の跡が付いた打放しの面を、size px 四方（tile_cm 四方の実寸）で描く。

    spec（surfaces.json の "formwork"。長さは mm、明るさは線形の倍率）:
      panel_mm: [幅, 高さ]（型枠のパネル 1 枚。横使いの合板なら [1820, 910]）
      joint_mm / joint_ratio: 目地の幅と、目地の中の明るさ
      pcon_mm / pcon_ratio: P コンの穴の直径と、穴の中の明るさ
      pcon_pitch_mm: 穴の間隔（縦横とも。パネルの縁から半ピッチ内側に並ぶ）
      mottle: [{"scale_mm": ..., "amount": ...}, ...]（ムラの粒の大きさと強さ）
      panel_tone: パネルごとの明るさのばらつき（標準偏差）
      grain: 細かい粒の強さ
      pores_per_m2 / pore_mm / pore_ratio: 気泡の数・直径・明るさ
    """
    tile_mm = float(tile_cm) * 10.0
    panel_w, panel_h, pitch = check_layout(spec, tile_mm)

    side = size * SUPERSAMPLE
    mm_per_px = tile_mm / side
    rng = seeded_rng(name)
    coords = (np.arange(side) + 0.5) * mm_per_px  # 画素の中心の位置（mm）
    y, x = coords[:, None], coords[None, :]

    # ムラ（大きな雲と、こまかい粒）
    shade = np.ones((side, side))
    for layer in spec["mottle"]:
        shade += float(layer["amount"]) * periodic_noise(rng, side, float(layer["scale_mm"]) / mm_per_px)
    shade += float(spec["grain"]) * periodic_noise(rng, side, 0.6 / mm_per_px)

    # 目地はパネルの半分ずらした位置に置き、タイルの端から離す
    joint_x0, joint_y0 = panel_w / 2, panel_h / 2

    # パネルごとの明るさ。どのパネルに入るかは目地の格子で決まる
    cols, rows = round(tile_mm / panel_w), round(tile_mm / panel_h)
    tones = 1.0 + float(spec["panel_tone"]) * rng.standard_normal((rows, cols))
    row = (np.floor(np.mod(y - joint_y0, tile_mm) / panel_h).astype(int)) % rows
    col = (np.floor(np.mod(x - joint_x0, tile_mm) / panel_w).astype(int)) % cols
    shade *= tones[row, col]

    # 目地（横と縦）
    half_joint = float(spec["joint_mm"]) / 2 / mm_per_px
    joint_y = coverage(wrapped_distance(y, joint_y0, panel_h) / mm_per_px, half_joint)
    joint_x = coverage(wrapped_distance(x, joint_x0, panel_w) / mm_per_px, half_joint)
    joint = np.maximum(joint_y, joint_x)
    shade *= 1.0 - joint * (1.0 - float(spec["joint_ratio"]))

    # P コンの穴（パネルの縁から半ピッチ内側、pitch ごと）
    hole_dx = wrapped_distance(x, joint_x0 + pitch / 2, pitch)
    hole_dy = wrapped_distance(y, joint_y0 + pitch / 2, pitch)
    hole = coverage(np.hypot(hole_dx, hole_dy) / mm_per_px, float(spec["pcon_mm"]) / 2 / mm_per_px)
    shade *= 1.0 - hole * (1.0 - float(spec["pcon_ratio"]))

    shade *= pores(rng, spec, side, mm_per_px, tile_mm)

    linear = BASE_LINEAR * np.clip(shade, 0.0, None)
    small = linear.reshape(size, SUPERSAMPLE, size, SUPERSAMPLE).mean(axis=(1, 3))
    gray = np.round(to_srgb(small) * 255).astype(np.uint8)
    return Image.fromarray(np.stack([gray] * 3, axis=-1), "RGB")


def pores(rng: np.random.Generator, spec: dict, side: int, mm_per_px: float, tile_mm: float) -> np.ndarray:
    """気泡の跡（小さな暗い点）の倍率。点はタイルの端をまたいでも反対側へ回り込む。"""
    factor = np.ones((side, side))
    count = round(float(spec["pores_per_m2"]) * (tile_mm / 1000.0) ** 2)
    ratio = float(spec["pore_ratio"])
    for _ in range(count):
        radius = float(spec["pore_mm"]) / 2 * rng.uniform(0.5, 1.0) / mm_per_px
        cy, cx = rng.uniform(0, side, size=2)
        reach = int(np.ceil(radius + 1))
        ys = np.arange(int(cy) - reach, int(cy) + reach + 2)
        xs = np.arange(int(cx) - reach, int(cx) + reach + 2)
        distance = np.hypot(ys[:, None] + 0.5 - cy, xs[None, :] + 0.5 - cx)
        cell = np.ix_(ys % side, xs % side)
        factor[cell] = np.minimum(factor[cell], 1.0 - coverage(distance, radius) * (1.0 - ratio))
    return factor

"""numpy で顔テクスチャと絣パターンを描く。

Blender の `Image.pixels` は左下原点・行優先・RGBA float なので、配列も
`arr[row, col]`（row=0 が下端）で持ち、そのまま `foreach_set` に流す。
色は PNG に書く値なので sRGB のまま扱う（リニア変換はしない）。
"""

from __future__ import annotations

import math

import numpy as np


# --------------------------------------------------------------------------
# 基本
# --------------------------------------------------------------------------


def hex_rgb(h: str) -> tuple[float, float, float]:
    """`#RRGGBB` を 0..1 の sRGB 値へ。画像は sRGB タグで書き出す。"""
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))  # type: ignore[return-value]


def canvas(size: int, rgb=(1.0, 1.0, 1.0), alpha: float = 1.0) -> np.ndarray:
    a = np.empty((size, size, 4), dtype=np.float32)
    a[..., 0], a[..., 1], a[..., 2] = rgb
    a[..., 3] = alpha
    return a


def _sub(size: int, x0: float, x1: float, y0: float, y1: float):
    c0 = max(0, int(math.floor(x0 * size)) - 2)
    c1 = min(size, int(math.ceil(x1 * size)) + 2)
    r0 = max(0, int(math.floor(y0 * size)) - 2)
    r1 = min(size, int(math.ceil(y1 * size)) + 2)
    if c1 <= c0 or r1 <= r0:
        return None
    cols = (np.arange(c0, c1, dtype=np.float32) + 0.5) / size
    rows = (np.arange(r0, r1, dtype=np.float32) + 0.5) / size
    X, Y = np.meshgrid(cols, rows)
    return r0, r1, c0, c1, X, Y


def _paint(arr: np.ndarray, box, mask: np.ndarray, rgb, alpha: float = 1.0) -> None:
    r0, r1, c0, c1 = box
    m = (mask * alpha)[..., None].astype(np.float32)
    region = arr[r0:r1, c0:c1, :3]
    col = np.asarray(rgb, dtype=np.float32).reshape(1, 1, 3)
    arr[r0:r1, c0:c1, :3] = region * (1.0 - m) + col * m
    arr[r0:r1, c0:c1, 3] = np.maximum(arr[r0:r1, c0:c1, 3], mask * alpha)


def _smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-9), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# --------------------------------------------------------------------------
# プリミティブ
# --------------------------------------------------------------------------


def ellipse(arr, cx, cy, rx, ry, rgb, *, rot=0.0, power=2.0, alpha=1.0,
            soft=0.0018, feather=0.0):
    size = arr.shape[0]
    rr = max(rx, ry) * 1.6 + feather + 0.01
    s = _sub(size, cx - rr, cx + rr, cy - rr, cy + rr)
    if s is None:
        return
    r0, r1, c0, c1, X, Y = s
    dx, dy = X - cx, Y - cy
    c, sn = math.cos(rot), math.sin(rot)
    u = (dx * c + dy * sn) / max(rx, 1e-6)
    v = (-dx * sn + dy * c) / max(ry, 1e-6)
    d = (np.abs(u) ** power + np.abs(v) ** power) ** (1.0 / power)
    if feather > 0.0:
        m = np.clip(1.0 - d, 0.0, 1.0) ** (feather * 40.0 + 1.0)
        m = _smoothstep(0.0, 1.0, m)
    else:
        e = soft / max(rx, ry, 1e-6)
        m = _smoothstep(1.0 + e, 1.0 - e, d)
    _paint(arr, (r0, r1, c0, c1), m.astype(np.float32), rgb, alpha)


def ring(arr, cx, cy, rx, ry, width, rgb, *, rot=0.0, power=2.0, alpha=1.0):
    size = arr.shape[0]
    rr = max(rx, ry) * 1.6 + width + 0.01
    s = _sub(size, cx - rr, cx + rr, cy - rr, cy + rr)
    if s is None:
        return
    r0, r1, c0, c1, X, Y = s
    dx, dy = X - cx, Y - cy
    c, sn = math.cos(rot), math.sin(rot)
    u = (dx * c + dy * sn) / max(rx, 1e-6)
    v = (-dx * sn + dy * c) / max(ry, 1e-6)
    d = (np.abs(u) ** power + np.abs(v) ** power) ** (1.0 / power)
    w = width / max(rx, ry)
    e = 0.004 / max(rx, ry)
    m = _smoothstep(1.0 + e, 1.0 - e, d) * _smoothstep(1.0 - w - e, 1.0 - w + e, d)
    _paint(arr, (r0, r1, c0, c1), m.astype(np.float32), rgb, alpha)


def polyline(arr, pts, width, rgb, *, alpha=1.0, taper=None, soft=0.0016):
    """折れ線をカプセルの和で描く。taper=(w0, w1) で先細り。"""
    pts = np.asarray(pts, dtype=np.float32)
    size = arr.shape[0]
    wmax = width if taper is None else max(taper)
    x0, x1 = pts[:, 0].min() - wmax - 0.01, pts[:, 0].max() + wmax + 0.01
    y0, y1 = pts[:, 1].min() - wmax - 0.01, pts[:, 1].max() + wmax + 0.01
    s = _sub(size, x0, x1, y0, y1)
    if s is None:
        return
    r0, r1, c0, c1, X, Y = s
    acc = np.zeros_like(X)
    n = len(pts) - 1
    for i in range(n):
        a, b = pts[i], pts[i + 1]
        ab = b - a
        L2 = float(ab @ ab) + 1e-12
        t = np.clip(((X - a[0]) * ab[0] + (Y - a[1]) * ab[1]) / L2, 0.0, 1.0)
        px, py = a[0] + t * ab[0], a[1] + t * ab[1]
        d = np.sqrt((X - px) ** 2 + (Y - py) ** 2)
        if taper is None:
            w = width
        else:
            g = (i + t) / max(n, 1)
            w = taper[0] + (taper[1] - taper[0]) * g
        acc = np.maximum(acc, _smoothstep(w + soft, w - soft, d))
    _paint(arr, (r0, r1, c0, c1), acc.astype(np.float32), rgb, alpha)


def arc(arr, p0, p1, p2, width, rgb, *, n=26, alpha=1.0, taper=None):
    """2 次ベジエの弧（眉・まつ毛・口）。"""
    t = np.linspace(0.0, 1.0, n).reshape(-1, 1)
    p0, p1, p2 = (np.asarray(p, dtype=np.float32) for p in (p0, p1, p2))
    pts = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t**2 * p2
    polyline(arr, pts, width, rgb, alpha=alpha, taper=taper)


def vgradient(arr, cx, cy, rx, ry, rgb_top, rgb_bottom, *, power=2.0, rot=0.0,
              alpha=1.0):
    """楕円の内側を縦グラデーションで塗る（虹彩）。"""
    size = arr.shape[0]
    rr = max(rx, ry) * 1.6 + 0.01
    s = _sub(size, cx - rr, cx + rr, cy - rr, cy + rr)
    if s is None:
        return
    r0, r1, c0, c1, X, Y = s
    dx, dy = X - cx, Y - cy
    c, sn = math.cos(rot), math.sin(rot)
    u = (dx * c + dy * sn) / max(rx, 1e-6)
    v = (-dx * sn + dy * c) / max(ry, 1e-6)
    d = (np.abs(u) ** power + np.abs(v) ** power) ** (1.0 / power)
    e = 0.0035 / max(rx, ry)
    m = _smoothstep(1.0 + e, 1.0 - e, d).astype(np.float32)
    g = np.clip((Y - (cy - ry)) / (2 * ry + 1e-9), 0.0, 1.0).astype(np.float32)
    top = np.asarray(rgb_top, dtype=np.float32).reshape(1, 1, 3)
    bot = np.asarray(rgb_bottom, dtype=np.float32).reshape(1, 1, 3)
    col = bot * (1.0 - g[..., None]) + top * g[..., None]
    mm = (m * alpha)[..., None]
    arr[r0:r1, c0:c1, :3] = arr[r0:r1, c0:c1, :3] * (1.0 - mm) + col * mm


def radial_blush(arr, cx, cy, rx, ry, rgb, strength=0.55):
    size = arr.shape[0]
    rr = max(rx, ry) * 1.3
    s = _sub(size, cx - rr, cx + rr, cy - rr, cy + rr)
    if s is None:
        return
    r0, r1, c0, c1, X, Y = s
    d = np.sqrt(((X - cx) / rx) ** 2 + ((Y - cy) / ry) ** 2)
    m = (np.clip(1.0 - d, 0.0, 1.0) ** 1.6 * strength).astype(np.float32)
    _paint(arr, (r0, r1, c0, c1), m, rgb, 1.0)


# --------------------------------------------------------------------------
# 顔テクスチャ（アトラス）
# --------------------------------------------------------------------------
#
# face.png は 1024x1024 を 512 四方の 4 タイルに分けて使う。
#   左上 TILE_BASE    : 肌・頬の赤み・鼻・眉（目と口は描かない）→ 頭部前面が参照
#   右上 TILE_FEATURE : 同じ下地の上に目と口 → 目/虹彩/口の可動パーツが参照
#   左下              : 無地の肌（予備）
#   右下              : 白（予備）
#
# どちらのタイルも「頭部を正面から平面投影した矩形」を 0..1 で表し、
#   u=0 → キャラの右手側（正面視で画面左）, v=0 → 顎の下, v=1 → 頭頂
# として描く。可動パーツは基準タイルに目/口が無いので、Shape Key で動かしても
# 元の位置に絵が残らない。
TILE_BASE = (0.0, 0.5, 0.5, 1.0)      # (u0, u1, v0, v1)
TILE_FEATURE = (0.5, 1.0, 0.5, 1.0)


def tile_uv(tile, u: float, v: float) -> tuple[float, float]:
    u0, u1, v0, v1 = tile
    return (u0 + (u1 - u0) * u, v0 + (v1 - v0) * v)


def draw_face_atlas(size: int, p: dict) -> np.ndarray:
    """顔アトラス（既定 2048x2048）を組み立てる。

    左下 = 素肌タイル、右下 = 目と口を描いた feature タイル。可動パーツは
    feature タイルを参照するので、Shape Key で動かしても下地に絵が残らない。
    """
    half = size // 2
    skin = p["skin"]
    base = _draw_face_ground(half, p)
    feat = base.copy()
    _draw_face_features(feat, p)

    atlas = canvas(size, skin, 1.0)
    atlas[half:size, 0:half] = base
    atlas[half:size, half:size] = feat
    atlas[0:half, 0:half] = canvas(half, skin, 1.0)
    atlas[0:half, half:size] = canvas(half, (1.0, 1.0, 1.0), 1.0)
    return atlas


def _draw_face_ground(size: int, p: dict) -> np.ndarray:
    """肌・頬の赤み・鼻。両タイル共通の下地。"""
    skin = p["skin"]
    arr = canvas(size, skin, 1.0)
    fl = p["face_layout"]
    eye_y = fl["eye_y"]
    eye_dx = fl["eye_dx"]
    eye_rx = fl["eye_rx"]
    eye_ry = fl["eye_ry"]

    # --- 頬の赤み -------------------------------------------------------
    blush = p.get("blush_color", (0.99, 0.58, 0.60))
    for sgn in (-1, 1):
        radial_blush(arr, 0.5 + sgn * (eye_dx + eye_rx * 0.46),
                     eye_y - eye_ry * 0.82, eye_rx * 1.00, eye_ry * 0.50,
                     blush, p.get("blush_strength", 0.5))

    # 額と顎に薄い陰影を入れて立体感を出す
    radial_blush(arr, 0.5, eye_y + eye_ry * 2.4, 0.30, 0.15,
                 tuple(min(1.0, c * 1.05 + 0.04) for c in skin), 0.32)
    radial_blush(arr, 0.5, eye_y * 0.30, 0.19, 0.11,
                 tuple(c * 0.94 for c in skin), 0.36)
    # 輪郭の影（頬の外側）
    for sgn in (-1, 1):
        radial_blush(arr, 0.5 + sgn * 0.40, eye_y - eye_ry * 0.6, 0.11, 0.22,
                     tuple(c * 0.90 for c in skin), 0.45)

    # --- 鼻 -------------------------------------------------------------
    # body.build_face_parts の鼻メッシュと同じ高さ（eye_y * 0.66）に置く
    nose_y = eye_y * 0.66
    ellipse(arr, 0.5 + 0.004, nose_y - 0.004, 0.0052, 0.0072,
            tuple(c * 0.82 for c in skin), power=2.4, alpha=0.55, feather=0.004)
    return arr


def _draw_face_brows(arr: np.ndarray, p: dict) -> None:
    fl = p["face_layout"]
    eye_y, eye_dx, eye_rx, eye_ry = (fl["eye_y"], fl["eye_dx"], fl["eye_rx"],
                                     fl["eye_ry"])
    brow = p.get("brow_color", tuple(min(1.0, c * 0.78) for c in p["hair_color"]))
    bw = p.get("brow_width", 0.011)
    if p.get("eye_style") == "dot":
        # 太くて短い直線の眉。内側を下げて眉根を寄せる（坊っちゃん）。
        by = eye_y + eye_ry * 2.9
        for sgn in (-1, 1):
            cx = 0.5 + sgn * eye_dx
            polyline(arr,
                     [(cx - sgn * eye_rx * 0.70, by - eye_ry * 0.25),
                      (cx + sgn * eye_rx * 1.75, by + eye_ry * 0.70)],
                     bw, brow, taper=(bw * 1.05, bw * 0.80))
        return
    by = eye_y + eye_ry * 1.72
    for sgn in (-1, 1):
        cx = 0.5 + sgn * eye_dx
        arc(arr,
            (cx - sgn * eye_rx * 0.95, by - eye_ry * 0.10),
            (cx + sgn * eye_rx * 0.05, by + eye_ry * p.get("brow_arch", 0.34)),
            (cx + sgn * eye_rx * 0.92, by - eye_ry * 0.02),
            bw, brow, taper=(bw * 1.25, bw * 0.45))


def _draw_face_features(arr: np.ndarray, p: dict) -> None:
    """目と口。可動パーツ（eye_white / eye_l / eye_r / mouth）が参照する層。"""
    fl = p["face_layout"]
    eye_y, eye_dx, eye_rx, eye_ry = (fl["eye_y"], fl["eye_dx"], fl["eye_rx"],
                                     fl["eye_ry"])
    iris = p["eye_color"]
    iris_dark = tuple(c * 0.40 for c in iris)
    iris_light = tuple(min(1.0, c * 1.28 + 0.18) for c in iris)
    lash = p.get("lash_color", (0.16, 0.10, 0.12))

    for sgn in (-1, 1):
        cx = 0.5 + sgn * eye_dx
        if p.get("eye_style") == "dot":
            _draw_dot_eye(arr, cx, eye_y, eye_rx, eye_ry, sgn, lash)
        else:
            _draw_eye(arr, cx, eye_y, eye_rx, eye_ry, sgn, iris, iris_dark,
                      iris_light, lash, p)

    # body.build_face_parts の口メッシュと同じ高さ（eye_y * 0.46）に置く
    mouth_y = eye_y * 0.46
    mw = p.get("mouth_w", 0.030)
    mcol = p.get("mouth_color", (0.80, 0.30, 0.34))
    if p.get("mouth_style", "smile") == "smile":
        # 口角を上げた小さな笑み。中央の谷 -> 両端の跳ね上げ
        arc(arr, (0.5 - mw, mouth_y + mw * 0.46), (0.5, mouth_y - mw * 0.48),
            (0.5 + mw, mouth_y + mw * 0.46), 0.0044, mcol,
            taper=(0.0022, 0.0022))
        # 口の中（うっすら赤）
        ellipse(arr, 0.5, mouth_y - mw * 0.08, mw * 0.56, mw * 0.30,
                tuple(min(1.0, c * 1.12 + 0.06) for c in mcol),
                power=2.2, alpha=0.48, feather=0.006)
        # 下唇のハイライト
        ellipse(arr, 0.5, mouth_y - mw * 0.52, mw * 0.34, mw * 0.13,
                (1.0, 0.90, 0.88), power=2.0, alpha=0.40, feather=0.005)
    elif p.get("mouth_style") == "frown":
        # への字。両端を下げる（坊っちゃん）。公式の口は唇でなく 1 本の濃い線
        # （実測 RGB 38,26,15）なので、mouth_color をそのまま使わず暗く落とす。
        ink = tuple(c * 0.52 for c in mcol)
        # 幅は口の円盤（_disc の rx = mw*1.35）いっぱいまで使う。mw のままだと
        # 円盤の 74% しか塗らず、公式の横長な口にならない。
        hx = mw * 1.15
        # 反りは公式の「幅の 0.26 だけ持ち上がる」に合わせる。u と v で
        # uv_box のスケールが 1.25 違うので、v 側はその分だけ大きく取る。
        # 線の太さも mw 基準にしておく。絶対値で持つと mouth_w を広げたときに
        # 口だけ細い線になって、公式の「太い 1 本線」から外れる。
        arc(arr, (0.5 - hx, mouth_y - mw * 0.36), (0.5, mouth_y + mw * 1.08),
            (0.5 + hx, mouth_y - mw * 0.36), mw * 0.26, ink,
            taper=(mw * 0.14, mw * 0.14))
    else:  # 真一文字（教授）
        arc(arr, (0.5 - mw, mouth_y + mw * 0.16), (0.5, mouth_y - mw * 0.06),
            (0.5 + mw, mouth_y + mw * 0.16), 0.0040, mcol,
            taper=(0.0022, 0.0022))


def _draw_dot_eye(arr, cx, cy, rx, ry, sgn, lash):
    """点目。白目も虹彩も無く、黒い楕円と小さなハイライトだけ。

    `feather` は「中心 1.0 → 外周 0.0」の放射グラデーションを作る指定なので、
    点目に使うと黒目全体が半透明になり、灰色の球に見えてしまう。公式イラストの
    目は輪郭のはっきりした真っ黒な楕円なので、ここは feather を使わず
    `soft`（アンチエイリアス幅だけのぼかし）で縁を締める。
    """
    ink = tuple(c * 0.55 for c in lash)
    # メッシュの白目円盤は rx*0.985 x ry*0.965。これより内側に描くと、はみ出た
    # 円盤の縁が肌色のまま明るく光り、黒目の外周に三日月形のフチが出る。
    # 円盤より一回り大きく塗って、縁まで黒で埋める。
    # power は 2.0（真の楕円）。uv_box の縦横比の都合で u と v の 1 単位は
    # ワールドで 1.26 倍ちがうので、2.3 にすると角の立った四角に見える。
    ellipse(arr, cx, cy, rx * 1.02, ry * 1.00, ink, power=2.0, soft=0.0012)
    # 公式にハイライトは無い。ただし黒目はドーム（dome=hd*0.012）なので
    # 真っ黒だと穴に見える。上外側に 2px 相当だけ置いて艶を残す。
    ellipse(arr, cx - sgn * rx * 0.36, cy + ry * 0.42, rx * 0.12, ry * 0.10,
            (1.0, 1.0, 1.0), power=2.0, alpha=0.85, soft=0.0010)


def _draw_eye(arr, cx, cy, rx, ry, sgn, iris, iris_dark, iris_light, lash, p):
    """アニメ目 1 個分。sgn=-1 がキャラの右目（正面視で画面左）。

    層の順序は作画の定石にそろえる:
    白目 -> 上まぶたの落ち影 -> 虹彩ベース(下明・上暗のグラデ) -> 虹彩上部の
    乗算影 -> 外周のリムリング -> 下部の内反射 -> 瞳孔 -> ハイライト大/小。
    まつ毛・二重線・眉はメッシュ側で作るので、ここでは「まつ毛の落ち影」だけを
    やわらかく敷く。
    """
    style = p.get("eye_style", "round")
    power = 2.7 if style == "round" else 3.1
    tilt = math.radians(p.get("eye_tilt", 4.0)) * sgn

    # --- 白目: 純白でなく青みのあるオフホワイト
    ellipse(arr, cx, cy, rx, ry, (0.985, 0.980, 0.988), power=power, rot=tilt)
    # 上まぶたの落ち影（白目の上 45%）
    ellipse(arr, cx, cy + ry * 0.58, rx * 0.98, ry * 0.48,
            (0.74, 0.74, 0.84), power=2.2, rot=tilt, alpha=0.62, feather=0.010)
    # 目頭側のわずかな赤み
    ellipse(arr, cx - sgn * rx * 0.86, cy - ry * 0.18, rx * 0.16, ry * 0.22,
            (0.95, 0.74, 0.76), power=2.0, alpha=0.45, feather=0.012)

    # --- 虹彩
    ir_x, ir_y = rx * 0.80, ry * 0.86
    icy = cy - ry * 0.05
    vgradient(arr, cx, icy, ir_x, ir_y, iris_light, iris_dark, power=2.2)
    # 上部 35% を暗く（まぶたの影）
    ellipse(arr, cx, icy + ir_y * 0.56, ir_x * 0.99, ir_y * 0.52,
            tuple(c * 0.55 for c in iris), power=2.2, alpha=0.72, feather=0.014)
    # 外周のリムリング（濃色）
    ring(arr, cx, icy, ir_x, ir_y, ir_x * 0.15, iris_dark, power=2.2, alpha=0.97)
    # 下部の内反射（虹彩の中で一番明るい帯）
    ellipse(arr, cx, icy - ir_y * 0.46, ir_x * 0.66, ir_y * 0.30,
            iris_light, power=2.2, alpha=0.80, feather=0.016)
    # 虹彩の放射スジ
    for i in range(18):
        a = i * math.pi / 9.0
        x0, y0 = cx + math.cos(a) * ir_x * 0.30, icy + math.sin(a) * ir_y * 0.30
        x1, y1 = cx + math.cos(a) * ir_x * 0.86, icy + math.sin(a) * ir_y * 0.86
        polyline(arr, [(x0, y0), (x1, y1)], ir_x * 0.045,
                 iris_dark if i % 2 == 0 else iris_light, alpha=0.30)

    # --- 瞳孔（虹彩より濃く、上端をぼかす）
    ellipse(arr, cx, icy, ir_x * 0.40, ir_y * 0.50, (0.09, 0.07, 0.12),
            power=2.2, feather=0.008)
    ellipse(arr, cx, icy + ir_y * 0.22, ir_x * 0.38, ir_y * 0.26,
            tuple(c * 0.45 for c in iris), power=2.2, alpha=0.40, feather=0.014)
    if p.get("star_eyes"):
        _star(arr, cx, icy, ir_x * 0.30, (1.0, 1.0, 1.0), alpha=0.82)

    # --- ハイライト 2 個（大: 上外側 / 小: 下内側、瞳孔をはさんで対角）
    hi = (1.0, 0.995, 0.97)
    ellipse(arr, cx - sgn * ir_x * 0.36, icy + ir_y * 0.42, ir_x * 0.32,
            ir_y * 0.30, hi, power=2.0)
    ellipse(arr, cx - sgn * ir_x * 0.30, icy + ir_y * 0.50, ir_x * 0.16,
            ir_y * 0.14, (1.0, 1.0, 1.0), power=2.0)
    ellipse(arr, cx + sgn * ir_x * 0.36, icy - ir_y * 0.44, ir_x * 0.17,
            ir_y * 0.16, hi, power=2.0, alpha=0.92)

    # --- まつ毛の落ち影（本体はメッシュ）
    lw = p.get("lash_width", 0.0)
    lw = lw if lw > 0 else rx * 0.20
    soft = tuple(min(1.0, c * 0.55 + 0.12) for c in lash)
    arc(arr,
        (cx - rx * 1.00, cy + ry * 0.20),
        (cx, cy + ry * 1.18),
        (cx + rx * 1.00, cy + ry * 0.22),
        lw * 0.62, soft, taper=(lw * 0.34, lw * 0.34), alpha=0.75)
    arc(arr, (cx - rx * 0.84, cy - ry * 0.74), (cx, cy - ry * 1.02),
        (cx + rx * 0.84, cy - ry * 0.72), lw * 0.22, soft, alpha=0.55)


def _star(arr, cx, cy, r, rgb, alpha=1.0):
    pts = []
    for i in range(11):
        a = math.pi / 2 + i * math.pi / 5
        rad = r if i % 2 == 0 else r * 0.42
        pts.append((cx + math.cos(a) * rad, cy + math.sin(a) * rad))
    polyline(arr, pts, r * 0.20, rgb, alpha=alpha)
    ellipse(arr, cx, cy, r * 0.34, r * 0.34, rgb, alpha=alpha)


# --------------------------------------------------------------------------
# 和柄パターン
# --------------------------------------------------------------------------


def draw_kasuri(size: int = 256, base=(0.89, 0.895, 0.905),
                ink=(0.32, 0.47, 0.73)) -> np.ndarray:
    """十字絣（坊っちゃんの着物）。薄い灰白地に青の十字。

    公式イラストの十字は一辺 13px・周期 24px（胴幅 130px に 5〜6 個）の大きな
    柄で、腕の太さは十字の一辺の 0.38 もある。1 タイルに 4x4 で敷くと 1 個が
    1/6 の細かさになり、遠目にはただの点々になる。タイルの繰り返し回数
    （params の pattern_scale）はこのファイルからは触れないので、
    「1 タイル = 十字 1 個」にして大きさを稼ぐ。

    色はレンダ結果を公式の実測値（地 RGB 213,214,217 / 十字 80,117,179）に
    合わせ込んだもの。テクスチャの値をそのまま置くとライティングで 6% ほど
    暗く青が浅く出るので、その分だけ明るく・青く振ってある。旧値の
    地 #F7F5ED は白く黄色すぎ、十字 #29579E は袴の濃紺で、着物の柄としては
    暗すぎた。
    """
    arr = canvas(size, base)
    # このタイルは Generated 座標（mats.py, uv=False）で貼るので、正方形の
    # タイルがオブジェクトのバウンディングボックス（身長 1.15m x 肩幅）の
    # 縦横比のぶんだけ縦に伸びる。前面レンダで柄の周期を実測すると
    # 横 46.7px に対して縦 78px = 1.67 倍だった。縦向きの辺をこれで割って置き、
    # レンダ上で公式どおりの正方形の十字になるようにする。
    # ※ バウンディングボックス依存なので、髪や下駄でシルエットが大きく
    #    変わったら周期を測り直すこと。
    aniso = 1.67
    # 十字の一辺 0.54 タイル / 腕の太さ 0.104*2 = 0.21 タイル。公式実測の
    # 十字 13px・周期 24px（0.54）、腕 5px（十字の 0.38）に合わせてある。
    arm, th = 0.27, 0.104
    ellipse(arr, 0.5, 0.5, arm, th / aniso, ink, power=3.4)
    ellipse(arr, 0.5, 0.5, th, arm / aniso, ink, power=3.4)
    return arr


def draw_yagasuri(size: int = 256, base=(0.99, 0.97, 0.96),
                  ink=(0.86, 0.22, 0.30), cols: int = 4,
                  rows: int = 4) -> np.ndarray:
    """矢絣（マドンナちゃんの着物）。

    矢羽根は「縦の帯の中で、への字が段々に積み上がる」文様で、隣り合う帯は
    地と柄が入れ替わる。折れ線を敷き詰めるだけだと編み目のように見えるので、
    帯ごとに塗り分けて面で出す。
    """
    u = (np.arange(size) + 0.5) / size
    v = (np.arange(size) + 0.5) / size
    uu, vv = np.meshgrid(u, v)

    band = np.floor(uu * cols).astype(np.int32)
    fu = uu * cols - band                      # 帯の中での横位置 0..1
    # 帯の中央ほど段を持ち上げて「へ」の字を作る
    lift = 1.0 - np.abs(2.0 * fu - 1.0)
    fv = np.mod(vv * rows + lift * 0.5, 1.0)

    odd = (band % 2) == 1
    # 羽根の太さ。段の下半分を柄、上半分を地にする
    feather = fv < 0.52
    paint = np.where(odd, ~feather, feather)

    # 帯の境に細い地の筋を残すと、矢羽根の輪郭が締まる
    edge = (fu < 0.045) | (fu > 0.955)
    paint = paint & ~edge

    arr = np.empty((size, size, 4), dtype=np.float32)
    arr[..., 3] = 1.0
    for c in range(3):
        arr[..., c] = np.where(paint, ink[c], base[c])
    # 着物の UV は縦方向が u なので、矢羽根が立つよう入れ替える
    return np.ascontiguousarray(arr.transpose(1, 0, 2))


def draw_check(size: int = 256, a=(0.95, 0.95, 0.95), b=(0.72, 0.74, 0.80),
               n: int = 8) -> np.ndarray:
    arr = canvas(size, a)
    step = size // n
    for iy in range(n):
        for ix in range(n):
            if (ix + iy) % 2:
                arr[iy * step:(iy + 1) * step, ix * step:(ix + 1) * step, :3] = b
    return arr


# --------------------------------------------------------------------------
# Blender への受け渡し
# --------------------------------------------------------------------------


def array_to_image(name: str, arr: np.ndarray, *, filepath: str | None = None,
                   colorspace: str = "sRGB"):
    import bpy

    h, w, _ = arr.shape
    img = bpy.data.images.get(name)
    if img is not None:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, width=w, height=h, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    if filepath:
        img.filepath_raw = filepath
        img.file_format = "PNG"
        img.save()
    else:
        img.pack()
    return img

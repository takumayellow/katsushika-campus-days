"""大人の男の髪型と眼鏡（教授 prof と、prof の体を使うモブ）。

髪は頭皮に沿った 1 枚の殻だけで作る。生え際の高さを方位ごとに決めて耳を出し、
殻の厚みを生え際へ向けて薄くして、縁に兜のような段を作らない。分け目は
キャラの左（+X）の前に置き、そこから右のこめかみへ流す前髪の束を、殻の
厚みの起伏として付ける（房を浮かせて重ねない）。
"""

from __future__ import annotations

import math

import numpy as np

from . import mesh as M
from .hair import FRONT, _off_skull, _outward, _smooth

# 生え際の高さ（顎から頭高の何割上か）。方位は正面からの符号付き角度（度、+ がキャラの左）。
# 分け目の側（+X）の額は高く、流した前髪が掛かる側（-X）は下がる。耳の上（約 80°〜104°）は
# 耳の天端（0.565）より上で切って耳を出し、耳の後ろから襟足へ下ろす。
_LINE_DEG = np.array([-180, -160, -148, -138, -128, -118, -110, -104, -80, -70, -58, -45, -25, 0,
                      25, 40, 52, 62, 72, 80, 104, 110, 118, 128, 138, 148, 160, 180],
                     dtype=float)
_LINE_H = np.array([0.11, 0.115, 0.12, 0.125, 0.15, 0.30, 0.50, 0.585, 0.585, 0.615, 0.645, 0.660,
                    0.668, 0.685, 0.705, 0.705, 0.690, 0.660, 0.625, 0.585, 0.585, 0.50, 0.30,
                    0.15, 0.125, 0.12, 0.115, 0.11])

# 殻の厚み（頭幅比）。生え際へ向けて薄くする前の、方位ごとの量。前は前髪の
# 立ち上がりのぶん厚く、横は刈り込んで薄く、後ろは後頭部の丸みのぶんだけ残す。
_THICK_DEG = np.array([0, 30, 55, 80, 110, 140, 180], dtype=float)
_THICK = np.array([0.115, 0.112, 0.095, 0.075, 0.075, 0.072, 0.068])
_THICK_TOP = 0.125


def _signed_deg(az: np.ndarray) -> np.ndarray:
    return np.degrees(((np.asarray(az) - FRONT + math.pi) % (2 * math.pi)) - math.pi)


def _el_for_height(head, az: np.ndarray, frac: np.ndarray, chin: float, hh: float):
    """方位ごとに、頭の面の高さが顎から frac*hh になる極角。"""
    grid = np.linspace(1e-3, 0.90 * math.pi, 400)
    out = np.empty(len(az))
    for i, (a, f) in enumerate(zip(az, frac)):
        zs = head.surface(np.full_like(grid, a), grid)[:, 2]
        out[i] = np.interp(-(chin + f * hh), -zs, grid)
    return out


def _sideburn(s_deg: np.ndarray) -> np.ndarray:
    """もみあげ: 耳の前（正面から 77°）で生え際を細く下げる量（頭高比）。"""
    return 0.13 * np.exp(-((np.abs(s_deg) - 77.0) / 3.6) ** 2)


def _fringe_tips(s_deg: np.ndarray) -> np.ndarray:
    """流した前髪の毛先: 額の生え際を、束ごとに少しだけ下げる量（頭高比）。

    分け目（+35°）から -X のこめかみまでの間に、流れの向きへ寄った低い山を並べる。
    """
    from_part = np.clip(35.0 - s_deg, 0.0, None)
    on = _smooth(from_part / 10.0) * _smooth((95.0 - from_part) / 20.0)
    frac = (from_part / 26.0) % 1.0
    # 毛先へゆっくり下がり、次の束の根元で戻る（流れの向きの非対称な山）
    tip = np.where(frac < 0.75, _smooth(frac / 0.75), _smooth((1.0 - frac) / 0.25))
    return 0.016 * on * tip


def _fringe_flow(s_deg: np.ndarray, t: np.ndarray) -> np.ndarray:
    """分け目から右のこめかみへ流した前髪の束の起伏（0..1）。

    束は分け目（+35°）から -X へ、生え際に近づくほど下がる弧に沿う。弧に直交する
    向きの位相で cos の山を 3 本並べ、束と束の間を浅い谷にする。
    """
    # 分け目からの角度の距離。分け目より +X 側（耳側）には流さない。
    from_part = np.clip(35.0 - s_deg, 0.0, None)
    on = _smooth(from_part / 8.0) * _smooth((105.0 - from_part) / 25.0)
    # 生え際に近いほど束が下がる（流れ）。t は頭頂 0、生え際 1。
    phase = from_part / 34.0 + (1.0 - t) * 1.1
    ridge = 0.5 + 0.5 * np.cos(2 * math.pi * phase)
    front = _smooth((t - 0.30) / 0.35)
    return on * front * ridge


def build_business(mb: M.MeshBuilder, p: dict, head, *, nu: int = 160, nv: int = 16):
    """七三に分けた大人の男の短髪。殻 1 枚（part は hair_cap）。"""
    hw, hh = p["head_w"], p["head_h"]
    chin = p["z"]["chin"]
    az = FRONT + np.radians(np.linspace(-180.0, 180.0, nu, endpoint=False))
    s = _signed_deg(az)
    line = np.interp(s, _LINE_DEG, _LINE_H) - _sideburn(s) - _fringe_tips(s)
    emax = _el_for_height(head, az, line, chin, hh)
    base = np.interp(np.abs(s), _THICK_DEG, _THICK)
    # 分け目より耳側（+X）は撫で付けて薄く、流した側は少しふくらませる
    base = base * np.where(s > 35.0, 0.85, 1.0 + 0.10 * _smooth((35.0 - s) / 40.0))
    # 分け目の溝: +35° の線に沿って、生え際から頭頂の手前まで
    groove = np.exp(-((s - 35.0) / 4.5) ** 2)
    # 前髪の縁（-X 側の額）は薄い唇だけ残し、ほかの生え際は肌へ沿わせる
    lip = np.where(np.abs(s) < 75.0, 0.18 + 0.12 * _smooth((10.0 - s) / 40.0), 0.10)

    rings = []
    for j in range(nv + 1):
        t = j / nv
        el = np.maximum(t * emax, 1e-3)
        crown = _smooth(t / 0.55)
        th = _THICK_TOP * (1.0 - crown) + base * crown
        edge = lip + (1.0 - lip) * (1.0 - _smooth((t - 0.70) / 0.30))
        flow = _fringe_flow(s, np.full(nu, t))
        part_dip = groove * _smooth((t - 0.25) / 0.20) * (1.0 - _smooth((t - 0.92) / 0.08))
        off = hw * (th * edge * (1.0 + 0.28 * flow) - 0.030 * part_dip)
        off = np.maximum(off, hw * 0.004)
        rings.append(_outward(head, head.surface(az, el), off))
    with mb.part("hair_cap"):
        mb.add_grid(rings, "hair", smooth=True, cap_start=True, cap_end=False,
                    close_u=True, flip=bool(p.get("outward_faces")))
    return rings[-1]


# --------------------------------------------------------------------------
# 眼鏡（金属の縁）
# --------------------------------------------------------------------------

#: 眼鏡の輪郭線の太さ（顔の輪郭線に対する倍率）。縁は直径数ミリの線なので、顔と同じ
#: 太さで囲むと縁の両側に黒い線が付いて 3 本線に見える。
GLASSES_OUTLINE_WIDTH = 0.35


def _lens_loop(cx, cz, half_w, half_h, sgn, n=36):
    """レンズの縁の (x, z)。角の丸い長方形で、下辺を少し狭め、外側の上角を張らせる。"""
    a = np.linspace(0.0, 2 * math.pi, n, endpoint=False)
    c, s = np.cos(a), np.sin(a)
    e = 2.0 / 3.4  # 超楕円の指数 3.4。2 なら楕円、大きいほど角が立つ
    px = np.sign(c) * np.abs(c) ** e
    pz = np.sign(s) * np.abs(s) ** e
    px = px * (1.0 + 0.06 * pz)
    return cx + sgn * px * half_w, cz + pz * half_h


def build_metal_glasses(mb: M.MeshBuilder, p: dict, head, fs, uv_box,
                        mat: str = "glasses") -> None:
    """鼻に乗せて耳に掛ける縁の眼鏡（part は glasses）。

    縁の太さは glasses_tube（頭幅比）。レンズは目の横幅の glasses_lens[0] 倍、
    縦は横の glasses_lens[1] 倍。縁は顔の面の手前に置き、外側ほど奥へ回す（顔の
    丸みに沿う）。ブリッジは鼻筋の上を通し、鼻パッドを鼻の横に当てる。つるは頭の
    横に沿わせて耳の付け根の上を通し、耳の後ろへ下ろす。
    """
    fl = p["face_layout"]
    x0, x1, z0, z1 = uv_box
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    chin = p["z"]["chin"]
    ex = (x1 - x0) * fl["eye_dx"]
    ez = z0 + (z1 - z0) * fl["eye_y"]
    k_w, k_h = p.get("glasses_lens", (1.55, 0.64))
    half_w = (x1 - x0) * fl["eye_rx"] * k_w
    half_h = half_w * k_h
    cz = ez - hw * 0.012
    tube = hw * p.get("glasses_tube", 0.0095)
    clear = hw * 0.022           # 顔の面からの最小の隙間
    y_front = fs.y_at(ex, ez) - hw * 0.040

    def frame_y(x, z):
        # 正面の平面を外側ほど奥へ回し、顔より手前に保つ
        wrap = hw * 0.30 * (np.abs(x) / hw) ** 2
        y_face = np.array([fs.y_at(float(a), float(b)) for a, b in zip(x, z)])
        return np.minimum(y_front + wrap, y_face - clear)

    with mb.part("glasses"):
        for sgn in (-1, 1):
            lx, lz = _lens_loop(sgn * ex, cz, half_w, half_h, sgn)
            loop = np.stack([lx, frame_y(lx, lz), lz], axis=1)
            mb.add_tube(np.vstack([loop, loop[:1]]), [tube] * (len(loop) + 1), mat,
                        n=6, cap_start=False, cap_end=False)

            # 鼻パッド: レンズの内側の下寄りから、鼻筋の横へ
            px_in = sgn * (ex - half_w * 0.92)
            pad = np.array([sgn * hw * 0.040, 0.0, cz - half_h * 0.20])
            pad[1] = fs.y_at(pad[0], pad[2]) - hw * 0.006
            rim = np.array([px_in, 0.0, cz - half_h * 0.05])
            rim[1] = frame_y(np.array([rim[0]]), np.array([rim[2]]))[0]
            mb.add_tube(np.array([rim, (rim + pad) * 0.5 + [0.0, -hw * 0.004, 0.0], pad]),
                        [tube * 0.55] * 3, mat, n=5)
            mb.add_sphere(tuple(pad), (hw * 0.006, hw * 0.010, hw * 0.014), mat,
                          nu=8, nv=5)

            # 蝶番とつる。蝶番はレンズの外側の上端。つるはそこから後ろへほぼ水平に
            # 伸ばし、耳の付け根の上（耳の天端より少し下）に掛けて耳の後ろへ下ろす。
            hx = sgn * (ex + half_w * 1.02)
            hz = cz + half_h * 0.85
            hy = frame_y(np.array([hx]), np.array([hz]))[0]
            ear_x = sgn * hw * 0.475
            ear_y = hd * 0.045
            ear_z = chin + hh * 0.520
            pts = np.array([
                [hx, hy, hz],
                [sgn * hw * 0.49, hy + hd * 0.08, hz + (ear_z - hz) * 0.65],
                [ear_x, ear_y - hd * 0.13, ear_z],
                [ear_x, ear_y, ear_z + hh * 0.004],
                [ear_x, ear_y + hd * 0.08, ear_z - hh * 0.012],
                [ear_x * 0.98, ear_y + hd * 0.115, ear_z - hh * 0.060],
            ])
            path = _resample(pts, 18)
            # 蝶番の先は頭の横から浮かせ、耳の付け根では頭の面に寄せる
            gap = np.interp(np.linspace(0.0, 1.0, len(path)), [0.0, 0.30, 0.65, 1.0],
                            [hw * 0.040, hw * 0.030, hw * 0.020, hw * 0.012])
            path[1:] = _off_skull(head, path[1:], gap[1:])
            mb.add_tube(path, [tube * 0.95] * len(path), mat, n=6)

        # ブリッジ: 左右のレンズの内側の上寄りを、鼻筋の上でつなぐ
        bx = ex - half_w * 0.96
        bz = cz + half_h * 0.42
        xs = np.linspace(-bx, bx, 9)
        zs = bz + half_h * 0.10 * (1.0 - (xs / bx) ** 2)
        ys = np.minimum(frame_y(xs, zs),
                        np.array([fs.y_at(float(a), float(b)) for a, b in zip(xs, zs)])
                        - tube * 2.2)
        mb.add_tube(np.stack([xs, ys, zs], axis=1), [tube * 0.85] * len(xs), mat, n=6)
    mb.set_outline_width(mb.part_indices("glasses"), GLASSES_OUTLINE_WIDTH)


def _resample(pts: np.ndarray, n: int) -> np.ndarray:
    """折れ線を、弧長で等間隔の n 点の滑らかな線（Catmull-Rom）にする。"""
    pts = np.asarray(pts, dtype=float)
    seg = []
    for i in range(len(pts) - 1):
        p0 = pts[max(i - 1, 0)]
        p1, p2 = pts[i], pts[i + 1]
        p3 = pts[min(i + 2, len(pts) - 1)]
        for t in np.linspace(0.0, 1.0, 12, endpoint=False):
            t2, t3 = t * t, t * t * t
            seg.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    seg.append(pts[-1])
    seg = np.array(seg)
    d = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(seg, axis=0), axis=1))])
    u = np.linspace(0.0, d[-1], n)
    return np.stack([np.interp(u, d, seg[:, k]) for k in range(3)], axis=1)

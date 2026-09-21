"""素体（頭・首・胴・腕・脚）と顔パーツの生成。

頭は球ではなく「丸い頭頂 / 小さく尖った顎 / 頬のふくらみ」を持つアニメ顔の
シルエットに変形したものを使う。顔パーツ（白目・虹彩・口）は頭表面の少し手前に
置き、顔テクスチャの feature タイルを参照する独立メッシュにしてある
（Shape Key で動かしても下地に絵が残らないため）。
"""

from __future__ import annotations

import math

import numpy as np

from . import mesh as M
from .tex import TILE_BASE, TILE_FEATURE, tile_uv


# --------------------------------------------------------------------------
# 頭
# --------------------------------------------------------------------------


def _head_deform(d: np.ndarray) -> np.ndarray:
    """単位球の方向ベクトル群をアニメ顔のシルエットへ変形する。"""
    x, y, z = d[:, 0], d[:, 1], d[:, 2]
    # 低頭身のアニメ顔は「丸い頭蓋 + 頬の幅を保ったまま顎先だけ細くする」。
    # 幅の減衰を高次にして、z が -0.75 を下回るまではほとんど細らせない。
    w = np.where(z >= 0.0, 1.0 - 0.045 * z**2,
                 1.0 - 0.400 * np.abs(z) ** 5.0)
    X = x * w
    Y = y * w
    Z = np.where(z >= 0.0, z, z * 0.94)
    # 正面は平らめ、後頭部はふっくら
    Y = np.where(Y < 0.0, Y * 0.870, Y * 1.070)
    # 頬のふくらみ。目の下から顎にかけて広く取り、平たい逆三角形を避ける
    cheek = (np.exp(-((z + 0.26) / 0.46) ** 2)
             * np.exp(-((np.abs(x) - 0.52) / 0.52) ** 2))
    front = np.clip(-y, 0.0, 1.0)
    Y -= 0.135 * cheek * front
    X += np.sign(x) * 0.090 * cheek
    # 顎先。前へ出しすぎると尖るので控えめにし、丸みを残す
    Y -= 0.042 * M.smoothstep(-0.40, -0.98, z)
    # 顎の底を持ち上げて丸くする
    Z += 0.055 * M.smoothstep(-0.55, -1.0, z)
    return np.stack([X, Y, Z], axis=1)


class Head:
    """頭部のパラメトリック表面。メッシュ生成と顔パーツの配置に使う。"""

    def __init__(self, p: dict):
        self.p = p
        hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
        # 変形後の正規化空間でのはみ出し量を数値で測ってスケールを決める
        probe = _head_deform(_sphere_dirs(48, 32))
        self.sx = (hw * 0.5) / float(np.abs(probe[:, 0]).max())
        self.sy = (hd * 0.5) / float(np.abs(probe[:, 1]).max())
        zmin, zmax = float(probe[:, 2].min()), float(probe[:, 2].max())
        self.sz = hh / (zmax - zmin)
        self.cz = p["z"]["chin"] - zmin * self.sz
        self.scale = np.array([self.sx, self.sy, self.sz])
        self.center = np.array([0.0, 0.0, self.cz])

    def surface(self, az: np.ndarray, el: np.ndarray) -> np.ndarray:
        """az: 方位角（0=正面, 反時計回り）, el: 極角 0..pi（0=頭頂）。"""
        a = np.asarray(az, dtype=float)
        e = np.asarray(el, dtype=float)
        se = np.sin(e)
        d = np.stack([np.cos(a) * se, np.sin(a) * se, np.cos(e)], axis=-1)
        loc = _head_deform(d.reshape(-1, 3))
        return loc * self.scale + self.center


def _sphere_dirs(nu: int, nv: int) -> np.ndarray:
    a = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    e = np.linspace(0.02, math.pi - 0.02, nv)
    A, E = np.meshgrid(a, e)
    se = np.sin(E)
    return np.stack([np.cos(A) * se, np.sin(A) * se, np.cos(E)], axis=-1).reshape(-1, 3)


class FaceSurface:
    """頭部前面の (x, z) → y の近似。顔パーツを表面の少し手前に置くために使う。"""

    def __init__(self, pts: np.ndarray):
        pts = np.asarray(pts, dtype=float)
        self.pts = pts[pts[:, 1] < 0.0]

    def y_at(self, x: float, z: float, k: int = 8) -> float:
        d2 = (self.pts[:, 0] - x) ** 2 + (self.pts[:, 2] - z) ** 2
        idx = np.argpartition(d2, k)[:k]
        w = 1.0 / (d2[idx] + 1e-6)
        return float((self.pts[idx, 1] * w).sum() / w.sum())


def build_head(mb: M.MeshBuilder, p: dict) -> Head:
    head = Head(p)
    nu, nv = 56, 40
    chin_z = p["z"]["chin"]
    face_top = chin_z + p["head_h"] * 0.93
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    # 正面 = -Y なので方位角 3pi/2 が顔の中心
    front = 1.5 * math.pi
    dist = np.abs(((az - front + math.pi) % (2 * math.pi)) - math.pi)

    rings = []
    for j in range(1, nv):
        el = j * math.pi / nv
        rings.append(head.surface(az, np.full(nu, el)))

    def mat_fn(i: int, j: int) -> str:
        zc = 0.5 * (rings[j][i][2] + rings[j + 1][i][2])
        if dist[i] < math.radians(74.0) and chin_z - 0.01 < zc < face_top:
            return "face"
        return "skin"

    with mb.part("head"):
        mb.add_grid(rings, mat_fn, smooth=True, cap_start=True, cap_end=True)

    # 耳
    ear_z = chin_z + p["head_h"] * 0.46
    for sgn in (-1, 1):
        with mb.part("head"):
            mb.add_sphere((sgn * p["head_w"] * 0.475, p["head_d"] * 0.045, ear_z),
                          (p["head_w"] * 0.055, p["head_d"] * 0.075,
                           p["head_h"] * 0.105), "skin", nu=10, nv=7)
    return head


# --------------------------------------------------------------------------
# 顔パーツ（白目・虹彩・口）
# --------------------------------------------------------------------------


def _disc(cu: float, cv: float, ru: float, rv: float, power: float,
          rings: int, seg: int):
    """UV 空間の（スーパー）楕円ディスク。(u, v, r) のリング列を返す。"""
    out = []
    a = np.linspace(0.0, 2 * math.pi, seg, endpoint=False)
    ca, sa = np.cos(a), np.sin(a)
    e = 2.0 / power
    ux = np.sign(ca) * np.abs(ca) ** e
    vy = np.sign(sa) * np.abs(sa) ** e
    for j in range(1, rings + 1):
        t = j / rings
        out.append((cu + ux * ru * t, cv + vy * rv * t, np.full(seg, t)))
    return out


def _uv_to_xz(p: dict, uv_box, u, v):
    x0, x1, z0, z1 = uv_box
    return x0 + (x1 - x0) * np.asarray(u), z0 + (z1 - z0) * np.asarray(v)


def _ribbon(mb: M.MeshBuilder, part: str, mat: str, p: dict, fs: FaceSurface,
            uv_box, us, vs, half_w, *, offset: float, thick: float,
            smooth: bool = False):
    """顔表面に沿う帯メッシュ（まつ毛・二重線・眉）。

    UV 空間の曲線 (us, vs) を頭部表面へ投影し、曲線の面内法線方向に half_w、
    奥行き方向に thick の厚みを持つリボンを張る。テクスチャに描くのではなく
    メッシュにすることで、斜めや横からでも輪郭として立ち上がる。
    """
    xs, zs = _uv_to_xz(p, uv_box, np.asarray(us), np.asarray(vs))
    tx, tz = np.gradient(xs), np.gradient(zs)
    ln = np.hypot(tx, tz) + 1e-12
    nx, nz = -tz / ln, tx / ln
    hw = np.asarray(half_w, dtype=float) * (uv_box[3] - uv_box[2])
    rings = []
    for i in range(len(xs)):
        y0 = fs.y_at(float(xs[i]), float(zs[i])) - offset
        y1 = y0 + thick
        ax, az_ = xs[i] + nx[i] * hw[i], zs[i] + nz[i] * hw[i]
        bx, bz = xs[i] - nx[i] * hw[i], zs[i] - nz[i] * hw[i]
        rings.append(np.array([
            [ax, y0, az_], [ax, y1, az_], [bx, y1, bz], [bx, y0, bz],
        ]))
    with mb.part(part):
        mb.add_grid(rings, mat, smooth=smooth, close_u=True,
                    cap_start=True, cap_end=True)


def _eye_curves(cu: float, eye_y: float, rx: float, ry: float, sgn: int):
    """片目ぶんの（上まつ毛・下まつ毛・二重線）の UV 曲線を返す。"""
    n = 22
    th = np.linspace(math.pi * 1.00, -math.pi * 0.14, n)
    t = np.linspace(0.0, 1.0, n)
    flick = np.clip((t - 0.80) / 0.20, 0.0, 1.0) ** 1.6
    up_u = cu + sgn * rx * 1.02 * np.cos(th) * (1.0 + 0.10 * flick)
    up_v = eye_y + ry * 0.99 * np.sin(th) + ry * 0.20 * flick
    up_w = ry * (0.040 + 0.105 * np.exp(-((t - 0.56) / 0.38) ** 2))
    up_w = np.maximum(up_w * (1.0 - 0.60 * flick ** 2), ry * 0.018)

    n2 = 16
    th2 = np.linspace(-math.pi * 0.06, -math.pi * 0.90, n2)
    t2 = np.linspace(0.0, 1.0, n2)
    lo_u = cu + sgn * rx * 0.99 * np.cos(th2)
    lo_v = eye_y + ry * 0.94 * np.sin(th2)
    lo_w = ry * (0.020 + 0.030 * np.exp(-((t2 - 0.12) / 0.34) ** 2))

    n3 = 16
    th3 = np.linspace(math.pi * 0.92, math.pi * 0.06, n3)
    t3 = np.linspace(0.0, 1.0, n3)
    dl_u = cu + sgn * rx * 1.00 * np.cos(th3)
    dl_v = eye_y + ry * (0.99 * np.sin(th3) + 0.36)
    dl_w = ry * (0.020 + 0.016 * np.sin(math.pi * t3))
    return (up_u, up_v, up_w), (lo_u, lo_v, lo_w), (dl_u, dl_v, dl_w)


def _brow_curve(cu: float, eye_y: float, rx: float, ry: float, sgn: int,
                width: float, arch: float):
    """眉の UV 曲線。内側は太く、外側は細く下がる。"""
    n = 16
    t = np.linspace(0.0, 1.0, n)
    u_in, u_out = cu - sgn * rx * 1.10, cu + sgn * rx * 1.28
    us = u_in + (u_out - u_in) * t
    # 前髪の毛先とぶつからない高さに置く。高く上げすぎると額に隠れる。
    vs = (eye_y + ry * 1.72 + arch * np.sin(math.pi * np.clip(t * 1.05, 0.0, 1.0))
          - ry * 0.34 * t ** 2)
    ws = width * (0.55 + 0.45 * np.cos(math.pi * 0.5 * t)) * (1.0 - 0.55 * t ** 2)
    return us, vs, np.maximum(ws, width * 0.16)


def _brow_curve_flat(cu: float, eye_y: float, rx: float, ry: float, sgn: int,
                     width: float):
    """点目キャラの眉。太く短い直線で、内側を下げて眉根を寄せる。"""
    n = 8
    t = np.linspace(0.0, 1.0, n)
    u_in, u_out = cu - sgn * rx * 0.70, cu + sgn * rx * 1.75
    us = u_in + (u_out - u_in) * t
    vs = eye_y + ry * 2.9 - ry * 0.25 + ry * 0.95 * t
    ws = width * (1.05 - 0.25 * t)
    return us, vs, ws


def build_face_parts(mb: M.MeshBuilder, p: dict, fs: FaceSurface, uv_box):
    """白目板・虹彩ドーム・まつ毛・二重線・眉・鼻・口を頭表面の手前に貼る。"""
    fl = p["face_layout"]
    eye_y, eye_dx, eye_rx, eye_ry = (fl["eye_y"], fl["eye_dx"], fl["eye_rx"],
                                     fl["eye_ry"])
    uvs: dict[int, tuple[float, float]] = {}
    hd = p["head_d"]

    def place(part_name: str, mat: str, discs, offset: float, dome: float,
              power: float):
        rings = []
        idx_uv = []
        for (uu, vv, tt) in discs:
            xs, zs = _uv_to_xz(p, uv_box, uu, vv)
            ys = np.array([fs.y_at(float(x), float(z)) for x, z in zip(xs, zs)])
            ys = ys - offset - dome * np.clip(1.0 - tt ** 2, 0.0, 1.0)
            rings.append(np.stack([xs, ys, zs], axis=1))
            idx_uv.append((uu, vv))
        cu = float(discs[0][0].mean())
        cv = float(discs[0][1].mean())
        cx, cz = _uv_to_xz(p, uv_box, cu, cv)
        cy = fs.y_at(float(cx), float(cz)) - offset - dome
        with mb.part(part_name):
            center = mb.add_verts([(float(cx), float(cy), float(cz))])
            base = mb.add_verts(np.concatenate(rings, axis=0))
        seg = len(rings[0])
        for i in range(seg):
            i2 = (i + 1) % seg
            mb.add_face([center, base + i2, base + i], mat, True)
        for j in range(len(rings) - 1):
            for i in range(seg):
                i2 = (i + 1) % seg
                mb.add_face([base + j * seg + i, base + j * seg + i2,
                             base + (j + 1) * seg + i2,
                             base + (j + 1) * seg + i], mat, True)
        uvs[center] = tile_uv(TILE_FEATURE, cu, cv)
        for j, (uu, vv) in enumerate(idx_uv):
            for i in range(seg):
                uvs[base + j * seg + i] = tile_uv(TILE_FEATURE,
                                                  float(uu[i]), float(vv[i]))
        _ = power

    power_eye = 2.7 if p.get("eye_style", "round") == "round" else 3.1
    # 輪郭シェル（身長 x outline.THICKNESS）より必ず手前に出す。ここが
    # 内側に入ると頭の膨張シェルが目を黒く覆ってしまう。
    grow = p["height"] * 0.0022
    unit = max(hd * 0.0075, grow * 1.9)
    off_white = unit
    off_iris = unit * 1.50
    off_lash = unit * 2.10
    off_brow = unit * 2.75
    thick = unit * 1.35

    for sgn in (-1, 1):
        cu = 0.5 + sgn * eye_dx
        mat_iris = "eye_l" if sgn > 0 else "eye_r"
        name = "eye_l" if sgn > 0 else "eye_r"
        place(name + "_white", "eye_white",
              # まぶたのカーブより内側に収める。外に出ると上まぶたの縁から
              # 白目が三日月形にはみ出して、目が飛び出して見える。
              _disc(cu, eye_y, eye_rx * 0.985, eye_ry * 0.965, power_eye, 3, 20),
              offset=off_white, dome=hd * 0.012, power=power_eye)
        place(name + "_iris", mat_iris,
              _disc(cu, eye_y - eye_ry * 0.04, eye_rx * 0.90, eye_ry * 0.93,
                    2.2, 3, 18),
              offset=off_iris, dome=hd * 0.020, power=2.2)
        dot = p.get("eye_style") == "dot"
        if not dot:
            # 点目にはまつ毛も二重線も無い
            up, lo, dl = _eye_curves(cu, eye_y, eye_rx, eye_ry, sgn)
            _ribbon(mb, name + "_lash_up", "lash", p, fs, uv_box, up[0], up[1],
                    up[2], offset=off_lash, thick=thick)
            _ribbon(mb, name + "_lash_lo", "lash", p, fs, uv_box, lo[0], lo[1],
                    lo[2], offset=off_lash * 0.82, thick=thick * 0.62)
            _ribbon(mb, name + "_crease", "eye_rim", p, fs, uv_box, dl[0],
                    dl[1], dl[2], offset=off_lash * 0.70, thick=thick * 0.46)
        bw = p.get("brow_width", 0.038)
        ba = p.get("brow_arch", 0.030)
        if dot:
            bu, bv, bwid = _brow_curve_flat(cu, eye_y, eye_rx, eye_ry, sgn, bw)
        else:
            bu, bv, bwid = _brow_curve(cu, eye_y, eye_rx, eye_ry, sgn, bw, ba)
        side = "l" if sgn > 0 else "r"
        _ribbon(mb, "brow_" + side, "brow", p, fs, uv_box,
                bu, bv, bwid, offset=off_brow, thick=thick * 0.70)

    # 鼻は「あるのが分かる程度」。アニメ顔では点か影で十分なので、
    # 表面からほとんど出さない小さなふくらみにする。
    nose_v = eye_y * 0.66
    nx_, nz_ = _uv_to_xz(p, uv_box, np.array([0.5]), np.array([nose_v]))
    ny = fs.y_at(float(nx_[0]), float(nz_[0]))
    with mb.part("nose"):
        mb.add_sphere((0.0, ny + hd * 0.012, float(nz_[0])),
                      (p["head_w"] * 0.017, hd * 0.014, p["head_h"] * 0.013),
                      "face", nu=10, nv=6)
    lo_i, hi_i = mb.parts["nose"][0]
    for i in range(lo_i, hi_i):
        x, _y, z = mb.verts[i]
        u = (x - uv_box[0]) / (uv_box[1] - uv_box[0])
        v = (z - uv_box[2]) / (uv_box[3] - uv_box[2])
        uvs[i] = tile_uv(TILE_FEATURE, float(np.clip(u, 0.0, 1.0)),
                         float(np.clip(v, 0.0, 1.0)))

    mw = p.get("mouth_w", 0.030)
    # 目線と顎の間の約 45% に置く。眼の半径に紐づけると個体差で顎まで下がる。
    mouth_y = eye_y * 0.46
    place("mouth", "face",
          _disc(0.5, mouth_y, mw * 1.35, mw * 1.00, 2.4, 2, 16),
          offset=unit * 0.80, dome=unit * 0.55, power=2.4)
    return uvs


# --------------------------------------------------------------------------
# 胴・首・腕・脚
# --------------------------------------------------------------------------


class Anatomy:
    """リグ・衣装・髪が参照する基準点をまとめたもの。"""

    def __init__(self, p: dict):
        b = p["build_params"]
        h = p["height"]
        z = p["z"]
        self.p = p
        self.h = h
        self.z = z
        self.shoulder_half = b["shoulder_half"] * h
        self.neck_r = b["neck_r"] * h
        self.hip_rx = b["hip_rx"] * h
        self.hip_ry = b["hip_ry"] * h
        self.waist_rx = b["waist_rx"] * h
        self.waist_ry = b["waist_ry"] * h

        self.arm_r = tuple(r * h for r in b["arm_r"])
        self.leg_r = tuple(r * h for r in b["leg_r"])
        self.foot = tuple(f * h for f in b["foot"])
        self.hand_r = b["hand"] * h

        # A ポーズの腕（水平から 52 度下げ、肘から 60 度）
        self.shoulder = np.array([self.shoulder_half * 0.90, 0.0,
                                  z["shoulder"] - h * 0.012])
        al = b.get("arm_len", (0.170, 0.150, 0.088))
        l1, l2, l3 = al[0] * h, al[1] * h, al[2] * h
        d1 = np.array([math.cos(math.radians(52.0)), 0.0,
                       -math.sin(math.radians(52.0))])
        d2 = np.array([math.cos(math.radians(60.0)), 0.0,
                       -math.sin(math.radians(60.0))])
        self.elbow = self.shoulder + d1 * l1 + np.array([0.0, -0.008, 0.0])
        self.wrist = self.elbow + d2 * l2 + np.array([0.0, -0.012, 0.0])
        self.hand_tip = self.wrist + d2 * l3
        self.arm_dirs = (d1, d2)

        self.hip_joint = np.array([self.hip_rx * 0.50, 0.0, z["crotch"] + h * 0.012])
        self.knee = np.array([self.hip_rx * 0.47, 0.0, z["knee"]])
        self.ankle = np.array([self.hip_rx * 0.45, 0.0, z["ankle"]])
        self.toe = np.array([self.hip_rx * 0.45, -self.foot[1] * 0.78, self.foot[2] * 0.30])


def _torso_profile(p: dict, a: Anatomy):
    b, h, z = p["build_params"], p["height"], p["z"]
    keys = [
        (z["crotch"] - 0.075 * h, b["crotch_rx"] * 0.55 * h, b["crotch_ry"] * 0.60 * h),
        (z["crotch"] - 0.030 * h, b["crotch_rx"] * 0.90 * h, b["crotch_ry"] * 0.92 * h),
        (z["crotch"], b["crotch_rx"] * h, b["crotch_ry"] * h),
        (z["hip"], b["hip_rx"] * h, b["hip_ry"] * h),
        (z["waist"], b["waist_rx"] * h, b["waist_ry"] * h),
        (z["underbust"], b["underbust_rx"] * h, b["underbust_ry"] * h),
        (z["bust"], b["bust_rx"] * h, b["bust_ry"] * h),
        (z["shoulder"] - 0.045 * h, b["shoulder_half"] * 0.88 * h,
         b["shoulder_ry"] * 0.97 * h),
        (z["shoulder"], b["shoulder_half"] * h, b["shoulder_ry"] * h),
        (z["shoulder"] + 0.022 * h, b["shoulder_half"] * 0.50 * h,
         b["shoulder_ry"] * 0.80 * h),
    ]
    zs = np.array([k[0] for k in keys])
    rx = np.array([k[1] for k in keys])
    ry = np.array([k[2] for k in keys])
    n = 24
    zz = np.linspace(zs[0], zs[-1], n)
    return zz, np.interp(zz, zs, rx), np.interp(zz, zs, ry)


#: 衣装モジュールからも使う公開名
torso_profile = _torso_profile


def build_torso(mb: M.MeshBuilder, p: dict, a: Anatomy, mat: str = "skin",
                *, inflate: float = 0.0, z_lo: float | None = None,
                z_hi: float | None = None, part: str = "torso",
                cap: bool = True, seg: int = 30):
    """胴。inflate>0 で服の内側レイヤとしても使い回す。"""
    zz, rx, ry = _torso_profile(p, a)
    b = p["build_params"]
    h = p["height"]
    bulge = b["bust_bulge"] * h
    rings = []
    ang = np.linspace(0.0, 2 * math.pi, seg, endpoint=False)
    for zi, rxi, ryi in zip(zz, rx, ry):
        if z_lo is not None and zi < z_lo:
            continue
        if z_hi is not None and zi > z_hi:
            continue
        e = 2.0 / 2.25
        ca, sa = np.cos(ang), np.sin(ang)
        X = np.sign(ca) * np.abs(ca) ** e * (rxi + inflate)
        Y = np.sign(sa) * np.abs(sa) ** e * (ryi + inflate)
        if bulge > 0.0:
            t = math.exp(-((zi - p["z"]["bust"]) / (0.045 * h)) ** 2)
            f = np.clip(-sa, 0.0, 1.0) ** 1.6
            lobe = 0.62 + 0.38 * np.cos(2.0 * ang)
            Y -= bulge * t * f * np.clip(lobe, 0.0, 1.5)
        rings.append(np.stack([X, Y, np.full(seg, zi)], axis=1))
    with mb.part(part):
        mb.add_grid(rings, mat, smooth=True, cap_start=cap, cap_end=cap)
    return rings


def build_neck(mb: M.MeshBuilder, p: dict, a: Anatomy):
    z = p["z"]
    path = np.array([
        [0.0, 0.0, z["shoulder"] - 0.010],
        [0.0, -0.004, z["shoulder"] + 0.035],
        [0.0, -0.008, z["chin"] - 0.012],
    ])
    r = a.neck_r
    with mb.part("neck"):
        mb.add_tube(path, [r * 1.62, r * 1.06, r * 0.94], "skin", n=18,
                    cap_start=False, cap_end=False)


def _limb(mb: M.MeshBuilder, path, radii, mat: str, part: str, n: int = 20):
    with mb.part(part):
        mb.add_tube(path, radii, mat, n=n, cap_start=True, cap_end=True)


def _finger(mb: M.MeshBuilder, part: str, base, f, n_hat, length: float,
            radius: float, curl: float) -> None:
    """3 節のカプセル指。curl で手のひら側へ軽く曲げる。"""
    seg = length / 3.0
    pts = [np.asarray(base, dtype=float)]
    d = np.asarray(f, dtype=float)
    for k in range(3):
        d = d - n_hat * curl * (0.35 + 0.35 * k)
        d = d / (np.linalg.norm(d) + 1e-12)
        pts.append(pts[-1] + d * seg * (1.0 - 0.10 * k))
    radii = [radius * 1.00, radius * 0.94, radius * 0.84, radius * 0.58]
    with mb.part(part):
        mb.add_tube(np.array(pts), radii, "skin", n=6,
                    cap_start=True, cap_end=True)


def build_arms(mb: M.MeshBuilder, p: dict, a: Anatomy):
    r0, r1, r2 = a.arm_r
    hr = a.hand_r
    chibi = bool(p.get("chibi"))
    for sgn in (-1, 1):
        side = "l" if sgn > 0 else "r"
        sh = a.shoulder * np.array([sgn, 1, 1])
        el = a.elbow * np.array([sgn, 1, 1])
        wr = a.wrist * np.array([sgn, 1, 1])
        tip = a.hand_tip * np.array([sgn, 1, 1])
        mid1 = sh + (el - sh) * 0.50
        mid2 = el + (wr - el) * 0.45
        path = np.array([sh - (el - sh) * 0.06, mid1, el, mid2, wr])
        radii = [r0 * 1.08, r0 * 0.92, r1 * 1.02, r1 * 0.94, r2 * 0.86]
        _limb(mb, path, radii, "skin", "arm_" + side)

        f = tip - wr
        f = f / (np.linalg.norm(f) + 1e-12)
        n_hat = np.array([-f[2], 0.0, f[0]])
        n_hat = n_hat / (np.linalg.norm(n_hat) + 1e-12)
        if n_hat[0] * sgn > 0:
            n_hat = -n_hat
        spread = np.array([0.0, 1.0, 0.0])

        palm_len = hr * (1.30 if chibi else 1.45)
        palm_w = hr * (1.85 if chibi else 1.70)
        palm_t = hr * (1.05 if chibi else 0.86)
        rings = []
        for t in np.linspace(0.0, 1.0, 5):
            k = 0.62 + 0.38 * math.sin(math.pi * min(1.0, t * 0.92 + 0.08))
            c = wr + f * palm_len * t
            for_w = palm_w * 0.5 * k
            for_t = palm_t * 0.5 * (0.80 + 0.20 * k)
            ring = []
            for ang in np.linspace(0.0, 2 * math.pi, 10, endpoint=False):
                e = 2.0 / 2.6
                ca, sa = math.cos(ang), math.sin(ang)
                uu = math.copysign(abs(ca) ** e, ca)
                vv = math.copysign(abs(sa) ** e, sa)
                ring.append(c + spread * uu * for_w + n_hat * vv * for_t)
            rings.append(np.array(ring))
        with mb.part("hand_" + side):
            mb.add_grid(rings, "skin", smooth=True, close_u=True,
                        cap_start=True, cap_end=True)

        fl_ = hr * (1.25 if chibi else 1.52)
        fr = hr * (0.235 if chibi else 0.200)
        knuckle = wr + f * palm_len * 0.96
        for i, (off, lk) in enumerate(zip((-0.34, -0.115, 0.115, 0.34),
                                          (0.86, 1.00, 0.96, 0.78))):
            base = knuckle + spread * (palm_w * off) - n_hat * palm_t * 0.06
            _finger(mb, "hand_" + side, base, f, n_hat, fl_ * lk,
                    fr * (1.0 - 0.05 * abs(i - 1.5)), curl=0.16)

        tb = wr + f * palm_len * 0.34 - spread * palm_w * 0.46 - n_hat * palm_t * 0.10
        tdir = f * 0.42 - spread * 0.80 - n_hat * 0.28
        tdir = tdir / np.linalg.norm(tdir)
        _finger(mb, "hand_" + side, tb, tdir, n_hat, fl_ * 0.66, fr * 1.16,
                curl=0.12)


def build_legs(mb: M.MeshBuilder, p: dict, a: Anatomy, *, bare: bool = True):
    r0, r1, r2 = a.leg_r
    for sgn in (-1, 1):
        side = "l" if sgn > 0 else "r"
        hp = a.hip_joint * np.array([sgn, 1, 1])
        kn = a.knee * np.array([sgn, 1, 1])
        an = a.ankle * np.array([sgn, 1, 1])
        thigh_mid = hp + (kn - hp) * 0.42 + np.array([0.0, -0.004, 0.0])
        calf_mid = kn + (an - kn) * 0.36 + np.array([0.0, -0.006, 0.0])
        path = np.array([hp + (hp - kn) * 0.10, thigh_mid, kn, calf_mid, an])
        radii = [r0 * 1.02, r0 * 0.86, r1, r1 * 1.12, r2]
        _limb(mb, path, radii, "skin", f"leg_{side}")
        _ = bare
        # 足
        fw, fl_, fh = a.foot
        cx = float(an[0])
        with mb.part(f"foot_{side}"):
            mb.add_rounded_box((cx, -fl_ * 0.26, fh * 0.52),
                               (fw, fl_, fh), "skin", seg=5, smooth=True)


# --------------------------------------------------------------------------


def build_base(mb: M.MeshBuilder, p: dict):
    """素体一式を組む。戻り値は (Anatomy, Head, FaceSurface, uv_box, face_uvs)。"""
    a = Anatomy(p)
    head = build_head(mb, p)

    head_pts = np.array(mb.verts[mb.parts["head"][0][0]:mb.parts["head"][0][1]])
    fs = FaceSurface(head_pts)

    hw = p["head_w"]
    uv_box = (-hw * 0.72, hw * 0.72,
              p["z"]["chin"] - p["head_h"] * 0.015, p["z"]["top"])

    build_neck(mb, p, a)
    build_torso(mb, p, a)
    build_arms(mb, p, a)
    build_legs(mb, p, a)
    face_uvs = build_face_parts(mb, p, fs, uv_box)
    return a, head, fs, uv_box, face_uvs


def assign_uvs(obj, uv_box, special: dict) -> None:
    """平面投影 UV を貼る。顔パーツだけ feature タイルへ差し替える。"""
    me = obj.data
    uvl = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    x0, x1, z0, z1 = uv_box
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    u = np.clip((co[:, 0] - x0) / (x1 - x0), 0.0, 1.0)
    v = np.clip((co[:, 2] - z0) / (z1 - z0), 0.0, 1.0)
    uu = TILE_BASE[0] + (TILE_BASE[1] - TILE_BASE[0]) * u
    vv = TILE_BASE[2] + (TILE_BASE[3] - TILE_BASE[2]) * v
    for vidx, (su, sv) in special.items():
        uu[vidx] = su
        vv[vidx] = sv
    loop_v = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", loop_v)
    uv = np.stack([uu[loop_v], vv[loop_v]], axis=1).astype(np.float32)
    uvl.data.foreach_set("uv", np.ascontiguousarray(uv).ravel())
    me.update()

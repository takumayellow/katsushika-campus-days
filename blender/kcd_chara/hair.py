"""髪型の生成。

頭皮のシェル（頭より一回り大きい殻）＋ベジエ芯線から作る房（strand）の組み合わせ。
房は断面を扁平にしてあるので、少ない本数でもボリュームのある面に見える。
"""

from __future__ import annotations

import math

import numpy as np

from . import mesh as M

FRONT = 1.5 * math.pi  # 正面（-Y）の方位角


def _angdist(az: np.ndarray) -> np.ndarray:
    """正面からの角度差（0..pi）。"""
    return np.abs(((np.asarray(az) - FRONT + math.pi) % (2 * math.pi)) - math.pi)


def _el_max(az: np.ndarray, front: float, back: float, power: float = 0.75):
    d = _angdist(az) / math.pi
    return front + (back - front) * d**power


def _outward(head, pts: np.ndarray, off) -> np.ndarray:
    d = pts - head.center
    d = d / (np.linalg.norm(d, axis=1, keepdims=True) + 1e-9)
    off = np.asarray(off, dtype=float).reshape(-1, 1)
    return pts + d * off


def scalp_pt(head, az, el, off: float) -> np.ndarray:
    az = np.atleast_1d(np.asarray(az, dtype=float))
    el = np.atleast_1d(np.asarray(el, dtype=float))
    pts = head.surface(az, el)
    return _outward(head, pts, np.full(len(az), off))


def build_scalp(mb: M.MeshBuilder, p: dict, head, *, front_el: float,
                back_el: float, thickness: float, part: str = "hair_cap"):
    nu, nv = 32, 10
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    emax = _el_max(az, front_el, back_el)
    t_hair = p["head_w"] * thickness
    rings = []
    for j in range(nv + 1):
        t = j / nv
        el = np.maximum(t * emax, 1e-3)
        off = t_hair * (0.55 + 0.45 * math.sin(min(1.0, t) * math.pi) ** 0.5)
        pts = head.surface(az, el)
        rings.append(_outward(head, pts, np.full(nu, off)))
    with mb.part(part):
        mb.add_grid(rings, "hair", smooth=True, cap_start=True, cap_end=False)
    return rings[-1]


def strand(mb: M.MeshBuilder, part: str, ctrl, r0: float, r1: float, *,
           n: int = 8, flat: float = 0.44, power: float = 2.6, seg: int = 11,
           taper: float = 1.5, mat: str = "hair"):
    path = M.bezier3(ctrl[0], ctrl[1], ctrl[2], ctrl[3], seg)
    t = np.linspace(0.0, 1.0, seg)
    rr = r0 + (r1 - r0) * t**taper
    radii = [(float(r * flat), float(r)) for r in rr]
    with mb.part(part):
        mb.add_tube(path, radii, mat, n=n, power=power, cap_start=False,
                    cap_end=True)


# --------------------------------------------------------------------------
# 房の配置
# --------------------------------------------------------------------------


def _thicken(P: np.ndarray, t: float) -> np.ndarray:
    """(nu, k, 3) のシート点列を面法線方向へ t だけ内側へずらした点列。"""
    du = np.gradient(P, axis=0)
    dv = np.gradient(P, axis=1)
    n = np.cross(du, dv)
    n = n / (np.linalg.norm(n, axis=2, keepdims=True) + 1e-9)
    return P - n * t


def _sheet(mb, part: str, ctrl_fn, us, *, seg: int = 11, frac: float = 0.82,
           thick: float = 0.0, mat: str = "hair") -> None:
    """房の裏に張る薄いシェル。房と房の隙間から地肌が見えるのを防ぐ。"""
    paths = [M.bezier3(*ctrl_fn(float(u)), seg) for u in us]
    P = np.stack(paths, axis=0)
    k = max(2, int(round(seg * frac)))
    P = P[:, :k, :]
    Q = _thicken(P, thick)
    with mb.part(part):
        mb.add_grid([P[:, j, :] for j in range(k)], mat, smooth=True,
                    close_u=False)
        mb.add_grid([Q[:, j, :] for j in range(k)], mat, smooth=True,
                    close_u=False, flip=True)
        mb.add_grid([P[:, k - 1, :], Q[:, k - 1, :]], mat, smooth=False,
                    close_u=False)


def _bang_ctrl(p, head, u: float, *, span: float, el: float, z_end: float,
               parted: float, sweep: float, jag: float, phase: float,
               lift: float):
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = FRONT + u * span
    s = scalp_pt(head, az, el, hw * lift)[0]
    side = math.sin(u * span)
    sign_u = 1.0 if u >= 0.0 else -1.0
    x_end = s[0] * 1.14 + parted * hw * 0.10 * sign_u + sweep * hw * 0.30
    y_end = -hd * (0.44 + 0.05 * (1.0 - abs(side)))
    # 房ごとに長さを散らす。全部同じ丈だと毛先が四角い歯のように並ぶ。
    wob = (math.sin(phase * 2.399) * 0.60
           + math.sin(phase * 5.131 + 1.7) * 0.40)
    jz = hh * 0.040 * jag * wob - hh * 0.010 * abs(u)
    # 中央を短く、こめかみへ向かって長くする（額の中央から眉が出る）
    e = np.array([x_end, y_end, z_end - hh * 0.085 * abs(u) ** 1.6 + jz])
    c1 = s + np.array([0.0, -hd * 0.14, hh * 0.02])
    c2 = e + np.array([0.0, -hd * 0.05, hh * 0.13])
    return s, c1, c2, e


def _bangs(mb, p, head, *, span: float, count: int, el: float,
           end_v: float, width: float, parted: float = 0.0, sweep: float = 0.0,
           jag: float = 1.0):
    """前髪。5〜7 房の独立した房で構成する。

    - 房は毛先へ向かって細り、先端が尖る（taper）。
    - 房は頭皮から浮かせて（lift）空気層を作る。
    - 房の裏に薄いシェルを張り、房の隙間から地肌が見えないようにする。
    """
    hw, hh = p["head_w"], p["head_h"]
    z0 = p["z"]["chin"] - hh * 0.015
    z1 = p["z"]["top"]
    z_end = z0 + (z1 - z0) * end_v
    n = int(min(9, max(6, round(count * 0.55))))

    # 裏当て。房より少し長くしておかないと房の隙間から地肌が覗く。
    _sheet(mb, "hair_front",
           lambda u: _bang_ctrl(p, head, u, span=span * 1.02, el=el + 0.03,
                                z_end=z_end - hh * 0.030, parted=parted,
                                sweep=sweep, jag=0.0, phase=0.0, lift=0.026),
           np.linspace(-1.0, 1.0, 23), seg=11, frac=0.86, thick=hw * 0.018)

    r0 = hw * (span / n) * 0.92
    for i in range(n):
        u = (i + 0.5) / n * 2.0 - 1.0
        ctrl = _bang_ctrl(p, head, u, span=span, el=el, z_end=z_end,
                          parted=parted, sweep=sweep, jag=jag, phase=i,
                          lift=0.078)
        strand(mb, "hair_front", ctrl, r0, r0 * 0.05, n=10, flat=0.54,
               power=2.05, seg=14, taper=2.2)
        if i < n - 1:
            u2 = u + 1.0 / n
            ctrl2 = _bang_ctrl(p, head, u2, span=span, el=el - 0.055,
                               z_end=z_end - hh * 0.048, parted=parted,
                               sweep=sweep, jag=jag * 1.5, phase=i + 0.5,
                               lift=0.050)
            strand(mb, "hair_front", ctrl2, r0 * 0.58, r0 * 0.06, n=8,
                   flat=0.52, power=2.05, seg=12, taper=2.0)


def _side(mb, p, head, *, count: int, az_lo: float, az_hi: float, el: float,
          z_end: float, width: float, curl: float = 0.0):
    """横髪。房数を絞って 1 房を太くし、毛先を尖らせる。"""
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    n = max(4, int(round(count * 1.35)))
    w = width * math.sqrt(count / n) * 0.66
    for sgn in (-1, 1):
        for i in range(n):
            t = (i + 0.5) / n
            az = FRONT + sgn * (az_lo + (az_hi - az_lo) * t)
            s = scalp_pt(head, az, el, hw * 0.062)[0]
            # 顔寄りの房を短く、後ろへ行くほど長くし、さらに房ごとに散らす。
            # 全部同じ丈だと横から見て段ボールの板になる。
            drop = hh * (0.30 * (1.0 - t) ** 1.6
                         - 0.075 * math.sin(i * 2.399)
                         - 0.045 * math.sin(i * 5.131 + 1.1))
            e = np.array([s[0] * (0.82 + 0.04 * t) + sgn * curl * hw * 0.12,
                          s[1] * 0.52 + hd * 0.02,
                          z_end + drop])
            c1 = s + np.array([sgn * hw * 0.08, 0.0, -hh * 0.18])
            c2 = np.array([e[0] + sgn * hw * 0.05, e[1],
                           e[2] + (s[2] - e[2]) * 0.34])
            strand(mb, "hair_side", (s, c1, c2, e), hw * w, hw * w * 0.05,
                   n=8, flat=0.70, power=2.1, seg=13, taper=2.3)


def _back_ctrl(p, head, u: float, *, el: float, z_end: float, puff: float,
               lift: float, jag: float = 0.0, phase: float = 0.0):
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = FRONT + math.pi + u * math.radians(86.0)
    s = scalp_pt(head, az, el, hw * lift)[0]
    jz = hh * (0.055 * jag * math.sin(phase * 2.399)
               + 0.030 * jag * math.sin(phase * 5.131 + 0.8))
    # 毛先へ向けて幅を絞る。頭の幅より外へ出ると、横から見たときに
    # 後ろ髪が扇のように張り出して見える。
    e = np.array([s[0] * 0.74, hd * (0.10 + 0.05 * (1.0 - abs(u))), z_end + jz])
    c1 = s + np.array([0.0, hd * 0.05 * puff, -hh * 0.30])
    c2 = np.array([e[0] * 1.06, e[1] + hd * 0.03 * puff,
                   e[2] + (s[2] - e[2]) * 0.34])
    return s, c1, c2, e


def _back(mb, p, head, *, count: int, el: float, z_end: float, width: float,
          puff: float = 1.0):
    """後ろ髪。裏当てシェル＋太めの房。毛先は尖らせる。"""
    hw, hh = p["head_w"], p["head_h"]
    n = max(8, int(round(count * 0.92)))
    w = width * math.sqrt(count / n) * 0.92

    _sheet(mb, "hair_back",
           lambda u: _back_ctrl(p, head, u, el=el - 0.05,
                                z_end=z_end + hh * 0.05, puff=puff,
                                lift=0.026),
           np.linspace(-1.0, 1.0, 23), seg=11, frac=0.86, thick=hw * 0.015)

    for i in range(n):
        u = (i + 0.5) / n * 2.0 - 1.0
        ctrl = _back_ctrl(p, head, u, el=el, z_end=z_end, puff=puff,
                          lift=0.062, jag=1.0, phase=i)
        strand(mb, "hair_back", ctrl, hw * w, hw * w * 0.05, n=8, flat=0.70,
               power=2.1, seg=13, taper=2.3)


def _tail(mb, p, head, origin, direction, length: float, *, width: float,
          seg_part: str, droop: float = 0.55):
    hw = p["head_w"]
    d = M.normalize(direction)
    down = np.array([0.0, 0.0, -1.0])
    s = np.asarray(origin, dtype=float)
    c1 = s + d * length * 0.34
    c2 = s + d * length * 0.62 + down * length * droop * 0.6
    e = s + d * length * 0.72 + down * length * droop
    strand(mb, seg_part, (s, c1, c2, e), hw * width, hw * width * 0.12,
           n=10, flat=0.62, seg=15, taper=2.1)


# --------------------------------------------------------------------------
# アクセサリ
# --------------------------------------------------------------------------


def _hairpin(mb, p, head):
    hw, hh = p["head_w"], p["head_h"]
    az = FRONT + math.radians(38.0)
    base = scalp_pt(head, az, 0.72, hw * 0.10)[0]
    for k in range(3):
        c = base + np.array([0.0, 0.0, -hh * 0.030 * k])
        with mb.part("hair_acc"):
            mb.add_rounded_box((c[0], c[1], c[2]), (hw * 0.055, hw * 0.16, hh * 0.022),
                               "metal", seg=4, smooth=True,
                               rot_z=math.radians(12.0))


def _bow(mb, p, head, mat: str = "ribbon_red", scale: float | None = None):
    """頭頂の大きなリボン。`bow_scale` で原作の大きさに合わせて拡大する。"""
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    bs = float(p.get("bow_scale", 1.0) if scale is None else scale)
    az = FRONT + math.radians(10.0)
    knot = scalp_pt(head, az, 0.46, hw * 0.14)[0]
    with mb.part("hair_acc"):
        mb.add_sphere(tuple(knot), (hw * 0.100 * bs, hw * 0.088 * bs,
                                    hh * 0.082 * bs), mat, nu=12, nv=8)
    up = np.array([0.0, 0.0, 1.0])
    # 結び目が頭頂に近いと out がほぼ真上になり、cross が退化して羽が
    # ウサギの耳のように立ってしまう。左右は素直にワールド X 軸で取る。
    side = np.array([1.0, 0.0, 0.0])
    fwd = np.array([0.0, -1.0, 0.0])
    # 羽は扁平な筒で作る。球だと正面から赤い団子 2 つにしか見えない。
    for sgn in (-1, 1):
        d = M.normalize(side * sgn + fwd * 0.16)
        c0 = knot + d * hw * 0.04 * bs
        c1 = knot + d * hw * 0.16 * bs + up * hh * 0.036 * bs
        c2 = knot + d * hw * 0.28 * bs + up * hh * 0.020 * bs
        c3 = knot + d * hw * 0.33 * bs - up * hh * 0.026 * bs
        strand(mb, "hair_acc", (c0, c1, c2, c3),
               hw * 0.055 * bs, hw * 0.185 * bs, flat=0.78, power=2.2,
               taper=1.0, mat=mat)
    # 垂れ
    for sgn in (-1, 1):
        st = knot + side * sgn * hw * 0.05 * bs
        e = st + np.array([side[0] * sgn * hw * 0.10 * bs, hd * 0.04,
                           -hh * 0.42 * bs])
        strand(mb, "hair_acc", (st, st + np.array([0.0, 0.0, -hh * 0.16 * bs]),
                                e + np.array([0.0, 0.0, hh * 0.14 * bs]), e),
               hw * 0.055 * bs, hw * 0.045 * bs, flat=0.30, power=3.0, mat=mat)


def _tail_bow(mb, p, base, sgn: int, mat: str = "ribbon_red") -> None:
    """ツインテールの結び目リボン。"""
    hw, hh = p["head_w"], p["head_h"]
    base = np.asarray(base, dtype=float)
    with mb.part("hair_acc"):
        mb.add_sphere(tuple(base), (hw * 0.070, hw * 0.062, hh * 0.058), mat,
                      nu=12, nv=8)
    side = np.array([0.0, 1.0, 0.0])
    up = np.array([0.0, 0.0, 1.0])
    out = np.array([float(sgn), 0.0, 0.0])
    for s2 in (-1, 1):
        d = M.normalize(side * s2 + out * 0.35 + up * 0.25)
        c0 = base + d * hw * 0.04
        c1 = base + d * hw * 0.18 + up * hh * 0.050
        c2 = base + d * hw * 0.32 + up * hh * 0.030
        c3 = base + d * hw * 0.36 - up * hh * 0.038
        strand(mb, "hair_acc", (c0, c1, c2, c3), hw * 0.030, hw * 0.098,
               flat=0.30, power=2.8, taper=1.1, mat=mat)


def _bandana(mb, p, head, mat: str = "cloth_bandana") -> None:
    """頭に巻くバンダナ。帯＋横結び。"""
    hw, hh = p["head_w"], p["head_h"]
    nu = 28
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    els = (0.40, 0.72)
    t = hw * 0.030
    outer = [_outward(head, head.surface(az, np.full(nu, e)),
                      np.full(nu, hw * 0.115)) for e in els]
    inner = [_outward(head, head.surface(az, np.full(nu, e)),
                      np.full(nu, hw * 0.115 - t)) for e in els]
    rings = [outer[0], outer[1], inner[1], inner[0], outer[0]]
    with mb.part("hair_acc"):
        mb.add_grid(rings, mat, smooth=False, close_u=True)
    # 横の結び目と垂れ
    knot = scalp_pt(head, FRONT + math.radians(104.0), 0.60, hw * 0.16)[0]
    with mb.part("hair_acc"):
        mb.add_sphere(tuple(knot), (hw * 0.062, hw * 0.052, hh * 0.048), mat,
                      nu=10, nv=6)
    for k, dz in ((0, -0.06), (1, 0.02)):
        d = M.normalize(np.array([0.30, -0.25 + 0.5 * k, -0.90 + dz]))
        strand(mb, "hair_acc",
               (knot, knot + d * hh * 0.10, knot + d * hh * 0.22,
                knot + d * hh * 0.30), hw * 0.042, hw * 0.012,
               flat=0.34, power=3.0, taper=1.8, mat=mat)


def _bow_shape(pts: np.ndarray, d: np.ndarray) -> np.ndarray:
    """楕円体を d 方向へ寄せてリボンの羽らしく絞る。"""
    t = np.clip(0.5 + 0.5 * (pts @ M.normalize(d)) / (np.abs(pts).max() + 1e-9), 0, 1)
    k = (0.35 + 0.65 * t)[:, None]
    return pts * np.array([1.0, 1.0, 1.0]) * k + M.normalize(d) * 0.0


def _glasses(mb, p, head, fs, uv_box, mat: str = "glasses"):
    fl = p["face_layout"]
    x0, x1, z0, z1 = uv_box
    hw = p["head_w"]
    ex = (x1 - x0) * fl["eye_dx"]
    ez = z0 + (z1 - z0) * fl["eye_y"]
    rx = (x1 - x0) * fl["eye_rx"] * 1.42
    rz = (z1 - z0) * fl["eye_ry"] * 1.28
    tube = hw * 0.016
    for sgn in (-1, 1):
        cx = sgn * ex
        yy = fs.y_at(cx, ez) - hw * 0.055
        n = 22
        a = np.linspace(0.0, 2 * math.pi, n, endpoint=False)
        e = 2.0 / 3.0
        px = cx + np.sign(np.cos(a)) * np.abs(np.cos(a)) ** e * rx
        pz = ez + np.sign(np.sin(a)) * np.abs(np.sin(a)) ** e * rz
        loop = np.stack([px, np.full(n, yy), pz], axis=1)
        path = np.vstack([loop, loop[:1]])
        with mb.part("glasses"):
            mb.add_tube(path, [tube] * len(path), mat, n=6, cap_start=False,
                        cap_end=False)
        # つる
        ear = np.array([sgn * hw * 0.50, p["head_d"] * 0.10, ez + rz * 0.30])
        temple = np.array([cx + sgn * rx, yy + hw * 0.02, ez + rz * 0.30])
        with mb.part("glasses"):
            mb.add_tube(np.array([temple, (temple + ear) * 0.5, ear]),
                        [tube * 0.9] * 3, mat, n=6)
    # ブリッジ
    ybr = fs.y_at(0.0, ez) - hw * 0.050
    with mb.part("glasses"):
        mb.add_tube(np.array([[-ex + rx * 0.9, ybr, ez + rz * 0.12],
                              [0.0, ybr - hw * 0.010, ez + rz * 0.16],
                              [ex - rx * 0.9, ybr, ez + rz * 0.12]]),
                    [tube * 0.85] * 3, mat, n=6)


# --------------------------------------------------------------------------


def build_hair(mb: M.MeshBuilder, p: dict, head, a, fs, uv_box) -> None:
    style = p["hair"]
    z = p["z"]
    hh = p["head_h"]
    hw = p["head_w"]
    chin = z["chin"]

    if style == "bob":
        build_scalp(mb, p, head, front_el=0.63, back_el=1.95, thickness=0.165)
        _bangs(mb, p, head, span=math.radians(82.0), count=17, el=0.58,
               end_v=0.614, width=0.152)
        _side(mb, p, head, count=7, az_lo=math.radians(52.0),
              az_hi=math.radians(126.0), el=1.20,
              z_end=chin - hh * 0.30, width=0.196, curl=0.40)
        _back(mb, p, head, count=15, el=1.78, z_end=chin - hh * 0.22,
              width=0.200, puff=1.80)

    elif style == "long_blunt":
        build_scalp(mb, p, head, front_el=0.70, back_el=1.94, thickness=0.104)
        _bangs(mb, p, head, span=math.radians(80.0), count=15, el=0.62,
               end_v=0.655, width=0.116)
        _side(mb, p, head, count=7, az_lo=math.radians(52.0),
              az_hi=math.radians(126.0), el=1.16,
              z_end=z["bust"], width=0.132, curl=0.14)
        _back(mb, p, head, count=13, el=1.66, z_end=z["waist"] - 0.02,
              width=0.140, puff=1.30)

    elif style == "twintail":
        build_scalp(mb, p, head, front_el=0.64, back_el=1.72, thickness=0.082)
        _bangs(mb, p, head, span=math.radians(76.0), count=11, el=0.58,
               end_v=0.650, width=0.110)
        _side(mb, p, head, count=3, az_lo=math.radians(64.0),
              az_hi=math.radians(104.0), el=1.20,
              z_end=chin + hh * 0.10, width=0.115, curl=0.22)
        _back(mb, p, head, count=6, el=1.60, z_end=chin + hh * 0.22, width=0.115)
        for sgn in (-1, 1):
            base = scalp_pt(head, FRONT + sgn * math.radians(100.0), 0.86,
                            hw * 0.12)[0]
            if p.get("tail_ribbon"):
                _tail_bow(mb, p, base, sgn)
            else:
                with mb.part("hair_acc"):
                    mb.add_sphere(tuple(base),
                                  (hw * 0.075, hw * 0.075, hh * 0.060),
                                  "ribbon_red", nu=12, nv=8)
            for k, spread in enumerate((-0.30, 0.0, 0.30)):
                d = np.array([sgn * 0.34, spread * 0.40, -0.58])
                _tail(mb, p, head, base, d, hh * 1.52,
                      width=0.168 - 0.028 * abs(k - 1), seg_part="hair_tail",
                      droop=1.05)

    elif style == "ponytail":
        build_scalp(mb, p, head, front_el=0.70, back_el=1.72, thickness=0.080)
        _bangs(mb, p, head, span=math.radians(72.0), count=10, el=0.62,
               end_v=0.668, width=0.105, sweep=0.20)
        _side(mb, p, head, count=2, az_lo=math.radians(66.0),
              az_hi=math.radians(96.0), el=1.16,
              z_end=chin + hh * 0.16, width=0.100, curl=0.10)
        _back(mb, p, head, count=6, el=1.56, z_end=chin + hh * 0.30, width=0.110)
        base = scalp_pt(head, FRONT + math.pi, 0.52, hw * 0.10)[0]
        with mb.part("hair_acc"):
            mb.add_sphere(tuple(base), (hw * 0.070, hw * 0.070, hh * 0.058),
                          "cloth_ribbon_green", nu=12, nv=8)
        # 細い房を離して並べると箒になる。太めに重ねて 1 本の尾に見せる。
        for spread in (-0.24, -0.08, 0.08, 0.24):
            _tail(mb, p, head, base, np.array([spread, 1.0, -0.08]), hh * 1.95,
                  width=0.172, seg_part="hair_tail", droop=1.15)

    elif style == "short":
        build_scalp(mb, p, head, front_el=0.68, back_el=1.74, thickness=0.086)
        _bangs(mb, p, head, span=math.radians(78.0), count=11, el=0.62,
               end_v=0.662, width=0.112)
        _side(mb, p, head, count=3, az_lo=math.radians(64.0),
              az_hi=math.radians(112.0), el=1.20,
              z_end=chin + hh * 0.06, width=0.120, curl=0.34)
        _back(mb, p, head, count=8, el=1.62, z_end=chin + hh * 0.20, width=0.120)

    elif style in ("slickback", "short_old"):
        # 頭頂を跨ぐ長い房を放射状に並べると房の間が開いてトゲトゲの
        # カツラになる。厚めの地髪＋短い前髪・横髪・襟足で構成する。
        front_el = 0.58 if style == "slickback" else 0.52
        build_scalp(mb, p, head, front_el=front_el, back_el=1.80,
                    thickness=0.108)
        _bangs(mb, p, head, span=math.radians(88.0), count=11,
               el=front_el - 0.05,
               end_v=0.668 if style == "slickback" else 0.678,
               width=0.098, sweep=0.22 if style == "slickback" else 0.0,
               jag=2.8 if style == "slickback" else 1.8)
        _side(mb, p, head, count=2, az_lo=math.radians(70.0),
              az_hi=math.radians(96.0), el=1.20,
              z_end=chin + hh * 0.70, width=0.058, curl=0.0)
        _back(mb, p, head, count=9, el=1.72, z_end=chin + hh * 0.48,
              width=0.102, puff=0.85)

    acc = p.get("hair_accessory")
    if acc == "pin_blue":
        _hairpin(mb, p, head)
    elif acc == "bow_red":
        _bow(mb, p, head)
    elif acc == "bandana":
        _bandana(mb, p, head)
    elif acc in ("glasses_red", "glasses_gray"):
        _glasses(mb, p, head, fs, uv_box)

    _ = a

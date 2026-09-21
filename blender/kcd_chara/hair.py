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


def _smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def build_helmet(mb: M.MeshBuilder, p: dict, head, *, front_el: float,
                 back_el: float, thickness: float, z_end_side: float,
                 z_end_back: float, puff: float = 1.0, ridges: int = 14,
                 ridge_amp: float = 0.30, jag: float = 0.030,
                 hang_lo: float = 0.26, hang_hi: float = 0.50,
                 inward: float = 0.74, depth: float = 0.10,
                 part: str = "hair_back", nu: int = 72, nv: int = 10,
                 nh: int = 12):
    """地髪と横髪・後ろ髪を 1 枚の連続した殻で作る。

    房を並べる方式だと、地髪の縁から房が垂れる境目が「帽子の下のプリーツ
    カーテン」に見える。殻を生え際から毛先まで途切れなく続け、縦の畝
    （ridges）と毛先のギザギザ（jag）で房の感じを出す。
    """
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = np.linspace(0.0, 2 * math.pi, nu, endpoint=False)
    emax = _el_max(az, front_el, back_el)
    d = _angdist(az) / math.pi                     # 0=正面 .. 1=真後ろ
    hang = _smooth((d - hang_lo) / (hang_hi - hang_lo))  # 垂れる割合
    wb = _smooth((d - 0.50) / 0.50)                # 後ろ寄りの重み
    t_hair = hw * thickness
    crest = 0.5 + 0.5 * np.cos(az * ridges)        # 畝の山
    rings = []
    for j in range(nv + 1):
        t = j / nv
        el = np.maximum(t * emax, 1e-3)
        off = t_hair * (0.55 + 0.45 * math.sin(min(1.0, t) * math.pi) ** 0.5)
        off = off * (1.0 + ridge_amp * crest * t ** 2 * hang)
        pts = head.surface(az, el)
        rings.append(_outward(head, pts, off))
    rim = rings[-1]
    nrm = rim * np.array([1.0, 1.0, 0.0])
    nrm = nrm / (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9)

    # 毛先の位置。畝の山ほど低く（尖る）、正面は垂らさない。
    z_end = z_end_side + (z_end_back - z_end_side) * wb
    z_end = z_end + hh * jag * (np.cos(az * ridges) - 0.3)
    z_end = np.minimum(z_end, rim[:, 2] - hh * 0.02)
    z_end = rim[:, 2] + (z_end - rim[:, 2]) * hang
    ex = rim[:, 0] * (0.90 - (0.90 - inward) * wb)
    ey = rim[:, 1] * (1.0 - wb) * 0.70 + hd * depth * wb + hd * 0.02
    e = np.stack([ex, ey, z_end], axis=1)
    e = rim + (e - rim) * hang[:, None]
    drop = (rim[:, 2] - e[:, 2])[:, None]
    c1 = rim + nrm * (hw * 0.055 * puff * hang[:, None]) - drop * 0.30 * np.array([0, 0, 1.0])
    c2 = e + nrm * (hw * 0.030 * puff * hang[:, None]) + drop * 0.34 * np.array([0, 0, 1.0])
    outer, inner = [], []
    for k in range(1, nh + 1):
        v = k / nh
        b0, b1, b2, b3 = (1 - v) ** 3, 3 * v * (1 - v) ** 2, 3 * v * v * (1 - v), v ** 3
        pos = b0 * rim + b1 * c1 + b2 * c2 + b3 * e
        ridge = nrm * (t_hair * ridge_amp * crest * hang * (1.0 - 0.55 * v))[:, None]
        outer.append(pos + ridge)
        inner.append(pos + ridge - nrm * (hw * 0.022 * hang)[:, None])
    with mb.part(part):
        mb.add_grid(rings + outer, "hair", smooth=True, cap_start=True,
                    cap_end=False)
        mb.add_grid(inner, "hair", smooth=True, flip=True)
        mb.add_grid([outer[-1], inner[-1]], "hair", smooth=False)
    return rim


def strand(mb: M.MeshBuilder, part: str, ctrl, r0: float, r1: float, *,
           n: int = 8, flat: float = 0.44, power: float = 2.6, seg: int = 11,
           taper: float = 1.5, mat: str = "hair", root: float = 1.0):
    """芯線 ctrl に沿う房。root < 1 なら根元を r0*root から太らせる。

    根元を頭皮の内側に置いて細く始めると、房の切り口が地髪の外に段差
    （棚）として見えなくなり、地髪から房が生えているように繋がる。
    """
    path = M.bezier3(ctrl[0], ctrl[1], ctrl[2], ctrl[3], seg)
    t = np.linspace(0.0, 1.0, seg)
    rr = r0 + (r1 - r0) * t**taper
    if root < 1.0:
        g = np.clip(t / 0.32, 0.0, 1.0)
        rr = rr * (root + (1.0 - root) * (g * g * (3.0 - 2.0 * g)))
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
               lift: float, tuck: float = 0.0):
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
    e = np.array([x_end, y_end + hd * 0.040 * tuck,
                  z_end - hh * 0.085 * abs(u) ** 1.6 + jz])
    c1 = s + np.array([0.0, -hd * 0.14, hh * 0.02])
    # 毛先の手前で一度前へ出してから内へ戻すと、房が額に沿って丸く
    # 内巻きになる（板を貼り付けたようにならない）
    c2 = e + np.array([0.0, -hd * (0.05 + 0.07 * tuck), hh * 0.13])
    # 根元は地髪の中。浮かせたまま始めると、房の切り口が頭頂側から
    # ギザギザの冠のように見える。
    s0 = scalp_pt(head, az, el - 0.20, hw * 0.010)[0]
    return s0, c1, c2, e


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
    n = int(min(7, max(5, round(count * 0.42))))

    # 裏当て。房より少し長くしておかないと房の隙間から地肌が覗く。
    _sheet(mb, "hair_front",
           lambda u: _bang_ctrl(p, head, u, span=span * 1.02, el=el + 0.03,
                                z_end=z_end - hh * 0.030, parted=parted,
                                sweep=sweep, jag=0.0, phase=0.0, lift=0.026),
           np.linspace(-1.0, 1.0, 23), seg=11, frac=0.90, thick=hw * 0.018)

    r0 = hw * (span / n) * 0.98
    for i in range(n):
        u = (i + 0.5) / n * 2.0 - 1.0
        # 房ごとに頭皮からの浮きを変える。全部同じだと 1 枚の板に見える。
        lift = 0.070 + 0.030 * math.sin(i * 2.399 + 0.6)
        ctrl = _bang_ctrl(p, head, u, span=span, el=el, z_end=z_end,
                          parted=parted, sweep=sweep, jag=jag, phase=i,
                          lift=lift, tuck=0.9)
        strand(mb, "hair_front", ctrl, r0, r0 * 0.05, n=10, flat=0.74,
               power=2.0, seg=14, taper=2.0, root=0.30)
        if i < n - 1:
            u2 = u + 1.0 / n
            ctrl2 = _bang_ctrl(p, head, u2, span=span, el=el - 0.055,
                               z_end=z_end - hh * 0.048, parted=parted,
                               sweep=sweep, jag=jag * 1.5, phase=i + 0.5,
                               lift=0.046, tuck=0.6)
            strand(mb, "hair_front", ctrl2, r0 * 0.52, r0 * 0.06, n=8,
                   flat=0.66, power=2.0, seg=12, taper=1.9, root=0.30)


def _side(mb, p, head, *, count: int, az_lo: float, az_hi: float, el: float,
          z_end: float, width: float, curl: float = 0.0):
    """横髪。房数を絞って 1 房を太くし、毛先を尖らせる。"""
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    n = max(3, int(round(count * 0.95)))
    w = width * math.sqrt(count / n) * 0.80
    for sgn in (-1, 1):
        for i in range(n):
            t = (i + 0.5) / n
            az = FRONT + sgn * (az_lo + (az_hi - az_lo) * t)
            row = i % 2
            lift = 0.050 + 0.032 * row
            # 根元は地髪の中（el を上へ、浮きをほぼ 0 に）。房の切り口が
            # 地髪の外へ出ると、横から見てそこが棚になる。
            s0 = scalp_pt(head, az, el - 0.30, hw * 0.012)[0]
            s = scalp_pt(head, az, el, hw * lift)[0]
            # 顔寄りの房を短く、後ろへ行くほど長くし、さらに房ごとに散らす。
            # 全部同じ丈だと横から見て段ボールの板になる。
            drop = hh * (0.30 * (1.0 - t) ** 1.6
                         - 0.075 * math.sin(i * 2.399)
                         - 0.045 * math.sin(i * 5.131 + 1.1))
            # 毛先の前後位置も房ごとに変える（同じ深さで揃うと 1 枚の板）
            depth = 0.52 + 0.09 * row - 0.05 * math.sin(i * 2.399 + 0.4)
            e = np.array([s[0] * (0.82 + 0.04 * t) + sgn * curl * hw * 0.12,
                          s[1] * depth + hd * 0.02,
                          z_end + drop])
            c1 = s + np.array([sgn * hw * 0.06, 0.0, -hh * 0.12])
            c2 = np.array([e[0] + sgn * hw * 0.05, e[1],
                           e[2] + (s[2] - e[2]) * 0.34])
            strand(mb, "hair_side", (s0, c1, c2, e), hw * w, hw * w * 0.05,
                   n=9, flat=0.86, power=2.0, seg=14, taper=2.1, root=0.28)


def _back_ctrl(p, head, u: float, *, el: float, z_end: float, puff: float,
               lift: float, jag: float = 0.0, phase: float = 0.0,
               depth: float = 0.0, sink: float = 0.0):
    """sink > 0 なら根元を el - sink の位置（地髪の中）に置く。"""
    hw, hd, hh = p["head_w"], p["head_d"], p["head_h"]
    az = FRONT + math.pi + u * math.radians(86.0)
    s = scalp_pt(head, az, el, hw * lift)[0]
    s0 = scalp_pt(head, az, el - sink, hw * 0.012)[0] if sink > 0 else s
    jz = hh * (0.055 * jag * math.sin(phase * 2.399)
               + 0.030 * jag * math.sin(phase * 5.131 + 0.8))
    # 毛先へ向けて幅を絞る。頭の幅より外へ出ると、横から見たときに
    # 後ろ髪が扇のように張り出して見える。
    e = np.array([s[0] * 0.74, hd * (0.10 + 0.05 * (1.0 - abs(u)) + depth),
                  z_end + jz])
    c1 = s + np.array([0.0, hd * 0.05 * puff, -hh * 0.22])
    c2 = np.array([e[0] * 1.06, e[1] + hd * 0.03 * puff,
                   e[2] + (s[2] - e[2]) * 0.34])
    return s0, c1, c2, e


def _back(mb, p, head, *, count: int, el: float, z_end: float, width: float,
          puff: float = 1.0):
    """後ろ髪。裏当てシェル＋太めの房。毛先は尖らせる。"""
    hw, hh = p["head_w"], p["head_h"]
    n = max(6, int(round(count * 0.66)))
    w = width * math.sqrt(count / n) * 1.02

    _sheet(mb, "hair_back",
           lambda u: _back_ctrl(p, head, u, el=el - 0.05,
                                z_end=z_end + hh * 0.05, puff=puff,
                                lift=0.026),
           np.linspace(-1.0, 1.0, 23), seg=11, frac=0.86, thick=hw * 0.015)

    for i in range(n):
        u = (i + 0.5) / n * 2.0 - 1.0
        row = i % 2
        lift = 0.052 + 0.028 * row
        ctrl = _back_ctrl(p, head, u, el=el, z_end=z_end, puff=puff,
                          lift=lift, jag=1.0, phase=i, depth=0.06 * row,
                          sink=0.32)
        strand(mb, "hair_back", ctrl, hw * w, hw * w * 0.05, n=9, flat=0.88,
               power=2.0, seg=14, taper=2.1, root=0.28)


def _spikes(mb, p, head, *, count: int, span: float, el: float,
            length: float, part: str = "hair_front", lift: float = 0.03,
            forward: float = 0.25, updown: float = 1.0, base: float = 0.60,
            taper: float = 1.6, phase: float = 0.0, flat: float = 0.85):
    """生え際から上へ跳ねるトゲ房（坊っちゃんの逆立てた髪）。

    updown < 0 なら下向き（生え際のギザギザ）。base は根元の太さ。
    """
    hw, hh = p["head_w"], p["head_h"]
    up = np.array([0.0, 0.0, updown])
    r0 = hw * (span / count) * base
    for i in range(count):
        u = (i + 0.5 + phase) / count * 2.0 - 1.0
        az = FRONT + u * span * 0.5
        s = scalp_pt(head, az, el, hw * lift)[0]
        d = M.normalize(s - head.center)
        wob = 0.85 + 0.15 * math.sin(i * 2.399)
        length_i = hh * length * wob * (1.0 - 0.22 * abs(u))
        direction = M.normalize(up * 1.0 + d * 0.55
                                + np.array([0.0, -forward, 0.0]))
        e = s + direction * length_i
        c1 = s + d * length_i * 0.30 + up * length_i * 0.12
        c2 = s + direction * length_i * 0.70
        strand(mb, part, (s, c1, c2, e), r0, r0 * 0.04, n=8, flat=flat,
               power=2.0, seg=9, taper=taper)


def _ridge_spikes(mb, p, head, *, n: int, th_lo: float, th_hi: float,
                  length: float, lean: float = 0.35, base: float = 0.60,
                  flat: float = 0.55, zig: float = 0.0, part="hair_front",
                  phase: float = 0.0, root_off: float = 0.10):
    """頭頂の稜線（正面→後頭部）に沿って並ぶトゲ房。横から見た輪郭が
    ギザギザになる。zig で左右に振ると正面から見ても山が並ぶ。"""
    hw, hh = p["head_w"], p["head_h"]
    up = np.array([0.0, 0.0, 1.0])
    r0 = hw * base * (th_hi - th_lo) / n
    for i in range(n):
        t = (i + 0.5 + phase) / n
        # th < 0 は正面側、th > 0 は後頭部側の頭頂からの角度
        th = th_lo + (th_hi - th_lo) * t
        az = (FRONT if th < 0.0 else FRONT + math.pi)             + zig * (1.0 if i % 2 == 0 else -1.0)
        el = abs(th)
        # 根元は地髪の殻の表面。頭皮に置くと厚い殻に埋もれて先しか出ない。
        s = scalp_pt(head, az, el, hw * root_off)[0]
        d = M.normalize(s - head.center)
        wob = 0.86 + 0.14 * math.sin(i * 2.399 + 0.4)
        # 前ほど長く、後ろへ行くほど短い
        length_i = hh * length * wob * (1.0 - 0.35 * t)
        direction = M.normalize(up * 1.0 + d * 0.45
                                + np.array([0.0, lean, 0.0]))
        e = s + direction * length_i
        c1 = s + d * length_i * 0.32 + up * length_i * 0.10
        c2 = s + direction * length_i * 0.68
        strand(mb, part, (s, c1, c2, e), r0, r0 * 0.04, n=8, flat=flat,
               power=2.0, seg=9, taper=1.2)


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
        build_helmet(mb, p, head, front_el=0.63, back_el=1.72, thickness=0.165,
                     z_end_side=chin - hh * 0.30, z_end_back=chin - hh * 0.22,
                     puff=1.10, ridges=14, ridge_amp=0.30, jag=0.030,
                     inward=0.76)
        _bangs(mb, p, head, span=math.radians(82.0), count=17, el=0.58,
               end_v=0.614, width=0.152)

    elif style == "long_blunt":
        build_helmet(mb, p, head, front_el=0.70, back_el=1.70, thickness=0.104,
                     z_end_side=z["bust"], z_end_back=z["waist"] - 0.02,
                     puff=1.25, ridges=16, ridge_amp=0.34, jag=0.028,
                     inward=0.74, depth=0.12)
        _bangs(mb, p, head, span=math.radians(80.0), count=15, el=0.62,
               end_v=0.655, width=0.116)

    elif style == "twintail":
        build_helmet(mb, p, head, front_el=0.64, back_el=1.62, thickness=0.082,
                     z_end_side=chin + hh * 0.10, z_end_back=chin + hh * 0.22,
                     puff=0.9, ridges=14, ridge_amp=0.34, jag=0.026,
                     hang_lo=0.30, inward=0.80)
        _bangs(mb, p, head, span=math.radians(76.0), count=11, el=0.58,
               end_v=0.650, width=0.110)
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
        build_helmet(mb, p, head, front_el=0.70, back_el=1.60, thickness=0.080,
                     z_end_side=chin + hh * 0.16, z_end_back=chin + hh * 0.30,
                     puff=0.8, ridges=14, ridge_amp=0.32, jag=0.024,
                     hang_lo=0.32, inward=0.82)
        _bangs(mb, p, head, span=math.radians(72.0), count=10, el=0.62,
               end_v=0.668, width=0.105, sweep=0.20)
        base = scalp_pt(head, FRONT + math.pi, 0.52, hw * 0.10)[0]
        with mb.part("hair_acc"):
            mb.add_sphere(tuple(base), (hw * 0.070, hw * 0.070, hh * 0.058),
                          "cloth_ribbon_green", nu=12, nv=8)
        # 細い房を離して並べると箒になる。太めに重ねて 1 本の尾に見せる。
        for spread in (-0.24, -0.08, 0.08, 0.24):
            _tail(mb, p, head, base, np.array([spread, 1.0, -0.08]), hh * 1.95,
                  width=0.172, seg_part="hair_tail", droop=1.15)

    elif style == "short":
        build_helmet(mb, p, head, front_el=0.68, back_el=1.62, thickness=0.086,
                     z_end_side=chin + hh * 0.06, z_end_back=chin + hh * 0.20,
                     puff=0.9, ridges=14, ridge_amp=0.34, jag=0.028,
                     hang_lo=0.28, inward=0.80)
        _bangs(mb, p, head, span=math.radians(78.0), count=11, el=0.62,
               end_v=0.662, width=0.112)

    elif style == "crew":
        # 坊っちゃん: 逆立てた短い黒髪。地髪は厚めに、生え際から上へ跳ねる
        # トゲ房を前に並べ、その後ろにもう一列。横髪は作らず耳を出す。
        build_scalp(mb, p, head, front_el=1.02, back_el=1.92, thickness=0.140)
        # 頭頂の輪郭に沿って大きく尖った房を 3 列。数を絞って 1 本ずつ
        # はっきり尖らせる（多いと松かさになる）。
        # 頭頂の稜線に沿って前から後ろへトゲを並べる。横から見て輪郭が
        # ギザギザになり、正面からは左右に振った山が並ぶ。
        _ridge_spikes(mb, p, head, n=6, th_lo=-0.72, th_hi=0.62, length=0.30,
                      lean=0.30, base=1.10, flat=0.55, zig=math.radians(14.0),
                      root_off=0.11)
        _ridge_spikes(mb, p, head, n=5, th_lo=-0.64, th_hi=0.54, length=0.24,
                      lean=0.40, base=0.95, flat=0.55,
                      zig=math.radians(46.0), phase=0.5, root_off=0.11)
        # 正面から見ても頭頂の幅いっぱいに山が並ぶよう、左右にも一列
        _ridge_spikes(mb, p, head, n=4, th_lo=-0.60, th_hi=0.48, length=0.20,
                      lean=0.35, base=1.00, flat=0.55,
                      zig=math.radians(78.0), phase=0.25, root_off=0.11)
        hd = p["head_d"]
        for sgn in (-1, 1):
            az = FRONT + sgn * math.radians(98.0)
            s = scalp_pt(head, az, 1.20, hw * 0.02)[0]
            e = np.array([s[0] * 1.02, s[1] - hd * 0.02, chin + hh * 0.52])
            c1 = s + np.array([0.0, 0.0, -hh * 0.06])
            c2 = e + np.array([0.0, 0.0, hh * 0.05])
            strand(mb, "hair_side", (s, c1, c2, e), hw * 0.055, hw * 0.012,
                   n=8, flat=0.70, power=2.0, seg=8, taper=1.4)

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

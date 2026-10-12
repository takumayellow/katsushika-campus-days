"""背広（教授 #172）。

上着は胴の断面を肩でなで肩の線へつなぎ、首の付け根まで 1 枚の面で包む。前は V に開け、
開きの縁に沿ってラペル（下襟）と上襟の板を載せる。ラペルと上襟の境には刻み（ノッチ）を入れる。
V の中には白いシャツの襟とネクタイ、袖口からはシャツのカフスを見せる。

断面は胴のプロファイル（body.torso_profile）を膨らませた超楕円で、
肩先より上は肩幅から首の付け根の楕円へ寄せる。上着・ラペル・ポケットは
すべてこの断面の上の点として置くので、面どうしが食い込まない。
"""

from __future__ import annotations

import math

import numpy as np

from . import body as B
from . import cloth as C
from . import mesh as M

_POWER = 2.3
# シャツの上端（肩の高さからの身長比）。上着の肩先の高さに揃えて、肩の上は上着だけにする
_SHIRT_TOP = 0.004


def _smin(a, b, k):
    """なめらかな min。k の幅で角を丸める。"""
    return 0.5 * (a + b - np.sqrt((a - b) ** 2 + k * k))


class _Section:
    """上着の断面。高さ z ごとに横幅 rx・奥行き ry・前後の中心 cy・超楕円の次数を持つ。"""

    def __init__(self, p: dict, a: B.Anatomy):
        h = p["height"]
        z = p["z"]
        self.h = h
        self.zz, self.brx, self.bry = B.torso_profile(p, a)
        self.inflate = h * 0.015
        # 腰より下は尻の一番太い所の幅のまま下ろし、裾へ向けて少し広げてズボンの腿を覆う
        low = self.zz < z["waist"]
        k = int(np.argmax(np.where(low, self.brx, -1.0)))
        self.hip_z = float(self.zz[k])
        self.hip_rx = float(self.brx[k])
        self.hip_ry = float(self.bry[k])
        self.crotch = z["crotch"]
        self.flare = h * 0.010
        # 肩先。肩パッドの分だけ素体の肩より外へ出し、角を丸める
        self.tip_z = z["shoulder"] + h * 0.004
        self.tip_x = a.shoulder_half + h * 0.012
        self.round = h * 0.010
        # 首の付け根（上襟が立ち上がる所）
        self.np_z = z["shoulder"] + h * 0.026
        self.np_x = a.neck_r * 1.46
        self.np_y = a.neck_r * 1.40
        self.np_cy = -h * 0.003
        # 素体の首より外に保つ隙間
        self.clear = h * 0.008

    def at(self, zs):
        zs = np.atleast_1d(np.asarray(zs, dtype=float))
        h = self.h
        bx = np.interp(zs, self.zz, self.brx)
        by = np.interp(zs, self.zz, self.bry)
        below = zs < self.hip_z
        t = np.clip((self.hip_z - zs) / (self.hip_z - self.crotch), 0.0, 1.0)
        bx = np.where(below, np.maximum(bx, self.hip_rx) + self.flare * t ** 1.3, bx)
        by = np.where(below, np.maximum(by, self.hip_ry) + self.flare * 0.4 * t ** 1.3, by)
        rx_b = bx + self.inflate
        ry_b = by + self.inflate
        # 肩の線: 肩先から首の付け根へなだらかに下る直線と、肩先の高さの水平線の
        # なめらかな min。肩先の角は丸めて、肩パッドの入った丸い肩にする
        slope = (self.tip_x - self.np_x) / (self.np_z - self.tip_z)
        line = self.tip_x - (zs - self.tip_z) * slope
        top = _smin(np.full_like(zs, self.tip_x), line, self.round)
        # 肩先より下は胸の幅から肩先の幅へ寄せる
        w = M.smoothstep(self.tip_z - h * 0.075, self.tip_z - h * 0.010, zs)
        rx = np.maximum(rx_b * (1.0 - w) + top * w, rx_b)
        above = zs > self.tip_z - h * 0.010
        rx = np.where(above, np.maximum(top, bx + self.clear), rx)
        u = np.clip((self.tip_x - rx) / (self.tip_x - self.np_x), 0.0, 1.0)
        u = np.where(above, u, 0.0)
        ry = ry_b + (self.np_y - ry_b) * u ** 1.5
        ry = np.maximum(ry, by + self.clear)
        cy = self.np_cy * u * u
        pw = _POWER - 0.3 * u
        return rx, ry, cy, pw

    def point(self, theta, z):
        """角度 theta（+X から、前の中央が -90 度。ラジアン）と高さ z の表面の点。"""
        theta = np.asarray(theta, dtype=float)
        rx, ry, cy, pw = (float(v[0]) for v in self.at([z]))
        return self._pt(theta, z, rx, ry, cy, pw)

    @staticmethod
    def _pt(theta, z, rx, ry, cy, pw):
        ca, sa = np.cos(theta), np.sin(theta)
        e = 2.0 / pw
        x = np.sign(ca) * np.abs(ca) ** e * rx
        y = cy + np.sign(sa) * np.abs(sa) ** e * ry
        return np.stack([x, y, np.full_like(x, z)], axis=-1)

    def normal(self, theta, z):
        """表面の外向きの法線。"""
        theta = np.asarray(theta, dtype=float)
        d = 1e-3
        dz = self.h * 1e-3
        pt = self.point(theta, z)
        tu = self.point(theta + d, z) - self.point(theta - d, z)
        tv = self.point(theta, z + dz) - self.point(theta, z - dz)
        n = np.cross(tu, tv)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-12)
        rx, ry, cy, pw = (float(v[0]) for v in self.at([z]))
        out = np.stack([pt[..., 0], pt[..., 1] - cy, np.zeros_like(pt[..., 0])], axis=-1)
        sgn = np.sign(np.sum(n * out, axis=-1, keepdims=True))
        return n * np.where(sgn == 0, 1.0, sgn)

    def opening(self, z, x):
        """前の開きの縁が x（中心からの横の距離）にあるときの、前の中央から測った角度。"""
        rx, ry, cy, pw = (float(v[0]) for v in self.at([z]))
        r = float(np.clip(x / rx, 0.0, 0.999))
        return math.asin(r ** (pw / 2.0))

    def theta_at_arc(self, z, theta0, widths, sgn):
        """theta0 から sgn の向きへ、表面に沿って widths だけ進んだ角度。"""
        ts = theta0 + sgn * np.linspace(0.0, 1.6, 400)
        pts = self.point(ts, z)
        arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
        return np.interp(widths, arc, ts)


class _Lines:
    """上着の線（裾・ボタン・前の開き）。"""

    def __init__(self, p: dict, a: B.Anatomy, sec: _Section):
        h = p["height"]
        z = p["z"]
        self.hem = z["crotch"] - h * 0.030
        self.b1 = z["waist"] + h * 0.008
        self.b2 = self.b1 - h * 0.058
        # 裾の側（腰と脚について動く jacket）と胸の側（胸と肩について動く jacket_yoke）の
        # 継ぎ目。腰の高さは背骨を曲げても、そこより上ほど動かない
        self.seam = z["waist"]
        # 裾は前の中央から丸く切り落とす
        self.cutaway = h * 0.040
        # V の上端の幅（首の付け根の幅に対する比）
        self.v_top = sec.np_x * 0.80

    def v_edge(self, sec: _Section, z):
        """前の開きの縁の、中心からの横の距離。"""
        if z <= self.b2:
            t = (self.b2 - z) / (self.b2 - self.hem)
            return self.cutaway * t ** 1.4
        if z <= self.b1:
            return 0.0
        t = min(1.0, (z - self.b1) / (sec.np_z - self.b1))
        return self.v_top * t


def _jacket_body(mb, sec: _Section, ln: _Lines, mat="cloth_jacket", seg=34):
    """上着の身頃。裾から首の付け根まで張り、その上に背中側だけ上襟の立ち上がりを足す。"""
    h = sec.h
    zb = sec.zz
    rows = np.unique(np.round(np.concatenate([
        np.linspace(ln.hem, ln.b2, 4), np.linspace(ln.b2, ln.b1, 3),
        np.linspace(ln.b1, sec.tip_z - h * 0.075, 5),
        zb[(zb > sec.tip_z - h * 0.12) & (zb < sec.tip_z)],
        sec.tip_z + h * np.array([-0.050, -0.030, -0.018, -0.010, -0.004, 0.0, 0.004]),
        np.linspace(sec.tip_z + h * 0.008, sec.np_z, 6), [ln.seam],
    ]), 6))
    rows = rows[(rows >= ln.hem) & (rows <= sec.np_z)]
    rings = []
    opens = []
    for z in rows:
        o = sec.opening(z, ln.v_edge(sec, z))
        opens.append(o)
        th = np.linspace(-math.pi / 2 + o, 1.5 * math.pi - o, seg)
        rings.append(sec.point(th, z))
    k = int(np.searchsorted(rows, round(ln.seam, 6)))
    with mb.part("jacket"):
        mb.add_grid(rings[:k + 1], mat, smooth=True, close_u=False)
    stand = _stand_rings(sec, rings[-1], opens[-1], seg)
    with mb.part("jacket_yoke"):
        mb.add_grid(rings[k:], mat, smooth=True, close_u=False)
        mb.add_grid(stand, mat, smooth=True, close_u=False, min_area=(h * 0.002) ** 2)
    return rows, opens


def _stand_height(sec: _Section, theta, o_top):
    """上襟の立ち上がりの高さ。前の開きの縁で 0 から、首を回って背中で最大になる。"""
    psi = np.abs(np.mod(theta + math.pi / 2 + math.pi, 2 * math.pi) - math.pi)
    f = M.smoothstep(o_top, math.pi, psi)
    return sec.h * 0.018 * np.sqrt(f)


def _stand_rings(sec: _Section, base, o_top, seg):
    """首の付け根から上へ、背中側だけ立ち上がる上襟。首へ少し寄せる。"""
    th = np.linspace(-math.pi / 2 + o_top, 1.5 * math.pi - o_top, seg)
    hs = _stand_height(sec, th, o_top)
    rx, ry, cy, pw = (float(v[0]) for v in sec.at([sec.np_z]))
    rings = [base]
    for v, inset in ((0.55, 0.0005), (1.0, 0.0010)):
        pt = _Section._pt(th, sec.np_z, rx - sec.h * inset, ry - sec.h * inset, cy, pw)
        pt[:, 2] = sec.np_z + hs * v
        rings.append(pt)
    return rings


def _slab(mb, sec: _Section, rows, mat, *, mirror=False):
    """表面に載せた厚みのある板。rows は (z, 角度の列, 浮かせる量の列) の並び。"""
    rings = []
    for z, th, lift in rows:
        th = np.asarray(th, dtype=float)
        pts = np.array([sec.point(t, z) for t in th]).reshape(-1, 3)
        nrm = np.array([sec.normal(t, z) for t in th]).reshape(-1, 3)
        rings.append(pts + nrm * np.asarray(lift, dtype=float)[:, None])
    if mirror:
        rings = [r * np.array([-1.0, 1.0, 1.0]) for r in rings]
    mb.add_grid(rings, mat, smooth=True, close_u=False, flip=mirror,
                min_area=(sec.h * 0.0005) ** 2)


def _lapel_width(sec: _Section, ln: _Lines, z):
    """ラペルと上襟の、開きの縁から外への幅。ノッチの所で細る。"""
    h = sec.h
    z_n = sec.np_z - h * 0.050
    w_lap = h * 0.038
    lo = z_n - h * 0.006
    if z <= lo:
        t = max(0.0, (z - ln.b1) / (lo - ln.b1))
        return max(h * 0.004, w_lap * t ** 0.6)
    pts = [(lo, w_lap), (z_n, w_lap * 0.42), (z_n + h * 0.010, h * 0.030),
           (sec.np_z - h * 0.008, h * 0.024), (sec.np_z, h * 0.020)]
    zs, ws = zip(*pts)
    return float(np.interp(z, zs, ws))


def _lapels(mb, sec: _Section, ln: _Lines, mat="cloth_jacket", cols=6):
    """左右のラペルと上襟。開きの縁を内側の縁とし、縁は厚く丸めて外へ薄く寝かせる。"""
    h = sec.h
    z_n = sec.np_z - h * 0.050
    zs = np.unique(np.round(np.concatenate([
        np.linspace(ln.b1, z_n - h * 0.006, 9),
        [z_n - h * 0.003, z_n, z_n + h * 0.004, z_n + h * 0.010],
        np.linspace(z_n + h * 0.016, sec.np_z, 4)]), 6))
    roll, flat, tuck, foot = h * 0.0060, h * 0.0034, -h * 0.0030, h * 0.0006
    rows = []
    for i, z in enumerate(zs):
        o = sec.opening(z, ln.v_edge(sec, z))
        t0 = -math.pi / 2 + o
        w = _lapel_width(sec, ln, z)
        tw = sec.theta_at_arc(z, t0, np.linspace(0.0, w, cols), +1.0)
        th = np.concatenate([[t0, t0], tw, [tw[-1]]])
        lift = np.concatenate([[tuck, roll], np.linspace(roll, flat, cols), [foot]])
        if i in (0, len(zs) - 1):
            # 上下の端は厚みを畳んで、板の口をふさぐ
            lift = np.minimum(lift, foot)
        rows.append((z, th, lift))
    # 下のボタンより上にあるので、すべて胸の側
    with mb.part("jacket_yoke"):
        for mirror in (False, True):
            _slab(mb, sec, rows, mat, mirror=mirror)


def _pockets(mb, sec: _Section, ln: _Lines, mat="cloth_jacket"):
    """腰の両脇のフラップポケットと、左胸の箱ポケット。"""
    h = sec.h
    lift, foot = h * 0.0034, h * 0.0006

    def patch(z0, z1, center, width, mirror):
        rows = []
        for z, top in ((z0, False), (z0, True), (z1, True), (z1, False)):
            th = sec.theta_at_arc(z, -math.pi / 2, np.array([center - width / 2, center + width / 2]),
                                  +1.0)
            ths = np.array([th[0], th[0], th[1], th[1]])
            lf = np.array([foot, lift, lift, foot]) if top else np.full(4, foot)
            rows.append((z, ths, lf))
        _slab(mb, sec, rows, mat, mirror=mirror)

    z_p = ln.b2 - h * 0.012
    with mb.part("jacket"):
        for mirror in (False, True):
            patch(z_p - h * 0.018, z_p, h * 0.055, h * 0.058, mirror)
    z_c = sec.tip_z - h * 0.100
    with mb.part("jacket_yoke"):
        patch(z_c - h * 0.011, z_c, h * 0.058, h * 0.040, False)


def _buttons(mb, sec: _Section, ln: _Lines, mat="shoes_loafer"):
    h = sec.h
    for z in (ln.b1, ln.b2):
        pt = sec.point(-math.pi / 2, z)
        with mb.part("jacket_yoke" if z > ln.seam else "jacket"):
            mb.add_sphere((0.0, float(pt[1]) - h * 0.0010, z),
                          (h * 0.0055, h * 0.0024, h * 0.0055), mat, nu=10, nv=6)


def _neck_r(p: dict, a: B.Anatomy, z):
    """素体の首の、高さ z での (前後の中心, 半径)。body.build_neck の輪から補間する。"""
    h = p["height"]
    zs = [p["z"]["shoulder"] - h * 0.009, p["z"]["shoulder"] + h * 0.030,
          p["z"]["chin"] + p["head_h"] * B.NECK_TOP]
    cys = [0.0, -h * 0.003, -h * 0.007]
    rs = [a.neck_r * 1.62, a.neck_r * 1.06, a.neck_r * 0.90]
    return float(np.interp(z, zs, cys)), float(np.interp(z, zs, rs))


def _shirt_point(p, a, phi, z, inflate):
    rings, _ = C.garment_rings(p, a, [z], inflate, seg=2, span=(phi, phi),
                               bust=0.0, shoulder=True)
    return rings[0][0]


def _shirt_collar(p, a, mb, sec: _Section, mat="cloth_blouse", seg=28):
    """シャツの台襟と、前で開いて胸へ下りる襟。台襟は首を一周して V の奥をふさぐ。"""
    h = p["height"]
    z_sh_top = p["z"]["shoulder"] + h * _SHIRT_TOP
    z_top_front = sec.np_z + h * 0.004
    z_top_back = sec.np_z + h * 0.022 + h * 0.007

    def ring_at(z, ths, out):
        cy, r = _neck_r(p, a, z)
        r += out
        ca, sa = np.cos(ths), np.sin(ths)
        return np.stack([ca * r, cy + sa * r, np.full_like(ca, z)], axis=1)

    def front_w(ths):
        psi = np.abs(np.mod(ths + math.pi / 2 + math.pi, 2 * math.pi) - math.pi)
        return M.smoothstep(math.radians(80.0), 0.0, psi)

    with mb.part("collar"):
        # 台襟: シャツの上端の高さで首に沿う所から首の付け根の上まで、首を一周する
        ths = np.linspace(0.0, 2 * math.pi, seg, endpoint=False)
        bottom = ring_at(z_sh_top, ths, h * 0.002)
        mid = ring_at(sec.np_z - h * 0.010, ths, h * 0.007)
        top = ring_at(sec.np_z, ths, h * 0.0030)
        mb.add_grid([bottom, mid, top], mat, smooth=True, close_u=True)
        # 襟: 前の中央を開け、前は低く胸へ垂らし、後ろは上襟より上へ出す
        gap_t, gap_b = math.radians(9.0), math.radians(34.0)
        u = np.linspace(0.0, 1.0, seg)
        rings = []
        for v, gap in ((0.0, gap_b), (0.5, (gap_b + gap_t) / 2), (1.0, gap_t)):
            th = -math.pi / 2 + gap + u * (2 * math.pi - 2 * gap)
            q = front_w(th)
            z_lo = sec.np_z - h * 0.004 - q * h * 0.032
            z_hi = z_top_back + (z_top_front - z_top_back) * q
            zz = z_lo + (z_hi - z_lo) * v
            pts = []
            for t, zi, qi in zip(th, zz, q):
                cy, r = _neck_r(p, a, zi)
                ring = np.array([math.cos(t) * (r + h * 0.0062),
                                 cy + math.sin(t) * (r + h * 0.0062), zi])
                if v < 1.0 and qi > 0.0:
                    # 前は下の縁をシャツの胸に寝かせる
                    on = _shirt_point(p, a, t, zi, h * 0.011)
                    s = qi * (1.0 - v)
                    ring = ring * (1.0 - s) + on * s
                pts.append(ring)
            rings.append(np.array(pts))
        mb.add_grid(rings, mat, smooth=True, close_u=False)


def _tie(p, a, mb, sec: _Section, ln: _Lines, mat="necktie"):
    h = p["height"]
    zz, rx, ry = B.torso_profile(p, a)
    z_k = sec.np_z - h * 0.008

    def front(z, out):
        return -float(np.interp(z, zz, ry)) - h * (0.008 + out)

    with mb.part("necktie"):
        cy, r = _neck_r(p, a, z_k)
        yk = min(cy - r - h * 0.010, front(z_k, 0.006))
        mb.add_rounded_box((0.0, yk, z_k), (h * 0.020, h * 0.012, h * 0.022),
                           mat, seg=3, smooth=True)
        zs = np.linspace(z_k - h * 0.010, ln.b1 + h * 0.010, 6)
        pts = np.array([[0.0, front(z, 0.0030), z] for z in zs])
        pts[0, 1] = min(pts[0, 1], yk + h * 0.002)
        widths = np.linspace(h * 0.008, h * 0.013, len(zs))
        mb.add_tube(pts, [(h * 0.0012, w) for w in widths], mat, n=8,
                    power=2.6, cap_start=True, cap_end=True)


def _sleeves_and_cuffs(mb, p, a: B.Anatomy, *, t_end=0.90):
    """上着の袖と、袖口から少し出すシャツのカフス。"""
    C._arm_sleeve(mb, p, a, "cloth_jacket", "sleeve", t_end=t_end, r_scale=1.30,
                  puff=0.0)
    r0, r1, r2 = a.arm_r
    for sgn in (-1, 1):
        sh = a.shoulder * np.array([sgn, 1, 1])
        el = a.elbow * np.array([sgn, 1, 1])
        wr = a.wrist * np.array([sgn, 1, 1])
        full = np.array([sh, sh + (el - sh) * 0.5, el, el + (wr - el) * 0.55, wr])
        tt = np.array([0.0, 0.25, 0.5, 0.78, 1.0])
        ts = np.array([t_end - 0.05, t_end, t_end + 0.03, t_end + 0.055])
        path = np.array([C._interp_path(full, tt, t) for t in ts])
        radii = [float(np.interp(t, [0.0, 0.5, 1.0], [r0, r1, r2]) * 1.16) for t in ts]
        with mb.part(f"cuff_{'l' if sgn > 0 else 'r'}"):
            mb.add_tube(path, radii, "cloth_blouse", n=12, cap_start=True, cap_end=True)


def build_suit(mb: M.MeshBuilder, p: dict, a: B.Anatomy) -> None:
    """2 つボタンの背広と、白いシャツとネクタイと、共布のズボンと革靴。"""
    sec = _Section(p, a)
    ln = _Lines(p, a, sec)
    C._shirt_and_slacks(mb, p, a, collar=False, yoke_rise=0.0, top=_SHIRT_TOP)
    C.shoe(mb, p, a, "shoes_loafer", heel=True, scale=1.12)
    _shirt_collar(p, a, mb, sec)
    if "necktie" in p.get("accessories", ()):
        _tie(p, a, mb, sec, ln)
    _jacket_body(mb, sec, ln)
    _lapels(mb, sec, ln)
    _pockets(mb, sec, ln)
    _buttons(mb, sec, ln)
    _sleeves_and_cuffs(mb, p, a)

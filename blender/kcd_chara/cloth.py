"""衣装の生成。

素体の上に載せるシェル（ブラウス・着物）と、独立形状（プリーツスカート・袴・
靴・鞄）で構成する。プリーツは UV やノーマルではなく実ジオメトリで折る。
"""

from __future__ import annotations

import math

import numpy as np

from . import body as B
from . import mesh as M


# --------------------------------------------------------------------------
# 共通ヘルパ
# --------------------------------------------------------------------------


def _profile(p: dict, a: B.Anatomy):
    return B.torso_profile(p, a)


def garment_rings(p: dict, a: B.Anatomy, z_list, inflate, *, seg: int = 28,
                  power: float = 2.3, span=None, bust: float = 1.0,
                  shoulder: bool = False):
    """胴プロファイルを膨らませた服のリング列。span=(a0,a1) で開襟にできる。"""
    zz, rx, ry = _profile(p, a)
    z_list = np.asarray(z_list, dtype=float)
    infl = np.full(len(z_list), float(inflate)) if np.isscalar(inflate) \
        else np.asarray(inflate, dtype=float)
    rxi = np.interp(z_list, zz, rx) + infl
    ryi = np.interp(z_list, zz, ry) + infl
    if shoulder:
        # 素肌のプロファイルは肩から上で首へ向かって急に細る。服がそれを
        # なぞると肩の面に布が載らず、上端の切り口から素肌の帯が出て
        # オフショルダーに見える。肩の高さから上は肩幅を保たせる。
        # ただし肩の高さより上まで同じ幅を保つと、上端が水平な棚になって
        # 肩が箱に見える。肩の頂点から上は円弧で内へ落とす。
        hh = p["height"]
        z_sh = a.shoulder[2]
        t = np.clip((z_list - (z_sh - hh * 0.045)) / (hh * 0.045), 0.0, 1.0)
        w = t * t * (3.0 - 2.0 * t)
        up = np.clip((z_list - z_sh) / (hh * 0.034), 0.0, 1.0)
        round_ = np.sqrt(np.clip(1.0 - up * up, 0.0, 1.0))
        tgt = rxi * (1.0 - w) + a.shoulder_half * round_ * w
        rxi = np.maximum(rxi, tgt)
    if span is None:
        ang = np.linspace(0.0, 2 * math.pi, seg, endpoint=False)
        closed = True
    else:
        ang = np.linspace(span[0], span[1], seg)
        closed = False
    ca, sa = np.cos(ang), np.sin(ang)
    e = 2.0 / power
    bx = np.sign(ca) * np.abs(ca) ** e
    by = np.sign(sa) * np.abs(sa) ** e
    h = p["height"]
    bulge = p["build_params"]["bust_bulge"] * h * bust
    rings = []
    for zi, rxx, ryy in zip(z_list, rxi, ryi):
        X = bx * rxx
        Y = by * ryy
        if bulge > 0.0:
            t = math.exp(-((zi - p["z"]["bust"]) / (0.048 * h)) ** 2)
            f = np.clip(-sa, 0.0, 1.0) ** 1.6
            lobe = 0.62 + 0.38 * np.cos(2.0 * ang)
            Y -= bulge * t * f * np.clip(lobe, 0.0, 1.5)
        rings.append(np.stack([X, Y, np.full(len(ang), zi)], axis=1))
    return rings, closed


def shell(mb: M.MeshBuilder, p, a, mat, part, z0, z1, inflate, *, levels=8,
          seg=28, span=None, bust=1.0, smooth=True, cap_end=False,
          shoulder=False):
    zs = np.linspace(z0, z1, levels)
    infl = inflate if np.isscalar(inflate) else np.interp(
        zs, np.linspace(z0, z1, len(inflate)), inflate)
    rings, closed = garment_rings(p, a, zs, infl, seg=seg, span=span,
                                  bust=bust, shoulder=shoulder)
    with mb.part(part):
        mb.add_grid(rings, mat, smooth=smooth, close_u=closed, cap_end=cap_end)
    return rings


def yoke(mb: M.MeshBuilder, p, a, mat, part, base, z_top, inflate, *,
         rise, seg=28, bust=1.0, closed=True, levels=3, floor=0.14):
    """肩ヨーク。胴シェルの水平な上端から、肩の上だけを持ち上げる。

    水平に切った上端で服を終わらせると、首から肩へ向かう斜面の素肌が
    その上に残って、制服でも着物でもボートネックのように見える。上端の
    リングを角度ごとに持ち上げ（真横で最大、前後で最小）、肩を包んでから
    首もとで開かせる。
    """
    top = garment_rings(p, a, [z_top + rise], inflate, seg=seg,
                        bust=bust)[0][0]
    n = len(base)
    ang = np.linspace(0.0, 2 * math.pi, n, endpoint=False)
    w = floor + (1.0 - floor) * np.abs(np.cos(ang)) ** 1.6
    rings = [np.asarray(base, dtype=float)]
    for i in range(1, levels + 1):
        ww = (w * (i / levels))[:, None]
        rings.append(rings[0] * (1.0 - ww) + top * ww)
    with mb.part(part):
        mb.add_grid(rings, mat, smooth=True, close_u=closed)
    return rings[-1]


def band(mb: M.MeshBuilder, p, a, mat, part, z0, z1, inflate, *, seg=28):
    return shell(mb, p, a, mat, part, z0, z1, inflate, levels=3, seg=seg,
                 smooth=True)


def pleated_skirt(mb: M.MeshBuilder, p, a, mat, part, *, z_top, z_bot,
                  r_top, r_bot, pleats=16, amp=0.16, flare=1.25,
                  ry_ratio=0.72, levels=9, smooth=False, power=2.25,
                  clear=0.0060):
    """実ジオメトリのプリーツを持つスカート/袴。r_* は腰幅に対する比率。

    断面は素体の胴と同じスーパー楕円（power=2.25）にする。真円/楕円で作ると
    斜め 45 度方向だけ服が細くなり、腰がヒダの谷を突き抜けて肌が三角形に
    覗く。さらに谷の最小半径が素体 + clear を下回らないよう半径を押し上げる。
    """
    h = p["height"]
    n = pleats * 4
    ang = np.linspace(0.0, 2 * math.pi, n, endpoint=False)
    phase = (np.arange(n) % 4) / 4.0
    saw = phase - 0.375
    e = 2.0 / power
    ca, sa = np.cos(ang), np.sin(ang)
    bx = np.sign(ca) * np.abs(ca) ** e
    by = np.sign(sa) * np.abs(sa) ** e
    base_x = a.hip_rx
    base_y = a.hip_ry * (ry_ratio / 0.72)
    gap = h * clear
    zl = np.array([z_top + (z_bot - z_top) * (j / (levels - 1))
                   for j in range(levels)])
    env_x, env_y = inner_envelope(p, a, zl)
    rings = []
    for j in range(levels):
        t = j / (levels - 1)
        z = float(zl[j])
        k = r_top + (r_bot - r_top) * t**flare
        aa = amp * M.smoothstep(0.0, 0.30, t)
        valley = 1.0 - aa * 0.375
        rx = max(base_x * k, (float(env_x[j]) + gap) / valley)
        ry = max(base_y * k, (float(env_y[j]) + gap) / valley)
        rr = 1.0 + aa * saw
        rings.append(np.stack([bx * rx * rr, by * ry * rr,
                               np.full(n, z)], axis=1))
    # 裾の折り返し（厚みが見えるように内側へ）
    last = rings[-1].copy()
    last[:, 0] *= 0.94
    last[:, 1] *= 0.94
    last[:, 2] += (z_top - z_bot) * 0.020
    rings.append(last)
    with mb.part(part):
        mb.add_grid(rings, mat, smooth=smooth)
    return rings


def leg_profile(a: B.Anatomy):
    """素体の脚（body.build_legs）と同じ z→半径プロファイル。

    足首と膝を直線で結ぶとふくらはぎの膨らみ（r1*1.12）を取りこぼし、靴下や
    ブーツが脚を突き抜けて肌が縞状に露出する。実キーポイントをそのまま使う。
    """
    r0, r1, r2 = a.leg_r
    z_an, z_kn, z_hp = a.ankle[2], a.knee[2], a.hip_joint[2]
    x_an, x_kn, x_hp = a.ankle[0], a.knee[0], a.hip_joint[0]
    zs = np.array([z_an,
                   z_kn + (z_an - z_kn) * 0.36,
                   z_kn,
                   z_hp + (z_kn - z_hp) * 0.42,
                   z_hp + (z_hp - z_kn) * 0.10])
    rr = np.array([r2, r1 * 1.12, r1, r0 * 0.86, r0 * 1.02])
    xs = np.array([x_an,
                   x_kn + (x_an - x_kn) * 0.36,
                   x_kn,
                   x_hp + (x_kn - x_hp) * 0.42,
                   x_hp + (x_hp - x_kn) * 0.10])
    return zs, rr, xs


def inner_envelope(p, a: B.Anatomy, z_list):
    """スカート/袴の内側にある素体（胴＋太もも）の外接半径 (rx, ry)。

    胴だけを見ると股関節まわりで太ももが胴より外に出ており、ヒダの谷から
    肌が三角形に覗く。太ももの外側端 |x| + r も取り込む。
    """
    zz, rxp, ryp = _profile(p, a)
    zs_l, rr_l, xs_l = leg_profile(a)
    z_list = np.asarray(z_list, dtype=float)
    rx = np.interp(z_list, zz, rxp)
    ry = np.interp(z_list, zz, ryp)
    top = float(zs_l[-1])
    lr = np.interp(z_list, zs_l, rr_l) * 1.02
    lx = np.abs(np.interp(z_list, zs_l, xs_l))
    inside = z_list <= top
    rx = np.where(inside, np.maximum(rx, lx + lr), rx)
    ry = np.where(inside, np.maximum(ry, lr), ry)
    return rx, ry


def leg_sleeve(mb: M.MeshBuilder, p, a: B.Anatomy, mat, part, z_lo, z_hi,
               inflate, *, levels=9, seg=18, taper_top=1.0):
    zs, rr, xs0 = leg_profile(a)
    for sgn in (-1, 1):
        xs = xs0 * sgn
        zl = np.linspace(z_lo, z_hi, levels)
        x = np.interp(zl, zs, xs)
        r = np.interp(zl, zs, rr) + inflate
        r = r * np.linspace(1.0, taper_top, levels)
        rings = []
        for zi, xi, ri in zip(zl, x, r):
            rings.append(M.ring(seg, ri, ri * 1.02, power=2.0, cx=float(xi),
                                cy=-0.004, z=float(zi)))
        with mb.part(f"{part}_{'l' if sgn > 0 else 'r'}"):
            mb.add_grid(rings, mat, smooth=True)


def shoe(mb: M.MeshBuilder, p, a: B.Anatomy, mat, *, heel=True, scale=1.18,
         lift=1.0, part="shoes"):
    fw, fl_, fh = a.foot
    for sgn in (-1, 1):
        cx = a.ankle[0] * sgn
        with mb.part(f"{part}_{'l' if sgn > 0 else 'r'}"):
            mb.add_rounded_box((cx, -fl_ * 0.26, fh * 0.66 * lift),
                               (fw * scale, fl_ * scale, fh * 1.62), mat,
                               seg=5, smooth=True)
            if heel:
                mb.add_box((cx, fl_ * 0.26, fh * 0.30),
                           (fw * scale * 0.72, fl_ * 0.28, fh * 0.60), mat)


# --------------------------------------------------------------------------
# セーラー/ブレザー系
# --------------------------------------------------------------------------


def _ribbon(mb, p, a, mat, z, *, size=1.0):
    h = p["height"]
    zz, rx, ry = _profile(p, a)
    yy = -float(np.interp(z, zz, ry)) - h * 0.012
    s = h * 0.016 * size
    with mb.part("ribbon"):
        mb.add_sphere((0.0, yy - s * 0.4, z), (s * 0.85, s * 0.75, s * 1.05),
                      mat, nu=10, nv=7)
        for sgn in (-1, 1):
            mb.add_sphere((sgn * s * 2.3, yy - s * 0.1, z + s * 0.25),
                          (s * 2.0, s * 0.62, s * 1.25), mat, nu=12, nv=8)
            tip = np.array([sgn * s * 1.5, yy - s * 0.2, z - h * 0.055])
            top = np.array([sgn * s * 0.3, yy - s * 0.3, z - s * 0.4])
            mb.add_tube(np.array([top, (top + tip) * 0.5, tip]),
                        [(s * 0.30, s * 0.85), (s * 0.26, s * 0.95),
                         (s * 0.22, s * 1.05)], mat, n=6, power=3.0)


def _surface_pt(p, a, az: float, z: float, inflate: float) -> np.ndarray:
    """胴プロファイルを inflate だけ膨らませた面上の 1 点。"""
    return garment_rings(p, a, [z], inflate, seg=1, span=(az, az))[0][0][0]


#: セーラー襟の背面フラップの角度範囲（+Y が背中）
_SAILOR_BACK_SPAN = (math.pi * 0.24, math.pi * 0.76)


def _sailor_collar(mb, p, a, z_top, *, mat="cloth_skirt_navy",
                   stripe="collar_white", drop=0.115, lapel_w=0.034):
    """セーラー襟。背中に垂れる四角いフラップと、胸のリボンへ向かう V 字の
    ラペルで作る。首もとを帯で巻くと、正面から見たときに肩幅いっぱいの
    白いケープになってしまうので、布の形をそのまま作る。"""
    h = p["height"]
    z = p["z"]
    base_inf = h * 0.0085
    # --- 背面フラップ（肩の上端から背中へ、下へ行くほど少し浮かせる）
    zs = np.linspace(z_top, z_top - h * drop, 5)
    infl = np.linspace(base_inf + h * 0.006, base_inf + h * 0.017, len(zs))
    # 上辺は首側へ絞る。肩幅いっぱいに取ると角が肩の外へ出て、後ろから
    # 見たとき肩章のように見える。
    narrow = math.pi * 0.085
    rings = []
    for k, (z_k, infl_k) in enumerate(zip(zs, infl)):
        f = 1.0 - k / (len(zs) - 1)
        span_k = (_SAILOR_BACK_SPAN[0] + narrow * f,
                  _SAILOR_BACK_SPAN[1] - narrow * f)
        rings.append(garment_rings(p, a, [z_k], infl_k, seg=16,
                                   span=span_k)[0][0])
    az_top = (_SAILOR_BACK_SPAN[0] + narrow, _SAILOR_BACK_SPAN[1] - narrow)
    with mb.part("collar"):
        mb.add_grid(rings, mat, smooth=True, close_u=False)
        # 白いライン（フラップの外周: 左辺 -> 下辺 -> 右辺）
        edge = np.vstack([np.array([r[0] for r in rings]),
                          rings[-1][1:],
                          np.array([r[-1] for r in rings[-2::-1]])])
        mb.add_tube(edge, [h * 0.0032] * len(edge), stripe, n=6,
                    cap_start=True, cap_end=True)
    # --- 前の V ラペル: フラップの前角からリボン位置へ
    z_end = z["bust"] + h * 0.018
    n = 9
    for sgn, az_s in ((-1, az_top[1]), (1, az_top[0])):
        az_e = math.pi * 1.5 - sgn * math.radians(6.0)
        if sgn > 0:
            az_e -= 2.0 * math.pi
        ts = np.linspace(0.0, 1.0, n)
        az_c = az_s + (az_e - az_s) * ts
        z_c = z_top + (z_end - z_top) * ts ** 1.15
        # 肩を越えるところ（az が真横を通る ts≈0.38）では高い位置を通す。
        # 肩の高さのまま回すと帯が肩先の外側を巻いて、後ろから見ると
        # 肩章のように張り出す。高い位置ほど胴の輪は首側に細るので、
        # 帯は肩の付け根寄りを横切る。
        bump = np.sin(math.pi * np.clip(ts / 0.76, 0.0, 1.0))
        z_c = z_c + h * 0.024 * bump
        infl_c = base_inf + h * 0.007
        ctr = np.array([_surface_pt(p, a, float(az_i), float(z_i), infl_c)
                        for az_i, z_i in zip(az_c, z_c)])
        tang = np.gradient(ctr, axis=0)
        nrm = ctr * np.array([1.0, 1.0, 0.0])
        nrm = nrm / (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9)
        # 肩の上では面の法線が上を向く。水平法線のままだと帯が肩の上で
        # 縦に立ち、肩章のような板が肩から突き出て見える。
        up_w = (np.clip(1.0 - ts / 0.38, 0.0, 1.0) ** 1.4)[:, None]
        nrm = nrm * (1.0 - up_w) + np.array([0.0, 0.0, 1.0]) * up_w * 1.3
        nrm = nrm / (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9)
        ctr = ctr + nrm * (h * 0.0025)
        # 肩の上では帯を首側へ寄せ、幅も絞る。肩先まで広げると肩の外に
        # はみ出して見える。寄せた分だけ上げて肩の面に沿わせる。
        axis_in = -ctr * np.array([1.0, 1.0, 0.0])
        axis_in = axis_in / (np.linalg.norm(axis_in, axis=1, keepdims=True) + 1e-9)
        shift = h * lapel_w * 0.35 * up_w
        ctr = ctr + axis_in * shift + np.array([0.0, 0.0, 1.0]) * shift * 0.20
        across = np.cross(nrm, tang)
        across = across / (np.linalg.norm(across, axis=1, keepdims=True) + 1e-9)
        # 幅は胸側で広く、リボンへ向かって細る。肩の上は細く。
        w = h * lapel_w * (1.0 - 0.45 * ts)[:, None] * (1.0 - 0.45 * up_w)
        # 肩の上では帯の外縁を芯線に揃える（外側へ張り出さない）
        out_dir = across * (-sgn)
        ctr = ctr - out_dir * w * 0.5 * up_w
        left = ctr + across * w * 0.5
        right = ctr - across * w * 0.5
        with mb.part("collar"):
            mb.add_quad_strip(left, right, mat)
            mb.add_quad_strip(right, left, mat)
            outer = left if sgn < 0 else right
            mb.add_tube(outer, [h * 0.0030] * len(outer), stripe, n=6,
                        cap_start=True, cap_end=True)


def _collar(mb, p, a, mat, z, *, drop=0.055):
    """首もとに巻く帯状の襟（セーラー襟でない服向け）。"""
    h = p["height"]
    zs = np.array([z, z - h * drop])
    rings, _ = garment_rings(p, a, zs, [h * 0.006, h * 0.020], seg=26)
    with mb.part("collar"):
        mb.add_grid(rings, mat, smooth=True)


def _arm_sleeve(mb, p, a: B.Anatomy, mat, part, *, t_end=0.55, r_scale=1.45,
                puff=1.0):
    r0, r1, r2 = a.arm_r
    hh = p["height"]
    for sgn in (-1, 1):
        sh = a.shoulder * np.array([sgn, 1, 1])
        el = a.elbow * np.array([sgn, 1, 1])
        wr = a.wrist * np.array([sgn, 1, 1])
        # 袖の筒は胴シェルの内側（肩関節より胴寄り）から始める。肩関節に
        # 球を置くと肩が四角い塊に見え、腕軸に沿って肩より上へ遡らせると
        # 白い板が飛び出す。付け根を細めにして肩の丸みへ滑らかにつなぐ。
        root = sh + (el - sh) * (-0.12) + np.array([0.0, 0.0, -0.010 * hh])
        full = np.array([root, sh, sh + (el - sh) * 0.5, el,
                         el + (wr - el) * 0.55, wr])
        tt = np.array([-0.12, 0.0, 0.25, 0.5, 0.78, 1.0])
        k = int(np.searchsorted(tt, t_end))
        path = list(full[:k])
        pt = _interp_path(full, tt, t_end)
        path.append(pt)
        ts = np.append(tt[:k], t_end)
        rr = np.interp(ts, [-0.12, 0.0, 0.5, 1.0], [r0, r0, r1, r2])
        # 肩口をふくらませる（パフスリーブ）。ピークを肩関節の少し先
        # （t=0.16）に置き、付け根と袖口は絞る。
        radii = []
        for r, t in zip(rr, ts):
            bulge = 0.22 * puff * math.exp(-((t - 0.16) / 0.22) ** 2)
            neck = 0.80 + 0.20 * float(np.clip((t + 0.12) / 0.12, 0.0, 1.0))
            cuff = 1.0 - 0.10 * float(np.clip((t - t_end + 0.06) / 0.06,
                                              0.0, 1.0))
            radii.append(float(r * r_scale * (1.0 + bulge) * neck * cuff))
        with mb.part(f"{part}_{'l' if sgn > 0 else 'r'}"):
            mb.add_tube(np.array(path), radii, mat, n=12, cap_start=True,
                        cap_end=True)


def _interp_path(pts, tt, t):
    i = int(np.clip(np.searchsorted(tt, t) - 1, 0, len(tt) - 2))
    f = (t - tt[i]) / (tt[i + 1] - tt[i])
    return pts[i] + (pts[i + 1] - pts[i]) * f


def grip_point(a: B.Anatomy, side: int = -1) -> np.ndarray:
    """手のひらの中心。小物はここから吊る。

    `hand_tip` は指先を越えた点なので、そのまま握りにすると鞄が手の
    斜め下外側へ離れて宙に浮く。手首と指先の間を取って手の内に置く。
    """
    w = a.wrist * np.array([side, 1, 1])
    t = a.hand_tip * np.array([side, 1, 1])
    return w + (t - w) * 0.58


def _tote(mb, p, a: B.Anatomy):
    h = p["height"]
    grip = grip_point(a)
    cx = grip[0]
    cz = grip[2] - h * 0.168
    body_hw, body_hd, body_hh = h * 0.118, h * 0.040, h * 0.120
    with mb.part("bag"):
        mb.add_rounded_box((cx, grip[1] + h * 0.008, cz),
                           (body_hw, body_hd, body_hh), "bag_tote",
                           seg=6, smooth=True)
        # 口縁（別色の当て布）
        mb.add_rounded_box((cx, grip[1] + h * 0.008, cz + body_hh * 0.40),
                           (body_hw * 1.02, body_hd * 1.04, body_hh * 0.22),
                           "cloth_ribbon_green", seg=6, smooth=True)
        for sgn in (-1, 1):
            base = np.array([cx + sgn * body_hw * 0.62, grip[1] + h * 0.008,
                             cz + body_hh * 0.48])
            top = np.array([grip[0] + sgn * h * 0.005, grip[1], grip[2]])
            mid = (base + top) * 0.5 + np.array([sgn * h * 0.018, 0.0,
                                                 -h * 0.006])
            mb.add_tube(np.array([base, mid, top]),
                        [(h * 0.0055, h * 0.0090)] * 3, "bag_tote", n=6)


def _hoodie(mb, p, a: B.Anatomy, mat="cloth_hoodie"):
    """羽織ったパーカー。胴のシェル＋首の後ろのフード＋前ポケット。"""
    h = p["height"]
    z = p["z"]
    shell(mb, p, a, mat, "hoodie", z["hip"] - h * 0.006,
          z["shoulder"] + h * 0.020, h * 0.020, levels=9)
    _arm_sleeve(mb, p, a, mat, "sleeve", t_end=0.86, r_scale=1.34, puff=0.55)
    # フード（後頭部の下に垂れる袋）
    zz, rx, ry = _profile(p, a)
    z_h = z["shoulder"] + h * 0.006
    yy = float(np.interp(z_h, zz, ry))
    with mb.part("hoodie"):
        mb.add_sphere((0.0, yy * 0.72, z_h + h * 0.016),
                      (a.shoulder[0] * 0.62, yy * 0.86, h * 0.062), mat,
                      nu=16, nv=10)
    # 前ポケット
    z_p = z["waist"] - h * 0.010
    yp = -float(np.interp(z_p, zz, ry)) - h * 0.018
    with mb.part("hoodie"):
        mb.add_rounded_box((0.0, yp, z_p), (a.shoulder[0] * 0.92, h * 0.014,
                                            h * 0.052), mat, seg=3,
                           smooth=True)


def _necktie(mb, p, a: B.Anatomy, mat="necktie"):
    h = p["height"]
    z = p["z"]
    zz, rx, ry = _profile(p, a)
    z_k = z["shoulder"] - h * 0.014
    yk = -float(np.interp(z_k, zz, ry)) - h * 0.008
    with mb.part("necktie"):
        mb.add_rounded_box((0.0, yk, z_k), (h * 0.026, h * 0.014, h * 0.026),
                           mat, seg=3, smooth=True)
        z_b = z["bust"] - h * 0.030
        yb = -float(np.interp(z_b, zz, ry)) - h * 0.010
        pts = np.array([[0.0, yk, z_k - h * 0.014],
                        [0.0, (yk + yb) * 0.5, (z_k + z_b) * 0.5],
                        [0.0, yb, z_b]])
        mb.add_tube(pts, [(h * 0.008, h * 0.018), (h * 0.010, h * 0.022),
                          (h * 0.008, h * 0.020)], mat, n=6, power=2.6,
                    cap_start=True, cap_end=True)


def build_seifuku(mb, p, a: B.Anatomy, *, apron: bool = False,
                  hoodie: bool = False):
    h = p["height"]
    z = p["z"]
    acc = p.get("accessories", ())
    waist = z["waist"]
    bl_rings = shell(mb, p, a, "cloth_blouse", "blouse", waist - h * 0.020,
                     z["shoulder"] + h * 0.016, h * 0.0085, levels=9,
                     shoulder=True)
    yoke(mb, p, a, "cloth_blouse", "blouse_yoke", bl_rings[-1],
         z["shoulder"] + h * 0.016, h * 0.0085, rise=h * 0.030)
    _sailor_collar(mb, p, a, z["shoulder"] + h * 0.016)
    _arm_sleeve(mb, p, a, "cloth_blouse", "sleeve", t_end=0.42, r_scale=1.26,
                puff=1.00)
    if hoodie:
        _hoodie(mb, p, a)
    else:
        _ribbon(mb, p, a, "cloth_ribbon_green", z["bust"] + h * 0.018)
    band(mb, p, a, "cloth_skirt_navy", "waistband", waist - h * 0.026,
         waist + h * 0.012, h * 0.016)
    hem = z["crotch"] - (z["crotch"] - z["knee"]) * 0.46
    skirt_rings = pleated_skirt(mb, p, a, "cloth_skirt_navy", "skirt",
                                z_top=waist - h * 0.010, z_bot=hem,
                                r_top=0.99, r_bot=1.56, pleats=26, amp=0.17)
    leg_sleeve(mb, p, a, "cloth_socks_black", "socks", z["ankle"] - h * 0.012,
               z["knee"] - h * 0.028, h * 0.0055)
    if "sneakers" in acc:
        shoe(mb, p, a, "shoes_sneaker", heel=False, scale=1.22)
    else:
        shoe(mb, p, a, "shoes_loafer", heel=True)
    if "necktie" in acc:
        _necktie(mb, p, a)
    if apron:
        _apron(mb, p, a, skirt_rings)
    if "tote" in acc:
        _tote(mb, p, a)


#: エプロン前面の角度範囲（キャラクターは -Y を向く）
_APRON_SPAN = (math.pi * 1.20, math.pi * 1.80)


def _apron(mb, p, a: B.Anatomy, skirt_rings=None):
    h = p["height"]
    z = p["z"]
    seg = 22
    ang = np.linspace(_APRON_SPAN[0], _APRON_SPAN[1], seg)
    ca, sa = np.cos(ang), np.sin(ang)
    e = 2.0 / 2.25
    bx = np.sign(ca) * np.abs(ca) ** e
    by = np.sign(sa) * np.abs(sa) ** e

    # 下半身: スカートのヒダの尾根より外側に置かないと、谷から紺色が
    # 突き出してエプロンの裾がギザギザに見える。
    lower = []
    z_waist = z["waist"]
    if skirt_rings:
        keep = skirt_rings[:-1]
        cut = max(2, int(len(keep) * 0.72))
        for r in keep[:cut]:
            rx = float(np.abs(r[:, 0]).max()) * 1.035
            ry = float(np.abs(r[:, 1]).max()) * 1.035
            zz = float(r[0, 2])
            lower.append(np.stack([bx * rx * 0.86, by * ry,
                                   np.full(seg, zz)], axis=1))
        z_waist = float(keep[0][0, 2])
        lower = lower[::-1]          # 裾 -> 腰

    # 上半身: 胴プロファイルに沿わせる
    zs = np.linspace(z_waist, z["bust"] + h * 0.010, 6)
    upper, _ = garment_rings(p, a, zs, h * 0.020, seg=seg, span=_APRON_SPAN)
    scaled = []
    for i, r in enumerate(upper):
        t = i / (len(upper) - 1)
        rr = r.copy()
        rr[:, 0] *= 0.92 - 0.16 * t
        scaled.append(rr)
    with mb.part("apron"):
        mb.add_grid(lower + scaled, "cloth_apron", smooth=True, close_u=False)
        for sgn in (-1, 1):
            s = np.array([sgn * a.shoulder_half * 0.42, -a.waist_ry * 1.1,
                          z["bust"] + h * 0.010])
            e = np.array([sgn * a.shoulder_half * 0.55, a.waist_ry * 0.9,
                          z["shoulder"] - h * 0.006])
            mb.add_tube(np.array([s, (s + e) * 0.5 + np.array([0, 0, h * 0.01]), e]),
                        [(h * 0.004, h * 0.016)] * 3, "cloth_apron", n=6)


# --------------------------------------------------------------------------
# 和装
# --------------------------------------------------------------------------


def _kimono_collar(mb, p, a: B.Anatomy, mat, z_top, z_cross):
    """衿は半衿の無地。着物と同じ柄地だと V が消えて船底襟に見える。"""
    """V 字に合わせた衿。左前（着る人の左が上）で重ねる。"""
    h = p["height"]
    zz, rx, ry = _profile(p, a)
    for sgn in (1, -1):
        pts_out, pts_in = [], []
        n = 9
        for i in range(n):
            t = i / (n - 1)
            zi = z_top + (z_cross - z_top) * t
            rxi = float(np.interp(zi, zz, rx))
            ryi = float(np.interp(zi, zz, ry))
            x = sgn * rxi * (0.62 - 0.56 * t)
            # 胸の楕円断面に載せたうえで、着物シェル（+h*0.012）より
            # さらに外へ出す。内側に置くと柄地に埋まって V が消える。
            k = max(0.0, 1.0 - (x / max(rxi, 1e-6)) ** 2) ** 0.5
            y = -(ryi * (0.40 + 0.60 * k) + h * 0.024)
            w = h * 0.026
            pts_out.append([x + sgn * w, y - h * 0.004, zi])
            pts_in.append([x - sgn * w * 0.30, y - h * 0.011, zi])
        with mb.part("collar"):
            mb.add_quad_strip(np.array(pts_out), np.array(pts_in), mat)


def _furi_sleeve(mb, p, a: B.Anatomy, mat, *, drop=1.0):
    """振り袖（袂が垂れる袖）。"""
    h = p["height"]
    r0, r1, r2 = a.arm_r
    for sgn in (-1, 1):
        sh = a.shoulder * np.array([sgn, 1, 1])
        el = a.elbow * np.array([sgn, 1, 1])
        wr = a.wrist * np.array([sgn, 1, 1])
        # 筒のリング法線は腕に垂直なので、太らせると袂が前後（Y）へ
        # 膨らんで凧のようになる。筒は腕に沿わせるだけにして、
        # 袂は肘から真下へ垂らす別パーツにする。
        path = np.array([sh + (el - sh) * 0.02, sh + (el - sh) * 0.45, el,
                         el + (wr - el) * 0.72])
        # 袖は「腕の太さ基準」で細く作る。身長基準だと低頭身で寸胴になる。
        sr = max(float(r0), float(r1))
        radii = [(sr * 1.14, sr * 1.06), (sr * 1.20, sr * 1.12),
                 (sr * 1.26, sr * 1.17), (sr * 1.18, sr * 1.10)]
        part = f"sleeve_{'l' if sgn > 0 else 'r'}"
        with mb.part(part):
            # 肩。着物シェルの上端からはみ出す肩の丸みを覆う。
            cap = sr * 1.16
            mb.add_sphere((float(sh[0]), float(sh[1]), float(sh[2])),
                          (cap, cap, cap), mat, nu=16, nv=10)
            mb.add_tube(path, radii, mat, n=14, power=2.4, cap_start=False,
                        cap_end=True)
            top = el + (wr - el) * 0.06
            cx = float(top[0]) + sgn * h * 0.006
            cy = float(top[1])
            z_top = float(top[2]) + h * 0.026
            z_bot = z_top - p["head_h"] * (0.52 * drop)
            rings = []
            k = 9
            for i in range(k):
                t = i / (k - 1)
                rx = sr * (1.20 + 0.34 * t)
                ry = sr * (1.12 + 0.22 * t)
                if t > 0.80:
                    s2 = math.sqrt(max(0.0, 1.0 - ((t - 0.80) / 0.20) ** 2))
                    rx *= 0.30 + 0.70 * s2
                    ry *= 0.30 + 0.70 * s2
                rings.append(M.ring(16, rx, ry, power=2.1, cx=cx, cy=cy,
                                    z=z_top + (z_bot - z_top) * t))
            mb.add_grid(rings, mat, smooth=True, cap_start=True, cap_end=True)


def _obi(mb, p, a: B.Anatomy, mat, z_c, width):
    h = p["height"]
    band(mb, p, a, mat, "obi", z_c - width * 0.5, z_c + width * 0.5, h * 0.016)


def _hakama_himo(mb, p, a: B.Anatomy, mat, z_c):
    h = p["height"]
    zz, rx, ry = _profile(p, a)
    ryi = float(np.interp(z_c, zz, ry)) + h * 0.022
    with mb.part("himo"):
        mb.add_box((0.0, -ryi, z_c), (h * 0.075, h * 0.012, h * 0.016), mat)
        for sgn in (-1, 1):
            mb.add_sphere((sgn * h * 0.030, -ryi - h * 0.006, z_c),
                          (h * 0.032, h * 0.012, h * 0.020), mat, nu=10, nv=6)


def _geta(mb, p, a: B.Anatomy):
    h = p["height"]
    fw, fl_, fh = a.foot
    for sgn in (-1, 1):
        cx = a.ankle[0] * sgn
        with mb.part(f"shoes_{'l' if sgn > 0 else 'r'}"):
            mb.add_box((cx, -fl_ * 0.18, fh * 0.035),
                       (fw * 1.52, fl_ * 1.40, fh * 0.48), "geta_wood")
            for dy in (-fl_ * 0.42, fl_ * 0.34):
                mb.add_box((cx, dy, fh * -0.435),
                           (fw * 1.34, fl_ * 0.26, fh * 0.59), "geta_wood")
            # 鼻緒
            tip = np.array([cx, -fl_ * 0.62, fh * 0.275])
            for s2 in (-1, 1):
                anchor = np.array([cx + s2 * fw * 0.55, -fl_ * 0.02, fh * 0.275])
                mb.add_tube(np.array([tip, (tip + anchor) * 0.5 + np.array([0, 0, h * 0.010]),
                                      anchor]),
                            [(h * 0.004, h * 0.008)] * 3, "furoshiki_red", n=5)


def _boots(mb, p, a: B.Anatomy):
    h = p["height"]
    shoe(mb, p, a, "boots_brown", heel=True, scale=1.14)
    top = a.ankle[2] + (a.knee[2] - a.ankle[2]) * 0.52
    leg_sleeve(mb, p, a, "boots_brown", "bootleg", a.ankle[2] + h * 0.006,
               top, h * 0.0075, levels=5)
    # 編み上げ
    for sgn in (-1, 1):
        cx = a.ankle[0] * sgn
        zs = np.linspace(a.ankle[2] + h * 0.020, top - h * 0.010, 6)
        for i in range(len(zs) - 1):
            r = a.leg_r[2] + h * 0.010
            p0 = np.array([cx - r * 0.55, -r * 0.92, zs[i]])
            p1 = np.array([cx + r * 0.55, -r * 0.92, zs[i + 1]])
            p2 = np.array([cx + r * 0.55, -r * 0.92, zs[i]])
            p3 = np.array([cx - r * 0.55, -r * 0.92, zs[i + 1]])
            with mb.part(f"bootleg_{'l' if sgn > 0 else 'r'}"):
                mb.add_tube(np.array([p0, p1]), [h * 0.0035] * 2, "metal", n=4)
                mb.add_tube(np.array([p2, p3]), [h * 0.0035] * 2, "metal", n=4)


def _furoshiki_shoulder(mb, p, a: B.Anatomy):
    """右肩に担いだ風呂敷包み。包みは肩の後ろ、結び目は肩の前。"""
    h = p["height"]
    sh = a.shoulder * np.array([-1.0, 1.0, 1.0])
    c = np.array([sh[0] * 0.78, sh[1] + h * 0.088, sh[2] + h * 0.052])
    knot = np.array([sh[0] * 0.74, sh[1] - h * 0.052, sh[2] + h * 0.006])
    top = np.array([sh[0] * 0.70, sh[1] + h * 0.006, sh[2] + h * 0.060])
    mat = "furoshiki_orange"
    with mb.part("bag"):
        mb.add_sphere(tuple(c), (h * 0.072, h * 0.062, h * 0.068), mat,
                      nu=16, nv=10)
        # 肩を越える布（結び目 -> 肩の上 -> 包み）
        path = np.array([knot, top, c + np.array([0.0, -h * 0.045, h * 0.040])])
        mb.add_tube(path, [(h * 0.034, h * 0.012), (h * 0.040, h * 0.013),
                           (h * 0.050, h * 0.020)], mat, n=8,
                    power=2.6, cap_start=False, cap_end=False)
        mb.add_sphere(tuple(knot), (h * 0.030, h * 0.026, h * 0.024), mat,
                      nu=10, nv=7)
        # 結び目から垂れる端
        for dx, dz in ((-0.6, -1.0), (0.5, -0.9)):
            e = knot + np.array([dx * h * 0.030, -h * 0.010, dz * h * 0.055])
            mb.add_tube(np.array([knot, (knot + e) * 0.5, e]),
                        [(h * 0.016, h * 0.011), (h * 0.013, h * 0.009),
                         (h * 0.004, h * 0.003)], mat, n=7)


def _furoshiki(mb, p, a: B.Anatomy):
    if p.get("bag_mount") == "shoulder":
        _furoshiki_shoulder(mb, p, a)
        return
    h = p["height"]
    g = grip_point(a)
    knot = np.array([g[0], g[1] + h * 0.004, g[2] - h * 0.010])
    c = (float(knot[0]), float(knot[1]), float(knot[2]) - h * 0.086)
    with mb.part("bag"):
        # 包み本体（角を落とした布の塊）
        mb.add_rounded_box(c, (h * 0.150, h * 0.108, h * 0.128),
                           "furoshiki_red", seg=3)
        # 手に掛かる結び目
        mb.add_sphere((float(knot[0]), float(knot[1]),
                       float(knot[2]) - h * 0.014),
                      (h * 0.030, h * 0.027, h * 0.025), "furoshiki_red",
                      nu=10, nv=7)
        # 結び目から跳ねる四隅
        for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            e = np.array([knot[0] + dx * h * 0.044, knot[1] + dy * h * 0.034,
                          knot[2] + h * 0.022])
            mb.add_tube(np.array([knot, (knot + e) * 0.5, e]),
                        [(h * 0.018, h * 0.013), (h * 0.015, h * 0.011),
                         (h * 0.004, h * 0.003)], "furoshiki_red", n=7)


def build_kimono(mb, p, a: B.Anatomy, *, kimono_mat, hakama_mat, shoes,
                 hakama_high=True, hakama_pleats=26):
    h = p["height"]
    z = p["z"]
    hak_z = z["underbust"] if hakama_high else z["waist"]
    body_bot = z["crotch"] + (z["hip"] - z["crotch"]) * 0.3
    k_rings = shell(mb, p, a, kimono_mat, "kimono", body_bot,
                    z["shoulder"] + h * 0.014, h * 0.012, levels=9,
                    bust=0.55, shoulder=True)
    yoke(mb, p, a, kimono_mat, "kimono_yoke", k_rings[-1],
         z["shoulder"] + h * 0.014, h * 0.012, rise=h * 0.030, bust=0.55)
    _kimono_collar(mb, p, a, "collar_white", z["shoulder"] + h * 0.010,
                   hak_z + h * 0.012)
    _furi_sleeve(mb, p, a, kimono_mat)
    _obi(mb, p, a, hakama_mat, hak_z + h * 0.012, h * 0.036)
    # 明治の袴は足首まで落ちる。ブーツのときは編み上げの口をわずかに隠す丈に
    # して、裾と履物のあいだに素足が出ないようにする。
    if shoes == "geta":
        hem = z["ankle"] + h * 0.022
    else:
        boot_top = a.ankle[2] + (a.knee[2] - a.ankle[2]) * 0.52
        hem = boot_top + h * 0.016
    pleated_skirt(mb, p, a, hakama_mat, "hakama",
                  z_top=hak_z + h * 0.004, z_bot=hem,
                  r_top=1.06, r_bot=1.30, pleats=hakama_pleats, amp=0.20,
                  flare=1.05, levels=16, clear=0.0235)
    _hakama_himo(mb, p, a, hakama_mat, hak_z + h * 0.014)
    if shoes == "geta":
        _geta(mb, p, a)
    else:
        _boots(mb, p, a)
    if "furoshiki" in p.get("accessories", ()):
        _furoshiki(mb, p, a)


# --------------------------------------------------------------------------
# 白衣
# --------------------------------------------------------------------------


def build_labcoat(mb, p, a: B.Anatomy):
    h = p["height"]
    z = p["z"]
    sh_rings = shell(mb, p, a, "cloth_blouse", "shirt",
                     z["waist"] - h * 0.010, z["shoulder"] + h * 0.014,
                     h * 0.008, levels=7, bust=0.0, shoulder=True)
    yoke(mb, p, a, "cloth_blouse", "shirt_yoke", sh_rings[-1],
         z["shoulder"] + h * 0.014, h * 0.008, rise=h * 0.028, bust=0.0)
    _collar(mb, p, a, "cloth_blouse", z["shoulder"] + h * 0.014, drop=0.040)
    # ズボン
    leg_sleeve(mb, p, a, "cloth_pants_gray", "pants", z["ankle"] + h * 0.020,
               z["crotch"] + h * 0.004, h * 0.016, levels=7, taper_top=1.05)
    shell(mb, p, a, "cloth_pants_gray", "pants_seat", z["crotch"] - h * 0.010,
          z["waist"] + h * 0.004, h * 0.016, levels=5, bust=0.0)
    shoe(mb, p, a, "shoes_loafer", heel=True, scale=1.12)
    # 白衣（前開き）
    zs = np.linspace(z["knee"] + (z["crotch"] - z["knee"]) * 0.45,
                     z["shoulder"] + h * 0.012, 9)
    rings, _ = garment_rings(p, a, zs, h * 0.026, seg=26, shoulder=True,
                             span=(math.radians(-72.0), math.radians(252.0)))
    with mb.part("labcoat"):
        mb.add_grid(rings, "labcoat", smooth=True, close_u=False)
    _arm_sleeve(mb, p, a, "labcoat", "sleeve", t_end=0.92, r_scale=1.40,
                puff=0.25)
    # 襟（折り返し）
    _collar(mb, p, a, "labcoat", z["shoulder"] + h * 0.012, drop=0.050)


# --------------------------------------------------------------------------


def build_outfit(mb: M.MeshBuilder, p: dict, a: B.Anatomy) -> None:
    """`params.py` の outfit 名でディスパッチする。"""
    outfit = p["outfit"]
    if outfit == "seifuku":
        build_seifuku(mb, p, a)
    elif outfit == "seifuku_apron":
        build_seifuku(mb, p, a, apron=True)
    elif outfit == "seifuku_hoodie":
        build_seifuku(mb, p, a, hoodie=True)
    elif outfit == "kimono_botchan":
        build_kimono(mb, p, a, kimono_mat="cloth_kimono_kasuri_blue",
                     hakama_mat="cloth_hakama_blue", shoes="geta",
                     hakama_high=False, hakama_pleats=24)
    elif outfit == "kimono_madonna":
        build_kimono(mb, p, a, kimono_mat="cloth_kimono_yagasuri_red",
                     hakama_mat="cloth_hakama_purple", shoes="boots",
                     hakama_high=True, hakama_pleats=28)
    elif outfit == "labcoat":
        build_labcoat(mb, p, a)
    else:
        raise KeyError(f"未知の outfit: {outfit}")

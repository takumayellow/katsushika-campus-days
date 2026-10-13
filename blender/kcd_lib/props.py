"""ベンチ・照明柱・看板などの小物（樹木は trees.py）。"""

import math

from . import geom
# 樹木は trees.py（#51）。build_campus.py は props.TREE_SPECIES を読む。
from .trees import TREE_SPECIES  # noqa: F401


def add_bench(mb, x, y, ang, back=True):
    """ベンチ。ローカル +v 側に背もたれ（back=True）。"""
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)

    # 座面
    mb.add_quad(P(-0.9, -0.25, 0.45), P(0.9, -0.25, 0.45),
                P(0.9, 0.25, 0.45), P(-0.9, 0.25, 0.45), "wood")
    mb.add_quad(P(-0.9, -0.25, 0.38), P(-0.9, 0.25, 0.38),
                P(0.9, 0.25, 0.38), P(0.9, -0.25, 0.38), "wood")
    mb.add_quad(P(-0.9, 0.25, 0.38), P(0.9, 0.25, 0.38),
                P(0.9, 0.25, 0.45), P(-0.9, 0.25, 0.45), "wood")
    mb.add_quad(P(0.9, -0.25, 0.38), P(-0.9, -0.25, 0.38),
                P(-0.9, -0.25, 0.45), P(0.9, -0.25, 0.45), "wood")
    # 脚
    for du in (-0.7, 0.7):
        p = P(du, 0, 0)
        mb.add_box_c(p[0], p[1], 0.0, 0.10, 0.55, 0.40, "metal_grey")
    if back:
        add_box_rot(mb, x, y, ang, -0.9, 0.22, 0.50, 0.9, 0.28, 0.92, "wood")
        for du in (-0.7, 0.7):
            add_box_rot(mb, x, y, ang, du - 0.03, 0.22, 0.40, du + 0.03, 0.28, 0.50, "metal_grey")


def add_lamp(mb, x, y, h=4.0):
    mb.add_cylinder(x, y, 0.0, h, 0.09, "metal_white", seg=8, cap_top=False)
    mb.add_box_c(x, y, h, 0.55, 0.30, h + 0.22, "metal_white", top_mat="metal_white")
    mb.add_box_c(x, y, h - 0.08, 0.45, 0.22, h, "line_white")


def scatter_positions(polys, density, blocked, rng, jitter=1.0, limit=100000):
    """ポリゴン内に density [本/m^2] でランダム散布する（blocked(x,y)==True は除外）。"""
    out = []
    for poly in polys:
        x0, y0, x1, y1 = geom.bbox(poly)
        area = abs(geom.poly_area(poly))
        n = int(area * density)
        tries = 0
        placed = 0
        while placed < n and tries < n * 12 and len(out) < limit:
            tries += 1
            px = rng.uniform(x0, x1)
            py = rng.uniform(y0, y1)
            if not geom.point_in_poly((px, py), poly):
                continue
            if blocked(px, py):
                continue
            out.append((px, py))
            placed += 1
    return out


def line_positions(a, b, step, blocked, offset=0.0):
    """a→b に等間隔で並べる（offset は左手側へのずらし）。"""
    d = geom.sub(b, a)
    L = geom.length(d)
    if L < step:
        return []
    e = geom.mul(d, 1.0 / L)
    nrm = (-e[1], e[0])
    out = []
    n = int(L / step)
    for i in range(n + 1):
        p = geom.add(geom.add(a, geom.mul(e, i * step)), geom.mul(nrm, offset))
        if not blocked(p[0], p[1]):
            out.append(p)
    return out


# --------------------------------------------------------------------------- #
#  外構小物（看板・自販機・ゴミ箱）
# --------------------------------------------------------------------------- #
def _local(x, y, ang):
    """(x, y) を原点に ang 回転したローカル座標 (du, dv, z) → ワールド 3D 点。"""
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)
    return P


def add_box_rot(mb, x, y, ang, du0, dv0, z0, du1, dv1, z1, mat, top=True):
    """ローカル座標 (du, dv) で指定した直方体を ang 回転して置く。"""
    P = _local(x, y, ang)
    poly = [P(du0, dv0, 0)[:2], P(du1, dv0, 0)[:2], P(du1, dv1, 0)[:2], P(du0, dv1, 0)[:2]]
    mb.add_prism(poly, z0, z1, mat, mat if top else None, None)


def add_pylon_sign(mb, x, y, ang, zc, w=2.4, h=1.2):
    """建物名の立て看板。ローカル +u が板の正面（法線）。文字は Unity 側で貼る。"""
    hw = w * 0.5
    band = 0.28
    add_box_rot(mb, x, y, ang, -0.05, -hw, zc - h * 0.5, 0.05, hw, zc + h * 0.5 - band,
                "sign_plate")
    add_box_rot(mb, x, y, ang, -0.05, -hw, zc + h * 0.5 - band, 0.05, hw, zc + h * 0.5,
                "tus_green")
    P = _local(x, y, ang)
    for dv in (-hw + 0.25, hw - 0.25):
        p = P(0.0, dv, 0.0)
        mb.add_cylinder(p[0], p[1], 0.0, zc - h * 0.5, 0.05, "metal_grey", seg=8,
                        cap_top=False)


def add_vending(mb, x, y, ang, color="vending_red"):
    """自販機 1.0 (幅) × 0.75 (奥行) × 1.85 m。ローカル +u が正面。"""
    add_box_rot(mb, x, y, ang, -0.375, -0.5, 0.0, 0.375, 0.5, 1.85, color)
    P = _local(x, y, ang)
    mb.add_quad(P(0.39, -0.42, 0.95), P(0.39, 0.42, 0.95),
                P(0.39, 0.42, 1.75), P(0.39, -0.42, 1.75), "glass_dark")
    mb.add_quad(P(0.39, -0.42, 0.25), P(0.39, 0.42, 0.25),
                P(0.39, 0.42, 0.60), P(0.39, -0.42, 0.60), "metal_grey")


def add_trash_can(mb, x, y):
    mb.add_cylinder(x, y, 0.0, 0.85, 0.28, "bin_green", seg=10, cap_top=True)
    mb.add_cylinder(x, y, 0.85, 0.92, 0.30, "metal_grey", seg=10, cap_top=True)

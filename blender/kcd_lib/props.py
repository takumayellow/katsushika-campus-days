"""樹木・ベンチ・照明柱などの小物。"""

import math
import random

from . import geom
from .mesh import MeshBuilder


def add_blob(mb, cx, cy, cz, rx, ry, rz, mat, seg=8, rings=4):
    """低ポリの楕円体（葉のかたまり）。"""
    prev = None
    for k in range(rings + 1):
        phi = math.pi * k / rings
        r = math.sin(phi)
        z = cz + rz * math.cos(phi)
        if k == 0 or k == rings:
            ring = [(cx, cy, z)] * seg
        else:
            ring = [(cx + rx * r * math.cos(2 * math.pi * i / seg),
                     cy + ry * r * math.sin(2 * math.pi * i / seg), z) for i in range(seg)]
        if prev is not None:
            for i in range(seg):
                j = (i + 1) % seg
                a, b, c, d = prev[i], prev[j], ring[j], ring[i]
                if k == rings:
                    mb.add_face([a, b, c], mat)
                elif k == 1:
                    mb.add_face([a, c, d], mat)
                else:
                    mb.add_quad(a, b, c, d, mat)
        prev = ring


def add_cone(mb, cx, cy, z0, z1, r, mat, seg=8):
    ring = [(cx + r * math.cos(2 * math.pi * i / seg),
             cy + r * math.sin(2 * math.pi * i / seg), z0) for i in range(seg)]
    tip = (cx, cy, z1)
    for i in range(seg):
        j = (i + 1) % seg
        mb.add_face([ring[i], ring[j], tip], mat)
    mb.add_face(list(reversed(ring)), mat)


def tree_keyaki(name="tree_mesh_keyaki"):
    """ケヤキ風。杯状に枝分かれし、上が広い。高さ 9.5 m。"""
    mb = MeshBuilder(name)
    mb.add_cylinder(0, 0, 0.0, 3.4, 0.26, "trunk", seg=8, cap_top=False)
    for i in range(3):
        a = 2 * math.pi * i / 3 + 0.4
        dx, dy = math.cos(a) * 1.3, math.sin(a) * 1.3
        mb.add_face([(0.20 * math.cos(a), 0.20 * math.sin(a), 3.0),
                     (dx, dy, 6.2), (dx * 0.5, dy * 0.5, 3.1)], "trunk")
        add_blob(mb, dx * 1.05, dy * 1.05, 7.1, 2.05, 2.05, 1.7, "leaf")
    add_blob(mb, 0, 0, 8.2, 2.5, 2.5, 1.7, "leaf_light")
    return mb


def tree_round(name="tree_mesh_round"):
    """丸い樹冠の中木。高さ 6.5 m。"""
    mb = MeshBuilder(name)
    mb.add_cylinder(0, 0, 0.0, 2.4, 0.20, "trunk", seg=8, cap_top=False)
    add_blob(mb, 0, 0, 4.3, 2.0, 2.0, 1.8, "leaf")
    add_blob(mb, 0.8, -0.5, 3.1, 1.2, 1.2, 1.0, "leaf_light")
    return mb


def tree_pine(name="tree_mesh_pine"):
    """常緑の円錐樹。高さ 8 m。"""
    mb = MeshBuilder(name)
    mb.add_cylinder(0, 0, 0.0, 1.6, 0.25, "trunk", seg=8, cap_top=False)
    add_cone(mb, 0, 0, 1.4, 4.6, 2.4, "leaf")
    add_cone(mb, 0, 0, 3.4, 6.4, 1.8, "leaf")
    add_cone(mb, 0, 0, 5.2, 8.0, 1.2, "leaf_light")
    return mb


TREE_SPECIES = [
    ("keyaki", tree_keyaki),
    ("round", tree_round),
    ("pine", tree_pine),
]


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
#  外構小物（看板・駐輪場・自販機・ゴミ箱）
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


def add_bike(mb, x, y, ang):
    """自転車の粗いシルエット（前後輪 + フレーム + サドル + ハンドル）。ローカル +u が前。"""
    for du in (-0.55, 0.55):
        add_box_rot(mb, x, y, ang, du - 0.33, -0.02, 0.0, du + 0.33, 0.02, 0.66, "bike_tire")
    add_box_rot(mb, x, y, ang, -0.45, -0.025, 0.55, 0.45, 0.025, 0.62, "bike_frame")
    add_box_rot(mb, x, y, ang, -0.30, -0.025, 0.30, -0.22, 0.025, 0.95, "bike_frame")
    add_box_rot(mb, x, y, ang, 0.40, -0.025, 0.30, 0.48, 0.025, 1.00, "bike_frame")
    add_box_rot(mb, x, y, ang, -0.36, -0.12, 0.93, -0.16, 0.12, 0.98, "bike_tire")
    add_box_rot(mb, x, y, ang, 0.42, -0.28, 0.98, 0.46, 0.28, 1.02, "bike_frame")


def add_bike_shed(mb, x, y, ang, length=14.0, depth=2.4, bikes=True):
    """屋根付き駐輪場。ローカル u = 長手、v = 奥行（-v 側が開口、+v 側が背面の腰壁）。"""
    P = _local(x, y, ang)
    hl, hd = length * 0.5, depth * 0.5
    z_roof = 2.3
    for du in (-hl + 0.3, hl - 0.3):
        for dv in (-hd + 0.3, hd - 0.3):
            p = P(du, dv, 0.0)
            mb.add_cylinder(p[0], p[1], 0.0, z_roof, 0.06, "metal_grey", seg=8, cap_top=False)
    add_box_rot(mb, x, y, ang, -hl - 0.2, -hd - 0.3, z_roof, hl + 0.2, hd + 0.3, z_roof + 0.12,
                "metal_grey")
    add_box_rot(mb, x, y, ang, -hl, hd - 0.10, 0.0, hl, hd, 1.0, "concrete_light")
    add_box_rot(mb, x, y, ang, -hl + 0.2, -0.2, 0.0, hl - 0.2, 0.2, 0.12, "concrete_grey")
    n = int(length / 0.6)
    for i in range(n):
        du = -hl + 0.5 + i * 0.6
        add_box_rot(mb, x, y, ang, du - 0.02, -0.15, 0.12, du + 0.02, 0.15, 0.45, "metal_grey")
        if bikes and i % 2 == 0:
            p = P(du, 0.0, 0.0)
            add_bike(mb, p[0], p[1], ang + math.pi * 0.5)


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

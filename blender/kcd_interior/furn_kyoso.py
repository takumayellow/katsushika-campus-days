"""共創棟（kyoso）だけで使う部品。plan_kyoso.py から呼ぶ。

座標はすべて建物のローカル（入口の真下の床が原点、+Y が奥、Z が上）。
材質の名前は mats_kyoso.py（kyoso_ で始まる）か、imats.py の共有の材質。
"""

import math

from . import furniture as F, kit, shell


def terrazzo(mb, x0, y0, x1, y1, rng, z=0.012, chips=40):
    """3 色を散らした人造石の床。淡い地に中間色と濃い色の小片を置く。"""
    kit.plate(mb, x0, y0, x1, y1, z, "kyoso_floor_terrazzo_pale")
    for i in range(chips):
        cx = x0 + 0.4 + rng.random() * (x1 - x0 - 0.8)
        cy = y0 + 0.4 + rng.random() * (y1 - y0 - 0.8)
        s = 0.18 + rng.random() * 0.34
        mat = ("kyoso_floor_terrazzo_dark" if i % 3 == 0
               else "kyoso_floor_terrazzo_mid")
        kit.plate(mb, cx - s, cy - s * 0.6, cx + s, cy + s * 0.6, z + 0.002, mat)


def _inside(x, y, holes):
    return any(h[0] <= x <= h[2] and h[1] <= y <= h[3] for h in holes)


def _runs_y(x, y0, y1, holes):
    """x の線上で、穴（矩形の列）を避けた y の区間。"""
    cuts = sorted((h[1], h[3]) for h in holes if h[0] <= x <= h[2])
    out, y = [], y0
    for a, b in cuts:
        if a > y + 1e-3:
            out.append((y, min(a, y1)))
        y = max(y, b)
    if y < y1 - 1e-3:
        out.append((y, y1))
    return [(a, b) for a, b in out if b - a > 0.2]


def louvers(mb, x0, y0, x1, y1, z, pitch=0.25, holes=(), w=0.08, h=0.12):
    """Y 方向に通る木の桟の天井。下端 z、桟は幅 w・せい h。穴の上は切る。

    下からしか見えないので、桟は下面と両側面の 3 枚だけにする。
    """
    n = int((x1 - x0) / pitch)
    for i in range(n):
        x = x0 + pitch * (i + 0.5)
        for a, b in _runs_y(x, y0, y1, holes):
            kit.plate(mb, x - w * 0.5, a, x + w * 0.5, b, z, "kyoso_louver_wood",
                      flip=True)
            kit.vplate(mb, (x - w * 0.5, b), (x - w * 0.5, a), z, z + h,
                       "kyoso_louver_wood")
            kit.vplate(mb, (x + w * 0.5, a), (x + w * 0.5, b), z, z + h,
                       "kyoso_louver_wood")


def rounded_rect(x0, y0, x1, y1, r, seg=3):
    """角を丸めた長方形の 2D 輪郭（反時計回り）。"""
    pts = []
    for cx, cy, a0 in ((x1 - r, y0 + r, -0.5 * math.pi), (x1 - r, y1 - r, 0.0),
                       (x0 + r, y1 - r, 0.5 * math.pi), (x0 + r, y0 + r, math.pi)):
        for k in range(seg + 1):
            a = a0 + 0.5 * math.pi * k / seg
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def sb_box(mb, x0, y0, x1, y1, top, r=0.7):
    """スターバックスの角の丸いセージ色の箱（厨房・倉庫）。黒い台座つき。"""
    mb.add_prism(rounded_rect(x0 + 0.03, y0 + 0.03, x1 - 0.03, y1 - 0.03, r),
                 0.0, 0.14, "kyoso_column_base")
    mb.add_prism(rounded_rect(x0, y0, x1, y1, r), 0.14, top, "kyoso_sb_sage",
                 "kyoso_sb_sage")


def deck(mb, x0, y0, x1, y1, tiers=3, rise=0.2, run=0.6):
    """南（-Y）と東（+X）へ下りる段床。踏面はオーク、蹴込みは黒。"""
    for k in range(tiers):
        kit.box(mb, x0, y0 + run * k, rise * k, x1 - run * k, y1, rise * (k + 1),
                "kyoso_steel_black", top="kyoso_floor_wood_oak")


def box_bench(mb, x0, y0, x1, y1, z=0.0, h=0.42, mat="kyoso_sofa_grey"):
    kit.box(mb, x0, y0, z, x1, y1, z + h, mat, top=mat)


def round_sofa(mb, x, y, z=0.0, r=0.85, mat="kyoso_sofa_beige"):
    """背もたれの柱を中心に持つ丸いソファ（4 人掛け）。"""
    kit.cyl(mb, x, y, z, z + 0.42, r, mat, seg=12)
    kit.cyl(mb, x, y, z + 0.42, z + 0.95, r * 0.36, mat, seg=10)


def pouf(mb, x, y, z=0.0, r=0.3, mat="kyoso_sofa_grey"):
    kit.cyl(mb, x, y, z, z + 0.4, r, mat, seg=8)


def box_stool(mb, x, y, z=0.0, s=0.42):
    kit.box_c(mb, x, y, z, s, s, z + 0.44, "kyoso_wood_oak", top="kyoso_wood_oak")


def modular_sofa(mb, x, y, ang, w=2.4, mat="kyoso_sofa_grey"):
    """背の低い箱型ソファ（3 人掛け）。ang=0 で座面が +Y を向く。"""
    t = kit.T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.42, 0.0, hw, 0.42, 0.42, mat)
    t.box(mb, -hw, -0.42, 0.42, hw, -0.22, 0.78, mat)
    t.box(mb, -hw, -0.22, 0.42, -hw + 0.2, 0.42, 0.62, mat)
    t.box(mb, hw - 0.2, -0.22, 0.42, hw, 0.42, 0.62, mat)


def side_table(mb, x, y, z=0.0):
    kit.cyl(mb, x, y, z + 0.48, z + 0.52, 0.24, "kyoso_steel_black", seg=8)
    kit.cyl(mb, x, y, z, z + 0.48, 0.03, "kyoso_steel_black", seg=4)


def tumbler_shelf(mb, x0, y0, x1, y1, h=1.8, tiers=4, rng=None):
    """忘れ物の棚（4 段）。段ごとに白いタンブラーを並べる。"""
    kit.box(mb, x0, y0, 0.0, x1, y1, 0.04, "plastic_white")
    for side in (y0, y1 - 0.03):
        kit.box(mb, x0, side, 0.0, x1, side + 0.03, h, "plastic_white")
    kit.box(mb, x1 - 0.03, y0, 0.0, x1, y1, h, "plastic_white")
    for k in range(1, tiers + 1):
        z = h * k / (tiers + 1)
        kit.box(mb, x0, y0, z, x1, y1, z + 0.03, "plastic_white")
        n = int((y1 - y0) / 0.22)
        for i in range(n):
            cy = y0 + 0.14 + i * 0.22
            if rng is not None and rng.random() < 0.3:
                continue
            kit.cyl(mb, (x0 + x1) * 0.5, cy, z + 0.03, z + 0.22, 0.045,
                    "plastic_white", seg=6)


def seat_mark(sink, x, y, z, ang, hw=0.25, hd=0.25, inner=0.5, depth=0.20):
    """(x, y) を外形の中心にした座面を 1 つ記録する。ang は座ったときの正面（kit.T と同じ）。"""
    F._seat(sink, kit.T(x, y, z, ang), len(sink.verts), hw, -hd, hd, inner, depth)


def glass_wall(mb, a, b, z1, gaps=(), pitch=1.8, plinth=0.12, door_top=None):
    """黒い方立てのガラス壁（a から b へ。gaps は a からの距離の区間）。

    gaps の上は door_top まで壁を立てる。ガラスは薄い板、床と天辺に黒い見切りを付ける。
    """
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length
    shell.partition(mb, a, b, 0.0, z1, "kyoso_steel_black", thick=0.08, gaps=gaps,
                    glass_top="glass_interior", glass_z=plinth)
    cuts = sorted(gaps)
    posts = [0.0, length]
    n = int(length / pitch)
    posts += [length * k / (n + 1) for k in range(1, n + 1)]
    for g in cuts:
        posts += [g[0], g[1]]
    for s in posts:
        if any(g[0] < s < g[1] for g in cuts):
            continue
        kit.box_c(mb, a[0] + ux * s, a[1] + uy * s, 0.0, 0.1, 0.1, z1, "kyoso_steel_black")
    for g in cuts:
        if door_top is not None and door_top < z1:
            shell.partition(mb, (a[0] + ux * g[0], a[1] + uy * g[0]),
                            (a[0] + ux * g[1], a[1] + uy * g[1]), door_top, z1,
                            "kyoso_steel_black", thick=0.08)

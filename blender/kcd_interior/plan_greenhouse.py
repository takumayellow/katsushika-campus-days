"""温室: ガラス framing・栽培ベンチ・鉢植え・灌水パイプ。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.30
Z_TOP = 3.60


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    rng = c.rng

    # 全面ガラス。腰壁を低く、方立を密にする。
    common.envelope(c, CEIL, "floor_concrete", door_w=1.6, z_top=Z_TOP,
                    sill=0.35, header=0.10, seg=1.1, ceil=False,
                    wall="metal_white", floor_thick=0.24)
    common.entry_kit(c, CEIL, door_w=1.6, spawn_depth=1.2)

    w = c.wall
    # ガラス屋根（切妻）
    ridge = (ix0 + ix1) * 0.5
    for side in (0, 1):
        ax = ix0 if side == 0 else ix1
        w.add_quad((ax, iy0, CEIL), (ax, iy1, CEIL),
                   (ridge, iy1, CEIL + 0.85), (ridge, iy0, CEIL + 0.85),
                   "glass_clear")
    # 棟木と垂木
    kit.tube(w, (ridge, iy0, CEIL + 0.85), (ridge, iy1, CEIL + 0.85), 0.055,
             "metal_white", seg=5)
    n = max(3, int((iy1 - iy0) / 1.1))
    for i in range(n + 1):
        ty = iy0 + (iy1 - iy0) * i / n
        kit.tube(w, (ix0, ty, CEIL), (ridge, ty, CEIL + 0.85), 0.04,
                 "metal_white", seg=4)
        kit.tube(w, (ix1, ty, CEIL), (ridge, ty, CEIL + 0.85), 0.04,
                 "metal_white", seg=4)
    # 妻面の三角ガラス
    for ty in (iy0, iy1):
        w.add_face([(ix0, ty, CEIL), (ix1, ty, CEIL),
                    (ridge, ty, CEIL + 0.85)], "glass_clear")

    # 栽培ベンチ（左右 2 列 + 中央通路）
    mb = c.furn("benches")
    length = (iy1 - iy0) - 2.2
    for sgn in (-1, 1):
        bx = (ix0 + ix1) * 0.5 + sgn * ((ix1 - ix0) * 0.5 - 1.15)
        F.greenhouse_bench(mb, bx, (iy0 + iy1) * 0.5, ang=math.pi * 0.5,
                           w=length, d=1.15, h=0.78, rng=rng)
    # 中央の低い育苗トレー
    F.greenhouse_bench(mb, (ix0 + ix1) * 0.5, iy1 - 2.4, ang=math.pi * 0.5,
                       w=3.2, d=0.9, h=0.45, rng=rng)

    # 床置きの大鉢
    for i in range(5):
        px = ix0 + 0.9 + (i % 2) * ((ix1 - ix0) - 1.8)
        py = iy0 + 2.0 + i * ((iy1 - iy0) - 3.4) / 4.0
        shell.planter(mb, px, py, r=0.36 + rng.random() * 0.12,
                      h=0.40, leaf_h=0.9 + rng.random() * 0.9)

    # 灌水パイプ（棟に沿って + ベンチ上の散水枝）
    kit.tube(mb, (ridge, iy0 + 0.4, CEIL + 0.45),
             (ridge, iy1 - 0.4, CEIL + 0.45), 0.045, "pipe_grey", seg=6)
    for k in range(7):
        ty = iy0 + 0.8 + k * ((iy1 - iy0) - 1.6) / 6.0
        kit.tube(mb, (ridge, ty, CEIL + 0.45), (ridge, ty, CEIL + 0.10),
                 0.016, "pipe_grey", seg=4)
    for sgn in (-1, 1):
        bx = (ix0 + ix1) * 0.5 + sgn * ((ix1 - ix0) * 0.5 - 1.15)
        kit.tube(mb, (bx, iy0 + 0.6, 2.10), (bx, iy1 - 0.6, 2.10), 0.03,
                 "pipe_grey", seg=5)
        for k in range(6):
            ty = iy0 + 1.0 + k * ((iy1 - iy0) - 2.0) / 5.0
            kit.tube(mb, (bx, ty, 2.10), (bx, ty, 1.70), 0.014, "pipe_grey",
                     seg=4)

    # 作業用の道具棚と流し
    F.lab_sink(mb, ix0 + 1.2, iy0 + 1.0, ang=0.0, w=1.0)
    F.bookshelf(mb, ix1 - 1.0, iy0 + 1.0, ang=0.0, w=0.9, h=1.60, shelves=4,
                rng=rng)

    # 照明（育成灯）
    pts = shell.ceiling_lights(w, ix0 + 0.8, iy0 + 0.8, ix1 - 0.8, iy1 - 0.8,
                               CEIL - 0.15, sx=2.0, sy=2.4, w=0.16, l=1.4,
                               mat="light_strip")
    c.lights_from(pts, CEIL - 0.2, energy=110.0)
    c.light((ix0 + ix1) * 0.5, (iy0 + iy1) * 0.5, 2.6, 260.0, 1.2)

    c.poi("plants", (ix0 + ix1) * 0.5, (iy0 + iy1) * 0.5, 0.0)
    c.npc((ix0 + ix1) * 0.5, iy0 + 2.6, 0.0)
    common.sign_board(c, mb, (ix0 + ix1) * 0.5, iy1 - 0.22, 2.20, ang=math.pi,
                      w=1.2, h=0.36)

    c.cam("", ((ix0 + ix1) * 0.5, iy0 + 0.9, 1.60),
          ((ix0 + ix1) * 0.5, iy1 - 1.0, 1.10), lens=16.0)
    c.note("切妻ガラス屋根の温室（栽培ベンチ 2 列・鉢植え・灌水パイプ）")

"""温室: ガラス framing・栽培ベンチ・鉢植え・灌水パイプ。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.30
Z_TOP = 3.60
# 中央通路の両わきのベンチの中心の x（中央から）。通路幅 = 2 * (INNER_X - 1.15 / 2) = 1.95 m
INNER_X = 1.55


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

    def pane(pts, inward):
        """ガラスの 1 枚。屋内モデルは中からしか見ないので、面は室内を向かせる。

        両面にすると屋根が「立てる面」になり、外周壁の上端（Z_TOP）を越えて
        外へ出られる面ができてしまう。天井（shell.ceiling）と同じ扱いにそろえる (#45)。
        inward は室内側の向き。巻き順がこれと逆なら裏返す。
        """
        a, b, c3 = pts[0], pts[1], pts[2]
        nx = (b[1] - a[1]) * (c3[2] - a[2]) - (b[2] - a[2]) * (c3[1] - a[1])
        ny = (b[2] - a[2]) * (c3[0] - a[0]) - (b[0] - a[0]) * (c3[2] - a[2])
        nz = (b[0] - a[0]) * (c3[1] - a[1]) - (b[1] - a[1]) * (c3[0] - a[0])
        if nx * inward[0] + ny * inward[1] + nz * inward[2] < 0.0:
            pts = list(reversed(pts))
        w.add_face(pts, "glass_clear")

    # ガラス屋根（切妻）
    ridge = (ix0 + ix1) * 0.5
    for ax in (ix0, ix1):
        pane([(ax, iy0, CEIL), (ax, iy1, CEIL),
              (ridge, iy1, CEIL + 0.85), (ridge, iy0, CEIL + 0.85)],
             (0.0, 0.0, -1.0))
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
    # 妻面の三角ガラス（手前は +Y、奥は -Y が室内）
    for ty, inward in ((iy0, 1.0), (iy1, -1.0)):
        pane([(ix0, ty, CEIL), (ix1, ty, CEIL), (ridge, ty, CEIL + 0.85)],
             (0.0, inward, 0.0))

    # 栽培ベンチ（外周に沿う 2 列 + 入口から奥への中央通路の両わきに 2 列）
    mb = c.furn("benches")
    length = (iy1 - iy0) - 2.2
    mid = (ix0 + ix1) * 0.5
    outer = (ix1 - ix0) * 0.5 - 1.15
    rows = [mid + d for d in (-outer, -INNER_X, INNER_X, outer)]
    for bx in rows:
        F.greenhouse_bench(mb, bx, (iy0 + iy1) * 0.5, ang=math.pi * 0.5,
                           w=length, d=1.15, h=0.78, rng=rng)

    # 床置きの大鉢（各列の奥の端と妻面の壁のあいだ。中央通路の突き当たりは看板の下なので空ける）
    for px in rows:
        shell.planter(mb, px, iy1 - 0.6, r=0.36 + rng.random() * 0.12,
                      h=0.40, leaf_h=0.9 + rng.random() * 0.9)

    # 灌水パイプ（棟に沿って + ベンチ上の散水枝）
    kit.tube(mb, (ridge, iy0 + 0.4, CEIL + 0.45),
             (ridge, iy1 - 0.4, CEIL + 0.45), 0.045, "pipe_grey", seg=6)
    for k in range(7):
        ty = iy0 + 0.8 + k * ((iy1 - iy0) - 1.6) / 6.0
        kit.tube(mb, (ridge, ty, CEIL + 0.45), (ridge, ty, CEIL + 0.10),
                 0.016, "pipe_grey", seg=4)
    for bx in rows:
        kit.tube(mb, (bx, iy0 + 0.6, 2.10), (bx, iy1 - 0.6, 2.10), 0.03,
                 "pipe_grey", seg=5)
        for k in range(6):
            ty = iy0 + 1.0 + k * ((iy1 - iy0) - 2.0) / 5.0
            kit.tube(mb, (bx, ty, 2.10), (bx, ty, 1.70), 0.014, "pipe_grey",
                     seg=4)

    # 作業用の道具棚と流し
    F.lab_sink(mb, ix0 + 1.2, iy0 + 1.0, ang=0.0, w=1.0)
    # 道具棚は「扉つきの箱」にする。段板のある書架 (h=1.60) だと
    # 栽培ベンチの天端 0.78 -> 本 1.16 -> 段板 1.37 -> 棚の天端 1.60 と
    # よじ登れて、棚と東側の壁の隙間へ 1.6 m 落ちられた (#45)。
    # 扉は南（入口）向き。足場になる段が外に出ないので登れない。
    cx_, cy_ = ix1 - 1.0, iy0 + 0.75
    kit.box(mb, cx_ - 0.45, cy_ - 0.17, 0.0, cx_ + 0.45, cy_ + 0.17, 1.85,
            "desk_wood")
    for sx in (-1, 1):
        kit.box(mb, cx_ + sx * 0.02, cy_ - 0.19, 0.28,
                cx_ + sx * 0.42, cy_ - 0.17, 1.76, "desk_white")
        kit.box(mb, cx_ + sx * 0.07, cy_ - 0.21, 0.98,
                cx_ + sx * 0.11, cy_ - 0.19, 1.12, "metal_gray")

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
    c.note("切妻ガラス屋根の温室（棟木は入口の妻面から奥へ・栽培ベンチ 4 列・鉢植え・灌水パイプ）")

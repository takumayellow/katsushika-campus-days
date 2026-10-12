"""温室: 陸屋根のガラス箱（L 字）・栽培ベンチ・鉢植え・灌水パイプ。

外装（kcd_lib.buildings.build_greenhouse）と同じく、外接矩形から奥の左の角
（spec.notch）を欠いた L 字にする。屋根は白い梁で区切った平らなガラス。
"""

import math

from . import common, furniture as F, kit, shell, signs

CEIL = 3.05          # 屋根ガラスの下面（建物の高さ 3.4 m から屋根の厚みぶん下げる）
FLOOR_T = 0.24       # 床スラブの厚み
Z_TOP = 3.35         # 外周壁の上端（屋根ガラスより上まで立ててすき間を作らない）
DOOR_W = 1.6
BEAM_X = 1.25        # 屋根の梁の間隔（x 方向に並ぶ梁）
BEAM_Y = 1.8         # 屋根の梁の間隔（y 方向に並ぶ梁）
# 中央通路の両わきのベンチの中心の x（中央から）。通路幅 = 2 * (INNER_X - 1.15 / 2) = 1.95 m
INNER_X = 1.55
BENCH_D = 1.15
# 灌水の本管は中央通路の中で中心から東へずらす。中心の真上は訪問ゾーン（POI）で、
# ゾーンの上端（床から 2.7 m）から下へ床を探すので、管があると床より先に当たる
PIPE_DX = 0.5


def _outline(s):
    """躯体外面の L 字（反時計回り。入口の辺から始まる）。"""
    nx0, ny0, nx1, ny1 = s.notch
    assert nx0 <= s.x0 + 1e-6 and ny1 >= s.y_back - 1e-6, "欠けた角は奥の左のはず"
    return [(s.x0, s.y_face), (s.x1, s.y_face), (s.x1, s.y_back),
            (nx1, s.y_back), (nx1, ny0), (s.x0, ny0)]


def _shell(c):
    """床スラブ + L 字の外周のガラス壁 + 入口のガラススクリーン。

    戻り値は室内から見た欠けた角（壁の厚みぶん広げた矩形）。
    """
    s = c.spec
    pts = _outline(s)
    n = len(pts)
    # 床スラブ。上面と下面は L を 2 つの矩形に分け、見付は L の外周に沿って立てる
    nx0, ny0, nx1, ny1 = s.notch
    zb = -FLOOR_T
    for r in ((s.x0, s.y_face, s.x1, ny0), (nx1, ny0, s.x1, s.y_back)):
        kit.plate(c.floor, r[0], r[1], r[2], r[3], 0.0, "floor_concrete")
        kit.plate(c.floor, r[0], r[1], r[2], r[3], zb, "concrete_grey", flip=True)
    for i in range(n):
        kit.vplate(c.floor, pts[i], pts[(i + 1) % n], zb, 0.0, "concrete_grey")
    d0, d1 = -DOOR_W * 0.5, DOOR_W * 0.5
    z_screen = CEIL - 0.05     # 入口の開口の上端。屋根ガラスより下でふさぐ
    for i in range(n):
        p, a, b = pts[i - 1], pts[i], pts[(i + 1) % n]
        # 入隅から出る辺は、壁の厚みぶん手前から立てて入隅の升を埋める。
        # そうしないと 2 枚のガラスが入隅で離れ、方立のあいだが外へ抜ける (#45)
        cross = (a[0] - p[0]) * (b[1] - a[1]) - (a[1] - p[1]) * (b[0] - a[0])
        if cross < 0.0:
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            a = (a[0] - (b[0] - a[0]) / L * shell.WALL,
                 a[1] - (b[1] - a[1]) / L * shell.WALL)
        gaps = [(d0 - s.x0, d1 - s.x0)] if i == 0 else []
        shell.outer_wall(c.wall, a, b, 0.0, Z_TOP, wall="metal_white",
                         glass="glass_clear", sill=0.35, header=0.10, seg=1.1,
                         gaps=gaps, gap_top=z_screen if gaps else None)
    shell.glass_entrance(c.wall, d0, d1, 0.0, z_screen,
                         s.y_face + shell.WALL * 0.5)
    c.door_gap = (d0, d1)
    # 入隅の升は柱で埋める。ガラスの板は壁の厚みの途中にあるので、2 辺の板が
    # 入隅で交わらず、升の中を外へ抜けられる (#45)
    kit.box(c.wall, nx1, ny0 - shell.WALL, 0.0, nx1 + shell.WALL, ny0, Z_TOP,
            "metal_white")
    return (nx0 - 1.0, ny0 - shell.WALL, nx1 + shell.WALL, ny1 + 1.0)


def _roof(c, hole):
    """平らなガラス屋根（室内を向いた 1 枚。#45）と白い梁の格子。"""
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    w = c.wall
    shell.ceiling(w, ix0, iy0, ix1, iy1, CEIL, "glass_clear", holes=[hole])
    nx = max(1, int(round((ix1 - ix0) / BEAM_X)))
    ny = max(1, int(round((iy1 - iy0) / BEAM_Y)))
    hw = 0.035
    for i in range(1, nx):
        x = ix0 + (ix1 - ix0) * i / nx
        for a in shell._cut(x - hw, iy0, x + hw, iy1, [hole]):
            kit.box(w, a[0], a[1], CEIL - 0.10, a[2], a[3], CEIL, "metal_white")
    for j in range(1, ny):
        y = iy0 + (iy1 - iy0) * j / ny
        for a in shell._cut(ix0, y - hw, ix1, y + hw, [hole]):
            kit.box(w, a[0], a[1], CEIL - 0.10, a[2], a[3], CEIL, "metal_white")


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    rng = c.rng

    # 全面ガラス。腰壁を低く、方立を密にする。
    hole = _shell(c)
    common.entry_kit(c, CEIL, door_w=DOOR_W, spawn_depth=1.2)
    _roof(c, hole)
    wx1, wy0 = hole[2], hole[1]     # 欠けた角の室内側の面（x と y）

    # 栽培ベンチ（入口から奥への中央通路の両わきに 2 列 + 東の壁ぎわに 1 列、
    # 西の袖は欠けた角の壁に沿って 1 台）
    mb = c.furn("benches")
    length = (iy1 - iy0) - 2.2
    mid = (ix0 + ix1) * 0.5
    outer = (ix1 - ix0) * 0.5 - BENCH_D
    rows = [mid + d for d in (-INNER_X, INNER_X, outer)]
    for bx in rows:
        F.greenhouse_bench(mb, bx, (iy0 + iy1) * 0.5, ang=math.pi * 0.5,
                           w=length, d=BENCH_D, h=0.78, rng=rng)
    wing_w = (wx1 - ix0) - 0.25
    wing = ((ix0 + wx1) * 0.5, wy0 - BENCH_D * 0.5 - 0.02)
    F.greenhouse_bench(mb, wing[0], wing[1], ang=0.0, w=wing_w, d=BENCH_D,
                       h=0.78, rng=rng)

    # 床置きの大鉢（各列の奥の端と奥の壁のあいだ。中央通路の突き当たりは看板の下なので空ける）
    for px in rows:
        shell.planter(mb, px, iy1 - 0.6, r=0.36 + rng.random() * 0.12,
                      h=0.40, leaf_h=0.9 + rng.random() * 0.9)

    # 灌水パイプ（入って右の隅の立て管と元栓 -> 中央通路の上を奥へ + ベンチ上の散水枝）
    rx, ry = ix1 - 0.15, iy0 + 0.4
    main_x = mid + PIPE_DX
    kit.tube(mb, (rx, ry, 0.0), (rx, ry, 2.55), 0.045, "pipe_grey", seg=6)
    kit.tube(mb, (rx, ry, 2.55), (main_x, ry, 2.55), 0.045, "pipe_grey", seg=6)
    kit.tube(mb, (rx, ry, 1.10), (rx - 0.12, ry, 1.10), 0.015, "pipe_grey", seg=4)
    kit.tube(mb, (rx - 0.12, ry, 1.10), (rx - 0.15, ry, 1.10), 0.09,
             "cushion_red", seg=8)
    kit.tube(mb, (main_x, ry, 2.55), (main_x, iy1 - 0.4, 2.55), 0.045,
             "pipe_grey", seg=6)
    for k in range(7):
        ty = iy0 + 0.8 + k * ((iy1 - iy0) - 1.6) / 6.0
        kit.tube(mb, (main_x, ty, 2.55), (main_x, ty, 2.25), 0.016, "pipe_grey", seg=4)
    for bx in rows:
        kit.tube(mb, (bx, iy0 + 0.6, 2.10), (bx, iy1 - 0.6, 2.10), 0.03,
                 "pipe_grey", seg=5)
        for k in range(6):
            ty = iy0 + 1.0 + k * ((iy1 - iy0) - 2.0) / 5.0
            kit.tube(mb, (bx, ty, 2.10), (bx, ty, 1.70), 0.014, "pipe_grey",
                     seg=4)
    kit.tube(mb, (ix0 + 0.4, wing[1], 2.10), (wx1 - 0.4, wing[1], 2.10), 0.03,
             "pipe_grey", seg=5)
    for k in range(3):
        tx = ix0 + 0.7 + k * ((wx1 - ix0) - 1.4) / 2.0
        kit.tube(mb, (tx, wing[1], 2.10), (tx, wing[1], 1.70), 0.014,
                 "pipe_grey", seg=4)

    # 作業用の流し（西の袖の壁ぎわ。蛇口は壁の側）
    F.lab_sink(mb, ix0 + 0.36, iy0 + 0.9, ang=-math.pi * 0.5, w=1.0)
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

    # 照明（育成灯）。欠けた角には置かない
    pts = shell.ceiling_lights(c.wall, ix0 + 0.8, iy0 + 0.8, ix1 - 0.8, iy1 - 0.8,
                               CEIL - 0.15, sx=2.0, sy=2.4, w=0.16, l=1.4,
                               mat="light_strip", holes=[hole])
    c.lights_from(pts, CEIL - 0.2, energy=110.0)
    c.light(mid, (iy0 + iy1) * 0.5, 2.6, 260.0, 1.2)

    c.poi("plants", mid, (iy0 + iy1) * 0.5, 0.0)
    c.npc(mid, iy0 + 2.6, 0.0)
    # 奥の壁はガラスなので、ガラスの室内側の面（内壁面 + GLASS_IN）に貼る
    signs.wall(c, mb, mid, iy1 + shell.GLASS_IN, 2.20, ang=math.pi,
               ja="温室", en="Greenhouse", w=1.2, h=0.36)

    c.cam("", (mid, iy0 + 0.9, 1.60), (mid, iy1 - 1.0, 1.10), lens=16.0)
    c.cam("wing", (mid + 2.0, iy0 + 0.9, 1.60), (ix0 + 0.6, wy0 - 0.3, 0.90),
          lens=16.0)
    c.note("陸屋根のガラス温室（L 字・白い梁の格子屋根・栽培ベンチ 4 台・鉢植え・灌水パイプ）")

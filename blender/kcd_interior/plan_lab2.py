"""第2実験棟 (lab2): 玄関ホール + 吹き抜けの大型実験ホール + 計測制御室 + 準備室。

内部の写真は公開されていないので、「各学科の特殊な要件に応える実験施設」という公式の説明と
OSM の高さ 9 m から、構造実験棟の一般的な作りを推定して置く（docs/ref/interiors/lab2/spec.md）。
入口は短辺（南の妻面）の X = 0。入口の内側が玄関ホール、その奥に大きな開口でホールが続く。
"""

import math

from . import common, furn_lab2 as FL, furniture as F, kit, shell, signs

CEIL = 3.20          # 玄関ホール・制御室・準備室の天井
BAY = 7.00           # 実験ホールの天井
Z_TOP = 7.40
HALL_Y0 = 7.60       # 玄関ホールとホールの境（間仕切りの中心線）
OPEN_W = 3.0         # 境の大きな開口（供試体の搬入口）
CTRL_X1 = -2.40      # 計測制御室の東の面
CTRL_Y1 = 13.00
CTRL_DOOR = -4.20
PREP_X0 = 4.50
PREP_Y0 = 20.00
PREP_DOOR = 6.40
REACT_X1 = 4.40      # 反力壁の東端
REACT_D = 1.20


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    common.envelope(c, CEIL, "floor_concrete", door_w=3.2, z_top=Z_TOP,
                    sill=2.60, header=0.40, seg=2.8, ceil=False)
    common.entry_kit(c, CEIL, door_w=3.2)

    _ceilings(c, ix0, iy0, ix1, iy1)
    _walls(c, ix0, ix1, iy1)
    _foyer(c, ix0, iy0, ix1)
    _hall(c, ix0, ix1, iy1)
    _control(c, ix0)
    _prep(c, ix1, iy1)
    c.note("玄関ホール + 吹き抜けの実験ホール（天井クレーン・反力壁・反力フレーム・振動台）"
           " + 計測制御室 + 準備室")


def _ceilings(c, ix0, iy0, ix1, iy1):
    w = c.wall
    shell.ceiling(w, ix0, iy0, ix1, HALL_Y0, CEIL, "ceiling_grid", grid=1.5)
    shell.ceiling(w, ix0, HALL_Y0, ix1, iy1, BAY, "lab2_roof_deck")
    shell.ceiling(w, ix0, HALL_Y0 + 0.1, CTRL_X1, CTRL_Y1, CEIL, "ceiling_grid", grid=1.5)
    shell.ceiling(w, PREP_X0, PREP_Y0, ix1, iy1, CEIL, "ceiling_grid", grid=1.5)
    pts = shell.ceiling_lights(w, ix0 + 1.0, iy0 + 0.6, ix1 - 1.0, HALL_Y0 - 0.6, CEIL,
                               sx=4.0, sy=2.0, mat="light_strip", w=0.24, l=1.5)
    c.lights_from(pts, CEIL, energy=112.0, step=2)
    pts = shell.ceiling_lights(w, ix0 + 1.5, HALL_Y0 + 1.2, ix1 - 1.5, iy1 - 1.2, BAY,
                               sx=4.2, sy=4.2, mat="light_strip", w=0.40, l=1.6)
    c.lights_from(pts, BAY, energy=360.0, step=2)
    for x0, y0, x1, y1 in ((ix0 + 0.6, HALL_Y0 + 0.6, CTRL_X1 - 0.5, CTRL_Y1 - 0.5),
                           (PREP_X0 + 0.6, PREP_Y0 + 0.6, ix1 - 0.6, iy1 - 0.6)):
        pts = shell.ceiling_lights(w, x0, y0, x1, y1, CEIL, sx=2.6, sy=2.6,
                                   mat="light_strip", w=0.24, l=1.2)
        c.lights_from(pts, CEIL, energy=95.0, step=1)


def _walls(c, ix0, ix1, iy1):
    w = c.wall
    # 玄関ホールとホールの境: 搬入口（中央）と、制御室への戸
    shell.partition(w, (ix0, HALL_Y0), (ix1, HALL_Y0), 0.0, BAY, "wall_white",
                    thick=0.16,
                    gaps=[(CTRL_DOOR - 0.6 - ix0, CTRL_DOOR + 0.6 - ix0),
                          (-OPEN_W * 0.5 - ix0, OPEN_W * 0.5 - ix0)])
    # 計測制御室: ホール側（東）は腰壁の上がガラス
    shell.partition(w, (CTRL_X1, HALL_Y0), (CTRL_X1, CTRL_Y1), 0.0, CEIL, "wall_white",
                    thick=0.14, glass_top="glass_partition", glass_z=0.95)
    shell.partition(w, (ix0, CTRL_Y1), (CTRL_X1, CTRL_Y1), 0.0, CEIL, "wall_white",
                    thick=0.14)
    # 準備室
    shell.partition(w, (PREP_X0, PREP_Y0), (PREP_X0, iy1), 0.0, CEIL, "wall_white",
                    thick=0.14)
    shell.partition(w, (PREP_X0, PREP_Y0), (ix1, PREP_Y0), 0.0, CEIL, "wall_white",
                    thick=0.14,
                    gaps=[(PREP_DOOR - 0.6 - PREP_X0, PREP_DOOR + 0.6 - PREP_X0)])
    shell.door(w, CTRL_DOOR, HALL_Y0, ang=0.0, w=1.10, h=2.10, leaf="desk_white",
               glass="glass_partition")
    shell.door(w, PREP_DOOR, PREP_Y0, ang=0.0, w=1.10, h=2.10, leaf="desk_white",
               glass="glass_partition")
    # 戸と搬入口の上の壁（天井まで）
    for x0, x1, z0, z1, y in ((CTRL_DOOR - 0.6, CTRL_DOOR + 0.6, 2.15, BAY, HALL_Y0),
                              (-OPEN_W * 0.5, OPEN_W * 0.5, CEIL, BAY, HALL_Y0),
                              (PREP_DOOR - 0.6, PREP_DOOR + 0.6, 2.15, CEIL, PREP_Y0)):
        kit.box(w, x0, y - 0.07, z0, x1, y + 0.07, z1, "wall_white")
    shell.exit_sign(w, 0.0, HALL_Y0 - 0.06, 2.95, ang=math.pi)
    shell.exit_sign(w, PREP_DOOR, PREP_Y0 - 0.06, 2.85, ang=math.pi)


def _foyer(c, ix0, iy0, ix1):
    mb = c.furn("foyer")
    kit.plate(c.floor, ix0, iy0, ix1, HALL_Y0, 0.014, "floor_tile_grey")
    FL.hardhat_rack(mb, ix0 + 0.06, 5.6, ang=-math.pi * 0.5, n=8)
    F.bench(mb, ix1 - 0.45, 5.3, ang=math.pi * 0.5, w=1.8)
    F.bench(mb, ix0 + 0.45, 5.6, ang=-math.pi * 0.5, w=1.8)
    shell.notice_board(mb, ix1 - 0.10, 6.6, 1.55, ang=math.pi * 0.5, w=1.4, h=0.95,
                       sheets=6, rng=c.rng)
    shell.vending(mb, ix1 - 0.8, iy0 + 0.5, ang=0.0)
    common.window_planters(c, mb, 3.9, 6.5, iy0 + 0.6, n=2)
    signs.wall(c, mb, 2.85, HALL_Y0 - 0.10, 2.05, ang=math.pi,
               ja="大型実験ホール", en="Experiment Hall", w=1.10, h=0.34)
    signs.wall(c, mb, CTRL_DOOR + 1.35, HALL_Y0 - 0.10, 2.05, ang=math.pi,
               ja="計測制御室", en="Control Room", w=0.90, h=0.30)
    signs.wall(c, mb, ix0 + 0.06, 4.1, 2.00, ang=-math.pi * 0.5,
               ja="ヘルメット着用", en="Hard Hats Required", w=1.2, h=0.38,
               plate="lab2_crane_yellow")
    c.npc(3.8, 5.6, 0.0)
    c.cam("", (-4.5, 4.4, 1.7), (1.5, 17.0, 2.1), lens=20.0)


def _hall(c, ix0, ix1, iy1):
    mb = c.furn("hall")
    yb = iy1 - REACT_D
    kit.plate(c.floor, ix0, HALL_Y0, ix1, iy1, 0.014, "lab2_floor_strong")
    FL.anchor_floor(mb, ix0, HALL_Y0 + 0.1, ix1, iy1)
    # 屋根の梁（天井の下に東西へ渡す）
    for y in (9.0, 12.2, 15.4, 18.6, 21.8, 25.0):
        kit.box(mb, ix0, y - 0.12, BAY - 0.55, ix1, y + 0.12, BAY - 0.05, "lab2_roof_deck")
    FL.crane(mb, ix0 + 0.3, ix1 - 0.3, HALL_Y0 + 0.5, iy1 - 0.4, BAY - 0.6, 20.0, 5.0)

    # 奥: 反力壁と、その手前の門形反力フレーム + 供試体
    FL.reaction_wall(mb, ix0, REACT_X1, iy1, depth=REACT_D, h=6.0)
    FL.reaction_frame(mb, -2.8, 19.5, w=4.0, d=1.0, h=5.0)
    FL.specimen_beam(mb, -2.8, 19.5, ang=0.0)
    signs.wall(c, mb, -1.0, yb - 0.04, 3.0, ang=math.pi,
               ja="反力壁  載荷試験", en="Reaction Wall  Loading Test", w=2.6, h=0.46)

    # 東: 振動台と安全柵、置き場
    FL.shaking_table(mb, 5.5, 14.0)
    FL.safety_rail(mb, 3.2, 11.8, 3.2, 16.2)
    FL.specimen_beam(mb, 6.6, 9.0, ang=0.0)
    FL.specimen_stack(mb, 8.4, 11.2)
    FL.specimen_stack(mb, -4.6, 14.6, n=4)

    F.bench(mb, ix1 - 0.45, 17.0, ang=math.pi * 0.5, w=1.8)
    shell.fire_extinguisher(mb, ix1 - 0.5, 9.0)
    shell.fire_extinguisher(mb, ix0 + 0.5, 16.5)
    shell.clock(mb, ix1 - 0.05, 12.0, 3.0, ang=math.pi * 0.5)
    signs.hanging(c, mb, 0.0, 12.0, BAY, ang=math.pi,
                  ja="載荷試験エリア  立入注意", en="Loading Test Area", w=2.8, h=0.48,
                  drop=2.2)

    c.poi("lab", 1.2, 19.5, 0.0)
    c.poi("lab_b", 2.0, 23.6, 0.0)
    c.npc(-3.6, 23.0, 0.0)
    c.npc(7.2, 18.6, 0.0)
    c.cam("hall", (7.9, 8.9, 2.3), (-2.0, 20.0, 1.6), lens=20.0)
    c.cam("frame", (3.0, 9.6, 1.7), (-2.8, 19.5, 1.6), lens=20.0)


def _control(c, ix0):
    mb = c.furn("control")
    kit.plate(c.floor, ix0, HALL_Y0, CTRL_X1, CTRL_Y1, 0.014, "floor_carpet_grey")
    for x in (-4.9, -3.5):
        F.desk(mb, x, 12.5, ang=0.0, w=1.3, d=0.7)
        F.monitor(mb, x, 12.4, 0.72, ang=0.0)
        F.chair_min(mb, F.T(x, 11.6, 0.0, 0.0), mat="chair_blue")
    F.whiteboard(mb, ix0 + 0.12, 9.6, 1.25, ang=-math.pi * 0.5, w=1.5, h=0.9)
    shell.fire_extinguisher(mb, CTRL_X1 - 0.4, 12.5)
    c.npc(-5.0, 9.4, 0.0)
    c.cam("control", (-5.2, 8.4, 1.6), (-1.0, 14.0, 1.5), lens=20.0)


def _prep(c, ix1, iy1):
    mb = c.furn("prep")
    kit.plate(c.floor, PREP_X0, PREP_Y0, ix1, iy1, 0.014, "floor_tile_grey")
    F.lab_bench(mb, 6.9, 24.6, ang=0.0, w=2.4, d=1.2, h=0.90, shelf=True, rng=c.rng)
    F.chem_cabinet(mb, ix1 - 0.25, 21.5, ang=math.pi * 0.5, w=1.2, h=1.85, rng=c.rng)
    F.chem_cabinet(mb, ix1 - 0.25, 23.0, ang=math.pi * 0.5, w=1.2, h=1.85, rng=c.rng)
    F.lab_sink(mb, PREP_X0 + 0.43, 22.8, ang=-math.pi * 0.5, w=1.3)
    F.gas_cylinders(mb, 5.3, iy1 - 0.5, ang=0.0, n=3)
    F.whiteboard(mb, PREP_X0 + 0.10, 24.6, 1.25, ang=-math.pi * 0.5, w=1.4, h=0.9)
    signs.wall(c, mb, PREP_DOOR + 0.95, PREP_Y0 - 0.10, 2.05, ang=math.pi,
               ja="準備室", en="Prep Room", w=0.70, h=0.28)
    c.poi("prep", 6.9, 22.4, 0.0)
    c.npc(7.8, 21.0, 0.0)
    c.cam("prep", (5.0, 20.8, 1.8), (8.2, 25.0, 1.0), lens=20.0)

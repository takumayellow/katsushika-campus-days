"""第1実験棟 (lab1): 1F の機械工作工場1・NMR・田村研究室・小林研究室。

入口が長辺にあるので、入口の面に沿って廊下を通し、その奥の左に機械工作工場1（L 字）、
右に研究室を 3 室並べる。写真の根拠と推定の区別は docs/ref/interiors/lab1/spec.md。
"""

import math

from . import common, furn_lab1 as FL, furniture as F, kit, shell, signs

CEIL = 3.80
Z_TOP = 4.50
COR_Y1 = 6.30        # 廊下の奥の面（間仕切りの中心線）
X_SPLIT = 2.00       # 工場の主棟と研究室の列の境
Y_ROOMS = 16.00      # 研究室の奥の面 = 工場の離れとの境
X_DIV = (7.0, 13.0)  # NMR | 田村研究室 | 小林研究室 の境
SHOP_GAP = (-4.0, -0.5)    # 工場の大きな開口（入口の正面）
SHOP_DOOR = -13.0          # 工場のもう一つの入口
ROOM_DOORS = (4.5, 10.0, 15.0)
ANNEX_GAP = (19.0, 23.0)   # 工場の主棟と離れの間の開口（y）
CRIB = (14.0, 24.0)        # 工具室の x0, y0（右奥の角）


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    common.envelope(c, CEIL, "floor_resin_grey", door_w=3.2, z_top=Z_TOP,
                    sill=1.05, header=0.40, seg=2.8, ceil=False)
    common.entry_kit(c, CEIL, door_w=3.2)

    _ceilings(c, ix0, iy0, ix1, iy1)
    _walls(c, ix0, iy0, ix1, iy1)
    _corridor(c, ix0, iy0, ix1)
    _shop(c, ix0, iy1)
    _annex(c, iy1, ix1)
    _crib(c, ix1, iy1)
    _nmr(c)
    _tamura(c)
    _kobayashi(c)
    c.note("廊下 + 機械工作工場1（L 字・旋盤とフライス盤）+ NMR + 田村研 + 小林研 + 工具室")


def _ceilings(c, ix0, iy0, ix1, iy1):
    """工場は躯体の天井（ダクトとケーブルラック）、廊下と研究室は白い格子の天井。"""
    w = c.wall
    shell.ceiling(w, ix0, iy0, ix1, COR_Y1, CEIL, "ceiling_grid", grid=1.5)
    shell.ceiling(w, X_SPLIT, COR_Y1, ix1, Y_ROOMS, CEIL, "ceiling_white", grid=1.5)
    shell.ceiling(w, ix0, COR_Y1, X_SPLIT, iy1, CEIL, "lab1_ceiling_slab")
    shell.ceiling(w, X_SPLIT, Y_ROOMS, ix1, iy1, CEIL, "lab1_ceiling_slab")
    pts = shell.ceiling_lights(w, ix0 + 1.0, iy0 + 0.5, ix1 - 1.0, COR_Y1 - 0.5, CEIL,
                               sx=4.0, sy=2.0, mat="light_strip", w=0.24, l=1.5)
    c.lights_from(pts, CEIL, energy=112.0, step=2)
    for x0, y0, x1, y1 in ((ix0 + 1.0, COR_Y1 + 1.0, X_SPLIT - 1.0, iy1 - 1.0),
                           (X_SPLIT + 1.0, Y_ROOMS + 1.0, ix1 - 1.0, iy1 - 1.0)):
        pts = shell.ceiling_lights(w, x0, y0, x1, y1, CEIL, sx=3.4, sy=3.8,
                                   mat="light_strip", w=0.14, l=1.3)
        c.lights_from(pts, CEIL, energy=120.0, step=2)
    pts = shell.ceiling_lights(w, X_SPLIT + 0.8, COR_Y1 + 0.8, ix1 - 0.8, Y_ROOMS - 0.8,
                               CEIL, sx=3.0, sy=4.0, mat="light_strip", w=0.24, l=1.5)
    c.lights_from(pts, CEIL, energy=105.0, step=1)


def _walls(c, ix0, iy0, ix1, iy1):
    w = c.wall
    # 廊下の奥の面: 工場側は壁、研究室側は上がガラス
    shell.partition(w, (ix0, COR_Y1), (X_SPLIT, COR_Y1), 0.0, CEIL, "wall_white",
                    thick=0.16, gaps=[(SHOP_GAP[0] - ix0, SHOP_GAP[1] - ix0),
                                      (SHOP_DOOR - 0.6 - ix0, SHOP_DOOR + 0.6 - ix0)])
    shell.partition(w, (X_SPLIT, COR_Y1), (ix1, COR_Y1), 0.0, CEIL, "wall_white",
                    thick=0.16, glass_top="glass_partition", glass_z=2.10,
                    gaps=[(d - 0.6 - X_SPLIT, d + 0.6 - X_SPLIT) for d in ROOM_DOORS])
    # 工場と研究室・離れの境（離れへは大きな開口）
    shell.partition(w, (X_SPLIT, COR_Y1), (X_SPLIT, iy1), 0.0, CEIL, "wall_white",
                    thick=0.16, gaps=[(ANNEX_GAP[0] - COR_Y1, ANNEX_GAP[1] - COR_Y1)])
    shell.partition(w, (X_SPLIT, Y_ROOMS), (ix1, Y_ROOMS), 0.0, CEIL, "wall_white",
                    thick=0.16)
    for x in X_DIV:
        shell.partition(w, (x, COR_Y1), (x, Y_ROOMS), 0.0, CEIL, "wall_white",
                        thick=0.14)
    # 工具室（右奥の角）
    cx, cy = CRIB
    shell.partition(w, (cx, cy), (cx, iy1), 0.0, CEIL, "wall_white", thick=0.14,
                    gaps=[(1.0, 2.2)])
    shell.partition(w, (cx, cy), (ix1, cy), 0.0, CEIL, "wall_white", thick=0.14)


def _corridor(c, ix0, iy0, ix1):
    cor = c.furn("corridor")
    w = c.wall
    for d in ROOM_DOORS:
        shell.door(w, d, COR_Y1, ang=0.0, w=1.10, h=2.10, leaf="desk_white",
                   glass="glass_partition")
        kit.box(w, d - 0.6, COR_Y1 - 0.08, 2.15, d + 0.6, COR_Y1 + 0.08, CEIL, "wall_white")
    shell.door(w, SHOP_DOOR, COR_Y1, ang=0.0, w=1.10, h=2.10, leaf="lab1_shutter")
    kit.box(w, SHOP_DOOR - 0.6, COR_Y1 - 0.08, 2.15, SHOP_DOOR + 0.6, COR_Y1 + 0.08, CEIL,
            "wall_white")
    FL.roll_up_shutter_box(cor, SHOP_GAP[0], SHOP_GAP[1], COR_Y1, CEIL - 0.45)
    kit.box(w, SHOP_GAP[0], COR_Y1 - 0.08, 2.55, SHOP_GAP[1], COR_Y1 + 0.08, CEIL,
            "wall_white")
    # 室名札（廊下側 = -Y を向く）
    mid = (SHOP_GAP[0] + SHOP_GAP[1]) * 0.5
    signs.wall(c, cor, mid, COR_Y1 - 0.10, CEIL - 0.9, ang=math.pi,
               ja="機械工作工場1", en="Machine Shop 1", w=2.4, h=0.46)
    signs.wall(c, cor, SHOP_DOOR + 0.95, COR_Y1 - 0.10, 2.05, ang=math.pi,
               ja="工場 入口", en="Shop Entrance", w=0.90, h=0.30)
    for d, dx, ja, en in zip(ROOM_DOORS, (0.95, 0.95, -1.15),
                             ("NMR室", "田村研究室", "小林研究室"),
                             ("NMR Room", "Tamura Lab", "Kobayashi Lab")):
        signs.wall(c, cor, d + dx, COR_Y1 - 0.10, 2.05, ang=math.pi, ja=ja,
                   en=en, w=0.90, h=0.30)
        shell.exit_sign(w, d, COR_Y1 - 0.06, 2.85, ang=math.pi)
    # 工場の開口の脇の掲示板
    shell.notice_board(cor, SHOP_GAP[1] + 1.3, COR_Y1 - 0.10, 1.58, ang=math.pi,
                       w=1.8, h=1.05, sheets=8, rng=c.rng)
    # 階段・エレベーター（2〜4F へ）
    shell.elevator_bank(cor, ix1 - 1.4, COR_Y1 - 0.22, count=1, ang=0.0, w=0.55, h=2.10)
    signs.hanging(c, cor, ix1 - 3.2, iy0 + 1.5, CEIL, ang=0.0,
                  ja="階段・EV  2F〜4F", en="Stairs / EV  2F-4F", w=2.6, h=0.42,
                  drop=0.3)
    F.locker_bank(cor, ix0 + 1.0, ix0 + 7.0, iy0 + 0.2, ang=0.0, h=1.80)
    shell.trash_bins(cor, ix1 - 9.0, iy0 + 0.6, ang=0.0, n=3)
    shell.vending(cor, ix1 - 1.2, iy0 + 0.5, ang=0.0)
    common.window_planters(c, cor, ix0 + 9.0, ix0 + 15.0, iy0 + 0.6, n=2)
    F.bench(cor, ix0 + 12.5, COR_Y1 - 0.55, ang=0.0, w=1.8)
    shell.exit_sign(w, 0.0, iy0 + 0.05, CEIL - 0.05, ang=math.pi)
    c.cam("", (ix0 + 1.0, iy0 + 1.4, 1.62), (ix1 - 5.0, iy0 + 1.6, 1.42), lens=24.0)


def _shop(c, ix0, iy1):
    """機械工作工場1の主棟（x ix0..X_SPLIT, y COR_Y1..iy1）。"""
    mb = c.furn("shop")
    rng = c.rng
    kit.plate(c.floor, ix0, COR_Y1, X_SPLIT, iy1, 0.014, "lab1_floor_epoxy_green")
    FL.ceiling_ducts(mb, ix0 + 0.5, X_SPLIT - 0.5, (10.0, 17.0, 24.0), CEIL)
    for x in (-13.0, -6.0):
        for y in (12.0, 21.0):
            FL.cassette_ac(mb, x, y, CEIL)

    # 奥の壁ぎわ: フライス盤、ボール盤、平面研削盤、ワイヤ放電加工機（正面は -Y）
    yb = iy1 - 1.0
    for x in (-16.0, -13.2, -10.4):
        FL.milling_machine(mb, x, yb, ang=math.pi)
    for x in (-7.6, -6.2):
        FL.drill_press(mb, x, yb + 0.1, ang=math.pi)
    FL.surface_grinder(mb, -3.8, yb, ang=math.pi)
    FL.wire_edm(mb, -0.2, yb - 0.1, ang=math.pi)
    FL.lane(mb, -17.4, yb - 1.8, 0.9, yb + 0.55)
    for x in (-14.8, -11.9, -6.9):
        FL.chip_bin(mb, x, yb - 1.1)

    # 西の壁ぎわ: 旋盤（正面は +X）
    xw = ix0 + 0.9
    for y in (11.0, 14.2, 17.4):
        FL.lathe(mb, xw, y, ang=-math.pi * 0.5)
    FL.lane(mb, ix0 + 0.1, 9.0, ix0 + 2.0, 19.2)
    FL.tool_chest(mb, ix0 + 0.6, 21.2, ang=-math.pi * 0.5)
    FL.tool_chest(mb, ix0 + 0.6, 22.2, ang=-math.pi * 0.5)
    FL.stock_rack(mb, ix0 + 0.5, 24.2, ang=-math.pi * 0.5, w=2.2, rng=rng)

    # 中央: 作業台の島（背中合わせ 2 台 × 2 組）。正面 = 台の外側
    for bx in (-11.0, -6.0):
        by = 9.6
        FL.workbench_vise(mb, bx, by - 0.45, ang=0.0)
        FL.workbench_vise(mb, bx, by + 0.45, ang=math.pi)
        FL.tech_stool(mb, bx - 0.5, by - 1.35, ang=0.0)
        FL.tech_stool(mb, bx + 0.6, by + 1.35, ang=math.pi)
        FL.lane(mb, bx - 1.5, by - 1.8, bx + 1.5, by + 1.8)

    # 東の壁ぎわ: 材料棚（正面は -X）
    for y in (8.4, 10.8, 13.2):
        FL.stock_rack(mb, X_SPLIT - 0.45, y, ang=math.pi * 0.5, w=2.0, rng=rng)

    shell.fire_extinguisher(mb, ix0 + 0.5, COR_Y1 + 0.7)
    shell.fire_extinguisher(mb, X_SPLIT - 0.5, iy1 - 4.5)
    shell.clock(mb, ix0 + 6.0, iy1 - 0.2, 2.8, ang=math.pi)
    signs.wall(c, mb, X_SPLIT - 0.10, 17.5, 2.6, ang=math.pi * 0.5,
               ja="安全第一  保護メガネ着用", en="Safety First  Wear Eye Protection",
               w=3.0, h=0.50, plate="lab1_lane_yellow")
    signs.hanging(c, mb, -9.0, iy1 - 2.8, CEIL, ang=math.pi,
                  ja="ワイヤ放電加工機 / フライス盤", en="Wire EDM  /  Milling",
                  w=3.6, h=0.46, drop=1.0)

    # POI と人: 作業台の島と奥の機械の間の通路
    c.poi("lab", -8.5, 15.8, 0.0)
    c.npc(-11.6, 15.8, 0.0)
    c.npc(-3.0, 20.0, 0.0)
    c.cam("shop", (-1.6, COR_Y1 + 1.1, 1.75), (-12.0, iy1 - 3.0, 1.0), lens=20.0)


def _annex(c, iy1, ix1):
    """工場の離れ（x X_SPLIT..ix1, y Y_ROOMS..iy1）。旋盤とマシニングセンタ。"""
    mb = c.furn("annex")
    rng = c.rng
    kit.plate(c.floor, X_SPLIT, Y_ROOMS, ix1, iy1, 0.014, "lab1_floor_epoxy_green")
    FL.ceiling_ducts(mb, X_SPLIT + 0.5, ix1 - 0.5, (20.5, 25.5), CEIL)
    FL.cassette_ac(mb, 9.0, 19.5, CEIL)
    FL.cassette_ac(mb, 9.0, 25.0, CEIL)
    # 南の壁（研究室の裏）ぎわ: 旋盤 3 台（正面は +Y）
    for x in (5.0, 8.5, 12.0):
        FL.lathe(mb, x, Y_ROOMS + 0.9, ang=0.0)
    FL.lane(mb, X_SPLIT + 1.0, Y_ROOMS + 0.2, 14.2, Y_ROOMS + 2.1)
    # マシニングセンタ（大きな箱形）と作業台
    FL.wire_edm(mb, 6.0, iy1 - 1.2, ang=math.pi)
    FL.milling_machine(mb, 9.6, iy1 - 1.0, ang=math.pi)
    FL.milling_machine(mb, 11.6, iy1 - 1.0, ang=math.pi)
    FL.lane(mb, 3.6, iy1 - 2.6, 12.6, iy1 - 0.25)
    FL.workbench_vise(mb, 17.2, 18.3, ang=0.0, w=2.0, vises=1)
    FL.stock_rack(mb, ix1 - 0.5, 17.4, ang=math.pi * 0.5, w=1.8, rng=rng)
    FL.chip_bin(mb, 6.4, 18.8)
    FL.chip_bin(mb, 10.6, 18.8)
    c.npc(8.0, 21.8, 0.0)
    c.cam("annex", (3.0, 20.8, 1.7), (13.0, iy1 - 2.0, 1.0), lens=20.0)


def _crib(c, ix1, iy1):
    """工具室（右奥の角）。小物と測定具を貸し出す場所。POI prep。"""
    mb = c.furn("crib")
    cx, cy = CRIB
    kit.plate(c.floor, cx, cy, ix1, iy1, 0.014, "floor_tile_grey")
    for i in range(3):
        FL.stock_rack(mb, cx + 0.6 + i * 1.5, iy1 - 0.45, ang=math.pi, w=1.3, d=0.5,
                      rng=c.rng)
    FL.tool_chest(mb, ix1 - 0.5, cy + 2.2, ang=math.pi * 0.5)
    FL.tool_chest(mb, ix1 - 0.5, cy + 3.2, ang=math.pi * 0.5)
    F.counter(mb, cx + 0.5, cy + 1.5, cx + 0.5 + 0.7, cy + 3.4)
    signs.wall(c, mb, cx + 0.10, cy + 3.4, 2.1, ang=-math.pi * 0.5,
               ja="工具室", en="Tool Crib", w=0.9, h=0.30)
    c.poi("prep", cx + 2.4, cy + 2.0, 0.0)


def _nmr(c):
    mb = c.furn("nmr")
    x0, x1 = X_SPLIT, X_DIV[0]
    kit.plate(c.floor, x0, COR_Y1, x1, Y_ROOMS, 0.014, "floor_tile_grey")
    cx = (x0 + x1) * 0.5 + 0.2
    FL.nmr_magnet(mb, cx, 12.2)
    FL.lane(mb, cx - 1.3, 10.9, cx + 1.3, 13.5, w=0.07)
    F.desk(mb, x0 + 1.4, 8.0, ang=0.0, w=1.6, d=0.7)
    F.monitor(mb, x0 + 1.4, 8.2, 0.72, ang=0.0)
    F.chair_min(mb, F.T(x0 + 1.4, 7.1, 0.0, 0.0), mat="chair_blue")
    F.gas_cylinders(mb, x1 - 0.7, 15.1, ang=0.0, n=3)
    F.whiteboard(mb, x0 + 0.12, 10.4, 1.05, ang=-math.pi * 0.5, w=1.6, h=1.0)
    signs.wall(c, mb, x1 - 0.10, 9.0, 1.75, ang=math.pi * 0.5,
               ja="強磁場注意  ペースメーカ", en="Strong Magnetic Field", w=1.5, h=0.46,
               plate="lab1_lane_yellow")
    c.npc(x0 + 1.2, 11.2, 0.0)
    c.cam("nmr", (x1 - 0.9, 7.3, 1.7), (x0 + 1.5, 13.5, 1.2), lens=20.0)


def _tamura(c):
    mb = c.furn("tamura")
    x0, x1 = X_DIV[0], X_DIV[1]
    cx = (x0 + x1) * 0.5
    kit.plate(c.floor, x0, COR_Y1, x1, Y_ROOMS, 0.014, "floor_resin_grey")
    FL.test_rig(mb, cx - 1.2, 11.0, ang=0.0)
    FL.test_rig(mb, cx + 1.3, 12.2, ang=math.pi * 0.5)
    for k in range(3):
        px = x0 + 1.1 + k * 1.7
        F.desk(mb, px, Y_ROOMS - 0.8, ang=math.pi, w=1.4, d=0.7)
        F.chair_min(mb, F.T(px, Y_ROOMS - 1.65, 0.0, 0.0), mat="chair_blue")
        F.monitor(mb, px, Y_ROOMS - 0.95, 0.72, ang=0.0)
    F.whiteboard(mb, x0 + 0.12, 9.3, 1.05, ang=-math.pi * 0.5, w=2.2, h=1.1)
    F.bookshelf(mb, x1 - 0.5, 8.4, ang=math.pi * 0.5, w=1.2, h=1.9, shelves=5,
                rng=c.rng)
    shell.fire_extinguisher(mb, x1 - 0.5, 7.2)
    c.npc(cx, 8.8, 0.0)


def _kobayashi(c):
    mb = c.furn("kobayashi")
    x0, x1 = X_DIV[1], 19.0
    cx = (x0 + x1) * 0.5
    kit.plate(c.floor, x0, COR_Y1, x1, Y_ROOMS, 0.014, "floor_resin_grey")
    F.lab_bench(mb, cx, 10.0, ang=0.0, w=3.2, d=1.4, h=0.90, shelf=True, rng=c.rng)
    for k in range(3):
        sx = cx - 1.0 + k * 1.0
        FL.tech_stool(mb, sx, 8.85, ang=0.0)
    # ドラフトチャンバー 2 基は奥の壁（正面は -Y）。lab_b はその手前
    F.fume_hood(mb, x0 + 1.4, Y_ROOMS - 0.7, ang=math.pi, w=1.6, d=0.9)
    F.fume_hood(mb, x0 + 3.2, Y_ROOMS - 0.7, ang=math.pi, w=1.6, d=0.9)
    F.lab_sink(mb, x1 - 0.5, 12.8, ang=math.pi * 0.5, w=1.4)
    F.chem_cabinet(mb, x1 - 0.5, 14.4, ang=math.pi * 0.5, w=1.2, h=1.85, rng=c.rng)
    F.gas_cylinders(mb, x1 - 0.7, 7.5, ang=0.0, n=3)
    F.whiteboard(mb, x0 + 0.12, 8.8, 1.05, ang=-math.pi * 0.5, w=1.8, h=1.0)
    shell.fire_extinguisher(mb, x0 + 0.5, 7.2)
    c.poi("lab_b", x0 + 2.3, 13.7, 0.0)
    c.npc(x1 - 1.4, 8.4, 0.0)
    c.cam("kobayashi", (x1 - 0.8, 7.2, 1.8), (x0 + 2.2, 14.2, 1.2), lens=20.0)

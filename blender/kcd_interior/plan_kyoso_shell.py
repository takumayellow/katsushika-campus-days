"""共創棟の躯体: 床・外周・1F の天井・柱・コアと EV・公園側の帯の壁・2F の床と壁・吹き抜け・鉄骨階段。

座標はすべて建物のローカル（plan_kyoso_dims の線）。エスカレーターは plan_kyoso_esc が組む。
"""

import math

from . import furn_kyoso as FK
from . import kit, shell
from . import plan_kyoso_dims as D

WALL = "wall_white"
GREY = "wall_grey"
CARPET = "kyoso_floor_carpet_grey"
STEEL = "kyoso_steel_black"
GLASS = "glass_clear"
COLUMN = "kyoso_column_pale"

# 1F の木ルーバーを切る所（エスカレーター・階段・吹き抜けの開口と東のコアの上）
HOLES_1F = (D.ESC_VOID, D.STAIR_HOLE, D.ATRIUM, D.CORE_E)


# ---- 外周 -------------------------------------------------------------- #
def _edge(mb, p, q, ranges, gaps_of=None):
    """p -> q の辺を ranges（(s0, s1, bands, solid)）で分けて外周壁にする。

    s0, s1 は辺の始点からの距離。bands は (z0, z1, sill, header) の列。
    """
    dx, dy = q[0] - p[0], q[1] - p[1]
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length
    for s0, s1, bands, solid in ranges:
        a = (p[0] + ux * s0, p[1] + uy * s0)
        b = (p[0] + ux * s1, p[1] + uy * s1)
        gaps = gaps_of(s0, s1) if gaps_of else ()
        for z0, z1, sill, header in bands:
            shell.outer_wall(mb, a, b, z0, z1, wall=WALL, glass=None if solid else GLASS,
                             sill=sill, header=header, seg=1.8, gaps=gaps,
                             gap_top=3.2 if gaps else None)


ONE = ((0.0, D.Z_SLAB, 0.95, 0.45),)
TOP = ONE + ((D.Z_SLAB, D.Z_SLAB3, 0.4, 0.6), (D.Z_SLAB3, D.Z_TOP, 0.0, 0.2))
# 屋根側へ立ち上げる壁（壁・スラブの天端に立っても外へ抜けない）
LIFT = ((D.Z_SLAB, D.Z_SLAB + 1.8, 0.0, 0.0),)
RISE = ((D.Z_TOP, D.Z_TOP + 1.2, 0.0, 0.0),)


def envelope(c):
    """床スラブ + 外周 + 入口のガラススクリーン。戻り値は入口の開口。

    モール側は全面ガラス。公園側は店とラーニングスクエア2 の前がガラスで、吹き抜けの前は
    3F の上までガラスを立ち上げる。部屋・階段の前と東西の面は窓なし。
    """
    s = c.spec
    x0, x1, ya, yb = s.x0, s.x1, s.y_face, s.y_back
    mb = c.wall
    shell.floor(c.floor, x0, ya, x1, yb, 0.0, "floor_tile_grey", thickness=0.30,
                side_mat="concrete_grey")
    d0, d1 = -D.DOOR_W * 0.5, D.DOOR_W * 0.5

    def door_gaps(s0, s1):
        g0, g1 = d0 - x0, d1 - x0
        return [(g0 - s0, g1 - s0)] if s0 <= g0 and g1 <= s1 else ()

    # モール側（東 -> 西）
    _edge(mb, (x0, ya), (x1, ya), ((0.0, x1 - x0, ONE, False),), door_gaps)
    _edge(mb, (x0, ya), (x1, ya), ((0.0, x1 - x0, LIFT, True),))
    shell.glass_entrance(mb, d0, d1, 0.0, 3.2, ya + shell.WALL * 0.5)

    # 公園側（西 -> 東）。距離は x1 から
    def pk(x):
        return x1 - x

    a0, a1 = pk(D.ATRIUM[2]), pk(D.ATRIUM[0])
    _edge(mb, (x1, yb), (x0, yb), (
        (0.0, pk(D.SB[2]), ONE, True),
        (pk(D.SB[2]), a0, ONE, False),
        (a0, a1, TOP, False),
        (a1, pk(D.FM[2]), ONE, True),
        (pk(D.FM[2]), pk(D.FM[0]), ONE, False),
        (pk(D.FM[0]), x1 - x0, ONE, True)))
    _edge(mb, (x1, yb), (x0, yb), (
        (0.0, a0, LIFT, True), (a1, x1 - x0, LIFT, True),
        (a0 - 0.4, a1 + 0.4, RISE, True)))
    # 東と西の面
    for p, q in (((x0, yb), (x0, ya)), ((x1, ya), (x1, yb))):
        _edge(mb, p, q, ((0.0, yb - ya, ONE + LIFT, True),))
    c.door_gap = (d0, d1)
    return d0, d1


# ---- 1F の天井 ---------------------------------------------------------- #
def _louvers(mb, rect, holes=()):
    shell.ceiling(mb, rect[0], rect[1], rect[2], rect[3], D.Z_VOID, "kyoso_ceiling_void",
                  holes=holes)
    FK.louvers(mb, rect[0], rect[1], rect[2], rect[3], D.Z_CEIL, holes=holes)


def _white(mb, rect, strips=()):
    shell.ceiling(mb, rect[0], rect[1], rect[2], rect[3], D.Z_CEIL, "ceiling_white",
                  grid=1.8)
    for y in strips:
        shell.light_strip(mb, rect[0] + 0.6, y, rect[2] - 0.6, y, D.Z_CEIL, "light_strip",
                          w=0.16)


def ceilings_1f(c):
    """エントランスホール・中央の帯・スタバは木ルーバー、スタジオ・西のコアの前・コンビニは白い天井。"""
    mb = c.wall
    _louvers(mb, D.LOBBY, holes=(D.ESC_VOID,))
    _louvers(mb, (D.X_IN0, D.Y_MALL, D.CORE_W[0], D.Y_PARK0), holes=HOLES_1F)
    _louvers(mb, D.SB)
    _white(mb, D.STUDIO, strips=(7.0, 11.6))
    _white(mb, (D.CORE_W[0], D.Y_MALL, D.X_IN1, D.Y_PARK0), strips=(29.5,))
    _white(mb, D.FM, strips=(32.2, 34.0, 35.8, 37.6))
    # 天井の高さの違う所の境（ルーバーの上の黒い面が隣の白い天井の上から見えない）
    for x0, x1 in ((D.FM[0], D.FM[2]), (D.CORE_W[0], D.X_IN1)):
        kit.box(mb, x0, D.Y_PARK0 - 0.15, D.Z_CEIL, x1, D.Y_PARK0, D.Z_SLAB, WALL)
    kit.box(mb, D.STUDIO[0] - 0.15, D.Y_IN0, D.Z_CEIL, D.STUDIO[0], D.Y_MALL, D.Z_SLAB, WALL)
    kit.box(mb, D.CORE_W[0] - 0.15, D.Y_MALL, D.Z_CEIL, D.CORE_W[0], D.Y_PARK0, D.Z_SLAB, WALL)


# ---- 柱 ---------------------------------------------------------------- #
def columns(c):
    """1F から 2F の天井まで通る淡いグレーの角柱（足元は濃い色）。"""
    mb = c.wall
    h = D.COLUMN_SIZE * 0.5
    for x, y in D.COLUMNS:
        kit.box(mb, x - h, y - h, 0.0, x + h, y + h, D.Z_CEIL2, COLUMN)
        kit.box(mb, x - h - 0.01, y - h - 0.01, 0.0, x + h + 0.01, y + h + 0.01, 0.12,
                "kyoso_column_base")


# ---- コア -------------------------------------------------------------- #
def _door(mb, x, y, ang, w=1.0, h=2.1):
    """閉じた鉄の扉（階段・トイレ・機械室）。(x, y) は壁の面、ang の正面へ出す。"""
    t = kit.T(x, y, 0.0, ang)
    t.box(mb, -w * 0.5, 0.0, 0.0, w * 0.5, 0.06, h, "kyoso_wall_bluegrey")
    t.box(mb, -w * 0.5 - 0.06, 0.0, 0.0, w * 0.5 + 0.06, 0.04, h + 0.06, "metal_gray")
    t.box(mb, w * 0.3, 0.06, 0.95, w * 0.36, 0.1, 1.15, "stainless")


def core_east(c, mb):
    """東のコア: 南の面に EV の乗り場の口、乗り場の東の面に EV 2 基（西を向く）、南の面にトイレの扉。"""
    x0, y0, x1, y1 = D.CORE_E
    e0, _, e1, e3 = D.EV_E
    z = D.Z_CEIL2
    t = 0.3
    kit.box(mb, x0, y0, 0.0, e0, y0 + t, z, "kyoso_wc_taupe")             # 南（EV の東）
    kit.box(mb, e1, y0, 0.0, x1, y0 + t, z, "kyoso_wc_taupe")             # 南（EV の西）
    kit.box(mb, e0, y0, 3.0, e1, y0 + t, z, "kyoso_wc_taupe")             # 乗り場の口の上
    kit.box(mb, x1 - t, y0, 0.0, x1, y1, z, WALL)                          # 西（階段の側）
    kit.box(mb, x0, y1 - t, 0.0, x1, y1, z, WALL)                          # 北
    kit.box(mb, x0, y0, 0.0, x0 + t, y1, z, WALL)                          # 東
    kit.box(mb, e0 - t, y0 + t, 0.0, e0, e3, D.Z_CEIL, "kyoso_wc_taupe")  # 乗り場の東
    kit.box(mb, e1, y0 + t, 0.0, e1 + t, e3, D.Z_CEIL, "kyoso_wc_taupe")  # 乗り場の西
    kit.box(mb, e0, e3, 0.0, e1, e3 + t, D.Z_CEIL, "kyoso_wc_taupe")      # 乗り場の奥
    shell.ceiling(mb, e0, y0 + t, e1, e3, 3.0, "ceiling_white")
    for y in (20.6, 23.0):
        shell.elevator_bank(mb, e0, y, count=1, ang=D.FACE_EAST, w=0.65, h=2.3)
    for x in (-15.2, -12.6):
        _door(mb, x, y0, D.FACE_MALL)


def core_west(c, mb):
    """西のコア: 東の面に自販機、北の面に EV の乗り場の口と西の端の EV 1 基。"""
    x0, y0, x1, y1 = D.CORE_W
    e0, e1, e2, _ = D.EV_W
    t = 0.3
    kit.box(mb, x0, y0, 0.0, x0 + t, y1, D.Z_CEIL, WALL)                    # 東（自販機の背）
    kit.box(mb, x0, y0, 0.0, x1, y0 + t, D.Z_CEIL, WALL)                    # 南（スタジオの奥）
    kit.box(mb, x0, y1 - t, 0.0, e0, y1, D.Z_CEIL, "kyoso_wc_taupe")        # 北（乗り場の東）
    kit.box(mb, e2, y1 - t, 0.0, x1, y1, D.Z_CEIL, "kyoso_wc_taupe")        # 北（乗り場の西）
    kit.box(mb, e0 - t, e1, 0.0, e0, y1 - t, D.Z_CEIL, "kyoso_wc_taupe")    # 乗り場の東
    kit.box(mb, e2, e1, 0.0, e2 + t, y1 - t, D.Z_CEIL, "kyoso_wc_taupe")    # 乗り場の西
    kit.box(mb, e0, e1 - t, 0.0, e2, e1, D.Z_CEIL, "kyoso_wc_taupe")        # 乗り場の奥
    for y in (24.0, 26.4):
        shell.elevator_bank(mb, e2, y, count=1, ang=D.FACE_WEST, w=0.65, h=2.3)
    sx = (D.EV_SINGLE_X[0] + D.EV_SINGLE_X[1]) * 0.5
    shell.elevator_bank(mb, sx, y1, count=1, ang=D.FACE_MALL, w=0.65, h=2.3)
    _door(mb, 22.0, y1, D.FACE_PARK)
    for y in D.VENDING_Y:
        shell.vending(mb, D.VENDING_X, y, ang=D.FACE_EAST)


# ---- 公園側の帯 ---------------------------------------------------------- #
def park_band(c, mb):
    """部屋・階段の閉じた壁と扉、コンビニのガラスの正面。ラーニングスクエア2 とスタバの前は開ける。"""
    y = D.Y_PARK0
    t = 0.2
    for x0, x1 in ((D.X_IN0, D.FM[0]), (D.STAIRWELL_E[0], D.STAIRWELL_E[2]),
                   (D.STAIRWELL_W[0], D.X_IN1)):
        kit.box(mb, x0, y, 0.0, x1, y + t, D.Z_SLAB, WALL)
    for x in (D.FM[0], D.STAIRWELL_E[0], D.STAIRWELL_E[2], D.STAIRWELL_W[0],
              D.STAIRWELL_W[2]):
        kit.box(mb, x - t * 0.5, y, 0.0, x + t * 0.5, D.Y_PARK, D.Z_SLAB, WALL)
    _door(mb, -25.6, y, D.FACE_MALL, w=1.6)
    _door(mb, D.STAIRWELL_E[0] + 0.8, y, D.FACE_MALL)
    _door(mb, D.STAIRWELL_W[0] + 0.8, y, D.FACE_MALL)
    _door(mb, 30.0, y, D.FACE_MALL, w=1.6)
    shell.exit_sign(mb, D.STAIRWELL_E[0] + 0.8, y - 0.06, 2.45, ang=D.FACE_MALL)
    shell.exit_sign(mb, D.STAIRWELL_W[0] + 0.8, y - 0.06, 2.45, ang=D.FACE_MALL)
    # コンビニの正面（自動扉の上まで）と白い帯
    x0, x1 = D.FM[0], D.FM[2]
    FK.glass_wall(mb, (x0, y), (x1, y), 3.4,
                  gaps=((D.FM_DOOR[0] - x0, D.FM_DOOR[1] - x0),), door_top=3.4)
    kit.box(mb, x0, y - 0.15, 3.4, x1, y, D.Z_CEIL, "kyoso_fm_white")


# ---- 2F ---------------------------------------------------------------- #
def slabs_2f(c, mb):
    """2F の床（スラブ A・B、階段の開口を抜く）と天井・まわりの壁・階段の開口の手すり。"""
    z = D.Z2
    sx0, sy0, sx1, sy1 = D.SLAB_B
    hx0, hy0, hx1, hy1 = D.STAIR_HOLE
    for rect in (D.SLAB_A, (sx0, sy0, sx1, hy0), (hx1, hy0, sx1, sy1),
                 (sx0, hy1, hx1, sy1)):
        shell.floor(mb, rect[0], rect[1], rect[2], rect[3], z, CARPET, thickness=0.40,
                    side_mat=WALL)
    shell.ceiling(mb, D.CORE_E[0], D.SLAB_A[1] - 0.3, D.CORE_W[0], D.SLAB_B[3],
                  D.Z_CEIL2, "ceiling_white", grid=1.8)
    for y in (16.6, 21.0, 24.4):
        shell.light_strip(mb, -3.0, y, 15.0, y, D.Z_CEIL2, "light_strip", w=0.2)
    z0, z1 = D.Z_CEIL, D.Z_CEIL2
    ya = D.SLAB_A[1]
    lx = D.ESC_LAND_X
    kit.box(mb, D.ESC_VOID[0] - 0.3, ya - 0.3, z0, D.CORE_W[0], ya, z1, WALL)   # モール側
    kit.box(mb, D.ESC_VOID[0] - 0.3, ya, z0, D.ESC_VOID[0], D.ESC_VOID[3] + 0.3, z1, WALL)
    kit.box(mb, D.ESC_VOID[0], D.ESC_VOID[3], z0, lx, D.ESC_VOID[3] + 0.3, z1, WALL)
    kit.box(mb, lx - 0.3, D.ESC_VOID[3] + 0.3, z0, lx, D.CORE_E[1], z1, WALL)    # 降り口の東
    kit.box(mb, D.CORE_W[0], ya - 0.3, D.Z_CEIL, D.CORE_W[0] + 0.3, sy1, z1, WALL)  # 西
    for x0, x1 in ((sx0, D.ATRIUM[0]), (D.ATRIUM[2], sx1)):                    # 公園側
        kit.box(mb, x0, sy1, z0, x1, sy1 + 0.3, z1, WALL)
    shell.railing(mb, [(hx0, hy0), (hx1, hy0), (hx1, hy1)], z, h=1.05, mat="metal_dark",
                  glass=GLASS, post=2.4)


# ---- 吹き抜け ------------------------------------------------------------ #
def atrium(c, mb):
    """ラーニングスクエア2 の上の吹き抜け: 両脇の壁・頂部・2F と 3F の縁の立ち上がりとガラス。"""
    x0, y0, x1, y1 = D.ATRIUM
    kit.box(mb, x0 - 0.3, y0, D.Z_CEIL, x0, y1, D.Z_TOP, WALL)
    kit.box(mb, x1, y0, D.Z_CEIL, x1 + 0.3, y1, D.Z_TOP, WALL)
    kit.box(mb, x0, y0, D.Z_CEIL2, x1, y0 + 0.3, D.Z_TOP, WALL)
    shell.ceiling(mb, x0, y0, x1, y1, D.Z_ATRIUM, "ceiling_white")
    for k in range(4):
        y = y0 + 2.0 + 2.2 * k
        shell.light_strip(mb, x0 + 1.0, y, x1 - 1.0, y, D.Z_ATRIUM, "light_strip", w=0.2)
    # 2F の縁: 白い小口（1F の天井から）+ グレーの立ち上がり + 枠の無いガラス
    kit.box(mb, x0, y0, D.Z_CEIL, x1, y0 + 0.25, D.Z2, WALL)
    kit.box(mb, x0, y0, D.Z2, x1, y0 + 0.25, D.Z2 + 1.1, GREY)
    kit.box(mb, x0, y0 + 0.1, D.Z2 + 1.1, x1, y0 + 0.13, D.Z2 + 1.6, GLASS)
    # 3F の縁（歩けない）: 吹き抜けの奥の壁の面に同じ構成の帯
    kit.box(mb, x0, y0 + 0.3, D.Z_SLAB3, x1, y0 + 0.55, D.Z_SLAB3 + 1.5, GREY)
    kit.box(mb, x0, y0 + 0.4, D.Z_SLAB3 + 1.5, x1, y0 + 0.43, D.Z_SLAB3 + 2.0, GLASS)


# ---- 鉄骨階段（東のコアの西の面に沿って +Y へ上がる）------------------- #
def stair(c, mb):
    """オークの段板・黒いささら桁。西の側にガラスの手すり（東の側はコアの壁）。"""
    xa, xb = D.STAIR_X
    y0, n = D.STAIR_Y0, D.STAIR_N
    run, rise = D.STAIR_RUN, D.STAIR_RISE
    for i in range(1, n):
        top = rise * i
        kit.box(mb, xa, y0 + run * (i - 1), top - 0.05, xb, y0 + run * i, top,
                "kyoso_wood_oak")
    y_end = D.STAIR_END_Y
    z_end = rise * n
    for x in (xa - 0.03, xb + 0.03):
        kit.thick_quad(mb, (x, y0 - 0.19, 0.0), (x, y_end, z_end - 0.30),
                       (x, y_end, z_end + 0.02), (x, y0, rise + 0.02), 0.05, STEEL)
    x = xb + 0.03
    kit.thick_quad(mb, (x, y0, rise + 0.07), (x, y_end, z_end + 0.07),
                   (x, y_end, z_end + 0.95), (x, y0, rise + 0.95), 0.03, GLASS)
    kit.tube(mb, (x, y0, rise + 0.97), (x, y_end, z_end + 0.97), 0.03, "metal_dark", seg=6)
    kit.box_c(mb, x, y0, 0.0, 0.07, 0.07, rise + 0.97, STEEL)

"""共創棟の躯体: 床・外周・天井（木ルーバー）・仕切り・柱・コア、奥のブロックの 2F・吹き抜け・鉄骨階段。

envelope・ceilings_1f・partitions・columns と東のコアは建物の座標で組む。west_block は西のブロック、
back_* と slabs_2f・stair は奥のブロックの局所座標で組み、plan_kyoso が common.turned で置く。
"""

import math

from . import furn_kyoso as FK
from . import kit, shell
from . import plan_kyoso_dims as D

WALL = "wall_white"
SLAB_SIDE = "wall_white"
CARPET = "kyoso_floor_carpet_grey"
STEEL = "kyoso_steel_black"
GLASS = "glass_clear"
BLACK = "kyoso_steel_black"
CONCRETE = "kyoso_concrete_exposed"

HOLES_LOUVER = (D.ATRIUM, D.STAIR_HOLE)


# ---- 外周 -------------------------------------------------------------- #
def _edge(mb, p, q, ranges, glass, gaps_of=None):
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
            shell.outer_wall(mb, a, b, z0, z1, wall=WALL, glass=None if solid else glass,
                             sill=sill, header=header, seg=1.8, gaps=gaps,
                             gap_top=3.2 if gaps else None)


def envelope(c):
    """床スラブ + 外周 + 入口のガラススクリーン。戻り値は入口の開口。

    モール側は全面ガラス。公園側は奥のブロックの 2F ラウンジと吹き抜けの高さまでガラスを
    立ち上げる。西の面・東の面と、閉じた区画に面した公園側は窓なし。
    """
    s = c.spec
    x0, x1, ya, yb = s.x0, s.x1, s.y_face, s.y_back
    mb = c.wall
    shell.floor(c.floor, x0, ya, x1, yb, 0.0, "floor_tile_grey", thickness=0.30,
                side_mat="concrete_grey")
    d0, d1 = -D.DOOR_W * 0.5, D.DOOR_W * 0.5
    z_screen = 3.2
    one = ((0.0, D.Z_SLAB, 0.95, 0.45),)
    two = one + ((D.Z_SLAB, D.Z_SLAB3, 0.4, 0.6),)
    top = two + ((D.Z_SLAB3, D.Z_TOP, 0.0, 0.2),)

    # 入口辺（西 -> 東）
    def door_gaps(s0, s1):
        g0, g1 = d0 - x0, d1 - x0
        return [(g0 - s0, g1 - s0)] if s0 <= g0 and g1 <= s1 else ()

    _edge(mb, (x0, ya), (x1, ya), ((0.0, x1 - x0, one, False),), GLASS, door_gaps)
    shell.glass_entrance(mb, d0, d1, 0.0, z_screen, ya + shell.WALL * 0.5)
    # 右辺（南 -> 北）: 前の通路の突き当たりだけガラス
    _edge(mb, (x1, ya), (x1, yb), ((0.0, D.Y_PASS - ya, one, False),
                                   (D.Y_PASS - ya, yb - ya, one, True)), GLASS)

    # 奥辺（東 -> 西）
    def bk(x):
        return x1 - x

    xb = D.B_DX
    _edge(mb, (x1, yb), (x0, yb), (
        (0.0, bk(D.CORE_E[0]), one, True),
        (bk(D.CORE_E[0]), bk(xb - 2.0), one, False),
        (bk(xb - 2.0), bk(xb - 16.0), two, False),
        (bk(xb - 16.0), bk(xb - 36.0), top, False),
        (bk(xb - 36.0), x1 - x0, one, True)), GLASS)
    # 屋根側へ立ち上げる壁（スラブの上面・吹き抜けの壁の天端に立っても外へ抜けない）
    lift = ((D.Z_SLAB, D.Z_SLAB + 1.8, 0.0, 0.0),)
    rise = ((D.Z_TOP, D.Z_TOP + 1.2, 0.0, 0.0),)
    _edge(mb, (x1, yb), (x0, yb), (
        (0.0, bk(xb - 1.7), lift, True),
        (bk(xb - 15.4), bk(xb - 36.4), rise, True)), GLASS)
    # 左辺（北 -> 南）。西のブロックの奥の壁（天端が Z_SLAB）に立っても外へ抜けないよう立ち上げる
    _edge(mb, (x0, yb), (x0, ya), ((0.0, yb - ya, one, True),), GLASS)
    _edge(mb, (x0, yb), (x0, ya), ((yb - D.Y_IN1 - 0.6, yb - D.Y_IN1 + 0.3, lift, True),),
          GLASS)
    c.door_gap = (d0, d1)
    return d0, d1


def partitions(c):
    """中ホールの西の仕切り・ラウンジ 1 の東の仕切り（東のコアと奥の閉じた区画を分ける）。"""
    mb = c.wall
    shell.partition(mb, (D.X_WEST, D.Y_IN1 + 0.3), (D.X_WEST, D.Y_PARK), 0.0, D.Z_SLAB,
                    "wall_grey", thick=0.24)
    shell.partition(mb, (D.X_EAST, D.Y_PASS), (D.X_EAST, D.Y_PARK), 0.0, D.Z_SLAB,
                    "wall_grey", thick=0.24)
    shell.exit_sign(mb, D.HALL_X[1] - 0.18, D.Y_HALL1 - 2.0, 2.40, ang=math.pi * 0.5)


# ---- 天井 -------------------------------------------------------------- #
def _louvers(mb, rect, holes=()):
    shell.ceiling(mb, rect[0], rect[1], rect[2], rect[3], D.Z_VOID, "kyoso_ceiling_void",
                  holes=holes)
    FK.louvers(mb, rect[0], rect[1], rect[2], rect[3], D.Z_CEIL, holes=holes)


def ceilings_1f(c):
    """前の列と中ホールの天井（西のブロックと奥のブロックの天井は各ブロックが張る）。"""
    mb = c.wall
    # 木ルーバー（桟は Y 方向）+ 黒い天井面: エントランスホール・前の通路・中ホール・奥の列の東
    for rect in ((D.X_CORRIDOR, D.Y_IN0, 7.0, D.Y_IN1),
                 (7.0, D.Y_IN0, D.X_IN1, D.Y_PASS),
                 (D.HALL_X[0], D.Y_IN1, D.HALL_X[1], D.Y_HALL1),
                 D.BACK_EAST):
        _louvers(mb, rect)
    # 白い天井（カフェラウンジ・ラウンジ）
    shell.ceiling(mb, 7.0, D.Y_PASS, D.HALL_X[1], D.Y_IN1, D.Z_CEIL, "ceiling_white",
                  grid=1.8)


# ---- 柱 ---------------------------------------------------------------- #
def columns(c):
    """前の列と中ホールのコンクリート打ち放しの柱。"""
    mb = c.wall
    for x, y in D.COLUMNS_FRONT + D.COLUMNS_HALL:
        shell.column(mb, x, y, 0.0, D.Z_SLAB, size=D.COLUMN_SIZE, mat=CONCRETE, base=False)


# ---- コア -------------------------------------------------------------- #
def core(c, mb, x0, x1, side_wall=False):
    """EV 2 基 + 階段・トイレの扉。前の通路（Y = Y_PASS）に面し、奥は入れない。

    side_wall なら x1 に奥へ向かう仕切り（となりの区画との境）を立てる。
    """
    z, y = D.Z_CEIL, D.Y_PASS
    kit.box(mb, x0, y, 0.0, x1, y + 0.16, z, "kyoso_wc_taupe")
    if side_wall:
        kit.box(mb, x1, y + 0.16, 0.0, x1 + 0.16, D.Y_IN1, z, WALL)
    shell.elevator_bank(mb, x0 + 1.7, y, count=2, ang=0.0, pitch=2.0, w=0.65, h=2.3)
    # 階段・トイレの扉（閉じた板。奥は作らない）
    dx = x0 + 5.4
    kit.box(mb, dx, y - 0.08, 0.0, dx + 1.2, y, 2.1, "kyoso_wall_bluegrey")
    kit.box(mb, dx - 0.06, y - 0.1, 0.0, dx, y, 2.16, "metal_gray")
    kit.box(mb, dx + 1.2, y - 0.1, 0.0, dx + 1.26, y, 2.16, "metal_gray")
    kit.box(mb, dx - 0.06, y - 0.1, 2.1, dx + 1.26, y, 2.16, "metal_gray")
    kit.box(mb, dx + 0.6, y - 0.14, 1.0, dx + 0.66, y - 0.08, 1.2, "stainless")


# ---- 西のブロック（局所座標）------------------------------------------ #
def west_block(c):
    """白い天井・コンビニの線状照明・中ホールとの境の壁・誘導灯。"""
    mb = c.wall
    shell.ceiling(mb, -58.0, D.Y_IN0, -34.0, D.Y_IN1, D.Z_CEIL, "ceiling_white", grid=1.8)
    # ファミマの線状照明（斜めの線は省き、長手の帯で表す）
    for k in range(4):
        shell.light_strip(mb, -49.4, 7.6 + 1.7 * k, -38.6, 7.6 + 1.7 * k, D.Z_CEIL,
                          w=0.16)
    # コアとコンビニの奥（中ホールとの境）。白い通路（X -38..-34）は中ホールへ抜ける
    kit.box(mb, -58.0, D.Y_IN1, 0.0, -38.0, D.Y_IN1 + 0.3, D.Z_SLAB, WALL)
    shell.exit_sign(mb, -58.0 + 0.18, D.Y_IN0 + 1.2, 2.40, ang=-math.pi * 0.5)


# ---- 奥のブロック（局所座標）------------------------------------------ #
def back_front(c):
    """中ホール側の面: 2F と吹き抜けの高さの壁（1F は中ホールへ開く）。"""
    mb = c.wall
    kit.box(mb, -36.0, 4.0, D.Z_SLAB, -1.8, 4.3, D.Z_SLAB3, WALL)
    kit.box(mb, -36.4, 4.0, D.Z_TOP, -35.4, 4.3, D.Z_TOP + 1.2, WALL)


def back_ceilings(c):
    """1F の天井: 木ルーバー（吹き抜けと階段の上は穴）と西の縁の白い天井。"""
    mb = c.wall
    _louvers(mb, (-34.0, 4.0, -1.8, D.Y_IN1), holes=HOLES_LOUVER)
    shell.ceiling(mb, -36.0, 4.0, -34.0, D.Y_IN1, D.Z_CEIL, "ceiling_white")


def back_columns(c):
    mb = c.wall
    for x in (-34.8, -27.6):
        shell.column(mb, x, 13.6, 0.0, D.Z_SLAB, size=D.COLUMN_SIZE, mat=CONCRETE,
                     base=False)
    for x, mat in ((-34.8, "kyoso_column_pale"), (-27.6, "kyoso_column_bluegrey"),
                   (-20.4, "kyoso_column_pale")):
        kit.box(mb, x - 0.9, 6.6, 0.0, x + 0.9, 7.6, D.Z_CEIL, mat)
        kit.box(mb, x - 0.9, 6.6, 0.0, x + 0.9, 7.6, 0.12, "kyoso_column_base")
        kit.box(mb, x - 0.2, 7.6, 0.45, x + 0.2, 7.65, 0.65, "plastic_white")      # コンセント
    # スタバの黒板の柱
    kit.box(mb, -4.1, 6.9, 0.0, -3.3, 8.9, D.Z_SLAB, "kyoso_wall_bluegrey")


def slabs_2f(c):
    mb = c.wall
    z = D.Z2
    for rect in (D.SLAB_STRIP, D.SLAB_BRIDGE, D.SLAB_LOUNGE,
                 (D.SLAB_BALCONY_X[0], 12.4, D.SLAB_BALCONY_X[1], 14.5)):
        shell.floor(mb, rect[0], rect[1], rect[2], rect[3], z, CARPET, thickness=0.40,
                    side_mat=SLAB_SIDE)
    # 2F の天井
    for rect in ((-36.0, D.Y_IN0, -16.0, 7.0), (-16.0, D.Y_IN0, -1.8, D.Y_IN1)):
        shell.ceiling(mb, rect[0], rect[1], rect[2], rect[3], D.Z_CEIL2, "ceiling_white",
                      grid=1.8)
    # 吹き抜けの頂部
    shell.ceiling(mb, -36.0, 7.0, -16.0, 14.5, D.Z_ATRIUM, "ceiling_white")
    for k in range(3):
        shell.light_strip(mb, -34.0, 8.2 + 1.8 * k, -17.0, 8.2 + 1.8 * k, D.Z_ATRIUM,
                          w=0.2)
    # 吹き抜けの壁
    kit.box(mb, -36.0, D.Y_IN0, D.Z_CEIL, -35.7, D.Y_IN1, D.Z_TOP, WALL)          # 西
    kit.box(mb, -16.0, 7.0, D.Z_CEIL2, -15.7, D.Y_IN1, D.Z_TOP, WALL)             # 東
    kit.box(mb, -2.0, D.Y_IN0, D.Z_SLAB, -1.8, D.Y_IN1, D.Z_SLAB3, WALL)          # 2F ラウンジの東
    kit.box(mb, -36.0, 6.8, D.Z_CEIL, -26.2, 7.0, 12.8, "kyoso_projection_wall",
            top=WALL)                                                              # 投影壁
    kit.box(mb, -36.0, 6.8, 12.8, -26.2, 7.0, D.Z_TOP, WALL)
    kit.box(mb, -26.2, 6.8, D.Z_CEIL2, -16.0, 7.0, D.Z_TOP, WALL)                 # 吹き抜けの手前
    # ガラス手すり（黒い笠木）
    rails = (
        [(-16.0, 7.0), (-16.0, D.Y_IN1)],
        [(-23.8, 7.0), (-16.0, 7.0)],
        [(-26.2, 7.0), (-26.2, 12.4), (-36.0, 12.4)],
        [(-23.8, 7.0), (-23.8, 12.4)],
    )
    for pts in rails:
        shell.railing(mb, pts, D.Z2, h=1.05, mat="metal_dark", glass="glass_clear", post=2.4)
    # ブリッジの白い腰壁（手すりの外側の小口）
    for x in (-26.2, -23.8):
        kit.box(mb, x - 0.05, 7.0, D.Z2, x + 0.05, 12.4, D.Z2 + 0.25, WALL)
    # 3F スラブの縁（白い小口 + ガラス手すり）。歩けない
    kit.box(mb, -36.0, 7.0, D.Z_SLAB3 + 0.4, -35.7, D.Y_IN1, D.Z_SLAB3 + 0.8, WALL)
    # 2F の開口 -> 蹴込みは階段が作る


# ---- 鉄骨階段（局所の西へ上がる）-------------------------------------- #
def stair(c):
    mb = c.wall
    x0, n = D.STAIR_X0, D.STAIR_N
    ya, yb = D.STAIR_Y
    run, rise = D.STAIR_RUN, D.STAIR_RISE
    for i in range(1, n):
        top = rise * i
        xa, xb = x0 - run * i, x0 - run * (i - 1)
        kit.box(mb, xa, ya, top - 0.05, xb, yb, top, "kyoso_wood_oak")
    x_end = D.STAIR_END_X
    z_end = rise * n
    for y in (ya - 0.03, yb + 0.03):
        kit.thick_quad(mb, (x0 - 0.19, y, 0.0), (x_end, y, z_end - 0.30),
                       (x_end, y, z_end + 0.02), (x0, y, rise + 0.02), 0.05, STEEL)
        kit.thick_quad(mb, (x0, y, rise + 0.07), (x_end, y, z_end + 0.07),
                       (x_end, y, z_end + 0.95), (x0, y, rise + 0.95), 0.03, GLASS)
        kit.tube(mb, (x0, y, rise + 0.97), (x_end, y, z_end + 0.97), 0.03, "metal_dark",
                 seg=6)
        kit.box_c(mb, x0, y, 0.0, 0.07, 0.07, rise + 0.97, STEEL)
    # 最上段の先の蹴込み（踏面の端 x_end がバルコニーの端 -23.7 とつながる）

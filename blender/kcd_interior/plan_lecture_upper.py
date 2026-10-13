"""講義棟 2F・3F の東側（X ≥ 16.4）の廊下とエスカレータの穴（spec §1-4・§1-5）。

2F（z 4.6）と 3F（z 8.8）の床と天井、X 35.7 より東の南北の室の壁、エスカレータの穴の
手すりと垂れ壁、廊下のダウンライト。2F の床は東端ホールの上（X 37.8 / 39.8 まで）も含む。
X 35.7 までの廊下の壁とコアの箱は plan_lecture_west、エスカレータの段・2F の渡りとラウンジ・
3F の弧の自習室は別のモジュールが作る。
"""

import math

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH

T = shell.PART
Y_CS, Y_CN = RD.Y_CS, RD.Y_CN       # 南・北の室の壁の中心（面 19.64 / 19.80、35.10 / 35.26）
Y_SLAB0, Y_SLAB1 = Y_CS - T * 0.5, Y_CN + T * 0.5   # 床は南北の壁の外面まで
X0 = D.UPPER_X0                     # この区画の床・天井の西端（西は plan_lecture_west の廊下）
X_2F_E = D.EAST_VOID_B[0]           # 2F の床の東端（東端ホールの南の吹き抜けの縁 37.8）
X_2F_WALL = D.EAST_VOID_A[0] - T * 0.5   # 2F の東の壁の中心（面 39.64 / 39.80）
Y_2F_STRIP = D.EAST_VOID_A[1]       # X 37.8〜39.8 の床の南端（20.7）
X_3F_E = 35.7                       # 3F の本体の東端（311 の東の壁。東は弧の自習室）
X_LOUNGE0 = D.LOUNGE_2F[0][0]       # 2F の北の L 字のラウンジの西端（39.7）
HOLE = (D.ESC_LAND_W[1], D.ESC_S_Y[0], D.ESC_LAND_E[0], D.ESC_N_Y[1])   # 22.6, 25.6, 30.6, 29.4
CEIL_H = 3.0                        # 2F・3F の廊下の天井（床から）
Z_TOP = D.F3 + CEIL_H               # 3F の天井（壁とコアの箱の上端）
RAIL_H = 1.1
RAIL_OUT = 0.08                     # 穴の手すりを穴の縁から外の床へ離す（エスカレータの腹板と当たらない）
HEADER_T = 0.1
DOWN = 0.30                         # ダウンライトの一辺
DOWN_CLEAR = 0.8                    # 吊り看板の吊り元からダウンライトまでの最小距離
HOLE_LIGHT_GAP = 1.5                # 穴の縁からこの範囲の天井には点光源を置かない（エスカレータの段に近すぎる）

CORR = "lecture_concrete"
CEIL = "lecture_ceiling_dark"
CARPET = "lecture_carpet_corridor"
WHITE = "lecture_rail_white"
GLASS_RAIL = "lecture_glass_rail"


def _floors(c):
    for z, x1 in ((D.F2, X_2F_E), (D.F3, X_3F_E)):
        shell.floor(c.floor, X0, Y_SLAB0, x1, Y_SLAB1, z, CARPET, thickness=D.SLAB,
                    side_mat=CORR, holes=(HOLE,))
    shell.floor(c.floor, X_2F_E, Y_2F_STRIP, D.EAST_VOID_A[0], Y_SLAB1, D.F2, CARPET,
                thickness=D.SLAB, side_mat=CORR)


def _ceilings(c):
    z2 = D.F2 + CEIL_H
    shell.ceiling(c.wall, X0, D.Y_ROOM_S1, X_2F_E, D.Y_ROOM_N0, z2, CEIL, holes=(HOLE,))
    shell.ceiling(c.wall, X_2F_E, Y_2F_STRIP, D.EAST_VOID_A[0], D.Y_ROOM_N0, z2, CEIL)
    shell.ceiling(c.wall, X0, D.Y_ROOM_S1, X_3F_E, D.Y_ROOM_N0, Z_TOP, CEIL)
    # 2F の天井の穴の縁から 3F の床の下面まで
    x0, y0, x1, y1 = HOLE
    z3 = D.F3 - D.SLAB
    t = HEADER_T
    kit.box(c.wall, x0 - t, y0 - t, z2, x1 + t, y0, z3, WHITE)
    kit.box(c.wall, x0 - t, y1, z2, x1 + t, y1 + t, z3, WHITE)
    kit.box(c.wall, x0 - t, y0, z2, x0, y1, z3, WHITE)
    kit.box(c.wall, x1, y0, z2, x1 + t, y1, z3, WHITE)


def _walls(c):
    # 3F の本体の東端（X 35.7）より東の南北の室の壁は 2F だけ（南は東端ホールの吹き抜けの縁、北はラウンジの西端まで）
    SH.part(c, (X_3F_E, Y_CS), (X_2F_E, Y_CS), D.F2, D.F3, mat=CORR)
    SH.part(c, (X_3F_E, Y_CN), (X_LOUNGE0, Y_CN), D.F2, D.F3, mat=CORR)
    # 2F の東の縁: 北の吹き抜けに面した壁、南の吹き抜けに面したガラス手すり
    SH.part(c, (X_2F_WALL, Y_2F_STRIP), (X_2F_WALL, D.BRIDGE_2F[1]), D.F2, D.F3, mat=CORR)
    shell.railing(c.wall, [(X_2F_E, D.Y_ROOM_S1), (X_2F_E, Y_2F_STRIP),
                           (X_2F_WALL - T * 0.5, Y_2F_STRIP)],
                  D.F2, h=RAIL_H, mat="stainless", glass=GLASS_RAIL)


def _hole_rails(c):
    """エスカレータの穴の手すり。穴の縁から RAIL_OUT 外の床に立て、乗り口・降り口になる短辺は開ける。"""
    x0, y0, x1, y1 = HOLE
    ys, yn = y0 - RAIL_OUT, y1 + RAIL_OUT
    for pts in ([(x0, ys), (x1, ys)], [(x0, yn), (x1, yn)]):
        shell.railing(c.wall, pts, D.F2, h=RAIL_H, mat="stainless", glass=GLASS_RAIL)
    # 3F: 南の列は東端で降り、北の列は西端で降りて東端から 4F へ上る。南の列の西端は下の階へ落ちる縁
    for pts in ([(x0 - RAIL_OUT, D.ESC_S_Y[1]), (x0 - RAIL_OUT, ys), (x1, ys)],
                [(x0, yn), (x1, yn)]):
        shell.railing(c.wall, pts, D.F3, h=RAIL_H, mat="stainless", glass=GLASS_RAIL)


def _clear(x, y, z0, z1, roots):
    return all(math.hypot(x - rx, y - ry) >= DOWN_CLEAR or not z0 <= rz < z1
               for rx, ry, rz in roots)


def downlight_centers(z_floor, x1, roots):
    """z_floor の階の廊下のダウンライトの中心 (x, y)。吊り看板の吊り元の近くは避ける。"""
    pts = [(x, y) for y in (21.65, 24.55, 30.45, 33.3)
           for x in (19.0 + 3.0 * i for i in range(7)) if x < x1 - 0.5]
    return [p for p in pts if _clear(p[0], p[1], z_floor, z_floor + CEIL_H, roots)]


def near_hole(x, y):
    """(x, y) が穴の縁から HOLE_LIGHT_GAP の内か。"""
    x0, y0, x1, y1 = HOLE
    g = HOLE_LIGHT_GAP
    return x0 - g < x < x1 + g and y0 - g < y < y1 + g


def _lights(c, roots):
    """ダウンライトの板は全部に、点光源は 1 つおきに置き、穴の近くの点光源は省く。"""
    h = DOWN * 0.5
    for z_floor, x1 in ((D.F2, X_2F_E), (D.F3, X_3F_E)):
        z = z_floor + CEIL_H
        pts = downlight_centers(z_floor, x1, roots)
        for x, y in pts:
            kit.plate(c.wall, x - h, y - h, x + h, y + h, z - 0.005, "light_panel", flip=True)
        c.lights_from([p for p in pts[::2] if not near_hole(*p)], z)


def build(c, roots):
    """roots は吊り看板の吊り元 (x, y, z) の列。"""
    _floors(c)
    _ceilings(c)
    _walls(c)
    _hole_rails(c)
    _lights(c, roots)

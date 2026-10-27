"""講義棟 2F〜7F の東側（X ≥ 16.4）の廊下とエスカレータの穴（spec §1-4〜§1-6）。

各階の床と天井、エスカレータの穴の手すりと垂れ壁、廊下のダウンライト、2F の X 35.7 より東の
南北の室の壁と東の縁。床の東端は 2F が東端ホールの上（X 37.8 / 39.8 まで）、3F が 311 の東の壁（35.7）、
4F〜7F が東の外壁（35.8）。穴は 2F〜6F の床（エスカレータは 1F から 6F まで）と 2F〜5F の天井にあり、
7F はエスカレータの上を黒い箱（plan_lecture_west）にする。
部品は plan_lecture_west の各階の区画へ積む（帯 s・n は廊下、帯 c はコアの帯。X 28.6 で区画 4 と 5 に切る）。
X 16.4 より西の廊下・廊下の壁・コアの箱は plan_lecture_west、エスカレータの段・2F の渡りとラウンジ・
3F の弧の自習室は別のモジュールが作る。
"""

import math

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH
from . import plan_lecture_west as W

T = shell.PART
Y_CS, Y_CN = RD.Y_CS, RD.Y_CN       # 南・北の室の壁の中心（面 19.64 / 19.80、35.10 / 35.26）
Y_SLAB0, Y_SLAB1 = Y_CS - T * 0.5, Y_CN + T * 0.5   # 床は南北の壁の外面まで
X0 = D.UPPER_X0                     # この区画の床・天井の西端（西は plan_lecture_west の廊下）
X_CORE0 = D.SCREEN_X[1]             # コアの帯の床・天井の西端（男子トイレの箱の東の面 18.0）
X_2F_E = D.EAST_VOID_B[0]           # 2F の床の東端（東端ホールの南の吹き抜けの縁 37.8）
X_2F_WALL = D.EAST_VOID_A[0] - T * 0.5   # 2F の東の壁の中心（面 39.64 / 39.80）
Y_2F_STRIP = D.EAST_VOID_A[1]       # X 37.8〜39.8 の床の南端（20.7）
X_3F_E = RD.X_311_E                 # 3F の本体の東端（311 の東の壁。東は弧の自習室）
X_LOUNGE0 = D.LOUNGE_2F[0][0]       # 2F の北の L 字のラウンジの西端（39.7）
X_EAST = {"2F": X_2F_E, "3F": X_3F_E}   # 床・天井の東端（4F〜7F は RD.X_UPPER_E）
HOLE = (D.ESC_LAND_W[1], D.ESC_S_Y[0], D.ESC_LAND_E[0], D.ESC_N_Y[1])   # 22.6, 25.6, 30.6, 29.4
FLOOR_HOLES = RD.FLOORS[:-1]        # 床に穴のある階（2F〜6F）
CEIL_HOLES = RD.FLOORS[:-2]         # 天井に穴のある階（2F〜5F）
CEIL_H = RD.CEIL_H                  # 廊下の天井（床から）
RAIL_H = 1.1
RAIL_OUT = 0.08                     # 穴の手すりを穴の縁から外の床へ離す（エスカレータの腹板と当たらない）
HEADER_T = 0.1
DOWN = 0.30                         # ダウンライトの一辺
DOWN_CLEAR = 0.8                    # 吊り看板の吊り元からダウンライトまでの最小距離
HOLE_LIGHT_GAP = 1.5                # 穴の縁からこの範囲の天井には点光源を置かない（エスカレータの段に近すぎる）

CORR = "lecture_concrete_light"
CEIL = "lecture_ceiling_upper"
CARPET = "lecture_carpet_upper"
WHITE = "lecture_rail_white"
GLASS_RAIL = "lecture_glass_rail"


def x_east(floor):
    """floor の階の床・天井の東端。"""
    return X_EAST.get(floor, RD.X_UPPER_E)


def x_core0(floor):
    """floor の階のコアの帯の床・天井の西端（7F はエスカレータの上の黒い箱の東）。"""
    return W.CORE_ESC[2] if floor == RD.TOP_FLOOR else X_CORE0


def rects(floor, y0, y1):
    """floor の階の床か天井の矩形 (帯, 区画の番号, x0, y0, x1, y1)。南北の帯とコアの帯、X 28.6 で切る。"""
    x1 = x_east(floor)
    out = []
    for b, ya, yb, xa in (("s", y0, D.Y_CORE0, X0), ("c", D.Y_CORE0, D.Y_CORE1, x_core0(floor)),
                          ("n", D.Y_CORE1, y1, X0)):
        xs = [xa] + [x for x in (D.X_STEP,) if xa < x < x1] + [x1]
        out += [(b, W.segment((a + e) * 0.5), a, ya, e, yb) for a, e in zip(xs[:-1], xs[1:])]
    return out


def _floors(c, floor):
    z = RD.FLOOR_Z[floor]
    bs = W.bands(c, "floor", floor)
    holes = (HOLE,) if floor in FLOOR_HOLES else ()
    for b, k, x0, y0, x1, y1 in rects(floor, Y_SLAB0, Y_SLAB1):
        shell.floor(bs.get(b, k), x0, y0, x1, y1, z, CARPET, thickness=D.SLAB, side_mat=CORR,
                    holes=holes)
    if floor == "2F":
        shell.floor(bs.get("c", 5), X_2F_E, Y_2F_STRIP, D.EAST_VOID_A[0], Y_SLAB1, z, CARPET,
                    thickness=D.SLAB, side_mat=CORR)


def _ceilings(walls, floor):
    """廊下の明るい灰色の天井。2F〜5F は穴を開け、穴の縁から上の階の床スラブの下面まで白い垂れ壁。"""
    z = RD.FLOOR_Z[floor] + CEIL_H
    holes = (HOLE,) if floor in CEIL_HOLES else ()
    for b, k, x0, y0, x1, y1 in rects(floor, D.Y_ROOM_S1, D.Y_ROOM_N0):
        shell.ceiling(walls.get(b, k), x0, y0, x1, y1, z, CEIL, holes=holes)
    if floor == "2F":
        shell.ceiling(walls.get("c", 5), X_2F_E, Y_2F_STRIP, D.EAST_VOID_A[0], D.Y_ROOM_N0, z, CEIL)
    if not holes:
        return
    x0, y0, x1, y1 = HOLE
    zt = RD.next_z(floor) - D.SLAB
    t = HEADER_T
    mb = walls.get("c", W.segment((x0 + x1) * 0.5))
    kit.box(mb, x0 - t, y0 - t, z, x1 + t, y0, zt, WHITE)
    kit.box(mb, x0 - t, y1, z, x1 + t, y1 + t, zt, WHITE)
    kit.box(mb, x0 - t, y0, z, x0, y1, zt, WHITE)
    kit.box(mb, x1, y0, z, x1 + t, y1, zt, WHITE)


def _walls_2f(walls):
    """2F の 311 の東の壁の線（X 35.7）より東: 南北の室の壁、北の吹き抜けに面した壁、南の吹き抜けのガラス手すり。"""
    z0, z1 = D.F2, D.F3
    SH.part(W._Walls(walls.get("s", 5)), (X_3F_E, Y_CS), (X_2F_E, Y_CS), z0, z1, mat=CORR)
    SH.part(W._Walls(walls.get("n", 5)), (X_3F_E, Y_CN), (X_LOUNGE0, Y_CN), z0, z1, mat=CORR)
    mb = walls.get("c", 5)
    SH.part(W._Walls(mb), (X_2F_WALL, Y_2F_STRIP), (X_2F_WALL, D.BRIDGE_2F[1]), z0, z1, mat=CORR)
    shell.railing(mb, [(X_2F_E, D.Y_ROOM_S1), (X_2F_E, Y_2F_STRIP), (X_2F_WALL - T * 0.5, Y_2F_STRIP)],
                  z0, h=RAIL_H, mat="stainless", glass=GLASS_RAIL)


def rail_lines(floor):
    """floor の階の穴の手すりの折れ線。穴の縁から RAIL_OUT 外の床に立て、乗り口・降り口になる短辺は開ける。

    6F は終点: 5F からの区間は南の列が東端、北の列が西端で 6F に着くので、
    南の列の西端と北の列の東端は下の階へ落ちる縁になる。"""
    x0, y0, x1, y1 = HOLE
    ys, yn = y0 - RAIL_OUT, y1 + RAIL_OUT
    if floor == FLOOR_HOLES[-1]:
        return ([(x0 - RAIL_OUT, D.ESC_S_Y[1]), (x0 - RAIL_OUT, ys), (x1, ys)],
                [(x0, yn), (x1 + RAIL_OUT, yn), (x1 + RAIL_OUT, D.ESC_N_Y[0])])
    return ([(x0, ys), (x1, ys)], [(x0, yn), (x1, yn)])


def _hole_rails(walls, floor):
    mb = walls.get("c", W.segment((HOLE[0] + HOLE[2]) * 0.5))
    for pts in rail_lines(floor):
        shell.railing(mb, pts, RD.FLOOR_Z[floor], h=RAIL_H, mat="stainless", glass=GLASS_RAIL)


def _clear(x, y, z0, z1, roots):
    return all(math.hypot(x - rx, y - ry) >= DOWN_CLEAR or not z0 <= rz < z1
               for rx, ry, rz in roots)


def downlight_centers(floor, roots):
    """floor の階の廊下のダウンライトの中心 (x, y)。吊り看板の吊り元の近くと 7F の黒い箱の上は避ける。"""
    z_floor = RD.FLOOR_Z[floor]
    x1 = x_east(floor)
    xc = x_core0(floor)
    pts = [(x, y) for y in (21.65, 24.55, 30.45, 33.3)
           for x in (19.0 + 3.0 * i for i in range(7)) if x < x1 - 0.5]
    pts = [p for p in pts if not (D.Y_CORE0 < p[1] < D.Y_CORE1 and p[0] < xc + DOWN)]
    return [p for p in pts if _clear(p[0], p[1], z_floor, z_floor + CEIL_H, roots)]


def near_hole(x, y):
    """(x, y) が穴の縁から HOLE_LIGHT_GAP の内か。"""
    x0, y0, x1, y1 = HOLE
    g = HOLE_LIGHT_GAP
    return x0 - g < x < x1 + g and y0 - g < y < y1 + g


def _lights(c, walls, floor, roots):
    """ダウンライトの板は全部に、プレビューの明かりは 1 つおきに置き、穴の近くは省く。"""
    h = DOWN * 0.5
    z = RD.FLOOR_Z[floor] + CEIL_H
    pts = downlight_centers(floor, roots)
    for x, y in pts:
        b = "c" if D.Y_CORE0 < y < D.Y_CORE1 else W.band(y)
        kit.plate(walls.get(b, W.segment(x)), x - h, y - h, x + h, y + h, z - 0.005, "light_panel",
                  flip=True)
    c.lights_from([p for p in pts[::2] if not near_hole(*p)], z)


def build(c, roots):
    """roots は吊り看板の吊り元 (x, y, z) の列。plan_lecture_west.build の後に呼ぶ。"""
    for floor in RD.FLOORS:
        walls = W.bands(c, "wall", floor)
        _floors(c, floor)
        _ceilings(walls, floor)
        if floor in FLOOR_HOLES:
            _hole_rails(walls, floor)
        if floor == "2F":
            _walls_2f(walls)
        _lights(c, walls, floor, roots)

"""講義棟の東端ホール（spec §1-6）。

南東のコア（エレベーター・階段室）の箱、1F の低い天井、2 層吹き抜けの天井（3F の床の下面）、
南東の風除室、点字ブロック、2F の渡りへ上る直線の階段、吹き抜けの縁の手すり。
2F の渡りとラウンジ・3F の弧の自習室は plan_lecture_lounge が作る。
"""

from . import kit, shell
from . import plan_lecture_dims as D

CONCRETE = "lecture_concrete"
CEIL = "lecture_ceiling_dark"
WHITE = "lecture_rail_white"
GLASS = "lecture_glass_rail"
COOL = "lecture_light_cool"
TACTILE = "lecture_tactile_yellow"
WOOD = "lecture_door_wood"
DARK = "lecture_core_black"

X_E_IN = 49.52                    # 東の外壁の内面
Y_HALL_S = 8.30                   # 南の外壁の内面
Y_MID = D.Y_ROOM_S1               # 南の室の廊下側の面 19.8
Y_FOYER_N = D.Y_ROOM_N0           # 北の室の廊下側の面 35.1
Z_VOID = D.CEIL_EAST_VOID         # 吹き抜けの天井 8.5
Z_LOW = D.CEIL_EAST_LOW           # 2F の床の下の天井 4.0
CORE_X = (D.X_STEP, D.EAST_VOID_B[0])        # 南東コアの箱 28.6〜37.8

# 直線階段（1F → 2F の渡り）
ST_X = (D.DIRECT_STAIR_X[0], X_E_IN)
ST_Y0, ST_Y1 = D.DIRECT_STAIR_Y            # 20.6 → 30.4
ST_STEPS = 13
ST_RUN = 0.30
ST_LAND = (ST_Y0 + ST_STEPS * ST_RUN, ST_Y0 + ST_STEPS * ST_RUN + 2.0)    # 24.5 → 26.5
ST_RISE = D.F2 / (2 * ST_STEPS)
RAIL_H = 1.0


def _core_box(c):
    x0, x1 = CORE_X
    kit.box(c.wall, x0, Y_HALL_S, 0.0, x1, Y_MID, D.F3, CONCRETE, top=CONCRETE)
    # エレベーターの扉は北の面（ホールの側）。階段室の扉は東の面
    shell.elevator_bank(c.wall, 30.0, Y_MID, count=2, ang=3.141592653589793, pitch=2.6)
    shell.door(c.wall, x1, 12.0, ang=-1.5707963267948966, w=1.2, h=2.2, leaf=WOOD,
               frame="metal_dark", open_=0.0)


def _ceilings(c):
    x0, x1 = CORE_X
    shell.ceiling(c.wall, x0, Y_MID, x1, Y_FOYER_N, Z_LOW, CEIL)
    shell.ceiling(c.wall, x1, D.EAST_VOID_A[1], D.EAST_VOID_A[0], Y_FOYER_N, Z_LOW, CEIL)
    shell.ceiling(c.wall, D.EAST_VOID_A[0], D.BRIDGE_2F[1], X_E_IN, Y_FOYER_N, Z_LOW, CEIL)
    # 2 層吹き抜けの天井（3F の床の下面）
    for y in (13.0, 19.0, 25.0, 31.0):
        shell.light_strip(c.wall, 38.6, y, 48.6, y + 0.16, Z_VOID, mat=COOL)
    for x in (41.0, 46.0):
        for y in (13.0, 22.0, 29.0):
            c.light(x, y, Z_VOID - 1.4, energy=140.0, radius=2.4)
    for x in (31.0, 34.5):
        c.light(x, 27.0, Z_LOW - 0.6, energy=70.0, radius=1.6)
    c.lights_from([(40.0, 33.0), (44.0, 33.0), (48.0, 33.0)], Z_LOW - 0.6, energy=70.0)


def _vestibule(c):
    """南東の風除室。左右と奥をガラスでふさぎ、奥の中央に引き戸を開けておく。"""
    x0, y0, x1, y1 = D.SE_VESTIBULE
    y_in = 11.6
    h = D.CEIL_CORR
    xm = (x0 + x1) * 0.5
    y_out = Y_HALL_S
    for x in (x0, x1):
        kit.thick_quad(c.wall, (x, y_out, 0.0), (x, y_in, 0.0), (x, y_in, h), (x, y_out, h),
                       shell.GLASS_T, GLASS)
        kit.box(c.wall, x - 0.04, y_in - 0.04, 0.0, x + 0.04, y_in + 0.04, h, "metal_dark")
    for xa, xb in ((x0, xm - 0.8), (xm + 0.8, x1)):
        kit.thick_quad(c.wall, (xa, y_in, 0.0), (xb, y_in, 0.0), (xb, y_in, h), (xa, y_in, h),
                       shell.GLASS_T, GLASS)
    kit.thick_quad(c.wall, (xm - 0.8, y_in, 2.3), (xm + 0.8, y_in, 2.3), (xm + 0.8, y_in, h),
                   (xm - 0.8, y_in, h), shell.GLASS_T, GLASS)
    shell.door(c.wall, xm, y_in, w=1.6, h=2.3, glass=GLASS, frame="metal_dark")
    shell.ceiling(c.wall, x0, y_out, x1, y_in, h, WHITE)
    c.light(xm, 10.0, h - 0.4, energy=70.0, radius=1.4)


def _tactile(c):
    xm = (D.SE_VESTIBULE[0] + D.SE_VESTIBULE[2]) * 0.5
    kit.plate(c.floor, xm - 0.30, 11.7, xm + 0.30, 20.3, 0.010, TACTILE)
    kit.plate(c.floor, xm + 0.30, 19.7, ST_X[0] - 0.1, 20.3, 0.010, TACTILE)


def _stair(c):
    x0, x1 = ST_X
    z = 0.0
    y = ST_Y0
    for k in range(ST_STEPS):
        kit.box(c.wall, x0, y + k * ST_RUN, 0.0, x1, ST_LAND[0], ST_RISE * (k + 1), DARK,
                top=WOOD)
    kit.box(c.wall, x0, ST_LAND[0], 0.0, x1, ST_LAND[1], D.F2 * 0.5, DARK, top=CONCRETE)
    z0 = D.F2 * 0.5
    for k in range(ST_STEPS):
        kit.box(c.wall, x0, ST_LAND[1] + k * ST_RUN, 0.0, x1, ST_Y1, z0 + ST_RISE * (k + 1),
                DARK, top=WOOD)
    # 西の縁のガラス手すり（傾きに合わせる）と握り
    xr = x0 + 0.04
    for ya, yb, za, zb in ((ST_Y0, ST_LAND[0], 0.0, z0), (ST_LAND[0], ST_LAND[1], z0, z0),
                           (ST_LAND[1], ST_Y1, z0, D.F2)):
        kit.thick_quad(c.wall, (xr, ya, za + 0.10), (xr, yb, zb + 0.10),
                       (xr, yb, zb + RAIL_H - 0.04), (xr, ya, za + RAIL_H - 0.04),
                       shell.GLASS_T, GLASS)
        kit.tube(c.wall, (xr, ya, za + RAIL_H), (xr, yb, zb + RAIL_H), 0.035, "metal_gray",
                 seg=6)
    for y, z in ((ST_Y0, 0.0), (ST_LAND[0], z0), (ST_LAND[1], z0), (ST_Y1, D.F2)):
        kit.box_c(c.wall, xr, y, z, 0.06, 0.06, z + RAIL_H, "metal_gray")


def _edges(c):
    """2F の渡りの南の縁と、渡りの下の手すり。"""
    shell.railing(c.wall, [(D.EAST_VOID_A[0], ST_Y1), (ST_X[0], ST_Y1)], D.F2, RAIL_H,
                  "metal_gray", glass=GLASS)
    shell.railing(c.wall, [(D.EAST_VOID_B[0], D.Y_ROOM_S1 + 0.9), (D.EAST_VOID_B[0],
                  D.EAST_VOID_A[1])], D.F2, RAIL_H, "metal_gray", glass=GLASS)


def build(c):
    _core_box(c)
    _ceilings(c)
    _vestibule(c)
    _tactile(c)
    _stair(c)
    _edges(c)
    shell.planter(c.wall, 41.5, 14.0)
    shell.planter(c.wall, 41.5, 18.0)

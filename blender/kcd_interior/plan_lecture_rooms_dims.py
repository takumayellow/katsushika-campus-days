"""講義棟 2F・3F の教室・看板・明かりの表（spec §1-4・§1-5・§1-11・§6-4）。

室の境の X は間仕切りの中心。西端 D.X_W は西の外周壁、東端 D.X_STEP は 101 の東の段差の外周壁。
扉の中心は、扉の枠（中心から ±0.87）が室の内法から 0.04 以上内側に収まるように置く（推定）。
"""

import math

from . import shell
from . import plan_lecture_dims as D

T = shell.PART
X_IN_W = D.X_W + shell.WALL           # 西の外周壁の内面（-28.3）
X_IN_STEP = D.X_STEP - shell.WALL     # 101 の東の段差の外周壁の内面（28.3）
Y_IN_S = D.Y_S + shell.WALL           # 南の外周壁の内面（4.3）
Y_CS = D.Y_ROOM_S1 - T * 0.5          # 南の室の壁の中心（面 19.64 / 19.80）
Y_CN = D.Y_ROOM_N0 + T * 0.5          # 北の室の壁の中心（面 35.10 / 35.26）
Y_S_ROOM1 = Y_CS - T * 0.5            # 南の室の廊下側の内法（19.64）
Y_N_ROOM0 = Y_CN + T * 0.5            # 北の室の廊下側の内法（35.26）
X_311_E = 35.7                        # 311 の東の壁（その東は 3F の弧の自習室）
Y_311_E0 = 46.4                       # 311 の東の壁のうち、自習室の弦の壁より北の部分の南端
CEIL_H = 3.0                          # 2F・3F の天井（床から）
Z_TOP = D.F3 + CEIL_H                 # 3F の天井（11.8）
DOOR_W, DOOR_H = D.DOOR2_W, D.DOOR2_H
DOOR_OPEN = 0.86
FLOOR_Z = {"2F": D.F2, "3F": D.F3}

# (室番号, 階, 並び, 西の境, 東の境, 扉の中心 X（西から順）, 型)
#   型 A: 黒板が西の壁、机は西を向く。型 B: PC ルーム（§1-11）
ROOMS = (
    ("203", "2F", "S", D.X_W, -10.9, (-26.5, -12.4), "A"),
    ("202", "2F", "S", -10.9, 6.9, (-9.9, 5.9), "A"),
    ("201", "2F", "S", 6.9, D.X_STEP, (8.1, 27.3), "A"),
    ("305", "3F", "S", D.X_W, -17.8, (-25.9, -18.9), "A"),
    ("304", "3F", "S", -17.8, -3.5, (-16.8, -4.5), "A"),
    ("303", "3F", "S", -3.5, 7.3, (-2.5, 6.3), "A"),
    ("302", "3F", "S", 7.3, 21.8, (8.3, 20.7), "A"),
    ("301", "3F", "S", 21.8, D.X_STEP, (27.3,), "A"),
    ("306", "3F", "N", D.X_W, -17.8, (-25.9, -18.9), "B"),
    ("307", "3F", "N", -17.8, -3.5, (-16.8, -4.5), "A"),
    ("308", "3F", "N", -3.5, 7.3, (-2.5, 6.3), "A"),
    ("309", "3F", "N", 7.3, 14.3, (8.3, 13.3), "A"),
    ("310", "3F", "N", 14.3, 21.3, (15.3, 20.3), "A"),
    ("311", "3F", "N", 21.3, X_311_E, (22.5, 32.7), "A"),
)

# 2F の北の室（中は作らない）: 廊下の面 Y 35.1 の閉じた扉の X と、室名札の白い板の X
DOORS_2F_N = (-27.4, -20.4, -13.1, -5.8, 5.9, 8.3, 34.6, 37.75)
PLATES_2F_N = (-26.1, -19.1, -11.8, -4.5, 9.6, 36.45)
WEST_DOOR_Y = 29.7                    # 西の外周壁の閉じた扉（2F・3F。学生食堂の上の階へ）


def room_faces(room):
    """室の内法 (x0, x1, y0, y1)。北の室の y1 は北の外周壁の内面のうち低い方。"""
    _, _, side, x0, x1, _, _ = room
    xf0 = X_IN_W if x0 == D.X_W else x0 + T * 0.5
    xf1 = X_IN_STEP if x1 == D.X_STEP else x1 - T * 0.5
    if side == "S":
        return xf0, xf1, Y_IN_S, Y_S_ROOM1
    return xf0, xf1, Y_N_ROOM0, D.north_y(xf1) - shell.WALL


def corridor_y(side):
    """室の扉の前の廊下の Y と、扉を入ってすぐの通路の Y。"""
    return (21.0, 18.4) if side == "S" else (34.0, 36.4)


# 室の明かりの Y。廊下の壁（南は Y 19.5、北は Y 35.4 まで）から 9 m より離し、点光源の範囲（9 m）が
# 廊下の帯のメッシュに届かないようにする（spec §1-13 の 1 つの物に 4 灯まで）。室の奥行きの中央から 1.7〜1.9 m 外へ寄る
LAMP_Y = {"S": 10.3, "N": 44.6}


def wide(room):
    """幅が 14 m を超える室は天井の明かりを 2 つにする。"""
    xf0, xf1, _, _ = room_faces(room)
    return xf1 - xf0 > 14.0


def lamp_points(room):
    """室の天井の明かりの (x, y)。Y は LAMP_Y（廊下の帯のメッシュから 9 m より離す）。"""
    xf0, xf1, _, _ = room_faces(room)
    cy = LAMP_Y[room[2]]
    if not wide(room):
        return [((xf0 + xf1) * 0.5, cy)]
    q = (xf1 - xf0) * 0.25
    return [(xf0 + q, cy), (xf1 - q, cy)]


# 廊下・西ロビー・他のモジュールの区画の明かり（階ごと）
LAMPS_CORRIDOR = tuple((x, y) for x in (-12.0, 0.0, 12.0, 28.0) for y in (21.65, 33.3)) + \
    ((-24.8, 27.5),)
LAMPS_EXTRA = {"2F": ((43.0, 43.0),), "3F": ((43.0, 33.4),)}   # 2F の L 字のラウンジ・3F の弧の自習室
LAMP_DZ = 2.6                         # 床から点光源まで

# ---- 2F・3F の看板（行の形は plan_lecture_dims.SIGNS と同じ。Empty は置かない） ----
S, N = math.pi, 0.0
BLACK, WHITE, BLUE = "lecture_sign_black", "lecture_sign_white", "sign_plate_blue"
NO_ANCHOR = {"anchor": False}


def _floor_signs(name, en, z):
    return (
        ("wall", name, en, 0.7, 0.6, 9.0, 23.49, z + 2.2, S, BLACK, NO_ANCHOR),
        ("wall", name, en, 0.7, 0.6, 9.0, 31.51, z + 2.2, N, BLACK, NO_ANCHOR),
        ("wall", "女子トイレ", "Women", 0.8, 0.3, -7.8, 23.49, z + 2.4, S, BLUE, NO_ANCHOR),
        ("wall", "男子トイレ", "Men", 0.8, 0.3, 14.0, 23.49, z + 2.4, S, BLUE, NO_ANCHOR),
        ("wall", "エレベーター", "Elevator", 1.0, 0.3, -17.6, 23.49, z + 2.4, S, BLUE, NO_ANCHOR),
    )


def _plate(no, x, side):
    z = FLOOR_Z[{"2": "2F", "3": "3F"}[no[0]]] + 1.8
    y, ang = (19.81, N) if side == "S" else (35.09, S)
    return ("wall", no, "Room " + no, 0.42, 0.9, x, y, z, ang, WHITE, NO_ANCHOR)


# 室名札（201・302・310・311 は plan_lecture_dims.SIGNS にある）
ROOM_PLATES = (("203", -25.2, "S"), ("202", -8.6, "S"), ("305", -24.6, "S"), ("304", -15.5, "S"),
               ("303", -1.2, "S"), ("301", 26.0, "S"), ("306", -24.6, "N"), ("307", -15.5, "N"),
               ("308", -1.2, "N"), ("309", 9.6, "N"))

SIGNS = (_floor_signs("2F", "2nd Floor", D.F2) + _floor_signs("3F", "3rd Floor", D.F3)
         + tuple(_plate(no, x, side) for no, x, side in ROOM_PLATES))

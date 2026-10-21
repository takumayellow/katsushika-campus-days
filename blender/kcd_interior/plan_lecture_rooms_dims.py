"""講義棟 2F〜7F の教室・看板・明かりの表（spec §1-4〜§1-11・§6-4）。

室の境の X は間仕切りの中心。西端 D.X_W は西の外周壁、東端 D.X_STEP は 101 の東の段差の外周壁。
扉の中心は、扉の枠（中心から ±0.87）が室の内法から 0.04 以上内側に収まるように置く（推定）。
4F〜7F は 3F と同じ間仕切りなので、扉も 3F の同じ並びの値を使う（x01 だけ東の境が EV のホール）。
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
CEIL_H = 3.0                          # 2F〜7F の廊下と室の天井（床から）
DOOR_W, DOOR_H = D.DOOR2_W, D.DOOR2_H
DOOR_OPEN = 0.86
FLOORS = ("2F", "3F", "4F", "5F", "6F", "7F")
FLOOR_Z = {f: D.FL[i + 1] for i, f in enumerate(FLOORS)}
TOP_FLOOR = FLOORS[-1]
X_UPPER_E = 35.8                      # 4F〜7F の本体の東端（東の外壁の中心。§1-6）
X_01_E = 28.4                         # 4F〜7F の x01 の東の境（南東 EV のホールの西の壁）


def next_z(floor):
    """floor の上の階の床の高さ（7F は屋根が無いので天井の高さ）。"""
    if floor == TOP_FLOOR:
        return FLOOR_Z[floor] + CEIL_H
    return FLOOR_Z[FLOORS[FLOORS.index(floor) + 1]]


def wall_top(floor):
    """廊下の壁・間仕切りの上端。2F〜6F は上の階の床スラブの下面、7F は天井（蓋つき）。"""
    if floor == TOP_FLOOR:
        return next_z(floor)
    return next_z(floor) - D.SLAB


# 4F〜7F の室の割り付け（§1-6）: (番号の下 2 桁, 西の境, 東の境, 扉の中心 X)
UPPER_ROWS = {
    "S": (("05", D.X_W, -17.8, (-25.9, -18.9)), ("04", -17.8, -3.5, (-16.8, -4.5)),
          ("03", -3.5, 7.3, (-2.5, 6.3)), ("02", 7.3, 21.8, (8.3, 20.7)),
          ("01", 21.8, X_01_E, (27.3,))),
    "N": (("06", D.X_W, -17.8, (-25.9, -18.9)), ("07", -17.8, -3.5, (-16.8, -4.5)),
          ("08", -3.5, 7.3, (-2.5, 6.3)), ("09", 7.3, 14.3, (8.3, 13.3)),
          ("10", 14.3, 21.3, (15.3, 20.3)), ("11", 21.3, X_UPPER_E, (22.5, 31.4))),
}
UPPER_BUILT = {"402": "A", "502": "A", "602": "A", "410": "C", "509": "C", "610": "C"}


def _upper_rooms():
    """4F〜7F の全室 (室番号, 階, 並び, 西の境, 東の境, 扉, 型)。型 None は閉じた室。"""
    out = []
    for fl in FLOORS[2:]:
        for side in ("S", "N"):
            for nn, x0, x1, doors in UPPER_ROWS[side]:
                no = fl[0] + nn
                out.append((no, fl, side, x0, x1, doors, UPPER_BUILT.get(no)))
    return tuple(out)


# (室番号, 階, 並び, 西の境, 東の境, 扉の中心 X（西から順）, 型)
#   型 A: 黒板が東の壁、机は東を向く。型 B: PC ルーム。型 C: ロの字の机の会議室。型 L: ラウンジ（§1-11）
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
    ("310", "3F", "N", 14.3, 21.3, (15.3, 20.3), "L"),
    ("311", "3F", "N", 21.3, X_311_E, (22.5, 32.7), "L"),
) + tuple(r for r in _upper_rooms() if r[6])

# 中を作らない室（廊下の壁に閉じた扉と室名札だけ。7F は札に番号を書かない）
CLOSED = tuple(r[:6] for r in _upper_rooms() if not r[6])

# 2F の北の室（中は作らない）: 廊下の面 Y 35.1 の閉じた扉の X と、室名札の白い板の X
DOORS_2F_N = (-27.4, -20.4, -13.1, -5.8, 5.9, 8.3, 34.6, 37.75)
PLATES_2F_N = (-26.1, -19.1, -11.8, -4.5, 9.6, 36.45)


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


# 室の明かりの X の格子。室のメッシュはこの X と、奥行きの ROOM_SPLIT_Y で切る（plan_lecture_rooms）。
# 格子の間が 12 m なので、切った片には両端の 2 列（室の灯と廊下の灯）しか届かない
LAMP_GRID_X = (-24.0, -12.0, 0.0, 12.0, 24.0)
ROOM_SPLIT_Y = {"S": 12.6, "N": 42.5}    # 廊下の灯（Y 21.65・33.3）から 9 m より奥の片


def lamp_points(room):
    """室の天井の明かりの (x, y)。格子の X のうち室の内法に入るもの。

    型 C で格子が入らない室は中央に 1 灯。型 A・B・L で入らない室（310）は廊下の灯で照らす
    （中央に置くと、両隣の室の灯と廊下の灯で室の手前の片に 5 灯届く）。"""
    xf0, xf1, _, _ = room_faces(room)
    cy = LAMP_Y[room[2]]
    pts = [(x, cy) for x in LAMP_GRID_X if xf0 < x < xf1]
    if not pts and room[6] == "C":
        pts = [((xf0 + xf1) * 0.5, cy)]
    return pts


# 廊下・西ロビー・他のモジュールの区画の明かり（階ごと）
LAMPS_CORRIDOR = tuple((x, y) for x in (-12.0, 0.0, 12.0, 28.0) for y in (21.65, 33.3)) + \
    ((-24.8, 27.5),)
LAMPS_EXTRA = {f: () for f in FLOORS}
LAMPS_EXTRA.update({"2F": ((43.0, 43.0),), "3F": ((43.0, 33.4),)})   # 2F の L 字のラウンジ・3F の弧の自習室
LAMP_DZ = 2.6                         # 床から点光源まで

# ---- 2F〜7F の看板（行の形は plan_lecture_dims.SIGNS と同じ。Empty は置かない） ----
S, N, E, W = math.pi, 0.0, -math.pi * 0.5, math.pi * 0.5
BLACK, WHITE, BLUE = "lecture_sign_black", "lecture_sign_white", "sign_plate_blue"
NO_ANCHOR = {"anchor": False}


def _floor_signs(name, en, z):
    return (
        ("wall", name, en, 0.7, 0.6, 9.0, 23.49, z + 2.2, S, BLACK, NO_ANCHOR),
        ("wall", name, en, 0.7, 0.6, 9.0, 31.51, z + 2.2, N, BLACK, NO_ANCHOR),
        ("wall", "女子トイレ", "Women", 0.8, 0.3, -7.8, 23.49, z + 2.4, S, BLUE, NO_ANCHOR),
        ("wall", "男子トイレ", "Men", 0.8, 0.3, 14.0, 23.49, z + 2.4, S, BLUE, NO_ANCHOR),
        ("wall", "エレベーター", "Elevator", 1.0, 0.3, -21.01, 27.5, z + 2.4, W, BLUE, NO_ANCHOR),
    )


def plate_at(no, x, side):
    """室名札の板の中心 (x, y, z) と向き。"""
    z = FLOOR_Z[no[0] + "F"] + 1.8
    y, ang = (19.81, N) if side == "S" else (35.09, S)
    return x, y, z, ang


def _plate(no, x, side):
    x, y, z, ang = plate_at(no, x, side)
    return ("wall", no, "Room " + no, 0.42, 0.9, x, y, z, ang, WHITE, NO_ANCHOR)


def upper_plate_x(room):
    """4F〜7F の室名札の X。西の扉の東 1.3 m（x01 は扉が 1 つなので西 1.3 m）。"""
    d = room[5][0]
    return d - 1.3 if room[0][1:] == "01" else d + 1.3


# 室名札（201・302・310・311 は plan_lecture_dims.SIGNS にある。7F は番号の無い板で plan_lecture_west が作る）
ROOM_PLATES = (("203", -25.2, "S"), ("202", -8.6, "S"), ("305", -24.6, "S"), ("304", -15.5, "S"),
               ("303", -1.2, "S"), ("301", 26.0, "S"), ("306", -24.6, "N"), ("307", -15.5, "N"),
               ("308", -1.2, "N"), ("309", 9.6, "N")) + tuple(
    (r[0], upper_plate_x(r), r[2]) for r in _upper_rooms() if r[1] != TOP_FLOOR)

FLOOR_NAMES = {"2F": "2nd Floor", "3F": "3rd Floor", "4F": "4th Floor", "5F": "5th Floor",
               "6F": "6th Floor", "7F": "7th Floor"}


def _partition_sign(fl):
    """仕切りの東面の階表示（2F・3F は plan_lecture_dims.SIGNS。7F はエスカレータの上を箱で塞ぐので無い）。"""
    return (("wall", fl, FLOOR_NAMES[fl], 0.7, 0.6, 18.01, 27.5, FLOOR_Z[fl] + 2.2, E, BLACK,
             NO_ANCHOR),)


SIGNS = (sum((_floor_signs(f, FLOOR_NAMES[f], FLOOR_Z[f]) for f in FLOORS), ())
         + sum((_partition_sign(f) for f in FLOORS[2:-1]), ())
         + tuple(_plate(no, x, side) for no, x, side in ROOM_PLATES))

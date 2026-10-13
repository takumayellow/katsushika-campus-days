"""講義棟 2F・3F の教室 14 室の中（spec §1-4・§1-5・§1-11・§1-13）。

2F は南の 203・202・201、3F は南の 305〜301 と北の 306〜311。どの室も床・白い天井と照明の板・
外周壁（南と北は窓の帯、西端の室は西の壁）・東の境の間仕切り・廊下の壁の室の側の白い面を持つ。
型 A は西の壁に緑の黒板・教卓・プロジェクター・東の壁に時計、机は西を向いた 3 人掛けの固定机。
型 B（306 の PC ルーム）は白板・教員の机・2 人掛けの PC の机。廊下の壁と扉は plan_lecture_west。

メッシュは室ごとに分ける（URP は 1 つのメッシュに効く点光源の数に上限がある）:
    floor_lecture_r<室> / wall_lecture_r<室> / furn_lecture_<nn>_r<室>
    lamp_lecture_<階>_<n>   室の天井の点光源（番号は廊下の点光源の続き）
"""

import math

from . import furniture as F
from . import furn_lecture as FL
from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH
from . import plan_lecture_west as W

CARPET = "lecture_carpet_corridor"
CONCRETE = "lecture_concrete"
WHITE = "wall_white"
CEIL_WHITE = "ceiling_white"
FACE_E = -math.pi * 0.5      # 正面 +X（西の壁から室の奥へ）
FACE_W = math.pi * 0.5       # 正面 -X（西の壁の黒板へ）
LINING = 0.005               # 廊下の壁の室の側の面から白い板まで
PANEL = (1.2, 0.6)           # 天井の照明の板（東西・南北）
PANEL_PITCH = 3.0
DESK_Y = {"S": (7.55, 10.55, 13.55, 16.55), "N": (38.1, 41.1, 44.1, 47.1)}   # 机の列の中心 Y
DESK_W = 1.8
DESK_DEPTH = 0.45            # furn_lecture.desk_bench の天板の奥行き
FRONT0 = 2.7                 # 西の壁の面から最前列の机の前の縁まで
ROW_PITCH = 1.0
BACK_CLEAR = 5.15            # 列の数を決める: 室の幅からこれを引いた残りを 1.0 m ごとに 1 列
PC_FRONT0, PC_PITCH, PC_ROWS, PC_W, PC_DEPTH = 2.2, 1.3, 5, 1.4, 0.6
NORTH_IN = 0.15              # 北の室の床・天井を北の外周壁の中まで延ばす（外面から）
EAST_311_X = RD.X_311_E + RD.T * 0.5   # 311 の北の窓の帯の東端（自習室の弦の壁の外面）


def bounds(room):
    """床・天井の範囲 (x0, y0, x1, y1)。室の境は間仕切りの中心、外周は壁の内面（北は壁の中）。"""
    _, _, side, x0, x1, _, _ = room
    bx0 = RD.X_IN_W if x0 == D.X_W else x0
    bx1 = RD.X_IN_STEP if x1 == D.X_STEP else x1
    if side == "S":
        return bx0, RD.Y_IN_S, bx1, RD.Y_S_ROOM1
    return bx0, RD.Y_N_ROOM0, bx1, D.north_y(bx1) - NORTH_IN


def rows(room):
    """型 A の机の列の数（最後の列の後ろに 1.4 m 以上の通路が残る）。"""
    xf0, xf1, _, _ = RD.room_faces(room)
    return int((xf1 - xf0 - BACK_CLEAR) // ROW_PITCH) + 1


def _grid(a, b, pitch=PANEL_PITCH):
    n = max(1, int((b - a) // pitch))
    step = (b - a) / n
    return [a + step * (i + 0.5) for i in range(n)]


def _floor_ceiling(c, walls, room):
    bx0, by0, bx1, by1 = bounds(room)
    z = RD.FLOOR_Z[room[1]]
    shell.floor(c.part("floor", "r" + room[0]), bx0, by0, bx1, by1, z, CARPET,
                thickness=D.SLAB, side_mat=CONCRETE)
    zc = z + RD.CEIL_H
    shell.ceiling(walls, bx0, by0, bx1, by1, zc, CEIL_WHITE)
    # 照明の板は下向きの 1 枚板（3F の天井の上に立てる面を作らない）
    xf0, xf1, yf0, yf1 = RD.room_faces(room)
    hx, hy = PANEL[0] * 0.5, PANEL[1] * 0.5
    for x in _grid(xf0, xf1):
        for y in _grid(yf0, yf1):
            kit.plate(walls, x - hx, y - hy, x + hx, y + hy, zc - LINING, "light_panel", flip=True)


def _face(mb, a, b, y, z0, z1, side):
    """廊下の壁の室の側に貼る白い板（南の室は -Y、北の室は +Y を向く）。"""
    p, q = ((a, y), (b, y)) if side == "S" else ((b, y), (a, y))
    kit.vplate(mb, p, q, z0, z1, WHITE)


def _lining(walls, room):
    """廊下の壁（灰色のコンクリート）の室の側を、扉の開口を除いて白くする。"""
    _, fl, side, _, _, doors, _ = room
    xf0, xf1, _, _ = RD.room_faces(room)
    z = RD.FLOOR_Z[fl]
    hw = RD.DOOR_W * 0.5
    y = RD.Y_S_ROOM1 - LINING if side == "S" else RD.Y_N_ROOM0 + LINING
    xs = [xf0] + [v for d in doors for v in (d - hw, d + hw)] + [xf1]
    for a, b in zip(xs[0::2], xs[1::2]):
        _face(walls, a, b, y, z, z + RD.DOOR_H, side)
    _face(walls, xf0, xf1, y, z + RD.DOOR_H, z + RD.CEIL_H, side)


def _outer(walls, room):
    """南・北の窓の帯の外周壁と、西端の室の西の外周壁。2F は 3F の床まで、3F は天井まで（蓋つき）。"""
    _, fl, side, x0, x1, _, _ = room
    top = fl == "3F"
    z0, z1 = RD.FLOOR_Z[fl], (RD.Z_TOP if top else D.F3)
    header = 0.6 if top else 1.4
    w = W._Walls(walls)
    if side == "S":
        line = ((x0, D.Y_S), (x1, D.Y_S))
    else:
        xe = EAST_311_X if x1 == RD.X_311_E else x1
        line = ((xe, D.north_y(xe)), (x0, D.north_y(x0)))
    SH.wall(w, line, z0, z1, mat=WHITE, glass=SH.GLASS, header=header, top=top)
    if x0 != D.X_W:
        return
    if side == "S":
        line = ((D.X_W, RD.Y_CS), (D.X_W, D.Y_S))
    else:
        line = ((D.X_W, D.north_y(D.X_W)), (D.X_W, RD.Y_CN))
    SH.wall(w, line, z0, z1, mat=WHITE, top=top)


def _east(walls, room):
    """東の境の壁。隣の室との間仕切り、201・301 は 101 の東の段差の外周壁、311 は自習室の北の壁。"""
    _, fl, side, _, x1, _, _ = room
    top = fl == "3F"
    z0 = RD.FLOOR_Z[fl]
    z1 = RD.Z_TOP if top else W.Z_SLAB3_UNDER
    w = W._Walls(walls)
    if x1 == D.X_STEP:
        SH.wall(w, SH.STEP, z0, RD.Z_TOP if top else D.F3, mat=WHITE, top=top)
        xs = D.X_STEP - shell.WALL * 0.5
        SH.part(w, (xs, SH.STEP[1][1]), (xs, RD.Y_CS), z0, z1, mat=WHITE, thick=shell.WALL,
                top=top)
    elif x1 == RD.X_311_E:
        y1 = D.north_y(x1) - shell.WALL + 0.1
        SH.part(w, (x1, RD.Y_311_E0), (x1, y1), z0, z1, mat=WHITE, top=top)
    elif side == "S":
        SH.part(w, (x1, D.Y_S + shell.WALL * 0.5), (x1, RD.Y_CS), z0, z1, mat=WHITE, top=top)
    else:
        y1 = D.north_y(x1) - shell.WALL + 0.1
        SH.part(w, (x1, RD.Y_CN), (x1, y1), z0, z1, mat=WHITE, top=top)


def _type_a(mb, room, z):
    """一般の教室: 西の壁の黒板・教卓・プロジェクター・東の壁の時計・西を向いた固定机の列。"""
    xf0, xf1, yf0, yf1 = RD.room_faces(room)
    cy = (yf0 + yf1) * 0.5
    FL.green_board(mb, kit.T(xf0, cy, z, FACE_E))
    W.lifted(mb, z, F.podium, xf0 + 1.3, cy, ang=FACE_E)
    F.projector(mb, xf0 + 0.4 * (xf1 - xf0), cy, z + RD.CEIL_H - 0.22, ang=FACE_W)
    shell.clock(mb, xf1, cy, z + 2.4, ang=FACE_W)
    for k in range(rows(room)):
        x = xf0 + FRONT0 + DESK_DEPTH + k * ROW_PITCH
        for y in DESK_Y[room[2]]:
            FL.desk_bench(mb, kit.T(x, y, z, FACE_W), w=DESK_W)


def _type_b(mb, room, z):
    """306 の PC ルーム: 西の壁の白板・教員の机・西を向いた PC の机の列（spec §1-11）。"""
    xf0, _, yf0, yf1 = RD.room_faces(room)
    cy = (yf0 + yf1) * 0.5
    F.whiteboard(mb, xf0, cy, z + 1.5, ang=FACE_E, w=3.6, h=1.2)
    W.lifted(mb, z, F.desk, xf0 + 1.3, cy, ang=FACE_W, w=1.4, d=0.7, top="desk_white")
    for k in range(PC_ROWS):
        x = xf0 + PC_FRONT0 + PC_DEPTH + k * PC_PITCH
        for y in DESK_Y[room[2]]:
            FL.pc_desk(mb, kit.T(x, y, z, FACE_W), w=PC_W)


def _lamps(c, room, n0):
    """室の天井の点光源の Empty（番号は n0 の続き）とプレビューの明かり。戻り値は置いた数。"""
    fl = room[1]
    z = RD.FLOOR_Z[fl]
    pts = RD.lamp_points(room)
    for i, (x, y) in enumerate(pts, 1):
        c._put("lamp_%s_%s_%d" % (c.spec.id, fl, n0 + i), x, y, z + RD.LAMP_DZ)
    c.lights_from(pts, z + RD.CEIL_H)
    return len(pts)


def build(c, lamp_count):
    """lamp_count は階 -> すでに置いた点光源の数。戻り値は教室の点光源を足した数。"""
    count = dict(lamp_count)
    for room in RD.ROOMS:
        no, fl = room[0], room[1]
        walls = c.part("wall", "r" + no)
        _floor_ceiling(c, walls, room)
        _lining(walls, room)
        _outer(walls, room)
        _east(walls, room)
        furn = _type_a if room[6] == "A" else _type_b
        furn(c.furn("r" + no), room, RD.FLOOR_Z[fl])
        count[fl] += _lamps(c, room, count[fl])
    return count

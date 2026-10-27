"""講義棟 2F〜7F の教室の中（spec §1-4・§1-5・§1-11・§1-13）。

2F は南の 203・202・201、3F は南の 305〜301 と北の 306〜311、4F〜6F は 402・410・502・509・602・610。
どの室も床・白い天井と照明の板・外周壁（南と北は窓の帯、西端の室は西の壁）・東の境の間仕切り・
廊下の壁の室の側の白い面を持つ。西隣の室を作らない階では西の境の間仕切りも持つ。
型 A は東の壁（金町駅の側）に緑の黒板・教卓・プロジェクター・西の壁に時計、机は東を向いた 3 人掛けの固定机。
型 B（306 の PC ルーム）は東の壁の白板・教員の机・2 人掛けの PC の机。型 C は東の壁の白板とロの字の机（会議室）。
型 L（310・311 のラウンジ）はソファ・低い机・丸机と椅子、311 は弁当を売る長机と保温ケースも置く。
廊下の壁と扉、中を作らない室の閉じた扉は plan_lecture_west。ブラインド・後ろの掲示板・教卓の小物・
天井の吹き出し口は plan_lecture_props.room。

メッシュは室ごとに、さらに灯りの届く範囲ごとに分ける（URP は 1 つのメッシュに効く点光源の数に
上限がある）。奥行きの RD.ROOM_SPLIT_Y より奥の帯が r<室>、手前を明かりの格子の X で切った片が
r<室>_<k>（k は西から数えた格子の間の番号）:
    floor_lecture_r<室>[_<k>] / wall_lecture_r<室>[_<k>] / furn_lecture_<nn>_r<室>[_<k>]
    lamp_lecture_<階>_<n>   室の天井の点光源（番号は廊下の点光源の続き）
    board_lecture_<室>      黒板・白板の面の中央（Unity が板書の文字と調べる対象を置く）
    board_lecture_7F       7F の廊下の南の壁の白板の面の中央（705 の 2 つの扉の間）
    board_lecture_<…>_f    板の面から 1 m 手前（板の正面の向きを Unity に渡す）
"""

import math

from kcd_lib.mesh import MeshBuilder

from . import furniture as F
from . import furn_lecture as FL
from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_props as PROPS
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH
from . import plan_lecture_west as W

CARPET = "lecture_carpet_upper"
CONCRETE = "lecture_concrete"
WHITE = "wall_white"
CEIL_WHITE = "ceiling_white"
BOARD = "lecture_board_room"           # 型 A の黒板（408 の写真で測った色。101・104 の黒板とは別）
LECTERN = "lecture_lectern_walnut"     # 型 A の教卓の胴（濃い木）
FACE_E = -math.pi * 0.5      # 正面 +X（東）
FACE_W = math.pi * 0.5       # 正面 -X（西）
LINING = 0.005               # 廊下の壁の室の側の面から白い板まで
PANEL = (1.2, 0.6)           # 天井の照明の板（東西・南北）
PANEL_PITCH = 3.0
DESK_Y = {"S": (7.55, 10.55, 13.55, 16.55), "N": (38.1, 41.1, 44.1, 47.1)}   # 机の列の中心 Y
DESK_W = 1.8
DESK_DEPTH = 0.45            # furn_lecture.desk_bench の天板の奥行き
FRONT0 = 2.7                 # 東の壁の面から最前列の机の前の縁まで
ROW_PITCH = 1.0
BACK_CLEAR = 5.15            # 列の数を決める: 室の幅からこれを引いた残りを 1.0 m ごとに 1 列
PC_FRONT0, PC_PITCH, PC_ROWS, PC_W, PC_DEPTH = 2.2, 1.3, 5, 1.4, 0.6
NORTH_IN = 0.15              # 北の室の床・天井を北の外周壁の中まで延ばす（外面から）
EAST_311_X = RD.X_311_E + RD.T * 0.5   # 311 の北の窓の帯の東端（自習室の弦の壁の外面）
C_RING_Y = 43.0              # 型 C のロの字の机の中心 Y（北の室の奥行きの中央）
C_LONG_X, C_LONG_Y = 1.2, (-0.9, 0.9)  # 長辺の机の中心（中心から）。机は 1.8 x 0.6 を Y に沿って置く
C_END_Y, C_END_W = 2.1, 3.0  # 短辺の机の中心 Y（中心から）と幅
C_CHAIR_X, C_CHAIR_Y = 1.9, (-1.35, -0.45, 0.45, 1.35)   # 長辺の椅子
C_END_CHAIR_Y, C_END_CHAIR_X = 2.8, (-0.6, 0.0, 0.6)      # 短辺の椅子
LOUNGE_TABLE = "lecture_table_white"
SOFA_Y = (40.5, 43.5, 46.5)  # 型 L の西の壁ぎわのソファの中心 Y（背を壁に、正面 +X）
SOFA_D = 0.85
LOW_TABLE_DX = 2.0           # 西の壁の面からソファの前の低い机の中心まで
ROUND_Y = (41.5, 46.0)       # 型 L の丸机の中心 Y
ROUND_X0, ROUND_PITCH, ROUND_EAST = 3.6, 3.5, 1.2   # 西の壁の面から最初の丸机・間隔・東の壁から空ける幅
BENTO_ROOM = "311"           # 昼に弁当を売る長机と保温ケースを置くラウンジ（spec §1-11）
BOARD_Z = 1.5                # 黒板・白板の中心の高さ（床から）
BOARD_FACE = {"A": 0.031, "B": 0.07, "C": 0.07}   # 壁の面から板の面まで（green_board / whiteboard）
HALL_BOARD_X, HALL_BOARD_W = -22.4, 2.4             # 7F の廊下の白板の中心 X と幅
BENTO_X, BENTO_Y, BENTO_W, BENTO_D = 27.8, 38.25, 3.6, 0.7   # 長机の中心・幅（東西）・奥行き


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


def _floor_ceiling(floor, walls, room):
    bx0, by0, bx1, by1 = bounds(room)
    z = RD.FLOOR_Z[room[1]]
    shell.floor(floor, bx0, by0, bx1, by1, z, CARPET, thickness=D.SLAB, side_mat=CONCRETE)
    zc = z + RD.CEIL_H
    shell.ceiling(walls, bx0, by0, bx1, by1, zc, CEIL_WHITE)
    # 照明の板は下向きの 1 枚板（天井の上に立てる面を作らない）
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


def _lidded(room):
    """壁の上端に蓋を貼るか。7F と、上の階の同じ所に室を作らない室（上に床も壁も無く、壁の上端に立ててしまう）。"""
    _, fl, side, x0, x1 = room[:5]
    if fl == RD.TOP_FLOOR:
        return True
    up = RD.FLOORS[RD.FLOORS.index(fl) + 1]
    return not any(r[1] == up and r[2] == side and r[3] <= x0 and x1 <= r[4] for r in RD.ROOMS)


def _outer(walls, room):
    """南・北の窓の帯の外周壁と、西端の室の西の外周壁。上の階の床まで、7F は天井まで。"""
    _, fl, side, x0, x1, _, _ = room
    top = _lidded(room)
    z0, z1 = RD.FLOOR_Z[fl], RD.next_z(fl)
    header = 0.6 if fl == RD.TOP_FLOOR else 1.4
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
    top = _lidded(room)
    z0, z1 = RD.FLOOR_Z[fl], RD.wall_top(fl)
    w = W._Walls(walls)
    if x1 == D.X_STEP:
        SH.wall(w, SH.STEP, z0, RD.next_z(fl), mat=WHITE, top=top)
        xs = D.X_STEP - shell.WALL * 0.5
        SH.part(w, (xs, SH.STEP[1][1]), (xs, RD.Y_CS), z0, z1, mat=WHITE, thick=shell.WALL,
                top=top)
    elif x1 == RD.X_311_E:
        y1 = D.north_y(x1) - shell.WALL + 0.1
        SH.part(w, (x1, RD.Y_311_E0), (x1, y1), z0, z1, mat=WHITE, top=top)
    else:
        _partition(w, room, x1, z0, z1, top)


def _partition(w, room, x, z0, z1, top):
    """室の境 X の間仕切り（南の室は外周壁から廊下の壁まで、北の室は廊下の壁から外周壁まで）。"""
    if room[2] == "S":
        SH.part(w, (x, D.Y_S + shell.WALL * 0.5), (x, RD.Y_CS), z0, z1, mat=WHITE, top=top)
    else:
        y1 = D.north_y(x) - shell.WALL + 0.1
        SH.part(w, (x, RD.Y_CN), (x, y1), z0, z1, mat=WHITE, top=top)


def _west(walls, room):
    """西隣の室を作らない階の、西の境の間仕切り（2F・3F は西隣の室の東の境が兼ねる）。"""
    no, fl, side, x0 = room[:4]
    if x0 == D.X_W or any(r[1] == fl and r[2] == side and r[4] == x0 for r in RD.ROOMS):
        return
    _partition(W._Walls(walls), room, x0, RD.FLOOR_Z[fl], RD.wall_top(fl), _lidded(room))


def _type_a(mb, room, z):
    """一般の教室: 東の壁の黒板・教卓・プロジェクター・西の壁の時計・東を向いた固定机の列。"""
    xf0, xf1, yf0, yf1 = RD.room_faces(room)
    cy = (yf0 + yf1) * 0.5
    FL.green_board(mb, kit.T(xf1, cy, z, FACE_W), board=BOARD)
    W.lifted(mb, z, F.podium, xf1 - 1.3, cy, ang=FACE_W, body=LECTERN)
    F.projector(mb, xf1 - 0.4 * (xf1 - xf0), cy, z + RD.CEIL_H - 0.22, ang=FACE_E)
    shell.clock(mb, xf0, cy, z + 2.4, ang=FACE_E)
    for k in range(rows(room)):
        x = xf1 - FRONT0 - DESK_DEPTH - k * ROW_PITCH
        for y in DESK_Y[room[2]]:
            FL.desk_bench(mb, kit.T(x, y, z, FACE_E), w=DESK_W)


def _type_b(mb, room, z):
    """306 の PC ルーム: 東の壁の白板・教員の机・東を向いた PC の机の列（spec §1-11）。"""
    _, xf1, yf0, yf1 = RD.room_faces(room)
    cy = (yf0 + yf1) * 0.5
    F.whiteboard(mb, xf1, cy, z + 1.5, ang=FACE_W, w=3.6, h=1.2)
    W.lifted(mb, z, F.desk, xf1 - 1.3, cy, ang=FACE_E, w=1.4, d=0.7, top="desk_white")
    for k in range(PC_ROWS):
        x = xf1 - PC_FRONT0 - PC_DEPTH - k * PC_PITCH
        for y in DESK_Y[room[2]]:
            FL.pc_desk(mb, kit.T(x, y, z, FACE_E), w=PC_W)


def _type_c(mb, room, z):
    """会議室: 東の壁の白板・ロの字に組んだ白い長机と、内を向いた灰色の椅子 14 脚（spec §1-11）。"""
    xf0, xf1, _, _ = RD.room_faces(room)
    cx, cy = (xf0 + xf1) * 0.5, C_RING_Y
    F.whiteboard(mb, xf1, cy, z + 1.5, ang=FACE_W)
    for sx in (-1.0, 1.0):
        for dy in C_LONG_Y:
            F.table(mb, cx + sx * C_LONG_X, cy + dy, ang=math.pi * 0.5, w=1.8, d=0.6,
                    top="desk_white", z=z)
        F.table(mb, cx, cy + sx * C_END_Y, w=C_END_W, d=0.6, top="desk_white", z=z)
    chairs = [(cx + sx * C_CHAIR_X, cy + dy, cx, cy + dy)
              for sx in (-1.0, 1.0) for dy in C_CHAIR_Y]
    chairs += [(cx + dx, cy + sy * C_END_CHAIR_Y, cx + dx, cy)
               for sy in (-1.0, 1.0) for dx in C_END_CHAIR_X]
    for x, y, tx, ty in chairs:
        W.lifted(mb, z, F.chair, x, y, ang=kit.face_ang(x, y, tx, ty), mat="chair_grey")


def _type_l(mb, room, z):
    """ラウンジ（310・311）: 西の壁ぎわのソファと低い机、丸机と椅子 3 脚の組（spec §1-11）。

    311 は扉を入った先に、弁当を売る白い長机と保温ケースも置く。扉から奥の通路（RD.corridor_y）は空ける。
    """
    xf0, xf1, _, _ = RD.room_faces(room)
    for y in SOFA_Y:
        W.lifted(mb, z, F.sofa, xf0 + SOFA_D * 0.5, y, ang=FACE_E, d=SOFA_D)
        F.table(mb, xf0 + LOW_TABLE_DX, y, ang=math.pi * 0.5, w=1.2, d=0.6, h=0.42,
                top=LOUNGE_TABLE, z=z)
    x = xf0 + ROUND_X0
    while x <= xf1 - ROUND_EAST:
        for y in ROUND_Y:
            _round_set(mb, x, y, z)
        x += ROUND_PITCH
    if room[0] == BENTO_ROOM:
        _bento_stand(mb, z)


def _round_set(mb, x, y, z):
    F.round_table(mb, x, y, r=0.45, h=0.73, top=LOUNGE_TABLE, leg="metal_dark", z=z)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        px, py = x + 0.85 * math.cos(a), y + 0.85 * math.sin(a)
        FL.cafe_chair(mb, kit.T(px, py, z, kit.face_ang(px, py, x, y)))


def _bento_stand(mb, z):
    """弁当を売る長机（正面は南の通路の側）と、その東の半分に載せた保温ケース・西の半分に積んだ弁当。"""
    top = z + 0.72
    F.table(mb, BENTO_X, BENTO_Y, w=BENTO_W, d=BENTO_D, h=0.72, top="desk_white", z=z)
    hd = BENTO_D * 0.5
    FL.warmer_case(mb, kit.T(BENTO_X + 0.9, BENTO_Y + hd - 0.05, top, math.pi), d=BENTO_D - 0.15)
    t = kit.T(BENTO_X, BENTO_Y, top, math.pi)
    for k in range(4):
        x0 = 0.3 + k * 0.3
        for layer in range(2):
            FL.bento(mb, t, x0, x0 + 0.24, -0.2, 0.0, layer * 0.065)


FURNISH = {"A": _type_a, "B": _type_b, "C": _type_c, "L": _type_l}


def _lamps(c, room, n0):
    """室の天井の点光源の Empty（番号は n0 の続き）とプレビューの明かり。戻り値は置いた数。"""
    fl = room[1]
    z = RD.FLOOR_Z[fl]
    pts = RD.lamp_points(room)
    for i, (x, y) in enumerate(pts, 1):
        c._put("lamp_%s_%s_%d" % (c.spec.id, fl, n0 + i), x, y, z + RD.LAMP_DZ)
    c.lights_from(pts, z + RD.CEIL_H)
    return len(pts)


def split_lines(room):
    """室のメッシュを切る線: 奥行きの RD.ROOM_SPLIT_Y と、室の内法に入る明かりの格子の X。"""
    xf0, xf1, _, _ = RD.room_faces(room)
    return [(1, RD.ROOM_SPLIT_Y[room[2]])] + [(0, x) for x in RD.LAMP_GRID_X if xf0 < x < xf1]


def cell(room, p):
    """点 p の入る片の名前（奥の帯は r<室>、手前は r<室>_<k>）。"""
    no, side = room[0], room[2]
    ys = RD.ROOM_SPLIT_Y[side]
    if (p[1] < ys) if side == "S" else (p[1] > ys):
        return "r" + no
    return "r%s_%d" % (no, 1 + sum(1 for x in RD.LAMP_GRID_X if x < p[0]))


def _route(c, room, role, mb):
    """仮に組んだ mb を片ごとの部品（role は floor / wall / furn）へ移す。座面も片へ付け替える。"""
    parts = {}

    def sink(p):
        name = cell(room, p)
        if name not in parts:
            parts[name] = c.furn(name) if role == "furn" else c.part(role, name)
        return parts[name]

    mb.route(split_lines(room), sink)
    for seat in getattr(mb, "seats", ()):
        dst = sink(seat["c"])
        dst.seats.append(dict(seat, vi=len(dst.verts)))   # 頂点はもう移したので、後の kit.lift で動かさない


def _board(c, key, p, front):
    """board_lecture_<key> と、板の正面 front（水平の単位ベクトル）へ 1 m の board_lecture_<key>_f。"""
    name = "board_lecture_" + key
    c._put(name, *p)
    c._put(name + "_f", p[0] + front[0], p[1] + front[1], p[2])


def _board_empty(c, room):
    """黒板・白板の面の中央の Empty。型 C の白板はロの字の机の正面。"""
    _, xf1, yf0, yf1 = RD.room_faces(room)
    cy = C_RING_Y if room[6] == "C" else (yf0 + yf1) * 0.5
    _board(c, room[0], (xf1 - BOARD_FACE[room[6]], cy, RD.FLOOR_Z[room[1]] + BOARD_Z), (-1.0, 0.0))


def _hall_board(c):
    """7F の廊下の南の壁（705 の扉の間）の白板。過去の学生が書き足していく板（#389）。"""
    z = RD.FLOOR_Z[RD.TOP_FLOOR] + BOARD_Z
    y = D.Y_ROOM_S1
    F.whiteboard(W.bands(c, "wall", RD.TOP_FLOOR).at(HALL_BOARD_X, y), HALL_BOARD_X, y, z,
                 w=HALL_BOARD_W)
    _board(c, RD.TOP_FLOOR, (HALL_BOARD_X, y + BOARD_FACE["B"], z), (0.0, 1.0))


def build(c, lamp_count):
    """lamp_count は階 -> すでに置いた点光源の数。戻り値は教室の点光源を足した数。"""
    count = dict(lamp_count)
    for room in RD.ROOMS:
        no, fl = room[0], room[1]
        floor, walls, furn = (MeshBuilder("tmp_" + no) for _ in range(3))
        furn.seats = []
        _floor_ceiling(floor, walls, room)
        _lining(walls, room)
        _outer(walls, room)
        _east(walls, room)
        _west(walls, room)
        FURNISH[room[6]](furn, room, RD.FLOOR_Z[fl])
        PROPS.room(furn, walls, room, RD.FLOOR_Z[fl])
        for role, mb in (("floor", floor), ("wall", walls), ("furn", furn)):
            _route(c, room, role, mb)
        count[fl] += _lamps(c, room, count[fl])
        if room[6] in BOARD_FACE:
            _board_empty(c, room)
    _hall_board(c)
    return count

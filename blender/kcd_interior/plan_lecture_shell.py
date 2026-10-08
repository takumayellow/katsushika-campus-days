"""講義棟の躯体: 外周壁・1F の床スラブと床の塗り分け・南の入口。

外周は直方体ではない（101 の東の段差・東端ホール・生協の北の張り出し・北の斜めの壁）。
外周の閉じ（closure）は外接矩形の内面線で測るので、壁の上端には下向きの蓋を 0.01 上に貼り、
壁の天面に立てないようにする。2F・3F の床と各区画の間仕切りは区画ごとのモジュールが作る。
"""

import math

from . import kit, shell
from . import plan_lecture_dims as D

CONCRETE = "lecture_concrete"
GLASS = "lecture_glass_curtain"
CAP_UP = 0.01        # 壁の天面に貼る蓋の高さ
Z_1F_TOP = D.F2      # 1F だけの壁の上端（2F の床の高さ）
Z_EAST_TOP = D.CEIL_3F   # 東端ホール・東の壁の上端（3F 自習室の天井）


def _n_at(x):
    return (x, D.north_y(x))


# 外周壁の外面の線（厚みは a -> b の左手＝室内側）
SOUTH = ((D.X_W, D.Y_S), (D.X_STEP, D.Y_S))
STEP = ((D.X_STEP, D.Y_S), (D.X_STEP, D.Y_EAST_S + shell.WALL))
EAST_S = ((D.X_STEP, D.Y_EAST_S), (D.X_E, D.Y_EAST_S))
EAST = ((D.X_E, D.Y_EAST_S), (D.X_E, D.Y_N_BUMP))
BUMP_N = ((D.X_E, D.Y_N_BUMP), (D.X_BUMP_W, D.Y_N_BUMP))
BUMP_W = ((D.X_BUMP_W, D.Y_N_BUMP), (D.X_BUMP_W, D.north_y(D.X_BUMP_W)))
NORTH = (_n_at(D.X_BUMP_W + shell.WALL), _n_at(D.X_W))
WEST = ((D.X_W, D.north_y(D.X_W)), (D.X_W, D.Y_S))


def _normal(a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    return (-dy / L, dx / L)


def cap(mb, a, b, z, off0, off1, mat=CONCRETE):
    """a -> b の線から左手へ off0〜off1 の帯に、高さ z + CAP_UP の下向きの蓋を貼る。"""
    n = _normal(a, b)
    pts = [(a[0] + n[0] * off0, a[1] + n[1] * off0), (b[0] + n[0] * off0, b[1] + n[1] * off0),
           (b[0] + n[0] * off1, b[1] + n[1] * off1), (a[0] + n[0] * off1, a[1] + n[1] * off1)]
    zc = z + CAP_UP
    mb.add_face([(p[0], p[1], zc) for p in reversed(pts)], mat)


def wall(c, line, z0, z1, mat=CONCRETE, glass=None, sill=0.95, header=0.45, gaps=(),
         gap_top=None, top=True):
    """外周壁の 1 帯。top=True なら上端に蓋を貼る。"""
    a, b = line
    shell.outer_wall(c.wall, a, b, z0, z1, wall=mat, glass=glass, sill=sill, header=header,
                     seg=3.0, gaps=gaps, gap_top=gap_top)
    if top:
        cap(c.wall, a, b, z1, 0.0, shell.WALL)


def part(c, a, b, z0, z1, mat=CONCRETE, thick=shell.PART, gaps=(), top=True):
    """間仕切り（線の中心に厚み）と天面の蓋。"""
    shell.partition(c.wall, a, b, z0, z1, mat, thick=thick, gaps=gaps)
    if top:
        cap(c.wall, a, b, z1, -thick * 0.5, thick * 0.5)


def _entrance(c):
    """南の入口: 外周壁の開口・ガラスの自動扉・spawn / exit。"""
    d0, d1 = D.DOOR_GAP
    shell.glass_entrance(c.wall, d0, d1, 0.0, D.DOOR_H, D.Y_S + shell.WALL * 0.5, glass=GLASS)
    c.door_gap = (d0, d1)
    c.spawn(*D.SPAWN)
    c.exit(*D.EXIT)


def _outer(c):
    door = (D.DOOR_GAP[0] - D.X_W, D.DOOR_GAP[1] - D.X_W)
    # 南（101・ロビー・102）: 1F の高さの窓の帯。入口の上は扉の高さからふさぐ
    wall(c, SOUTH, 0.0, Z_1F_TOP, glass=GLASS, header=0.9, gaps=[door], gap_top=D.DOOR_H)
    wall(c, STEP, 0.0, Z_1F_TOP)
    # 東端ホール（2 層吹き抜け + 3F）: 1F・2F は窓の帯、3F は腰までの壁の上に窓
    sv0, sv1 = D.SE_VESTIBULE[0] - D.X_STEP, D.SE_VESTIBULE[2] - D.X_STEP
    wall(c, EAST_S, 0.0, D.F2, glass=GLASS, sill=0.0, header=0.8, gaps=[(sv0, sv1)],
         gap_top=D.CEIL_CORR, top=False)
    wall(c, EAST_S, D.F2, D.F3, glass=GLASS, sill=0.0, header=0.6, top=False)
    wall(c, EAST_S, D.F3, Z_EAST_TOP)
    wall(c, EAST, 0.0, D.F2, glass=GLASS, sill=0.95, header=0.8, top=False)
    wall(c, EAST, D.F2, D.F3, glass=GLASS, sill=0.95, header=0.6, top=False)
    wall(c, EAST, D.F3, Z_EAST_TOP)
    # 生協の北の張り出し（北は窓の帯）・北・西
    wall(c, BUMP_N, 0.0, Z_1F_TOP, glass=GLASS, header=1.2)
    wall(c, BUMP_W, 0.0, Z_1F_TOP)
    wall(c, NORTH, 0.0, Z_1F_TOP)
    wall(c, WEST, 0.0, Z_1F_TOP)
    # 南東の風除室の外の扉（閉じたガラス）
    shell.glass_entrance(c.wall, D.SE_VESTIBULE[0], D.SE_VESTIBULE[2], 0.0, D.CEIL_CORR,
                         D.Y_EAST_S + shell.WALL * 0.5, glass=GLASS, door_h=2.4)


def _slab(c, rect, mat):
    shell.floor(c.floor, rect[0], rect[1], rect[2], rect[3], 0.0, mat, thickness=0.30,
                side_mat=CONCRETE)


def _floors(c):
    base = "lecture_carpet_corridor"
    _slab(c, (D.X_W, D.Y_S, D.X_STEP, D.Y_EAST_S), base)
    _slab(c, (D.X_W, D.Y_EAST_S, D.X_BUMP_W, D.N_WALL[1][1]), base)
    _slab(c, (D.X_BUMP_W, D.Y_EAST_S, D.X_E, D.Y_N_BUMP), base)
    # 北の斜めの壁の下まで（台形の上面）
    ya = D.N_WALL[1][1]
    quad = [(D.X_W, ya), (D.X_BUMP_W, ya), _n_at(D.X_BUMP_W), _n_at(D.X_W)]
    c.floor.add_face([(p[0], p[1], 0.0) for p in quad], base)
    # 塗り分け（スラブの上に薄く重ねる）
    x0, x1 = D.LOBBY_X
    kit.plate(c.floor, x0, D.Y_S + shell.WALL, x1, D.Y_ROOM_S1, 0.004, "lecture_lobby_carpet")
    _stone_bands(c)
    kit.plate(c.floor, D.COOP_X[0], D.Y_ROOM_N0, D.COOP_X[1], D.Y_N_BUMP - shell.WALL, 0.004,
              "lecture_coop_floor")


def _stone_bands(c):
    """東端ホールの石の床（明るい帯と暗い帯を東西に交互）。"""
    x0, x1 = D.EAST_X
    y0, y1 = D.Y_EAST_S + shell.WALL, D.Y_ROOM_N0
    kit.plate(c.floor, x0, y0, x1, y1, 0.004, "lecture_stone_light")
    band, pitch = 0.6, 2.4
    y = y0 + pitch
    while y + band < y1:
        kit.plate(c.floor, x0, y, x1, y + band, 0.006, "lecture_stone_dark")
        y += pitch


def build(c):
    _outer(c)
    _entrance(c)
    _floors(c)

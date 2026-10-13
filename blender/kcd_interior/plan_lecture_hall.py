"""101 講義室（spec §4）: 270 席の段床に並べる座席と固定机。

北の扉から入った後ろの通路（z 0）から、南の黒板へ向かって段が下りる。座席は南の黒板を向く。
列 k（1〜10）の踏面は Y 6.05+k〜7.05+k・高さ 0.12k-1.2 で、平場は 1F の床から 1.2 m 掘り下げる。
3 つのブロックに西から机を並べる（西・東は 4 人掛け 2 本、中央は 4+4+3 人掛け）。
割り付け（rows / seat_points）は bpy を使わない関数にして、席数と寸法をテストで確かめる。
"""

import math
from collections import namedtuple

from . import furn_lecture as FL
from . import furniture as F
from . import kit
from . import plan_lecture_dims as D

RUN = 1.0                   # 1 列の踏面の奥行き
SEAT_PITCH = 0.60           # 1 人分の机の幅
DESK_SEATS = 4              # 1 本の机の最大の席数
DESK_D = 0.40               # 机の奥行き
DESK_TOP = (0.68, 0.72)     # 天板の下端・上端（踏面から）
DESK_GAP = 0.02             # 踏面の南端（段鼻）から机の前板まで
SEAT_SET = 0.72             # 踏面の南端から座席の中心まで
SEAT_MAT = "lecture_seat_navy"
FRAME_MAT = "metal_dark"
DESK_MAT = "lecture_desk_ivory"
FLOOR_TOP = "lecture_carpet_hall"
FLOOR_SIDE = "lecture_concrete"
WALL_IN = 0.08              # 段板を壁の中へ差し込む長さ（壁との間に溝を残さない。#42）
FACING = math.pi            # 座席の正面（-Y = 南の黒板）

Row = namedtuple("Row", "k z y0 y1 desks")    # desks: ((x0, x1, 席数), ...)


def block_desks(x0, x1, pitch=SEAT_PITCH, per=DESK_SEATS):
    """ブロック x0〜x1 に西から机を並べる。4 人掛けで埋め、端数は短い机にする。"""
    n = int(round((x1 - x0) / pitch))
    out, x = [], x0
    while n > 0:
        m = min(per, n)
        out.append((x, x + m * pitch, m))
        x += m * pitch
        n -= m
    return tuple(out)


def rows():
    """前（南）から後ろ（北）へ 10 列。どの列も机の並びは同じ。"""
    desks = tuple(d for b in D.HALL_BLOCKS for d in block_desks(*b))
    y_front = D.HALL_FLAT_Y[1]
    return tuple(Row(k, D.HALL_RISE * k - D.HALL_DROP, y_front + RUN * (k - 1), y_front + RUN * k,
                     desks)
                 for k in range(1, D.HALL_ROWS + 1))


def seat_points(rs=None):
    """座席の中心 (x, y, z)。z は踏面の高さ。"""
    out = []
    for r in rs or rows():
        y = r.y0 + SEAT_SET
        for x0, _x1, n in r.desks:
            out.extend((x0 + (i + 0.5) * SEAT_PITCH, y, r.z) for i in range(n))
    return out


def _desk(mb, x0, x1, r):
    """固定机: 天板と南向きの前板（12 三角形）。"""
    yf, yb = r.y0 + DESK_GAP, r.y0 + DESK_GAP + DESK_D
    za, zb = r.z + DESK_TOP[0], r.z + DESK_TOP[1]
    kit.box_nb(mb, x0 + 0.01, yf, za, x1 - 0.01, yb, zb, DESK_MAT)
    mb.add_face([(x0 + 0.01, yf, r.z), (x1 - 0.01, yf, r.z), (x1 - 0.01, yf, za),
                 (x0 + 0.01, yf, za)], DESK_MAT)


def build_seats(c, seats_mb, desks_mb):
    """座席と机を置き、c.seats に席数を足す。戻り値は席数。"""
    rs = rows()
    pts = seat_points(rs)
    for x, y, z in pts:
        FL.shell_seat(seats_mb, kit.T(x, y, z, FACING), seat=SEAT_MAT, frame=FRAME_MAT)
    for r in rs:
        for x0, x1, _n in r.desks:
            _desk(desks_mb, x0, x1, r)
    c.seats += len(pts)
    return len(pts)


def tiers():
    """段床の板 (x0, y0, x1, y1, z)。列 1〜10 の踏面の順（南から北へ）。列 10 は後ろの通路と同じ高さ。"""
    x0, x1 = D.R101_X[0] - WALL_IN, D.R101_X[1] + WALL_IN
    return tuple((x0, r.y0, x1, r.y1, r.z) for r in rows())


def build_floor(c):
    """段床（踏面はカーペット、蹴込みはコンクリート）と平場を floor_lecture_pit に作る。

    この部品は 1F の床より下にあるので、Unity は部品の範囲だけ屋内の下の地面を抜く（GroundTiles）。
    後ろの通路は 1F の床の上にカーペットを薄く重ねる。
    """
    pit = c.part("floor", "pit")
    for x0, y0, x1, y1, z in tiers():
        pit.add_prism([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], -D.HALL_DROP, z, FLOOR_SIDE,
                      FLOOR_TOP)
    kit.plate(pit, D.R101_X[0], D.HALL_FLAT_Y[0], D.R101_X[1], D.HALL_FLAT_Y[1], -D.HALL_DROP,
              FLOOR_TOP)
    kit.plate(c.floor, D.R101_X[0], D.HALL_BACK_Y[0], D.R101_X[1], D.HALL_BACK_Y[1], 0.004,
              FLOOR_TOP)

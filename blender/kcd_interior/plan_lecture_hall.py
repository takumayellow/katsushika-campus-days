"""101 講義室（spec §4）: 270 席の段床に並べる座席と固定机。

座席は北の黒板を向く。列 k（1〜10）の踏面は Y 16.9-k〜17.9-k・高さ 0.12k で、
3 つのブロックに西から机を並べる（西・東は 4 人掛け 2 本、中央は 4+4+3 人掛け）。
割り付け（rows / seat_points）は bpy を使わない関数にして、席数と寸法をテストで確かめる。
"""

from collections import namedtuple

from . import furniture as F
from . import kit
from . import plan_lecture_dims as D

RUN = 1.0                   # 1 列の踏面の奥行き
SEAT_PITCH = 0.60           # 1 人分の机の幅
DESK_SEATS = 4              # 1 本の机の最大の席数
DESK_D = 0.40               # 机の奥行き
DESK_TOP = (0.68, 0.72)     # 天板の下端・上端（踏面から）
DESK_GAP = 0.02             # 踏面の北端（段鼻）から机の前板まで
SEAT_SET = 0.72             # 踏面の北端から座席の中心まで
SEAT_MAT = "lecture_seat_navy"
FRAME_MAT = "metal_dark"
DESK_MAT = "lecture_desk_ivory"

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
    """前（北）から後ろへ 10 列。どの列も机の並びは同じ。"""
    desks = tuple(d for b in D.HALL_BLOCKS for d in block_desks(*b))
    y_front = D.HALL_FLAT_Y[0]
    return tuple(Row(k, D.HALL_RISE * k, y_front - RUN * k, y_front - RUN * (k - 1), desks)
                 for k in range(1, D.HALL_ROWS + 1))


def seat_points(rs=None):
    """座席の中心 (x, y, z)。z は踏面の高さ。"""
    out = []
    for r in rs or rows():
        y = r.y1 - SEAT_SET
        for x0, _x1, n in r.desks:
            out.extend((x0 + (i + 0.5) * SEAT_PITCH, y, r.z) for i in range(n))
    return out


def _desk(mb, x0, x1, r):
    """固定机: 天板と北向きの前板（12 三角形）。"""
    yb, yf = r.y1 - DESK_GAP - DESK_D, r.y1 - DESK_GAP
    za, zb = r.z + DESK_TOP[0], r.z + DESK_TOP[1]
    kit.box_nb(mb, x0 + 0.01, yb, za, x1 - 0.01, yf, zb, DESK_MAT)
    mb.add_face([(x1 - 0.01, yf, r.z), (x0 + 0.01, yf, r.z), (x0 + 0.01, yf, za),
                 (x1 - 0.01, yf, za)], DESK_MAT)


def build_seats(c, seats_mb, desks_mb):
    """座席と机を置き、c.seats に席数を足す。戻り値は席数。"""
    rs = rows()
    pts = seat_points(rs)
    for x, y, z in pts:
        F.hall_seat(seats_mb, kit.T(x, y, z), mat=SEAT_MAT, frame=FRAME_MAT)
    for r in rs:
        for x0, x1, _n in r.desks:
            _desk(desks_mb, x0, x1, r)
    c.seats += len(pts)
    return len(pts)

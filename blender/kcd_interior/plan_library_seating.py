"""図書館棟 ホールの客席: 段床・通路の小段・1 階席 480 席・2 階席と側方の 2 階席。

1 階席は中央 12 席と左右のブロック（前 7 席から後 13 席）を 15 列。列の段差は 0.2 で、
縦通路にはその中間の 0.1 の小段を置く。2 階席は 3 段（0.4 ずつ）で、通路に 3 分割の小段。
"""

import math

from . import kit
from . import plan_library_dims as D

CARPET = "library_aisle_carpet"
RISER = "library_seat_wood"
FABRIC = "library_seat_fabric"
SWOOD = "library_seat_wood"
FRONT = "library_balcony_front"
WHITE = "library_ceiling_white"
ZB = D.Z3 - 0.22
W = D.SEAT_W
CX0 = D.HALL_AXIS - D.CENTRE_N * W * 0.5     # 中央ブロックの西端
CX1 = D.HALL_AXIS + D.CENTRE_N * W * 0.5
STEP_D = 0.55                                # 通路の小段の奥行き
# 2 階席のブロック（西端, 席数）と縦通路（x0, x1）
BALC_BLOCKS = ((-11.875, 8), (-6.275, 17), (4.275, 8))
BALC_AISLES = ((-13.075, -11.875), (-7.475, -6.275), (3.075, 4.275), (8.675, 9.875))
SIDE_N = 9
PARAPET_Z = 16.95


def _seat_row(mb, t, n):
    """t の原点から +X へ n 席（正面 +Y）。木の脚の帯・座面・背もたれ。"""
    L = n * W
    t.box_nb(mb, 0.0, -0.15, 0.0, L, 0.15, 0.36, SWOOD)
    t.box_nb(mb, 0.0, -0.22, 0.36, L, 0.22, 0.42, FABRIC)
    for i in range(n):
        a, b = i * W, (i + 1) * W
        t.box_nb(mb, a + 0.02, -0.40, 0.36, b - 0.02, -0.30, 0.95, SWOOD)
        t.vplate(mb, b - 0.04, -0.295, a + 0.04, -0.295, 0.45, 0.9, FABRIC)


def _band(mb, y0, y1, z):
    mb.add_prism(D.fan_poly(y0, y1), ZB, z, RISER, CARPET, None)


def _rows():
    return [r for r in range(1, 16)]


def _block_ends(r):
    n = D.side_n(r)
    return CX0 - D.AISLE_W - n * W, CX1 + D.AISLE_W + n * W


def _aisle_steps(mb, r):
    """r 列目の後ろ半分（1 つ後ろの列の側）の、縦通路と脇の通路の小段。"""
    y0, _ = D.row_y(r)
    ya, yb = y0, y0 + STEP_D
    z0 = D.row_z(r)
    z1 = z0 + 0.1
    for x0, x1 in ((CX0 - D.AISLE_W, CX0), (CX1, CX1 + D.AISLE_W)):
        kit.box(mb, x0, ya, z0, x1, yb, z1, RISER, top=CARPET)
    w0, e1 = _block_ends(r)
    west = [(w0, ya), (w0, yb)] + D.wall_edge(D.HALL_WALL_W, ya, yb)[::-1]
    east = D.wall_edge(D.HALL_WALL_E, ya, yb) + [(e1, yb), (e1, ya)]
    for poly in (west, east):
        mb.add_prism(poly, z0, z1, RISER, CARPET, None)


def _floor(mb):
    _band(mb, D.REAR_AISLE[0], D.REAR_AISLE[1], D.Z_ANTE)
    for r in _rows():
        y0, y1 = D.row_y(r)
        _band(mb, y0, y1, D.row_z(r))
    _band(mb, D.CROSS[0], D.CROSS[1], D.CROSS[2])
    _band(mb, D.STAGE_FRONT[0], D.STAGE_FRONT[1], D.row_z(1))
    for r in _rows():
        if r not in (7, 15):
            _aisle_steps(mb, r)


def _hall_seats(mb):
    for r in _rows():
        y0, _ = D.row_y(r)
        yc, z = y0 + 0.7, D.row_z(r)
        n = D.side_n(r)
        w0, _ = _block_ends(r)
        _seat_row(mb, kit.T(CX0, yc, z, 0.0), D.CENTRE_N)
        _seat_row(mb, kit.T(w0, yc, z, 0.0), n)
        _seat_row(mb, kit.T(CX1 + D.AISLE_W, yc, z, 0.0), n)


def build_seats(c, mb):
    """mb は furn_library_14_seats。"""
    _floor(mb)
    _hall_seats(mb)


# ---- 2 階席 ----
def _tiers(mb):
    xi0, xi1 = D.HALL_IN[0], D.HALL_IN[2]
    d = (D.BALCONY[0][1] - D.BALCONY[0][0]) / 3.0
    for y0, y1, z in D.BALCONY:
        kit.box(mb, xi0, y0, D.C_UNDER, xi1, y1, z, FRONT, top=CARPET, bottom=WHITE)
        for x0, x1 in BALC_AISLES:
            for k, h in ((0, 0.4 * 2.0 / 3.0), (1, 0.4 / 3.0)):
                poly = [(x0, y0 + d * k), (x1, y0 + d * k), (x1, y0 + d * (k + 1)),
                        (x0, y0 + d * (k + 1))]
                mb.add_prism(poly, z, z + h, RISER, CARPET, None)
        for x0, n in BALC_BLOCKS:
            _seat_row(mb, kit.T(x0, y0 + 0.55, z, 0.0), n)
    # 通路と最上段の境の腰壁（縦通路の口は空ける）
    ya = D.BALC_AISLE[1]
    z = D.BALCONY[-1][2]
    edges = [xi0] + [x for a in BALC_AISLES for x in a] + [xi1]
    for x0, x1 in zip(edges[0::2], edges[1::2]):
        kit.box(mb, x0, ya, z, x1, ya + 0.08, 18.0, FRONT, top=SWOOD)


def _side_balconies(mb):
    (wx0, wx1), (ex0, ex1), (y0, y1), z = D.SIDE_BALC
    for x0, x1 in ((wx0, wx1), (ex0, ex1)):
        kit.box(mb, x0, y0, D.C_UNDER, x1, y1, z, FRONT, top=CARPET, bottom=WHITE)
    L = SIDE_N * W
    _seat_row(mb, kit.T(-17.4, y0 + 0.35 + L, z, -math.pi * 0.5), SIDE_N)
    _seat_row(mb, kit.T(2.0 * D.HALL_AXIS + 17.4, y0 + 0.35, z, math.pi * 0.5), SIDE_N)


def _parapets(mb):
    (_, wx1), (ex0, _), (y0, y1), _ = D.SIDE_BALC
    zb = D.C_UNDER
    kit.box(mb, wx1, y0, zb, ex0, y0 + 0.15, PARAPET_Z, FRONT, top=SWOOD)
    kit.box(mb, wx1, y0, zb, wx1 + 0.15, y1, PARAPET_Z, FRONT, top=SWOOD)
    kit.box(mb, ex0 - 0.15, y0, zb, ex0, y1, PARAPET_Z, FRONT, top=SWOOD)
    # 手すりの下端を光らせる
    kit.box(mb, wx1, y0 + 0.02, zb - 0.06, ex0, y0 + 0.13, zb, "library_wall_washer")


def build_balcony(c, mb):
    """mb は furn_library_15_balcony。"""
    _tiers(mb)
    _side_balconies(mb)
    _parapets(mb)
    for x in (-12.0, D.HALL_AXIS, 8.8):
        c.light(x, 32.5, 19.6, 300.0, 1.6)

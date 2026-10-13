"""図書館棟のブックギャラリー: L 字の段（M1・M2）の縁・本の壁・手すり・机・階段 S1〜S6。

段の塊そのもの（床と側面）は plan_library_shell が作る。ここでは段の立ち上がりに
本の壁を貼り、縁にガラスの手すりを回し、上の段に机と椅子を置く。吹き抜けに面した
2F の縁（西の縁を除く）の手すりと、1F 手前の文庫・新書の棚もここで作る。
"""

import math

from . import kit
from . import furn_library as FL
from . import plan_library_dims as D

DESK_TOP = "library_desk_top_dark"
WOOD = FL.WOOD
FACE_W = math.pi * 0.5       # 正面 -X
FACE_S = math.pi             # 正面 -Y
FACE_E = -math.pi * 0.5      # 正面 +X

_STAIRS = {s[0]: s[1:] for s in D.STAIRS}


def _stair_span(name):
    """階段の幅方向の範囲（手すり・本の壁を切る区間）。"""
    x0, y0, x1, y1, _z0, _z1, _n, up = _STAIRS[name]
    return (y0, y1) if up in ("+x", "-x") else (x0, x1)


def _cut(s0, s1, gaps):
    """区間 s0..s1 から gaps を抜いた残りの区間の列（s0 > s1 なら逆向きのまま返す）。"""
    lo, hi = min(s0, s1), max(s0, s1)
    out, cur = [], lo
    for g0, g1 in sorted(gaps):
        if g1 <= cur or g0 >= hi:
            continue
        if g0 - cur > 0.05:
            out.append((cur, g0))
        cur = max(cur, g1)
    if hi - cur > 0.05:
        out.append((cur, hi))
    if s0 > s1:
        out = [(b, a) for a, b in reversed(out)]
    return out


def _rail_x(mb, x, y0, y1, z, gaps=()):
    """x 一定の線に沿うガラス手すり（gaps は Y の区間）。"""
    for a, b in _cut(y0, y1, gaps):
        FL.glass_rail(mb, [(x, a), (x, b)], z)


def _rail_y(mb, y, x0, x1, z, gaps=()):
    for a, b in _cut(x0, x1, gaps):
        FL.glass_rail(mb, [(a, y), (b, y)], z)


def _book_x(mb, x, y0, y1, z0, z1, rng, gaps=()):
    """x 一定の立ち上がりに本の壁（-X を向く）。"""
    for a, b in _cut(y0, y1, gaps):
        FL.book_wall(mb, (x, a), (x, b), z0, z1, rng)


def _book_y(mb, y, x0, x1, z0, z1, rng, gaps=()):
    """y 一定の立ち上がりに本の壁（-Y を向く）。"""
    for a, b in _cut(x1, x0, gaps):
        FL.book_wall(mb, (a, y), (b, y), z0, z1, rng)


# ---- 段の縁 ----
def _risers(mb, rng):
    (_, z1, (e1x0, _e1x1), (n1y0, _)), (_, z2, (e2x0, e2x1), (n2y0, n2y1)) = D.TIERS
    x_w, y0 = -9.0, D.TIER_Y0
    # 1F → M1
    _book_x(mb, e1x0, y0, n1y0, D.Z1, z1, rng, gaps=[_stair_span("S1")])
    _book_y(mb, n1y0, x_w, e1x0, D.Z1, z1, rng, gaps=[_stair_span("S2")])
    # M1 → M2
    _book_x(mb, e2x0, y0, n2y0, z1, z2, rng, gaps=[_stair_span("S3")])
    _book_y(mb, n2y0, x_w, e2x0, z1, z2, rng, gaps=[_stair_span("S6")])
    # M2 → 2F
    _book_x(mb, e2x1, y0, n2y1, z2, D.Z2, rng, gaps=[_stair_span("S4")])
    _book_y(mb, n2y1, x_w, e2x1, z2, D.Z2, rng, gaps=[_stair_span("S5")])


def _tier_rails(mb):
    (_, z1, (e1x0, e1x1), (n1y0, n1y1)), (_, z2, (e2x0, e2x1), (n2y0, n2y1)) = D.TIERS
    x_w, y0, d = -9.0, D.TIER_Y0, 0.05
    # M1 の縁（1F へ）・南の端・西の端
    _rail_x(mb, e1x0 + d, y0 + d, n1y0 + d, z1, gaps=[_stair_span("S1")])
    _rail_y(mb, n1y0 + d, x_w + d, e1x0 + d, z1, gaps=[_stair_span("S2")])
    _rail_y(mb, y0 + d, e1x0 + d, e1x1, z1)
    _rail_x(mb, x_w + d, n1y0 + d, n1y1, z1)
    # M2 の縁（M1 へ）・南の端・西の端
    _rail_x(mb, e2x0 + d, y0 + d, n2y0 + d, z2, gaps=[_stair_span("S3")])
    _rail_y(mb, n2y0 + d, x_w + d, e2x0 + d, z2, gaps=[_stair_span("S6")])
    _rail_y(mb, y0 + d, e2x0 + d, e2x1, z2)
    _rail_x(mb, x_w + d, n2y0 + d, n2y1, z2)


def _upper_rails(mb):
    """吹き抜けに面した 2F の縁（西の縁 X -9 は 2F のモジュール）。"""
    _, _, (_e2x0, e2x1), (_n2y0, n2y1) = D.TIERS[1]
    z, d = D.Z2, 0.05
    ax0, ay0, ax1, ay1 = D.ATRIUM
    _rail_y(mb, ay0 - d, ax0, ax1, z)                     # 正面のブリッジ
    _rail_x(mb, ax1 + d, ay0 - d, D.TIER_Y0 + d, z)       # 東の閲覧席の縁
    _rail_y(mb, D.TIER_Y0 + d, e2x1, ax1 + d, z)          # 東の帯の南の端
    _rail_x(mb, e2x1 + d, D.TIER_Y0 + d, n2y1 + d, z, gaps=[_stair_span("S4")])
    _rail_y(mb, n2y1 + d, ax0, e2x1 + d, z, gaps=[_stair_span("S5")])


# ---- 階段 ----
def _stairs(mb):
    for name, (x0, y0, x1, y1, z0, z1, n, up) in _STAIRS.items():
        FL.stair(mb, x0, y0, x1, y1, z0, z1, n, up)
        FL.stair_rails(mb, x0, y0, x1, y1, z0, z1, up)


# ---- 机・椅子 ----
def _m1_desks(mb):
    """M1 の縁に沿うカウンター机と白い椅子（座面 09・10 はこの椅子）。"""
    (_, z, (ex0, _), (ny0, _)) = D.TIERS[0]
    x0, x1 = ex0 + 0.12, ex0 + 0.6
    for ya, yb in ((15.6, 19.6), (22.4, 33.0)):
        FL.desk(mb, x0, ya, x1, yb, z, top=DESK_TOP)
    for y in (16.4, 18.0, 23.0, 24.0, 27.0, 30.0, 31.8):
        FL.chair(mb, ex0 + 1.0, y, z, FACE_W)
    # 北の腕（S2・S6 を避ける）
    y0, y1 = ny0 + 0.12, ny0 + 0.6
    for xa, xb in ((-8.4, -0.5), (4.8, 7.4)):
        FL.desk(mb, xa, y0, xb, y1, z, top=DESK_TOP)
    for x in (-7.6, -6.0, -3.2, -1.6, 5.6, 6.8):
        FL.chair(mb, x, ny0 + 1.0, z, FACE_S)
    for y in (25.0, 29.0):
        FL.desk_light(mb, ex0 + 0.35, y, z + 0.72, FACE_E)


def _m2_chairs(mb):
    """M2 の黄緑のタブチェア（吹き抜けを向く）。"""
    (_, z, (ex0, _), (ny0, _)) = D.TIERS[1]
    for y in (18.6, 19.6, 22.2, 23.2, 26.0, 27.0, 37.0, 38.0, 40.6):
        FL.tab_chair(mb, ex0 + 1.15, y, z, FACE_W)
    for x in (-8.2, -7.4, -2.0, -1.0, 6.4, 7.4):
        FL.tab_chair(mb, x, ny0 + 1.15, z, FACE_S)


# ---- 文庫・新書 ----
def _paperbacks(mb, rng):
    """1F 手前の明るい木の低い書架（両面）と、看板「文庫・新書」の板。"""
    y_back = 9.05
    for xc in (10.0, 13.5, 17.0):
        t = kit.T(xc, y_back - 0.3, 0.0, 0.0)
        FL.wall_shelf(mb, t, 3.2, 1.35, 0.3, rng, levels=4, frame=WOOD)
        tb = kit.T(xc, y_back - 0.3, 0.0, math.pi)
        FL.wall_shelf(mb, tb, 3.2, 1.35, 0.3, rng, levels=4, frame=WOOD)
    kit.box(mb, 12.3, y_back - 0.1, 1.35, 14.7, y_back, 2.6, WOOD)


def build(c, mb):
    """mb は furn_library_04_gallery。"""
    rng = c.rng
    _risers(mb, rng)
    _tier_rails(mb)
    _upper_rails(mb)
    _stairs(mb)
    _m1_desks(mb)
    _m2_chairs(mb)
    _paperbacks(mb, rng)

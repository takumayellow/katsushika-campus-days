"""体育館の躯体: 外周壁・床・天井・間仕切り・風除室。

外周は FOOT の各辺に壁を立てる（厚みは a -> b の左手＝室内側）。アリーナの屋根は C_HALL、
2F の室は C_2F、東棟は C_EAST。観覧席・ギャラリーなどの 2F の床は plan_gym_stand が作る。
"""

import math

from . import kit, shell
from . import plan_gym_dims as D

WHITE = "wall_white"
CONC = "gym_concrete_panel"
GREY = "gym_wall_grey"
WOOD_L = "gym_wall_wood_lower"
WOOD_U = "gym_wall_wood_upper"
GLASS = "glass_interior"
BLACK = "gym_mullion_black"
CEIL = "gym_ceiling_offwhite"
CAP_UP = 0.01
HALF_PART = shell.PART * 0.5
Z_BASE = -0.22


def _normal(a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    n = math.hypot(dx, dy)
    return (-dy / n, dx / n)


def cap(mb, a, b, z, off0, off1, mat=CONC):
    """a -> b の線から左手へ off0〜off1 の帯に、高さ z + CAP_UP の下向きの蓋を貼る。"""
    n = _normal(a, b)
    pts = [(a[0] + n[0] * off0, a[1] + n[1] * off0), (b[0] + n[0] * off0, b[1] + n[1] * off0),
           (b[0] + n[0] * off1, b[1] + n[1] * off1), (a[0] + n[0] * off1, a[1] + n[1] * off1)]
    mb.add_face([(p[0], p[1], z + CAP_UP) for p in reversed(pts)], mat)


def wall(c, line, z0, z1, mat=WHITE, top=True, **kw):
    """外周壁の 1 帯（line = (a, b)）。top=True なら上端に蓋を貼る。"""
    a, b = line
    shell.outer_wall(c.wall, a, b, z0, z1, wall=mat, **kw)
    if top:
        cap(c.wall, a, b, z1, 0.0, shell.WALL)


def along(a, b, p):
    """a -> b の線上の点 p までの距離。"""
    return math.hypot(p[0] - a[0], p[1] - a[1])


def part(c, a, b, z0, z1, mat=WHITE, gaps=(), lintel=None, top=True):
    """間仕切り（線の中心に厚み）と天面の蓋。lintel を与えると開口の上を z1 までふさぐ。"""
    shell.partition(c.wall, a, b, z0, z1, mat, thick=shell.PART, gaps=gaps)
    if lintel is not None and gaps:
        n = _normal(a, b)
        for s, e in gaps:
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            p0 = (a[0] + (b[0] - a[0]) * s / L, a[1] + (b[1] - a[1]) * s / L)
            p1 = (a[0] + (b[0] - a[0]) * e / L, a[1] + (b[1] - a[1]) * e / L)
            poly = [(p0[0] - n[0] * HALF_PART, p0[1] - n[1] * HALF_PART),
                    (p1[0] - n[0] * HALF_PART, p1[1] - n[1] * HALF_PART),
                    (p1[0] + n[0] * HALF_PART, p1[1] + n[1] * HALF_PART),
                    (p0[0] + n[0] * HALF_PART, p0[1] + n[1] * HALF_PART)]
            c.wall.add_prism(poly, lintel, z1, mat, mat, mat)
    if top:
        cap(c.wall, a, b, z1, -HALF_PART, HALF_PART)


def xwall(mb, x0, x1, y0, y1, z0, z1, mat=WHITE, gaps=(), lintel=None):
    """x 方向に延びる厚み y0..y1 の壁。gaps は x の範囲で、lintel 以上をふさぐ（None なら全高が開口）。"""
    xs = x0
    for g0, g1 in sorted(gaps):
        kit.box(mb, xs, y0, z0, g0, y1, z1, mat)
        if lintel is not None:
            kit.box(mb, g0, y0, lintel, g1, y1, z1, mat)
        xs = g1
    kit.box(mb, xs, y0, z0, x1, y1, z1, mat)


def ywall(mb, x0, x1, y0, y1, z0, z1, mat=WHITE, gaps=(), lintel=None):
    """y 方向に延びる厚み x0..x1 の壁。gaps は y の範囲。"""
    ys = y0
    for g0, g1 in sorted(gaps):
        kit.box(mb, x0, ys, z0, x1, g0, z1, mat)
        if lintel is not None:
            kit.box(mb, x0, g0, lintel, x1, g1, z1, mat)
        ys = g1
    kit.box(mb, x0, ys, z0, x1, y1, z1, mat)


# ---- 外周 ----
def _south(c):
    """辺 13（南壁）: アリーナ側・玄関・東の 2F 側。"""
    E = D.E_SOUTH
    a, b = D.edge_span(E, 0, -27.8, -4.0)
    wall(c, (a, b), 0.0, 3.0, WOOD_L)
    a, b = D.edge_span(E, 0, -27.8, -4.0)
    wall(c, (a, b), 3.0, D.C_HALL, WOOD_U, top=False)
    # 玄関（風除室の外側）
    a, b = D.edge_span(E, 0, -4.0, 2.3)
    s0 = along(a, b, D.edge_point(E, 0, D.DOOR[0]))
    s1 = along(a, b, D.edge_point(E, 0, D.DOOR[1]))
    wall(c, (a, b), 0.0, D.C_HALL, WHITE, gaps=[(s0, s1)], gap_top=D.DOOR_TOP)
    y = D.edge_point(E, 0, 0.0)[1] + 0.15
    shell.glass_entrance(c.wall, D.DOOR[0], D.DOOR[1], 0.0, D.DOOR_TOP, y,
                         frame=BLACK, glass=GLASS, seg=1.6, door_h=2.4)
    c.door_gap = D.DOOR
    a, b = D.edge_span(E, 0, 2.3, 8.9)
    wall(c, (a, b), 0.0, D.C_2F, WHITE)


def _north(c):
    """辺 11（北壁）: 2F ホール・エントランスホール・通路・用具庫で高さが違う。"""
    E = D.E_NORTH
    for x0, x1, top, mat in ((7.3, 2.3, D.C_2F, WHITE), (2.3, 2.0, D.C_FOYER_E, WHITE),
                             (2.0, -3.7, D.C_STAND + 0.4, WHITE), (-3.7, -7.1, 3.4, WHITE),
                             (-7.1, -27.3, D.C_STORE + 0.3, WHITE)):
        a, b = D.edge_span(E, 0, x0, x1)
        wall(c, (a, b), 0.0, top, mat)


def _west(c):
    """辺 12（西壁 G）: 連続窓（床上 0.45〜2.55）・木の腰壁と上壁。"""
    E = D.E_WEST
    y0, y1 = D.GLASS_Y
    a, b = D.edge_span(E, 1, 48.2, y1)
    wall(c, (a, b), 0.0, D.C_STORE + 0.3, WHITE, top=False)
    a, b = D.edge_span(E, 1, y1, y0)
    wall(c, (a, b), 0.0, 3.0, WOOD_L, glass=GLASS, sill=D.GLASS_Z[0], header=3.0 - D.GLASS_Z[1],
         seg=1.5, inner_mat=WOOD_L, mullion=0.08, mullion_mat=BLACK, top=False)
    a, b = D.edge_span(E, 1, y1, y0)
    wall(c, (a, b), 3.0, D.C_HALL, WOOD_U, top=False)
    a, b = D.edge_span(E, 1, y0, 4.6)
    wall(c, (a, b), 0.0, 3.0, WOOD_L, top=False)
    wall(c, (a, b), 3.0, D.C_HALL, WOOD_U, top=False)
    a, b = D.edge_span(E, 1, 48.2, y1)
    wall(c, (a, b), D.C_STORE + 0.3, D.C_HALL, WOOD_U, top=False)


def _east_side(c):
    """東棟と 2F 側の外周。"""
    F = D.FOOT
    n = len(F)
    # 辺 0・8〜10: エントランスホールの東壁（2F の室まで）
    a, b = F[0], F[1]
    wall(c, (a, b), 0.0, D.C_2F, WHITE)
    # 辺 8: 1F はガラス窓、2F は白壁に水平窓（トレーニングルーム）
    a, b = F[8], F[9]
    wall(c, (a, b), 0.0, D.C_FOYER_E, WHITE, glass=GLASS, sill=0.9, header=0.7, seg=3.0,
         mullion=0.08, mullion_mat=BLACK)
    ya, yb = 14.8, 29.6
    a, b = D.edge_span(8, 1, ya, yb)
    wall(c, (a, b), D.C_FOYER_E, D.C_2F, WHITE, glass=GLASS, sill=5.7 - D.C_FOYER_E,
         header=D.C_2F - 7.0, seg=1e9, mullion_mat=BLACK)
    a, b = D.edge_span(8, 1, yb, 38.6)
    wall(c, (a, b), D.C_FOYER_E, D.C_2F, WHITE)
    for i in (9, 10):
        wall(c, (F[i], F[(i + 1) % n]), 0.0, D.C_2F, WHITE)
    # 連絡通路の 2F の壁（開口の上と、2F の室の東壁）
    kit.box(c.wall, 8.6, 9.4, C_CONN_TOP, 8.9, 14.8, D.C_2F, WHITE)
    # 東棟（天井 C_EAST）
    for i in (1, 2, 3, 4, 7):
        wall(c, (F[i], F[(i + 1) % n]), 0.0, D.C_EAST, WHITE)
    # 辺 5（北）・辺 6（廊下の西壁の北）
    wall(c, (F[5], F[6]), 0.0, D.C_EAST, WHITE)
    wall(c, (F[6], F[7]), 0.0, D.C_EAST, WHITE)


C_CONN_TOP = D.C_EAST     # 連絡通路の開口の上端


def _outer(c):
    _south(c)
    _north(c)
    _west(c)
    _east_side(c)


# ---- 床 ----
def floors(c):
    """全域のスラブ（FOOT の外形）と、アリーナの床・東棟のサブアリーナの床。"""
    foot = [tuple(p) for p in D.FOOT]
    c.floor.add_prism(foot, Z_BASE, 0.0, "concrete_light", "gym_floor_grey", "concrete_light")
    z = 0.010
    s0 = D.ARENA[1]
    x1 = D.WALL_B[0]
    ya, yb = D.APRON_Y1, D.ARENA[3]
    wx = lambda y: D.west_x(y) + 0.01

    def quad(ya_, yb_, mat):
        pts = [(wx(ya_), D.south_y(wx(ya_)) if ya_ == s0 else ya_), (x1, D.south_y(x1) if ya_ == s0 else ya_),
               (x1, yb_), (wx(yb_), yb_)]
        c.floor.add_face([(p[0], p[1], z) for p in pts], mat)

    quad(s0, ya, "gym_floor_apron")
    quad(ya, yb, "gym_floor_wood")
    # サブアリーナ（レジン床）
    x0, y0, x1s, y1 = D.SUB
    kit.plate(c.floor, x0, y0, x1s + 0.4, y1, z, "gym_floor_resin_yellow")
    # 用具庫の床（ゴム）
    sx0, sy0, sx1, sy1 = D.STORE
    kit.plate(c.floor, sx0 + 0.3, sy0, sx1, sy1, z, "gym_floor_grey")


def _clip(poly, x0, y0, x1, y1):
    """多角形 poly を矩形（x0, y0, x1, y1）で切る（Sutherland-Hodgman。凹でもよい）。"""
    def cut(pts, inside, cross):
        out = []
        for i, p in enumerate(pts):
            q = pts[(i + 1) % len(pts)]
            pi, qi = inside(p), inside(q)
            if pi:
                out.append(p)
            if pi != qi:
                out.append(cross(p, q))
        return out

    def at_x(v):
        return lambda p, q: (v, p[1] + (q[1] - p[1]) * (v - p[0]) / (q[0] - p[0]))

    def at_y(v):
        return lambda p, q: (p[0] + (q[0] - p[0]) * (v - p[1]) / (q[1] - p[1]), v)

    pts = [tuple(p) for p in poly]
    for inside, cross in ((lambda p: p[0] >= x0, at_x(x0)), (lambda p: p[0] <= x1, at_x(x1)),
                          (lambda p: p[1] >= y0, at_y(y0)), (lambda p: p[1] <= y1, at_y(y1))):
        pts = cut(pts, inside, cross)
    return pts


def _ceil(c, x0, y0, x1, y1, z, mat):
    """矩形を FOOT の内側に切った天井板。下面だけを室内に見せる。"""
    pts = _clip(D.FOOT, x0, y0, x1, y1)
    if len(pts) >= 3:
        c.wall.add_prism(pts, z, z + 0.10, CEIL, CEIL, mat)


def ceilings(c):
    ax0 = -27.8
    # アリーナの屋根（トラスの上）
    _ceil(c, ax0, 4.5, 2.3, 39.3, D.C_HALL, "gym_ceiling_dark")
    # エントランスホールの西側（観覧席の下）
    _ceil(c, -3.7, 4.7, 2.0, 47.6, D.C_STAND, "gym_ceiling_offwhite")
    # 2F の室
    _ceil(c, 2.3, 4.7, 8.9, 38.6, D.C_2F, "gym_ceiling_offwhite")
    _ceil(c, 2.3, 38.6, 7.2, 47.6, D.C_2F, "gym_ceiling_offwhite")
    # 用具庫・北の通路
    _ceil(c, ax0, 39.3, -7.1, 48.0, D.C_STORE, "gym_ceiling_offwhite")
    _ceil(c, -7.1, 39.3, -3.7, 48.0, D.C_PASS, "gym_ceiling_offwhite")
    # 東棟（連絡通路・廊下・各室）
    _ceil(c, 8.6, 9.4, 13.4, 14.8, D.C_EAST, "gym_ceiling_offwhite")
    _ceil(c, 13.0, 3.6, 27.8, 41.5, D.C_EAST, "gym_ceiling_offwhite")


def deck(c, holes_stair=True):
    """2F の床（トレーニングルーム・書庫・ホール）。階段の穴を抜く。"""
    hole = [D.STAIR] if holes_stair else []
    for rect in ((2.0, 4.7, 8.6, 14.8), (2.0, 14.8, 8.9, 38.6), (2.0, 38.6, 7.2, 47.6)):
        shell.floor(c.wall, *rect, D.Z2, "gym_floor_grey", thickness=D.Z2 - D.C_FOYER_E,
                    side_mat=CEIL, holes=hole)


def build(c):
    _outer(c)
    floors(c)
    ceilings(c)
    deck(c)

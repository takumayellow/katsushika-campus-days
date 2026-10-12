"""講義棟の東端の上階（spec §1-6）: 2F の渡りと L 字のラウンジ、3F の弧の自習室とテラス。

2F: 直線階段の上から北へ渡り（X 39.8〜49.52・Y 30.4〜35.18）、その北の L 字のラウンジへつながる。
3F: 吹き抜けの上の床（テラス）と、東へ膨らむ半楕円の自習室。西の弦（X 35.7）の Y 20.4〜35.26 は
3F の廊下へ開けてあり、残りは壁。曲面のガラスは thick_quad の分割で、柱と細い光で区切る。
"""

import math

from . import furn_lecture as FL
from . import furniture as F
from . import kit, shell
from . import plan_lecture_dims as D

WHITE = "lecture_rail_white"
GLASS = "lecture_glass_curtain"
GLASS_RAIL = "lecture_glass_rail"
COL = "lecture_lounge_column"
CEIL = "lecture_lounge_ceiling"
CARPET = "lecture_lounge_carpet"
TABLE = "lecture_table_white"
WALL = "lecture_rail_white"
CONCRETE = "lecture_concrete"
STONE = "lecture_stone_light"
COOL = "lecture_light_cool"

X_E_IN = 49.52
Y_N_TERRACE = 46.4
Z2_CEIL = D.F2 + 3.0                  # 2F ラウンジの天井
CX, CY = D.ARC_C
RX, RY = D.ARC_R
ARC_SEG = 24
Y_CHORD = (D.Y_ROOM_S1 + 0.6, D.Y_ROOM_N0 + 0.16)   # 廊下へ開く弦の範囲（20.4〜35.26）
LOUNGE_L = D.LOUNGE_2F


def _arc_pts():
    return [(CX + RX * math.cos(t), CY + RY * math.sin(t))
            for t in (-math.pi / 2 + math.pi * i / ARC_SEG for i in range(ARC_SEG + 1))]


# ------------------------------------------------------------------ 2F
def _bridge_and_lounge(c):
    x0, y0, x1, y1 = D.BRIDGE_2F[0], D.BRIDGE_2F[1], X_E_IN, D.Y_ROOM_N0 + 0.08
    shell.floor(c.floor, x0, y0, x1, y1, D.F2, "lecture_carpet_corridor", thickness=D.SLAB,
                side_mat=CONCRETE)
    for (ax, ay, bx, by) in LOUNGE_L:
        shell.floor(c.floor, ax, ay, bx, by, D.F2, CARPET, thickness=D.SLAB, side_mat=CONCRETE)
    h = 3.0
    z0, z1 = D.F2, D.F2 + h
    # L 字の外周の壁
    ex, ey = LOUNGE_L[0][2], LOUNGE_L[0][3]
    nx, ny = LOUNGE_L[1][2], LOUNGE_L[1][3]
    wx = LOUNGE_L[0][0]
    for a, b in (((wx, ny), (wx, LOUNGE_L[0][1])), ((nx, ny), (wx, ny)),
                 ((nx, ey), (nx, ny)), ((ex, ey), (nx, ey)), ((ex, LOUNGE_L[0][1]), (ex, ey))):
        shell.partition(c.wall, a, b, z0, z1, WALL)
    # 渡りの北の壁（ラウンジの口の両わき）は吹き抜けの天井まで
    zt = D.CEIL_EAST_VOID
    shell.partition(c.wall, (D.EAST_VOID_B[0], D.Y_ROOM_N0 + 0.08), (wx, D.Y_ROOM_N0 + 0.08),
                    z0, zt, WALL)
    shell.partition(c.wall, (ex, D.Y_ROOM_N0 + 0.08), (X_E_IN, D.Y_ROOM_N0 + 0.08), z0, zt, WALL)
    shell.ceiling(c.wall, wx, LOUNGE_L[0][1], ex, ey, z1, CEIL)
    shell.ceiling(c.wall, wx, ey, nx, ny, z1, CEIL)
    for (px, py) in ((43.0, 37.0), (41.4, 43.0), (41.4, 48.0)):
        c.light(px, py, z1 - 0.5, energy=90.0, radius=1.8)
    shell.light_strip(c.wall, wx + 0.4, 36.2, ex - 0.4, 36.36, z1, mat=COOL)
    shell.light_strip(c.wall, wx + 0.6, ey + 0.4, wx + 0.76, ny - 0.4, z1, mat=COOL)


def _lounge_furniture(c, mb):
    z = D.F2
    for (x, y) in ((42.0, 37.1), (41.0, 43.0), (41.4, 47.6)):
        F.round_table(mb, x, y, r=0.45, h=0.73, top=TABLE, leg="metal_dark", z=z)
        for k in range(3):
            a = math.radians(90 + 120 * k)
            px, py = x + 0.85 * math.cos(a), y + 0.85 * math.sin(a)
            FL.cafe_chair(mb, kit.T(px, py, z, kit.face_ang(px, py, x, y)))


# ------------------------------------------------------------------ 3F
def _terrace(c):
    """吹き抜けの上の床。下面が吹き抜けの天井（Z 8.5）になる。"""
    shell.floor(c.floor, D.EAST_VOID_B[0], D.Y_EAST_S + shell.WALL, X_E_IN, Y_N_TERRACE, D.F3,
                STONE, thickness=D.F3 - D.CEIL_EAST_VOID, side_mat=WHITE)


def _arc_room(c):
    pts = _arc_pts()
    z0 = D.F3
    z1 = D.CEIL_3F
    # 床（楕円の半分。弦の側は 3F の廊下の床につながる）
    c.floor.add_ngon_flat([(CX, CY - RY)] + pts + [(CX, CY + RY)], z0 + 0.003, CARPET)
    c.wall.add_ngon_flat([(CX, CY - RY)] + pts + [(CX, CY + RY)], z1, CEIL, flip=True)
    # 曲面のガラス
    for i in range(ARC_SEG):
        a, b = pts[i], pts[i + 1]
        kit.thick_quad(c.wall, (a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1),
                       (a[0], a[1], z1), shell.GLASS_T, GLASS)
    for i in range(0, ARC_SEG + 1, 4):
        p = pts[i]
        kit.cyl(c.wall, p[0], p[1], z0, z1, 0.14, COL, seg=8)
    # 弦の側の壁: 南の端と、北の閉じ
    for a, b in chord_walls():
        shell.partition(c.wall, a, b, z0, z1, WALL)
    # 天井の細い光（放射状）と点光源
    for yy in (24.0, 29.0, 38.0, 43.0):
        shell.light_strip(c.wall, CX + 1.0, yy, CX + 6.5, yy + 0.16, z1, mat=COOL)
    for (px, py) in ((39.5, 24.0), (43.0, 30.5), (39.5, 38.0), (40.0, 43.5), (37.8, 29.0)):
        c.light(px, py, z1 - 0.7, energy=130.0, radius=2.6)


def _inside(x, y, k=0.82):
    return ((x - CX) / RX) ** 2 + ((y - CY) / RY) ** 2 < k * k


def _arc_furniture(c, mb):
    z = D.F3
    tables = ((38.8, 24.8), (38.8, 27.5), (41.8, 35.0), (38.5, 35.5), (38.5, 40.0),
              (41.8, 39.0), (38.3, 43.0), (38.9, 31.0))
    for (x, y) in tables:
        if not _inside(x, y):
            continue
        F.round_table(mb, x, y, r=0.45, h=0.73, top=TABLE, leg="metal_dark", z=z)
        for k in range(3):
            a = math.radians(30 + 120 * k)
            px, py = x + 0.85 * math.cos(a), y + 0.85 * math.sin(a)
            FL.cafe_chair(mb, kit.T(px, py, z, kit.face_ang(px, py, x, y)),
                          mat="plastic_white", frame="metal_gray")
    # 西の壁ぎわの自動販売機 2 台
    for y in (D.VENDING_3F[1] + 0.2, D.VENDING_3F[1] + 1.4):
        base = len(mb.verts)
        shell.vending(mb, CX + 0.45, y, ang=-math.pi * 0.5)
        kit.lift(mb, base, z)


def chord_walls():
    """弦の側の壁の線分。南の端が弧の端と一致するときは作らない（長さ 0 の壁になる）。"""
    runs = (((CX, CY - RY), (CX, Y_CHORD[0])), ((CX, Y_CHORD[1]), (CX, CY + RY)))
    return [(a, b) for a, b in runs if abs(b[1] - a[1]) > 1e-6]


def build(c, mb):
    _bridge_and_lounge(c)
    _lounge_furniture(c, mb)
    _terrace(c)
    _arc_room(c)
    _arc_furniture(c, mb)

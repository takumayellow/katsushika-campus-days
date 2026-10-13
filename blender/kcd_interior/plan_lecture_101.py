"""講義棟 101（大教室）の前壁・後ろの壁・側壁・天井の設え（spec §4）。

前壁（南。面は Y 4.3）: 掘り下げた平場（z -1.2）から天井まで、黒板 3 枚と木の枠・受け、
z 2.6 より下の縦スリットの木のパネル、z 2.6 より上の木の横ルーバー、丸い時計。
後ろの壁（北。面は Y 19.64）: 扉 4 枚の間に同じパネルとルーバー。東の側壁: 木の縦格子。
西の側壁: 入口ロビーのレンガを 101 の側から打放しの板で覆う。掘り下げた所の東も打放しの板。
平場: 教卓と AV ラック（hall_booth）。天井: 斜めのライン照明 6 本とプロジェクター 2 台。
"""

import math

from . import furniture as F
from . import kit
from . import plan_lecture_dims as D
from . import shell

WOOD = "lecture_wood_louver"
FRAME = "lecture_wood_shell"
DARK = "lecture_panel_dark"
CONC = "lecture_concrete"

Y_BACK = D.Y_ROOM_S1 - shell.PART       # 後ろの壁（北）の 101 側の面
Y_FRONT = D.HALL_Y[0]                   # 前壁（南の外壁の内面）
Z_PIT = -D.HALL_DROP                    # 平場の床
X0, X1 = D.R101_X
PANEL_TOP = 2.6                         # 縦スリットのパネルと横ルーバーの境
SLIT_PITCH = 0.30
LOUVER_N, LOUVER_PITCH, LOUVER_H = 6, 0.24, 0.12
LATTICE_Y = (8.3, 19.4)                 # 東の側壁の縦格子（101 の東の壁は Y 8.0 から）
LATTICE_PITCH = 0.30
LINE_ANG = math.radians(-35.0)          # 天井の斜めのライン照明の向き（X 軸から）
LINE_LEN, LINE_W = 6.0, 0.08
LINE_XS = (10.3, 13.3, 16.3, 19.3, 22.3, 25.3)
LINE_Y = 11.95
PROJECTORS = ((17.8, 10.95), (24.2, 10.95))   # 南の黒板へ向ける


def _wall_spans():
    """後ろの壁のうち扉の開口を除いた X の区間。"""
    half = D.DOOR2_W * 0.5
    gaps = sorted((x - half, x + half) for x in D.R101_DOORS)
    spans, x = [], X0
    for g0, g1 in gaps:
        if g0 > x:
            spans.append((x, g0))
        x = max(x, g1)
    if x < X1:
        spans.append((x, X1))
    return spans, gaps


def _panels(mb, y_face, inward, z0, spans, gaps=()):
    """縦スリットの木のパネル（z0〜2.6）と、その上の黒い板と横ルーバー。inward は室内へ向かう Y の向き（±1）。"""

    def y(d):
        return y_face + inward * d

    for x0, x1 in spans:
        kit.box(mb, x0, y(0.03), z0, x1, y(0.0), PANEL_TOP, WOOD)
        n = int((x1 - x0) / SLIT_PITCH)
        for i in range(1, n + 1):
            x = x0 + (x1 - x0) * i / (n + 1)
            kit.box(mb, x - 0.012, y(0.036), z0 + 0.12, x + 0.012, y(0.03), PANEL_TOP - 0.10, DARK)
    for g0, g1 in gaps:
        kit.box(mb, g0, y(0.03), D.DOOR2_H, g1, y(0.0), PANEL_TOP, WOOD)
    kit.box(mb, X0, y(0.02), PANEL_TOP, X1, y(0.0), D.CEIL_101, DARK)
    for i in range(LOUVER_N):
        zl = PANEL_TOP + 0.06 + LOUVER_PITCH * i
        kit.box(mb, X0, y(0.08), zl, X1, y(0.02), zl + LOUVER_H, WOOD)


def _front_panels(mb):
    _panels(mb, Y_FRONT, 1.0, Z_PIT, [(X0, X1)])


def _back_panels(mb):
    spans, gaps = _wall_spans()
    _panels(mb, Y_BACK, -1.0, 0.0, spans, gaps)


def _boards(mb):
    z0, z1 = D.BOARD_Z
    yf = Y_FRONT
    for mat, x0, x1 in D.BOARDS:
        kit.box(mb, x0, yf + 0.04, z0, x1, yf + 0.09, z1, mat)
    bx0, bx1 = D.BOARDS[0][1], D.BOARDS[-1][2]
    kit.box(mb, bx0 - 0.06, yf + 0.04, z1, bx1 + 0.06, yf + 0.11, z1 + 0.06, FRAME)
    kit.box(mb, bx0 - 0.06, yf + 0.04, z0 - 0.06, bx1 + 0.06, yf + 0.18, z0, FRAME)
    for x in (bx0 - 0.03, bx1 + 0.03):
        kit.box(mb, x - 0.03, yf + 0.04, z0, x + 0.03, yf + 0.11, z1, FRAME)
    for _mat, _x0, x in D.BOARDS[:-1]:
        kit.box(mb, x - 0.02, yf + 0.05, z0, x + 0.02, yf + 0.10, z1, FRAME)


def _clock(mb):
    cx, cz = D.CLOCK_101
    r, y_face = 0.20, Y_FRONT + 0.13
    kit.tube(mb, (cx, Y_FRONT + 0.08, cz), (cx, y_face, cz), r, "metal_dark", seg=12)
    pts = [(cx - r * 0.92 * math.cos(2 * math.pi * i / 12), y_face,
            cz + r * 0.92 * math.sin(2 * math.pi * i / 12)) for i in range(12)]
    mb.add_face(pts, "plastic_white")
    kit.box(mb, cx - 0.008, y_face, cz, cx + 0.008, y_face + 0.008, cz + 0.16, "plastic_black")
    kit.box(mb, cx - 0.11, y_face, cz - 0.01, cx, y_face + 0.008, cz + 0.01, "plastic_black")


def _side_walls(mb):
    y0, y1 = LATTICE_Y
    n = int((y1 - y0) / LATTICE_PITCH)
    for i in range(n + 1):
        y = y0 + (y1 - y0) * i / n
        z0 = Z_PIT if y < D.HALL_BACK_Y[0] else 0.0
        kit.box(mb, X1 - 0.06, y - 0.03, z0, X1, y + 0.03, D.CEIL_101, WOOD)
    kit.box(mb, X0, Y_FRONT, Z_PIT, X0 + 0.02, Y_BACK, D.CEIL_101, CONC)
    # 掘り下げた所の東の壁（1F の床より下。格子の奥に見える）
    kit.vplate(mb, (X1, Y_FRONT), (X1, D.HALL_BACK_Y[0]), Z_PIT, 0.0, CONC, flip=True)


def _flat_floor(mb):
    lx, ly = D.HALL_LECTERN
    z = Z_PIT
    kit.box(mb, lx - 0.9, ly - 0.35, z, lx + 0.9, ly + 0.35, z + 0.86, FRAME)
    kit.box(mb, lx - 0.93, ly - 0.38, z + 0.86, lx + 0.93, ly + 0.38, z + 0.90, "desk_dark")
    rx, ry = D.HALL_RACK
    kit.box(mb, rx - 0.3, ry - 0.3, z, rx + 0.3, ry + 0.3, z + 1.05, "plastic_black")
    kit.box(mb, rx - 0.32, ry - 0.32, z + 1.05, rx + 0.32, ry + 0.32, z + 1.08, "metal_dark")
    for zs in (0.32, 0.56, 0.80):
        kit.box(mb, rx - 0.26, ry + 0.30, z + zs, rx + 0.26, ry + 0.31, z + zs + 0.07,
                "metal_dark")


def _ceiling(mb):
    ux, uy = math.cos(LINE_ANG), math.sin(LINE_ANG)
    hx, hy = ux * LINE_LEN * 0.5, uy * LINE_LEN * 0.5
    wx, wy = -uy * LINE_W * 0.5, ux * LINE_W * 0.5
    z = D.CEIL_101 - 0.015
    for cx in LINE_XS:
        a = (cx - hx - wx, LINE_Y - hy - wy, z)
        b = (cx + hx - wx, LINE_Y + hy - wy, z)
        c = (cx + hx + wx, LINE_Y + hy + wy, z)
        d = (cx - hx + wx, LINE_Y - hy + wy, z)
        kit.thick_quad(mb, a, b, c, d, 0.02, "light_strip")
    for x, y in PROJECTORS:
        F.projector(mb, x, y, D.CEIL_101 - 0.22, ang=math.pi)


def build(mb):
    """101 の設えを mb（furn_lecture_<nn>_hall_front）に作る。"""
    _front_panels(mb)
    _back_panels(mb)
    _boards(mb)
    _clock(mb)
    _side_walls(mb)
    _flat_floor(mb)
    _ceiling(mb)

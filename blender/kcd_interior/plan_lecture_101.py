"""講義棟 101（大教室）の前壁・側壁・天井の設え（spec §4）。

前壁（面は Y 19.645）: 黒板 3 枚と木の枠・受け、z 2.6 より下の縦スリットの木のパネル、
z 2.6 より上の木の横ルーバー、丸い時計。東の側壁: 木の縦格子。
西の側壁: 入口ロビーのレンガを 101 の側から打放しの板で覆う。
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

Y_FACE = D.Y_ROOM_S1 - shell.PART       # 前壁の 101 側の面
X0, X1 = D.R101_X
PANEL_TOP = 2.6                         # 縦スリットのパネルと横ルーバーの境
SLIT_PITCH = 0.30
LOUVER_N, LOUVER_PITCH, LOUVER_H = 6, 0.24, 0.12
LATTICE_Y = (8.3, 19.4)                 # 東の側壁の縦格子（101 の東の壁は Y 8.0 から）
LATTICE_PITCH = 0.30
LINE_ANG = math.radians(35.0)           # 天井の斜めのライン照明の向き（X 軸から）
LINE_LEN, LINE_W = 6.0, 0.08
LINE_XS = (10.3, 13.3, 16.3, 19.3, 22.3, 25.3)
LINE_Y = 12.0
PROJECTORS = ((17.8, 13.0), (24.2, 13.0))


def _wall_spans():
    """前壁のうち扉の開口を除いた X の区間。"""
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


def _front_panels(mb):
    spans, gaps = _wall_spans()
    for x0, x1 in spans:
        kit.box(mb, x0, Y_FACE - 0.03, 0.0, x1, Y_FACE, PANEL_TOP, WOOD)
        n = int((x1 - x0) / SLIT_PITCH)
        for i in range(1, n + 1):
            x = x0 + (x1 - x0) * i / (n + 1)
            kit.box(mb, x - 0.012, Y_FACE - 0.036, 0.12, x + 0.012, Y_FACE - 0.03,
                    PANEL_TOP - 0.10, DARK)
    for g0, g1 in gaps:
        kit.box(mb, g0, Y_FACE - 0.03, D.DOOR2_H, g1, Y_FACE, PANEL_TOP, WOOD)
    kit.box(mb, X0, Y_FACE - 0.02, PANEL_TOP, X1, Y_FACE, D.CEIL_101, DARK)
    for i in range(LOUVER_N):
        z0 = PANEL_TOP + 0.06 + LOUVER_PITCH * i
        kit.box(mb, X0, Y_FACE - 0.08, z0, X1, Y_FACE - 0.02, z0 + LOUVER_H, WOOD)


def _boards(mb):
    z0, z1 = D.BOARD_Z
    for mat, x0, x1 in D.BOARDS:
        kit.box(mb, x0, Y_FACE - 0.09, z0, x1, Y_FACE - 0.04, z1, mat)
    bx0, bx1 = D.BOARDS[0][1], D.BOARDS[-1][2]
    kit.box(mb, bx0 - 0.06, Y_FACE - 0.11, z1, bx1 + 0.06, Y_FACE - 0.04, z1 + 0.06, FRAME)
    kit.box(mb, bx0 - 0.06, Y_FACE - 0.18, z0 - 0.06, bx1 + 0.06, Y_FACE - 0.04, z0, FRAME)
    for x in (bx0 - 0.03, bx1 + 0.03):
        kit.box(mb, x - 0.03, Y_FACE - 0.11, z0, x + 0.03, Y_FACE - 0.04, z1, FRAME)
    for _mat, _x0, x in D.BOARDS[:-1]:
        kit.box(mb, x - 0.02, Y_FACE - 0.10, z0, x + 0.02, Y_FACE - 0.05, z1, FRAME)


def _clock(mb):
    cx, cz = D.CLOCK_101
    r, y_face = 0.20, Y_FACE - 0.13
    kit.tube(mb, (cx, Y_FACE - 0.08, cz), (cx, y_face, cz), r, "metal_dark", seg=12)
    pts = [(cx + r * 0.92 * math.cos(2 * math.pi * i / 12), y_face,
            cz + r * 0.92 * math.sin(2 * math.pi * i / 12)) for i in range(12)]
    mb.add_face(pts, "plastic_white")
    kit.box(mb, cx - 0.008, y_face - 0.008, cz, cx + 0.008, y_face, cz + 0.16, "plastic_black")
    kit.box(mb, cx, y_face - 0.008, cz - 0.01, cx + 0.11, y_face, cz + 0.01, "plastic_black")


def _side_walls(mb):
    y0, y1 = LATTICE_Y
    n = int((y1 - y0) / LATTICE_PITCH)
    for i in range(n + 1):
        y = y0 + (y1 - y0) * i / n
        kit.box(mb, X1 - 0.06, y - 0.03, 0.0, X1, y + 0.03, D.CEIL_101, WOOD)
    kit.box(mb, X0, D.HALL_BACK_Y[0], 0.0, X0 + 0.02, Y_FACE, D.CEIL_101, CONC)


def _flat_floor(mb):
    lx, ly = D.HALL_LECTERN
    kit.box(mb, lx - 0.9, ly - 0.35, 0.0, lx + 0.9, ly + 0.35, 0.86, FRAME)
    kit.box(mb, lx - 0.93, ly - 0.38, 0.86, lx + 0.93, ly + 0.38, 0.90, "desk_dark")
    rx, ry = D.HALL_RACK
    kit.box(mb, rx - 0.3, ry - 0.3, 0.0, rx + 0.3, ry + 0.3, 1.05, "plastic_black")
    kit.box(mb, rx - 0.32, ry - 0.32, 1.05, rx + 0.32, ry + 0.32, 1.08, "metal_dark")
    for z in (0.32, 0.56, 0.80):
        kit.box(mb, rx - 0.26, ry - 0.31, z, rx + 0.26, ry - 0.30, z + 0.07, "metal_dark")


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
        F.projector(mb, x, y, D.CEIL_101 - 0.22, ang=0.0)


def build(mb):
    """101 の設えを mb（furn_lecture_<nn>_hall_front）に作る。"""
    _front_panels(mb)
    _boards(mb)
    _clock(mb)
    _side_walls(mb)
    _flat_floor(mb)
    _ceiling(mb)

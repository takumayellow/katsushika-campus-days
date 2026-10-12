"""講義棟 1F の生協（spec §2）と、その西の受付窓の小室・北の出口通路。

生協の南の壁（東京理科大学生協の帯と入口）、西の仕切り、白い天井、レジのカウンターと札の台、
スチール棚、グッズの台、コーヒーの機械、受付窓の小室の奥の棚、北の出口の扉を作る。
"""

import math

from . import kit, shell
from . import furn_lecture as FL
from . import plan_lecture_dims as D

WALL = "lecture_concrete"
BAND = "lecture_coop_band"
COUNTER = "lecture_coop_counter"
CEIL = "lecture_rail_white"
COOL = "lecture_light_cool"
BLACK = "lecture_core_black"

X0, X1 = 35.8, 49.52
Y_S = D.Y_ROOM_N0                  # 生協の南の壁の廊下側の面 35.1
Y_N = D.Y_N_BUMP - shell.WALL      # 張り出しの北の壁の内面
ENTRANCE = (37.4, 39.8)
CEIL_H = 3.0
COUNTER_BOX = (39.6, 39.0, 45.6, 39.6)
# レジの札の板の中心。札の裏に台座を立てる（板は壁から 0.01 m 浮く）
REGISTERS = tuple((40.2 + i, 39.0) for i in range(4))
GOODS_RACK = (44.0, 46.0)
BOARD_A = (37.0, 33.0)
KIOSK_N = 40.0                     # 受付窓の小室の奥の壁
N_PASSAGE_X = D.N_EXIT_X


def _wall(c):
    ya, yb = Y_S, Y_S + shell.PART
    a, b = (X0, (ya + yb) * 0.5), (X1, (ya + yb) * 0.5)
    g = (ENTRANCE[0] - X0, ENTRANCE[1] - X0)
    shell.partition(c.wall, a, b, 0.0, 2.6, WALL, gaps=[g])
    shell.partition(c.wall, a, b, 2.6, 3.4, BAND, thick=shell.PART + 0.02)
    shell.partition(c.wall, a, b, 3.4, D.F2, WALL)
    shell.partition(c.wall, (X0, ya), (X0, D.north_y(X0) - shell.WALL), 0.0, CEIL_H, WALL)
    # 入口の石の敷居と、壁の帯の下の濃い巾木
    kit.box(c.wall, ENTRANCE[0], ya, 0.0, ENTRANCE[1], yb, 0.02, "lecture_stone_dark")


def _ceiling(c):
    y1 = D.north_y(N_PASSAGE_X[1]) - shell.WALL
    shell.ceiling(c.wall, X0 + 0.08, Y_S + shell.PART, X1, Y_N, CEIL_H, CEIL)
    pts = shell.ceiling_lights(c.wall, X0 + 0.5, Y_S + 0.6, X1 - 0.3, Y_N - 0.2, CEIL_H,
                               sx=3.4, sy=3.8, mat="light_panel")
    c.lights_from(pts, CEIL_H, energy=110.0, step=2)
    # 北の出口通路（X 28.7〜31.0）
    shell.ceiling(c.wall, N_PASSAGE_X[0], D.Y_ROOM_N0, N_PASSAGE_X[1], y1, CEIL_H, "lecture_ceiling_dark")
    xm = sum(N_PASSAGE_X) * 0.5
    c.light(xm, 42.0, CEIL_H - 0.3, energy=60.0, radius=1.4)
    c.light(xm, 47.5, CEIL_H - 0.3, energy=60.0, radius=1.4)
    shell.door(c.wall, xm, D.north_y(xm) - shell.WALL, ang=math.pi, w=1.6, h=2.3,
               leaf="lecture_door_wood", frame="metal_dark", open_=0.0)


def _counter(c, mb):
    x0, y0, x1, y1 = COUNTER_BOX
    kit.box(mb, x0, y0, 0.0, x1, y1, 0.95, COUNTER, top="lecture_stone_light")
    for x, y in REGISTERS:
        kit.box(mb, x - 0.22, y + 0.01, 0.95, x + 0.22, y + 0.21, 1.62, BLACK)
        kit.box(mb, x - 0.20, y + 0.22, 0.95, x + 0.20, y + 0.55, 1.20, "metal_dark")
    # 売り場の側の面に白い巾木
    kit.box(mb, x0, y0 - 0.02, 0.0, x1, y0, 0.12, "metal_gray")


def _shelves(c, mb):
    for y in (41.2, 43.8, 46.4):
        FL.steel_shelf(mb, kit.T(X0 + 0.12, y, 0.0, -math.pi * 0.5), w=2.4)
    for x in (38.0, 40.6, 43.2, 45.8):
        FL.steel_shelf(mb, kit.T(x, Y_N - 0.03, 0.0, math.pi), w=2.4)
    # 売り場の中の低いワゴン
    kit.box(mb, 41.0, 44.2, 0.0, 43.0, 45.0, 0.85, COUNTER, top="lecture_stone_light")
    x, y = GOODS_RACK
    kit.box(mb, x - 0.6, y + 0.01, 0.0, x + 0.6, y + 0.50, 1.9, "metal_gray")
    for z in (0.5, 1.0, 1.5):
        kit.box(mb, x - 0.55, y + 0.02, z, x + 0.55, y + 0.60, z + 0.03, "metal_dark")
    # 立て看板（コーヒーの黒板）
    bx, by = BOARD_A
    kit.box(mb, bx - 0.45, by + 0.01, 0.0, bx + 0.45, by + 0.12, 1.5, "metal_dark")


def _coffee(c, mb):
    kit.box(mb, 48.7, 39.0, 0.0, 49.4, 43.5, 0.9, COUNTER, top="lecture_stone_light")
    kit.box(mb, 48.9, 40.0, 0.9, 49.4, 40.7, 1.45, "metal_dark")
    kit.box(mb, 48.95, 41.4, 0.9, 49.3, 41.8, 1.15, "stainless")


def _kiosk(c, mb):
    """受付窓の小室。奥の壁を Y 40 に立てて閉じ、白い天井と奥の棚を置く。"""
    x0, x1 = D.COOP_KIOSK_X
    shell.partition(c.wall, (x0, KIOSK_N), (x1, KIOSK_N), 0.0, CEIL_H, WALL)
    shell.ceiling(c.wall, x0, D.Y_ROOM_N0, x1, KIOSK_N, CEIL_H, CEIL)
    c.light(33.4, 37.5, CEIL_H - 0.3, energy=80.0, radius=1.6)
    kit.box(mb, x0 + 0.6, KIOSK_N - 0.50, 0.0, x1 - 0.6, KIOSK_N - 0.03, 1.8, "metal_gray")
    kit.box(mb, x0 + 0.6, KIOSK_N - 0.50, 0.9, x1 - 0.6, KIOSK_N - 0.03, 0.93, "metal_dark")
    kit.box(mb, x0 + 0.6, KIOSK_N - 0.50, 1.4, x1 - 0.6, KIOSK_N - 0.03, 1.43, "metal_dark")


def build(c, mb):
    _wall(c)
    _ceiling(c)
    _counter(c, mb)
    _shelves(c, mb)
    _coffee(c, mb)
    _kiosk(c, mb)

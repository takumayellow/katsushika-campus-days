"""講義棟 1F 北の 104（演習室）の中（spec §3 の 104）。

白い天板の 2 人掛けの机 32 前後、灰色の椅子、北の壁の緑の黒板、木の教卓、天井吊りのプロジェクター。
扉は plan_lecture_core が開けてある。他の北の室（保健管理センター・103・中央監視室）は閉じたまま。
"""

import math

from . import furniture as F
from . import kit, shell
from . import plan_lecture_dims as D

TOP = "lecture_table_white"
PANEL = "lecture_panel_dark"
BOARD = "lecture_board_green"
WOOD = "lecture_door_wood"
SEAT = "chair_grey"
LEG = "metal_gray"

X0, X1 = D.R104_X
Y0 = D.Y_ROOM_N0
ROW_Y = (38.0, 39.7, 41.4, 43.1, 44.8, 46.5)
COL_X = (9.9, 11.85, 13.8, 15.75, 17.7, 19.65)
DESK_W, DESK_D = 1.50, 0.50
KEEP_CLEAR = ((14.6, 42.0), (12.0, 44.0))      # POI と NPC の立つ所
CLEAR_R = 1.0


def _y_north():
    return D.north_y((X0 + X1) * 0.5) - shell.WALL


def _desk(mb, x, y):
    hw, hd = DESK_W * 0.5, DESK_D * 0.5
    kit.box(mb, x - hw, y - hd, 0.70, x + hw, y + hd, 0.74, TOP)
    kit.box_nb(mb, x - hw + 0.02, y + hd - 0.03, 0.30, x + hw - 0.02, y + hd, 0.70, PANEL)
    for sx in (-1, 1):
        cx = x + sx * (hw - 0.12)
        kit.box_nb(mb, cx - 0.02, y - hd + 0.05, 0.0, cx + 0.02, y + hd - 0.05, 0.04, LEG)
        kit.box_nb(mb, cx - 0.02, y - 0.02, 0.04, cx + 0.02, y + 0.02, 0.70, LEG)


def desk_sites():
    out = []
    for y in ROW_Y:
        for x in COL_X:
            if any(math.hypot(x - px, y - py) < CLEAR_R for px, py in KEEP_CLEAR):
                continue
            out.append((x, y))
    return out


def build(c, mb):
    sites = desk_sites()
    for x, y in sites:
        _desk(mb, x, y)
        for dx in (-0.40, 0.40):
            F.chair_canteen(mb, kit.T(x + dx, y - 0.55, 0.0, 0.0), mat=SEAT, frame=LEG)
    yn = _y_north()
    kit.box(mb, 10.9, yn - 0.04, 0.85, 18.3, yn, 2.15, "metal_gray")
    kit.box(mb, 11.0, yn - 0.06, 0.90, 18.2, yn - 0.04, 2.10, BOARD)
    kit.box(mb, 11.0, yn - 0.12, 0.86, 18.2, yn - 0.04, 0.90, "metal_dark")
    # 教卓
    kit.box(mb, 13.6, 47.6, 0.0, 15.2, 48.3, 0.95, WOOD, top="lecture_door_wood")
    # 天井吊りのプロジェクター
    kit.box(mb, 14.2, 40.0, 2.70, 15.0, 40.5, 2.95, "plastic_black")
    kit.box(mb, 14.55, 40.2, 2.95, 14.65, 40.3, D.CEIL_104, "metal_dark")
    c.light(14.5, 42.0, D.CEIL_104 - 0.5, energy=90.0, radius=2.2)
    c.light(11.0, 46.0, D.CEIL_104 - 0.5, energy=90.0, radius=2.2)
    c.light(18.0, 46.0, D.CEIL_104 - 0.5, energy=90.0, radius=2.2)
    c.light(11.0, 38.5, D.CEIL_104 - 0.5, energy=90.0, radius=2.2)
    c.light(18.0, 38.5, D.CEIL_104 - 0.5, energy=90.0, radius=2.2)
    return len(sites)

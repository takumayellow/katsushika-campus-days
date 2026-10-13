"""第2研究棟のロビーの座標系。host_local（食堂の入口を X=0 とした第2研究棟の局所座標）→ ロビーのローカル。

ロビーの入口は東面（host_local の X が LOBBY_X1、Y が LOBBY_DOOR_Y）にあり、内向きは -X。
InteriorSpec の内向き "-u" は (du, dv) → (dv, -du) なので、ロビーのローカルは host から
x = Y - LOBBY_DOOR_Y、y = LOBBY_X1 + 4.0 - X（原点は入口、4.0 は入口から壁までの距離）。
時計回りに 90 度回しただけの写しで、向きは変わらない（鏡像にならない）。
"""

import math

from . import spec as ispec

FACE_OFFSET = 4.0                    # spec._FACE_OFFSET["research2"]
STAIR_UP = {"+x": "-y", "-x": "+y", "+y": "+x", "-y": "-x"}


def pt(x, y):
    """host の (X, Y) → ロビーのローカル (x, y)。"""
    return (y - ispec.LOBBY_DOOR_Y, ispec.LOBBY_X1 + FACE_OFFSET - x)


def rect(x0, y0, x1, y1):
    """host の矩形 → ロビーのローカルの矩形 (x0, y0, x1, y1)。辺は昇順にそろえる。"""
    ax, ay = pt(x0, y0)
    bx, by = pt(x1, y1)
    return (min(ax, bx), min(ay, by), max(ax, bx), max(ay, by))


def ang(a):
    """host の向き（kit.T の角度。正面 +Y が 0）→ ロビーのローカルの向き。"""
    return a - math.pi * 0.5

"""大浴場の男湯（1F、西奥）。脱衣所と浴室。

公式の写真: 白い壁タイル、淡いグレーの床、黒い背の高い仕切りのシャワー、手すりのついた細長い濃い色の浴槽、
鏡の並ぶ木の洗面台と白い丸椅子。男湯・女湯は同じ大浴場で、女湯の扉は閉めてあり、その奥は入れない。

間取りは推定。湯上がり処の奥（y 27.0）に男湯の引き戸（x -3.72、開けてある）と女湯の扉（x -2.32、閉）。
男湯の脱衣所は西側（x -4.38 – -3.02、y 27.0 – 29.4）、その北が浴室（y 29.4 – 34.4）。
女湯の扉の奥（x -3.02 – -1.68、y 27.0 – 29.4）は壁でふさいだ死に空間。
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_interior import kit                          # noqa: E402
from kcd_interior import shell as sh                  # noqa: E402
from kcd_route import dorm_rooms_props as R           # noqa: E402
from kcd_route.dorm_levels import CORR_X, Y_BATH, Y_EV, Z_PART  # noqa: E402

X_DIV = -3.02                      # 脱衣所と女湯の扉の奥の境
Y_WET = 29.40                      # 脱衣所の北端（ここから浴室）
MEN_X = -3.72                      # 男湯の引き戸の中心
MEN_W = 1.00


def men_gap(xw):
    """Y_BATH の壁に開ける男湯の入口（壁の始点 xw からの距離）。"""
    return (MEN_X - MEN_W * 0.5 - xw, MEN_X + MEN_W * 0.5 - xw)


def build(c, xw):
    mb = c.furn("bath_men")
    xe = -CORR_X - sh.PART * 0.5            # 東の壁の室内側の面
    ys = Y_BATH + sh.PART * 0.5
    yn = Y_EV - sh.PART * 0.5

    # 仕切り: 脱衣所と女湯の扉の奥、その北の壁
    sh.partition(c.wall, (X_DIV, Y_BATH), (X_DIV, Y_WET), 0.0, Z_PART)
    sh.partition(c.wall, (X_DIV, Y_WET), (-CORR_X, Y_WET), 0.0, Z_PART)
    kit.plate(c.floor, xw, ys, xe, yn, 0.006, "floor_tile_grey")

    # 白い壁タイル（西と北の壁の室内側）
    kit.box(mb, xw, ys + 0.0, 0.0, xw + 0.02, yn, 2.10, "wall_white")
    kit.box(mb, xw, yn - 0.02, 0.0, xe, yn, 2.10, "wall_white")

    # 脱衣所: 木の洗面台と白い丸椅子
    R.vanity(mb, xw, 27.50, xw + 0.55, 29.10, side=-1)
    R.pouf(mb, X_DIV - 0.45, 28.20)
    R.pouf(mb, X_DIV - 0.45, 28.80)

    # 浴室: 西に長い浴槽と手すり、東に黒い仕切りのシャワー
    R.tub(mb, xw, 30.30, xw + 1.00, 34.10, h=0.50)
    R.handrail(mb, xw + 1.10, 30.90, 33.60, 0.90)
    y = 30.10
    for _ in range(4):
        R.shower_stall(mb, xe, y, y + 1.00, depth=0.70)
        y += 1.00
    kit.box(mb, xe - 0.70, y - 0.02, 0.0, xe, y + 0.02, 1.65, "plastic_black")

    sh.ceiling_lights(c.wall, xw, ys, xe, yn, 2.70, sx=2.4, sy=2.4, w=0.60, l=0.60)
    c.poi("yokushitsu", -3.05, 32.00)
    return mb

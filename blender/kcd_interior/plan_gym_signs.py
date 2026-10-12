"""体育館の看板 13 枚（文字入り）と誘導灯。置く順が Empty の sign_gym_1〜13 の番号になる。

文言と位置は docs/ref/interiors/gym/spec.md §4。
"""

import math

from . import kit, shell, signs

PI = math.pi
HALF_PI = math.pi * 0.5
ORANGE = "gym_accent_orange"
NAVY = "sign_plate_blue"

# (x, y, z, ang, ja, en, w, h, plate)。ang: -π/2 は +X 向き、π/2 は -X 向き、π は -Y 向き
WALL_SIGNS = (
    (-3.68, 10.0, 2.1, -HALF_PI, "メインアリーナ", "MAIN ARENA", 1.3, 0.34, ORANGE),
    (-3.68, 42.4, 2.1, -HALF_PI, "メインアリーナ", "MAIN ARENA", 1.3, 0.34, ORANGE),
    (2.15, 18.1, 6.2, HALF_PI, "トレーニングルーム", "TRAINING ROOM", 1.5, 0.34, ORANGE),
    (2.6, 8.4, 1.25, PI, "【体育館施設利用】\n体育館・トレーニングルーム\nシャワー室、更衣室\n7:00〜22:00",
     "Gym facilities\nGymnasium, Training Room,\nShowers and Locker Rooms\n7:00-22:00",
     1.6, 1.0, NAVY),
    (-3.68, 17.8, 2.1, -HALF_PI, "土足厳禁", "No Outdoor Shoes", 1.0, 0.3, NAVY),
    (15.6, 12.0, 2.7, HALF_PI, "体育事務室", "Gym Office", 1.2, 0.3, NAVY),
    (15.6, 13.6, 1.5, HALF_PI, "受付 月〜金\n10:00〜13:00 / 14:00〜17:00",
     "Office Hours Mon-Fri\n10:00-13:00 / 14:00-17:00", 1.2, 0.6, NAVY),
    (15.6, 6.6, 2.4, HALF_PI, "男子更衣室", "Men's Locker Room", 1.2, 0.3, NAVY),
    (15.6, 16.5, 2.4, HALF_PI, "女子更衣室", "Women's Locker Room", 1.2, 0.3, NAVY),
    (15.6, 19.7, 2.4, HALF_PI, "シャワー室", "Showers", 1.0, 0.3, NAVY),
    (15.6, 28.5, 2.0, HALF_PI, "サブアリーナ", "SUB ARENA", 1.3, 0.34, ORANGE),
    (-19.4, 38.83, 3.0, PI, "用具庫", "Equipment Storage", 1.0, 0.3, NAVY),
    (-3.68, 20.5, 2.0, -HALF_PI, "AED", "AED", 0.5, 0.25, NAVY),
)

# 誘導灯 (x, y, z, ang)。z は緑の箱の上端。南壁の 2 つの扉・北の開口・玄関・サブアリーナの扉の上
EXITS = (
    (-12.0, 4.7, 3.5, 0.0),
    (-20.0, 4.0, 3.5, 0.0),
    (-4.5, 40.5, 3.0, 0.0),
    (0.0, 7.65, 3.0, 0.0),
    (15.6, 26.9, 3.0, HALF_PI),
)


def _stand(c):
    """入口の内側の掲示スタンド（板を載せる裏板と脚）。"""
    mb = c.wall
    kit.box(mb, 1.7, 8.4, 0.25, 3.5, 8.55, 1.95, "gym_wall_wood_lower")
    for x in (1.8, 3.4):
        kit.box(mb, x - 0.04, 8.4, 0.0, x + 0.04, 8.55, 0.25, "gym_rail_metal")


def build(c):
    _stand(c)
    for x, y, z, ang, ja, en, w, h, plate in WALL_SIGNS:
        signs.wall(c, c.wall, x, y, z, ang, ja=ja, en=en, w=w, h=h, plate=plate)
    for x, y, z, ang in EXITS:
        shell.exit_sign(c.wall, x, y, z, ang=ang)

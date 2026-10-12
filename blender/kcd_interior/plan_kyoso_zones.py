"""共創棟 1F のうち店以外の区画: ラーニングスクエア・ホール・カフェラウンジ・ラウンジ 1/2。"""

import math

from . import furn_kyoso as FK, furniture as F, kit, shell
from . import plan_kyoso_dims as D

WOOD = "kyoso_wood_oak"
BLACK = "kyoso_steel_black"
GREY = "kyoso_sofa_grey"
SEAT_FRONT = {"n": 0.0, "s": math.pi, "w": math.pi * 0.5, "e": -math.pi * 0.5}


def _chair(mb, x, y, ang, mat="plastic_white", frame="metal_gray"):
    F.chair_min(mb, kit.T(x, y, 0.0, ang), mat, frame)
    FK.seat_mark(mb, x, y, 0.0, ang, hw=0.22, hd=0.22, inner=0.44)


def _dir_ang(dx, dy):
    return math.atan2(-dx, dy)


def learning_square(c, mb):
    rng = c.rng
    x0, y0, x1, y1 = D.LS
    FK.terrazzo(mb, x0, y0, x1, y1, rng)
    FK.deck(mb, -31.0, 7.8, -18.0, 12.0)
    # 最上段（x -31..-19.8, y 9.0..12.0）の縁にグレーの箱形ベンチ
    FK.box_bench(mb, -29.6, 11.5, -20.4, 12.0, z=D.DECK_H)
    FK.box_bench(mb, -19.9, 9.4, -19.4, 11.5, z=D.DECK_H)
    for sx in (-27.6, -23.2):
        FK.round_sofa(mb, sx, 10.1, D.DECK_H)
        for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
            FK.seat_mark(mb, sx + 0.55 * dx, 10.1 + 0.55 * dy, D.DECK_H,
                         _dir_ang(dx, dy), hw=0.25, hd=0.25, inner=0.5)
    for px, py, mat in ((-25.6, 11.2, "kyoso_sofa_charcoal"), (-25.0, 11.5, GREY),
                        (-21.0, 10.6, "kyoso_sofa_charcoal"), (-21.8, 11.2, GREY)):
        FK.pouf(mb, px, py, D.DECK_H, mat=mat)
    for bx, by in ((-29.4, 9.6), (-29.0, 10.3), (-21.6, 9.4), (-30.0, 10.9)):
        FK.box_stool(mb, bx, by, D.DECK_H)
    F.round_table(mb, -25.4, 9.5, r=0.45, h=0.73, top="plastic_white", z=D.DECK_H)

    # テーブル席: 前の通路寄りに 2 台, 階段の足元の手前に 2 台
    for tx, ty in ((-32.3, 8.5), (-32.3, 11.2), (-16.6, 8.4), (-16.6, 10.8)):
        F.table(mb, tx, ty, 0.0, w=1.6, d=0.8, h=0.72, top=WOOD, leg=BLACK)
        for dx in (-0.4, 0.4):
            _chair(mb, tx + dx, ty - 0.72, 0.0)
            _chair(mb, tx + dx, ty + 0.72, math.pi)
    for wx in (-34.8, -27.6, -20.4):
        F.whiteboard(mb, wx, 7.6, 1.5, 0.0, w=1.4, h=1.0)

    # 西の縁の段状のベンチ（通路側は腰壁）
    kit.box(mb, -34.3, 9.5, 0.0, -34.0, 13.4, 0.9, "wall_white")
    kit.box(mb, -34.0, 9.5, 0.0, -33.4, 13.4, 0.42, "kyoso_sage_grey")
    kit.box(mb, -34.0, 9.5, 0.42, -33.85, 13.4, 0.95, "kyoso_sage_grey")
    kit.box(mb, -33.7, 12.0, 0.42, -33.4, 12.6, 0.47, "plant_green")
    for sy in (10.0, 11.0, 12.0, 13.0):
        FK.seat_mark(mb, -33.7, sy, 0.0, SEAT_FRONT["e"], hw=0.3, hd=0.3, inner=0.6)


def hall(c, mb):
    F.bench(mb, -3.0, 13.4, 0.0, w=1.8, mat=WOOD)
    F.bench(mb, 3.0, 13.4, 0.0, w=1.8, mat=WOOD)
    # 1F の案内板（自立）
    kit.box(mb, 3.0, 6.2, 0.05, 4.2, 6.32, 1.9, "kyoso_info_beige")
    for lx in (3.1, 4.1):
        kit.box_c(mb, lx, 6.26, 0.0, 0.05, 0.05, 0.1, BLACK)
    kit.box(mb, 3.0, 6.2, 1.9, 4.2, 6.32, 1.95, BLACK)


def cafe_lounge(c, mb):
    x0, y0, x1, y1 = D.CAFE_LOUNGE
    kit.plate(mb, x0, y0, x1, y1, 0.012, "kyoso_floor_carpet_grey")
    gz = 4.6
    # 廊下側の仕切りは すりガラスの帯 + 黒い枠 (X = 7 の扉は Y 7.6..8.6)
    shell.partition(mb, (7.0, 8.6), (7.0, y1), 0.0, gz, "wall_white",
                    glass_top="kyoso_glass_frosted", glass_z=1.1)
    shell.partition(mb, (7.0, y0), (15.0, y0), 0.0, gz, "wall_white",
                    glass_top="kyoso_glass_frosted", glass_z=1.1)
    for px, py in ((7.0, 8.6), (7.0, y0), (15.0, y0)):
        kit.box_c(mb, px, py, 0.0, 0.14, 0.14, gz, BLACK)
    kit.box(mb, 6.9, 7.5, 2.2, 7.1, 8.6, 2.3, BLACK)
    # 東は 15 のガラス間仕切り (Y 8.8..10.2 が出入口)
    FK.glass_wall(mb, (15.0, y0), (15.0, y1), gz, gaps=((1.3, 2.7),), door_top=2.6)
    F.table(mb, 11.0, 10.2, 0.0, w=4.8, d=1.2, h=0.74, top=WOOD, leg=BLACK)
    base = len(mb.verts)
    shell.planter(mb, 11.0, 10.2, r=0.24, h=0.3, leaf_h=0.8)
    kit.lift(mb, base, 0.74)
    for x in (9.0, 10.0, 11.0, 12.0, 13.0):
        _chair(mb, x, 8.95, 0.0, WOOD, BLACK)
        _chair(mb, x, 11.45, math.pi, WOOD, BLACK)
    F.counter(mb, 8.2, 13.2, 14.0, 13.8, h=0.95, body=WOOD, top=WOOD, toe=False)
    for k in range(6):
        sx = 8.6 + 1.0 * k
        F.stool(mb, sx, 12.55, 0.0, mat=WOOD, h=0.7)
        FK.seat_mark(mb, sx, 12.55, 0.0, 0.0, hw=0.2, hd=0.2, inner=0.4)
    F.bench(mb, 14.5, 11.6, D.FACE_W, w=2.4, mat=GREY)


def lounges(c, mb):
    for rect, wx in ((D.LOUNGE2, 21.5), (D.LOUNGE1, 28.5)):
        x0, y0, x1, y1 = rect
        kit.plate(mb, x0, y0, x1, y1, 0.012, "kyoso_floor_wood_oak")
        for sx in (x0 + 2.5, x0 + 5.9):
            FK.modular_sofa(mb, sx, 8.4, 0.0)
            FK.seat_mark(mb, sx, 8.4, 0.0, 0.0, hw=1.2, hd=0.42, inner=2.1)
        FK.side_table(mb, x0 + 4.2, 8.4)
        rx, ry = x0 + 3.6, 11.4
        FK.round_sofa(mb, rx, ry)
        for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
            FK.seat_mark(mb, rx + 0.55 * dx, ry + 0.55 * dy, 0.0, _dir_ang(dx, dy))
        F.table(mb, wx, 13.4, 0.0, w=1.2, d=0.6, h=1.0, top="plastic_white", leg=BLACK)
        for dx in (-0.35, 0.35):
            _chair(mb, wx + dx, 12.7, 0.0)
    # X = 23 の低い植栽の列 (Y 9.6..10.8 は通り抜け)
    for py in (7.4, 8.2, 9.0, 11.4, 12.2, 13.0):
        shell.planter(mb, 23.0, py, r=0.28, h=0.5, leaf_h=0.9)
    kit.box(mb, 30.30, 7.2, 0.0, 30.42, 13.6, 3.6, "kyoso_wall_terracotta")

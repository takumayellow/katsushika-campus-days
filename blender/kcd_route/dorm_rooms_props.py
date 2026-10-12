"""寮の階段・居室・ランドリー・大浴場・屋上の専用什器（形だけ。Empty や c.* は触らない）。

dorm_stairs.py / dorm_floor2.py / dorm_bath.py / dorm_roof.py から呼ぶ。座標は建物ローカル。
向きは kit.T と同じ（正面 = (-sin ang, cos ang)）。形と色は公式サイトの写真
（居室の間取り図・ランドリー・大浴場・屋上テラス）から読んだもの。
z=0 から組む什器を上階に置くときは kit.lift(mb, base, dz) で持ち上げる。
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_interior import kit                          # noqa: E402
from kcd_interior.kit import T                        # noqa: E402


# --------------------------------------------------------------------------- #
#  階段
# --------------------------------------------------------------------------- #
def stair_run(mb, x0, x1, y_from, y_to, z_from, z_to, n, z_base=0.0,
              tread_mat="floor_tile_grey", riser_mat="wall_white"):
    """Y 方向にまっすぐ上る階段 1 本。y_from から y_to へ z_from から z_to へ上る（n 段）。

    各段は z_base（下の階の床）まで詰めた箱なので、下からも側面からも落ちる隙間が無い。
    """
    dy = (y_to - y_from) / n
    dz = (z_to - z_from) / n
    for i in range(n):
        ya = y_from + dy * i
        yb = ya + dy
        top = z_from + dz * (i + 1)
        kit.box(mb, x0, min(ya, yb), z_base, x1, max(ya, yb), top, riser_mat,
                top=tread_mat)


# --------------------------------------------------------------------------- #
#  居室（公式の間取り図: 11.34 ㎡）
# --------------------------------------------------------------------------- #
def bed(mb, x, y, ang=0.0, w=0.97, length=1.95):
    """すのこベッド。頭は +Y 側。"""
    t = T(x, y, 0.0, ang)
    hw, hl = w * 0.5, length * 0.5
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box_nb(mb, sx * (hw - 0.04) - 0.03, sy * (hl - 0.04) - 0.03, 0.0,
                     sx * (hw - 0.04) + 0.03, sy * (hl - 0.04) + 0.03, 0.30, "desk_wood")
    t.box(mb, -hw, -hl, 0.30, hw, hl, 0.34, "desk_wood")
    t.box(mb, -hw + 0.02, -hl + 0.02, 0.34, hw - 0.02, hl - 0.02, 0.46, "fabric_beige")
    t.box(mb, -hw + 0.08, hl - 0.42, 0.46, hw - 0.08, hl - 0.08, 0.56, "paper_white")
    t.box(mb, -hw, hl - 0.03, 0.30, hw, hl + 0.03, 0.80, "desk_wood")


def wardrobe(mb, x, y, ang=0.0, w=0.90, d=0.55, h=1.80):
    """タンス（観音開き）。"""
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -d * 0.5, 0.0, hw, d * 0.5, h, "desk_white")
    t.box(mb, -0.01, d * 0.5, 0.05, 0.01, d * 0.5 + 0.01, h - 0.05, "metal_gray")
    for sx in (-1, 1):
        t.box(mb, sx * 0.07 - 0.015, d * 0.5, 0.85, sx * 0.07 + 0.015,
              d * 0.5 + 0.03, 1.10, "metal_gray")


def mini_fridge(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.25, -0.25, 0.0, 0.25, 0.25, 0.86, "plastic_white")
    t.box(mb, -0.22, 0.25, 0.55, 0.22, 0.26, 0.58, "metal_gray")
    t.box(mb, 0.15, 0.25, 0.60, 0.19, 0.27, 0.80, "metal_gray")


def aircon(mb, x, y, z, ang=0.0, w=0.80):
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5, 0.0, 0.0, w * 0.5, 0.22, 0.28, "plastic_white")
    t.box(mb, -w * 0.5 + 0.04, 0.22, 0.03, w * 0.5 - 0.04, 0.23, 0.07, "chair_grey")


def curtain(mb, x, y0, y1, z0, z1, side=-1):
    """窓際のカーテン。side は室内へ出る向き（x の符号）。"""
    kit.box_nb(mb, x, y0, z0, x + side * 0.06, y1, z1, "curtain_blue")
    n = max(2, int((y1 - y0) / 0.28))
    for i in range(n):
        yc = y0 + (y1 - y0) * (i + 0.5) / n
        kit.box_nb(mb, x + side * 0.06, yc - 0.015, z0, x + side * 0.09, yc + 0.015,
                   z1, "curtain_blue")


def shelf_over_desk(mb, x0, y0, x1, y1, z):
    """机の上の棚（2 段）。"""
    for k in range(2):
        kit.box(mb, x0, y0, z + k * 0.34, x1, y1, z + k * 0.34 + 0.03, "desk_wood")
    for x in (x0, x1 - 0.03):
        kit.box_nb(mb, x, y0, z, x + 0.03, y1, z + 0.40, "desk_wood")


# --------------------------------------------------------------------------- #
#  ランドリー（黄色い床・白い洗濯機に青緑のふた・棚の上の乾燥機）
# --------------------------------------------------------------------------- #
def washer(mb, x, y, ang=0.0):
    """洗濯機 + その上の棚 + 棚の上の乾燥機。幅 0.64、高さ 2.0。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.32, -0.30, 0.0, 0.32, 0.30, 0.90, "plastic_white")
    t.box(mb, -0.30, -0.30, 0.90, 0.30, 0.18, 0.94, "washer_lid")
    t.box(mb, -0.22, 0.30, 0.74, 0.22, 0.31, 0.84, "plastic_black")
    for sx in (-1, 1):
        t.box_nb(mb, sx * 0.31 - 0.015, -0.30, 0.94, sx * 0.31 + 0.015, 0.30, 1.40,
                 "metal_gray")
    t.box(mb, -0.32, -0.30, 1.40, 0.32, 0.30, 1.43, "metal_gray")
    t.box(mb, -0.30, -0.28, 1.43, 0.30, 0.28, 2.00, "plastic_white")
    t.box(mb, -0.17, 0.28, 1.55, 0.17, 0.29, 1.88, "plastic_black")


def laundry_table(mb, x, y, ang=0.0, w=1.60, d=0.80):
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.80, hw, hd, 0.84, "plastic_white")
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box_nb(mb, sx * (hw - 0.06) - 0.025, sy * (hd - 0.06) - 0.025, 0.0,
                     sx * (hw - 0.06) + 0.025, sy * (hd - 0.06) + 0.025, 0.80, "metal_gray")


# --------------------------------------------------------------------------- #
#  大浴場（白いタイル・黒い高い仕切りのシャワー・濃い色の浴槽・洗面の鏡）
# --------------------------------------------------------------------------- #
def tub(mb, x0, y0, x1, y1, h=0.50):
    """浴槽。縁は黒、湯は暗い青。"""
    kit.box(mb, x0, y0, 0.0, x1, y1, h - 0.05, "floor_tile_dark", top="water_bath")
    for (a, b, c_, d_) in ((x0, y0, x1, y0 + 0.07), (x0, y1 - 0.07, x1, y1),
                           (x0, y0, x0 + 0.07, y1), (x1 - 0.07, y0, x1, y1)):
        kit.box(mb, a, b, h - 0.05, c_, d_, h + 0.03, "plastic_black")


def handrail(mb, x, y0, y1, z):
    kit.tube(mb, (x, y0, z), (x, y1, z), 0.025, "stainless", seg=6)
    for y in (y0, y1):
        kit.tube(mb, (x, y, z - 0.30), (x, y, z), 0.02, "stainless", seg=5)


def shower_stall(mb, x_wall, y0, y1, depth=0.80):
    """東壁付けのシャワー 1 席。室内は -X 側。黒い仕切りが y0 側。"""
    yc = (y0 + y1) * 0.5
    kit.box(mb, x_wall - depth, y0 - 0.02, 0.0, x_wall, y0 + 0.02, 1.65, "plastic_black")
    kit.box_nb(mb, x_wall - 0.03, yc - 0.28, 1.05, x_wall, yc + 0.28, 1.60, "mirror_glass")
    kit.box(mb, x_wall - 0.06, yc - 0.06, 0.92, x_wall, yc + 0.06, 1.0, "stainless")
    kit.tube(mb, (x_wall - 0.06, yc, 1.00), (x_wall - 0.06, yc, 1.78), 0.015,
             "stainless", seg=4)
    kit.box(mb, x_wall - 0.18, yc - 0.06, 1.74, x_wall - 0.04, yc + 0.06, 1.80, "stainless")
    kit.cyl(mb, x_wall - 0.50, yc, 0.0, 0.30, 0.17, "plastic_white", seg=8)


def vanity(mb, x0, y0, x1, y1, side=1):
    """脱衣所の洗面台（木の台・石の天板・鏡）。side は鏡のある壁の向き（+1 = x1 側）。"""
    kit.box(mb, x0, y0, 0.0, x1, y1, 0.78, "counter_wood", top="counter_stone")
    xm = x1 if side > 0 else x0
    half = (y1 - y0 - 0.20) * 0.5
    for k in range(2):
        ya = y0 + 0.10 + k * half
        yb = ya + half - 0.06
        kit.box_nb(mb, xm - 0.01, ya, 1.00, xm + 0.01, yb, 1.95, "mirror_glass")
        kit.cyl(mb, (x0 + x1) * 0.5, (ya + yb) * 0.5, 0.78, 0.88, 0.17, "plastic_white", seg=8)


def pouf(mb, x, y):
    kit.cyl(mb, x, y, 0.0, 0.40, 0.19, "plastic_white", seg=8)


# --------------------------------------------------------------------------- #
#  屋上（黒い木のデッキ・黒いベンチ・黄緑の植え込み・金網のフェンス）
# --------------------------------------------------------------------------- #
def shrub_bed(mb, x0, y0, x1, y1, z, rng, pitch=0.80):
    """植え込み: 低い縁石に黄緑の低木を並べる。z は床の上面。"""
    kit.box(mb, x0, y0, z, x1, y1, z + 0.28, "floor_concrete", top="soil_dark")
    along_x = (x1 - x0) >= (y1 - y0)
    length = (x1 - x0) if along_x else (y1 - y0)
    n = max(1, int(length / pitch))
    r = max(0.18, min(x1 - x0, y1 - y0) * 0.5 + 0.04)
    for i in range(n):
        f = (i + 0.5) / n
        cx = x0 + (x1 - x0) * (f if along_x else 0.5)
        cy = y0 + (y1 - y0) * (0.5 if along_x else f)
        h = 0.34 + rng.uniform(0.0, 0.16)
        kit.blob(mb, cx, cy, z + 0.28 + h * 0.4, r, r, h * 0.6, "plant_yellow",
                 seg=5, rings=2)


def park_bench(mb, x, y, ang=0.0, w=1.60):
    """黒いパークベンチ（背もたれつき）。正面 = ang の向き。"""
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.22, 0.42, hw, 0.20, 0.47, "plastic_black")
    t.box(mb, -hw, -0.24, 0.47, hw, -0.20, 0.86, "plastic_black")
    for sx in (-1, 1):
        t.box_nb(mb, sx * (hw - 0.12) - 0.03, -0.22, 0.0, sx * (hw - 0.12) + 0.03,
                 0.20, 0.42, "metal_dark")


def mesh_fence(mb, a, b, z, h=1.8, post=2.0):
    """金網のフェンス（厚みのある板 + 支柱）。a, b は 2D。"""
    n = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / post))
    kit.thick_quad(mb, (a[0], a[1], z + 0.05), (b[0], b[1], z + 0.05),
                   (b[0], b[1], z + h), (a[0], a[1], z + h), 0.04, "metal_gray")
    for k in range(n + 1):
        f = k / n
        kit.box_c(mb, a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, z,
                  0.06, 0.06, z + h + 0.04, "metal_dark")

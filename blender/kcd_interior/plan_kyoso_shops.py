"""共創棟 1F の店: カフェとコンビニ（公園側の帯）。内装の色だけで雰囲気を出し、店名・ロゴは描かない。"""

import math

from . import furn_kyoso as FK, furniture as F, kit
from . import plan_kyoso_dims as D

WOOD = "kyoso_wood_oak"
SAGE = "kyoso_sb_sage"


def _on_top(mb, z, build):
    """build() が積んだ頂点を z だけ持ち上げる（カウンターの上の機器用）。"""
    base = len(mb.verts)
    build()
    kit.lift(mb, base, z)


def starbucks(c, mb):
    """公園側の角の丸いセージの箱（厨房）と、その前の L 字のカウンター・ショーケース・客席。"""
    FK.sb_box(mb, 12.2, 36.6, 18.55, 39.1, D.SB_TOP)
    # カウンター（前線 Y = 34.4）。白い天板, オーク突板の前板, セージの台輪。東の端で奥へ折れる
    F.counter(mb, 12.2, 34.4, 16.4, 35.1, h=1.0, body=WOOD, top="plastic_white", toe=False)
    F.counter(mb, 11.6, 34.4, 12.2, 36.6, h=1.0, body=WOOD, top="plastic_white", toe=False)
    kit.box(mb, 11.6, 34.4, 0.0, 16.4, 34.46, 0.1, SAGE)
    F.showcase(mb, 17.4, 34.75, math.pi, w=1.6, d=0.7, rng=c.rng)
    kit.box(mb, 18.2, 34.4, 0.0, 18.6, 35.1, 1.0, WOOD, top="plastic_white")
    _on_top(mb, 1.0, lambda: F.espresso_machine(mb, 15.2, 34.75, math.pi))
    _on_top(mb, 1.0, lambda: F.register(mb, 13.0, 34.75, math.pi))
    # フェイシア: 厚いセージの帯 + 銅色の細い帯
    kit.box(mb, 11.6, 34.4, 2.9, 18.6, 36.6, 3.7, SAGE)
    kit.box(mb, 11.6, 34.37, 2.84, 18.6, 34.43, 2.9, "kyoso_sb_copper")
    kit.box(mb, 11.6, 34.4, 3.7, 18.6, 36.6, 3.78, "kyoso_sb_top")
    # 箱の正面（南）は白い六角タイル、東面は茶色の文様の壁
    kit.box(mb, 12.4, 36.57, 0.9, 18.4, 36.6, 2.9, "kyoso_tile_hex_white")
    kit.box(mb, 12.14, 36.9, 0.0, 12.2, 39.0, 3.0, "kyoso_sb_pattern")
    # 客席: 丸机と椅子 2 脚の組
    for tx in (15.2, 17.6):
        F.round_table(mb, tx, 32.2, r=0.35, top="sb_wood")
        for dy, ang in ((-0.62, D.FACE_PARK), (0.62, D.FACE_MALL)):
            F.chair_min(mb, kit.T(tx, 32.2 + dy, 0.0, ang), WOOD, "kyoso_steel_black")
            FK.seat_mark(mb, tx, 32.2 + dy, 0.0, ang, hw=0.22, hd=0.22, inner=0.44)


def familymart(c, mb):
    """東の壁沿いの冷蔵ケース、ゴンドラ 3 本、レジ、西の壁沿いのコーヒーマシン。"""
    rng = c.rng
    x0, y0, x1, y1 = D.FM
    kit.plate(mb, x0, y0, x1, y1, 0.012, "floor_tile_white")
    for fy in (33.0, 37.0):
        F.fridge_case(mb, x0 + 0.46, fy, D.FACE_WEST, length=4.0, rng=rng)
    F.fridge_case(mb, -16.0, y1 - 0.46, D.FACE_MALL, length=7.2, rng=rng)
    for gx in (-18.5, -16.3, -14.1):
        F.gondola(mb, gx, 36.2, D.FACE_EAST, length=2.4, rng=rng)
    F.counter(mb, -16.6, 33.0, -13.0, 33.6, h=1.0, body="counter_wood", top="counter_wood")
    for rx in (-15.8, -13.8):
        _on_top(mb, 1.0, lambda rx=rx: F.register(mb, rx, 33.3, math.pi))
    # 西の壁沿い: コーヒーマシン 2 台と黒いカップ棚
    F.counter(mb, -8.5, 32.0, -7.8, 35.0, h=1.0, body="counter_wood", top="counter_stone",
              toe=False)
    for my in (32.8, 34.2):
        _on_top(mb, 1.0, lambda my=my: F.espresso_machine(mb, -8.15, my, D.FACE_EAST))
    kit.box(mb, -8.3, 35.3, 0.0, -7.8, 36.3, 1.7, "plastic_black")

"""共創棟 1F の店: スターバックス風のカフェとファミリーマート風のコンビニ。"""

import math

from . import furn_kyoso as FK, furniture as F, kit, shell
from . import plan_kyoso_dims as D

WOOD = "kyoso_wood_oak"
SAGE = "kyoso_sb_sage"


def _on_top(mb, z, build):
    """build() が積んだ頂点を z だけ持ち上げる（カウンターの上の機器用）。"""
    base = len(mb.verts)
    build()
    kit.lift(mb, base, z)


def starbucks(c, mb, bar):
    """mb: 箱・カウンター・壁。bar: カウンター席（ガラス沿い）。"""
    FK.sb_box(mb, -13.5, 10.6, -4.5, 14.0, D.SB_TOP)
    # カウンター（前線 Y = 9.0）。白い天板, オーク突板の前板, セージの台輪
    F.counter(mb, -12.9, 9.0, -4.5, 9.7, h=1.0, body=WOOD, top="plastic_white", toe=False)
    kit.box(mb, -12.9, 9.0, 0.0, -4.5, 9.7, 0.1, SAGE)
    kit.box(mb, -4.5, 9.0, 0.0, -3.7, 10.6, 0.96, WOOD)
    kit.box(mb, -4.54, 8.96, 0.96, -3.66, 10.64, 1.0, "plastic_white")
    F.showcase(mb, -4.1, 11.2, 0.0, w=1.2, d=0.7, rng=c.rng)
    # フェイシア: 厚い角丸の帯 + 銅色の細い帯
    kit.box(mb, -12.9, 9.0, 2.9, -4.5, 10.6, 3.7, SAGE)
    kit.box(mb, -12.9, 8.97, 2.84, -4.5, 9.03, 2.9, "kyoso_sb_copper")
    kit.box(mb, -12.9, 9.0, 3.7, -4.5, 10.6, 3.78, "kyoso_sb_top")
    # 背面の壁（箱の南面）
    kit.box(mb, -12.8, 10.57, 0.9, -4.7, 10.6, 3.0, "kyoso_tile_hex_white")
    kit.box(mb, -5.6, 10.52, 0.0, -4.8, 10.6, 2.0, "plastic_white")
    _on_top(mb, 1.0, lambda: F.espresso_machine(mb, -9.0, 9.35, math.pi))
    _on_top(mb, 1.0, lambda: F.register(mb, -6.0, 9.35, math.pi))
    # 箱の西面: 受け渡し窓, オークの台, 茶色の文様の壁, サイレン
    kit.box(mb, -13.56, 10.75, 0.9, -13.5, 11.85, 1.9, "plastic_black")
    kit.box(mb, -13.8, 10.7, 0.0, -13.5, 11.9, 0.9, WOOD)
    kit.box(mb, -13.56, 12.0, 0.0, -13.5, 14.0, 3.0, "kyoso_sb_pattern")
    FK.siren(mb, -13.5, 13.2, 3.0)
    # ガラス沿いのカウンター席
    F.counter(bar, -13.0, 4.4, -4.5, 5.0, h=1.0, body=WOOD, top=WOOD, toe=False)
    for k in range(12):
        sx = -12.6 + 0.7 * k
        F.stool(bar, sx, 5.45, math.pi, mat=WOOD, h=0.75)
        FK.seat_mark(bar, sx, 5.45, 0.0, math.pi, hw=0.2, hd=0.2, inner=0.4)


def familymart(c, mb):
    rng = c.rng
    x0, y0, x1, y1 = D.FM
    kit.plate(mb, x0, y0, x1, y1, 0.012, "floor_tile_white")
    # 東面 (X = -38) と南面 (Y = 6.8) はガラス張り。自動扉は Y 8.5..10.3
    FK.glass_wall(mb, (x1, y0), (x1, y1), 3.4, gaps=((1.7, 3.5),), door_top=3.4)
    FK.glass_wall(mb, (x0, y0 + 0.02), (x1, y0 + 0.02), 3.4)
    kit.box(mb, -38.15, y0, 3.4, -38.0, y1, 4.6, "kyoso_fm_white")
    kit.box(mb, -38.17, y0, 3.5, -38.0, y1, 3.62, "kyoso_fm_green")
    kit.box(mb, -38.17, y0, 3.66, -38.0, y1, 3.69, "kyoso_fm_blue")
    for gx in (-47.8, -45.6, -43.4):
        F.gondola(mb, gx, 11.4, math.pi * 0.5, length=3.0, rng=rng)
    for fx in (-48.0, -44.0, -40.0):
        F.fridge_case(mb, fx, 13.75, math.pi, length=4.0, rng=rng)
    F.counter(mb, -44.6, 8.0, -40.6, 8.8, h=1.0, body="counter_wood", top="counter_wood")
    for rx in (-43.6, -41.6):
        _on_top(mb, 1.0, lambda rx=rx: F.register(mb, rx, 8.4, math.pi))
    # 西の壁沿い: コーヒーマシン 2 台と黒いカップ棚
    F.counter(mb, -49.8, 8.6, -49.1, 11.6, h=1.0, body="counter_wood", top="counter_stone",
              toe=False)
    for my in (9.2, 10.8):
        _on_top(mb, 1.0, lambda my=my: F.espresso_machine(mb, -49.45, my, -math.pi * 0.5))
    kit.box(mb, -49.9, 11.8, 0.0, -49.4, 12.8, 1.7, "plastic_black")

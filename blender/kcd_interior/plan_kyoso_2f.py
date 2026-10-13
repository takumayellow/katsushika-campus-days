"""共創棟 2F のラウンジ（スラブ B、床 FL 5.4）。z=0 で組んで最後に持ち上げる。

ソファの島・机と長椅子の組・丸机と椅子、吹き抜けに向いた長いカウンター、
西の壁沿いに学生支援課の窓口と忘れ物の棚。
"""

from . import furn_kyoso as FK, furniture as F, kit
from . import plan_kyoso_dims as D

WOOD = "kyoso_wood_oak"
BLACK = "kyoso_steel_black"


def _sofa(mb, x, y, ang, mat="kyoso_sofa_grey"):
    FK.modular_sofa(mb, x, y, ang, mat=mat)
    FK.seat_mark(mb, x, y, 0.0, ang, hw=1.2, hd=0.42, inner=2.1)


def lounge(c, mb):
    base = len(mb.verts)
    for sx in (0.0, 9.5):
        _sofa(mb, sx, 20.0, D.FACE_PARK)
        _sofa(mb, sx, 22.6, D.FACE_MALL, mat="kyoso_sofa_beige")
        FK.side_table(mb, sx, 21.3)
    # 机と長椅子の組を 3 つ
    for tx in (-2.0, 2.5, 7.0):
        F.table(mb, tx, 24.8, 0.0, w=1.6, d=0.8, top=WOOD, leg=BLACK)
        F.bench(mb, tx, 24.0, D.FACE_PARK, w=1.6, mat=WOOD)
        F.bench(mb, tx, 25.6, D.FACE_MALL, w=1.6, mat=WOOD)
    # 丸机とラウンジチェア
    for ty in (21.0, 23.5):
        F.round_table(mb, 12.5, ty, r=0.4, top=WOOD)
        for dx in (-0.8, 0.8):
            F.lounge_chair(mb, 12.5 + dx, ty, D.FACE_WEST if dx < 0 else D.FACE_EAST)
    # 吹き抜けに向いた長いカウンターとスツール
    F.counter(mb, -3.6, 27.4, 10.2, 27.9, h=1.0, body=WOOD, top=WOOD, toe=False)
    for k in range(13):
        sx = -3.0 + 1.0 * k
        F.stool(mb, sx, 26.85, D.FACE_PARK, mat=WOOD, h=0.72)
        FK.seat_mark(mb, sx, 26.85, 0.0, D.FACE_PARK, hw=0.2, hd=0.2, inner=0.4)
    # 学生支援課の窓口と忘れ物の棚（西の壁沿い）
    F.counter(mb, 15.8, 21.0, 16.6, 24.2, h=1.05, body="counter_wood", top="counter_stone",
              toe=False)
    FK.tumbler_shelf(mb, 16.0, 25.6, 16.6, 27.2, rng=c.rng)
    kit.lift(mb, base, D.Z2)

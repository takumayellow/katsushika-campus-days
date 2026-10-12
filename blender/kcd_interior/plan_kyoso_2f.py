"""共創棟 2F のラウンジ（床 FL 5.4）。z=0 で組んで最後に持ち上げる。"""

import math

from . import furn_kyoso as FK, furniture as F, kit
from . import plan_kyoso_dims as D

WOOD = "kyoso_wood_oak"


def lounge(c, mb):
    base = len(mb.verts)
    for sx in (-12.0, -8.0):
        FK.modular_sofa(mb, sx, 8.0, 0.0)
        FK.seat_mark(mb, sx, 8.0, 0.0, 0.0, hw=1.2, hd=0.42, inner=2.1)
    FK.side_table(mb, -10.0, 8.0)
    # 窓際のカウンターと椅子 4
    F.counter(mb, -12.0, 13.4, -8.0, 14.0, h=1.0, body=WOOD, top=WOOD, toe=False)
    for k in range(4):
        sx = -11.4 + 1.0 * k
        F.stool(mb, sx, 12.75, 0.0, mat=WOOD, h=0.72)
        FK.seat_mark(mb, sx, 12.75, 0.0, 0.0, hw=0.2, hd=0.2, inner=0.4)
    # 入口の前の帯: 机と長椅子の組を 3 つ
    for tx in (-12.6, -9.2, -5.8):
        F.table(mb, tx, 5.7, 0.0, w=1.6, d=0.8, top=WOOD)
        F.bench(mb, tx, 4.9, 0.0, w=1.6, mat=WOOD)
        F.bench(mb, tx, 6.5, math.pi, w=1.6, mat=WOOD)
    # 窓際の丸机と椅子
    for ty in (10.0, 11.4):
        F.round_table(mb, -14.2, ty, r=0.4, top=WOOD)
        for dx in (-0.8, 0.8):
            F.lounge_chair(mb, -14.2 + dx, ty, -math.pi / 2 if dx < 0 else math.pi / 2)
    F.sofa(mb, -6.0, 9.6, 0.0, w=2.0)
    F.round_table(mb, -6.0, 11.0, r=0.4, top=WOOD)
    # 学生支援課の窓口と忘れ物の棚
    F.counter(mb, -4.0, 8.0, -3.2, 11.0, h=1.05, body="counter_wood", top="counter_stone",
              toe=False)
    FK.tumbler_shelf(mb, -2.6, 11.4, -2.0, 13.0, rng=c.rng)
    kit.lift(mb, base, D.Z2)

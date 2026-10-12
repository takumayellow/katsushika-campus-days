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
    # 学生支援課の窓口と忘れ物の棚
    F.counter(mb, -4.0, 8.0, -3.2, 11.0, h=1.05, body="counter_wood", top="counter_stone",
              toe=False)
    FK.tumbler_shelf(mb, -2.6, 11.4, -2.0, 13.0, rng=c.rng)
    kit.lift(mb, base, D.Z2)

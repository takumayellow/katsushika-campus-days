"""寮の屋上テラス。

公式の写真: 黒に近い茶のウッドデッキ、黒いパークベンチ、黄緑の低木の植え込み、金網のフェンス、
白い塔屋。デッキの床とパラペット・フェンスは dorm_floor2.shell、塔屋は dorm_stairs.roof_stair。
ここでは植え込みとベンチと看板を置く。位置は推定。
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_interior import kit                          # noqa: E402
from kcd_interior import signs                        # noqa: E402
from kcd_route import dorm_rooms_props as R           # noqa: E402
from kcd_route.dorm_levels import ZR                  # noqa: E402


def build(c):
    mb = c.furn("roof")
    base = len(mb.verts)
    # 南（入口側）の手すり際と東の手すり際に植え込み、その手前にベンチ
    R.shrub_bed(mb, -7.00, 4.00, -2.60, 4.90, 0.0, c.rng)
    R.shrub_bed(mb, 2.60, 4.00, 7.00, 4.90, 0.0, c.rng)
    R.shrub_bed(mb, 6.70, 11.00, 7.40, 25.00, 0.0, c.rng)
    for x in (-5.60, -3.80, 3.80, 5.60):
        R.park_bench(mb, x, 6.60, ang=math.pi, w=1.60)
    for y in (13.00, 18.00, 23.00):
        R.park_bench(mb, 5.40, y, ang=-math.pi * 0.5, w=1.60)
    kit.lift(mb, base, ZR)
    signs.wall(c, mb, -2.60, 38.00, ZR + 1.50, ang=-math.pi * 0.5, ja="屋上テラス",
               en="Roof Terrace", w=1.40, h=0.34)
    c.poi("okujo", 0.0, 14.00, ZR)
    return mb

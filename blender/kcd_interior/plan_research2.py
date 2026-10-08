"""第2研究棟: 1F 食堂 + 2F 自習とカフェのフロア + 研究棟側のロビー。

寸法は plan_research2_dims、躯体は plan_research2_shell、1F の中身は plan_research2_1f が持つ。
ここは組み立ての順番だけを決める。
"""

from . import furn_research2 as FR
from . import plan_research2_1f as P1
from . import plan_research2_2f as P2
from . import plan_research2_dims as D
from . import plan_research2_shell as S

FURN = ("hall", "serving", "seats1", "stairs", "counters2", "seats2", "terrace")


def _seats(c, sink):
    """座面の Empty を D.SEATS の順に並べる（家具ヘルパの記録は使わない）。"""
    for _name, mb in c.furns:
        mb.seats = []
    for x, y, z, ang in D.SEATS:
        FR.seat_mark(sink, x, y, z - D.SEAT_H, ang)


def build(c):
    S.build(c)
    M = {kind: c.furn(kind) for kind in FURN}
    c.spawn(*D.SPAWN)
    c.exit(*D.EXIT)

    P1.build(c, M)
    P2.build(c, M)

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    _seats(c, M["hall"])
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens)
    c.note("1F 食堂（配膳口・返却口・窓際の下がり天井）+ 2F 自習とカフェのフロア（木の天井・"
           "厨房前のカウンター・テラス）+ 研究棟側のロビー")

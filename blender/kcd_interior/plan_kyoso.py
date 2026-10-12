"""共創棟 1F: ラーニングスクエア・スターバックス・ファミリーマート・ホール・ラウンジ、2F: ラウンジ。

寸法は plan_kyoso_dims、躯体は plan_kyoso_shell、区画は plan_kyoso_zones / _shops / _2f、
看板は plan_kyoso_signs に分ける。
"""

from . import common
from . import plan_kyoso_2f as P2
from . import plan_kyoso_dims as D
from . import plan_kyoso_shell as SH
from . import plan_kyoso_shops as SHOP
from . import plan_kyoso_signs as SIGNS
from . import plan_kyoso_zones as Z

# Unity が 02_lobby と 05_lounge を名前で引くので、この順を変えない。
KINDS = ("lobby", "cafe", "store", "lounge", "ls", "cafelounge", "lounges", "core")


def build(c):
    SH.envelope(c)
    common.entry_kit(c, D.Z_CEIL, door_w=D.DOOR_W)
    M = {k: c.furn(k) for k in KINDS}
    SH.ceilings_1f(c)
    SH.slabs_2f(c)
    SH.columns(c)
    SH.stair(c)
    SH.core(c, M["core"])

    Z.hall(c, M["lobby"])
    SHOP.starbucks(c, M["cafe"], M["cafe"])
    SHOP.familymart(c, M["store"])
    P2.lounge(c, M["lounge"])
    Z.learning_square(c, M["ls"])
    Z.cafe_lounge(c, M["cafelounge"])
    Z.lounges(c, M["lounges"])
    SIGNS.build(c, c.wall)

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F=ラーニングスクエア・スターバックス・ファミリーマート、2F=ラウンジ。q_coffee の舞台")
    c.seats = sum(len(s["anchors"]) for s in c.seat_list())

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

    # プレビュー用の照明（暖色）: 1F は天井の下、2F は 2F の天井の下
    c.light_color = (1.0, 0.80, 0.56)
    c.lights_from([(x, y) for x in range(-50, 31, 6) for y in (8.0, 11.6)], D.Z_CEIL - 0.1,
                  energy=440.0, radius=1.6)
    c.lights_from([(x, y) for x in (-13.0, -8.0, -3.5) for y in (6.5, 10.0, 13.0)],
                  D.Z2 + 3.0, energy=170.0, radius=1.4)

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F=ラーニングスクエア・スターバックス・ファミリーマート、2F=ラウンジ。q_coffee の舞台")
    c.seats = sum(len(s["anchors"]) for s in c.seat_list())

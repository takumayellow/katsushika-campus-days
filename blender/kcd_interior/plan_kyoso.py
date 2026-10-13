"""共創棟 1F: エントランスホール・中ホール・ラーニングスクエア・スターバックス・ファミリーマート・ラウンジ、
2F: ラウンジ。

寸法は plan_kyoso_dims、躯体は plan_kyoso_shell、区画は plan_kyoso_zones / _shops / _2f、
看板は plan_kyoso_signs に分ける。西のブロックと奥のブロックは局所座標で組み、common.turned で置く。
"""

from . import common
from . import plan_kyoso_2f as P2
from . import plan_kyoso_dims as D
from . import plan_kyoso_shell as SH
from . import plan_kyoso_shops as SHOP
from . import plan_kyoso_signs as SIGNS
from . import plan_kyoso_zones as Z

# Unity が 02_lobby と 05_lounge を名前で引くので、この順を変えない。
KINDS = ("lobby", "cafe", "store", "lounge", "ls", "cafelounge", "lounges", "core", "hall")

# 1F の天井の下の照明。前の列は 2 列（Y 8.0 と 11.6）に並べる
FRONT_ROWS = (8.0, 11.6)


def _ceiling_lights(c, pts):
    c.lights_from(pts, D.Z_CEIL - 0.1, energy=440.0, radius=1.6)


def _west(c, M):
    with common.turned(c, D.W_DX, 0.0, 0.0):
        SH.west_block(c)
        SH.core(c, M["core"], *D.CORE_W, side_wall=True)
        SHOP.familymart(c, M["store"])


def _back(c, M):
    with common.turned(c, D.B_DX, D.B_DY, 0.0):
        SH.back_front(c)
        SH.back_ceilings(c)
        SH.slabs_2f(c)
        SH.back_columns(c)
        SH.stair(c)
        SHOP.starbucks(c, M["cafe"], M["cafe"])
        P2.lounge(c, M["lounge"])
        Z.learning_square(c, M["ls"])
        _ceiling_lights(c, [(x, y) for x in range(-34, -1, 6) for y in FRONT_ROWS])
        c.lights_from([(x, y) for x in (-13.0, -8.0, -3.5) for y in (6.5, 10.0, 13.0)],
                      D.Z2 + 3.0, energy=170.0, radius=1.4)


def build(c):
    SH.envelope(c)
    common.entry_kit(c, D.Z_CEIL, door_w=D.DOOR_W)
    M = {k: c.furn(k) for k in KINDS}
    SH.ceilings_1f(c)
    SH.columns(c)
    SH.partitions(c)
    SH.core(c, M["core"], *D.CORE_E)

    # プレビュー用の照明（暖色）: 1F は天井の下、2F は 2F の天井の下
    c.light_color = (1.0, 0.80, 0.56)
    _west(c, M)
    _back(c, M)
    Z.hall(c, M["lobby"])
    Z.cafe_lounge(c, M["cafelounge"])
    Z.lounges(c, M["lounges"])
    Z.middle_hall(c, M["hall"])
    SIGNS.build(c, c.wall)

    _ceiling_lights(c, [(x, y) for x in range(-26, 31, 6) for y in FRONT_ROWS])
    _ceiling_lights(c, [(x, y) for x in range(-8, 31, 6) for y in (17.5, 22.0, 26.5)]
                    + [(27.0, 32.0), (27.0, 36.5)])

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F=エントランスホール・中ホール・ラーニングスクエア・スターバックス・ファミリーマート、"
           "2F=ラウンジ。q_coffee の舞台")
    c.seats = sum(len(s["anchors"]) for s in c.seat_list())

"""共創棟 1F: エントランスホール・スタジオ・エスカレーター・ラーニングスクエア1/2・曲線テーブルのホール・
スターバックス・ファミリーマート、2F: ラウンジ、3F: 吹き抜けから見えるガラスの教室。

寸法は plan_kyoso_dims、躯体は plan_kyoso_shell、エスカレーターは plan_kyoso_esc、
区画は plan_kyoso_zones / _shops / _2f / _3f、看板は plan_kyoso_signs に分ける。座標はすべて建物のローカル。
"""

from . import common
from . import plan_kyoso_2f as P2
from . import plan_kyoso_3f as P3
from . import plan_kyoso_dims as D
from . import plan_kyoso_esc as ESC
from . import plan_kyoso_shell as SH
from . import plan_kyoso_shops as SHOP
from . import plan_kyoso_signs as SIGNS
from . import plan_kyoso_zones as Z

# Unity が 02_lobby と 05_lounge を名前で引くので、lobby と lounge の位置を変えない。
KINDS = ("lobby", "cafe", "store", "lounge", "ls", "studio", "ls2", "core", "hall")


def _inside_any(x, y, rects):
    return any(r[0] <= x <= r[2] and r[1] <= y <= r[3] for r in rects)


def _lights(c):
    """プレビュー用の照明（暖色）。1F は天井の下、2F・3F はその階の天井の下、吹き抜けは頂部の下。"""
    c.light_color = (1.0, 0.80, 0.56)
    pts = ([(x, y) for x in range(-26, 4, 6) for y in (8.0, 11.6)]
           + [(x, y) for x in range(8, 42, 6) for y in (7.0, 11.6)]
           + [(x, y) for x in range(-26, 17, 6) for y in (17.5, 22.0, 26.5)]
           + [(x, y) for x in (22.0, 34.0) for y in (16.5, 29.5)]
           + [(x, y) for x in (-18.0, -12.0) for y in (33.0, 37.0)]
           + [(13.0, 32.0), (16.5, 32.0)])
    skip = (D.CORE_E, D.CORE_W, D.ESC_VOID, D.STAIR_HOLE)
    c.lights_from([p for p in pts if not _inside_any(p[0], p[1], skip)],
                  D.Z_CEIL - 0.1, energy=440.0, radius=1.6)
    c.lights_from([(x, y) for x in (-2.0, 4.0, 10.0, 14.5) for y in (16.4, 21.0, 25.4)],
                  D.Z_CEIL2, energy=170.0, radius=1.4)
    c.lights_from([(x, y) for x in (-2.0, 2.5, 7.0) for y in (32.0, 36.5)],
                  D.Z_ATRIUM - 0.1, energy=900.0, radius=2.0)
    c.lights_from([(x, y) for x in (-2.0, 7.0) for y in (23.5, 27.8)],
                  D.Z_CEIL3 - 0.1, energy=170.0, radius=1.4)


def build(c):
    SH.envelope(c)
    common.entry_kit(c, D.Z_CEIL, door_w=D.DOOR_W)
    M = {k: c.furn(k) for k in KINDS}
    SH.ceilings_1f(c)
    SH.columns(c)
    SH.core_east(c, M["core"])
    SH.core_west(c, M["core"])
    SH.park_band(c, c.wall)
    SH.slabs_2f(c, c.wall)
    SH.atrium(c, c.wall)
    P3.shell_3f(c, c.wall)
    SH.stair(c, c.wall)
    ESC.build(c)

    Z.lobby(c, M["lobby"])
    Z.studio(c, M["studio"])
    Z.learning_square(c, M["ls"])
    Z.hall(c, M["hall"])
    Z.learning_square_2(c, M["ls2"])
    SHOP.starbucks(c, M["cafe"])
    SHOP.familymart(c, M["store"])
    P2.lounge(c, M["lounge"])
    SIGNS.build(c, c.wall)
    _lights(c)

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F=エントランスホール・スタジオ・ラーニングスクエア1/2・ホール・スターバックス・"
           "ファミリーマート、2F=ラウンジ、3F=ガラスの教室（見るだけ）。q_coffee の舞台")
    c.seats = sum(len(s["anchors"]) for s in c.seat_list())

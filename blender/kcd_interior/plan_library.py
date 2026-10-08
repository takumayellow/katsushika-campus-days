"""図書館棟: 1F ブックギャラリー + 2F 開架書架・閲覧席・黙考書院 + 3F ホワイエ + 3〜4F 大ホール。

寸法は plan_library_dims、躯体は plan_library_shell、階ごとの中身は plan_library_1f / gallery /
2f / foyer / hall / seating、看板は plan_library_signs が持つ。ここは組み立ての順番だけを決める。
家具の MeshBuilder は spec §8 の番号の順（furn_library_01_entry 〜 16_stage）に作る。
"""

import math

from . import furn_library as FL
from . import plan_library_1f as P1
from . import plan_library_2f as P2
from . import plan_library_dims as D
from . import plan_library_foyer as PF
from . import plan_library_gallery as PG
from . import plan_library_hall as PH
from . import plan_library_seating as PS
from . import plan_library_shell as S
from . import plan_library_signs as SG

FURN = ("entry", "counter", "lobby", "gallery", "west2f", "stacks", "frontdesk", "carrel",
        "group", "mokko", "stair", "foyer", "hallshell", "seats", "balcony", "stage")
SPAWN = (0.0, 5.8, D.Z1)
EXIT = (0.0, 4.55, D.Z1)


def _island_facing(x, y):
    """ロビーの円弧のソファの座面: いちばん近い島の中心を向く角度。"""
    (cx, cy), _r, _open = min(D.ISLANDS, key=lambda i: math.hypot(i[0][0] - x, i[0][1] - y))
    return math.atan2(-(cx - x), cy - y)


def _seats(c, sink):
    """座面の Empty を spec §3 の表の順に並べる（家具ヘルパの記録は使わない）。"""
    for _name, mb in c.furns:
        mb.seats = []
    for x, y, z, ang in D.SEATS:
        if ang is None:
            ang = _island_facing(x, y)
        FL.seat_mark(sink, x, y, z - D.SEAT_H, ang)


def build(c):
    S.build(c, SG.HANGS)
    M = {kind: c.furn(kind) for kind in FURN}
    c.spawn(*SPAWN)
    c.exit(*EXIT)

    P1.build(c, M)
    PG.build(c, M["gallery"])
    P2.build(c, M)
    PF.build(c, M["foyer"])
    PH.build_shell(c, M["hallshell"])
    PS.build_seats(c, M["seats"])
    PS.build_balcony(c, M["balcony"])
    PH.build_stage(c, M["stage"])

    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    SG.build(c, M)
    _seats(c, M["entry"])
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens)
    c.note("1F ブックギャラリー（段状の書架・赤いソファのロビー・カウンター）+ 2F 開架書架 14 連・"
           "キャレル・黙考書院・グループ学習室 5 室 + 3F ホワイエ + 3〜4F 大ホール（1 階席 480・"
           "2 階席 117）")

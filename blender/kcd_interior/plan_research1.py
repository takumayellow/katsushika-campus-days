"""第1研究棟（1F 全域 + 西と東の EV ホールの 2F）。

中央の吹き抜け（入口ホール・FJR710・窓口）から南北 2 本の廊下が東西へ延び、室が並ぶ。
寸法と出典は docs/ref/interiors/research1/spec.md、座標の表は plan_research1_dims。
"""

import math

from . import furniture as F
from . import plan_research1_atrium as atrium
from . import plan_research1_core as core
from . import plan_research1_dims as D
from . import plan_research1_rooms as rooms
from . import plan_research1_shell as shell_r1
from . import plan_research1_signs as signs_r1
from .kit import T


def _seats(c):
    """座面の Empty を spec §4-3 の順に並べる（家具ヘルパの記録は使わない）。

    01〜08 は吹き抜けの丸テーブルの椅子（椅子の中心、正面は卓の中心）、09〜10 は西側ラウンジの
    ベンチ。記録は c.floor に積む（research1 は kit.lift を使わないので vi は 0 でよい）。
    """
    for mb in c.builders():
        mb.seats = []
    for ti, ai in D.TABLE_SEATS:
        tx, ty = D.TABLES[ti]
        th = D.CHAIR_ANGLES[ai]
        t = T(tx + D.CHAIR_R * math.cos(th), ty + D.CHAIR_R * math.sin(th), D.Z1,
              th + math.pi * 0.5)
        F._seat(c.floor, t, 0, 0.25, -0.25, 0.25, 0.5, 0.20)
    for x, y in D.SEAT_BENCHES:
        F._seat(c.floor, T(x, y, D.Z1, 0.0), 0, D.BENCH_W * 0.5, -0.24, 0.22, D.BENCH_W, 0.20)
    c.seats += len(D.TABLE_SEATS) + len(D.SEAT_BENCHES)


def build(c):
    shell_r1.build(c)
    c.spawn(*D.SPAWN)
    c.exit(*D.EXIT)
    atrium.build(c)
    core.build(c)
    rooms.build(c)
    _seats(c)
    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    signs_r1.build(c)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F 吹き抜け（トップライト・FJR710・窓口）+ 南北 2 本の廊下 + 西と東の EV ホール（2F まで）"
           " + 実験室・研究室・産学試作開発室・西側ラウンジ")

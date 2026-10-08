"""第1研究棟（1F 全域 + 西と東の EV ホールの 2F）。

中央の吹き抜け（入口ホール・FJR710・窓口）から南北 2 本の廊下が東西へ延び、室が並ぶ。
寸法と出典は docs/ref/interiors/research1/spec.md、座標の表は plan_research1_dims。
"""

from . import plan_research1_atrium as atrium
from . import plan_research1_core as core
from . import plan_research1_dims as D
from . import plan_research1_rooms as rooms
from . import plan_research1_shell as shell_r1
from . import plan_research1_signs as signs_r1


def build(c):
    shell_r1.build(c)
    c.spawn(*D.SPAWN)
    c.exit(*D.EXIT)
    atrium.build(c)
    core.build(c)
    rooms.build(c)
    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    signs_r1.build(c)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F 吹き抜け（トップライト・FJR710・窓口）+ 南北 2 本の廊下 + 西と東の EV ホール（2F まで）"
           " + 実験室・研究室・産学試作開発室・西側ラウンジ")

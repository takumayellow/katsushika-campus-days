"""講義棟（spec: docs/ref/interiors/lecture/spec.md）。

組み立ての順:
躯体（plan_lecture_shell）→ 中廊下・コア・入口ロビー・吹き抜け（plan_lecture_core）→
2F・3F の東側の廊下とエスカレータの穴（plan_lecture_upper）→
101 の段床と 270 席（plan_lecture_hall）・黒板と木の壁（plan_lecture_101）→ 看板（plan_lecture_signs）→ POI・NPC・カメラ。
寸法・POI・NPC・カメラ・看板の表は plan_lecture_dims が持つ。
"""

from . import plan_lecture_101 as R101
from . import plan_lecture_core as CORE
from . import plan_lecture_dims as D
from . import plan_lecture_hall as HALL
from . import plan_lecture_shell as SHELL
from . import plan_lecture_signs as SIGNS
from . import plan_lecture_upper as UPPER

# 家具のメッシュ（furn_lecture_<nn>_<区画名>）
FURN = ("hall_seats", "hall_desks", "hall_front", "corridor", "lobby", "signs")
# 生協のレジの札の位置（生協のモジュールができるまでの仮置き。カウンターの前の Y 39.0）
REGISTERS = ((36.6, 39.0), (37.6, 39.0), (38.6, 39.0), (39.6, 39.0))


def build(c):
    M = {kind: c.furn(kind) for kind in FURN}
    SHELL.build(c)
    CORE.build(c, M, SIGNS.hang_roots())
    UPPER.build(c, SIGNS.hang_roots())
    HALL.build_floor(c.floor)
    seats = HALL.build_seats(c, M["hall_seats"], M["hall_desks"])
    R101.build(M["hall_front"])
    for x in (11.0, 17.8, 24.6):
        for y in (8.0, 13.0, 17.5):
            c.light(x, y, D.CEIL_101 - 0.9, energy=90.0, radius=1.6)
    SIGNS.build(c, M["signs"], REGISTERS)
    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens)
    c.note("中廊下・黒いコア・入口ロビー・6 層の吹き抜け・101 の %d 席の段床" % seats)

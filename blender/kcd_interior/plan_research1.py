"""第1研究棟（1F 全域 + 西と東の EV ホールの 2F）。

中央の吹き抜け（入口ホール・FJR710・窓口）から南北 2 本の廊下が東西へ延び、室が並ぶ。
寸法と出典は docs/ref/interiors/research1/spec.md、座標の表は plan_research1_dims。
"""

from . import furniture as F
from . import plan_research1_atrium as atrium
from . import plan_research1_core as core
from . import plan_research1_dims as D
from . import plan_research1_rooms as rooms
from . import plan_research1_shell as shell_r1
from . import plan_research1_signs as signs_r1
from .kit import T

# 裏から見えなくする材質（下向きの天井・トップライトの発光面・スラブの下面）。Unity の Lit は裏面を
# 描かないので、Blender のプレビューもそろえる（俯瞰で天井の裏が室を隠さないように）
BACKFACE_CULLED = ("research1_ceiling", "research1_slab_exposed", "research1_skylight_glow")


def _seats(c):
    """座面の Empty を spec §4-3 の順に並べる（家具ヘルパの記録は使わない）。

    01〜02 は西側ラウンジのベンチ。記録は c.floor に積む（research1 は kit.lift を使わないので
    vi は 0 でよい）。
    """
    for mb in c.builders():
        mb.seats = []
    for x, y in D.SEAT_BENCHES:
        F._seat(c.floor, T(x, y, D.Z1, 0.0), 0, D.BENCH_W * 0.5, -0.24, 0.22, D.BENCH_W, 0.20)
    c.seats += len(D.SEAT_BENCHES)


def _cull_backfaces():
    """BACKFACE_CULLED の材質の裏面を描かない（Blender の外ではなにもしない）。"""
    try:
        import bpy
    except ImportError:
        return
    mats = getattr(getattr(bpy, "data", None), "materials", None)
    if mats is None:
        return
    for name in BACKFACE_CULLED:
        mat = mats.get(name)
        if mat is not None:
            mat.use_backface_culling = True


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
    _cull_backfaces()
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("1F 吹き抜け（トップライト・FJR710・窓口）+ 南北 2 本の廊下 + 西と東の EV ホール（2F まで）"
           " + 実験室・研究室・産学試作開発室・西側ラウンジ")

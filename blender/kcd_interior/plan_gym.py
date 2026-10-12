"""体育館（メインアリーナ・観覧席とギャラリー・トレーニングルーム・ホワイエ・東棟・サブアリーナ・用具庫）。

寸法と出典は docs/ref/interiors/gym/spec.md、座標の表は plan_gym_dims。q_gym の舞台。
"""

from . import furniture as F
from . import plan_gym_arena as arena
from . import plan_gym_dims as D
from . import plan_gym_east as east
from . import plan_gym_foyer as foyer
from . import plan_gym_shell as shell_g
from . import plan_gym_signs as signs_g
from . import plan_gym_stand as stand
from . import plan_gym_store as store
from . import plan_gym_train as train
from .kit import T

# 裏から見えなくする材質（下向きの天井板の上面ではなく、屋根の下面を見せるための設定）
BACKFACE_CULLED = ("gym_ceiling_dark", "gym_ceiling_offwhite")


def _seats(c):
    """座面の Empty を D.SEATS の順に並べる（家具ヘルパの記録は使わない）。"""
    for mb in c.builders():
        mb.seats = []
    for _kind, x, y, z in D.SEATS:
        F._seat(c.floor, T(x, y, z, 0.0), 0, 0.6, -0.24, 0.22, 1.2, 0.20)
    c.seats += len(D.SEATS)


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
    shell_g.build(c)
    c.spawn(*D.SPAWN)
    c.exit(*D.EXIT)
    arena.build(c)
    stand.build(c)
    foyer.build(c)
    east.build(c)
    train.build(c)
    store.build(c)
    _seats(c)
    for name, x, y, z in D.POIS:
        c.poi(name, x, y, z)
    for x, y, z in D.NPCS:
        c.npc(x, y, z)
    signs_g.build(c)
    _cull_backfaces()
    for suffix, loc, target, lens in D.CAMS:
        c.cam(suffix, loc, target, lens=lens)
    c.note("アリーナ（コート・吊りゴール・屋根トラス）+ 2F 観覧席・ギャラリー・トレーニングルーム"
           " + ホワイエ + 東棟（廊下・体育事務室・サブアリーナ）+ 用具庫")

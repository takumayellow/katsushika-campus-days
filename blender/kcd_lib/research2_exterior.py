"""第2研究棟（と 1〜2F の食堂）の外観。仕様は docs/ref/interiors/research2/spec.md の §0・§1・§6。

2 層の食堂が台座で、その上の 3〜6F の研究棟は台座の外形のうち矩形の範囲だけに建つ。
全面ガラスは食堂の面（南南西から西北西の曲面とロビーの面）だけ。"""
import math

from . import facade, geom

FLOORS_PODIUM = 2
# 局所（原点 = 研究棟の入口の真下）からワールドへ。spec.md §0。
_YAW = math.radians(24.5514)
_ORIGIN = (6.0634, -21.6235)

# 3〜6F の床の範囲（局所 X, Y）。spec.md §0。
TOWER_RECT = (-39.64, 15.4, 31.86, 41.3)
# 北北東のレンガの階段室（局所 X, Y）。spec.md §1-5。3F から上で、研究棟の北の壁より 0.35 m 出す。
CORE_RECT = (23.3, 37.3, 31.1, 41.65)

# 食堂の面（外形の辺 index。campus.json の 15 点の順）。3〜10 = 西から南の曲面、12 = 研究棟側のロビーの面。
# 西北西の突き出し（0〜2）は裏方の壁。
GLASS_EDGES = (3, 4, 5, 6, 7, 8, 9, 10, 12)
FOOTPRINT_POINTS = 15
# 屋上機器（局所の中心 X, Y、半幅、半奥行）
TOWER_ROOF_BOXES = ((-24.0, 24.0, 6.0, 4.0), (10.0, 26.0, 7.0, 3.5))
ROOF_BOX_H = 3.0


def _lw(x, y):
    """局所 (X, Y) をワールド (x, y) にする。x = 6.0634 + 0.9096X + 0.4156Y、y = -21.6235 - 0.4156X + 0.9096Y。"""
    c, s = math.cos(_YAW), math.sin(_YAW)
    return (_ORIGIN[0] + c * x + s * y, _ORIGIN[1] - s * x + c * y)


def _rect(x0, y0, x1, y1):
    return [_lw(x0, y0), _lw(x1, y0), _lw(x1, y1), _lw(x0, y1)]


def build(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    assert len(loop) == FOOTPRINT_POINTS, "GLASS_EDGES は campus.json の 15 点の順に対応する"
    h = b["height"]
    lv = b["levels"]
    podium_fh = 4.8
    podium_h = podium_fh * FLOORS_PODIUM
    tower_fh = (h - podium_h) / (lv - FLOORS_PODIUM)

    # 台座（1〜2F）：食堂の面だけ全面ガラス、ほかは窓のない壁（裏方・階段室）
    glass = [i for i in GLASS_EDGES if i < len(loop)]
    solid = [i for i in range(len(loop)) if i not in glass]
    facade.add_solid(mb, loop, 0.0, podium_h, "concrete_grey", edges=solid)
    for f, sill in ((0, 0.9), (1, 0.55)):
        facade.add_facade(mb, loop, 0.0, podium_fh, f, f + 1,
                          wall="concrete_light", glass="glass_clear",
                          seg=3.4, sill=sill, header=0.40, inset=0.45, mullion=0.26,
                          edges=glass)
    mb.add_ngon_flat(loop, podium_h, "roof_grey")
    facade.add_parapet(mb, loop, podium_h, 0.7, 0.35, "concrete_light")

    # 研究棟（3〜6F）：台座の上の矩形。水平連続窓
    tower = _rect(*TOWER_RECT)
    facade.add_facade(mb, tower, podium_h, tower_fh, 0, lv - FLOORS_PODIUM,
                      wall="concrete_grey", glass="glass_dark",
                      seg=2.9, sill=1.10, header=0.45, inset=0.42, mullion=0.34)
    mb.add_ngon_flat(tower, podium_h, "concrete_grey", flip=True)   # 南へ張り出す所の裏
    mb.add_ngon_flat(tower, h, "roof_grey")
    facade.add_parapet(mb, tower, h, 1.1, 0.4, "concrete_light")
    for x, y, w, d in TOWER_ROOF_BOXES:
        box = _rect(x - w, y - d, x + w, y + d)
        mb.add_prism(box, h + 1.1, h + 1.1 + ROOF_BOX_H, "concrete_grey", "roof_grey")
        facade.add_parapet(mb, box, h + 1.1 + ROOF_BOX_H, 0.35, 0.25, "metal_grey")

    # 北北東のレンガの階段室（3F から屋上の上まで）
    core = _rect(*CORE_RECT)
    mb.add_slab(core, podium_h, h + 1.8, "brick_red")
    facade.add_facade(mb, core, podium_h, tower_fh, 0, lv - FLOORS_PODIUM, wall="brick_red", glass="glass_dark",
                      seg=2.2, sill=1.5, header=0.9, inset=0.28, mullion=0.5)
    # 入口と看板は kcd_lib.entrances が置く

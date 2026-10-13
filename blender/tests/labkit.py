"""第1・第2実験棟のプランのテストが共有する部品（スタブで組む・間取りの距離を測る）。"""

import math

import stubs
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx

from conftest import CAMPUS_JSON

SEED = 1709
SPAWN_CLEAR = 0.75     # 出入口に立つ位置から家具までの空き（InteriorExitSpawnPlayTests と同じ）
STAND_CLEAR = 0.45     # POI・人の位置から家具までの空き
CAM_CLEAR = 0.30
DOOR_CLEAR = 0.45
BLOCK_Z0, BLOCK_Z1 = 0.25, 1.6   # 立つ人の邪魔になる高さの帯


def ear_candidates(contours):
    out = []
    for c in contours:
        try:
            idx = stubs.tessellate_polygon([c])
        except ValueError:
            idx = [(0, k, k + 1) for k in range(1, len(c) - 1)]
        out.extend(tuple(c[i] for i in t) for t in idx)
    return out


def build(bid):
    """スタブの三角分割で建物を組み、(Ctx, spec) を返す。呼び出し側が MonkeyPatch の中で使う。"""
    specs = ispec.build_specs(ispec.load_campus(CAMPUS_JSON))
    c = Ctx(specs[bid], seed=SEED)
    registry.get(bid).build(c)
    signs.check(c)
    c.flush_seats()
    return c


def sections(c):
    out = {"floor": c.floor, "wall": c.wall}
    out.update({name.split("_", 2)[2].split("_", 1)[1]: mb for name, mb in c.furns})
    return out


def tri_count(mb):
    return sum(max(0, len(f) - 2) for f in mb.faces)


def _seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def _inside(px, py, poly):
    hit = False
    n = len(poly)
    for i in range(n):
        ax, ay = poly[i]
        bx, by = poly[(i + 1) % n]
        if (ay > py) != (by > py) and px < ax + (py - ay) / (by - ay) * (bx - ax):
            hit = not hit
    return hit


def clearance(c, x, y, z0=BLOCK_Z0, z1=BLOCK_Z1):
    """(x, y) から、高さ z0..z1 に掛かる面（壁・家具）までの水平距離の最小。面の中なら 0。"""
    best = 1e9
    for name, mb in [("wall", c.wall)] + list(c.furns):
        vs = mb.verts
        for f in mb.faces:
            zs = [vs[i][2] for i in f]
            if max(zs) <= z0 or min(zs) >= z1:
                continue
            poly = [(vs[i][0], vs[i][1]) for i in f]
            if _inside(x, y, poly):
                return 0.0
            for k in range(len(poly)):
                a, b = poly[k], poly[(k + 1) % len(poly)]
                best = min(best, _seg_dist(x, y, a[0], a[1], b[0], b[1]))
    return best

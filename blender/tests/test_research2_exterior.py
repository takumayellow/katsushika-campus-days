"""第2研究棟（と食堂）の外観 (#275)。

外形の外の芝の低い棟を無くし、3〜6F の研究棟を spec.md の床の範囲に建て、2 層の食堂を台座にする。
全面ガラスは食堂の面だけ。地面の高さの輪郭と入口の位置は変えない。
"""

import math

import pytest

from kcd_lib import buildings, entrances, geom, research2_exterior as R2
from kcd_lib.mesh import MeshBuilder
from test_lab1_exterior import WALL_STEP, WALL_ZS, _inside_or_on, _ray_hits_tri

PODIUM_H = 9.6
# 作り直す前（8a5c8a0）の近景の合計 190,391 / 200,000 を越えないための、この建物の三角数の上限
TRIS_MAX = 24000
# 研究棟が台座の外へ張り出すのは南の 3.7 m と階段室の 1.2 m。それより外には何も出ない
MAX_OVERHANG = 4.5


def _r2(campus_data):
    return next(b for b in campus_data["buildings"] if b["id"] == "research2")


def _to_local(p):
    c, s = math.cos(R2._YAW), math.sin(R2._YAW)
    dx, dy = p[0] - R2._ORIGIN[0], p[1] - R2._ORIGIN[1]
    return (c * dx - s * dy, s * dx + c * dy)


@pytest.fixture(scope="module")
def built(campus_data, campus_frame):
    mb = MeshBuilder("bld_research2")
    ctx = {"entrance": [], "sign": [], "footprints": [], "bg_window_spacing": 2.4}
    buildings.BUILDERS["office"](mb, _r2(campus_data), campus_frame, ctx)
    return mb


@pytest.fixture(scope="module")
def loop(campus_data):
    return geom.ensure_ccw(geom.dedup(_r2(campus_data)["footprint"]))


def _tris(mb):
    return sum(len(f) - 2 for f in mb.faces)


def test_local_frame_matches_the_footprint(loop):
    """局所座標の換算が campus.json の外形と合う（spec.md §0 の先頭の頂点）。"""
    x, y = R2._lw(-39.68, 41.26)
    assert math.dist((x, y), (-12.882, 32.394)) < 0.05
    assert any(math.dist(R2._lw(31.78, 41.35), p) < 0.05 for p in loop)


def test_no_grass_wing_outside_the_outline(built):
    assert "grass" not in set(built.mat_names)


def test_nothing_sticks_out_of_the_outline(loop, built):
    far = [v for v in built.verts
           if not _inside_or_on((v[0], v[1]), loop)
           and geom.dist_point_poly_edges((v[0], v[1]), loop) > MAX_OVERHANG]
    assert far == []


def test_ground_level_stays_on_the_outline(loop, built):
    low = [v for v in built.verts if v[2] < PODIUM_H - 0.4]
    assert low
    assert [v for v in low if not _inside_or_on((v[0], v[1]), loop)] == []


def test_ground_level_walls_close_the_outline(loop, built):
    tris = []
    for f in built.faces:
        vs = [built.verts[i] for i in f]
        if min(v[2] for v in vs) < max(WALL_ZS):
            tris.extend((vs[0], vs[k], vs[k + 1]) for k in range(1, len(vs) - 1))
    holes = []
    n = len(loop)
    for i in range(n):
        a, b = loop[i], loop[(i + 1) % n]
        e, nrm, L = buildings._lab1_edge(a, b)
        steps = max(1, int((L - 0.2) / WALL_STEP))
        for k in range(steps + 1):
            q = geom.add(a, geom.mul(e, 0.1 + (L - 0.2) * k / steps))
            for z in WALL_ZS:
                o = (q[0] + nrm[0], q[1] + nrm[1], z)
                d = (-nrm[0], -nrm[1], 0.0)
                near = [t for t in tris
                        if min(p[0] for p in t) - 1.1 <= q[0] <= max(p[0] for p in t) + 1.1
                        and min(p[1] for p in t) - 1.1 <= q[1] <= max(p[1] for p in t) + 1.1]
                if not any(_ray_hits_tri(o, d, *t, 2.0) for t in near):
                    holes.append((i, round(L, 2), round(geom.length(geom.sub(q, a)), 2), z))
    assert holes == []


def test_tower_stands_on_the_slab_range(built):
    """3F より上（階段室を除く）は spec.md の床の範囲 X −39.64〜31.86、Y 15.4〜41.3 に収まる。"""
    x0, y0, x1, y1 = R2.TOWER_RECT
    cx0, cy0, cx1, cy1 = R2.CORE_RECT
    up = []
    for v in built.verts:
        if v[2] <= PODIUM_H + 0.8:
            continue
        X, Y = _to_local((v[0], v[1]))
        if cx0 - 0.1 <= X <= cx1 + 0.1 and cy0 - 0.1 <= Y <= cy1 + 0.1:
            continue
        up.append((X, Y))
    assert up
    assert [p for p in up if not (x0 - 0.1 <= p[0] <= x1 + 0.1 and y0 - 0.1 <= p[1] <= y1 + 0.1)] == []
    xs = [p[0] for p in up]
    ys = [p[1] for p in up]
    assert min(xs) < x0 + 0.1 and max(xs) > x1 - 0.1
    assert min(ys) < y0 + 0.1 and max(ys) > y1 - 0.1


def test_clear_glass_only_on_the_cafeteria_faces(loop, built):
    """全面ガラス（glass_clear）は食堂の面の辺の上だけ。研究棟（3F より上）には貼らない。"""
    n = len(loop)
    glass_edges = [(loop[i], loop[(i + 1) % n]) for i in R2.GLASS_EDGES]
    idx = built.mat_names.index("glass_clear")
    bad = []
    for f, m in zip(built.faces, built.face_mat):
        if m != idx:
            continue
        for i in f:
            v = built.verts[i]
            on = any(_seg_dist((v[0], v[1]), a, b) < 0.8 for a, b in glass_edges)
            if v[2] > PODIUM_H + 0.1 or not on:
                bad.append(v)
    assert bad == []


def _seg_dist(p, a, b):
    ab = geom.sub(b, a)
    t = max(0.0, min(1.0, geom.dot(geom.sub(p, a), ab) / geom.dot(ab, ab)))
    return math.dist(p, geom.add(a, geom.mul(ab, t)))


def test_door_is_unchanged(loop):
    doors = [d for d in entrances.DOORS if d[0] in ("research2", "cafeteria")]
    assert doors == [("cafeteria", (14.5, -22.0), (0.0, 1.0), "loop", +1, 3.4, entrances.STANDARD),
                     ("research2", (40.9, -22.0), (0.0, 1.0), "loop", -1, 3.4, entrances.STANDARD)]


def test_tris_stay_within_the_budget(built):
    assert 0 < _tris(built) < TRIS_MAX

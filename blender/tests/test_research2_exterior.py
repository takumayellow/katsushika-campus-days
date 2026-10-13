"""第2研究棟（と食堂）の外観 (#275, #293)。

3〜6F の研究棟を spec.md の床の範囲に建て、2 層の食堂を台座にする。台座の屋根は屋上緑化。
全面ガラスは食堂の面だけで、西北西の曲面の 2F は奥に引いたガラスの前の屋外テラス。
地面の高さで外形の外に出るのは、テラスへ上がる外階段だけ。入口の位置は変えない。
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


def test_grass_only_on_the_podium_roof(loop, built):
    """芝（grass）は台座の屋根の面だけ。外形の外の芝の低い棟は無い。"""
    idx = built.mat_names.index("grass")
    vs = [built.verts[i] for f, m in zip(built.faces, built.face_mat) if m == idx for i in f]
    assert vs
    assert [v for v in vs if abs(v[2] - PODIUM_H) > 1e-6 or not _inside_or_on((v[0], v[1]), loop)] == []


def _on_stair(loop, p):
    """外階段の範囲（辺 STAIR_EDGE の外、頂点から踊り場と段の長さの中）に入るか。"""
    a, b = loop[R2.STAIR_EDGE], loop[(R2.STAIR_EDGE + 1) % len(loop)]
    e = geom.normalize(geom.sub(b, a))
    n = geom.outward_normal(a, b)
    t = geom.dot(geom.sub(p, a), e)
    d = geom.dot(geom.sub(p, a), n)
    run = R2.STAIR_LANDING + (R2.STAIR_STEPS - 1) * R2.STAIR_GOING
    return -0.01 <= t <= run + 0.01 and -0.01 <= d <= R2.STAIR_OFFSET[1] + 0.01


def test_nothing_sticks_out_of_the_outline(loop, built):
    far = [v for v in built.verts
           if not _inside_or_on((v[0], v[1]), loop)
           and geom.dist_point_poly_edges((v[0], v[1]), loop) > MAX_OVERHANG]
    assert far == []


def test_ground_level_stays_on_the_outline(loop, built):
    low = [v for v in built.verts if v[2] < PODIUM_H - 0.4]
    assert low
    assert [v for v in low if not _inside_or_on((v[0], v[1]), loop) and not _on_stair(loop, v)] == []


def test_outdoor_stair_climbs_to_the_terrace(loop, built):
    """外階段は外形の外にあり、頂点の側の踊り場が 2F のテラスの床（4.8 m）に着く。"""
    out = [v for v in built.verts if not _inside_or_on((v[0], v[1]), loop) and _on_stair(loop, v)]
    assert out
    tops = [v[2] for v in out if math.dist((v[0], v[1]), loop[R2.STAIR_EDGE]) < 2.6]
    assert max(tops) == pytest.approx(PODIUM_H / 2 + 1.0)   # 踊り場の手すりの上端
    assert min(v[2] for v in out) == pytest.approx(0.0)


def test_terrace_glass_is_set_back(loop, built):
    """テラスの辺では、2F の高さ（腰の手すりより上、天井まで）のガラスは外形の線から 3.5 m より奥。"""
    n = len(loop)
    edges = [(loop[i], loop[(i + 1) % n]) for i in R2.TERRACE_EDGES]
    idx = built.mat_names.index("glass_clear")
    front = []
    for f, m in zip(built.faces, built.face_mat):
        if m != idx:
            continue
        for i in f:
            v = built.verts[i]
            if 6.0 < v[2] < R2.TERRACE_CEILING and min(_seg_dist((v[0], v[1]), a, b) for a, b in edges) < 3.5:
                front.append(v)
    assert front == []


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
    """3F より上（レンガの塊 2 つを除く）は spec.md の床の範囲 X −39.64〜31.86、Y 15.4〜41.3 に収まる。"""
    x0, y0, x1, y1 = R2.TOWER_RECT
    up = []
    for v in built.verts:
        if v[2] <= PODIUM_H + 0.8:
            continue
        X, Y = _to_local((v[0], v[1]))
        if any(r[0] - 0.1 <= X <= r[2] + 0.1 and r[1] - 0.1 <= Y <= r[3] + 0.1
               for r in (R2.CORE_RECT, R2.WEST_CORE_RECT)):
            continue
        up.append((X, Y))
    assert up
    m = R2.LEDGE_OUT + 0.1   # 床の線の帯は面より前に出る
    assert [p for p in up if not (x0 - m <= p[0] <= x1 + m and y0 - m <= p[1] <= y1 + m)] == []
    xs = [p[0] for p in up]
    ys = [p[1] for p in up]
    assert min(xs) < x0 + 0.1 and max(xs) > x1 - 0.1
    assert min(ys) < y0 + 0.1 and max(ys) > y1 - 0.1


def test_clear_glass_only_on_the_cafeteria_faces(loop, built):
    """全面ガラス（glass_clear）は食堂の面の辺の上と、テラスの奥に引いたガラスの線の上だけ。
    研究棟（3F より上）には貼らない。"""
    n = len(loop)
    inner = geom.offset_polygon(loop, -R2.TERRACE_SETBACK)
    glass_edges = [(loop[i], loop[(i + 1) % n]) for i in R2.GLASS_EDGES]
    glass_edges += [(inner[i], inner[(i + 1) % n]) for i in R2.TERRACE_EDGES]
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


def test_brick_blocks_rise_above_the_tower_roof(built, campus_data):
    """レンガの塊は西の端と北北東の 2 つで、どちらも研究棟の屋根の上まで出る。"""
    h = _r2(campus_data)["height"]
    idx = built.mat_names.index("brick_red")
    pts = [built.verts[i] for f, m in zip(built.faces, built.face_mat) if m == idx for i in f]
    for r in (R2.CORE_RECT, R2.WEST_CORE_RECT):
        inside = [v for v in pts
                  if r[0] - 0.1 <= _to_local((v[0], v[1]))[0] <= r[2] + 0.1
                  and r[1] - 0.1 <= _to_local((v[0], v[1]))[1] <= r[3] + 0.1]
        assert inside, r
        assert max(v[2] for v in inside) == pytest.approx(h + R2.CORE_TOP)
        assert min(v[2] for v in inside) >= PODIUM_H - 1e-6


def test_doors_are_on_the_south_and_east_faces(loop):
    doors = [d for d in entrances.DOORS if d[0] in ("research2", "cafeteria")]
    assert doors == [("cafeteria", (14.5, -22.0), (0.0, 1.0), "loop", +1, 3.4, entrances.STANDARD),
                     ("research2", (50.5, 15.1509), (-1.0, 0.0), "loop", -1, 3.4, entrances.STANDARD)]


def test_tris_stay_within_the_budget(built):
    assert 0 < _tris(built) < TRIS_MAX


def test_nothing_blocks_the_space_in_front_of_the_doors(campus_data, campus_frame, built, loop):
    """扉の前の通り道（entrances.corridor）の上 4 m までに、壁も張り出しも無い。
    外形の線から 0.6 m 以内は、扉の風除室（entrances.py）が壁を置き換えるので数えない。"""
    ctx = {"entrance": [], "sign": [], "footprints": [], "bg_window_spacing": 2.4}
    doors = [d for d in entrances.plan(campus_data, campus_frame, ctx) if d["id"] in ("research2", "cafeteria")]
    assert len(doors) == 2
    tris = []
    for f in built.faces:
        vs = [built.verts[i] for i in f]
        tris.extend((vs[0], vs[k], vs[k + 1]) for k in range(1, len(vs) - 1))
    for dr in doors:
        poly = entrances.corridor(dr)
        xs, ys = [p[0] for p in poly], [p[1] for p in poly]
        near = [t for t in tris
                if max(p[0] for p in t) >= min(xs) and min(p[0] for p in t) <= max(xs)
                and max(p[1] for p in t) >= min(ys) and min(p[1] for p in t) <= max(ys)]
        hits = []
        for i in range(21):
            for j in range(21):
                a, b = i / 20, j / 20
                q = geom.add(geom.add(poly[0], geom.mul(geom.sub(poly[1], poly[0]), a)),
                             geom.mul(geom.sub(poly[3], poly[0]), b))
                if not geom.point_in_poly(q, poly):
                    continue
                if geom.point_in_poly(q, loop) or geom.dist_point_poly_edges(q, loop) < 0.6:
                    continue
                if any(_ray_hits_tri((q[0], q[1], 0.0), (0.0, 0.0, 1.0), *t, 4.0) for t in near):
                    hits.append((round(q[0], 2), round(q[1], 2)))
        assert hits == [], dr["id"]

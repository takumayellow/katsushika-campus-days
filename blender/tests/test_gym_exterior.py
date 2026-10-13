"""体育館の外観 (#240)。

北の別棟（部室棟）を無くして 6 階・21.8 m の 1 棟にし、アリーナの大壁と部室の棟で窓を変える。
地面の高さの輪郭・入口の位置・ミニマップの形（campus.json の輪郭）は変えない。
"""

import math

import pytest

from kcd_lib import buildings, entrances, geom, gym_exterior
from kcd_lib.mesh import MeshBuilder
from test_lab1_exterior import TOL, WALL_STEP, WALL_ZS, _inside_or_on, _ray_hits_tri

# 作り直す前（3d457f3）の三角数。屋内の窓の外の近景にも入るので、これより重くしない
GYM_TRIS_BEFORE = 4552
# 最も出が大きいのは庇と踊り場。それより外には何も出ない（北の別棟は輪郭の外へ 9 m 出ていた）
MAX_OVERHANG = 2.6


def _gym(campus_data):
    return next(b for b in campus_data["buildings"] if b["id"] == "gym")


@pytest.fixture(scope="module")
def built(campus_data, campus_frame):
    mb = MeshBuilder("bld_gym")
    ctx = {"entrance": [], "sign": [], "footprints": [], "bg_window_spacing": 2.4}
    buildings.BUILDERS["gym"](mb, _gym(campus_data), campus_frame, ctx)
    return mb


@pytest.fixture(scope="module")
def loop(campus_data):
    return geom.ensure_ccw(geom.dedup(_gym(campus_data)["footprint"]))


def _tris(mb):
    return sum(len(f) - 2 for f in mb.faces)


def test_gym_is_one_six_storey_building(campus_data):
    b = _gym(campus_data)
    assert (b["levels"], b["height"], b["style"]) == (6, 21.8, "gym")
    assert len(b["footprint"]) == 14


def test_gym_is_lighter_than_before(built):
    assert 0 < _tris(built) < GYM_TRIS_BEFORE


def test_gym_uses_brick_fins_and_roof_units(built):
    names = set(built.mat_names)
    assert {"brick_red", "louver_white", "glass_dark", "roof_grey", "metal_grey",
            "metal_white", "concrete_light"} <= names
    assert "concrete_grey" not in names     # 北の別棟の材


def test_gym_edges_split_into_arena_and_wing(loop, campus_frame):
    arena, wing = gym_exterior.split_edges(loop, campus_frame)
    assert sorted(arena + wing) == list(range(len(loop)))
    # アリーナの塊は西の大きな面（南・西・北）を全部持ち、部室の棟は東の長い面を持つ
    sides = {buildings._lab1_facing(campus_frame, loop[i], loop[(i + 1) % len(loop)])
             for i in arena}
    assert sides == {"n", "s", "w", "e"}
    longest_wing = max(wing, key=lambda i: math.dist(loop[i], loop[(i + 1) % len(loop)]))
    assert buildings._lab1_facing(campus_frame, loop[longest_wing],
                                  loop[(longest_wing + 1) % len(loop)]) == "e"


def test_gym_has_no_north_annex(loop, built):
    """輪郭から外へ出るのは庇・踊り場・フィン・ルーバーだけ（北の別棟は 9 m 出ていた）。"""
    far = [v for v in built.verts
           if not _inside_or_on((v[0], v[1]), loop)
           and geom.dist_point_poly_edges((v[0], v[1]), loop) > MAX_OVERHANG]
    assert far == []


def test_gym_ground_level_stays_on_the_outline(campus_data, loop, built):
    """2 階の床より下の頂点は外形の内側か外形の上にある（当たり判定の輪郭を変えない）。"""
    fh = _gym(campus_data)["height"] / _gym(campus_data)["levels"]
    low = [v for v in built.verts if v[2] < fh - 0.4]
    assert low
    assert [v for v in low if not _inside_or_on((v[0], v[1]), loop)] == []


def test_gym_ground_level_walls_close_the_outline(loop, built):
    """外形の各辺は、地面から 2.7 m まで面でふさがっている（当たり判定に穴が無い）。"""
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


def test_gym_door_is_unchanged_and_on_the_arena_south_face(loop, campus_frame):
    doors = [d for d in entrances.DOORS if d[0] == "gym"]
    assert doors == [("gym", (-2.1, 28.0), (0.0, 1.0), "loop", +1, 3.4, entrances.STANDARD)]
    n = len(loop)
    hits = [i for i in range(n)
            if buildings._lab1_door_t(loop[i], loop[(i + 1) % n], campus_frame, "gym") is not None
            and buildings._lab1_facing(campus_frame, loop[i], loop[(i + 1) % n]) == "s"]
    arena, _wing = gym_exterior.split_edges(loop, campus_frame)
    assert hits and all(i in arena for i in hits)


def test_gym_canopy_leaves_the_door_open(campus_data, campus_frame, loop, built):
    """南面の庇は入口の庇（半幅 CAN_S）の上を空け、両脇には出る。"""
    n = len(loop)
    door = None
    for i in range(n):
        a, c = loop[i], loop[(i + 1) % n]
        t = buildings._lab1_door_t(a, c, campus_frame, "gym")
        if t is not None and buildings._lab1_facing(campus_frame, a, c) == "s":
            e, _n, _L = buildings._lab1_edge(a, c)
            door = geom.add(a, geom.mul(e, t))
    assert door is not None
    fh = _gym(campus_data)["height"] / _gym(campus_data)["levels"]
    out = [(v[0], v[1]) for v in built.verts
           if abs(v[2] - fh) < 0.35 and not geom.point_in_poly((v[0], v[1]), loop)
           and geom.dist_point_poly_edges((v[0], v[1]), loop) > buildings.LAB1_FIN_D + 0.2]
    can_s = entrances.STANDARD["CAN_S"]
    assert [p for p in out if geom.length(geom.sub(p, door)) < can_s] == []
    assert [p for p in out if geom.length(geom.sub(p, door)) < can_s + 3.0]


def test_lab1_door_lookup_still_defaults_to_lab1(campus_data, campus_frame):
    lab = next(b for b in campus_data["buildings"] if b["id"] == "lab1")
    main, _annex = buildings._lab1_split(geom.ensure_ccw(geom.dedup(lab["footprint"])), campus_frame)
    assert any(buildings._lab1_door_t(main[i], main[i + 1], campus_frame) is not None
               for i in range(len(main) - 1))

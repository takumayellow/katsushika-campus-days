"""第1実験棟のプラン（kcd_interior.plan_lab1）: スタブで組み、区画・Empty・看板・カメラ・座席・三角数を確かめる。"""

import math

import pytest

import labkit
from kcd_interior import plan_lab1
from kcd_lib import signtext

ID = "lab1"
TRI_CAP = 24500
SIGNS = 10
POIS = {"lab": (-8.5, 15.8), "prep": (16.4, 26.0), "lab_b": (15.3, 13.7)}
NPCS = 6
CAMS = ["", "shop", "annex", "nmr", "room_b"]
# 区画ごとの三角形の上限（現在の値に 1 割強の余裕）。wall は看板の文字を含む。
SECTION_CAPS = {
    "floor": 30, "wall": 2530, "entry": 180, "corridor": 3810, "shop": 5860, "annex": 1520, "crib": 1500, "nmr": 1120, "room_a": 1150, "room_b": 2280,
}


@pytest.fixture(scope="module")
def built():
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", labkit.ear_candidates)
        yield labkit.build(ID)


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def test_spawn_and_exit(built):
    names = [n for n, _ in built.empties]
    assert names.count("spawn_%s" % ID) == 1
    assert names.count("exit_%s" % ID) == 1
    sp = dict(built.empties)["spawn_%s" % ID]
    assert sp[:2] == pytest.approx((0.0, built.spec.y_face + 0.15 + 1.5), abs=0.2)


def test_pois_keep_their_names(built):
    got = {n: p for n, p in _empties(built, "poi_%s_" % ID)}
    assert sorted(got) == sorted("poi_%s_%s" % (ID, k) for k in POIS)
    for k, (x, y) in POIS.items():
        assert got["poi_%s_%s" % (ID, k)] == pytest.approx((x, y, 0.0))


def test_npcs_are_numbered_from_one(built):
    got = [n for n, _ in _empties(built, "npc_%s_" % ID)]
    assert got == ["npc_%s_%d" % (ID, k) for k in range(1, NPCS + 1)]


def test_signs_are_numbered(built):
    assert len(built.signs) == SIGNS
    assert all(r["ja"].strip() and r["en"].strip() for r in built.signs)
    named = [r["empty"] for r in built.signs if r["empty"]]
    assert named == [n for n, _ in _empties(built, "sign_%s_" % ID)]


def test_cameras(built):
    assert [cam[0] for cam in built.cams] == CAMS


def test_seats_are_registered(built):
    assert len(built.seat_list()) >= 8
    anchors = [n for n, _ in built.empties if n.startswith("seat_%s_" % ID) and "_a" in n]
    assert len(anchors) >= 8


def test_seats_stand_on_the_floor_and_apart(built):
    ix0, iy0, ix1, iy1 = built.spec.inner()
    pts = []
    for q in built.seat_list():
        assert q["c"][2] == pytest.approx(0.0)
        for a in q["anchors"]:
            assert a[2] == pytest.approx(0.0)
            assert ix0 < a[0] < ix1 and iy0 < a[1] < iy1, a
            pts.append(a)
    for i, p in enumerate(pts):
        for q in pts[i + 1:]:
            assert math.hypot(p[0] - q[0], p[1] - q[1]) > 0.3, (p, q)


def test_triangles_fit_the_cap(built):
    assert built.tris() <= TRI_CAP


def test_every_section_has_a_cap(built):
    assert set(labkit.sections(built)) == set(SECTION_CAPS)


def test_sections_fit_their_triangle_caps(built):
    for name, mb in labkit.sections(built).items():
        assert 0 < labkit.tri_count(mb) <= SECTION_CAPS[name], name


def test_sections_stay_on_the_footprint(built):
    s, tol = built.spec, 0.01
    for name, mb in labkit.sections(built).items():
        vs = mb.verts
        assert min(v[0] for v in vs) >= s.x0 - tol and max(v[0] for v in vs) <= s.x1 + tol, name
        assert min(v[1] for v in vs) >= s.y_face - tol and max(v[1] for v in vs) <= s.y_back + tol, name


def test_pois_npcs_and_cameras_are_inside_the_walls(built):
    ix0, iy0, ix1, iy1 = built.spec.inner()
    pts = [p[:2] for n, p in built.empties if n.startswith(("poi_", "npc_", "spawn_"))]
    pts += [cam[1][:2] for cam in built.cams] + [cam[2][:2] for cam in built.cams]
    assert [p for p in pts if not (ix0 + 0.1 < p[0] < ix1 - 0.1 and iy0 < p[1] < iy1 - 0.1)] == []
    for cam in built.cams:
        assert 1.0 < cam[1][2] < plan_lab1.Z_TOP


def test_standing_places_are_clear_of_furniture(built):
    for n, p in built.empties:
        if n.startswith("spawn_"):
            assert labkit.clearance(built, p[0], p[1]) >= labkit.SPAWN_CLEAR, n
        elif n.startswith(("poi_", "npc_")):
            assert labkit.clearance(built, p[0], p[1]) >= labkit.STAND_CLEAR, n


def test_cameras_are_not_inside_solids(built):
    for name, pos, _look, *_ in built.cams:
        assert labkit.clearance(built, pos[0], pos[1], pos[2] - 0.05, pos[2] + 0.05)             >= labkit.CAM_CLEAR, name


def test_every_door_can_be_walked_through(built):
    doors = [(d, plan_lab1.COR_Y1 - 0.5) for d in plan_lab1.ROOM_DOORS + (plan_lab1.SHOP_DOOR,)]
    cx, cy = plan_lab1.CRIB
    doors += [(cx - 0.5, cy + 1.6), (cx, cy + 1.6), (cx + 0.9, cy + 1.6)]
    for x, y in doors:
        assert labkit.clearance(built, x, y) >= labkit.DOOR_CLEAR, (x, y)

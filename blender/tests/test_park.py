"""kcd_lib.park: にいじゅくみらい公園の池・せせらぎ・橋・地球釜・遊具・木 (#168)。"""

import math
import random

import pytest

from kcd_lib import geom, park, props, site
from kcd_lib import mesh as M
from kcd_lib.mesh import MeshBuilder


@pytest.fixture(scope="module")
def park_data():
    return park.load()


def _nz(poly):
    n = M._newell(poly)
    ln = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2)
    return n[2] / ln if ln > 0 else 0.0


def _near(p, polys, tol):
    """p が polys のどれかの中か、境界から tol 以内か（park.json の座標は 0.1 m に丸めてある）。"""
    for q in polys:
        if geom.point_in_poly(p, q):
            return True
        for a, b in zip(q, q[1:] + q[:1]):
            d = geom.sub(b, a)
            n = d[0] ** 2 + d[1] ** 2
            t = max(0.0, min(1.0, ((p[0] - a[0]) * d[0] + (p[1] - a[1]) * d[1]) / n)) if n else 0.0
            if geom.length(geom.sub(p, geom.add(a, geom.mul(d, t)))) <= tol:
                return True
    return False


def _faces(mb):
    return [[mb.verts[k] for k in f] for f in mb.faces]


def _built(campus_data, park_data):
    """build_campus と同じ順で、水底・橋・地球釜・パーゴラ・遊具を組む。"""
    data = park.apply_buildings(campus_data, park_data)
    occ, hard = site.Occupancy(), site.Occupancy()
    ground, props_mb, water = MeshBuilder("site_ground"), MeshBuilder("site_props_park"), MeshBuilder("site_water")
    park.build_bed(ground, park_data, occ, hard)
    n_bridge = park.build_bridges(ground, props_mb, data, park_data, hard)
    park.build_water(water, park_data)
    n_play = park.build_props(props_mb, park_data, occ, hard)
    return {"data": data, "occ": occ, "hard": hard, "ground": ground, "props": props_mb,
            "water": water, "n_bridge": n_bridge, "n_play": n_play}


@pytest.fixture(scope="module")
def built(campus_data, park_data):
    return _built(campus_data, park_data)


# ---- 高さ ----

def test_bed_sits_between_footway_and_water():
    assert site.Z_FOOT < park.Z_BED < site.Z_WATER


def test_walking_into_the_water_is_a_small_step():
    """芝から水底へは 2 cm 程度の段差で、乗り越えられる（#30）。"""
    assert park.Z_BED - site.Z_PARK <= 0.022


def test_bridge_deck_is_above_the_water_and_ramps_down_to_the_footway():
    prof = park._deck_profile((0.0, 0.0), (10.0, 0.0))
    assert prof[1][2] == prof[2][2] == park.BRIDGE_Z > site.Z_WATER
    for end, top in ((prof[0], prof[1]), (prof[3], prof[2])):
        assert end[2] - site.Z_FOOT == pytest.approx(0.002)
        run = math.hypot(top[0] - end[0], top[1] - end[1])
        assert run == pytest.approx(park.BRIDGE_RAMP)
        assert (top[2] - end[2]) / run <= 0.05


# ---- park.json と campus.json の組み合わせ ----

def test_apply_buildings_drops_kama_and_pergola_without_touching_the_input(campus_data, park_data):
    before = [dict(b) for b in campus_data["buildings"]]
    ids = {b["id"] for b in campus_data["buildings"]}
    drop = {park_data["kama"]["id"], park_data["pergola"]["id"]}
    assert drop <= ids

    out = park.apply_buildings(campus_data, park_data)

    assert campus_data["buildings"] == before
    out_ids = {b["id"] for b in out["buildings"]}
    assert out_ids == ids - drop
    heights = {b["id"]: b["height"] for b in out["buildings"]}
    for bid, h in park_data["heights"].items():
        assert heights[bid] == h


def test_park_trees_are_inside_the_park_and_out_of_the_water(campus_data, park_data):
    parks = park.park_polys(campus_data)
    water = park.water_poly(park_data)
    assert len(park_data["trees"]) > 300
    for x, y, kind in park_data["trees"]:
        assert kind in ("deciduous", "evergreen", "sakura")
        assert _near((x, y), parks, 0.1), (x, y)
        assert not geom.point_in_poly((x, y), water), (x, y)


def test_kama_and_play_spots_are_inside_the_park(campus_data, park_data):
    """遊具の場所は公園か、公園の中の遊び場（OSM の playground）の中。"""
    parks = park.park_polys(campus_data) + [geom.ensure_ccw(geom.dedup(a["polygon"]))
                                            for a in campus_data["areas"] if a["kind"] == "playground"]
    k = park_data["kama"]
    for p in [(k["x"], k["z"])] + [tuple(q) for q in park_data["play"]]:
        assert any(geom.point_in_poly(p, q) for q in parks), p


# ---- 組み立て ----

def test_one_bridge_crosses_the_water_and_lands_on_dry_ground(built, park_data):
    spans = park.bridge_spans(built["data"], park_data)
    assert built["n_bridge"] == len(spans) == 1
    water = park.water_poly(park_data)
    a, b, width = spans[0]
    assert not geom.point_in_poly(a, water) and not geom.point_in_poly(b, water)
    assert width > 0.0


def test_every_play_spot_gets_equipment(built, park_data):
    assert built["n_play"] == len(park_data["play"]) == 3


@pytest.mark.parametrize("name", ["ground", "water"])
def test_walkable_meshes_have_no_downward_faces(built, name):
    faces = _faces(built[name])
    assert faces
    assert [f for f in faces if _nz(f) < -0.99] == []


def test_water_surface_is_flat_at_water_height(built):
    for f in _faces(built["water"]):
        assert all(p[2] == pytest.approx(site.Z_WATER) for p in f)


def test_park_trees_avoid_water_bridge_kama_and_play(built, park_data):
    trees = park.park_trees(park_data, built["hard"], random.Random(0))
    water = park.water_poly(park_data)
    k = park_data["kama"]
    assert len(trees) > 400
    for x, y, sp, sc, _ in trees:
        assert not geom.point_in_poly((x, y), water)
        assert math.hypot(x - k["x"], y - k["z"]) > k["r"]
        for px, py in park_data["play"]:
            assert math.hypot(x - px, y - py) >= park.PLAY_CLEAR - site.Occupancy.CELL
        assert sp in (park.KEYAKI, park.ROUND, park.PINE) and 0.7 < sc < 1.2
    for i, a in enumerate(trees):
        for b in trees[i + 1:]:
            assert math.hypot(a[0] - b[0], a[1] - b[1]) >= park.MIN_TREE_GAP


def test_collect_trees_keeps_every_park_tree(campus_data, campus_frame, built, park_data):
    fixed = park.park_trees(park_data, built["hard"], random.Random(0))
    occ, hard = site.Occupancy(), site.Occupancy()
    out = site.collect_trees(campus_data, campus_frame, occ, hard, random.Random(1), park_trees=fixed)
    kept = set(out)
    assert all(t in kept for t in fixed)


def test_species_numbers_match_tree_species():
    """KEYAKI / ROUND / PINE は props.TREE_SPECIES の並びの番号。並べ替えると桜が別の木になる。"""
    names = [name for name, _ in props.TREE_SPECIES]
    assert names[park.KEYAKI] == "keyaki"
    assert names[park.ROUND] == "round"
    assert names[park.PINE] == "pine"

"""kcd_route（寮までの回廊）の純 Python 部分 (#68)。

ground.check() は build_route.py が Blender の中で最初に呼ぶ検算。数字が動いたら Blender を
起動する前にここで落とす。"""

import pytest

from kcd_lib import mesh as M
from kcd_lib.mesh import MeshBuilder
from kcd_route import ground, props


@pytest.fixture(scope="module")
def route_data():
    return ground.load()


def test_ground_check_passes(route_data):
    info = ground.check(route_data, verbose=False)
    assert info["roads_kept"] == 24
    assert info["roads_dropped"] == 40
    assert info["roads_total"] == 64
    assert info["dropped_in_campus_json"] + info["dropped_shared_ge2"] == 40
    assert info["houses"] == 46
    assert abs(info["gate_crossing_z"] - ground.GATE_CENTER_Z) < 0.05
    assert info["dorm_clearance_house_m"] >= ground.DORM_KEEPOUT
    assert info["dorm_clearance_road_m"] > 0.5
    x0, z0, x1, z1 = info["content_bbox"]
    assert ground.BAND[0] <= x0 and x1 <= ground.GROUND_E
    assert -ground.GROUND_E <= z0 and z1 <= ground.GROUND_E


def test_road_selection(route_data):
    kept = ground.pick_roads(route_data)
    for r in kept:
        assert not r.get("in_campus_json")
        assert int(r.get("campus_shared_vertices") or 0) <= 1
        assert ground.road_width(r) > 0
    assert ground.excluded({"in_campus_json": True})
    assert ground.excluded({"campus_shared_vertices": 2})
    assert not ground.excluded({"campus_shared_vertices": 1})
    assert ground.road_width({"kind": "footway"}) == 2.5
    assert ground.road_width({"kind": "nope"}) == 4.0
    assert ground.road_width({"kind": "footway", "width": 3.2}) == 3.2


def test_band_meets_campus_ground_without_overlap():
    mb = MeshBuilder("band")
    area = ground.build_band(mb)
    x0, z0, x1, z1 = ground.BAND
    assert area == pytest.approx((x1 - x0) * (z1 - z0))
    assert x1 == -ground.GROUND_E
    assert len(mb.faces) == 1
    n = M._newell([mb.verts[k] for k in mb.faces[0]])
    assert n[2] > 0
    n_split, n_faces = mb.split_by_grid(ground.BAND_GRID)
    assert n_split == 1 and n_faces > 1
    longest = max(max(abs(a[i] - b[i]) for i in range(2))
                  for f in mb.faces
                  for a in (mb.verts[k] for k in f) for b in (mb.verts[k] for k in f))
    assert longest <= ground.BAND_GRID + 1e-9


def test_small_geometry_helpers():
    assert ground.dedup([(0, 0), (0, 0), (1, 0), (1, 1), (0, 0)]) == [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0)]
    assert ground.polyline_length([(0, 0), (3, 4), (3, 10)]) == pytest.approx(11.0)
    assert ground.crossing_z([(-350, 200), (-330, 260)], -340.0) == pytest.approx(230.0)
    assert ground.crossing_z([(0, 0), (1, 1)], 5.0) is None
    assert ground.in_rect((1, 1), (0, 0, 2, 2))
    assert not ground.in_rect((3, 1), (0, 0, 2, 2))
    assert ground.in_rect((2.4, 1), (0, 0, 2, 2), margin=0.5)
    sq = [(0, 0), (4, 0), (4, 4), (0, 4)]
    assert ground.point_in_poly((1, 1), sq) and not ground.point_in_poly((5, 1), sq)
    assert ground.dist_point_segment((0, 3), (-1, 0), (1, 0)) == pytest.approx(3.0)
    assert ground.seg_cross((0, 0), (2, 2), (0, 2), (2, 0))
    assert not ground.seg_cross((0, 0), (1, 0), (0, 1), (1, 1))


def test_fence_ring_has_no_gap_wider_than_player(route_data):
    """build_route.py が Blender の中で止める条件（塀の輪の隙間 < プレイヤーの直径）。"""
    kept = ground.pick_roads(route_data)
    houses = ground.pick_houses(route_data)
    enc = props.check_enclosure(ground.ANNEX, ground.GATE_Z, ground.WALL_X,
                                kept, houses, ground.road_width)
    assert enc["worst_open_m"] < props.PLAYER_DIAMETER, enc["worst_open_at"]


def test_ring_segments_leave_the_gate_open():
    segs = props.ring_segments(ground.ANNEX, ground.GATE_Z, ground.WALL_X)
    east = [(a, b) for _, a, b in segs if abs(a[0] - ground.WALL_X) < 1e-9 and abs(b[0] - ground.WALL_X) < 1e-9]
    assert east, "東辺（x = WALL_X）の塀が無い"
    lo, hi = ground.GATE_Z
    for a, b in east:
        z0, z1 = sorted((a[1], b[1]))
        assert z1 <= lo + 1e-9 or z0 >= hi - 1e-9, "門の口 %.2f..%.2f に塀が掛かる" % (lo, hi)

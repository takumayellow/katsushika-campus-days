"""tools/osm_route.py: 寮までの道のグラフ・最短経路・コリドー (#68)。

生データ (data/osm/raw_route.json) は gitignore なので、グラフとコリドーは合成データで試す。
後半は commit 済みの route.json を、出力された座標だけから測り直して突き合わせる。
"""

import math

import pytest

import osm_common as oc
import osm_route as R


def _identity(lon, lat):
    """合成データ用の投影: lon をそのまま x (m), lat を z (m) にする。"""
    return (lon, lat)


def _way(wid, nodes, pts, **tags):
    return {"id": wid, "nodes": nodes, "tags": tags,
            "geometry": [{"lon": x, "lat": z} for x, z in pts]}


# ---- 幾何 ----

def test_seg_dist():
    assert R.seg_dist((0, 3), (-1, 0), (1, 0)) == pytest.approx(3.0)
    assert R.seg_dist((4, 4), (0, 0), (1, 0)) == pytest.approx(5.0)
    assert R.seg_dist((3, 4), (0, 0), (0, 0)) == pytest.approx(5.0)


def test_poly_dist_is_symmetric_and_uses_both_directions():
    route = [(0.0, 0.0), (100.0, 0.0)]
    pts = [(50.0, 7.0)]
    assert R.poly_dist(pts, route) == pytest.approx(7.0)
    # 長い辺の途中に route の頂点が近い場合も拾う
    wall = [(-100.0, 5.0), (200.0, 5.0)]
    assert R.poly_dist(wall, [(50.0, 0.0), (50.0, -10.0)]) == pytest.approx(5.0)
    assert R.poly_dist(wall, route) == R.poly_dist(route, wall)


def test_polyline_length():
    assert R.polyline_length([(0, 0), (3, 4), (3, 10)]) == pytest.approx(11.0)
    assert R.polyline_length([(1, 1)]) == 0


# ---- 道路グラフ ----

# 1 ─(steps 20 m)─ 3 の真上を footway が山なりに回り (25.6 m)、下を私道 (20.1 m) が回る。
FOOT = _way(100, [1, 6, 3], [(0, 0), (10, 8), (20, 0)], highway="footway")
STEPS = _way(101, [1, 2, 3], [(0, 0), (10, 0), (20, 0)], highway="steps")
PRIVATE = _way(102, [1, 7, 3], [(0, 0), (10, -1), (20, 0)], highway="service", access="private")
MOTORWAY = _way(103, [1, 3], [(0, 0), (20, 0)], highway="motorway")
ISLAND = _way(200, [8, 9], [(500, 500), (510, 500)], highway="footway")


@pytest.fixture(scope="module")
def graph():
    return R.build_graph([FOOT, STEPS, PRIVATE, MOTORWAY, ISLAND], _identity)


def _edges(adj, a, b):
    return [e for e in adj[a] if e[0] == b]


def test_build_graph_weights(graph):
    np_, adj = graph
    assert np_[6] == (10, 8)
    (edge,) = _edges(adj, 1, 6)
    assert edge == (6, pytest.approx(math.hypot(10, 8)), 100, pytest.approx(math.hypot(10, 8)))
    (edge,) = _edges(adj, 1, 2)
    assert edge[1] == pytest.approx(10 * R.WALK_WEIGHT["steps"]) and edge[3] == pytest.approx(10)
    (edge,) = _edges(adj, 1, 7)
    assert edge[1] == pytest.approx(math.hypot(10, 1) * 8.0)
    # motorway は歩けないので辺を張らない
    assert not _edges(adj, 1, 3)


def test_build_graph_is_undirected(graph):
    _, adj = graph
    for a, edges in adj.items():
        for b, cost, wid, raw in edges:
            assert any(e[0] == a and e[2] == wid and e[1] == cost and e[3] == raw for e in adj[b])


@pytest.mark.parametrize("tags, weight", [
    ({"highway": "service", "service": "parking_aisle"}, 1.6),
    ({"highway": "footway", "foot": "no"}, 8.0),
    ({"highway": "footway", "access": "no"}, 8.0),
    # 私道は駐車場の通路より重い（後から上書きする）
    ({"highway": "service", "service": "parking_aisle", "access": "private"}, 8.0),
    ({"highway": "secondary"}, R.WALK_WEIGHT["secondary"]),
])
def test_build_graph_penalties(tags, weight):
    way = {"id": 1, "nodes": [1, 2], "tags": tags, "geometry": [{"lon": 0, "lat": 0}, {"lon": 3, "lat": 4}]}
    _, adj = R.build_graph([way], _identity)
    assert adj[1][0][1] == pytest.approx(5.0 * weight)
    assert adj[1][0][3] == pytest.approx(5.0)


def test_dijkstra_prefers_cheap_route_and_reports_real_length(graph):
    _, adj = graph
    nodes, wids, length = R.dijkstra(adj, 1, 3)
    assert nodes == [1, 6, 3]              # 階段 (20 m × 1.6) より遠回りの footway (25.6 m)
    assert wids == [100, 100]
    assert length == pytest.approx(2 * math.hypot(10, 8))   # 重みではなく実距離


def test_dijkstra_trivial_and_unreachable(graph):
    _, adj = graph
    assert R.dijkstra(adj, 1, 1) == ([1], [], 0.0)
    assert R.dijkstra(adj, 1, 9) is None


def test_nearest_node_breaks_ties_by_smaller_id():
    np_ = {5: (1.0, 0.0), 3: (-1.0, 0.0), 9: (0.0, 3.0)}
    assert R.nearest_node(np_, 0.0, 0.0) == (3, 1.0)
    assert R.nearest_node(np_, 0.0, 2.5) == (9, pytest.approx(0.5))


# ---- コリドー ----

@pytest.fixture(scope="module")
def corridor():
    return R.Corridor([
        {"key": "a", "_pts": [(0.0, 0.0), (100.0, 0.0)]},
        {"key": "b", "_pts": [(0.0, 50.0), (100.0, 50.0)]},
    ])


def test_corridor_distances(corridor):
    assert corridor.point_dist((50.0, 10.0)) == pytest.approx(10.0)
    assert corridor.point_dist((50.0, 45.0)) == pytest.approx(5.0)
    assert corridor.dist([(50.0, 20.0), (60.0, 20.0)]) == pytest.approx(20.0)
    assert corridor.near_keys([(50.0, 20.0)], 25.0) == ["a"]
    assert corridor.near_keys([(50.0, 20.0)], 30.0) == ["a", "b"]


def test_clip_cuts_exactly_at_the_limit(corridor):
    limit = 20.0
    line = [(-200.0, 10.0), (-50.0, 10.0), (50.0, 10.0), (150.0, 10.0)]
    (piece,) = corridor.clip(line, limit)
    assert len(piece) == 3
    assert piece[1] == (50.0, 10.0)
    cut = math.sqrt(limit ** 2 - 10.0 ** 2)
    assert piece[0] == pytest.approx((-cut, 10.0), abs=1e-4)
    assert piece[2] == pytest.approx((100.0 + cut, 10.0), abs=1e-4)
    for p in (piece[0], piece[2]):
        assert corridor.point_dist(p) <= limit
        assert corridor.point_dist(p) == pytest.approx(limit, abs=1e-4)


def test_clip_splits_into_pieces_and_drops_far_lines(corridor):
    # 内 → 外 → 内 は 2 本に切れる
    line = [(10.0, -5.0), (10.0, -80.0), (90.0, -80.0), (90.0, -5.0)]
    pieces = corridor.clip(line, 20.0)
    assert len(pieces) == 2
    assert pieces[0][0] == (10.0, -5.0) and pieces[0][-1] == pytest.approx((10.0, -20.0), abs=1e-4)
    assert pieces[1][0] == pytest.approx((90.0, -20.0), abs=1e-4) and pieces[1][-1] == (90.0, -5.0)
    assert corridor.clip([(0.0, -300.0), (100.0, -300.0)], 20.0) == []
    # 全部内側ならそのまま
    inner = [(10.0, 5.0), (90.0, 5.0)]
    assert corridor.clip(inner, 20.0) == [inner]


# ---- 出力レコード ----

@pytest.mark.parametrize("value, want", [
    ("3.5", 3.5), ("3.5 m", 3.5), ("12m", 12.0), (None, 7.0), ("wide", 7.0), (4, 4.0),
])
def test_f_parses_osm_lengths(value, want):
    assert R._f(value, 7.0) == want


def test_building_entry():
    closed_cw = [(0.0, 0.0), (0.0, 6.0), (10.0, 6.0), (10.0, 0.0), (0.0, 0.0)]
    e = {"id": 42, "tags": {"building": "house", "building:levels": "2.5", "name": "家"}}
    b = R.building_entry(e, closed_cw, campus_ids={42})
    assert b["id"] == "rt_42" and b["osm_id"] == 42 and b["name"] == "家" and b["kind"] == "house"
    assert b["levels"] == 2 and b["height"] == pytest.approx(6.4)
    assert b["area"] == 60.0
    assert b["bbox"] == [0.0, 0.0, 10.0, 6.0]
    assert b["in_campus_json"]
    assert len(b["footprint"]) == 4 and oc.ring_area(b["footprint"]) > 0

    tall = R.building_entry({"id": 7, "tags": {"height": "12 m", "building:levels": "3"}}, closed_cw, set(), "bld")
    assert tall["id"] == "bld_7" and tall["height"] == 12.0 and tall["levels"] == 3
    assert not tall["in_campus_json"]
    odd = R.building_entry({"id": 8, "tags": {"building:levels": "abc"}}, closed_cw, set())
    assert odd["levels"] is None and odd["height"] == 8.0


# 20 m × 10 m の寮（反時計回り）。辺 0 = 南の長辺, 辺 1 = 東の短辺
DORM = [(0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0)]
DORM_CENTER = (10.0, 5.0)
SOUTH_ROAD = {"osm_id": 1, "kind": "tertiary", "points": [[-10.0, -6.0], [30.0, -6.0]]}


def _east_road(x, kind="residential", osm_id=2):
    return {"osm_id": osm_id, "kind": kind, "points": [[x, -10.0], [x, 20.0]]}


def test_entrance_faces_the_road():
    ent = R.entrance_of(DORM, [SOUTH_ROAD], DORM_CENTER)
    assert ent["edge_index"] == 0
    assert ent["point"] == [10.0, 0.0]
    assert ent["edge_length"] == 20.0
    assert ent["facing_bearing"] == 180.0 and ent["facing"] == "南"
    assert ent["faces_osm_way"] == 1 and ent["faces_road_kind"] == "tertiary"
    assert ent["dist_to_road_center"] == 6.0


def test_entrance_long_edge_bonus():
    # 東の道まで 4 m: 4 - 10 × 0.15 = 2.5 < 南 6 - 20 × 0.15 = 3 → 東
    ent = R.entrance_of(DORM, [SOUTH_ROAD, _east_road(24.0)], DORM_CENTER)
    assert (ent["edge_index"], ent["facing"], ent["facing_bearing"]) == (1, "東", 90.0)
    # 東の道まで 5.5 m: 5.5 - 1.5 = 4 > 3 → 長い南の辺が勝つ
    ent = R.entrance_of(DORM, [SOUTH_ROAD, _east_road(25.5)], DORM_CENTER)
    assert ent["edge_index"] == 0


def test_entrance_ignores_unsuitable_and_far_roads():
    far = _east_road(DORM_CENTER[0] + R.ENTRANCE_ROAD_RANGE + 1.0, osm_id=3)
    trunk = _east_road(21.0, kind="secondary", osm_id=4)
    ent = R.entrance_of(DORM, [SOUTH_ROAD, far, trunk], DORM_CENTER)
    assert ent["faces_osm_way"] == 1


# ---- commit 済みの route.json ----

def _pts(seq):
    return [tuple(p) for p in seq]


@pytest.fixture(scope="module")
def committed(route_json):
    routes = [dict(r, _pts=_pts(r["points"])) for r in route_json["routes"]]
    return route_json, R.Corridor(routes)


def _length_tol(n_points):
    # 出力の座標は 2 桁、長さは 1 桁に丸めてある
    return 0.05 + 0.015 * max(0, n_points - 1)


def test_committed_routes(committed):
    data, _ = committed
    routes = {r["key"]: r for r in data["routes"]}
    assert set(routes) == {"nw_gate", "east_gate"}
    assert routes["nw_gate"]["from"]["osm_node"] == R.NW_GATE_NODE
    assert routes["east_gate"]["from"]["osm_node"] == R.GATE_NODE
    assert routes["nw_gate"]["to"] == routes["east_gate"]["to"]
    for key, r in routes.items():
        pts = r["points"]
        assert pts[0] == [r["from"]["x"], r["from"]["z"]]
        assert pts[-1] == [r["to"]["x"], r["to"]["z"]]
        assert r["length_m"] == pytest.approx(R.polyline_length(pts), abs=_length_tol(len(pts)))
        assert r["length_m"] >= r["straight_m"]
        assert all(a != b for a, b in zip(r["way_ids"], r["way_ids"][1:]))
        st = data["stats"]["routes"][key]
        assert (st["nodes"], st["ways"], st["length_m"]) == (len(pts), len(r["way_ids"]), r["length_m"])
    assert R.DORM_FRONT_ROAD in routes["nw_gate"]["way_ids"]


def test_committed_roads(committed):
    data, cor = committed
    roads = data["roads"]
    keys = [(not r["on_route"], r["dist_to_route"], r["osm_id"], r["part"]) for r in roads]
    assert keys == sorted(keys)
    for r in roads:
        pts = _pts(r["points"])
        assert r["length"] == pytest.approx(R.polyline_length(pts), abs=_length_tol(len(pts)))
        assert r["dist_to_route"] == pytest.approx(cor.dist(pts), abs=0.06)
        assert r["near_route"] == cor.near_keys(pts, R.ROAD_CORRIDOR)
        # 切った折れ線は経路から ROAD_CLIP の内側（切り口の丸め 1 cm まで）
        assert max(cor.point_dist(p) for p in pts) <= R.ROAD_CLIP + 0.01
        assert r["in_campus_json"] == (r["campus_shared_vertices"] == len(pts))
        assert r["width"] > 0
    st = data["stats"]
    assert st["roads"] == len(roads)
    assert st["road_osm_ways"] == len({r["osm_id"] for r in roads})
    assert st["roads_on_route"] == sum(r["on_route"] for r in roads)
    assert st["roads_total_length_m"] == pytest.approx(sum(r["length"] for r in roads), abs=0.05)


def test_committed_buildings_and_areas(committed):
    data, cor = committed
    buildings = data["buildings"]
    assert [(b["dist_to_route"], b["osm_id"]) for b in buildings] == \
        sorted((b["dist_to_route"], b["osm_id"]) for b in buildings)
    for b in buildings:
        fp = _pts(b["footprint"])
        assert oc.ring_area(fp) > 0, b["id"]
        # 抽出時は閉じた輪（最後の辺も含む）で距離を測っている
        assert b["dist_to_route"] == pytest.approx(cor.dist(fp + fp[:1]), abs=0.06), b["id"]
        assert b["dist_to_route"] <= R.BLD_CORRIDOR
    areas = data["areas"]
    assert [(-a["area"], a["osm_id"]) for a in areas] == sorted((-a["area"], a["osm_id"]) for a in areas)
    for a in areas:
        poly = _pts(a["polygon"])
        assert oc.ring_area(poly) > 0
        assert a["dist_to_route"] <= R.AREA_CORRIDOR
        assert a["kind"] in R.AREA_KINDS
    assert data["stats"]["buildings"] == len(buildings)
    assert data["stats"]["areas"] == len(areas)


def test_committed_dormitory_entrance(committed):
    data, _ = committed
    dorm = data["dormitory"]
    fp = _pts(dorm["footprint"])
    assert dorm["osm_id"] == R.DORM_WAY
    assert oc.ring_area(fp) > 0
    assert dorm["centroid"] == pytest.approx(list(R.centroid(fp)), abs=0.011)
    ent = R.entrance_of(fp, data["roads"], tuple(dorm["centroid"]))
    stored = dorm["entrance"]
    for key in ("edge_index", "facing", "faces_osm_way", "faces_road_kind"):
        assert ent[key] == stored[key], key
    assert ent["point"] == pytest.approx(stored["point"], abs=0.011)
    assert ent["facing_bearing"] == pytest.approx(stored["facing_bearing"], abs=0.2)
    assert stored["faces_osm_way"] == R.DORM_FRONT_ROAD
    assert data["stats"]["dorm_entrance"] == stored["point"]


def test_committed_bboxes_come_from_output_coordinates(committed):
    """bbox は丸めたあとの座標から作る約束（消費側が測り直しても外に出ない）。"""
    data, _ = committed
    solid = ([p for r in data["routes"] for p in r["points"]]
             + [p for b in data["buildings"] for p in b["footprint"]]
             + data["dormitory"]["footprint"]
             + [p for r in data["roads"] for p in r["points"]])
    areas = [p for a in data["areas"] for p in a["polygon"]]

    def bbox(pts):
        xs = [p[0] for p in pts]
        zs = [p[1] for p in pts]
        return [round(min(xs), 1), round(min(zs), 1), round(max(xs), 1), round(max(zs), 1)]

    assert data["stats"]["content_bbox_no_areas"] == bbox(solid)
    assert data["stats"]["content_bbox"] == bbox(solid + areas)
    assert data["stats"]["area_bbox"] == bbox(areas)

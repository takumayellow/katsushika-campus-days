"""tools/osm_extract.py: OSM の elements → campus.json の変換 (#68)。

生データ (data/osm/raw_overpass.json) は gitignore なので、合成した elements を _load に差し込む。
後半は commit 済みの campus.json が extract() の約束（向き・並び・原点）を満たしているかを見る。
"""

import pytest

import osm_common as oc
import osm_extract

LAT0, LON0 = 35.7719, 139.8630
DLAT = 0.001    # 約 111 m
DLON = 0.0012   # 約 109 m

# osm_extract.extract() の並び順（キャンパス内 → 寮 → 背景）と同じ。
STYLE_ORDER = ["lab_tower", "lecture", "office", "kyoso", "library", "gym", "lab_low",
               "greenhouse", "dormitory", "misc", "background"]

BG_LEVELS_ID = 9001
BG_HEIGHT_ID = 9002
BG_PLAIN_ID = 9003
DORM_ID = 9004
MISC_ID = 9005
CATALOG_ID = 9006
FOOTWAY_ID = 9101
RESIDENTIAL_ID = 9102
MOTORWAY_ID = 9103
FAR_ROAD_ID = 9104
PARK_ID = 9201
NO_GEOM_ID = 9301


def _box(lat, lon, dlat, dlon, closed=True, cw=False):
    ring = [(lat - dlat, lon - dlon), (lat - dlat, lon + dlon),
            (lat + dlat, lon + dlon), (lat + dlat, lon - dlon)]
    if cw:
        ring.reverse()
    if closed:
        ring.append(ring[0])
    return [{"lat": la, "lon": lo} for la, lo in ring]


def _way(wid, geometry, **tags):
    return {"type": "way", "id": wid, "tags": tags, "geometry": geometry}


def _node(nid, lat, lon, **tags):
    return {"type": "node", "id": nid, "lat": lat, "lon": lon, "tags": tags}


def _elements():
    south = LAT0 - DLAT - 0.0004       # 敷地の南 44 m（ROI の内側）
    return [
        # 敷地（時計回りで閉じた輪）。amenity=university なので areas にも入る
        _way(oc.CAMPUS_WAY_ID, _box(LAT0, LON0, DLAT, DLON, cw=True),
             amenity="university", name="東京理科大学 葛飾キャンパス"),
        # 敷地内: 名前で引く建物 / id で引く名無し建物 / どちらでもない建物
        _way(CATALOG_ID, _box(LAT0 + 0.0003, LON0, 0.0001, 0.0002, cw=True), building="university", name="研究棟"),
        _way(1552199070, _box(LAT0 - 0.0003, LON0, 0.0001, 0.0002), building="yes"),
        _way(MISC_ID, _box(LAT0, LON0 + 0.0006, 0.00005, 0.00005), building="yes"),
        # 敷地外: 背景の建物（階数あり / 高さあり / どちらも無し）と寮
        _way(BG_LEVELS_ID, _box(south, LON0, 0.00005, 0.00005), building="house", **{"building:levels": "3"}),
        _way(BG_HEIGHT_ID, _box(south, LON0 + 0.0003, 0.00005, 0.00005), building="yes", height="12"),
        _way(BG_PLAIN_ID, _box(south, LON0 - 0.0003, 0.00005, 0.00005), building="yes"),
        _way(DORM_ID, _box(south, LON0 + 0.0006, 0.00005, 0.00005), building="dormitory", height="20"),
        # 道: width タグは表より優先 / 表に無い種類は捨てる / ROI の外は捨てる
        _way(FOOTWAY_ID, _box(LAT0, LON0, 0.0002, 0.0002), highway="footway", width="3"),
        _way(RESIDENTIAL_ID, [{"lat": south, "lon": LON0 - 0.001}, {"lat": south, "lon": LON0 + 0.001}],
             highway="residential", name="水元通り"),
        _way(MOTORWAY_ID, [{"lat": south, "lon": LON0}, {"lat": south, "lon": LON0 + 0.001}], highway="motorway"),
        _way(FAR_ROAD_ID, [{"lat": LAT0 + 0.02, "lon": LON0}, {"lat": LAT0 + 0.02, "lon": LON0 + 0.001}],
             highway="residential"),
        # 面
        _way(PARK_ID, _box(south, LON0 - 0.0008, 0.0001, 0.0001, cw=True), leisure="park", name="公園"),
        # geometry の無い way / relation は読まない
        {"type": "way", "id": NO_GEOM_ID, "tags": {"building": "yes"}, "nodes": [1, 2, 3]},
        {"type": "relation", "id": 1, "tags": {"building": "yes"}},
        # 点
        _node(1, LAT0, LON0 + 0.0001, natural="tree"),
        _node(2, LAT0, LON0 + 0.0002, amenity="bench"),
        _node(3, LAT0, LON0 + 0.0003, highway="street_lamp"),
        _node(4, LAT0, LON0 + 0.0004, man_made="ceremonial_gate"),
        _node(5, LAT0, LON0 - 0.0001, highway="bus_stop", name="理科大前"),
        _node(6, LAT0, LON0 - 0.0002, shop="convenience"),         # 種類に無い
        _node(7, LAT0 + 0.02, LON0, natural="tree"),                # ROI の外 (2 km 北)
        {"type": "node", "id": 8, "tags": {"natural": "tree"}},     # 座標の無い node
    ]


@pytest.fixture()
def extracted(monkeypatch):
    elements = _elements()
    monkeypatch.setattr(osm_extract, "_load", lambda: elements)
    return osm_extract.extract()


def _by_osm_id(data):
    return {b["osm_id"]: b for b in data["buildings"]}


def test_origin_is_campus_centroid(extracted):
    origin = extracted["meta"]["origin"]
    assert origin["lat"] == pytest.approx(LAT0, abs=1e-12)
    assert origin["lon"] == pytest.approx(LON0, abs=1e-12)
    assert extracted["meta"]["campus_osm_way"] == oc.CAMPUS_WAY_ID


def test_campus_boundary_is_ccw_open_ring_around_origin(extracted):
    boundary = extracted["campus_boundary"]
    assert len(boundary) == 4
    assert oc.ring_area(boundary) > 0
    assert boundary[0] != boundary[-1]
    assert oc.centroid(boundary) == pytest.approx((0.0, 0.0), abs=1e-3)
    xs = sorted({round(p[0], 3) for p in boundary})
    zs = sorted({round(p[1], 3) for p in boundary})
    assert xs[1] == pytest.approx(DLON * oc.EXPECTED_M_PER_DEG_LON, abs=0.05)
    assert zs[1] == pytest.approx(DLAT * oc.EXPECTED_M_PER_DEG_LAT, abs=0.05)


def test_catalog_and_unnamed_buildings(extracted):
    b = _by_osm_id(extracted)
    r1 = b[CATALOG_ID]
    assert r1["id"] == "research1" and r1["style"] == "lab_tower"
    assert r1["osm_name"] == "研究棟" and r1["on_campus"]
    assert r1["height"] == osm_extract.BUILDING_CATALOG["研究棟"]["height"]
    kyoso = b[1552199070]
    assert kyoso["id"] == "kyoso" and kyoso["on_campus"] and kyoso["osm_name"] is None
    misc = b[MISC_ID]
    assert misc["id"] == f"misc_{MISC_ID}" and misc["style"] == "misc" and misc["on_campus"]
    assert misc["height"] == 10.0 and misc["levels"] is None


def test_background_building_heights(extracted):
    b = _by_osm_id(extracted)
    assert b[BG_LEVELS_ID]["levels"] == 3
    assert b[BG_LEVELS_ID]["height"] == pytest.approx(3 * 3.2)
    assert b[BG_HEIGHT_ID]["height"] == 12.0 and b[BG_HEIGHT_ID]["levels"] is None
    assert b[BG_PLAIN_ID]["height"] == 8.0
    for wid in (BG_LEVELS_ID, BG_HEIGHT_ID, BG_PLAIN_ID):
        assert b[wid]["id"] == f"bg_{wid}"
        assert b[wid]["style"] == "background"
        assert not b[wid]["on_campus"]
    dorm = b[DORM_ID]
    assert dorm["id"] == f"dorm_{DORM_ID}" and dorm["style"] == "dormitory"
    assert dorm["height"] == 20.0 and not dorm["on_campus"]
    assert NO_GEOM_ID not in b


def test_building_order_and_footprints(extracted):
    buildings = extracted["buildings"]
    keys = [(STYLE_ORDER.index(b["style"]), b["id"]) for b in buildings]
    assert keys == sorted(keys)
    assert [b["style"] for b in buildings][:2] == ["lab_tower", "kyoso"]
    for b in buildings:
        fp = b["footprint"]
        assert len(fp) == 4, b["id"]
        assert oc.ring_area(fp) > 0, b["id"]


def test_paths(extracted):
    paths = {p["name"] or p["kind"]: p for p in extracted["paths"]}
    assert set(paths) == {"footway", "水元通り"}
    assert paths["footway"]["width"] == 3.0          # width タグが表より優先
    assert paths["footway"]["on_campus"]
    assert paths["水元通り"]["width"] == 6.0
    assert not paths["水元通り"]["on_campus"]
    assert len(paths["footway"]["points"]) == 5      # 道は輪を閉じたまま（ccw をかけない）


def test_areas(extracted):
    kinds = sorted(a["kind"] for a in extracted["areas"])
    assert kinds == ["park", "university"]
    for a in extracted["areas"]:
        assert oc.ring_area(a["polygon"]) > 0
        assert a["polygon"][0] != a["polygon"][-1]


def test_points(extracted):
    kinds = sorted(p["kind"] for p in extracted["points"])
    assert kinds == ["bench", "bus_stop", "lamp", "shrine", "tree"]
    bus = next(p for p in extracted["points"] if p["kind"] == "bus_stop")
    assert bus["name"] == "理科大前"
    assert bus["z"] == 0.0 and bus["x"] < 0


def test_extract_is_deterministic(monkeypatch):
    elements = _elements()
    monkeypatch.setattr(osm_extract, "_load", lambda: elements)
    first = osm_extract.extract()
    monkeypatch.setattr(osm_extract, "_load", lambda: list(reversed(elements)))
    second = osm_extract.extract()
    assert first["buildings"] == second["buildings"]


# ---- commit 済みの campus.json ----

def test_committed_campus_json_invariants(campus_json):
    boundary = campus_json["campus_boundary"]
    assert oc.ring_area(boundary) > 0 and boundary[0] != boundary[-1]
    # 原点 = 敷地の頂点の重心。等距円筒は線形なので、メートル座標でも重心は原点（丸め 1 mm）
    assert oc.centroid(boundary) == pytest.approx((0.0, 0.0), abs=1e-3)

    buildings = campus_json["buildings"]
    assert len({b["id"] for b in buildings}) == len(buildings)
    keys = [(STYLE_ORDER.index(b["style"]), b["id"]) for b in buildings]
    assert keys == sorted(keys)
    for b in buildings:
        fp = b["footprint"]
        assert len(fp) >= 3 and fp[0] != fp[-1], b["id"]
        assert oc.ring_area(fp) > 0, b["id"]
    for a in campus_json["areas"]:
        assert oc.ring_area(a["polygon"]) > 0

    named = {b["id"] for b in buildings}
    catalog = {m["id"] for m in osm_extract.BUILDING_CATALOG.values()}
    unnamed = {m["id"] for m in osm_extract.UNNAMED_BUILDINGS.values()}
    assert catalog | unnamed <= named
    for b in buildings:
        if b["id"] in catalog | unnamed:
            assert b["on_campus"], b["id"]

"""tools/osm_common.py: 原点・投影・幾何のヘルパ (#68)。

campus.json と route.json は同じ原点と投影で作られている。ここが動くと両方の座標がずれる。
"""

import json
import math

import pytest

import osm_common as oc

LAT0 = oc.EXPECTED_ORIGIN_LAT
LON0 = oc.EXPECTED_ORIGIN_LON

# WGS84 楕円体
WGS84_A = 6378137.0
WGS84_F = 1 / 298.257223563


def _ellipsoid_m_per_deg(lat):
    """緯度 lat での 1 度あたりの南北・東西の長さ (子午線曲率半径と卯酉線曲率半径から)。"""
    e2 = WGS84_F * (2 - WGS84_F)
    phi = math.radians(lat)
    s = 1 - e2 * math.sin(phi) ** 2
    m = WGS84_A * (1 - e2) / s ** 1.5
    n = WGS84_A / math.sqrt(s)
    return m * math.pi / 180, n * math.cos(phi) * math.pi / 180


@pytest.fixture(scope="module")
def project():
    return oc.make_projector(LAT0, LON0)


# ---- 投影 ----

def test_scales_are_the_committed_values(project):
    m_lat, m_lon = oc.scales_of(project, LAT0, LON0)
    assert m_lat == pytest.approx(oc.EXPECTED_M_PER_DEG_LAT, abs=1e-3)
    assert m_lon == pytest.approx(oc.EXPECTED_M_PER_DEG_LON, abs=1e-3)


def test_scales_agree_with_wgs84_ellipsoid(project):
    """級数近似の係数が楕円体の式と 2e-6 以内（キャンパス 1 km で 2 mm 以内）で合う。"""
    m_lat, m_lon = oc.scales_of(project, LAT0, LON0)
    e_lat, e_lon = _ellipsoid_m_per_deg(LAT0)
    assert m_lat == pytest.approx(e_lat, rel=2e-6)
    assert m_lon == pytest.approx(e_lon, rel=2e-6)


def test_projection_axes_and_rounding(project):
    assert project(LON0, LAT0) == (0.0, 0.0)
    x, z = project(LON0 + 0.001, LAT0)
    assert x > 0 and z == 0.0            # 東 = +x
    x, z = project(LON0, LAT0 + 0.001)
    assert x == 0.0 and z > 0            # 北 = +z
    x, z = project(LON0 + 0.0012345678, LAT0 - 0.0023456789)
    assert x == round(x, 3) and z == round(z, 3)
    assert x == pytest.approx(0.0012345678 * oc.EXPECTED_M_PER_DEG_LON, abs=1e-3)
    assert z == pytest.approx(-0.0023456789 * oc.EXPECTED_M_PER_DEG_LAT, abs=1e-3)


# ---- 自己検査 ----

def test_selfcheck_roundtrip_passes(tmp_path, capsys):
    """生データ (gitignore) が無い CI でも、campus.json の往復で投影を確かめられる。"""
    assert oc.selfcheck(raw=tmp_path / "missing_raw.json") == 0
    out = capsys.readouterr().out
    assert "SELFCHECK OK" in out
    assert "生データ無し" in out


def test_selfcheck_fails_on_shifted_origin(tmp_path, campus_json, capsys):
    shifted = json.loads(json.dumps(campus_json))
    shifted["meta"]["origin"]["lat"] += 1e-6
    path = tmp_path / "campus.json"
    path.write_text(json.dumps(shifted, ensure_ascii=False), encoding="utf-8")
    assert oc.selfcheck(raw=tmp_path / "missing_raw.json", campus_json=path) == 1
    assert "SELFCHECK FAILED" in capsys.readouterr().out


def test_campus_json_origin_is_the_expected_origin(campus_json):
    origin = campus_json["meta"]["origin"]
    assert origin["lat"] == pytest.approx(LAT0, abs=1e-12)
    assert origin["lon"] == pytest.approx(LON0, abs=1e-12)
    assert campus_json["meta"]["campus_osm_way"] == oc.CAMPUS_WAY_ID


# ---- 幾何 ----

SQUARE = [(0.0, 0.0), (4.0, 0.0), (4.0, 4.0), (0.0, 4.0)]
L_SHAPE = [(0.0, 0.0), (6.0, 0.0), (6.0, 2.0), (2.0, 2.0), (2.0, 6.0), (0.0, 6.0)]


def test_ring_area_sign():
    assert oc.ring_area(SQUARE) == pytest.approx(16.0)
    assert oc.ring_area(list(reversed(SQUARE))) == pytest.approx(-16.0)
    assert oc.ring_area(L_SHAPE) == pytest.approx(20.0)
    # 閉じた輪（最後 = 最初）でも面積は同じ
    assert oc.ring_area(L_SHAPE + [L_SHAPE[0]]) == pytest.approx(20.0)


def test_ccw_drops_closing_point_and_orients():
    closed_cw = list(reversed(SQUARE)) + [SQUARE[-1]]
    out = oc.ccw(closed_cw)
    assert len(out) == 4
    assert oc.ring_area(out) > 0
    assert out[0] != out[-1]
    assert oc.ccw(SQUARE) == SQUARE
    assert oc.ccw(SQUARE + [SQUARE[0]]) == SQUARE
    assert oc.ccw([]) == []


def test_point_in_poly_concave():
    assert oc.point_in_poly(1, 1, L_SHAPE)
    assert oc.point_in_poly(5, 1, L_SHAPE)
    assert oc.point_in_poly(1, 5, L_SHAPE)
    assert not oc.point_in_poly(4, 4, L_SHAPE)
    assert not oc.point_in_poly(-1, 1, L_SHAPE)
    assert oc.point_in_poly(1, 1, list(reversed(L_SHAPE)))


def test_centroid_and_geom():
    assert oc.centroid(SQUARE) == (2.0, 2.0)
    e = {"geometry": [{"lat": 1.0, "lon": 2.0}, {"lat": 3.0, "lon": 4.0}]}
    assert oc.geom(e) == [(2.0, 1.0), (4.0, 3.0)]   # (lon, lat) の順
    assert oc.geom({}) == []


def test_origin_from_ignores_closing_point():
    """閉じた敷地ポリゴンの最後の点（= 最初の点）は重心に入れない。"""
    ring = [(35.0, 139.0), (35.0, 139.004), (35.002, 139.004), (35.002, 139.0), (35.0, 139.0)]
    elements = [
        {"type": "node", "id": oc.CAMPUS_WAY_ID, "lat": 0.0, "lon": 0.0},   # 同じ id の node は無視される
        {"type": "way", "id": oc.CAMPUS_WAY_ID,
         "geometry": [{"lat": la, "lon": lo} for la, lo in ring]},
    ]
    lat0, lon0 = oc.origin_from(elements)
    assert lat0 == pytest.approx(35.001)
    assert lon0 == pytest.approx(139.002)


def test_load_elements(tmp_path):
    path = tmp_path / "raw.json"
    path.write_text(json.dumps({"version": 0.6, "elements": [{"type": "node", "id": 1}]}), encoding="utf-8")
    assert oc.load_elements(path) == [{"type": "node", "id": 1}]

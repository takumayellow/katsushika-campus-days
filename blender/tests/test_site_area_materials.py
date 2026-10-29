"""運動場と遊び場の地面のマテリアルと、テニスのハードコート（#59）。"""

import math

import pytest

from kcd_lib import geom, site
from kcd_lib import mesh as M
from kcd_lib.mesh import MeshBuilder

# campus.json の運動場（OSM の way id）。どれがどれかは tools/osm_extract.py の PITCH_SURFACE
SOCCER_ID = 771657620
HARDCOURT_ID = 771657619
SAND_TURF_ID = 771657617


def _sq(x0, size):
    return [(x0, 0.0), (x0 + size, 0.0), (x0 + size, size), (x0, size)]


def _faces(mb, mat):
    """マテリアル mat の面（頂点のリスト）。"""
    return [[mb.verts[k] for k in f] for f, mi in zip(mb.faces, mb.face_mat) if mb.mat_names[mi] == mat]


def _court_st(c, u, pts):
    """XY の点をコートの座標 (s = 長手, t = 短手, 原点はコートの中心) に直す。"""
    f = geom.Frame(u)
    return [f.uv(geom.sub(p[:2], c)) for p in pts]


# ---- 区画ごとのマテリアル ----

@pytest.mark.parametrize("sport, surface, want", [
    ("soccer", "artificial_turf", "grass"),
    ("tennis", "acrylic", "hardcourt_outer"),
    ("tennis", "artificial_turf", "soil"),
    ("tennis", None, "soil"),
    (None, None, "soil"),
])
def test_pitch_material_follows_sport_and_surface(sport, surface, want):
    area = {"kind": "pitch", "sport": sport, "surface": surface, "polygon": _sq(0, 10)}
    assert site.area_materials([area]) == [want]


def test_pitch_without_tags_is_soil_and_other_kinds_are_unchanged():
    """sport・surface の無い区画は、いちばん大きくても土のまま（面積では決めない）。"""
    areas = [
        {"kind": "pitch", "polygon": _sq(0, 80)},
        {"kind": "pitch", "polygon": _sq(100, 10)},
        {"kind": "playground", "polygon": _sq(200, 20)},
        {"kind": "parking", "polygon": _sq(300, 20)},
        {"kind": "university", "polygon": _sq(400, 20)},
        {"kind": "park", "polygon": _sq(500, 5)},
    ]
    assert site.area_materials(areas) == ["soil", "soil", "sand", "asphalt", None, "grass"]


def test_campus_json_pitches_by_osm_id(campus_data):
    """campus.json の 3 区画を OSM の id で見分ける: サッカー場は芝、ハードコートは外周の緑、
    砂入り人工芝の 2 面は土。遊び場は砂。"""
    areas = campus_data["areas"]
    mats = site.area_materials(areas)
    pitch = {a["osm_id"]: m for a, m in zip(areas, mats) if a["kind"] == "pitch"}
    assert pitch == {SOCCER_ID: "grass", HARDCOURT_ID: "hardcourt_outer", SAND_TURF_ID: "soil"}
    assert all(m == "sand" for a, m in zip(areas, mats) if a["kind"] == "playground")


# ---- ハードコートの白線 ----

def test_court_lines_stay_inside_the_doubles_court_without_overlapping():
    hl, hw = site.COURT_LEN / 2, site.COURT_WIDTH / 2
    lines = site.court_lines()
    # ベースライン 2・サイドライン 4・サービスライン 2・センターサービスライン 1・センターマーク 2
    assert len(lines) == 11
    eps = 1e-9
    for s0, t0, s1, t1 in lines:
        assert s0 < s1 and t0 < t1
        assert -hl - eps <= s0 and s1 <= hl + eps and -hw - eps <= t0 and t1 <= hw + eps
    # 同じ高さに重なった面はちらつくので、線どうしは重ねない
    for i, a in enumerate(lines):
        for b in lines[i + 1:]:
            assert not site._overlaps(a, b), (a, b)


def test_court_lines_are_symmetric_and_on_the_itf_marks():
    """線は前後・左右に対称で、線の外側がダブルス・シングルス・サービスラインの寸法に来る。"""
    lines = {tuple(round(x, 6) + 0.0 for x in r) for r in site.court_lines()}
    flip_s = {tuple(round(x, 6) + 0.0 for x in (-s1, t0, -s0, t1)) for s0, t0, s1, t1 in lines}
    flip_t = {tuple(round(x, 6) + 0.0 for x in (s0, -t1, s1, -t0)) for s0, t0, s1, t1 in lines}
    assert flip_s == lines and flip_t == lines
    outer_t = {round(t1, 6) for _s0, _t0, _s1, t1 in lines}
    outer_s = {round(s1, 6) for _s0, _t0, s1, _t1 in lines}
    assert round(site.COURT_WIDTH / 2, 6) in outer_t       # ダブルスのサイドライン
    assert round(site.COURT_SINGLES / 2, 6) in outer_t     # シングルスのサイドライン
    assert round(site.COURT_LEN / 2, 6) in outer_s         # ベースライン
    assert round(site.COURT_SERVICE, 6) in outer_s         # サービスライン（ネットから 6.40 m）


def test_court_layers_sit_on_the_pitch():
    assert site.Z_AREA < site.Z_COURT < site.Z_COURT_LINE < site.Z_FOOT
    assert site.Z_COURT - site.Z_AREA >= 0.003 - 1e-12
    assert site.Z_COURT_LINE - site.Z_COURT >= 0.003 - 1e-12


def _rotated_rect(c, u, length, width):
    f = geom.Frame(u)
    return [geom.add(c, p) for p in f.rect(-length / 2, -width / 2, length / 2, width / 2)]


def test_court_axes_follow_the_long_sides():
    u = geom.normalize((0.6, 0.8))
    poly = _rotated_rect((40.0, -15.0), u, 36.6, 18.3)
    c, d = site.court_axes(poly)
    assert c == pytest.approx((40.0, -15.0))
    assert abs(geom.dot(d, u)) == pytest.approx(1.0)
    # 長辺が平行でない四角形でも、2 辺の向きの間に来る
    skew = [(0.0, 0.0), (18.0, 0.0), (19.0, 36.0), (0.0, 36.0)]
    _c, d = site.court_axes(skew)
    ang = math.degrees(math.atan2(abs(d[0]), abs(d[1])))
    assert 0.0 < ang < math.degrees(math.atan2(1.0, 36.0))


def test_build_tennis_court_lays_the_court_in_the_middle_of_the_pitch():
    u = geom.normalize((0.3346, 0.9424))
    c0 = (173.0, 70.2)
    poly = _rotated_rect(c0, u, 36.6, 18.3)
    mb = MeshBuilder("t")
    site.build_tennis_court(mb, poly)
    inner = _faces(mb, "hardcourt_inner")
    lines = _faces(mb, "line_white")
    assert len(inner) == 1 and len(lines) == 11
    assert all(p[2] == site.Z_COURT for p in inner[0])
    assert all(p[2] == site.Z_COURT_LINE for f in lines for p in f)
    st = _court_st(c0, u, inner[0])
    hl, hw = site.COURT_LEN / 2, site.COURT_WIDTH / 2
    assert sorted({round(abs(s), 6) for s, _t in st}) == [round(hl, 6)]
    assert sorted({round(abs(t), 6) for _s, t in st}) == [round(hw, 6)]
    # 上向きの面（法線 z > 0）
    for f in inner + lines:
        assert M._newell(f)[2] > 0


def test_campus_json_hardcourt_fits_inside_its_pitch(campus_data, campus_frame):
    """campus.json のハードコートは区画の重心に置かれ、四隅が区画の縁から 2 m 以上内側にある。"""
    area = next(a for a in campus_data["areas"] if a.get("osm_id") == HARDCOURT_ID)
    poly = geom.ensure_ccw(geom.dedup(area["polygon"]))
    mb = MeshBuilder("site_ground")
    site.build_ground(mb, campus_data, campus_frame)
    inner = _faces(mb, "hardcourt_inner")
    assert len(inner) == 1
    assert M._area3(inner[0]) == pytest.approx(site.COURT_LEN * site.COURT_WIDTH)
    corners = [p[:2] for p in inner[0]]
    assert geom.centroid(corners) == pytest.approx(geom.centroid(poly))
    for p in corners:
        assert geom.point_in_poly(p, poly)
        assert geom.dist_point_poly_edges(p, poly) > 2.0
    outer = sum(M._area3(f) for f in _faces(mb, "hardcourt_outer"))
    assert outer == pytest.approx(abs(geom.poly_area(poly)))
    assert len([f for f in _faces(mb, "line_white") if f[0][2] == site.Z_COURT_LINE]) == 11


def test_no_path_runs_over_the_hardcourt(campus_data):
    """Z_COURT と Z_COURT_LINE は車道（Z_ROAD）と白線（Z_LINE）と同じ高さなので、
    運動場の上を道が通ると面が重なってちらつく。ハードコートの区画には道が掛からない。"""
    area = next(a for a in campus_data["areas"] if a.get("osm_id") == HARDCOURT_ID)
    poly = geom.ensure_ccw(geom.dedup(area["polygon"]))
    for p in campus_data["paths"]:
        half = (p.get("width") or 2.5) * 0.5
        for q in geom.resample(p["points"], 0.5):
            assert not geom.point_in_poly(q, poly), p["kind"]
            assert geom.dist_point_poly_edges(q, poly) > half, p["kind"]

"""kcd_lib.site: 堀の水際の辺（basin_edges）と、敷地の地面（site_ground）の組み立て (#68)。"""

import math

import basin_fixture
import pytest

from kcd_lib import geom, site
from kcd_lib import mesh as M
from kcd_lib.mesh import MeshBuilder
from kcd_route import ground

# build_campus.py の値（build_campus は import 時に argparse などを引かないので、そのまま読む）
import build_campus  # noqa: E402  (conftest が sys.path を通す)


# ---- 堀の水際: fixture（Unity の EditMode テストと共有）----

def test_fixture_is_up_to_date():
    """blender/tests/fixtures/basin_edges.json が今の site.py の答えと同じか。

    落ちたら python blender/tests/basin_fixture.py で作り直してコミットし、Unity の EditMode テスト
    BasinEdgeAgreementTests で CampusStage.BasinEdges も同じ辺を出すことを確かめる。"""
    import json
    with open(basin_fixture.FIXTURE, encoding="utf-8") as fp:
        on_disk = json.load(fp)
    fresh = json.loads(json.dumps(basin_fixture.build()))
    assert on_disk == fresh, "fixtures/basin_edges.json が古い: python blender/tests/basin_fixture.py で作り直す"


def test_fixture_text_is_canonical():
    """手で書き換えず、basin_fixture.py の出力そのものであること（差分が読める形を保つ）。"""
    with open(basin_fixture.FIXTURE, encoding="utf-8", newline="") as fp:
        text = fp.read()
    assert text == basin_fixture.dumps(basin_fixture.build())


def test_fixture_basins_case_is_site_basins():
    case = basin_fixture.build()["cases"][0]
    assert case["name"] == "basins"
    assert [tuple(r) for r in case["rects"]] == [tuple(r) for r in site.BASINS]


def _edges(rects):
    return site.basin_edges(rects)


def test_single_rect_has_four_extended_edges():
    edges = _edges([(0.0, 0.0, 10.0, 4.0)])
    assert edges == [
        ("v", 0.0, 0.0, 10.0, -1, True, True),
        ("v", 4.0, 0.0, 10.0, +1, True, True),
        ("u", 0.0, 0.0, 4.0, -1, True, True),
        ("u", 10.0, 0.0, 4.0, +1, True, True),
    ]


def test_default_argument_is_basins():
    assert site.basin_edges() == site.basin_edges(site.BASINS)


def _covered(t0, t1, spans, eps=1e-9):
    """[t0, t1] が spans の和で覆われているか。"""
    t = t0
    for s0, s1 in sorted(spans):
        if s0 > t + eps:
            return False
        t = max(t, s1)
    return t >= t1 - eps


@pytest.mark.parametrize("case", basin_fixture.build()["cases"], ids=lambda c: c["name"])
def test_every_side_is_edge_or_shared(case):
    """矩形のどの辺も、水際の辺か、外側に接する別の水面のどちらかで端から端まで覆われる。
    水際の辺どうしは重ならず、水際の辺の外側に水は無い。"""
    rects = [tuple(r) for r in case["rects"]]
    edges = _edges(rects)
    eps = site.BASIN_EDGE_EPS
    for i, (u0, v0, u1, v1) in enumerate(rects):
        for axis, c, t0, t1, out in (("v", v0, u0, u1, -1), ("v", v1, u0, u1, +1),
                                     ("u", u0, v0, v1, -1), ("u", u1, v0, v1, +1)):
            mine = [(e[2], e[3]) for e in edges
                    if e[0] == axis and e[1] == c and e[4] == out and t0 - 1e-9 <= e[2] and e[3] <= t1 + 1e-9]
            shared = []
            for j, o in enumerate(rects):
                if j == i:
                    continue
                face = (o[1] if out > 0 else o[3]) if axis == "v" else (o[0] if out > 0 else o[2])
                if abs(face - c) <= eps:
                    a, b = (o[0], o[2]) if axis == "v" else (o[1], o[3])
                    if max(a, t0) < min(b, t1):
                        shared.append((max(a, t0), min(b, t1)))
            # 捨てた切れ端（eps 以下）のぶんだけ緩める
            assert _covered(t0 + eps, t1 - eps, mine + shared), (case["name"], i, axis, c)
            for s0, s1 in mine:
                for a, b in shared:
                    assert min(s1, b) - max(s0, a) <= 1e-9, ("水際の辺が隣の水面と重なる", case["name"], i)
    for axis, c, t0, t1, out, _, _ in edges:
        assert t1 - t0 > eps
        mid = (t0 + t1) * 0.5
        d = 2 * eps  # near_miss の隙間（1 mm）より内側
        probe = (c + out * d, mid) if axis == "u" else (mid, c + out * d)
        assert not any(r[0] < probe[0] < r[2] and r[1] < probe[1] < r[3] for r in rects), \
            ("水際の辺の外側に水がある", case["name"], axis, c, t0, t1)


def test_inner_corners_are_not_extended():
    """L 字の入隅（隣の水面に掛かる端）は延ばさず、出隅は延ばす。"""
    edges = _edges([(0.0, 0.0, 10.0, 4.0), (0.0, 4.0, 4.0, 12.0)])
    by_key = {(e[0], e[1], e[2], e[3], e[4]): (e[5], e[6]) for e in edges}
    # 下の矩形の上辺は u 4..10 だけが残り、u = 4 の端（入隅）は延ばさない
    assert by_key[("v", 4.0, 4.0, 10.0, +1)] == (False, True)
    # 上の矩形の右辺は v 4..12。v = 4 の端（入隅）は延ばさない
    assert by_key[("u", 4.0, 4.0, 12.0, +1)] == (False, True)
    # 出隅（外周の角）は延ばす
    assert by_key[("v", 0.0, 0.0, 10.0, -1)] == (True, True)
    assert by_key[("v", 12.0, 0.0, 4.0, +1)] == (True, True)
    assert by_key[("u", 10.0, 0.0, 4.0, +1)] == (True, True)


def test_real_basins_rims_do_not_cover_water():
    """今の堀の縁石（延ばした端を含む）は、どの水面にも面積を持って掛からない。"""
    rim = site.BASIN_RIM
    for axis, c, t0, t1, out, e0, e1 in site.basin_edges():
        s0 = t0 - (rim if e0 else 0.0)
        s1 = t1 + (rim if e1 else 0.0)
        ca, cb = sorted((c, c + out * rim))
        sq = (s0, ca, s1, cb) if axis == "v" else (ca, s0, cb, s1)
        for r in site.BASINS:
            assert not site._overlaps(sq, r), (axis, c, t0, t1, r)


def test_tolerance_matches_unity():
    """CampusStage.UncoveredSpans は 1e-4f で比べる。fixture の near_touch / sliver がこの値に依る。"""
    assert site.BASIN_EDGE_EPS == 1e-4
    assert basin_fixture.build()["eps"] == site.BASIN_EDGE_EPS


# ---- 占有マップ ----

def test_occupancy_stamps():
    occ = site.Occupancy()
    assert not occ.blocked(0.0, 0.0)
    assert occ.blocked(1000.0, 0.0)  # 範囲外は塞がっている扱い
    occ.stamp_disc(10.0, 10.0, 3.0)
    assert occ.blocked(10.0, 10.0) and occ.blocked(12.0, 10.0)
    assert not occ.blocked(20.0, 10.0)
    occ.stamp_poly([(50, 50), (70, 50), (70, 70), (50, 70)])
    assert occ.blocked(60.0, 60.0) and not occ.blocked(45.0, 60.0)
    occ.stamp_poly([(100, 100), (110, 100), (110, 110), (100, 110)], margin=4.0)
    assert occ.blocked(97.0, 105.0) and not occ.blocked(92.0, 105.0)
    occ.stamp_polyline([(-100.0, -50.0), (-60.0, -50.0)], 2.0)
    assert occ.blocked(-80.0, -50.0) and not occ.blocked(-80.0, -60.0)


def test_clip_polyline():
    pts = [(0, 0), (10, 0), (400, 0), (20, 5), (30, 5), (40, 5)]
    assert site._clip_polyline(pts, 348.0) == [[(0, 0), (10, 0)], [(20, 5), (30, 5), (40, 5)]]


# ---- 敷地の地面（build_campus の site_ground と同じ手順を素の Python で）----

@pytest.fixture(scope="module")
def site_ground(campus_data, campus_frame):
    mb = MeshBuilder("site_ground")
    occ = site.Occupancy()
    site.build_ground(mb, campus_data, campus_frame, occ)
    site.build_paths(mb, campus_data, occ)
    site.build_mall(mb, campus_frame, occ)
    site.build_basin(mb, campus_frame, occ)
    before = _area_by_mat(mb)
    n_faces_before = len(mb.faces)
    n_split, n_faces = mb.split_by_grid(build_campus.SITE_GRID)
    return {"mb": mb, "occ": occ, "before": before, "n_faces_before": n_faces_before,
            "n_split": n_split, "n_faces": n_faces}


def _area_by_mat(mb):
    out = {}
    for face, mi in zip(mb.faces, mb.face_mat):
        name = mb.mat_names[mi]
        out[name] = out.get(name, 0.0) + M._area3([mb.verts[k] for k in face])
    return out


def _nz(poly):
    n = M._newell(poly)
    ln = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2)
    return n[2] / ln if ln > 0 else 0.0


def test_site_ground_has_no_downward_floor(site_ground):
    """build_campus.check_site_ground の 1 つ目: 水平な面はすべて上向き。"""
    mb = site_ground["mb"]
    down = [f for f in mb.faces if _nz([mb.verts[k] for k in f]) < -0.99]
    assert down == []
    up = sum(1 for f in mb.faces if _nz([mb.verts[k] for k in f]) > 0.99)
    assert up > 1000


def test_site_ground_edges_fit_physx_limit(site_ground):
    """check_site_ground の 2 つ目: 三角形に割ったときの辺も含めて SITE_MAX_EDGE 以下。

    どう割られても三角形の辺は面の頂点どうしを結ぶので、面の頂点どうしの最大距離で測る。"""
    mb = site_ground["mb"]
    longest = 0.0
    for f in mb.faces:
        pts = [mb.verts[k] for k in f]
        for i in range(len(pts)):
            for j in range(i + 1, len(pts)):
                longest = max(longest, math.dist(pts[i], pts[j]))
    assert longest <= build_campus.SITE_GRID * math.sqrt(2) + 1e-6
    assert longest <= build_campus.SITE_MAX_EDGE


def test_site_ground_split_keeps_area_per_material(site_ground):
    after = _area_by_mat(site_ground["mb"])
    before = site_ground["before"]
    assert after.keys() == before.keys()
    for k in before:
        assert after[k] == pytest.approx(before[k], rel=1e-9), k
    # 外周の地面は ±350 m の 1 枚
    E = ground.GROUND_E
    assert after["grass_dark"] == pytest.approx((2 * E) ** 2, rel=1e-9)
    assert site_ground["n_split"] > 0 and site_ground["n_faces"] > site_ground["n_faces_before"]


def test_site_ground_campus_lawn_matches_boundary(site_ground, campus_data):
    """敷地の芝の面積は campus_boundary の面積と同じ（向きも形も崩れていない）。"""
    mb = site_ground["mb"]
    lawn = 0.0
    for f, mi in zip(mb.faces, mb.face_mat):
        pts = [mb.verts[k] for k in f]
        if mb.mat_names[mi] == "grass" and all(abs(p[2] - site.Z_CAMPUS) < 1e-12 for p in pts):
            lawn += M._area3(pts)
    want = abs(geom.poly_area(geom.dedup(campus_data["campus_boundary"])))
    assert lawn == pytest.approx(want, rel=1e-9)


def test_site_ground_occupancy_blocks_mall_and_basins(site_ground, campus_frame):
    occ = site_ground["occ"]
    for u, v in [(0.0, site.MALL_V), (150.0, site.MALL_V)]:
        assert occ.blocked(*campus_frame.xy(u, v))
    for u0, v0, u1, v1 in site.BASINS:
        assert occ.blocked(*campus_frame.xy((u0 + u1) * 0.5, (v0 + v1) * 0.5))


# ---- 写した定数（kcd_route/ground.py）----

def test_route_layers_match_site():
    """build_route.check_layers と同じ突き合わせ（Blender を待たずに落とす）。"""
    for name in ("Z_GROUND", "Z_ROAD", "Z_LINE", "Z_FOOT", "PATH_THICKNESS"):
        assert getattr(ground, name) == pytest.approx(getattr(site, name), abs=1e-9), name
    import build_route
    build_route.check_layers()


def test_z_layers_are_ordered():
    order = [site.Z_GROUND, site.Z_PARK, site.Z_CAMPUS, site.Z_AREA, site.Z_ROAD,
             site.Z_LINE, site.Z_FOOT, site.Z_MALL, site.Z_BASIN_FLOOR, site.Z_WATER, site.Z_BASIN_RIM]
    assert order == sorted(order)
    assert len(set(order)) == len(order)

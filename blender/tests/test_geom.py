"""kcd_lib.geom の性質テスト (#68)。乱数は種を固定して、毎回同じ形で試す。"""

import math
import random

import pytest

from kcd_lib import geom

RNG_SEED = 20260924


def _star(rng, n, r_in, r_out, cx=0.0, cy=0.0):
    """原点まわりに角度順に並べた、凹みのある星形（反時計回り）。"""
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        r = r_out if k % 2 == 0 else rng.uniform(r_in * 0.6, r_in)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def _random_polys(count=40):
    rng = random.Random(RNG_SEED)
    polys = []
    for _ in range(count):
        n = rng.randrange(3, 14) * 2
        polys.append(_star(rng, n, rng.uniform(2, 6), rng.uniform(7, 12),
                           rng.uniform(-50, 50), rng.uniform(-50, 50)))
    return polys


def _winding_number(pt, poly):
    """点の周りの巻き数（point_in_poly の突き合わせ用）。"""
    total = 0.0
    n = len(poly)
    for i in range(n):
        a = geom.sub(poly[i], pt)
        b = geom.sub(poly[(i + 1) % n], pt)
        total += math.atan2(geom.cross(a, b), geom.dot(a, b))
    return round(total / (2 * math.pi))


SQUARE = [(0.0, 0.0), (4.0, 0.0), (4.0, 4.0), (0.0, 4.0)]
# 凹 6 角形（L 字）。反時計回り
L_SHAPE = [(0.0, 0.0), (6.0, 0.0), (6.0, 2.0), (2.0, 2.0), (2.0, 6.0), (0.0, 6.0)]


# ---- 面積と向き ----

def test_poly_area_sign_follows_winding():
    assert geom.poly_area(SQUARE) == pytest.approx(16.0)
    assert geom.poly_area(list(reversed(SQUARE))) == pytest.approx(-16.0)
    assert geom.poly_area(L_SHAPE) == pytest.approx(20.0)


@pytest.mark.parametrize("poly", _random_polys())
def test_ensure_ccw_orients_and_is_idempotent(poly):
    for p in (poly, list(reversed(poly))):
        ccw = geom.ensure_ccw(p)
        assert geom.poly_area(ccw) > 0
        assert geom.ensure_ccw(ccw) == ccw
        assert abs(geom.poly_area(ccw)) == pytest.approx(abs(geom.poly_area(p)))
        assert sorted(ccw) == sorted(p)


def test_ensure_ccw_keeps_ccw_order_and_returns_a_copy():
    out = geom.ensure_ccw(SQUARE)
    assert out == SQUARE
    assert out is not SQUARE
    assert geom.ensure_ccw(list(reversed(SQUARE))) == SQUARE


def test_dedup_drops_repeats_and_closing_point():
    poly = [(0, 0), (0, 0), (1, 0), (1.00001, 0), (1, 1), (0, 1), (0, 0)]
    assert geom.dedup(poly) == [(0, 0), (1, 0), (1, 1), (0, 1)]
    assert all(isinstance(p, tuple) for p in geom.dedup([[0, 0], [1, 0], [1, 1]]))


def test_centroid_of_square_and_l_shape():
    assert geom.centroid(SQUARE) == pytest.approx((2.0, 2.0))
    # L 字 = 6x2 の帯（重心 (3, 1)）+ 2x4 の帯（重心 (1, 4)）
    assert geom.centroid(L_SHAPE) == pytest.approx(((12 * 3 + 8 * 1) / 20, (12 * 1 + 8 * 4) / 20))
    assert geom.centroid(list(reversed(L_SHAPE))) == pytest.approx(geom.centroid(L_SHAPE))


def test_bbox_perimeter_longest_edge():
    assert geom.bbox(L_SHAPE) == (0.0, 0.0, 6.0, 6.0)
    assert geom.perimeter(SQUARE) == pytest.approx(16.0)
    assert geom.longest_edge([(0, 0), (1, 0), (1, 5), (0, 5)]) == 1


# ---- オフセット ----

@pytest.mark.parametrize("d", [0.3, 1.0, -0.5])
def test_offset_square_is_exact(d):
    out = geom.offset_polygon(SQUARE, d)
    assert out == pytest.approx([(-d, -d), (4 + d, -d), (4 + d, 4 + d), (-d, 4 + d)])


def test_offset_accepts_clockwise_input():
    assert geom.offset_polygon(list(reversed(SQUARE)), 0.5) == pytest.approx(geom.offset_polygon(SQUARE, 0.5))


def test_offset_l_shape_moves_every_edge_by_d():
    d = 0.25
    out = geom.offset_polygon(L_SHAPE, d)
    assert geom.poly_area(out) > 0
    n = len(out)
    for i in range(n):
        # 元の辺と、オフセット後の対応する辺の距離がちょうど d
        a, b = L_SHAPE[i], L_SHAPE[(i + 1) % n]
        mid = geom.lerp(out[i], out[(i + 1) % n], 0.5)
        assert geom.dist_point_segment(mid, a, b) == pytest.approx(d)
        assert not geom.point_in_poly(mid, L_SHAPE)


@pytest.mark.parametrize("poly", _random_polys(20))
def test_offset_grows_outward_and_shrinks_inward(poly):
    area = geom.poly_area(geom.ensure_ccw(poly))
    grown = geom.offset_polygon(poly, 0.1)
    shrunk = geom.offset_polygon(poly, -0.1)
    assert geom.poly_area(grown) > area > geom.poly_area(shrunk) > 0
    for p in grown:
        assert not geom.point_in_poly(p, poly)
    for p in shrunk:
        assert geom.point_in_poly(p, poly)


def test_outward_normal_points_out_of_ccw_polygon():
    assert geom.outward_normal((0, 0), (4, 0)) == pytest.approx((0.0, -1.0))
    assert geom.outward_normal((4, 0), (4, 4)) == pytest.approx((1.0, 0.0))


# ---- 点の内外 ----

def test_point_in_poly_concave():
    assert geom.point_in_poly((1, 1), L_SHAPE)
    assert geom.point_in_poly((5, 1), L_SHAPE)
    assert geom.point_in_poly((1, 5), L_SHAPE)
    assert not geom.point_in_poly((4, 4), L_SHAPE)  # 入隅の外側
    assert not geom.point_in_poly((7, 1), L_SHAPE)
    assert geom.point_in_poly((1, 1), list(reversed(L_SHAPE)))


@pytest.mark.parametrize("poly", _random_polys(20))
def test_point_in_poly_matches_winding_number(poly):
    rng = random.Random(RNG_SEED + len(poly))
    x0, y0, x1, y1 = geom.bbox(poly)
    checked = 0
    for _ in range(200):
        pt = (rng.uniform(x0 - 1, x1 + 1), rng.uniform(y0 - 1, y1 + 1))
        if geom.dist_point_poly_edges(pt, poly) < 1e-6:
            continue
        assert geom.point_in_poly(pt, poly) == (_winding_number(pt, poly) != 0), pt
        checked += 1
    assert checked > 150


# ---- 距離と折れ線 ----

def test_dist_point_segment():
    assert geom.dist_point_segment((0, 3), (-1, 0), (1, 0)) == pytest.approx(3.0)
    assert geom.dist_point_segment((4, 3), (0, 0), (1, 0)) == pytest.approx(math.hypot(3, 3))
    assert geom.dist_point_segment((1, 1), (0, 0), (0, 0)) == pytest.approx(math.sqrt(2))


@pytest.mark.parametrize("step", [0.7, 1.0, 2.5])
def test_resample_spacing(step):
    line = [(0.0, 0.0), (5.0, 0.0), (5.0, 3.0), (5.0, 3.0), (9.0, 3.0)]
    out = geom.resample(line, step)
    assert out[0] == (0.0, 0.0)
    total = 12.0
    assert len(out) == 1 + int(total // step + 1e-9)
    # 同じ辺の上の隣り合う点は step ちょうど、角をまたぐ所は折れ線沿いに step
    arc = [0.0]
    for p in out[1:]:
        if p[1] == 0.0:
            arc.append(p[0])
        elif p[0] == 5.0:
            arc.append(5.0 + p[1])
        else:
            arc.append(8.0 + (p[0] - 5.0))
    for a, b in zip(arc, arc[1:]):
        assert b - a == pytest.approx(step)


def test_resample_short_input():
    assert geom.resample([(1, 2)], 1.0) == [(1, 2)]


def test_fillet_vertex_replaces_corner_with_arc():
    out = geom.fillet_vertex(SQUARE, 2, 1.0, segments=8)
    assert len(out) == len(SQUARE) - 1 + 9
    center = (3.0, 3.0)
    for p in out[2:11]:
        assert geom.length(geom.sub(p, center)) == pytest.approx(1.0)
    assert out[2] == pytest.approx((4.0, 3.0))
    assert out[10] == pytest.approx((3.0, 4.0))
    # 直線上の頂点（角度 180 度）はそのまま
    straight = [(0, 0), (1, 0), (2, 0), (2, 2), (0, 2)]
    assert geom.fillet_vertex(straight, 1, 0.5) == straight


# ---- キャンパスの軸 ----

@pytest.mark.parametrize("angle", [0.0, 0.4, 1.3, -2.2])
def test_frame_round_trip_and_ccw_rect(angle):
    f = geom.Frame((3 * math.cos(angle), 3 * math.sin(angle)))
    assert geom.length(f.u) == pytest.approx(1.0)
    assert geom.dot(f.u, f.v) == pytest.approx(0.0, abs=1e-12)
    assert geom.cross(f.u, f.v) == pytest.approx(1.0)  # v は u の左 → (u, v) も右手系
    for p in [(0.0, 0.0), (12.5, -7.0), (-300.0, 41.0)]:
        assert f.xy(*f.uv(p)) == pytest.approx(p)
    rect = f.rect(-2.0, -1.0, 5.0, 3.0)
    assert geom.poly_area(rect) == pytest.approx(7.0 * 4.0)
    assert f.uv_bbox(rect) == pytest.approx((-2.0, -1.0, 5.0, 3.0))


def test_campus_frame_follows_research1(campus_data, campus_frame):
    """build_campus.make_frame は研究棟 1 の長辺を u にする（建物の長手方向がそろう）。"""
    r1 = next(b for b in campus_data["buildings"] if b["id"] == "research1")
    poly = geom.dedup(r1["footprint"])
    i = geom.longest_edge(poly)
    edge = geom.normalize(geom.sub(poly[(i + 1) % len(poly)], poly[i]))
    assert abs(geom.dot(edge, campus_frame.u)) == pytest.approx(1.0)
    assert campus_frame.u[0] >= 0  # 東向きにそろえる

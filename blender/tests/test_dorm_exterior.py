"""寮の外観の自販機 (#41)。

躯体（bld_dorm）は footprint の外へ出さない約束で、ground.check() が外形の輪を測る。
手すりや自販機などの細部（bld_dorm_trim）はその検査の外なので、自販機が敷地の外へ出たり
隣家に寄ったり、勝手口をふさいだりしないことをここで確かめる。
"""

import math

import pytest

from kcd_lib import geom
from kcd_route import dorm, ground

VENDING = "vending_blue"
EPS = 1e-6


@pytest.fixture(scope="module")
def route_data():
    return ground.load()


@pytest.fixture(scope="module")
def built(route_data):
    return dorm.build_exterior(route_data["dormitory"])


@pytest.fixture(scope="module")
def loop(route_data):
    return dorm.loop_of(route_data["dormitory"])


@pytest.fixture(scope="module")
def side(built, loop):
    """自販機を置く東北東面の辺の枠（始点, 単位方向, 外向き法線, 長さ）。"""
    return dorm._edge_frame(loop, built[2]["balcony_edge"][1])


@pytest.fixture(scope="module")
def corners(built):
    """自販機の箱の 4 隅の XY（反時計回り）。vending_blue の面の頂点から取る。"""
    trim = built[1]
    assert VENDING in trim.mat_names
    mi = trim.mat_names.index(VENDING)
    pts = {(round(x, 6), round(y, 6))
           for face, m in zip(trim.faces, trim.face_mat) if m == mi
           for x, y, _ in (trim.verts[k] for k in face)}
    assert len(pts) == 4, "自販機は箱 1 つのはず: %s" % sorted(pts)
    cx = sum(p[0] for p in pts) / 4
    cy = sum(p[1] for p in pts) / 4
    return sorted(pts, key=lambda p: math.atan2(p[1] - cy, p[0] - cx))


def _along(corners, side, axis):
    """4 隅を辺ローカルに直した値（axis=1: 辺に沿う t, axis=2: 外向きの出 d）。"""
    return [geom.dot(geom.sub(c, side[0]), side[axis]) for c in corners]


def test_vending_is_inside_footprint(corners, loop):
    for c in corners:
        assert ground.point_in_poly(c, loop), "自販機の角 (%.3f, %.3f) が footprint の外" % c


def test_vending_stands_in_front_of_plinth_wall(corners, side):
    """基壇の外壁は footprint から CORE_IN 内側。その手前に立ち、壁に埋まらない。"""
    ds = _along(corners, side, 2)
    assert -dorm.CORE_IN - EPS <= min(ds) and max(ds) <= EPS, (min(ds), max(ds))


def test_vending_keeps_clear_of_neighbouring_houses(corners, route_data):
    """ground.check() が寮の輪に課すのと同じ距離（DORM_KEEPOUT）を自販機にも課す。"""
    houses = ground.pick_houses(route_data)
    assert houses, "隣家が 1 棟も拾えていない"
    ring = corners + [corners[0]]
    for b in houses:
        fp = ground.dedup(b["footprint"])
        gap = ground.dist_polyline_polyline(ring, fp + [fp[0]])
        if any(ground.point_in_poly(c, fp) for c in corners):
            gap = -gap
        assert gap >= ground.DORM_KEEPOUT, "建物 %s まで %.2f m" % (b["id"], gap)


def test_vending_does_not_overlap_annex_or_kitchen_door(corners, side, route_data):
    ts = _along(corners, side, 1)
    t0, t1 = min(ts), max(ts)
    annex = (dorm.CORE_U, dorm.CORE_U + dorm.ANNEX_L)
    # 勝手口とガラリはどちらも厨房の区間に入る
    kitchen = sorted(dorm._span(route_data["dormitory"], side,
                                8.0, dorm.KITCHEN_Y[0], 8.0, dorm.KITCHEN_Y[1]))
    for name, (a, b) in (("付属棟", annex), ("厨房の勝手口とガラリ", kitchen)):
        assert t1 <= a or t0 >= b, \
            "自販機 t %.2f..%.2f が%s t %.2f..%.2f に掛かる" % (t0, t1, name, a, b)

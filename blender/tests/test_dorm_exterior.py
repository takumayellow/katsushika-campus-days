"""寮の外観の自販機 (#41)。

躯体（bld_dorm）は footprint の外へ出さない約束で、ground.check() が外形の輪を測る。
手すりや自販機などの細部（bld_dorm_trim）はその検査の外なので、自販機が敷地の外へ出たり
隣家に寄ったり、勝手口や角の柱に掛かったりしないことをここで確かめる。あわせて、
README に書いた寸法と、石張りの面・2 階のスラブとの位置を固める。
"""

import math

import pytest

from kcd_lib import geom
from kcd_route import dorm, ground

VENDING = "vending_blue"
STONE = "stone_dark"
EPS = 1e-6
TOL = 1e-4      # 6 桁に丸めた頂点から測る長さの許容（m）


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
def box(built):
    """自販機の箱の 8 隅。vending_blue の面の頂点から取る。"""
    trim = built[1]
    assert VENDING in trim.mat_names
    mi = trim.mat_names.index(VENDING)
    pts = {tuple(round(c, 6) for c in trim.verts[k])
           for face, m in zip(trim.faces, trim.face_mat) if m == mi
           for k in face}
    assert len(pts) == 8, "自販機は箱 1 つのはず: %s" % sorted(pts)
    return sorted(pts)


@pytest.fixture(scope="module")
def corners(box):
    """自販機の箱の 4 隅の XY（反時計回り）。"""
    pts = {(x, y) for x, y, _ in box}
    assert len(pts) == 4, sorted(pts)
    cx = sum(p[0] for p in pts) / 4
    cy = sum(p[1] for p in pts) / 4
    return sorted(pts, key=lambda p: math.atan2(p[1] - cy, p[0] - cx))


def _along(corners, side, axis):
    """XY の点を辺ローカルに直した値（axis=1: 辺に沿う t, axis=2: 外向きの出 d）。"""
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


def test_vending_does_not_overlap_other_ground_features(corners, side, route_data):
    """東北東面の 1 階に並ぶ階段室・付属棟・厨房の勝手口とガラリ・角 A の柱と、辺に沿って重ならない。"""
    ts = _along(corners, side, 1)
    t0, t1 = min(ts), max(ts)
    # 勝手口とガラリはどちらも厨房の区間に入る
    kitchen = sorted(dorm._span(route_data["dormitory"], side,
                                8.0, dorm.KITCHEN_Y[0], 8.0, dorm.KITCHEN_Y[1]))
    spans = (("階段室", (0.0, dorm.CORE_U)),
             ("付属棟", (dorm.CORE_U, dorm.CORE_U + dorm.ANNEX_L)),
             ("厨房の勝手口とガラリ", kitchen),
             ("角 A の柱", (side[3] - dorm.CORE_IN, side[3])))
    for name, (a, b) in spans:
        assert t1 <= a or t0 >= b, \
            "自販機 t %.2f..%.2f が%s t %.2f..%.2f に掛かる" % (t0, t1, name, a, b)


def test_vending_has_the_documented_size(box, corners, side):
    """README_dorm.md に書いた幅 1.0 m・奥行き 0.41 m・高さ 1.83 m。"""
    ts = _along(corners, side, 1)
    ds = _along(corners, side, 2)
    zs = [z for _, _, z in box]
    assert max(ts) - min(ts) == pytest.approx(1.0, abs=TOL)
    assert max(ds) - min(ds) == pytest.approx(0.41, abs=TOL)
    assert min(zs) == pytest.approx(0.0, abs=TOL)
    assert max(zs) == pytest.approx(1.83, abs=TOL)


def test_vending_stays_below_the_balcony_slab(box, built):
    """1 階の壁の天端（2 階のバルコニーのスラブの下面）より上へ出ない。"""
    top1 = built[2]["gf"] - dorm.SLAB_T
    top = max(z for _, _, z in box)
    assert top <= top1 + EPS, "自販機の上端 %.3f m が 2 階のスラブの下面 %.3f m を越える" % (top, top1)


def test_vending_back_is_1cm_in_front_of_the_stone_face(box, corners, side, built):
    """背面は石張りの面の 1 cm 手前。同じ平面に重ねるとちらつく（z-fighting）。"""
    trim = built[1]
    stone = trim.mat_names.index(STONE)
    ts = _along(corners, side, 1)
    ds = _along(corners, side, 2)
    zs = [z for _, _, z in box]
    behind = set()
    for face, m in zip(trim.faces, trim.face_mat):
        if m != stone:
            continue
        pts = [trim.verts[k] for k in face]
        xy = [p[:2] for p in pts]
        pt, pd = _along(xy, side, 1), _along(xy, side, 2)
        pz = [p[2] for p in pts]
        # 辺に平行で、自販機の背面をすっかり覆う板
        if (max(pd) - min(pd) < EPS and min(pt) <= min(ts) and max(pt) >= max(ts)
                and min(pz) <= min(zs) and max(pz) >= max(zs)):
            behind.add(round(pd[0], 6))
    assert len(behind) == 1, "自販機の背後の石張りの面が 1 枚でない: %s" % sorted(behind)
    assert min(ds) - behind.pop() == pytest.approx(0.01, abs=TOL)

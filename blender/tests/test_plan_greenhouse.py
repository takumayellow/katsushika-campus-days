"""温室のプラン（kcd_interior.plan_greenhouse）: スタブで組み、L 字・高さ・Empty・三角数を確かめる (#114)。"""

import pytest

import stubs
from kcd_interior import closure, registry, signs
from kcd_interior import shell as ishell
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 5000
HEIGHT = 3.4          # PLATEAU の計測高さ（13122-bldg-97017）
FLOOR_T = 0.24        # 床スラブの厚み
ZONE_TOP = 2.7        # 屋内の訪問ゾーンの上端（InteriorStage: 中心 1.2 m + 高さ 3 m の半分）
EPS = 1e-6


def _ear_candidates(contours):
    out = []
    for c in contours:
        try:
            idx = stubs.tessellate_polygon([c])
        except ValueError:
            idx = [(0, k, k + 1) for k in range(1, len(c) - 1)]
        out.extend(tuple(c[i] for i in t) for t in idx)
    return out


@pytest.fixture(scope="module")
def built():
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", _ear_candidates)
        specs = ispec.build_specs(ispec.load_campus(CAMPUS_JSON))
        c = Ctx(specs["greenhouse"], seed=SEED)
        registry.get("greenhouse").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _verts(mb):
    return list(mb.verts)


def test_footprint_is_the_l_shape_of_plateau(built):
    """外接矩形 約 10.7 x 7.5 m、北東の角 約 2.0 x 3.5 m が欠ける（PLATEAU / OSM の外形）。"""
    s = built.spec
    assert s.width == pytest.approx(10.74, abs=0.05)
    assert s.depth == pytest.approx(7.47, abs=0.05)
    nx0, ny0, nx1, ny1 = s.notch
    assert nx0 == pytest.approx(s.x0) and ny1 == pytest.approx(s.y_back)
    assert nx1 - nx0 == pytest.approx(2.03, abs=0.05)
    assert ny1 - ny0 == pytest.approx(3.48, abs=0.05)


def test_nothing_is_taller_than_the_building(built):
    for mb in built.builders():
        for v in _verts(mb):
            assert -FLOOR_T - EPS <= v[2] <= HEIGHT + EPS, (mb.name, v)


def test_nothing_above_the_floor_stands_in_the_cut_corner(built):
    """欠けた角（外）には床から上の物を置かない。壁は外面線までなので、境界上は許す。"""
    nx0, ny0, nx1, ny1 = built.spec.notch
    for mb in built.builders():
        for v in _verts(mb):
            if v[2] <= EPS:
                continue
            inside = nx0 + EPS < v[0] < nx1 - EPS and ny0 + EPS < v[1] < ny1 - EPS
            assert not inside, (mb.name, v)


def test_furniture_stays_inside_the_room(built):
    """家具は外周壁と欠けた角の壁の室内側に収まる。"""
    s = built.spec
    ix0, iy0, ix1, iy1 = s.inner()
    nx0, ny0, nx1, ny1 = s.notch
    for name, mb in built.furns:
        for x, y, _z in _verts(mb):
            # 奥の壁の看板はガラスの室内側の面（内壁面 + GLASS_IN）に貼る
            y_max = iy1 + ishell.GLASS_IN
            assert ix0 - EPS <= x <= ix1 + EPS and iy0 - EPS <= y <= y_max + EPS, (name, x, y)
            in_wing = x < nx1 + ishell.WALL - EPS and y > ny0 - ishell.WALL + EPS
            assert not in_wing, (name, x, y)


def _glass_roof_faces(mb):
    gi = mb.mat_names.index("glass_clear")
    for f, m in zip(mb.faces, mb.face_mat):
        pts = [mb.verts[i] for i in f]
        if m == gi and pts[0][2] > 2.0 and all(abs(p[2] - pts[0][2]) < EPS for p in pts):
            yield pts


def test_roof_is_flat_glass_facing_the_room(built):
    """屋根は 1 つの高さの水平なガラスで、面は下（室内）を向く (#45)。"""
    faces = list(_glass_roof_faces(built.wall))
    assert faces
    # いちばん上の水平なガラスが屋根（入口のガラススクリーンの天面はそれより下）
    top = max(f[0][2] for f in faces)
    assert top <= HEIGHT
    faces = [f for f in faces if abs(f[0][2] - top) < EPS]
    area = 0.0
    for pts in faces:
        a, b, c = pts[0], pts[1], pts[2]
        nz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
        assert nz < 0.0
        area += abs(_area(pts))
    s = built.spec
    ix0, iy0, ix1, iy1 = s.inner()
    nx0, ny0, nx1, ny1 = s.notch
    hole = (nx1 + ishell.WALL - ix0) * (iy1 - (ny0 - ishell.WALL))
    assert area == pytest.approx((ix1 - ix0) * (iy1 - iy0) - hole, rel=1e-6)


def _area(pts):
    return 0.5 * sum(p[0] * q[1] - q[0] * p[1] for p, q in zip(pts, pts[1:] + pts[:1]))


def test_spawn_exit_poi_npc(built):
    names = [n for n, _ in built.empties]
    assert names.count("spawn_greenhouse") == 1
    assert names.count("exit_greenhouse") == 1
    assert "poi_greenhouse_plants" in names
    assert any(n.startswith("npc_greenhouse_") for n in names)


def test_nothing_hangs_over_the_poi(built):
    """訪問ゾーン（POI）の真上は、ゾーンの上端から床まで空ける。

    Unity のゾーンは床から 2.7 m の箱（InteriorStage）で、プレイスルーのテストは
    その上端から真下へ床を探す。途中に管などがあると床より先に当たる。
    """
    x, y, _z = dict(built.empties)["poi_greenhouse_plants"]
    for mb in built.builders():
        for f in mb.faces:
            pts = [mb.verts[i] for i in f]
            xs, ys, zs = zip(*pts)
            over = min(xs) <= x <= max(xs) and min(ys) <= y <= max(ys)
            assert not (over and max(zs) > EPS and min(zs) < ZONE_TOP), (mb.name, pts)


def test_cameras_are_inside_the_room(built):
    s = built.spec
    ix0, iy0, ix1, iy1 = s.inner()
    assert [cam[0] for cam in built.cams] == ["", "wing"]
    for _suffix, loc, _target, _lens in built.cams:
        assert ix0 < loc[0] < ix1 and iy0 < loc[1] < iy1


def test_note_describes_a_flat_roof(built):
    note = " ".join(built.notes)
    assert "陸屋根" in note and "切妻" not in note


def test_triangle_budget(built):
    assert built.tris() <= INT_CAP


def test_wall_check_follows_the_l_shape(built):
    """壁の穴の検査（closure）は、書き出した外形（notch つき）の 6 辺の内面をなぞる。"""
    s = built.spec
    w = ishell.WALL
    env = {"x0": s.x0, "x1": s.x1, "y_face": s.y_face, "y_back": s.y_back,
           "notch": list(s.notch)}
    edges = closure._edges(env, w)
    assert len(edges) == 6
    nx0, ny0, nx1, ny1 = s.notch
    inner = [e[1] for e in edges]
    assert inner[0] == pytest.approx((s.x0 + w, s.y_face + w))
    # 入隅は壁厚ぶん室内側（欠けた角から離れる側）へずれる
    assert (nx1 + w, ny0 - w) in [pytest.approx(p) for p in inner]
    for i, a, b, out in edges:
        # 外向きは辺に直交する単位ベクトル
        assert abs((b[0] - a[0]) * out[0] + (b[1] - a[1]) * out[1]) < EPS
        assert abs(out[0]) + abs(out[1]) == pytest.approx(1.0)


def test_wall_check_of_a_rect_keeps_the_four_sides():
    env = {"x0": -4.0, "x1": 4.0, "y_face": 2.5, "y_back": 10.0}
    edges = closure._edges(env, 0.3)
    assert [e[3] for e in edges] == [(0.0, -1.0), (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0)]
    assert [e[1] for e in edges] == [pytest.approx(p) for p in
                                      [(-3.7, 2.8), (3.7, 2.8), (3.7, 9.7), (-3.7, 9.7)]]

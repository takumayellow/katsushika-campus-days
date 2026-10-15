"""食堂の長机の列（plan_research2_1f.RUNS1 / plan_research2_2f.RUNS2）: 通路を空けて並ぶか、座面と人の位置が合うか。"""

import math

import pytest

from kcd_interior import furn_research2 as FR
from kcd_interior import plan_research2_1f as P1
from kcd_interior import plan_research2_2f as P2
from kcd_interior import plan_research2_dims as D
from kcd_interior import plan_research2_finish as FIN
from kcd_interior import plan_research2_poly as P
from kcd_lib.mesh import MeshBuilder

GAP = 0.6           # 列（椅子まで含む）のまわりに空ける通路
HALF_W = 1.0        # 列の中心から椅子の外側まで
DOOR = (21.0, 22.8, 23.0, 25.2)     # 研究棟への扉の前
# 列が接してよい、歩く場所として空けておく所
WALKS1 = ((-6.8, 6.8, -4.6, 10.5),      # 端末の前
          (-4.6, 13.0, -0.4, 15.0),     # 入口ホールの通り口
          DOOR)
WALKS2 = ((2.0, 17.9, 5.0, 20.0),       # 手前の階段の上り口
          (10.7, 31.0, 13.6, 34.0),     # 奥の階段の上り口
          DOOR)
# GAP を空ける物（壁と同じ扱い）
SOLIDS1 = (D.HALL, D.WC, D.FRONT_HOLE, D.AISLE, D.TRAY_RETURN) + tuple(
    (x - D.ISLAND_L / 2, D.ISLAND_Y[0], x + D.ISLAND_L / 2, D.ISLAND_Y[1]) for x in D.ISLAND_X)
SOLIDS2 = (D.FRONT_HOLE, D.BACK_HOLE, (D.WOOD_WALL[0], D.COUNTER2_Y - 2.5, 10.0, 41.0))


def _circles1():
    return ([((x, y), D.COL1 / 2) for x, y in D.COLS1]
            + [(D.edge_pt(i, x, P1.STOOL_OFF), 0.2) for i, x in D.STOOLS_1F]
            + [(D.edge_pt(i, x, P1.ROUND_OFF), 1.0) for i, x in P1.ROUNDS1])


def _circles2():
    out = ([((x, y), D.COL2 / 2) for x, y in D.COLS2]
           + [(c, 1.0) for c in P2.round_centers()]
           + [(D.edge_pt(i, x, D.BENCH_OFF), 0.95) for i, x in D.BENCHES2]
           + [(D.edge_pt(i, x, D.CHAIR_OFF), 0.3) for i, x in D.HIGH_CHAIRS])
    for i, x0, x1, _h in D.WINDOW_COUNTER_2F:
        out += [(D.edge_pt(i, x0 + (x1 - x0) * t / 10, 0.5), 0.3) for t in range(11)]
    return out


def _foot(x, y0, n):
    return (x - HALF_W, y0 - D.RUN_PITCH / 2, x + HALF_W, y0 + D.RUN_PITCH * (n - 0.5))


def _overlaps(a, b, m):
    return a[0] < b[2] + m and b[0] < a[2] + m and a[1] < b[3] + m and b[1] < a[3] + m


def _rect_dist(r, p):
    return math.hypot(max(r[0] - p[0], 0.0, p[0] - r[2]), max(r[1] - p[1], 0.0, p[1] - r[3]))


def _problems(run, poly, solids, walks, circles):
    f = _foot(*run)
    corners = ((f[0], f[1]), (f[2], f[1]), (f[2], f[3]), (f[0], f[3]))
    probes = corners + (((f[0] + f[2]) / 2, f[1]), ((f[0] + f[2]) / 2, f[3]))
    bad = ["outside" for c in corners if not P.inside(c, poly)]
    for k in range(len(poly)):
        a, b = poly[k], poly[(k + 1) % len(poly)]
        bad += ["wall" for c in probes if P.seg_dist(c, a, b) < GAP]
    bad += ["solid %r" % (r,) for r in solids if _overlaps(f, r, GAP)]
    bad += ["walk %r" % (r,) for r in walks if _overlaps(f, r, 0.0)]
    bad += ["circle %r" % (c,) for c, rad in circles if _rect_dist(f, c) < rad + GAP]
    return sorted(set(bad))


@pytest.mark.parametrize("run", P1.RUNS1)
def test_1f_runs_keep_the_aisles_clear(run):
    assert _problems(run, D.INNER, SOLIDS1, WALKS1, _circles1()) == []


@pytest.mark.parametrize("run", P2.RUNS2)
def test_2f_runs_keep_the_aisles_clear(run):
    assert _problems(run, D.FLOOR2, SOLIDS2, WALKS2, _circles2()) == []


@pytest.mark.parametrize("runs", [P1.RUNS1, P2.RUNS2])
def test_runs_do_not_touch_each_other(runs):
    feet = [_foot(*r) for r in runs]
    for i, a in enumerate(feet):
        for b in feet[i + 1:]:
            assert not _overlaps(a, b, GAP), (a, b)


def test_runs_stand_across_the_windows():
    """長手が Y（南の窓に直角）。どの列も柱の間を 6 等分した線の上にある。"""
    lines = set(D.run_x(-99.0, 99.0))
    for x, _y, _n in P1.RUNS1 + P2.RUNS2:
        assert x in lines


@pytest.mark.parametrize("p", [n[1:] for n in D.POIS] + list(D.NPCS) + [D.SPAWN])
def test_people_and_points_stand_in_the_aisles(p):
    runs = P1.RUNS1 if p[2] == D.Z1 else P2.RUNS2
    assert not any(_rect_dist(_foot(*r), p[:2]) < 0.3 for r in runs), p


def _chairs(mp, runs, z):
    got = []
    mp.setattr(FR, "chair", lambda mb, x, y, zz, ang, mat: got.append((x, y, zz, ang)))
    mb = MeshBuilder("t")
    for x, y, n in runs:
        FR.table_run(mb, x, y, n, z, ("m",), w=D.RUN_PITCH)
    return got


def test_1f_seat_marks_sit_on_the_chairs(monkeypatch):
    chairs = _chairs(monkeypatch, P1.RUNS1, D.Z1)
    marks = [s for s in D.SEATS if s[2] == pytest.approx(D.Z1 + D.SEAT_H)]
    assert len(marks) == 16
    for x, y, _z, ang in marks:
        hit = [c for c in chairs if math.hypot(c[0] - x, c[1] - y) < 1e-6]
        assert len(hit) == 1, (x, y)
        assert math.cos(hit[0][3] - ang) == pytest.approx(1.0), (x, y)


def test_table_run_chairs_face_the_table(monkeypatch):
    chairs = _chairs(monkeypatch, ((0.0, 0.0, 2),), 0.0)
    assert len(chairs) == 8
    for x, _y, _z, ang in chairs:
        want = D.FACE_W if x > 0 else D.FACE_E
        assert math.cos(ang - want) == pytest.approx(1.0)


@pytest.mark.parametrize("cam", [c for c in D.CAMS if c[0] != "top"], ids=lambda c: c[0] or "main")
def test_cameras_stand_in_the_aisles(cam):
    eye = cam[1]
    runs = P1.RUNS1 if eye[2] < D.Z2 else P2.RUNS2
    assert not any(_rect_dist(_foot(*r), eye[:2]) < 0.3 for r in runs), cam[0]


def test_table_run_is_one_top_along_y(monkeypatch):
    monkeypatch.setattr(FR, "chair", lambda *a: None)
    mb = MeshBuilder("t")
    FR.table_run(mb, 0.0, 0.0, 3, 0.0, ("m",), w=D.RUN_PITCH)
    xs = [v[0] for v in mb.verts]
    ys = [v[1] for v in mb.verts]
    assert (min(xs), max(xs)) == (pytest.approx(-0.4), pytest.approx(0.4))
    assert (min(ys), max(ys)) == (pytest.approx(-0.8), pytest.approx(4.0))
    tops = {(round(v[0], 6), round(v[1], 6)) for v in mb.verts if v[2] == pytest.approx(0.72)}
    assert len(tops) == 4


def test_2f_outlets_sit_past_the_south_end_of_every_run():
    pts = FIN.outlet_points()
    assert len(pts) == len(P2.RUNS2) == 27
    for p in pts:
        assert not any(_rect_dist(_foot(*r), p) < 0.3 for r in P2.RUNS2), p

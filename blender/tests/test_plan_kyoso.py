"""共創棟のプラン（kcd_interior.plan_kyoso）: スタブで組み、Empty・看板・カメラ・座席・三角数を確かめる。"""

import math

import pytest

import stubs
from kcd_interior import plan_kyoso_dims as D
from kcd_interior import registry, signs
from kcd_interior import shell as ishell
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 45000
N_SIGNS = 23
CAMS = ["", "_ls", "_sbback", "_2f", "_lounge2f", "_top", "_store"]
MARGIN = ishell.WALL          # 外周壁の厚み
EYE_MIN = 1.2                 # 人の目の高さの下限（床からの高さ）
STEP_MAX = 0.20               # 蹴上げの上限
HEAD_CLEAR = 2.0              # 段の上面から上の構造までの空き


# 座れない所: 柱（1F と 2F）・鉄骨階段・エスカレーター・自販機・カフェの箱。(x0, y0, x1, y1, z0, z1)
_H = D.COLUMN_SIZE * 0.5
SOLIDS = (
    [(x - _H, y - _H, x + _H, y + _H, 0.0, D.Z_CEIL2) for x, y in D.COLUMNS]
    + [(D.STAIR_X[0], D.STAIR_Y0, D.STAIR_X[1], D.STAIR_END_Y, 0.0, D.Z2 - 0.4),
       (D.ESC_X_LO - 0.8, D.ESC_Y[0], D.ESC_X_HI + 0.8, D.ESC_Y[1], 0.0, D.Z2 + 1.2),
       (D.VENDING_X - 0.4, D.VENDING_Y[0] - 0.55, D.VENDING_X + 0.4, D.VENDING_Y[-1] + 0.55,
        0.0, 2.0),
       (12.2, 36.6, 18.55, 39.1, 0.0, D.SB_TOP)]
)
# 区画ごとの三角形の上限（現在の値に 1 割強の余裕）。wall は看板の文字を含む。
SECTION_CAPS = {
    "floor": 100, "wall": 15000, "entry": 200, "lobby": 500, "cafe": 900, "store": 10700,
    "lounge": 2400, "ls": 1100, "studio": 1100, "ls2": 2000, "core": 900, "hall": 1600,
}


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
        c = Ctx(specs["kyoso"], seed=SEED)
        registry.get("kyoso").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def test_spawn_and_exit(built):
    names = [n for n, _ in built.empties]
    assert names.count("spawn_kyoso") == 1
    assert names.count("exit_kyoso") == 1


def test_pois_are_in_table_order(built):
    got = _empties(built, "poi_kyoso_")
    assert [n for n, _ in got] == ["poi_kyoso_%s" % p[0] for p in D.POIS]
    for (_, pos), (_name, x, y, z) in zip(got, D.POIS):
        assert pos == pytest.approx((x, y, z))


def test_npcs_are_in_table_order(built):
    got = _empties(built, "npc_kyoso_")
    assert [n for n, _ in got] == ["npc_kyoso_%d" % k for k in range(1, len(D.NPCS) + 1)]
    assert [p for _, p in got] == [pytest.approx(p) for p in D.NPCS]


def test_signs_are_numbered_in_spec_order(built):
    ja = [r["ja"] for r in built.signs]
    assert len(ja) == N_SIGNS
    assert all(s.strip() for s in ja)
    assert all(r["en"].strip() for r in built.signs)
    named = [r["empty"] for r in built.signs if r["empty"]]
    assert named == ["sign_kyoso_%d" % k for k in range(1, N_SIGNS + 1)]
    assert named == [n for n, _ in _empties(built, "sign_kyoso_")]
    assert "忘れ物" in ja


def test_cameras(built):
    assert [cam[0] for cam in built.cams] == CAMS


def _seat_names(c):
    return [n for n, _ in c.empties if n.startswith("seat_kyoso_") and n.count("_") == 2]


def test_seats_are_registered(built):
    assert len(_seat_names(built)) >= 85
    anchors = [n for n, _ in built.empties if "_a" in n and n.startswith("seat_kyoso_")]
    assert len(anchors) == built.seats >= 110


def test_seats_stand_on_a_floor_and_clear_of_solids(built):
    floors = (0.0, D.DECK_H, D.Z2)
    seats = built.seat_list()
    for q in seats:
        assert min(abs(q["c"][2] - z) for z in floors) < 1e-6, q["c"]
        for a in q["anchors"]:
            assert min(abs(a[2] - z) for z in floors) < 1e-6, a
            assert _inside(a[0], a[1]), a
            for x0, y0, x1, y1, z0, z1 in SOLIDS:
                assert not (x0 < a[0] < x1 and y0 < a[1] < y1 and z0 - 0.01 <= a[2] < z1), (a, x0, y0)
    pts = [a for q in seats for a in q["anchors"]]
    for i, p in enumerate(pts):
        for q in pts[i + 1:]:
            if abs(p[2] - q[2]) < 1e-6:
                assert math.hypot(p[0] - q[0], p[1] - q[1]) > 0.3, (p, q)


def test_triangles_fit_the_cap(built):
    assert built.tris() <= INT_CAP


def _sections(c):
    out = {"floor": c.floor, "wall": c.wall}
    out.update({name.split("_", 3)[3]: mb for name, mb in c.furns})
    return out


def test_every_section_has_a_cap(built):
    assert set(_sections(built)) == set(SECTION_CAPS)


def test_sections_fit_their_triangle_caps(built):
    for name, mb in _sections(built).items():
        tris = sum(max(0, len(f) - 2) for f in mb.faces)
        assert 0 < tris <= SECTION_CAPS[name], name


def test_sections_stay_on_the_footprint(built):
    s, tol = built.spec, 0.01
    for name, mb in _sections(built).items():
        vs = mb.verts
        assert min(v[0] for v in vs) >= s.x0 - tol and max(v[0] for v in vs) <= s.x1 + tol, name
        assert min(v[1] for v in vs) >= s.y_face - tol and max(v[1] for v in vs) <= s.y_back + tol, name


def _inside(x, y):
    if not (D.X_IN0 < x < D.X_IN1 and D.Y_IN0 < y < D.Y_PARK):
        return False
    return not any(x0 < x < x1 and y0 < y < y1 for x0, y0, x1, y1 in D.CLOSED)


def test_plan_lines_follow_the_footprint(built):
    s = built.spec
    assert s.levels == 11
    assert D.X_IN0 == pytest.approx(s.x0 + MARGIN)
    assert D.X_IN1 == pytest.approx(s.x1 - MARGIN)
    assert D.Y_IN0 == pytest.approx(s.y_face + MARGIN)
    assert D.Y_PARK == pytest.approx(s.y_back - MARGIN)


def test_pois_npcs_and_cameras_are_inside_the_walls(built):
    pts = [p[1:3] for p in D.POIS] + [n[:2] for n in D.NPCS] + [cam[1][:2] for cam in D.CAMS]
    assert [p for p in pts if not _inside(*p)] == []
    for _, _, _, z in D.POIS:
        assert z in (0.0, D.Z2)
    for _, _, z in D.NPCS:
        assert z in (0.0, D.DECK_H, D.Z2)
    for cam in D.CAMS:
        assert 0.0 < cam[1][2] < D.Z_TOP and 0.0 < cam[2][2] < D.Z_TOP


def test_stair_steps_are_climbable():
    assert 0.0 < D.STAIR_RISE <= STEP_MAX
    assert D.STAIR_RUN >= 0.25
    assert D.STAIR_RISE * D.STAIR_N == pytest.approx(D.Z2)
    assert D.STAIR_END_Y == pytest.approx(D.STAIR_Y0 + D.STAIR_RUN * (D.STAIR_N - 1))


def test_stair_has_headroom_under_the_slab():
    hx0, hy0, hx1, hy1 = D.STAIR_HOLE
    assert hx0 <= D.STAIR_X[0] and D.STAIR_X[1] <= hx1
    for i in range(1, D.STAIR_N):
        top = D.STAIR_RISE * i
        ym = D.STAIR_Y0 + D.STAIR_RUN * (i - 0.5)
        if not hy0 <= ym <= hy1:
            assert D.Z_SLAB - top >= HEAD_CLEAR, i
    # 最後の段は 2F の床へつながる
    assert hy1 == pytest.approx(D.STAIR_END_Y)


def test_escalator_contract(built):
    names = dict(built.empties)
    lo, hi = names["esc_kyoso_1f_lo"], names["esc_kyoso_1f_hi"]
    assert lo == pytest.approx((D.ESC_X_LO, D.ESC_YC, 0.0))
    assert hi == pytest.approx((D.ESC_X_HI, D.ESC_YC, D.Z2))
    assert [p.name for p in built.parts] == ["escsteps_kyoso_1f"]
    assert D.ESC_N == round(D.Z2 / D.ESC_RISE) == 27
    # 2F の降り口の床がスラブ A の縁になっている
    assert D.SLAB_A[0] == pytest.approx(D.ESC_LAND_X)

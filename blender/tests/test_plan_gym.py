"""体育館のプラン（kcd_interior.plan_gym）: スタブで組み、Empty・座面・看板・カメラ・三角数・外形を確かめる。"""

import json
import re
from pathlib import Path

import pytest

import stubs
from kcd_interior import plan_gym_dims as D
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 40000
N_SIGNS = 13
STEP_MAX = 0.4         # Unity の stepOffset
TOP = D.C_HALL + 0.2   # 屋根の板より上に何も出さない


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
        c = Ctx(specs["gym"], seed=SEED)
        registry.get("gym").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def _faces(c):
    for mb in c.builders():
        for f in mb.faces:
            yield [mb.verts[i] for i in f]


def _in_polygon(poly, x, y):
    inside = False
    for (ax, ay), (bx, by) in zip(poly, poly[1:] + poly[:1]):
        if (ay > y) != (by > y) and x < ax + (y - ay) * (bx - ax) / (by - ay):
            inside = not inside
    return inside


def test_spawn_and_exit_are_inside_the_vestibule(built):
    x0, x1, y0, y1 = D.VEST
    assert D.DOOR[0] < D.SPAWN[0] < D.DOOR[1]
    assert y0 < D.EXIT[1] < D.SPAWN[1] < y1


def test_pois_are_in_table_order(built):
    got = _empties(built, "poi_gym_")
    assert [n for n, _ in got] == ["poi_gym_%s" % p[0] for p in D.POIS]
    for (_, pos), (_name, x, y, z) in zip(got, D.POIS):
        assert pos == pytest.approx((x, y, z))


def test_npcs_are_in_table_order(built):
    got = _empties(built, "npc_gym_")
    assert [n for n, _ in got] == ["npc_gym_%d" % k for k in range(1, len(D.NPCS) + 1)]
    assert [p for _, p in got] == [pytest.approx(p) for p in D.NPCS]


def test_seats_follow_the_table(built):
    names = [n for n, _ in built.empties if re.fullmatch(r"seat_gym_\d\d", n)]
    assert names == ["seat_gym_%02d" % k for k in range(1, len(D.SEATS) + 1)]
    assert built.seats == len(D.SEATS) == 10
    pos = dict(built.empties)
    for k, (_kind, x, y, z) in enumerate(D.SEATS, 1):
        name = "seat_gym_%02d" % k
        assert pos[name] == pytest.approx((x, y, z), abs=0.02)
        assert name + "_a0" in pos


def test_signs_are_in_table_order(built):
    ja = [r["ja"] for r in built.signs]
    assert len(ja) == N_SIGNS
    assert ja[:3] == ["メインアリーナ", "メインアリーナ", "トレーニングルーム"]
    assert ja[-1] == "AED"
    assert [r["empty"] for r in built.signs if r["empty"]] == [
        "sign_gym_%d" % k for k in range(1, N_SIGNS + 1)]
    assert all(r["en"] for r in built.signs)


def test_six_cameras(built):
    assert [cam[0] for cam in built.cams] == [c[0] for c in D.CAMS]
    assert len(built.cams) == 6


def test_triangles_fit_the_cap_and_top(built):
    assert built.tris() <= INT_CAP
    assert max(p[2] for f in _faces(built) for p in f) <= TOP


def test_everything_stays_on_the_footprint(built):
    """全頂点が外形（FOOT）の外へ 0.35 m 以上はみ出さない（壁の厚み分は許す）。"""
    foot = [tuple(p) for p in D.FOOT]
    xs = [p[0] for p in foot]
    ys = [p[1] for p in foot]
    tol = 0.35
    for mb in built.builders():
        for v in mb.verts:
            assert min(xs) - tol <= v[0] <= max(xs) + tol, v
            assert min(ys) - tol <= v[1] <= max(ys) + tol, v


def test_pois_npcs_and_cameras_are_inside_the_walls(built):
    foot = [tuple(p) for p in D.FOOT]
    pts = [p[1:3] for p in D.POIS] + [n[:2] for n in D.NPCS] + [s[1:3] for s in D.SEATS]
    pts += [cam[1][:2] for cam in D.CAMS]
    assert [p for p in pts if not _in_polygon(foot, *p)] == []


def test_stair_steps_are_climbable():
    rise = D.Z2 / D.STAIR_N
    assert 0.0 < rise <= STEP_MAX
    assert D.STAIR_TREAD >= 0.25


def test_stand_tiers_step_by_climbable_rises():
    tops = [t[2] for t in D.TIERS]
    levels = [D.Z_FRONT] + tops + [D.Z2]
    for a, b in zip(levels, levels[1:]):
        assert 0.0 <= b - a <= 2 * 0.21 + 1e-6, (a, b)


def test_quest_and_collectible_destinations_exist(built):
    data = Path(CAMPUS_JSON).parent.parent.parent / "unity" / "KatsushikaCampusDays" / "Assets" / "Data"
    coll = json.loads((data / "Collectibles" / "collectibles.json").read_text(encoding="utf-8"))
    targets = {i["position"] for i in coll["collectibles"]
               if i.get("building") == "gym" and str(i.get("position", "")).startswith("poi_")}
    names = {n for n, _ in built.empties}
    assert targets and targets <= names, targets - names

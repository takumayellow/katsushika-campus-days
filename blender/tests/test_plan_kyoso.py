"""共創棟のプラン（kcd_interior.plan_kyoso）: スタブで組み、Empty・看板・カメラ・座席・三角数を確かめる。"""

import pytest

import stubs
from kcd_interior import plan_kyoso_dims as D
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 45000
N_SIGNS = 23
CAMS = ["", "_ls", "_sbback", "_2f", "_top", "_store"]


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
    assert "忘れもの" in ja


def test_six_cameras(built):
    assert [cam[0] for cam in built.cams] == CAMS


def test_seats_are_registered(built):
    seats = [n for n, _ in built.empties if n.startswith("seat_kyoso_")]
    assert len(seats) >= 40


def test_triangles_fit_the_cap(built):
    assert built.tris() <= INT_CAP

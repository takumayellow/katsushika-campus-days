"""第2研究棟（ロビー）のプラン（kcd_interior.plan_research2）と、食堂との分け方（spec.py の定数・入口の扉）。"""

import pytest

import stubs
from kcd_interior import plan_research2 as L
from kcd_interior import plan_research2_dims as D
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import entrances, signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 20000


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
def specs():
    return ispec.build_specs(ispec.load_campus(CAMPUS_JSON))


@pytest.fixture(scope="module")
def built(specs):
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", _ear_candidates)
        c = Ctx(specs["research2"], seed=SEED)
        registry.get("research2").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def test_split_constants_match_the_dims():
    assert ispec.CAFETERIA_X1 == D.CUT_X
    assert ispec.LOBBY_X0 < D.CUT_X < ispec.CAFETERIA_X1 + 0.2
    assert L.HOST_Y0 == pytest.approx(ispec.LOBBY_Y0 - 4.0)


def test_two_specs_share_one_host(specs):
    assert specs["cafeteria"].host == specs["research2"].host == "research2"
    assert specs["cafeteria"].display == "食堂"
    assert specs["research2"].display == "第2研究棟"
    assert specs["research2"].levels == 1


def test_lobby_door_is_the_second_campus_door():
    rows = {r[0]: r for r in entrances.DOORS if r[0] in ("cafeteria", "research2")}
    assert set(rows) == {"cafeteria", "research2"}
    assert rows["cafeteria"][1] == (14.5, -22.0)
    assert rows["research2"][1] == (14.5 + ispec.LOBBY_DOOR_X, -22.0)


def test_spawn_exit_and_name_signs(built):
    names = [n for n, _ in built.empties]
    assert names.count("spawn_research2") == 1
    assert names.count("exit_research2") == 1
    ja = [r["ja"] for r in built.signs]
    assert "第2研究棟" in ja
    assert all(r["en"].strip() for r in built.signs)


def test_everything_stands_inside_the_walls(built):
    x0, y0, x1, y1 = built.spec.inner()
    for name, (x, y, z) in built.empties:
        if name.startswith(("spawn_", "poi_", "npc_")):
            assert x0 < x < x1 and y0 < y < y1, name
    for suffix, loc, _t, _l in built.cams:
        if suffix != "top":
            assert x0 < loc[0] < x1 and y0 < loc[1] < y1, suffix
    for x, y, *_ in built.lights:
        assert x0 < x < x1 and y0 < y < y1


def test_no_seats_and_small(built):
    assert built.seats == 0
    assert built.tris() <= INT_CAP

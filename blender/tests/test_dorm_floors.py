"""寮の屋内（1F・2F・屋上）: スタブで組み、Empty の約束・階ごとの高さ・三角数の予算を確かめる。"""

import json

import pytest

from kcd_interior import signs
from kcd_interior.ctx import Ctx
from kcd_lib import signtext
from kcd_route import dorm_interior as D
from kcd_route.dorm_levels import SLAB, Z2, ZR

from test_interior_signs import DORM_SEED, ROUTE_JSON, _ear_candidates

DORM_TRI_CAP = 30000


@pytest.fixture(scope="module")
def dorm():
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr(signtext, "cdt_candidates", _ear_candidates)
        with open(ROUTE_JSON, encoding="utf-8") as fp:
            info = json.load(fp)["dormitory"]
        info["footprint"] = [tuple(p) for p in info["footprint"]]
        c = Ctx(D.make_spec(info), seed=DORM_SEED)
        D.build(c)
        signs.check(c)
    return c


def _empties(c):
    return dict(c.empties)


def test_triangles_within_budget(dorm):
    assert 0 < dorm.tris() <= DORM_TRI_CAP


def test_unity_contract_empties(dorm):
    e = _empties(dorm)
    for name in ("spawn_dorm", "exit_dorm", "npc_dorm_1", "npc_dorm_head"):
        assert name in e, name
    assert e["npc_dorm_head"] == e["npc_dorm_1"]


@pytest.mark.parametrize("name,z", [
    ("poi_dorm_lounge2", Z2), ("poi_dorm_room", Z2), ("poi_dorm_laundry", Z2),
    ("poi_dorm_kitchen2", Z2), ("poi_dorm_okujo", ZR),
])
def test_upper_pois_stand_on_their_floor(dorm, name, z):
    assert _empties(dorm)[name][2] == pytest.approx(z)


def test_ground_floor_pois_stay_at_zero(dorm):
    e = _empties(dorm)
    for name in ("poi_dorm_lounge", "poi_dorm_shokudo", "poi_dorm_daiyokujo", "poi_dorm_yokushitsu"):
        assert e[name][2] == pytest.approx(0.0), name


def test_levels_are_ordered():
    assert 0.0 < Z2 < ZR
    assert SLAB < Z2

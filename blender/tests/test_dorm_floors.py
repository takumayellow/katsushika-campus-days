"""寮の屋内（1F・2F・屋上）: スタブで組み、Empty の約束・階ごとの高さ・三角数の予算を確かめる。"""

import json

import numpy as np
import pytest

from kcd_interior import signs
from kcd_interior.ctx import Ctx
from kcd_lib import signtext
from kcd_route import dorm_interior as D
from kcd_route.dorm_levels import SLAB, Z2, ZR

from raycast import first_hit
from test_interior_signs import DORM_SEED, ROUTE_JSON, _ear_candidates, _triangles

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


def _up_faces(mb, z, tol=0.02):
    """高さ z の上向きの面の数。"""
    n = 0
    for f in mb.faces:
        pts = [mb.verts[i] for i in f]
        if all(abs(p[2] - z) < tol for p in pts):
            n += 1
    return n


def test_stair_treads_and_floors_are_ground(dorm):
    """踏面と各階の床は c.floor（Unity は "floor" 接頭辞のメッシュだけを Ground にする）に積まれる。"""
    for z in (Z2, ZR):
        assert _up_faces(dorm.floor, z) > 0, z
    tread_tops = {round(p[2], 3) for f in dorm.floor.faces for p in (dorm.floor.verts[i] for i in f)}
    steps = [t for t in tread_tops if 0.1 < t < Z2 - 0.1]
    assert len(steps) >= 10, "主階段の踏面が c.floor に無い"
    roof_steps = [t for t in tread_tops if Z2 + 0.1 < t < ZR - 0.1]
    assert len(roof_steps) >= 15, "塔屋の階段の踏面が c.floor に無い"


def test_roof_is_enclosed_on_all_four_sides(dorm):
    """屋上の高さ（腰から胸）で四方へ撃つと、外周の縁のすぐ外までに必ず壁か柵に当たる。"""
    tri = _triangles(dorm)
    x0, y0, x1, y1 = dorm.spec.inner()
    holes = []
    for z in (ZR + 0.6, ZR + 1.1):
        for k in range(1, 8):
            f = k / 8.0
            x = x0 + (x1 - x0) * f
            y = y0 + (y1 - y0) * f
            for o, d, limit in (
                ((x, y0 + 4.0, z), (0.0, -1.0, 0.0), 4.0),
                ((x, y1 - 4.0, z), (0.0, 1.0, 0.0), 4.0),
                ((x0 + 4.0, y, z), (-1.0, 0.0, 0.0), 4.0),
                ((x1 - 4.0, y, z), (1.0, 0.0, 0.0), 4.0),
            ):
                t = first_hit(np.array(o), np.array(d), tri)
                if not t < limit + 0.6:
                    holes.append((o, d, t))
    assert holes == [], holes[:5]

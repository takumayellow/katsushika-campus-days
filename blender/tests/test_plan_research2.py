"""第2研究棟（ロビー）のプラン（kcd_interior.plan_research2）と、食堂との分け方（spec.py の定数・入口の扉）。"""

import pytest

import stubs
from kcd_interior import plan_research2 as L
from kcd_interior import plan_research2_dims as D
from kcd_interior import plan_research2_upper as U
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


def _in_upper(x, y, z):
    """上の階の廊下か EV ホールの内面の内（ロビーのローカル、z は床の高さの範囲）。"""
    cx0, cy0, cx1, cy1 = D.UPPER_CORR
    hx0, hy0, hx1, hy1 = D.UPPER_EV_HALL
    hx, hy = x + ispec.LOBBY_DOOR_X, y + L.HOST_Y0
    z_lo = D.UPPER_FLOORS[0][1]
    in_corr = cx0 < hx < cx1 and cy0 < hy < cy1
    in_hall = hx0 < hx < hx1 and hy0 <= hy < hy1
    return z >= z_lo and (in_corr or in_hall)


def test_everything_stands_inside_the_walls(built):
    x0, y0, x1, y1 = built.spec.inner()

    def inside(x, y, z):
        return (x0 < x < x1 and y0 < y < y1 and z < D.UPPER_FLOORS[0][1]) or _in_upper(x, y, z)

    for name, (x, y, z) in built.empties:
        if name.startswith(("spawn_", "poi_", "npc_", "ev_", "lamp_")):
            assert inside(x, y, z), name
    for suffix, loc, _t, _l in built.cams:
        if suffix != "top":
            assert inside(*loc), suffix
    for x, y, z, *_ in built.lights:
        assert inside(x, y, z - 1.0)


def test_elevator_stops_on_every_floor(built):
    pos = dict(built.empties)
    floors = [("1F", 0.0)] + list(D.UPPER_FLOORS)
    for name, z in floors:
        p, f = pos["ev_research2_%s" % name], pos["ev_research2_%s_f" % name]
        assert p[2] == pytest.approx(z) and f[2] == pytest.approx(z), name
        # 降りた所は EV の扉の前で、正面は +X（扉を背にする）
        assert f[0] - p[0] == pytest.approx(1.0) and f[1] == pytest.approx(p[1]), name
        assert p[0] == pytest.approx(L.EV[2] + U.EV_OUT), name
        assert L.EV_Y[0] < p[1] < L.EV_Y[1], name
    assert not any(n.startswith("ev_research2_2F") for n in pos)


def test_each_upper_floor_has_its_own_lamps_and_parts(built):
    names = [n for n, _ in built.empties]
    meshes = [mb.name for mb in built.builders()]
    for name, z in D.UPPER_FLOORS:
        lamps = [p for n, p in built.empties if n.startswith("lamp_research2_%s_" % name)]
        assert len(lamps) == len(D.UPPER_LAMP_X) + 1, name
        assert all(p[2] == pytest.approx(z + U.LAMP_DZ) for p in lamps), name
        key = name.lower()
        for role in ("floor", "wall"):
            got = [m for m in meshes if m.startswith("%s_research2_%s_" % (role, key))]
            assert len(got) == len(U.EDGES) - 1, (role, name)
    assert len(set(names)) == len(names)


def test_upper_floor_walls_close_the_corridor(built):
    """上の階の外周の壁が、廊下と EV ホールの内面を 4 方から囲む（各階・壁の高さぶん）。"""
    zn = built.closure_zones
    assert len(zn) == 1
    z = zn[0]
    w = D.UPPER_WALL
    assert z["x0"] == pytest.approx(L.hx(D.UPPER_CORR[0] - w), abs=1e-3)
    assert z["x1"] == pytest.approx(L.hx(D.UPPER_CORR[2] + w), abs=1e-3)
    assert z["y_face"] == pytest.approx(L.hy(D.UPPER_CORR[1] - w), abs=1e-3)
    assert z["y_back"] == pytest.approx(L.hy(D.UPPER_EV_HALL[3] + w), abs=1e-3)
    nx0, ny0, nx1, ny1 = z["notch"]
    assert nx0 == pytest.approx(z["x0"]) and ny1 == pytest.approx(z["y_back"])
    assert ny0 == pytest.approx(L.hy(D.UPPER_CORR[3] + w), abs=1e-3)
    assert nx1 == pytest.approx(L.hx(D.UPPER_EV_HALL[0] - w), abs=1e-3)
    assert z["z_min"] < D.UPPER_FLOORS[0][1] and z["z_max"] > D.UPPER_FLOORS[-1][1]
    # 上の階の床はロビーの天井より上
    assert D.UPPER_FLOORS[0][1] - D.SLAB > D.C2


def test_upper_floor_signs_name_the_use(built):
    ja = [r["ja"] for r in built.signs]
    for name, _z in D.UPPER_FLOORS:
        assert D.UPPER_USE[name][0] in ja, name


def test_no_seats_and_small(built):
    assert built.seats == 0
    assert built.tris() <= INT_CAP

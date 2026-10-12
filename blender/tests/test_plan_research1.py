"""第1研究棟のプラン（kcd_interior.plan_research1）: スタブで組み、Empty・座面・看板・カメラ・三角数を確かめる。"""

import math
import re

import pytest

import stubs
from kcd_interior import plan_research1_dims as D
from kcd_interior import registry, signs
from kcd_interior import spec as ispec
from kcd_interior.ctx import Ctx
from kcd_lib import signtext

from conftest import CAMPUS_JSON

SEED = 1709
INT_CAP = 50000
N_BOARDS = 25          # 文字入りの板の枚数
N_SIGNS = 25           # そのうち Empty を持つ板（全部）
STAND_STEP = 0.55      # Unity の StandUp が前へ出す距離
CAPSULE_R = 0.31       # プレイヤーのカプセルの半径
TABLE_R = 0.45         # 丸テーブルの天板の半径
TOP = D.Z_SKY + 0.05   # トップライトより上に何も出さない
STEP_MAX = 0.4         # Unity の stepOffset


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
        c = Ctx(specs["research1"], seed=SEED)
        registry.get("research1").build(c)
        signs.check(c)
        c.flush_seats()
    return c


def _empties(c, prefix):
    return [(n, p) for n, p in c.empties if n.startswith(prefix)]


def _faces(c):
    for mb in c.builders():
        for f in mb.faces:
            yield [mb.verts[i] for i in f]


def test_spawn_and_exit_are_inside_the_vestibule(built):
    assert D.DOOR[0] < D.SPAWN[0] < D.DOOR[1]
    assert D.Y_DOOR < D.EXIT[1] < D.SPAWN[1]


def test_pois_are_in_table_order(built):
    got = _empties(built, "poi_research1_")
    assert [n for n, _ in got] == ["poi_research1_%s" % p[0] for p in D.POIS]
    for (_, pos), (_name, x, y, z) in zip(got, D.POIS):
        assert pos == pytest.approx((x, y, z))


def test_notice_poi_is_present(built):
    assert "poi_research1_notice" in [n for n, _ in built.empties]


def test_npcs_are_in_table_order(built):
    got = _empties(built, "npc_research1_")
    assert [n for n, _ in got] == ["npc_research1_%d" % k for k in range(1, len(D.NPCS) + 1)]
    assert [p for _, p in got] == [pytest.approx(p) for p in D.NPCS]


def _seat_centers():
    """spec §4-3 の順の座面の中心（01〜02 は西側ラウンジのベンチ）。"""
    return [(x, y) for x, y in D.SEAT_BENCHES]


def test_seats_follow_the_spec_table(built):
    names = [n for n, _ in built.empties if re.fullmatch(r"seat_research1_\d\d", n)]
    assert names == ["seat_research1_%02d" % k for k in range(1, len(D.SEAT_BENCHES) + 1)]
    assert built.seats == len(D.SEAT_BENCHES) == 2
    pos = dict(built.empties)
    for k, (x, y) in enumerate(_seat_centers(), 1):
        name = "seat_research1_%02d" % k
        assert pos[name] == pytest.approx((x, y, D.Z1), abs=0.02)
        assert name + "_a0" in pos


def test_standing_up_never_lands_in_a_table(built):
    """立つと座る位置 a0 から前へ 0.55 m 出る。その位置のカプセルが丸テーブルに掛からない。"""
    pos = dict(built.empties)
    seats = [n for n, _ in built.empties if re.fullmatch(r"seat_research1_\d\d", n)]
    assert seats
    for name in seats:
        cx, cy, _ = pos[name]
        fx, fy, _ = pos[name + "_f"]
        L = math.hypot(fx - cx, fy - cy)
        ux, uy = (fx - cx) / L, (fy - cy) / L
        for k in range(len([n for n in pos if n.startswith(name + "_a")])):
            ax, ay, _ = pos["%s_a%d" % (name, k)]
            sx, sy = ax + STAND_STEP * ux, ay + STAND_STEP * uy
            for tx, ty in D.TABLES:
                assert math.hypot(sx - tx, sy - ty) >= TABLE_R + CAPSULE_R, (name, k, tx, ty)


def test_signs_are_in_table_order(built):
    ja = [r["ja"] for r in built.signs]
    assert len(ja) == N_BOARDS
    assert ja[0].startswith("第1研究棟 フロアガイド\n")
    assert ja[1:4] == ["入試センター", "庶務課", "学科事務センター"]
    assert ja[6].startswith("民間航空機用FJR710ジェットエンジン")
    assert ja[7:10] == ["インキュベーションルーム(%d)" % k for k in (1, 2, 3)]
    assert ja[-1] == "実験室"
    assert [r["empty"] for r in built.signs if r["empty"]] == [
        "sign_research1_%d" % k for k in range(1, N_SIGNS + 1)]


def test_floor_guide_lists_every_floor(built):
    guide = built.signs[0]["ja"]
    for k in range(1, 12):
        assert "\n%dF " % k in guide


def test_six_cameras(built):
    assert [cam[0] for cam in built.cams] == ["", "atrium", "fjr710", "upper", "corridor", "top"]
    s = built.spec
    for suffix, loc, _target, _lens in built.cams:
        if suffix == "top":
            continue
        assert s.x0 < loc[0] < s.x1
        assert 0.0 < loc[2] < D.Z_SKY


def test_triangles_fit_the_cap_and_top(built):
    assert built.tris() <= INT_CAP
    assert max(p[2] for f in _faces(built) for p in f) <= TOP


def _sections(c):
    out = {"floor": c.floor, "wall": c.wall}
    out.update({name.split("_", 3)[3]: mb for name, mb in c.furns})
    return out


def test_sections_stay_on_the_footprint(built):
    s, tol = built.spec, 0.01
    for name, mb in _sections(built).items():
        vs = mb.verts
        assert min(v[0] for v in vs) >= s.x0 - tol and max(v[0] for v in vs) <= s.x1 + tol, name
        assert min(v[1] for v in vs) >= s.y_face - tol and max(v[1] for v in vs) <= s.y_back + tol, name


def _in_polygon(poly, x, y):
    inside = False
    for (ax, ay), (bx, by) in zip(poly, poly[1:] + poly[:1]):
        if (ay > y) != (by > y) and x < ax + (y - ay) * (bx - ax) / (by - ay):
            inside = not inside
    return inside


def test_pois_npcs_and_cameras_are_inside_the_walls(built):
    pts = [p[1:3] for p in D.POIS] + [n[:2] for n in D.NPCS] + [cam[1][:2] for cam in D.CAMS if cam[0] != "top"]
    assert [p for p in pts if not _in_polygon(list(D.FOOT), *p)] == []
    for _, _, _, z in D.POIS:
        assert z in (D.Z1, D.Z2)
    for _, _, z in D.NPCS:
        assert z == D.Z1
    for cam in D.CAMS:
        assert 0.0 < cam[1][2] <= 100.0 and 0.0 <= cam[2][2] < 100.0


def _flights():
    from kcd_interior import plan_research1_core as core
    west = [(D.W_STAIR[0], D.W_STAIR[1], f[0], f[1], f[2], f[3]) for f in D.W_FLIGHTS]
    e1, e2 = D.E_FLIGHT1, D.E_FLIGHT2
    east = [(e1[0], e1[1], e1[2], e1[3], D.Z1, 2.25), (e2[0], e2[1], e2[2], e2[3], 2.25, D.Z2)]
    return core, west, east


def test_stair_steps_are_climbable():
    core, west, east = _flights()
    for x0, x1, y0, y1, z0, z1 in west + east:
        n = max(1, int(round((z1 - z0) / core.RISE)))
        assert 0.0 < (z1 - z0) / n <= STEP_MAX
        assert abs(y1 - y0) / n >= 0.25
        assert x1 - x0 >= 0.9
    assert west[-1][5] == D.Z2 and east[-1][5] == D.Z2


def test_stair_has_headroom_under_the_slab():
    core, west, east = _flights()
    holes = ((D.W_HOLE, west), (D.E_HOLE, east))
    for (hx0, hy0, hx1, hy1), flights in holes:
        for x0, x1, y0, y1, _z0, _z1 in flights:
            assert hx0 <= x0 and x1 <= hx1 and hy0 <= min(y0, y1) and max(y0, y1) <= hy1
    assert core.Z_UNDER < D.Z2


def test_quest_and_collectible_destinations_exist(built):
    import json
    from pathlib import Path
    data = Path(CAMPUS_JSON).parent.parent.parent / "unity" / "KatsushikaCampusDays" / "Assets" / "Data"
    quest = json.loads((data / "Quests" / "q_sq_lab_tour.json").read_text(encoding="utf-8"))
    targets = {s["target"] for s in quest["steps"] if str(s.get("target", "")).startswith("poi_research1_")}
    coll = json.loads((data / "Collectibles" / "collectibles.json").read_text(encoding="utf-8"))
    items = coll["collectibles"]
    targets |= {i["position"] for i in items
                if i.get("building") == "research1" and str(i.get("position", "")).startswith("poi_")}
    names = {n for n, _ in built.empties}
    assert targets and targets <= names, targets - names

"""第1研究棟のプラン（kcd_interior.plan_research1）: スタブで組み、Empty・看板・カメラ・三角数を確かめる。"""

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
N_SIGNS = 23           # そのうち Empty を持つ板（東のトイレと東の実験室は anchor=False）
TOP = D.Z_SKY + 0.05   # トップライトより上に何も出さない


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

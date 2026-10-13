"""build_interiors.over_budget: 屋内の三角数を棟ごとの上限と合計の両方で見る（DESIGN.md §3.4）。"""

import json
import os

import build_interiors as bi
from conftest import REPO
from kcd_interior import registry

INTERIORS = os.path.join(REPO, "unity", "KatsushikaCampusDays", "Assets", "Models", "Interiors")


def _row(bid, tris, ext_tris=0):
    return {"id": bid, "tris": tris, "ext_tris": ext_tris}


def _at_caps():
    return [_row(bid, cap) for bid, cap in bi.INT_BUDGET_ONE.items()]


def _check(rows):
    return bi.over_budget(rows, sum(r["tris"] for r in rows), sum(r["ext_tris"] for r in rows))


def test_every_building_with_an_interior_has_its_own_cap():
    assert set(bi.INT_BUDGET_ONE) == set(registry.ORDER)


def test_the_caps_fit_in_the_total():
    assert bi.INT_BUDGET == 450000
    assert sum(bi.INT_BUDGET_ONE.values()) <= bi.INT_BUDGET


def test_every_building_at_its_cap_is_fine():
    assert _check(_at_caps()) == []


def test_one_building_over_its_cap_is_named():
    rows = [_row(r["id"], r["tris"] + 1) if r["id"] in ("library", "greenhouse") else r
            for r in _at_caps()]
    cap = bi.INT_BUDGET_ONE

    # 合計は上限の内に置き、棟ごとの上限だけを見る（上限の和は合計にちょうど一致し得る）
    assert bi.over_budget(rows, bi.INT_BUDGET, 0) == [
        "library の屋内 %d > %d" % (cap["library"] + 1, cap["library"]),
        "greenhouse の屋内 %d > %d" % (cap["greenhouse"] + 1, cap["greenhouse"])]


def test_a_building_under_its_cap_can_still_break_the_total():
    rows = [_row("library", 1000)]

    assert bi.over_budget(rows, 450001, 0) == ["屋内の合計 450001 > 450000"]


def test_a_building_without_a_cap_is_rejected():
    assert _check([_row("dorm", 10)]) == ["dorm の屋内の上限が INT_BUDGET_ONE に無い"]


def test_near_view_keeps_its_per_building_and_total_caps():
    rows = [_row("lecture", 1000, 50001)]

    assert bi.over_budget(rows, 1000, 200001) == ["lecture の近景 50001 > 50000", "近景の合計 200001 > 200000"]


def test_library_near_view_is_capped_by_its_spec():
    rows = [_row("library", 1000, 14470), _row("lecture", 1000, 14470)]

    assert bi.over_budget(rows, 2000, 28940) == ["library の近景 14470 > 14469"]


def test_committed_near_views_fit_their_caps():
    """書き出し済みの屋内 JSON の近景（ext_<id> と ext_<id>_trees の合計）が棟の上限に収まる。"""
    over = []
    for fn in sorted(os.listdir(INTERIORS)):
        if not fn.endswith(".json") or fn.startswith("_"):
            continue
        with open(os.path.join(INTERIORS, fn), encoding="utf-8") as f:
            info = json.load(f)
        if "ext" not in info:
            continue   # 近景を入れない棟（寮は build_dorm.py で別に作る）
        bid = fn[:-len(".json")]
        cap = bi.EXT_BUDGET_BY_ID.get(bid, bi.EXT_BUDGET_ONE)
        if info["ext"]["triangles"] > cap:
            over.append("%s %d > %d" % (bid, info["ext"]["triangles"], cap))

    assert over == []

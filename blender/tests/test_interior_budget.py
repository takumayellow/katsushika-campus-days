"""build_interiors.over_budget: 屋内の三角数を棟ごとの上限と合計の両方で見る（DESIGN.md §3.4）。"""

import build_interiors as bi
from kcd_interior import registry


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

    assert _check(rows) == ["library の屋内 80001 > 80000", "greenhouse の屋内 15001 > 15000"]


def test_a_building_under_its_cap_can_still_break_the_total():
    rows = [_row("library", 1000)]

    assert bi.over_budget(rows, 450001, 0) == ["屋内の合計 450001 > 450000"]


def test_a_building_without_a_cap_is_rejected():
    assert _check([_row("dorm", 10)]) == ["dorm の屋内の上限が INT_BUDGET_ONE に無い"]


def test_near_view_keeps_its_per_building_and_total_caps():
    rows = [_row("lecture", 1000, 50001)]

    assert bi.over_budget(rows, 1000, 180001) == ["lecture の近景 50001 > 50000", "近景の合計 180001 > 180000"]

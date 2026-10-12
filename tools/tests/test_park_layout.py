"""tools/park_layout.py: 公園全体図の木の記号 → data/osm/park.json (#168)。

全体図の画像と Overpass の生データはコミットしないので、座標変換・色の判定・木を捨てる理由と、
コミット済みの park.json の形だけを見る。
"""

import json
import os

import pytest

import park_layout as pl

SQUARE = [(0.0, 0.0), (100.0, 0.0), (100.0, 100.0), (0.0, 100.0)]
POND = [(10.0, 10.0), (20.0, 10.0), (20.0, 20.0), (10.0, 20.0)]
HOUSE = [(30.0, 10.0), (40.0, 10.0), (40.0, 20.0), (30.0, 20.0)]
PITCH = [(50.0, 10.0), (70.0, 10.0), (70.0, 30.0), (50.0, 30.0)]
PATH = [(0.0, 50.0), (100.0, 50.0)]


@pytest.mark.parametrize("x,z", [(0.0, 0.0), (-131.0, -136.5), (208.0, 36.0), (-200.0, 150.0)])
def test_to_px_and_to_game_are_inverse(x, z):
    gx, gz = pl.to_game(*pl.to_px(x, z))
    assert gx == pytest.approx(x, abs=1e-9) and gz == pytest.approx(z, abs=1e-9)


def test_map_is_north_up_and_scaled():
    """北（+z）へ行くと画素の y は減り、1 m は約 4.6 px（300 dpi）。"""
    x0, y0 = pl.to_px(0.0, 0.0)
    x1, y1 = pl.to_px(0.0, 10.0)
    assert y1 < y0 and abs(x1 - x0) < 1.0
    assert (y0 - y1) / 10.0 == pytest.approx(pl.GEOREF["s"], rel=1e-3)


@pytest.mark.parametrize("rgb,kind", [
    ((240, 190, 220), "sakura"),
    ((60, 130, 60), "evergreen"),
    ((180, 205, 175), "deciduous"),
    ((200, 230, 190), "deciduous"),
])
def test_classify(rgb, kind):
    assert pl.classify(rgb) == kind


@pytest.mark.parametrize("p,why", [
    ((150.0, 50.0), "outside"),
    ((15.0, 15.0), "water"),
    ((35.0, 15.0), "building"),
    ((60.0, 20.0), "pitch"),
    ((80.0, 51.0), "path"),
    ((80.0, 80.0), None),
])
def test_reject_reason(p, why):
    assert pl.reject_reason(p, "deciduous", [SQUARE], POND, [HOUSE], [PATH], [PITCH]) == why


def test_path_clearance_is_two_metres():
    assert pl.reject_reason((80.0, 52.1), "deciduous", [SQUARE], POND, [], [PATH]) is None
    assert pl.reject_reason((80.0, 51.9), "deciduous", [SQUARE], POND, [], [PATH]) == "path"


def test_labels_drop_green_circles_but_keep_sakura():
    x0, z0, x1, z1 = pl.LABELS[0]
    p = ((x0 + x1) / 2, (z0 + z1) / 2)
    park = [[(x0 - 50, z0 - 50), (x1 + 50, z0 - 50), (x1 + 50, z1 + 50), (x0 - 50, z1 + 50)]]
    far = [(x0 - 40, z0 - 40), (x0 - 39, z0 - 40), (x0 - 39, z0 - 39)]
    assert pl.reject_reason(p, "deciduous", park, far, [], []) == "label"
    assert pl.reject_reason(p, "sakura", park, far, [], []) is None


# ---- コミット済みの park.json ----

@pytest.fixture(scope="module")
def park_json():
    with open(os.path.join(os.path.dirname(pl.OUT), "park.json"), encoding="utf-8") as fp:
        return json.load(fp)


def test_park_json_records_how_it_was_made(park_json):
    meta = park_json["meta"]
    assert meta["issue"] == 168
    assert meta["georef"] == pl.GEOREF
    assert set(meta["dropped"]) <= {"outside", "water", "building", "pitch", "path", "label"}
    assert any("ODbL" in s for s in meta["sources"])


def test_park_json_trees_are_sorted_and_typed(park_json):
    trees = park_json["trees"]
    assert trees == sorted(trees)
    assert {t[2] for t in trees} == {"deciduous", "evergreen", "sakura"}
    assert all(len(t) == 3 for t in trees)


def test_park_json_constants_match_the_tool(park_json):
    assert (park_json["kama"]["x"], park_json["kama"]["z"], park_json["kama"]["r"]) == pl.KAMA
    assert park_json["kama"]["id"] == "bg_%d" % pl.KAMA_WAY
    assert park_json["pergola"]["id"] == "bg_%d" % pl.PERGOLA_WAY
    assert [tuple(p) for p in park_json["play"]] == pl.PLAY_SPOTS
    assert park_json["heights"] == pl.HEIGHTS

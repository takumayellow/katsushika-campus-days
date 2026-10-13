"""髪の地の色と、Unity へ渡す色表 palette.json (#47, #55)。

ゲームの陰は「地の色 × 陰の色」の一色で、リムライトも切ってある。黒髪の地の色を
輪郭線（Unity の輪郭は L* 10）と同じくらい暗くすると、明部も陰も輪郭線と同じ暗さに
なり、ツインテールと後ろ髪の境も房の輪郭も読めない一塊になる。Homura
（mitakihara）の黒髪は参照コマで L* 31.6（#4E494B）だった。黒髪も地の色は L* 28
以上にする。

palette.json は Blender（mats.color_of）が書き、Unity の MaterialLibrary が .mat を
塗る。色を手で直したときに片方だけ変わらないよう、柄の無い材質は両者を突き合わせる。
"""

import json
import os

import pytest

from kcd_chara import mats, params

CHARACTERS = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                          "unity", "KatsushikaCampusDays", "Assets", "Models", "Characters")

MIN_HAIR_LIGHTNESS = 28.0


def _lightness(rgb) -> float:
    """sRGB (0..1) の CIE L*。"""
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    y = 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]
    f = y ** (1 / 3) if y > 0.008856 else 7.787 * y + 16 / 116
    return 116 * f - 16


def test_lightness_matches_reference_values():
    assert _lightness(mats.hexc("#4E494B")) == pytest.approx(31.6, abs=0.1)
    assert _lightness(mats.hexc("#000000")) == pytest.approx(0.0, abs=0.01)
    assert _lightness(mats.hexc("#FFFFFF")) == pytest.approx(100.0, abs=0.01)


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_hair_is_lighter_than_the_outline(name):
    p = params.resolve(name)
    hair = mats.color_of(p, "hair")
    assert _lightness(hair) >= MIN_HAIR_LIGHTNESS, (name, mats._hex8(hair))
    # hair_color は眉の既定色の元にしか使わないが、髪の材質の色と食い違わないようにする
    assert mats._hex8(p["hair_color"]) == mats._hex8(hair), name


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_palette_json_matches_blender_colors(name):
    p = params.resolve(name)
    with open(os.path.join(CHARACTERS, name, "palette.json"), encoding="utf-8") as f:
        doc = json.load(f)
    plain = [e for e in doc["materials"] if "pattern" not in e]
    assert any(e["name"] == "hair" for e in plain), name
    for entry in plain:
        assert entry["hex"] == mats._hex8(mats.color_of(p, entry["name"])), (name, entry["name"])

"""tools/make_ui_font.py が作った画面のフォントが、ゲームの文字を欠かさず持っていることを確かめる (#70)。"""

from __future__ import annotations

import pytest

import make_ui_font

pytest.importorskip("fontTools")
from fontTools.ttLib import TTFont  # noqa: E402


@pytest.fixture(scope="module")
def ui_cmap() -> set[int]:
    return set(TTFont(str(make_ui_font.OUT), lazy=True).getBestCmap())


@pytest.fixture(scope="module")
def source_cmap() -> set[int]:
    return set(TTFont(str(make_ui_font.SRC), lazy=True).getBestCmap())


def test_has_every_character_of_the_game(ui_cmap, source_cmap):
    missing = sorted((make_ui_font.text_codepoints() & source_cmap) - ui_cmap)
    assert not missing, ("画面のフォントに無い字がある。python tools/make_ui_font.py で作り直してコミットする: "
                         + "".join(chr(cp) for cp in missing[:40]))


def test_collects_escapes_osm_names_and_skips_binary_assets(tmp_path):
    assets = tmp_path / "Assets"
    assets.mkdir()
    (assets / "a.json").write_text('{"t": "\\u3042 \\ud83d\\ude00"}', encoding="utf-8")
    (assets / "b.cs").write_text('string s = "\\u3044\\U00020B9F";', encoding="utf-8")
    (assets / "c.unity").write_text('%YAML 1.1\n  m_text: "\\u3046"\n', encoding="utf-8")
    (assets / "d.asset").write_bytes(b"\x00\x01" + "\u3048".encode("utf-8"))
    osm = tmp_path / "campus.json"
    osm.write_text('{"buildings": [{"display": "\\u8449\\u98fe", "id": "x"}]}', encoding="utf-8")

    cps = make_ui_font.text_codepoints(assets, (osm,))

    assert cps == {0x3042, 0x1F600, 0x3044, 0x20B9F, 0x3046, 0x8449, 0x98FE}


def test_osm_names_are_collected():
    assert all(p.is_file() for p in make_ui_font.OSM_JSON)
    assert make_ui_font.text_codepoints(extra_json=make_ui_font.OSM_JSON) >= {ord("館")}


def test_has_ascii_kana_and_jis_kanji(ui_cmap):
    for text in ("Enter 0123456789 ABC xyz", "あいうえお アイウエオ ヴ ｱｲｳ", "葛飾東京理科大学 ① ※ 〜 ～ ー"):
        for ch in text:
            assert ord(ch) in ui_cmap, ch


def test_is_fixed_weight_and_renamed():
    font = TTFont(str(make_ui_font.OUT), lazy=True)
    assert "fvar" not in font and "gvar" not in font
    assert font["OS/2"].usWeightClass == make_ui_font.WEIGHT
    assert font["name"].getDebugName(1) == make_ui_font.FAMILY
    assert "Source" not in font["name"].getDebugName(1)


def test_license_ships_with_the_font():
    text = make_ui_font.OUT_OFL.read_text(encoding="utf-8")
    assert make_ui_font.OUT.name in text
    assert "SIL OPEN FONT LICENSE Version 1.1" in text
    assert "Adobe" in text

"""kcd_interior.imats.compose: 共有の材質と棟ごとの mats_<id>.py を合わせる（DESIGN.md §3.4）。"""

import types

import pytest

from kcd_interior import imats, registry
from kcd_lib import mats as exterior_mats

GREY = ((0.5, 0.5, 0.5), 0.5, 0.0, 1.0)
GLASS = ((0.6, 0.7, 0.7), 0.05, 0.0, 0.3)
GLOW = ((1.0, 0.9, 0.8), 3.0)


def _common(palette=None):
    return (palette or {"wall_white": GREY}, {"glass_interior": GLASS}, {"light_panel": GLOW})


def _mats(palette=None, transparent=None, emissive=None):
    return types.SimpleNamespace(PALETTE=palette or {}, TRANSPARENT=transparent or {},
                                 EMISSIVE=emissive or {})


def _library(**tables):
    return imats.compose(_common(), {"library": _mats(**tables)})


def test_every_building_has_a_mats_file_in_registry_order():
    assert list(imats.BUILDING_MATS) == registry.ORDER
    for bid, mod in imats.BUILDING_MATS.items():
        assert mod.__name__ == "kcd_interior.mats_%s" % bid
        for kind in ("PALETTE", "TRANSPARENT", "EMISSIVE"):
            assert isinstance(getattr(mod, kind), dict), "mats_%s.%s" % (bid, kind)


def test_registered_tables_start_with_every_shared_material():
    for common, merged in ((imats.COMMON_PALETTE, imats.PALETTE),
                           (imats.COMMON_TRANSPARENT, imats.TRANSPARENT),
                           (imats.COMMON_EMISSIVE, imats.EMISSIVE)):
        assert list(merged)[:len(common)] == list(common)
        assert {name: merged[name] for name in common} == common


def test_registered_building_materials_carry_their_building_prefix():
    for name, bid in imats.OWNER.items():
        assert bid in registry.ORDER
        assert name.startswith(bid + "_")
        assert name in imats.PALETTE or name in imats.TRANSPARENT


def test_no_exterior_material_uses_a_building_prefix():
    """Unity は外装の色（CampusColors）を先に引くので、外装に <id>_ の名前があると棟の材質が隠れる。"""
    hidden = [name for name in exterior_mats.PALETTE
              for bid in registry.ORDER if name.startswith(bid + "_")]
    assert hidden == []


def test_building_materials_are_appended_and_owned():
    palette, transparent, emissive, owner = _library(
        palette={"library_lamp": GREY}, transparent={"library_case_glass": GLASS},
        emissive={"library_lamp": GLOW})

    assert list(palette) == ["wall_white", "library_lamp"]
    assert list(transparent) == ["glass_interior", "library_case_glass"]
    assert list(emissive) == ["light_panel", "library_lamp"]
    assert owner == {"library_lamp": "library", "library_case_glass": "library"}


def test_compose_leaves_its_inputs_alone():
    common = _common()
    lib = _mats({"library_lamp": GREY})

    imats.compose(common, {"library": lib})

    assert common == _common()
    assert lib.PALETTE == {"library_lamp": GREY}


def test_a_name_in_both_the_shared_and_a_building_table_is_rejected():
    common = _common({"wall_white": GREY, "library_desk": GREY})

    with pytest.raises(ValueError, match="材質名 library_desk が imats.COMMON_PALETTE と mats_library.PALETTE で重なっている"):
        imats.compose(common, {"library": _mats({"library_desk": GREY})})


def test_a_name_in_both_palette_and_transparent_of_one_building_is_rejected():
    with pytest.raises(ValueError, match="材質名 library_case が mats_library.PALETTE と mats_library.TRANSPARENT で重なっている"):
        _library(palette={"library_case": GREY}, transparent={"library_case": GLASS})


@pytest.mark.parametrize("name", [
    "desk_oak",           # 接頭辞が無い
    "gym_floor",          # ほかの棟の接頭辞
    "library_Desk",       # 大文字（Unity は小文字にして探す）
    "library_desk.001",   # Unity は "." から後ろを捨てる
    "library_desk-oak",
    "library_",           # 接頭辞だけ
    "librarydesk",
])
def test_a_building_material_needs_its_own_prefix(name):
    with pytest.raises(ValueError, match="mats_library.PALETTE の .* は \"library_\" で始まる"):
        _library(palette={name: GREY})


def test_an_emissive_name_needs_the_prefix_too():
    with pytest.raises(ValueError, match="mats_library.EMISSIVE の 'light_panel' は \"library_\" で始まる"):
        _library(emissive={"light_panel": GLOW})


@pytest.mark.parametrize("tables", [
    {"emissive": {"library_lamp": GLOW}},
    {"transparent": {"library_lamp": GLASS}, "emissive": {"library_lamp": GLOW}},
])
def test_an_emissive_material_must_be_opaque_in_the_same_building(tables):
    with pytest.raises(ValueError, match="mats_library.EMISSIVE の library_lamp が mats_library.PALETTE に無い"):
        _library(**tables)


@pytest.mark.parametrize("kind, value, rule", [
    ("palette", ((0.5, 0.5, 0.5), 0.5, 0.0, 0.5), "alpha は 1.0"),
    ("palette", ((0.5, 0.5, 1.2), 0.5, 0.0, 1.0), "alpha は 1.0"),
    ("palette", ([0.5, 0.5, 0.5], 0.5, 0.0, 1.0), "alpha は 1.0"),
    ("palette", ((0.5, 0.5, 0.5), 0.5, 0.0), "alpha は 1.0"),
    ("palette", ((0.5, 0.5, 0.5), 0.5, True, 1.0), "alpha は 1.0"),
    ("transparent", ((0.6, 0.7, 0.7), 0.05, 0.0, 1.0), "alpha は 0 より大きく 1 未満"),
    ("transparent", ((0.6, 0.7, 0.7), 0.05, 0.0, 0.0), "alpha は 0 より大きく 1 未満"),
])
def test_surface_values_follow_the_shared_shape(kind, value, rule):
    with pytest.raises(ValueError, match=rule):
        _library(**{kind: {"library_x": value}})


@pytest.mark.parametrize("value", [
    ((1.0, 0.9, 0.8), 0.0),
    ((1.0, 0.9, 0.8), -1.0),
    ((1.0, 0.9), 3.0),
    ((1.0, 0.9, 0.8), True),
    ((1.0, 0.9, 0.8),),
])
def test_emission_values_need_a_colour_and_a_positive_strength(value):
    with pytest.raises(ValueError, match="strength は正の数"):
        _library(palette={"library_lamp": GREY}, emissive={"library_lamp": value})


def test_shared_values_are_checked_too():
    with pytest.raises(ValueError, match="imats.COMMON_PALETTE の wall_white"):
        imats.compose(_common({"wall_white": ((0.5, 0.5, 0.5), 0.5, 0.0, 0.9)}), {})

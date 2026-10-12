"""tools/interior_palette.py: 屋内の材質（Blender の imats）を Unity の InteriorPalette.cs に写す。"""

import sys

import pytest

import interior_palette as ip


def _palette():
    return ip.Palette(
        surfaces={"floor_tile_white": ((0.855, 0.850, 0.830), 0.30, 0.02, 1.0),
                  "metal_dark": ((0.150, 0.155, 0.160), 0.42, 0.75, 1.0)},
        transparent={"glass_interior": ((0.620, 0.720, 0.740), 0.05, 0.0, 0.22)},
        emissive={"light_panel": ((1.00, 0.975, 0.920), 4.5)},
    )


# _palette() と同じ値を持つ kcd_interior/imats.py（main を通しで試すときに使う）
_IMATS = '''PALETTE = {
    "floor_tile_white": ((0.855, 0.850, 0.830), 0.30, 0.02, 1.0),
    "metal_dark": ((0.150, 0.155, 0.160), 0.42, 0.75, 1.0),
}
TRANSPARENT = {"glass_interior": ((0.620, 0.720, 0.740), 0.05, 0.0, 0.22)}
EMISSIVE = {"light_panel": ((1.00, 0.975, 0.920), 4.5)}
'''


_SOURCE = """namespace KCD.Editor
{
    public static class InteriorPalette
    {
        private static readonly Dictionary<string, Surface> Surfaces = new Dictionary<string, Surface>
        {
            { "old", new Surface("000000", 0.00f, 0.00f) }
        };

        private static readonly Dictionary<string, KeyValuePair<string, float>> Glass = new Dictionary<string, KeyValuePair<string, float>>
        {
        };

        private static readonly Dictionary<string, KeyValuePair<Color, float>> Emissive = new Dictionary<string, KeyValuePair<Color, float>>
        {
            { "old_light", new KeyValuePair<Color, float>(new Color(0.000f, 0.000f, 0.000f), 0.00f) }
        };

        public static bool TryGetSurface(string name, out Surface surface)
        {
            return Surfaces.TryGetValue(name, out surface);
        }
    }
}
"""


def _fake_blender_dir(tmp_path):
    package = tmp_path / "blender" / "kcd_interior"
    package.mkdir(parents=True)
    (package / "__init__.py").write_text("", encoding="utf-8")
    (package / "imats.py").write_text(_IMATS, encoding="utf-8")
    return tmp_path / "blender"


def _cli_args(tmp_path, source_bytes):
    cs = tmp_path / "InteriorPalette.cs"
    cs.write_bytes(source_bytes)
    return cs, ["--blender-dir", str(_fake_blender_dir(tmp_path)), "--cs", str(cs)]


def test_linear_to_srgb_endpoints_and_knee():
    assert ip.linear_to_srgb_byte(0.0) == 0
    assert ip.linear_to_srgb_byte(1.0) == 255
    # 0.0031308 以下は 12.92 倍の直線
    assert ip.linear_to_srgb_byte(0.003) == 10
    # 範囲外は切り詰める
    assert ip.linear_to_srgb_byte(-0.5) == 0
    assert ip.linear_to_srgb_byte(1.5) == 255


def test_hex_matches_values_already_in_unity():
    assert ip.srgb_hex((0.855, 0.850, 0.830)) == "EEEDEB"
    assert ip.srgb_hex((0.000, 0.560, 0.360)) == "00C5A2"
    assert ip.srgb_hex((0.620, 0.720, 0.740)) == "CEDDDF"


def test_surface_line_uses_smoothness_not_roughness():
    line = ip.surface_lines({"metal_dark": ((0.150, 0.155, 0.160), 0.42, 0.75, 1.0)})[0]
    assert line == '{ "metal_dark", new Surface("6C6E6F", 0.58f, 0.75f) }'


def test_material_names_must_be_identifiers():
    with pytest.raises(ValueError, match="材質名"):
        ip.surface_lines({'bad"name': ((0.5, 0.5, 0.5), 0.5, 0.0, 1.0)})


def test_render_replaces_only_the_dictionary_bodies():
    out = ip.render(_SOURCE, _palette())

    assert '"old"' not in out and '"old_light"' not in out
    assert '            { "floor_tile_white", new Surface("EEEDEB", 0.70f, 0.02f) },\n' in out
    assert '            { "metal_dark", new Surface("6C6E6F", 0.58f, 0.75f) }\n        };' in out
    assert '            { "glass_interior", new KeyValuePair<string, float>("CEDDDF", 0.22f) }\n        };' in out
    assert ('            { "light_panel", new KeyValuePair<Color, float>(new Color(1.000f, 0.975f, 0.920f), 4.50f) }'
            in out)
    # 辞書の外はそのまま
    assert out.endswith(_SOURCE[_SOURCE.index("        public static bool TryGetSurface"):])
    assert ip.render(out, _palette()) == out


def test_render_accepts_brace_on_the_declaration_line_and_loose_spacing():
    source = _SOURCE.replace(
        "Dictionary<string, Surface> Surfaces = new Dictionary<string, Surface>\n        {",
        "Dictionary<string,Surface> Surfaces = new Dictionary<string,Surface> {")

    out = ip.render(source, _palette())

    assert ('new Dictionary<string,Surface> {\n'
            '            { "floor_tile_white", new Surface("EEEDEB", 0.70f, 0.02f) },\n') in out
    assert ip.check(out, _palette()) == []


def test_diff_lists_missing_changed_and_stale_entries():
    source = ip.render(_SOURCE, _palette())
    base = _palette()
    palette = base._replace(
        surfaces={**base.surfaces,
                  "floor_tile_white": ((0.855, 0.850, 0.830), 0.50, 0.02, 1.0),
                  "new_one": ((0.5, 0.5, 0.5), 0.5, 0.0, 1.0)},
        emissive={},
    )

    problems = ip.diff(source, palette)

    assert any(p.startswith("Surfaces: floor_tile_white が違う") for p in problems)
    assert any(p.startswith("Surfaces: new_one が無い") for p in problems)
    assert any(p == "Emissive: light_panel は Blender 側に無い" for p in problems)
    assert len(problems) == 3


def test_check_catches_a_new_order_that_diff_cannot_see():
    source = ip.render(_SOURCE, _palette())
    base = _palette()
    reordered = base._replace(surfaces=dict(reversed(list(base.surfaces.items()))))

    assert ip.diff(source, reordered) == []
    assert ip.check(source, reordered) == ["材質の値は同じで、並び順か書式が生成結果と違う"]
    assert ip.check(source, base) == []


def test_check_waits_for_building_materials_until_they_are_copied():
    base = _palette()
    palette = base._replace(
        surfaces={**base.surfaces, "library_desk_oak": ((0.40, 0.25, 0.12), 0.55, 0.0, 1.0)},
        owners={"library_desk_oak": "library"},
    )
    source = ip.render(_SOURCE, base)

    # 建物の材質がまだ写されていないだけなら、optional に渡すと通る（optional 無しは厳しいまま）
    assert ip.check(source, palette, optional=palette.owners) == []
    assert any(p.startswith("Surfaces: library_desk_oak が無い") for p in ip.check(source, palette))

    # 写した後は値まで比べる
    copied = ip.render(_SOURCE, palette)
    assert ip.check(copied, palette, optional=palette.owners) == []
    changed = palette._replace(
        surfaces={**palette.surfaces, "library_desk_oak": ((0.40, 0.25, 0.12), 0.80, 0.0, 1.0)})
    assert any(p.startswith("Surfaces: library_desk_oak が違う")
               for p in ip.check(copied, changed, optional=palette.owners))

    # 共有の材質は optional に入らないので、欠けていれば落とす
    assert ip.check(ip.render(_SOURCE, base._replace(emissive={})), palette, optional=palette.owners) != []


def test_load_palette_reads_the_building_owners(tmp_path):
    blender = _fake_blender_dir(tmp_path)
    imats = blender / "kcd_interior" / "imats.py"
    imats.write_text(_IMATS + 'OWNER = {"metal_dark": "lab1"}\n', encoding="utf-8")

    assert dict(ip.load_palette(blender).owners) == {"metal_dark": "lab1"}


def test_main_check_reports_drift_without_writing(tmp_path, capsys):
    cs, args = _cli_args(tmp_path, _SOURCE.encode("utf-8"))

    assert ip.main(["--check"] + args) == 1

    assert cs.read_bytes() == _SOURCE.encode("utf-8")
    assert "InteriorPalette.cs: 6 件のずれ" in capsys.readouterr().out


def test_main_rewrites_and_keeps_crlf_and_bom(tmp_path, capsys):
    cs, args = _cli_args(tmp_path, b"\xef\xbb\xbf" + _SOURCE.replace("\n", "\r\n").encode("utf-8"))

    assert ip.main(args) == 0
    data = cs.read_bytes()
    assert data.startswith(b"\xef\xbb\xbf")
    written = data[3:].decode("utf-8")
    assert written.replace("\r\n", "\n") == ip.render(_SOURCE, _palette())
    assert "\n" not in written.replace("\r\n", "")

    assert ip.main(args) == 0
    assert ip.main(["--check"] + args) == 0
    assert "InteriorPalette.cs は最新" in capsys.readouterr().out


def test_main_reports_an_unreadable_file_with_exit_code_2(tmp_path, capsys):
    broken = _SOURCE.replace("KeyValuePair<string, float>> Glass", "KeyValuePair<string, float>> Glasses")
    cs, args = _cli_args(tmp_path, broken.encode("utf-8"))

    assert ip.main(["--check"] + args) == 2
    assert ip.main(args) == 2

    assert cs.read_bytes() == broken.encode("utf-8")
    assert "InteriorPalette.cs を読めない: Glass の宣言が無い" in capsys.readouterr().out


def test_main_blames_blender_for_a_bad_material_name(tmp_path, capsys):
    cs, args = _cli_args(tmp_path, _SOURCE.encode("utf-8"))
    imats = tmp_path / "blender" / "kcd_interior" / "imats.py"
    imats.write_text(_IMATS.replace('"metal_dark"', '"metal-dark"'), encoding="utf-8")

    assert ip.main(args) == 2

    assert cs.read_bytes() == _SOURCE.encode("utf-8")
    out = capsys.readouterr().out
    assert "Blender 側の材質を写せない: 材質名に使えない文字がある: 'metal-dark'" in out
    assert "を読めない" not in out


def test_interior_palette_cs_is_in_sync_with_blender():
    """共有の材質と、写し済みの建物の材質が InteriorPalette.cs と一致する。

    建物の mats_<id>.py に足した材質は、統合ブランチで python tools/interior_palette.py を走らせて写す。
    それまでは InteriorPalette.cs に無くてもよい。
    """
    source = ip.PALETTE_CS.read_text(encoding="utf-8-sig")
    palette = ip.load_palette()
    problems = ip.check(source, palette, optional=palette.owners)
    assert not problems, "InteriorPalette.cs が Blender の材質とずれている（python tools/interior_palette.py）:\n" + "\n".join(problems)


def test_load_palette_does_not_leave_the_bpy_stub_behind():
    before = sys.modules.get("bpy")
    ip.load_palette()
    assert sys.modules.get("bpy") is before

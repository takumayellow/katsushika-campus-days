"""屋内の材質（blender/kcd_interior の PALETTE / TRANSPARENT / EMISSIVE）を Unity の InteriorPalette.cs に写す。

Blender 側の値が正で、InteriorPalette.cs の 3 つの辞書の中身はこのスクリプトが書く。
- 色: 線形 RGB → sRGB の 16 進（6 桁、大文字）
- 不透明: Surface("HEX", smoothness, metallic)。smoothness = 1 - roughness
- 透明: KeyValuePair<string, float>("HEX", alpha)
- 発光: KeyValuePair<Color, float>(new Color(r, g, b), strength)。色は線形のまま

使い方:
    python tools/interior_palette.py           # InteriorPalette.cs を書き直す
    python tools/interior_palette.py --check   # 生成結果と 1 字でも違えば内訳を出して終了コード 1

InteriorPalette.cs の辞書の形が読めないときは終了コード 2。

建物の mats_<id>.py に足した材質は、建物の PR では InteriorPalette.cs に写さない（棟ごとに書き直すと
同じ辞書の末尾で衝突する）。統合ブランチでこのスクリプトを 1 回走らせて写す。pytest の一致テストは、
InteriorPalette.cs にまだ無い建物の材質だけを見逃す（共有の材質と、写した後の値の食い違いは落とす）。
"""

from __future__ import annotations

import argparse
import codecs
import importlib
import re
import sys
import types
from collections.abc import Collection, Mapping, Sequence
from pathlib import Path
from types import MappingProxyType
from typing import NamedTuple

REPO = Path(__file__).resolve().parents[1]
BLENDER_DIR = REPO / "blender"
PALETTE_CS = REPO / "unity" / "KatsushikaCampusDays" / "Assets" / "Scripts" / "Editor" / "InteriorPalette.cs"

Rgb = tuple[float, float, float]


class Palette(NamedTuple):
    """imats が合成した 3 つの辞書（挿入順を保つ）と、建物の材質の持ち主（材質名 -> 建物 ID）。"""

    surfaces: Mapping[str, tuple[Rgb, float, float, float]]
    transparent: Mapping[str, tuple[Rgb, float, float, float]]
    emissive: Mapping[str, tuple[Rgb, float]]
    owners: Mapping[str, str] = MappingProxyType({})


def linear_to_srgb_byte(c: float) -> int:
    """線形の 0..1 を sRGB の 0..255 にする（IEC 61966-2-1）。"""
    c = min(max(float(c), 0.0), 1.0)
    s = c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1.0 / 2.4) - 0.055
    return int(s * 255.0 + 0.5)


def srgb_hex(rgb: Sequence[float]) -> str:
    return "".join("%02X" % linear_to_srgb_byte(c) for c in rgb[:3])


def _is_imats_module(name: str) -> bool:
    return name in ("bpy", "kcd_interior") or name.startswith("kcd_interior.")


def load_palette(blender_dir: Path = BLENDER_DIR) -> Palette:
    """bpy を空のモジュールに差し替えて kcd_interior.imats を読み込み、合成後の辞書を返す。"""
    saved_modules = {name: module for name, module in sys.modules.items() if _is_imats_module(name)}
    saved_path = list(sys.path)
    for name in saved_modules:
        del sys.modules[name]
    sys.modules["bpy"] = types.ModuleType("bpy")
    sys.path.insert(0, str(blender_dir))
    try:
        imats = importlib.import_module("kcd_interior.imats")
        return Palette(dict(imats.PALETTE), dict(imats.TRANSPARENT), dict(imats.EMISSIVE),
                       dict(getattr(imats, "OWNER", {})))
    finally:
        sys.path[:] = saved_path
        for name in [n for n in sys.modules if _is_imats_module(n)]:
            del sys.modules[name]
        sys.modules.update(saved_modules)


def _cs_float(value: float, digits: int) -> str:
    """C# の float リテラル（例 0.58f）。"""
    return "%.*ff" % (digits, value)


# 材質名は C# の文字列リテラルにそのまま入れるので、英数字と _ に限る。
_MATERIAL_NAME = re.compile(r"[A-Za-z0-9_]+")


class BadMaterialName(ValueError):
    """Blender 側の材質名が C# の文字列リテラルに書けない。"""


def _checked_names(names: Mapping[str, object]) -> Mapping[str, object]:
    bad = [name for name in names if not _MATERIAL_NAME.fullmatch(name)]
    if bad:
        raise BadMaterialName("材質名に使えない文字がある: " + ", ".join(repr(name) for name in bad))
    return names


def surface_lines(surfaces: Mapping[str, tuple[Rgb, float, float, float]]) -> list[str]:
    return [
        '{ "%s", new Surface("%s", %s, %s) }' % (name, srgb_hex(rgb), _cs_float(1.0 - rough, 2), _cs_float(metal, 2))
        for name, (rgb, rough, metal, _alpha) in _checked_names(surfaces).items()
    ]


def glass_lines(transparent: Mapping[str, tuple[Rgb, float, float, float]]) -> list[str]:
    return [
        '{ "%s", new KeyValuePair<string, float>("%s", %s) }' % (name, srgb_hex(rgb), _cs_float(alpha, 2))
        for name, (rgb, _rough, _metal, alpha) in _checked_names(transparent).items()
    ]


def emissive_lines(emissive: Mapping[str, tuple[Rgb, float]]) -> list[str]:
    return [
        '{ "%s", new KeyValuePair<Color, float>(new Color(%s, %s, %s), %s) }'
        % (name, _cs_float(rgb[0], 3), _cs_float(rgb[1], 3), _cs_float(rgb[2], 3), _cs_float(strength, 2))
        for name, (rgb, strength) in _checked_names(emissive).items()
    ]


def _declaration(text: str) -> re.Pattern[str]:
    """宣言の字句の間の空白の有無・量を問わない正規表現。"""
    return re.compile(r"\s*".join(re.escape(token) for token in re.findall(r"\w+|[^\w\s]", text)))


# InteriorPalette.cs の辞書の宣言。中身は続く { から対応する } までの間。
_BLOCKS = {
    "Surfaces": _declaration("Dictionary<string, Surface> Surfaces = new Dictionary<string, Surface>"),
    "Glass": _declaration("Dictionary<string, KeyValuePair<string, float>> Glass"
                          " = new Dictionary<string, KeyValuePair<string, float>>"),
    "Emissive": _declaration("Dictionary<string, KeyValuePair<Color, float>> Emissive"
                             " = new Dictionary<string, KeyValuePair<Color, float>>"),
}


def _block_span(source: str, key: str) -> tuple[int, int, str]:
    """辞書の中身（{ の次の行から } の行の手前まで）の範囲と、中身の字下げを返す。

    改行は LF に揃えてから渡す（main が読み込み時に揃える）。{ は宣言と同じ行でも次の行でもよい。
    """
    declaration = _BLOCKS[key].search(source)
    if declaration is None:
        raise ValueError(key + " の宣言が無い")
    opening = source.find("{", declaration.end())
    if opening < 0 or source[declaration.end():opening].strip():
        raise ValueError(key + " の宣言の後ろに { が無い")
    first_line = source.find("\n", opening) + 1
    if first_line == 0 or source[opening + 1:first_line].strip():
        raise ValueError(key + " の { の後ろは改行にする")
    depth = 0
    for at in range(opening, len(source)):
        depth += {"{": 1, "}": -1}.get(source[at], 0)
        if depth == 0:
            break
    else:
        raise ValueError(key + " の辞書が閉じていない")
    closing_line = source.rfind("\n", 0, at) + 1
    indent = source[closing_line:at]
    if indent.strip():
        raise ValueError(key + " の } は行の頭に置く")
    return first_line, closing_line, indent + "    "


def render(source: str, palette: Palette) -> str:
    """source の 3 つの辞書の中身を palette で置き換えた文字列を返す。"""
    bodies = {
        "Surfaces": surface_lines(palette.surfaces),
        "Glass": glass_lines(palette.transparent),
        "Emissive": emissive_lines(palette.emissive),
    }
    for key in _BLOCKS:
        start, end, indent = _block_span(source, key)
        lines = bodies[key]
        body = "".join(indent + line + ("\n" if i == len(lines) - 1 else ",\n") for i, line in enumerate(lines))
        source = source[:start] + body + source[end:]
    return source


def current_entries(source: str) -> dict[str, dict[str, str]]:
    """InteriorPalette.cs の辞書ごとの {名前: 行} を返す。"""
    result: dict[str, dict[str, str]] = {}
    for key in _BLOCKS:
        start, end, _indent = _block_span(source, key)
        entries = {}
        for line in source[start:end].splitlines():
            line = line.strip().rstrip(",")
            name = re.match(r'\{ "([^"]+)"', line)
            if name:
                entries[name.group(1)] = line
        result[key] = entries
    return result


def diff(source: str, palette: Palette) -> list[str]:
    """InteriorPalette.cs と palette の食い違いを材質ごとに返す（並び順と書式は見ない）。"""
    expected = current_entries(render(source, palette))
    actual = current_entries(source)
    problems = []
    for key in _BLOCKS:
        for name, line in expected[key].items():
            have = actual[key].get(name)
            if have is None:
                problems.append("%s: %s が無い → %s" % (key, name, line))
            elif have != line:
                problems.append("%s: %s が違う\n    今: %s\n    正: %s" % (key, name, have, line))
        for name in actual[key]:
            if name not in expected[key]:
                problems.append("%s: %s は Blender 側に無い" % (key, name))
    return problems


def _present_only(source: str, palette: Palette, optional: Collection[str]) -> Palette:
    """optional の材質のうち source の辞書にまだ無いものを palette から外す。"""
    present = current_entries(source)

    def keep(key: str, table: Mapping[str, tuple]) -> dict[str, tuple]:
        return {name: value for name, value in table.items() if name not in optional or name in present[key]}

    return palette._replace(surfaces=keep("Surfaces", palette.surfaces),
                            transparent=keep("Glass", palette.transparent),
                            emissive=keep("Emissive", palette.emissive))


def check(source: str, palette: Palette, optional: Collection[str] = ()) -> list[str]:
    """source が生成結果と 1 字でも違えばその内訳を返す（同じなら空）。

    optional に挙げた材質は、source にまだ無ければ比べない（あれば値まで比べる）。
    """
    if optional:
        palette = _present_only(source, palette, optional)
    if render(source, palette) == source:
        return []
    return diff(source, palette) or ["材質の値は同じで、並び順か書式が生成結果と違う"]


def main(argv: Sequence[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="書き換えず、ずれを表示して終了コードで返す")
    parser.add_argument("--blender-dir", type=Path, default=BLENDER_DIR)
    parser.add_argument("--cs", type=Path, default=PALETTE_CS)
    args = parser.parse_args(argv)

    palette = load_palette(args.blender_dir)
    data = args.cs.read_bytes()
    raw = data.decode("utf-8-sig")
    source = raw.replace("\r\n", "\n")
    try:
        if args.check:
            problems = check(source, palette)
        else:
            updated = render(source, palette)
    except BadMaterialName as error:
        print("Blender 側の材質を写せない: %s" % error)
        return 2
    except ValueError as error:
        print("%s を読めない: %s" % (args.cs.name, error))
        return 2

    if args.check:
        for problem in problems:
            print(problem)
        print("%s: %d 件のずれ" % (args.cs.name, len(problems)))
        return 1 if problems else 0

    if updated == source:
        print("%s は最新" % args.cs.name)
        return 0
    newline = "\r\n" if "\r\n" in raw else "\n"
    bom = codecs.BOM_UTF8 if data.startswith(codecs.BOM_UTF8) else b""
    args.cs.write_bytes(bom + updated.replace("\n", newline).encode("utf-8"))
    print("%s を書き直した（不透明 %d・透明 %d・発光 %d）"
          % (args.cs.name, len(palette.surfaces), len(palette.transparent), len(palette.emissive)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

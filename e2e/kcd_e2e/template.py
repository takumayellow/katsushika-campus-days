"""WebGL テンプレート（Assets/WebGLTemplates/KCD）を Unity の代わりに展開する (#102)。

Unity はビルドのときに index.html の {{{ 式 }}} を値に置き換え、#if / #else / #endif の行で
中身を取捨する。テンプレートのスクリプト（読み込みの進み具合・失敗の知らせ方）を Unity の
ビルド無しにブラウザで動かして確かめるため、その規則のうちこのテンプレートが使う分だけを真似る。
知らない式や指令が出てきたら例外にして、テンプレートの変更に気付けるようにする。
"""

from __future__ import annotations

import json
import re
import shutil
from pathlib import Path

TEMPLATE_DIR = (Path(__file__).resolve().parents[2] / "unity" / "KatsushikaCampusDays" / "Assets"
                / "WebGLTemplates" / "KCD")

# 偽のビルドのファイル名。ローダーだけはテストが中身を差し替えて配る。
LOADER_FILENAME = "kcd.loader.js"

# Unity 6000.6 が KCD の設定（wasm・Gzip + 復元フォールバック・段階読み込みなし）で渡す値に近いもの。
DEFAULT_VARIABLES: dict[str, object] = {
    "LOADER_FILENAME": LOADER_FILENAME,
    "DATA_FILENAME": "kcd.data.unityweb",
    "FRAMEWORK_FILENAME": "kcd.framework.js.unityweb",
    "CODE_FILENAME": "kcd.wasm.unityweb",
    "SYMBOLS_FILENAME": "",
    "BACKGROUND_FILENAME": "",
    "PROGRESSIVE_ASSET_LOADING": False,
    "PRIMARY_DATA_FILES": [],
    "SECONDARY_DATA_FILES": [],
    "USE_WASM": True,
    "WIDTH": 960,
    "HEIGHT": 600,
    "COMPANY_NAME": "DefaultCompany",
    "PRODUCT_NAME": "葛飾キャンパスデイズ",
    "PRODUCT_VERSION": "0.1",
}

_SLOT = re.compile(r"\{\{\{\s*(.*?)\s*\}\}\}")
_NAME = re.compile(r"[A-Z][A-Z0-9_]*")
_STRINGIFY = re.compile(r"JSON\.stringify\(\s*([A-Z][A-Z0-9_]*)\s*\)")
# 行頭（空白は可）の #if 式 / #else / #endif。CSS の #unity-... などは指令にしない。
_DIRECTIVE = re.compile(r"^\s*#(if|else|endif)(?=\s|$)\s*(.*?)\s*$")


def _lookup(name: str, variables: dict[str, object]) -> object:
    if name not in variables:
        raise KeyError(f"テンプレートの変数に値が無い: {name}")
    return variables[name]


def _js_truthy(value: object) -> bool:
    """JavaScript の真偽。空の配列やオブジェクトは真（Python の bool とは違う）。"""
    if value is None or value is False:
        return False
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return value != 0 and value == value  # NaN は偽
    if isinstance(value, str):
        return value != ""
    return True


def _as_js_text(value: object) -> str:
    """{{{ 名前 }}} に入る文字列。JavaScript の String(value) と同じ。"""
    if isinstance(value, str):
        return value
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    raise TypeError(f"文字列にできない値: {value!r}")


def evaluate_slot(expression: str, variables: dict[str, object]) -> str:
    """{{{ 式 }}} の中身を置き換える文字列にする。使えるのは「名前」と「JSON.stringify(名前)」。"""
    if _NAME.fullmatch(expression):
        return _as_js_text(_lookup(expression, variables))
    match = _STRINGIFY.fullmatch(expression)
    if match:
        return json.dumps(_lookup(match.group(1), variables), ensure_ascii=False)
    raise ValueError(f"展開できない式（kcd_e2e/template.py に足す）: {expression}")


def evaluate_condition(expression: str, variables: dict[str, object]) -> bool:
    """#if の条件。このテンプレートは名前 1 つだけを使う。"""
    if not _NAME.fullmatch(expression):
        raise ValueError(f"評価できない #if の条件（kcd_e2e/template.py に足す）: {expression}")
    return _js_truthy(_lookup(expression, variables))


def render(source: str, variables: dict[str, object] | None = None) -> str:
    """テンプレートの文字列を、Unity がビルドで書き出すのと同じ形の HTML にする。"""
    values = dict(DEFAULT_VARIABLES if variables is None else variables)
    # 開いている #if ごとに [外側が有効か, この枝が有効か, #else を見たか]。
    stack: list[list[bool]] = []
    out: list[str] = []
    for number, line in enumerate(source.splitlines(keepends=True), start=1):
        directive = _DIRECTIVE.match(line)
        active = all(frame[1] for frame in stack)
        if directive:
            kind, argument = directive.groups()
            if kind == "if":
                stack.append([active, active and evaluate_condition(argument, values), False])
            elif not stack:
                raise ValueError(f"{number} 行目: 対応する #if の無い #{kind}")
            elif kind == "else":
                frame = stack[-1]
                if frame[2]:
                    raise ValueError(f"{number} 行目: #else が 2 つある")
                frame[1] = frame[0] and not frame[1]
                frame[2] = True
            else:
                stack.pop()
            continue
        if active:
            out.append(_SLOT.sub(lambda m: evaluate_slot(m.group(1), values), line))
    if stack:
        raise ValueError("#endif が足りない")
    return "".join(out)


def write_template_build(out_dir: Path, variables: dict[str, object] | None = None,
                         template_dir: Path = TEMPLATE_DIR) -> Path:
    """テンプレートを展開した index.html と TemplateData を out_dir に書き、out_dir を返す。

    Build/ は空のまま。ローダーはテスト側が Playwright の route で配る（配らなければ 404）。
    """
    out_dir = Path(out_dir)
    (out_dir / "Build").mkdir(parents=True, exist_ok=True)
    html = render((template_dir / "index.html").read_text(encoding="utf-8"), variables)
    (out_dir / "index.html").write_text(html, encoding="utf-8")
    shutil.copytree(template_dir / "TemplateData", out_dir / "TemplateData",
                    ignore=shutil.ignore_patterns("*.meta"), dirs_exist_ok=True)
    return out_dir

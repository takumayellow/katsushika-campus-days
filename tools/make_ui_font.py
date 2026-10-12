"""画面の文字（TextMeshPro のフォントアセット KCD_JP）に使うフォント
unity/KatsushikaCampusDays/Assets/Fonts/KCDUIJP-Thin.ttf を作る (#70)。

ビルドでは走らない。画面に出す文字を足して tools/tests/test_make_ui_font.py が落ちたら、開発者が手で走らせ、
出来たファイルをコミットする。

元はリポジトリの Noto Sans JP（Assets/Fonts/NotoSansJP-VF.ttf、可変フォント 9.6 MB）。KCD_JP は動的アトラスで、
元のフォントがそのまま WebGL の data に入る（gzip 後に約 5.6 MiB）。そこで:
- 太さの軸 wght を WEIGHT（100、Thin）に固定する。Unity の FontEngine は可変フォントを既定の太さ（wght 100）で
  描くので、画面の字形と字幅は変わらない（2026-10-12 に FontEngine の advance と幅を比べて確かめた）。
  可変の差分の表（gvar / HVAR）が無くなる
- 文字を ASCII・Latin-1・Windows-31J（JIS X 0208 全部と NEC / IBM 拡張）と、Assets の中の文字に絞る
- 組版の表（GSUB / GPOS / GDEF / BASE）は残す。TextMeshPro が詰めと置き換えに使う
- 名前を "KCD UI JP" に変える。SIL Open Font License 1.1 の改変版なので予約名（'Source'）を名前に
  使わない。著作権（nameID 0）とライセンス（13 / 14）の欄は元のまま残す

フォントと一緒に OFL の本文 Assets/StreamingAssets/Licenses/KCDUIJP-OFL.txt を置く（このスクリプトが書く）。

使い方:
    pip install fonttools
    python tools/make_ui_font.py

2026-10-12 に Python 3.10 / fontTools 4.63.0 で作った。
"""

from __future__ import annotations

import argparse
import hashlib
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "unity" / "KatsushikaCampusDays"
ASSETS = PROJECT / "Assets"
SRC = ASSETS / "Fonts" / "NotoSansJP-VF.ttf"
SRC_OFL = ASSETS / "StreamingAssets" / "Licenses" / "NotoSansJP-OFL.txt"
OUT = ASSETS / "Fonts" / "KCDUIJP-Thin.ttf"
OUT_OFL = ASSETS / "StreamingAssets" / "Licenses" / "KCDUIJP-OFL.txt"

WEIGHT = 100
FAMILY = "KCD UI JP"
STYLE = "Thin"
PS_NAME = "KCDUIJP-Thin"
DESCRIPTION = ("Modified Version of Noto Sans JP 2.04 for the screen text of Katsushika Campus Days: "
               "fixed at wght %d, subset to Windows-31J and the characters used in the game. "
               "Made with tools/make_ui_font.py." % WEIGHT)
# 残す名前の欄（それ以外は落とす）。0 = 著作権, 5 = 版, 13 = ライセンス, 14 = ライセンスの URL
KEEP_NAME_IDS = (0, 5, 13, 14)
# 作り直しても head の日付でファイルが変わらないように固定する（2026-10-12 00:00 UTC。1904 年起点の秒）
TIMESTAMP = 3874608000

# 画面に出る文字が書かれうるファイル。台詞・ローカライズは JSON、定型文は C#、
# シーン・プレハブ・アセットの TextMeshPro の本文は YAML（ASCII 以外は \uXXXX）
TEXT_SUFFIXES = (".json", ".cs", ".txt", ".csv", ".unity", ".prefab", ".asset")
_YAML_ESCAPE = re.compile(r"\\u([0-9a-fA-F]{4})")


def _windows31j() -> set[int]:
    out = set()
    for cp in range(0x80, 0x10000):
        if 0xD800 <= cp <= 0xF8FF:  # サロゲートと私用領域
            continue
        try:
            chr(cp).encode("cp932")
        except UnicodeEncodeError:
            continue
        out.add(cp)
    return out


def text_codepoints(assets: Path = ASSETS) -> set[int]:
    """Assets の中のテキストに出てくる ASCII 以外の文字（制御文字を除く）。"""
    cps = set()
    for path in sorted(assets.rglob("*")):
        if path.suffix not in TEXT_SUFFIXES or not path.is_file():
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        cps.update(ord(c) for c in text if ord(c) > 0x7E)
        if path.suffix in (".unity", ".prefab", ".asset"):
            cps.update(int(h, 16) for h in _YAML_ESCAPE.findall(text))
    return {cp for cp in cps if cp >= 0xA0 and not 0xD800 <= cp <= 0xDFFF}


def codepoints(assets: Path = ASSETS) -> set[int]:
    """フォントに残す文字の候補。元のフォントに無い字は後で落ちる。"""
    cps = set(range(0x20, 0x7F)) | set(range(0xA0, 0x100))
    return cps | _windows31j() | text_codepoints(assets)


def build(src: Path = SRC, out: Path = OUT) -> dict:
    from fontTools import subset
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer

    font = TTFont(str(src))
    have = set(font.getBestCmap())
    want = sorted(codepoints() & have)

    font = instancer.instantiateVariableFont(font, {"wght": WEIGHT})

    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_languages = [0x0409]
    opts.name_legacy = False
    opts.notdef_outline = True
    opts.glyph_names = False
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=want)
    sub.subset(font)
    if "STAT" in font:
        del font["STAT"]

    name = font["name"]
    kept = {i: name.getDebugName(i) for i in KEEP_NAME_IDS}
    name.names = []
    for i, text in kept.items():
        if text:
            name.setName(text, i, 3, 1, 0x409)
    for i, text in ((1, FAMILY), (2, STYLE), (3, "2.04;KCD;%s" % PS_NAME),
                    (4, "%s %s" % (FAMILY, STYLE)), (6, PS_NAME), (10, DESCRIPTION)):
        name.setName(text, i, 3, 1, 0x409)
    font["OS/2"].usWeightClass = WEIGHT
    font["head"].created = TIMESTAMP
    font["head"].modified = TIMESTAMP
    font.recalcTimestamp = False

    out.parent.mkdir(parents=True, exist_ok=True)
    font.save(str(out))
    data = out.read_bytes()
    return {"path": out, "chars": len(want), "glyphs": len(font.getGlyphOrder()),
            "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def write_license(src_ofl: Path = SRC_OFL, out: Path = OUT_OFL) -> Path:
    text = src_ofl.read_text(encoding="utf-8")
    start = text.index("This Font Software is licensed")
    head = ("%s (%s)\n"
            "Modified Version of Noto Sans JP (NotoSansJP-VF.ttf, Version 2.04), made by tools/make_ui_font.py:\n"
            "fixed at wght %d, subset to Windows-31J and the characters used in the game, renamed.\n"
            "© 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'.\n\n"
            % (FAMILY, OUT.name, WEIGHT))
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(head + text[start:], encoding="utf-8", newline="\n")
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.parse_args(argv)
    info = build()
    lic = write_license()
    print("%s  chars=%d glyphs=%d bytes=%d sha256=%s" % (
        info["path"].relative_to(REPO).as_posix(), info["chars"], info["glyphs"],
        info["bytes"], info["sha256"]))
    print(lic.relative_to(REPO).as_posix())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

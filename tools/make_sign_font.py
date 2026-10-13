"""屋内の看板の文字に使うフォント blender/fonts/KCDSignJP-Bold.ttf を作る。

ビルドでは走らない。フォントを作り直すときに開発者が手で走らせ、出来たファイルをコミットする
（Blender 側 blender/kcd_lib/ttf.py はコミット済みのファイルだけを読むので、どの機械で書き出しても
同じ形の文字になる）。

元はリポジトリの Noto Sans JP（unity/KatsushikaCampusDays/Assets/Fonts/NotoSansJP-VF.ttf）。
- 文字を ASCII・Latin-1・JIS X 0208 の非漢字（1〜8 区）と第 1 水準の漢字（16〜47 区）・かな全部・半角カナ・
  看板に出る記号・EXTRA に絞る（JIS X 0208 全部と Windows-31J の拡張まで入れると 2.4 MB、第 1 水準までで
  約 1.0 MB）。第 2 水準の字が看板に要るときは EXTRA に足して作り直す
- 太さの軸 wght を 700 に固定し、輪郭の重なりを取り除く（skia-pathops が要る）
- ヒンティングと組版の表（GSUB / GPOS / GDEF / BASE / 縦書きの表 / STAT）を落とす
- 名前を "KCD Sign JP" に変える。SIL Open Font License 1.1 の改変版なので予約名（'Source'）を名前に
  使わない。著作権（nameID 0）とライセンス（13 / 14）の欄は元のまま残す

blender/fonts/ にはフォントと一緒に OFL の本文 KCDSignJP-OFL.txt を置く（このスクリプトが書く）。

使い方:
    pip install fonttools skia-pathops
    python tools/make_sign_font.py

2026-10-03 に Python 3.10 / fontTools 4.63.0 / skia-pathops 0.9.2 で作った。
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SRC = REPO / "unity" / "KatsushikaCampusDays" / "Assets" / "Fonts" / "NotoSansJP-VF.ttf"
SRC_OFL = REPO / "unity" / "KatsushikaCampusDays" / "Assets" / "StreamingAssets" / "Licenses" / "NotoSansJP-OFL.txt"
OUT_DIR = REPO / "blender" / "fonts"
OUT = OUT_DIR / "KCDSignJP-Bold.ttf"
OUT_OFL = OUT_DIR / "KCDSignJP-OFL.txt"

WEIGHT = 700
FAMILY = "KCD Sign JP"
STYLE = "Bold"
PS_NAME = "KCDSignJP-Bold"
DESCRIPTION = ("Modified Version of Noto Sans JP 2.04 for the in-game sign text of Katsushika Campus Days: "
               "fixed at wght 700, overlaps removed, subset to JIS X 0208 non-kanji and level 1 kanji. "
               "Made with tools/make_sign_font.py.")
# 第 1 水準に無いが看板に要る字（第 2 水準など）。足したら作り直してコミットする
EXTRA = "丼"
# 残す名前の欄（それ以外は落とす）。0 = 著作権, 5 = 版, 13 = ライセンス, 14 = ライセンスの URL
KEEP_NAME_IDS = (0, 5, 13, 14)
DROP_TABLES = ("GSUB", "GPOS", "GDEF", "BASE", "vhea", "vmtx", "STAT", "DSIG", "meta")
# 作り直しても head の日付でファイルが変わらないように固定する（2026-10-03 00:00 UTC。1904 年起点の秒）
TIMESTAMP = 3873830400


def _decode_all(encoding: str, leads, trails) -> set[int]:
    out = set()
    for hi in leads:
        for lo in trails:
            try:
                s = bytes([hi, lo]).decode(encoding)
            except UnicodeDecodeError:
                continue
            if len(s) == 1:
                out.add(ord(s))
    return out


def codepoints() -> set[int]:
    """看板に書く文字の候補。フォントに無い字は後で落ちる。"""
    cps = set(range(0x20, 0x7F)) | set(range(0xA0, 0x100))
    # JIS X 0208 の 1〜8 区（記号・英数・かな・ギリシャ・キリル・罫線）と 16〜47 区（第 1 水準の漢字）。
    # EUC-JP では区 k の先頭バイトが 0xA0 + k
    rows = list(range(1, 9)) + list(range(16, 48))
    cps |= _decode_all("euc_jp", [0xA0 + k for k in rows], range(0xA1, 0xFF))
    cps |= set(range(0xFF61, 0xFFA0))            # 半角カナ
    cps |= set(range(0x3041, 0x3097)) | set(range(0x30A1, 0x3100))  # かな全部（ゔ・ヷ など）
    cps |= set(range(0x2010, 0x203C))            # ダッシュ・引用符・…・※
    cps |= set(range(0x2190, 0x219A)) | {0x21D2, 0x21D4}  # 矢印
    cps |= set(range(0x2460, 0x2474))            # 丸数字 1〜20
    cps |= set(range(0x2160, 0x216A)) | set(range(0x2170, 0x217A))  # ローマ数字
    cps |= {0x2122, 0x2116, 0x2121, 0x20AC, 0x2103, 0x301C, 0xFF5E, 0x2212, 0xFF0D, 0x3000}
    cps |= {ord(ch) for ch in EXTRA}
    return cps


def build(src: Path = SRC, out: Path = OUT) -> dict:
    from fontTools import subset
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer

    font = TTFont(str(src))
    have = set(font.getBestCmap())
    want = sorted(codepoints() & have)

    opts = subset.Options()
    opts.layout_features = []
    opts.name_IDs = ["*"]
    opts.name_languages = [0x0409]
    opts.name_legacy = False
    opts.notdef_outline = True
    opts.hinting = False
    opts.glyph_names = False
    opts.drop_tables = list(opts.drop_tables) + list(DROP_TABLES)
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=want)
    sub.subset(font)

    font = instancer.instantiateVariableFont(font, {"wght": WEIGHT},
                                             overlap=instancer.OverlapMode.REMOVE)
    glyf = font["glyf"]
    composites = [n for n in font.getGlyphOrder() if glyf[n].isComposite()]
    if composites:
        raise RuntimeError("複合グリフが残った: %s" % composites[:10])
    for tag in DROP_TABLES:
        if tag in font:
            del font[tag]

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
    head = ("KCD Sign JP (KCDSignJP-Bold.ttf)\n"
            "Modified Version of Noto Sans JP (NotoSansJP-VF.ttf, Version 2.04), made by tools/make_sign_font.py:\n"
            "fixed at wght 700, overlaps removed, subset to JIS X 0208 non-kanji and level 1 kanji, renamed.\n"
            "© 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'.\n\n")
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

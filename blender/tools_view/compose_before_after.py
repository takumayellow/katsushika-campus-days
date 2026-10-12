"""surfaces_before_after.py の出力（<視点>-flat.png / <視点>-textured.png）を左右に並べて JPG にする。

左が before（--base の ref の単色、既定 main）、右が after（テクスチャ）で、左上に小さなラベルを入れる。
Blender ではなく Pillow の入った Python で動かす。ふつうは `surfaces_before_after.sh` から呼ばれる。

使い方:
  python blender/tools_view/compose_before_after.py --renders <PNG のフォルダ> \
      --out-dir <出力先> [--date YYYYMMDD] [--views overview,mall] [--base main]

出力: <out-dir>/<date>-blender-<視点>-before-after.jpg（1280x720 を 2 枚並べた 2566x720）
"""

from __future__ import annotations

import argparse
import datetime
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

BEFORE_LABEL = "before: flat colour (%s)"
AFTER_LABEL = "after: textures (Blender preview)"
#: 左右の間の白い帯（px）
GAP = 6
QUALITY = 88
#: ラベルの字（macOS / Windows / Linux の順に探し、無ければ Pillow の既定の字）
FONT_PATHS = ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/SFNS.ttf",
              "C:/Windows/Fonts/arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
#: 並べる順（ここに無い視点は名前順で後ろに付ける）
ORDER = ("overview", "mall", "library", "mall-beds", "research1-brick", "road-grass")


def parse_args():
    ap = argparse.ArgumentParser(prog="compose_before_after.py")
    ap.add_argument("--renders", required=True, help="surfaces_before_after.py の出力フォルダ")
    ap.add_argument("--out-dir", required=True, help="JPG の出力先")
    ap.add_argument("--date", default=datetime.date.today().strftime("%Y%m%d"))
    ap.add_argument("--views", default="", help="並べる視点をカンマ区切りで（既定は揃っている全部）")
    ap.add_argument("--base", default="main", help="before を描いた ref（左のラベルに出す）")
    return ap.parse_args()


def find_views(renders):
    """flat と textured の両方が揃っている視点名。"""
    names = set(os.listdir(renders))
    views = [n[:-len("-flat.png")] for n in names if n.endswith("-flat.png")]
    views = [v for v in views if v + "-textured.png" in names]
    rank = {v: i for i, v in enumerate(ORDER)}
    return sorted(views, key=lambda v: (rank.get(v, len(ORDER)), v))


def load_font(size):
    for path in FONT_PATHS:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    try:
        return ImageFont.load_default(size)
    except TypeError:  # Pillow 10.1 より前の load_default は大きさを取らない
        return ImageFont.load_default()


def draw_label(img, text, font):
    """左上に黒地・白文字のラベル（docs/progress/56 の比較画像と同じ体裁）。"""
    draw = ImageDraw.Draw(img)
    pad, margin = 8, 12
    x0, y0, x1, y1 = draw.textbbox((0, 0), text, font=font)
    box = (margin, margin, margin + (x1 - x0) + 2 * pad, margin + (y1 - y0) + 2 * pad)
    draw.rectangle(box, fill=(0, 0, 0))
    draw.text((margin + pad - x0, margin + pad - y0), text, font=font, fill=(255, 255, 255))


def compose(before_path, after_path, out_path, labels):
    before = Image.open(before_path).convert("RGB")
    after = Image.open(after_path).convert("RGB")
    if before.size != after.size:
        raise ValueError("size mismatch: %s %s vs %s %s"
                         % (before_path, before.size, after_path, after.size))
    w, h = before.size
    font = load_font(max(14, h // 32))
    canvas = Image.new("RGB", (2 * w + GAP, h), (255, 255, 255))
    for i, (img, text) in enumerate(zip((before, after), labels)):
        panel = img.copy()
        draw_label(panel, text, font)
        canvas.paste(panel, (i * (w + GAP), 0))
    canvas.save(out_path, "JPEG", quality=QUALITY, optimize=True)
    return canvas.size


def main():
    args = parse_args()
    # 日付はファイル名にそのまま入るので、/ や .. で out-dir の外へ出ないよう形を確かめる
    if not re.fullmatch(r"[0-9]{8}", args.date):
        sys.exit("--date は YYYYMMDD で渡す: " + args.date)
    views = find_views(args.renders)
    if args.views:
        wanted = [v.strip() for v in args.views.split(",") if v.strip()]
        missing = [v for v in wanted if v not in views]
        if missing:
            sys.exit("flat / textured が揃っていない視点: " + ", ".join(missing))
        views = wanted
    if not views:
        sys.exit("並べる画像が無い: " + args.renders)
    os.makedirs(args.out_dir, exist_ok=True)
    labels = (BEFORE_LABEL % args.base, AFTER_LABEL)
    for view in views:
        out = os.path.join(args.out_dir, "%s-blender-%s-before-after.jpg" % (args.date, view))
        size = compose(os.path.join(args.renders, view + "-flat.png"),
                       os.path.join(args.renders, view + "-textured.png"), out, labels)
        print("[compose] %s %dx%d" % (out.replace("\\", "/"), size[0], size[1]))


if __name__ == "__main__":
    main()

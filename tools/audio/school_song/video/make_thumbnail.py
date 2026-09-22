"""投稿動画のサムネ (1920x1080) を作る.

youtube-pipeline の make_thumbnail_v2 (integrations/kiritan_singing/scripts/make_video_v2.py) と同じ構成.
楽譜の 1 ページ目を背景にし, 上に曲名の帯, 右にきりたんの立ち絵, 左下にクレジットを置く.
元の関数は立ち絵を画面の高さの 96% で置くので, 頭が曲名の帯にかかる. ここでは立ち絵の頭を帯の下に揃え, 足元は画面の外に切る.

    python make_thumbnail.py <楽譜 1 ページ目の PNG>

立ち絵: A・Loveる さんの「きりたん立ち絵素材」の制服差分 (youtube-pipeline/assets/kiritan/05_aloveru_school_left.png).
非商用の動画に限って使える. 二次配布は禁止なので, このリポジトリには入れない.
"""
from __future__ import annotations

import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
KIRITAN = os.path.expanduser("~/dev/youtube-pipeline/assets/kiritan/05_aloveru_school_left.png")
OUT = os.path.join(HERE, "thumbnail.png")

W, H = 1920, 1080
BAND_H = 160
SCORE_X = 40
TITLE = "東京理科大学 校歌"
CREDIT = "歌唱: 東北きりたん (NEUTRINO)"
NAVY = (20, 24, 48)


def font(size: int) -> ImageFont.ImageFont:
    for f in ("C:/Windows/Fonts/YuGothB.ttc", "C:/Windows/Fonts/meiryob.ttc"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def score_background(page_png: str) -> Image.Image:
    # 楽譜の 1 ページ目. 上の曲名・作者の行は帯と重なるので切り, 1 段目から見せる
    page = Image.open(page_png).convert("RGBA")
    flat = Image.new("RGB", page.size, "white")
    flat.paste(page, mask=page.split()[-1])
    w = int(W * 0.62)
    flat = flat.resize((w, int(flat.height * w / flat.width)), Image.LANCZOS)
    top = int(flat.height * 0.125)
    return flat.crop((0, top, w, top + H - BAND_H - 20))


def main(argv: list) -> int:
    if len(argv) != 2:
        print(__doc__)
        return 2
    bg = Image.new("RGB", (W, H), (250, 248, 240))
    score = score_background(argv[1])
    bg.paste(score, (SCORE_X, BAND_H + 20))

    # 立ち絵: 楽譜の右の空きの真ん中. 頭を帯の下 (BAND_H + 15) に揃え, 太ももから下は画面の外
    k = Image.open(KIRITAN).convert("RGBA")
    kh = 1100
    k = k.resize((int(k.width * kh / k.height), kh), Image.LANCZOS)
    free_left = SCORE_X + score.width
    bg.paste(k, ((free_left + W - k.width) // 2, BAND_H + 15), k)

    d = ImageDraw.Draw(bg, "RGBA")
    d.rectangle((0, 0, W, BAND_H), fill=NAVY + (220,))
    tf = font(110)
    tb = d.textbbox((0, 0), TITLE, font=tf)
    d.text(((W - (tb[2] - tb[0])) // 2, (BAND_H - (tb[3] - tb[1])) // 2 - tb[1]), TITLE,
           fill=(255, 245, 230), font=tf)

    sf = font(38)
    sb = d.textbbox((0, 0), CREDIT, font=sf)
    pad = 18
    sh = sb[3] - sb[1]
    top = H - sh - pad * 2 - 20
    d.rectangle((20, top, 20 + sb[2] - sb[0] + pad * 2, top + sh + pad * 2), fill=NAVY + (200,))
    d.text((20 + pad, top + pad - sb[1]), CREDIT, fill=(220, 230, 250), font=sf)

    bg.save(OUT)
    print(OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

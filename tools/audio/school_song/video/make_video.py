"""投稿動画 (school_song_kiritan.mp4, 1920x1080) を作る. 楽譜は 4 小節ずつの画面送り.

    python make_video.py [--frames 秒,秒,...] [--check-sync]

--frames を付けると動画は作らず, その時刻の画面を build/frame_<秒>.png に書く (見た目の確認用).
--check-sync は, 各行の歌い出しの予定の時刻と, 歌だけの wav での実際の歌い出しを並べる.

youtube-pipeline の make_video_v2 のスクロールは, 速さの上限と末端の静止のせいで前半が速く後半が止まって見える
(youtube-pipeline #256). この曲は Vo. + Pno. を 4 小節ずつ画面送りで見せる.

- 楽譜: make_display_score.py で作った人が読む楽譜 (school_song_kiritan_piano.musicxml). 曲名などの文字を消し,
  4 小節ごとに改ページした描画用のコピー (build/layout.musicxml) を MuseScore で PNG と .mpos にする.
  1 ページ = 1 段 = 4 小節.
- 画面: 左に今の段 (大きく) と次の段 (小さく薄く), 下に歌詞のテロップ, 右にきりたんの立ち絵.
  鳴っている小節は薄い色で塗る. 段の切り替えは小節の頭より FLIP_LEAD 秒早くする.
- テロップ: 公式の歌詞 (make_display_score.LINES) を 1 行ずつ. 行の最初の音の TELOP_LEAD 秒前に出し,
  次の行が出るか, 行の最後の音が終わって TELOP_HOLD 秒で消す (youtube-pipeline #257).
- 時刻: 楽譜の時刻 (テンポ 117) をそのまま使う. きりたんの歌は楽譜どおりの時刻で鳴っている
  (--check-sync で, 各行の歌い出しを歌だけの wav から拾って比べる. 2026-09-22 時点で ±0.04 s).
- 音: ../audio/school_song_kiritan_mix.mp3

立ち絵: A・Loveる さんの「きりたん立ち絵素材」の制服差分 (make_thumbnail.KIRITAN). このリポジトリには入れない.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_display_score  # noqa: E402
import make_thumbnail  # noqa: E402

SCORE = HERE / "school_song_kiritan_piano.musicxml"
MIX = HERE.parent / "audio" / "school_song_kiritan_mix.mp3"
BUILD = HERE / "build"
OUT = HERE / "school_song_kiritan.mp4"
MSCORE = make_display_score.MSCORE

W, H = 1920, 1080
FPS = 30
DPI = 200
MPOS_UNITS_PER_INCH = 14400.0   # MuseScore 4 の .mpos の座標の単位 (youtube-pipeline の render.py と同じ)
BARS_PER_PAGE = 4
TEMPO = 117.0
FLIP_LEAD = 0.30
TELOP_LEAD = 0.20
TELOP_HOLD = 0.80
VOCAL = HERE.parent / "audio" / "vocal_kiritan_full.wav"   # ミックスの頭に揃えて置いてある
MIX_OFFSET = 0.0   # ミックスの秒 - 楽譜の秒

BG = (250, 248, 240)
NAVY = (20, 24, 48)
HILITE = (255, 214, 120, 90)
SCORE_X, SCORE_W = 36, 1330      # 楽譜を置く幅. 右はきりたん
KIRITAN_H = 1330                 # 立ち絵の高さ. 太ももから下は画面の外
KIRITAN_TOP = 40
NEXT_SCALE = 0.72                # 次の段の大きさ (今の段に対して)
NEXT_ALPHA = 0.42
TELOP_TOP = 890
TITLE = "東京理科大学 校歌"
CREDIT = "歌唱: 東北きりたん (NEUTRINO)"
NO_HEADER_MSS = """<?xml version="1.0" encoding="UTF-8"?>
<museScore version="4.40">
  <Style>
    <showHeader>0</showHeader>
    <showFooter>0</showFooter>
  </Style>
</museScore>
"""


def font(size: int) -> ImageFont.ImageFont:
    return make_thumbnail.font(size)


def to_mix(t: float) -> float:
    return t + MIX_OFFSET


def check_sync(lines: list["Line"]) -> None:
    """各行の歌い出しを, 歌だけの wav で無音から声が立ち上がるところとして拾い, 予定の時刻と比べる.
    前の行から息継ぎなしで続く行 (おお若き...) は前の音を拾うので, 差が負に大きく出る."""
    hop, sr = 80, 8000
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(VOCAL), "-ac", "1", "-ar", str(sr),
                          "-f", "f32le", "-"], check=True, capture_output=True).stdout
    x = np.frombuffer(raw, dtype=np.float32)
    frames = x[: len(x) // hop * hop].reshape(-1, hop)
    db = 20 * np.log10(np.sqrt((frames ** 2).mean(axis=1)) + 1e-9)
    voiced = db > db.max() - 35
    rise = np.flatnonzero(voiced[1:] & ~voiced[:-1]) + 1
    for ln in lines:
        pred = to_mix(ln.start)
        near = rise[np.abs(rise * hop / sr - pred) < 0.6]
        act = f"{near[0] * hop / sr:7.2f}  差 {near[0] * hop / sr - pred:+.2f}" if len(near) else "   (無音からの立ち上がりなし)"
        print(f"{ln.verse}番 {ln.text:<14} 予定 {pred:7.2f}  実際 {act}")


# ---------------------------------------------------------------- 楽譜の時刻

@dataclass
class Bar:
    number: int
    start: float     # 楽譜の秒
    end: float


@dataclass
class Line:
    verse: int
    text: str
    start: float     # 最初の音の頭 (楽譜の秒)
    end: float       # 最後の音の終わり


def score_timing(root: ET.Element) -> tuple[list[Bar], list[Line]]:
    """Vo. のパートを歩いて, 小節の頭と各行の最初・最後の音の時刻 (楽譜の秒) を出す."""
    sec_per_q = 60.0 / TEMPO
    part = root.findall("part")[0]
    divisions = 1
    q = 0.0
    bars: list[Bar] = []
    notes: list[tuple[float, float, bool]] = []   # (頭, 終わり, 歌詞あり) 休符でない音, 順に
    for m in part.findall("measure"):
        d = m.find("attributes/divisions")
        if d is not None:
            divisions = int(d.text)
        pos = 0.0
        longest = 0.0
        for el in m:
            if el.tag == "note":
                dur = int(el.findtext("duration", "0")) / divisions
                if el.find("chord") is not None:
                    continue
                if el.find("rest") is None:
                    # タイの後ろの音は前の音の続き
                    if any(t.get("type") == "stop" for t in el.findall("tie")) and notes:
                        s, _, ly = notes[-1]
                        notes[-1] = (s, (q + pos + dur) * sec_per_q, ly)
                    else:
                        notes.append(((q + pos) * sec_per_q, (q + pos + dur) * sec_per_q,
                                      el.find("lyric") is not None))
                pos += dur
            elif el.tag == "backup":
                pos -= int(el.findtext("duration")) / divisions
            elif el.tag == "forward":
                pos += int(el.findtext("duration")) / divisions
            longest = max(longest, pos)
        bars.append(Bar(int(m.get("number")), q * sec_per_q, (q + longest) * sec_per_q))
        q += longest

    # 行ごとの音符の数は make_display_score.LINES の表示かな ("_" も 1 音) と同じ.
    # タイの後ろはここでは前の音にまとめてある
    lines: list[Line] = []
    i = 0
    for verse, text, kana in make_display_score.LINES:
        n = len(kana.split())
        seg = notes[i:i + n]
        assert len(seg) == n and seg[0][2], (text, len(seg))
        lines.append(Line(verse, text, seg[0][0], seg[-1][1]))
        i += n
    assert i == len(notes), (i, len(notes))
    return bars, lines


# ---------------------------------------------------------------- 楽譜の画像

def layout_score(src: Path, dst: Path) -> None:
    """曲名などの文字を消し, BARS_PER_PAGE 小節ごとに改ページした描画用のコピーを書く."""
    tree = ET.parse(src)
    root = tree.getroot()
    for tag in ("credit", "work", "movement-title", "movement-number"):
        for el in root.findall(tag):
            root.remove(el)
    ident = root.find("identification")
    if ident is not None:
        for c in ident.findall("creator"):
            ident.remove(c)
    for part in root.findall("part"):
        measures = part.findall("measure")
        for i, m in enumerate(measures):
            for p in m.findall("print"):
                m.remove(p)
            if i and i % BARS_PER_PAGE == 0:
                m.insert(0, ET.Element("print", {"new-page": "yes"}))
    tree.write(dst, encoding="UTF-8", xml_declaration=True)


@dataclass
class Page:
    image: Image.Image            # 段だけを切り出した画像 (RGB)
    bar_x: list[tuple[int, int]]  # 小節ごとの画像上の x の範囲


def render_pages(layout: Path, work: Path) -> list[Page]:
    mscz = work / "layout.mscz"
    mpos = work / "layout.mpos"
    # ページ番号 (ヘッダ・フッタ) を出さない. 読み込むときに当てて, PNG と .mpos を同じ組版から取る
    style = work / "no_header.mss"
    style.write_text(NO_HEADER_MSS, encoding="utf-8")
    subprocess.run([MSCORE, "-S", str(style), "-o", str(mscz), str(layout)], check=True, capture_output=True)
    subprocess.run([MSCORE, "-o", str(mpos), str(mscz)], check=True, capture_output=True)
    for old in work.glob("page-*.png"):
        old.unlink()
    subprocess.run([MSCORE, "-r", str(DPI), "-o", str(work / "page.png"), str(mscz)],
                   check=True, capture_output=True)
    pngs = sorted(work.glob("page-*.png"), key=lambda p: int(p.stem.split("-")[1]))

    k = DPI / MPOS_UNITS_PER_INCH
    by_page: dict[int, list[tuple[float, float, float, float]]] = {}
    for el in ET.parse(mpos).getroot().find("elements").findall("element"):
        by_page.setdefault(int(el.get("page")), []).append(
            tuple(float(el.get(a)) * k for a in ("x", "y", "sx", "sy")))

    pages = []
    for i, png in enumerate(pngs):
        rgba = Image.open(png).convert("RGBA")
        img = Image.new("RGB", rgba.size, "white")
        img.paste(rgba, mask=rgba.split()[-1])
        boxes = by_page[i]
        top = min(b[1] for b in boxes)
        bottom = max(b[1] + b[3] for b in boxes)
        left = min(b[0] for b in boxes)
        right = max(b[0] + b[2] for b in boxes)
        # 段の上 (小節番号・テンポ) と左 (楽器名) の余白. ページ番号は段より上なので入らない
        crop = (int(left - 1.05 * DPI), int(top - 0.42 * DPI), int(right + 0.08 * DPI), int(bottom + 0.22 * DPI))
        # ページの外まで切ると黒で埋まるので, 白の画用紙に貼ってから切る
        pad = int(1.2 * DPI)
        canvas = Image.new("RGB", (img.width + 2 * pad, img.height + 2 * pad), "white")
        canvas.paste(img, (pad, pad))
        pages.append(Page(canvas.crop(tuple(c + pad for c in crop)), [(int(b[0] - crop[0]), int(b[0] + b[2] - crop[0])) for b in boxes]))
    return pages


# ---------------------------------------------------------------- 画面

class Composer:
    def __init__(self, pages: list[Page]):
        self.pages = pages
        k = Image.open(make_thumbnail.KIRITAN).convert("RGBA")
        self.kiritan = k.resize((int(k.width * KIRITAN_H / k.height), KIRITAN_H), Image.LANCZOS)
        self.scale = SCORE_W / max(p.image.width for p in pages)
        self.cur = [self._fit(p.image, self.scale) for p in pages]
        self.nxt = [self._dim(self._fit(p.image, self.scale * NEXT_SCALE)) for p in pages]
        self.f_telop = font(66)
        self.f_verse = font(30)
        self.f_small = font(26)

    @staticmethod
    def _fit(img: Image.Image, s: float) -> Image.Image:
        return img.resize((int(img.width * s), int(img.height * s)), Image.LANCZOS)

    @staticmethod
    def _dim(img: Image.Image) -> Image.Image:
        return Image.blend(Image.new("RGB", img.size, BG), img, NEXT_ALPHA)

    def frame(self, page: int, bar: int | None, line: Line | None) -> Image.Image:
        bg = Image.new("RGB", (W, H), BG)
        d = ImageDraw.Draw(bg, "RGBA")
        cur = self.cur[page]
        y0 = 28
        if bar is not None:
            x0, x1 = self.pages[page].bar_x[bar]
            ov = Image.new("RGBA", cur.size, (0, 0, 0, 0))
            ImageDraw.Draw(ov).rectangle((int(x0 * self.scale), 0, int(x1 * self.scale), cur.height), fill=HILITE)
            shown = Image.alpha_composite(cur.convert("RGBA"), ov).convert("RGB")
            # 塗りは白地の上だけに見せたいので, 音符の黒は元の画像から戻す
            shown = Image.composite(cur, shown, cur.convert("L").point(lambda v: 255 if v < 128 else 0))
        else:
            shown = cur
        bg.paste(shown, (SCORE_X, y0))
        if page + 1 < len(self.pages):
            nxt = self.nxt[page + 1]
            bg.paste(nxt, (SCORE_X, y0 + cur.height + 14))

        # 立ち絵: 楽譜の右の空きの真ん中
        free_left = SCORE_X + SCORE_W
        bg.paste(self.kiritan, ((free_left + W - self.kiritan.width) // 2, KIRITAN_TOP), self.kiritan)

        # テロップ
        d.rounded_rectangle((SCORE_X, TELOP_TOP, SCORE_X + SCORE_W, H - 28), radius=18, fill=NAVY + (215,))
        if line is not None:
            tb = d.textbbox((0, 0), line.text, font=self.f_telop)
            tw, th = tb[2] - tb[0], tb[3] - tb[1]
            cy = (TELOP_TOP + H - 28) // 2 + 14
            d.text((SCORE_X + (SCORE_W - tw) // 2, cy - th // 2 - tb[1]), line.text,
                   fill=(255, 245, 230), font=self.f_telop)
        head = TITLE + (f"　{line.verse}番" if line is not None else "")
        d.text((SCORE_X + 26, TELOP_TOP + 14), head, fill=(170, 185, 225), font=self.f_verse)

        cb = d.textbbox((0, 0), CREDIT, font=self.f_small)
        d.text((W - (cb[2] - cb[0]) - 24, H - (cb[3] - cb[1]) - 26 - cb[1]), CREDIT,
               fill=(255, 255, 255), font=self.f_small, stroke_width=3, stroke_fill=NAVY)
        return bg


# ---------------------------------------------------------------- 時間割

def timeline(bars: list[Bar], lines: list[Line], total: float) -> list[tuple[float, tuple]]:
    """(ミックスの秒, 画面の状態) を時刻順に. 状態 = (ページ, ページ内の小節 or None, 行の番号 or None)."""
    events: list[tuple[float, str, int]] = []
    for i, b in enumerate(bars):
        if i % BARS_PER_PAGE == 0 and i:
            events.append((to_mix(b.start) - FLIP_LEAD, "page", i // BARS_PER_PAGE))
        events.append((to_mix(b.start), "bar", i))
    events.append((to_mix(bars[-1].end), "bar", -1))
    for j, ln in enumerate(lines):
        events.append((max(0.0, to_mix(ln.start) - TELOP_LEAD), "line", j))
        nxt = to_mix(lines[j + 1].start) - TELOP_LEAD if j + 1 < len(lines) else total
        events.append((min(to_mix(ln.end) + TELOP_HOLD, nxt), "noline", j))
    events.sort(key=lambda e: e[0])

    page, bar, line = 0, None, None
    out: list[tuple[float, tuple]] = [(0.0, (0, None, None))]
    for t, kind, v in events:
        if kind == "page":
            page, bar = v, None
        elif kind == "bar":
            if v < 0:
                bar = None
            else:
                page, bar = v // BARS_PER_PAGE, v % BARS_PER_PAGE
        elif kind == "line":
            line = v
        elif kind == "noline" and line == v:
            line = None
        t = max(0.0, t)
        if out and abs(out[-1][0] - t) < 1e-6:
            out[-1] = (t, (page, bar, line))
        else:
            out.append((t, (page, bar, line)))
    return out


def state_at(tl: list[tuple[float, tuple]], t: float) -> tuple:
    st = tl[0][1]
    for tt, s in tl:
        if tt > t:
            break
        st = s
    return st


def main(argv: list) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", help="この秒の画面だけを書く (カンマ区切り)")
    ap.add_argument("--check-sync", action="store_true", help="歌詞の行の時刻を歌と比べるだけ")
    args = ap.parse_args(argv[1:])
    if args.check_sync:
        check_sync(score_timing(ET.parse(SCORE).getroot())[1])
        return 0

    BUILD.mkdir(exist_ok=True)
    work = BUILD / "pages"
    work.mkdir(exist_ok=True)
    layout = BUILD / "layout.musicxml"
    layout_score(SCORE, layout)
    pages = render_pages(layout, work)

    bars, lines = score_timing(ET.parse(SCORE).getroot())
    assert len(pages) == -(-len(bars) // BARS_PER_PAGE), (len(pages), len(bars))
    for i, p in enumerate(pages):
        assert len(p.bar_x) == len(bars[i * BARS_PER_PAGE:(i + 1) * BARS_PER_PAGE]), i
    total = float(subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(MIX)],
        check=True, capture_output=True, text=True).stdout)
    tl = timeline(bars, lines, total)
    comp = Composer(pages)
    print(f"{len(pages)} 画面, {len(bars)} 小節, {len(lines)} 行, 切り替え {len(tl)}, 音 {total:.2f} s")

    if args.frames:
        for s in args.frames.split(","):
            t = float(s)
            p, b, ln = state_at(tl, t)
            path = BUILD / f"frame_{t:g}.png"
            comp.frame(p, b, lines[ln] if ln is not None else None).save(path)
            print(path, (p, b, lines[ln].text if ln is not None else None))
        return 0

    frames = BUILD / "frames"
    shutil.rmtree(frames, ignore_errors=True)
    frames.mkdir()
    cache: dict[tuple, Path] = {}
    concat = []
    # 切り替えの時刻はフレームの境目に丸める (concat の尺の誤差を積み上げない)
    ticks = [round(t * FPS) for t, _ in tl] + [round(total * FPS)]
    for i, (_, st) in enumerate(tl):
        n = ticks[i + 1] - ticks[i]
        if n <= 0:
            continue
        if st not in cache:
            path = frames / f"{len(cache):04d}.png"
            p, b, ln = st
            comp.frame(p, b, lines[ln] if ln is not None else None).save(path)
            cache[st] = path
        concat.append(f"file '{cache[st].name}'\nduration {n / FPS:.6f}")
    concat.append(f"file '{cache[tl[-1][1]].name}'")
    (frames / "list.txt").write_text("\n".join(concat) + "\n", encoding="utf-8")
    print(f"画面 {len(cache)} 枚")

    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", "list.txt", "-i", str(MIX),
         "-map", "0:v", "-map", "1:a", "-vf", f"fps={FPS},format=yuv420p", "-c:v", "libx264",
         "-preset", "medium", "-crf", "18", "-tune", "stillimage", "-c:a", "aac", "-b:a", "320k",
         "-shortest", "-movflags", "+faststart", str(OUT)],
        check=True, cwd=frames)
    print(OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

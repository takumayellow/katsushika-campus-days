"""docs/HOW_TO_PLAY.md から、Pages の遊び方のページ（howto/index.html）を作る (#18)。

Pages に載るのは webgl.zip の中身そのもので、.github/workflows/pages.yml は zip を展開するだけ
（リポジトリを checkout しない）。そのため tools/deploy_pages.py が zip を作るときに、このモジュールが
作ったページと画像を zip に足す。変換した HTML はリポジトリに置かない。遊び方の元は
docs/HOW_TO_PLAY.md の 1 つだけなので、手で二重に書いてずれることがない。

    howto/index.html    docs/HOW_TO_PLAY.md を tools/mini_markdown.py で変換したページ
    howto/img/<名前>    docs/screenshots から選んだ画像（HERO と FIGURES に書いたもの）

ページは JS も外部の読み込みも使わない。<meta> の CSP は、スタイルをこのファイルの CSS の
ハッシュに固定する（style 属性は使わない）。ゲームへは相対リンク ../ で戻る。
docs/HOW_TO_PLAY.md に mini_markdown が扱えない書き方を足すと、配信の前にここで止まる。
"""

from __future__ import annotations

import base64
import hashlib
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping
from urllib.parse import quote

import mini_markdown
from mini_markdown import Heading, Paragraph, escape, plain_text, render_block

ROOT = Path(__file__).resolve().parents[1]
GUIDE = ROOT / "docs" / "HOW_TO_PLAY.md"
SCREENSHOTS = ROOT / "docs" / "screenshots"

PAGE_DIR = "howto"
PAGE_PATH = f"{PAGE_DIR}/index.html"
IMAGE_DIR = f"{PAGE_DIR}/img"
GAME_URL = "../"
SITE_NAME = "葛飾キャンパスデイズ"

# ページと画像の合計の上限（バイト）。ゲームを読む量（deploy_pages.py の上限）には数えないが、
# zip と Pages に置く量は増えるので、画像を足しすぎないよう止める。実測は約 1.1 MiB。
MAX_PAGE_BYTES = 2 * 1024 * 1024


@dataclass(frozen=True)
class Figure:
    file: str          # docs/screenshots の下のファイル名
    alt: str           # 画像に写っているもの（読み上げ用）
    caption: str = ""  # 画像の下に出す短い説明。空なら出さない


HERO = Figure(
    "campus.jpg",
    "朝のメインストリートに立つ主人公の後ろ姿。左上にクエスト、右上にミニマップと時刻が出ている")

# 見出しの文字（強調とコードの記号を除いたもの）に、その直後へ置く画像を対応させる。
# 見出しが遊び方に無ければ、画像が黙って消えないよう止める。
FIGURES: dict[str, tuple[Figure, ...]] = {
    "タイトル画面": (
        Figure("title.png",
               "タイトル画面。回転台に立つ 3 人の主人公の候補と、「つづきから」「はじめから」の選択肢",
               "タイトル画面"),
    ),
    "1 日の流れ": (
        Figure("entrance.jpg",
               "図書館の入口の前に立つ主人公。奥に「図書館」の看板、手前に花壇とベンチがある",
               "建物の入口"),
        Figure("interior.jpg",
               "図書館の中。閲覧席の机が奥まで並ぶ広い室内",
               "建物の中"),
        Figure("talk.jpg",
               "木立の中で、主人公と NPC が向かい合う会話の画面。下の枠に話し手の名前とセリフが出ている",
               "NPC との会話"),
        Figure("dusk.jpg",
               "夕暮れのメインストリート。空がオレンジ色に染まり、時刻は 17:48",
               "夕方のキャンパス"),
    ),
}

# タブのアイコン。ゲームのページ（WebGLTemplates/KCD/index.html）と同じ絵。
FAVICON = ("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E"
           "%3Crect width='32' height='32' rx='6' fill='%2312151b'/%3E"
           "%3Cpath d='M6 20l10-9 10 9v6H6z' fill='%233f7ad6'/%3E%3C/svg%3E")

# 色と書体は、ゲームのページ（TemplateData/style.css）と同じ。暗い一色の見た目で、body にも背景を持たせる。
CSS = """
:root {
  color-scheme: dark;
  --kcd-bg: #12151b;
  --kcd-panel: #1b2029;
  --kcd-ink: #f2efe6;
  --kcd-muted: #a7b0bf;
  --kcd-accent: #3f7ad6;
  --kcd-accent-hover: #4f8ae8;
  --kcd-link: #8fb6f2;
  --kcd-line: rgba(255, 255, 255, 0.14);
  --gutter: 1rem;
  --bar: 3.4rem;
}
*, *::before, *::after { box-sizing: border-box; }
html {
  -webkit-text-size-adjust: 100%;
  text-size-adjust: 100%;
  scroll-padding-top: calc(var(--bar) + env(safe-area-inset-top, 0px) + 0.5rem);
}
body {
  margin: 0;
  background: var(--kcd-bg);
  color: var(--kcd-ink);
  font: 1rem/1.85 "Hiragino Kaku Gothic ProN", "Noto Sans JP", "Yu Gothic UI", "Meiryo", system-ui, sans-serif;
  overflow-wrap: break-word;
  word-break: auto-phrase;
}
a { color: var(--kcd-link); overflow-wrap: anywhere; }
a:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }

.bar {
  position: sticky;
  top: 0;
  z-index: 2;
  padding-top: env(safe-area-inset-top, 0px);
  background: rgba(18, 21, 27, 0.95);
  border-bottom: 1px solid var(--kcd-line);
}
.bar-in, main, footer {
  max-width: 48rem;
  margin: 0 auto;
  padding-left: max(var(--gutter), env(safe-area-inset-left, 0px));
  padding-right: max(var(--gutter), env(safe-area-inset-right, 0px));
}
.bar-in {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem 1rem;
  min-height: var(--bar);
  padding-top: 0.3rem;
  padding-bottom: 0.3rem;
}
.brand { color: var(--kcd-muted); font-weight: 600; letter-spacing: 0.08em; }
.play {
  display: inline-block;
  padding: 0.3rem 1.1rem;
  border-radius: 0.4rem;
  background: var(--kcd-accent);
  color: #fff;
  font-size: 1.2rem;
  font-weight: 700;
  line-height: 1.5;
  text-decoration: none;
}
.play:hover { background: var(--kcd-accent-hover); }

main { padding-bottom: 1rem; }
h1, h2, h3 { text-wrap: balance; }
/* 題は 1 行に収める（390 px 幅で 28 px だと、末尾の 1 字だけが次の行に落ちる）。 */
h1 { margin: 1.4rem 0 0.7rem; font-size: clamp(1.25rem, 6.4vw, 1.75rem); line-height: 1.4; }
h2 {
  margin: 2.8rem 0 0.9rem;
  padding-bottom: 0.4rem;
  border-bottom: 1px solid var(--kcd-line);
  font-size: 1.4rem;
  line-height: 1.45;
}
h3 { margin: 2rem 0 0.7rem; font-size: 1.15rem; line-height: 1.5; }
p { margin: 0 0 1rem; }
h1 + p { color: var(--kcd-muted); font-size: 1.05rem; }
ul, ol { margin: 0 0 1rem; padding-left: 1.5rem; }
li + li { margin-top: 0.3rem; }
strong { color: #fff; }
code {
  padding: 0.1em 0.35em;
  border-radius: 0.25rem;
  background: var(--kcd-panel);
  font: 0.9em ui-monospace, "Cascadia Mono", Consolas, monospace;
  overflow-wrap: anywhere;
}

.cta { margin: 1.4rem 0 0; }
.toc { margin: 1.4rem 0 0; }
.toc ul { display: flex; flex-wrap: wrap; gap: 0.5rem; margin: 0; padding: 0; list-style: none; }
.toc li + li { margin-top: 0; }
.toc a {
  display: block;
  padding: 0.45rem 0.9rem;
  border: 1px solid var(--kcd-line);
  border-radius: 999px;
  background: var(--kcd-panel);
  color: var(--kcd-ink);
  font-size: 0.9rem;
  line-height: 1.5;
  text-decoration: none;
}
.toc a:hover { border-color: var(--kcd-accent); }

.shot { margin: 1.2rem 0; }
.shot img {
  display: block;
  width: 100%;
  height: auto;
  border-radius: 0.5rem;
  background: var(--kcd-panel);
}
.shot figcaption { margin-top: 0.4rem; color: var(--kcd-muted); font-size: 0.85rem; line-height: 1.5; }
.hero-shot { margin: 1rem 0 0; }
.gallery { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; margin: 1.2rem 0; }
.gallery .shot { margin: 0; }

.table-wrap {
  margin: 0 0 1.4rem;
  overflow-x: auto;
  border: 1px solid var(--kcd-line);
  border-radius: 0.5rem;
  background: var(--kcd-panel);
}
table { width: 100%; border-collapse: collapse; font-size: 0.95rem; line-height: 1.65; }
th, td { padding: 0.6rem 0.8rem; border-top: 1px solid var(--kcd-line); text-align: left; vertical-align: top; }
thead th, thead td { border-top: 0; background: rgba(255, 255, 255, 0.05); color: var(--kcd-muted); font-weight: 600; }
tbody th { font-weight: 600; }

footer {
  margin-top: 2.5rem;
  padding-top: 1.5rem;
  padding-bottom: calc(2rem + env(safe-area-inset-bottom, 0px));
  border-top: 1px solid var(--kcd-line);
}
.back { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 0.5rem 1rem; margin: 0; }
.top { display: inline-flex; align-items: center; min-height: 2.75rem; padding: 0 0.75rem; }

/* 狭い画面では、3 列以上の表を行ごとの札に組み替える（横にはみ出させない）。 */
@media (max-width: 40rem) {
  .table-wrap { overflow: visible; border: 0; background: none; }
  table.cards, table.cards tbody, table.cards tr, table.cards th, table.cards td { display: block; }
  table.cards thead {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip: rect(0 0 0 0);
    white-space: nowrap;
  }
  table.cards tr {
    margin: 0 0 0.75rem;
    overflow: hidden;
    border: 1px solid var(--kcd-line);
    border-radius: 0.5rem;
    background: var(--kcd-panel);
  }
  table.cards th { border-top: 0; background: rgba(255, 255, 255, 0.06); }
  table.cards td {
    display: grid;
    grid-template-columns: 6.5rem minmax(0, 1fr);
    gap: 0.6rem;
  }
  table.cards td::before { content: attr(data-label); color: var(--kcd-muted); font-size: 0.85rem; }
  /* 目次のチップは 1 行に並べて横に送る。12 個を折り返すと 5 段（222 px）になり、最初の見出しが画面の外に出る。 */
  .toc ul {
    --edge-left: max(var(--gutter), env(safe-area-inset-left, 0px));
    --edge-right: max(var(--gutter), env(safe-area-inset-right, 0px));
    flex-wrap: nowrap;
    overflow-x: auto;
    margin: 0 calc(-1 * var(--edge-right)) 0 calc(-1 * var(--edge-left));
    padding: 0.25rem var(--edge-right) 0.25rem var(--edge-left);
    scroll-padding-inline: var(--edge-left) var(--edge-right);
  }
  .toc li { flex: none; }
  .toc a { display: flex; align-items: center; min-height: 2.75rem; white-space: nowrap; }
}
@media (max-width: 22.5rem) {
  .brand { display: none; }
  .bar-in { justify-content: center; }
}
/* 横持ちのスマホは縦が足りない。上のバーを画面に張り付けず、流す。 */
@media (max-height: 30rem) {
  .bar { position: static; }
  html { scroll-padding-top: 0.5rem; }
}
"""


def csp(css: str = CSS) -> str:
    """meta に書く Content-Security-Policy。スタイルは css のハッシュに合うものだけ通す。"""
    digest = base64.b64encode(hashlib.sha256(css.encode("utf-8")).digest()).decode("ascii")
    return ("default-src 'none'; img-src 'self' data:; "
            f"style-src 'sha256-{digest}'; base-uri 'none'; form-action 'none'")


# ---- 画像 ----

_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
# 大きさを持つ JPEG のマーカー（SOF0〜SOF15 から DHT・JPG・DAC を除く）。
_JPEG_SOF = frozenset(range(0xC0, 0xD0)) - {0xC4, 0xC8, 0xCC}


def _jpeg_size(data: bytes) -> tuple[int, int]:
    position = 2
    while position + 4 <= len(data):
        if data[position] != 0xFF:
            break
        marker = data[position + 1]
        if marker == 0xFF:  # 詰め物
            position += 1
        elif marker == 0x01 or 0xD0 <= marker <= 0xD8:  # 長さを持たないマーカー
            position += 2
        elif marker == 0xD9:
            break
        else:
            length = int.from_bytes(data[position + 2:position + 4], "big")
            if marker in _JPEG_SOF and length >= 7 and position + 9 <= len(data):
                height, width = struct.unpack(">HH", data[position + 5:position + 9])
                return width, height
            if length < 2:
                break
            position += 2 + length
    raise ValueError("JPEG の大きさを読めない")


def image_size(data: bytes) -> tuple[int, int]:
    """PNG か JPEG の（幅, 高さ）。ヘッダから読む。"""
    if data.startswith(_PNG_SIGNATURE) and data[12:16] == b"IHDR" and len(data) >= 24:
        width, height = struct.unpack(">II", data[16:24])
    elif data.startswith(b"\xff\xd8"):
        width, height = _jpeg_size(data)
    else:
        raise ValueError("PNG でも JPEG でもない画像")
    if width <= 0 or height <= 0:
        raise ValueError(f"画像の大きさが 0: {width}x{height}")
    return width, height


def image_names() -> tuple[str, ...]:
    """ページが使う画像のファイル名（重複なし、出てくる順）。"""
    names = [HERO.file]
    for figures in FIGURES.values():
        names.extend(figure.file for figure in figures)
    return tuple(dict.fromkeys(names))


# ---- ページ ----

def _figure(figure: Figure, sizes: Mapping[str, tuple[int, int]], *, hero: bool = False) -> str:
    if figure.file not in sizes:
        raise ValueError(f"画像の大きさが渡されていない: {figure.file}")
    width, height = sizes[figure.file]
    lazy = "" if hero else ' loading="lazy"'
    caption = f"<figcaption>{escape(figure.caption)}</figcaption>" if figure.caption else ""
    return (f'<figure class="shot{" hero-shot" if hero else ""}">'
            f'<img src="img/{escape(quote(figure.file))}" width="{width}" height="{height}" '
            f'alt="{escape(figure.alt)}" decoding="async"{lazy}>{caption}</figure>')


def _figure_group(figures: tuple[Figure, ...], sizes: Mapping[str, tuple[int, int]]) -> str:
    shots = [_figure(figure, sizes) for figure in figures]
    if len(shots) == 1:
        return shots[0]
    return '<div class="gallery">\n' + "\n".join(shots) + "\n</div>"


def _toc(blocks: tuple[mini_markdown.Block, ...]) -> str:
    items = [f'<li><a href="#{quote(block.slug, safe="")}">{escape(plain_text(block.text))}</a></li>'
             for block in blocks if isinstance(block, Heading) and block.level in (2, 3)]
    if not items:
        return ""
    return '<nav class="toc" id="toc" aria-label="目次">\n<ul>\n' + "\n".join(items) + "\n</ul>\n</nav>"


def _main(blocks: tuple[mini_markdown.Block, ...], sizes: Mapping[str, tuple[int, int]]) -> str:
    # 題の直後に、導入の段落があればその後に、遊ぶボタンと目次を置く。
    lead = 1 if len(blocks) > 1 and isinstance(blocks[1], Paragraph) else 0
    parts = [_figure(HERO, sizes, hero=True)]
    for index, block in enumerate(blocks):
        parts.append(render_block(block))
        if isinstance(block, Heading) and plain_text(block.text) in FIGURES:
            parts.append(_figure_group(FIGURES[plain_text(block.text)], sizes))
        if index == lead:
            parts.append(f'<p class="cta"><a class="play" href="{GAME_URL}">ゲームを遊ぶ</a></p>')
            parts.append(_toc(blocks))
    return "\n".join(part for part in parts if part)


def render_page(markdown: str, sizes: Mapping[str, tuple[int, int]]) -> str:
    """遊び方の Markdown 全体を、1 枚の HTML にする。sizes は画像のファイル名 → （幅, 高さ）。"""
    blocks = mini_markdown.parse(markdown)
    if not blocks or not isinstance(blocks[0], Heading) or blocks[0].level != 1:
        raise ValueError("遊び方の先頭が # の見出しではない")
    headings = {plain_text(block.text) for block in blocks if isinstance(block, Heading)}
    absent = [name for name in FIGURES if name not in headings]
    if absent:
        raise ValueError("画像を置く見出しが遊び方に無い: " + ", ".join(absent))

    title = escape(plain_text(blocks[0].text))
    intro = next((block for block in blocks if isinstance(block, Paragraph)), None)
    description = (f'<meta name="description" content="{escape(plain_text(intro.text))}">\n'
                   if intro else "")
    return f"""<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="{csp()}">
<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover">
<meta name="theme-color" content="#12151b">
<meta name="color-scheme" content="dark">
{description}<title>{title}</title>
<link rel="icon" href="{FAVICON}">
<style>{CSS}</style>
</head>
<body>
<header class="bar"><div class="bar-in">
<span class="brand">{escape(SITE_NAME)}</span>
<a class="play" href="{GAME_URL}">ゲームを遊ぶ</a>
</div></header>
<main>
{_main(blocks, sizes)}
</main>
<footer>
<p class="back"><a class="play" href="{GAME_URL}">ゲームを遊ぶ</a><a class="top" href="#">ページの先頭へ</a></p>
</footer>
</body>
</html>
"""


def site_files(guide: Path = GUIDE, screenshots: Path = SCREENSHOTS,
               max_bytes: int = MAX_PAGE_BYTES) -> list[tuple[str, bytes]]:
    """zip に足すファイル（サイトの根からのパス, 中身）。ページと、ページが使う画像。

    Markdown が変換できない、画像が読めない、合計が max_bytes を超えるときは ValueError か OSError。
    """
    markdown = guide.read_text(encoding="utf-8")
    images: dict[str, bytes] = {}
    for name in image_names():
        path = screenshots / name
        if not path.is_file():
            raise FileNotFoundError(f"遊び方のページの画像が無い: {path}")
        images[name] = path.read_bytes()
    try:
        sizes = {name: image_size(data) for name, data in images.items()}
    except ValueError as error:
        raise ValueError(f"遊び方のページの画像を読めない: {error}") from None
    page = render_page(markdown, sizes).encode("utf-8")

    files = [(PAGE_PATH, page)] + [(f"{IMAGE_DIR}/{name}", data) for name, data in images.items()]
    total = sum(len(data) for _, data in files)
    if total > max_bytes:
        raise ValueError(f"遊び方のページと画像の合計 {total:,} B が上限 {max_bytes:,} B を超えた")
    return files

"""tools/howto_page.py のテスト (#18)。python -m pytest tests で回す。

docs/HOW_TO_PLAY.md から Pages の遊び方のページ（howto/index.html）を作る部分。ブラウザは使わず、
出てきた HTML を読んで、元の md との一致・エスケープ・CSP・リンク・画像を確かめる。
"""

from __future__ import annotations

import base64
import hashlib
import struct
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import howto_page as hp  # noqa: E402
import mini_markdown as md  # noqa: E402

GUIDE = hp.GUIDE.read_text(encoding="utf-8")
SIZES = {name: (1600, 900) for name in hp.image_names()}
MINIMAL_GUIDE = "# 題\n\n導入の文。\n\n## タイトル画面\n\n本文\n\n## 1 日の流れ\n\n本文\n"


class Page(HTMLParser):
    """出力した HTML を読んで、タグ・style の中身・目次のリンクを集める。"""

    def __init__(self, source: str):
        super().__init__(convert_charrefs=True)
        self.tags: list[tuple[str, dict[str, str | None]]] = []
        self.style = ""
        self.toc_links: list[str] = []
        self._in_style = False
        self._in_toc = False
        self.feed(source)
        self.close()

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        self.tags.append((tag, attributes))
        if tag == "style":
            self._in_style = True
        if tag == "nav" and "toc" in (attributes.get("class") or "").split():
            self._in_toc = True
        if tag == "a" and self._in_toc:
            self.toc_links.append(attributes["href"])

    def handle_endtag(self, tag):
        if tag == "style":
            self._in_style = False
        if tag == "nav":
            self._in_toc = False

    def handle_data(self, data):
        if self._in_style:
            self.style += data

    def find(self, tag: str) -> list[dict[str, str | None]]:
        return [attributes for name, attributes in self.tags if name == tag]

    def meta(self, name_or_equiv: str) -> str | None:
        for attributes in self.find("meta"):
            if name_or_equiv in (attributes.get("name"), attributes.get("http-equiv")):
                return attributes.get("content")
        return None


def render(markdown: str = GUIDE) -> str:
    return hp.render_page(markdown, SIZES)


def png_bytes(width: int, height: int) -> bytes:
    return (b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height)
            + b"\x08\x02\x00\x00\x00" + b"\x00" * 4)


def segment(marker: int, body: bytes) -> bytes:
    return bytes([0xFF, marker]) + struct.pack(">H", len(body) + 2) + body


def jpeg_bytes(width: int, height: int, marker: int = 0xC0, before: bytes = b"") -> bytes:
    frame = b"\x08" + struct.pack(">HH", height, width) + b"\x03\x01\x22\x00\x02\x11\x01\x03\x11\x01"
    return b"\xff\xd8" + before + segment(marker, frame) + b"\xff\xda\x00\x02" + b"\x00" * 8 + b"\xff\xd9"


# ---- 画像の大きさ ----

def test_png_size():
    assert hp.image_size(png_bytes(1600, 900)) == (1600, 900)


@pytest.mark.parametrize("marker", [0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC9, 0xCF])
def test_jpeg_size_for_each_frame_type(marker):
    assert hp.image_size(jpeg_bytes(640, 360, marker)) == (640, 360)


def test_jpeg_size_skips_other_segments_first():
    before = segment(0xE0, b"JFIF\x00" + b"\x01\x01\x00\x00\x01\x00\x01\x00\x00")
    before += segment(0xE1, b"Exif\x00\x00" + b"\xff\xc0" * 20)  # 中に SOF に似た並びがあっても読み飛ばす
    before += segment(0xC4, b"\x00" * 20)                       # ハフマン表（0xC4）は大きさではない
    before += segment(0xDB, b"\x00" * 65)
    assert hp.image_size(jpeg_bytes(1920, 1080, before=before)) == (1920, 1080)


def test_jpeg_size_skips_fill_bytes_and_markers_without_length():
    before = b"\xff\xff" + b"\xff\xd0"
    assert hp.image_size(jpeg_bytes(100, 50, before=before)) == (100, 50)


@pytest.mark.parametrize("data", [
    b"",
    b"GIF89a" + b"\x00" * 30,
    b"not an image",
    b"\x89PNG\r\n\x1a\n",                       # 切れている
    png_bytes(0, 900),
    png_bytes(900, 0),
    b"\xff\xd8",                                # SOF が無い
    b"\xff\xd8" + segment(0xE0, b"x") + b"\xff\xd9",
    b"\xff\xd8\xff\xc0\x00",                    # 切れている
    jpeg_bytes(0, 10),
])
def test_image_size_rejects_what_it_cannot_read(data):
    with pytest.raises(ValueError):
        hp.image_size(data)


def test_real_screenshots_are_read_without_error():
    for name in hp.image_names():
        width, height = hp.image_size((hp.SCREENSHOTS / name).read_bytes())
        assert 100 <= width <= 8192 and 100 <= height <= 8192, name


def test_image_names_are_unique_and_exist():
    names = hp.image_names()
    assert len(names) == len(set(names))
    assert names[0] == hp.HERO.file
    assert all((hp.SCREENSHOTS / name).is_file() for name in names)


# ---- ページの骨組み ----

def test_page_is_a_japanese_html_document_that_fits_a_phone():
    page = render()
    parsed = Page(page)
    assert page.startswith("<!DOCTYPE html>\n<html lang=\"ja\">")
    assert parsed.find("meta")[0] == {"charset": "utf-8"}
    assert parsed.meta("viewport") == "width=device-width, initial-scale=1.0, viewport-fit=cover"
    assert parsed.meta("theme-color") == "#12151b"


def test_title_and_description_come_from_the_guide():
    page = render()
    assert "<title>葛飾キャンパスデイズ 遊び方</title>" in page
    intro = next(block for block in md.parse(GUIDE) if isinstance(block, md.Paragraph))
    assert Page(page).meta("description") == md.plain_text(intro.text)


def test_title_and_description_are_escaped():
    page = render("# 題 & \"引用\" 'x'\n\n導入 & \"説明\"\n\n## タイトル画面\n\nA\n\n## 1 日の流れ\n\nB\n")
    assert "<title>題 &amp; &quot;引用&quot; &#x27;x&#x27;</title>" in page
    assert 'content="導入 &amp; &quot;説明&quot;"' in page
    assert Page(page).meta("description") == '導入 & "説明"'


def test_page_needs_a_title_heading_first():
    for source in ("", "本文だけ\n", "## 章\n\n本文\n"):
        with pytest.raises(ValueError):
            render(source)


def test_page_stops_when_a_figure_heading_is_missing():
    with pytest.raises(ValueError, match="タイトル画面"):
        render("# 題\n\n導入\n\n## 1 日の流れ\n\n本文\n")


def test_unsupported_markdown_stops_the_page_with_a_line_number():
    with pytest.raises(md.UnsupportedMarkdown) as error:
        render(MINIMAL_GUIDE + "\n![図](a.png)\n")
    assert error.value.line == 13


# ---- 安全 ----

def test_csp_pins_the_style_to_the_css_hash():
    parsed = Page(render())
    digest = base64.b64encode(hashlib.sha256(parsed.style.encode("utf-8")).digest()).decode("ascii")
    policy = parsed.meta("Content-Security-Policy")
    assert f"style-src 'sha256-{digest}'" in policy
    assert parsed.style == hp.CSS


def test_csp_denies_everything_else():
    policy = Page(render()).meta("Content-Security-Policy")
    directives = {part.split()[0]: part.split()[1:] for part in policy.split("; ")}
    assert directives["default-src"] == ["'none'"]
    assert directives["img-src"] == ["'self'", "data:"]
    assert directives["base-uri"] == ["'none'"]
    assert directives["form-action"] == ["'none'"]
    assert set(directives) == {"default-src", "img-src", "style-src", "base-uri", "form-action"}
    assert "unsafe" not in policy and "script-src" not in policy


def test_csp_hash_follows_the_css():
    assert hp.csp("a{}") != hp.csp("b{}")
    assert hp.csp(hp.CSS) == hp.csp()


def test_page_has_no_script_inline_style_or_event_handler():
    parsed = Page(render())
    names = {name for name, _ in parsed.tags}
    assert "script" not in names and "iframe" not in names and "object" not in names and "form" not in names
    for name, attributes in parsed.tags:
        assert "style" not in attributes, name
        assert not any(key.startswith("on") for key in attributes), name


def test_page_loads_nothing_from_outside():
    parsed = Page(render())
    for attributes in parsed.find("img"):
        assert attributes["src"].startswith("img/")
    for attributes in parsed.find("link"):
        assert attributes["rel"] == "icon" and attributes["href"].startswith("data:image/svg+xml,")
    for name, attributes in parsed.tags:
        if name not in ("img", "link", "a"):
            assert "src" not in attributes and "href" not in attributes, name


def test_every_absolute_link_is_https_or_http_with_rel():
    anchors = Page(render()).find("a")
    absolute = [a for a in anchors if "://" in a["href"]]
    assert absolute, "遊び方には URL がある"
    for anchor in absolute:
        assert anchor["href"].startswith(("https://", "http://"))
        assert anchor["rel"] == "noopener noreferrer"


def test_other_links_stay_inside_the_page_or_go_back_to_the_game():
    for anchor in Page(render()).find("a"):
        href = anchor["href"]
        assert href in ("../", "#") or href.startswith(("#", "https://", "http://")), href


# ---- 元の md との一致 ----

def test_every_block_of_the_guide_is_in_the_page():
    page = render()
    for block in md.parse(GUIDE):
        assert md.render_block(block) in page


def test_page_follows_the_guide_text():
    changed = GUIDE + "\n## 新しい節\n\nここは新しく足した一文。\n"
    assert "ここは新しく足した一文。" not in render()
    assert "ここは新しく足した一文。" in render(changed)
    removed = GUIDE.replace("## 言語", "## 言葉")
    assert 'id="言語"' in render() and 'id="言語"' not in render(removed)


def test_guide_keeps_the_headings_the_page_and_the_zip_need():
    headings = {md.plain_text(block.text) for block in md.parse(GUIDE) if isinstance(block, md.Heading)}
    assert set(hp.FIGURES) <= headings
    assert {"操作", "動作環境", "既知の問題", "クレジット"} <= headings


def test_toc_lists_every_h2_and_h3_and_each_link_has_a_target():
    page = render()
    parsed = Page(page)
    ids = {attributes["id"] for name, attributes in parsed.tags if name in ("h1", "h2", "h3")}
    expected = [b for b in md.parse(GUIDE) if isinstance(b, md.Heading) and b.level in (2, 3)]
    assert len(parsed.toc_links) == len(expected)
    for link, heading in zip(parsed.toc_links, expected):
        assert link.startswith("#")
        assert unquote(link[1:]) == heading.slug
        assert heading.slug in ids


def test_heading_ids_are_unique():
    ids = [a["id"] for name, a in Page(render()).tags if name in ("h1", "h2", "h3")]
    assert len(ids) == len(set(ids))


# ---- ゲームへ戻るリンク ----

def test_page_links_back_to_the_game_from_top_middle_and_bottom():
    page = render()
    play = [a for a in Page(page).find("a") if a["href"] == hp.GAME_URL]
    assert hp.GAME_URL == "../"
    assert len(play) == 3 and all(a["class"] == "play" for a in play)
    assert page.index('<header class="bar">') < page.index('class="cta"') < page.index("<footer>")
    assert page.index("<footer>") < page.rindex('href="../"')


def test_play_buttons_sit_right_after_the_title_and_intro_before_the_first_section():
    page = render()
    assert page.index("<h1 ") < page.index('class="cta"') < page.index('class="toc"') < page.index("<h2 ")
    lead = page[page.index("</h1>"):page.index('class="cta"')]
    assert lead.count("<p>") == 1 and lead.endswith("</p>\n<p ")  # 導入の段落の直後に置く


# ---- 画像 ----

def test_every_image_has_size_alt_and_decoding():
    images = Page(render()).find("img")
    assert [a["src"] for a in images] == [f"img/{name}" for name in hp.image_names()]
    for attributes in images:
        assert (attributes["width"], attributes["height"]) == ("1600", "900")
        assert attributes["alt"].strip()
        assert attributes["decoding"] == "async"


def test_only_the_hero_image_loads_eagerly():
    images = Page(render()).find("img")
    assert "loading" not in images[0] and images[0]["src"] == f"img/{hp.HERO.file}"
    assert all(a["loading"] == "lazy" for a in images[1:])


def test_hero_image_is_first_in_main():
    page = render()
    assert page.index("<main>") < page.index('class="shot hero-shot"') < page.index("<h1 ")


def test_image_size_comes_from_the_sizes_given():
    sizes = dict(SIZES, **{hp.HERO.file: (800, 450)})
    page = hp.render_page(GUIDE, sizes)
    assert 'src="img/campus.jpg" width="800" height="450"' in page


def test_missing_size_stops():
    with pytest.raises(ValueError, match="title.png"):
        hp.render_page(GUIDE, {name: size for name, size in SIZES.items() if name != "title.png"})


def test_figures_sit_under_their_headings():
    page = render()
    positions = [match for match in range(len(page)) if page.startswith("<h", match)
                 and page[match + 2] in "123" and page[match + 3] == " "]
    for heading_text, figures in hp.FIGURES.items():
        slug = next(b.slug for b in md.parse(GUIDE) if isinstance(b, md.Heading)
                    and md.plain_text(b.text) == heading_text)
        start = page.index(f'id="{slug}"')
        end = min(p for p in positions if p > start)
        for figure in figures:
            assert start < page.index(f'src="img/{figure.file}"') < end, figure.file


def test_several_figures_form_a_two_column_gallery_and_one_does_not():
    page = render()
    assert page.count('<div class="gallery">') == 1
    gallery = page[page.index('<div class="gallery">'):]
    gallery = gallery[:gallery.index("\n</div>") + len("\n</div>")]
    assert gallery.count("<figure ") == len(hp.FIGURES["1 日の流れ"])
    assert 'src="img/title.png"' not in gallery


def test_figure_text_is_escaped(monkeypatch):
    figure = hp.Figure("title.png", 'a "b" <i> & c', "d < e & 'f'")
    monkeypatch.setattr(hp, "FIGURES", {"タイトル画面": (figure,), "1 日の流れ": ()})
    page = render()
    assert 'alt="a &quot;b&quot; &lt;i&gt; &amp; c"' in page
    assert "<figcaption>d &lt; e &amp; &#x27;f&#x27;</figcaption>" in page
    assert Page(page).find("img")[1]["alt"] == 'a "b" <i> & c'


def test_figure_without_a_caption_has_no_figcaption(monkeypatch):
    monkeypatch.setattr(hp, "FIGURES", {"タイトル画面": (hp.Figure("title.png", "代替文"),), "1 日の流れ": ()})
    assert "<figcaption>" not in render()


def test_file_names_in_img_src_are_url_quoted_and_escaped(monkeypatch):
    monkeypatch.setattr(hp, "FIGURES", {"タイトル画面": (hp.Figure("a b&c.png", "x"),), "1 日の流れ": ()})
    page = hp.render_page(GUIDE, dict(SIZES, **{"a b&c.png": (10, 10)}))
    assert 'src="img/a%20b%26c.png"' in page


# ---- 見た目の取り決め（ブラウザでの確認は撮影で行う） ----

def test_css_keeps_wide_things_inside_the_screen():
    css = hp.CSS
    assert ".shot img" in css and "width: 100%" in css and "height: auto" in css
    assert "overflow-x: auto" in css                      # 表の入れ物は横にスクロールさせる
    assert "@media (max-width: 40rem)" in css             # 狭い画面で札に組み替える
    assert "content: attr(data-label)" in css
    assert "env(safe-area-inset-left" in css and "viewport-fit" not in css
    assert "minmax(0, 1fr)" in css                        # 画像の枠が中身で広がらない


def test_css_uses_the_game_page_palette():
    for token in ("#12151b", "#1b2029", "#f2efe6", "#a7b0bf", "#3f7ad6"):
        assert token in hp.CSS


def test_page_uses_the_card_class_the_css_styles():
    page = render()
    assert '<table class="cards">' in page
    assert "table.cards" in hp.CSS


# ---- zip に足すファイル ----

def write_screenshots(directory: Path) -> dict[str, bytes]:
    directory.mkdir(parents=True, exist_ok=True)
    files = {}
    for index, name in enumerate(hp.image_names()):
        data = png_bytes(640, 360) if name.endswith(".png") else jpeg_bytes(640, 360)
        data += bytes([index]) * (index + 1)  # ファイルごとに中身を変える
        (directory / name).write_bytes(data)
        files[name] = data
    return files


@pytest.fixture
def source(tmp_path):
    guide = tmp_path / "HOW_TO_PLAY.md"
    guide.write_text(MINIMAL_GUIDE, encoding="utf-8", newline="\n")
    images = write_screenshots(tmp_path / "screenshots")
    return guide, tmp_path / "screenshots", images


def test_site_files_are_the_page_then_the_images(source):
    guide, screenshots, images = source
    files = hp.site_files(guide, screenshots)
    assert [name for name, _ in files] == [hp.PAGE_PATH] + [f"{hp.IMAGE_DIR}/{n}" for n in hp.image_names()]
    assert len({name for name, _ in files}) == len(files)
    assert dict(files)[hp.PAGE_PATH].decode("utf-8").startswith("<!DOCTYPE html>")
    for name, data in images.items():
        assert dict(files)[f"{hp.IMAGE_DIR}/{name}"] == data


def test_site_files_use_the_image_sizes_in_the_files(source):
    guide, screenshots, _ = source
    page = dict(hp.site_files(guide, screenshots))[hp.PAGE_PATH].decode("utf-8")
    assert 'width="640" height="360"' in page and 'width="1600"' not in page


def test_site_files_stop_when_an_image_is_missing(source):
    guide, screenshots, _ = source
    (screenshots / "talk.jpg").unlink()
    with pytest.raises(FileNotFoundError, match="talk.jpg"):
        hp.site_files(guide, screenshots)


def test_site_files_stop_when_an_image_cannot_be_read(source):
    guide, screenshots, _ = source
    (screenshots / "talk.jpg").write_bytes(b"<html>not a jpeg</html>")
    with pytest.raises(ValueError, match="画像を読めない"):
        hp.site_files(guide, screenshots)


def test_site_files_stop_when_the_guide_is_missing(source):
    guide, screenshots, _ = source
    guide.unlink()
    with pytest.raises(OSError):
        hp.site_files(guide, screenshots)


def test_site_files_stop_on_unsupported_markdown(source):
    guide, screenshots, _ = source
    guide.write_text(MINIMAL_GUIDE + "\n> 引用\n", encoding="utf-8", newline="\n")
    with pytest.raises(md.UnsupportedMarkdown, match="13 行目"):
        hp.site_files(guide, screenshots)


def test_site_files_limit_the_total_size(source):
    guide, screenshots, _ = source
    total = sum(len(data) for _, data in hp.site_files(guide, screenshots))
    assert sum(len(data) for _, data in hp.site_files(guide, screenshots, max_bytes=total)) == total
    with pytest.raises(ValueError, match="上限"):
        hp.site_files(guide, screenshots, max_bytes=total - 1)


# ---- 実物 ----

def test_real_site_files():
    files = hp.site_files()
    names = [name for name, _ in files]
    assert names[0] == "howto/index.html"
    assert names[1:] == [f"howto/img/{name}" for name in hp.image_names()]
    assert all(name.startswith("howto/") for name in names)
    assert sum(len(data) for _, data in files) <= hp.MAX_PAGE_BYTES
    for name, data in files[1:]:
        assert data == (hp.SCREENSHOTS / name.rsplit("/", 1)[1]).read_bytes()


def test_real_page_matches_what_render_page_gives():
    page = dict(hp.site_files())[hp.PAGE_PATH].decode("utf-8")
    sizes = {name: hp.image_size((hp.SCREENSHOTS / name).read_bytes()) for name in hp.image_names()}
    assert page == hp.render_page(GUIDE, sizes)


def test_page_size_leaves_room_under_the_cap():
    total = sum(len(data) for _, data in hp.site_files())
    assert total < hp.MAX_PAGE_BYTES * 0.75


def test_page_is_a_small_fraction_of_the_game():
    # ゲームの配信サイズの上限（tools/deploy_pages.py）に対して、ページは 3% に収まる。
    import deploy_pages
    assert hp.MAX_PAGE_BYTES < 0.03 * deploy_pages.MAX_TOTAL_BYTES

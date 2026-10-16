"""tools/mini_markdown.py のテスト (#18)。python -m pytest tests で回す。

遊び方（docs/HOW_TO_PLAY.md）を Pages のページにする変換器。扱える書き方を決めて出力の形を固定し、
扱わない書き方は黙って落とさず、行番号つきで止めることを確かめる。
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import mini_markdown as md  # noqa: E402

GUIDE = ROOT / "docs" / "HOW_TO_PLAY.md"


def only_block(markdown: str) -> md.Block:
    blocks = md.parse(markdown)
    assert len(blocks) == 1, blocks
    return blocks[0]


# ---- 見出し ----

def test_headings_carry_level_text_slug_and_line():
    blocks = md.parse("# 題\n\n## 1 日の流れ\n\n### 小さな見出し")
    assert [(b.level, b.text, b.slug, b.line) for b in blocks] == [
        (1, "題", "題", 1), (2, "1 日の流れ", "1-日の流れ", 3), (3, "小さな見出し", "小さな見出し", 5)]


def test_heading_html_has_id():
    assert md.convert("## 操作") == '<h2 id="操作">操作</h2>'


def test_duplicate_heading_slugs_get_a_suffix():
    slugs = [block.slug for block in md.parse("## 操作\n\n## 操作\n\n## 操作")]
    assert slugs == ["操作", "操作-2", "操作-3"]


def test_heading_slug_ignores_emphasis_and_code_marks():
    assert only_block("## **太い** と `code`").slug == "太い-と-code"


def test_heading_id_is_escaped():
    assert md.convert('## a"b&c') == '<h2 id="a&quot;b&amp;c">a&quot;b&amp;c</h2>'


def test_hash_without_space_is_a_paragraph():
    assert md.convert("#タグ") == "<p>#タグ</p>"


@pytest.mark.parametrize("source, line", [
    ("# 題\n\n#### 深すぎる", 3),
    ("# 題\n\n##", 3),
    ("###### 6", 1),
])
def test_unusable_headings_stop(source, line):
    with pytest.raises(md.UnsupportedMarkdown) as error:
        md.parse(source)
    assert error.value.line == line


# ---- 段落 ----

def test_paragraphs_split_on_blank_lines():
    assert md.convert("一つ目\n\n二つ目") == "<p>一つ目</p>\n<p>二つ目</p>"


def test_newline_in_a_paragraph_is_a_space_between_latin_words():
    assert md.convert("one\ntwo") == "<p>one two</p>"


def test_newline_between_japanese_characters_adds_no_space():
    assert md.convert("日本語の\n文章") == "<p>日本語の文章</p>"


def test_newline_between_japanese_and_latin_adds_a_space():
    assert md.convert("日本語\nWASD") == "<p>日本語 WASD</p>"


def test_heading_right_after_a_paragraph_line_starts_a_new_block():
    kinds = [type(block).__name__ for block in md.parse("段落\n## 見出し\n本文")]
    assert kinds == ["Paragraph", "Heading", "Paragraph"]


def test_crlf_and_bom_are_accepted():
    assert md.convert("\ufeff# 題\r\n\r\n本文\r\n") == '<h1 id="題">題</h1>\n<p>本文</p>'


def test_trailing_spaces_are_ignored():
    assert md.convert("本文   \n") == "<p>本文</p>"


# ---- 箇条書き ----

def test_bullet_list():
    assert md.convert("- 一\n- 二") == "<ul>\n<li>一</li>\n<li>二</li>\n</ul>"


def test_ordered_list_starts_at_one():
    assert md.convert("1. 一\n2. 二\n3. 三") == "<ol>\n<li>一</li>\n<li>二</li>\n<li>三</li>\n</ol>"


def test_indented_line_continues_the_previous_item():
    block = only_block("- 長い\n  続き\n- 次")
    assert block.items == ("長い続き", "次")


def test_list_ends_at_a_blank_line_and_the_next_list_is_separate():
    kinds = [(type(block).__name__, getattr(block, "ordered", None)) for block in md.parse("- a\n\n1. b")]
    assert kinds == [("ListBlock", False), ("ListBlock", True)]


def test_ordered_list_after_bullets_without_blank_line_is_a_new_list():
    blocks = md.parse("- a\n1. b")
    assert [block.ordered for block in blocks] == [False, True]


@pytest.mark.parametrize("source, line", [
    ("- 親\n  - 入れ子", 2),
    ("- 項目\n続き", 2),
    ("2. 二から始まる", 1),
    ("- a\n-", 2),
    ("-", 1),
    ("* アスタリスク", 1),
    ("+ プラス", 1),
    ("1) かっこ", 1),
    ("段落\n* 項目", 2),
])
def test_unsupported_list_shapes_stop(source, line):
    with pytest.raises(md.UnsupportedMarkdown) as error:
        md.parse(source)
    assert error.value.line == line


# ---- 表 ----

TABLE_3 = "| 操作 | キーボード | ゲームパッド |\n| --- | --- | --- |\n| 移動 | WASD | 左スティック |\n"
TABLE_2 = "| キー | 動き |\n| --- | --- |\n| 左 | 回る |\n"


def test_three_column_table_becomes_cards_with_labels():
    assert md.convert(TABLE_3) == (
        '<div class="table-wrap"><table class="cards"><thead><tr><th scope="col">操作</th>'
        '<th scope="col">キーボード</th><th scope="col">ゲームパッド</th></tr></thead>\n<tbody>\n'
        '<tr><th scope="row">移動</th><td data-label="キーボード">WASD</td>'
        '<td data-label="ゲームパッド">左スティック</td></tr>\n</tbody></table></div>')


def test_two_column_table_stays_a_plain_table():
    assert md.convert(TABLE_2) == (
        '<div class="table-wrap"><table><thead><tr><th scope="col">キー</th><th scope="col">動き</th></tr>'
        '</thead>\n<tbody>\n<tr><th scope="row">左</th><td>回る</td></tr>\n</tbody></table></div>')


def test_empty_header_cell_is_not_a_column_header():
    html = md.convert("| | 値 |\n| --- | --- |\n| a | b |\n")
    assert "<td></td><th scope=\"col\">値</th>" in html


def test_table_cells_go_through_inline_formatting():
    html = md.convert("| 名前 | 内容 |\n| --- | --- |\n| **W** | `A < B` |\n")
    assert '<th scope="row"><strong>W</strong></th>' in html
    assert "<td><code>A &lt; B</code></td>" in html


def test_table_may_be_followed_by_a_heading_without_a_blank_line():
    kinds = [type(block).__name__ for block in md.parse(TABLE_2 + "## 次")]
    assert kinds == ["Table", "Heading"]


@pytest.mark.parametrize("source, line", [
    ("| a | b |", 1),                                   # 区切りの行が無い
    ("| a | b |\n| c | d |", 2),                        # 2 行目が区切りでない
    ("| a | b |\n| :--- | --- |\n| 1 | 2 |", 2),        # 寄せ指定
    ("| a | b |\n| --- | --- |\n| 1 |", 3),             # 列の数が違う
    ("| a | b |\n| --- | --- |\n| 1 | 2 | 3 |", 3),
    ("| a | b\n| --- | --- |", 1),                      # 行末の | が無い
    ("| a | b |\n| --- | --- |\n| 1 \\| x | 2 |", 3),   # | のエスケープ
    ("| a | b |\n| --- | --- |\n| 1 | 2 |\n続き", 4),   # 表の直後が空行でない
])
def test_unsupported_table_shapes_stop(source, line):
    with pytest.raises(md.UnsupportedMarkdown) as error:
        md.parse(source)
    assert error.value.line == line


# ---- 文字の中 ----

def test_bold_and_code():
    assert md.convert("**太字**と`code`") == "<p><strong>太字</strong>と<code>code</code></p>"


def test_code_is_not_formatted_further():
    assert md.convert("`**not bold** https://example.com`") == (
        "<p><code>**not bold** https://example.com</code></p>")


def test_bare_url_becomes_a_link_with_rel():
    assert md.convert("見る: https://example.com/a?b=1&c=2 まで") == (
        '<p>見る: <a href="https://example.com/a?b=1&amp;c=2" rel="noopener noreferrer">'
        "https://example.com/a?b=1&amp;c=2</a> まで</p>")


def test_url_stops_before_japanese_punctuation_and_trailing_dot():
    html = md.convert("https://example.com/x。と https://example.com/y.")
    assert '<a href="https://example.com/x" rel="noopener noreferrer">https://example.com/x</a>。と ' in html
    assert html.endswith('<a href="https://example.com/y" rel="noopener noreferrer">https://example.com/y</a>.</p>')


def test_only_http_and_https_are_linked():
    html = md.convert("javascript:alert(1) と ftp://example.com と data:text/html,x")
    assert "<a " not in html


def test_scheme_alone_is_not_a_link():
    assert "<a " not in md.convert("https:// だけ")


def test_url_in_bold_is_linked_inside_the_strong():
    html = md.convert("**https://example.com**")
    assert html == ('<p><strong><a href="https://example.com" rel="noopener noreferrer">'
                    "https://example.com</a></strong></p>")


def test_text_is_html_escaped():
    assert md.convert("a \"b\" & 'c' > d < e") == "<p>a &quot;b&quot; &amp; &#x27;c&#x27; &gt; d &lt; e</p>"


def test_list_item_text_is_html_escaped():
    assert md.convert("- 1 < 2 と R&D") == "<ul>\n<li>1 &lt; 2 と R&amp;D</li>\n</ul>"


# ---- 止める書き方 ----

@pytest.mark.parametrize("source, line", [
    ("![図](a.png)", 1),
    ("[文字](https://example.com)", 1),
    ("段落\n\n見て [これ](https://example.com)", 3),
    ("<script>alert(1)</script>", 1),
    ("本文 <b>太い</b>", 1),
    ("本文 <!-- comment -->", 1),
    ("> 引用", 1),
    ("```\ncode\n```", 1),
    ("~~~\ncode\n~~~", 1),
    ("---", 1),
    ("***", 1),
    ("題\n===", 2),
    ("題\n---", 2),
    ("*強調*", 1),
    ("_強調_ と **太い** と *強調*", 1),
    ("~~消す~~", 1),
    ("&copy; 2026", 1),
    ("&#169;", 1),
    ("a \\# b", 1),
    ("閉じない ` 記号", 1),
    ("**閉じない", 1),
    ("タブ\tあり", 1),
    ("制御\x07文字", 1),
    ("    字下げ", 1),
    ("段落\n    字下げ", 2),
])
def test_unsupported_syntax_stops_with_the_line_number(source, line):
    with pytest.raises(md.UnsupportedMarkdown) as error:
        md.parse(source)
    assert error.value.line == line
    assert f"{line} 行目" in str(error.value)


def test_unsupported_markdown_is_a_value_error():
    assert issubclass(md.UnsupportedMarkdown, ValueError)


def test_less_than_before_a_space_or_digit_is_plain_text():
    assert md.convert("1 < 2 and 3 <4") == "<p>1 &lt; 2 and 3 &lt;4</p>"


def test_underscore_and_ampersand_text_without_a_semicolon_are_plain():
    assert md.convert("snake_case と R&D") == "<p>snake_case と R&amp;D</p>"


def test_unsupported_syntax_in_a_table_cell_reports_the_row():
    with pytest.raises(md.UnsupportedMarkdown) as error:
        md.parse("| a | b |\n| --- | --- |\n| 1 | ![x](y) |\n")
    assert error.value.line == 3


# ---- 実物 ----

def test_real_guide_converts_and_keeps_every_heading_and_table():
    source = GUIDE.read_text(encoding="utf-8")
    blocks = md.parse(source)
    headings = [line for line in source.splitlines() if line.startswith("#")]
    assert [type(b).__name__ for b in blocks].count("Heading") == len(headings)
    separators = [line for line in source.splitlines() if line.startswith("| ---")]
    assert [type(b).__name__ for b in blocks].count("Table") == len(separators)
    html = md.to_html(blocks)
    assert "<script" not in html and " style=" not in html and "<img" not in html


def test_real_guide_heading_slugs_are_unique():
    slugs = [b.slug for b in md.parse(GUIDE.read_text(encoding="utf-8")) if isinstance(b, md.Heading)]
    assert len(slugs) == len(set(slugs))

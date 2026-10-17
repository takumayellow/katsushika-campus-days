"""docs/HOW_TO_PLAY.md が使う範囲の Markdown を HTML にする小さな変換器 (#18)。

標準ライブラリだけで書いてある。使える記法は次のとおり。

    見出し       # から ### まで（行頭の # の数）
    段落         空行で区切る。段落の中の改行は空白 1 つにする（日本語の文字どうしなら空白なし）
    箇条書き     "- 項目" と "1. 項目"（番号は 1 から）。2 文字以上字下げした行は直前の項目の続き
    表           "| a | b |" の行と、2 行目の "| --- | --- |"
    強調         **太字**
    コード       `code`
    リンク       https:// か http:// で始まる文字列は、そのままリンクにする

これ以外の書き方は、黙って落としたり別の見た目にしたりせず UnsupportedMarkdown で止める
（画像、[文字](URL)、参照リンクと脚注、生の HTML、引用、コードブロック、入れ子の箇条書き、
チェックボックス、& で始まる文字参照、バックスラッシュのエスケープ、* と _ での強調、
~ での取り消し線、行末の空白 2 つやバックスラッシュでの改行、見出しの下線、見出し末尾の #、
先頭が | でない表、括弧を含む URL など）。止めるときは行番号を添える。
語の中の _（snake_case）と 1 つだけの ~ は文字のまま通す。止まった記号を文字として書くときは ` で囲む。

文字はすべて HTML としてエスケープして出す。リンクの先は http と https だけで、
リンクにする文字は ASCII の URL に使える文字に限る。
"""

from __future__ import annotations

import html
import re
import unicodedata
from dataclasses import dataclass
from typing import Iterable, Union

MAX_HEADING_LEVEL = 3


class UnsupportedMarkdown(ValueError):
    """この変換器が扱わない書き方。line は 1 始まりの行番号（分かるとき）。"""

    def __init__(self, reason: str, line: int | None = None):
        self.reason = reason
        self.line = line
        super().__init__(f"{line} 行目: {reason}" if line else reason)


@dataclass(frozen=True)
class Heading:
    level: int
    text: str
    slug: str
    line: int


@dataclass(frozen=True)
class Paragraph:
    text: str
    line: int


@dataclass(frozen=True)
class ListBlock:
    ordered: bool
    items: tuple[str, ...]
    line: int


@dataclass(frozen=True)
class Table:
    header: tuple[str, ...]
    rows: tuple[tuple[str, ...], ...]
    line: int


Block = Union[Heading, Paragraph, ListBlock, Table]

_CONTROL = re.compile(r"[\x00-\x1f\x7f]")
_HEADING = re.compile(r"(#{1,6})(?:[ ]+(\S.*))?")
_ITEM = re.compile(r"(?:(?P<bullet>-)|(?P<number>[0-9]{1,9})\.)[ ]+(?P<body>\S.*)")
_EMPTY_ITEM = re.compile(r"(?:[-*+]|[0-9]{1,9}[.)])")
_OTHER_MARKER = re.compile(r"(?:[*+]|[0-9]{1,9}\))[ ]")
# - は 2 文字でも、段落の下に置くと見出しの下線になる
_BREAK = re.compile(r"(?:-[ ]*){2,}|(?:\*[ ]*){3,}|(?:_[ ]*){3,}|=+")
_FENCE = re.compile(r"```|~~~")
_SEPARATOR_CELL = re.compile(r":?-+:?")
_LINE_BREAK = re.compile(r"(?:[ ]{2,}|\\)$")
_TASK = re.compile(r"\[[ xX]\](?:[ ]|$)")
_CLOSING_HASHES = re.compile(r"(?:^|[ ])#+$")

_CODE = re.compile(r"`([^`]+)`")
_BOLD = re.compile(r"\*\*(?=\S)(.+?)(?<=\S)\*\*")
# リンクにする文字。ASCII の URL に使える文字だけで、日本語の句読点や括弧の手前で切れる。
_URL = re.compile(r"https?://[A-Za-z0-9._~:/?#@!$&+,;=%-]+")
_URL_TAIL = ".,;:!?"
_ENTITY = re.compile(r"&(?:#[0-9]+|#[xX][0-9A-Fa-f]+|[A-Za-z][A-Za-z0-9]*);")
_TAG = re.compile(r"<[A-Za-z/!?]")
_ESCAPE = re.compile(r"\\[!-/:-@\[-`{-~]")
_DEFINITION = re.compile(r"\[[^\]]*\]:")
# 語の端にある _（前後のどちらかが英数字でない）。snake_case のような語の中の _ は当たらない。
_EDGE_UNDERSCORE = re.compile(r"(?<![^\W_])_|_(?![^\W_])")


def escape(text: str) -> str:
    return html.escape(text, quote=True)


def plain_text(text: str) -> str:
    """強調とコードの記号を外した文字。見出しの id や、図の置き場所の照合に使う。"""
    return text.replace("**", "").replace("`", "")


# ---- 行の読み取り ----

def _lines(markdown: str) -> list[str]:
    text = markdown.removeprefix("\ufeff").replace("\r\n", "\n").replace("\r", "\n")
    lines = text.split("\n")
    for number, line in enumerate(lines, 1):
        if _CONTROL.search(line):
            raise UnsupportedMarkdown("タブと制御文字は使えない（空白にする）", number)
        if _breaks_into_next_line(lines, number):
            raise UnsupportedMarkdown("行末の空白 2 つと \\ では改行できない（行を分けるなら空行を挟む）", number)
    return [line.rstrip() for line in lines]


def _breaks_into_next_line(lines: list[str], number: int) -> bool:
    """number 行目（1 始まり）が、行末の空白 2 つか \\ で、空でない次の行へ続いているか。"""
    line = lines[number - 1]
    return (_LINE_BREAK.search(line) is not None and line.strip() != ""
            and number < len(lines) and lines[number].strip() != "")


def _reject_line(text: str, number: int) -> None:
    """行の頭が、対応していないブロックの書き出しなら止める（text は字下げを外した行）。"""
    if text.startswith(">"):
        raise UnsupportedMarkdown("引用（>）は使えない", number)
    if _FENCE.match(text):
        raise UnsupportedMarkdown("コードブロックは使えない（コードは ` で囲む）", number)
    if _BREAK.fullmatch(text):
        raise UnsupportedMarkdown("区切り線と見出しの下線は使えない", number)
    if _EMPTY_ITEM.fullmatch(text) or _OTHER_MARKER.match(text):
        raise UnsupportedMarkdown("箇条書きは「- 項目」と「1. 項目」だけ使える（中身が空の項目も不可）", number)


def _starts_block(line: str) -> bool:
    return (_HEADING.fullmatch(line) is not None or line.startswith("|")
            or _ITEM.fullmatch(line) is not None)


def _is_wide(char: str) -> bool:
    return unicodedata.east_asian_width(char) in ("W", "F")


def _join(parts: list[str]) -> str:
    """折り返しの改行を空白に直す。前後が日本語の文字なら空白は入れない。"""
    text = parts[0]
    for part in parts[1:]:
        text += part if _is_wide(text[-1]) and _is_wide(part[0]) else " " + part
    return text


def _check_inline(text: str, number: int) -> None:
    """インラインの書き方に対応していなければ、行番号つきで止める。"""
    try:
        inline(text)
    except UnsupportedMarkdown as error:
        raise UnsupportedMarkdown(error.reason, number) from None


def _slug(text: str, taken: set[str]) -> str:
    base = re.sub(r"\s+", "-", plain_text(text).strip()) or "section"
    slug, count = base, 2
    while slug in taken:
        slug, count = f"{base}-{count}", count + 1
    taken.add(slug)
    return slug


# ---- ブロック ----

def _read_paragraph(lines: list[str], index: int) -> tuple[Paragraph, int]:
    start = index
    parts: list[str] = []
    while index < len(lines) and lines[index]:
        line = lines[index]
        if parts and _starts_block(line):
            break
        if line[0] == " ":
            raise UnsupportedMarkdown("字下げした行は箇条書きの続きにだけ使える", index + 1)
        _reject_line(line, index + 1)
        parts.append(line)
        index += 1
    text = _join(parts)
    _check_inline(text, start + 1)
    return Paragraph(text, start + 1), index


def _read_list(lines: list[str], index: int) -> tuple[ListBlock, int]:
    start = index
    first = _ITEM.fullmatch(lines[index])
    ordered = first.group("number") is not None
    if ordered and int(first.group("number")) != 1:
        raise UnsupportedMarkdown("番号つきの箇条書きは 1 から始める", index + 1)
    items: list[tuple[int, list[str]]] = []
    while index < len(lines) and lines[index]:
        line = lines[index]
        item = _ITEM.fullmatch(line)
        if line.startswith("  "):
            text = line.strip()
            _reject_line(text, index + 1)
            if _ITEM.fullmatch(text):
                raise UnsupportedMarkdown("入れ子の箇条書きは使えない", index + 1)
            items[-1][1].append(text)
        elif item:
            if (item.group("number") is not None) != ordered:
                break
            _reject_line(line, index + 1)
            if _TASK.match(item.group("body")):
                raise UnsupportedMarkdown("チェックボックス（- [ ]）は使えない", index + 1)
            items.append((index + 1, [item.group("body")]))
        elif _starts_block(line):
            break
        else:
            raise UnsupportedMarkdown("箇条書きの続きの行は 2 文字以上字下げする", index + 1)
        index += 1
    joined = []
    for number, parts in items:
        text = _join(parts)
        _check_inline(text, number)
        joined.append(text)
    return ListBlock(ordered, tuple(joined), start + 1), index


def _split_row(line: str, number: int) -> tuple[str, ...]:
    if len(line) < 2 or not line.endswith("|"):
        raise UnsupportedMarkdown("表の行は | で始めて | で終える", number)
    if "\\|" in line:
        raise UnsupportedMarkdown("表の中で | は使えない", number)
    return tuple(cell.strip() for cell in line[1:-1].split("|"))


def _read_table(lines: list[str], index: int) -> tuple[Table, int]:
    start = index
    rows: list[tuple[str, ...]] = []
    while index < len(lines) and lines[index].startswith("|"):
        rows.append(_split_row(lines[index], index + 1))
        index += 1
    if len(rows) < 2:
        raise UnsupportedMarkdown("表には見出しの行と、その下の | --- | --- | の行がいる", start + 1)
    header, separator, *body = rows
    if not all(_SEPARATOR_CELL.fullmatch(cell) for cell in separator):
        raise UnsupportedMarkdown("表の 2 行目は | --- | --- | の形にする", start + 2)
    if any(":" in cell for cell in separator):
        raise UnsupportedMarkdown("表の列の寄せ指定（:）は使えない", start + 2)
    for offset, row in enumerate(rows[1:], 1):
        if len(row) != len(header):
            raise UnsupportedMarkdown("表の列の数が見出しの行と違う", start + 1 + offset)
    for offset, row in enumerate(rows):
        if offset != 1:
            for cell in row:
                _check_inline(cell, start + 1 + offset)
    if index < len(lines) and lines[index] and not _HEADING.fullmatch(lines[index]):
        raise UnsupportedMarkdown("表の次の行は空行にする", index + 1)
    return Table(header, tuple(body), start + 1), index


def parse(markdown: str) -> tuple[Block, ...]:
    """Markdown をブロックに分ける。対応していない書き方があれば UnsupportedMarkdown。"""
    lines = _lines(markdown)
    blocks: list[Block] = []
    taken: set[str] = set()
    index = 0
    while index < len(lines):
        line = lines[index]
        number = index + 1
        if not line:
            index += 1
            continue
        if line[0] == " ":
            raise UnsupportedMarkdown("字下げした行は箇条書きの続きにだけ使える", number)
        _reject_line(line, number)
        heading = _HEADING.fullmatch(line)
        if heading:
            level, text = len(heading.group(1)), heading.group(2)
            if text is None:
                raise UnsupportedMarkdown("見出しの文字が無い", number)
            if level > MAX_HEADING_LEVEL:
                raise UnsupportedMarkdown(f"見出しは {'#' * MAX_HEADING_LEVEL} までしか使えない", number)
            if _CLOSING_HASHES.search(text):
                raise UnsupportedMarkdown("見出しの末尾に # は付けない", number)
            _check_inline(text, number)
            blocks.append(Heading(level, text, _slug(text, taken), number))
            index += 1
        elif line.startswith("|"):
            table, index = _read_table(lines, index)
            blocks.append(table)
        elif _ITEM.fullmatch(line):
            items, index = _read_list(lines, index)
            blocks.append(items)
        else:
            paragraph, index = _read_paragraph(lines, index)
            blocks.append(paragraph)
    return tuple(blocks)


# ---- インライン ----

def _reject_inline(text: str) -> None:
    """コードと太字を取り除いた残りの文字に、対応していない記法が残っていれば止める。"""
    if "*" in text:
        raise UnsupportedMarkdown("* での強調は使えない（太字は **語** と書く）")
    if "`" in text:
        raise UnsupportedMarkdown("閉じていない ` がある")
    if "](" in text:
        raise UnsupportedMarkdown("リンク記法 [文字](URL) と画像は使えない（URL をそのまま書く）")
    if "][" in text or _DEFINITION.search(text):
        raise UnsupportedMarkdown("参照リンク（[文字][名前] と [名前]: URL）は使えない（URL をそのまま書く）")
    if "[^" in text:
        raise UnsupportedMarkdown("脚注（[^名前]）は使えない")
    if any(text[match.end():match.end() + 1] == "(" for match in _URL.finditer(text)):
        raise UnsupportedMarkdown("括弧を含む URL はリンクが途中で切れるので使えない")
    if _EDGE_UNDERSCORE.search(text):
        raise UnsupportedMarkdown("_ での強調は使えない（太字は **語** と書く。語の中の _ は文字のまま通る）")
    if text.count("~") >= 2:
        raise UnsupportedMarkdown("~ で囲む取り消し線は使えない（~ は 1 つだけなら文字のまま通る）")
    if "|" in text:
        raise UnsupportedMarkdown("表は | で始めて | で終える（| を文字として書くなら ` で囲む）")
    if _TAG.search(text):
        raise UnsupportedMarkdown("HTML のタグに見える < は使えない")
    if _ENTITY.search(text):
        raise UnsupportedMarkdown("& で始まる文字参照は使えない（文字をそのまま書く）")
    if _ESCAPE.search(text):
        raise UnsupportedMarkdown("バックスラッシュでのエスケープは使えない")


def _plain(text: str) -> str:
    """装飾の無い文字。URL はリンクにし、そのほかはエスケープする。"""
    _reject_inline(text)
    out = []
    position = 0
    for match in _URL.finditer(text):
        url = match.group().rstrip(_URL_TAIL)
        if not url.partition("://")[2]:
            continue
        out.append(escape(text[position:match.start()]))
        out.append(f'<a href="{escape(url)}" rel="noopener noreferrer">{escape(url)}</a>')
        position = match.start() + len(url)
    out.append(escape(text[position:]))
    return "".join(out)


def _bold(text: str) -> str:
    out = []
    for index, part in enumerate(_BOLD.split(text)):
        out.append(f"<strong>{_plain(part)}</strong>" if index % 2 else _plain(part))
    return "".join(out)


def inline(text: str) -> str:
    """段落・見出し・セルの中の文字を HTML にする。"""
    out = []
    for index, part in enumerate(_CODE.split(text)):
        out.append(f"<code>{escape(part)}</code>" if index % 2 else _bold(part))
    return "".join(out)


# ---- HTML ----

def _table_html(table: Table) -> str:
    # 3 列以上の表は、狭い画面で行ごとの札に組み替える。札の中で列の名前を出すための印を付ける。
    labelled = len(table.header) > 2
    head = "".join(f'<th scope="col">{inline(cell)}</th>' if cell else "<td></td>"
                   for cell in table.header)
    rows = []
    for row in table.rows:
        cells = []
        for column, cell in enumerate(row):
            if column == 0 and len(row) > 1:
                cells.append(f'<th scope="row">{inline(cell)}</th>')
                continue
            label = f' data-label="{escape(plain_text(table.header[column]))}"' if labelled else ""
            # 札の td は grid なので、太字やコードが並ぶセルが割れないよう中身を 1 つの span にまとめる
            content = f"<span>{inline(cell)}</span>" if labelled else inline(cell)
            cells.append(f"<td{label}>{content}</td>")
        rows.append(f"<tr>{''.join(cells)}</tr>")
    table_tag = '<table class="cards">' if labelled else "<table>"
    return ('<div class="table-wrap">' + table_tag +
            f"<thead><tr>{head}</tr></thead>\n<tbody>\n" + "\n".join(rows) + "\n</tbody></table></div>")


def render_block(block: Block) -> str:
    if isinstance(block, Heading):
        return f'<h{block.level} id="{escape(block.slug)}">{inline(block.text)}</h{block.level}>'
    if isinstance(block, Paragraph):
        return f"<p>{inline(block.text)}</p>"
    if isinstance(block, ListBlock):
        tag = "ol" if block.ordered else "ul"
        items = "\n".join(f"<li>{inline(item)}</li>" for item in block.items)
        return f"<{tag}>\n{items}\n</{tag}>"
    if isinstance(block, Table):
        return _table_html(block)
    raise TypeError(f"HTML にできないブロック: {type(block).__name__}")


def to_html(blocks: Iterable[Block]) -> str:
    return "\n".join(render_block(block) for block in blocks)


def convert(markdown: str) -> str:
    return to_html(parse(markdown))

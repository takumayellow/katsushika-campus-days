"""辞書の dialogue.* が会話 JSON の text / text_en と一致していること (#371)。python -m pytest tests で回す。

ゲームが読むのは Dialogue/*.json (DialogueLine.DisplayText) で、辞書 (Localization/ja.json, en.json) の
dialogue.<npc>.<話題 id>.<n> は外部ツール・検索用の写し (docs/CONTENT_SPEC.md §3)。会話 JSON だけ直して
辞書を直し忘れると、検索で見つかる文と画面に出る文が食い違う。写しがある行だけを見る（写しの無い行は
表示に影響しない）。
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

ASSETS = Path(__file__).resolve().parents[1] / "unity" / "KatsushikaCampusDays" / "Assets"
# ゲームが読むのは Resources/KCD。Data はそのコピー元 (DataBundler) で、どちらにも同じものがある
ROOTS = ["Data", "Resources/KCD"]
# (辞書の言語, 会話 JSON の行で見る項目)
LOCALES = [("ja", "text"), ("en", "text_en")]
CASES = [(root, locale, field) for root in ROOTS for locale, field in LOCALES]
CASE_IDS = [f"{root}-{locale}" for root, locale, _ in CASES]
SHOWN = 10  # 失敗メッセージに並べるキーの数


def _read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _dialogue_lines(root: str) -> dict[str, dict]:
    """会話 JSON の話題の行を {"dialogue.<npc>.<話題 id>.<n>": 行} にする。

    文字列だけの行は NPC の台詞で、text だけを持つ行として扱う (DialogueData.ParseLines と同じ)。
    話題に id が無ければ t<順番> (DialogueData.ParseTopic と同じ)。
    """
    lines: dict[str, dict] = {}
    for path in sorted((ASSETS / root / "Dialogue").glob("*.json")):
        data = _read_json(path)
        for index, topic in enumerate(data.get("topics", [])):
            prefix = f"dialogue.{data['id']}.{topic.get('id', f't{index}')}"
            for n, line in enumerate(topic.get("lines", [])):
                lines[f"{prefix}.{n}"] = {"text": line} if isinstance(line, str) else line
    return lines


def _copies(root: str, locale: str) -> dict[str, str]:
    strings = _read_json(ASSETS / root / "Localization" / f"{locale}.json")["strings"]
    return {key: value for key, value in strings.items() if key.startswith("dialogue.")}


def _names(keys: list[str]) -> str:
    return ", ".join(keys[:SHOWN]) + (" ほか" if len(keys) > SHOWN else "")


@pytest.mark.parametrize(("root", "locale"), [(root, locale) for root, locale, _ in CASES], ids=CASE_IDS)
def test_every_copy_has_a_dialogue_line(root: str, locale: str):
    """辞書の dialogue.* には、対応する会話 JSON の行がある（話題や行を直して写しが残った状態を見つける）。"""
    copies = _copies(root, locale)
    assert copies, f"{root}/Localization/{locale}.json に dialogue.* が 1 つも無い（キーの形が変わったなら、このテストも合わせる）"
    orphans = sorted(set(copies) - set(_dialogue_lines(root)))
    assert not orphans, (
        f"{root}/Localization/{locale}.json に、対応する会話 JSON の行が無い dialogue.* がある "
        f"({len(orphans)} 件): {_names(orphans)}。辞書の写しを会話 JSON に合わせて直す"
    )


@pytest.mark.parametrize(("root", "locale", "field"), CASES, ids=CASE_IDS)
def test_every_copy_matches_the_dialogue_json(root: str, locale: str, field: str):
    """辞書の dialogue.* は、会話 JSON の同じ行の text (ja) / text_en (en) と一致する。"""
    lines = _dialogue_lines(root)
    stale = sorted(key for key, value in _copies(root, locale).items()
                   if key in lines and value != lines[key].get(field, ""))
    assert not stale, (
        f"{root}/Localization/{locale}.json の dialogue.* が会話 JSON の {field} とずれている "
        f"({len(stale)} 件): {_names(stale)}。ゲームが読むのは会話 JSON なので、辞書の写しを会話 JSON に揃える"
    )

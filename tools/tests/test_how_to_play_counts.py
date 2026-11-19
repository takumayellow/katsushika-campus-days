"""遊び方の説明（配布 zip の README）に書いた本数と個数が、ゲームのデータと合っていることを確かめる (#410)。"""

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "unity" / "KatsushikaCampusDays" / "Assets" / "Data"
HOW_TO_PLAY = ROOT / "docs" / "HOW_TO_PLAY.md"


def _load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def _quest_counts():
    main = side = 0
    for path in sorted((DATA / "Quests").glob("*.json")):
        quest = _load(path)
        if quest.get("secret"):
            continue
        if quest.get("side"):
            side += 1
        else:
            main += 1
    return main, side


def _number(text, pattern):
    match = re.search(pattern, text)
    assert match, f"HOW_TO_PLAY.md に「{pattern}」が無い"
    return int(match.group(1))


def test_quest_counts_match_data():
    text = HOW_TO_PLAY.read_text(encoding="utf-8")
    main, side = _quest_counts()
    assert _number(text, r"メインクエスト (\d+) 本") == main
    assert _number(text, r"サブクエスト (\d+) 本") == side


def test_collectible_counts_match_data():
    text = HOW_TO_PLAY.read_text(encoding="utf-8")
    catalog = _load(DATA / "Collectibles" / "collectibles.json")
    hidden = sum(1 for item in catalog["collectibles"] if item.get("source") == "hidden")
    assert _number(text, r"隠しアイテム (\d+) 個") == hidden
    assert _number(text, r"写真スポット (\d+) か所") == len(catalog["photo_spots"])

"""人が読む楽譜 (school_song_kiritan_piano.musicxml / .mscz / .pdf) を作る.

    python make_display_score.py

元は school_song_kiritan_piano_neutrino.musicxml (Vo. + Pno.). 歌詞は NEUTRINO に歌わせるためのかな
(score/school_song_vocal.musicxml と同じ) のままで, メリスマの母音 (「い い ぶ」) や発音どおりの表記
(「わこど」「ちわ」) が入っている. 普通の楽譜の歌詞の振り方に直す (youtube-pipeline #258).

- メリスマ (1 音節を複数の音で歌う) の 2 音目からは歌詞を書かず, 前の音節に伸ばしの線 (<extend/>) を引く
- タイで続く音も同じく歌詞なしで, 前の音節から線を引く
- 表記は公式の歌詞 (https://www.tus.ac.jp/about/university/symbol/) の読みに合わせる (こう, は など)

LINES は公式の歌詞 1 行ごとの, 歌詞のある音符 1 つずつの表示かな. "_" はメリスマ (歌詞なし + 線).
行ごとの音符の数は元の楽譜と assert で突き合わせる.
"""
from __future__ import annotations

import os
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "school_song_kiritan_piano_neutrino.musicxml")
OUT = os.path.join(HERE, "school_song_kiritan_piano.musicxml")
MSCORE = r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe"

# 1〜3 番の 6〜8 行目は同じ
_REFRAIN = [
    ("浩洋の行く手輝く", "こう よ う の ゆ く て か _ が や く"),
    ("おお若き若き血は躍る", "お お _ わ か _ き わ か き ち は お ど る"),
    ("我らが学園", "わ れ ら が が く え ん"),
]
# (番, 公式の歌詞の行, 表示かな)
LINES = [
    (1, "新生のいぶきも高ら若人よ", "し ん せ い の い _ ぶ き も た か ら わ こう ど よ"),
    (1, "かたき故蹤のいしずえを", "か た き こ _ しょ う の い し _ ず え を"),
    (1, "守りて更に栄えゆく", "ま も り て さ ら に さ か _ え ゆ く"),
    (1, "理学の精華かぐわしき", "り が く の せ い か か _ ぐ わ し き"),
    (1, "たかき鵠志の根とならん", "た か き こ く し の ね と な ら ん"),
    *[(1, a, b) for a, b in _REFRAIN],
    (2, "乾坤の真理きわめん若人よ", "け ん こ ん の し _ ん り き わ め ん わ こう ど よ"),
    (2, "たかき理想の峻嶺に", "た か き り _ そ う の しゅ ん _ れ い に"),
    (2, "のぞみてわれら意気高し", "の ぞ み て わ れ ら い き _ た か し"),
    (2, "学理を愛する心もて", "が く り を あい す る こ _ こ ろ も て"),
    (2, "いざ奎運の根とならん", "い ざ け い う ん の ね と な ら ん"),
    *[(2, a, b) for a, b in _REFRAIN],
    (3, "晨鐘のひびきも高ら若人よ", "し ん しょ う の ひ _ び き も た か ら わ こう ど よ"),
    (3, "あらき天地の園囿に", "あ ら き て ん ち _ の え ん _ ゆ う に"),
    (3, "きよき心の華さかん", "き よ き こ こ ろ の は な _ さ か ん"),
    (3, "平和の萌芽はぐくみて", "へ い わ の ほ う が は _ ぐ く み て"),
    (3, "ふかき寛恕の根とならん", "ふ か き か ん じょ の ね と な ら ん"),
    *[(3, a, b) for a, b in _REFRAIN],
]
# 行ごとの, 歌詞のある音符の数 (1 番の並び. 2・3 番も同じ)
NOTES_PER_LINE = [17, 14, 13, 13, 12, 12, 15, 8]


def vocal_notes(root: ET.Element) -> list[tuple[ET.Element, int]]:
    """Vo. の休符でない音符と小節番号を順に返す."""
    part = root.findall("part")[0]
    out = []
    for m in part.findall("measure"):
        for n in m.findall("note"):
            if n.find("rest") is None:
                out.append((n, int(m.get("number"))))
    return out


def main() -> int:
    for i, (_, _, kana) in enumerate(LINES):
        assert len(kana.split()) == NOTES_PER_LINE[i % 8], (i, kana)

    tree = ET.parse(SRC)
    root = tree.getroot()
    tokens = [t for _, _, kana in LINES for t in kana.split()]
    notes = vocal_notes(root)
    sung = [n for n, _ in notes if n.find("lyric") is not None]
    assert len(sung) == len(tokens), (len(sung), len(tokens))

    token_of = dict(zip(map(id, sung), tokens))
    last_lyric = None  # 直前に歌詞を書いた音符の <lyric>
    changed = 0
    for n, bar in notes:
        lyric = n.find("lyric")
        tok = token_of.get(id(n))
        if tok is None:
            # 歌詞の無い音 = タイの後ろ. 前の音節から線を引く
            assert any(t.get("type") == "stop" for t in n.findall("tie")), bar
            tok = "_"
        if tok == "_":
            if lyric is not None:
                n.remove(lyric)
            if last_lyric is not None and last_lyric.find("extend") is None:
                ET.SubElement(last_lyric, "extend")
            continue
        if lyric.findtext("text") != tok:
            changed += 1
        lyric.find("text").text = tok
        lyric.find("syllabic").text = "single"
        last_lyric = lyric

    ET.indent(tree, "  ")
    tree.write(OUT, encoding="UTF-8", xml_declaration=True)
    n_extend = sum(1 for _ in root.iter("extend"))
    print(f"{OUT}: 歌詞 {sum(t != '_' for t in tokens)} 音, 書き換え {changed}, 伸ばしの線 {n_extend}")

    for ext in ("mscz", "pdf"):
        dst = OUT[:-len("musicxml")] + ext
        subprocess.run([MSCORE, "-o", dst, OUT], check=True, capture_output=True)
        print(dst)
    return 0


if __name__ == "__main__":
    sys.exit(main())

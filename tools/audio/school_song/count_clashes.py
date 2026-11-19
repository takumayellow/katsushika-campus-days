#!/usr/bin/env python3
"""歌とピアノ伴奏のぶつかりを数える (Issue #27).

    python tools/audio/school_song/count_clashes.py
    python tools/audio/school_song/count_clashes.py --accomp 別の伴奏.musicxml --strict

正本 score/school_song_vocal.musicxml と伴奏譜 score/bgm_yamaha.musicxml を music21 で読み,
同時に鳴る歌の音と伴奏の音 (和音は構成音ごと) の音程が, 音程クラスで短 2 度 / 長 7 度の系統 (1) か
三全音 (6) になるものを「ぶつかり」として拍ごとに列挙する. 「音がずれて聞こえる」原因の洗い出しと,
伴奏譜を直した後の確認に使う.

- 小節: 正本は小節番号 5〜95. 伴奏譜は全小節の number が 0 なので, パート内の順番 (1 始まり) を
  伴奏の小節 i とする. 正本の小節 = i + 4 (どちらも 4/4 で全長 364 拍).
- 音符番号: 歌の音を onset の順に数えた通し番号 % 104 (その番の弱起 = 0), 番 = 通し番号 // 104 + 1.
  tie の継続は数えない. 歌詞は著作物なので, この出力にも仮名は出さない.
- 伴奏はピアノのパートだけを読む (ソプラノのパートは 1 番の旋律の重ねで, 伴奏ではない).
- 重さ: 歌の 1 音にぶつかる区間の和集合の長さ (拍). 1 拍以上を「重い」, それ未満を「軽い」とする.
  旋律の経過音が和音の構成音に当たるだけの 0.5 拍のぶつかりは軽い側に落ちる.
"""
from __future__ import annotations

import argparse
import os
import sys
import warnings
from dataclasses import dataclass

HERE = os.path.dirname(os.path.abspath(__file__))
VOCAL_XML = os.path.join(HERE, "score", "school_song_vocal.musicxml")
ACCOMP_XML = os.path.join(HERE, "score", "bgm_yamaha.musicxml")

BEATS_PER_BAR = 4.0
NOTES_PER_VERSE = 104
HEAVY_BEATS = 1.0
# 半音の距離 % 12 -> 名前. 1 = 短 2 度 (短 9 度), 11 = 長 7 度, 6 = 三全音
KINDS = {1: "短2度系", 11: "長7度系", 6: "三全音"}
EPS = 1e-9


@dataclass(frozen=True)
class Tone:
    """鳴っている 1 音. start / end は曲頭からの拍 (tie は 1 音に結合済み)."""
    start: float
    end: float
    midi: int
    name: str
    voice: str  # "歌" / "右手" / "左手"


@dataclass(frozen=True)
class Clash:
    """歌の 1 音と伴奏の 1 音のぶつかり."""
    vocal_index: int  # 歌の通し番号 (onset 順, 0 始まり)
    vocal: Tone
    accomp: Tone
    start: float  # 重なりの区間 (曲頭からの拍)
    end: float
    kind: str
    semitones: int

    @property
    def beats(self) -> float:
        return self.end - self.start


@dataclass(frozen=True)
class NoteClash:
    """歌の 1 音にぶつかる伴奏の音をまとめたもの."""
    vocal_index: int
    vocal: Tone
    clashes: tuple
    beats: float  # ぶつかる区間の和集合の長さ

    @property
    def verse(self) -> int:
        return self.vocal_index // NOTES_PER_VERSE + 1

    @property
    def note(self) -> int:
        return self.vocal_index % NOTES_PER_VERSE

    def heavy(self, threshold: float = HEAVY_BEATS) -> bool:
        return self.beats >= threshold - EPS


def clash_kind(a: int, b: int) -> str | None:
    """2 つの MIDI 音高がぶつかる (短 2 度 / 長 7 度の系統か三全音) なら名前を返す."""
    return KINDS.get(abs(a - b) % 12)


def find_clashes(vocal: list, accomp: list) -> list:
    """同時に鳴る歌の音と伴奏の音の組のうち, ぶつかるものを返す. vocal は onset 順."""
    out = []
    for vi, v in enumerate(vocal):
        for a in accomp:
            start, end = max(v.start, a.start), min(v.end, a.end)
            if end - start <= EPS:
                continue
            kind = clash_kind(v.midi, a.midi)
            if kind:
                out.append(Clash(vi, v, a, start, end, kind, abs(v.midi - a.midi)))
    return out


def union_length(spans: list) -> float:
    total, cur_s, cur_e = 0.0, None, None
    for s, e in sorted(spans):
        if cur_e is None or s > cur_e + EPS:
            if cur_e is not None:
                total += cur_e - cur_s
            cur_s, cur_e = s, e
        else:
            cur_e = max(cur_e, e)
    if cur_e is not None:
        total += cur_e - cur_s
    return total


def group_by_vocal_note(clashes: list) -> list:
    """ぶつかりを歌の音ごとにまとめ, 曲の順に返す."""
    groups: dict = {}
    for c in clashes:
        groups.setdefault(c.vocal_index, []).append(c)
    return [NoteClash(vi, cs[0].vocal, tuple(cs), union_length([(c.start, c.end) for c in cs]))
            for vi, cs in sorted(groups.items())]


# --------------------------------------------------------------------------
# 譜面の読み込み (music21)
# --------------------------------------------------------------------------
def _parse(path: str):
    from music21 import converter
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        return converter.parse(path)


def sounding_tones(part, voice: str) -> list:
    """music21 のパートから鳴っている音を取り出す. tie は 1 音に結合し, 和音は構成音に分け,
    和音記号 (長さ 0) は除く. 開始の早い順."""
    from music21 import harmony
    open_ties: dict = {}
    out: list = []
    for n in part.flatten().notes:
        if isinstance(n, harmony.Harmony) or n.quarterLength == 0:
            continue
        pairs = [(p, x.tie) for p, x in zip(n.pitches, n.notes)] if n.isChord else [(n.pitch, n.tie)]
        for p, tie in pairs:
            start = float(n.offset)
            end = start + float(n.quarterLength)
            if tie is not None and tie.type in ("stop", "continue") and p.midi in open_ties:
                i = open_ties[p.midi]
                out[i] = Tone(out[i].start, end, out[i].midi, out[i].name, voice)
                if tie.type == "stop":
                    del open_ties[p.midi]
                continue
            out.append(Tone(start, end, p.midi, p.nameWithOctave, voice))
            if tie is not None and tie.type in ("start", "continue"):
                open_ties[p.midi] = len(out) - 1
    return sorted(out, key=lambda t: (t.start, t.midi))


def read_vocal(path: str = VOCAL_XML) -> tuple:
    """正本の歌を (音のリスト, 最初の小節番号) で返す."""
    from music21 import stream
    score = _parse(path)
    part = score.parts[0]
    first = int(part.getElementsByClass(stream.Measure)[0].number)
    return sounding_tones(part, "歌"), first


def read_accomp(path: str = ACCOMP_XML) -> list:
    """伴奏譜のピアノ (右手 = 上の段, 左手 = 下の段) の音を返す."""
    score = _parse(path)
    pianos = [p for p in score.parts if (p.partName or "").lower() == "piano"] or list(score.parts)[1:]
    tones: list = []
    for part, voice in zip(pianos, ("右手", "左手")):
        tones += sounding_tones(part, voice)
    return sorted(tones, key=lambda t: (t.start, t.midi))


def analyze(vocal_path: str = VOCAL_XML, accomp_path: str = ACCOMP_XML) -> tuple:
    """(歌の音, 伴奏の音, 正本の最初の小節番号, 歌の音ごとのぶつかり) を返す."""
    vocal, first = read_vocal(vocal_path)
    accomp = read_accomp(accomp_path)
    return vocal, accomp, first, group_by_vocal_note(find_clashes(vocal, accomp))


# --------------------------------------------------------------------------
# 表示
# --------------------------------------------------------------------------
def pretty(name: str) -> str:
    return name.replace("-", "♭")


def where(start: float, first_vocal_measure: int) -> str:
    bar = int(start // BEATS_PER_BAR)
    return f"正本の小節 {first_vocal_measure + bar} / 伴奏の小節 {bar + 1} の拍 {start % BEATS_PER_BAR + 1:g}"


def format_report(vocal: list, accomp: list, first: int, notes: list, threshold: float = HEAVY_BEATS) -> str:
    heavy = [n for n in notes if n.heavy(threshold)]
    pairs = sum(len(n.clashes) for n in notes)
    lines = [f"歌の音 {len(vocal)} 個 / 伴奏の音 {len(accomp)} 個 "
             f"(右手 {sum(t.voice == '右手' for t in accomp)}, 左手 {sum(t.voice == '左手' for t in accomp)})",
             f"ぶつかる歌の音 {len(notes)} 個 / 組 {pairs}: 重い (>= {threshold:g} 拍) {len(heavy)} 個, "
             f"軽い {len(notes) - len(heavy)} 個"]
    for n in notes:
        tag = "重い" if n.heavy(threshold) else "軽い"
        lines.append(f"[{tag}] {n.verse} 番 音符 {n.note} 歌 {pretty(n.vocal.name)} "
                     f"({where(n.vocal.start, first)}) ぶつかる区間 {n.beats:g} 拍")
        for c in n.clashes:
            lines.append(f"    {c.accomp.voice} {pretty(c.accomp.name)} {c.kind} ({c.semitones} 半音) "
                         f"拍 {c.start % BEATS_PER_BAR + 1:g} から {c.beats:g} 拍")
    return "\n".join(lines)


def main(argv: list) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--vocal", default=VOCAL_XML, help="歌の譜面 (既定: score/school_song_vocal.musicxml)")
    parser.add_argument("--accomp", default=ACCOMP_XML, help="伴奏の譜面 (既定: score/bgm_yamaha.musicxml)")
    parser.add_argument("--heavy-beats", type=float, default=HEAVY_BEATS, help="重いとみなす拍の数")
    parser.add_argument("--strict", action="store_true", help="重いぶつかりがあれば終了コード 1 にする")
    args = parser.parse_args(argv[1:])
    vocal, accomp, first, notes = analyze(args.vocal, args.accomp)
    print(format_report(vocal, accomp, first, notes, args.heavy_beats))
    if args.strict and any(n.heavy(args.heavy_beats) for n in notes):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

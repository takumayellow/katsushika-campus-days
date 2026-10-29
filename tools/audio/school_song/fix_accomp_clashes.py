#!/usr/bin/env python3
"""伴奏譜 score/bgm_yamaha.musicxml の, 歌とぶつかる音を直す (Issue #27).

    python tools/audio/school_song/fix_accomp_clashes.py           # 直す (直し済みの行は飛ばす)
    python tools/audio/school_song/fix_accomp_clashes.py --check   # 直し済みかを見るだけ (未了があれば終了コード 1)

count_clashes.py が見つけた「重い」ぶつかり (歌の 1 音に 1 拍以上) と, 明らかに外れている
短いものを, その拍の和声 (ヘ長調) の構成音に替える編集表 FIXES を持つ. 各行に, その拍を何の和音と
読んだかを書く. 直した後は count_clashes.py で数え直す.

- 編集は music21 で書き出し直さず, xml のテキストの該当する <note> だけを書き換える.
  (ヤマハの変換結果を MuseScore で出し直した体裁・和音記号・レイアウトをそのまま保つ)
- 各行は「小節・段・拍・直す前の音と長さ」を指定する. 一致しなければ止まり, 直し済みなら飛ばす.
- 小節は伴奏譜のピアノのパート内の順番 (1 始まり). 正本の小節 = 伴奏の小節 + 4.
- 音は music21 の表記 ("B-3" = B♭3, "F#4" = F♯4). 拍は小節内で 0 始まり (拍 1 = 0).
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass

HERE = os.path.dirname(os.path.abspath(__file__))
ACCOMP_XML = os.path.join(HERE, "score", "bgm_yamaha.musicxml")

RH, LH = 1, 2
STAFF_NAME = {RH: "右手", LH: "左手"}
DIVISIONS = 4  # <divisions>: 4 分音符 = 4
BAR_DIVISIONS = 16  # 4/4 の 1 小節
BAR_OFFSET = 4  # 正本の小節 = 伴奏の小節 + 4
TYPE_BY_DIVISIONS = {1: "16th", 2: "eighth", 4: "quarter", 8: "half", 16: "whole"}
STEPS = "CDEFGAB"
ALTER_MARK = {None: "", "0": "", "-1": "-", "1": "#"}

TOKEN_RE = re.compile(r"<note\b.*?</note>|<backup>.*?</backup>|<forward>.*?</forward>", re.S)
PITCH_RE = re.compile(r"(<pitch>)(\s*)<step>\w</step>(?:\s*<alter>-?\d+</alter>)?\s*<octave>\d+</octave>")
PITCH_NAME_RE = re.compile(r"<step>(\w)</step>\s*(?:<alter>(-?\d+)</alter>\s*)?<octave>(\d+)</octave>")


@dataclass(frozen=True)
class Fix:
    bar: int  # 伴奏の小節 (1 始まり)
    staff: int  # RH / LH
    beat: float  # 小節内の拍 (0 始まり)
    old: tuple  # (音, 拍の長さ) 直す前の 1 音
    new: tuple  # ((音, 拍の長さ), ...) 同じ拍から続けて置く. 空なら消す
    target: str  # 歌のどの音か
    reading: str  # その拍の和声の読み


def retune(bar: int, staff: int, beat: float, old: str, beats: float, new: str, target: str, reading: str) -> Fix:
    """長さは変えずに音高だけ替える."""
    return Fix(bar, staff, beat, (old, beats), ((new, beats),), target, reading)


def drop(bar: int, staff: int, beat: float, old: str, beats: float, target: str, reading: str) -> Fix:
    return Fix(bar, staff, beat, (old, beats), (), target, reading)


READ_F = "拍 1〜2 を F (I) と読む (歌 A4 は F の 3 度)"
READ_C = "拍 4 を C (V) と読む (歌 E4 は C の 3 度). 低音は B♭ (拍 3) → C (拍 4) → 次の小節の F"
READ_E = "拍 4 を C (V) と読み, 歌 E4 の 1 オクターブ下の E (C の 3 度) を左手に置く (1 番の同じ位置, 正本の小節 18 と同じ形)"
READ_GM = "拍 1 は Gm (ii). 歌 A4 に長 7 度の B♭3 だけを外し, D4 + A4 は残す"
READ_SHORT = "拍 4 の F4 を 1 拍に伸ばし, 歌 A4 の終わり 0.25 拍に短 2 度でぶつかる B♭4 を外す (2・3 番の同じ位置, 正本の小節 35・65 の右手と同じ形)"

FIXES = (
    # 音符 53 (正本の小節 24 / 49 / 79 = 伴奏の 20 / 45 / 75 の拍 1〜2): 歌 A4 に左手の B♭ が長 7 度
    retune(20, LH, 0.0, "B-2", 0.5, "F2", "音符 53", READ_F + ". 下拍の B♭2 を根音 F2 に"),
    retune(20, LH, 1.0, "B-3", 0.5, "A3", "音符 53", READ_F + ". B♭3 を最寄りの構成音 A3 に"),
    retune(45, LH, 0.0, "B-1", 0.5, "F1", "音符 53", READ_F + " (右手の D4 + F4 + A4 は F の 6 の和音). B♭1 を根音 F1 に"),
    retune(45, LH, 1.0, "B-2", 0.5, "A2", "音符 53", READ_F + ". B♭2 を最寄りの構成音 A2 に"),
    retune(75, LH, 0.0, "B-2", 0.5, "F2", "音符 53", READ_F + ". 下拍の B♭2 を根音 F2 に"),
    retune(75, LH, 1.0, "B-3", 0.5, "A3", "音符 53", READ_F + ". B♭3 を最寄りの構成音 A3 に"),
    # 音符 29 (正本の小節 16 / 41 / 71 = 伴奏の 12 / 37 / 67 の拍 4): 歌 E4 に左手の B♭3 が三全音
    retune(12, LH, 3.0, "B-3", 0.5, "C3", "音符 29", READ_C + ". 左手 B♭3 を根音 C3 に"),
    retune(12, LH, 3.5, "F3", 0.5, "G3", "音符 29", READ_C + ". 左手 F3 (歌 E4 と長 7 度) を C の 5 度 G3 に"),
    retune(12, RH, 3.5, "B-3", 0.5, "C4", "音符 29", READ_C + ". 右手 B♭3 (歌 E4 と三全音) を C4 に"),
    retune(37, LH, 3.0, "B-3", 1.0, "C3", "音符 29", READ_C + ". 左手 B♭3 を根音 C3 に"),
    retune(67, LH, 3.0, "B-3", 1.0, "C3", "音符 29", READ_C + ". 左手 B♭3 を根音 C3 に"),
    # 音符 35 (正本の小節 43 / 73 = 伴奏の 39 / 69 の拍 4): 歌 E4 に左手の F3 / B♭3 が当たる
    Fix(39, LH, 2.0, ("F3", 2.0), (("D3", 1.0), ("E3", 1.0)), "音符 35",
        READ_E + ". 拍 3〜4 の F3 (2 分音符) を D3 (B♭ の 3 度) + E3 に分ける"),
    retune(69, LH, 3.0, "B-3", 0.5, "E3", "音符 35", READ_E + ". B♭3 を E3 に"),
    retune(69, LH, 3.5, "F3", 0.5, "E3", "音符 35", READ_E + ". F3 を E3 に"),
    # 音符 39 (正本の小節 45 = 伴奏の 41 の拍 1): 歌 A4 に右手の和音の B♭3 が長 7 度
    drop(41, RH, 0.0, "B-3", 1.5, "音符 39", READ_GM),
    # 音符 5 (正本の小節 10 = 伴奏の 6 の拍 4): 歌 F4 に右手の B♭4 が 0.25 拍. 消してから F4 を伸ばす (順序に意味がある)
    drop(6, RH, 3.5, "B-4", 0.5, "音符 5", READ_SHORT),
    Fix(6, RH, 3.0, ("F4", 0.5), (("F4", 1.0),), "音符 5", READ_SHORT),
)


# --------------------------------------------------------------------------
# xml のテキストを読む
# --------------------------------------------------------------------------
@dataclass(frozen=True)
class Elem:
    """小節のテキストの中の 1 つの <note>."""
    start: int
    end: int
    staff: int
    voice: int
    onset: int  # 小節内の位置 (divisions)
    dur: int
    pitch: str  # "B-3". 休符は ""
    chord: bool  # <chord/> が付いている (和音の 2 つ目以降)


def split_pitch(name: str) -> tuple:
    m = re.fullmatch(r"([A-G])([-#]?)(\d)", name)
    if not m:
        raise ValueError(f"音の表記が読めない: {name!r}")
    step, mark, octave = m.groups()
    return step, {"": 0, "-": -1, "#": 1}[mark], int(octave)


def diatonic(name: str) -> int:
    step, _, octave = split_pitch(name)
    return octave * 7 + STEPS.index(step)


def _int_tag(xml: str, tag: str) -> int:
    m = re.search(rf"<{tag}>(\d+)</{tag}>", xml)
    if not m:
        raise ValueError(f"<{tag}> が無い音は扱わない (装飾音など): {xml[:60]!r}")
    return int(m.group(1))


def _pitch_name(xml: str) -> str:
    m = PITCH_NAME_RE.search(xml)
    if not m:
        return ""
    step, alter, octave = m.groups()
    return step + ALTER_MARK[alter] + octave


def measure_spans(text: str) -> list:
    """ピアノのパート (P2) の各小節の (開始, 終了) の位置."""
    part = text.index('<part id="P2">')
    end = text.index("</part>", part)
    starts = [m.start() for m in re.finditer(r"<measure\s", text[part:end])]
    starts = [part + s for s in starts]
    return list(zip(starts, starts[1:] + [end]))


def scan_notes(bar: str) -> list:
    """小節のテキストの <note> を, 段・小節内の位置・長さ・音高つきで文書順に返す."""
    out, cursor, last_onset = [], 0, 0
    for m in TOKEN_RE.finditer(bar):
        tok = m.group()
        dur = _int_tag(tok, "duration")
        if tok.startswith("<backup"):
            cursor -= dur
        elif tok.startswith("<forward"):
            cursor += dur
        else:
            chord = "<chord/>" in tok
            onset = last_onset if chord else cursor
            if not chord:
                last_onset, cursor = cursor, cursor + dur
            out.append(Elem(m.start(), m.end(), _int_tag(tok, "staff"), _int_tag(tok, "voice"), onset, dur,
                            _pitch_name(tok), chord))
    return out


def bar_problems(bar: str) -> list:
    """小節の長さ (段・声ごとに 4 拍) と beam の組 (begin → continue → end) を確かめる. 問題の文を返す."""
    problems = []
    notes = scan_notes(bar)
    for staff, voice in sorted({(n.staff, n.voice) for n in notes}):
        total = sum(n.dur for n in notes if (n.staff, n.voice) == (staff, voice) and not n.chord)
        if total != BAR_DIVISIONS:
            problems.append(f"段 {staff} 声 {voice} の長さが {total / DIVISIONS:g} 拍")
    open_beams: dict = {}
    for m in TOKEN_RE.finditer(bar):
        tok = m.group()
        if not tok.startswith("<note") or "<chord/>" in tok:
            continue
        key = (_int_tag(tok, "staff"), _int_tag(tok, "voice"))
        beam = re.search(r'<beam number="1">(\w+)</beam>', tok)
        state = beam.group(1) if beam else None
        if state == "begin" and open_beams.get(key):
            problems.append(f"{key} の beam が begin の途中でまた begin")
        if state in ("continue", "end") and not open_beams.get(key):
            problems.append(f"{key} の beam が begin 無しで {state}")
        open_beams[key] = state in ("begin", "continue")
    problems += [f"{k} の beam が閉じていない" for k, v in open_beams.items() if v]
    return problems


# --------------------------------------------------------------------------
# 1 つの <note> を書き換える
# --------------------------------------------------------------------------
def _replace_pitch(xml: str, name: str) -> str:
    step, alter, octave = split_pitch(name)

    def sub(m: re.Match) -> str:
        indent = m.group(2)  # <pitch> の子の字下げ (改行 + 空白)
        parts = [f"<step>{step}</step>"] + ([f"<alter>{alter}</alter>"] if alter else []) + [f"<octave>{octave}</octave>"]
        return m.group(1) + indent + indent.join(parts)

    out, n = PITCH_RE.subn(sub, xml, count=1)
    if n != 1:
        raise ValueError("<pitch> を書き換えられない")
    return out


def _shift_default_y(xml: str, steps: int) -> str:
    """見た目だけの値. 譜表上で音が動いた段の数 (1 段 = 5 tenths) ぶん default-y をずらす."""
    if steps == 0:
        return xml

    def shifted(m: re.Match) -> str:
        value = round(float(m.group(2)) + 5 * steps, 2)
        text = f"{value:.2f}".rstrip("0").rstrip(".")  # 元の書式 ("-10" / "-140.88") に合わせて 0 を落とす
        return f'{m.group(1)}{"0" if text in ("-0", "") else text}"'

    return re.sub(r'(<note\b[^>]*default-y=")(-?[\d.]+)"', shifted, xml, count=1)


def with_pitch_and_length(xml: str, name: str, beats: float) -> str:
    """<note> の音高と長さを替えた <note> を返す. 長さを替えたら type を合わせ, beam を外す."""
    old_name = _pitch_name(xml)
    if "<accidental" in xml or "<tie" in xml:
        raise ValueError("臨時記号・tie の付いた音は書き換えない")
    out = _shift_default_y(_replace_pitch(xml, name), diatonic(name) - diatonic(old_name))
    dur = round(beats * DIVISIONS)
    if dur != _int_tag(out, "duration"):
        if "<dot" in out or dur not in TYPE_BY_DIVISIONS:
            raise ValueError("付点の音・割り切れない長さには替えない")
        out = re.sub(r"<duration>\d+</duration>", f"<duration>{dur}</duration>", out)
        out = re.sub(r"<type>\w+</type>", f"<type>{TYPE_BY_DIVISIONS[dur]}</type>", out)
        out = re.sub(r'\n[ \t]*<beam number="\d+">\w+</beam>', "", out)
    return out


def _leading_blank(bar: str, pos: int) -> int:
    """pos の直前の空白 (改行 + 字下げ) の先頭の位置."""
    s = pos
    while s > 0 and bar[s - 1] in " \t":
        s -= 1
    return s - 1 if s > 0 and bar[s - 1] == "\n" else s


def _other_x(bar: str, notes: list, onset: int) -> str | None:
    for n in notes:
        if n.onset == onset:
            m = re.search(r'default-x="(-?[\d.]+)"', bar[n.start:n.end])
            if m:
                return m.group(1)
    return None


def replace_note(bar: str, notes: list, target: Elem, new: tuple) -> str:
    """小節のテキストの target を new ((音, 拍の長さ), ...) に替える. 空なら消す."""
    i = notes.index(target)
    nxt = notes[i + 1] if i + 1 < len(notes) else None
    leads_chord = not target.chord and nxt is not None and nxt.chord  # 和音の先頭 (後ろに構成音が続く)
    if not new:
        if "<tie" in bar[target.start:target.end]:
            raise ValueError("tie の付いた音は消さない (相手の tie が宙に浮く)")
        out = bar
        if leads_chord:
            # 和音の先頭を消すので, 次の音を先頭にする
            out = out[:nxt.start] + re.sub(r"\n[ \t]*<chord/>", "", out[nxt.start:nxt.end], count=1) + out[nxt.end:]
        s = _leading_blank(out, target.start)
        return out[:s] + out[target.end:]
    if len(new) > 1 and (target.chord or leads_chord):
        raise ValueError("和音の音は分けない")
    old_xml = bar[target.start:target.end]
    pieces, onset = [], target.onset
    for k, (name, beats) in enumerate(new):
        piece = with_pitch_and_length(old_xml, name, beats)
        if k:
            x = _other_x(bar, notes, onset)
            if x is not None:
                piece = re.sub(r'(<note\b[^>]*default-x=")(-?[\d.]+)"', lambda m: f'{m.group(1)}{x}"', piece, count=1)
        pieces.append(piece)
        onset += round(beats * DIVISIONS)
    sep = bar[_leading_blank(bar, target.start):target.start]
    return bar[:target.start] + sep.join(pieces) + bar[target.end:]


# --------------------------------------------------------------------------
# 編集表を当てる
# --------------------------------------------------------------------------
def _matches(notes: list, fix: Fix, beat: float, name: str, beats: float | None) -> list:
    onset = round(beat * DIVISIONS)
    return [n for n in notes if n.staff == fix.staff and n.onset == onset and n.pitch == name
            and (beats is None or n.dur == round(beats * DIVISIONS))]


def fix_state(text: str, fix: Fix) -> str:
    """"old" (未修正) / "new" (修正済み). どちらでもなければ ValueError."""
    start, end = measure_spans(text)[fix.bar - 1]
    notes = scan_notes(text[start:end])
    if _matches(notes, fix, fix.beat, *fix.old):
        return "old"
    if not fix.new:
        if not _matches(notes, fix, fix.beat, fix.old[0], None):
            return "new"
    else:
        beat, ok = fix.beat, True
        for name, beats in fix.new:
            ok = ok and bool(_matches(notes, fix, beat, name, beats))
            beat += beats
        if ok:
            return "new"
    raise ValueError(f"小節 {fix.bar} {STAFF_NAME[fix.staff]} 拍 {fix.beat + 1:g}: "
                     f"直す前の音 {fix.old} も直した後の音 {fix.new} も見つからない")


def apply_fix(text: str, fix: Fix) -> str:
    """未修正の fix を 1 つ当てた xml のテキストを返す."""
    start, end = measure_spans(text)[fix.bar - 1]
    bar = text[start:end]
    notes = scan_notes(bar)
    target = _matches(notes, fix, fix.beat, *fix.old)[0]
    new_bar = replace_note(bar, notes, target, fix.new)
    return text[:start] + new_bar + text[end:]


def apply_all(text: str, fixes: tuple = FIXES) -> tuple:
    """(直した xml のテキスト, 各 fix の元の状態 "old" / "new" のリスト) を返す.

    直した小節の長さ・beam が崩れていれば ValueError にする (何も返さないので, 呼び出し側は書き込まない).
    途中の行は小節が欠けることがある (消してから隣の音を伸ばす) ので, 全行を当てた後に見る."""
    states, touched = [], set()
    for fix in fixes:
        state = fix_state(text, fix)
        states.append(state)
        if state == "old":
            text = apply_fix(text, fix)
            touched.add(fix.bar)
    spans = measure_spans(text)
    for bar in sorted(touched):
        problems = bar_problems(text[spans[bar - 1][0]:spans[bar - 1][1]])
        if problems:
            raise ValueError(f"小節 {bar}: 直した後の小節が崩れている ({'; '.join(problems)}). 編集表の順序と長さを見直す")
    return text, states


def pretty(name: str) -> str:
    return name.replace("-", "♭")


def describe(fix: Fix) -> str:
    new = " + ".join(f"{pretty(n)}({b:g} 拍)" for n, b in fix.new) or "(消す)"
    return (f"正本の小節 {fix.bar + BAR_OFFSET} / 伴奏の小節 {fix.bar} 拍 {fix.beat + 1:g} {STAFF_NAME[fix.staff]} "
            f"{pretty(fix.old[0])}({fix.old[1]:g} 拍) -> {new}  [{fix.target}] {fix.reading}")


def main(argv: list) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--score", default=ACCOMP_XML, help="伴奏の譜面 (既定: score/bgm_yamaha.musicxml)")
    parser.add_argument("--check", action="store_true", help="書き換えず, 直し済みかを確かめる")
    args = parser.parse_args(argv[1:])
    with open(args.score, "rb") as f:  # 改行コードを保つためバイトで読み書きする
        raw = f.read()
    if b"\r" in raw:
        parser.error("改行が CRLF の譜面は扱わない (編集は LF の字下げを前提にしている)")
    text, states = apply_all(raw.decode("utf-8"), FIXES)
    for fix, state in zip(FIXES, states):
        print(("修正済み " if state == "new" else "未修正   ") + describe(fix))
    pending = states.count("old")
    print(f"{len(FIXES)} 行のうち 未修正 {pending} / 修正済み {len(FIXES) - pending}")
    if args.check:
        return 1 if pending else 0
    if pending:
        with open(args.score, "wb") as f:
            f.write(text.encode("utf-8"))
        print(f"{args.score} を書き換えた")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

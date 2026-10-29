"""伴奏譜の修正スクリプト fix_accomp_clashes.py のテスト (#27)。

編集は music21 で書き出し直さず xml のテキストの <note> だけを書き換えるので, 合成した小さな譜面で
1 操作ずつ確かめ (音高の入れ替え・分割・和音の先頭を消す・長さを替えて beam を外す・順序・
拒否する場合), そのうえで本物の譜面が「編集表の全行が直し済み」で, 小節の長さと beam と
xml の体裁が壊れていないことを確かめる。music21 は要らないので CI でも走る。

歌詞は著作物なので, 音の高さと拍だけを書き, 仮名は書かない。
"""

import difflib
import os
import re
import sys
import xml.etree.ElementTree as ET
from fractions import Fraction

import pytest

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SONG_DIR = os.path.join(REPO, "tools", "audio", "school_song")
if SONG_DIR not in sys.path:
    sys.path.insert(0, SONG_DIR)

import fix_accomp_clashes as fc  # noqa: E402

RH, LH = fc.RH, fc.LH


# --------------------------------------------------------------------------
# MuseScore の書き出しと同じ体裁の合成譜面
# --------------------------------------------------------------------------
def note_xml(name, dur, typ, staff, voice, x="10", y="-20", chord=False, beam=None, dot=False,
             tie=None, accidental=False):
    step, alter, octave = fc.split_pitch(name)
    lines = [f'      <note default-x="{x}" default-y="{y}">']
    if chord:
        lines.append("        <chord/>")
    lines += ["        <pitch>", f"          <step>{step}</step>"]
    if alter:
        lines.append(f"          <alter>{alter}</alter>")
    lines += [f"          <octave>{octave}</octave>", "          </pitch>", f"        <duration>{dur}</duration>"]
    if tie:
        lines.append(f'        <tie type="{tie}"/>')
    lines += [f"        <voice>{voice}</voice>", f"        <type>{typ}</type>"]
    if dot:
        lines.append('        <dot default-x="30.5" default-y="-5"/>')
    if accidental:
        lines.append("        <accidental>natural</accidental>")
    lines += ["        <stem>up</stem>", f"        <staff>{staff}</staff>"]
    if beam:
        lines.append(f'        <beam number="1">{beam}</beam>')
    if tie:
        lines.append(f'        <notations><tied type="{tie}"/></notations>')
    lines.append("        </note>")
    return "\n".join(lines)


def rest_xml(dur, typ, dots=0, staff=1, voice=1, invisible=True, x="99.43", y="-122.37"):
    attrs = f' default-x="{x}" default-y="{y}"' + (' print-object="no"' if invisible else "")
    lines = [f"      <note{attrs}>", "        <rest/>", f"        <duration>{dur}</duration>",
             f"        <voice>{voice}</voice>", f"        <type>{typ}</type>"]
    lines += ['        <dot default-x="113.43" default-y="-117.37"/>'] * dots
    lines += [f"        <staff>{staff}</staff>", "        </note>"]
    return "\n".join(lines)


def measure(rh, lh):
    backup = "      <backup>\n        <duration>16</duration>\n        </backup>"
    return "\n".join(['    <measure number="0" width="100">', *rh, backup, *lh, "      </measure>"])


def score(*measures):
    return ('<?xml version="1.0" encoding="UTF-8"?>\n<score-partwise version="3.1">\n'
            '  <part id="P1">\n    <measure number="0" width="1">\n      </measure>\n    </part>\n'
            '  <part id="P2">\n' + "\n".join(measures) + "\n    </part>\n  </score-partwise>\n")


def rows(text, bar=1):
    """小節 bar の音を (段, 声, 位置, 長さ, 音高, 和音の 2 つ目以降か) の並びにする."""
    start, end = fc.measure_spans(text)[bar - 1]
    return [(n.staff, n.voice, n.onset, n.dur, n.pitch, n.chord) for n in fc.scan_notes(text[start:end])]


def bar_text(text, bar=1):
    start, end = fc.measure_spans(text)[bar - 1]
    return text[start:end]


def eighths_bar_lh():
    """左手: B♭2 F3 B♭3 F3 (8 分, beam) + C3 の 2 分音符. 右手: A4 の全音符."""
    lh = [note_xml("B-2", 2, "eighth", 2, 5, y="-246.44", beam="begin"),
          note_xml("F3", 2, "eighth", 2, 5, beam="continue"),
          note_xml("B-3", 2, "eighth", 2, 5, beam="continue"),
          note_xml("F3", 2, "eighth", 2, 5, beam="end"),
          note_xml("C3", 8, "half", 2, 5)]
    return measure([note_xml("A4", 16, "whole", 1, 1)], lh)


# --------------------------------------------------------------------------
# 読み取り
# --------------------------------------------------------------------------
def test_scan_notes_gives_staff_voice_onset_and_chord_membership():
    bar = measure(
        [note_xml("C4", 8, "half", 1, 1), note_xml("E4", 8, "half", 1, 1, chord=True),
         note_xml("D4", 4, "quarter", 1, 1), note_xml("F4", 4, "quarter", 1, 1)],
        [note_xml("F2", 4, "quarter", 2, 5), note_xml("C3", 4, "quarter", 2, 5), note_xml("F3", 8, "half", 2, 5)])
    text = score(bar)
    assert rows(text) == [
        (1, 1, 0, 8, "C4", False), (1, 1, 0, 8, "E4", True), (1, 1, 8, 4, "D4", False), (1, 1, 12, 4, "F4", False),
        (2, 5, 0, 4, "F2", False), (2, 5, 4, 4, "C3", False), (2, 5, 8, 8, "F3", False)]
    assert fc.bar_problems(bar) == []


def test_scan_notes_reads_rests_and_forward():
    rest = ("      <note>\n        <rest/>\n        <duration>4</duration>\n        <voice>1</voice>\n"
            "        <type>quarter</type>\n        <staff>1</staff>\n        </note>")
    forward = "      <forward>\n        <duration>4</duration>\n        </forward>"
    bar = measure([rest, forward, note_xml("G4", 8, "half", 1, 1)], [note_xml("F2", 16, "whole", 2, 5)])
    assert rows(score(bar)) == [(1, 1, 0, 4, "", False), (1, 1, 8, 8, "G4", False), (2, 5, 0, 16, "F2", False)]


def test_measure_spans_reads_only_the_piano_part():
    text = score(eighths_bar_lh(), eighths_bar_lh(), eighths_bar_lh())
    assert len(fc.measure_spans(text)) == 3  # P1 の 1 小節は数えない
    assert fc.measure_spans(text)[0][0] > text.index('<part id="P2">')


@pytest.mark.parametrize("name, expected", [("B-3", ("B", -1, 3)), ("F#4", ("F", 1, 4)), ("C3", ("C", 0, 3))])
def test_split_pitch(name, expected):
    assert fc.split_pitch(name) == expected


@pytest.mark.parametrize("bad", ["H3", "B--3", "C", "c3", ""])
def test_split_pitch_rejects_other_notations(bad):
    with pytest.raises(ValueError):
        fc.split_pitch(bad)


def test_diatonic_counts_one_step_per_letter():
    assert fc.diatonic("C4") - fc.diatonic("B3") == 1
    assert fc.diatonic("B-3") == fc.diatonic("B3")  # 臨時記号は段の位置を変えない
    assert fc.diatonic("C4") - fc.diatonic("C3") == 7


# --------------------------------------------------------------------------
# 1 つの音の書き換え
# --------------------------------------------------------------------------
@pytest.mark.parametrize("y, steps, expected", [
    ("-10", 2, "0"), ("-10", 1, "-5"), ("-246.44", -3, "-261.44"), ("-140.88", 1, "-135.88"),
    ("5", -1, "0"), ("-10.5", 0, "-10.5"),
])
def test_default_y_follows_the_staff_step_and_keeps_the_original_format(y, steps, expected):
    out = fc._shift_default_y(f'<note default-x="1.5" default-y="{y}">', steps)
    assert out == f'<note default-x="1.5" default-y="{expected}">'


def test_retune_changes_only_that_note_and_drops_the_alter():
    text = score(eighths_bar_lh())
    fix = fc.retune(1, LH, 0.0, "B-2", 0.5, "F2", "t", "r")
    assert fc.fix_state(text, fix) == "old"
    out = fc.apply_fix(text, fix)
    before, after = rows(text), rows(out)
    assert [q for r, q in zip(before, after) if r != q] == [(2, 5, 0, 2, "F2", False)]
    assert before[1] == (2, 5, 0, 2, "B-2", False) and after[1] == (2, 5, 0, 2, "F2", False)
    assert text.count("<alter>-1</alter>") == 2  # B♭2 と B♭3
    assert out.count("<alter>-1</alter>") == 1  # B♭2 の分だけ消え, B♭3 は残る
    assert 'default-y="-261.44"' in out and 'default-y="-246.44"' not in out  # F2 は B♭2 の 3 段下
    changed = [d for d in difflib.ndiff(text.splitlines(), out.splitlines()) if d[0] in "+-"]
    assert len(changed) == 5  # <note> 行と <step> 行を 1 行ずつ替え (2 + 2), <alter> 行を 1 行消す (1)
    assert fc.fix_state(out, fix) == "new"
    assert fc.bar_problems(bar_text(out)) == []


def test_retune_writes_an_alter_with_the_same_indent_when_the_new_note_needs_one():
    xml = note_xml("C4", 4, "quarter", 1, 1)
    out = fc.with_pitch_and_length(xml, "B-3", 1.0)
    assert "          <step>B</step>\n          <alter>-1</alter>\n          <octave>3</octave>\n" in out
    assert "<step>C</step>" not in out and out.count("<pitch>") == 1


def test_lengthening_a_note_updates_duration_and_type_and_removes_the_beam():
    xml = note_xml("F4", 2, "eighth", 1, 1, beam="begin")
    out = fc.with_pitch_and_length(xml, "F4", 1.0)
    assert "<duration>4</duration>" in out and "<type>quarter</type>" in out
    assert "<beam" not in out and "<pitch>" in out
    assert fc.with_pitch_and_length(xml, "F4", 0.5) == xml  # 何も替えなければそのまま


def test_split_a_half_note_into_two_quarters_keeps_the_bar_length():
    rh = [note_xml("A4", 8, "half", 1, 1), note_xml("D4", 4, "quarter", 1, 1, x="150"),
          note_xml("E4", 4, "quarter", 1, 1, x="219.43")]
    lh = [note_xml("F2", 4, "quarter", 2, 5), note_xml("C3", 4, "quarter", 2, 5),
          note_xml("F3", 8, "half", 2, 5, x="180.8", y="-209.08")]
    text = score(measure(rh, lh))
    fix = fc.Fix(1, LH, 2.0, ("F3", 2.0), (("D3", 1.0), ("E3", 1.0)), "t", "r")
    out = fc.apply_fix(text, fix)
    assert rows(out)[-2:] == [(2, 5, 8, 4, "D3", False), (2, 5, 12, 4, "E3", False)]
    assert fc.bar_problems(bar_text(out)) == []
    assert 'default-x="219.43" default-y="-214.08"' in out  # 2 つ目の x は同じ拍の他の音に合わせ, y は 1 段上
    assert 'default-x="180.8" default-y="-219.08"' in out  # D3 は F3 の 2 段下
    assert "<type>half</type>" not in bar_text(out).split("<backup>")[1]
    assert fc.fix_state(out, fix) == "new"


def test_drop_a_chord_leader_promotes_the_next_chord_note():
    rh = [note_xml("B-3", 6, "quarter", 1, 1, dot=True), note_xml("D4", 6, "quarter", 1, 1, chord=True, dot=True),
          note_xml("A4", 6, "quarter", 1, 1, chord=True, dot=True), note_xml("G4", 2, "eighth", 1, 1),
          note_xml("A3", 4, "quarter", 1, 1), note_xml("C4", 4, "quarter", 1, 1)]
    text = score(measure(rh, [note_xml("G2", 16, "whole", 2, 5)]))
    out = fc.apply_fix(text, fc.drop(1, RH, 0.0, "B-3", 1.5, "t", "r"))
    assert rows(out)[:3] == [(1, 1, 0, 6, "D4", False), (1, 1, 0, 6, "A4", True), (1, 1, 6, 2, "G4", False)]
    assert "\n\n" not in out  # 消した音の後に空行を残さない
    assert fc.bar_problems(bar_text(out)) == []


def test_drop_then_lengthen_leaves_a_well_formed_beam_group():
    rh = [note_xml("A4", 8, "half", 1, 1), note_xml("G4", 4, "quarter", 1, 1),
          note_xml("F4", 2, "eighth", 1, 1, beam="begin"), note_xml("B-4", 2, "eighth", 1, 1, beam="end")]
    text = score(measure(rh, [note_xml("F2", 16, "whole", 2, 5)]))
    fixes = (fc.drop(1, RH, 3.5, "B-4", 0.5, "t", "r"), fc.Fix(1, RH, 3.0, ("F4", 0.5), (("F4", 1.0),), "t", "r"))
    out, states = fc.apply_all(text, fixes)
    assert states == ["old", "old"]
    assert rows(out)[:3] == [(1, 1, 0, 8, "A4", False), (1, 1, 8, 4, "G4", False), (1, 1, 12, 4, "F4", False)]
    assert "<beam" not in out and fc.bar_problems(bar_text(out)) == []


def test_lengthening_before_dropping_is_refused_because_the_next_note_moves():
    rh = [note_xml("A4", 8, "half", 1, 1), note_xml("G4", 4, "quarter", 1, 1),
          note_xml("F4", 2, "eighth", 1, 1, beam="begin"), note_xml("B-4", 2, "eighth", 1, 1, beam="end")]
    text = score(measure(rh, [note_xml("F2", 16, "whole", 2, 5)]))
    wrong = (fc.Fix(1, RH, 3.0, ("F4", 0.5), (("F4", 1.0),), "t", "r"), fc.drop(1, RH, 3.5, "B-4", 0.5, "t", "r"))
    with pytest.raises(ValueError, match="小節 1"):
        fc.apply_all(text, wrong)


def test_bar_problems_reports_a_short_bar_and_a_broken_beam():
    rh = [note_xml("A4", 8, "half", 1, 1), note_xml("G4", 4, "quarter", 1, 1),
          note_xml("F4", 2, "eighth", 1, 1, beam="begin")]  # 14 しかなく, beam が閉じない
    problems = fc.bar_problems(measure(rh, [note_xml("F2", 16, "whole", 2, 5)]))
    assert any("3.5 拍" in p for p in problems) and any("閉じていない" in p for p in problems)
    lone_end = measure([note_xml("A4", 16, "whole", 1, 1, beam="end")], [note_xml("F2", 16, "whole", 2, 5)])
    assert any("begin 無し" in p for p in fc.bar_problems(lone_end))


# --------------------------------------------------------------------------
# 編集表の適用 (状態の判定と冪等性)
# --------------------------------------------------------------------------
def test_apply_all_is_idempotent_and_reports_the_state_of_each_row():
    text = score(eighths_bar_lh())
    fixes = (fc.retune(1, LH, 0.0, "B-2", 0.5, "F2", "t", "r"), fc.retune(1, LH, 1.0, "B-3", 0.5, "A3", "t", "r"))
    out, states = fc.apply_all(text, fixes)
    assert states == ["old", "old"] and out != text
    again, states2 = fc.apply_all(out, fixes)
    assert states2 == ["new", "new"] and again == out


def test_a_row_that_matches_neither_state_stops_with_the_bar_and_beat():
    text = score(eighths_bar_lh())
    with pytest.raises(ValueError, match="小節 1 .* 拍 1"):
        fc.apply_all(text, (fc.retune(1, LH, 0.0, "D2", 0.5, "F2", "t", "r"),))
    with pytest.raises(ValueError):  # 長さが違う音も「直す前」とは見なさない
        fc.apply_all(text, (fc.retune(1, LH, 0.0, "B-2", 1.0, "F2", "t", "r"),))


@pytest.mark.parametrize("kwargs", [{"accidental": True}, {"tie": "start"}])
def test_notes_with_an_accidental_or_a_tie_are_not_retuned(kwargs):
    xml = note_xml("B-3", 4, "quarter", 2, 5, **kwargs)
    with pytest.raises(ValueError):
        fc.with_pitch_and_length(xml, "C3", 1.0)


def test_a_tied_note_is_not_dropped_and_a_dotted_note_is_not_lengthened():
    tied = score(measure([note_xml("A4", 16, "whole", 1, 1)],
                         [note_xml("F2", 12, "half", 2, 5, dot=True, tie="start"), note_xml("C3", 4, "quarter", 2, 5)]))
    with pytest.raises(ValueError, match="tie"):
        fc.apply_fix(tied, fc.drop(1, LH, 0.0, "F2", 3.0, "t", "r"))
    dotted = note_xml("F2", 12, "half", 2, 5, dot=True)
    with pytest.raises(ValueError, match="付点"):
        fc.with_pitch_and_length(dotted, "F2", 4.0)


@pytest.mark.parametrize("old", ["C4", "E4"])  # 和音の先頭 (C4) も 2 つ目 (E4) も分けない
def test_a_note_in_a_chord_is_not_split(old):
    rh = [note_xml("C4", 8, "half", 1, 1), note_xml("E4", 8, "half", 1, 1, chord=True),
          note_xml("G4", 8, "half", 1, 1)]
    text = score(measure(rh, [note_xml("F2", 16, "whole", 2, 5)]))
    split = fc.Fix(1, RH, 0.0, (old, 2.0), ((old, 1.0), ("D5", 1.0)), "t", "r")
    with pytest.raises(ValueError, match="和音"):
        fc.apply_fix(text, split)


def test_describe_names_the_bars_the_notes_and_the_target():
    fix = next(f for f in fc.FIXES if (f.bar, f.staff, f.beat) == (20, LH, 0.0))
    text = fc.describe(fix)
    assert "正本の小節 24 / 伴奏の小節 20 拍 1 左手" in text
    assert "B♭2(0.5 拍) -> F2(0.5 拍)" in text and "音符 53" in text


def test_main_applies_the_table_to_the_file_and_check_reports_the_state(tmp_path, monkeypatch, capsys):
    path = tmp_path / "score.musicxml"
    original = score(eighths_bar_lh()).encode("utf-8")
    path.write_bytes(original)
    monkeypatch.setattr(fc, "FIXES", (fc.retune(1, LH, 0.0, "B-2", 0.5, "F2", "t", "r"),))
    assert fc.main(["fix", "--score", str(path), "--check"]) == 1
    assert path.read_bytes() == original  # --check は書き換えない
    assert fc.main(["fix", "--score", str(path)]) == 0
    fixed = path.read_bytes()
    assert fixed != original and b"\r" not in fixed
    assert fc.main(["fix", "--score", str(path), "--check"]) == 0
    assert fc.main(["fix", "--score", str(path)]) == 0
    assert path.read_bytes() == fixed  # 2 回目は何も書き換えない
    assert "修正済み" in capsys.readouterr().out


def test_main_refuses_a_crlf_score(tmp_path):
    path = tmp_path / "crlf.musicxml"
    path.write_bytes(score(eighths_bar_lh()).replace("\n", "\r\n").encode("utf-8"))
    with pytest.raises(SystemExit):
        fc.main(["fix", "--score", str(path)])


# --------------------------------------------------------------------------
# 休符の長さ (MuseScore は休符を <type> / <dot> から読む)
# --------------------------------------------------------------------------
def bar_with_bad_rest():
    """小節 51 と同じ形. 右手は 8 分 3 つ (1.5 拍) + duration 10 (2.5 拍) の休符で, 休符は 2 分音符 + 付点 (3 拍) と書かれている."""
    rh = [note_xml("C5", 2, "eighth", 1, 1, beam="begin"), note_xml("A4", 2, "eighth", 1, 1, beam="continue"),
          note_xml("C5", 2, "eighth", 1, 1, beam="end"), rest_xml(10, "half", dots=1)]
    return measure(rh, [note_xml("F2", 16, "whole", 2, 5)])


def test_notated_divisions_reads_type_dots_and_tuplets():
    assert fc.notated_divisions(rest_xml(4, "quarter")) == 4
    assert fc.notated_divisions(rest_xml(12, "half", dots=1)) == 12
    assert fc.notated_divisions(rest_xml(14, "half", dots=2)) == 14
    assert fc.notated_divisions(rest_xml(1, "16th")) == 1
    assert fc.notated_divisions('<note><rest measure="yes"/><duration>16</duration></note>') is None
    triplet = rest_xml(4, "eighth").replace("        <staff>", "        <time-modification><actual-notes>3</actual-notes>"
                                            "<normal-notes>2</normal-notes></time-modification>\n        <staff>")
    assert fc.notated_divisions(triplet) == Fraction(4, 3)
    with pytest.raises(ValueError):
        fc.notated_divisions(rest_xml(1, "32nd"))


def test_rest_pieces_splits_into_plain_values():
    assert [fc.rest_pieces(n) for n in (10, 6, 3, 16, 15, 1)] == [[8, 2], [4, 2], [2, 1], [16], [8, 4, 2, 1], [1]]


def test_inconsistent_rests_finds_only_the_rests_whose_length_disagrees():
    bad = fc.inconsistent_rests(bar_text(score(bar_with_bad_rest())))
    assert [(n.staff, n.onset, n.dur) for n in bad] == [(1, 6, 10)]
    ok = measure([note_xml("C5", 2, "eighth", 1, 1), rest_xml(14, "half", dots=2)], [note_xml("F2", 16, "whole", 2, 5)])
    assert fc.inconsistent_rests(ok) == []


def test_normalize_rests_splits_the_rest_and_keeps_the_bar_length():
    text = score(bar_with_bad_rest())
    assert [d for _, d in fc.length_mismatches(text)] == ["書かれた長さ 12 / duration 10"]
    fixed, found = fc.normalize_rests(text)
    assert len(found) == 1 and "拍 2.5 右手" in found[0] and "2 拍 + 0.5 拍" in found[0]
    assert fc.length_mismatches(fixed) == []
    assert fc.bar_problems(bar_text(fixed)) == []
    assert rows(fixed) == [(1, 1, 0, 2, "C5", False), (1, 1, 2, 2, "A4", False), (1, 1, 4, 2, "C5", False),
                           (1, 1, 6, 8, "", False), (1, 1, 14, 2, "", False), (2, 5, 0, 16, "F2", False)]
    pieces = [n for n in fc.NOTE_RE.findall(fixed) if "<rest/>" in n]
    assert [re.search(r"<type>(\w+)</type>", p).group(1) for p in pieces] == ["half", "eighth"]
    assert all('print-object="no"' in p and "<dot" not in p for p in pieces)  # 見えない休符のまま, 付点は外す
    assert 'default-x="99.43"' in pieces[0] and "default-x" not in pieces[1] and "default-y" not in pieces[1]
    assert ET.fromstring(fixed.encode("utf-8")) is not None
    assert fc.normalize_rests(fixed) == (fixed, [])  # 2 回目は何もしない


def test_normalize_rests_leaves_consistent_and_whole_measure_rests_alone():
    whole = ('      <note>\n        <rest measure="yes"/>\n        <duration>16</duration>\n        <voice>1</voice>\n'
             '        <staff>1</staff>\n        </note>')
    text = score(measure([whole], [note_xml("F2", 16, "whole", 2, 5)]),
                 measure([note_xml("C5", 2, "eighth", 1, 1), rest_xml(14, "half", dots=2)],
                         [note_xml("F2", 16, "whole", 2, 5)]))
    assert fc.normalize_rests(text) == (text, [])


def test_normalize_rests_handles_several_bars_and_both_staves():
    text = score(bar_with_bad_rest(), eighths_bar_lh(), bar_with_bad_rest())
    fixed, found = fc.normalize_rests(text)
    assert len(found) == 2 and found[0].startswith("正本の小節 5 ") and found[1].startswith("正本の小節 7 ")
    assert fc.length_mismatches(fixed) == []
    assert bar_text(fixed, 2) == bar_text(text, 2)  # 食い違いの無い小節は 1 字も変わらない
    both = score(measure([note_xml("C5", 2, "eighth", 1, 1), rest_xml(14, "half", dots=1)],
                         [note_xml("F2", 2, "eighth", 2, 5), rest_xml(14, "half", dots=1, staff=2, voice=5)]))
    fixed, found = fc.normalize_rests(both)
    assert len(found) == 2 and "右手" in found[0] and "左手" in found[1] and "8 拍" not in found[0]
    assert rows(fixed) == [(1, 1, 0, 2, "C5", False), (1, 1, 2, 8, "", False), (1, 1, 10, 4, "", False),
                           (1, 1, 14, 2, "", False), (2, 5, 0, 2, "F2", False), (2, 5, 2, 8, "", False),
                           (2, 5, 10, 4, "", False), (2, 5, 14, 2, "", False)]
    assert fc.length_mismatches(fixed) == [] and fc.bar_problems(bar_text(fixed)) == []


def test_split_rest_refuses_a_tied_or_tuplet_rest():
    with pytest.raises(ValueError):
        fc.split_rest(rest_xml(10, "half", dots=1).replace("        <staff>", '        <tie type="start"/>\n        <staff>'), [8, 2])


def test_main_fixes_the_rest_lengths_too_and_check_reports_them(tmp_path, monkeypatch, capsys):
    path = tmp_path / "score.musicxml"
    original = score(bar_with_bad_rest()).encode("utf-8")
    path.write_bytes(original)
    monkeypatch.setattr(fc, "FIXES", ())
    assert fc.main(["fix", "--score", str(path), "--check"]) == 1
    assert path.read_bytes() == original
    assert "休符の長さの食い違い 1 個" in capsys.readouterr().out
    assert fc.main(["fix", "--score", str(path)]) == 0
    fixed = path.read_bytes()
    assert fixed != original and b"\r" not in fixed
    assert fc.main(["fix", "--score", str(path), "--check"]) == 0
    assert "休符の長さの食い違い 0 個" in capsys.readouterr().out


# --------------------------------------------------------------------------
# 編集表そのもの
# --------------------------------------------------------------------------
def test_table_targets_and_new_pitches():
    assert len(fc.FIXES) == 17
    assert {f.target for f in fc.FIXES} == {"音符 5", "音符 29", "音符 35", "音符 39", "音符 53"}
    assert sorted({f.bar for f in fc.FIXES}) == [6, 12, 20, 37, 39, 41, 45, 67, 69, 75]
    # 置き換える音はヘ長調の構成音 (臨時記号なし)
    assert all("-" not in name and "#" not in name for f in fc.FIXES for name, _ in f.new)
    assert all(f.reading and f.old[1] > 0 for f in fc.FIXES)


def test_table_rows_do_not_overlap_in_the_same_bar_and_staff():
    seen = set()
    for f in fc.FIXES:
        key = (f.bar, f.staff, f.beat, f.old[0])
        assert key not in seen
        seen.add(key)


# --------------------------------------------------------------------------
# 本物の譜面
# --------------------------------------------------------------------------
@pytest.fixture(scope="module")
def real_text():
    with open(fc.ACCOMP_XML, "rb") as f:
        raw = f.read()
    assert b"\r" not in raw and not raw.startswith(b"\xef\xbb\xbf")  # LF のまま, BOM 無し
    return raw.decode("utf-8")


def test_real_score_has_every_fix_applied(real_text):
    states = [fc.fix_state(real_text, f) for f in fc.FIXES]
    assert states == ["new"] * len(fc.FIXES)
    assert fc.apply_all(real_text, fc.FIXES)[0] == real_text


def test_real_score_bars_keep_their_length_and_beams(real_text):
    spans = fc.measure_spans(real_text)
    assert len(spans) == 91  # 91 小節 = 364 拍
    bad = {i: fc.bar_problems(real_text[s:e]) for i, (s, e) in enumerate(spans, 1)}
    assert {i: p for i, p in bad.items() if p} == {}


def test_real_score_notated_lengths_agree_with_durations(real_text):
    # MuseScore は休符を <type> / <dot> から読む. 食い違いが 1 つあると小節が長く読まれ, 以降が半拍ずれる (小節 51)
    assert fc.length_mismatches(real_text) == []
    assert fc.normalize_rests(real_text) == (real_text, [])


def test_real_score_bar_51_right_hand_ends_with_a_half_and_an_eighth_rest(real_text):
    right = [r[3:5] for r in rows(real_text, 51) if r[0] == RH]
    assert right == [(2, "C5"), (2, "A4"), (2, "C5"), (8, ""), (2, "")]


def test_real_score_is_well_formed_xml_with_two_parts(real_text):
    root = ET.fromstring(real_text.encode("utf-8"))
    parts = {p.get("id"): p for p in root.iter("part")}
    assert set(parts) == {"P1", "P2"}
    assert len(parts["P2"].findall("measure")) == 91
    # 直した小節の音数 (右手 + 左手) が編集表どおり
    bars = parts["P2"].findall("measure")
    counts = {i: len(bars[i - 1].findall("note")) for i in (6, 39, 41)}
    assert counts == {6: 14, 39: 11, 41: 9}  # 直す前は 15 / 10 / 10 (小節 6 は 1 つ消え, 39 は 1 つ増え, 41 は 1 つ消えた)

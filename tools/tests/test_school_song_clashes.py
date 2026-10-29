"""校歌 BGM の歌とピアノ伴奏のぶつかりを数える道具 (#27)。

数え方の核 (音程の分類・重なりの取り出し・重さ・番号) は music21 なしで動くので, CI でも走る。
music21 で譜面を読む部分と本物の譜面の不変条件は `pytest.importorskip` で守る (CI の
requirements-test.txt に music21 は無く, 手元でだけ走る)。

譜面の抜粋は #27 の監査が名指しした重い 3 か所 (歌の音符 53 / 29 / 35)。位置は小節の頭を 0 とした拍。
歌詞は著作物なので、音の高さと拍だけを書き、仮名は書かない。
"""

import os
import sys

import pytest

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SONG_DIR = os.path.join(REPO, "tools", "audio", "school_song")
if SONG_DIR not in sys.path:
    sys.path.insert(0, SONG_DIR)

import count_clashes as cc  # noqa: E402

_STEPS = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi(name):
    """'B-3' (music21 の表記, - が♭) / 'F#4' / 'A4' -> MIDI 番号."""
    i = 1
    while name[i] in "#-":
        i += 1
    shift = name[1:i].count("#") - name[1:i].count("-")
    return 12 * (int(name[i:]) + 1) + _STEPS[name[0]] + shift


def tone(name, start, end, voice):
    return cc.Tone(start, end, midi(name), name, voice)


def note_clashes(vocal, accomp):
    return cc.group_by_vocal_note(cc.find_clashes(vocal, accomp))


# --------------------------------------------------------------------------
# 音程の分類
# --------------------------------------------------------------------------
@pytest.mark.parametrize("semitones", range(0, 36))
def test_clash_kind_by_pitch_class_distance(semitones):
    """半音の距離 % 12 が 1 / 11 / 6 のものだけがぶつかる。複合音程 (9 度・14 度) も同じ系統。"""
    expected = {1: "短2度系", 11: "長7度系", 6: "三全音"}.get(semitones % 12)
    assert cc.clash_kind(69, 69 - semitones) == expected
    assert cc.clash_kind(69 - semitones, 69) == expected  # 向きに依らない


def test_clash_kind_examples_from_the_audit():
    assert cc.clash_kind(midi("A4"), midi("B-3")) == "長7度系"  # 音符 53: A4 の下の B♭3
    assert cc.clash_kind(midi("E4"), midi("B-3")) == "三全音"  # 音符 29: E4 の下の B♭3
    assert cc.clash_kind(midi("E4"), midi("F3")) == "長7度系"  # 音符 35: E4 の下の F3
    assert cc.clash_kind(midi("A4"), midi("F3")) is None  # 長 3 度 + オクターブ
    assert cc.clash_kind(midi("A4"), midi("A3")) is None  # オクターブ


def test_midi_helper():
    assert midi("A4") == 69 and midi("C4") == 60 and midi("B-3") == 58 and midi("F#4") == 66


# --------------------------------------------------------------------------
# 重なりの取り出し
# --------------------------------------------------------------------------
def test_find_clashes_ignores_notes_that_only_touch():
    vocal = [tone("A4", 0, 2, "歌")]
    accomp = [tone("B-3", 2, 3, "左手"), tone("B-3", -1, 0, "左手")]
    assert cc.find_clashes(vocal, accomp) == []


def test_find_clashes_reports_the_overlapping_span_only():
    vocal = [tone("A4", 0, 2, "歌")]
    accomp = [tone("B-3", 1, 3, "左手")]
    (c,) = cc.find_clashes(vocal, accomp)
    assert (c.start, c.end, c.beats) == (1, 2, 1)
    assert (c.kind, c.semitones, c.vocal_index) == ("長7度系", 11, 0)
    assert c.accomp.voice == "左手"


def test_find_clashes_skips_consonant_overlaps_and_keeps_vocal_order():
    vocal = [tone("C4", 0, 1, "歌"), tone("A4", 1, 3, "歌")]
    accomp = [tone("F3", 0, 4, "左手"), tone("B-3", 0, 4, "左手"), tone("A4", 0, 4, "右手")]
    found = cc.find_clashes(vocal, accomp)
    # C4 は F3 (完全 5 度), B♭3 (長 2 度), A4 (長 6 度) のどれにもぶつからない。A4 が B♭3 (長 7 度) にだけぶつかる
    assert [(c.vocal_index, c.accomp.name) for c in found] == [(1, "B-3")]


def test_union_length_merges_overlaps_and_adjacent_spans():
    assert cc.union_length([]) == 0
    assert cc.union_length([(0, 1), (0.5, 1.5)]) == pytest.approx(1.5)
    assert cc.union_length([(0, 0.5), (0.5, 1)]) == pytest.approx(1.0)
    assert cc.union_length([(0, 0.5), (1, 1.5)]) == pytest.approx(1.0)
    assert cc.union_length([(1, 1.5), (0, 0.5)]) == pytest.approx(1.0)  # 順不同でもよい


def test_weight_is_the_union_not_the_sum_of_simultaneous_clashes():
    """同じ歌の音に 2 つの音が同時にぶつかっても, 重さは区間の和集合 (0.5 拍) であって 1 拍ではない。"""
    vocal = [tone("E4", 0, 1, "歌")]
    accomp = [tone("F3", 0.5, 1, "左手"), tone("B-3", 0.5, 1, "右手")]
    (n,) = note_clashes(vocal, accomp)
    assert len(n.clashes) == 2
    assert n.beats == pytest.approx(0.5)
    assert not n.heavy()


def test_heavy_threshold_is_one_beat_and_configurable():
    vocal = [tone("E4", 0, 1, "歌")]
    light = note_clashes(vocal, [tone("F3", 0, 0.75, "左手")])[0]
    full = note_clashes(vocal, [tone("F3", 0, 1, "左手")])[0]
    assert not light.heavy() and full.heavy()
    assert light.heavy(threshold=0.5)
    assert not full.heavy(threshold=1.5)


# --------------------------------------------------------------------------
# 歌の音符番号
# --------------------------------------------------------------------------
@pytest.mark.parametrize(
    "index, verse, note",
    [(0, 1, 0), (53, 1, 53), (103, 1, 103), (104, 2, 0), (104 + 35, 2, 35), (208 + 29, 3, 29), (311, 3, 103)],
)
def test_note_numbering_counts_per_verse_with_the_pickup_as_zero(index, verse, note):
    t = tone("A4", 0, 1, "歌")
    n = cc.NoteClash(index, t, (), 0.0)
    assert (n.verse, n.note) == (verse, note)


def test_where_maps_to_the_canonical_and_accompaniment_measures():
    # 音符 53 は正本の小節 24 の頭 = 伴奏の小節 20 の頭 (正本の小節 = 伴奏の小節 + 4)
    assert cc.where(76.0, 5) == "正本の小節 24 / 伴奏の小節 20 の拍 1"
    assert cc.where(47.0, 5) == "正本の小節 16 / 伴奏の小節 12 の拍 4"


# --------------------------------------------------------------------------
# 監査が名指しした重い 3 か所 (修正前の譜面の抜粋)
# --------------------------------------------------------------------------
def _bar_53(left_hand):
    """伴奏の小節 20 (歌の音符 53): 歌 A4 が 2 拍, 右手も A4。左手は 8 分音符 4 つ (拍 1〜2)。"""
    vocal = [tone("A4", 0, 2, "歌")]
    accomp = [tone("A4", 0, 2, "右手")]
    accomp += [tone(n, s, s + 0.5, "左手") for n, s in left_hand]
    return vocal, accomp


def _bar_29(rh_last, lh_beat4, lh_last):
    """伴奏の小節 12 (歌の音符 29): 歌 E4 が拍 4。右手 E4 + 次の 8 分音符, 左手は拍 4 の 8 分音符 2 つ。"""
    vocal = [tone("E4", 3, 4, "歌")]
    accomp = [tone("E4", 3, 3.5, "右手"), tone(rh_last, 3.5, 4, "右手"),
              tone(lh_beat4, 3, 3.5, "左手"), tone(lh_last, 3.5, 4, "左手")]
    return vocal, accomp


def _bar_35(left_hand):
    """伴奏の小節 39 (歌の音符 35): 歌 E4 が拍 4。左手は拍 3〜4 に (名前, 開始, 終了)。"""
    vocal = [tone("E4", 3, 4, "歌")]
    accomp = [tone("E4", 3, 4, "右手")] + [tone(n, s, e, "左手") for n, s, e in left_hand]
    return vocal, accomp


def test_note_53_before_the_fix_is_heavy_with_two_octave_b_flats():
    vocal, accomp = _bar_53([("B-2", 0), ("F3", 0.5), ("B-3", 1), ("F3", 1.5)])
    (n,) = note_clashes(vocal, accomp)
    assert n.heavy() and n.beats == pytest.approx(1.0)
    assert sorted(c.accomp.name for c in n.clashes) == ["B-2", "B-3"]
    assert all(c.kind == "長7度系" for c in n.clashes)


def test_note_53_after_the_fix_has_no_clash():
    vocal, accomp = _bar_53([("F2", 0), ("F3", 0.5), ("A3", 1), ("F3", 1.5)])
    assert note_clashes(vocal, accomp) == []


def test_note_29_before_the_fix_is_heavy_with_a_tritone_and_a_major_seventh():
    vocal, accomp = _bar_29(rh_last="B-3", lh_beat4="B-3", lh_last="F3")
    (n,) = note_clashes(vocal, accomp)
    assert n.heavy() and n.beats == pytest.approx(1.0)
    assert {(c.accomp.voice, c.accomp.name, c.kind) for c in n.clashes} == {
        ("左手", "B-3", "三全音"), ("右手", "B-3", "三全音"), ("左手", "F3", "長7度系")}


def test_note_29_after_the_fix_has_no_clash():
    vocal, accomp = _bar_29(rh_last="C4", lh_beat4="C3", lh_last="G3")
    assert note_clashes(vocal, accomp) == []


def test_note_35_before_the_fix_is_heavy_with_a_half_note_f3():
    vocal, accomp = _bar_35([("F3", 2, 4)])
    (n,) = note_clashes(vocal, accomp)
    assert n.heavy() and n.beats == pytest.approx(1.0)
    assert [(c.accomp.name, c.kind) for c in n.clashes] == [("F3", "長7度系")]


def test_note_35_after_the_fix_has_no_clash():
    vocal, accomp = _bar_35([("D3", 2, 3), ("E3", 3, 4)])
    assert note_clashes(vocal, accomp) == []


def test_report_lists_each_clash_with_the_verse_and_note_numbers():
    vocal, accomp = _bar_53([("B-2", 0), ("F3", 0.5), ("B-3", 1), ("F3", 1.5)])
    text = cc.format_report(vocal, accomp, 5, note_clashes(vocal, accomp))
    assert "重い" in text and "音符 0 " in text  # この抜粋は 1 音だけなので通し番号 0
    assert "B♭2" in text and "B♭3" in text and "長7度系" in text


# --------------------------------------------------------------------------
# music21 で譜面を読む部分 (CI には music21 が無いので手元でだけ走る)
# --------------------------------------------------------------------------
def test_sounding_tones_merges_ties_splits_chords_and_skips_chord_symbols():
    pytest.importorskip("music21")
    from music21 import chord, harmony, note, stream, tie

    part = stream.Part()
    m1, m2 = stream.Measure(number=1), stream.Measure(number=2)
    sym = harmony.ChordSymbol("F")
    c4 = note.Note("C4", quarterLength=3)
    c4.tie = tie.Tie("start")
    e_g = chord.Chord(["E4", "G4"], quarterLength=1)
    c4_again = note.Note("C4", quarterLength=2)
    c4_again.tie = tie.Tie("stop")
    m1.insert(0, sym)
    m1.insert(0, c4)
    m1.insert(3, e_g)
    m2.insert(0, c4_again)
    part.append([m1, m2])

    tones = cc.sounding_tones(part, "右手")
    assert [(t.name, t.start, t.end) for t in tones] == [
        ("C4", 0.0, 6.0),  # 小節をまたぐ tie は 1 音
        ("E4", 3.0, 4.0),  # 和音は構成音ごと
        ("G4", 3.0, 4.0),
    ]
    assert {t.voice for t in tones} == {"右手"}


def test_a_chord_note_can_be_tied_on_its_own():
    pytest.importorskip("music21")
    from music21 import chord, stream, tie

    part = stream.Part()
    m1, m2 = stream.Measure(number=1), stream.Measure(number=2)
    first = chord.Chord(["C4", "E4"], quarterLength=4)
    first.notes[0].tie = tie.Tie("start")  # C4 だけ次へ続く
    second = chord.Chord(["C4", "G4"], quarterLength=2)
    second.notes[0].tie = tie.Tie("stop")
    m1.insert(0, first)
    m2.insert(0, second)
    part.append([m1, m2])

    tones = cc.sounding_tones(part, "左手")
    assert [(t.name, t.start, t.end) for t in tones] == [
        ("C4", 0.0, 6.0), ("E4", 0.0, 4.0), ("G4", 4.0, 6.0)]


def test_real_vocal_has_312_notes_and_the_numbering_matches_the_issue():
    pytest.importorskip("music21")
    vocal, first = cc.read_vocal()
    assert first == 5  # 正本の最初の小節番号
    assert len(vocal) == 3 * cc.NOTES_PER_VERSE  # 3 番 x (弱起 + 103 音)
    # 各番の弱起 (音符 0) の位置
    assert [vocal[i].start for i in (0, 104, 208)] == [19.0, 119.0, 239.0]
    # 音符 53 は小節 24 の頭の A4, 2 拍
    n53 = vocal[53]
    assert (n53.name, n53.start, n53.end) == ("A4", 76.0, 78.0)
    assert cc.where(n53.start, first) == "正本の小節 24 / 伴奏の小節 20 の拍 1"


def test_real_accomp_reads_only_the_piano_hands_within_the_song_length():
    pytest.importorskip("music21")
    accomp = cc.read_accomp()
    assert {t.voice for t in accomp} == {"右手", "左手"}
    assert max(t.end for t in accomp) <= 91 * cc.BEATS_PER_BAR + cc.EPS  # 91 小節 = 364 拍
    assert all(t.end > t.start for t in accomp)

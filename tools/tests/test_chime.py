"""tools/audio の時報チャイム (#68)。

学校のチャイム = ウェストミンスターの鐘、E major の 4 フレーズ 16 音 (#38)。
合成そのもの (se_chime) は 5 秒ほどかかるので鳴らさず、旋律のデータと、
commit 済みの se_chime.wav の長さが今の拍の設定と合っているかを見る。
"""

import inspect
import math
import os
import wave

import pytest

import sfx
import synth

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CHIME_WAV = os.path.join(REPO, "unity", "KatsushikaCampusDays", "Assets", "Audio", "SE", "se_chime.wav")

E_MAJOR = {"E", "F#", "G#", "A", "B", "C#", "D#"}
# ド = E4。B3 は下のソ
SOLFEGE = {"B3": "ソ", "E4": "ド", "F#4": "レ", "G#4": "ミ"}


def _pitch_class(name):
    return name[:-1]


def _midi(name):
    return round(69 + 12 * math.log2(synth.nf(name) / 440.0))


def test_four_phrases_sixteen_notes():
    assert len(sfx.CHIME_PHRASES) == 4
    assert all(len(p) == 4 for p in sfx.CHIME_PHRASES)
    assert sum(len(p) for p in sfx.CHIME_PHRASES) == 16


def test_all_notes_are_in_e_major():
    for phrase in sfx.CHIME_PHRASES:
        for name in phrase:
            assert _pitch_class(name) in E_MAJOR, name


def test_melody_is_do_mi_re_so():
    sung = [" ".join(SOLFEGE[n] for n in phrase) for phrase in sfx.CHIME_PHRASES]
    assert sung == ["ド ミ レ ソ", "ソ レ ミ ド", "ミ ド レ ソ", "ソ レ ミ ド"]


def test_intervals_from_the_tonic():
    """ド (E4) からの半音数: レ +2, ミ +4, 下のソ -5（移調していない）。"""
    tonic = _midi("E4")
    steps = {name: _midi(name) - tonic for phrase in sfx.CHIME_PHRASES for name in phrase}
    assert steps == {"E4": 0, "F#4": 2, "G#4": 4, "B3": -5}
    assert synth.nf("E4") == pytest.approx(329.6276, abs=1e-3)


@pytest.mark.parametrize("name, hz", [
    ("A4", 440.0), ("A3", 220.0), ("C4", 261.6256), ("B3", 246.9417), ("Bb3", 233.0819), ("C#5", 554.3653),
])
def test_note_frequency(name, hz):
    assert synth.nf(name) == pytest.approx(hz, abs=1e-3)


def test_phrase_timing():
    """各フレーズ = 4 分音符 3 つ + 2 分音符 1 つ + 1 拍の間 = 6 拍。"""
    assert sfx.CHIME_PHRASE_BEATS == 3 + 2 + 1
    tail = inspect.signature(sfx.se_chime).parameters["tail"].default
    total = len(sfx.CHIME_PHRASES) * sfx.CHIME_PHRASE_BEATS * sfx.CHIME_BEAT + tail
    assert total == pytest.approx(12.84)


def test_committed_wav_matches_the_timing():
    """commit 済みの se_chime.wav が今の拍の設定から作られている（ヘッダだけ読む）。"""
    tail = inspect.signature(sfx.se_chime).parameters["tail"].default
    total = len(sfx.CHIME_PHRASES) * sfx.CHIME_PHRASE_BEATS * sfx.CHIME_BEAT + tail
    with wave.open(CHIME_WAV, "rb") as w:
        assert w.getframerate() == synth.SR
        assert w.getnchannels() == 2
        assert w.getnframes() == synth.n_samples(total)

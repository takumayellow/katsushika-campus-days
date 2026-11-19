"""吹奏楽版の校歌譜 (build_school_song.py) の音の高さを確かめる (#29)。

- Trumpet (in B♭) / Horn (in F) を実音のまま置いて MusicXML に書くと, 読む側 (MuseScore) は
  記譜音とみなして全音 / 完全 5 度低く鳴らす。書き出した MusicXML を読み直して実音に戻した高さが,
  生成した実音と一致することを確かめる。
- 旋律 VERSE が, 歌の正本 score/school_song_vocal.musicxml の 1 番と同じ音であることを確かめる。

music21 が無い CI では飛ばす (requirements-test.txt に music21 は無い)。
"""

import os
import sys

import pytest

music21 = pytest.importorskip("music21")
from music21 import converter  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SONG_DIR = os.path.join(REPO, "tools", "audio", "school_song")
if SONG_DIR not in sys.path:
    sys.path.insert(0, SONG_DIR)

import build_school_song as B  # noqa: E402

VOCAL_XML = os.path.join(SONG_DIR, "score", "school_song_vocal.musicxml")
VOCAL_BAR_SHIFT = 4  # 正本の小節 = VERSE の小節 + 4 (1 番)


def sounding(part):
    """(拍の位置, 音高の組) の列. タイはつないでから数える."""
    return [
        (float(n.getOffsetInHierarchy(part)), tuple(sorted(p.midi for p in n.pitches)))
        for n in part.stripTies().flatten().notes
    ]


@pytest.fixture(scope="module")
def band_roundtrip(tmp_path_factory):
    score = B.build_band_score()
    path = tmp_path_factory.mktemp("band") / "band.musicxml"
    score.write("musicxml", fp=str(path))
    return score, converter.parse(str(path)).toSoundingPitch()


@pytest.mark.parametrize("name", ["Flute", "Trumpet", "Horn", "Piano", "Tuba"])
def test_musicxml_sounds_at_generated_pitch(band_roundtrip, name):
    generated, reread = band_roundtrip
    want = sounding(next(p for p in generated.parts if p.partName == name))
    got = sounding(next(p for p in reread.parts if p.partName == name))
    assert got == want


def test_trumpet_opens_on_concert_a4(band_roundtrip):
    """前奏の頭 (A5 の 1 オクターブ下) が実音 A4 = 69 で鳴る. 直す前は 67 (G4) だった."""
    _, reread = band_roundtrip
    trumpet = next(p for p in reread.parts if p.partName == "Trumpet")
    assert sounding(trumpet)[0][1] == (69,)


def test_horn_opens_on_concert_f_major(band_roundtrip):
    """小節 1 の和音が実音 F3-A3-C4. 直す前は B♭2-D3-F3 だった."""
    _, reread = band_roundtrip
    horn = next(p for p in reread.parts if p.partName == "Horn")
    assert sounding(horn)[0][1] == (53, 57, 60)


def verse_pitches(number):
    out, offset = [], 0.0
    for n in B.make_notes(B.VERSE[number]):
        if n.isNote:
            out.append((offset, n.pitch.midi))
        offset += float(n.quarterLength)
    return out


@pytest.mark.skipif(not os.path.isfile(VOCAL_XML), reason="歌の正本が無い")
def test_verse_matches_vocal_score():
    vocal = converter.parse(VOCAL_XML).parts[0]
    bars = {m.number: m for m in vocal.getElementsByClass("Measure")}
    for number in range(6, 30):
        got = [(float(n.offset), n.pitch.midi) for n in bars[number + VOCAL_BAR_SHIFT].notes]
        assert got == verse_pitches(number), f"VERSE 小節 {number}"

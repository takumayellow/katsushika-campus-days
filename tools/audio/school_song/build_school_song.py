"""東京理科大学校歌を楽譜から起こし, 吹奏楽風の BGM と歌唱用の譜面を作る.

    python tools/audio/school_song/build_school_song.py            # 譜面 + MIDI + WAV + manifest
    python tools/audio/school_song/build_school_song.py --no-wav   # 譜面と MIDI だけ (MuseScore 不要)

出力 (tools/audio/school_song/out/):
    school_song_vocal.musicxml   旋律 1 声部 + 1 番の歌詞. きりたん (NEUTRINO) 等に渡す用
    school_song_band.musicxml    吹奏楽風 5 声部 (Fl / Tp / Hn / Pf / Tuba). 前奏 + 1 番 + 2 番の形
    school_song_band.mid         同じ内容の MIDI
WAV は MuseScore 4 の CLI でレンダリングし, 正規化して
    unity/KatsushikaCampusDays/Assets/Audio/BGM/bgm_school_song.wav
に置く. 生成後は tools/audio/manifest.json と Assets/Audio/README.md も更新する.

旋律の出典は tools/audio/README_school_song.md を参照 (F 長調 4/4, 公式音源の実測テンポ ♩≈104).
"""
from __future__ import annotations

import argparse
import copy
import os
import shutil
import subprocess
import sys
import tempfile
import time
import wave

import numpy as np
from music21 import chord as m21chord
from music21 import clef, dynamics, instrument, key, metadata, meter, note, stream, tempo, tie

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO_TOOLS = os.path.dirname(HERE)
sys.path.insert(0, AUDIO_TOOLS)

import analyze  # noqa: E402
import build_audio  # noqa: E402
import synth as S  # noqa: E402

ROOT = os.path.abspath(os.path.join(AUDIO_TOOLS, "..", ".."))
OUT_DIR = os.path.join(HERE, "out")
BGM_NAME = "bgm_school_song"
BGM_PATH = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Audio", "BGM", BGM_NAME + ".wav")
MUSESCORE_CANDIDATES = (
    os.environ.get("MUSESCORE_EXE", ""),
    r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe",
    "/usr/bin/mscore",
    "/usr/bin/musescore4",
)

TEMPO_BPM = 104
KEY = "F"

# --------------------------------------------------------------------------
# 譜面データ
#   1 音 = "音名:長さ[~][:歌詞]"  長さは w h q e s (付点は '.'), '~' は次の同音へタイ,
#   休符は "r:長さ". 歌詞が無い音はメリスマ (前の音節を延ばす).
# --------------------------------------------------------------------------
INTRO = {
    0: "r:h r:q A5:e. A5:s",
    1: "C6:h~ C6:e C6:e A5:e C6:e",
    2: "F6:h~ F6:e F6:e E6:e D6:e",
    3: "C6:e. C6:s C6:e C6:e C6:q r:e C5:e",
    4: "C5:q C5:q G5:q G5:e. A5:s",
    5: "F5:h~ F5:e r:e r:q",
}

# 歌 (1 番). 小節 5 の 4 拍目がアウフタクト「しん」.
VERSE = {
    5: "r:h r:q C4:e:し C4:e:ん",
    6: "F4:q.:せ G4:e:い A4:q:の A4:e.:い Bb4:s",
    7: "A4:q.:ぶ A4:e:き G4:q:も F4:e.:た A4:s:か",
    8: "C5:q.:ら F4:e:わ D5:q:こ C5:q:う",
    9: "C5:h.:ど C5:q:よ",
    10: "D5:h:か C5:q:た A4:e.:き Bb4:s:こ",
    11: "A4:q.:しょ G4:e:う F4:q:の F4:q:い",
    12: "A4:q.:し F4:e E4:q:ず E4:q:え",
    13: "C4:h.:を C4:q:ま",
    14: "D4:q.:も D4:e:り D4:q:て E4:q:さ",
    15: "F4:q:ら C4:q:に r:q F4:q:さ",
    16: "A4:q.:か G4:e F4:q:え A4:q:ゆ",
    17: "C5:h.:く F4:q:り",
    18: "D5:q.:が C5:e:く C5:q:の C5:q:せ",
    19: "F5:q:い C5:q:か r:q A4:e.:か Bb4:s",
    20: "A4:h:ぐ G4:q:わ G4:q:し",
    21: "F4:h.:き r:q",
    22: "E4:q:た E4:e:か F4:e:き G4:e.:こ G4:s:く C4:e:し C4:e:の",
    23: "F4:e.:ね E4:s:と F4:e:な G4:e:ら A4:h:ん",
    24: "G4:q:こ G4:e:う A4:e:よ Bb4:e.:う Bb4:s:の G4:e:ゆ G4:e:く",
    25: "A4:e.:て G4:s:か A4:e:が Bb4:e:や C5:q:く F4:e.:お A4:s:お",
    26: "C5:h~:わ C5:e C5:e:か A4:e C5:e:き",
    27: "F5:h~ F5:e F5:e:わ E5:e:か D5:e:き",
    28: "C5:e.:ち C5:s:は C5:e:お D5:e:ど C5:q:る r:e C4:e:わ",
    29: "C4:q:れ C4:q:ら G4:q:が G4:e.:が A4:s:く",
}
VERSE_END_REPEAT = "F4:h:え F4:e:ん r:e C4:e:し C4:e:ん"   # 1 括弧: 次の番へ
VERSE_END_FINAL = "F4:h:え F4:e:ん r:e r:q"                 # 2 括弧: 終止

# 各小節の和音 (小節番号 -> [(和音, 拍数), ...]). 拍数の合計は 4.
CHORDS = {
    0: [("F", 4)],
    1: [("F", 4)], 2: [("F", 4)], 3: [("C7", 4)], 4: [("C7", 4)], 5: [("F", 4)],
    6: [("F", 4)], 7: [("F", 4)], 8: [("F", 2), ("Bb", 1), ("F", 1)], 9: [("C7", 4)],
    10: [("Bb", 2), ("F", 2)], 11: [("F", 4)], 12: [("F", 2), ("C7", 2)], 13: [("C7", 4)],
    14: [("Gm", 2), ("C7", 2)], 15: [("F", 4)], 16: [("F", 4)], 17: [("F", 4)],
    18: [("Bb", 2), ("F", 2)], 19: [("F", 4)], 20: [("F", 2), ("C7", 2)], 21: [("F", 4)],
    22: [("C7", 4)], 23: [("F", 4)], 24: [("C7", 2), ("Gm", 2)], 25: [("F", 2), ("C7", 1), ("F", 1)],
    26: [("F", 4)], 27: [("F", 2), ("Bb", 2)], 28: [("C7", 2), ("F", 2)], 29: [("C7", 4)],
    30: [("F", 4)],
}
CHORD_TONES = {
    "F": ("F3", "A3", "C4"),
    "Bb": ("Bb3", "D4", "F4"),
    "C7": ("C4", "E4", "G4", "Bb4"),
    "Gm": ("G3", "Bb3", "D4"),
}
BASS_ROOT = {"F": "F2", "Bb": "Bb2", "C7": "C3", "Gm": "G2"}
BASS_FIFTH = {"F": "C3", "Bb": "F3", "C7": "G2", "Gm": "D3"}

DUR = {"w": 4.0, "h": 2.0, "q": 1.0, "e": 0.5, "s": 0.25}

LYRICS_ALL = (
    "1. 新生のいぶきも高ら若人よ / かたき故蹤のいしずえを / 守りて更に栄えゆく / 理学の精華かぐわしき /"
    " たかき鵠志の根とならん / 浩洋の行く手輝く / おお若き若き血は躍る / 我らが学園"
)


def parse_token(token: str) -> tuple:
    """'F4:q.~:せ' -> (pitch or None, quarterLength, tied, lyric)."""
    parts = token.split(":")
    name = parts[0]
    dur_s = parts[1]
    lyric = parts[2] if len(parts) > 2 else None
    tied = dur_s.endswith("~")
    if tied:
        dur_s = dur_s[:-1]
    base = DUR[dur_s[0]]
    if dur_s.endswith("."):
        base *= 1.5
    return (None if name == "r" else name.replace("b", "-") if len(name) == 3 else name, base, tied, lyric)


def make_notes(spec: str, octave_shift: int = 0, with_lyrics: bool = False) -> list:
    out = []
    prev_tied = False
    for token in spec.split():
        name, ql, tied, lyric = parse_token(token)
        if name is None:
            n = note.Rest(quarterLength=ql)
        else:
            n = note.Note(name, quarterLength=ql)
            if octave_shift:
                n.transpose(12 * octave_shift, inPlace=True)
            if with_lyrics and lyric:
                n.lyric = lyric
            if prev_tied and tied:
                n.tie = tie.Tie("continue")
            elif prev_tied:
                n.tie = tie.Tie("stop")
            elif tied:
                n.tie = tie.Tie("start")
        out.append(n)
        prev_tied = tied and name is not None
    return out


def measure(number: int, elements: list, extra: list | None = None) -> stream.Measure:
    m = stream.Measure(number=number)
    for e in extra or []:
        m.insert(0, e)
    for e in elements:
        m.append(e)
    return m


def chord_block(symbol: str, ql: float, octave_shift: int = 0) -> m21chord.Chord:
    c = m21chord.Chord(list(CHORD_TONES[symbol]), quarterLength=ql)
    if octave_shift:
        c.transpose(12 * octave_shift, inPlace=True)
    return c


def piano_bar(chords: list) -> list:
    """行進曲の「ウン・パ」: 1・3 拍はベース音, 2・4 拍は和音."""
    out = []
    beat = 0.0
    for symbol, beats in chords:
        b = 0.0
        while b < beats:
            on_beat = int(beat + b)
            if (beats - b) < 1.0:
                out.append(chord_block(symbol, beats - b))
                b = beats
                continue
            if on_beat % 2 == 0:
                out.append(note.Note(BASS_ROOT[symbol], quarterLength=1.0))
                out[-1].transpose(12, inPlace=True)
            else:
                out.append(chord_block(symbol, 0.5))
                out.append(note.Rest(quarterLength=0.5))
            b += 1.0
        beat += beats
    return out


def bass_bar(chords: list) -> list:
    out = []
    beat = 0.0
    for symbol, beats in chords:
        b = 0.0
        while b < beats:
            if (beats - b) < 1.0:
                out.append(note.Note(BASS_ROOT[symbol], quarterLength=beats - b))
                b = beats
                continue
            on_beat = int(beat + b)
            name = BASS_ROOT[symbol] if on_beat % 2 == 0 else BASS_FIFTH[symbol]
            out.append(note.Note(name, quarterLength=1.0))
            b += 1.0
        beat += beats
    return out


def pad_bar(chords: list) -> list:
    return [chord_block(symbol, beats) for symbol, beats in chords]


# --------------------------------------------------------------------------
# スコア組み立て
# --------------------------------------------------------------------------
def song_form() -> list:
    """(小節番号, 旋律 spec, 和音) の列. 前奏 → 1 番 → 2 番 (終止)."""
    form = []
    for n in range(0, 5):
        form.append((n, INTRO[n], CHORDS[n]))
    # 小節 5 は前奏の終わりと歌のアウフタクトが重なる
    form.append((5, VERSE[5], CHORDS[5]))
    for verse_index in range(2):
        for n in range(6, 30):
            form.append((n, VERSE[n], CHORDS[n]))
        form.append((30, VERSE_END_REPEAT if verse_index == 0 else VERSE_END_FINAL, CHORDS[30]))
    return form


def header_elements() -> list:
    return [key.Key(KEY), meter.TimeSignature("4/4"), tempo.MetronomeMark(number=TEMPO_BPM)]


def build_part(name: str, inst, bars: list, first_extra: list) -> stream.Part:
    p = stream.Part(id=name)
    p.partName = name
    p.insert(0, inst)
    for i, (number, elements) in enumerate(bars):
        p.append(measure(number, elements, first_extra if i == 0 else None))
    return p


def build_band_score() -> stream.Score:
    form = song_form()
    intro_bars = {0, 1, 2, 3, 4}

    flute, trumpet, horn, piano, tuba = [], [], [], [], []
    for number, mel, chords in form:
        if number in intro_bars:
            flute.append((number, make_notes(mel)))
            trumpet.append((number, make_notes(mel, octave_shift=-1)))
        elif number == 5:
            flute.append((number, make_notes(INTRO[5])))
            trumpet.append((number, make_notes(mel)))
        else:
            flute.append((number, make_notes(mel, octave_shift=1)))
            trumpet.append((number, make_notes(mel)))
        if number == 0:
            # 小節 0 は 4 拍目のアウフタクトだけ. 伴奏は全休符 (先頭の無音は trim_silence が落とす).
            horn.append((number, [note.Rest(quarterLength=4.0)]))
            piano.append((number, [note.Rest(quarterLength=4.0)]))
            tuba.append((number, [note.Rest(quarterLength=4.0)]))
        else:
            horn.append((number, pad_bar(chords)))
            piano.append((number, piano_bar(chords)))
            tuba.append((number, bass_bar(chords)))

    score = stream.Score()
    score.insert(0, metadata.Metadata(title="東京理科大学校歌 (吹奏楽風 BGM)", composer="大和憲史 / 編曲: 本ツール"))
    parts = [
        build_part("Flute", instrument.Flute(), flute, header_elements() + [dynamics.Dynamic("f")]),
        build_part("Trumpet", instrument.Trumpet(), trumpet, header_elements() + [dynamics.Dynamic("f")]),
        build_part("Horn", instrument.Horn(), horn, header_elements() + [dynamics.Dynamic("mp")]),
        build_part("Piano", instrument.Piano(), piano, header_elements() + [dynamics.Dynamic("mf")]),
        build_part("Tuba", instrument.Tuba(), tuba, [clef.BassClef()] + header_elements() + [dynamics.Dynamic("mf")]),
    ]
    for p in parts:
        score.insert(0, p)
    return score


def build_vocal_score() -> stream.Score:
    bars = [(5, make_notes(VERSE[5], with_lyrics=True))]
    for n in range(6, 30):
        bars.append((n, make_notes(VERSE[n], with_lyrics=True)))
    bars.append((30, make_notes(VERSE_END_FINAL, with_lyrics=True)))
    score = stream.Score()
    score.insert(0, metadata.Metadata(title="東京理科大学校歌 (1 番)", composer="大和憲史", lyricist="佐治巌"))
    score.insert(0, build_part("Voice", instrument.Soprano(), bars, header_elements() + [dynamics.Dynamic("mf")]))
    return score


# --------------------------------------------------------------------------
# WAV レンダリング (MuseScore 4 CLI)
# --------------------------------------------------------------------------
def find_musescore() -> str | None:
    for cand in MUSESCORE_CANDIDATES:
        if cand and os.path.isfile(cand):
            return cand
    return shutil.which("mscore") or shutil.which("MuseScore4")


def render_wav(musicxml: str, wav_out: str, exe: str) -> None:
    """MuseScore 4 で OGG に書き出し, ffmpeg で 44.1 kHz WAV にする.

    MuseScore 4.7 の CLI は WAV 直接書き出しが exit 51 で落ちる (OGG/MP3 は通る) ため, OGG を経由する.
    """
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("ffmpeg が見つからない (winget install Gyan.FFmpeg)")
    env = dict(os.environ, MSYS_NO_PATHCONV="1")
    ogg = os.path.splitext(wav_out)[0] + ".ogg"
    cmd = [exe, "-o", ogg, musicxml]
    print("$", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True, env=env, timeout=600)
    if not os.path.isfile(ogg):
        raise RuntimeError(f"MuseScore が音声を出さなかった: {ogg}")
    subprocess.run([ffmpeg, "-v", "error", "-y", "-i", ogg, "-ar", str(S.SR), "-sample_fmt", "s16", wav_out],
                   check=True, timeout=600)


def read_wav_float(path: str) -> tuple[np.ndarray, int]:
    with wave.open(path, "rb") as w:
        sr = w.getframerate()
        ch = w.getnchannels()
        width = w.getsampwidth()
        raw = w.readframes(w.getnframes())
    if width == 2:
        x = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    elif width == 4:
        x = np.frombuffer(raw, dtype="<i4").astype(np.float64) / 2147483648.0
    else:
        raise ValueError(f"未対応のサンプル幅: {width}")
    if ch > 1:
        x = x.reshape(-1, ch)
    return x, sr


def resample(x: np.ndarray, sr: int, target: int) -> np.ndarray:
    if sr == target:
        return x
    n_out = int(round(len(x) * target / sr))
    t_in = np.arange(len(x)) / sr
    t_out = np.arange(n_out) / target
    if x.ndim == 1:
        return np.interp(t_out, t_in, x)
    return np.stack([np.interp(t_out, t_in, x[:, c]) for c in range(x.shape[1])], axis=1)


def trim_silence(x: np.ndarray, sr: int, threshold_db: float = -60.0, tail_sec: float = 1.5) -> np.ndarray:
    mono = x if x.ndim == 1 else np.abs(x).max(axis=1)
    thr = S.db_to_lin(threshold_db)
    loud = np.nonzero(mono > thr)[0]
    if loud.size == 0:
        return x
    start = max(0, loud[0] - int(0.02 * sr))
    end = min(len(x), loud[-1] + int(tail_sec * sr))
    return x[start:end]


def finish_bgm(raw_wav: str, out_path: str) -> dict:
    x, sr = read_wav_float(raw_wav)
    x = resample(x, sr, S.SR)
    x = trim_silence(x, S.SR)
    x = S.fade(x, fin=0.01, fout=1.0)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    S.write_wav(out_path, x, peak_dbfs=build_audio.PEAK_DB["BGM"])
    info = analyze.analyze(out_path, loop=False)
    info.update(
        name=BGM_NAME,
        category="BGM",
        loop=False,
        path=os.path.relpath(out_path, ROOT).replace("\\", "/"),
        bytes=os.path.getsize(out_path),
        suggested_volume=build_audio.SUGGESTED_VOLUME["BGM"],
        source="tools/audio/school_song/build_school_song.py (music21 -> MuseScore 4)",
    )
    return info


def main(argv: list) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--no-wav", action="store_true", help="MuseScore でのレンダリングを省く")
    parser.add_argument("--out", default=BGM_PATH, help="BGM の書き出し先 (既定: Assets/Audio/BGM)")
    args = parser.parse_args(argv[1:])

    t0 = time.time()
    os.makedirs(OUT_DIR, exist_ok=True)
    vocal_xml = os.path.join(OUT_DIR, "school_song_vocal.musicxml")
    band_xml = os.path.join(OUT_DIR, "school_song_band.musicxml")
    band_mid = os.path.join(OUT_DIR, "school_song_band.mid")

    build_vocal_score().write("musicxml", fp=vocal_xml)
    band = build_band_score()
    band.write("musicxml", fp=band_xml)
    band.write("midi", fp=band_mid)
    total_bars = len(song_form())
    print(f"譜面: {os.path.relpath(vocal_xml, ROOT)}, {os.path.relpath(band_xml, ROOT)} "
          f"({total_bars} 小節, BPM {TEMPO_BPM}, 約 {total_bars * 4 * 60 / TEMPO_BPM:.0f} 秒)")
    if args.no_wav:
        return 0

    exe = find_musescore()
    if not exe:
        print("MuseScore 4 が見つからない (MUSESCORE_EXE で指定)", file=sys.stderr)
        return 2
    with tempfile.TemporaryDirectory() as tmp:
        raw = os.path.join(tmp, "band.wav")
        render_wav(band_xml, raw, exe)
        info = finish_bgm(raw, args.out)
    print(f"  BGM/{BGM_NAME}.wav  {info['duration_sec']:.2f}s peak {info['peak_dbfs']:+.1f} "
          f"rms {info['rms_dbfs']:+.1f} dBFS")
    build_audio.write_manifest([info], time.time() - t0)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

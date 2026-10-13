"""図書館大ホールのピアノを弾くときのフレーズ (自作, 約 19.5 秒).

C major の 5 小節. Cmaj7 - Am7 - Fmaj7 - G - C. 左手は八分のアルペジオ, 右手は
ゆっくりした旋律で, 最後の小節だけ少し遅らせて和音を響かせて終える. 既存の曲の旋律は使っていない.
音は synth.piano の加算合成だけで作り, 打鍵の強さと位置に小さなばらつきを入れる.
"""
from __future__ import annotations

import numpy as np

import synth as S
from synth import SR, add_at

BPM = 72.0
BEATS_PER_BAR = 4
RITARD_BAR = 4              # この小節から少しずつ遅らせる
TAIL_SEC = 2.4              # 最後の和音の余韻

# 小節ごとの左手の和音 (MIDI, 低い方から) と八分の弾き順
CHORDS = (
    (48, 55, 59, 64),       # Cmaj7
    (45, 52, 55, 60),       # Am7
    (41, 48, 52, 57),       # Fmaj7
    (43, 50, 55, 59),       # G
)
ARPEGGIO = (0, 1, 2, 3, 2, 1, 2, 1)
FINAL_CHORD = (48, 55, 60, 64, 72)

# 右手の旋律: (小節, 拍, MIDI, 拍の長さ)
MELODY = (
    (0, 0.0, 76, 1.5), (0, 1.5, 79, 0.5), (0, 2.0, 81, 1.0), (0, 3.0, 79, 1.0),
    (1, 0.0, 76, 1.0), (1, 1.0, 72, 1.0), (1, 2.0, 74, 1.5), (1, 3.5, 76, 0.5),
    (2, 0.0, 77, 1.5), (2, 1.5, 76, 0.5), (2, 2.0, 72, 1.0), (2, 3.0, 69, 1.0),
    (3, 0.0, 71, 1.0), (3, 1.0, 74, 1.0), (3, 2.0, 79, 1.5), (3, 3.5, 77, 0.5),
    (4, 0.0, 76, 2.0), (4, 2.0, 72, 3.0),
)


def _freq(midi: int) -> float:
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def _beat_to_sec(bar: int, beat: float) -> float:
    """拍の位置を秒にする. RITARD_BAR 以降は 1 拍ごとに少しずつ伸ばす."""
    spb = 60.0 / BPM
    total = bar * BEATS_PER_BAR + beat
    start = RITARD_BAR * BEATS_PER_BAR
    if total <= start:
        return total * spb
    extra = total - start
    return start * spb + extra * spb * (1.0 + 0.035 * extra)


def build_piano_phrase() -> np.ndarray:
    rng = np.random.default_rng(1012)
    bars = len(CHORDS) + 1
    length = _beat_to_sec(bars, 0.0) + TAIL_SEC
    buf = np.zeros(S.n_samples(length))

    def strike(midi, bar, beat, dur_beats, vel, bright):
        start = _beat_to_sec(bar, beat) + rng.normal(0.0, 0.006)
        sec = max(0.3, _beat_to_sec(bar, beat + dur_beats) - _beat_to_sec(bar, beat)) + 1.2
        sig = S.piano(_freq(midi), sec, vel=vel * (1.0 + rng.normal(0.0, 0.05)), bright=bright)
        add_at(buf, sig, max(0, S.n_samples(start)))

    for bar, chord in enumerate(CHORDS):
        for i, k in enumerate(ARPEGGIO):
            accent = 1.0 if i % 4 == 0 else 0.72
            strike(chord[k], bar, i * 0.5, 1.5, 0.5 * accent, 0.8)
    for i, midi in enumerate(FINAL_CHORD):
        strike(midi, len(CHORDS), 2.0 + i * 0.05, 3.0, 0.55 if i < 4 else 0.4, 0.8)
    for bar, beat, midi, dur in MELODY:
        strike(midi, bar, beat, dur, 0.78, 1.0)

    out = S.reverb(buf, room=0.72, damp=0.4, mix=0.22)
    return S.fade(out, 0.004, 0.9)

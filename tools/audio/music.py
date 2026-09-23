"""BGM (ループ曲) とジングルの合成.

すべて `synth` のプリミティブだけで作る. ループ曲は
「小節グリッドへ音を置く → バッファ末尾を越えた分は先頭へ回り込ませる」
方式なので, 残響やピアノのリリースが継ぎ目でぶつ切りにならない.
"""
from __future__ import annotations

import numpy as np

import synth as S
from synth import SR, add_at, chord, nf, pan


class Seq:
    """小節 / 拍でステレオバッファへ音を置くだけの簡易シーケンサ."""

    def __init__(self, bars: int, bpm: float, beats_per_bar: int = 4):
        self.bpm = bpm
        self.bpb = beats_per_bar
        self.spb = 60.0 / bpm
        self.bars = bars
        self.length = S.n_samples(bars * beats_per_bar * self.spb)
        self.buf = np.zeros((self.length, 2), dtype=np.float64)
        self._cache: dict = {}

    def beat_sec(self, beats: float) -> float:
        return beats * self.spb

    def at(self, bar: float, beat: float = 0.0) -> int:
        return int(round((bar * self.bpb + beat) * self.spb * SR))

    def put(self, sig: np.ndarray, bar: float, beat: float = 0.0,
            p: float = 0.0, gain: float = 1.0) -> None:
        add_at(self.buf, pan(sig, p) * gain, self.at(bar, beat), wrap=True)

    def cached(self, key, factory):
        if key not in self._cache:
            self._cache[key] = factory()
        return self._cache[key]


# --------------------------------------------------------------------------
# 共通パーツ
# --------------------------------------------------------------------------
def lay_melody(seq: Seq, notes, inst, bar0: int, gain: float, p: float = 0.0,
               tail: float = 1.6, vel: float = 1.0) -> None:
    for b, d, name in notes:
        dur = seq.beat_sec(d) + tail
        f = nf(name)
        sig = seq.cached((inst.__name__, round(f, 3), round(dur, 3), round(vel, 2)),
                         lambda f=f, dur=dur, vel=vel: inst(f, dur, vel))
        seq.put(sig, bar0 + int(b // seq.bpb), b % seq.bpb, p=p, gain=gain)


def lay_chord_stabs(seq: Seq, prog, bar0: int, beats, gain: float,
                    octave: int = 3, p: float = 0.0, tail: float = 1.8) -> None:
    for i, sym in enumerate(prog):
        freqs = chord(sym, octave)
        for beat in beats:
            dur = seq.beat_sec(1.0) + tail
            for j, f in enumerate(freqs):
                sig = seq.cached(("piano", round(f, 3), round(dur, 3)),
                                 lambda f=f, dur=dur: S.piano(f, dur, 0.55))
                seq.put(sig, bar0 + i, beat, p=p + (j - 1) * 0.06, gain=gain)


def lay_pad(seq: Seq, prog, bar0: int, gain: float, octave: int = 3,
            cutoff: float = 2000.0, attack: float = 0.5) -> None:
    dur = seq.beat_sec(seq.bpb) + 0.9
    for i, sym in enumerate(prog):
        freqs = chord(sym, octave)
        sig = seq.cached(("pad", sym, octave, round(dur, 3), cutoff, round(attack, 3)),
                         lambda freqs=freqs: S.pad(freqs, dur, cutoff=cutoff, attack=attack))
        seq.put(sig, bar0 + i, 0.0, gain=gain * 0.7)
        seq.put(sig, bar0 + i, 0.0, p=-0.35, gain=gain * 0.5)
        seq.put(sig, bar0 + i, 0.0, p=0.35, gain=gain * 0.5)


def lay_bass(seq: Seq, prog, bar0: int, gain: float, octave: int = 2,
             pattern=(0.0, 1.5, 2.0, 3.5)) -> None:
    for i, sym in enumerate(prog):
        freqs = chord(sym, octave)
        root, fifth = freqs[0], freqs[2] if len(freqs) > 2 else freqs[0]
        for j, beat in enumerate(pattern):
            f = root if j % 2 == 0 else fifth
            dur = seq.beat_sec(1.2)
            sig = seq.cached(("bass", round(f, 3), round(dur, 3)),
                             lambda f=f, dur=dur: S.bass(f, dur))
            seq.put(sig, bar0 + i, beat, gain=gain)


def lay_drums(seq: Seq, bar0: int, bars: int, gain: float, rng,
              style: str = "pop", fill_last: bool = True) -> None:
    kd = seq.cached(("kick",), lambda: S.kick(0.45, rng))
    sd = seq.cached(("snare",), lambda: S.snare(0.28, rng))
    hc = seq.cached(("hat_c",), lambda: S.hat(0.09, rng))
    ho = seq.cached(("hat_o",), lambda: S.hat(0.3, rng, open_=True))
    sh = seq.cached(("shaker",), lambda: S.shaker(0.12, rng))
    rim = seq.cached(("rim",), lambda: S.rimshot(0.1, rng))
    for b in range(bars):
        bar = bar0 + b
        last = fill_last and (b == bars - 1)
        if style == "pop":
            seq.put(kd, bar, 0.0, gain=gain)
            seq.put(kd, bar, 2.5, gain=gain * 0.85)
            seq.put(sd, bar, 1.0, gain=gain * 0.8, p=-0.05)
            seq.put(sd, bar, 3.0, gain=gain * 0.8, p=-0.05)
            for e in range(8):
                acc = 1.0 if e % 2 == 0 else 0.55
                src = ho if e == 7 and b % 4 == 3 else hc
                seq.put(src, bar, e * 0.5, p=0.28, gain=gain * 0.5 * acc)
        elif style == "brush":
            seq.put(kd, bar, 0.0, gain=gain * 0.7)
            seq.put(kd, bar, 2.0, gain=gain * 0.5)
            seq.put(rim, bar, 2.0, gain=gain * 0.5, p=-0.2)
            for e in range(4):
                seq.put(sh, bar, e * 1.0 + 0.5, p=0.3, gain=gain * 0.55)
        elif style == "lofi":
            seq.put(kd, bar, 0.0, gain=gain * 0.9)
            seq.put(kd, bar, 1.75, gain=gain * 0.6)
            seq.put(sd, bar, 1.0, gain=gain * 0.55, p=-0.1)
            seq.put(sd, bar, 3.0, gain=gain * 0.55, p=-0.1)
            for e in range(8):
                if e == 5:
                    continue
                seq.put(hc, bar, e * 0.5, p=0.25, gain=gain * 0.38 * (1.0 if e % 2 == 0 else 0.6))
        if last:
            for k, beat in enumerate((3.0, 3.25, 3.5, 3.75)):
                seq.put(sd, bar, beat, gain=gain * (0.45 + 0.12 * k), p=(k - 1.5) * 0.15)


def finish(buf: np.ndarray, reverb_mix: float = 0.2, room: float = 0.84,
           cutoff: float | None = None, drive: float = 1.15,
           peak_db: float = -1.0) -> np.ndarray:
    """ループを壊さない仕上げ (助走つきリバーブ → コンプ → ソフトクリップ)."""
    out = buf
    if cutoff:
        out = S.preroll(out, lambda y: S.lowpass(y, cutoff, order=2), pre=0.5)
    out = S.preroll(out, lambda y: S.reverb(y, room=room, damp=0.32, mix=reverb_mix), pre=5.0)
    out = S.compress(out, thresh_db=-20.0, ratio=2.6, circular=True)
    out = S.soft_clip(out, drive)
    return S.normalize(out, peak_db)


# --------------------------------------------------------------------------
# bgm_day : 昼のキャンパス (120 BPM / C major / 王道進行 IV-V-iii-vi)
# --------------------------------------------------------------------------
PROG_DAY = ["F", "G", "Em", "Am", "F", "G", "C", "G"]

MEL_DAY_A = [
    (0, 1, "A4"), (1, 1, "C5"), (2, 1.5, "F5"), (3.5, 0.5, "E5"),
    (4, 1, "D5"), (5, 1, "B4"), (6, 2, "G4"),
    (8, 1, "E5"), (9, 1, "G5"), (10, 1.5, "B4"), (11.5, 0.5, "D5"),
    (12, 1, "C5"), (13, 1, "A4"), (14, 2, "E5"),
    (16, 1, "F5"), (17, 1, "E5"), (18, 1, "D5"), (19, 1, "C5"),
    (20, 1.5, "D5"), (21.5, 0.5, "E5"), (22, 2, "G5"),
    (24, 1, "E5"), (25, 1, "G5"), (26, 1, "C6"), (27, 1, "G5"),
    (28, 2, "D5"), (30, 2, "B4"),
]

MEL_DAY_B = [
    (0, 0.5, "C5"), (0.5, 0.5, "F5"), (1, 1, "A5"), (2, 1, "G5"), (3, 1, "F5"),
    (4, 0.5, "D5"), (4.5, 0.5, "G5"), (5, 1, "B5"), (6, 2, "D5"),
    (8, 1, "G5"), (9, 0.5, "E5"), (9.5, 0.5, "G5"), (10, 2, "B5"),
    (12, 1, "A5"), (13, 1, "E5"), (14, 2, "C5"),
    (16, 1, "F5"), (17, 1, "A5"), (18, 1, "C6"), (19, 1, "A5"),
    (20, 1, "G5"), (21, 1, "D5"), (22, 2, "B4"),
    (24, 0.5, "C5"), (24.5, 0.5, "E5"), (25, 0.5, "G5"), (25.5, 0.5, "C6"), (26, 2, "E5"),
    (28, 1, "D5"), (29, 1, "B4"), (30, 2, "G4"),
]


def build_day() -> np.ndarray:
    rng = np.random.default_rng(20260921)
    seq = Seq(bars=32, bpm=120)
    for p_ in range(4):
        b0 = p_ * 8
        lay_pad(seq, PROG_DAY, b0, gain=0.42 if p_ else 0.5, octave=3, cutoff=2300.0)
        lay_bass(seq, PROG_DAY, b0, gain=0.85 if p_ else 0.55)
        if p_ == 0:
            lay_melody(seq, MEL_DAY_A, S.marimba, b0, gain=0.42, p=0.15, tail=0.9)
        elif p_ == 1:
            lay_melody(seq, MEL_DAY_A, S.piano, b0, gain=0.62, p=-0.12)
            lay_chord_stabs(seq, PROG_DAY, b0, beats=(1.5, 3.0), gain=0.3, octave=4, p=0.2)
            lay_drums(seq, b0, 8, gain=0.72, rng=rng, style="pop")
        elif p_ == 2:
            lay_melody(seq, MEL_DAY_B, S.piano, b0, gain=0.6, p=-0.12)
            lay_melody(seq, MEL_DAY_A, S.marimba, b0, gain=0.22, p=0.4, tail=0.8)
            lay_chord_stabs(seq, PROG_DAY, b0, beats=(1.5, 3.0), gain=0.28, octave=4, p=0.2)
            lay_drums(seq, b0, 8, gain=0.78, rng=rng, style="pop")
        else:
            lay_melody(seq, MEL_DAY_B, S.piano, b0, gain=0.6, p=-0.1)
            lay_melody(seq, MEL_DAY_B, S.bell, b0, gain=0.16, p=0.45, tail=1.4)
            lay_chord_stabs(seq, PROG_DAY, b0, beats=(1.5, 3.0), gain=0.26, octave=4, p=0.2)
            lay_drums(seq, b0, 8, gain=0.78, rng=rng, style="pop")
    return finish(seq.buf, reverb_mix=0.19, room=0.82, drive=1.2)


# --------------------------------------------------------------------------
# bgm_evening : 夕暮れ (84 BPM / F major / ノスタルジック)
# --------------------------------------------------------------------------
PROG_EVE = ["Bb", "C", "Am", "Dm", "Bb", "C", "F", "F"]

MEL_EVE_A = [
    (0, 2, "F4"), (2, 1, "A4"), (3, 1, "C5"),
    (4, 2, "Bb4"), (6, 2, "G4"),
    (8, 1, "A4"), (9, 1, "C5"), (10, 2, "E5"),
    (12, 1.5, "D5"), (13.5, 0.5, "C5"), (14, 2, "A4"),
    (16, 2, "D5"), (18, 1, "C5"), (19, 1, "Bb4"),
    (20, 2, "C5"), (22, 2, "E5"),
    (24, 1, "F5"), (25, 1, "E5"), (26, 2, "C5"),
    (28, 2, "A4"), (30, 2, "F4"),
]

MEL_EVE_B = [
    (0, 1, "D5"), (1, 1, "F5"), (2, 2, "D5"),
    (4, 1, "E5"), (5, 1, "G5"), (6, 2, "E5"),
    (8, 1.5, "C5"), (9.5, 0.5, "B4"), (10, 2, "A4"),
    (12, 1, "D5"), (13, 1, "F5"), (14, 2, "A5"),
    (16, 2, "G5"), (18, 1, "F5"), (19, 1, "D5"),
    (20, 2, "E5"), (22, 2, "C5"),
    (24, 1, "A4"), (25, 1, "C5"), (26, 2, "F5"),
    (28, 2, "C5"), (30, 2, "F4"),
]


def build_evening() -> np.ndarray:
    rng = np.random.default_rng(842026)
    seq = Seq(bars=24, bpm=84)
    for p_ in range(3):
        b0 = p_ * 8
        lay_pad(seq, PROG_EVE, b0, gain=0.55, octave=3, cutoff=1500.0, attack=0.9)
        lay_bass(seq, PROG_EVE, b0, gain=0.6 if p_ else 0.42, pattern=(0.0, 2.0))
        lay_melody(seq, MEL_EVE_A if p_ != 1 else MEL_EVE_B, S.epiano,
                   b0, gain=0.7, p=-0.1, tail=2.0)
        if p_ >= 1:
            lay_melody(seq, MEL_EVE_A if p_ == 1 else MEL_EVE_B, S.bell,
                       b0, gain=0.13, p=0.4, tail=1.8)
            lay_drums(seq, b0, 8, gain=0.4, rng=rng, style="brush", fill_last=(p_ == 2))
        for i, sym in enumerate(PROG_EVE):
            for j, f in enumerate(chord(sym, 4)):
                dur = seq.beat_sec(2.0) + 1.6
                sig = seq.cached(("ep", round(f, 3), round(dur, 3)),
                                 lambda f=f, dur=dur: S.epiano(f, dur, 0.45))
                seq.put(sig, b0 + i, 2.0 + j * 0.25, p=0.3, gain=0.22)
    return finish(seq.buf, reverb_mix=0.3, room=0.88, cutoff=9000.0, drive=1.1)


# --------------------------------------------------------------------------
# bgm_indoor : 図書館 / カフェ (96 BPM / ローファイ / 7th コード)
# --------------------------------------------------------------------------
PROG_IN = ["Dm7", "G7", "Cmaj7", "Am7", "Dm7", "G7", "Cmaj7", "C69"]

MEL_IN_A = [
    (0, 1, "F4"), (1, 1, "A4"), (2, 2, "D5"),
    (4, 1, "B4"), (5, 1, "D5"), (6, 2, "F5"),
    (8, 1.5, "E5"), (9.5, 0.5, "D5"), (10, 2, "B4"),
    (12, 1, "C5"), (13, 1, "E5"), (14, 2, "G4"),
    (16, 1, "A4"), (17, 1, "D5"), (18, 2, "F5"),
    (20, 1, "B4"), (21, 1, "F5"), (22, 2, "D5"),
    (24, 1, "E5"), (25, 1, "G5"), (26, 2, "B4"),
    (28, 2, "D5"), (30, 2, "E5"),
]

MEL_IN_B = [
    (0, 0.5, "D5"), (0.5, 0.5, "F5"), (1, 1, "A5"), (2, 2, "F5"),
    (4, 1, "D5"), (5, 1, "B4"), (6, 2, "G4"),
    (8, 1, "E5"), (9, 1, "B4"), (10, 2, "D5"),
    (12, 1, "C5"), (13, 1, "A4"), (14, 2, "E5"),
    (16, 1.5, "F5"), (17.5, 0.5, "E5"), (18, 2, "D5"),
    (20, 1, "F5"), (21, 1, "B4"), (22, 2, "F4"),
    (24, 1, "G4"), (25, 1, "B4"), (26, 1, "E5"), (27, 1, "D5"),
    (28, 2, "A4"), (30, 2, "G4"),
]


def build_indoor() -> np.ndarray:
    rng = np.random.default_rng(962026)
    seq = Seq(bars=32, bpm=96)
    for p_ in range(4):
        b0 = p_ * 8
        lay_pad(seq, PROG_IN, b0, gain=0.34, octave=3, cutoff=1200.0, attack=0.8)
        lay_bass(seq, PROG_IN, b0, gain=0.72, pattern=(0.0, 1.5, 2.5))
        lay_melody(seq, MEL_IN_A if p_ % 2 == 0 else MEL_IN_B, S.epiano,
                   b0, gain=0.62, p=-0.15, tail=1.8)
        for i, sym in enumerate(PROG_IN):
            for j, f in enumerate(chord(sym, 4)):
                dur = seq.beat_sec(1.5) + 1.5
                sig = seq.cached(("ep2", round(f, 3), round(dur, 3)),
                                 lambda f=f, dur=dur: S.epiano(f, dur, 0.4))
                seq.put(sig, b0 + i, 1.5 + j * 0.08, p=0.28, gain=0.26)
        if p_ >= 1:
            lay_drums(seq, b0, 8, gain=0.6, rng=rng, style="lofi", fill_last=(p_ == 3))
        if p_ == 3:
            # 木琴は S.marimba ではなく S.soft_mallet. 図書館など屋内で鳴らす曲なので,
            # S.marimba の 3.93 倍・9.2 倍の非整数倍音が残響に乗って鐘のように響くのを避ける (#28).
            lay_melody(seq, MEL_IN_A, S.soft_mallet, b0, gain=0.18, p=0.45, tail=0.8)

    out = S.preroll(seq.buf, lambda y: S.lowpass(y, 6200.0, order=2), pre=0.5)
    out = S.preroll(out, lambda y: S.highpass(y, 55.0, order=2), pre=0.5)
    hiss = S.spectral_noise(len(out) / SR,
                            lambda f: 1.0 / (1.0 + (np.maximum(f, 20.0) / 900.0) ** 1.4),
                            rng, stereo=True)
    out = out + hiss * 0.012
    out = S.preroll(out, lambda y: S.reverb(y, room=0.8, damp=0.45, mix=0.17), pre=4.0)
    out = S.compress(out, thresh_db=-22.0, ratio=3.2, circular=True)
    return S.normalize(S.soft_clip(out, 1.25), -1.0)


# --------------------------------------------------------------------------
# bgm_title : タイトル (72 BPM / bgm_day のモチーフをゆったり / 40 秒)
# --------------------------------------------------------------------------
PROG_TITLE = ["F", "G", "Em", "Am", "F", "G", "C", "C", "Am", "F", "G", "C"]

MEL_TITLE = [
    (0, 2, "A4"), (2, 2, "C5"),
    (4, 3, "F5"), (7, 1, "E5"),
    (8, 2, "D5"), (10, 2, "B4"),
    (12, 4, "G4"),
    (16, 2, "E5"), (18, 2, "G5"),
    (20, 3, "B4"), (23, 1, "D5"),
    (24, 2, "C5"), (26, 2, "E5"),
    (28, 4, "G5"),
    (32, 2, "A5"), (34, 2, "E5"),
    (36, 2, "F5"), (38, 2, "A5"),
    (40, 2, "G5"), (42, 2, "D5"),
    (44, 4, "C5"),
]


def build_title() -> np.ndarray:
    seq = Seq(bars=12, bpm=72)
    lay_pad(seq, PROG_TITLE, 0, gain=0.62, octave=3, cutoff=1800.0, attack=1.2)
    lay_pad(seq, PROG_TITLE, 0, gain=0.22, octave=4, cutoff=2600.0, attack=1.6)
    lay_melody(seq, MEL_TITLE, S.piano, 0, gain=0.7, p=-0.08, tail=2.4)
    lay_melody(seq, MEL_TITLE, S.bell, 0, gain=0.14, p=0.4, tail=2.2)
    for i, sym in enumerate(PROG_TITLE):
        root = chord(sym, 2)[0]
        dur = seq.beat_sec(4.0) + 1.2
        sig = seq.cached(("tbass", round(root, 3)),
                         lambda f=root, dur=dur: S.bass(f, dur, 0.8))
        seq.put(sig, i, 0.0, gain=0.5)
        for j, f in enumerate(chord(sym, 4)):
            d2 = seq.beat_sec(1.0) + 2.0
            arp = seq.cached(("tarp", round(f, 3)),
                             lambda f=f, d2=d2: S.pluck(f, d2, 0.6, damp=0.8))
            seq.put(arp, i, 2.0 + j * 0.5, p=0.25, gain=0.22)
    return finish(seq.buf, reverb_mix=0.34, room=0.9, drive=1.05)


# --------------------------------------------------------------------------
# ジングル (ループしないワンショット)
# --------------------------------------------------------------------------
def build_jingle_quest() -> np.ndarray:
    """クエスト達成 2 秒. 校歌の調 (F major) の上行アルペジオ. ベルは使わず木琴 + ピアノ (#28).
    木琴は S.marimba ではなく S.soft_mallet. A6 まで上がるので, S.marimba の 3.93 倍・9.2 倍の
    非整数倍音が 6.9 / 16.2 kHz に出て鈴のように鳴り, 屋内 (図書館以外) でも聞こえていた (#28)."""
    buf = np.zeros((S.n_samples(2.0), 2))
    notes = [("F5", 0.00), ("A5", 0.09), ("C6", 0.18), ("F6", 0.27), ("C6", 0.40), ("A6", 0.50)]
    for i, (name, t0) in enumerate(notes):
        f = nf(name)
        sig = S.soft_mallet(f, 1.7, 0.9) + S.piano(f, 1.7, 0.5)
        add_at(buf, pan(sig, (i - 2.5) * 0.12), S.n_samples(t0))
    for j, f in enumerate(chord("F", 4)):
        add_at(buf, pan(S.piano(f, 1.5, 0.45), (j - 1) * 0.2), S.n_samples(0.5 + j * 0.01))
    out = S.reverb(buf, room=0.82, damp=0.3, mix=0.3)
    return S.normalize(S.fade(S.soft_clip(out, 1.1), 0.003, 0.25), -1.0)


def build_jingle_day_end() -> np.ndarray:
    """1 日の終わり 4 秒. 校歌の調 (F major) の温かい終止形 (IV - V - I) と長い残響. ピアノだけ (#28)."""
    buf = np.zeros((S.n_samples(4.0), 2))
    plan = [("Bb", 0.0, 3), ("C", 0.7, 3), ("F", 1.5, 3), ("F", 1.5, 4)]
    for sym, t0, octv in plan:
        for j, f in enumerate(chord(sym, octv)):
            add_at(buf, pan(S.piano(f, 3.4, 0.72), (j - 1) * 0.18),
                   S.n_samples(t0 + j * 0.012))
    for j, f in enumerate(chord("F", 5)):
        add_at(buf, pan(S.marimba(f, 2.4, 0.25), (j - 1) * 0.3), S.n_samples(1.55 + j * 0.1))
    add_at(buf, pan(S.bass(nf("F2"), 2.6, 0.9), 0.0), S.n_samples(1.5))
    out = S.reverb(buf, room=0.9, damp=0.28, mix=0.34)
    out = S.compress(out, thresh_db=-20.0, ratio=2.4)
    return S.normalize(S.fade(S.soft_clip(out, 1.1), 0.004, 0.6), -1.0)


# --------------------------------------------------------------------------
# bgm_anthem_original : オリジナルの「校歌風」行進曲
#   東京理科大学の実際の校歌は権利判定が付かなかったため再現していない.
#   経緯は tools/audio/README_school_song.md を参照.
# --------------------------------------------------------------------------
PROG_ANTHEM_A = ["Bb", "Eb", "F", "Bb", "Gm", "Eb", "F", "Bb"]
PROG_ANTHEM_B = ["Eb", "Bb", "Cm", "F", "Bb", "Gm", "F", "Bb"]

MEL_ANTHEM_A = [
    (0, 2, "Bb4"), (2, 1, "Bb4"), (3, 1, "C5"),
    (4, 2, "D5"), (6, 2, "Bb4"),
    (8, 2, "Eb5"), (10, 2, "D5"),
    (12, 3, "C5"), (15, 1, "D5"),
    (16, 2, "Bb4"), (18, 2, "D5"),
    (20, 2, "F5"), (22, 2, "D5"),
    (24, 2, "Eb5"), (26, 2, "C5"),
    (28, 4, "Bb4"),
]
MEL_ANTHEM_B = [
    (0, 2, "F5"), (2, 2, "G5"),
    (4, 3, "F5"), (7, 1, "Eb5"),
    (8, 2, "D5"), (10, 2, "F5"),
    (12, 4, "Bb4"),
    (16, 2, "D5"), (18, 2, "Eb5"),
    (20, 2, "F5"), (22, 2, "G5"),
    (24, 2, "F5"), (26, 2, "D5"),
    (28, 4, "Bb4"),
]


def _march_drums(seq: Seq, bar0: int, bars: int, gain: float, rng) -> None:
    """行進曲のスネア: 表拍を刻み, 小節頭に前打音のロールを付ける."""
    sd = seq.cached(("m_sn",), lambda: S.snare(0.26, rng, vel=0.95))
    sd_l = seq.cached(("m_sn_l",), lambda: S.snare(0.2, rng, vel=0.55))
    bd = seq.cached(("m_bd",), lambda: S.kick(0.4, rng, f0=120.0, f1=52.0))
    cy = seq.cached(("m_cy",), lambda: S.hat(0.42, rng, open_=True, vel=0.5))
    for b in range(bar0, bar0 + bars):
        for beat in (0.0, 2.0):
            seq.put(bd, b, beat, gain=gain * 0.9)
        for beat in (1.0, 3.0):
            seq.put(sd, b, beat, p=0.12, gain=gain * 0.8)
        # 小節頭へ向かう 3 連の刻み
        for k, beat in enumerate((3.5, 3.667, 3.833)):
            seq.put(sd_l, b, beat, p=0.12, gain=gain * (0.3 + 0.12 * k))
        if (b - bar0) % 4 == 0:
            seq.put(cy, b, 0.0, p=-0.2, gain=gain * 0.5)


def _lay_brass(seq: Seq, prog, bar0: int, gain: float, octave: int = 3,
               beats=(0.0, 2.0), tail: float = 0.5) -> None:
    for i, sym in enumerate(prog):
        for beat in beats:
            dur = seq.beat_sec(2.0) + tail
            for j, f in enumerate(chord(sym, octave)):
                sig = seq.cached(("brass", round(f, 3), round(dur, 3)),
                                 lambda f=f, dur=dur: S.brass(f, dur, 0.7))
                seq.put(sig, bar0 + i, beat, p=(j - 1) * 0.22, gain=gain)


def build_anthem_original() -> np.ndarray:
    """オリジナル校歌風行進曲 (108 BPM / Bb major / A-B 形式 8 小節 × 2 を 2 周)."""
    rng = np.random.default_rng(77)
    seq = Seq(bars=32, bpm=108)
    for rep in range(2):
        base = rep * 16
        soft = 1.0 if rep else 0.78
        _lay_brass(seq, PROG_ANTHEM_A, base, gain=0.34 * soft, octave=3)
        _lay_brass(seq, PROG_ANTHEM_B, base + 8, gain=0.34 * soft, octave=3)
        lay_bass(seq, PROG_ANTHEM_A, base, gain=0.7, octave=2, pattern=(0.0, 1.0, 2.0, 3.0))
        lay_bass(seq, PROG_ANTHEM_B, base + 8, gain=0.7, octave=2, pattern=(0.0, 1.0, 2.0, 3.0))
        _march_drums(seq, base, 16, gain=0.5 * soft, rng=rng)
        # 主旋律はトランペット風ブラス, 裏でピアノが同じ線をなぞる
        for notes, b0 in ((MEL_ANTHEM_A, base), (MEL_ANTHEM_B, base + 8)):
            lay_melody(seq, notes, S.brass, b0, gain=0.75 * soft, p=-0.1, tail=0.35)
            lay_melody(seq, notes, S.piano, b0, gain=0.3 * soft, p=0.22, tail=1.1)
        if rep:
            # 2 周目はオクターブ上のピッコロ風を重ねる
            for notes, b0 in ((MEL_ANTHEM_A, base), (MEL_ANTHEM_B, base + 8)):
                up = [(b, d, n[:-1] + str(int(n[-1]) + 1)) for b, d, n in notes]
                lay_melody(seq, up, S.pluck, b0, gain=0.16, p=0.45, tail=0.5)
    return finish(seq.buf, reverb_mix=0.24, room=0.88, drive=1.2)


# --------------------------------------------------------------------------
# bgm_night : 夜の余韻 (72 BPM / D minor / 静かなピアノ + パッド)
# --------------------------------------------------------------------------
PROG_NIGHT = ["Dm", "Am", "Bb", "F", "Gm", "Dm", "Gm", "A"]

MEL_NIGHT_A = [
    (0, 3, "D5"), (3, 1, "C5"),
    (4, 4, "A4"),
    (8, 2, "F5"), (10, 2, "D5"),
    (12, 4, "C5"),
    (16, 3, "Bb4"), (19, 1, "D5"),
    (20, 4, "A4"),
    (24, 2, "G4"), (26, 2, "Bb4"),
    (28, 4, "A4"),
]
MEL_NIGHT_B = [
    (0, 2, "F5"), (2, 2, "E5"),
    (4, 4, "D5"),
    (8, 3, "D5"), (11, 1, "F5"),
    (12, 4, "A5"),
    (16, 2, "G5"), (18, 2, "F5"),
    (20, 4, "D5"),
    (24, 3, "E5"), (27, 1, "F5"),
    (28, 4, "D5"),
]


def build_night() -> np.ndarray:
    """夜 80 秒. 8 小節の進行を 3 周し, 2 周目だけ旋律を変える."""
    seq = Seq(bars=24, bpm=72)
    for rep in range(3):
        b0 = rep * 8
        lay_pad(seq, PROG_NIGHT, b0, gain=0.5 + 0.06 * rep, octave=3,
                cutoff=1400.0 + 200.0 * rep, attack=1.4)
        lay_bass(seq, PROG_NIGHT, b0, gain=0.42, octave=2, pattern=(0.0, 2.5))
        mel = MEL_NIGHT_B if rep == 1 else MEL_NIGHT_A
        lay_melody(seq, mel, S.piano, b0, gain=0.6 if rep else 0.5, p=-0.12, tail=2.8)
        if rep:
            lay_melody(seq, mel, S.bell, b0, gain=0.09, p=0.42, tail=2.4)
        for i, sym in enumerate(PROG_NIGHT):
            for j, f in enumerate(chord(sym, 4)):
                d2 = seq.beat_sec(1.0) + 2.4
                arp = seq.cached(("narp", round(f, 3)),
                                 lambda f=f, d2=d2: S.pluck(f, d2, 0.5, damp=0.85))
                seq.put(arp, b0 + i, 1.0 + j * 0.75, p=0.3, gain=0.16 + 0.03 * rep)
    return finish(seq.buf, reverb_mix=0.36, room=0.9, cutoff=5200.0, drive=1.04)


# --------------------------------------------------------------------------
# bgm_result : リザルト画面 (128 BPM / C major / 16 小節 = ちょうど 30 秒)
# --------------------------------------------------------------------------
PROG_RESULT = ["C", "G", "Am", "F", "C", "G", "F", "G"]

MEL_RESULT = [
    (0, 1, "G4"), (1, 1, "C5"), (2, 2, "E5"),
    (4, 1, "D5"), (5, 1, "G5"), (6, 2, "D5"),
    (8, 1, "C5"), (9, 1, "E5"), (10, 2, "A5"),
    (12, 2, "G5"), (14, 2, "F5"),
    (16, 1, "E5"), (17, 1, "G5"), (18, 2, "C6"),
    (20, 2, "B5"), (22, 2, "G5"),
    (24, 1, "A5"), (25, 1, "F5"), (26, 2, "A5"),
    (28, 4, "G5"),
]


def build_result() -> np.ndarray:
    """リザルト 30 秒. 明るく短く, 2 周目で少し賑やかになる."""
    rng = np.random.default_rng(303)
    seq = Seq(bars=16, bpm=128)
    for rep in range(2):
        b0 = rep * 8
        lay_pad(seq, PROG_RESULT, b0, gain=0.3, octave=3, cutoff=2400.0, attack=0.35)
        lay_bass(seq, PROG_RESULT, b0, gain=0.62, octave=2)
        lay_drums(seq, b0, 8, gain=0.46 + 0.06 * rep, rng=rng, style="pop",
                  fill_last=(rep == 1))
        lay_chord_stabs(seq, PROG_RESULT, b0, (1.5, 3.0), gain=0.24, octave=4, p=0.2)
        lay_melody(seq, MEL_RESULT, S.piano, b0, gain=0.72, p=-0.1, tail=1.1)
        if rep:
            lay_melody(seq, MEL_RESULT, S.marimba, b0, gain=0.2, p=0.4, tail=0.6)
    return finish(seq.buf, reverb_mix=0.2, room=0.82, drive=1.22)

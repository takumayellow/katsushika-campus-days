"""環境音 (20〜30 秒のシームレスループ) の合成.

ループを厳密に成立させるための 3 つの決まりごと:
  1. ベッドのノイズは `spectral_noise` で作る (周波数領域で合成するので
     長さちょうどの周期信号になり, 末尾と先頭がサンプル単位で繋がる).
  2. ドローン / ハムの周波数は `pfreq` でループ長の整数倍に丸める.
  3. 鳥・水滴などの単発イベントは `wrap=True` で置く (末尾で鳴り始めた音が
     先頭へ回り込む).

屋内 (図書館・体育館・温室・カフェ・食堂) は「静かな部屋」にする. 以前は屋外と同じく
帯域ノイズのベッドを LFO でうねらせ, 食器の金属音 (dish_clink) を散らしていたので,
建物の中で風の音と「チンカン」が鳴っていた. いまは 150 Hz 以下でうねらない空調音
(`room_tone`) に, 部屋に合った金属でない音をまばらに置くだけで, 書き出しも小さくする.
"""
from __future__ import annotations

import numpy as np

import synth as S
from synth import SR, add_at, pan

# 書き出しピーク (dBFS). 屋外 2 本は build_audio.PEAK_DB["Ambient"] (-3) のまま.
# 屋内は BGM の下でほとんど気にならない大きさまで下げる (屋外より RMS で 20 dB ほど小さい).
PEAK_DB = {
    "amb_library": -21.0,
    "amb_gym": -15.0,
    "amb_greenhouse": -20.0,
    "amb_cafe": -21.0,
    "amb_cafeteria": -19.0,
}


def pfreq(f: float, dur: float) -> float:
    """ループ長 dur の中で整数周期になるよう周波数を丸める."""
    return max(round(f * dur), 1) / dur


def bed(dur: float, shape, rng, gain: float = 1.0) -> np.ndarray:
    return S.spectral_noise(dur, shape, rng, stereo=True) * gain


def scatter(buf: np.ndarray, factory, count: int, dur: float, rng,
            spread: float = 0.8, gain: float = 1.0) -> None:
    """イベントをループ全体にばらまく (末尾を越えた分は先頭へ回り込む)."""
    for i in range(count):
        sig = factory(i, rng)
        g = gain * rng.uniform(0.55, 1.0)
        add_at(buf, pan(sig * g, rng.uniform(-spread, spread)),
               S.n_samples(rng.uniform(0.0, dur)), wrap=True)


def drone(dur: float, freq: float, harmonics, rng) -> np.ndarray:
    """ループ長に同期したトーナルなドローン (空調・換気扇など)."""
    t = S.tline(dur)
    out = np.zeros_like(t)
    f0 = pfreq(freq, dur)
    for k, a in harmonics:
        out += a * np.sin(2 * np.pi * f0 * k * t + rng.uniform(0, 2 * np.pi))
    return out


def sprinkle(buf: np.ndarray, factory, count: int, dur: float, rng,
             spread: float = 0.8, gain: float = 1.0) -> None:
    """ループを count 個の枠に分け, 1 枠に 1 つずつずらして置く (scatter と違って固まらない)."""
    slot = dur / count
    for i in range(count):
        sig = factory(i, rng)
        g = gain * rng.uniform(0.6, 1.0)
        add_at(buf, pan(sig * g, rng.uniform(-spread, spread)),
               S.n_samples((i + rng.uniform(0.1, 0.9)) * slot), wrap=True)


def room_tone(dur: float, rng, cut: float = 130.0) -> np.ndarray:
    """屋内の空調の気配 (RMS 1). cut より上を急に落とし, LFO も掛けないので風のようにうねらない."""
    y = bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 1.0) / cut) ** 4)
            / (1.0 + (28.0 / np.maximum(f, 1.0)) ** 4), rng)
    return y / np.sqrt(np.mean(y ** 2))


# --------------------------------------------------------------------------
# 単発イベント
# --------------------------------------------------------------------------
def bird_chirp(_i, rng) -> np.ndarray:
    """小鳥のさえずり: 2〜4 音の短いピッチスイープ."""
    n_notes = rng.integers(2, 5)
    gap = rng.uniform(0.07, 0.16)
    total = float(n_notes) * gap + 0.35
    buf = np.zeros(S.n_samples(total))
    base = rng.uniform(2300.0, 4300.0)
    for k in range(int(n_notes)):
        d = rng.uniform(0.05, 0.13)
        t = S.tline(d)
        f0 = base * rng.uniform(0.9, 1.1)
        f1 = f0 * rng.uniform(0.65, 1.9)
        f = (f0 + (f1 - f0) * (t / d)) * (1.0 + 0.05 * np.sin(2 * np.pi * rng.uniform(30, 70) * t))
        ph = 2 * np.pi * np.cumsum(f) / SR
        y = (np.sin(ph) + 0.28 * np.sin(2 * ph)) * np.sin(np.pi * t / d) ** 1.4
        add_at(buf, y * 0.45, S.n_samples(k * gap))
    return buf


def higurashi(_i, rng) -> np.ndarray:
    """ヒグラシ: 3.5 kHz 前後を毎秒 11 回ほど振幅変調した「カナカナ」."""
    dur = float(rng.uniform(2.2, 3.4))
    t = S.tline(dur)
    base = rng.uniform(3250.0, 3950.0)
    pitch = base * (1.0 - 0.13 * np.clip((t - dur * 0.62) / (dur * 0.38), 0, 1))
    ph = 2 * np.pi * np.cumsum(pitch) / SR
    tone = np.sin(ph) + 0.55 * np.sin(2 * ph) + 0.22 * np.sin(3 * ph)
    am = (0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(10.5, 12.5) * t - np.pi / 2)) ** 2.4
    env = np.clip(np.sin(np.pi * t / dur), 0, 1) ** 0.8
    return S.bandpass(tone * am * env, 2000.0, 9500.0) * 0.3


def page_turn(_i, rng) -> np.ndarray:
    """紙をめくる: 2 度に分かれた擦れ音. 高域を落として柔らかく."""
    dur = 0.5
    t = S.tline(dur)
    nz = S.bandpass(rng.standard_normal(len(t)), 600.0, 4500.0)
    env = (np.exp(-((t - 0.06) / 0.04) ** 2) * 0.9
           + np.exp(-((t - 0.22) / 0.07) ** 2) * 0.7)
    return nz * env * 0.3


def water_drop(_i, rng) -> np.ndarray:
    """水滴: 葉から落ちる小さな「ポト」. 打撃のノイズは入れず, 低めのピッチが少し上がるだけ."""
    dur = 0.25
    t = S.tline(dur)
    f = rng.uniform(420.0, 780.0) * (1.0 + 0.8 * np.clip(t / 0.04, 0, 1))
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * S.perc_env(dur, 0.002, 0.035)
    return y * 0.3


def ball_bounce(_i, rng) -> np.ndarray:
    """バスケットボールのバウンド (低い胴鳴り + 鈍い床のスラップ)."""
    dur = 0.4
    t = S.tline(dur)
    f = 150.0 * np.exp(-t / 0.02) + 82.0
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * S.perc_env(dur, 0.001, 0.055)
    slap = S.bandpass(rng.standard_normal(len(t)), 500.0, 3000.0) * S.perc_env(dur, 0.001, 0.012)
    return (body * 0.8 + slap * 0.35) * 0.55


def shoe_squeak(_i, rng) -> np.ndarray:
    dur = 0.28
    t = S.tline(dur)
    f = rng.uniform(850.0, 1500.0) * (1.0 + 0.5 * t / dur)
    f = f * (1.0 + 0.08 * np.sin(2 * np.pi * 23.0 * t))
    y = np.sin(2 * np.pi * np.cumsum(f) / SR)
    y += 0.3 * np.sin(4 * np.pi * np.cumsum(f) / SR)
    return y * np.sin(np.pi * t / dur) ** 2 * 0.22


def voice_blob(_i, rng) -> np.ndarray:
    """ざわめきの粒: 声の帯域だけを持つ短い塊 (言葉には聞こえない)."""
    dur = float(rng.uniform(0.35, 0.9))
    t = S.tline(dur)
    nz = rng.standard_normal(len(t))
    lo = rng.uniform(180.0, 420.0)
    y = S.bandpass(nz, lo, lo * rng.uniform(4.0, 7.0))
    y *= 0.6 + 0.4 * np.sin(2 * np.pi * rng.uniform(3.5, 7.0) * t + rng.uniform(0, 6.2))
    y *= np.sin(np.pi * t / dur) ** 1.2
    return y * 0.18


# 日本語の 5 母音 (あ い う え お) の第 1 / 第 2 フォルマント (Hz)
_VOWELS = ((800.0, 1200.0), (300.0, 2200.0), (350.0, 1300.0), (500.0, 1900.0), (500.0, 850.0))


def murmur(_i, rng) -> np.ndarray:
    """離れた席の話し声: 声の高さを持つ倍音を母音のフォルマントで 3〜7 音節ぶん鳴らし, 壁越しにこもらせる.
    ノイズでなく声帯の音なので, 小さく鳴らしても風や空調には聞こえない."""
    n_syl = int(rng.integers(3, 8))
    syl = float(rng.uniform(0.14, 0.21))
    dur = n_syl * syl + 0.25
    t = S.tline(dur)
    base = float(rng.choice((115.0, 130.0, 205.0, 230.0))) * rng.uniform(0.94, 1.06)
    f0 = base * (1.06 - 0.14 * t / dur) * (1.0 + 0.025 * np.sin(2 * np.pi * 2.7 * t + rng.uniform(0, 6.2)))
    ph = 2 * np.pi * np.cumsum(f0) / SR
    src = sum(np.sin(k * ph) / k for k in range(1, 26))
    out = np.zeros_like(t)
    for s in range(n_syl):
        f1, f2 = _VOWELS[int(rng.integers(len(_VOWELS)))]
        voiced = S.fft_filter(src, lambda f: (np.exp(-((f - f1) / 110.0) ** 2)
                                              + 0.5 * np.exp(-((f - f2) / 160.0) ** 2) + 0.03))
        u = np.clip((t - s * syl) / (syl * 1.35), 0.0, 1.0)
        out += voiced * np.sin(np.pi * u) ** 1.5 * rng.uniform(0.55, 1.0)
    # 壁越し・離れた席なので 1.6 kHz より上をなだらかに落とす
    out = S.fft_filter(out, lambda f: 1.0 / (1.0 + (np.maximum(f, 1.0) / 1600.0) ** 4))
    return out / (np.max(np.abs(out)) + 1e-9) * 0.2


# --------------------------------------------------------------------------
# ループ本体
# --------------------------------------------------------------------------
def _wind(dur: float, rng, cut: float = 700.0, gust=(1, 2, 3, 5)) -> np.ndarray:
    """風: 低域寄りのノイズをループ同期の LFO でうねらせる."""
    w = bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 15.0) / cut) ** 1.7), rng)
    lfo = S.periodic_lfo(dur, gust, [1.0, 0.6, 0.35, 0.2], rng)
    return w * (0.35 + 0.65 * lfo)[:, None]


def build_campus_day(dur: float = 24.0) -> np.ndarray:
    """昼のキャンパス: 風 + 小鳥 + 遠くのざわめき + 遠い交通のうなり."""
    rng = np.random.default_rng(1001)
    buf = _wind(dur, rng, cut=620.0) * 0.55
    buf += bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 15.0) / 120.0) ** 2.2), rng) * 0.3
    murmur = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 520.0)) ** 2) / 0.9), rng)
    buf += murmur * (0.18 + 0.12 * S.periodic_lfo(dur, (1, 3), [1.0, 0.5], rng))[:, None]
    scatter(buf, voice_blob, 26, dur, rng, spread=0.85, gain=0.5)
    scatter(buf, bird_chirp, 22, dur, rng, spread=0.9, gain=0.85)
    leaves = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 4200.0)) ** 2) / 0.6), rng)
    buf += leaves * (0.05 + 0.12 * S.periodic_lfo(dur, (2, 3, 7), [1.0, 0.6, 0.3], rng))[:, None]
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.7, damp=0.5, mix=0.1), pre=3.0)
    return S.normalize(out, -3.0)


def build_campus_evening(dur: float = 24.0) -> np.ndarray:
    """夕暮れ: ヒグラシの合唱 + 弱い風 + 遠い交通."""
    rng = np.random.default_rng(1002)
    buf = _wind(dur, rng, cut=480.0, gust=(1, 2, 4)) * 0.45
    buf += bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 15.0) / 90.0) ** 2.4), rng) * 0.26
    scatter(buf, higurashi, 9, dur, rng, spread=0.95, gain=0.9)
    scatter(buf, bird_chirp, 5, dur, rng, spread=0.9, gain=0.35)
    crickets = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 5200.0)) ** 2) / 0.25), rng)
    buf += crickets * (0.04 + 0.05 * S.periodic_lfo(dur, (3, 5), [1.0, 0.5], rng))[:, None]
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.75, damp=0.45, mix=0.13), pre=3.0)
    return S.normalize(out, -3.0)


def indoor(buf: np.ndarray, dur: float, rng, name: str, tone_db: float, cut: float = 130.0,
           room: float = 0.75, damp: float = 0.6, mix: float = 0.18, pre: float = 3.0) -> np.ndarray:
    """屋内の仕上げ: イベントに部屋の残響を掛け, ピークより tone_db 小さい RMS で空調音を敷き,
    書き出しピーク PEAK_DB[name] へ揃える."""
    out = S.preroll(buf, lambda y: S.reverb(y, room=room, damp=damp, mix=mix), pre=pre)
    peak = float(np.max(np.abs(out)))
    out = out + room_tone(dur, rng, cut) * peak * S.db_to_lin(-tone_db)
    return S.normalize(out, PEAK_DB[name])


def build_cafe(dur: float = 22.0) -> np.ndarray:
    """カフェ (共創棟): 静かな空調 + 離れた席の小さな話し声. 食器の金属音・蒸気・ざわめきのノイズは入れない."""
    rng = np.random.default_rng(1003)
    buf = np.zeros((S.n_samples(dur), 2))
    sprinkle(buf, murmur, 8, dur, rng, spread=0.7)
    return indoor(buf, dur, rng, "amb_cafe", tone_db=32.0, room=0.7, damp=0.65, mix=0.22)


def build_library(dur: float = 26.0) -> np.ndarray:
    """図書館 (ほかの建物の既定): ほぼ無音の空調 + たまにページをめくる音."""
    rng = np.random.default_rng(1004)
    buf = np.zeros((S.n_samples(dur), 2))
    sprinkle(buf, page_turn, 4, dur, rng, spread=0.75)
    return indoor(buf, dur, rng, "amb_library", tone_db=32.0, cut=110.0, room=0.8, damp=0.6, mix=0.16)


def build_gym(dur: float = 24.0) -> np.ndarray:
    """体育館: 静かな空調 + ときどき誰かがボールをつく音とシューズのキュッ (広い残響)."""
    rng = np.random.default_rng(1005)
    buf = np.zeros((S.n_samples(dur), 2))
    # ドリブルは 3 回だけ, 4〜6 回ずつ続けてつく (ずっと鳴らし続けない)
    for k in range(3):
        start = (k + rng.uniform(0.1, 0.45)) * dur / 3
        period = rng.uniform(0.55, 0.75)
        p = rng.uniform(-0.6, 0.6)
        for j in range(int(rng.integers(4, 7))):
            sig = ball_bounce(0, rng) * rng.uniform(0.8, 1.0)
            add_at(buf, pan(sig, p), S.n_samples(start + j * period), wrap=True)
    sprinkle(buf, shoe_squeak, 4, dur, rng, spread=0.85, gain=0.5)
    return indoor(buf, dur, rng, "amb_gym", tone_db=38.0, cut=110.0,
                  room=0.9, damp=0.35, mix=0.3, pre=6.0)


def build_cafeteria(dur: float = 22.0) -> np.ndarray:
    """食堂 (第 2 研究棟): 静かな空調 + あちこちの席の小さな話し声. トレイや食器の金属音は入れない."""
    rng = np.random.default_rng(1006)
    buf = np.zeros((S.n_samples(dur), 2))
    sprinkle(buf, murmur, 13, dur, rng, spread=0.9)
    return indoor(buf, dur, rng, "amb_cafeteria", tone_db=32.0,
                  room=0.82, damp=0.55, mix=0.26, pre=4.0)


def build_greenhouse(dur: float = 24.0) -> np.ndarray:
    """温室: 小さな換気扇のうなり + ときどき葉から落ちる水滴."""
    rng = np.random.default_rng(1007)
    fan = drone(dur, 118.0, ((1, 0.5), (2, 0.2)), rng)
    fan = fan * (0.85 + 0.15 * np.sin(2 * np.pi * pfreq(23.0, dur) * S.tline(dur)))
    buf = np.stack([fan, np.roll(fan, 97)], axis=1) * 0.015
    sprinkle(buf, water_drop, 8, dur, rng, spread=0.9)
    return indoor(buf, dur, rng, "amb_greenhouse", tone_db=34.0, cut=150.0,
                  room=0.8, damp=0.5, mix=0.22, pre=4.0)


AMBIENTS = {
    "amb_campus_day": build_campus_day,
    "amb_campus_evening": build_campus_evening,
    "amb_cafe": build_cafe,
    "amb_library": build_library,
    "amb_gym": build_gym,
    "amb_cafeteria": build_cafeteria,
    "amb_greenhouse": build_greenhouse,
}

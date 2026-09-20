"""環境音 (20〜30 秒のシームレスループ) の合成.

ループを厳密に成立させるための 3 つの決まりごと:
  1. ベッドのノイズは `spectral_noise` で作る (周波数領域で合成するので
     長さちょうどの周期信号になり, 末尾と先頭がサンプル単位で繋がる).
  2. ドローン / ハムの周波数は `pfreq` でループ長の整数倍に丸める.
  3. 鳥・水滴などの単発イベントは `wrap=True` で置く (末尾で鳴り始めた音が
     先頭へ回り込む).
"""
from __future__ import annotations

import numpy as np

import synth as S
from synth import SR, add_at, pan


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


def dish_clink(_i, rng) -> np.ndarray:
    """食器: 高い金属モード + 立ち上がりのノイズ."""
    dur = 0.35
    t = S.tline(dur)
    out = np.zeros_like(t)
    f0 = rng.uniform(2200.0, 5200.0)
    for r, a, tau in ((1.0, 1.0, 0.13), (1.73, 0.5, 0.08), (2.61, 0.28, 0.05)):
        if f0 * r < SR * 0.45:
            out += a * np.exp(-t / tau) * np.sin(2 * np.pi * f0 * r * t)
    tick = S.bandpass(rng.standard_normal(len(t)), 2500.0, 13000.0)
    out += tick * S.perc_env(dur, 0.0004, 0.004) * 0.8
    return out * 0.22


def page_turn(_i, rng) -> np.ndarray:
    """紙をめくる: 2 度に分かれた擦れ音."""
    dur = 0.45
    t = S.tline(dur)
    nz = S.bandpass(rng.standard_normal(len(t)), 1400.0, 9000.0)
    env = (np.exp(-((t - 0.05) / 0.035) ** 2) * 0.9
           + np.exp(-((t - 0.19) / 0.06) ** 2) * 0.7)
    return nz * env * 0.3


def water_drop(_i, rng) -> np.ndarray:
    """水滴: 落ちた直後にピッチが上がる短いピング."""
    dur = 0.3
    t = S.tline(dur)
    f = rng.uniform(550.0, 1100.0) * (1.0 + 1.6 * np.clip(t / 0.045, 0, 1))
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.05)
    nz = S.bandpass(rng.standard_normal(len(t)), 1500.0, 8000.0) * S.perc_env(dur, 0.0005, 0.005)
    return (y * 0.6 + nz * 0.25) * 0.5


def ball_bounce(_i, rng) -> np.ndarray:
    """バスケットボールのバウンド (低い胴鳴り + 床のスラップ)."""
    dur = 0.4
    t = S.tline(dur)
    f = 150.0 * np.exp(-t / 0.02) + 82.0
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * S.perc_env(dur, 0.001, 0.055)
    slap = S.bandpass(rng.standard_normal(len(t)), 900.0, 7000.0) * S.perc_env(dur, 0.0005, 0.012)
    return (body * 0.8 + slap * 0.45) * 0.55


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


def tray_clatter(_i, rng) -> np.ndarray:
    dur = 0.5
    buf = np.zeros(S.n_samples(dur))
    for k in range(int(rng.integers(2, 5))):
        add_at(buf, dish_clink(0, rng) * rng.uniform(0.6, 1.2),
               S.n_samples(rng.uniform(0.0, 0.16)))
    return buf * 0.9


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


def build_cafe(dur: float = 22.0) -> np.ndarray:
    """カフェ: ざわめきの帯域ノイズ + 食器の高音 + エスプレッソの蒸気."""
    rng = np.random.default_rng(1003)
    babble = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 620.0)) ** 2) / 1.1), rng)
    buf = babble * (0.35 + 0.2 * S.periodic_lfo(dur, (1, 2, 5), [1.0, 0.5, 0.3], rng))[:, None]
    buf += bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 15.0) / 70.0) ** 2.0), rng) * 0.22
    scatter(buf, voice_blob, 48, dur, rng, spread=0.8, gain=0.9)
    scatter(buf, dish_clink, 20, dur, rng, spread=0.7, gain=0.8)
    steam = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 3400.0)) ** 2) / 0.8), rng)
    gate = S.periodic_lfo(dur, (1,), [1.0], rng)
    buf += steam * np.clip((gate - 0.72) * 3.6, 0, 1)[:, None] * 0.22
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.72, damp=0.55, mix=0.18), pre=3.0)
    return S.normalize(out, -3.0)


def build_library(dur: float = 26.0) -> np.ndarray:
    """図書館: ほぼ無音の空調 + 低いハム + たまにページをめくる音."""
    rng = np.random.default_rng(1004)
    buf = bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 12.0) / 180.0) ** 2.6), rng) * 0.5
    hum = drone(dur, 100.0, ((1, 0.5), (2, 0.18), (3, 0.06)), rng)
    buf += np.stack([hum, hum * 0.9], axis=1) * 0.06
    air = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 1500.0)) ** 2) / 1.6), rng)
    buf += air * (0.035 + 0.02 * S.periodic_lfo(dur, (1, 2), [1.0, 0.4], rng))[:, None]
    scatter(buf, page_turn, 7, dur, rng, spread=0.75, gain=0.55)
    scatter(buf, dish_clink, 2, dur, rng, spread=0.6, gain=0.12)
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.8, damp=0.6, mix=0.16), pre=3.0)
    return S.normalize(out, -6.0)


def build_gym(dur: float = 24.0) -> np.ndarray:
    """体育館: 残響の強いボールのバウンドとシューズのキュッ."""
    rng = np.random.default_rng(1005)
    buf = bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 12.0) / 150.0) ** 2.2), rng) * 0.32
    hum = drone(dur, 62.0, ((1, 0.4), (2, 0.15)), rng)
    buf += np.stack([hum, hum], axis=1) * 0.05
    # 3 人ぶんのドリブル (テンポ違い) をループ同期の間隔で置く
    for player, (period, gain_) in enumerate(((0.62, 0.9), (0.85, 0.6), (1.2, 0.45))):
        k = max(1, round(dur / period))
        step = dur / k
        p = rng.uniform(-0.7, 0.7)
        for j in range(k):
            sig = ball_bounce(0, rng) * gain_ * rng.uniform(0.8, 1.15)
            add_at(buf, pan(sig, p + rng.uniform(-0.12, 0.12)),
                   S.n_samples((j + 0.13 * player) * step), wrap=True)
    scatter(buf, shoe_squeak, 9, dur, rng, spread=0.85, gain=0.7)
    scatter(buf, voice_blob, 10, dur, rng, spread=0.9, gain=0.3)
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.93, damp=0.18, mix=0.42), pre=6.0)
    return S.normalize(out, -3.0)


def build_cafeteria(dur: float = 22.0) -> np.ndarray:
    """食堂: 密度の高いざわめき + トレイと椅子の音."""
    rng = np.random.default_rng(1006)
    babble = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 700.0)) ** 2) / 1.3), rng)
    buf = babble * (0.45 + 0.22 * S.periodic_lfo(dur, (1, 2, 3, 7), [1, 0.6, 0.4, 0.2], rng))[:, None]
    buf += bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 15.0) / 85.0) ** 2.0), rng) * 0.26
    scatter(buf, voice_blob, 90, dur, rng, spread=0.9, gain=1.0)
    scatter(buf, tray_clatter, 12, dur, rng, spread=0.8, gain=0.7)
    scatter(buf, dish_clink, 26, dur, rng, spread=0.85, gain=0.6)
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.85, damp=0.4, mix=0.26), pre=4.0)
    return S.normalize(out, -3.0)


def build_greenhouse(dur: float = 24.0) -> np.ndarray:
    """温室: 換気扇のうなりと, 葉から落ちる水滴."""
    rng = np.random.default_rng(1007)
    buf = bed(dur, lambda f: 1.0 / (1.0 + (np.maximum(f, 12.0) / 260.0) ** 2.0), rng) * 0.34
    fan = drone(dur, 118.0, ((1, 0.5), (2, 0.28), (3, 0.14), (5, 0.07)), rng)
    blade = 0.75 + 0.25 * np.sin(2 * np.pi * pfreq(23.0, dur) * S.tline(dur))
    fan = fan * blade
    buf += np.stack([fan, np.roll(fan, 97)], axis=1) * 0.09
    hiss = bed(dur, lambda f: np.exp(-((np.log(np.maximum(f, 20.0) / 2600.0)) ** 2) / 1.2), rng)
    buf += hiss * (0.05 + 0.03 * S.periodic_lfo(dur, (1, 4), [1.0, 0.4], rng))[:, None]
    scatter(buf, water_drop, 26, dur, rng, spread=0.9, gain=0.75)
    out = S.preroll(buf, lambda y: S.reverb(y, room=0.86, damp=0.3, mix=0.3), pre=4.0)
    return S.normalize(out, -4.0)


AMBIENTS = {
    "amb_campus_day": build_campus_day,
    "amb_campus_evening": build_campus_evening,
    "amb_cafe": build_cafe,
    "amb_library": build_library,
    "amb_gym": build_gym,
    "amb_cafeteria": build_cafeteria,
    "amb_greenhouse": build_greenhouse,
}

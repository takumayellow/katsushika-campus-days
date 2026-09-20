"""SE (効果音) の合成.

すべてモノラルのワンショット. 足音は素材ごとに共鳴とノイズ帯域を変え,
4 バリエーションはシード違い + 微小なピッチ変化で作る.
"""
from __future__ import annotations

import math

import numpy as np

import synth as S
from synth import SR, add_at, nf


def pitch_shift(x: np.ndarray, factor: float) -> np.ndarray:
    """線形補間の再生速度変更 (短い SE のバリエーション用)."""
    n = len(x)
    idx = np.arange(0, n, factor)
    idx = idx[idx < n - 1]
    i0 = idx.astype(np.int64)
    frac = idx - i0
    return x[i0] * (1 - frac) + x[i0 + 1] * frac


def modes(freqs, taus, amps, dur: float) -> np.ndarray:
    """減衰する共鳴モードの和 (木・タイルの響き)."""
    t = S.tline(dur)
    out = np.zeros_like(t)
    for f, tau, a in zip(freqs, taus, amps):
        out += a * np.exp(-t / tau) * np.sin(2 * np.pi * f * t)
    return out


def burst(dur: float, lo: float, hi: float, tau: float, rng,
          attack: float = 0.0006) -> np.ndarray:
    nz = S.bandpass(rng.standard_normal(S.n_samples(dur)), lo, hi)
    return nz * S.perc_env(dur, attack, tau)


# --------------------------------------------------------------------------
# 足音
# --------------------------------------------------------------------------
def step_concrete(rng, var: int) -> np.ndarray:
    dur = 0.16
    y = burst(dur, 220.0, 3600.0, 0.022, rng) * 0.9
    y += burst(dur, 60.0, 200.0, 0.045, rng) * 0.55
    y += modes([95.0, 148.0], [0.03, 0.02], [0.18, 0.1], dur)
    return S.fade(pitch_shift(y, 1.0 + (var - 1.5) * 0.045), 0.0008, 0.02)


def step_grass(rng, var: int) -> np.ndarray:
    dur = 0.22
    y = burst(dur, 900.0, 9500.0, 0.055, rng, attack=0.004) * 1.0
    y += burst(dur, 300.0, 1200.0, 0.028, rng) * 0.35
    # 草は「ざっ」と擦れるので細かい振幅ゆらぎを掛ける
    t = S.tline(dur)
    y *= 1.0 + 0.5 * np.sin(2 * np.pi * (70.0 + var * 9) * t)
    return S.fade(pitch_shift(y, 1.0 + (var - 1.5) * 0.05), 0.003, 0.03)


def step_wood(rng, var: int) -> np.ndarray:
    dur = 0.28
    y = burst(dur, 180.0, 4200.0, 0.018, rng) * 0.8
    y += modes([176.0, 421.0, 903.0], [0.10, 0.06, 0.03], [0.55, 0.28, 0.12], dur)
    return S.fade(pitch_shift(y, 1.0 + (var - 1.5) * 0.055), 0.0008, 0.03)


def step_tile(rng, var: int) -> np.ndarray:
    dur = 0.18
    y = burst(dur, 800.0, 13000.0, 0.012, rng) * 1.0
    y += modes([2430.0, 3980.0, 1260.0], [0.035, 0.022, 0.05], [0.3, 0.18, 0.22], dur)
    y += burst(dur, 80.0, 260.0, 0.02, rng) * 0.25
    return S.fade(pitch_shift(y, 1.0 + (var - 1.5) * 0.05), 0.0006, 0.02)


STEP_KINDS = {
    "concrete": step_concrete,
    "grass": step_grass,
    "wood": step_wood,
    "tile": step_tile,
}


def build_steps() -> dict:
    out = {}
    for kind_index, (kind, fn) in enumerate(STEP_KINDS.items()):
        for var in range(4):
            # 文字列の hash() はプロセスごとに変わるので、固定整数で種を組む
            rng = np.random.default_rng(710_000 + kind_index * 100 + var)
            out[f"step_{kind}_{var + 1}"] = fn(rng, var)
    return out


# --------------------------------------------------------------------------
# UI
# --------------------------------------------------------------------------
def _blip(freq: float, dur: float, tau: float, shape: str = "sine",
          sweep: float = 1.0) -> np.ndarray:
    t = S.tline(dur)
    f = freq * np.exp(np.log(max(sweep, 1e-3)) * t / max(dur, 1e-6))
    ph = 2 * np.pi * np.cumsum(f) / SR
    if shape == "sine":
        y = np.sin(ph)
    elif shape == "tri":
        y = np.sin(ph) + 0.12 * np.sin(3 * ph) + 0.04 * np.sin(5 * ph)
    else:
        y = np.sin(ph) + 0.33 * np.sin(3 * ph) + 0.2 * np.sin(5 * ph)
    return y * S.perc_env(dur, 0.0025, tau)


def ui_move() -> np.ndarray:
    buf = np.zeros(S.n_samples(0.07))
    add_at(buf, _blip(1180.0, 0.07, 0.016, "tri", sweep=1.18) * 0.8, 0)
    add_at(buf, _blip(2360.0, 0.05, 0.008, "sine") * 0.25, 0)
    return S.fade(buf, 0.001, 0.012)


def ui_confirm() -> np.ndarray:
    buf = np.zeros(S.n_samples(0.42))
    for i, name in enumerate(("C6", "G6")):
        add_at(buf, S.bell(nf(name), 0.36, 0.9) * 1.6, S.n_samples(i * 0.055))
    add_at(buf, _blip(nf("C6"), 0.08, 0.02, "tri") * 0.5, 0)
    return S.fade(buf, 0.001, 0.06)


def ui_cancel() -> np.ndarray:
    buf = np.zeros(S.n_samples(0.3))
    add_at(buf, _blip(nf("G5"), 0.12, 0.03, "tri") * 0.8, 0)
    add_at(buf, _blip(nf("C5"), 0.18, 0.045, "tri") * 0.7, S.n_samples(0.06))
    return S.fade(S.lowpass(buf, 5200.0), 0.001, 0.04)


def ui_open() -> np.ndarray:
    dur = 0.34
    rng = np.random.default_rng(11)
    buf = _blip(420.0, dur, 0.1, "tri", sweep=3.2) * 0.55
    buf += burst(dur, 1800.0, 9000.0, 0.09, rng, attack=0.02) * 0.3
    for i, name in enumerate(("E6", "B6")):
        add_at(buf, S.bell(nf(name), 0.28, 0.35), S.n_samples(0.05 + i * 0.05))
    return S.fade(buf, 0.004, 0.05)


def ui_close() -> np.ndarray:
    dur = 0.28
    rng = np.random.default_rng(12)
    y = _blip(900.0, dur, 0.07, "tri", sweep=0.32) * 0.6
    y += burst(dur, 900.0, 5000.0, 0.05, rng, attack=0.004) * 0.28
    return S.fade(S.lowpass(y, 7000.0), 0.002, 0.05)


def ui_toast() -> np.ndarray:
    buf = np.zeros(S.n_samples(0.5))
    for i, name in enumerate(("E6", "A6")):
        add_at(buf, S.marimba(nf(name), 0.42, 0.8), S.n_samples(i * 0.085))
        add_at(buf, S.bell(nf(name), 0.4, 0.25), S.n_samples(i * 0.085))
    return S.fade(buf, 0.002, 0.07)


# --------------------------------------------------------------------------
# ゲーム動作
# --------------------------------------------------------------------------
def door_open() -> np.ndarray:
    """ラッチのカチッ + 蝶番のきしみ (AM したバンドノイズのピッチ上昇)."""
    rng = np.random.default_rng(21)
    dur = 0.95
    t = S.tline(dur)
    creak_f = 320.0 * (1.0 + 0.9 * t / dur)
    creak = np.sin(2 * np.pi * np.cumsum(creak_f) / SR)
    creak *= 0.5 + 0.5 * np.sin(2 * np.pi * 27.0 * t + np.sin(2 * np.pi * 3.1 * t))
    creak *= np.clip(np.sin(np.pi * np.clip(t / (dur * 0.85), 0, 1)) ** 1.3, 0, 1)
    creak = S.bandpass(creak, 260.0, 2600.0) * 0.45
    buf = np.zeros(S.n_samples(dur))
    add_at(buf, burst(0.06, 900.0, 8000.0, 0.008, rng) * 0.9, 0)
    add_at(buf, burst(0.3, 70.0, 400.0, 0.08, rng) * 0.35, S.n_samples(0.01))
    add_at(buf, creak, S.n_samples(0.05))
    return S.fade(buf, 0.001, 0.12)


def door_close() -> np.ndarray:
    rng = np.random.default_rng(22)
    buf = np.zeros(S.n_samples(0.55))
    thud = burst(0.35, 60.0, 340.0, 0.07, rng) * 0.9
    thud += modes([88.0, 143.0, 246.0], [0.07, 0.05, 0.03], [0.5, 0.3, 0.15], 0.35)
    add_at(buf, burst(0.22, 180.0, 1800.0, 0.07, rng, attack=0.03) * 0.3, 0)
    add_at(buf, thud, S.n_samples(0.18))
    add_at(buf, burst(0.07, 1200.0, 9000.0, 0.009, rng) * 0.7, S.n_samples(0.2))
    return S.fade(buf, 0.002, 0.08)


def item_get() -> np.ndarray:
    buf = np.zeros(S.n_samples(0.75))
    for i, name in enumerate(("G5", "C6", "E6")):
        add_at(buf, S.bell(nf(name), 0.66, 0.95) * 1.5, S.n_samples(i * 0.065))
        add_at(buf, S.marimba(nf(name), 0.3, 0.4), S.n_samples(i * 0.065))
    add_at(buf, S.bell(nf("G6"), 0.5, 0.3), S.n_samples(0.2))
    return S.fade(buf, 0.002, 0.1)


def quest_start() -> np.ndarray:
    """短いファンファーレ (C - E - G - C)."""
    buf = np.zeros(S.n_samples(1.15))
    for name, t0 in (("C5", 0.0), ("E5", 0.10), ("G5", 0.20), ("C6", 0.30)):
        add_at(buf, S.marimba(nf(name), 0.85, 0.9), S.n_samples(t0))
        add_at(buf, S.bell(nf(name), 0.8, 0.45), S.n_samples(t0))
    for f in S.chord("C", 5):
        add_at(buf, S.bell(f, 0.7, 0.3), S.n_samples(0.42))
    return S.fade(buf, 0.002, 0.15)


def quest_update() -> np.ndarray:
    buf = np.zeros(S.n_samples(0.55))
    for i, name in enumerate(("A5", "D6")):
        add_at(buf, S.marimba(nf(name), 0.45, 0.85), S.n_samples(i * 0.08))
    add_at(buf, S.bell(nf("D6"), 0.45, 0.25), S.n_samples(0.08))
    return S.fade(buf, 0.002, 0.08)


def camera_shutter() -> np.ndarray:
    rng = np.random.default_rng(23)
    buf = np.zeros(S.n_samples(0.4))
    add_at(buf, burst(0.05, 1500.0, 11000.0, 0.005, rng), 0)
    add_at(buf, modes([3100.0, 5200.0], [0.006, 0.004], [0.4, 0.2], 0.04), 0)
    add_at(buf, burst(0.06, 1100.0, 9000.0, 0.006, rng) * 0.85, S.n_samples(0.085))
    whir = burst(0.16, 700.0, 2600.0, 0.05, rng) * 0.2
    whir *= 0.6 + 0.4 * np.sin(2 * np.pi * 160.0 * S.tline(0.16))
    add_at(buf, whir, S.n_samples(0.1))
    return S.fade(buf, 0.0006, 0.05)


def jump() -> np.ndarray:
    """可愛い「ぴょん」. 基音が上がり, 軽い布ずれを添える."""
    rng = np.random.default_rng(24)
    buf = np.zeros(S.n_samples(0.28))
    add_at(buf, _blip(380.0, 0.26, 0.075, "tri", sweep=3.4) * 0.8, 0)
    add_at(buf, burst(0.16, 1600.0, 7000.0, 0.035, rng, attack=0.006) * 0.16, 0)
    return S.fade(buf, 0.002, 0.04)


def land() -> np.ndarray:
    rng = np.random.default_rng(25)
    dur = 0.3
    y = burst(dur, 55.0, 420.0, 0.045, rng) * 0.9
    y += burst(dur, 600.0, 5000.0, 0.02, rng) * 0.35
    y += modes([78.0, 132.0], [0.05, 0.035], [0.35, 0.18], dur)
    return S.fade(y, 0.0008, 0.04)


def sit() -> np.ndarray:
    rng = np.random.default_rng(26)
    buf = np.zeros(S.n_samples(0.55))
    add_at(buf, burst(0.35, 800.0, 6500.0, 0.12, rng, attack=0.05) * 0.4, 0)
    add_at(buf, modes([164.0, 392.0], [0.12, 0.07], [0.35, 0.16], 0.35) * 0.6,
           S.n_samples(0.09))
    add_at(buf, burst(0.12, 60.0, 300.0, 0.035, rng) * 0.4, S.n_samples(0.09))
    return S.fade(buf, 0.01, 0.09)


def wave() -> np.ndarray:
    """手を振る: 柔らかい布の風切り + 小さなチャイム."""
    rng = np.random.default_rng(27)
    dur = 0.6
    t = S.tline(dur)
    air = S.bandpass(rng.standard_normal(len(t)), 700.0, 6000.0)
    air *= np.sin(np.pi * np.clip(t / (dur * 0.7), 0, 1)) ** 2
    air *= 0.55 + 0.45 * np.sin(2 * np.pi * 3.2 * t)
    buf = air * 0.3
    add_at(buf, S.bell(nf("A6"), 0.45, 0.22), S.n_samples(0.12))
    add_at(buf, S.bell(nf("E7"), 0.35, 0.14), S.n_samples(0.2))
    return S.fade(buf, 0.012, 0.1)


# --------------------------------------------------------------------------
# 会話ブリップ (文字送り)
# --------------------------------------------------------------------------
def talk_blip(freq: float, shape: str, cutoff: float, dur: float = 0.065) -> np.ndarray:
    buf = _blip(freq, dur, 0.016, shape, sweep=1.12)
    add_at(buf, _blip(freq * 2.01, dur * 0.7, 0.008, "sine") * 0.18, 0)
    return S.fade(S.lowpass(buf, cutoff), 0.0015, 0.012)


# ウェストミンスターの鐘 (Westminster Quarters).
# 旋律は 1793 年にケンブリッジの Great St Mary 教会のために作られた伝承曲で,
# ヘンデル『メサイア』(1741) の一節に由来するとされる. 作者の没後 200 年以上が
# 経過しておりパブリックドメイン. E major の 4 音を 4 フレーズ並べた正時の形.
CHIME_PHRASES = (
    ("G#4", "F#4", "E4", "B3"),
    ("E4", "G#4", "F#4", "B3"),
    ("E4", "F#4", "G#4", "E4"),
    ("G#4", "E4", "F#4", "B3"),
)


def se_chime(note_sec: float = 0.78, phrase_gap: float = 1.05) -> np.ndarray:
    """学校のチャイム. 鐘を加算合成し, 長い残響を付けたステレオ素材."""
    span = 4 * note_sec + phrase_gap
    total = len(CHIME_PHRASES) * span + 3.4
    buf = np.zeros((S.n_samples(total), 2))
    t = 0.0
    for phrase in CHIME_PHRASES:
        for k, name in enumerate(phrase):
            ring = total - t          # 最後まで鳴らし切る
            sig = S.chime_bell(nf(name), min(ring, 4.6), vel=0.92 - 0.04 * k)
            add_at(buf, S.pan(sig, (k - 1.5) * 0.14), S.n_samples(t))
            t += note_sec
        t += phrase_gap
    out = S.reverb(buf, room=0.9, damp=0.22, mix=0.34)
    out = S.soft_clip(out, 1.05)
    return S.normalize(S.fade(out, 0.004, 1.2), -1.5)


def build_ui_and_game() -> dict:
    return {
        "se_chime": se_chime(),
        "ui_move": ui_move(), "ui_confirm": ui_confirm(), "ui_cancel": ui_cancel(),
        "ui_open": ui_open(), "ui_close": ui_close(), "ui_toast": ui_toast(),
        "door_open": door_open(), "door_close": door_close(), "item_get": item_get(),
        "quest_start": quest_start(), "quest_update": quest_update(),
        "camera_shutter": camera_shutter(), "jump": jump(), "land": land(),
        "sit": sit(), "wave": wave(),
        "talk_blip_f1": talk_blip(660.0, "tri", 7000.0),
        "talk_blip_f2": talk_blip(742.0, "tri", 8000.0, dur=0.058),
        "talk_blip_m1": talk_blip(348.0, "square", 4200.0, dur=0.07),
        "talk_blip_prof": talk_blip(262.0, "square", 3000.0, dur=0.082),
    }

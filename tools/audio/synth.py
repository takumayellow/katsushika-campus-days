"""numpy だけで音を作るための合成ライブラリ.

外部素材を一切使わず, 正弦波・ノイズ・フィルタ・簡易リバーブから
BGM / SE / 環境音を組み立てる. scipy があれば IIR フィルタに使い,
無ければ FFT 領域のゼロ位相フィルタへ自動フォールバックする.

座標系: 音声は float64 の numpy 配列. モノラルは (n,), ステレオは (n, 2).
サンプリングレートは全体で 44100 Hz 固定.
"""
from __future__ import annotations

import math
import wave

import numpy as np

SR = 44100

try:  # scipy は必須ではない
    from scipy.signal import butter, sosfilt, lfilter  # type: ignore

    HAVE_SCIPY = True
except Exception:  # pragma: no cover - 環境依存
    HAVE_SCIPY = False


# --------------------------------------------------------------------------
# 基本ユーティリティ
# --------------------------------------------------------------------------
def n_samples(dur: float) -> int:
    """秒数をサンプル数へ (最低 1)."""
    return max(1, int(round(dur * SR)))


def tline(dur: float) -> np.ndarray:
    """0 から dur までの時間軸 (秒)."""
    return np.arange(n_samples(dur), dtype=np.float64) / SR


def silence(dur: float, stereo: bool = False) -> np.ndarray:
    n = n_samples(dur)
    return np.zeros((n, 2) if stereo else n, dtype=np.float64)


def to_stereo(x: np.ndarray) -> np.ndarray:
    if x.ndim == 2:
        return x
    return np.repeat(x[:, None], 2, axis=1)


def pan(x: np.ndarray, p: float) -> np.ndarray:
    """等パワーパン. p = -1 (左) .. 0 (中央) .. +1 (右)."""
    p = float(np.clip(p, -1.0, 1.0))
    ang = (p + 1.0) * 0.25 * math.pi
    mono = x if x.ndim == 1 else x.mean(axis=1)
    return np.stack([mono * math.cos(ang), mono * math.sin(ang)], axis=1)


def add_at(buf: np.ndarray, sig: np.ndarray, start: int, wrap: bool = False) -> None:
    """buf の start サンプル目に sig を加算する (破壊的).

    wrap=True なら末尾を越えた分を先頭へ回り込ませる. ループ素材で
    「末尾で鳴り始めた音が先頭へ続く」状態を作るために使う.
    """
    n = len(buf)
    if buf.ndim == 2 and sig.ndim == 1:
        sig = to_stereo(sig)
    if buf.ndim == 1 and sig.ndim == 2:
        sig = sig.mean(axis=1)
    m = len(sig)
    if m == 0:
        return
    start = int(start)
    if not wrap:
        if start >= n or start + m <= 0:
            return
        s0 = max(0, start)
        e0 = min(n, start + m)
        buf[s0:e0] += sig[s0 - start:e0 - start]
        return
    start %= n
    pos = 0
    while pos < m:
        chunk = min(n - start, m - pos)
        buf[start:start + chunk] += sig[pos:pos + chunk]
        pos += chunk
        start = 0


def db_to_lin(db: float) -> float:
    return float(10.0 ** (db / 20.0))


def lin_to_db(x: float) -> float:
    return float(20.0 * math.log10(max(x, 1e-12)))


def remove_dc(x: np.ndarray) -> np.ndarray:
    """DC 成分と超低域のドリフトを取り除く (ループ位相を壊さない平均引き + FFT HPF)."""
    y = x - x.mean(axis=0, keepdims=True)
    return fft_filter(y, lambda f: 1.0 / np.sqrt(1.0 + (18.0 / np.maximum(f, 1e-6)) ** 4))


def normalize(x: np.ndarray, peak_dbfs: float = -1.0) -> np.ndarray:
    peak = float(np.max(np.abs(x))) if x.size else 0.0
    if peak <= 1e-9:
        return x
    return x * (db_to_lin(peak_dbfs) / peak)


def fade(x: np.ndarray, fin: float = 0.005, fout: float = 0.01) -> np.ndarray:
    """先頭・末尾に cos^2 フェードをかける (ワンショット用)."""
    y = x.copy()
    ni = min(n_samples(fin), len(y) // 2)
    no = min(n_samples(fout), len(y) // 2)
    if ni > 0:
        env = np.sin(np.linspace(0, math.pi / 2, ni)) ** 2
        y[:ni] *= env[:, None] if y.ndim == 2 else env
    if no > 0:
        env = np.cos(np.linspace(0, math.pi / 2, no)) ** 2
        y[len(y) - no:] *= env[:, None] if y.ndim == 2 else env
    return y


def write_wav(path: str, x: np.ndarray, peak_dbfs: float = -1.0) -> dict:
    """16bit PCM WAV として書き出し, 統計を返す."""
    y = remove_dc(np.asarray(x, dtype=np.float64))
    y = normalize(y, peak_dbfs)
    ch = 1 if y.ndim == 1 else y.shape[1]
    ints = np.clip(np.round(y * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(ints.tobytes())
    return {"samples": len(y), "channels": ch, "duration": len(y) / SR}


# --------------------------------------------------------------------------
# フィルタ
# --------------------------------------------------------------------------
def fft_filter(x: np.ndarray, shape) -> np.ndarray:
    """FFT 領域で振幅特性 shape(freqs) を掛けるゼロ位相フィルタ.

    巡回畳み込みなので「ちょうど 1 周期」の信号に掛けてもループ点が壊れない.
    """
    n = len(x)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    h = np.asarray(shape(freqs), dtype=np.float64)
    if x.ndim == 1:
        return np.fft.irfft(np.fft.rfft(x) * h, n)
    cols = [np.fft.irfft(np.fft.rfft(x[:, c]) * h, n) for c in range(x.shape[1])]
    return np.stack(cols, axis=1)


def _butter_mag(f, fc, order, kind):
    f = np.maximum(f, 1e-6)
    if kind == "low":
        return 1.0 / np.sqrt(1.0 + (f / fc) ** (2 * order))
    return 1.0 / np.sqrt(1.0 + (fc / f) ** (2 * order))


def _iir(x, sos):
    if x.ndim == 1:
        return sosfilt(sos, x)
    return np.stack([sosfilt(sos, x[:, c]) for c in range(x.shape[1])], axis=1)


def lowpass(x: np.ndarray, fc: float, order: int = 4, zero_phase: bool = False) -> np.ndarray:
    fc = min(fc, SR * 0.49)
    if HAVE_SCIPY and not zero_phase:
        return _iir(x, butter(order, fc / (SR / 2), btype="low", output="sos"))
    return fft_filter(x, lambda f: _butter_mag(f, fc, order, "low"))


def highpass(x: np.ndarray, fc: float, order: int = 4, zero_phase: bool = False) -> np.ndarray:
    fc = max(min(fc, SR * 0.49), 1.0)
    if HAVE_SCIPY and not zero_phase:
        return _iir(x, butter(order, fc / (SR / 2), btype="high", output="sos"))
    return fft_filter(x, lambda f: _butter_mag(f, fc, order, "high"))


def bandpass(x: np.ndarray, lo: float, hi: float, order: int = 2,
             zero_phase: bool = False) -> np.ndarray:
    lo = max(lo, 10.0)
    hi = min(hi, SR * 0.49)
    if hi <= lo * 1.02:
        hi = lo * 1.05
    if HAVE_SCIPY and not zero_phase:
        return _iir(x, butter(order, [lo / (SR / 2), hi / (SR / 2)], btype="band", output="sos"))
    return fft_filter(x, lambda f: _butter_mag(f, hi, order, "low") * _butter_mag(f, lo, order, "high"))


def peaking(x: np.ndarray, fc: float, q: float, gain_db: float) -> np.ndarray:
    """ピーキング EQ (ゼロ位相・ループ安全)."""
    g = db_to_lin(gain_db) - 1.0

    def shape(f):
        f = np.maximum(f, 1e-6)
        w = (f / fc - fc / f) * q
        return 1.0 + g / (1.0 + w * w)

    return fft_filter(x, shape)


def resonant_lp(x: np.ndarray, fc: float, q: float = 4.0) -> np.ndarray:
    """2 次共振ローパスの振幅特性 (ゼロ位相)."""
    def shape(f):
        r = np.maximum(f, 1e-6) / fc
        return 1.0 / np.sqrt((1.0 - r * r) ** 2 + (r / q) ** 2)

    return fft_filter(x, shape)


# --------------------------------------------------------------------------
# オシレータ / ノイズ
# --------------------------------------------------------------------------
def sine(freq, dur: float, phase: float = 0.0) -> np.ndarray:
    t = tline(dur)
    f = np.full_like(t, float(freq)) if np.isscalar(freq) else np.asarray(freq)[: len(t)]
    return np.sin(2 * np.pi * np.cumsum(f) / SR + phase)


def saw(freq: float, dur: float, nharm: int | None = None) -> np.ndarray:
    """加算合成の帯域制限のこぎり波 (エイリアス無し)."""
    t = tline(dur)
    nmax = int((SR * 0.45) // max(freq, 1.0))
    if nharm is not None:
        nmax = min(nmax, nharm)
    out = np.zeros_like(t)
    for k in range(1, max(nmax, 1) + 1):
        out += np.sin(2 * np.pi * freq * k * t) / k
    return out * (2.0 / np.pi)


def square(freq: float, dur: float, nharm: int | None = None) -> np.ndarray:
    t = tline(dur)
    nmax = int((SR * 0.45) // max(freq, 1.0))
    if nharm is not None:
        nmax = min(nmax, nharm)
    out = np.zeros_like(t)
    for k in range(1, max(nmax, 1) + 1, 2):
        out += np.sin(2 * np.pi * freq * k * t) / k
    return out * (4.0 / np.pi) * 0.5


def triangle(freq: float, dur: float, nharm: int = 20) -> np.ndarray:
    t = tline(dur)
    out = np.zeros_like(t)
    for i, k in enumerate(range(1, nharm * 2, 2)):
        out += ((-1) ** i) * np.sin(2 * np.pi * freq * k * t) / (k * k)
    return out * (8.0 / (np.pi ** 2))


def noise(dur: float, rng: np.random.Generator) -> np.ndarray:
    return rng.standard_normal(n_samples(dur))


def spectral_noise(dur: float, shape, rng: np.random.Generator,
                   stereo: bool = False) -> np.ndarray:
    """指定の振幅スペクトルを持つ「厳密に周期 dur」のノイズ.

    周波数領域でランダム位相を与えて逆 FFT するので, 末尾と先頭が
    サンプル単位で連続する (環境音ループのベース).
    """
    n = n_samples(dur)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    mag = np.asarray(shape(freqs), dtype=np.float64)
    mag[0] = 0.0

    def one():
        ph = rng.uniform(0, 2 * np.pi, len(freqs))
        spec = mag * np.exp(1j * ph)
        spec[0] = 0.0
        y = np.fft.irfft(spec, n)
        m = np.max(np.abs(y))
        return y / m if m > 1e-12 else y

    if not stereo:
        return one()
    return np.stack([one(), one()], axis=1)


def periodic_lfo(dur: float, cycles, amps, rng: np.random.Generator) -> np.ndarray:
    """dur をちょうど整数周期で割り切る LFO の和 (0..1 に正規化).

    cycles は「dur の中に何周期入るか」の整数列. 周期が dur を割り切るので
    ループ点で不連続にならない.
    """
    t = tline(dur)
    out = np.zeros_like(t)
    for c, a in zip(cycles, amps):
        out += a * np.sin(2 * np.pi * c * t / dur + rng.uniform(0, 2 * np.pi))
    lo, hi = out.min(), out.max()
    return (out - lo) / (hi - lo + 1e-12)


# --------------------------------------------------------------------------
# エンベロープ
# --------------------------------------------------------------------------
def adsr(dur: float, a: float, d: float, s: float, r: float) -> np.ndarray:
    n = n_samples(dur)
    na, nd, nr = n_samples(a), n_samples(d), n_samples(r)
    na = min(na, n)
    nd = min(nd, max(n - na, 0))
    nr = min(nr, max(n - na - nd, 0))
    ns = max(n - na - nd - nr, 0)
    parts = [
        np.linspace(0.0, 1.0, na, endpoint=False) ** 0.7 if na else np.empty(0),
        (1.0 - (1.0 - s) * np.linspace(0.0, 1.0, nd, endpoint=False)) if nd else np.empty(0),
        np.full(ns, s),
        (s * np.cos(np.linspace(0, math.pi / 2, nr)) ** 2) if nr else np.empty(0),
    ]
    env = np.concatenate(parts)
    if len(env) < n:
        env = np.concatenate([env, np.zeros(n - len(env))])
    return env[:n]


def expdecay(dur: float, tau: float, attack: float = 0.004) -> np.ndarray:
    t = tline(dur)
    env = np.exp(-t / max(tau, 1e-4))
    na = min(n_samples(attack), len(env))
    if na > 1:
        env[:na] *= np.sin(np.linspace(0, math.pi / 2, na)) ** 2
    return env


def perc_env(dur: float, attack: float, decay_tau: float, hold: float = 0.0) -> np.ndarray:
    n = n_samples(dur)
    env = np.ones(n)
    na = min(n_samples(attack), n)
    if na > 1:
        env[:na] = np.sin(np.linspace(0, math.pi / 2, na)) ** 2
    nh = min(na + n_samples(hold), n)
    tail = np.arange(n - nh, dtype=np.float64) / SR
    env[nh:] = np.exp(-tail / max(decay_tau, 1e-4))
    return env


# --------------------------------------------------------------------------
# エフェクト
# --------------------------------------------------------------------------
_COMB = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617]
_ALLPASS = [556, 441, 341, 225]


def _comb(x: np.ndarray, delay: int, g: float, damp: float) -> np.ndarray:
    if HAVE_SCIPY:
        a = np.zeros(delay + 1)
        a[0] = 1.0
        a[1] = -damp
        a[delay] += -g * (1.0 - damp)
        return lfilter(np.array([1.0, -damp]), a, x)
    # scipy 無しのときは有限次数へ打ち切った級数展開で近似
    y = x.copy()
    src = lowpass(x, 3000.0 * (1.0 - damp) + 800.0, order=1, zero_phase=True)
    gk = g
    k = 1
    while gk > 1e-4 and k * delay < len(x):
        y[k * delay:] += gk * src[: len(x) - k * delay]
        gk *= g
        k += 1
    return y


def _allpass(x: np.ndarray, delay: int, g: float) -> np.ndarray:
    if HAVE_SCIPY:
        b = np.zeros(delay + 1)
        b[0] = -g
        b[delay] = 1.0
        a = np.zeros(delay + 1)
        a[0] = 1.0
        a[delay] = -g
        return lfilter(b, a, x)
    y = -g * x
    k = 1
    while g ** (k - 1) > 1e-4 and k * delay < len(x):
        c = (g ** (k - 1)) * (1.0 - g * g)
        y[k * delay:] += c * x[: len(x) - k * delay]
        k += 1
    return y


def reverb(x: np.ndarray, room: float = 0.84, damp: float = 0.30,
           mix: float = 0.22, width: int = 0) -> np.ndarray:
    """Schroeder / Freeverb 型の簡易リバーブ. 戻りはステレオ."""
    mono = x if x.ndim == 1 else x.mean(axis=1)
    mono = np.asarray(mono, dtype=np.float64)
    outs = []
    for side in (0, 1):
        wet = np.zeros_like(mono)
        for d in _COMB:
            wet += _comb(mono, d + side * 23 + width, room, damp)
        wet /= len(_COMB)
        for d in _ALLPASS:
            wet = _allpass(wet, d + side * 11, 0.5)
        outs.append(wet)
    wet_st = np.stack(outs, axis=1)
    dry = to_stereo(x)
    peak = np.max(np.abs(wet_st))
    if peak > 1e-9:
        wet_st = wet_st / peak * (np.max(np.abs(dry)) + 1e-9)
    return dry * (1.0 - mix) + wet_st * mix


def delay_fx(x: np.ndarray, time: float, feedback: float = 0.35,
             mix: float = 0.2, wrap: bool = False) -> np.ndarray:
    """フィードバックディレイ. wrap=True でループ素材の折り返しに対応."""
    d = n_samples(time)
    y = x.copy()
    wet = np.zeros_like(x)
    tap = x.copy()
    g = feedback
    for _ in range(12):
        shifted = np.zeros_like(x)
        if wrap:
            add_at(shifted, tap, d, wrap=True)
        else:
            if d < len(x):
                shifted[d:] = tap[: len(x) - d]
        wet += g * shifted
        tap = shifted
        g *= feedback
        if g < 1e-3:
            break
    return y * (1.0 - mix * 0.3) + wet * mix


def chorus(x: np.ndarray, depth_ms: float = 6.0, rate: float = 0.35,
           mix: float = 0.35) -> np.ndarray:
    """LFO で読み出し位置を揺らす簡易コーラス (線形補間)."""
    st = to_stereo(x)
    n = len(st)
    t = np.arange(n) / SR
    out = st.copy()
    for c, phase in enumerate((0.0, math.pi * 0.5)):
        base = 0.012 * SR
        mod = base + depth_ms * 1e-3 * SR * np.sin(2 * np.pi * rate * t + phase)
        idx = np.arange(n) - mod
        idx = np.clip(idx, 0, n - 1.001)
        i0 = idx.astype(np.int64)
        frac = idx - i0
        src = st[:, c]
        wet = src[i0] * (1 - frac) + src[i0 + 1] * frac
        out[:, c] = src * (1 - mix) + wet * mix
    return out


def soft_clip(x: np.ndarray, drive: float = 1.3) -> np.ndarray:
    """tanh のソフトクリップ. 軽い倍音付加とピーク抑制を兼ねる."""
    return np.tanh(x * drive) / math.tanh(drive)


def _smooth(v: np.ndarray, win: int, circular: bool) -> np.ndarray:
    """移動平均. circular=True なら端を反対側へ巻いて畳む (ループ素材用).

    mode="same" のゼロ詰めは先頭と末尾の包絡を過小評価するので, ループ素材に
    使うと継ぎ目だけ圧縮が甘くなって段差になる. 巻き込めばその段差が消える.
    """
    win = max(1, min(win, len(v)))
    kernel = np.ones(win) / win
    if not circular:
        return np.convolve(v, kernel, mode="same")
    pad = win
    ext = np.concatenate([v[-pad:], v, v[:pad]])
    return np.convolve(ext, kernel, mode="same")[pad:pad + len(v)]


def compress(x: np.ndarray, thresh_db: float = -18.0, ratio: float = 3.0,
             attack: float = 0.012, release: float = 0.16,
             circular: bool = False) -> np.ndarray:
    """移動平均で包絡を取り, 閾値超過分を圧縮する簡易コンプレッサ."""
    mono = np.abs(x if x.ndim == 1 else x.mean(axis=1))
    env = _smooth(mono, n_samples(attack), circular)
    env = _smooth(env, n_samples(release), circular)
    env_db = 20.0 * np.log10(np.maximum(env, 1e-9))
    over = np.maximum(env_db - thresh_db, 0.0)
    gain_db = -over * (1.0 - 1.0 / ratio)
    gain = 10.0 ** (gain_db / 20.0)
    makeup = db_to_lin(-thresh_db * (1.0 - 1.0 / ratio) * 0.45)
    if x.ndim == 2:
        gain = gain[:, None]
    return x * gain * makeup


def widen(x: np.ndarray, amount: float = 1.25) -> np.ndarray:
    st = to_stereo(x)
    mid = st.mean(axis=1)
    side = (st[:, 0] - st[:, 1]) * 0.5 * amount
    return np.stack([mid + side, mid - side], axis=1)


# --------------------------------------------------------------------------
# ループ加工
# --------------------------------------------------------------------------
def wrap_tail(buf: np.ndarray, loop_len: int) -> np.ndarray:
    """loop_len より後ろ (残響やリリース) を先頭へ足し込んでシームレス化."""
    head = buf[:loop_len].copy()
    tail = buf[loop_len:]
    if len(tail):
        add_at(head, tail[:loop_len], 0, wrap=True)
        if len(tail) > loop_len:
            add_at(head, tail[loop_len:], 0, wrap=True)
    return head


def xfade_loop(buf: np.ndarray, loop_len: int, xfade: float = 0.25) -> np.ndarray:
    """末尾に余分を作っておき, 等パワークロスフェードでループ化する."""
    nx = min(n_samples(xfade), loop_len // 2, max(len(buf) - loop_len, 0))
    out = buf[:loop_len].copy()
    if nx <= 0:
        return out
    fade_in = np.sin(np.linspace(0, math.pi / 2, nx)) ** 2
    fade_out = 1.0 - fade_in
    extra = buf[loop_len:loop_len + nx]
    if out.ndim == 2:
        fade_in = fade_in[:, None]
        fade_out = fade_out[:, None]
    out[:nx] = out[:nx] * fade_in + extra * fade_out
    return out


# --------------------------------------------------------------------------
# 音程・和音
# --------------------------------------------------------------------------
_PC = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5,
       "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}

_QUALITY = {
    "": [0, 4, 7], "m": [0, 3, 7], "dim": [0, 3, 6], "aug": [0, 4, 8],
    "7": [0, 4, 7, 10], "maj7": [0, 4, 7, 11], "m7": [0, 3, 7, 10],
    "m7b5": [0, 3, 6, 10], "6": [0, 4, 7, 9], "m6": [0, 3, 7, 9],
    "sus4": [0, 5, 7], "add9": [0, 4, 7, 14], "9": [0, 4, 7, 10, 14],
    "m9": [0, 3, 7, 10, 14], "maj9": [0, 4, 7, 11, 14], "69": [0, 4, 7, 9, 14],
}


def nf(name: str) -> float:
    """'C4' / 'F#3' → 周波数 Hz (A4 = 440)."""
    i = 2 if len(name) > 2 and name[1] in "#b" else 1
    pc = _PC[name[:i]]
    octave = int(name[i:])
    midi = (octave + 1) * 12 + pc
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def chord(symbol: str, octave: int = 3) -> list[float]:
    """'Am7' → 構成音の周波数リスト."""
    i = 2 if len(symbol) > 1 and symbol[1] in "#b" else 1
    root, quality = symbol[:i], symbol[i:]
    semis = _QUALITY[quality]
    base = nf(f"{root}{octave}")
    return [base * 2.0 ** (s / 12.0) for s in semis]


def scale_freqs(root: str, mode: str, octaves: int = 2, start_oct: int = 4) -> list[float]:
    steps = {"major": [0, 2, 4, 5, 7, 9, 11], "minor": [0, 2, 3, 5, 7, 8, 10],
             "pentatonic": [0, 2, 4, 7, 9], "dorian": [0, 2, 3, 5, 7, 9, 10]}[mode]
    base = nf(f"{root}{start_oct}")
    out = []
    for o in range(octaves):
        for s in steps:
            out.append(base * 2.0 ** ((s + 12 * o) / 12.0))
    return out


def preroll(x: np.ndarray, fn, pre: float = 4.0) -> np.ndarray:
    """ループ素材に因果フィルタ / リバーブを掛けるときの助走つき適用.

    末尾 pre 秒を前に継いでから fn を通し, 助走分を捨てる. これで
    「ループ 1 周前から鳴っていた残響」が先頭に入り, 継ぎ目が消える.
    """
    p = min(n_samples(pre), len(x))
    y = np.concatenate([x[len(x) - p:], x], axis=0)
    out = fn(y)
    return out[p:]


# --------------------------------------------------------------------------
# 楽器
# --------------------------------------------------------------------------
def piano(freq: float, dur: float, vel: float = 1.0, bright: float = 1.0) -> np.ndarray:
    """加算合成のピアノ風. 部分音ごとに減衰時間を変え, 弦の硬さ由来のずれを入れる."""
    t = tline(dur)
    out = np.zeros_like(t)
    stiff = 0.0004
    nmax = max(1, min(18, int(SR * 0.45 / max(freq, 1.0))))
    base_tau = 1.1 + 1.6 * (220.0 / max(freq, 60.0)) ** 0.7
    ph0 = (freq * 7919.0) % (2 * np.pi)
    for k in range(1, nmax + 1):
        fk = freq * k * math.sqrt(1.0 + stiff * k * k)
        amp = 1.0 / k ** (1.45 / max(bright, 0.3))
        tau = base_tau / (k ** 0.55)
        out += amp * np.exp(-t / tau) * np.sin(2 * np.pi * fk * t + ph0 * k)
    na = min(n_samples(0.004), len(out))
    out[:na] *= np.sin(np.linspace(0, math.pi / 2, na)) ** 2
    thump = np.zeros_like(t)
    nt = min(n_samples(0.02), len(t))
    ph = np.random.default_rng(int(freq * 13) % 9973).standard_normal(nt)
    thump[:nt] = ph * np.exp(-np.arange(nt) / (0.004 * SR)) * 0.12
    return (out * 0.42 + thump) * vel


def epiano(freq: float, dur: float, vel: float = 1.0) -> np.ndarray:
    """FM 2 オペレータのエレピ風 (比 1 の胴 + 比 14 のベル)."""
    t = tline(dur)
    body_env = np.exp(-t / (1.2 + 90.0 / max(freq, 60.0)))
    idx_env = np.exp(-t / 0.35)
    mod = np.sin(2 * np.pi * freq * t) * idx_env * 2.6
    body = np.sin(2 * np.pi * freq * t + mod) * body_env
    bell_env = np.exp(-t / 0.22)
    bell = np.sin(2 * np.pi * freq * t
                  + np.sin(2 * np.pi * freq * 14 * t) * bell_env * 0.55) * bell_env
    y = body * 0.8 + bell * 0.22
    na = min(n_samples(0.005), len(y))
    y[:na] *= np.sin(np.linspace(0, math.pi / 2, na)) ** 2
    return y * 0.55 * vel


def pluck(freq: float, dur: float, vel: float = 1.0, damp: float = 0.55) -> np.ndarray:
    """撥弦を加算合成で近似 (Karplus-Strong 風の減衰分布)."""
    t = tline(dur)
    out = np.zeros_like(t)
    nmax = max(1, min(24, int(SR * 0.45 / max(freq, 1.0))))
    for k in range(1, nmax + 1):
        tau = 0.9 * damp / (k ** 0.8)
        out += (1.0 / k) * np.exp(-t / tau) * np.sin(2 * np.pi * freq * k * t + k * 0.7)
    na = min(n_samples(0.002), len(out))
    out[:na] *= np.linspace(0, 1, na) ** 2
    return out * 0.38 * vel


def bell(freq: float, dur: float, vel: float = 1.0) -> np.ndarray:
    """非整数倍音のベル / グロッケン."""
    t = tline(dur)
    ratios = [1.0, 2.76, 5.40, 8.93, 13.34]
    amps = [1.0, 0.5, 0.28, 0.14, 0.07]
    taus = [1.7, 0.9, 0.55, 0.32, 0.2]
    out = np.zeros_like(t)
    for r, a, tau in zip(ratios, amps, taus):
        f = freq * r
        if f > SR * 0.45:
            continue
        out += a * np.exp(-t / tau) * np.sin(2 * np.pi * f * t)
    na = min(n_samples(0.003), len(out))
    out[:na] *= np.linspace(0, 1, na) ** 2
    return out * 0.32 * vel


def marimba(freq: float, dur: float, vel: float = 1.0) -> np.ndarray:
    t = tline(dur)
    out = (np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.45)
           + 0.35 * np.sin(2 * np.pi * freq * 3.93 * t) * np.exp(-t / 0.12)
           + 0.12 * np.sin(2 * np.pi * freq * 9.2 * t) * np.exp(-t / 0.05))
    na = min(n_samples(0.002), len(out))
    out[:na] *= np.linspace(0, 1, na) ** 2
    return out * 0.5 * vel


def soft_mallet(freq: float, dur: float, vel: float = 1.0) -> np.ndarray:
    """高い音域 (F5〜A6) 用の木琴の「ポン」. marimba と同じ基音に, 整数倍 (4 倍) の短い倍音だけを足す.
    marimba の 3.93 倍・9.2 倍の非整数倍音は, この音域だと 2.7〜13 kHz に突出 25〜64 dB の山を作って
    0.28 秒ほど残り, 鈴やグロッケンのような金属音に聞こえていた (#28). 4 倍は本物のマリンバの調律で, 25 ms で消す."""
    t = tline(dur)
    out = (np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.45)
           + 0.2 * np.sin(2 * np.pi * freq * 4.0 * t) * np.exp(-t / 0.025))
    na = min(n_samples(0.002), len(out))
    out[:na] *= np.linspace(0, 1, na) ** 2
    return out * 0.5 * vel


def bass(freq: float, dur: float, vel: float = 1.0) -> np.ndarray:
    """サイン基音 + 低次倍音のエレキベース風."""
    t = tline(dur)
    env = adsr(dur, 0.008, 0.10, 0.62, min(0.22, dur * 0.4))
    y = (np.sin(2 * np.pi * freq * t)
         + 0.30 * np.sin(2 * np.pi * freq * 2 * t)
         + 0.12 * np.sin(2 * np.pi * freq * 3 * t)
         + 0.05 * np.sin(2 * np.pi * freq * 4 * t))
    return y * env * 0.32 * vel


def pad(freqs, dur: float, vel: float = 1.0, detune: float = 0.006,
        cutoff: float = 2200.0, attack: float = 0.55, nharm: int = 12) -> np.ndarray:
    """デチューンした鋸波を重ねたストリングス / シンセパッド."""
    t = tline(dur)
    out = np.zeros_like(t)
    vib = np.cumsum(1.0 + 0.0022 * np.sin(2 * np.pi * 4.6 * t)) / SR
    for i, f in enumerate(freqs):
        for d in (-detune, 0.0, detune):
            ff = f * (1.0 + d)
            nmax = max(1, min(nharm, int(SR * 0.45 / max(ff, 1.0))))
            for k in range(1, nmax + 1):
                out += np.sin(2 * np.pi * ff * k * vib + (i + k) * 0.61) / (k * 1.4)
    out /= max(len(freqs), 1) * 3.0
    env = adsr(dur, attack, 0.3, 0.8, min(0.9, dur * 0.45))
    return lowpass(out * env, cutoff, order=2, zero_phase=True) * 0.45 * vel


def organ(freqs, dur: float, vel: float = 1.0) -> np.ndarray:
    t = tline(dur)
    out = np.zeros_like(t)
    for f in freqs:
        for k, a in ((1, 1.0), (2, 0.5), (3, 0.28), (4, 0.2), (6, 0.1)):
            if f * k < SR * 0.45:
                out += a * np.sin(2 * np.pi * f * k * t)
    out /= max(len(freqs), 1) * 2.0
    return out * adsr(dur, 0.03, 0.1, 0.9, 0.12) * 0.3 * vel


# --------------------------------------------------------------------------
# ドラム
# --------------------------------------------------------------------------
def kick(dur: float = 0.45, rng=None, f0: float = 150.0, f1: float = 46.0,
         vel: float = 1.0) -> np.ndarray:
    rng = rng if rng is not None else np.random.default_rng(0)
    t = tline(dur)
    f = f1 + (f0 - f1) * np.exp(-t / 0.035)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * perc_env(dur, 0.001, 0.115)
    click = np.zeros_like(t)
    nc = min(n_samples(0.006), len(t))
    click[:nc] = rng.standard_normal(nc) * np.exp(-np.arange(nc) / (0.0015 * SR)) * 0.35
    return (body * 0.95 + highpass(click, 1200.0)) * vel


def snare(dur: float = 0.28, rng=None, vel: float = 1.0) -> np.ndarray:
    rng = rng if rng is not None else np.random.default_rng(1)
    t = tline(dur)
    nz = bandpass(rng.standard_normal(len(t)), 900.0, 9000.0)
    nz *= perc_env(dur, 0.0008, 0.085)
    tone = np.sin(2 * np.pi * 186.0 * t) + 0.7 * np.sin(2 * np.pi * 331.0 * t)
    tone *= perc_env(dur, 0.0008, 0.045)
    return (nz * 0.62 + tone * 0.3) * vel


def hat(dur: float = 0.09, rng=None, open_: bool = False, vel: float = 1.0) -> np.ndarray:
    rng = rng if rng is not None else np.random.default_rng(2)
    nz = bandpass(rng.standard_normal(n_samples(dur)), 7200.0, 15000.0)
    return nz * perc_env(dur, 0.0004, 0.16 if open_ else 0.022) * 0.5 * vel


def shaker(dur: float = 0.12, rng=None, vel: float = 1.0) -> np.ndarray:
    rng = rng if rng is not None else np.random.default_rng(3)
    nz = bandpass(rng.standard_normal(n_samples(dur)), 3800.0, 11000.0)
    return nz * perc_env(dur, 0.012, 0.035) * 0.45 * vel


def rimshot(dur: float = 0.1, rng=None, vel: float = 1.0) -> np.ndarray:
    rng = rng if rng is not None else np.random.default_rng(4)
    t = tline(dur)
    y = (np.sin(2 * np.pi * 420 * t) + np.sin(2 * np.pi * 780 * t) * 0.6)
    y = y * perc_env(dur, 0.0005, 0.018)
    nz = bandpass(rng.standard_normal(len(t)), 1500.0, 6000.0) * perc_env(dur, 0.0005, 0.012)
    return (y * 0.5 + nz * 0.4) * vel


def brass(freq: float, dur: float, vel: float = 1.0, bright: float = 1.0) -> np.ndarray:
    """ブラス. 立ち上がりで高次倍音が遅れて開く「ブワッ」を倍音ごとの
    遅延アタックで作り, 弱いビブラートを乗せる."""
    t = tline(dur)
    atk = min(0.09, dur * 0.35)
    out = np.zeros_like(t)
    vib = 1.0 + 0.004 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.18) / 0.3, 0, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    nh = max(1, int(min(16, (SR * 0.42) / max(freq, 1.0))))
    for k in range(1, nh + 1):
        a = (1.0 / k ** 1.12) * (bright ** min(k - 1, 6))
        # 高次ほど遅れて立ち上がる
        lag = atk * (0.25 + 0.75 * (k - 1) / max(nh - 1, 1))
        env = np.clip(t / max(lag, 1e-4), 0.0, 1.0)
        out += a * np.sin(k * ph) * env
    out /= max(np.abs(out).max(), 1e-9)
    body = adsr(dur, atk, 0.12, 0.74, min(0.22, dur * 0.4))
    # 息のノイズをわずかに足す
    rng = np.random.default_rng(int(freq * 7) & 0xFFFF)
    air = lowpass(rng.standard_normal(len(t)), 3800.0) * np.exp(-t / 0.05) * 0.04
    return (out * body + air) * 0.5 * vel


def chime_bell(freq: float, dur: float, vel: float = 1.0) -> np.ndarray:
    """教会鐘 / チャイム. ハム音 (基音の 1 オクターブ下) と
    短三度のタース音を含む実物に近い部分音構成."""
    t = tline(dur)
    # 短三度のタース (1.19) は長調の BGM に重なると濁るので控えめにしてある (#38)
    ratios = [0.5, 1.0, 1.19, 1.5, 2.0, 2.5, 2.66, 3.01, 4.07, 5.43]
    amps = [0.42, 1.0, 0.16, 0.26, 0.55, 0.14, 0.08, 0.12, 0.07, 0.04]
    taus = [3.6, 2.9, 1.5, 1.4, 1.9, 0.9, 0.7, 0.8, 0.45, 0.3]
    out = np.zeros_like(t)
    for r, a, tau in zip(ratios, amps, taus):
        f = freq * r
        if f >= SR * 0.45:
            continue
        beat = 1.0 + 0.004 * np.sin(2 * np.pi * (0.7 + r) * t)
        out += a * np.sin(2 * np.pi * f * np.cumsum(beat) / SR) * np.exp(-t / (tau * dur / 2.9 + 1e-9))
    na = min(n_samples(0.004), len(out))
    out[:na] *= np.linspace(0, 1, na) ** 2
    return out / max(np.abs(out).max(), 1e-9) * 0.55 * vel

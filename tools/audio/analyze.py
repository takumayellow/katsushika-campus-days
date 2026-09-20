"""生成した WAV を数値で検証する.

音を聴かずに品質を確かめるため, 以下を測る:
  - ピーク / RMS / クリップ数 / 無音でないこと
  - スペクトル重心と RMS の時間変化 (「ちゃんと鳴っているか」)
  - ループ継ぎ目の連続性 (末尾と先頭の相関・段差・継ぎ目の高域増加)
"""
from __future__ import annotations

import os
import wave

import numpy as np

SR = 44100
_EPS = 1e-12


def read_wav(path: str):
    with wave.open(path, "rb") as w:
        ch = w.getnchannels()
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
    x = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    if ch > 1:
        x = x.reshape(-1, ch)
    return x, sr, ch


def _db(v: float) -> float:
    return float(20.0 * np.log10(max(float(v), _EPS)))


def spectral_centroid(mono: np.ndarray, win: int = 4096, hop: int = 2048) -> np.ndarray:
    """フレームごとのスペクトル重心 (Hz)."""
    if len(mono) < win:
        win = max(256, len(mono) // 2 * 2)
        hop = max(128, win // 2)
    w = np.hanning(win)
    freqs = np.fft.rfftfreq(win, 1.0 / SR)
    out = []
    for s in range(0, len(mono) - win + 1, hop):
        seg = mono[s:s + win] * w
        mag = np.abs(np.fft.rfft(seg))
        tot = mag.sum()
        out.append(float((mag * freqs).sum() / tot) if tot > _EPS else 0.0)
    return np.array(out) if out else np.array([0.0])


def rms_envelope(mono: np.ndarray, win: int = 4410) -> np.ndarray:
    if len(mono) < win:
        return np.array([float(np.sqrt(np.mean(mono ** 2)))])
    n = len(mono) // win
    seg = mono[: n * win].reshape(n, win)
    return np.sqrt(np.mean(seg ** 2, axis=1))


# ループ継ぎ目の指標の読み方 (2026-09-21 に実測で確認)
#
# seam_step_ratio / seam_hf_ratio は「継ぎ目が内部と区別できるか」の目安で,
# 1 を下回れば区別できない. ただし **1 を超えても不連続とは限らない**.
# 小節頭に強いアタック (ピアノや鐘) が来る曲では, 指標がそのアタック自体を
# 拾って 1 前後まで上がる.
#
# 判定が要るときは指標でなく周期性を直接確かめる. 同じ編曲を 2 周ぶん描画し,
# ループ長 n として two[n] - two[n-1] を one[0] - one[-1] と比べればよい.
# bgm_title で実測したところ両者が 0.07121 で完全に一致し,
# two[n:2n] は one と最大誤差 0.0 で一致した. すなわち回り込みによる配置は
# サンプル単位で正確に周期的で, 指標が示していたのは拍頭のアタックだった.


def loop_metrics(mono: np.ndarray, win: int = 2048) -> dict:
    """ループ継ぎ目の連続性を数値化する.

    loop_corr      : 末尾 win サンプルと先頭 win サンプルの波形ピアソン相関.
                     同じ波形が繰り返す素材 (環境音ベッド) では高くなるが,
                     音楽では継ぎ目の前後で内容が違うので低くて正常.
    loop_spec_corr : 同じ 2 窓の振幅スペクトルの相関. 継ぎ目の前後で音色 /
                     帯域バランスが揃っているか (0.8 以上なら段差を感じない).
    seam_step_ratio: 継ぎ目の段差 |x[0]-x[-1]| を, 曲中の隣接サンプル差の
                     99.9 パーセンタイルで割った値. 1.0 以下なら段差は
                     通常の波形変化に埋もれておりプチノイズにならない.
    seam_hf_ratio  : 継ぎ目をまたぐ窓の 8 kHz 以上のエネルギーを, 曲中の
                     同じ窓長の 99 パーセンタイルで割った値. 1.0 以下なら
                     継ぎ目でクリックが発生していない.
    """
    n = len(mono)
    win = min(win, n // 4) if n >= 8 else max(1, n)
    tail = mono[n - win:]
    head = mono[:win]
    if np.std(tail) > _EPS and np.std(head) > _EPS:
        corr = float(np.corrcoef(tail, head)[0, 1])
    else:
        corr = 1.0 if (np.std(tail) <= _EPS and np.std(head) <= _EPS) else 0.0

    d = np.abs(np.diff(mono))
    step = abs(float(mono[0]) - float(mono[-1]))
    p999 = float(np.percentile(d, 99.9)) if len(d) else _EPS
    step_ratio = step / max(p999, _EPS)

    half = win // 2
    straddle = np.concatenate([mono[n - half:], mono[:half]])
    w = np.hanning(len(straddle))
    freqs = np.fft.rfftfreq(len(straddle), 1.0 / SR)
    hf = freqs >= 8000.0

    def hf_energy(seg):
        mag = np.abs(np.fft.rfft(seg * w))
        return float((mag[hf] ** 2).sum())

    seam_hf = hf_energy(straddle)
    refs = []
    stride = max(len(straddle), (n - len(straddle)) // 200 or 1)
    for s in range(0, n - len(straddle), stride):
        refs.append(hf_energy(mono[s:s + len(straddle)]))
    ref = float(np.percentile(refs, 99)) if refs else seam_hf
    hf_ratio = seam_hf / max(ref, _EPS)

    wt = np.hanning(len(tail))
    st_tail = np.abs(np.fft.rfft(tail * wt))
    st_head = np.abs(np.fft.rfft(head * wt))
    if np.std(st_tail) > _EPS and np.std(st_head) > _EPS:
        spec_corr = float(np.corrcoef(st_tail, st_head)[0, 1])
    else:
        spec_corr = 0.0

    tail_rms = float(np.sqrt(np.mean(tail ** 2)))
    head_rms = float(np.sqrt(np.mean(head ** 2)))
    return {
        "loop_corr": round(corr, 4),
        "loop_spec_corr": round(spec_corr, 4),
        "seam_step_ratio": round(step_ratio, 4),
        "seam_hf_ratio": round(hf_ratio, 4),
        "edge_rms_db_diff": round(_db(tail_rms) - _db(head_rms), 2),
    }


def analyze(path: str, loop: bool = False) -> dict:
    x, sr, ch = read_wav(path)
    mono = x if x.ndim == 1 else x.mean(axis=1)
    peak = float(np.max(np.abs(x))) if x.size else 0.0
    rms = float(np.sqrt(np.mean(mono ** 2)))
    cent = spectral_centroid(mono)
    env = rms_envelope(mono)
    info = {
        "file": os.path.basename(path),
        "samplerate": sr,
        "channels": ch,
        "duration_sec": round(len(mono) / sr, 3),
        "bytes": os.path.getsize(path),
        "peak_dbfs": round(_db(peak), 2),
        "rms_dbfs": round(_db(rms), 2),
        "clipped_samples": int(np.sum(np.abs(x) >= 0.9999)),
        "dc_offset": round(float(np.mean(mono)), 6),
        "centroid_hz_mean": round(float(np.mean(cent)), 1),
        "centroid_hz_min": round(float(np.min(cent)), 1),
        "centroid_hz_max": round(float(np.max(cent)), 1),
        "rms_env_min_dbfs": round(_db(np.min(env)), 2),
        "rms_env_max_dbfs": round(_db(np.max(env)), 2),
        "loop": bool(loop),
    }
    if loop:
        info.update(loop_metrics(mono))
    return info

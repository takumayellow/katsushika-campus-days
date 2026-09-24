"""MuseScore.com から落とした楽譜動画の音を, きりたんのミックスに差し替える.

    python replace_audio.py <MuseScore.com の動画.mp4> [--offset 秒] [--out 出力.mp4]

MuseScore.com の動画は楽譜を MuseScore の音源で鳴らしている. 冒頭に待ちが入ることがあるので,
動画の元の音とミックス (audio/school_song_kiritan_mix.mp3) の音の立ち上がり (onset) を相互相関で比べ,
ずれ (秒) を自動で求める. 正ならミックスをその分遅らせ, 負なら頭を切る.
--offset を渡すと自動の推定を使わない.
映像は再エンコードしない (-c:v copy). 音は AAC 320 kbps.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
MIX = os.path.join(HERE, "..", "audio", "school_song_kiritan_mix.mp3")
SR = 8000
HOP = 80            # 10 ms
MAX_LAG_SEC = 20.0


def load_mono(path: str) -> np.ndarray:
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-vn", "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
        check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def onset_envelope(x: np.ndarray) -> np.ndarray:
    n = len(x) // HOP
    frames = x[: n * HOP].reshape(n, HOP)
    energy = np.log1p(100.0 * np.sqrt((frames ** 2).mean(axis=1)))
    flux = np.maximum(np.diff(energy, prepend=energy[0]), 0.0)
    return (flux - flux.mean()) / (flux.std() + 1e-9)


def estimate_offset(video: str) -> tuple[float, float]:
    """(ミックスを遅らせる秒, 相関のピークの強さ) を返す."""
    a = onset_envelope(load_mono(video))
    b = onset_envelope(load_mono(MIX))
    max_lag = int(MAX_LAG_SEC * SR / HOP)
    n = 1 << int(np.ceil(np.log2(len(a) + len(b))))
    corr = np.fft.irfft(np.fft.rfft(a, n) * np.conj(np.fft.rfft(b, n)), n)
    lags = np.concatenate([np.arange(0, max_lag + 1), np.arange(-max_lag, 0)])
    vals = np.concatenate([corr[: max_lag + 1], corr[n - max_lag:]])
    i = int(np.argmax(vals))
    strength = float(vals[i] / (np.sqrt((a ** 2).sum() * (b ** 2).sum()) + 1e-9))
    return lags[i] * HOP / SR, strength


def main(argv: list) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("video")
    ap.add_argument("--offset", type=float, help="ミックスを遅らせる秒 (負なら頭を切る). 省くと自動")
    ap.add_argument("--out", default=os.path.join(HERE, "school_song_kiritan.mp4"))
    args = ap.parse_args(argv[1:])

    if args.offset is None:
        offset, strength = estimate_offset(args.video)
        print(f"offset {offset:+.2f} s (相関 {strength:.3f})")
        if strength < 0.2:
            print("相関が弱い. 動画を見てずれを確かめ, --offset で渡し直すこと")
    else:
        offset = args.offset

    if offset >= 0:
        ms = int(round(offset * 1000))
        af = f"adelay={ms}|{ms}"
    else:
        af = f"atrim=start={-offset:.3f},asetpts=PTS-STARTPTS"
    # 映像の長さに合わせる. ミックスが短ければ残りは無音, 長ければ切る
    af += ",apad"
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-i", args.video, "-i", MIX,
         "-map", "0:v:0", "-map", "1:a:0", "-af", af, "-shortest",
         "-c:v", "copy", "-c:a", "aac", "-b:a", "320k", "-movflags", "+faststart", args.out],
        check=True)
    print(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

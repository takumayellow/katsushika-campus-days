"""図書館大ホールのピアノの曲 piano_library.wav を, ショパンの夜想曲 作品 9-2 の録音から切り出す.

    python tools/audio/piano_nocturne.py                     # Wikimedia Commons から取ってきて書き出す
    python tools/audio/piano_nocturne.py --src nocturne.ogg  # 手元に落とした同じファイルから

録音は Musopen の Peter Johnston による演奏 (CC0, Wikimedia Commons). 使うのは冒頭の主題の最初の一節で,
弱起の B♭4 から 4 小節目で E♭ の主和音に落ち着くまで. 5 小節目で主題を繰り返す弱起の B♭4 の手前で切り,
主和音の余韻と左手の八分を末尾のフェードで消す. 区間は波形の立ち上がりを見て決めた (START_SEC / END_SEC).

取ってきたファイルは SOURCE_SHA256 と照合し, 違えば書き出さない. 書き出したあとは
tools/audio/manifest.json と Assets/Audio/README.md を更新する. build_audio.py はこの曲を作らない.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import wave

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import analyze  # noqa: E402
import build_audio  # noqa: E402
import synth as S  # noqa: E402

NAME = "piano_library"
TITLE = "Chopin: Nocturne Op. 9 No. 2 (opening phrase)"
PERFORMER = "Peter Johnston"
LICENSE = "CC0 1.0"
SOURCE_URL = ("https://upload.wikimedia.org/wikipedia/commons/0/04/"
              "Chopin_Nocturne_No._2_in_E_Flat_Major%2C_Op._9.ogg")
SOURCE_PAGE = "https://commons.wikimedia.org/wiki/File:Chopin_Nocturne_No._2_in_E_Flat_Major,_Op._9.ogg"
MUSOPEN_PAGE = "http://musopen.org/music/352/"
SOURCE_SHA256 = "93e15e61baacdf129f21eacf8404ae5c2bd0f41e0256319597de54c40aa1cb22"
USER_AGENT = "KatsushikaCampusDays-audio-tools/1.0 (https://github.com/takumayellow/katsushika-campus-days)"

# 元の録音 (259.24 秒, モノラル 44.1 kHz) の中の区間. 弱起の B♭4 の打鍵は 3.47 秒,
# 4 小節目の E♭ への着地は 28.84 秒, 主題を繰り返す弱起の B♭4 の打鍵は 31.76 秒.
START_SEC = 3.40
END_SEC = 31.70
FADE_IN_SEC = 0.03     # 打鍵の手前の部屋の雑音だけにかかる
FADE_OUT_SEC = 2.5     # 29.20 秒から. 着地した E♭ を 0.36 秒そのまま聞かせてから消す


def n_frames() -> int:
    """書き出す wav のフレーム数."""
    return S.n_samples(END_SEC - START_SEC)


def sha256_of(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def verify(path: str) -> None:
    got = sha256_of(path)
    if got != SOURCE_SHA256:
        raise ValueError(f"{path} の SHA256 が記録と違う ({got}). 別の録音か, 取得に失敗している")


def fetch(dest: str) -> str:
    req = urllib.request.Request(SOURCE_URL, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=120) as resp, open(dest, "wb") as fh:
        shutil.copyfileobj(resp, fh)
    return dest


def decode(src: str, tmp: str) -> np.ndarray:
    """ogg を ffmpeg で 44.1 kHz / 16 bit の wav にして, (n, 2) の float にする."""
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("ffmpeg が見つからない (winget install Gyan.FFmpeg)")
    out = os.path.join(tmp, "source.wav")
    subprocess.run([ffmpeg, "-v", "error", "-y", "-i", src, "-ar", str(S.SR), "-c:a", "pcm_s16le", out],
                   check=True, timeout=600)
    with wave.open(out, "rb") as w:
        ch = w.getnchannels()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768.0
    x = x.reshape(-1, ch)
    if ch == 1:
        x = np.repeat(x, 2, axis=1)
    return x[:, :2]


def cut(x: np.ndarray) -> np.ndarray:
    """START_SEC から n_frames() ぶんを取り, 頭と末尾にフェードをかける."""
    a = S.n_samples(START_SEC)
    y = x[a:a + n_frames()]
    if len(y) != n_frames():
        raise ValueError(f"録音が短い ({len(x) / S.SR:.2f} 秒)")
    return S.fade(y, fin=FADE_IN_SEC, fout=FADE_OUT_SEC)


def install(src: str, tmp: str) -> dict:
    verify(src)
    y = cut(decode(src, tmp))
    out = os.path.join(build_audio.AUDIO_DIR, "SE", NAME + ".wav")
    S.write_wav(out, y, peak_dbfs=build_audio.PEAK_DB["SE"])
    info = analyze.analyze(out, loop=False)
    info.update(
        name=NAME,
        category="SE",
        loop=False,
        path=os.path.relpath(out, build_audio.ROOT).replace("\\", "/"),
        bytes=os.path.getsize(out),
        suggested_volume=build_audio.SUGGESTED_VOLUME["SE"],
        source=f"{TITLE}, performed by {PERFORMER} (Musopen {MUSOPEN_PAGE}) via tools/audio/piano_nocturne.py",
        license=LICENSE,
        source_url=SOURCE_URL,
        source_page=SOURCE_PAGE,
        source_sha256=SOURCE_SHA256,
        source_range_sec=[START_SEC, END_SEC],
        fade_sec=[FADE_IN_SEC, FADE_OUT_SEC],
    )
    print(f"  SE/{NAME}.wav  <- {START_SEC:.2f}-{END_SEC:.2f} s  {info['duration_sec']:.2f}s "
          f"peak {info['peak_dbfs']:+.1f} rms {info['rms_dbfs']:+.1f} dBFS")
    return info


def main(argv: list) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--src", help="取得済みの ogg (SOURCE_SHA256 と一致するもの). 省略すると取ってくる")
    args = ap.parse_args(argv[1:])
    with tempfile.TemporaryDirectory() as tmp:
        src = args.src or fetch(os.path.join(tmp, "nocturne.ogg"))
        info = install(src, tmp)
    # manifest の generated_sec は build_audio.py で全部を作ったときの時間なので, 1 曲だけ書くここでは変えない
    build_audio.write_manifest([info], previous_generated_sec())
    return 0


def previous_generated_sec() -> float:
    try:
        with open(build_audio.MANIFEST, encoding="utf-8") as fh:
            return float(json.load(fh).get("generated_sec", 0.0))
    except (OSError, ValueError):
        return 0.0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

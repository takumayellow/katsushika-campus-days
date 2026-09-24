"""校歌の音源をゲームの BGM (Assets/Audio/BGM) に入れる.

    python tools/audio/school_song/install_game_bgm.py

ファイル名は既存の BGM と同じにして中身だけ差し替えるので, コードとシーンは触らない.
差し替え先は MAP で決める (どこで使うかの検討は Issue #28).
入れた後は tools/audio/manifest.json と Assets/Audio/README.md も更新する.
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_school_song as B  # noqa: E402

S, analyze, build_audio = B.S, B.analyze, B.build_audio
BGM_DIR = B.GAME_BGM_DIR

# ゲーム内の BGM 名 -> (音源の候補 (先にあるものを使う), 説明, ffmpeg フィルタ)
# ピアノだけの伴奏はアタックのピークが大きく, ピーク基準だと他の BGM より 7 dB ほど小さい. リミッタで持ち上げる
BOOST = "volume=13dB,alimiter=limit=0.891:attack=5:release=80:level=false"
MAP = {
    "bgm_school_song": (("work/mix.wav", "audio/school_song_kiritan_mix.mp3"),
                        "タイトル. きりたん歌唱 1〜3 番 + ヤマハ採譜の伴奏 (mix.sh)", None),
    # 吹奏楽版 (audio/bgm_band.wav) は旋律・和音が公式譜とずれていて, 直すまで使わない (#28)
    "bgm_day": (("work/bgm_yamaha.wav", "audio/bgm_yamaha.mp3"),
                "キャンパス昼 (ゲーム内の基本). ヤマハ採譜のピアノ伴奏 (歌なし). 吹奏楽版が直ったら差し替える", BOOST),
    "bgm_evening": (("work/bgm_yamaha.wav", "audio/bgm_yamaha.mp3"),
                    "キャンパス夕方. ヤマハ採譜のピアノ伴奏 (歌なし)", BOOST),
}


def pick(cands: tuple) -> str:
    for c in cands:
        p = os.path.join(HERE, c)
        if os.path.exists(p):
            return p
    raise FileNotFoundError(cands)


def to_wav(src: str, tmp: str, af: str | None) -> str:
    """read_wav_float は 16/24bit PCM しか読めないので, mp3 や float wav を ffmpeg で 16bit wav にする."""
    out = os.path.join(tmp, os.path.basename(src) + ".wav")
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-i", src]
    if af:
        cmd += ["-af", af]
    subprocess.run(cmd + ["-c:a", "pcm_s16le", out], check=True)
    return out


def install(name: str, src: str, note: str, af: str | None, tmp: str) -> dict:
    x, sr = B.read_wav_float(to_wav(src, tmp, af))
    if x.ndim == 1:
        x = x[:, None].repeat(2, axis=1)
    x = B.resample(x, sr, S.SR)
    x = B.trim_silence(x, S.SR)
    x = S.fade(x, fin=0.01, fout=1.0)
    out = os.path.join(BGM_DIR, name + ".wav")
    S.write_wav(out, x, peak_dbfs=build_audio.PEAK_DB["BGM"])
    info = analyze.analyze(out, loop=False)
    info.update(
        name=name,
        category="BGM",
        loop=False,
        path=os.path.relpath(out, B.ROOT).replace("\\", "/"),
        bytes=os.path.getsize(out),
        suggested_volume=build_audio.SUGGESTED_VOLUME["BGM"],
        source=f"tools/audio/school_song/{os.path.relpath(src, HERE).replace(os.sep, '/')} ({note})",
    )
    print(f"  BGM/{name}.wav  <- {os.path.relpath(src, HERE)}  {info['duration_sec']:.2f}s "
          f"peak {info['peak_dbfs']:+.1f} rms {info['rms_dbfs']:+.1f} dBFS")
    return info


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    t0 = time.time()
    with tempfile.TemporaryDirectory() as tmp:
        infos = [install(n, pick(c), note, af, tmp) for n, (c, note, af) in MAP.items()]
    build_audio.write_manifest(infos, time.time() - t0)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

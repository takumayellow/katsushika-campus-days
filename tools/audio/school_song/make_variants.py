"""校歌のピアノ伴奏から夜 / 屋内の BGM を作る (#28).

    python tools/audio/school_song/make_variants.py            # audio/bgm_yamaha.mp3 (work/bgm_yamaha.wav があればそちら) から
    python tools/audio/school_song/make_variants.py --render   # score/bgm_yamaha.musicxml を MuseScore 4 で鳴らし直してから

昼 / 夕方 (bgm_day, bgm_evening) と同じ演奏を, テンポとフィルタで場面に合わせる.
  bgm_night  : 0.8 倍のテンポ, 2.4 kHz ローパス, -4 dB. 夜の余韻
  bgm_indoor : 0.92 倍のテンポ, 3.2 kHz ローパス, 短い残響, -3 dB. 建物の中
レベルは install_game_bgm.py と同じ BOOST (+13 dB + リミッタ) で揃える.
ファイル名は既存の BGM と同じなので, コードとシーンは触らない. 元の曲 (music.py) に戻すときは
build_audio.py の SCHOOL_SONG_BGM から外して build_audio.py bgm を回す.
"""
from __future__ import annotations

import argparse
import os
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_school_song as B  # noqa: E402
import install_game_bgm as I  # noqa: E402

SOURCES = ("work/bgm_yamaha.wav", "audio/bgm_yamaha.mp3")
SCORE = os.path.join(HERE, "score", "bgm_yamaha.musicxml")

# ゲーム内の BGM 名 -> (説明, ffmpeg フィルタ). BOOST は最後に掛ける
VARIANTS = {
    "bgm_night": ("夜. 校歌のピアノ伴奏を 0.8 倍のテンポにして 2.4 kHz から上を落とし -4 dB",
                  "atempo=0.8,lowpass=f=2400,volume=-4dB," + I.BOOST),
    "bgm_indoor": ("屋内. 校歌のピアノ伴奏を 0.92 倍のテンポにして 3.2 kHz から上を落とし, 短い残響, -3 dB",
                   "atempo=0.92,lowpass=f=3200,aecho=0.8:0.7:60:0.25,volume=-3dB," + I.BOOST),
}


def source(render: bool, tmp: str) -> str:
    if render:
        exe = B.find_musescore()
        if exe:
            raw = os.path.join(tmp, "bgm_yamaha_render.wav")
            B.render_wav(SCORE, raw, exe)
            return raw
        print("MuseScore 4 が見つからないので音源ファイルを使う", file=sys.stderr)
    return I.pick(SOURCES)


def main(argv: list) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--render", action="store_true", help="MuseScore 4 で譜面から鳴らし直す")
    args = parser.parse_args(argv[1:])
    t0 = time.time()
    with tempfile.TemporaryDirectory() as tmp:
        src = source(args.render, tmp)
        infos = [I.install(name, src, note, af, tmp) for name, (note, af) in VARIANTS.items()]
    B.build_audio.write_manifest(infos, time.time() - t0)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

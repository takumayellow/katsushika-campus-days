#!/bin/bash
# きりたん歌唱 (audio/vocal_kiritan_full.wav) と BGM を重ねて audio/school_song_kiritan_mix.mp3 を作る.
#   BGM: work/bgm_yamaha.wav があればそれを, 無ければ audio/bgm_yamaha.mp3 を使う
#   BGM は元が小さい (-25.7 LUFS) ので +8 dB 持ち上げ, 最終段のリミッターで -1 dB に抑える
set -e
cd "$(dirname "$0")"
BGM=work/bgm_yamaha.wav
[ -f "$BGM" ] || BGM=audio/bgm_yamaha.mp3
BGM_GAIN=${BGM_GAIN:-8dB}
ffmpeg -y -loglevel error -i "$BGM" -i audio/vocal_kiritan_full.wav -filter_complex \
  "[0:a]volume=${BGM_GAIN}[b];[1:a]aresample=44100,pan=stereo|c0=c0|c1=c0[v];[b][v]amix=inputs=2:normalize=0:duration=longest,alimiter=limit=0.891:attack=5:release=80:level=false" \
  -c:a pcm_s16le work/mix.wav
ffmpeg -y -loglevel error -i work/mix.wav -b:a 256k audio/school_song_kiritan_mix.mp3
echo "-> audio/school_song_kiritan_mix.mp3 (wav: work/mix.wav)"

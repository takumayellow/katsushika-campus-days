#!/bin/bash
# 東京理科大学校歌 (katsushika-campus-days) 用.
# 譜面は katsushika リポジトリの out/ を正本として読み込み, 歌唱 wav も同じ out/ へ直接書き出す.
cd `dirname $0`

# Project settings
BASENAME=school_song_vocal
REPO_OUT=/mnt/c/Users/takum/dev/katsushika-campus-days/tools/audio/school_song/out
OUT_WAV=${REPO_OUT}/${OUT_NAME:-school_song_kiritan_full}.wav

# musicXML_to_label
SUFFIX=musicxml

# neutrino
ModelDir=KIRITAN
NumThreads=4
Transpose=0

# PATH to current library
export LD_LIBRARY_PATH=$PWD/bin:$PWD/NSF/bin:$LD_LIBRARY_PATH

set -e
cp ${REPO_OUT}/${BASENAME}.${SUFFIX} score/musicxml/${BASENAME}.${SUFFIX}

echo "`date +"%M:%S.%2N"` : start MusicXMLtoLabel"
bin/musicXMLtoLabel score/musicxml/${BASENAME}.${SUFFIX} score/label/full/${BASENAME}.lab score/label/mono/${BASENAME}.lab

echo "`date +"%M:%S.%2N"` : start NEUTRINO"
bin/neutrino score/label/full/${BASENAME}.lab score/label/timing/${BASENAME}.lab output/${BASENAME}.f0 output/${BASENAME}.melspec output/${BASENAME}.wav ./model/${ModelDir}/ -n ${NumThreads} -f ${Transpose} -m -t

cp output/${BASENAME}.wav ${OUT_WAV}
echo "`date +"%M:%S.%2N"` : END -> ${OUT_WAV}"

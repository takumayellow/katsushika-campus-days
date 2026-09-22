# 東京理科大学校歌 — 音源と譜面

経緯と権利のメモは `../README_school_song.md`, 作業の記録は Issue #34, ゲームのどこで流すかは Issue #28。

## ゲームに入っているもの

`python tools/audio/school_song/install_game_bgm.py` が `Assets/Audio/BGM/` に書き込む（ファイル名は既存のまま中身だけ差し替え）。

| ゲームの BGM | 場面 | 元のファイル |
|---|---|---|
| `bgm_school_song.wav` | タイトル画面 | `audio/school_song_kiritan_mix.mp3`（`work/mix.wav` があればそちら） |
| `bgm_day.wav` | キャンパス昼（基本） | `audio/bgm_band.wav` |
| `bgm_evening.wav` | キャンパス夕方 | `audio/bgm_yamaha.mp3`（`work/bgm_yamaha.wav` があればそちら）。+13 dB してリミッタをかける |

割り当ては `install_game_bgm.py` の `MAP`。`build_audio.py` はこの 2 曲（`SCHOOL_SONG_BGM`）を合成しない。

## ファイル

| パス | 内容 |
|---|---|
| `score/school_song_vocal.musicxml` | **きりたん用メインボーカルの正本**。1〜3 番, 91 小節, F major 4/4, ♩=117。BGM と同じ拍に乗る |
| `score/school_song_vocal_verse1.mscz` | 1 番だけの MuseScore 原本（手で仕上げたもの） |
| `score/bgm_yamaha.musicxml` | ピアノ伴奏の譜面。ヤマハの自動採譜で, 小節 29 の余分な 1 拍を削った |
| `score/bgm_yamaha_raw.xml` | 同じ採譜の生の出力（Piano Sheet Converter） |
| `score/school_song_band.musicxml` / `.mid` | 吹奏楽風 5 声部（`build_school_song.py` が生成） |
| `score/school_song_vocal_draft.musicxml` | `build_school_song.py` が出す 1 番だけの下書き。正本は上書きしない |
| `audio/vocal_kiritan_full.wav` | きりたん歌唱 1〜3 番（NEUTRINO, 48 kHz mono, 186.67 s） |
| `audio/vocal_kiritan_verse1.wav` | 1 番の完成版（最初に手で仕上げたもの） |
| `audio/bgm_yamaha.mp3` | ピアノ伴奏（小節 29 の 1 拍, 61.030〜61.543 s を切った版, 189.67 s） |
| `audio/school_song_kiritan_mix.mp3` | **完成版**。歌 + 伴奏（BGM +8 dB, -11.3 LUFS, ピーク -0.8 dBFS）。動画の音声もこれ |
| `audio/bgm_band.wav` | 吹奏楽風アレンジの WAV（`build_school_song.py` の出力） |
| `work/` | git に入れない作業用（`mix.wav`, `bgm_yamaha.wav`, 修正前の `bgm_yamaha.orig.wav`, 旧ファイル） |

## 作り直すとき

```sh
# 歌を作り直す (WSL の NEUTRINO. GPU で約 1 分). score/school_song_vocal.musicxml -> audio/vocal_kiritan_full.wav
wsl bash -lc "cd /mnt/c/Users/takum/dev/kiritan/tools/neutrino/NEUTRINO && bash Run_tus.sh"
# 伴奏と混ぜる. BGM_GAIN=6dB などで伴奏の音量を変えられる
bash tools/audio/school_song/mix.sh
# ゲームへ入れる
python tools/audio/school_song/install_game_bgm.py
# 吹奏楽版を作り直す (MuseScore 4 が要る). 最後に install_game_bgm.py も走る
python tools/audio/school_song/build_school_song.py
```

`Run_tus.sh` は NEUTRINO フォルダにも同じものを置いて使う（このリポジトリのものが原本）。

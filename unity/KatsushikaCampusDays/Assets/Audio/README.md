# Audio

ゲーム内の音はすべて **Python と numpy だけで合成**している。
サンプリング素材・外部ライブラリの音源・録音物は一切使っていない。

## 生成方法

```
python tools/audio/build_audio.py              # 全部作り直す
python tools/audio/build_audio.py bgm ambient  # カテゴリを絞る
```

| スクリプト | 役割 |
|---|---|
| `tools/audio/synth.py` | 発振器・フィルタ・エンベロープ・楽器・ドラム・リバーブ |
| `tools/audio/music.py` | BGM とジングル (小節グリッドの簡易シーケンサ) |
| `tools/audio/sfx.py` | SE 37 種 |
| `tools/audio/ambient.py` | 環境音 7 種 |
| `tools/audio/analyze.py` | 波形統計とループ継ぎ目の計測 |
| `tools/audio/build_audio.py` | 全体のエントリ。`manifest.json` と本 README を書き出す |

出力は 44.1 kHz / 16 bit の WAV。合計 54 ファイル, 169.6 MiB, 音の長さ 1013.8 秒。生成時間は 17 秒。

## Unity へ取り込むときの設定

| 項目 | 値 |
|---|---|
| Load Type | BGM / Ambient は Streaming, SE は Decompress On Load |
| Compression Format | Vorbis (WAV のままだと 100 MiB 超) |
| Quality | BGM 70 / Ambient 50 / SE 100 |
| Loop | BGM と Ambient は `AudioSource.loop = true` |

`tools/audio/manifest.json` に 1 ファイルずつ長さ・ピーク・RMS・ループ可否・推奨音量が入っている。ミキサーの初期値はそこから読む。

推奨音量 (`AudioSource.volume` の初期値): BGM 0.55, SE 0.8, Ambient 0.35。

## ループの継ぎ目について

ループ素材は次の 4 つでサンプル単位の連続性を作っている。

1. 音符や環境音イベントは末尾を越えた分を先頭へ回り込ませて置く (`add_at(wrap=True)`)
2. リバーブとフィルタは末尾を助走として前置してから掛け, 助走分を捨てる (`preroll`)
3. 環境音のベッドは逆 FFT で作るのでループ長でちょうど 1 周する (`spectral_noise`)
4. ドローンと LFO はループ長に整数周期が入る周波数へ丸める (`pfreq` / `periodic_lfo`)

README の表の「ループ継ぎ目」は `seam_step_ratio / seam_hf_ratio` で, どちらも 1 を下回れば継ぎ目が内部と区別できないことを意味する。

**`bgm_title` はこの 2 つが 1 前後になるが, 不連続ではない。** 同じ編曲を 2 周ぶん描画して内部の継ぎ目と比較したところ, 回り込みの段差 0.07121 と 2 周描画の内部の段差 0.07121 が完全に一致し, 2 周目の波形は 1 周ぶんの出力と最大誤差 0.0 で一致した。指標が拾っているのは小節頭のピアノのアタックそのもので, 継ぎ目の欠陥ではない。

クリップしたサンプルは全 54 ファイルで 0 個。

## BGM (10 ファイル)

| ファイル | 長さ | ピーク | RMS | ループ継ぎ目 | 用途 |
|---|---:|---:|---:|---:|---|
| `bgm_anthem_original.wav` | 71.11 s | -1.0 dBFS | -12.5 dBFS | 0.733 / 0.158 | オリジナルの校歌風行進曲 (未使用). 108 BPM / Bb major / A-B 形式 |
| `bgm_day.wav` | 130.52 s | -1.0 dBFS | -14.9 dBFS | ループしない | 昼のキャンパス (ゲーム内の基本). 校歌の吹奏楽風アレンジ. 104 BPM / F major / 前奏 + 2 コーラス |
| `bgm_evening.wav` | 189.35 s | -1.0 dBFS | -16.7 dBFS | ループしない | 夕方. 校歌のピアノ伴奏 (ヤマハ自動採譜, 歌なし). 117 BPM / F major / 1〜3 番 |
| `bgm_indoor.wav` | 80.00 s | -1.0 dBFS | -11.9 dBFS | 0.104 / 0.063 | 屋内. 96 BPM / C major / ジャズ 7th の lo-fi |
| `bgm_night.wav` | 80.00 s | -1.0 dBFS | -15.4 dBFS | 0.028 / 1.052 | 夜の余韻. 72 BPM / D minor / 静かなピアノ + パッド |
| `bgm_result.wav` | 30.00 s | -1.0 dBFS | -11.0 dBFS | 0.299 / 0.620 | リザルト画面. 128 BPM / C major / 明るく短い |
| `bgm_school_song.wav` | 189.06 s | -1.0 dBFS | -15.4 dBFS | ループしない | タイトル画面. 東京理科大学校歌. 東北きりたん (NEUTRINO) 歌唱 1〜3 番 + ピアノ伴奏. 117 BPM / F major |
| `bgm_title.wav` | 40.00 s | -1.0 dBFS | -13.7 dBFS | 0.999 / 1.484 | 旧タイトル曲 (未使用). 72 BPM / bgm_day のモチーフを遅く |
| `jingle_day_end.wav` | 4.00 s | -1.0 dBFS | -11.2 dBFS | ループしない | 1 日の終わり |
| `jingle_quest.wav` | 2.00 s | -1.0 dBFS | -12.7 dBFS | ループしない | クエスト達成 |

## SE (効果音) (37 ファイル)

| ファイル | 長さ | ピーク | RMS | 用途 |
|---|---:|---:|---:|---|
| `camera_shutter.wav` | 0.40 s | -1.5 dBFS | -28.5 dBFS | 撮影 |
| `door_close.wav` | 0.55 s | -1.5 dBFS | -23.8 dBFS | 扉を閉める |
| `door_open.wav` | 0.95 s | -1.5 dBFS | -22.4 dBFS | 扉を開ける |
| `item_get.wav` | 0.75 s | -1.5 dBFS | -13.4 dBFS | アイテム入手 |
| `jump.wav` | 0.28 s | -1.5 dBFS | -13.8 dBFS | ジャンプ |
| `land.wav` | 0.30 s | -1.5 dBFS | -21.8 dBFS | 着地 |
| `quest_start.wav` | 1.15 s | -1.5 dBFS | -13.7 dBFS | クエスト開始 |
| `quest_update.wav` | 0.55 s | -1.5 dBFS | -12.3 dBFS | クエスト進行 |
| `se_chime.wav` | 20.08 s | -1.5 dBFS | -15.2 dBFS | 時報チャイム (9:00 / 12:00 / 17:00). ウェストミンスターの鐘 |
| `sit.wav` | 0.55 s | -1.5 dBFS | -19.9 dBFS | 座る |
| `step_concrete_1.wav` | 0.17 s | -1.5 dBFS | -19.5 dBFS | コンクリートの足音. 4 バリエーションをランダムに再生する |
| `step_concrete_2.wav` | 0.16 s | -1.5 dBFS | -22.4 dBFS | コンクリートの足音. 4 バリエーションをランダムに再生する |
| `step_concrete_3.wav` | 0.16 s | -1.5 dBFS | -19.9 dBFS | コンクリートの足音. 4 バリエーションをランダムに再生する |
| `step_concrete_4.wav` | 0.15 s | -1.5 dBFS | -20.0 dBFS | コンクリートの足音. 4 バリエーションをランダムに再生する |
| `step_grass_1.wav` | 0.24 s | -1.5 dBFS | -21.6 dBFS | 芝生の足音. 4 バリエーションをランダムに再生する |
| `step_grass_2.wav` | 0.23 s | -1.5 dBFS | -21.5 dBFS | 芝生の足音. 4 バリエーションをランダムに再生する |
| `step_grass_3.wav` | 0.21 s | -1.5 dBFS | -21.9 dBFS | 芝生の足音. 4 バリエーションをランダムに再生する |
| `step_grass_4.wav` | 0.20 s | -1.5 dBFS | -22.7 dBFS | 芝生の足音. 4 バリエーションをランダムに再生する |
| `step_tile_1.wav` | 0.20 s | -1.5 dBFS | -23.8 dBFS | タイルの足音. 4 バリエーションをランダムに再生する |
| `step_tile_2.wav` | 0.18 s | -1.5 dBFS | -21.7 dBFS | タイルの足音. 4 バリエーションをランダムに再生する |
| `step_tile_3.wav` | 0.18 s | -1.5 dBFS | -23.4 dBFS | タイルの足音. 4 バリエーションをランダムに再生する |
| `step_tile_4.wav` | 0.17 s | -1.5 dBFS | -23.2 dBFS | タイルの足音. 4 バリエーションをランダムに再生する |
| `step_wood_1.wav` | 0.30 s | -1.5 dBFS | -17.0 dBFS | 木の床の足音. 4 バリエーションをランダムに再生する |
| `step_wood_2.wav` | 0.29 s | -1.5 dBFS | -19.4 dBFS | 木の床の足音. 4 バリエーションをランダムに再生する |
| `step_wood_3.wav` | 0.27 s | -1.5 dBFS | -19.2 dBFS | 木の床の足音. 4 バリエーションをランダムに再生する |
| `step_wood_4.wav` | 0.26 s | -1.5 dBFS | -18.3 dBFS | 木の床の足音. 4 バリエーションをランダムに再生する |
| `talk_blip_f1.wav` | 0.07 s | -1.5 dBFS | -12.8 dBFS | 会話ブリップ (女子 1) |
| `talk_blip_f2.wav` | 0.06 s | -1.5 dBFS | -12.5 dBFS | 会話ブリップ (女子 2) |
| `talk_blip_m1.wav` | 0.07 s | -1.5 dBFS | -13.3 dBFS | 会話ブリップ (男子) |
| `talk_blip_prof.wav` | 0.08 s | -1.5 dBFS | -13.5 dBFS | 会話ブリップ (教員) |
| `ui_cancel.wav` | 0.30 s | -1.5 dBFS | -13.1 dBFS | キャンセル |
| `ui_close.wav` | 0.28 s | -1.5 dBFS | -15.8 dBFS | ウィンドウを閉じる |
| `ui_confirm.wav` | 0.42 s | -1.5 dBFS | -12.1 dBFS | 決定 |
| `ui_move.wav` | 0.07 s | -1.5 dBFS | -14.0 dBFS | カーソル移動 |
| `ui_open.wav` | 0.34 s | -1.5 dBFS | -15.0 dBFS | ウィンドウを開く |
| `ui_toast.wav` | 0.50 s | -1.5 dBFS | -12.2 dBFS | 通知 |
| `wave.wav` | 0.60 s | -1.5 dBFS | -17.1 dBFS | 手を振る |

## 環境音 (ループ) (7 ファイル)

| ファイル | 長さ | ピーク | RMS | ループ継ぎ目 | 用途 |
|---|---:|---:|---:|---:|---|
| `amb_cafe.wav` | 22.00 s | -3.0 dBFS | -20.2 dBFS | 0.055 / 0.001 | カフェ: ざわめき + 食器 |
| `amb_cafeteria.wav` | 22.00 s | -3.0 dBFS | -19.7 dBFS | 0.227 / 0.001 | 食堂: 賑わい |
| `amb_campus_day.wav` | 24.00 s | -3.0 dBFS | -22.6 dBFS | 0.057 / 0.012 | 昼のキャンパス: 風 + 小鳥 + 遠くのざわめき |
| `amb_campus_evening.wav` | 24.00 s | -3.0 dBFS | -24.8 dBFS | 0.048 / 0.071 | 夕暮れ: ヒグラシ + 風 |
| `amb_greenhouse.wav` | 24.00 s | -3.0 dBFS | -19.6 dBFS | 0.020 / 0.329 | 温室: 換気扇 + 水滴 |
| `amb_gym.wav` | 24.00 s | -3.0 dBFS | -20.9 dBFS | 0.008 / 0.973 | 体育館: 残響のあるボールのバウンド |
| `amb_library.wav` | 26.00 s | -3.0 dBFS | -19.1 dBFS | 0.004 / 0.005 | 図書館: 空調のほぼ無音 + ページ |

## 校歌について

東京理科大学校歌 (作曲 大和憲史) を 3 つの BGM で使っている。タイトル画面の `bgm_school_song.wav` は東北きりたん (NEUTRINO) の歌唱 1〜3 番にピアノ伴奏を重ねたもの, 昼の `bgm_day.wav` は管弦楽版スコアの旋律を `build_school_song.py` に書き起こして MuseScore 4 で演奏させた吹奏楽風アレンジ, 夕方の `bgm_evening.wav` は歌なしのピアノ伴奏 (ヤマハの自動採譜を修正したもの)。3 つとも `tools/audio/school_song/install_game_bgm.py` が書き込むので, `build_audio.py` はこの 2 曲 (`bgm_day`, `bgm_evening`) を生成しない。`music.py` の元の曲は `SCHOOL_SONG_BGM` から外すと戻る。作曲者の没年が確認できず保護期間の満了は立証できていないので, 公開配布の前に権利確認が要る。経緯と出典は `tools/audio/README_school_song.md`, ファイルの一覧は `tools/audio/school_song/README.md` を参照。`bgm_anthem_original.wav` は権利確認が取れなかった場合の差し替え用に残してある。

時報チャイム `se_chime.wav` のウェストミンスターの鐘は 1793 年の伝承曲で, パブリックドメインであることを確認したうえで音高から合成している。

# 東京理科大学 校歌 — 再現の経緯と権利メモ

再現日: 2026-09-21（2026-09-21 早朝の調査で一度「収録見送り」としたが, 同日ユーザーの指示で再現した）

## 1. 何を作ったか

| ファイル | 内容 |
|----------|------|
| `unity/.../Assets/Audio/BGM/bgm_day.wav` | 昼のキャンパスの BGM（2026-09-22 まではタイトル曲）。校歌の吹奏楽風アレンジ（前奏 + 1 番 + 2 番の器楽, 131 秒, 104 BPM, F major）。原本は `school_song/audio/bgm_band.wav` |
| `unity/.../Assets/Audio/BGM/bgm_school_song.wav` | タイトル画面の BGM。きりたん歌唱の 1〜3 番 + ピアノ伴奏（189 秒, 117 BPM）。`AudioManager.Scene.cs` がタイトルで `bgm_school_song` を鳴らす |
| `tools/audio/school_song/build_school_song.py` | 旋律・和音・編曲の定義と生成スクリプト。music21 で譜面を組み, MuseScore 4 CLI で OGG に演奏させ, ffmpeg で 44.1 kHz WAV にして -1 dBFS に正規化する |
| `tools/audio/school_song/score/school_song_vocal.musicxml` | 歌の旋律 1 声部 + 1〜3 番の歌詞。きりたん（NEUTRINO）に渡す正本。ファイルの一覧は `school_song/README.md` |
| `tools/audio/school_song/score/school_song_band.musicxml` / `.mid` | 器楽 5 声部（Flute / Trumpet / Horn / Piano / Tuba）の譜面 |
| `tools/audio/school_song/KIRITAN_HANDOFF.md` | 歌唱版を作るペインへの依頼文と歌詞 3 番分 |

再生成: `python tools/audio/school_song/build_school_song.py`（MuseScore 4 と ffmpeg が要る。`--no-wav` で譜面だけ）。最後に `install_game_bgm.py` を呼んでゲームの BGM に入れる。

`bgm_anthem_original.wav`（オリジナルの校歌風行進曲, `music.build_anthem_original()`）と `bgm_title.wav` は
差し替え用に残してあり, コードからは参照していない。

## 2. 出典と転写の方法

- 旋律: なかじま音楽工房が公開している二管編成管弦楽版スコア（©2020 M.Nakajima, 23 ページ, F major 4/4, Allegro ♩=108）。
  総譜の Perc. と Vln. I の間にある無名の五線が歌の旋律（ボーカルキュー）で, 5 小節目のアウフタクトから 30 小節目まで
  ここから読んだ。6〜21 小節は Violin I とも一致する。前奏（1〜5 小節）は Violin I / Flute から。
- 歌詞: 大学の式次第 PDF（作詞 佐治巌 / 作曲 大和憲史, 3 番まで）。楽譜の歌詞割りは公式サイトの楽譜画像で確認した。
- テンポ: 公式サイトの MP3（203.2 秒）を 22.05 kHz mono に落として自己相関でテンポを取ると ≈103〜104 BPM。
  スコアの Allegro ♩=108 と整合し（公式楽譜画像の Andante ではない）, 3 番構成（5 + 25×3 + 終止 ≈ 81 小節）で
  長さも合う。BGM は ♩=104 を採った。
- 和音: スコアの低音と内声から小節ごとに F / Bb / C7 / Gm を割り当てた（`CHORDS`）。編曲は本ツールのもので, 原編曲は写していない。

音源は 1 音ずつ `build_school_song.py` の `INTRO` / `VERSE` に書いてあるので, 修正はそこを直して再生成する。

## 3. 権利について（配布前に要確認）

| 項目 | 内容 |
|------|------|
| 作曲者 | 大和憲史 |
| 没年 | **確認できず**（Web 検索では人物を特定できる典拠に到達しなかった） |
| 保護期間 | 著作者の死後 70 年（著作権法 51 条 2 項）。1967 年末までに死亡なら満了, 1968 年以降なら死後 70 年まで保護 |
| 満了の立証 | **不可** |

- 旋律を再現している以上, 保護期間内であれば **複製権・翻案権の対象**。大学の教職員・学生が学内で使う分には
  慣行として黙認されることが多いが, GitHub Pages で不特定多数へ配信するのは「公衆送信」に当たる。
- 歌詞は BGM には入っていない（インストのみ）。歌唱版を作って配布する場合は歌詞（作詞 佐治巌）も同じ確認が要る。
- 管弦楽版スコアの編曲そのものは写していない（和音の割り当てとオーケストレーションは本ツール独自）。
- 公開を続けるなら, 大学（広報課）へ校歌の使用可否を問い合わせるか, 没年を国立国会図書館典拠・『東京理科大学百年史』等で
  確認する。確認が取れるまでは `bgm_anthem_original` に戻す選択肢を残してある（`AudioManager.Scene.cs` の 1 行）。

## 4. 参考

- [大学のシンボル｜ABOUT TUS｜東京理科大学](https://www.tus.ac.jp/about/university/symbol/)（MP3 と楽譜画像）
- [2022 年度 学位記・修了証書授与式 式次第 (PDF)](https://www.tus.ac.jp/tuslife/campuslife/event/shikishidai_2022.pdf)（歌詞）
- [著作物等の保護期間の延長に関する Q&A｜文化庁](https://www.bunka.go.jp/seisaku/chosakuken/hokaisei/kantaiheiyo_chosakuken/1411890.html)

## 5. ウェストミンスターの鐘について（`se_chime.wav`）

チャイムの旋律は 1793 年にケンブリッジの Great St Mary 教会のために作られた伝承曲で,
ヘンデル『メサイア』（1741 年）の一節に由来するとされる。作者の没後 200 年以上が
経過しておりパブリックドメイン。音高は E major の 4 音（B3, E4, F#4, G#4）を
4 フレーズに並べた正時の形で, 実装は `tools/audio/sfx.py` の `CHIME_PHRASES`。

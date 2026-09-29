# クレジット・権利表

ゲームに入っている素材と、このリポジトリで公開している参考資料の出所と権利をまとめる。
ゲーム内のクレジット画面（タイトル →「クレジット」、`CreditsView.BuildText`）はこの表から要点を抜いたもので、
EditMode テスト `CreditsTextTests` が、両方に同じ素材の名前が出ていることと、フォントのライセンス本文がビルドに入ることを確かめる。

確認状況の書き方:

- 確認済み: リポジトリ内の実物（ライセンスファイル、フォントのメタデータ、README、生成スクリプトとその出力）で確かめた。
- 未確認: 根拠がリポジトリに無く、確かめられていない。配布を続ける前に確かめる項目は末尾の「配布前に確かめること」に並べる。

本作は東京理科大学とは無関係の、非公式・非営利のファン作品です。大学の公式見解を示すものではありません。

## 地図データ

| 素材 | 出所・権利者 | 根拠 | 条件・表記 | 確認状況 |
|---|---|---|---|---|
| キャンパスの建物の形・通路・緑地（`data/osm/campus.json`） | © OpenStreetMap contributors | `tools/osm_extract.py` が出力に書く `"source": "OpenStreetMap (ODbL) via Overpass API, 2026-09-20"` | Open Database License (ODbL) 1.0。「© OpenStreetMap contributors」と https://www.openstreetmap.org/copyright を表示する | 確認済み |
| 学生寮までの道と沿道の建物（`data/osm/route.json`） | © OpenStreetMap contributors | `tools/osm_route.py` の `"source": "OpenStreetMap (ODbL) via Overpass API, 2026-09-23 (timestamp_osm_base 2026-09-22T08:45:51Z)"` | 同上 | 確認済み |
| キャンパスの建物の階数・高さ | 階数は TUS LIFE と Wikipedia、高さは OSM の値を優先し、無ければ階数 × 3.9 m | `tools/osm_extract.py` の `BUILDING_CATALOG` とそのコメント | OSM の値は ODbL | 階数の典拠の URL と参照日は記録なし |
| 沿道の建物の高さ | OSM の `height`、無ければ `building:levels` × 3.2 m、どちらも無ければ 8 m | `tools/osm_route.py` | ODbL。国土交通省 PLATEAU から OSM に取り込まれた値なら、PLATEAU の利用規約に沿った表記も要る | 取得した生データ（`raw_route.json`）は git に入っておらず `source` タグも残していないので、PLATEAU 由来かは未確認 |
| ミニマップ（`Assets/Textures/minimap.png`） | 本作の生成物。上の地図データから作った `campus.fbx` と `trees.fbx` を真上から描いたもの | `tools/render_minimap.py` | 形は OSM 由来なので ODbL の表示に含める | 確認済み |

ゲーム内の表示: クレジット画面の先頭に「地図データ © OpenStreetMap contributors (ODbL)」と https://www.openstreetmap.org/copyright。

## 音楽・音声

| 素材 | 出所・権利者 | 根拠 | 条件・表記 | 確認状況 |
|---|---|---|---|---|
| タイトルの BGM `bgm_school_song.wav`（189.07 秒） | 東京理科大学校歌（作詞 佐治巌 / 作曲 大和憲史）の 1〜3 番を、東北きりたん（NEUTRINO）の歌声合成で歌わせ、下のピアノ伴奏を重ねたもの | `tools/audio/README_school_song.md`、`tools/audio/school_song/README.md`、`AudioManager.Scene.cs`（タイトルで `bgm_school_song` を鳴らす） | 歌詞・旋律の著作権: 作曲者の没年を確認できず、保護期間（死後 70 年）の満了を立証できない。作詞者も同じ確認が要る。NEUTRINO と東北きりたんの利用規約、クレジット表記の指定はリポジトリに無い | 未確認 |
| キャンパスの BGM `bgm_day` / `bgm_evening`（189.35 秒）、`bgm_night`（235.94 秒）、`bgm_indoor`（205.31 秒） | 校歌のピアノ伴奏。ヤマハの自動採譜（Piano Sheet Converter）の出力を手直しした譜面を鳴らしたもの。夜は 0.8 倍、屋内は 0.92 倍のテンポにフィルタをかけた版 | `score/bgm_yamaha_raw.xml` の `<creator type="composer">Created by Piano Sheet Converter</creator>` と `encoding-date` 2026-09-21。「ヤマハ」は `tools/audio/school_song/README.md` の記述。テンポ違いは `make_variants.py` | 校歌の権利は上と同じ。採譜に入れた元の音源、採譜サービスの利用規約、`audio/bgm_yamaha.mp3` をどの音源ソフトで鳴らしたかは記録なし | 未確認 |
| 校歌の旋律・歌詞の転写元（ゲームには入らない） | 旋律: なかじま音楽工房の管弦楽版スコア（©2020 M.Nakajima）。歌詞: 大学の式次第 PDF。テンポ: 大学公式サイトの MP3 | `tools/audio/README_school_song.md` の 2 節 | README には「原編曲は写していない」とある | 資料はリポジトリに入っていない |
| 効果音 41、環境音 7、ジングル 2（`jingle_quest`、`jingle_day_end`） | 本作のオリジナル。Python と numpy で合成 | `tools/audio/build_audio.py`、`synth.py`、`sfx.py`、`ambient.py`、`music.py`、`manifest.json` | なし | 確認済み |
| 時報のチャイム `se_chime.wav` | ウェストミンスターの鐘の旋律（1793 年の伝承曲）を numpy で合成 | `tools/audio/README_school_song.md` の 5 節、`sfx.py` の `CHIME_PHRASES` | 旋律はパブリックドメイン | 確認済み |
| BGM `bgm_anthem_original`（71.11 秒）、`bgm_result`（30.0 秒）、`bgm_title`（40.0 秒） | 本作のオリジナル。numpy で合成 | `music.py`。コードからは鳴らしていないが、`AudioFactory` が `Assets/Audio` の全クリップを AudioManager に入れるのでビルドには入る | なし | 確認済み |

`Assets/Audio/README.md` の冒頭にある「ゲーム内の音はすべて Python と numpy だけで合成」「サンプリング素材・外部ライブラリの音源・録音物は一切使っていない」は、
校歌の 5 曲（上の 2 行）が入る前の記述で、今の中身とは合わない。

ゲーム内の表示: 「音楽: 東京理科大学校歌（作詞 佐治巌 / 作曲 大和憲史）」、歌唱とピアノ伴奏の出所、効果音・環境音・ジングルが numpy のオリジナルであること。

## フォント

| 素材 | 出所・権利者 | 根拠 | 条件・表記 | 確認状況 |
|---|---|---|---|---|
| Noto Sans JP（`Assets/Fonts/NotoSansJP-VF.ttf`、Version 2.04。TextMeshPro のフォントアセット `KCD_JP` の元） | © 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source' | フォントの name テーブル（著作権表示とライセンスの欄） | SIL Open Font License 1.1。フォントと一緒にライセンス本文を配る | 確認済み |
| Liberation Sans（`Assets/TextMesh Pro/Fonts/LiberationSans.ttf`。TextMeshPro に同梱の `LiberationSans SDF` の元） | Digitized data © 2010 Google Corporation, with Reserved Font Arimo, Tinos and Cousine. © 2012 Red Hat, Inc., with Reserved Font Name Liberation | `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` | SIL Open Font License 1.1 | 確認済み |

ライセンス本文は `Assets/StreamingAssets/Licenses/`（`NotoSansJP-OFL.txt`、`LiberationSans-OFL.txt`）に置く。
StreamingAssets はビルドにそのまま入るので、配布物にもフォントと一緒に本文が入る。フォントを足したら、ここと `CreditsTextTests.ShippedFonts` と StreamingAssets/Licenses に足す（足さないと `EveryFontFile_HasItsLicense` が落ちる）。

## キャラクター・店名

| 素材 | 出所・権利者 | 根拠 | 条件・表記 | 確認状況 |
|---|---|---|---|---|
| 坊っちゃん・マドンナちゃん | 東京理科大学の公式キャラクター。3D モデルとその顔・柄のテクスチャは本作が Blender で生成したもの | `blender/build_characters.py`、`blender/kcd_chara/`。公式イラスト `docs/ref/tus_chara01.jpg` / `tus_chara02.jpg` を頭身・形・色の参照に使った（画像そのものはゲームに入らない） | 公式キャラクターであることと、モデルが独自制作であることを表示する | 大学の許諾の記録なし（未確認） |
| みらい・いなり・かなめ・そら・教授 | 本作のオリジナルキャラクター | `docs/DESIGN.md` | 学生・教授は架空の人物と表示する | 確認済み |
| スターバックス、ファミリーマート、ローソン | 各社の商標。店名は会話文と OSM の名称（`campus.json` の `display`）に出る。建物の看板はブランドの色の板だけで、ロゴや文字は描いていない | `Assets/Data/campus.json`、`Localization/*.json`、`blender/kcd_lib/buildings.py`、`blender/kcd_lib/mats.py` | 各社の商標であることを表示する | 使用の許諾の記録なし |

## 3D モデル・テクスチャ

建物・木・内装・キャラクターの形とテクスチャ（`Assets/Models/`、`Assets/Textures/`）は、このリポジトリの Blender 4.5 の Python スクリプト（`blender/`）で生成した本作のオリジナル。
建物の形は上の地図データに基づく。外から画像素材は取り込んでいない。2026-09-24 時点で `Assets` にある画像は、
キャラクターの顔・柄（`Assets/Models/Characters/*/`、`blender/kcd_chara/`）、ミニマップ（`tools/render_minimap.py`）、
ミニマップの印（`Assets/Generated/UI/minimap_*.png`、`Scripts/Editor/MinimapAssets.cs` が描く）だけで、どれもスクリプトの出力。

外壁の煉瓦のテクスチャ（`Assets/Textures/surfaces/brick_red.jpg`、#109）だけは例外で、[ambientCG](https://ambientcg.com/) の Bricks092（CC0 1.0）から煉瓦の肌の粒を借り、`tools/brick_bond.py` で張り方・1 枚ずつの色・目地を組み直したもの。

## 参考資料（`docs/ref`、ゲームには入らない）

| ファイル | 内容 | 権利者 | 確認状況 |
|---|---|---|---|
| `tus_chara01.jpg` / `tus_chara02.jpg` | 坊っちゃん・マドンナちゃんの公式イラスト | 東京理科大学 | 未確認 |
| `tus_symbole.jpg` / `tus_symbole02.jpg` / `tus_symbole03.jpg` | 校旗など大学のシンボルの画像 | 東京理科大学 | 未確認 |
| `katsushika_map.png` | 葛飾キャンパスの公式鳥瞰図 | 東京理科大学 | 未確認 |
| `nikken_02.jpg` 〜 `nikken_09.jpg` | キャンパスの建物の写真（日建設計） | 日建設計（撮影者を含む） | 未確認 |

どれもモデルを作るときの参照で、ゲームにもビルドにも入らない。ただしリポジトリが公開なので、GitHub 上では誰でも取得できる状態にある。
公開の許諾の記録は無く、置き続けてよいかは未確認。

## ソフトウェア

- Unity 6000.6.2f1（Unity Personal）、Universal Render Pipeline、Input System、Cinemachine、AI Navigation、TextMeshPro
- Blender 4.5 LTS
- 校歌の制作: NEUTRINO（歌声合成）、MuseScore 4、music21、ffmpeg（`tools/audio/school_song/README.md`）
- 効果音・環境音の合成: Python、numpy
- Unity 公式 agent skills（Unity-Technologies/skills、`skills-lock.json` で固定）

## 配布前に確かめること

1. 東京理科大学校歌の作曲者（大和憲史）と作詞者（佐治巌）の没年。保護期間内なら、大学（広報課）へ使用の可否を問い合わせる。確認が取れるまでは、`AudioManager.Scene.cs` の 1 行で `bgm_anthem_original` に戻せる。
2. NEUTRINO と東北きりたんの利用規約と、クレジット表記の指定。
3. ピアノ伴奏の採譜に入れた元の音源と、ヤマハの採譜サービスの利用規約。
4. 沿道の建物の高さが PLATEAU 由来か。由来なら PLATEAU の利用規約に沿った表記を足す。
5. `docs/ref` の画像を公開リポジトリに置き続けてよいか。
6. 坊っちゃん・マドンナちゃんの 3D 化と、店名・ブランドの色の使用。

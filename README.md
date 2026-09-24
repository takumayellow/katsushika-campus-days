# Katsushika Campus Days（葛飾キャンパスデイズ）

東京理科大学 葛飾キャンパスを OpenStreetMap の実測フットプリントから 3D 再現し、
アニメ調の女の子（と公式マスコットのオマージュ）で歩き回る三人称探索アドベンチャー。
建物 9 棟は内部まで入れて、学生 NPC との会話でクエストが進み、1 日の終わりにリザルトが出る。

![タイトル](docs/screenshots/title.png)

- エンジン: Unity 6 (6000.6.2f1, URP)
- アセット生成: Blender 4.5 LTS を headless Python で駆動（手作業ゼロで再生成できる）
- 音声: BGM 8 / ジングル 2 / SE 41 / 環境音 7。タイトルとキャンパスの BGM 5 曲は東京理科大学校歌（タイトルは東北きりたんの歌唱 + ピアノ伴奏、キャンパスはピアノ伴奏）。ほかは numpy で合成したオリジナル
- 設計書: [docs/DESIGN.md](docs/DESIGN.md)、データ仕様: [docs/CONTENT_SPEC.md](docs/CONTENT_SPEC.md)
- 遊び方: [docs/HOW_TO_PLAY.md](docs/HOW_TO_PLAY.md)
- 複数のエージェントで分担するときの決まり: [docs/AGENT_COORDINATION.md](docs/AGENT_COORDINATION.md)

## 遊ぶ（ブラウザ版）

https://takumayellow.github.io/katsushika-campus-days/ を開く。Chrome / Edge / Firefox の最新版、
WebGL 2 対応 GPU。初回は約 100 MB を読み込む。セーブはブラウザに 1 枠（PlayerPrefs = IndexedDB）。

## 遊ぶ（配布版）

`dist/KatsushikaCampusDays_win64.zip` を展開して `KatsushikaCampusDays.exe` を起動する。
Windows 10/11 64bit、インストール不要。操作は WASD / 矢印キー移動、マウス視点、Shift ダッシュ、Space ジャンプ、
E で話す/入る、Tab クエストログ、Esc メニュー。詳細は [docs/HOW_TO_PLAY.md](docs/HOW_TO_PLAY.md)。

## 自分でビルドする

必要なもの: Unity 6000.6.2f1（Hub 版、URP テンプレート）、Blender 4.5、Python 3.11 以上（numpy）。

```bash
# 1. 地図 → キャンパス外構 FBX（Blender）。raw_overpass.json は Overpass API の `out geom;` 出力
python tools/osm_extract.py
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --python blender/build_campus.py
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --python blender/build_characters.py
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --python blender/build_interiors.py
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --python tools/render_minimap.py

# 2. 音声（約 6 分）
python tools/audio/build_audio.py

# 3. Unity: 取り込み → シーン生成 → Windows ビルド（unity/README.md に詳細）
export UNITY='/c/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe'
export KCD="$(pwd -W)"
MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit -projectPath "$KCD\unity\KatsushikaCampusDays" -logFile "$KCD\unity\logs\import.log"
MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit -projectPath "$KCD\unity\KatsushikaCampusDays" -executeMethod KCD.Editor.SceneBuilder.BuildAll -logFile "$KCD\unity\logs\scene.log"
MSYS_NO_PATHCONV=1 "$UNITY" -batchmode -nographics -quit -projectPath "$KCD\unity\KatsushikaCampusDays" -executeMethod KCD.Editor.BuildPlayer.BuildWindows -logFile "$KCD\unity\logs\build.log"
python tools/check_unity_log.py unity/logs/import.log unity/logs/scene.log unity/logs/build.log

# 4. テストとスモーク
python tools/run_tests.py   # EditMode → PlayMode → pytest。Unity が動いていたら止まる（--wait で待つ）
python -m pytest -q         # pytest だけ（tests/, tools/, blender/kcd_lib の純 Python 部分。bpy は差し替え。数秒。pip install -r requirements-test.txt）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/smoke_run.ps1 -WaitSec 25 -Out docs/screenshots/smoke_title.png

# 5. 配布 zip
python tools/package_zip.py

# 6. ブラウザ版を GitHub Pages に載せる（WebGL ビルド → Release web-latest → pages.yml）
#    unity/KatsushikaCampusDays の下に未コミットの変更があると止まる（--allow-dirty で通す）。
#    ビルド元のコミットは index.html の <meta name="kcd-build"> とページ右下に出る。
python tools/deploy_pages.py --build --smoke   # --smoke: 載せる前に build/WebGL を E2E スモーク

# 7. ブラウザ版の E2E スモーク（Playwright。初回だけ pip install -r e2e/requirements.txt）
python e2e/run_webgl_smoke.py                      # 公開版を Edge の headless + 実 GPU で、キャンパスまで
python e2e/run_webgl_smoke.py --serve build/WebGL  # ローカルのビルドを Pages と同じ条件で配信して
python -m pytest e2e                               # スモークの部品の単体テスト
```

`tools/run_tests.py` は結果を `unity/logs/tests_{editmode,playmode}.xml` と `.log` に書き、失敗とスキップの
名前・メッセージだけを抜き出して表示する。`--platform playmode`、`--filter <テスト名の正規表現>`、
`--skip-unity`（pytest だけ）が使える。batchmode が書き換える URP GlobalSettings・`ProjectSettings.asset`・
`.mat` は、実行前に変更が無かったものだけ元に戻す。終了コードは 0 = 全部通った、1 = 失敗かスキップか
ログのエラーがある、2 = 走らせられなかった、3 = 別の Unity が動いている。
PlayMode のテスト（`Assets/Tests/PlayMode`）は Title と Campus を読み込んで動かすので、
`SceneBuilder.BuildAll` でシーンを作り直したあとに回す。

E2E スモーク（`e2e/`）は、読み込み時間・Build/ の応答・コンソールのエラー・キャンバスの描画・
キャンバス内の案内（`#kcd-overlay`）・Enter でのキャラ選択とキャンパス入りを確かめ、
`build/e2e/<対象>-<日時>/` に `metrics.json`（時間と wasm / JS のヒープ）とスクリーンショットを残す。
既知の警告は `e2e/kcd_e2e/console_policy.py` の許可リストに理由付きで載せる。
Pages の配信が終わると `.github/workflows/e2e-pages.yml` が GPU なし（SwiftShader）でキャラ選択まで確かめる。

Unity のライセンスは Hub でサインインしてから batchmode を使う。Unity 公式の agent skills は
`skills-lock.json` で固定してあり、`npx skills experimental_install` で `.agents/skills/` に復元できる。

## パイプライン

```
data/osm/raw_overpass.json ─ tools/osm_extract.py ─▶ data/osm/campus.json
data/osm/raw_route.json    ─ tools/osm_route.py   ─▶ data/osm/route.json（寮への道。隠しエンド #41 用）
                                                        │
blender/build_campus.py     ◀───────────────────────────┘  ─▶ unity/.../Assets/Models/Campus/*.fbx
blender/build_characters.py                                  ─▶ unity/.../Assets/Models/Characters/<id>/
blender/build_interiors.py                                   ─▶ unity/.../Assets/Models/Interiors/*.fbx + *.json
tools/audio/build_audio.py                                   ─▶ unity/.../Assets/Audio/{BGM,SE,Ambient}/*.wav
Unity -batchmode -executeMethod KCD.Editor.SceneBuilder.BuildAll   ─▶ Scenes/{Title,Campus}.unity
Unity -batchmode -executeMethod KCD.Editor.BuildPlayer.BuildWindows ─▶ build/Windows/
```

原点と投影（敷地ポリゴン way 175463006 の重心、x = 東 m / z = 北 m）は `tools/osm_common.py` で共有していて、
`python tools/osm_common.py --selfcheck` で `data/osm/campus.json` と照合できる。
Overpass の生データ 2 つは大きいので gitignore。寮への道の分は `data/osm/route_query.ql` を投げれば取り直せる。

## フォルダ

| 場所 | 内容 |
| --- | --- |
| `blender/` | 外構・キャラ 7 体・建物内部 9 棟の生成スクリプト（`kcd_lib` / `kcd_chara` / `kcd_interior`） |
| `tools/` | OSM 抽出（キャンパス／寮への道）、ミニマップ描画、音声合成、Unity ログ検査、スモーク起動、zip 化 |
| `unity/KatsushikaCampusDays/` | Unity プロジェクト。`Assets/Scripts/Editor` がシーンを組み立て、`Runtime` がゲーム本体 |
| `unity/KatsushikaCampusDays/Assets/Data/` | クエスト・会話・収集物・ローカライズ・モブ・リザルトの JSON |
| `docs/` | 設計書、データ仕様、クレジット、プレビュー画像、スクリーンショット |

## クレジット

素材ごとの出所・権利・確認状況は [docs/CREDITS.md](docs/CREDITS.md)。ゲーム内のクレジット画面も同じ内容。

- 地図データ © OpenStreetMap contributors (ODbL)。https://www.openstreetmap.org/copyright
- 音楽: 東京理科大学校歌（作詞 佐治巌 / 作曲 大和憲史）。歌唱は東北きりたん（NEUTRINO）、ピアノ伴奏はヤマハの自動採譜（Piano Sheet Converter）を手直ししたもの。校歌の権利は未確認で、配布前に確かめる項目を CREDITS.md に並べている
- 効果音・環境音・ジングルは numpy で合成したオリジナル
- 「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクター。本作は非公式・非営利のファンメイドで、3D モデルは独自に制作している
- フォント: Noto Sans JP（© Adobe）、Liberation Sans（© Google, Red Hat）。どちらも SIL Open Font License 1.1 で、本文は `Assets/StreamingAssets/Licenses` に同梱
- `docs/ref` の参考画像（大学の公式イラスト・鳥瞰図、日建設計の写真など）は各権利者のもので、ゲームには入っていない

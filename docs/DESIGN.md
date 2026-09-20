# Katsushika Campus Days — 設計書（全パイプライン共通の契約）

東京理科大学 葛飾キャンパスを実測データから 3D 再現し、その中をアニメ調の女の子たちが歩き回る
三人称探索アドベンチャー。Blender (4.5 LTS, headless Python) でアセットを生成し、
Unity 6 (6000.6.2f1, URP) で組み立てる。**このファイルが Blender 側 / Unity 側 / データ側の唯一の契約**。
変更するときはここを先に直す。

## 1. ゲーム概要

- タイトル: **Katsushika Campus Days（かつしかキャンパスデイズ）**
- ジャンル: 三人称視点のキャンパス探索アドベンチャー（お使いクエスト + 収集 + 会話）
- 舞台: 東京理科大学 葛飾キャンパス（東京都葛飾区新宿 6-3-1）と隣接する葛飾にいじゅくみらい公園、理科大通り。
  実寸（1 unit = 1 m）。全長 250 m のキャンパスモールが南西（金町駅側）→北東（図書館）へ貫く。
- 1 日の流れ: 朝に理科大通りから登校 → クエスト（講義・図書館・食堂・共創棟スタバ・体育館・公園）→ 夕方。
  時間は `DayNightCycle` が進める（1 ゲーム日 = 実時間 12 分、開始 08:30）。
- 操作: WASD 移動 / Shift ダッシュ / Space ジャンプ / E 話す・調べる / Tab クエストログ / Esc メニュー。
  ゲームパッド対応（Input System）。

## 2. キャラクター（選択可能 3 名 + NPC）

全員 **アニメ調**（セルシェード、大きな目、小さな鼻・口、髪は房のかたまり）。
身長比は美少女 2 人が **6 頭身**、坊っちゃんは **5 頭身でやや骨太**。可愛さ最優先。

| id | 名前 | 立場 | 外見（Blender で作る仕様） |
|----|------|------|----------------------------|
| `mirai` | 新宿 みらい（にいじゅく みらい） | 主人公。工学部情報工学科 2 年。明るくて好奇心旺盛 | 身長 158 cm。ミディアムボブ（ミルクティー色 #C9A27E）+ 左側に青いヘアピン。大きなアクア色の瞳。白いブラウス + 理科大グリーン (#00843D) のリボン + 紺のプリーツスカート + 黒ハイソックス + ローファー。肩掛けトートバッグ |
| `botchan` | 坊っちゃん | 理科大公式マスコットのオマージュ。東京物理学校卒の数学教師。真っ直ぐで喧嘩っ早い | 身長 165 cm・5 頭身。短い黒髪（オールバック気味）、太い眉、きりっとした目。白地に **青の十字絣** の着物 + **青い袴** + **高下駄** + 背中に **赤い風呂敷包み** |
| `madonna` | マドンナちゃん | 理科大公式サブマスコットのオマージュ。明治・大正の女学生。おっとり上品 | 身長 156 cm・6 頭身。栗色（#8B5A2B）のロングヘア・前髪ぱっつん + 頭頂に **大きな赤いリボン**。星の入った瞳。白地に **赤の矢絣** の着物 + **紫の袴 (#6A3FA0)** + 茶色の **編み上げブーツ** |

NPC（同じ生成器の派生でよい。髪色・服色違い）:

| id | 名前 | 場所 | 役割 |
|----|------|------|------|
| `inari` | 花之木 いなり | 図書館前の池のほとり | 図書館クエストの依頼人。黒髪ツインテール + 赤メガネ |
| `kaname` | 中川 かなめ | 第2研究棟 1F 食堂 | 食堂クエスト。栗色ポニーテール + エプロン |
| `sora` | 金町 そら | 共創棟 1F スターバックス前 | 薬学部生。水色ショートヘア |
| `prof` | 教授 | 講義棟 大教室 | 講義クエスト。白衣・眼鏡・白髪の男性（簡略でよい） |

### 2.1 リグ（Unity Humanoid 互換・必須）

ボーン名は **そのまま Unity の Humanoid 自動マッピングに乗る名前** にする（Mecanim で歩行を共有するため）:

```
Hips
 Spine
  Chest
   UpperChest
    Neck
     Head
    LeftShoulder / LeftUpperArm / LeftLowerArm / LeftHand
    RightShoulder / RightUpperArm / RightLowerArm / RightHand
 LeftUpperLeg / LeftLowerLeg / LeftFoot / LeftToes
 RightUpperLeg / RightLowerLeg / RightFoot / RightToes
```

- 姿勢は **A ポーズ**、Y-up・Z-forward（Blender で作り、FBX エクスポート時に `axis_forward='-Z', axis_up='Y'`）。
- ウェイトは自動（`ARMATURE_AUTO`）+ 髪・スカート・袴は Head / Hips に寄せる。
- 表情はブレンドシェイプ（Shape Keys）: `blink`, `smile`, `mouth_open`, `surprised`。名前固定。
- 各キャラ **同じアニメーション名** で NLA/Action を持つ: `Idle`, `Walk`, `Run`, `Jump`, `Wave`, `Talk`。
  Blender 側で作れない場合は Unity 側の共有 AnimatorController（`mirai` の Action を Humanoid リターゲット）で流用するので、最低限 `mirai` に全 6 種、他は `Idle` があればよい。
- スケール: 1 Blender unit = 1 m。エクスポート時 `apply_scale_options='FBX_SCALE_ALL'`, `apply_unit_scale=True`。

### 2.2 マテリアル

- Unity 側にトゥーンシェーダ `KCD/Toon`（URP・HLSL 手書き）を置く。Blender からは **色情報だけ** 渡せばよい:
  マテリアル名で識別する（`skin`, `hair`, `eye_l`, `eye_r`, `eye_white`, `cloth_*`, `metal`, …）。
  Unity のインポータ（`Assets/Editor/CharacterImporter.cs`）がマテリアル名 → Toon マテリアルへ差し替える。
- 顔（目・眉・口）は **テクスチャ PNG**（Blender の `bpy.data.images` で 1024×1024 を Python 描画して保存）
  を `Assets/Models/Characters/<id>/face.png` に置く。UV は顔前面の平面投影。

## 3. ワールド（キャンパス）

データ: `data/osm/campus.json`（`tools/osm_extract.py` が生成。ODbL・OpenStreetMap 由来）。

```
meta.origin           緯度経度の原点（敷地重心）
campus_boundary       [[x,z],...]  敷地ポリゴン
buildings[]           id/display/levels/height/style/desc/on_campus/footprint[[x,z],...]  (反時計回り)
paths[]               kind/name/width/points[[x,z],...]
areas[]               kind(park/pitch/playground/parking/university)/polygon
points[]              kind(shrine/toilets/...)/x/z
```

座標: **x=東, z=北 (m)**、原点 = 敷地重心。Unity ではそのまま `(x, y, z)`、Blender では `(x, y=z_north, z=up)`。
**Blender → FBX → Unity で位置がズレないよう、FBX の原点 = 敷地重心 に固定**する（オブジェクトのワールド位置を保つ）。

### 3.1 建物リスト（campus.json の `style` ごとの作り込み方針）

参考写真: `docs/ref/nikken_*.jpg`（日建設計）、`docs/ref/katsushika_map.png`（公式鳥瞰図）。
外装の共通語彙: **グレーのプレキャストコンクリート格子（窓が水平連続）+ レンガ色 (#8E3B2F) の階段コア / 端部ボリューム + ガラスのエレベーター塔（薄い庇つき）**。

| id | style | 作り込み |
|----|-------|---------|
| `research1` 第1研究棟 | `lab_tower` | 11F・49.5 m・東西に長い。各階に水平連続窓（グレー格子 + 濃いガラス）。両端と中央にレンガ色コア。屋上に機械室ボックス 3 つ。南面 1F はピロティ（柱列）で モールに開く |
| `kyoso` 共創棟 | `kyoso` | 11F・47 m。新しい。白〜ライトグレーのルーバー + ガラス。1F はガラス張りで内側に **スターバックス**（緑の看板）と **ファミリーマート**（青緑白の看板）のサイン。ラーニングスクエア |
| `lecture` 講義棟 | `lecture` | 7F・30 m。**南西角が曲面（弧）** で、そこにガラスの大階段ホール。それ以外は格子窓。1F にラウンジ。屋上に機械室 |
| `research2` 第2研究棟（旧管理棟） | `office` | 6F・25 m。1〜2F は食堂（大きなガラス面、屋上緑化の低層部が西へ張り出す）。上階は研究室 |
| `library` 図書館 | `library` | 5F・22 m。**キャンパスのシンボル**。大きな水平屋根（軒が深く、細い柱で支える）、南側に **浅い池（水盤）** が広がる。屋上に **八角形のドーム（大ホール、600 席）**。全面ガラスのカーテンウォール |
| `gym` 体育館 | `gym` | 6F・21.8 m。大きな箱。メインアリーナ側は無窓の壁 + 上部ハイサイドライト。北側に部室棟（小窓が並ぶ） |
| `lab1` / `lab2` 実験棟 | `lab_low` | 4F/2F。格子窓 + レンガ色の階段コア。屋上に排気ダクト |
| `greenhouse` 温室 | `greenhouse` | ガラスの小屋（切妻、白いフレーム） |
| `dorm_*` 学生寮 | `dormitory` | 6F。簡易バルコニー付き |
| `bg_*` 周辺の家・ビル | `background` | 高さ押し出しだけ。色はランダムなクリーム〜グレー。窓はテクスチャ無しで可 |

### 3.2 外構（必ず作る）

- **キャンパスモール**: 講義棟・第1研究棟の南〜図書館まで幅 12 m の石畳（グレー 2 色の市松）。両脇に街路樹（ケヤキ風・高さ 8〜10 m）を 8 m 間隔。ベンチ・照明柱（高さ 4 m、白いポール）を 20 m 間隔。
- **図書館前の水盤**: 図書館南面に沿う 60×25 m 程度の浅い池（`areas` に無いので図書館 footprint の南に手で置く）。
- **にいじゅくみらい公園**: `areas.park` の芝生（緑 #6FA84A）、`pitch` は土色、`playground` は明るい砂色。樹木をランダム散布（密度 1 本/150 m²、敷地境界沿いは列植）。
- **道路**: `paths` を幅どおりの帯で押し出し（高さ 0.05）。`tertiary`/`residential` はアスファルト（#3C3C3C）+ 白線、`footway` は明るいグレー。
- **地面**: 全体を覆う 700×700 m の平面（芝生色）。
- **歩行可能領域**: 建物は全部 **コライダー付き**。建物 1F 入口は **ガラス扉のくぼみ**（interior は作らない。入口前にトリガー用の目印 Empty `entrance_<id>` を置く）。
- **サイン**: 各建物入口に名称プレート（テキストは Unity 側の TMP で出すので、Blender は Empty `sign_<id>` を置くだけ）。

### 3.3 Blender 出力

```
unity/KatsushikaCampusDays/Assets/Models/Campus/campus.fbx        建物・外構（Empty 含む）
unity/KatsushikaCampusDays/Assets/Models/Campus/trees.fbx         樹木（インスタンス配置用: 3 種のツリーメッシュ + Empty `tree_<n>` で位置）
unity/KatsushikaCampusDays/Assets/Models/Characters/<id>/<id>.fbx
unity/KatsushikaCampusDays/Assets/Models/Characters/<id>/face.png
docs/previews/campus_*.png, docs/previews/<id>_*.png                 検証用レンダ（Eevee/Workbench, 1280×720）
```

FBX 共通オプション: `use_selection=False`, `global_scale=1.0`, `apply_unit_scale=True`, `apply_scale_options='FBX_SCALE_ALL'`,
`axis_forward='-Z'`, `axis_up='Y'`, `bake_space_transform=True`（キャンパス）, `mesh_smooth_type='FACE'`,
`use_mesh_modifiers=True`, `add_leaf_bones=False`, `bake_anim=True`（キャラ）, `path_mode='COPY'`, `embed_textures=False`。

## 4. Unity プロジェクト

- パス: `unity/KatsushikaCampusDays/`（Unity 6000.6.2f1、URP 17、Input System、Cinemachine 3、TextMeshPro）。
- `Packages/manifest.json` に `com.unity.render-pipelines.universal`, `com.unity.inputsystem`, `com.unity.cinemachine`, `com.unity.ai.navigation`, `com.unity.ugui`, `com.unity.textmeshpro` を明記。
- 名前空間: `KCD`（ランタイム）, `KCD.Editor`（エディタ）。アセンブリ定義 `KCD.Runtime.asmdef`, `KCD.Editor.asmdef`。
- フォルダ:

```
Assets/
  Scripts/Runtime/{Player,Camera,World,Quest,Dialogue,UI,NPC,Save}/
  Scripts/Editor/            SceneBuilder.cs, CharacterImporter.cs, BuildPlayer.cs
  Shaders/KCD_Toon.shader    URP セルシェード（2 段階の影 + リムライト + アウトライン）
  Models/Campus/, Models/Characters/<id>/
  Data/Quests/*.json, Data/Dialogue/*.json, Data/campus.json (data/osm/campus.json のコピー)
  Prefabs/, Materials/, Scenes/Campus.unity, Scenes/Title.unity
  Settings/ (URP asset, renderer, InputActions)
```

- **バッチで完結すること（必須）**: 人手でシーンを組まない。
  - `KCD.Editor.SceneBuilder.BuildAll()` — campus.fbx / trees.fbx / キャラ FBX を配置し、コライダー・NavMesh・ライト・Cinemachine・UI・クエスト定義から `Scenes/Campus.unity` と `Scenes/Title.unity` を生成する。
  - `KCD.Editor.BuildPlayer.BuildWindows()` — `build/Windows/KatsushikaCampusDays.exe` を出力。
  - 実行例: `"C:\Program Files\Unity 6000.6.2f1\Editor\Unity.exe" -batchmode -nographics -projectPath unity/KatsushikaCampusDays -executeMethod KCD.Editor.SceneBuilder.BuildAll -logFile build/unity.log -quit`
- ゲームプレイ:
  - `PlayerController`（CharacterController ベース、Humanoid Animator、カメラ相対移動、ジャンプ、ダッシュ）
  - `ThirdPersonCamera`（Cinemachine 3 の `CinemachineCamera` + OrbitalFollow。無ければ自前追従）
  - `Interactable` / `InteractionPrompt`（E キーで話す・調べる。範囲 2.5 m）
  - `DialogueSystem`（JSON の会話ツリー、名前・立ち絵は不要、テキストは 1 文字ずつ表示）
  - `QuestSystem`（JSON: id/title/steps[go_to|talk|collect]/reward。HUD にトラッカー）
  - `NPCWander`（NavMeshAgent でウェイポイント巡回、プレイヤー接近で停止して向く）
  - `DayNightCycle`（太陽の回転 + スカイ色 + 街灯 ON/OFF）
  - `CharacterSelect`（Title シーン。3 人を回転台で表示、決定で Campus へ。選択は `PlayerPrefs`）
  - `Minimap`（上空の直交カメラ RenderTexture、右上）
  - `BuildingLabel`（`sign_<id>` に TMP のワールドテキスト、近づくと表示）
  - `SaveSystem`（JSON、クエスト進捗と時刻）
- クエスト初期セット（`Assets/Data/Quests`）:
  1. `q_orientation` 登校: 理科大通りの入口 → キャンパスモール → 講義棟前で教授と話す
  2. `q_library` 図書館: いなりに頼まれた本を図書館で受け取り、池のほとりのいなりに届ける
  3. `q_lunch` 食堂: かなめのために共創棟のファミマで「牛乳」を買って第2研究棟食堂へ
  4. `q_coffee` スタバ: そらと一緒にキャンパスモールのベンチへ
  5. `q_gym` 体育館: 体育館まで 60 秒以内にダッシュ
  6. `q_park` 公園: にいじゅくみらい公園に散らばった「理科大グリーンの葉」5 枚を集める
  7. `q_sunset` 夕焼け: 18:00 以降に図書館の水盤前へ（エンディング会話）

## 5. 検証（PROVE-BEFORE-CLOSE）

- Blender: headless 実行の exit code 0、FBX のファイルサイズ > 0、プレビュー PNG を `Read` で目視。
- Unity: `-batchmode` のログに `Compilation succeeded` 相当が出ること、`Scenes/Campus.unity` の生成、EXE の生成。
- ゲーム: 起動 → タイトル → キャラ選択 → キャンパスを歩く → クエスト 1 が完了できる。

## 6. クレジット・権利

- 地図データ © OpenStreetMap contributors (ODbL)。
- 「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクター。本作はファンメイドのオマージュで、
  公式画像は使わず独自にモデリングする。非公式・非営利。

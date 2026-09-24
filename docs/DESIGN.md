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
- 操作: WASD / 矢印キー 移動 / Shift ダッシュ / Space ジャンプ / E 話す・調べる / Tab クエストログ / Esc メニュー。
  ゲームパッド対応（Input System）。

## 2. キャラクター（選択可能 3 名 + NPC）

全員 **アニメ調**（セルシェード、大きな目、小さな鼻・口、髪は房のかたまり）。
身長比は主人公 みらい と NPC 4 人が **6 頭身**。公式マスコットのオマージュである
坊っちゃんとマドンナちゃんは、公式イラスト（`docs/ref/tus_chara01.jpg` /
`tus_chara02.jpg`）が頭の直径 ≒ 全高 ÷ 3 のデフォルメなので、それに合わせた
**約 3 頭身**（実測 2.83 / 2.95 頭身）。可愛さ最優先。

| id | 名前 | 立場 | 外見（Blender で作る仕様） |
|----|------|------|----------------------------|
| `mirai` | 新宿 みらい（にいじゅく みらい） | 主人公。工学部情報工学科 2 年。明るくて好奇心旺盛 | 身長 158 cm。ミディアムボブ（ミルクティー色 #C9A27E）+ 左側に青いヘアピン。大きなアクア色の瞳。白いブラウス + 理科大グリーン (#00843D) のリボン + 紺のプリーツスカート + 黒ハイソックス + ローファー。肩掛けトートバッグ |
| `botchan` | 坊っちゃん | 理科大公式マスコットのオマージュ。東京物理学校卒の数学教師。真っ直ぐで喧嘩っ早い | 全高 115 cm・2.83 頭身（公式イラスト `tus_chara01.jpg` の実測 2.80 頭身に合わせる）。逆立てた短い黒髪、太い眉、きりっとした目。白地に **青の十字絣** の着物 + **青い袴** + **高下駄** + 肩に **橙色の風呂敷包み** |
| `madonna` | マドンナちゃん | 理科大公式サブマスコットのオマージュ。明治・大正の女学生。おっとり上品 | 全高 114 cm・2.95 頭身（公式イラスト `tus_chara02.jpg` に合わせる）。栗色（#8B5A2B）のロングヘア・前髪ぱっつん + 頭頂に **大きな赤いリボン**。星の入った瞳。白地に **赤の矢絣** の着物 + **紫の袴 (#6A3FA0)** + 茶色の **編み上げブーツ** |

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
| `library` 図書館 | `library` | 5F・22 m。**キャンパスのシンボル**。大きな水平屋根（軒が深く、細い柱で支える）、東と南を **堀のような浅い水盤** が囲む。屋上に **八角形のドーム（大ホール、600 席）**。全面ガラスのカーテンウォール |
| `gym` 体育館 | `gym` | 6F・21.8 m。大きな箱。メインアリーナ側は無窓の壁 + 上部ハイサイドライト。北側に部室棟（小窓が並ぶ） |
| `lab1` / `lab2` 実験棟 | `lab_low` | 4F/2F。格子窓 + レンガ色の階段コア。屋上に排気ダクト |
| `greenhouse` 温室 | `greenhouse` | ガラスの小屋（切妻、白いフレーム） |
| `dorm_*` 学生寮 | `dormitory` | 6F。簡易バルコニー付き |
| `bg_*` 周辺の家・ビル | `background` | 高さ押し出しだけ。色はランダムなクリーム〜グレー。窓はテクスチャ無しで可 |

### 3.2 外構（必ず作る）

- **キャンパスモール**: 講義棟・第1研究棟の南〜図書館まで幅 12 m の石畳（グレー 2 色の市松）。両脇に街路樹（ケヤキ風・高さ 8〜10 m）を 8 m 間隔。ベンチ・照明柱（高さ 4 m、白いポール）を 20 m 間隔。
- **図書館を囲む水盤**: 図書館の東面と南面に沿う幅 9.5〜12 m の堀のような浅い水（`areas` に無いので
  `site.py` の `BASINS` に手で置く）。東の帯はモールで途切れる。東の芝生広場は木を植えない。
- **モール北側の花壇**: モールの北縁に沿う幅 3.6 m の立ち上がり花壇（長さ 9 m、間 3 m）。歩道と北側の
  建物の入口の前は空ける。
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

### 3.4 建物内部

屋内は `blender/build_interiors.py`（+ `blender/kcd_interior/*.py`）が屋外とは独立に生成する。
棟ごとに 1 FBX。運用手順は `blender/README_interiors.md`。

```
unity/KatsushikaCampusDays/Assets/Models/Interiors/<building_id>.fbx   屋内 + 窓の外の近景（ext_<building_id>）
unity/KatsushikaCampusDays/Assets/Models/Interiors/<building_id>.json  配置用メタ（entrance_world / yaw_deg / envelope / 全 Empty のローカル座標 / ext）
unity/KatsushikaCampusDays/Assets/Models/Interiors/_summary.json   全棟の三角数（屋内・近景）・Empty 名・座席数
docs/previews/interior_<building_id>*.png                          検証用レンダ（Eevee Next, 1280×720）
```

**座標**: ローカル原点は `entrance_<id>` 直下の床レベル。Unity の +Z が入口から建物の奥へ向く。
近景（`ext_<id>`）は屋外と同じ場所に同じ形を持つので、屋内は屋外から離れた場所に置く
（Unity の `InteriorStage` は x = 1200 m から棟ごとに並べる）。
FBX オプションは §3.3 と同じ。

**メッシュ分割**（Unity で MeshCollider を個別に張るため）:

- `floor_<id>` 床スラブ / `wall_<id>` 外壁・間仕切り・建具・ガラス
- `furn_<id>_<nn>_<区画名>` 区画ごとの什器（例 `furn_lecture_02_hall_seats`）
- `ext_<id>` / `ext_<id>_trees` 窓の外の近景（地面・道・花壇・隣の棟 / 木）。外周の外にしか無く、
  プレイヤーは届かないので当たり判定は要らない（Unity 側で `ext_` を当たり判定から外す作業は #60）

**Empty**: `spawn_<id>`（入口から 1.5 m 内側）、`exit_<id>`、`poi_<id>_<name>`、`npc_<id>_<n>`、`sign_<id>_<n>`。
§4 のクエストが屋内で踏む地点はすべて用意してある。`q_library` は `poi_library_counter` と
`poi_library_desk`、`q_lunch` は `poi_kyoso_store` と `poi_research2_counter`・`poi_research2_hall`、
`q_coffee` は `poi_kyoso_starbucks`、`q_gym` は `poi_gym_court`。
`q_coffee` の最終目的地（キャンパスモールのベンチ）と `q_orientation` / `q_park` / `q_sunset` は屋外。

**棟ごとの中身**（実測 2026-09-24・合計 297,441 三角形 / 上限 300,000）:

| id | 三角形 | 中身 |
|---|---:|---|
| `research1` | 40,439 | 1F ロビー（受付・ソファ・掲示板・立席島）+ EV 3 基 + 廊下 142 m + 研究室 2 室 + 教授室 |
| `lecture` | 47,429 | 3 層吹き抜けの大階段ホール + 大ホール 600 席 + ホワイエ + 演習室 3 室 + 中教室 60 席 |
| `research2` | 64,210 | 1〜2F 吹き抜けの大食堂 1,400 席（配膳カウンター 38 m・券売機 6 台・返却口・2F 回廊） |
| `kyoso` | 25,544 | 1F カフェ + コンビニ、2F ラウンジ（計 90 席） |
| `library` | 57,924 | 開架書架 14 連 + 閲覧長机 6 列 + 個人閲覧ブース 66 席 + 2F 回廊（計 299 席） |
| `gym` | 20,965 | アリーナ（28×15 m コート・ゴール 2 基・ステージ）+ 観覧席 540 席 + 屋根トラス + 用具庫 |
| `lab1` | 22,502 | 廊下（ロッカー・掲示板・自販機）+ 実験室 3 室（実験台・ドラフト・ボンベ・薬品庫・試薬棚・流し） |
| `lab2` | 14,300 | 廊下 + 実験室 2 室（同上） |
| `greenhouse` | 4,128 | 切妻ガラス屋根・栽培ベンチ 2 列・鉢植え・灌水パイプ |

1 棟あたり 20,000〜80,000 三角形を目安とする。`lab2`（23.7×15.8 m）と `greenhouse`（7.5×10.7 m）は
フットプリントが小さく、下限を割るのを許容する。

**窓の外の近景**（#84）: 屋外の `campus.fbx` / `trees.fbx` から、外周を四方へ 30 m 広げた矩形の中を
切り出して同じ FBX に入れる（`kcd_interior/exterior.py`）。木は切らずに丸ごと入れる。屋外と同じ形・同じマテリアル名・同じ木なので、
窓から見える景色と外へ出たときの景色が一致する。屋内とは別の予算で数える
（実測 2026-09-24・合計 166,260 三角形 / 上限 180,000、1 棟 50,000 まで）:

| id | 近景 | うち木 | 木の本数 |
|---|---:|---:|---:|
| `research1` | 24,613 | 9,172 | 52 |
| `lecture` | 43,851 | 17,017 | 101 |
| `research2` | 25,385 | 7,630 | 45 |
| `kyoso` | 26,221 | 7,893 | 44 |
| `library` | 12,969 | 9,723 | 58 |
| `gym` | 11,721 | 4,242 | 25 |
| `lab1` | 10,895 | 5,713 | 40 |
| `lab2` | 6,496 | 3,515 | 26 |
| `greenhouse` | 4,109 | 1,092 | 9 |

- 自分の棟の外装と扉まわり（風除室・ガラス扉・枠・庇・マット）は入れない。外装の窓割りは屋内と別に作っているので、
  残すと外壁・ルーバー・柱廊が屋内の窓の真ん前をふさぐ。扉の前の石張りは地面として残す。
- 屋内の入口の前（開口の両脇 1 m、外周から外へ 8 m）にかかる花壇・ベンチ・照明柱などと木は
  入れず、入口から外へ出る先を空ける。自分の棟の立て看板は入口の脇の目印として残す。
- 木は間引かない（近景の 4 割弱が木）。減らすと窓の外と屋外で木の並びが合わなくなる。
  樹冠が外周にかかる木だけは、切ると断面が見えるので入れない。
- 花壇の株は外周から 12 m より遠いものを低ポリの形に替える。
- 建物の中のどこから見ても裏を向く面（隣の棟の向こう側の外壁、樹冠の奥側など）は入れない。
  キャンパスのマテリアルはすべて片面描画なので、室内からの見た目は変わらない。
- 屋内は 1 棟ずつしか見えないので、効くのは描画よりメモリ。頂点は 9 棟で 324,215（屋内 584,710）。
  FBX は位置と法線だけを持つ（UV なし）ので 1 頂点 24 B、Unity の取り込みで接線（16 B）が付いても
  40 B で、近景の頂点は約 8〜13 MB。16 bit インデックスが約 1 MB
  （1 メッシュの頂点は最大 55,014 で 16 bit に収まる）。

**上層階**: 床スラブ・手すり・吹き抜けから見える什器までを作り、上層の個室は作らない
（EV 扉と階数表示のみ）。

**共通の作り付け**: 壁厚 0.3 m、外観と揃えた `glass_clear` の窓、`light_panel` の天井照明、
誘導灯、掲示板・ポスター、ゴミ箱、消火器、`sign_<id>_<n>` 付きの案内サイン。
マテリアルは `kcd_lib/mats.py` の命名規則に `kcd_interior/imats.py` が屋内分を足す。

**検証**: 書き出した FBX を bpy で読み戻し、Empty と近景のメッシュがすべて揃っているかを照合してから終了する
（欠落があれば exit 1）。

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

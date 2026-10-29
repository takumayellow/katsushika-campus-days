# Katsushika Campus Days — 設計書（全パイプライン共通の契約）

東京理科大学 葛飾キャンパスを実測データから 3D 再現し、その中をアニメ調の女の子たちが歩き回る
三人称探索アドベンチャー。Blender (4.5 LTS, headless Python) でアセットを生成し、
Unity 6 (6000.6.2f1, URP) で組み立てる。**このファイルが Blender 側 / Unity 側 / データ側の唯一の契約**。
変更するときはここを先に直す。

## 1. ゲーム概要

- タイトル: **Katsushika Campus Days（かつしかキャンパスデイズ）**
- ジャンル: 三人称視点のキャンパス探索アドベンチャー（お使いクエスト + 収集 + 会話）
- 舞台: 東京理科大学 葛飾キャンパス（東京都葛飾区新宿 6-3-1）と隣接する葛飾にいじゅくみらい公園、理科大通り。
  実寸（1 unit = 1 m）。全長 250 m のメインストリートが南西（金町駅側）→北東（図書館）へ貫く。大学の公式案内での名称はキャンパスモール。
- 1 日の流れ: 朝に理科大通りから登校 → クエスト（講義・図書館・食堂・共創棟カフェ・体育館・公園）→ 夕方。
  時間は `DayNightCycle` が進める（1 ゲーム日 = 実時間 12 分、開始 08:30）。
- 操作: WASD / 矢印キー 移動 / Shift ダッシュ / Space ジャンプ / E 話す・調べる / Tab クエストログ / Esc メニュー。
  ゲームパッド対応（Input System）。

## 2. キャラクター（選択可能 3 名 + NPC）

全員 **アニメ調**（セルシェード、大きな目、小さな鼻・口、髪は房のかたまり）。
身長比は主人公 みらい と NPC 4 人が **6 頭身**。公式マスコットのオマージュである
坊っちゃんとマドンナちゃんは、公式イラスト（`docs/ref/tus_chara01.jpg` /
`tus_chara02.jpg`）が頭の直径 ≒ 全高 ÷ 3 のデフォルメなので、それに合わせた
**約 3 頭身**（2.83 / 2.98 頭身）。可愛さ最優先。

| id | 名前 | 立場 | 外見（Blender で作る仕様） |
|----|------|------|----------------------------|
| `mirai` | 新宿 みらい（にいじゅく みらい） | 主人公。工学部情報工学科 2 年。明るくて好奇心旺盛 | 身長 158 cm。ミディアムボブ（ミルクティー色 #C9A27E）+ 左側に青いヘアピン。大きなアクア色の瞳。白いブラウス + 理科大グリーン (#00843D) のリボン + 紺のプリーツスカート + 黒ハイソックス + ローファー。右手に提げたトートバッグ |
| `botchan` | 坊っちゃん | 理科大公式マスコットのオマージュ。東京物理学校卒の数学教師。真っ直ぐで喧嘩っ早い | 全高 128 cm・2.83 頭身（公式イラスト `tus_chara01.jpg` の実測 2.80 頭身に合わせる）。逆立てた短い黒髪、太い眉、きりっとした目。白地に **青の十字絣** の着物 + **青い袴** + **高下駄** + 肩に **橙色の風呂敷包み** |
| `madonna` | マドンナちゃん | 理科大公式サブマスコットのオマージュ。明治・大正の女学生。おっとり上品 | 全高 122 cm・2.98 頭身（公式イラスト `tus_chara02.jpg` に合わせる）。明るい黄土色（#AA6D26）のロングヘア・前髪ぱっつん + 頭頂に **大きな赤いリボン**。星の入った瞳。白地に **赤のハート柄** の振袖 + **紫の行灯袴 (#9066A8)** + 茶色の **編み上げブーツ** |

NPC（同じ生成器の派生でよい。髪色・服色違い）:

| id | 名前 | 場所 | 役割 |
|----|------|------|------|
| `inari` | 花之木 いなり | 図書館前の池のほとり | 図書館クエストの依頼人。黒髪ツインテール + 赤メガネ |
| `kaname` | 中川 かなめ | 第2研究棟 1F 食堂 | 食堂クエスト。栗色ポニーテール + エプロン |
| `sora` | 金町 そら | 共創棟 1F カフェ前 | 薬学部生。水色ショートヘア |
| `prof` | 教授 | 講義棟 大教室 | 講義クエスト。紺の背広・ネクタイ・眼鏡・黒髪の男性（簡略でよい） |

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
buildings[]           id/display/levels/height/style/desc/osm_id/osm_name/on_campus/footprint[[x,z],...]  (反時計回り)
                      display は画面に出る名前（カタログの建物は名前、背景の建物は総称か空）。OSM の元の名前は osm_name で、画面には使わない
paths[]               kind/name/width/points[[x,z],...]
areas[]               kind(park/pitch/playground/parking/university)/polygon
points[]              kind(shrine/toilets/...)/x/z
```

座標: **x=東, z=北 (m)**、原点 = 敷地重心。Unity ではそのまま `(x, y, z)`、Blender では `(x, y=z_north, z=up)`。
**Blender → FBX → Unity で位置がズレないよう、FBX の原点 = 敷地重心 に固定**する（オブジェクトのワールド位置を保つ）。

### 3.1 建物リスト（campus.json の `style` ごとの作り込み方針）

参考写真: `docs/ref/nikken_*.jpg`（日建設計）、`docs/ref/katsushika_map.png`（公式鳥瞰図）。
外装の共通語彙: **グレーのプレキャストコンクリート格子（窓が水平連続）+ レンガ色 (#7A5C50) の階段コア / 端部ボリューム + ガラスのエレベーター塔（薄い庇つき）**。

| id | style | 作り込み |
|----|-------|---------|
| `research1` 第1研究棟 | `lab_tower` | 11F・49.5 m・東西に長い。各階に水平連続窓（グレー格子 + 濃いガラス）。両端と中央にレンガ色コア。屋上に機械室ボックス 3 つ。南面 1F はピロティ（柱列）で メインストリートに開く |
| `kyoso` 共創棟 | `kyoso` | 11F・45 m。2024 年竣工。長手の 2 面はグレーの縦フィンの格子 + 濃いガラス、妻面はレンガタイル。1F はガラス張りで内側に **カフェ** と **コンビニ**。外には店の看板も店名も出さない。ラーニングスクエア（吹き抜け・鉄骨階段）|
| `lecture` 講義棟 | `lecture` | 7F・30 m。**南西角が曲面（弧）** で、そこにガラスの大階段ホール。それ以外は格子窓。1F にラウンジ。屋上に機械室 |
| `research2` 第2研究棟（旧管理棟） | `office` | 6F・25 m。外観は 1 棟で、1〜2F は食堂（大きなガラス面、屋上緑化の低層部が西へ張り出す）、上階は研究室。扉は 2 つ（食堂 `cafeteria` と研究棟ロビー `research2`）で、屋内もそれぞれ別の空間 |
| `library` 図書館 | `library` | 5F・22 m。**キャンパスのシンボル**。大きな水平屋根（軒が深く、細い柱で支える）、東と南を **堀のような浅い水盤** が囲む。屋上に **八角形のドーム（大ホール、600 席）**。全面ガラスのカーテンウォール |
| `gym` 体育館 | `gym` | 6F・21.8 m。大きな箱。メインアリーナ側は無窓の壁 + 上部ハイサイドライト。北側に部室棟（小窓が並ぶ） |
| `lab1` 第1実験棟 | `lab_low` | 4F。東西はレンガの壁、南北は白い縦フィンの格子窓。東面に外階段の切り込みと踊り場、南面に 2 階の床の高さの庇。屋上に設備の箱・室外機の列。西に 1 階建ての別棟（`build_lab1`、`docs/ref/exterior/lab1.md`） |
| `lab2` 第2実験棟 | `lab_low` | 2F。格子窓 + レンガ色の階段コア。屋上に排気ダクト |
| `greenhouse` 温室 | `greenhouse` | 陸屋根のガラスの小屋（L 字、白いフレーム） |
| `dorm_*` 学生寮 | `dormitory` | 6F。簡易バルコニー付き |
| `bg_*` 周辺の家・ビル | `background` | 高さ押し出しだけ。色はランダムなクリーム〜グレー。窓はテクスチャ無しで可 |

### 3.2 外構（必ず作る）

地面・舗装・芝の色の参考: `docs/ref/aerial/`（国土地理院の航空写真）と `docs/ref/commons/`（Wikimedia Commons の構内の写真。煉瓦・敷石・季節ごとの芝）。面ごとの色の見本はそれぞれのフォルダの README。

- **メインストリート**: 講義棟・第1研究棟の南〜図書館まで幅 12 m の石畳（グレー 2 色の市松）。両脇に街路樹（ケヤキ風・高さ 8〜10 m）を 8 m 間隔。ベンチ・照明柱（高さ 4 m、白いポール）を 20 m 間隔。
- **図書館を囲む水盤**: 図書館の東面と南面に沿う幅 9.5〜12 m の堀のような浅い水（`areas` に無いので
  `site.py` の `BASINS` に手で置く）。東の帯はメインストリートで途切れる。東の芝生広場は木を植えない。
- **メインストリート北側の花壇**: メインストリートの北縁に沿う幅 3.6 m の立ち上がり花壇（長さ 9 m、間 3 m）。歩道と北側の
  建物の入口の前は空ける。
- **にいじゅくみらい公園**: `areas.park` の芝生（緑 #6FA84A）、`pitch` は `sport` と `surface` で分ける（サッカー場の人工芝は芝、砂入り人工芝のテニスコートは土色、ハードコートは緑の区画に青のコートと白線）、`playground` は明るい砂色。樹木をランダム散布（密度 1 本/150 m²、敷地境界沿いは列植）。
- **道路**: `paths` を幅どおりの帯で押し出し（高さ 0.05）。`tertiary`/`residential` はアスファルト（#6A6A68）+ 白線、`footway` は明るいグレー。
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
入口の面と面に沿った位置はキャンパスの扉（`blender/kcd_lib/entrances.py` の `DOORS`）と同じにし、
書き出しの検証（`[verify]`）で棟ごとに照合する。
近景（`ext_<id>`）は屋外と同じ場所に同じ形を持つので、屋内は屋外から離れた場所に置く
（Unity の `InteriorStage` は x = 1200 m から棟ごとに並べる）。
FBX オプションは §3.3 と同じ。

**メッシュ分割**（Unity で MeshCollider を個別に張るため）:

- `floor_<id>` 床スラブ / `wall_<id>` 外壁・間仕切り・建具・ガラス
- `furn_<id>_<nn>_<区画名>` 区画ごとの什器（例 `furn_lecture_02_hall_seats`）
- `ext_<id>` / `ext_<id>_trees` 窓の外の近景（地面・道・花壇・隣の棟 / 木）。外周の外にしか無く、
  プレイヤーは届かないので当たり判定は要らない（Unity 側で `ext_` を当たり判定から外す作業は #60）

**Empty**: `spawn_<id>`（入口から 1.5 m 内側）、`exit_<id>`、`poi_<id>_<name>`、`npc_<id>_<n>`、`sign_<id>_<n>`。
階のある屋内は、EV を降りた所の `ev_<id>_<階>`（扉から 1.4 m 前の床）と、降りたときに向く向きの `ev_<id>_<階>_f`（1 m 先）、
廊下の天井の点光源 `lamp_<id>_<階>_<n>` も置く。Unity はそこに乗り場（`ElevatorPanel`。E で行き先の画面を開く）と
点光源を置き、今いる階の明かりだけを点ける（`InteriorFloorLights`）。
§4 のクエストが屋内で踏む地点はすべて用意してある。`q_library` は `poi_library_counter` と
`poi_library_desk`、`q_lunch` は `poi_kyoso_store` と `poi_cafeteria_counter`・`poi_cafeteria_hall`、
`q_coffee` は `poi_kyoso_starbucks`、`q_gym` は `poi_gym_court`、`q_lecture` は `poi_lecture_hall_seats`、
`q_sq_lab_tour` は `poi_research1_notice` と `poi_research1_lab_a`。
`q_coffee` の最終目的地（メインストリートのベンチ）と `q_orientation` / `q_park` / `q_sunset` は屋外。

**棟ごとの中身**（実測 2026-10-14、各棟の `Assets/Models/Interiors/<id>.json`・合計 387,395 三角形 / 上限 450,000。うち看板の文字 110,320）:

| id | 三角形 | 上限 | うち文字 | 中身 |
|---|---:|---:|---:|---|
| `research1` | 44,097 | 50,000 | 24,455 | 1F 吹き抜け（トップライト・FJR710・窓口）+ 南北 2 本の廊下 + 西と東の EV ホール（2F まで）+ 実験室・研究室・産学試作開発室・西側ラウンジ |
| `lecture` | 121,172 | 130,000 | 25,816 | 1F: 大教室 101（270 席）・104・生協・東端ホール・コアの吹き抜け（1F〜6F）とエスカレータ。2F〜7F: 南北の廊下・西ロビー・西の階段・コア（トイレの入口・1F〜7F の EV）と教室 20 室（2F の 203・202・201、3F の 305〜301・306 PC ルーム・307〜309、4F〜6F の 402・502・602 と会議室 410・509・610。黒板・教卓・机と長椅子 1,476 席）、3F のラウンジ 310・311（311 は弁当の販売）、2F の L 字ラウンジ、3F の弧の自習室 |
| `cafeteria` | 59,663 | 62,000 | 14,588 | 食堂。1F 学食（見た目の席 392）と 2F（見た目の席 370）。1F と 2F の間に吹き抜けは無い。配膳カウンター・セルフオーダー端末 2〜3 台とえんじ色のパネル 2 台・返却口。研究棟ロビーとは X 23.0 の間仕切りで分ける |
| `research2` | 11,298 | 20,000 | 4,092 | 第2研究棟のロビー（EV・階段室・案内板）と 3F〜6F の中廊下（EV ホール・閉じた事務室・研究室・会議室の扉）。ロビーの EV から各階へ行く |
| `kyoso` | 34,235 | 45,000 | 8,533 | 1F コンビニ・玄関ホール・カフェ・ラーニングスクエア・カフェラウンジ・ラウンジ 2 室、吹き抜けの鉄骨階段、2F ラウンジ（計 118 席） |
| `library` | 73,496 | 80,000 | 15,916 | 開架書架 14 連 + 閲覧長机 6 列 + 個人閲覧ブース 66 席 + 2F 回廊（計 299 席） |
| `gym` | 17,312 | 21,000 | 6,382 | アリーナ（コート・吊りゴール・壁際の観覧席・ギャラリー・屋根トラス）+ 2F トレーニングルーム + ホワイエ + 東棟（事務室・更衣室・サブアリーナ）+ 用具庫 |
| `lab1` | 20,058 | 24,500 | 6,274 | 廊下（ロッカー・掲示板・自販機）+ 実験室 2 室 + 準備室 1 室（実験台・ドラフト・ボンベ・薬品庫・試薬棚・流し） |
| `lab2` | 10,411 | 12,500 | 4,003 | 短辺（東面）の入口から玄関ホール + 廊下 + 実験室 1 室 + 準備室 1 室（同上） |
| `greenhouse` | 3,991 | 5,000 | 261 | L 字の陸屋根のガラス箱・栽培ベンチ 4 台・鉢植え・灌水パイプ |

上限は `build_interiors.py` の `INT_BUDGET_ONE`（棟ごと）と `INT_BUDGET`（10 棟の合計 450,000）。
棟の三角形には看板の文字も入る。どちらかを越えると書き出しが `[budget] NG` を出して exit 1 で止まる。
上限は建物の広さと中身の多さで決めてあり、建物担当は自分の棟の上限の中で作る（上限を上げるのは
統合ブランチでの判断）。寮（`dorm`、`build_dorm.py`）は別の予算で 30,000（実測 13,775、うち文字 2,571）。

**窓の外の近景**（#84）: 屋外の `campus.fbx` / `trees.fbx` から、外周を四方へ 30 m 広げた矩形の中を
切り出して同じ FBX に入れる（`kcd_interior/exterior.py`）。木は切らずに丸ごと入れる。屋外と同じ形・同じマテリアル名・同じ木なので、
窓から見える景色と外へ出たときの景色が一致する。屋内とは別の予算で数える
（実測 2026-10-14・合計 175,655 三角形 / 上限 200,000、1 棟 50,000 まで）:

| id | 近景 | うち木 | 木の本数 |
|---|---:|---:|---:|
| `research1` | 37,044 | 24,480 | 120 |
| `lecture` | 34,532 | 18,473 | 95 |
| `research2` | 8,711 | 3,547 | 21 |
| `cafeteria` | 21,846 | 6,797 | 36 |
| `kyoso` | 27,629 | 13,915 | 71 |
| `library` | 13,676 | 9,929 | 59 |
| `gym` | 9,858 | 5,633 | 31 |
| `lab1` | 9,893 | 4,616 | 31 |
| `lab2` | 7,702 | 5,333 | 35 |
| `greenhouse` | 4,764 | 1,981 | 15 |

- 自分の棟の外装と扉まわり（風除室・ガラス扉・枠・庇・マット）は入れない。外装の窓割りは屋内と別に作っているので、
  残すと外壁・ルーバー・柱廊が屋内の窓の真ん前をふさぐ。扉の前の石張りは地面として残す。
- 屋内の入口の前（開口の両脇 1 m、外周から外へ 8 m）にかかる花壇・ベンチ・照明柱などと木は
  入れず、入口から外へ出る先を空ける。自分の棟の立て看板は入口の脇の目印として残す。
- 木は間引かない（近景の 4 割弱が木）。減らすと窓の外と屋外で木の並びが合わなくなる。
  樹冠が外周にかかる木だけは、切ると断面が見えるので入れない。
  図書館は近景の上限（`ext_library` と `ext_library_trees` の合計で 14,469）に収めるため、
  幹から外周まで 12 m より遠い木だけ幹の分割数を半分にする（`exterior.COARSE_TRUNK`）。
- 花壇の株は外周から 12 m より遠いものを低ポリの形に替える。
- 建物の中のどこから見ても裏を向く面（隣の棟の向こう側の外壁、樹冠の奥側など）は入れない。
  キャンパスのマテリアルはすべて片面描画なので、室内からの見た目は変わらない。
- 屋内は 1 棟ずつしか見えないので、効くのは描画よりメモリ。2026-09-24 の実測で、頂点は 9 棟で 324,215（屋内 584,710）。
  FBX は位置と法線だけを持つ（UV なし）ので 1 頂点 24 B、Unity の取り込みで接線（16 B）が付いても
  40 B で、近景の頂点は約 8〜13 MB。16 bit インデックスが約 1 MB
  （1 メッシュの頂点は最大 55,014 で 16 bit に収まる）。

**上層階**: 床スラブ・手すり・吹き抜けから見える什器までを作り、上層の個室は作らない
（EV 扉と階数表示のみ）。第2研究棟は EV で行く 3F〜6F の中廊下を作り、部屋は閉じた扉と文字の無い名札だけにする。

**共通の作り付け**: 壁厚 0.3 m、外観と揃えた `glass_clear` の窓、`light_panel` の天井照明、
誘導灯、掲示板・ポスター、ゴミ箱、消火器、`sign_<id>_<n>` 付きの案内サイン。

**屋内の材質**: `kcd_lib/mats.py` の命名規則に `kcd_interior/imats.py` が屋内分を足す。何棟かで使う材質は
`imats.py` の `COMMON_*`、1 棟だけで使う材質はその棟の `kcd_interior/mats_<id>.py` に書く。
`imats.compose()` が両方を合わせ、次のどれかに当たると `ValueError` でビルドを止める。

- `mats_<id>.py` は `PALETTE`（不透明。alpha は 1.0）・`TRANSPARENT`（0 < alpha < 1）・`EMISSIVE`
  （`名前 -> ((r, g, b), strength)`）の 3 つの dict を持つ。色は線形 RGB（0〜1）
- 名前は `<id>_` で始まる英小文字・数字・`_` だけ（Unity は名前を小文字にして探す）
- `COMMON_*` や同じ棟の別の表と同じ名前は使わない。`EMISSIVE` の名前はその棟の `PALETTE` にあること
- 共有の材質で足りる色は書かない。建物担当は `imats.py` を触らない
- Unity の `InteriorPalette.cs` へは、統合ブランチで `python tools/interior_palette.py` が写す

**看板の文字**: 屋内の看板は `kcd_interior/signs.py` の `signs.wall()`（壁に付ける板）と
`signs.hanging()`（天井から吊る板）で置き、文言を `ja=` と `en=` で渡す
（例 `signs.hanging(c, mb, x, y, z_ceil, ang, ja="貸出・返却", en="Circulation Desk", w=3.2, h=0.62)`）。

- 文字は `kcd_lib/signtext.py` がメッシュにし、板と同じ MeshBuilder に入れて板の面に貼る。屋内の看板には
  Unity の TextMeshPro を使わない。フォントは `blender/fonts/KCDSignJP-Bold.ttf`（Noto Sans JP を
  `tools/make_sign_font.py` で太さ 700 に固定し、第 1 水準の漢字まで絞った OFL 1.1 の改変版）
- `ja` と `en` は両方必須で、空なら `ValueError`。日本語の行を上、英語の行を下に小さく並べる。改行は `"
"`。
  フォントに無い字（第 2 水準の漢字など）も `ValueError`。要る字は `make_sign_font.py` の `EXTRA` に足して
  フォントを作り直す
- 字の大きさは板に収まる最大。em が `signtext.MIN_SIZE`（0.03 m）を割る組は `ValueError` なので、
  板を大きくするか文言を短くする。文字の材質は板の色に対して `plastic_white` / `plastic_black` の
  対比が大きいほう（`text=` で替えられる）。吊り看板は既定で両面（`both=False` で表だけ）
- 看板 1 枚に照明の目印の Empty `sign_<id>_<n>` を 1 つ置く（Unity は天井灯の位置に使う）。
  同じ札を近くに並べるときは `anchor=False` で省く。`c.sign()` を直に呼んだ文字の無い Empty は
  `signs.check()` がビルドを止める
- 置いた看板は `<id>.json` の `signs`（文言・位置・向き・板の大きさ）と `text_triangles` に書き出す
- 文言は実物に合わせる。寮は大学の直営ではないので、寮の看板には大学の名前を出さず「大学の寮」とも書かない

**建物担当が触ってよいファイル**: 棟ごとの作業（統合ブランチ `wf/interior-base` への PR）では、
自分の棟の次のファイルだけを変える。

- `blender/kcd_interior/plan_<id>.py`・`furn_<id>.py`・`mats_<id>.py`
- `blender/tests/test_plan_<id>.py`
- `docs/ref/interiors/<id>/`（参考資料）・`docs/progress/<issue>/`（前後の画像）
- `unity/KatsushikaCampusDays/Assets/Models/Interiors/<id>.fbx` と `<id>.json`
- 寮は `blender/build_dorm.py` と `blender/kcd_route/dorm.py` ほか寮の生成に使うモジュール、
  `Models/Interiors/dorm.fbx` / `dorm.json`

共有のファイル（`imats.py`・`shell.py`・`signs.py`・`build_interiors.py`・`_summary.json`・Unity の C#）と
上限の変更は統合ブランチで行い、最後に Unity で確かめてから dev/game へマージコミットで入れる。

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
  - `SignFaceText`（入口の立て看板 `sign_<id>` の板の表と裏に貼る建物の名前。建物の名前の文字は 1 棟につきこの 1 枚だけ）
  - `SaveSystem`（JSON、クエスト進捗と時刻）
- クエスト初期セット（`Assets/Data/Quests`）:
  1. `q_orientation` 登校: 理科大通りの入口 → メインストリート → 講義棟前で教授と話す
  2. `q_library` 図書館: いなりに頼まれた本を図書館で受け取り、池のほとりのいなりに届ける
  3. `q_lunch` 食堂: かなめのために共創棟のコンビニで「牛乳」を買って第2研究棟食堂へ
  4. `q_coffee` カフェ: そらと一緒にメインストリートのベンチへ
  5. `q_gym` 体育館: 体育館まで 60 秒以内にダッシュ
  6. `q_park` 公園: にいじゅくみらい公園に散らばった「理科大グリーンの葉」5 枚を集める
  7. `q_lecture` 講義: 講義棟の大教室に入り、席に着いて、教壇の教授（会話データ `lecture`）と話す
  8. `q_sunset` 夕焼け: 1〜7 をすべて終えたあと、18:00 以降に図書館の水盤前へ
- エンディング（`FormalEnding`）: `q_sunset` を達成すると操作を止め、2 秒後にいなりの会話 `t_sunset` を流す。
  会話が終わると暗転して 3 ページ（今日の振り返り → 会った人 → クレジット）を出し、最後のページを閉じると
  キャンパスに戻る。日付は進まず、そのまま遊び続けられる。一度だけ出る。
- サブクエストは `q_sq_*`（研究室見学 `q_sq_lab_tour` は `q_lecture` のあとに教授から受ける。講義棟の
  「黒板の 1 バイト」`q_sq_hello_world` を終えると、教授から 3F の 306 PC ルームの「306 の居残り」`q_sq_first_program`
  を受け、TA（会話データ `ta_306`）と最前列の PC（`pc_306`）で HELLO, WORLD を動かす）、
  隠しクエストは `q_secret_*`。総数の 17（隠しクエストを除く）は実績・一日の終わりのリザルト・エンディングで共通。

## 5. 検証（PROVE-BEFORE-CLOSE）

- Blender: headless 実行の exit code 0、FBX のファイルサイズ > 0、プレビュー PNG を `Read` で目視。
- Unity: `-batchmode` のログに `Compilation succeeded` 相当が出ること、`Scenes/Campus.unity` の生成、EXE の生成。
- ゲーム: 起動 → タイトル → キャラ選択 → キャンパスを歩く → クエスト 1 が完了できる。

## 6. クレジット・権利

- 地図データ © OpenStreetMap contributors (ODbL)。
- 「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクター。本作はファンメイドのオマージュで、
  公式画像は使わず独自にモデリングする。非公式・非営利。

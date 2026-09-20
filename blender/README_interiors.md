# 建物内部（インテリア）ジェネレータ

`blender/build_interiors.py` は `data/osm/campus.json` のフットプリントから
9 棟の**屋内**を手続き生成し、棟ごとに 1 つの FBX として書き出す。
屋外を作る `blender/build_campus.py` とは独立して動く。共有するのは `kcd_lib/` の読み取りだけ。

仕様は `docs/DESIGN.md` §3.4「建物内部」が正本。

## 実行

```bash
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" \
  -b --python blender/build_interiors.py -- --ids all --preview
```

主なオプション:

| オプション | 既定 | 意味 |
|---|---|---|
| `--ids` | `all` | 対象の棟。カンマ区切り（`--ids library,gym`） |
| `--preview` | off | `docs/previews/interior_<id>*.png` を 1280×720 でレンダする |
| `--no-export` | off | FBX を書かず三角数だけ測る（全棟 2 秒弱。予算調整用） |
| `--engine` | `auto` | レンダエンジン。`auto` / `eevee` / `workbench`。`auto` は EEVEE Next が使えればそれを選ぶ |
| `--out-dir` | `unity/.../Assets/Models/Interiors` | FBX の出力先 |
| `--seed` | `20250921` | 小物のばらつきの乱数種。同じ種なら三角数まで完全に再現する |
| `--data-dir` | `unity/.../Assets/Data` | クエスト・収集物 JSON の場所。データが参照する `poi_*` が FBX に揃っているかの照合に使う |

所要時間（RTX 5070 Ti / Blender 4.5.10 LTS）:

- `--no-export` 全棟: **1.5 秒**
- `--preview` 付き全棟: **220 秒**（うち図書館のレンダだけで 129 秒）

実行の最後に、書き出した FBX を bpy で**読み戻して** Empty の数と名前を照合する
（`[verify] OK <id> meshes=N empties=M/M 必須POI=K`）。1 つでも欠けると `NG` を出して exit 1。
あわせて `Assets/Data/{Quests,Collectibles}/*.json` の中の `poi_<id>_<name>` 文字列を全部集め、
その棟の Empty に**データが参照する POI がすべて存在する**ことを契約として照合する
（無ければ `契約違反` と出して exit 1）。クエスト側で新しい POI 名を使い始めたら、
対応するプランに `c.poi(...)` を足してから流し直す。

## 出力

- `unity/KatsushikaCampusDays/Assets/Models/Interiors/<id>.fbx`
- 同 `<id>.json` — Unity 側の配置に要るメタデータ。キャンパス座標での入口位置 `entrance_world`、
  建物の向き `yaw_deg`、外周 `envelope`、壁厚、`spawn`、全 Empty のローカル座標、三角数、座席数。
- 同 `_summary.json` — 全棟の三角数・オブジェクト名・Empty 名・座席数・FBX サイズ
- `docs/previews/interior_<id>.png` ほか（`--preview` のとき。全 14 枚 1280×720）

## 棟ごとの内容と規模

合計 **295,249 三角形**（上限 300,000）。

| id | 建物 | 三角形 | FBX | Empty | 中身 |
|---|---|---:|---:|---:|---|
| `research1` | 第1研究棟 | 40,285 | 0.40 MB | 23 | 1F ロビー（受付・ソファ・掲示板・立席島）+ EV 3 基 + 廊下 142 m + 研究室 2 室 + 教授室 |
| `lecture` | 講義棟 | 46,053 | 0.45 MB | 29 | 3 層吹き抜けの大階段ホール + **大ホール 600 席**（16/18/16 × 12 列）+ ホワイエ + 演習室 3 室 + 中教室 60 席 |
| `research2` | 第2研究棟 | 65,000 | 0.61 MB | 24 | 1〜2F 吹き抜けの**大食堂 1,400 席**（配膳カウンター 38 m・券売機 6 台・返却口・2F 回廊 240 席） |
| `kyoso` | 共創棟 | 25,186 | 0.30 MB | 17 | 1F カフェ（吹き抜け）+ コンビニ、2F ラウンジ。計 90 席 |
| `library` | 図書館 | 57,654 | 0.53 MB | 20 | 開架書架 14 連 + 閲覧長机 6 列 + 個人閲覧ブース 66 席 + 2F 回廊の吹き抜け。計 299 席 |
| `gym` | 体育館 | 20,181 | 0.25 MB | 16 | アリーナ（28×15 m コート・ゴール 2 基・ステージ）+ 観覧席 540 席 + 屋根トラス + 用具庫 |
| `lab1` | 実験棟1 | 22,444 | 0.25 MB | 11 | 廊下（ロッカー・掲示板・自販機）+ 実験室 2 室 + 準備室 1 室（実験台・ドラフト・ボンベ・薬品庫・試薬棚・流し） |
| `lab2` | 実験棟2 | 14,230 | 0.19 MB | 10 | 廊下 + 実験室 1 室 + 準備室 1 室（同上） |
| `greenhouse` | 温室 | 4,216 | 0.11 MB | 5 | 切妻ガラス屋根・栽培ベンチ 2 列・鉢植え・灌水パイプ |

`lab2` と `greenhouse` はフットプリントが 23.7×15.8 m / 7.5×10.7 m しかなく、
20,000 三角形を積むと実在しない什器で埋めることになるため、下限を下回るのを許容している。

## 座標と Unity への置き方

FBX のローカル原点は **`entrance_<id>` の直下の床レベル**、Unity の **+Z が入口から建物奥へ**向く。
そのまま `entrance_<id>` に置いても、地下のオフセット領域に置いても成立する。

書き出しオプションは `docs/DESIGN.md` §3.3 と `build_campus.py` に合わせてある
（`axis_forward='-Z'`, `axis_up='Y'`, `bake_space_transform=True`, `global_scale=1.0`, `apply_scale_options='FBX_SCALE_ALL'`）。

メッシュは Unity の MeshCollider を個別に張れるよう分けてある:

- `floor_<id>` — 床スラブ
- `wall_<id>` — 外壁・間仕切り・建具・ガラス
- `furn_<id>_<nn>_<区画名>` — 区画ごとの什器（`furn_lecture_02_hall_seats` など）

Empty の命名:

| 名前 | 用途 |
|---|---|
| `spawn_<id>` | 入口から 1.5 m 内側。入館時のプレイヤー位置 |
| `exit_<id>` | 退館位置 |
| `poi_<id>_<name>` | クエスト対象（`poi_library_desk`, `poi_kyoso_starbucks`, `poi_lecture_hall_stage`, `poi_gym_court`, `poi_research2_counter` ほか） |
| `npc_<id>_<n>` | NPC の立ち位置 |
| `sign_<id>_<n>` | TextMeshPro を貼る看板の位置 |

## コード構成

| ファイル | 役割 |
|---|---|
| `build_interiors.py` | CLI・FBX 書き出し・再インポート検証・集計 |
| `kcd_interior/kit.py` | メッシュ生成の基本プリミティブ（`box` / `cyl` / `tube` / `plate` / `lift` ほか） |
| `kcd_interior/imats.py` | 屋内マテリアル（`kcd_lib/mats.py` の命名規則に合わせた追加分） |
| `kcd_interior/shell.py` | 床・壁・天井・階段・手すり・建具・EV・サイン等の躯体部品 |
| `kcd_interior/furniture.py` | 什器（机・椅子・書架・実験台・座席・カウンター等） |
| `kcd_interior/common.py` | 全棟共通の外周・入口まわり・廊下の作り付け |
| `kcd_interior/plan_*.py` | 棟ごとの間取り（`plan_lecture.py` ほか 8 本） |

`kcd_lib/` と `build_campus.py` は読むだけで、このジェネレータからは変更しない（屋外側と共有しているため）。

## 既知の制約

- EEVEE Next が `Shadow buffer full (約 3,000 / 2,048)` を出す。プレビューの一部で影が落ちないが、
  FBX の形状には影響しない。Unity 側のライティングとも無関係。
- 上層階は床スラブ・手すり・什器までで、上層の**個室は作っていない**（EV 扉と階数表示のみ）。
  吹き抜けから見える範囲に絞って三角形を使っている。
- 家具の当たり判定は Unity 側で MeshCollider を張る前提。凸包が必要なものは別途 Convex 指定が要る。

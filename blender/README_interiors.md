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
| `--no-export` | off | FBX と `<id>.json` を書かず三角数だけ測る（全棟で約 4 秒。予算調整用） |
| `--engine` | `auto` | レンダエンジン。`auto` / `eevee` / `workbench`。`auto` は EEVEE Next が使えればそれを選ぶ |
| `--out-dir` | `unity/.../Assets/Models/Interiors` | FBX の出力先 |
| `--seed` | `20250921` | 小物のばらつきの乱数種。同じ種なら三角数まで完全に再現する |
| `--data-dir` | `unity/.../Assets/Data` | クエスト・収集物 JSON の場所。データが参照する `poi_*` が FBX に揃っているかの照合に使う |
| `--no-ext` | off | 窓の外の近景（`ext_<id>`、後述）を入れない |
| `--campus-dir` | `unity/.../Assets/Models/Campus` | 近景の元にする `campus.fbx` / `trees.fbx` の場所 |

所要時間（RTX 5070 Ti / Blender 4.5.10 LTS）:

- `--no-export` 全棟: **約 4 秒**（うち近景の元の読み込み約 1 秒。`--no-ext` なら約 1.4 秒）
- 書き出し全棟（読み戻しの検証を含む）: **約 10 秒**
- `--preview` 付き全棟: **246 秒**（うち図書館のレンダだけで 133 秒）

実行の最後に、書き出した FBX を bpy で**読み戻して** Empty の数と名前を照合する
（`[verify] OK <id> meshes=N+E empties=M/M 必須POI=K`、E は近景のメッシュ数）。
近景のメッシュも書いたぶんだけ揃っているかを数える。1 つでも欠けると `NG` を出して exit 1。
あわせて `Assets/Data/{Quests,Collectibles}/*.json` の中の `poi_<id>_<name>` 文字列を全部集め、
その棟の Empty に**データが参照する POI がすべて存在する**ことを契約として照合する
（無ければ `契約違反` と出して exit 1）。クエスト側で新しい POI 名を使い始めたら、
対応するプランに `c.poi(...)` を足してから流し直す。

同じ読み戻しのついでに、**外周が人の通れない壁で閉じているか**も測る
（`外周=閉` / `外周=穴3 計7.2m 最大2.6m(右(+X) z=4.4)`）。0.25 m より広い穴が 1 つでもあれば exit 1。
判定は `kcd_interior/closure.py`、単体で流すなら `blender/check_interior_walls.py`（後述）。

## 出力

- `unity/KatsushikaCampusDays/Assets/Models/Interiors/<id>.fbx` — 屋内と窓の外の近景
- 同 `<id>.json` — Unity 側の配置に要るメタデータ。キャンパス座標での入口位置 `entrance_world`、
  建物の向き `yaw_deg`、外周 `envelope`、壁厚、`spawn`、全 Empty のローカル座標、三角数、座席数。
  近景を入れたときは `ext`（`objects` = 近景のオブジェクト名、`triangles`、`radius`、`dz`）も入る。
- 同 `_summary.json` — 全棟の三角数・オブジェクト名・Empty 名・座席数・FBX サイズ。
  近景は `total_ext_tris` と棟ごとの `ext_tris` / `ext_objects` / `ext_stats`。`ext_stats` は
  `tris`（出どころ別の三角数。棟の外装は `bld`、木は `trees`）、`culled_tris`（裏を向くので
  入れなかった三角数。`site` = 木以外、`trees` = 木）、`plants`（入れた花壇の株の数。
  `full` = 元の形、`simple` = 簡略形）、`trees`（入れた木の本数 `placed` と、樹冠が外周に
  かかるので入れなかった本数 `skipped`）
- `docs/previews/interior_<id>.png` ほか（`--preview` のとき。全 14 枚 1280×720）

## 棟ごとの内容と規模

合計 **297,441 三角形**（上限 300,000）。窓の外の近景は別に数えて **172,246 三角形**（上限 180,000、
1 棟 50,000）。どれかを越えると `[budget] NG` を出して exit 1。FBX の大きさは近景を含む。

| id | 建物 | 三角形 | 近景 | FBX | Empty | 中身 |
|---|---|---:|---:|---:|---:|---|
| `research1` | 第1研究棟 | 40,439 | 25,633 | 0.89 MB | 80 | 1F ロビー（受付・ソファ・掲示板・立席島）+ EV 3 基 + 廊下 142 m + 研究室 2 室 + 教授室 |
| `lecture` | 講義棟 | 47,429 | 43,949 | 1.36 MB | 162 | 3 層吹き抜けの大階段ホール + **大ホール 600 席**（16/18/16 × 12 列）+ ホワイエ + 演習室 3 室 + 中教室 60 席 |
| `research2` | 第2研究棟 | 64,210 | 27,475 | 1.16 MB | 36 | 1〜2F 吹き抜けの**大食堂 1,400 席**（配膳カウンター 38 m・券売機 6 台・返却口・2F 回廊 240 席） |
| `kyoso` | 共創棟 | 25,544 | 26,239 | 0.78 MB | 87 | 1F カフェ（吹き抜け）+ コンビニ、2F ラウンジ（ロビーの階段から吹き抜けを通って上がる）。計 90 席 |
| `library` | 図書館 | 57,924 | 14,484 | 0.86 MB | 84 | 開架書架 14 連 + 閲覧長机 6 列 + 個人閲覧ブース 66 席 + 2F 回廊の吹き抜け（閲覧室から回廊へ階段）。計 299 席 |
| `gym` | 体育館 | 20,965 | 12,751 | 0.48 MB | 30 | アリーナ（28×15 m コート・ゴール 2 基・ステージ）+ 観覧席 540 席 + 屋根トラス + 用具庫 |
| `lab1` | 実験棟1 | 22,502 | 10,911 | 0.47 MB | 17 | 廊下（ロッカー・掲示板・自販機）+ 実験室 2 室 + 準備室 1 室（実験台・ドラフト・ボンベ・薬品庫・試薬棚・流し） |
| `lab2` | 実験棟2 | 14,300 | 6,597 | 0.33 MB | 10 | 廊下 + 実験室 1 室 + 準備室 1 室（同上） |
| `greenhouse` | 温室 | 4,128 | 4,207 | 0.19 MB | 5 | 切妻ガラス屋根・栽培ベンチ 2 列・鉢植え・灌水パイプ |

`lab2` と `greenhouse` はフットプリントが 23.7×15.8 m / 7.5×10.7 m しかなく、
20,000 三角形を積むと実在しない什器で埋めることになるため、下限を下回るのを許容している。

## 座標と Unity への置き方

FBX のローカル原点は **`entrance_<id>` の直下の床レベル**、Unity の **+Z が入口から建物奥へ**向く。
窓の外の近景は屋外と同じ場所に同じ形を持つので、屋内は屋外から離れた場所に置く
（Unity の `InteriorStage` は x = 1200 m から棟ごとに並べる）。

書き出しオプションは `docs/DESIGN.md` §3.3 と `build_campus.py` に合わせてある
（`axis_forward='-Z'`, `axis_up='Y'`, `bake_space_transform=True`, `global_scale=1.0`, `apply_scale_options='FBX_SCALE_ALL'`）。

メッシュは Unity の MeshCollider を個別に張れるよう分けてある:

- `floor_<id>` — 床スラブ
- `wall_<id>` — 外壁・間仕切り・建具・ガラス
- `furn_<id>_<nn>_<区画名>` — 区画ごとの什器（`furn_lecture_02_hall_seats` など）
- `ext_<id>` / `ext_<id>_trees` — 窓の外の近景（後述）。外周の外にしか無くプレイヤーは届かないので、
  当たり判定は要らない（Unity 側で `ext_` を当たり判定から外す作業は #60）

Empty の命名:

| 名前 | 用途 |
|---|---|
| `spawn_<id>` | 入口から 1.5 m 内側。入館時のプレイヤー位置 |
| `exit_<id>` | 退館位置 |
| `poi_<id>_<name>` | クエスト対象（`poi_library_desk`, `poi_kyoso_starbucks`, `poi_lecture_hall_stage`, `poi_gym_court`, `poi_research2_counter` ほか） |
| `npc_<id>_<n>` | NPC の立ち位置 |
| `sign_<id>_<n>` | TextMeshPro を貼る看板の位置 |
| `seat_<id>_<nn>` | 座れる家具（ソファ・ベンチ・ラウンジチェア）の外形の中心（床の高さ） |
| `seat_<id>_<nn>_f` / `_s` | 正面の辺の中点 / 側面の辺の中点。中心からの向きが座ったときの正面、距離が奥行き・幅の半分 |
| `seat_<id>_<nn>_a<k>` | 座る位置（床の高さ）。座面の前端から 0.20〜0.22 m 奥 |

`seat_` は `furniture.bench` / `sofa` / `lounge_chair` が記録し、`Ctx.flush_seats()` が書き出す。
Unity の `SeatFactory.PlaceInterior` がこれを読み、E で座る操作（`SeatInteractable`）と、
家具の外形いっぱいの見えない壁（高さ 1.9 m）を付ける。座面の高さ 0.38〜0.46 m は
CharacterController の stepOffset 0.4 m とほぼ同じなので、壁が無いと座る代わりに上に乗り上げる。

## 階段・座れる家具を置くときの決まり

- 階段（`shell.stair_flight`）の上に天井・上階スラブがあるときは、踏み面から **2.1 m** の頭上が取れない区間を
  すべて吹き抜けにする（`shell.floor` / `shell.ceiling` / `shell.ceiling_lights` の `holes`）。
  天井 3.9 m の下では、踏み面が 1.8 m を越えるあたりから先は吹き抜けが要る。
- 階段の上端は上階の床の縁にそろえ、その先に 1.2 m 以上の床を残す。吹き抜けの縁には手すりを付け、
  上がり口だけ手すりを切る。
- 座れる家具の前は、立ち上がった位置（アンカーから前へ 0.55 m、カプセル半径 0.31 m）を空けておく。
  テーブルなどを前に置くときは、立ち位置がテーブルに掛からない距離まで離す。
- **什器を「はしご」にしない（#45）**。`CharacterController` の `stepOffset` は 0.40 m なので、
  高さの差が 0.40 m 以内で重なる天端が続くと、そこを伝って登れてしまう。
  実際に見つかった登り口は 2 つ:
  - 個人閲覧ブース（`furniture.study_booth`）— 天板 0.73 → 側板 1.04 → 前板 1.28 と伝って
    通路へ 1.0〜1.3 m 落ちられた。側板は天板 +0.46 m 以上にして 1 段で届かなくした。
  - 温室の道具棚 — 段板のある書架（`F.bookshelf` h=1.60）が
    栽培ベンチ 0.78 → 本 1.16 → 段板 1.37 → 天端 1.60 のはしごになり、壁との隙間へ 1.6 m 落ちた。
    段の出ない**扉つきの箱**に差し替えた。
  新しい什器を足したら、天端が床から 0.45 m を越えるものは「隣の天端との差が 0.40 m 以内で
  並んでいないか」を見る。落下は `spawn_<id>` からの BFS（カプセル R=0.28・`stepOffset` 0.40・
  身長 1.62 m・格子 0.2 m）で測り、**1 m を越える落差は 0 か所**を保つ。

## 窓の外の近景（#84）

屋内の FBX には、窓から見える屋外も入れてある。屋外の `campus.fbx` / `trees.fbx`
（`build_campus.py` の出力。コミット済みのものを読む）を最初に 1 回だけ読み込み、棟ごとに
外周を四方へ 30 m（`exterior.RADIUS`）広げた矩形の中を切り出して建物ローカル座標へ写す。

- `ext_<id>` — 地面・道・水盤・花壇・ベンチ・照明柱・看板・自販機・ゴミ箱・隣の棟
- `ext_<id>_trees` — 木（屋外と同じ樹種・位置・大きさ）

屋外と同じ形・同じマテリアル名なので、窓から見える景色と外へ出たときの景色が一致する。
切り出し方（`kcd_interior/exterior.py`）:

- 外周の内側に入る部分は切り落とす。自分の棟の外装は外周から 1.5 m（`OWN_MARGIN`）以内を落とす。
  外装の窓割りは屋内と別に作っているので、残すと外壁やルーバーが屋内の窓の真ん前をふさぐ。
- 木は幹の位置で選び（幹から外周までが 30 m + 樹冠の半径以内）、切らずに丸ごと入れる。
  樹冠が外周 + 0.3 m（`TREE_MARGIN`）にかかる木は入れない（切ると断面が見える）。
- 建物の中（外周の矩形 × 床〜屋内の最高点の箱）のどこから見ても裏を向く面は入れない。
  キャンパスのマテリアルはすべて片面描画なので、室内からの見た目は変わらない。
  外から空撮すると樹冠に穴があき、隣の棟のルーバーが浮いて見えるのはこのため。
- 花壇の株は、外周から 12 m（`PLANT_FULL`）より遠いものを簡略形に替える。下の輪から天面へ
  1 段の側面で結び、花の代わりに天面をその株の花の色で塗る（1 株 54 → 19 三角形）。
- 全体を 0.03 m 下げ（`DZ`）、-0.045 m（`Z_MIN`）より下の頂点は持ち上げる。地面が屋内の床
  （z = 0）より下、Unity で屋内の下に敷く面（`OutsideGround` y = -0.06、x 2000 m までのスロットでは
  `CampusStage` の `OuterGround` y = -0.05）より上に来る。

屋外を作り直して `campus.fbx` / `trees.fbx` が変わったら、`build_interiors.py` も流し直す。
近景の三角数は屋内と別の予算で数える（`docs/DESIGN.md` §3.4）。`[budget] NG` が出たら
`RADIUS` か `PLANT_FULL` を見直す。

## 外周を閉じる決まり（#45）

Unity の `CharacterController`（PhysX）は MeshCollider の三角形を**片面でしか受け止めない**。
進む向きと同じ側を向いた面（裏面）はすり抜けるので、**外向きの 1 枚の面だけで張ったガラスは
室内から素通しになる**。窓の向こうには地面の板しか無いので、抜けると空の青だけの空間に出る。

- 外周に張るガラスは**厚みを持たせる**（`shell.GLASS_T = 0.04 m`、`add_prism`）。
  `shell.outer_wall()` の連窓はこれで室内側にも面がある。`add_quad` の 1 枚張りにしない。
- 入口の開口は `shell.glass_entrance()` が `0〜min(3.2, z_top-0.2)` しか塞がない。
  `shell.perimeter(door_top=...)` にその高さを渡し、上は辺の他の部分と同じ構成
  （腰壁・連窓・垂れ壁）で続ける。`extra_gaps` で開口を増やすときも高さを決める。
- 屋内モデルは中からしか見ない。屋根・天井のガラス（温室の切妻など）は**室内を向いた 1 枚**にする。
  両面にすると屋根が「立てる面」になり、外周壁の上端を越えて外へ出られる面ができる。
- 確かめ方: `blender/check_interior_walls.py`。書き出し済みの FBX を読み戻し、外周の帯へ
  室内側から水平レイを撃って、身長 1.60 m・半径 0.28 m・ジャンプ 1.10 m のカプセルが
  抜けられる区間を測る。`--two-sided` を付けると面の向きを無視するので、
  「形はあるが片面だから抜ける」のか「そもそも形が無い」のかを見分けられる。

```bash
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" \
  -b --python blender/check_interior_walls.py -- --ids all
# [walls] OK library     穴   0 か所  計    0.00 m  最大   0.00 m  床 -
```

既定の許容 0.25 m では 9 棟とも 0 か所（`[verify] … 外周=閉`）。`--max-gap 0.0` まで下げると
`kyoso` 0.45 m / `lab1` 0.20 m / `lab2` 0.10 m / `greenhouse` 0.15 m（計 0.90 m）が残るが、
いずれも屋根・棟木の継ぎ目（床の高さ 3.2〜8.6 m）で、BFS の最高到達 z
（lab1 0.90 / lab2 0.90 / greenhouse 1.07 / kyoso 5.12）より上にあり、プレイヤーは立てない。

## コード構成

| ファイル | 役割 |
|---|---|
| `build_interiors.py` | CLI・FBX 書き出し・再インポート検証・集計 |
| `check_interior_walls.py` | 書き出し済み FBX の外周が閉じているかを単体で測る（#45） |
| `kcd_interior/closure.py` | 外周の穴のレイ判定（build と check が共有） |
| `kcd_interior/exterior.py` | 窓の外の近景。`campus.fbx` / `trees.fbx` を読み、棟ごとに外周の外を切り出す（#84） |
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

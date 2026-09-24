# blender/ — 葛飾キャンパスの 3D モデル生成

`data/osm/campus.json`（実測フットプリント）から、Unity 用の `campus.fbx` / `trees.fbx` と
プレビュー PNG を **headless で** 生成する。`DESIGN.md` §3（ワールド）・§3.3（Blender 出力）が契約。

## 実行

```bash
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
    --python blender/build_campus.py -- --trees 700 --preview
```

（リポジトリのルートで実行する。`blender.exe` は Blender 4.5 LTS。）

### 引数

| 引数 | 既定 | 意味 |
|------|------|------|
| `--json` | `data/osm/campus.json` | 入力フットプリント |
| `--out-dir` | `unity/KatsushikaCampusDays/Assets/Models/Campus` | FBX の出力先（無ければ作る） |
| `--preview` | off | プレビュー PNG（1280x720）を 4 枚描画する |
| `--preview-dir` | `docs/previews` | プレビューの出力先 |
| `--trees` | 900 | 樹木の最大本数。重いときはここを下げる |
| `--engine` | `auto` | `auto` は EEVEE Next → 失敗したら Workbench |
| `--seed` | 20250920 | 樹木散布の乱数シード（同じ値なら同じ配置） |

### 所要時間（Windows 11 / Blender 4.5.10 LTS / `--trees 700 --preview` の実測）

| 工程 | 時間 |
|------|------|
| ジオメトリ生成 + FBX 書き出し | 約 1.2 s |
| プレビュー 4 枚（EEVEE Next） | 約 2.2 s（初回はシェーダコンパイルで 20 s ほど） |
| 合計 | **約 3.6 s** |

`--preview` 無しなら合計 1.5 s。10 分の上限に対して十分余裕がある。

## 出力

| ファイル | 内容 |
|----------|------|
| `unity/.../Campus/campus.fbx` | 建物・外構 18 メッシュ（入口の `bld_entrances` を含む）+ `entrance_<id>` / `door_<id>` / `sign_<id>` / `spawn_player` の Empty 28 個。約 1.62 MB / 126,680 頂点 |
| `unity/.../Campus/trees.fbx` | 樹木 3 種のメッシュ（計 240 頂点）と、それを参照する `tree_<n>` Empty 588 個 |
| `unity/.../Campus/trees.json` | 樹木インスタンスの (x, y, z, 樹種, スケール, 回転) 一覧。Unity 側で GPU インスタンシングしたい場合の元データ |
| `docs/previews/campus_{overview,mall,library,lecture}.png` | 1280x720 のプレビュー |

頂点数はキャンパス全体で 126,680（上限 200 万に対して 6.3 %）。`site_ground` を 30 m グリッドで割ったぶん（#30）増えている。うち入口 9 か所は 1,116 頂点 / 1,314 三角形。

## 構成

| ファイル | 役割 |
|----------|------|
| `build_campus.py` | 引数処理・組み立て・FBX 書き出し・プレビュー |
| `kcd_lib/geom.py` | 2D ポリゴン演算（重心・オフセット・内外判定・リサンプル） |
| `kcd_lib/mesh.py` | `MeshBuilder`。面を貯めて 1 オブジェクトに焼く |
| `kcd_lib/mats.py` | マテリアル定義。**名前が Unity 側との契約** |
| `kcd_lib/facade.py` | 階ごとの連続窓・カーテンウォール・ルーバー・パラペット・ピロティ柱列 |
| `kcd_lib/buildings.py` | 建物ごとの造形（第1研究棟・共創棟・講義棟・第2研究棟・図書館・体育館・実験棟・温室・背景建物） |
| `kcd_lib/entrances.py` | 入口（風除室・ガラスの両開き扉・庇・足元の石張り）と、Unity の入口トリガー・看板の位置の計画 |
| `kcd_lib/site.py` | 地面・キャンパスモール・道路と白線・水盤・ベンチ/照明柱・樹木配置 |
| `kcd_lib/props.py` | 樹木 3 種・ベンチ・照明柱・散布アルゴリズム |
| `kcd_lib/render.py` | ワールド・太陽・カメラ・レンダ設定 |

### 座標系

`campus.json` の点は `[x(東), z(北)]`。これをそのまま Blender の `(x, y)` に使い、Blender の
z を上方向とする。すべてのオブジェクトは**ワールド座標のまま**（オブジェクト原点は移動しない）で、
`bake_space_transform=True` / `axis_forward='-Z'` / `axis_up='Y'` で書き出すので、Unity 側では
`(x, y=Blender z=上, z=Blender y=北)` になる。

配置計算はキャンパス固有の直交座標 `(u, v)` で行う。`u` 軸は第1研究棟の最長辺の向き
（真東から **-24.55°**）で、キャンパスモールはこの `u` 軸に平行に `v = -24` を走る。

### 地面の高さレイヤと面の向き（#30）

地面まわりは「歩ける面どうしが段差にならない」ことを最優先にする。Unity の
`CharacterController` は 1 cm の段でも接地が外れれば落下 → 着地の判定を出すので、
z ファイティングを避けるためだけに数 cm ずらすと、歩いただけでガタガタする。

- **高さレイヤ**（`kcd_lib/site.py` 冒頭）: 外周の地面 `Z_GROUND = 0.000` から
  キャンパスモール `Z_MALL = 0.021` まで **3 mm 刻み**で積む。歩ける面どうしの段差は
  最大 2.1 cm。半径 0.28 m のカプセルに対して約 22 度の坂と同じで、そのまま越えられる。
  以前は 1.0〜7.5 cm 刻みで、さらに敷地の縁に 31 cm の落差（外周 -0.30）があった。
  **意図した段差は水盤の縁石（`Z_BASIN_RIM = 0.36`）だけ。**
  石張り（`kcd_lib/entrances.py` の `APRON_Z`）だけは別管理なので、ここを動かすときは合わせて見る。
- **面の向き**: `MeshBuilder.add_ribbon` は天面が +z、側面が外向きになるよう巻く。
  Unity は裏面を描かない（`_Cull: 2`）うえ、PhysX も裏面を床として扱わないので、
  下を向いた天面は「見えない・立てない線」になる。帯の端にも蓋を付ける。
- **大きい三角形を割る**: `MeshBuilder.split_by_grid(cell)` が `x = k·cell` / `y = k·cell` で
  全面を切る（Sutherland–Hodgman。向きとマテリアルは保つ）。`build_campus.py` は
  `SITE_GRID = 30.0` で `site_ground` を割ってから焼く。1,208 面 → 6,084 面、
  三角形の最長辺 989.9 m → 42.4 m。500 m を超える辺があると PhysX が
  "distance between any 2 vertices is greater than 500 units" を出し、当たり判定が荒れる。
- **焼いた後の検査**: `build_campus.py` の `check_site_ground()` が
  ①下を向いた水平面（法線 z < -0.99）が 1 枚でもある、②三角形の辺が
  `SITE_MAX_EDGE = 50.0` m を超える、のどちらかでビルドを失敗させる。
  成功すると `[site] check ok: up-facing N / M faces, ...` を出す。

### 入口（#39）

入れる建物 9 棟の入口は `kcd_lib/entrances.py` がまとめて作る。どの建物でも同じ形・同じ色
（紺の風除室 + 白い扉枠のガラス両開き + 裏が光る庇 + 足元の明るい石張り）にそろえて、
「ここから入れる」と一目で分かるようにしている。形・Unity のトリガー・看板は同じ計画
（`entrances.plan`）から出すので、見た目の扉とトリガーの位置が食い違わない。

- 扉の位置は `DOORS`（建物 id・探索の起点 (u, v)・建物へ向かう向き・扉を付ける面・看板の側）で決める。
  起点から外壁へレイを飛ばし、当たった壁の外向き法線を扉の正面にする。
- Empty: `door_<id>` = 扉の外面（風除室の先端）の中心、`entrance_<id>` = その 0.8 m 外の床。
  Unity の `CampusProps` は `door_` にトリガーを置き、`door_` → `entrance_` を「外」とみなす。
  高さは石張りの天端（0.12 m）。扉の前には庇があるので、Unity 側で上からのレイで接地させない。
- 扉の前（風除室の先から 8 m・半幅 3.6 m）には木・ベンチ・照明柱を置かない。自販機とゴミ箱
  （第2研究棟・共創棟）は扉の脇の看板と反対側に、外壁に背を付けて並べる。
- 入口以外が扉に見えないよう、地上階の全面ガラスには腰壁を立てる
  （`facade.add_curtain_wall(..., ground_sill=0.9)`、温室は 0.5 m）。地面から 0.45 m 未満まで
  下りている高さ 1.5 m 超のガラス面は、入口以外で 0 枚（以前は 235 枚）。
- マテリアル `wall_accent_navy` / `plastic_black` / `light_panel` は Unity 側の内装と同名のものを使う。

### 検証

`campus.fbx` を再インポートして bbox を確認した結果:

```
[json] research1 footprint  x 15.47..150.46   z(north) -149.38..-63.10   height 49.5
[fbx ] bld_research1        x 15.47..150.46   z(north) -151.20..-61.70   up 0.00..54.85
```

x は完全一致、北方向は 1.4〜1.8 m だけ外側に出るが、これはパラペットと屋上機械室の
張り出しぶんで設計どおり。高さは 49.5 m の躯体 + パラペット + 屋上機械室で 54.85 m。

## 既知の形状の逸脱

- **共創棟**: OSM のフットプリント（6 点・面積 722 m²・奥行 5.6〜6.8 m）を実測どおりに
  使う。第1研究棟の北面に全長で接するので、接する辺（`_hidden_edges` で自動判定）には壁を
  出さない。以前あった「モール側へ奥行 10.5 m まで広げる」逸脱は廃止した。
- 背景建物（`style == "background"`）は押し出し + 各階の窓（暗いガラスの四角。合計
  `BG_WINDOW_BUDGET` 枚以内になるよう間隔を自動調整）。1 メッシュにまとめている。
- 外構小物: 水面は `site_water`（Unity で反射用に別マテリアル）、看板は `site_props_signs`
  （`sign_<id>` Empty は板の位置・+X が板の正面）、モール北側の花壇は `site_props_beds`
  （縁石 + 土 + 花の株。歩道と北側の建物の入口の前は空ける。入口の駐輪場は実物に無いので置かない）、
  自販機・ゴミ箱は `site_props_vending` /
  `site_props_trash`（第2研究棟の食堂前と共創棟のコンビニ前）。

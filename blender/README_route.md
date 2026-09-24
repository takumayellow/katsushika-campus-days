# 西の回廊ジェネレータ（裏エンド「寮でぐーたら」/ #41）

`blender/build_route.py` は `data/osm/route.json` から、**北西門（x = -340, z ≒ 239.8）から
葛飾コミュニティハウス（寮, 重心 -464.52, 202.00）までの屋外**を手続き生成して
1 つの FBX に書き出す。

キャンパスの中身は `build_campus.py` が持っているので触らない。
**寮そのもの（外観・屋内）も別のジェネレータの担当**で、ここは敷地を空けたまま残す。

```
data/osm/route.json ──┬──> build_route.py ──> Assets/Models/Campus/route.fbx
                      │                        Assets/Models/Campus/route.json (sidecar)
                      └──> （寮は別担当）  ──> Assets/Models/Interiors/dorm.fbx
```

## 実行

```bash
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" \
  -b --python blender/build_route.py -- --preview --preview-dir docs/previews
```

Blender を起動しなくても、選別と寸法の検算だけは素の Python で走る:

```bash
python -c "import sys; sys.path.insert(0, 'blender'); from kcd_route import ground; ground.report()"
```

主なオプション:

| オプション | 既定 | 意味 |
|---|---|---|
| `--json` | `data/osm/route.json` | 入力 |
| `--out-dir` | `unity/.../Assets/Models/Campus` | FBX と sidecar の出力先 |
| `--name` | `route` | 出力名（`route.fbx` / `route.json`） |
| `--budget` | `14000` | 三角形の上限。超えたら例外で止める |
| `--window-budget` | `700` | 窓の数の上限。これに収まるまで窓の間隔を自動で広げる |
| `--no-lamps` | off | 街灯を置かない |
| `--preview` | off | プレビュー PNG を 4 枚レンダする |
| `--engine` | `auto` | `auto` / `eevee` / `workbench` |

## 出力先が `Assets/Models/Campus/` な理由

`CharacterImporter.IsCampus` が **`Assets/Models/Campus/` と `Assets/Models/Interiors/` しか**
キャンパス用の取り込み設定（マテリアル名の引き当て）に通さない。
`Assets/Models/route.fbx` に置くとマテリアルが全部灰色 (B0B0AC) になる。
Unity 側の `RouteStage.RouteFbx` も `Assets/Models/Campus/route.fbx` を見ている。

## 作るもの

| オブジェクト名 | 中身 | Unity 側の扱い |
|---|---|---|
| `route_ground` | 西へ足す地面の帯 + 道路 + 白線 | 名前に `ground` を含む → Ground レイヤ |
| `bld_route_background` | 沿道の建物 46 棟（躯体 + 窓） | 名前に `background` を含む → `glass*` を collider から落とす・NavMesh 除外 |
| `bld_route_fence` | ブロック塀・ガードレール・門柱 | `bld` 始まり → Building レイヤ |
| `route_props_lamp` | 街灯 | 通常の MeshCollider |

### 1. 地面の帯（`kcd_route/ground.py`）

`kcd_lib/site.py` の `build_ground` が `(-350..350)²` を `grass_dark` で 1 枚張っている。
帯はその**外側だけ**を埋める:

```
BAND = (-560.0, -350.0, -350.0, 350.0)    # x0, z0, x1, z1
```

- 東端は `-GROUND_E = -350.0` **ちょうど**。天面も `Z_GROUND = 0.000` ちょうど、
  マテリアルも同じ `grass_dark`。→ 継ぎ目は**重なり 0 m²・段差 0 m**。
- z の範囲も既存の地盤と完全に同じにして、継ぎ目が端から端までそろうようにしてある。
- スカート（側面）は付けない。既存の `site_ground` も付けていないし、
  Unity 側には ±2000 m の `OuterGround` が y = -0.05 に敷いてある。
- `split_by_grid(30.0)` で切る。三角形の最長辺は 42.4 m（PhysX 対策の上限 50 m 以下）。

### 2. 道路（`kcd_route/roads.py`）

`route.json` の `roads`（64 本）のうち、campus.json と重なるものを落として **24 本**だけ敷く。

```python
def excluded(road):
    return bool(road.get("in_campus_json")) or int(road.get("campus_shared_vertices") or 0) >= 2
```

落とす 40 本の内訳は `in_campus_json` が 24 本、頂点を 2 つ以上共有しているものが 16 本。
頂点の共有が 1 つだけのものは交差点を 1 点共有しているだけなので敷く。
**同じ道を二重に敷かない**のが目的（同じ高さレイヤなので z ファイティングになる）。

見た目は `site.build_paths` とそろえる。マテリアル名も高さレイヤも同じで、違うのは
キャンパス用の `Occupancy`（樹木散布の占有格子）を持たないことと、±348 m でクリップしないこと。
高さレイヤは `ground.py` に写してあるので、`build_route.py` の `check_layers()` が
毎回 `kcd_lib/site.py` と突き合わせて、ずれていたら止める。

### 3. 沿道の建物（`kcd_route/props.py`）

`route.json` の `buildings` 82 棟のうち `in_campus_json == false` の **46 棟**。
寮は `buildings[]` ではなく `data["dormitory"]` にいるので、ここには入らない。

**#45 の契約 — 当たり判定に使う面は必ず閉じた立体の面にする。**

- 躯体は `add_prism(loop, -0.30, h, wall, "roof_grey", wall)`。
  側面 + 屋根 + **底面**。開いた筒にしない。
- 窓は「壁から 4 cm 浮かせた厚み 0 の板」（`kcd_lib/buildings.py` の `_bg_windows` のやり方）を
  **使わない**。壁をまたぐ**厚み 6 cm の閉じた箱**（`add_slab`, 12 三角形）にする。
  こうしておけば、Unity が `glass*` を collider から落としても（`IsBackgroundNonSolidMaterial`, #50）
  壁の閉じ方は一切変わらない。
- 窓は**道から見える面にだけ**貼る（`facing_edges`）。辺の外向き 2.5 m の点が
  道から 40 m 以内、かつ内向きより道に近い辺だけ。356 辺 → 216 辺。
- 窓の数は `--window-budget` に収まるまで間隔を自動で広げる（2.4 → 12.0 m）。

### 4. 塀・ガードレール・門柱（`kcd_route/props.py`）

歩ける西区画 `ANNEX = (-490, 156, -340, 262)` の**南・西・北**を囲む。
東（x = -340）は Unity 側の見えない壁 `Wall_West` が兼ねるので、門柱と短い返しだけ立てる。

- **道の上には塀を立てない。**塀の線を 0.5 m 刻みで歩いて舗装の縁からの距離を見て、
  舗装 + 1.0 m の範囲を開ける。開けた所には**ガードレール**（閉じた立体）を立てて塞ぐ。
  横切る道だけでなく、塀の線と並走してしまう道も拾える。
- **建物が塀の代わりをしている所は開けっぱなし。**こちらはガードレールを立てないので、
  標本点ではなく**厳密な交点**で区間を求め、さらに両端を 0.30 m 縮めて塀を建物に食い込ませる。
  隙間ができる向きに誤差が出ないようにするため。
- 門柱は開口の端ぴったり（`gate_z ± gate_half_width` = z 227.83 / 251.83）。

## 検算（`build_route.py` が毎回やる。外れたら例外で止まる）

| 検算 | 中身 | 直近の値 |
|---|---|---|
| `ground.check()` | 敷く道 24 / 落とす道 40 / 建物 46、帯の寸法、門の位置、寮の敷地が空いているか | 隣家まで 0.96 m, 道まで 1.36 m |
| `check_layers()` | 写した高さレイヤが `kcd_lib/site.py` と一致するか | ok |
| `check_seam()` | 帯の東端 = -350.0、天面が全部 `Z_GROUND` | 段差 0.00000 m |
| `check_no_down_faces()` | 地面に下向きの水平面が無い / 三角形の辺 ≤ 50 m（#30） | 最長 42.4 m |
| `check_closed()` | 1 面にしか使われていない辺（穴の縁）の総延長（#45） | 全立体 **0 本 / 0.000 m** |
| `props.check_enclosure()` | 輪を 5 cm 刻みで歩いて、塀 / ガードレール / 建物 のどれかで塞がっているか | 開き **最大 0.15 m**（通れる幅 0.56 m 未満） |
| 三角形の予算 | `--budget` を超えたら止める | 15,529 / 16,000 |

`check_enclosure` の 0.56 m は Unity の `ActorFactory.BodyRadius = 0.28 m` の直径。
これより狭い隙間は CharacterController が通れない。

直近の内訳:

```
bld_route_background   5,960 verts   4,790 faces   9,112 tris
route_ground           1,347 verts     910 faces   1,820 tris
bld_route_fence          344 verts     302 faces     604 tris
route_props_lamp         224 verts     208 faces     352 tris
                                                   -------
                                                   11,888 tris   route.fbx 0.16 MB
```

窓 656 個で 7,872 三角形（全体の 66 %）。減らすなら `--window-budget` を下げる。

## sidecar（`Assets/Models/Campus/route.json`）

Unity の `Runtime/World/DormRoute.cs` の `JsonShape` が読む。
`JsonUtility` は平たい構造しか読まないので、`door` / `centroid` / `annex` は**数の配列**で書く。
知らない鍵は黙って飛ばされるので、人が読むぶんは `notes` にまとめてある。

```json
{
  "door": [-455.83, 183.24],
  "door_bearing": 159.5,
  "centroid": [-464.52, 202.0],
  "gate_z": 239.83,
  "gate_half_width": 12.0,
  "annex": [-490.0, 156.0, -340.0, 262.0],
  "notes": { ... }
}
```

値は `DormRoute.Default*` と同じ。**両方を同時に直すこと**（`kcd_route/ground.py` の
`ANNEX` / `GATE_HALF` / `GATE_CENTER_Z` / `WALL_X` にコメントで対応を書いてある）。
`DormRoute.IsPlausible` が筋の通らない値を弾いて既定値に戻すので、壊れた sidecar で
シーンが崩れることはない。

## プレビュー

`--preview` で 4 枚。`--preview-dir` の既定は `docs/previews/`。

| 名前 | 何を見るか |
|---|---|
| `route_front` | 北西門の外から寮のほうへ西を向いた目線 |
| `route_gate` | 逆向き。門柱と Wall_West の開口 |
| `route_annex` | 歩ける西区画の真上。塀の輪と、空けてある寮の敷地 |
| `route_overhead` | 回廊ぜんたい |

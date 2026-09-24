# 葛飾コミュニティハウス（寮）ジェネレータ

裏エンド「寮でぐーたら」(#41) の舞台。キャンパスの外、水戸街道ぞいの実在の建物。
**外観**（route.fbx に混ぜる）と**屋内**（独立した `dorm.fbx`）の 2 つを
`blender/kcd_route/dorm.py` が組み、`blender/build_dorm.py` が headless で回す。

形の数字（footprint・高さ・階数・玄関の点と方位）は **1 つ残らず
`data/osm/route.json` の `dormitory` から読む**。このリポジトリのどこにも
寮の寸法をベタ書きしない。

> **運営は共立メンテナンス（学生会館ドーミー）で、大学の直営ではない。**
> 見た目にも文言にも大学のブランド（`tus_green` など）を使わないこと。
> ゲーム内のセリフでも「大学の寮」と断定しない。

## 実行

```bash
"/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" \
  -b --python blender/build_dorm.py -- --mode both --preview --preview-dir <どこか>
```

| オプション | 既定 | 意味 |
|---|---|---|
| `--mode` | `both` | `exterior` / `interior` / `both` |
| `--json` | `data/osm/route.json` | 形の出どころ |
| `--out-dir` | `unity/.../Assets/Models/Interiors` | `dorm.fbx` と `dorm.json` の出力先 |
| `--exterior-fbx` | off | 外観を単体 FBX に出す。既定では**出さない**（外観の持ち主は route.fbx） |
| `--preview` / `--preview-dir` | off | `dorm_front` / `dorm_oblique` / `interior_dorm*` を 1280×720 でレンダ |
| `--no-export` | off | FBX を書かず数だけ測る。ただし読み戻し検証は飛ぶ |
| `--seed` | `20250923` | 小物のばらつき。同じ種なら三角形数まで再現する |
| `--report` | なし | 測った数値を JSON で書き出す |

所要（RTX 5070 Ti / Blender 4.5.10 LTS）: `--mode both` で 2.8 秒、`--preview` 付きで 17 秒。

**`_summary.json` は書かない。** 既存 9 棟の予算（298,267/300,000）には 1 三角形も足さない。
`dorm.fbx` は独立した予算（上限 30,000）。

## 測った値（2026-09-23）

| 項目 | 値 |
|---|---|
| 外観 三角形 | **3,210**（躯体 `bld_dorm` 2,400 + 付属 `bld_dorm_trim` 810） |
| 屋内 三角形 | **6,452 / 30,000** |
| 屋内 FBX | 0.15 MB / メッシュ 7 / Empty 45（読み戻して 45/45 一致） |
| 屋内の穴 | **0 件・総延長 0.000 m**（`kcd_interior/closure.py`） |
| footprint のずれ | 頂点 **0.0000 m** / 外へのはみ出し **0.0000 m** / 辺の欠け なし |
| 玄関の位置 | json `[-455.83, 183.24]` = 生成 `[-455.83, 183.24]`、**ずれ 0.0000 m** |
| 玄関の向き | json 159.5 度 = 生成 159.500 度（実測の辺 159.464 度、差 **0.0357 度**） |
| 外形 | 5 階 / 軒高 17.8 m（OSM `height`）/ 階高 4.00 + 3.45×4 / 辺の長さ 12.917, 22.094, 3.572, 19.895, **16.477（玄関の辺）**, 41.963 m |
| シルエットの高さ | 軒高 17.8 m の上にパラペットと塔屋が載るので、見た目の天辺はもっと高い。route.fbx を読み直した実測で `bld_dorm` **z −0.60〜18.75 m**（軒 17.8 + パラペット 0.95）、`bld_dorm_trim` **z 0.00〜20.90 m**（塔屋＋高架水槽） |

## 外観（`dorm.build_exterior`）

* 躯体コアは footprint を 0.45 m 内へ寄せた**閉じた角柱**（z −0.60〜17.78）。
  中には入れないので、外装はその上に貼る「ころも」。
* 1 階は `concrete_grey` + `glass_clear`（腰 1.00 / まぐさ 0.60 / 見込み 0.22）、
  2〜5 階は `concrete_light` + `glass_dark`（腰 1.05 / まぐさ 0.95 / 見込み 0.30）。
  窓の間隔 3.2 m。各階の境に見切り（`offset_polygon(loop, +0.12)`, 厚み 0.14）を回す。
* いちばん長い辺（41.963 m の辺 5）に 4 層ぶんのバルコニー。隔て板が「集合住宅」に見せる決め手。
* 屋上はパラペット 0.95 m + 塔屋 2.80 m + 高架水槽。
* 玄関は `kcd_lib/entrances.build_one`（庇・両開き・風除室）。銘板は壁付け板 + 自立サインの 2 つ。
  色は `wall_accent_navy` + `sign_plate`。**大学色は使わない。**
* 窓・壁・手すり・銘板はすべて `add_prism` の**厚みのある立体**。板 1 枚の面は
  PhysX が片面でしか受けず、室内から外へ素通りになる（#45）。

### route.fbx への混ぜ方

外観の持ち主は `route.fbx`（`blender/build_route.py`）なので、build_dorm.py は既定で
外観の FBX を書かない。`build_route.py` 側に数行足す差分が要る。手順は
`blender/kcd_route/dorm.py` の docstring と、当てる差分の控えを参照。要点だけ:

```python
from kcd_route import dorm, ground, props, roads

dmb = MeshBuilder(dorm.SHELL_OBJ)          # "bld_dorm"
tmb = MeshBuilder(dorm.TRIM_OBJ)           # "bld_dorm_trim"
stats["dorm"] = dorm.build_exterior(ground.dorm(data), dmb, tmb)[2]
solids = [h_obj, f_obj, dmb.to_object(coll), tmb.to_object(coll)]
```

注意が 2 つ:

1. **三角形の上限**。route.fbx は今 11,888 / 14,000。外観 3,210 を足すと 15,098 なので、
   `--budget` を 16,000 に上げるか `--window-budget` を 700 → 500 に下げる。
2. **`check_closed` から外観を外す**。外装は `kcd_lib/facade.py` と同じ
   「パネルを積む」作りなので、水平目地に面 1 枚ぶんの縁が残る（campus.fbx の建物も全部同じで、
   `build_campus.py` はそもそもこの検査をしていない）。躯体コアは閉じた立体で中には入れないため、
   辺の多様体判定である `check_closed` の対象からは外装だけ外す。
   実測: `bld_dorm` 縁 852 本 / 2,569.8 m、`bld_dorm_trim` 縁 146 本 / 181.0 m。
   z を見ると全部 1.00 / 3.40 / 5.05 / 6.50 … の**階ごとの目地の高さ**に並ぶ。

Unity 側（`DormStage.BuildEntrance`）は玄関を route.json の `door` / `door_bearing` から置くので、
`door_dorm` / `entrance_dorm` の Empty は要らない。

## 屋内（`dorm.build` / `dorm.make_spec`）

16.0 × 38.0 m、天井 2.70 m。原点は `entrance_dorm` の真下の床、+Y が奥。
既存 9 棟と同じ `kcd_interior` の Ctx / InteriorSpec 契約に乗るので、Unity 側は同じ名前を読めばよい。

```
 y=40.5 ┌─────────── 行き止まり（EV・掲示板・ロープ）───────────┐
        │        居室の扉（見た目だけ。開口は開けていない）×10        │
 y=23.0 ├── ラウンジ ──┬── 中廊下 ──┬── 食堂 ──────────────┤
 y=12.6 ├──────────┴─ 引き戸 ─┴──────────────────┤
        │  玄関ホール（郵便受け・掲示板・自販機・ベンチ）  管理人室   │
  y=2.8 └──────────── 玄関（ガラススクリーン）────────────┘
```

* **寮長**は玄関ホール、管理人カウンターの手前 (2.60, 9.60)。
  Empty は `npc_dorm_1` と **`npc_dorm_head`** の 2 つを同じ位置に出す。
  Unity の `DormStage.NpcEmpty` は `"npc_" + DormRoute.DialogueId` = `npc_dorm_head` を探すが、
  既存 9 棟の契約は連番の `npc_<id>_<n>` なので、両方に応える。
* プレイヤーの出現は `spawn_dorm` (0, 4.6)、外へ戻るのは `exit_dorm` (0, 3.35)。
* POI は `poi_dorm_kanrinin`（管理人カウンター）/ `poi_dorm_lounge` / `poi_dorm_shokudo` /
  `poi_dorm_corridor_end`。サインは `sign_dorm_1`〜`4`（Unity はここに点光源を吊る）。
* 中廊下の引き戸は `shell.door(..., ang=±pi/2)`。建具の向きは「面の法線 = +Y を ang 回転」なので、
  Y 方向に走る壁に `ang=0` で置くと**引き戸が廊下を横切って立つ**。プレビューで一度やった。
* 間仕切りの天端は `Z_PART = 3.20`、外周壁の天端は `Z_TOP = 4.45`。
  **外周壁は間仕切りより 1.10 m（ジャンプの高さ）以上高くしておくこと。**
  同じ高さだと「天井裏の間仕切りの上に立って外へ出られる」と判定され、
  接合部ごとに 0.20 m の隙間（計 1.800 m）が残る。窓の上端 `Z_WIN_TOP = 2.55` は天井より下。

## 出力

| ファイル | 中身 |
|---|---|
| `unity/.../Assets/Models/Interiors/dorm.fbx` | 屋内。メッシュ 7 + Empty 45 |
| `unity/.../Assets/Models/Interiors/dorm.json` | サイドカー。`operator` / `not_university` / `off_campus` / `minimap` / `roles` / `exterior` を持つ |

サイドカーの `minimap: "black"` と `off_campus: true` は #41 の仕様
（キャンパス外なのでミニマップは黒のまま）を Unity 側に渡すためのもの。

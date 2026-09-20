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
| `unity/.../Campus/campus.fbx` | 建物 12 メッシュ + `entrance_<id>` / `sign_<id>` / `spawn_player` の Empty 18 個。約 1.07 MB / 67,620 頂点 |
| `unity/.../Campus/trees.fbx` | 樹木 3 種のメッシュ（計 240 頂点）と、それを参照する `tree_<n>` Empty 589 個 |
| `unity/.../Campus/trees.json` | 樹木インスタンスの (x, y, z, 樹種, スケール, 回転) 一覧。Unity 側で GPU インスタンシングしたい場合の元データ |
| `docs/previews/campus_{overview,mall,library,lecture}.png` | 1280x720 のプレビュー |

頂点数はキャンパス全体で 67,620（上限 200 万に対して 3.4 %）。

## 構成

| ファイル | 役割 |
|----------|------|
| `build_campus.py` | 引数処理・組み立て・FBX 書き出し・プレビュー |
| `kcd_lib/geom.py` | 2D ポリゴン演算（重心・オフセット・内外判定・リサンプル） |
| `kcd_lib/mesh.py` | `MeshBuilder`。面を貯めて 1 オブジェクトに焼く |
| `kcd_lib/mats.py` | マテリアル定義。**名前が Unity 側との契約** |
| `kcd_lib/facade.py` | 階ごとの連続窓・カーテンウォール・ルーバー・パラペット・ピロティ柱列 |
| `kcd_lib/buildings.py` | 建物ごとの造形（第1研究棟・共創棟・講義棟・第2研究棟・図書館・体育館・実験棟・温室・背景建物） |
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

### 検証

`campus.fbx` を再インポートして bbox を確認した結果:

```
[json] research1 footprint  x 15.47..150.46   z(north) -149.38..-63.10   height 49.5
[fbx ] bld_research1        x 15.47..150.46   z(north) -151.20..-61.70   up 0.00..54.85
```

x は完全一致、北方向は 1.4〜1.8 m だけ外側に出るが、これはパラペットと屋上機械室の
張り出しぶんで設計どおり。高さは 49.5 m の躯体 + パラペット + 屋上機械室で 54.85 m。

## 既知の形状の逸脱

- **共創棟**: OSM のフットプリントは奥行 5.6 m（面積 722 m²）しか取れておらず、11F・47 m の
  建物として成立しない。`buildings.build_kyoso` でモール側（+v）へ **奥行 10.5 m** まで
  広げた板状棟として作っている。これがフットプリントに対する唯一の意図的な逸脱。
- 背景建物（`style == "background"`）は押し出しのみ・窓なし。1 メッシュにまとめている。

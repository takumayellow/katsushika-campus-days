# 温室 作り直し仕様

Issue #114。調査日 2026-10-13。資料の画像はリポジトリに入れない（大学の著作物）。ここには URL と読み取った内容だけを書く。`docs/ref/katsushika_map.png` は前からリポジトリにある公式マップと同じ画像。

確からしさ: **高** = 公式資料・計測データをそのまま読んだもの。**中** = 縮尺の無い図や空中写真から推定したもの。**低** = ゲームの都合で決めたもの。

## 資料

| 略号 | 資料 | 出典 URL |
|---|---|---|
| MAP | TUS LIFE 葛飾キャンパスのキャンパスマップ（鳥瞰のイラスト）。`docs/ref/katsushika_map.png` と同じ画像 | https://www.tus.ac.jp/tuslife/campus/katsushika/ 、画像 https://www.tus.ac.jp/tuslife/campus/katsushika/img/katsushika_map.png |
| PL | PLATEAU 葛飾区 2025 の建物 13122-bldg-97017（3D Tiles の `data/data50.b3dm` の属性） | https://assets.cms.plateau.reearth.io/assets/75/8a1ab9-084d-4d29-9ac9-3591a73267c8/13122_katsushika-ku_pref_2025_citygml_1_op_bldg_3dtiles_13122_katsushika-ku_lod2/tileset.json |
| OSM | OpenStreetMap way 1552188506（輪郭と高さ） | https://www.openstreetmap.org/way/1552188506 、履歴 https://api.openstreetmap.org/api/0.6/way/1552188506/history |
| AIR | 国土地理院の空中写真 | https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg 、https://cyberjapandata.gsi.go.jp/xyz/ort/{z}/{x}/{y}.jpg |
| NODA | 理科大の薬用植物園（野田キャンパス。葛飾の温室とは別） | https://www.ps.noda.tus.ac.jp/utility/garden/ |

## 正体

| 項目 | 内容 | 確からしさ | 根拠 |
|---|---|---|---|
| 用途 | 温室。MAP に「温室」と書かれた棟がある | 高 | MAP |
| 位置 | 第1実験棟の東どなり（8.5 m 東）、駐車場の近く。第2実験棟は北西にあり、温室とは接しない | 高 | MAP、OSM と `data/osm/campus.json` の位置 |
| PLATEAU の属性 | measuredHeight 3.4 m、用途は「不明」、LOD2 の形は無い（LOD1 の箱だけ） | 高 | PL |
| OSM の履歴 | v1 2026-08-25、`building`・`height=3.4`、`source=MLIT_PLATEAU`。それより前の版は無い | 高 | OSM |
| 屋内の写真 | 見つからない。大学の施設紹介・ブログ・動画にも出てこない | — | — |

## 形

| 項目 | 内容 | 確からしさ | 根拠 |
|---|---|---|---|
| 平面 | 外接矩形 10.74 × 7.47 m（長辺が西面と東面）。北東の角が 2.03 × 3.48 m 欠けた L 字 | 高 | OSM の輪郭（`campus.json`） |
| 高さ | 3.4 m | 高 | PL、OSM |
| 屋根 | 陸屋根。白い枠で区切ったガラスの格子 | 中 | MAP の絵 |
| 壁 | 全面ガラス。白い縦の方立が密に並ぶ。棟の片側が一段引っ込む（欠けた角） | 中 | MAP の絵 |
| 屋根の見え方 | 空中写真では淡い灰色。ガラスかどうかは判別できない | 中 | AIR |

## ゲームでの作り

| 項目 | 決めたこと | 確からしさ |
|---|---|---|
| 外装 | L 字のガラス箱。ガラス壁の上端 3.15 m、白い鼻隠しで 3.4 m。屋根は平らなガラスに白い桟の格子 | 中（MAP）、低（寸法の割り振り） |
| 入口 | 西面（歩道側、長辺）の中央。#108 でそろえたまま | 低 |
| 屋内 | 天井（屋根ガラスの下面）3.05 m。白い梁の格子、栽培ベンチ 4 台（中央通路の両わきに 2 列 + 東の壁ぎわ 1 列 + 欠けた角に沿った袖に 1 台）、床置きの鉢 3 つ、灌水パイプ（入って右の隅に立て管と元栓）、流し、道具棚、育成灯 | 低（屋内の資料は無い） |
| クエスト文 | 「第1実験棟の東どなりにある、陸屋根のガラスの温室」 | 高（位置と屋根は MAP） |

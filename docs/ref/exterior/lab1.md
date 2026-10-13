# 第1実験棟 外観

Issue #240。調査日 2026-10-13。資料の画像はリポジトリに入れない（大学・撮影者の著作物）。ここには URL と読み取った内容だけを書く。`docs/ref/katsushika_map.png` は前からリポジトリにある公式マップと同じ画像。

確からしさ: **高** = 公式資料・計測データ・空中写真をそのまま読んだもの。**中** = 縮尺の無い絵や斜めの写真から推定したもの。**低** = 写真が無く、ゲームの都合で決めたもの。

## 資料

| 略号 | 資料 | 出典 URL |
|---|---|---|
| MAP | TUS LIFE 葛飾キャンパスのキャンパスマップ（北東から見た鳥瞰のイラスト）。`docs/ref/katsushika_map.png` と同じ画像 | https://www.tus.ac.jp/tuslife/campus/katsushika/ 、画像 https://www.tus.ac.jp/tuslife/campus/katsushika/img/katsushika_map.png |
| HELI21 | ヘリコプター遊覧のブログ（2021-08、Ⅱ期計画の着工）。上空からの斜めの写真 | http://building-pc.cocolog-nifty.com/helicopter/2021/08/post-521c28.html 、画像 https://building-pc.cocolog-nifty.com/helicopter/images/tokyokatsushika210811.jpg 〜 tokyokatsushika210815.jpg |
| HELI24 | 同じブログ（2024-09、薬学部の移転）。Ⅱ期計画の完成予想図（北東から見た鳥瞰）を含む | http://building-pc.cocolog-nifty.com/helicopter/2024/09/post-715540.html 、画像 https://building-pc.cocolog-nifty.com/helicopter/images/tokyokatsushika24011.jpg 、tokyokatsushika24012.jpg |
| AIR | 国土地理院の空中写真（真上から） | https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg 、https://cyberjapandata.gsi.go.jp/xyz/ort/{z}/{x}/{y}.jpg 、切り出しは `docs/ref/aerial/` |
| OSM | OpenStreetMap way 565475708（輪郭）。階数と高さは `data/osm/campus.json`（4 階・17.0 m） | https://www.openstreetmap.org/way/565475708 |
| PL | PLATEAU 葛飾区 2025 の建物 13122-bldg-96895（3D Tiles の `data/data50.b3dm` の属性） | https://assets.cms.plateau.reearth.io/assets/75/8a1ab9-084d-4d29-9ac9-3591a73267c8/13122_katsushika-ku_pref_2025_citygml_1_op_bldg_3dtiles_13122_katsushika-ku_lod2/tileset.json |
| NK | 日建設計の作品紹介（葛飾キャンパス） | https://www.nikken.co.jp/ja/projects/education/tokyo_university_of_science.html |

MAP と HELI24 の完成予想図は、どちらも北東から見ている。絵の左の面が第1実験棟の東面、右の面が北面にあたる。

## 正体

| 項目 | 内容 | 確からしさ | 根拠 |
|---|---|---|---|
| 位置 | キャンパスの北寄り。北どなり（約 1.3 m）に第2実験棟、東どなりに温室、西に体育館 | 高 | OSM、`campus.json` |
| 階数・高さ | 4 階建て・17.0 m | 高 | OSM、`campus.json` |
| PLATEAU の属性 | 第1実験棟と第2実験棟を 1 つの LOD1 の箱にまとめている。measuredHeight 23 m、地上 4 階、調査 2021 年。LOD2 の形は無い | 高 | PL |
| 他の資料 | Wikimedia Commons と NK には第1実験棟が写った使える写真が無い | — | NK、Commons の Tokyo University of Science カテゴリ |

## 形

| 面・部位 | 内容 | 確からしさ | 根拠 |
|---|---|---|---|
| 東面 | 赤茶のレンガ（タイル）の壁。窓は無い | 高 | MAP、HELI24 |
| 東面の外階段 | 壁の南から 5 割強の位置に縦長の切り込みがあり、2〜4 階に片持ちのコンクリートの踊り場が出る | 中 | MAP、HELI24 |
| 北面 | 白い縦フィンが細かく並ぶ格子窓。各階の床の位置に白い帯。4 層 | 高（材と色）、中（フィンの間隔） | MAP、HELI24 |
| 南面 | 斜めの写真が無い。長辺なので北面と同じ格子窓とした | 低 | — |
| 西面 | 写真が無い。短辺なので東面と同じレンガとした | 低 | — |
| 庇 | 南面の外に幅約 2.3 m の明るい帯が東西に続く。2 階の床の高さの庇と見た | 中（あること）、低（高さ） | AIR |
| 入口 | 南面。#108 の扉の位置（u 56.2）のまま | 高（#108 で実測済み） | `entrances.DOORS` |
| 屋上 | 灰色の陸屋根に設備が並ぶ。緑化は無い（緑の屋上は第2実験棟）。北の縁に沿った長い明るい箱、南寄りの長い白い箱と中くらいの箱、室外機の列が 2 か所、北西の隅に白い円形の機器 | 高 | AIR |
| 西の別棟 | 本体の南西に、屋根が一段低い小さな棟がつく。1 階建てと見た | 中（低い屋根）、低（階数） | AIR、OSM の輪郭 |

## ゲームでの作り

`blender/kcd_lib/buildings.py` の `build_lab1`。第2実験棟は今までどおり `build_lab_low` で作り、見た目は変えていない。

| 項目 | 決めたこと | 確からしさ |
|---|---|---|
| 本体と別棟の分け方 | 輪郭の西側の 2 点（u 41.5 / v 51.5 と u 41.7 / v 46.2）で切り、西の小さな部分を別棟、残りを本体とする | 中（AIR） |
| 東西の面 | `brick_red` の壁を地面から屋上まで。窓なし | 高（東面）、低（西面） |
| 外階段 | 東の最長の辺の南から 55% の位置に幅 3.4 m・奥行き 1.6 m の切り込み。2 階から上を凹ませ、奥の壁に窓、各階に踊り場（外へ 1.5 m）と手すり。1 階はふさいで小窓 | 中 |
| 南北の面 | 格子窓（区画 2.4 m）。2〜4 階に白い縦フィン（間隔 1.2 m・出 0.40 m）。1 階は窓台を高くし（#39）、フィンなし | 中（北面）、低（南面） |
| 庇 | 南の最長の辺に、2 階の床の高さで出 2.0 m のコンクリートの庇。入口の前（±3.8 m）は空けて、入口の庇とぶつけない | 低 |
| 屋上 | パラペット 1.0 m（格子の面は白、レンガの面はレンガ色）。北の長い箱、南の長い箱と中くらいの箱、室外機 2 × 5 と 3 × 4、北西の円筒 | 中（配置は AIR、寸法は推定） |
| 別棟 | 1 階建て・4.6 m。外周の面に窓、陸屋根とパラペット。本体の壁との境は本体側のレンガで閉じる | 低 |
| 当たり判定 | 地面の高さの輪郭は OSM の輪郭のまま。張り出し（踊り場・庇）は 2 階の床より上だけ | — |
| 看板 | 店名・人名は出さない | — |

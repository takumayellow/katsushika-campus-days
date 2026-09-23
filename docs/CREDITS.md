# クレジット・帰属表示

## 地図データ

キャンパスの建物フットプリント・通路・緑地は **OpenStreetMap** のデータを Overpass API 経由で取得し、
`tools/osm_extract.py` で `data/osm/campus.json` に変換したものです。

キャンパスの外に出て歩く道と沿道の建物（隠しエンドで行く学生寮までの経路）も同じく OpenStreetMap から取得し、
`tools/osm_route.py` で `data/osm/route.json` に変換しています。建物の高さ・階数は OSM の `height` /
`building:levels` タグ（国土交通省 PLATEAU 由来）をそのまま使っています。

> © OpenStreetMap contributors — データは [Open Database License (ODbL) 1.0](https://opendatacommons.org/licenses/odbl/1-0/) の下で提供されています。
> https://www.openstreetmap.org/copyright

ゲーム内のタイトル画面・クレジット画面と、この README に同じ帰属表示を掲載します。

## キャラクター

- みらい・いなり・かなめ・そら・教授はこのプロジェクトのオリジナルキャラクターです。
- 坊っちゃん・マドンナちゃんは東京理科大学の公式マスコットをモチーフにした、非公式・非営利のファンアート的 3D 解釈です。
  公式画像・公式データは使用していません（`docs/ref/` の参考画像は配色とシルエットの参照のみ）。
  本プロジェクトは東京理科大学とは無関係で、大学の公式見解を示すものではありません。

## 3D モデル・テクスチャ・音声

すべて本リポジトリの Python スクリプト（Blender bpy / numpy）で手続き生成しています。外部アセットは使用していません。

## フォント

- Noto Sans JP（Google Fonts、SIL Open Font License 1.1）。`unity/KatsushikaCampusDays/Assets/Fonts/NotoSansJP-VF.ttf` として同梱し、TextMeshPro のフォントアセットを生成しています。

## ソフトウェア

- Unity 6000.6.2f1（Unity Personal）、Universal Render Pipeline、Input System、Cinemachine、AI Navigation、TextMeshPro
- Blender 4.5 LTS
- Unity 公式 agent skills（Unity-Technologies/skills、`skills-lock.json` で固定）

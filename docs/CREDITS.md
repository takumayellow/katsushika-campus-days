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

### 航空写真（色の参考）

地面・舗装・芝の色を決めるときに見た航空写真（`docs/ref/aerial/`）は、国土地理院の地理院タイル（電子国土基本図（オルソ画像））をつなぎ合わせて切り出したものです。ゲームには入っていません。

> 出典：国土地理院 電子国土基本図（オルソ画像）を加工して作成

## キャラクター

- みらい・いなり・かなめ・そら・教授はこのプロジェクトのオリジナルキャラクターです。
- 坊っちゃん・マドンナちゃんは東京理科大学の公式マスコットをモチーフにした、非公式・非営利のファンアート的 3D 解釈です。
  公式画像・公式データは使用していません（`docs/ref/` の参考画像は配色とシルエットの参照のみ）。
  本プロジェクトは東京理科大学とは無関係で、大学の公式見解を示すものではありません。

## 3D モデル・テクスチャ

建物・外構・キャラクター・建物内部の 3D モデルは、本リポジトリの Blender (bpy) スクリプトで手続き生成しています。
キャラクターの顔と絣の模様（`blender/kcd_chara/tex.py`）、ミニマップの画像（`tools/render_minimap.py`・`MinimapAssets.cs`）も同じくスクリプトで描いています。

地面と外壁の面のテクスチャ（`unity/KatsushikaCampusDays/Assets/Textures/surfaces/`）には **ambientCG** の素材を使っています。
`tools/fetch_textures.py` が `data/textures/surfaces.json` に書いた素材を取得し、平均色を `MaterialLibrary.cs` の
CampusColors に合わせて焼き直したものです。

| 素材 | 使っている面 |
| --- | --- |
| [PavingStones136](https://ambientcg.com/a/PavingStones136) | stone_light・stone_dark（舗装） |
| [Asphalt033](https://ambientcg.com/a/Asphalt033) | asphalt（車道・駐車場） |
| [Grass004](https://ambientcg.com/a/Grass004) | grass・grass_dark（芝地） |
| [Ground048](https://ambientcg.com/a/Ground048) | bed_soil（花壇の土） |
| [Concrete034](https://ambientcg.com/a/Concrete034) | concrete_light・concrete_grey・concrete_dark（外壁） |
| [Bricks101](https://ambientcg.com/a/Bricks101) | brick_red（煉瓦の面） |

> Created using PavingStones136, Asphalt033, Grass004, Ground048, Concrete034 and Bricks101 from ambientCG.com, licensed under the Creative Commons CC0 1.0 Universal License.

ambientCG の素材は [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) で配布されていて、表記は義務ではありません
（[ambientCG のライセンス](https://ambientcg.com/license)、2026-09-24 確認）。どの素材を使ったかを辿れるように載せています。

## 音声

効果音と環境音は本リポジトリの Python スクリプト（numpy）で合成しています。
一部の BGM は東京理科大学校歌（作曲 大和憲史）をもとにしていて、タイトル画面の曲は東北きりたん（NEUTRINO）の歌唱です。
出典と権利確認の状況は `unity/KatsushikaCampusDays/Assets/Audio/README.md` の「校歌について」を参照してください。

## フォント

- Noto Sans JP（Google Fonts、SIL Open Font License 1.1）。`unity/KatsushikaCampusDays/Assets/Fonts/NotoSansJP-VF.ttf` として同梱し、TextMeshPro のフォントアセットを生成しています。

## ソフトウェア

- Unity 6000.6.2f1（Unity Personal）、Universal Render Pipeline、Input System、Cinemachine、AI Navigation、TextMeshPro
- Blender 4.5 LTS
- Unity 公式 agent skills（Unity-Technologies/skills、`skills-lock.json` で固定）

# 金町駅北口から葛飾キャンパス入口まで（#443）

調査日: 2026-11-11。現地調査・Unity 実機確認は未実施。

## 目的と範囲

JR 金町駅を降り、北口から東京理科大学葛飾キャンパスへ歩いて入る経路をゲームに追加する。まず大学が示す通学路を一本通せるようにする。駅構内やキャンパス内の既存建物はこの作業の範囲に含めない。

## 根拠資料

| 資料 | URL | この作業で確認できること |
| --- | --- | --- |
| 東京理科大学「葛飾キャンパスまでの通学路と注意点」 | https://www.tus.ac.jp/tuslife/campus/katsushika/schoolroutemap.pdf | JR 金町駅からキャンパスへの赤い点線の通学路、別経路、交差点・歩道などの注意 |
| 東京理科大学「葛飾キャンパス」 | https://www.tus.ac.jp/tuslife/campus/katsushika/index.html | 敷地と建物の配置、キャンパスモールの位置 |
| 東京理科大学「葛飾キャンパス 交通アクセス」 | https://www.tus.ac.jp/access/katsushika_campus/ | 金町駅からの徒歩アクセス |
| JR 東日本「金町駅 構内図・バリアフリー情報」 | https://www.jreast.co.jp/estation/stations/462.html | 改札と北口の位置 |
| OpenStreetMap | https://www.openstreetmap.org/copyright | 道路・歩道の形の候補。使用時は ODbL と地図データの帰属を守る |

資料の地図画像そのものをゲームに貼らない。道の形は、地図データと現地で確認した折れ線から生成する。

## 確定していること

- 大学の通学路図は、JR 金町駅の北口と葛飾キャンパスを結ぶ赤い点線の経路を示している。
- 大学は金町駅から徒歩約 8 分と案内している。
- リポジトリの座標系は `tools/osm_common.py` で共有され、原点は敷地ポリゴンの重心、x は東、z は北、単位は m。
- `data/osm/route.json` と `blender/kcd_route/`、Unity の `RouteStage` は #41 の寮への道のためにある。駅からの道には流用できる仕組みがあるが、経路そのものは別に定義する。
- `Assets/Scenes/Campus.unity` は `SceneBuilder.BuildAll` で生成する。新しい道をシーンだけに手で置くと再生成で失われる。

## 要確認（形を確定する前に必要）

| 項目 | 確認方法 |
| --- | --- |
| 北口のゲーム内出発点 | JR 構内図と現地で出口の外側を確認し、OSM 座標に対応付ける |
| 通学路図の赤い点線に沿う各曲がり角 | 道路・歩道の OSM 折れ線と、北口から入口までの連続動画を照合する |
| 横断歩道・信号・歩道幅・段差 | 現地の写真と動画。実測できない幅は数値を断定しない |
| 終点のキャンパスへの入り方 | 大学のキャンパスマップと現地の入口表示を、既存シーンの正門位置と照合する |
| 周辺建物・看板の外観 | 公開資料または人の顔が写らない現地写真。資料が無いものは詳細を作らない |
| 既存の #41 ルートとの重なり | `data/osm/campus.json` と `route.json` の way・頂点を照合し、道路を二重に敷かない |

## 実装に入るときの確認点

1. 新しい OSM 抽出は `tools/osm_common.py` の投影を再利用し、駅ルートの元データと出典・取得日を分けて残す。`campus.json` 全体の再抽出は既存の建物座標を揺らすため、駅ルート分だけを追加する。
2. Blender では既存の `kcd_route` の地面・道路・背景建物の生成規約を参考にする。新経路を #41 の寮ルートに混ぜず、別のモデルと sidecar にする。
3. Unity では Editor スクリプトからモデル・当たり判定・境界・ミニマップを追加し、`SceneBuilder.BuildAll` で再生成できるようにする。
4. 公式通学路に沿って駅からキャンパスまで歩く Unity 実機確認を行い、PR に確認済みと要確認を分けて書く。

現地での記録項目と撮影地点は [field_survey.md](field_survey.md) にまとめる。

## 現地撮影メモ

北口の外、各曲がり角の前後、横断箇所、理科大大学通り、キャンパス入口を進行方向と逆方向から撮る。北口から入口までの連続動画も一本撮り、曲がり角の順序を記録する。人の顔が写らないようにし、写った場合は共有前に顔が分からないよう処理する。

## コード上の接続点（2026-11-11 確認）

- `SceneBuilder.BuildCampus` は `CampusStage.Build` → `WorldBoundsStage.Build` → 寮の `RouteStage.Build` → `CampusStage.BakeNavMesh` の順。駅ルートをモデルとして追加する場合は、NavMesh を焼く前に地面と境界を配置する。既存の寮ルートは `route.fbx` が無いと何もしない。
- `WorldBounds.DefaultArea` は x/z とも −340〜340 m の矩形。 `WorldBoundsStage` が四辺に見えない BoxCollider を作り、実行時の `WorldBounds` も同じ範囲を見張る。駅方面がこの範囲を越える場合、壁の開口・追加区画の囲い・実行時の見張りをセットで変える。境界を外すだけの変更はしない。
- 寮ルートは `RouteStage.OpenWestGate` と `BuildAnnexWalls`、`WidenWorldBounds` で西の追加区画を扱う。駅ルート側は東側の別区画として設計し、西側の門と既存の寮区画を維持する。
- `MinimapAssets` は `Assets/Textures/minimap.png` と `minimap.json` を読む。画像が無い場合の中心・半幅はキャンパスだけの既定値（中心 13.69, −29.15、半幅 175.37 m）。駅を含む描画範囲とミニマップの扱いは別途決める必要がある。
- FBX は `Assets/Models/Campus/` に置く。既存の `RouteStage` と `CharacterImporter` はこの場所のキャンパス用取り込み設定を前提にしている（`blender/README_route.md`）。

## 駅側の範囲についての予備計算

既存の寮ルート用 `data/osm/route_query.ql` は東端が東経 139.8682 度。金町駅の駅中心として [MapFan](https://mapfan.com/spots/SCH%2CJ%2C2P) が示す緯度 35.7695069・経度 139.8704004 は、その東端より約 199 m 東にある。これは北口の座標ではなく、**駅ルートの折れ線には使わない**。データ取得範囲を決めるための目安に限る。

同じ駅中心を `tools/osm_common.py` の原点・投影に入れると x ≈ +672 m、z ≈ −267 m。現在の `WorldBounds.DefaultArea` の東端 x = +340 m の外である。したがって、駅から開始できるシーンには東側の区画拡張、見えない壁と実行時の境界の更新が必要。範囲と出口位置は北口の現地記録と OSM 折れ線から確定する。#41 の寮ルート用の bbox と `route.json` は書き換えない。

この節はコードの現状だけを述べる。駅ルートの実際の折れ線や追加区画の寸法は、地図データと現地確認前には決めない。

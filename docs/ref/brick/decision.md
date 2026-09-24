# 煉瓦の色とテクスチャの決定（#109）

## 決めた値
| 項目 | 値 |
|---|---|
| albedo（CampusColors / retint 先） | **#654F44**（Lab 35.3/7.4/10.0、線形 0.130/0.078/0.058） |
| テクスチャ | ambientCG **Bricks082A**（CC0） |
| tile_cm | **160** |
| saturation / contrast | **1.0 / 1.0**（元の Color を弱めない） |
| depth | **0.5**（目地は AO / Displacement で暗く焼く。目地を別に塗らない） |
| 煉瓦ごとのまだら | 追加の手順 mottle（下記）。コードの変更が要る |

比較画像: `pick/compare.jpg`（左から 写真の近写 / main の #8E3B2F / PR #80 の Bricks101 / コード変更なしの Bricks082A / 提案。下の段は _ShadeColor を掛けた影側）。

## albedo の理由
- 2 つの測色の差（#634E42 と #6F5850, ΔL* 4.5）は、ほぼ全部が基準面の置き方の違いから来る。
  - ratio 法は基準面を concrete_grey #B4B2AC（線形 Y 0.445、少し暖かい灰）に置いた。
  - WB 法は基準面を無彩色の Y 0.55 に置いた。
  - WB 法の値を concrete_grey の明るさと色に置き直すと #664F45 になり、ratio 法と ΔE00 約 1 で一致する。
- ゲームで基準面（塔屋・研究棟の壁）を塗っているのは concrete_grey。煉瓦と打放しの明るさの比をゲームで保つには、concrete_grey を基準にするのが正しい。
- 2 つを置き直した値の線形平均を取って #654F44 とした。PR #80 の #7A5C50 とは ΔE00 6.2、main の #8E3B2F とは ΔE00 15.1 離れている。
- retint は目地を含んだ面の平均に色を合わせる。目地込みの実測 #654E43 とほぼ同じなので、この値をそのまま retint 先に使える。

## テクスチャの理由
- 実物は二丁掛（227×60 mm、ピッチ約 233×66、縦横比 3.4〜3.8）。Bricks082A は縦横比 3.79 で小口が混じらず、縦目地・横目地とも細い。候補の中で一番近い。
  - Bricks101（PR #80）は小口が段に混じる（25%）。
  - Bricks092 は縦横比 4.21 で目地が明るい。
- ambientCG の素材なので、fetch_textures.py の SOURCE_HOST（ambientcg.com）を変えずに取れる。
- tile_cm: 1 タイルに 24 段ある（Displacement を分けて数えた）。実物の段ピッチ 6.6 cm に合わせて 24 × 6.6 ≈ 158 cm、丸めて 160 cm にした。長手方向のピッチは確かめていない。

## まだらの強さ（タイル 1 枚の大きさでぼかした統計、#654F44 に揃えて比べた）
| | L* sd | C* sd |
|---|---|---|
| 写真 近写（看板の壁） | 8.1 | 7.4 |
| 写真 flickr 近景（斜め） | 5.1 | 3.6 |
| PR #80（Bricks101, sat 0.4 / con 0.7） | 1.7 | 1.6 |
| Bricks082A sat 1 / con 1 | 2.2 | 4.6 |
| Bricks082A sat 1.6 / con 1.6 | 2.7 | 10.3 |
| **Bricks082A + mottle（palette どおり）** | **5.5** | **5.9** |
| Bricks082A + mottle（強さ 0.7） | 4.0 | 5.2 |
| Bricks092 + mottle（強さ 0.7） | 4.9 | 6.7 |

- 実物は 1 枚ごとの差が大きい（赤橙・赤茶・茶・灰茶・黒に近い色が混じり、暗い色が多い）。
- 今の sat 0.4 / con 0.7 はこの差をさらに小さくしており、逆の向きに効いている。
- saturation / contrast を上げても明るさの差（L sd）はほとんど増えない。増えるのは色の粒（C sd）だけで、煉瓦 1 枚ごとの色にならない。だから saturation / contrast は 1.0 に戻す。そのうえで、煉瓦ごとの色は別の手順で付ける。
- mottle の手順（試作: `pick/bake.py` の `brick_labels` / `mottle`）
  1. Displacement から煉瓦を 1 枚ずつ分ける。タイルの端をまたぐ煉瓦は同じ 1 枚としてつなぐ。
  2. 煉瓦ごとの平均を面全体の平均にそろえる。
  3. 実測の palette から 1 色を割合どおりに引き、その比を線形で掛ける。palette は #413834 26% / #5D4A41 34% / #73584A 25% / #8B5E45 7.5% / #816C5F 8%。
  4. 煉瓦の中の粒は元の Color のまま残す。
  5. 乱数の種は asset 名から作るので、何度焼いても同じ絵になる。最後の retint で平均は #654F44 に戻る。
- mottle を入れない（コードを変えない）場合の値: Bricks082A, tile_cm 160, sat 1.0, con 1.0, depth 0.5。まだらは実物より弱いままになる（L sd 2.2）。

## 目地
- 写真では、目地は暗く細い線。明るい目地の格子は見えない。
- だから depth 0.5 で AO / Displacement の暗さを残し、目地を別に塗らない。
- Bricks082A の Color の目地は明るい灰（#8c8981）だが、焼くと AO で暗くなる（compare.jpg の 4・5 枚目）。

## 書き方
- surface.json（data/textures/surfaces.json）の煉瓦の項:
  `{"asset":"Bricks082A","tile_cm":160,"size_px":512,"saturation":1.0,"contrast":1.0,"depth":0.5,"materials":["brick_red"],"note":"二丁掛（227x60mm、ピッチ約233x66）。1 タイル 24 段 x 6.6cm で 160cm。目地は AO で暗く焼く（実物も暗い細線）。色は #109 の実測 #654F44。"}`
  - mottle を入れるなら、`"mottle":{"palette":[["413834",0.26],["5D4A41",0.338],["73584A",0.245],["8B5E45",0.075],["816C5F",0.083]],"strength":1.0}` を足す。
  - fetch_textures.py の bake_surface で、shape の後・bake_detail の前に mottle を掛ける（`pick/bake.py` の `bake` と同じ順番）。
- mats.py: `"brick_red": ((0.130, 0.078, 0.058), 0.85, 0.0, 1.0),  # #654F44`
- MaterialLibrary.cs の CampusColors: `{ "brick_red", "654F44" }`
- 焼いた後は fetch_textures.py の report で MIN_SPREAD / MAX_SEAM を確かめる。この試作の継ぎ目の比は seam_ratio 0.52（上限 1.1）。MIN_SPREAD は確かめていない。

## 残る問題
- 張り方: surface.md では施工中の写真から馬張りとした。一方、看板の壁の近写（`photos/street/homemate_sign_brick_closeup.jpg`）では縦目地が上下の段で揃っていて、芋張りに見える。壁によって違う可能性がある。どちらが多いかは確かめていない。Bricks082A は馬張り。
- compare.jpg の 1 枚目は写真のまま（照明の色を補正していない。幅は約 1.2 m で、ほかの 3 m 角とは縮尺が違う）。
- mottle は試作のコードで、本体の fetch_textures.py には入っていない。
- Unity での見え方は確かめていない（Unity は起動していない）。

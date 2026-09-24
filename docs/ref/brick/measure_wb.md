# 葛飾キャンパス外装煉瓦の色（白基準による補正, Issue #109）

## 結論

- ゲームの albedo の推奨値: **#6F5850**（Lab [39.6, 8.2, 8.2]）。晴天順光の平均と曇天の平均の中点。
- 光の条件ごとの重み付き平均: 晴天 #6C564C Lab [38.6, 7.4, 9.1]（17 枚, SD [5.6, 2.2, 4.8]）/ 日陰 #7C695C Lab [45.7, 5.7, 10.2]（4 枚）/ 曇天 #735A54 Lab [40.5, 9.1, 7.4]（6 枚）。
- 元の #8E3B2F（Lab [36.1, 34.4, 24.9]）と PR #80 の #7A5C50（Lab [41.8, 10.4, 11.7]）の間には入らない。#8E3B2F→#7A5C50 の線分に射影すると t=1.11（0 が元, 1 が PR #80）で、PR #80 を少し越えた先にある。線分からの距離は 3.5。
- ΔE2000: 実測と #8E3B2F が 14.9、実測と #7A5C50 が 3.4（参考: #8E3B2F と #7A5C50 の間は 13.6）。#7A5C50 との差は ΔL*=-2.3, Δa*=-2.2, Δb*=-3.4 で、少し暗く、赤みも黄みも少し弱い。
- 「中間くらい」という見立てとは合わない。実測は #7A5C50 のほぼ近傍で、#8E3B2F のような赤い煉瓦ではない（a* は 8 前後で、#8E3B2F の 34 の 4 分の 1 程度）。

## 手法

1. 写真ごとに無彩色の面（白い塔・塔屋の打放し・白パネル）を基準矩形に取り、明るい側 50% の画素（白飛びを除く）の中央値でチャンネルごとのゲインを出してホワイトバランスをそろえた。基準面が無い写真だけ gray-world。
2. 露出をそろえた: 基準面の線形 Y を一つの値（明るい面 = 0.55）に合わせる。塔屋の打放しと白い塔の明るさの比は晴天の写真で 0.95〜1.08 だったので、同じ値に置いた。暗い打放し・ルーバーしか基準に取れなかった 4 枚は 0.40 に置き、重みを 1 に下げた。
3. 煉瓦矩形から、明るく彩度の低い画素（C*<10 かつ L* が中央値+12 超: 明るい目地・透かし・文字）と、ごく暗い画素（L* が中央値−25 未満: 影の目地）を除き、残りの中央値を取った。
4. 光の条件（晴天順光 / 日陰 / 曇天）ごとに重み付き平均を出した。重みは解像度・煉瓦の面積・基準面の確かさで 0〜3。
5. 感度: 明るい基準の目標を 0.45 にすると #655049 L*=36.0、0.65 にすると #785F57 L*=42.7。L* にしておよそ ±3 動く。色相と彩度はほぼ動かない。

## albedo を曇天と晴天の間に取る理由

- 晴天の写真は直射光の当たる面を撮っているが、基準面も同じ光を受けているので、比を取った値はおおむね反射率になる。ただしカメラのトーンカーブとハイライトの圧縮で、明るい基準面が相対的に潰れ、煉瓦がやや暗めに出やすい。
- 曇天は光が拡散していて、トーンカーブの影響が小さい。そのかわり空の青みや周囲の反射を拾いやすい。
- ゲームは直射光と環境光をシェーダで足すので、albedo はどちらか一方の条件の見た目ではなく、両方の中間に置くのが無難。日陰の組は基準面も日陰か拡散光の下にあり、比が反射率に近い一方で逆光のフレアで明るく出る写真を含むので、中点の計算には入れず参考に留めた。

## 煉瓦ごとの斑（目地を除いた KMeans 4 色）

近接写真 7 枚で、写真ごとに目地を除いた煉瓦画素を Lab で KMeans(4) にかけ、各中心と写真の中央値の差を明るさの順位ごとに平均し、albedo に足した。

| 色 | 割合 | 中央値からの差 ΔLab |
|---|---|---|
| #594540 | 29% | [-8.2, -0.6, -2.1] |
| #635140 | 20% | [-3.7, -4.0, 4.6] |
| #7D6057 | 31% | [3.8, 2.5, 1.0] |
| #837359 | 19% | [9.6, -5.8, 8.3] |

明るい側は赤みが減って黄みが増す（ベージュ寄り）、暗い側は赤茶。斑の幅は L* で約 18。

## 目地（モルタル）

- 近接 3 枚（113387204, 113386448, 38331645）の明るい目地画素の中央値は #A09699。目地は幅が 1〜2 画素で煉瓦と混ざるので、これは暗い側の下限で、実物はこれより明るい。この解像度では目地だけの画素が取れず、実際の明るさは測れなかった。

## 旧手法との比較

docs/ref/commons/samples.json の値（補正なしの sRGB 中央値）: Campus_01 lab1 end sun #8C6C55, Campus_02 brick shade #7E5C47, sign brick sun #856854, TUS-Katsushika sign overcast #483A30。平均 Lab [40.6, 8.4, 14.9]。今回の補正で、晴天の写真はわずかに暗く、曇天の写真（#483A30）は露出を持ち上げて明るくなった。旧手法の平均と今回の albedo の差は主に b*（黄み）で、今回のほうが黄みが弱い。

## 写真ごとの値

矩形は画像幅・高さに対する百分率 [x0,y0,x1,y1]。raw は補正前の煉瓦画素の中央値、corrected は補正後。重み 0 は集計から外したもの。

| # | ファイル | 光 | 煉瓦矩形 | 基準矩形（種類） | raw | corrected | Lab | 重み | 出典 / 作者 / ライセンス | メモ |
|---|---|---|---|---|---|---|---|---|---|---|
| 00 | commons/commons_lab1_brick_ends_sunny.jpg | sun | [[69.5, 15, 75, 70]] | [[81.5, 15, 84, 55]] (light) | #8C6D55 | #856650 | [45.8, 9.1, 17.1] | 2 | https://commons.wikimedia.org/wiki/File:Tokyo_University_of_Science_Katsushika_Campus_01.JPG / あばさー / Public domain | 第1研究棟の妻壁（東端） |
| 01 | commons/commons_lecture_brick_volumes_sunny.jpg | sun | [[43.5, 22, 49.5, 54]] | [[55.5, 15, 62, 48]] (light) | #7D5C46 | #6D503D | [36.7, 9.5, 16.3] | 3 | https://commons.wikimedia.org/wiki/File:Tokyo_University_of_Science_Katsushika_Campus_02.JPG / あばさー / Public domain | 講義棟の左の煉瓦ボリューム |
| 02 | commons/commons_lecture_brick_volumes_sunny.jpg#sign | shade | [[79, 41.5, 93, 49]] | [[55.5, 15, 62, 48]] (light) | #866955 | #755C4A | [41.1, 7.8, 14.3] | 1 | https://commons.wikimedia.org/wiki/File:Tokyo_University_of_Science_Katsushika_Campus_02.JPG / あばさー / Public domain | 講義棟の校名サイン面（一部日陰） |
| 03 | commons/commons_lecture_crossing_overcast.jpg | overcast | [[50.5, 38.5, 62, 48]] | [[36, 21, 42, 28]] (gray) | #473930 | #5A4840 | [32.3, 6.1, 7.8] | 1 | https://commons.wikimedia.org/wiki/File:TUS-Katsushika.JPG / Nyao148 / CC BY-SA 3.0 | 講義棟の校名サイン面 |
| 04 | official/tus_hero_lecture_entrance_brick_sign_sunny.jpg | sun | [[51, 31, 66, 47]] | [[35, 20, 41, 40]] (light) | #7A625B | #705954 | [40.0, 8.1, 6.7] | 3 | https://www.tus.ac.jp/admissions/lp/yy/ / Tokyo University of Science (official site, photographer not credited) / unknown/copyrighted | 講義棟の校名サイン面 |
| 05 | official/nikken_top_lab1_lecture_mall_sunny.jpg | sun | [[10.5, 22, 17, 60]] | [[63, 37, 66.5, 62]] (light) | #674A45 | #533A38 | [27.3, 10.5, 5.7] | 3 | https://www.nikken.co.jp/ja/projects/education/tokyo_university_of_science.html / 日建設計（作品ページのメイン写真、撮影者の表記なし） / unknown/copyrighted | 第1研究棟の端部 |
| 06 | official/tus_en_hero_lab1_lecture_mall_sunny.jpg | sun | [[5.5, 5, 13.5, 60], [76, 45, 84, 55]] | [[63, 22, 67, 55]] (light) | #7C655F | #6B5650 | [38.4, 7.9, 6.6] | 2 | https://www.tus.ac.jp/en/campus/katsushika.html / Tokyo University of Science (official site, photographer not credited) / unknown/copyrighted | 第1研究棟の端部と講義棟のサイン面 |
| 07 | official/nikken_02_lab1_brick_towers_sunny.jpg | sun | [[80, 18, 88, 33]] | [[36, 57, 47, 63]] (gray) | #826D68 | #665551 | [37.7, 6.2, 4.9] | 1 | https://www.nikken.co.jp/ja/projects/education/tokyo_university_of_science.html / 日建設計（撮影者の表記なし） / unknown/copyrighted | 第1研究棟の煉瓦の塔 |
| 08 | official/prtimes_kyoso_building_brick_endwall_overcast.jpg | overcast | [[21.5, 12, 29.5, 54]] | [[60, 38, 72, 55]] (gray) | #7D726B | #6D625B | [42.5, 3.0, 5.6] | 1 | https://prtimes.jp/main/html/rd/p/000000114.000102047.html / 学校法人東京理科大学（PR TIMES のプレスリリース） / unknown/copyrighted | 共創棟の端壁（ルーバー面を基準） |
| 09 | official/shinkenchiku_thumb_lab1_lecture_mall_overcast.jpg | overcast | [[6, 5, 11.5, 90]] | [[75, 30, 80, 68]] (light) | #886C60 | #7A6058 | [43.1, 8.9, 8.6] | 2 | https://data.shinkenchiku.online/en/articles/SK_2013_07_168-0 / 新建築社（新建築 2013年7月号 p.168 の記事サムネイル、撮影者の表記はログインが必要な本文側） / unknown/copyrighted | 第1研究棟の端部 |
| 10 | official/tus_lab1_facade_grid_brick_sunny.jpg | sun | [[35.5, 10, 39, 46]] | [[15, 25, 19, 55]] (gray) | #6C6473 | #61555A | [37.4, 6.2, -1.1] | 1 | https://www.tus.ac.jp/en/campus/katsushika.html / Tokyo University of Science (official site) / unknown/copyrighted | 第1研究棟の縦の帯（青かぶり強い） |
| 11 | street/pixta_113386436_sign_wall_sunny_2024.jpg | sun | [[36, 60, 98, 95]] | [[37, 13, 63, 31]] (light) | #8C7165 | #755E56 | [42.0, 7.8, 8.1] | 3 | https://pixta.jp/photo/113386436 / arikura machiko / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面 |
| 12 | street/pixta_38331645_sign_wall_sunny_winter_2018.jpg | sun | [[22, 73, 99, 95]] | [[28, 26, 58, 45]] (light) | #62554F | #5E4E49 | [34.7, 5.5, 5.5] | 3 | https://pixta.jp/photo/38331645 / route134 / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面 |
| 13 | street/pixta_78951245_sign_wall_sunny_summer_2021.jpg | sun | [[42, 58, 99, 72], [72, 24, 99, 38]] | [[44, 15, 68, 35]] (light) | #80654A | #655544 | [37.4, 3.7, 12.7] | 3 | https://pixta.jp/photo/78951245 / Ystudio / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面 |
| 14 | street/pixta_113387204_sign_wall_overcast_closeup_2024.jpg | overcast | [[26, 17, 99, 24], [30, 45, 65, 62]] | [[26, 1, 70, 12]] (light) | #8E7372 | #735D59 | [41.5, 8.3, 5.5] | 3 | https://pixta.jp/photo/113387204 / arikura machiko / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面（近接） |
| 15 | street/pixta_113386448_sign_wall_overcast_2024.jpg | overcast | [[27, 36, 43, 55], [60, 20, 91, 24], [70, 37, 90, 42]] | [[29, 3, 55, 18]] (light) | #785750 | #674C47 | [34.9, 10.5, 7.5] | 3 | https://pixta.jp/photo/113386448 / arikura machiko / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面 |
| 16 | street/pixta_78951244_sign_wall_sunny_near_2021.jpg | sun | [[36, 52, 97, 62], [75, 24, 97, 38]] | [[38, 18, 72, 28]] (light) | #8C6F52 | #6E5D4B | [40.8, 3.7, 13.3] | 3 | https://pixta.jp/photo/78951244 / Ystudio / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面（近め） |
| 17 | street/pixta_49951792_sign_wall_shade_2019.jpg | shade | [[29, 45, 40, 70], [60, 36, 79, 48]] | [[30, 2, 53, 12]] (light) | #685D58 | #6E5B4E | [40.2, 5.3, 10.4] | 2 | https://pixta.jp/photo/49951792 / arikura machiko / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面（日陰） |
| 18 | street/pixta_49951791_sign_wall_shade_portrait_2019.jpg | shade | [[22, 44, 50, 57], [60, 40, 77, 55]] | [[22, 3, 44, 15]] (light) | #847A74 | #877364 | [49.9, 5.1, 11.1] | 2 | https://pixta.jp/photo/49951791 / arikura machiko / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面（日陰） |
| 19 | street/pixta_8703771_sign_wall_sunny_autumn_2013.jpg | sun | [[60, 72, 84, 95]] | [[32, 62, 39, 95]] (light) | #987870 | #7F645E | [44.9, 9.8, 7.5] | 2 | https://pixta.jp/photo/8703771 / TK_Garnett / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面 |
| 20 | street/pixta_6952728_sign_wall_sunny_spring_2013.jpg | sun | [[47, 55, 74, 70]] | [[48, 37, 58, 44]] (light) | #6F5A53 | #695751 | [38.6, 6.3, 6.5] | 2 | https://pixta.jp/photo/6952728 / TK_Garnett / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面 |
| 21 | street/pixta_96261618_sign_wall_backlit_autumn_2022.jpg | shade | [[66, 43, 94, 52], [82, 28, 94, 40]] | [[36, 20, 44, 50]] (light) | #A38E86 | #84726A | [49.4, 5.5, 7.0] | 2 | https://pixta.jp/photo/96261618 / pretty world / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 講義棟サイン面（逆光で日陰） |
| 22 | street/pixta_79533670_research_end_brick_sunny_2021.jpg | sun | [[24, 28, 33, 60]] | [[38, 25, 47, 55]] (light) | #805A40 | #664936 | [33.7, 9.5, 16.0] | 2 | https://pixta.jp/photo/79533670 / Ystudio / PIXTA / unknown/copyrighted (PIXTA のストック写真。透かし入りのプレビュー。調査用のみ) | 正門側の研究棟の妻壁 |
| 23 | commons/flickr_brick_wall_concrete_sunny.jpg | sun | [[72, 0, 88, 42]] | [[25, 0, 50, 15]] (light) | #7C6B75 | #6C5E66 | [41.5, 6.8, -2.6] | 1 | https://www.flickr.com/photos/129571461@N07/26782497005/ / tomooki0414 / unknown/copyrighted (Flickr all rights reserved) | 煉瓦壁と打放し（逆光ぎみ） |
| 24 | commons/flickr_brick_wall_near_person_1.jpg | shade | [[10, 2, 33, 55]] | [] (grayworld) | #795F5A | #55433F | [30.2, 6.8, 5.4] | 0 | https://www.flickr.com/photos/129571461@N07/26715371701/ / tomooki0414 / unknown/copyrighted (Flickr all rights reserved) | 日陰の近接（基準面なし. gray-world は煉瓦色を中和するので色の集計から外し, 斑とモルタルだけに使う） |
| 25 | commons/flickr_brick_wall_near_person_2.jpg | shade | [[25, 2, 60, 30], [25, 30, 40, 55]] | [] (grayworld) | #7D6159 | #5E4D46 | [34.2, 5.9, 6.8] | 0 | https://www.flickr.com/photos/129571461@N07/26509762030/ / tomooki0414 / unknown/copyrighted (Flickr all rights reserved) | 日陰の近接（基準面なし. gray-world は煉瓦色を中和するので色の集計から外し, 斑とモルタルだけに使う） |
| 26 | commons/flickr_library_pilotis_view_brick_far.jpg | sun | [[56, 61, 65, 73], [77, 60, 88, 72]] | [[20, 73, 50, 83]] (light) | #A98A7B | #8F7569 | [51.3, 8.6, 10.2] | 0 | https://www.flickr.com/photos/77597060@N02/41777696201/ / christinayan01 (busy) / unknown/copyrighted (Flickr all rights reserved) | 図書館ピロティ越しの遠景. christinayan の同じ写真と同じ値になったので重複として外す |
| 27 | official/christinayan_view_from_library_brick_tower_sunny.jpg | sun | [[55, 60, 62, 72], [80, 59, 88, 72]] | [[92, 62, 99, 72]] (light) | #A98A7B | #8F766D | [51.6, 8.2, 8.6] | 2 | https://christinayan01.jp/architecture/archives/6794 / Takahiro Yanai (christinayan) / unknown/copyrighted | 図書館ピロティ越しの研究棟 |
| 28 | official/jspe_guide_lecture_building_brick_sunny.jpg | sun | [[34, 31, 40, 52], [62, 50, 69, 60]] | [[42, 5, 52, 20]] (light) | #423A36 | #51423A | [29.3, 5.2, 7.5] | 1 | https://2023-03spring.jspe.or.jp/wp/wp-content/uploads/pdf/23-03-kaijo.pdf / 精密工学会 2023年度春季大会の会場案内 PDF（写真の出所は大学提供とみられるが記載なし） / unknown/copyrighted | 講義棟（低解像度） |
| 29 | official/pixta_113387205_lecture_sign_brick_cherry.jpg | overcast | [[50, 30, 75, 42]] | [[2, 3, 12, 25]] (light) | #A27975 | #8D6962 | [47.9, 13.1, 9.7] | 2 | https://pixta.jp/photo/113387205 / PIXTA 投稿者（有料素材のプレビュー） / unknown/copyrighted（PIXTA の有料素材、透かし入りプレビュー） | 講義棟サイン面と桜（透かしあり） |

矩形の図: measure_wb_rects.jpg（マゼンタ = 煉瓦, 緑 = 基準面）。

## 測れなかったもの

- 時間切れで矩形を取れなかった写真: official の pixta_57787293, tus_ci 4 枚, tus_lab_stairtower, tus_mall_sculpture, tus_top_highview、street の homemate 5 枚, pixta_9683295, 78717712, 78717708。christinayan の図書館ピロティ 2 枚と pixta_56918409 は煉瓦が深い日陰か逆光で、基準面と同じ光の下にないので外した。
- 目地の実際の明るさ（上記）。

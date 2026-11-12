# 屋根・金属・木に写真の質感を張る材質の決定（#416）

## 決めた値

| 材質 | 素材（ambientCG、CC0） | tile_cm | saturation | contrast | depth | 面積 m²（順位） |
|---|---|---:|---:|---:|---:|---:|
| roof_grey | Concrete046 | 240 | 0.2 | 0.45 | 0.2 | 83,470（3） |
| metal_grey | Metal032 | 100 | 0.2 | 0.5 | 0.25 | 10,565（20） |
| metal_white | Metal016 | 100 | 0.2 | 0.25 | 0.08 | 7,145（21） |
| wood | Wood049 | 80 | 0.45 | 1.0 | 0.2 | 299（45） |
| deck_wood | Planks029S | 240 | 0.4 | 0.7 | 0.35 | 262（47） |

- 面積は `area.md`（`SurfaceAreaProbe.Run` の出力）。
- 平均色は `fetch_textures.py` が CampusColors の値に焼き直すので、遠くから見た色は変わらない。変わるのは近くで見える模様だけ。
- 追加した jpg 5 枚は合計 372,097 B。

## 選んだ理由

- **roof_grey**: 写真の質感の無い材質の中で面積が最大。上から見下ろす視点（空撮・上階の窓）で一番広く見える。陸屋根の防水面の、目の細かいまだらを Concrete046 から借りた。
- **metal_grey / metal_white**: 共創棟の縦フィン・パラペット・温室の壁など、人の目の高さで近くに来る金属。無地だと平らな板に見える。どちらも筋の目立たない素材を選び、まだらを弱く（contrast 0.25〜0.5）した。metal_white は contrast 0.5 ではまだらが汚れに見えたので 0.25、depth 0.08 まで下げた。
- **wood / deck_wood**: 面積は小さいが、ベンチや寮の屋上テラスのように歩いて近づく面で、無地だと木に見えない。wood は contrast 0.45 では模様が `MIN_SPREAD`（4.0）に届かず、1.0 に上げた。deck_wood は板の継ぎ目が見える Planks029S を選んだ。素材名の末尾に版の 1 字（S）が付くので、`fetch_textures.py` の素材名の正規表現を広げた。
- metal_grey と metal_white は ambientCG に実寸の登録が無く、tile_cm は 100 にした。

## 選ばなかった材質

| 材質 | 面積の順位 | 理由 |
|---|---:|---|
| leaf・trunk | 4・18 | 葉と幹は別の手順（樹木のモデル）で作っている。#416 の対象外 |
| glass_dark・glass_clear | 7・19 | ガラスは映り込みで見せる面で、写真の質感は合わない |
| bg_wall_0〜5 | 5〜16 | 遠くの背景の建物で、近づけないので質感が見えない。6 材質あり、追加の容量も見合わない |
| dorm | 9 | 寮の屋内の窓の外に置く背景の箱（`InteriorBackdropStage`）で、屋外の面ではない |
| dorm_white・wall_white など | 24〜 | 白い塗り壁で、写真の模様を弱く張っても見た目がほとんど変わらない |

## 確認

- `python tools/fetch_textures.py --check`: すべて狙いどおり。
- 撮影（`ScenePreview` の shot_surface_lab2_roof・shot_surface_kyoso_fins・shot_surface_dorm_terrace・shot_campus_moat_bank・shot_aerial_southwest）の before / after を `docs/progress/416/` に置いた。

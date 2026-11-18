# Campus シーンの材質ごとの面積 (#416)

`SurfaceAreaProbe.Run`（`Assets/Scripts/Editor/SurfaceAreaProbe.cs`）が、Campus シーンの有効な MeshRenderer（屋内の `Interiors` の下は除く）の三角形のワールド面積を、サブメッシュのマテリアルごとに足した値。裏表の 2 面や、地面に隠れた面も数える。「写真」は `tiling.json` にあり、すでに写真の質感を張ってある材質。

| 順位 | 材質 | 面積 m² | 割合 | 写真 |
|---:|---|---:|---:|:---:|
| 1 | `grass_dark` | 16,636,731 | 95.4% | 済 |
| 2 | `grass` | 118,627 | 0.7% | 済 |
| 3 | `roof_grey` | 83,470 | 0.5% |  |
| 4 | `leaf` | 73,159 | 0.4% |  |
| 5 | `bg_wall_3` | 58,550 | 0.3% |  |
| 6 | `concrete_grey` | 56,977 | 0.3% | 済 |
| 7 | `glass_dark` | 52,400 | 0.3% |  |
| 8 | `bg_wall_4` | 36,319 | 0.2% |  |
| 9 | `dorm` | 32,531 | 0.2% |  |
| 10 | `bg_wall_5` | 32,167 | 0.2% |  |
| 11 | `asphalt` | 28,596 | 0.2% | 済 |
| 12 | `concrete_light` | 28,438 | 0.2% | 済 |
| 13 | `bg_wall_2` | 25,974 | 0.1% |  |
| 14 | `bg_wall_1` | 24,668 | 0.1% |  |
| 15 | `stone_light` | 24,534 | 0.1% | 済 |
| 16 | `bg_wall_0` | 19,625 | 0.1% |  |
| 17 | `brick_red` | 18,681 | 0.1% | 済 |
| 18 | `trunk` | 12,725 | 0.1% |  |
| 19 | `glass_clear` | 10,816 | 0.1% |  |
| 20 | `metal_grey` | 10,565 | 0.1% |  |
| 21 | `metal_white` | 7,145 | 0.0% |  |
| 22 | `stone_dark` | 5,427 | 0.0% | 済 |
| 23 | `sand` | 4,810 | 0.0% | 済 |
| 24 | `dorm_white` | 4,027 | 0.0% |  |
| 25 | `concrete_dark` | 3,449 | 0.0% | 済 |
| 26 | `wall_white` | 3,356 | 0.0% |  |
| 27 | `metal_charcoal` | 2,990 | 0.0% |  |
| 28 | `KCD_WindowGlow` | 2,977 | 0.0% |  |
| 29 | `water` | 2,638 | 0.0% |  |
| 30 | `KCD_LampGlow` | 2,503 | 0.0% |  |
| 31 | `louver_white` | 2,381 | 0.0% |  |
| 32 | `dorm_cream` | 1,517 | 0.0% |  |
| 33 | `soil` | 1,283 | 0.0% | 済 |
| 34 | `floor_tile_grey` | 1,241 | 0.0% |  |
| 35 | `ceiling_white` | 1,131 | 0.0% |  |
| 36 | `line_white` | 779 | 0.0% |  |
| 37 | `wall_accent_navy` | 710 | 0.0% |  |
| 38 | `hardcourt_outer` | 645 | 0.0% |  |
| 39 | `deck_dark` | 601 | 0.0% |  |
| 40 | `flower_leaf` | 560 | 0.0% |  |
| 41 | `plastic_black` | 453 | 0.0% |  |
| 42 | `bed_soil` | 447 | 0.0% | 済 |
| 43 | `metal_gray` | 436 | 0.0% |  |
| 44 | `glass_frosted` | 343 | 0.0% |  |
| 45 | `wood` | 299 | 0.0% |  |
| 46 | `tile_mauve` | 289 | 0.0% |  |
| 47 | `deck_wood` | 262 | 0.0% |  |
| 48 | `hardcourt_inner` | 261 | 0.0% |  |
| 49 | `light_panel` | 215 | 0.0% |  |
| 50 | `tile_charcoal` | 177 | 0.0% |  |
| 51 | `desk_dark` | 174 | 0.0% |  |
| 52 | `floor_wood_light` | 147 | 0.0% |  |
| 53 | `floor_carpet_gold` | 135 | 0.0% |  |
| 54 | `wallpaper_orange` | 132 | 0.0% |  |
| 55 | `plastic_white` | 123 | 0.0% |  |
| 56 | `desk_wood` | 110 | 0.0% |  |
| 57 | `fabric_yellow` | 98 | 0.0% |  |
| 58 | `panel_brown` | 75 | 0.0% |  |
| 59 | `stainless` | 75 | 0.0% |  |
| 60 | `sign_plate` | 70 | 0.0% |  |
| 61 | `metal_dark` | 52 | 0.0% |  |
| 62 | `counter_wood` | 38 | 0.0% |  |
| 63 | `glass_partition` | 37 | 0.0% |  |
| 64 | `ceiling_grid` | 36 | 0.0% |  |
| 65 | `floor_laundry_yellow` | 35 | 0.0% |  |
| 66 | `vending_blue` | 34 | 0.0% |  |
| 67 | `leaf_light` | 33 | 0.0% |  |
| 68 | `floor_concrete` | 32 | 0.0% |  |
| 69 | `plant_yellow` | 29 | 0.0% |  |
| 70 | `plant_green` | 29 | 0.0% |  |
| 71 | `tus_green` | 27 | 0.0% |  |
| 72 | `light_strip` | 27 | 0.0% |  |
| 73 | `soil_dark` | 22 | 0.0% |  |
| 74 | `fabric_beige` | 21 | 0.0% |  |
| 75 | `flower_yellow` | 19 | 0.0% |  |
| 76 | `flower_red` | 19 | 0.0% |  |
| 77 | `floor_wood` | 19 | 0.0% |  |
| 78 | `screen_blue` | 18 | 0.0% |  |
| 79 | `flower_orange` | 17 | 0.0% |  |
| 80 | `vending_red` | 15 | 0.0% |  |
| 81 | `flower_purple` | 15 | 0.0% |  |
| 82 | `flower_white` | 15 | 0.0% |  |
| 83 | `flower_pink` | 15 | 0.0% |  |
| 84 | `counter_dark` | 14 | 0.0% |  |
| 85 | `sign_plate_blue` | 14 | 0.0% |  |
| 86 | `plant_pot` | 14 | 0.0% |  |
| 87 | `curtain_blue` | 12 | 0.0% |  |
| 88 | `counter_stone` | 12 | 0.0% |  |
| 89 | `leaf_top` | 9 | 0.0% |  |
| 90 | `glass_interior` | 9 | 0.0% |  |
| 91 | `fm_blue` | 8 | 0.0% |  |
| 92 | `floor_tile_dark` | 8 | 0.0% |  |
| 93 | `desk_white` | 8 | 0.0% |  |
| 94 | `book_g` | 8 | 0.0% |  |
| 95 | `chair_grey` | 7 | 0.0% |  |
| 96 | `floor_carpet_grey` | 7 | 0.0% |  |
| 97 | `door_frosted` | 7 | 0.0% |  |
| 98 | `book_a` | 7 | 0.0% |  |
| 99 | `book_c` | 7 | 0.0% |  |
| 100 | `book_e` | 7 | 0.0% |  |
| 101 | `paper_white` | 6 | 0.0% |  |
| 102 | `mirror_glass` | 5 | 0.0% |  |
| 103 | `cushion_red` | 4 | 0.0% |  |
| 104 | `washer_lid` | 4 | 0.0% |  |
| 105 | `water_bath` | 4 | 0.0% |  |
| 106 | `sign_exit_green` | 3 | 0.0% |  |
| 107 | `bin_green` | 3 | 0.0% |  |
| 108 | `fire_red` | 2 | 0.0% |  |
| 109 | `book_d` | 2 | 0.0% |  |
| 110 | `chair_green` | 2 | 0.0% |  |
| 111 | `book_h` | 2 | 0.0% |  |
| 112 | `book_b` | 1 | 0.0% |  |
| 113 | `white` | 1 | 0.0% |  |
| 114 | `chair_blue` | 1 | 0.0% |  |
| 115 | `bike_frame` | 1 | 0.0% |  |
| 116 | `book_f` | 1 | 0.0% |  |
| 117 | `mark_common` | 1 | 0.0% |  |
| 118 | `mark_uncommon` | 0 | 0.0% |  |
| 119 | `item_metal_silver` | 0 | 0.0% |  |
| 120 | `glass_marble` | 0 | 0.0% |  |
| 121 | `item_metal_brass` | 0 | 0.0% |  |
| 122 | `item_metal_gold` | 0 | 0.0% |  |
| 123 | `mark_rare` | 0 | 0.0% |  |
| 124 | `item_acorn_cap` | 0 | 0.0% |  |
| 125 | `item_acorn` | 0 | 0.0% |  |
| 126 | `item_red` | 0 | 0.0% |  |
| 127 | `KCD_JP Material` | 0 | 0.0% |  |

合計 17,440,851 m²、127 材質。

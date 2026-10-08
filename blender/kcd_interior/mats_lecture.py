"""講義棟（lecture）の屋内だけで使う材質。imats.py が共有の材質と合わせて登録する。

名前は "lecture_" で始め、英小文字・数字・_ だけで書く（Unity は名前を小文字にして探す）。
共有の材質（imats.py の COMMON_*）と同じ名前は使えない。共有の材質で足りる色はここに書かない。
値の形は imats.py と同じ:

  PALETTE      名前 -> ((r, g, b), roughness, metallic, 1.0)    色は線形 RGB（0〜1）
  TRANSPARENT  名前 -> ((r, g, b), roughness, metallic, alpha)  透けるもの（0 < alpha < 1）
  EMISSIVE     名前 -> ((r, g, b), strength)                   この棟の PALETTE の材質を光らせる

Unity の InteriorPalette.cs には、統合ブランチで python tools/interior_palette.py が写す。
"""

PALETTE = {
    "lecture_wood_louver":      ((0.381, 0.156, 0.034), 0.55, 0.0, 1.0),  # #A66E34
    "lecture_wood_shell":       ((0.423, 0.178, 0.032), 0.45, 0.0, 1.0),  # #AE7532
    "lecture_seat_navy":        ((0.027, 0.028, 0.102), 0.90, 0.0, 1.0),  # #2E2F5A
    "lecture_desk_ivory":       ((0.905, 0.847, 0.745), 0.50, 0.0, 1.0),  # #F4EDE0
    "lecture_board_bluegreen":  ((0.070, 0.296, 0.381), 0.85, 0.0, 1.0),  # #4B94A6
    "lecture_board_green":      ((0.061, 0.235, 0.162), 0.85, 0.0, 1.0),  # #468570
    "lecture_carpet_hall":      ((0.042, 0.044, 0.051), 0.95, 0.0, 1.0),  # #3A3B40
    "lecture_concrete":         ((0.275, 0.238, 0.178), 0.85, 0.0, 1.0),  # #8F8675
    "lecture_carpet_corridor":  ((0.056, 0.044, 0.040), 0.95, 0.0, 1.0),  # #433B38
    "lecture_ceiling_dark":     ((0.050, 0.040, 0.032), 0.90, 0.0, 1.0),  # #3F3832
    "lecture_core_black":       ((0.006, 0.005, 0.008), 0.60, 0.0, 1.0),  # #121016
    "lecture_door_wood":        ((0.337, 0.220, 0.082), 0.50, 0.0, 1.0),  # #9D8151
    "lecture_panel_dark":       ((0.020, 0.020, 0.022), 0.60, 0.0, 1.0),  # #272729
    "lecture_lounge_ceiling":   ((0.087, 0.078, 0.080), 0.85, 0.0, 1.0),  # #534F50
    "lecture_lounge_column":    ((0.156, 0.153, 0.175), 0.50, 0.0, 1.0),  # #6E6D74
    "lecture_lounge_carpet":    ((0.122, 0.112, 0.120), 0.95, 0.0, 1.0),  # #625E61
    "lecture_table_white":      ((0.738, 0.687, 0.738), 0.40, 0.0, 1.0),  # #DFD8DF
    "lecture_brick":            ((0.254, 0.068, 0.032), 0.85, 0.0, 1.0),  # #8A4A32
    "lecture_lobby_carpet":     ((0.332, 0.296, 0.254), 0.95, 0.0, 1.0),  # #9C948A
    "lecture_alcove_green":     ((0.474, 0.584, 0.392), 0.70, 0.0, 1.0),  # #B7C9A8
    "lecture_stone_light":      ((0.584, 0.571, 0.539), 0.50, 0.0, 1.0),  # #C9C7C2
    "lecture_stone_dark":       ((0.366, 0.356, 0.332), 0.50, 0.0, 1.0),  # #A3A19C
    "lecture_tactile_yellow":   ((0.745, 0.434, 0.014), 0.60, 0.0, 1.0),  # #E0B01F
    "lecture_coop_band":        ((0.007, 0.007, 0.007), 0.50, 0.0, 1.0),  # #141414
    "lecture_coop_counter":     ((0.195, 0.084, 0.037), 0.50, 0.0, 1.0),  # #7A5236
    "lecture_coop_floor":       ((0.068, 0.070, 0.076), 0.95, 0.0, 1.0),  # #4A4B4E
    "lecture_rail_white":       ((0.791, 0.791, 0.768), 0.50, 0.2, 1.0),  # #E6E6E3
    "lecture_seat_green":       ((0.076, 0.195, 0.068), 0.85, 0.0, 1.0),  # #4E7A4A
    "lecture_sign_white":       ((0.888, 0.888, 0.863), 0.40, 0.0, 1.0),  # #F2F2EF
    "lecture_sign_black":       ((0.007, 0.007, 0.007), 0.45, 0.0, 1.0),  # #141414
}

TRANSPARENT = {
    "lecture_glass_rail":       ((0.687, 0.776, 0.807), 0.05, 0.0, 0.25),  # #D8E4E8
    "lecture_glass_curtain":    ((0.552, 0.672, 0.701), 0.05, 0.0, 0.20),  # #C4D6DA
}

EMISSIVE = {}

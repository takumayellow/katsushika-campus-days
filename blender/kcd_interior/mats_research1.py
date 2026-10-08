"""第1研究棟（research1）の屋内だけで使う材質。imats.py が共有の材質と合わせて登録する。

名前は "research1_" で始め、英小文字・数字・_ だけで書く（Unity は名前を小文字にして探す）。
共有の材質（imats.py の COMMON_*）と同じ名前は使えない。共有の材質で足りる色はここに書かない。
値の形は imats.py と同じ:

  PALETTE      名前 -> ((r, g, b), roughness, metallic, 1.0)    色は線形 RGB（0〜1）
  TRANSPARENT  名前 -> ((r, g, b), roughness, metallic, alpha)  透けるもの（0 < alpha < 1）
  EMISSIVE     名前 -> ((r, g, b), strength)                   この棟の PALETTE の材質を光らせる

Unity の InteriorPalette.cs には、統合ブランチで python tools/interior_palette.py が写す。
"""

PALETTE = {
    "research1_floor_navy":       ((0.014, 0.027, 0.107), 0.25, 0.0, 1.0),
    "research1_floor_corridor":   ((0.694, 0.716, 0.730), 0.55, 0.0, 1.0),
    "research1_floor_strip":      ((0.068, 0.159, 0.462), 0.50, 0.0, 1.0),
    "research1_tile_dark":        ((0.209, 0.223, 0.235), 0.60, 0.0, 1.0),
    "research1_tile_light":       ((0.397, 0.413, 0.423), 0.60, 0.0, 1.0),
    "research1_concrete":         ((0.509, 0.479, 0.423), 0.85, 0.0, 1.0),
    "research1_wall_white":       ((0.863, 0.863, 0.839), 0.80, 0.0, 1.0),
    "research1_ceiling":          ((0.905, 0.905, 0.888), 0.90, 0.0, 1.0),
    "research1_slab_exposed":     ((0.270, 0.275, 0.262), 0.85, 0.0, 1.0),
    "research1_navy_panel":       ((0.013, 0.023, 0.091), 0.55, 0.0, 1.0),
    "research1_mullion_white":    ((0.888, 0.888, 0.871), 0.40, 0.0, 1.0),
    "research1_sash_silver":      ((0.479, 0.503, 0.521), 0.35, 0.8, 1.0),
    "research1_frame_black":      ((0.012, 0.012, 0.013), 0.45, 0.6, 1.0),
    "research1_skylight_glow":    ((0.905, 0.930, 0.956), 0.50, 0.0, 1.0),
    "research1_table_white":      ((0.913, 0.913, 0.913), 0.40, 0.0, 1.0),
    "research1_chair_white":      ((0.871, 0.871, 0.871), 0.50, 0.0, 1.0),
    "research1_chair_green":      ((0.262, 0.565, 0.050), 0.50, 0.0, 1.0),
    "research1_steel_leg":        ((0.462, 0.474, 0.485), 0.35, 0.9, 1.0),
    "research1_sus_hairline":     ((0.539, 0.552, 0.565), 0.30, 0.9, 1.0),
    "research1_ev_door":          ((0.815, 0.776, 0.672), 0.40, 0.3, 1.0),
    "research1_counter_front":    ((0.013, 0.013, 0.013), 0.40, 0.0, 1.0),
    "research1_counter_top":      ((0.888, 0.888, 0.888), 0.30, 0.0, 1.0),
    "research1_band_charcoal":    ((0.024, 0.024, 0.026), 0.60, 0.0, 1.0),
    "research1_door_lightblue":   ((0.159, 0.323, 0.578), 0.50, 0.0, 1.0),
    "research1_door_white":       ((0.807, 0.807, 0.791), 0.50, 0.0, 1.0),
    "research1_locker_grey":      ((0.386, 0.397, 0.413), 0.45, 0.4, 1.0),
    "research1_bench_top":        ((0.016, 0.016, 0.016), 0.35, 0.0, 1.0),
    "research1_cabinet_white":    ((0.871, 0.871, 0.871), 0.50, 0.0, 1.0),
    "research1_carpet_grey":      ((0.091, 0.098, 0.107), 0.95, 0.0, 1.0),
    "research1_vinyl_lightgrey":  ((0.584, 0.604, 0.610), 0.35, 0.0, 1.0),
    "research1_engine_metal":     ((0.270, 0.292, 0.319), 0.35, 0.9, 1.0),
    "research1_engine_dark":      ((0.042, 0.047, 0.054), 0.50, 0.7, 1.0),
    "research1_red_outlet":       ((0.578, 0.005, 0.027), 0.50, 0.0, 1.0),
    "research1_whiteboard":       ((0.956, 0.956, 0.956), 0.20, 0.0, 1.0),
    "research1_light_panel":      ((1.000, 1.000, 1.000), 0.50, 0.0, 1.0),
}

TRANSPARENT = {
    "research1_glass":            ((0.716, 0.807, 0.839), 0.05, 0.0, 0.22),
}

EMISSIVE = {
    "research1_skylight_glow":    ((0.905, 0.930, 0.956), 2.0),
    "research1_light_panel":      ((1.000, 1.000, 1.000), 3.0),
}

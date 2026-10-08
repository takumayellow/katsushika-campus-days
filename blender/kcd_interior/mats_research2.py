"""第2研究棟（research2）の屋内だけで使う材質。imats.py が共有の材質と合わせて登録する。

名前は "research2_" で始め、英小文字・数字・_ だけで書く（Unity は名前を小文字にして探す）。
共有の材質（imats.py の COMMON_*）と同じ名前は使えない。共有の材質で足りる色はここに書かない。
値の形は imats.py と同じ:

  PALETTE      名前 -> ((r, g, b), roughness, metallic, 1.0)    色は線形 RGB（0〜1）
  TRANSPARENT  名前 -> ((r, g, b), roughness, metallic, alpha)  透けるもの（0 < alpha < 1）
  EMISSIVE     名前 -> ((r, g, b), strength)                   この棟の PALETTE の材質を光らせる

Unity の InteriorPalette.cs には、統合ブランチで python tools/interior_palette.py が写す。
"""

PALETTE = {
    "research2_floor_1f_wood":         ((0.262, 0.118, 0.062), 0.35, 0.0, 1.0),
    "research2_floor_1f_dark":         ((0.235, 0.102, 0.054), 0.35, 0.0, 1.0),
    "research2_floor_tape_red":        ((0.479, 0.032, 0.023), 0.50, 0.0, 1.0),
    "research2_ceiling_white":         ((0.871, 0.852, 0.810), 0.90, 0.0, 1.0),
    "research2_soffit":                ((0.913, 0.784, 0.584), 0.90, 0.0, 1.0),
    "research2_glow_warm":             ((1.000, 0.694, 0.392), 0.90, 0.0, 1.0),
    "research2_column_concrete":       ((0.332, 0.347, 0.309), 0.85, 0.0, 1.0),
    "research2_wall_concrete":         ((0.402, 0.378, 0.334), 0.85, 0.0, 1.0),
    "research2_band_enji":             ((0.381, 0.063, 0.067), 0.50, 0.0, 1.0),
    "research2_band_light":            ((0.939, 0.054, 0.028), 0.50, 0.0, 1.0),
    "research2_counter_front":         ((0.133, 0.045, 0.044), 0.50, 0.0, 1.0),
    "research2_island_front":          ((0.147, 0.144, 0.107), 0.60, 0.0, 1.0),
    "research2_island_top":            ((0.171, 0.191, 0.181), 0.40, 0.0, 1.0),
    "research2_tray_lime":             ((0.296, 0.672, 0.020), 0.40, 0.0, 1.0),
    "research2_tray_black":            ((0.023, 0.023, 0.023), 0.40, 0.0, 1.0),
    "research2_wall_red":              ((0.283, 0.020, 0.012), 0.50, 0.0, 1.0),
    "research2_sign_black":            ((0.056, 0.051, 0.048), 0.60, 0.0, 1.0),
    "research2_kiosk_white":           ((0.888, 0.888, 0.871), 0.30, 0.0, 1.0),
    "research2_kiosk_black":           ((0.050, 0.044, 0.041), 0.40, 0.0, 1.0),
    "research2_panel_grey":            ((0.392, 0.418, 0.376), 0.60, 0.0, 1.0),
    "research2_stainless":             ((0.578, 0.591, 0.604), 0.25, 0.9, 1.0),
    "research2_table_white":           ((0.947, 0.905, 0.905), 0.30, 0.0, 1.0),
    "research2_chair_wood_light":      ((0.216, 0.109, 0.053), 0.40, 0.0, 1.0),
    "research2_chair_wood_orange":     ((0.314, 0.076, 0.001), 0.40, 0.0, 1.0),
    "research2_chair_walnut":          ((0.061, 0.014, 0.009), 0.40, 0.0, 1.0),
    "research2_chrome":                ((0.687, 0.701, 0.716), 0.15, 0.9, 1.0),
    "research2_mullion_dark":          ((0.056, 0.072, 0.105), 0.40, 0.6, 1.0),
    "research2_grille_black":          ((0.013, 0.013, 0.013), 0.70, 0.0, 1.0),
    "research2_floor_2f_oak":          ((0.552, 0.309, 0.165), 0.30, 0.0, 1.0),
    "research2_ceiling_wood":          ((0.672, 0.356, 0.159), 0.60, 0.0, 1.0),
    "research2_led_linear":            ((1.000, 0.905, 0.745), 0.50, 0.0, 1.0),
    "research2_ceiling_2f_white":      ((0.888, 0.888, 0.905), 0.90, 0.0, 1.0),
    "research2_column_2f":             ((0.491, 0.418, 0.366), 0.85, 0.0, 1.0),
    "research2_steel_dark":            ((0.027, 0.032, 0.037), 0.50, 0.7, 1.0),
    "research2_sash_2f":               ((0.074, 0.087, 0.107), 0.40, 0.6, 1.0),
    "research2_counter_orange":        ((0.429, 0.058, 0.001), 0.50, 0.0, 1.0),
    "research2_wall_orange":           ((0.847, 0.198, 0.038), 0.60, 0.0, 1.0),
    "research2_wall_charcoal":         ((0.048, 0.068, 0.122), 0.60, 0.0, 1.0),
    "research2_wall_lilac":            ((0.242, 0.246, 0.337), 0.80, 0.0, 1.0),
    "research2_wood_wall":             ((0.597, 0.332, 0.122), 0.60, 0.0, 1.0),
    "research2_board_green":           ((0.100, 0.159, 0.001), 0.50, 0.0, 1.0),
    "research2_cork":                  ((0.515, 0.202, 0.048), 0.90, 0.0, 1.0),
    "research2_door_steel":            ((0.262, 0.279, 0.296), 0.40, 0.6, 1.0),
    "research2_chair_shell_white":     ((0.617, 0.651, 0.768), 0.35, 0.0, 1.0),
    "research2_chair_shell_green":     ((0.156, 0.262, 0.063), 0.35, 0.0, 1.0),
    "research2_chair_shell_orange":    ((0.258, 0.030, 0.010), 0.35, 0.0, 1.0),
    "research2_chair_shell_dark":      ((0.030, 0.019, 0.023), 0.35, 0.0, 1.0),
    "research2_chair_shell_red":       ((0.716, 0.036, 0.063), 0.35, 0.0, 1.0),
    "research2_terrace_white":         ((0.847, 0.847, 0.823), 0.40, 0.3, 1.0),
    "research2_mesh_fence":            ((0.156, 0.168, 0.181), 0.50, 0.8, 1.0),
    "research2_lobby_panel":           ((0.047, 0.054, 0.068), 0.60, 0.0, 1.0),
    "research2_brick_core":            ((0.036, 0.030, 0.027), 0.85, 0.0, 1.0),
    "research2_monitor_black":         ((0.010, 0.010, 0.012), 0.30, 0.2, 1.0),
    "research2_display_blue":          ((0.014, 0.114, 0.521), 0.30, 0.0, 1.0),
    "research2_ground_brick":          ((0.323, 0.105, 0.060), 0.90, 0.0, 1.0),
}

TRANSPARENT = {
    "research2_glass":                 ((0.716, 0.791, 0.823), 0.05, 0.0, 0.22),
}

EMISSIVE = {
    "research2_glow_warm":             ((1.000, 0.694, 0.392), 3.0),
    "research2_band_light":            ((0.939, 0.054, 0.028), 4.0),
    "research2_led_linear":            ((1.000, 0.905, 0.745), 5.0),
    "research2_display_blue":          ((0.014, 0.114, 0.521), 1.5),
}

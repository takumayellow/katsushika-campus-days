"""図書館（library）の屋内だけで使う材質。imats.py が共有の材質と合わせて登録する。

名前は "library_" で始め、英小文字・数字・_ だけで書く（Unity は名前を小文字にして探す）。
共有の材質（imats.py の COMMON_*）と同じ名前は使えない。共有の材質で足りる色はここに書かない。
値の形は imats.py と同じ:

  PALETTE      名前 -> ((r, g, b), roughness, metallic, 1.0)    色は線形 RGB（0〜1）
  TRANSPARENT  名前 -> ((r, g, b), roughness, metallic, alpha)  透けるもの（0 < alpha < 1）
  EMISSIVE     名前 -> ((r, g, b), strength)                   この棟の PALETTE の材質を光らせる

Unity の InteriorPalette.cs には、統合ブランチで python tools/interior_palette.py が写す。
"""

PALETTE = {
    "library_carpet_olive":        ((0.195, 0.178, 0.084), 0.95, 0.0, 1.0),
    "library_carpet_khaki":        ((0.262, 0.238, 0.117), 0.95, 0.0, 1.0),
    "library_carpet_brown":        ((0.068, 0.034, 0.021), 0.95, 0.0, 1.0),
    "library_wood_light":          ((0.584, 0.371, 0.168), 0.55, 0.0, 1.0),
    "library_wood_slat":           ((0.479, 0.254, 0.102), 0.60, 0.0, 1.0),
    "library_ceiling_wood_dark":   ((0.084, 0.060, 0.036), 0.70, 0.0, 1.0),
    "library_steel_underside":     ((0.068, 0.070, 0.074), 0.50, 0.6, 1.0),
    "library_concrete":            ((0.397, 0.392, 0.361), 0.85, 0.0, 1.0),
    "library_counter_red":         ((0.407, 0.078, 0.033), 0.45, 0.0, 1.0),
    "library_counter_black":       ((0.013, 0.013, 0.013), 0.40, 0.0, 1.0),
    "library_wall_ivory":          ((0.815, 0.776, 0.687), 0.80, 0.0, 1.0),
    "library_louver_black":        ((0.024, 0.024, 0.024), 0.50, 0.3, 1.0),
    "library_signage_screen":      ((0.723, 0.791, 0.871), 0.30, 0.0, 1.0),
    "library_sofa_red":            ((0.434, 0.021, 0.025), 0.80, 0.0, 1.0),
    "library_table_white":         ((0.871, 0.871, 0.839), 0.40, 0.0, 1.0),
    "library_desk_top_dark":       ((0.042, 0.044, 0.047), 0.45, 0.0, 1.0),
    "library_chair_white":         ((0.839, 0.839, 0.807), 0.50, 0.0, 1.0),
    "library_rail_white":          ((0.888, 0.888, 0.871), 0.40, 0.3, 1.0),
    "library_perforated_grey":     ((0.171, 0.171, 0.171), 0.70, 0.2, 1.0),
    "library_partition_white":     ((0.807, 0.807, 0.776), 0.60, 0.0, 1.0),
    "library_wall_darkgrey":       ((0.042, 0.047, 0.051), 0.80, 0.0, 1.0),
    "library_wenge":               ((0.044, 0.023, 0.014), 0.55, 0.0, 1.0),
    "library_mokko_board":         ((0.144, 0.023, 0.018), 0.50, 0.0, 1.0),
    "library_floor_wood_gloss":    ((0.254, 0.112, 0.045), 0.35, 0.0, 1.0),
    "library_rug_beige":           ((0.578, 0.479, 0.323), 0.95, 0.0, 1.0),
    "library_carpet_crimson":      ((0.270, 0.014, 0.024), 0.95, 0.0, 1.0),
    "library_shell_alcast":        ((0.205, 0.231, 0.195), 0.45, 0.7, 1.0),
    "library_shell_joint":         ((1.000, 0.815, 0.552), 0.50, 0.0, 1.0),
    "library_hall_louver":         ((0.624, 0.392, 0.188), 0.60, 0.0, 1.0),
    "library_joint_black":         ((0.010, 0.010, 0.012), 0.60, 0.0, 1.0),
    "library_hall_ceiling":        ((0.847, 0.768, 0.604), 0.85, 0.0, 1.0),
    "library_skylight":            ((1.000, 1.000, 1.000), 0.50, 0.0, 1.0),
    "library_seat_fabric":         ((0.019, 0.025, 0.048), 0.90, 0.0, 1.0),
    "library_seat_wood":           ((0.578, 0.352, 0.162), 0.50, 0.0, 1.0),
    "library_aisle_carpet":        ((0.159, 0.212, 0.262), 0.95, 0.0, 1.0),
    "library_stage_wall":          ((0.694, 0.423, 0.156), 0.60, 0.0, 1.0),
    "library_balcony_front":       ((0.807, 0.738, 0.578), 0.60, 0.0, 1.0),
    "library_wall_washer":         ((1.000, 0.880, 0.672), 0.50, 0.0, 1.0),
    "library_chandelier":          ((1.000, 0.905, 0.745), 0.50, 0.0, 1.0),
    "library_info_grey":           ((0.270, 0.283, 0.305), 0.50, 0.4, 1.0),
    "library_tabchair_green":      ((0.392, 0.479, 0.042), 0.50, 0.0, 1.0),
    "library_lounge_magenta":      ((0.270, 0.023, 0.156), 0.80, 0.0, 1.0),
    "library_ceiling_white":       ((0.888, 0.871, 0.823), 0.90, 0.0, 1.0),
    "library_glass_dark":          ((0.023, 0.023, 0.025), 0.15, 0.3, 1.0),
}

TRANSPARENT = {
    "library_glass_floor":         ((0.716, 0.768, 0.791), 0.05, 0.0, 0.35),
    "library_glass_frit":          ((0.045, 0.050, 0.054), 0.10, 0.0, 0.60),
}

EMISSIVE = {
    "library_signage_screen":      ((0.723, 0.791, 0.871), 2.0),
    "library_shell_joint":         ((1.000, 0.815, 0.552), 3.0),
    "library_skylight":            ((1.000, 1.000, 1.000), 5.0),
    "library_wall_washer":         ((1.000, 0.880, 0.672), 3.0),
    "library_chandelier":          ((1.000, 0.905, 0.745), 4.0),
}

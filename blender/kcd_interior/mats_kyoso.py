"""共創棟（kyoso）の屋内だけで使う材質。imats.py が共有の材質と合わせて登録する。

名前は "kyoso_" で始め、英小文字・数字・_ だけで書く（Unity は名前を小文字にして探す）。
共有の材質（imats.py の COMMON_*）と同じ名前は使えない。共有の材質で足りる色はここに書かない。
値の形は imats.py と同じ:

  PALETTE      名前 -> ((r, g, b), roughness, metallic, 1.0)    色は線形 RGB（0〜1）
  TRANSPARENT  名前 -> ((r, g, b), roughness, metallic, alpha)  透けるもの（0 < alpha < 1）
  EMISSIVE     名前 -> ((r, g, b), strength)                   この棟の PALETTE の材質を光らせる

色は docs/ref/interiors/kyoso/spec.md §3 の HEX（写真の読み取り）を sRGB の式で線形にしたもの。
行末のコメントが元の HEX。床の名前には足音の判定のため floor / wood / carpet のどれかを入れる。
Unity の InteriorPalette.cs には、統合ブランチで python tools/interior_palette.py が写す。
"""

PALETTE = {
    # 床
    "kyoso_floor_terrazzo_pale":      ((0.791, 0.753, 0.687), 0.35, 0.0, 1.0),   # E6E1D8
    "kyoso_floor_terrazzo_mid":       ((0.397, 0.381, 0.347), 0.38, 0.0, 1.0),   # A9A69F
    "kyoso_floor_terrazzo_dark":      ((0.107, 0.109, 0.114), 0.40, 0.0, 1.0),   # 5C5D5F
    "kyoso_floor_wood_oak":           ((0.694, 0.485, 0.270), 0.45, 0.0, 1.0),   # D9B98E
    "kyoso_floor_carpet_grey":        ((0.076, 0.080, 0.084), 0.90, 0.0, 1.0),   # 4E5052
    # 木・天井
    "kyoso_wood_oak":                 ((0.584, 0.376, 0.195), 0.45, 0.0, 1.0),   # C9A57A
    "kyoso_louver_wood":              ((0.644, 0.456, 0.254), 0.55, 0.0, 1.0),   # D2B48A
    "kyoso_ceiling_void":             ((0.013, 0.013, 0.013), 0.90, 0.0, 1.0),   # 1E1E1E
    # 柱・壁
    "kyoso_concrete_exposed":         ((0.485, 0.474, 0.445), 0.85, 0.0, 1.0),   # B9B7B2
    "kyoso_column_pale":              ((0.584, 0.604, 0.597), 0.60, 0.0, 1.0),   # C9CCCB
    "kyoso_column_bluegrey":          ((0.397, 0.497, 0.539), 0.60, 0.0, 1.0),   # A9BBC2
    "kyoso_column_grey":              ((0.262, 0.275, 0.283), 0.60, 0.0, 1.0),   # 8C8F91
    "kyoso_column_base":              ((0.070, 0.074, 0.080), 0.60, 0.0, 1.0),   # 4B4D50
    "kyoso_wall_bluegrey":            ((0.068, 0.091, 0.117), 0.70, 0.0, 1.0),   # 4A5560
    "kyoso_projection_wall":          ((0.624, 0.597, 0.552), 0.80, 0.0, 1.0),   # CFCBC4
    "kyoso_info_beige":               ((0.687, 0.597, 0.451), 0.80, 0.0, 1.0),   # D8CBB3
    # スターバックス
    "kyoso_sb_sage":                  ((0.275, 0.392, 0.314), 0.50, 0.0, 1.0),   # 8FA898
    "kyoso_sage_grey":                ((0.337, 0.434, 0.402), 0.55, 0.0, 1.0),   # 9DB0AA
    "kyoso_sb_pattern":               ((0.434, 0.195, 0.091), 0.60, 0.0, 1.0),   # B07A55
    "kyoso_sb_pattern_line":          ((0.042, 0.023, 0.014), 0.60, 0.0, 1.0),   # 3A2A20
    "kyoso_sb_copper":                ((0.392, 0.130, 0.042), 0.35, 0.8, 1.0),   # A8653A
    "kyoso_sb_top":                   ((0.880, 0.863, 0.823), 0.25, 0.0, 1.0),   # F1EFEA
    "kyoso_tile_hex_white":           ((0.888, 0.888, 0.855), 0.30, 0.0, 1.0),   # F2F2EE
    "kyoso_chalkboard":               ((0.019, 0.021, 0.019), 0.90, 0.0, 1.0),   # 262826
    "kyoso_siren_green":              ((0.000, 0.178, 0.068), 0.40, 0.0, 1.0),   # 00754A
    # ファミリーマート
    "kyoso_fm_white":                 ((0.905, 0.913, 0.888), 0.40, 0.0, 1.0),   # F4F5F2
    "kyoso_fm_green":                 ((0.000, 0.352, 0.051), 0.45, 0.0, 1.0),   # 00A040
    "kyoso_fm_blue":                  ((0.014, 0.050, 0.392), 0.45, 0.0, 1.0),   # 1F3FA8
    # 家具・ラウンジ
    "kyoso_sofa_grey":                ((0.479, 0.462, 0.423), 0.85, 0.0, 1.0),   # B8B5AE
    "kyoso_sofa_charcoal":            ((0.045, 0.045, 0.048), 0.85, 0.0, 1.0),   # 3C3C3E
    "kyoso_sofa_beige":               ((0.610, 0.521, 0.392), 0.85, 0.0, 1.0),   # CDBFA8
    "kyoso_wall_terracotta":          ((0.485, 0.231, 0.188), 0.70, 0.0, 1.0),   # B98478
    "kyoso_wc_taupe":                 ((0.254, 0.212, 0.175), 0.60, 0.0, 1.0),   # 8A7F74
    "kyoso_steel_black":              ((0.023, 0.023, 0.023), 0.45, 0.7, 1.0),   # 2A2A2A
}

TRANSPARENT = {
    "kyoso_glass_frosted":            ((0.815, 0.847, 0.847), 0.30, 0.0, 0.55),  # E9EDED
}

EMISSIVE = {
    "kyoso_siren_green":              ((0.000, 0.178, 0.068), 2.0),
}

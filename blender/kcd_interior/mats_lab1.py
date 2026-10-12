"""実験棟1（lab1）の屋内だけで使う材質。imats.py が共有の材質と合わせて登録する。

名前は "lab1_" で始め、英小文字・数字・_ だけで書く（Unity は名前を小文字にして探す）。
共有の材質（imats.py の COMMON_*）と同じ名前は使えない。共有の材質で足りる色はここに書かない。
値の形は imats.py と同じ:

  PALETTE      名前 -> ((r, g, b), roughness, metallic, 1.0)    色は線形 RGB（0〜1）
  TRANSPARENT  名前 -> ((r, g, b), roughness, metallic, alpha)  透けるもの（0 < alpha < 1）
  EMISSIVE     名前 -> ((r, g, b), strength)                   この棟の PALETTE の材質を光らせる

Unity の InteriorPalette.cs には、統合ブランチで python tools/interior_palette.py が写す。
"""

PALETTE = {}

TRANSPARENT = {}

EMISSIVE = {}

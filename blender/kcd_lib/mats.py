"""マテリアル登録。Unity 側はマテリアル名で判別するので名前が契約（DESIGN.md §3.1）。"""

import bpy

# name -> (base_color RGB 0..1, roughness, metallic, alpha)
PALETTE = {
    # 建築
    "concrete_grey":   ((0.456, 0.445, 0.413), 0.78, 0.0, 1.0),  # #B4B2AC
    "concrete_light":  ((0.687, 0.672, 0.631), 0.80, 0.0, 1.0),  # #D8D6D0
    "concrete_dark":   ((0.254, 0.246, 0.231), 0.82, 0.0, 1.0),  # #8A8884
    "brick_red":       ((0.270, 0.044, 0.028), 0.85, 0.0, 1.0),  # #8E3B2F
    "glass_dark":      ((0.027, 0.042, 0.054), 0.10, 0.35, 1.0),  # #2E3A42
    "glass_clear":     ((0.347, 0.552, 0.687), 0.06, 0.20, 1.0),  # #9FC4D8
    "metal_white":     ((0.761, 0.761, 0.730), 0.35, 0.55, 1.0),  # #E2E2DE
    "metal_grey":      ((0.323, 0.323, 0.305), 0.30, 0.80, 1.0),  # #9A9A96
    "louver_white":    ((0.847, 0.847, 0.807), 0.45, 0.30, 1.0),  # #EDEDE8
    "roof_grey":       ((0.156, 0.156, 0.144), 0.90, 0.0, 1.0),  # #6E6E6A
    # 入口（kcd_lib.entrances）。Unity 側の同名マテリアル（内装と共用）に合わせる
    "wall_accent_navy": ((0.333, 0.412, 0.545), 0.60, 0.0, 1.0),
    "plastic_black":   ((0.259, 0.259, 0.267), 0.50, 0.0, 1.0),
    "light_panel":     ((1.000, 0.992, 0.969), 0.40, 0.0, 1.0),
    # 外構
    "stone_light":     ((0.624, 0.604, 0.552), 0.75, 0.0, 1.0),  # #CFCCC4
    "stone_dark":      ((0.392, 0.371, 0.328), 0.78, 0.0, 1.0),  # #A8A49B
    "asphalt":         ((0.045, 0.045, 0.045), 0.92, 0.0, 1.0),  # #3C3C3C
    "line_white":      ((0.900, 0.900, 0.880), 0.70, 0.0, 1.0),
    "grass":           ((0.159, 0.392, 0.068), 0.92, 0.0, 1.0),  # #6FA84A
    "grass_dark":      ((0.076, 0.212, 0.034), 0.93, 0.0, 1.0),  # #4E7F34
    "water":           ((0.275, 0.479, 0.578), 0.05, 0.0, 0.80),  # #8FB8C8 半透明（site_water）
    "soil":            ((0.400, 0.300, 0.200), 0.94, 0.0, 1.0),
    # モール北側の花壇（#56）。MaterialLibrary.CampusColors と同じ色
    "bed_soil":        ((0.102, 0.056, 0.031), 0.95, 0.0, 1.0),  # #5A4331
    "flower_leaf":     ((0.048, 0.195, 0.027), 0.90, 0.0, 1.0),  # #3E7A2E
    "flower_red":      ((0.694, 0.056, 0.076), 0.70, 0.0, 1.0),  # #D9434E
    "flower_yellow":   ((0.888, 0.578, 0.070), 0.70, 0.0, 1.0),  # #F2C84B
    "flower_white":    ((0.905, 0.880, 0.823), 0.70, 0.0, 1.0),  # #F4F1EA
    "flower_pink":     ((0.815, 0.275, 0.434), 0.70, 0.0, 1.0),  # #E98FB0
    "sand":            ((0.687, 0.578, 0.323), 0.92, 0.0, 1.0),  # #D8C89A
    "wood":            ((0.323, 0.147, 0.050), 0.80, 0.0, 1.0),  # #9A6B3F
    # 樹木・小物
    # 葉は理科大グリーン #00843D 基準の 4 段ランプ（#51）。以前の leaf は
    # grass と同じ #6FA84A で、芝の上に置くと樹冠が溶けて見えなかった。
    #
    # ここの値は Blender のプレビュー専用。FBX が Unity へ渡すのはマテリアル「名前」だけで、
    # 実際の色は MaterialLibrary.CampusColors が決める。両者がずれていると、
    # プレビューを見て色を決めてもゲームでは別の色になる（#51 の採点がまさにこれで狂った）。
    # 値を変えるときは必ず CampusColors と揃える。コメントの hex が突き合わせ用の鍵。
    "leaf_dark":       ((0.003, 0.147, 0.040), 0.90, 0.0, 1.0),  # #0A6B38
    "leaf":            ((0.004, 0.262, 0.060), 0.90, 0.0, 1.0),  # #0C8C45
    "leaf_light":      ((0.072, 0.423, 0.105), 0.90, 0.0, 1.0),  # #4CAE5B
    "leaf_top":        ((0.270, 0.597, 0.125), 0.90, 0.0, 1.0),  # #8ECB63
    # 幹は #A09385 の明るい灰褐色だった。暗い樹冠を背に明るい枝が槍のように立って
    # いちばん目立つ欠陥になっていたので、Unity 側がすでに宣言していた濃い茶へ揃える。
    "trunk":           ((0.147, 0.068, 0.028), 0.92, 0.0, 1.0),  # #6B4A2F
    # サイン
    "sign_starbucks_green": ((0.000, 0.122, 0.053), 0.55, 0.0, 1.0),  # #006241
    "sign_familymart_green": ((0.000, 0.319, 0.058), 0.55, 0.0, 1.0),  # #009944
    "sign_familymart_blue": ((0.000, 0.138, 0.474), 0.55, 0.0, 1.0),  # #0068B7
    "sign_familymart_white": ((1.000, 1.000, 1.000), 0.55, 0.0, 1.0),  # #FFFFFF
    "sign_plate":      ((0.730, 0.730, 0.708), 0.60, 0.0, 1.0),  # #DEDEDB
    "tus_green":       ((0.000, 0.231, 0.047), 0.55, 0.0, 1.0),  # #00843D
    # 外構小物（自販機・ゴミ箱）
    "vending_red":     ((0.539, 0.010, 0.009), 0.45, 0.0, 1.0),  # #C21A17
    "vending_blue":    ((0.005, 0.089, 0.479), 0.45, 0.0, 1.0),  # #0F54B8
    "bin_green":       ((0.022, 0.133, 0.040), 0.60, 0.0, 1.0),  # #296638
    "bike_frame":      ((0.027, 0.027, 0.033), 0.45, 0.60, 1.0),  # #2E2E33
    "bike_tire":       ((0.005, 0.005, 0.005), 0.85, 0.0, 1.0),  # #0F0F0F
}

# 面ごとの色を持たせる頂点カラーの名前（mesh.MeshBuilder が書く）。
COLOR_ATTR = "Col"

# 基本色を頂点カラーで決めるマテリアル。Blender のプレビューでは頂点カラーを基本色につなぎ、
# Unity 側は MaterialLibrary.VertexColored が _VERTEXCOLOR_ON を入れて白 × 頂点カラーにする（#51）。
# 葉は 1 本の木の中で PALETTE の leaf_dark〜leaf_top の間の色を面ごとに焼くので、
# 上の "leaf" の値はプレビューのワークベンチ表示と、CampusColors との突き合わせにだけ使う。
VERTEX_COLORED = ("leaf",)

# 背景建物用のクリーム〜グレー
BG_COLORS = [
    ((0.780, 0.755, 0.700), 0.85),
    ((0.700, 0.690, 0.665), 0.85),
    ((0.640, 0.640, 0.630), 0.85),
    ((0.820, 0.800, 0.760), 0.85),
    ((0.560, 0.560, 0.565), 0.85),
    ((0.720, 0.700, 0.645), 0.85),
]


def _make(name, rgb, rough, metal, alpha):
    mat = bpy.data.materials.get(name)
    if mat is not None:
        return mat
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is None:
        bsdf = mat.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if "Alpha" in bsdf.inputs:
        bsdf.inputs["Alpha"].default_value = alpha
    if name in VERTEX_COLORED:
        attr = mat.node_tree.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = COLOR_ATTR
        mat.node_tree.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    mat.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)  # Workbench / ビューポート用
    mat.roughness = rough
    mat.metallic = metal
    if alpha < 1.0:
        # 半透明（水面）。Eevee Next はブレンド方式を surface_render_method で持つ
        for attr, val in (("surface_render_method", "BLENDED"), ("blend_method", "BLEND"),
                          ("show_transparent_back", False)):
            try:
                setattr(mat, attr, val)
            except Exception:
                pass
    return mat


def build_all():
    for name, (rgb, rough, metal, alpha) in PALETTE.items():
        _make(name, rgb, rough, metal, alpha)
    for i, (rgb, rough) in enumerate(BG_COLORS):
        _make("bg_wall_%d" % i, rgb, rough, 0.0, 1.0)


def get(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        raise KeyError("material not registered: %s" % name)
    return mat

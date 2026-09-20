"""マテリアル登録。Unity 側はマテリアル名で判別するので名前が契約（DESIGN.md §3.1）。"""

import bpy

# name -> (base_color RGB 0..1, roughness, metallic, alpha)
PALETTE = {
    # 建築
    "concrete_grey":   ((0.588, 0.592, 0.580), 0.78, 0.0, 1.0),
    "concrete_light":  ((0.760, 0.756, 0.735), 0.80, 0.0, 1.0),
    "concrete_dark":   ((0.360, 0.362, 0.358), 0.82, 0.0, 1.0),
    "brick_red":       ((0.360, 0.130, 0.095), 0.85, 0.0, 1.0),
    "glass_dark":      ((0.055, 0.075, 0.090), 0.10, 0.35, 1.0),
    "glass_clear":     ((0.300, 0.420, 0.450), 0.06, 0.20, 1.0),
    "metal_white":     ((0.880, 0.885, 0.880), 0.35, 0.55, 1.0),
    "metal_grey":      ((0.480, 0.490, 0.500), 0.30, 0.80, 1.0),
    "louver_white":    ((0.820, 0.825, 0.820), 0.45, 0.30, 1.0),
    "roof_grey":       ((0.300, 0.305, 0.300), 0.90, 0.0, 1.0),
    # 外構
    "stone_light":     ((0.700, 0.695, 0.670), 0.75, 0.0, 1.0),
    "stone_dark":      ((0.520, 0.518, 0.500), 0.78, 0.0, 1.0),
    "asphalt":         ((0.145, 0.145, 0.148), 0.92, 0.0, 1.0),
    "line_white":      ((0.900, 0.900, 0.880), 0.70, 0.0, 1.0),
    "grass":           ((0.250, 0.430, 0.170), 0.92, 0.0, 1.0),
    "grass_dark":      ((0.310, 0.372, 0.250), 0.93, 0.0, 1.0),
    "water":           ((0.045, 0.110, 0.130), 0.05, 0.0, 1.0),
    "soil":            ((0.400, 0.300, 0.200), 0.94, 0.0, 1.0),
    "sand":            ((0.760, 0.690, 0.520), 0.92, 0.0, 1.0),
    "wood":            ((0.400, 0.260, 0.140), 0.80, 0.0, 1.0),
    # 樹木・小物
    "leaf":            ((0.205, 0.372, 0.155), 0.90, 0.0, 1.0),
    "leaf_light":      ((0.290, 0.500, 0.170), 0.90, 0.0, 1.0),
    "trunk":           ((0.352, 0.292, 0.235), 0.92, 0.0, 1.0),
    # サイン
    "sign_starbucks_green": ((0.000, 0.380, 0.230), 0.55, 0.0, 1.0),
    "sign_familymart_green": ((0.000, 0.560, 0.360), 0.55, 0.0, 1.0),
    "sign_familymart_blue": ((0.000, 0.330, 0.620), 0.55, 0.0, 1.0),
    "sign_familymart_white": ((0.940, 0.940, 0.930), 0.55, 0.0, 1.0),
    "sign_plate":      ((0.870, 0.870, 0.860), 0.60, 0.0, 1.0),
    "tus_green":       ((0.000, 0.517, 0.239), 0.55, 0.0, 1.0),
}

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
    mat.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)  # Workbench / ビューポート用
    mat.roughness = rough
    mat.metallic = metal
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

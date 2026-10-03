"""インテリア専用マテリアルの追加登録。

kcd_lib.mats のパレットは触らず、ここで足りない名前だけを同じ流儀
（Principled BSDF・名前が Unity との契約）で bpy.data.materials に足す。
既に同名が登録されていれば何もしない（= 外装側の定義が勝つ）。

何棟かで使う材質はこのファイルの COMMON_*、1 棟だけで使う材質はその棟の mats_<id>.py に書く。
compose() が両方を合わせて PALETTE / TRANSPARENT / EMISSIVE を作る。建物担当はこのファイルを
触らず、自分の棟の mats_<id>.py にだけ足す。
"""

import re

import bpy

from . import (mats_greenhouse, mats_gym, mats_kyoso, mats_lab1, mats_lab2,
               mats_lecture, mats_library, mats_research1, mats_research2)

# name -> (rgb, roughness, metallic, alpha)
COMMON_PALETTE = {
    # ---- 床 ----
    "floor_tile_white":   ((0.855, 0.850, 0.830), 0.30, 0.02, 1.0),
    "floor_tile_grey":    ((0.560, 0.565, 0.565), 0.34, 0.02, 1.0),
    "floor_tile_dark":    ((0.230, 0.235, 0.240), 0.38, 0.02, 1.0),
    "floor_wood":         ((0.520, 0.352, 0.198), 0.48, 0.0, 1.0),
    "floor_wood_light":   ((0.760, 0.592, 0.352), 0.42, 0.0, 1.0),
    "floor_carpet_blue":  ((0.130, 0.185, 0.330), 0.95, 0.0, 1.0),
    "floor_carpet_grey":  ((0.300, 0.305, 0.315), 0.95, 0.0, 1.0),
    "floor_carpet_red":   ((0.330, 0.105, 0.110), 0.95, 0.0, 1.0),
    "floor_concrete":     ((0.545, 0.545, 0.535), 0.72, 0.0, 1.0),
    "floor_resin_grey":   ((0.415, 0.425, 0.430), 0.46, 0.0, 1.0),
    # ---- 壁・天井 ----
    "wall_white":         ((0.895, 0.893, 0.880), 0.62, 0.0, 1.0),
    "wall_grey":          ((0.690, 0.692, 0.690), 0.66, 0.0, 1.0),
    "wall_wood":          ((0.470, 0.325, 0.190), 0.60, 0.0, 1.0),
    "wall_accent_green":  ((0.000, 0.430, 0.215), 0.58, 0.0, 1.0),
    "wall_accent_navy":   ((0.090, 0.140, 0.260), 0.60, 0.0, 1.0),
    "ceiling_white":      ((0.925, 0.925, 0.915), 0.72, 0.0, 1.0),
    "ceiling_grid":       ((0.640, 0.645, 0.650), 0.60, 0.20, 1.0),
    "ceiling_dark":       ((0.155, 0.158, 0.162), 0.80, 0.0, 1.0),
    # ---- 照明・サイン ----
    "light_panel":        ((1.000, 0.985, 0.930), 0.20, 0.0, 1.0),
    "light_strip":        ((1.000, 0.960, 0.870), 0.20, 0.0, 1.0),
    "sign_exit_green":    ((0.055, 0.700, 0.320), 0.30, 0.0, 1.0),
    "screen_white":       ((0.930, 0.930, 0.925), 0.42, 0.0, 1.0),
    "screen_blue":        ((0.075, 0.180, 0.360), 0.24, 0.0, 1.0),
    "board_white":        ((0.940, 0.942, 0.935), 0.28, 0.0, 1.0),
    "board_green":        ((0.110, 0.290, 0.210), 0.55, 0.0, 1.0),
    "paper_white":        ((0.940, 0.938, 0.920), 0.78, 0.0, 1.0),
    "sign_plate_blue":    ((0.075, 0.230, 0.430), 0.45, 0.0, 1.0),
    # ---- 家具 ----
    "desk_wood":          ((0.635, 0.455, 0.265), 0.55, 0.0, 1.0),
    "desk_white":         ((0.870, 0.868, 0.855), 0.45, 0.0, 1.0),
    "desk_dark":          ((0.185, 0.145, 0.115), 0.52, 0.0, 1.0),
    "counter_dark":       ((0.135, 0.105, 0.085), 0.42, 0.0, 1.0),
    "counter_wood":       ((0.420, 0.268, 0.145), 0.50, 0.0, 1.0),
    "counter_stone":      ((0.180, 0.180, 0.185), 0.28, 0.05, 1.0),
    "chair_blue":         ((0.120, 0.235, 0.470), 0.66, 0.0, 1.0),
    "chair_hall_red":     ((0.360, 0.070, 0.085), 0.72, 0.0, 1.0),
    "chair_green":        ((0.085, 0.330, 0.215), 0.68, 0.0, 1.0),
    "chair_orange":       ((0.720, 0.330, 0.080), 0.68, 0.0, 1.0),
    "chair_grey":         ((0.245, 0.250, 0.258), 0.70, 0.0, 1.0),
    "fabric_beige":       ((0.700, 0.640, 0.540), 0.88, 0.0, 1.0),
    "fabric_green":       ((0.190, 0.340, 0.250), 0.88, 0.0, 1.0),
    "cushion_red":        ((0.430, 0.130, 0.135), 0.88, 0.0, 1.0),
    # ---- 金属・設備 ----
    "metal_gray":         ((0.480, 0.490, 0.500), 0.35, 0.80, 1.0),
    "metal_dark":         ((0.150, 0.155, 0.160), 0.42, 0.75, 1.0),
    "stainless":          ((0.700, 0.705, 0.712), 0.22, 0.90, 1.0),
    "plastic_white":      ((0.885, 0.885, 0.875), 0.40, 0.0, 1.0),
    "plastic_black":      ((0.055, 0.055, 0.058), 0.45, 0.0, 1.0),
    "rubber_black":       ((0.038, 0.038, 0.040), 0.88, 0.0, 1.0),
    "fire_red":           ((0.560, 0.055, 0.045), 0.45, 0.0, 1.0),
    "pipe_grey":          ((0.560, 0.575, 0.585), 0.40, 0.60, 1.0),
    "gas_green":          ((0.055, 0.330, 0.190), 0.40, 0.35, 1.0),
    "gas_blue":           ((0.060, 0.230, 0.470), 0.40, 0.35, 1.0),
    # ---- 本（背表紙） ----
    "book_a":             ((0.540, 0.130, 0.120), 0.80, 0.0, 1.0),
    "book_b":             ((0.110, 0.260, 0.480), 0.80, 0.0, 1.0),
    "book_c":             ((0.135, 0.350, 0.210), 0.80, 0.0, 1.0),
    "book_d":             ((0.620, 0.480, 0.150), 0.80, 0.0, 1.0),
    "book_e":             ((0.320, 0.170, 0.400), 0.80, 0.0, 1.0),
    "book_f":             ((0.720, 0.700, 0.640), 0.80, 0.0, 1.0),
    "book_g":             ((0.580, 0.290, 0.110), 0.80, 0.0, 1.0),
    "book_h":             ((0.140, 0.145, 0.160), 0.80, 0.0, 1.0),
    # ---- 体育館 ----
    "court_line":         ((0.930, 0.930, 0.915), 0.55, 0.0, 1.0),
    "court_line_blue":    ((0.120, 0.250, 0.520), 0.60, 0.0, 1.0),
    "backboard_white":    ((0.900, 0.905, 0.900), 0.25, 0.0, 1.0),
    "hoop_orange":        ((0.760, 0.290, 0.055), 0.45, 0.30, 1.0),
    "net_white":          ((0.880, 0.880, 0.870), 0.70, 0.0, 1.0),
    "curtain_blue":       ((0.115, 0.215, 0.400), 0.92, 0.0, 1.0),
    # ---- 店舗 ----
    "sb_green":           ((0.000, 0.380, 0.230), 0.50, 0.0, 1.0),
    "sb_wood":            ((0.330, 0.205, 0.115), 0.55, 0.0, 1.0),
    "fm_green":           ((0.000, 0.560, 0.360), 0.50, 0.0, 1.0),
    "fm_blue":            ((0.000, 0.330, 0.620), 0.50, 0.0, 1.0),
    "cake_pink":          ((0.880, 0.640, 0.640), 0.60, 0.0, 1.0),
    "coffee_brown":       ((0.230, 0.130, 0.070), 0.55, 0.0, 1.0),
    # ---- 植栽・その他 ----
    "plant_green":        ((0.140, 0.340, 0.130), 0.88, 0.0, 1.0),
    "plant_pot":          ((0.400, 0.310, 0.255), 0.80, 0.0, 1.0),
    "soil_dark":          ((0.185, 0.135, 0.095), 0.95, 0.0, 1.0),
    "tray_beige":         ((0.640, 0.560, 0.430), 0.65, 0.0, 1.0),
}

# 透けるもの（アルファは EEVEE の blend 設定も要る）
COMMON_TRANSPARENT = {
    "glass_interior":  ((0.620, 0.720, 0.740), 0.05, 0.0, 0.22),
    "glass_partition": ((0.760, 0.820, 0.830), 0.05, 0.0, 0.16),
}

# 発光するもの -> (emission rgb, strength)
COMMON_EMISSIVE = {
    "light_panel":      ((1.00, 0.975, 0.920), 4.5),
    "light_strip":      ((1.00, 0.955, 0.870), 5.5),
    "sign_exit_green":  ((0.10, 0.950, 0.420), 3.0),
    "screen_blue":      ((0.18, 0.420, 0.850), 1.6),
    "screen_white":     ((0.92, 0.940, 0.980), 0.22),
}

BOOK_MATS = ["book_a", "book_b", "book_c", "book_d",
             "book_e", "book_f", "book_g", "book_h"]

# 棟ごとの材質ファイル（並びは registry.ORDER と同じ）
BUILDING_MATS = {
    "research1": mats_research1,
    "lecture": mats_lecture,
    "research2": mats_research2,
    "kyoso": mats_kyoso,
    "library": mats_library,
    "gym": mats_gym,
    "lab1": mats_lab1,
    "lab2": mats_lab2,
    "greenhouse": mats_greenhouse,
}


def _is_unit(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool) and 0.0 <= x <= 1.0


def _is_rgb(rgb):
    return isinstance(rgb, tuple) and len(rgb) == 3 and all(_is_unit(c) for c in rgb)


def _check_surface(where, name, value, opaque):
    """PALETTE / TRANSPARENT の値 ((r, g, b), roughness, metallic, alpha) の形を確かめる。"""
    ok = (isinstance(value, tuple) and len(value) == 4 and _is_rgb(value[0])
          and all(_is_unit(v) for v in value[1:]))
    if ok:
        alpha = value[3]
        ok = alpha == 1.0 if opaque else 0.0 < alpha < 1.0
    if not ok:
        raise ValueError("%s の %s の値 %r は ((r, g, b), roughness, metallic, alpha) で、"
                         "0〜1 の数にする（alpha は %s）"
                         % (where, name, value, "1.0" if opaque else "0 より大きく 1 未満"))


def _check_emission(where, name, value):
    """EMISSIVE の値 ((r, g, b), strength) の形を確かめる。"""
    ok = (isinstance(value, tuple) and len(value) == 2 and _is_rgb(value[0])
          and isinstance(value[1], (int, float)) and not isinstance(value[1], bool)
          and value[1] > 0.0)
    if not ok:
        raise ValueError("%s の %s の値 %r は ((r, g, b), strength) で、色は 0〜1、"
                         "strength は正の数にする" % (where, name, value))


def compose(common, buildings):
    """共有の材質に棟ごとの材質を足し、(PALETTE, TRANSPARENT, EMISSIVE, OWNER) を返す。

    common    : 共有の (PALETTE, TRANSPARENT, EMISSIVE)
    buildings : {棟 ID: PALETTE / TRANSPARENT / EMISSIVE を持つモジュール}
    OWNER     : 棟の材質名 -> 棟 ID

    次のときは ValueError:
      - 同じ名前が 2 か所にある（共有と棟、同じ棟の PALETTE と TRANSPARENT など）
      - 棟の材質名が "<id>_" で始まる英小文字・数字・_ の名前でない（EMISSIVE も同じ）
      - 棟の EMISSIVE の名前が、その棟の PALETTE に無い
      - 値の形が違う（PALETTE の alpha は 1.0、TRANSPARENT の alpha は 0 より大きく 1 未満）
    """
    common_p, common_t, common_e = common
    palette, transparent, emissive = dict(common_p), dict(common_t), dict(common_e)
    owner = {}
    where_of = {}

    def claim(name, where):
        if name in where_of:
            raise ValueError("材質名 %s が %s と %s で重なっている" % (name, where_of[name], where))
        where_of[name] = where

    for kind, table, opaque in (("PALETTE", common_p, True), ("TRANSPARENT", common_t, False)):
        for name, value in table.items():
            where = "imats.COMMON_" + kind
            _check_surface(where, name, value, opaque)
            claim(name, where)
    for name, value in common_e.items():
        _check_emission("imats.COMMON_EMISSIVE", name, value)

    for bid, mod in buildings.items():
        rule = re.compile(re.escape(bid) + r"_[a-z0-9_]+")
        tables = {kind: dict(getattr(mod, kind)) for kind in ("PALETTE", "TRANSPARENT", "EMISSIVE")}
        for kind in ("PALETTE", "TRANSPARENT", "EMISSIVE"):
            where = "mats_%s.%s" % (bid, kind)
            for name, value in tables[kind].items():
                if not (isinstance(name, str) and rule.fullmatch(name)):
                    raise ValueError("%s の %r は \"%s_\" で始まる英小文字・数字・_ の名前にする"
                                     % (where, name, bid))
                if kind == "EMISSIVE":
                    if name not in tables["PALETTE"]:
                        raise ValueError("%s の %s が mats_%s.PALETTE に無い" % (where, name, bid))
                    _check_emission(where, name, value)
                    continue
                _check_surface(where, name, value, kind == "PALETTE")
                claim(name, where)
                owner[name] = bid
        palette.update(tables["PALETTE"])
        transparent.update(tables["TRANSPARENT"])
        emissive.update(tables["EMISSIVE"])
    return palette, transparent, emissive, owner


# 登録するのはこの 3 つ（共有 + 全棟）。Unity の InteriorPalette.cs はこれを写したもの
PALETTE, TRANSPARENT, EMISSIVE, OWNER = compose(
    (COMMON_PALETTE, COMMON_TRANSPARENT, COMMON_EMISSIVE), BUILDING_MATS)


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
    emit = EMISSIVE.get(name)
    if emit is not None:
        col, strength = emit
        for key in ("Emission Color", "Emission"):
            if key in bsdf.inputs:
                try:
                    bsdf.inputs[key].default_value = (col[0], col[1], col[2], 1.0)
                except Exception:
                    pass
                break
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = strength
    if alpha < 1.0:
        for attr, val in (("blend_method", "BLEND"),
                          ("surface_render_method", "BLENDED"),
                          ("show_transparent_back", False)):
            try:
                setattr(mat, attr, val)
            except Exception:
                pass
    mat.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)
    mat.roughness = rough
    mat.metallic = metal
    return mat


def build_all():
    """外装のパレットを壊さずに、インテリア用マテリアルを足す。"""
    for name, (rgb, rough, metal, alpha) in PALETTE.items():
        _make(name, rgb, rough, metal, alpha)
    for name, (rgb, rough, metal, alpha) in TRANSPARENT.items():
        _make(name, rgb, rough, metal, alpha)
    return len(PALETTE) + len(TRANSPARENT)


def book(i):
    return BOOK_MATS[i % len(BOOK_MATS)]

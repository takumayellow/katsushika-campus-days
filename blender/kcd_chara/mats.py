"""マテリアル生成。

Unity 側の `CharacterImporter.cs` は **マテリアル名** で Toon マテリアルに
差し替えるので、Blender では名前と基本色（＋顔と和柄のテクスチャ）だけ正しく
持たせればよい。ビューポート表示色も入れて Workbench レンダでも色が出るようにする。
"""

from __future__ import annotations

import math

import bpy


def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(rgb) -> tuple[float, float, float, float]:
    r, g, b = rgb[:3]
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)


def _srgb_to_linear(v: float) -> float:
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def hexc(h: str) -> tuple[float, float, float]:
    """`#RRGGBB` を 0..1 の sRGB 値へ。リニア変換は `lin()` が行う。"""
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))  # type: ignore[return-value]


def make_material(name: str, rgb, *, roughness: float = 0.62,
                  metallic: float = 0.0, image=None, uv: bool = True,
                  tex_scale: float = 1.0, alpha: float = 1.0):
    """Principled BSDF 1 枚のシンプルなマテリアルを作る。"""
    mat = bpy.data.materials.get(name)
    if mat is not None:
        bpy.data.materials.remove(mat)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if bsdf is None:
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        out = nt.nodes.get("Material Output") or nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(bsdf.outputs[0], out.inputs[0])

    col = lin(rgb)
    _set(bsdf, "Base Color", col)
    _set(bsdf, "Roughness", roughness)
    _set(bsdf, "Metallic", metallic)
    _set(bsdf, "Specular IOR Level", 0.28)
    _set(bsdf, "Alpha", alpha)
    if alpha < 1.0:
        mat.blend_method = "BLEND"

    if image is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.interpolation = "Smart"
        tex.location = (-420, 200)
        if uv:
            src = nt.nodes.new("ShaderNodeUVMap")
            src.uv_map = "UVMap"
            src.location = (-800, 200)
            nt.links.new(src.outputs[0], tex.inputs[0])
        else:
            src = nt.nodes.new("ShaderNodeTexCoord")
            src.location = (-980, 200)
            mp = nt.nodes.new("ShaderNodeMapping")
            mp.location = (-780, 200)
            mp.inputs["Scale"].default_value = (tex_scale, tex_scale, tex_scale)
            # 和柄を 1 枚の平面投影（Rotation=(pi/2,0,0) の Y 軸投影）で貼ると、
            # 法線が ±X を向く面（袖の外側・肩・胴の真横）はテクスチャの 1 本の
            # 線を引き伸ばすだけになり、絣の十字が消えて縦縞になる。側面の
            # プレビューで袖が「白い板」に見えていたのはこれ。
            # ボックス投影なら面の向きに応じて XZ / YZ / XY の 3 面から選ぶので、
            # どちらを向いた面にも十字が乗る。FBX には materialのノードは
            # 入らないので、Unity 側の見え方はこの変更では変わらない。
            tex.projection = "BOX"
            tex.projection_blend = 0.25
            nt.links.new(src.outputs["Generated"], mp.inputs[0])
            nt.links.new(mp.outputs[0], tex.inputs[0])
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

    mat.diffuse_color = col
    mat.roughness = roughness
    mat.metallic = metallic
    return mat


def _set(node, key: str, value) -> None:
    if key in node.inputs:
        node.inputs[key].default_value = value


# --------------------------------------------------------------------------

#: 全キャラ共通で使うマテリアル名 → 既定色（sRGB）
BASE_COLORS: dict[str, str] = {
    "skin": "#F6C7AE",
    "hair": "#C9A27E",
    "eye_white": "#F4F2F4",
    "eye_l": "#57D2DB",
    "eye_r": "#57D2DB",
    "face": "#F6C7AE",
    "cloth_blouse": "#FDFDFA",
    "cloth_ribbon_green": "#00843D",
    "cloth_skirt_navy": "#26304E",
    "cloth_socks_black": "#2A2A30",
    "shoes_loafer": "#4A2F22",
    "cloth_kimono_kasuri_blue": "#F4F2EC",
    "cloth_hakama_blue": "#1F4C8F",
    "cloth_kimono_yagasuri_red": "#FCF6F3",
    "cloth_hakama_purple": "#4C2A70",
    "ribbon_red": "#D8222F",
    "collar_white": "#F6F1E6",
    "boots_brown": "#7A4A28",
    "geta_wood": "#C8A063",
    "furoshiki_red": "#C4212B",
    "furoshiki_orange": "#D2782E",
    "bag_tote": "#8D6A4B",
    "glasses": "#C02A2A",
    "labcoat": "#F5F6F7",
    "cloth_apron": "#F2E5D2",
    "cloth_skirt_pink": "#D9606E",
    "cloth_pants_gray": "#4C4F58",
    "metal": "#B9BCC4",
    "lash": "#33222A",
    "brow": "#8A6440",
    "eye_rim": "#5A3A3A",
    "cloth_hoodie": "#8FD3E8",
    "cloth_bandana": "#E8555F",
    "necktie": "#2C3E6B",
    "shoes_sneaker": "#F3F3F5",
    "outline": "#141118",
}


def build_materials(p: dict, face_image, pattern_images: dict) -> dict:
    """キャラ 1 体分のマテリアル辞書を作る。"""
    out: dict = {}
    over = p.get("mat_colors", {})

    def col(name: str):
        return hexc(over.get(name, BASE_COLORS.get(name, "#FF00FF")))

    def rgb_of(name: str):
        """まつ毛・眉はキャラごとの色をそのまま使う。"""
        if name in over:
            return hexc(over[name])
        if name in ("skin", "face") and "skin" in p:
            # 顔テクスチャはキャラ固有の肌色で描く。体だけ共通色にすると
            # 首から上だけ色が違って見える。
            return tuple(p["skin"])[:3]
        if name == "lash" and "lash_color" in p:
            return tuple(p["lash_color"])[:3]
        if name == "brow" and "brow_color" in p:
            return tuple(p["brow_color"])[:3]
        if name == "eye_rim" and "lash_color" in p:
            c = tuple(p["lash_color"])[:3]
            return tuple(min(1.0, v * 0.55 + 0.30) for v in c)
        return hexc(BASE_COLORS.get(name, "#FF00FF"))

    for name in BASE_COLORS:
        if name in ("face", "eye_l", "eye_r", "eye_white"):
            continue
        rough = 0.55
        if name.startswith("shoes") or name.startswith("boots") or name == "glasses":
            rough = 0.35
        if name == "hair":
            rough = 0.42
        if name == "metal":
            rough = 0.25
        # Unity 側はマテリアル名で識別するので、名前は接頭辞なしの素の名前にする
        if name in ("lash", "brow", "eye_rim"):
            rough = 0.48
        if name == "outline":
            rough = 1.0
        out[name] = make_material(name, rgb_of(name), roughness=rough,
                                  metallic=0.9 if name == "metal" else 0.0)

    # 顔まわりは共通の face.png を同じ平面投影 UV で参照する
    for name in ("face", "eye_white", "eye_l", "eye_r"):
        out[name] = make_material(name, col(name), roughness=0.46,
                                  image=face_image, uv=True)

    # 和柄（Generated 座標でタイリングするので UV 展開は不要）
    if "kasuri" in pattern_images:
        out["cloth_kimono_kasuri_blue"] = make_material(
            "cloth_kimono_kasuri_blue", col("cloth_kimono_kasuri_blue"),
            roughness=0.7, image=pattern_images["kasuri"], uv=False,
            tex_scale=p.get("pattern_scale", 7.0))
    if "yagasuri" in pattern_images:
        out["cloth_kimono_yagasuri_red"] = make_material(
            "cloth_kimono_yagasuri_red", col("cloth_kimono_yagasuri_red"),
            roughness=0.7, image=pattern_images["yagasuri"], uv=False,
            tex_scale=p.get("pattern_scale", 6.0))

    return out

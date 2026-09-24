"""キャンパスの FBX を 1 つ読み、単色（flat）か面のテクスチャ（textured）で同じ視点を描く。

面のテクスチャと色を変えた前後を並べるための素材（#59 の比較用）。ふつうは
`surfaces_before_after.sh` から before / after の 2 回呼ばれ、`compose_before_after.py`
が左右に並べる。単独で回すときは:

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup \
      --python-exit-code 1 --python blender/tools_view/surfaces_before_after.py -- \
      --fbx <campus.fbx> --mode textured --out-dir <出力先> \
      [--trees-fbx <trees.fbx> --trees-json <trees.json>] [--palette-ref origin/main]

出力: <out-dir>/<視点>-<mode>.png（1280x720、全景・モール・図書館と地上目線の 3 視点）

- 色は Unity の MaterialLibrary.cs（CampusColors → InteriorPalette → B0B0AC）を読んで決める。
  --palette-ref を渡すと、その ref の .cs を読む（before を main の色で描くため）。
- textured では data/textures/surfaces.json に載っていて、画像があり、その面に UV が
  書かれているものだけ画像を貼る。Scale = 100 / tile_cm（UV はメートル）。
- ライトとワールドは kcd_lib.render.setup_world、既存の 3 視点は build_campus.CAM_SPECS を使う。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "blender"))

import bpy  # noqa: E402
import numpy as np  # noqa: E402

import build_campus as BC  # noqa: E402
from kcd_lib import render, uv  # noqa: E402

UV_LAYER = "UVMap"
DEFAULT_HEX = "B0B0AC"
EDITOR_DIR = "unity/KatsushikaCampusDays/Assets/Scripts/Editor/"

#: 地上目線の近景。build_campus.CAM_SPECS と同じ (名前, 視点 (u, v, z), 注視点 (u, v, z),
#: 焦点距離) に、樹木を置かない半径を足したもの。u, v は第 1 研究棟の長辺に沿った軸
CLOSE_SPECS = [
    ("mall-beds", (114.0, -25.0, 1.60), (106.5, -19.5, 0.10), 28.0, 2.5),
    ("research1-brick", (69.5, -87.0, 1.60), (63.0, -79.0, 2.20), 26.0, 2.5),
    ("road-grass", (96.0, -91.5, 1.60), (112.0, -86.5, 0.00), 28.0, 2.5),
]
#: 既存のプレビューのうち比較に使う視点（build_campus.CAM_SPECS の名前 → 出力名）
EXISTING_VIEWS = [("campus_overview", "overview"), ("campus_mall", "mall"),
                  ("campus_library", "library")]


def parse_args(argv):
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser(prog="surfaces_before_after.py")
    ap.add_argument("--fbx", required=True, help="描く campus.fbx")
    ap.add_argument("--mode", required=True, choices=["flat", "textured"])
    ap.add_argument("--out-dir", required=True, help="<視点>-<mode>.png を書く先")
    ap.add_argument("--trees-fbx", default="", help="trees.fbx（無ければ樹木なし）")
    ap.add_argument("--trees-json", default="", help="trees.json（trees.fbx の配置）")
    ap.add_argument("--views", default="", help="描く視点をカンマ区切りで（既定は全部）")
    ap.add_argument("--samples", type=int, default=32, help="EEVEE の TAA サンプル数")
    ap.add_argument("--percent", type=int, default=100, help="解像度 %（1280x720 基準）")
    ap.add_argument("--palette-ref", default="",
                    help="色を読む git の ref（例 origin/main）。空なら作業ツリーの .cs")
    args = ap.parse_args(argv)
    # ref は git show の引数になるので、オプションと取り違える値は受けない
    if args.palette_ref.startswith("-"):
        ap.error("--palette-ref に - で始まる値は渡せません: " + args.palette_ref)
    return args


def read_text(path):
    with open(path, "r", encoding="utf-8") as fp:
        return fp.read()


def read_repo_file(rel, ref):
    """ref があれば `git show <ref>:<rel>`、無ければ作業ツリーのファイルを読む。"""
    if not ref:
        return read_text(os.path.join(ROOT, rel))
    return subprocess.run(["git", "-C", ROOT, "show", "%s:%s" % (ref, rel)],
                          check=True, capture_output=True, text=True).stdout


def dict_block(src, name):
    """C# の `name = new Dictionary...{ ... };` の中身だけを、// の行コメントを除いて切り出す。"""
    start = src.index(name + " = new Dictionary")
    return re.sub(r"//[^\n]*", "", src[start:src.index("};", start)])


def dict_entries(block, pattern, label):
    """block の `{ "名前", ... }` を pattern で全部読む。読めない行が 1 つでもあれば止める
    （黙って読み落とすと、その面が既定の灰色で描かれる）。"""
    found = re.findall(pattern, block)
    expected = len(re.findall(r'\{\s*"', block))
    if len(found) != expected:
        raise ValueError("%s: %d 件のうち %d 件しか読めない" % (label, expected, len(found)))
    return found


def load_palette(ref):
    """名前 → (hex, smoothness or None, metallic or None)。

    CampusColors に載っていれば smoothness は MaterialLibrary.Smoothness の規則で決める
    （None）。載っていなければ InteriorPalette の Surfaces を引く（EnsureCampus と同じ順）。"""
    lib = dict_block(read_repo_file(EDITOR_DIR + "MaterialLibrary.cs", ref), "CampusColors")
    interior = dict_block(read_repo_file(EDITOR_DIR + "InteriorPalette.cs", ref), "Surfaces")
    print("[palette] %s" % (ref or "working tree"))
    palette = {}
    num = r'\s*([0-9.]+)[fF]?\s*'
    surface = (r'\{\s*"([A-Za-z0-9_]+)"\s*,\s*new Surface\(\s*"([0-9A-Fa-f]{6})"\s*,'
               + num + ',' + num + r'\)')
    for name, hx, sm, mt in dict_entries(interior, surface, "InteriorPalette.Surfaces"):
        palette[name] = (hx, float(sm), float(mt))
    color = r'\{\s*"([A-Za-z0-9_]+)"\s*,\s*"([0-9A-Fa-f]{6})"\s*\}'
    for name, hx in dict_entries(lib, color, "MaterialLibrary.CampusColors"):
        palette[name] = (hx, None, None)
    return palette


def load_textures():
    """名前 → (画像のパス, tile_cm)。画像が無いものは載せない。"""
    spec_path = os.path.join(ROOT, "data", "textures", "surfaces.json")
    out = json.loads(read_text(spec_path))["output"]
    folder = os.path.join(ROOT, out["folder"])
    found = {}
    for name, tile_cm in uv.load_surfaces(spec_path).items():
        path = os.path.join(folder, "%s.%s" % (name, out["format"]))
        if os.path.exists(path):
            found[name] = (path, tile_cm)
        else:
            print("[tex] %s: 画像が無い (%s/%s.%s)" % (name, out["folder"], name, out["format"]))
    return found


def normalize(raw):
    """MaterialLibrary.Normalize と同じ（小文字化して最初の '.' から後ろを落とす）。"""
    name = (raw or "").strip().lower()
    if not name:
        return "default"
    dot = name.find(".")
    return name[:dot] if dot > 0 else name


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgba(hx):
    return tuple(srgb_to_linear(int(hx[i:i + 2], 16) / 255.0) for i in (0, 2, 4)) + (1.0,)


def smoothness_rule(name):
    """MaterialLibrary.Smoothness の写し。"""
    if name.startswith("metal") or name.startswith("sign"):
        return 0.62
    if name == "asphalt" or name.startswith("grass") or name == "sand":
        return 0.08
    return 0.22


def surface_params(name, palette):
    """(hex, smoothness, metallic, alpha)。透過の値は MaterialLibrary.EnsureCampus の写し。"""
    hx, sm, mt = palette.get(name, (DEFAULT_HEX, None, None))
    if sm is None:
        sm = smoothness_rule(name)
        mt = 0.8 if name.startswith("metal") else 0.0
    alpha = 1.0
    if name.startswith("glass"):
        alpha, sm = (0.32 if name == "glass_clear" else 0.72), 0.92
    elif name == "water":
        alpha, sm = 0.58, 0.96
    return hx, sm, mt, alpha


def new_material(key, name, palette, texture):
    hx, sm, mt, alpha = surface_params(name, palette)
    mat = bpy.data.materials.new(key)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = hex_rgba(hx)
    bsdf.inputs["Roughness"].default_value = 1.0 - sm
    bsdf.inputs["Metallic"].default_value = mt
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        mat.surface_render_method = "BLENDED"
        mat.use_transparent_shadow = True
    if texture is not None:
        link_texture(nt, bsdf, texture)
    return mat


def link_texture(nt, bsdf, texture):
    """UVMap（メートル）→ Mapping（Scale = 100 / tile_cm）→ 画像 → Base Color。色は掛けない。"""
    path, tile_cm = texture
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = UV_LAYER
    mapping = nt.nodes.new("ShaderNodeMapping")
    s = 100.0 / tile_cm
    mapping.inputs["Scale"].default_value = (s, s, 1.0)
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(path, check_existing=True)
    img.image.colorspace_settings.name = "sRGB"
    img.interpolation = "Linear"
    img.extension = "REPEAT"
    nt.links.new(uvn.outputs["UV"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], img.inputs["Vector"])
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])


def uv_used_by_material(obj):
    """マテリアル番号ごとに、その面に 0 でない UV が 1 つでもあるか。"""
    me = obj.data
    layer = me.uv_layers.get(UV_LAYER)
    if layer is None or not me.polygons:
        return set()
    uvs = np.empty(len(me.loops) * 2, dtype=np.float32)
    layer.data.foreach_get("uv", uvs)
    loop_nonzero = np.abs(uvs.reshape(-1, 2)).max(axis=1) > 1e-6
    starts = np.empty(len(me.polygons), dtype=np.int64)
    totals = np.empty(len(me.polygons), dtype=np.int64)
    mids = np.empty(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_start", starts)
    me.polygons.foreach_get("loop_total", totals)
    me.polygons.foreach_get("material_index", mids)
    poly_nonzero = np.logical_or.reduceat(loop_nonzero, starts) & (totals > 0)
    return set(np.unique(mids[poly_nonzero]).tolist())


def assign_materials(objects, palette, textures, textured):
    """FBX のマテリアルを名前で引き直す。画像は UV のある面にだけ貼る。"""
    cache, used, done = {}, {}, set()
    for obj in objects:
        # 樹木の複製はメッシュを共有しているので、1 つのメッシュは 1 回だけ引き直す
        if obj.type != "MESH" or obj.data.name in done:
            continue
        done.add(obj.data.name)
        with_uv = uv_used_by_material(obj) if textured else set()
        for i, slot in enumerate(obj.material_slots):
            name = normalize(slot.material.name if slot.material else "")
            tex = textures.get(name) if (textured and i in with_uv) else None
            key = name + (".tex" if tex else ".flat")   # normalize で元の名前に戻る
            if key not in cache:
                cache[key] = new_material(key, name, palette, tex)
            slot.material = cache[key]
            if textured and name in textures:
                used.setdefault(name, set()).add((obj.name, bool(tex)))
    for name in sorted(used):
        flat = sorted(o for o, t in used[name] if not t)
        print("[tex] %-15s textured on %d objects%s" % (
            name, sum(1 for _o, t in used[name] if t),
            ("; flat (UV 無し): " + ", ".join(flat)) if flat else ""))


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def place_trees(trees_fbx, trees_json, keep_clear):
    """trees.fbx の tree_mesh_<種> を trees.json の位置に複製する（build_campus と同じ置き方）。
    keep_clear = [(x, y, r)] の円の中の木は置かない（近景のカメラが葉に埋まるのを防ぐ）。"""
    if not (trees_fbx and os.path.exists(trees_fbx) and os.path.exists(trees_json)):
        print("[trees] なし")
        return []
    new = import_fbx(trees_fbx)
    meshes = {o.name[len("tree_mesh_"):]: o for o in new if o.name.startswith("tree_mesh_")}
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes.values():
        o.data.transform(o.matrix_world)   # 取り込みの軸変換を頂点へ焼き、原点に置き直す
        o.matrix_world.identity()
        o.hide_render = True
    placed, skipped = [], 0
    for t in json.loads(read_text(trees_json))["trees"]:
        if any((t["x"] - x) ** 2 + (t["y"] - y) ** 2 < r * r for x, y, r in keep_clear):
            skipped += 1
            continue
        o = bpy.data.objects.new("tree_inst_%04d" % t["i"], meshes[t["species"]].data)
        o.location = (t["x"], t["y"], 0.0)
        o.rotation_euler = (0.0, 0.0, t["rot"])
        o.scale = (t["scale"],) * 3
        bpy.context.scene.collection.objects.link(o)
        placed.append(o)
    print("[trees] placed %d, skipped %d near close-up cameras" % (len(placed), skipped))
    return list(meshes.values()) + placed


def view_specs():
    """(出力名, 視点 uvz, 注視点 uvz, 焦点距離, 樹木を置かない半径)。"""
    by_name = {s[0]: s for s in BC.CAM_SPECS}
    specs = [(out,) + tuple(by_name[src][1:]) for src, out in EXISTING_VIEWS]
    return specs + list(CLOSE_SPECS)


def make_cameras(frame, specs):
    cams = {}
    for name, eye, tgt, lens, _r in specs:
        a = frame.xy(eye[0], eye[1])
        b = frame.xy(tgt[0], tgt[1])
        cams[name] = render.make_camera("cam_" + name, (a[0], a[1], eye[2]),
                                        (b[0], b[1], tgt[2]), lens=lens)
    return cams


def main():
    args = parse_args(sys.argv)
    t0 = time.time()
    # Blender は相対パスを blend ファイル基準に直すので、ここで絶対パスにする
    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # 地上目線では路面を斜めから見るので、異方性フィルタを上げてタイルのにじみを抑える
    bpy.context.preferences.system.anisotropic_filter = "FILTER_16"

    frame = BC.make_frame(BC.load_data(os.path.join(ROOT, "data", "osm", "campus.json")))
    specs = view_specs()
    if args.views:
        wanted = [v.strip() for v in args.views.split(",") if v.strip()]
        specs = [s for s in specs if s[0] in wanted]
    keep_clear = [frame.xy(s[1][0], s[1][1]) + (s[4],) for s in CLOSE_SPECS]

    objects = import_fbx(os.path.abspath(args.fbx))
    objects += place_trees(args.trees_fbx and os.path.abspath(args.trees_fbx),
                           args.trees_json and os.path.abspath(args.trees_json), keep_clear)
    palette = load_palette(args.palette_ref)
    textures = load_textures() if args.mode == "textured" else {}
    assign_materials(objects, palette, textures, args.mode == "textured")

    render.setup_world("BLENDER_EEVEE_NEXT")
    scene = bpy.context.scene
    scene.eevee.taa_render_samples = args.samples
    scene.render.resolution_percentage = args.percent
    cams = make_cameras(frame, specs)
    print("[setup] %.1fs" % (time.time() - t0))
    for name, cam in cams.items():
        t1 = time.time()
        path = os.path.join(out_dir, "%s-%s.png" % (name, args.mode))
        if not render.render_to(cam, path):
            raise RuntimeError("render failed: %s" % path)
        print("[render] %-16s %s %.1fs" % (name, args.mode, time.time() - t1))
    print("[done] %s %.1fs" % (args.mode, time.time() - t0))


main()

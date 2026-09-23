"""Web ビューア用の GLB を書き出す（Blender 4.5 LTS / headless）。

Unity 用に生成済みの FBX（`unity/KatsushikaCampusDays/Assets/Models/**`）を読み直し、
three.js で表示できる GLB へ変換して `build/viewer/models/` に置く。

    "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
        --python blender/export_web_glb.py -- --out build/viewer/models

出力:

| ファイル | 内容 |
|----------|------|
| `campus.glb`        | `campus.fbx` + `trees.fbx` の樹種メッシュを `trees.json` の 595 本ぶん配置 |
| `interior_<id>.glb` | `Interiors/<id>.fbx`（9 棟） |
| `chara_<id>.glb`    | `Characters/<id>/<id>.fbx`（7 体。アーマチュアと Action を含む） |
| `index.json`        | 表示名・種別・三角形数・バイト数・アニメ名の一覧 |

座標系は FBX と同じ（x=東, y=北, z=上）。glTF は Y-up なので `export_yup=True` の
既定変換に任せる（three.js 側でそのまま Y-up として扱える）。
"""

import argparse
import json
import os
import sys
import time

import bpy
from mathutils import Matrix

# --------------------------------------------------------------------------- #
#  定数
# --------------------------------------------------------------------------- #
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MODELS = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Models")

INTERIOR_IDS = ["research1", "research2", "lecture", "kyoso", "library",
                "gym", "lab1", "lab2", "greenhouse"]

INTERIOR_NAMES = {
    "research1": "第1研究棟",
    "research2": "第2研究棟（大食堂）",
    "lecture": "講義棟（大ホール）",
    "kyoso": "共創棟（カフェ）",
    "library": "図書館",
    "gym": "体育館",
    "lab1": "実験棟1",
    "lab2": "実験棟2",
    "greenhouse": "温室",
}

CHARA_IDS = ["mirai", "botchan", "madonna", "inari", "kaname", "sora", "prof"]

CHARA_NAMES = {
    "mirai": "新宿 みらい",
    "botchan": "坊っちゃん",
    "madonna": "マドンナちゃん",
    "inari": "花之木 いなり",
    "kaname": "中川 かなめ",
    "sora": "金町 そら",
    "prof": "教授",
}

# アニメ名（Action 名）の日本語ラベル。ビューアのドロップダウンに出す。
ANIM_LABELS = {
    "Idle": "待機",
    "Walk": "歩行",
    "Run": "走行",
    "Jump": "ジャンプ",
    "Talk": "会話",
    "Wave": "手を振る",
}

# glTF 書き出しの共通オプション。
GLTF_OPTS = dict(
    export_format="GLB",
    use_selection=False,
    export_apply=False,          # FBX 由来でモディファイアは無い。Shape Key を壊さない
    export_yup=True,
    export_materials="EXPORT",
    export_image_format="AUTO",
    export_texture_dir="",
    export_cameras=False,
    export_lights=False,
    export_extras=False,
    export_attributes=False,
    export_normals=True,
    export_tangents=False,
    export_skins=True,
    export_morph=True,
    export_all_influences=False,
)

DRACO_OPTS = dict(
    export_draco_mesh_compression_enable=True,
    export_draco_mesh_compression_level=6,
    export_draco_position_quantization=14,
    export_draco_normal_quantization=10,
    export_draco_texcoord_quantization=12,
    export_draco_color_quantization=10,
    export_draco_generic_quantization=12,
)


# --------------------------------------------------------------------------- #
#  ヘルパ
# --------------------------------------------------------------------------- #
def log(msg):
    print("[glb] %s" % msg)
    sys.stdout.flush()


def reset_scene():
    """まっさらなシーンにする。"""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    if not os.path.isfile(path):
        raise SystemExit("FBX が見つからない: %s" % path)
    bpy.ops.import_scene.fbx(filepath=path)


def drop_empties():
    """glTF に出しても意味の無い Empty（entrance/spawn/npc/poi/sign）を消す。"""
    for obj in [o for o in bpy.data.objects if o.type == "EMPTY"]:
        bpy.data.objects.remove(obj, do_unlink=True)


def fix_materials():
    """three.js で破綻しないようにマテリアルを整える。

    - トゥーン輪郭線は反転シェルなので、裏面カリングを有効にして単面として書き出す。
      （両面のままだと黒い塊がキャラクターを覆う）
    - アルファを使っていないマテリアルは不透明にして、透過ソートを避ける。
    """
    for mat in bpy.data.materials:
        if mat.name.startswith("outline"):
            mat.use_backface_culling = True
        if not mat.use_nodes:
            continue
        bsdf = next((n for n in mat.node_tree.nodes
                     if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        alpha = bsdf.inputs.get("Alpha")
        opaque = alpha is not None and not alpha.is_linked and alpha.default_value >= 0.999
        if opaque and hasattr(mat, "surface_render_method"):
            mat.surface_render_method = "DITHERED"


def count_tris():
    """シーン内のメッシュの三角形数（インスタンス込み）。"""
    total = 0
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    return total


def glb_animations(path):
    """書き出した GLB の JSON チャンクを読んで、実際のアニメーション名を取り出す。"""
    import struct
    with open(path, "rb") as fh:
        data = fh.read()
    length = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + length].decode("utf-8"))
    names = [a.get("name", "") for a in doc.get("animations", [])]
    order = list(ANIM_LABELS)
    return sorted(names, key=lambda n: (order.index(n) if n in order else 99, n))


def export_glb(path, draco):
    opts = dict(GLTF_OPTS)
    opts.update(DRACO_OPTS if draco else
                dict(export_draco_mesh_compression_enable=False))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, **opts)
    return os.path.getsize(path)


# --------------------------------------------------------------------------- #
#  キャンパス全景
# --------------------------------------------------------------------------- #
def bake_transform(obj):
    """オブジェクトの行列をメッシュデータへ焼き、行列を単位行列に戻す。"""
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix()


def build_campus(out_dir, draco):
    reset_scene()
    import_fbx(os.path.join(MODELS, "Campus", "campus.fbx"))
    drop_empties()
    base_tris = count_tris()

    # 樹種メッシュだけを trees.fbx から取り込み、trees.json のぶんだけ配置する。
    import_fbx(os.path.join(MODELS, "Campus", "trees.fbx"))
    drop_empties()
    species = {}
    for obj in [o for o in bpy.data.objects if o.name.startswith("tree_mesh_")]:
        bake_transform(obj)
        species[obj.name[len("tree_mesh_"):]] = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
    log("樹種メッシュ: %s" % ", ".join(sorted(species)))

    with open(os.path.join(MODELS, "Campus", "trees.json"), encoding="utf-8") as fh:
        trees = json.load(fh)["trees"]

    coll = bpy.context.scene.collection
    tree_tris = 0
    placed = 0
    for spec in trees:
        mesh = species.get(spec["species"])
        if mesh is None:
            continue
        # メッシュデータを共有させるので glTF 側では 3 メッシュ + 595 ノードになる。
        inst = bpy.data.objects.new("tree_%04d" % spec["i"], mesh)
        inst.location = (spec["x"], spec["y"], 0.0)
        inst.rotation_euler = (0.0, 0.0, spec["rot"])
        scale = spec["scale"]
        inst.scale = (scale, scale, scale)
        coll.objects.link(inst)
        placed += 1
        mesh.calc_loop_triangles()
        tree_tris += len(mesh.loop_triangles)
    log("樹木 %d 本を配置（建物 %d tris + 樹木 %d tris）" % (placed, base_tris, tree_tris))

    fix_materials()
    path = os.path.join(out_dir, "campus.glb")
    size = export_glb(path, draco)
    return dict(id="campus", kind="campus", name="キャンパス全景",
                note="建物 12 棟 + 外構 + 樹木 %d 本" % placed,
                file="campus.glb", tris=base_tris + tree_tris,
                bytes=size, animations=[])


# --------------------------------------------------------------------------- #
#  建物内部
# --------------------------------------------------------------------------- #
def build_interior(iid, out_dir, draco):
    reset_scene()
    import_fbx(os.path.join(MODELS, "Interiors", "%s.fbx" % iid))
    drop_empties()
    fix_materials()
    tris = count_tris()
    path = os.path.join(out_dir, "interior_%s.glb" % iid)
    size = export_glb(path, draco)
    return dict(id="interior_%s" % iid, kind="interior",
                name=INTERIOR_NAMES.get(iid, iid), note="建物内部",
                file="interior_%s.glb" % iid, tris=tris, bytes=size,
                animations=[])


# --------------------------------------------------------------------------- #
#  キャラクター
# --------------------------------------------------------------------------- #
def tidy_actions():
    """FBX 由来の Action 名（`Armature|Armature|Idle`）を `Idle` に直す。

    Shape Key 用の `Key|...` は glTF では armature 側と同名になって衝突するので捨てる
    （表情は静止の既定形で書き出される）。
    """
    for act in list(bpy.data.actions):
        if act.name.startswith("Key|"):
            bpy.data.actions.remove(act)
    for act in bpy.data.actions:
        act.name = act.name.split("|")[-1]
        act.use_fake_user = True


def build_character(cid, out_dir, draco):
    reset_scene()
    import_fbx(os.path.join(MODELS, "Characters", cid, "%s.fbx" % cid))
    tidy_actions()
    fix_materials()
    tris = count_tris()
    path = os.path.join(out_dir, "chara_%s.glb" % cid)
    size = export_glb(path, draco)
    anims = glb_animations(path)
    return dict(id="chara_%s" % cid, kind="character",
                name=CHARA_NAMES.get(cid, cid), note="リグ + アニメ %d 本" % len(anims),
                file="chara_%s.glb" % cid, tris=tris, bytes=size,
                animations=anims)


# --------------------------------------------------------------------------- #
#  プレビュー一覧
# --------------------------------------------------------------------------- #
def collect_previews():
    """`docs/previews/*.png` をギャラリー用のメタデータにする。"""
    src = os.path.join(ROOT, "docs", "previews")
    if not os.path.isdir(src):
        return []
    view_ja = {"front": "正面", "side": "側面", "back": "背面", "face": "顔",
               "idle": "待機", "walk": "歩行", "turn": "回転"}
    out = []
    for name in sorted(os.listdir(src)):
        if not name.lower().endswith(".png"):
            continue
        stem = name[:-4]
        if stem.startswith("campus_"):
            out.append(dict(file=name, group="キャンパス全景",
                            label="キャンパス / %s" % stem[len("campus_"):]))
        elif stem.startswith("interior_"):
            key = stem[len("interior_"):]
            base = key.split("_")[0]
            out.append(dict(file=name, group="建物内部",
                            label=INTERIOR_NAMES.get(base, base)
                                  + ("" if key == base else " / %s" % key[len(base) + 1:])))
        else:
            cid = stem.split("_")[0]
            if cid not in CHARA_NAMES:
                continue
            view = stem[len(cid) + 1:]
            out.append(dict(file=name, group="キャラクター",
                            label="%s / %s" % (CHARA_NAMES[cid],
                                               view_ja.get(view, view))))
    return out


# --------------------------------------------------------------------------- #
#  main
# --------------------------------------------------------------------------- #
def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="export_web_glb.py")
    ap.add_argument("--out", default=os.path.join("build", "viewer", "models"),
                    help="GLB の出力先（相対ならリポジトリルート基準）")
    ap.add_argument("--no-draco", action="store_true",
                    help="Draco 圧縮を使わない（デバッグ用）")
    ap.add_argument("--only", default="all",
                    help="campus / interiors / characters をカンマ区切りで")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    out_dir = args.out if os.path.isabs(args.out) else os.path.join(ROOT, args.out)
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)
    draco = not args.no_draco
    only = {s.strip() for s in args.only.split(",")} if args.only != "all" \
        else {"campus", "interiors", "characters"}

    t0 = time.time()
    entries = []
    if "campus" in only:
        entries.append(build_campus(out_dir, draco))
        log("campus.glb  %.2f MB  %d tris" % (entries[-1]["bytes"] / 1e6, entries[-1]["tris"]))
    if "interiors" in only:
        for iid in INTERIOR_IDS:
            entries.append(build_interior(iid, out_dir, draco))
            log("interior_%s.glb  %.2f MB  %d tris"
                % (iid, entries[-1]["bytes"] / 1e6, entries[-1]["tris"]))
    if "characters" in only:
        for cid in CHARA_IDS:
            entries.append(build_character(cid, out_dir, draco))
            log("chara_%s.glb  %.2f MB  %d tris  anims=%s"
                % (cid, entries[-1]["bytes"] / 1e6, entries[-1]["tris"],
                   ",".join(entries[-1]["animations"])))

    index = dict(
        generated=time.strftime("%Y-%m-%dT%H:%M:%S"),
        draco=draco,
        anim_labels=ANIM_LABELS,
        models=entries,
        previews=collect_previews(),
    )
    index_path = os.path.join(out_dir, "index.json")
    # 既存の index.json に別カテゴリの結果が残っていれば引き継ぐ（--only 用）。
    if len(only) < 3 and os.path.isfile(index_path):
        with open(index_path, encoding="utf-8") as fh:
            old = json.load(fh)
        keep = [m for m in old.get("models", [])
                if m["file"] not in {e["file"] for e in entries}]
        index["models"] = entries + keep
    with open(index_path, "w", encoding="utf-8") as fh:
        json.dump(index, fh, ensure_ascii=False, indent=1)

    total = sum(e["bytes"] for e in index["models"])
    log("合計 %.2f MB / %d ファイル / プレビュー %d 枚 / %.1f s"
        % (total / 1e6, len(index["models"]), len(index["previews"]), time.time() - t0))


if __name__ == "__main__":
    main()

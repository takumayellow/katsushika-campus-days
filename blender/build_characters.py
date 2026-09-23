"""アニメ調キャラクターを headless で生成し、FBX・顔テクスチャ・プレビューを出力する。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/build_characters.py -- \
      --ids mirai,botchan,madonna,inari,kaname,sora,prof \
      --out-dir unity/KatsushikaCampusDays/Assets/Models/Characters \
      --preview

DESIGN.md §2 / §2.1 / §2.2 / §3.3 が契約。ボーン名・Shape Key 名・Action 名・
マテリアル名・FBX オプションはそこに合わせてある。

出力:
  <out-dir>/<id>/<id>.fbx     リグ + Shape Key + Action 6 本
  <out-dir>/<id>/face.png     2048x2048 の顔アトラス
  docs/previews/<id>_{front,side,back,turn,walk,idle,face}.png
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Matrix  # noqa: E402
import numpy as np  # noqa: E402

from kcd_chara import (anim, body, cloth, hair, outline, params, render, rig,  # noqa: E402
                        shapes)
from kcd_chara import mats as kmats  # noqa: E402
from kcd_chara import tex  # noqa: E402
from kcd_chara.mesh import MeshBuilder  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

#: DESIGN.md §3.3。キャラは bake_space_transform を使わない（アーマチュアが歪むため）。
FBX_OPTS = dict(
    use_selection=False,
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=False,
    object_types={"ARMATURE", "MESH"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    primary_bone_axis="Y",
    secondary_bone_axis="X",
    use_armature_deform_only=False,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    path_mode="COPY",
    embed_textures=False,
)


# --------------------------------------------------------------------------- #
#  ユーティリティ
# --------------------------------------------------------------------------- #
def parse_args(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    ap = argparse.ArgumentParser(prog="build_characters.py")
    ap.add_argument("--ids", default=",".join(params.ALL_IDS),
                    help="生成するキャラ id をカンマ区切りで")
    ap.add_argument("--out-dir",
                    default=os.path.join(ROOT, "unity", "KatsushikaCampusDays",
                                         "Assets", "Models", "Characters"))
    ap.add_argument("--preview", action="store_true", help="プレビュー PNG を描画する")
    ap.add_argument("--preview-dir", default=os.path.join(ROOT, "docs", "previews"))
    ap.add_argument("--face-size", type=int, default=2048)
    ap.add_argument("--no-outline", action="store_true",
                    help="輪郭線シェルを作らない")
    ap.add_argument("--no-verify", action="store_true",
                    help="FBX の再インポート検証を省く")
    return ap.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.frame_start = 1
    scene.frame_end = 120
    scene.frame_set(1)


def probe_shape_keys_survive_modifiers(tmp_dir: str) -> bool:
    """use_mesh_modifiers=True で Shape Key が FBX に残るかを実測で判定する。

    Blender のバージョンによって「モディファイア適用 = Shape Key 消失」になる。
    契約（§3.3）は use_mesh_modifiers=True だが、Shape Key（§2.1）は必須なので、
    両立しない場合だけ False に落とす。その判断を推測でなく実測で行う。
    """
    os.makedirs(tmp_dir, exist_ok=True)
    path = os.path.join(tmp_dir, "_probe.fbx")
    reset_scene()
    me = bpy.data.meshes.new("probe")
    me.from_pydata([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], [],
                   [(0, 1, 2, 3)])
    me.update()
    ob = bpy.data.objects.new("probe", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.shape_key_add(name="Basis", from_mix=False)
    k = ob.shape_key_add(name="blink", from_mix=False)
    k.data[0].co = (0.0, 0.0, 0.25)

    opts = dict(FBX_OPTS)
    opts["use_mesh_modifiers"] = True
    opts["bake_anim"] = False
    opts["object_types"] = {"MESH"}
    try:
        bpy.ops.export_scene.fbx(filepath=path, **opts)
        reset_scene()
        bpy.ops.import_scene.fbx(filepath=path)
        found = any(o.type == "MESH" and o.data.shape_keys is not None
                    and "blink" in o.data.shape_keys.key_blocks
                    for o in bpy.context.scene.objects)
    except Exception as exc:  # noqa: BLE001
        print(f"[probe] 判定に失敗（安全側で modifiers=False）: {exc}")
        found = False
    finally:
        if os.path.exists(path):
            try:
                os.remove(path)
            except OSError:
                pass
    print(f"[probe] use_mesh_modifiers=True で Shape Key が残る: {found}")
    return bool(found)


# --------------------------------------------------------------------------- #
#  1 体を組む
# --------------------------------------------------------------------------- #
def build_textures(p: dict, cdir: str, face_size: int):
    cid = p["id"]
    face_png = os.path.join(cdir, "face.png")
    face_arr = tex.draw_face_atlas(face_size, p)
    face_img = tex.array_to_image(f"{cid}_face", face_arr, filepath=face_png)

    patterns: dict = {}
    pattern_arrays: dict = {}
    outfit = p["outfit"]
    if outfit == "kimono_botchan":
        arr = pattern_arrays["kasuri"] = tex.draw_kasuri(256)
        patterns["kasuri"] = tex.array_to_image(
            f"{cid}_kasuri", arr, filepath=os.path.join(cdir, "kasuri.png"))
    elif outfit == "kimono_madonna":
        arr = pattern_arrays["heart"] = tex.draw_hearts(256)
        patterns["heart"] = tex.array_to_image(
            f"{cid}_heart", arr, filepath=os.path.join(cdir, "heart.png"))
    return face_img, patterns, face_png, pattern_arrays


def write_palette(p: dict, cdir: str, names, pattern_arrays: dict) -> str:
    """<cdir>/palette.json に Unity 用の色表を書く（#55, MaterialLibrary が読む）。"""
    pal = kmats.palette(p, names, pattern_arrays)
    path = os.path.join(cdir, "palette.json")
    pats = kmats.pattern_entries(p, names, pattern_arrays)
    doc = {"materials": [{"name": n, "hex": h, **pats.get(n, {})}
                         for n, h in sorted(pal.items())]}
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return path


def lift_to_ground(obj, arm) -> float:
    """メッシュの最下点が z=0 に来るよう, 頂点・全 Shape Key・ボーンの静止位置を同じだけ動かす。

    Unity の Humanoid は原点を地面として足を置く。高下駄のように素足の底より
    下へ伸びる履物は, そのままだと原点より下に出てゲームで地面にめり込む
    （坊っちゃんは 10.45 cm, #47）。Blender のプレビューは床をメッシュの最下点に
    敷くので, この差はプレビューでは見えない。静止位置ごと動かすので
    Armature の変形は恒等のままで, アクション（ボーン局所の値）も変わらない。
    戻り値は持ち上げた量 [m]。
    """
    n = len(obj.data.vertices)
    co = np.empty(n * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)
    dz = -float(co.reshape(-1, 3)[:, 2].min())
    if abs(dz) < 1e-6:
        return 0.0
    shift = np.tile([0.0, 0.0, dz], n)
    obj.data.vertices.foreach_set("co", co + shift)
    if obj.data.shape_keys is not None:
        for kb in obj.data.shape_keys.key_blocks:
            kco = np.empty(n * 3, dtype=np.float64)
            kb.data.foreach_get("co", kco)
            kb.data.foreach_set("co", kco + shift)
    obj.data.update()
    # EditBone の head/tail を 1 本ずつ動かすと, 親の tail に繋がった子の head が
    # 親と自分とで 2 回動いて 2 倍上がる。Armature.transform は全ボーンを一度に動かす。
    arm.data.transform(Matrix.Translation((0.0, 0.0, dz)))
    return dz


def build_character(cid: str, out_root: str, face_size: int, fbx_opts: dict,
                    *, with_outline: bool = True) -> dict:
    t0 = time.time()
    p = params.resolve(cid)
    cdir = os.path.join(out_root, cid)
    os.makedirs(cdir, exist_ok=True)

    reset_scene()
    face_img, patterns, face_png, pattern_arrays = build_textures(p, cdir, face_size)
    materials = kmats.build_materials(p, face_img, patterns)

    mb = MeshBuilder()
    a, head, fs, uv_box, face_uvs = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    nverts, ntris = mb.stats()

    obj = mb.to_object(cid, materials)
    body.assign_uvs(obj, uv_box, face_uvs)

    arm, pos = rig.build_armature(p, a, cid)
    method = rig.bind(obj, arm, pos)
    rig.override_weights(obj, mb, p, a)

    keys = shapes.build_shape_keys(obj, mb, p)
    face_actions = anim.setup_shape_drivers(obj, arm)
    actions = anim.build_actions(arm)
    lift = lift_to_ground(obj, arm)

    out_obj = (outline.build_outline(obj, mb, p, materials)
               if with_outline else None)
    out_tris = outline.outline_tris(out_obj)

    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    z_lo, z_hi = float(co[:, 2].min()), float(co[:, 2].max())

    fbx = os.path.join(cdir, f"{cid}.fbx")
    bpy.ops.export_scene.fbx(filepath=fbx, **fbx_opts)
    size = os.path.getsize(fbx)

    mats_used = sorted({m for m in mb.face_mat})
    write_palette(p, cdir, mats_used, pattern_arrays)
    bones = [b.name for b in arm.data.bones]

    info = dict(id=cid, jp=p["jp"], fbx=fbx, face=face_png, size=size,
                verts=nverts, tris=ntris, bones=bones, actions=actions,
                keys=keys, weight_method=method, z_lo=z_lo, z_hi=z_hi, lift=lift,
                height=p["height"], heads=p["heads"], materials=mats_used,
                seconds=time.time() - t0, obj=obj, arm=arm, params=p,
                face_actions=face_actions, outline_tris=out_tris,
                outline=out_obj, total_tris=ntris + out_tris)
    return info


# --------------------------------------------------------------------------- #
#  検証
# --------------------------------------------------------------------------- #
def contract_bone_diff(bones: list[str]) -> tuple[list[str], list[str]]:
    want = set(rig.BONE_NAMES)
    have = set(bones)
    return sorted(want - have), sorted(have - want)


def take_name(raw: str) -> str:
    """FBX の AnimStack 名（Armature|Idle 等）から素の Action 名を取り出す。"""
    return raw.rsplit("|", 1)[-1]


def verify_fbx(cid: str, path: str) -> dict:
    """FBX を読み直して Action・ボーン・Shape Key・身長を実測する。"""
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    takes = sorted({take_name(a.name) for a in bpy.data.actions})
    raw_acts = sorted({a.name for a in bpy.data.actions})
    bones = sorted({b.name for o in arms for b in o.data.bones})
    keys: list[str] = []
    tris = 0
    zs: list[float] = []
    for m in meshes:
        me = m.data
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        if me.shape_keys is not None:
            keys = [k.name for k in me.shape_keys.key_blocks]
        co = np.empty(len(me.vertices) * 3, dtype=np.float64)
        me.vertices.foreach_get("co", co)
        w = np.array(m.matrix_world.to_4x4())
        pts = co.reshape(-1, 3) @ w[:3, :3].T + w[:3, 3]
        zs.extend([float(pts[:, 2].min()), float(pts[:, 2].max())])
    # 表情カーブが armature の take と同じ AnimStack に入っているかを実測する
    face: dict[str, list[str]] = {}
    for a in bpy.data.actions:
        if not a.name.startswith("Key|"):
            continue
        fcs = list(getattr(a, "fcurves", []) or [])
        if not fcs:
            for lay in getattr(a, "layers", []):
                for st in lay.strips:
                    for cb in st.channelbags:
                        fcs += list(cb.fcurves)
        hit = []
        for fc in fcs:
            vals = [kp.co[1] for kp in fc.keyframe_points]
            if vals and max(vals) > 0.5:
                dp = fc.data_path
                hit.append(dp.split('"')[1] if '"' in dp else dp)
        if hit:
            face[take_name(a.name)] = sorted(set(hit))
    return dict(id=cid, meshes=len(meshes), armatures=len(arms), tris=tris,
                actions=takes, raw_actions=raw_acts, bones=bones, keys=keys,
                face_curves=face,
                span=(min(zs) if zs else 0.0, max(zs) if zs else 0.0))


# --------------------------------------------------------------------------- #
#  main
# --------------------------------------------------------------------------- #
def main():
    t_start = time.time()
    args = parse_args(sys.argv)
    ids = [s.strip() for s in args.ids.split(",") if s.strip()]
    for cid in ids:
        if cid not in params.CHARACTERS:
            raise SystemExit(f"未知の id: {cid}（有効: {', '.join(params.ALL_IDS)}）")
    # Blender の Image.save() は相対パスをドライブの直下から解決するので,
    # 相対のままだと face.png やプレビューが C:\unity\... や C:\docs\... に落ちる
    args.out_dir = os.path.abspath(args.out_dir)
    args.preview_dir = os.path.abspath(args.preview_dir)
    os.makedirs(args.out_dir, exist_ok=True)

    fbx_opts = dict(FBX_OPTS)
    probe_dir = os.path.join(tempfile.gettempdir(), "kcd_chara_probe")
    if not probe_shape_keys_survive_modifiers(probe_dir):
        fbx_opts["use_mesh_modifiers"] = False
        print("[probe] Shape Key を優先して use_mesh_modifiers=False で書き出す "
              "（アーマチュア以外のモディファイアは使っていないので形状は同一）")

    built: list[dict] = []
    engines: dict[str, str] = {}
    for cid in ids:
        info = build_character(cid, args.out_dir, args.face_size, fbx_opts,
                               with_outline=not args.no_outline)
        built.append(info)
        print("-" * 72)
        print("[%s] %s  %.2fs" % (cid, info["jp"], info["seconds"]))
        print("   verts=%d  tris=%d(+輪郭%d=%d)  fbx=%.2f MB  weights=%s"
              % (info["verts"], info["tris"], info["outline_tris"],
                 info["total_tris"], info["size"] / 1048576.0,
                 info["weight_method"]))
        print("   height=%.3fm (mesh z %.3f..%.3f, 接地のため %+.4fm)  heads=%.1f"
              % (info["height"], info["z_lo"], info["z_hi"], info["lift"],
                 info["heads"]))
        print("   actions=%s" % ", ".join(info["actions"]))
        print("   shapekeys=%s" % ", ".join(info["keys"]))
        print("   表情ドライバ=%s" % (", ".join(info["face_actions"]) or "なし"))
        miss, extra = contract_bone_diff(info["bones"])
        print("   bones=%d  contract_missing=%s  extra=%s"
              % (len(info["bones"]), miss or "なし", extra or "なし"))
        print("   materials=%s" % ", ".join(info["materials"]))

        if args.preview:
            walk = bpy.data.actions.get("Walk")
            idle = bpy.data.actions.get("Idle")
            made = render.render_previews(info["params"], args.preview_dir,
                                          arm=info["arm"], walk_action=walk,
                                          obj=info["obj"], idle_action=idle)
            engines[cid] = made.get("engine", "?")
            print("   preview=%s" % ", ".join(
                v for k, v in made.items() if k != "engine"))

    # --- 再インポート検証 -------------------------------------------------
    checks: list[dict] = []
    if not args.no_verify:
        print("=" * 72)
        print("FBX 再インポート検証")
        for info in built:
            c = verify_fbx(info["id"], info["fbx"])
            checks.append(c)
            miss, extra = contract_bone_diff(c["bones"])
            ok_act = set(anim.ACTION_NAMES) <= set(c["actions"])
            ok_key = set(shapes.KEY_NAMES) <= set(c["keys"])
            print("[%s] mesh=%d arm=%d tris=%d span=%.3f..%.3f"
                  % (c["id"], c["meshes"], c["armatures"], c["tris"],
                     c["span"][0], c["span"][1]))
            print("    takes(%d)=%s  6本そろい=%s"
                  % (len(c["actions"]), ", ".join(c["actions"]), ok_act))
            print("    animstacks=%s" % ", ".join(c["raw_actions"]))
            print("    shapekeys=%s  7種そろい=%s" % (", ".join(c["keys"]), ok_key))
            print("    bones=%d  contract_missing=%s  extra=%s"
                  % (len(c["bones"]), miss or "なし", extra or "なし"))
            print("    表情カーブ=%s"
                  % (", ".join(f"{k}:{'+'.join(v)}"
                               for k, v in sorted(c["face_curves"].items()))
                     or "なし"))

    # --- まとめ -----------------------------------------------------------
    print("=" * 72)
    print("%-9s %-8s %7s %7s %6s %5s %6s %s"
          % ("id", "身長m", "tris", "KB", "Act", "Key", "Bone", "FBX"))
    by_id = {c["id"]: c for c in checks}
    for info in built:
        c = by_id.get(info["id"])
        span = (c["span"][1] - c["span"][0]) if c else (info["z_hi"] - info["z_lo"])
        print("%-9s %-8.3f %7d %7d %6d %5d %6d %s"
              % (info["id"], span, c["tris"] if c else info["tris"],
                 info["size"] // 1024,
                 len(c["actions"]) if c else len(info["actions"]),
                 len(c["keys"]) - 1 if c else len(info["keys"]),
                 len(c["bones"]) if c else len(info["bones"]),
                 os.path.relpath(info["fbx"], ROOT).replace("\\", "/")))
    if engines:
        print("render engine: %s" % ", ".join(f"{k}={v}" for k, v in engines.items()))
    print("total: %.1fs" % (time.time() - t_start))
    print("=" * 72)
    _ = math


if __name__ == "__main__":
    main()

"""東京理科大学 葛飾キャンパス — 建物内部を headless で生成して FBX に書き出す。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/build_interiors.py -- --ids all --preview

座標（DESIGN.md §3.4）:
  原点  = entrance_<id> の真下の床レベル
  +Y    = 入口から建物の中へ向かう向き（FBX 変換後、Unity の +Z）
  +X    = 入口を背にして右、+Z = 上
FBX は build_campus.py と同一オプション（axis_forward='-Z' / axis_up='Y' /
bake_space_transform=True）なので、Unity 側では (x, y=Blender z, z=Blender y)。

出力:
  unity/KatsushikaCampusDays/Assets/Models/Interiors/<id>.fbx
  unity/KatsushikaCampusDays/Assets/Models/Interiors/<id>.json  （配置メタ）
  docs/previews/interior_<id>.png
"""

import argparse
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from kcd_lib import mats, render  # noqa: E402
from kcd_interior import imats, registry, spec as ispec  # noqa: E402
from kcd_interior.ctx import Ctx  # noqa: E402

FBX_OPTS = dict(
    use_selection=False,
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    object_types={"EMPTY", "MESH"},
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
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
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    p = argparse.ArgumentParser(prog="build_interiors.py")
    p.add_argument("--json",
                   default=os.path.join(root, "data", "osm", "campus.json"))
    p.add_argument("--out-dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Models", "Interiors"))
    p.add_argument("--data-dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Data"),
                   help="クエスト/収集物 JSON の場所（必須 POI の契約照合に使う）")
    p.add_argument("--ids", default="all",
                   help="カンマ区切りの建物 ID、または all")
    p.add_argument("--preview", action="store_true")
    p.add_argument("--preview-dir",
                   default=os.path.join(root, "docs", "previews"))
    p.add_argument("--engine", default="auto",
                   choices=["auto", "eevee", "workbench"])
    p.add_argument("--seed", type=int, default=20250921)
    p.add_argument("--no-export", action="store_true",
                   help="FBX を書き出さない（形だけ確認したいとき）")
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


def engine_name(choice):
    if choice == "workbench":
        return "BLENDER_WORKBENCH"
    if choice == "eevee":
        return "BLENDER_EEVEE_NEXT"
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            bpy.context.scene.render.engine = name
            return name
        except TypeError:
            # そのバージョンに無い enum 値。次の候補を試す。
            continue
    return "BLENDER_WORKBENCH"


def add_empty(name, loc, size=0.6):
    o = bpy.data.objects.new(name, None)
    o.empty_display_type = "PLAIN_AXES"
    o.empty_display_size = size
    o.location = (loc[0], loc[1], loc[2])
    bpy.context.scene.collection.objects.link(o)
    return o


def add_point_light(name, loc, energy, radius):
    data = bpy.data.lights.new(name, type="POINT")
    data.energy = energy
    data.shadow_soft_size = radius
    data.color = (1.0, 0.97, 0.92)
    o = bpy.data.objects.new(name, data)
    o.location = loc
    bpy.context.scene.collection.objects.link(o)
    return o


def mesh_tris():
    n = 0
    for o in bpy.context.scene.objects:
        if o.type == "MESH":
            for poly in o.data.polygons:
                n += max(0, len(poly.vertices) - 2)
    return n


def export_fbx(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    return os.path.getsize(path)


# --------------------------------------------------------------------------- #
#  1 棟ぶん
# --------------------------------------------------------------------------- #
def build_one(sp, plan, args, eng):
    reset_scene()
    mats.build_all()      # 外装パレット（concrete_* / glass_clear など）
    imats.build_all()     # インテリア専用パレット

    c = Ctx(sp, seed=args.seed)
    plan.build(c)

    objects = []
    for mb in c.builders():
        if not mb.faces:
            continue
        objects.append(mb.to_object())

    for name, loc in c.empties:
        add_empty(name, loc)

    info = {
        "tris": mesh_tris(),
        "objects": [o.name for o in objects],
        "empties": [n for n, _ in c.empties],
        "seats": c.seats,
        "notes": c.notes,
    }

    # --- 配置メタ（Unity 用） ---
    meta_extra = {
        "spawn": next(({"x": round(p[0], 3), "y": round(p[2], 3),
                        "z": round(p[1], 3)}
                       for n, p in c.empties if n.startswith("spawn_")), None),
        "empties": {n: {"x": round(p[0], 3), "y": round(p[2], 3),
                        "z": round(p[1], 3)} for n, p in c.empties},
        "triangles": info["tris"],
        "seats": c.seats,
        "notes": c.notes,
        "objects": info["objects"],
    }
    sidecar = os.path.join(args.out_dir, "%s.json" % sp.id)
    ispec.dump_sidecar(sp, meta_extra, sidecar)

    # --- FBX ---
    if args.no_export:
        info["fbx"] = None
        info["size"] = 0
    else:
        path = os.path.join(args.out_dir, "%s.fbx" % sp.id)
        info["size"] = export_fbx(path)
        info["fbx"] = path

    # --- プレビュー ---
    info["previews"] = []
    if args.preview and c.cams:
        render.setup_world(eng, sky=(0.55, 0.68, 0.86))
        sun = bpy.data.objects.get("KCD_Sun")
        if sun is not None:
            sun.data.energy = 1.4
        for i, (x, y, z, energy, radius) in enumerate(c.lights):
            add_point_light("KCD_IL_%03d" % i, (x, y, z), energy, radius)
        for idx, (suffix, loc, target, lens) in enumerate(c.cams):
            if suffix.startswith("="):
                stem = suffix[1:]
            elif suffix:
                stem = "interior_%s_%s" % (sp.id, suffix)
            else:
                stem = "interior_%s" % sp.id
            png = os.path.join(args.preview_dir, "%s.png" % stem)
            cam = render.make_camera("KCD_ICam_%d" % idx, loc, target, lens)
            if render.render_to(cam, png):
                info["previews"].append(png)

    return info


POI_RE = re.compile(r"^poi_([a-z0-9]+)_[a-z0-9_]+$")


def required_pois(data_dir):
    """クエスト・収集物データが参照する poi_<棟>_<名前> を棟ごとに集める。

    Data 側は文字列値として POI 名を持つ（quests の position など）ので、
    JSON を再帰的に歩いて poi_ で始まる文字列を全部拾う。棟 ID は 2 つ目の
    アンダースコアまで（poi_lab1_lab_b → lab1）。
    """
    out = {}
    if not os.path.isdir(data_dir):
        return out
    for sub in ("Quests", "Collectibles"):
        d = os.path.join(data_dir, sub)
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".json"):
                continue
            with open(os.path.join(d, fn), encoding="utf-8") as fp:
                node = json.load(fp)
            for name in _walk_strings(node):
                m = POI_RE.match(name)
                if m:
                    out.setdefault(m.group(1), set()).add(name)
    return out


def _walk_strings(node):
    if isinstance(node, dict):
        for v in node.values():
            yield from _walk_strings(v)
    elif isinstance(node, list):
        for v in node:
            yield from _walk_strings(v)
    elif isinstance(node, str):
        yield node


def missing_contract(bid, empties, required):
    """棟 bid の Empty 一覧に、データが要求する POI が全部あるか。"""
    have = set(empties)
    return sorted(n for n in required.get(bid, ()) if n not in have)


def verify_fbx(path, expect_empties):
    """書き出した FBX を読み直して Empty がそろっているか確かめる。"""
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    got = {o.name for o in bpy.context.scene.objects if o.type == "EMPTY"}
    # FBX は同名衝突時に連番を足すことがあるので前方一致でも拾う
    missing = []
    for name in expect_empties:
        if name in got or any(g.startswith(name) for g in got):
            continue
        missing.append(name)
    meshes = [o.name for o in bpy.context.scene.objects if o.type == "MESH"]
    return {"empties_found": len(got), "missing": missing,
            "meshes": len(meshes)}


# --------------------------------------------------------------------------- #
def main():
    t_all = time.time()
    args = parse_args(sys.argv)
    ids = None
    if args.ids and args.ids != "all":
        ids = [s.strip() for s in args.ids.split(",") if s.strip()]

    data = ispec.load_campus(args.json)
    specs = ispec.build_specs(data, ids=ids)
    order = [b for b in registry.ORDER if b in specs]
    for bid in sorted(specs):
        if bid not in order:
            order.append(bid)

    eng = engine_name(args.engine)
    print("[interiors] engine=%s buildings=%d" % (eng, len(order)))

    report = []
    for bid in order:
        plan = registry.get(bid)
        if plan is None:
            print("[interiors] %-11s プラン未実装のためスキップ" % bid)
            continue
        t0 = time.time()
        sp = specs[bid]
        info = build_one(sp, plan, args, eng)
        info["id"] = bid
        info["label"] = registry.LABELS.get(bid, bid)
        info["sec"] = time.time() - t0
        print("[interiors] %-11s %6.1f x %5.1f m  tris=%7d  empties=%3d  "
              "%5.1f s  %s"
              % (bid, sp.width, sp.depth, info["tris"], len(info["empties"]),
                 info["sec"],
                 "%.2f MB" % (info["size"] / 1048576.0) if info["size"] else "-"))
        for n in info["notes"]:
            print("             - %s" % n)
        report.append(info)

    # --- 検証: 書き出した FBX を読み直す ---
    print("\n[verify] 書き出した FBX を再インポートして Empty を確認")
    required = required_pois(args.data_dir)
    print("[verify] データが要求する POI: %d 棟 %d 件 (%s)"
          % (len(required), sum(len(v) for v in required.values()),
             args.data_dir))
    ok = True
    for info in report:
        if not info.get("fbx"):
            continue
        res = verify_fbx(info["fbx"], info["empties"])
        lacking = missing_contract(info["id"], info["empties"], required)
        bad = bool(res["missing"] or lacking)
        mark = "OK " if not bad else "NG "
        if bad:
            ok = False
        print("[verify] %s%-11s meshes=%3d empties=%3d/%3d 必須POI=%d %s%s"
              % (mark, info["id"], res["meshes"], res["empties_found"],
                 len(info["empties"]), len(required.get(info["id"], ())),
                 "" if not res["missing"] else "欠落: %s " % res["missing"][:5],
                 "" if not lacking else
                 "契約違反(データが参照するのに無い): %s" % lacking))

    total = sum(i["tris"] for i in report)
    print("\n[interiors] 合計 %d 三角形 / %d 棟 / %.1f s  (%s)"
          % (total, len(report), time.time() - t_all,
             "OK" if ok else "Empty 欠落 / POI 契約違反あり"))

    # 集計を JSON で残す（README 生成の材料）。
    # 一部の棟だけを流したときに上書きすると全棟ぶんの集計が失われるので、
    # 全棟を書き出したときだけ更新する。
    summary = os.path.join(args.out_dir, "_summary.json")
    if len(report) == len(registry.ORDER) and not args.no_export:
        os.makedirs(args.out_dir, exist_ok=True)
        with open(summary, "w", encoding="utf-8") as fp:
            json.dump({"total_tris": total, "buildings": report}, fp,
                      ensure_ascii=False, indent=1)
        print("[interiors] 集計: %s" % summary)
    else:
        print("[interiors] 集計は据え置き（全棟の書き出しではないため）: %s"
              % summary)

    if not ok:
        # Empty が欠けた FBX は Unity 側の配置が壊れるので、失敗として終了する。
        sys.exit(1)


if __name__ == "__main__":
    main()

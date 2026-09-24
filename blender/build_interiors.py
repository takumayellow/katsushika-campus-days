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

窓の外の近景（ext_<id> / ext_<id>_trees）は campus.fbx / trees.fbx から切り出して同じ
FBX に入れる（kcd_interior/exterior.py）。--no-ext で入れない。
"""

import argparse
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from kcd_lib import entrances, mats, render  # noqa: E402
from kcd_interior import closure, exterior, imats, registry, spec as ispec  # noqa: E402
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


EXT_PREFIX = exterior.EXT_PREFIX

# 三角数の上限（docs/DESIGN.md §3.4）。超えたら NG で終える
INT_BUDGET = 300000       # 屋内の合計
EXT_BUDGET = 180000       # 近景の合計
EXT_BUDGET_ONE = 50000    # 近景 1 棟

# 屋内の入口とキャンパスの扉の照合。屋内の内向きと扉の外向き法線の内積の上限と、
# 面に沿った位置の許容差 [m]
DOOR_DOT_MAX = -0.9
DOOR_LATERAL_TOL = 0.05


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
    p.add_argument("--no-ext", action="store_true",
                   help="窓の外の近景（ext_<id>）を入れない")
    p.add_argument("--campus-dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Models", "Campus"),
                   help="近景の元にする campus.fbx / trees.fbx の場所")
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


def mesh_tris(ext=False):
    """シーンの三角形数。ext=False なら屋内だけ、True なら近景（ext_）だけ。"""
    n = 0
    for o in bpy.context.scene.objects:
        if o.type == "MESH" and o.name.startswith(EXT_PREFIX) == ext:
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
def build_one(sp, plan, args, eng, ext_src=None):
    reset_scene()
    mats.build_all()      # 外装パレット（concrete_* / glass_clear など）
    imats.build_all()     # インテリア専用パレット

    c = Ctx(sp, seed=args.seed)
    plan.build(c)
    c.flush_seats()       # 座れる家具 -> seat_ Empty（Unity の SeatFactory が読む）

    objects = []
    for mb in c.builders():
        if not mb.faces:
            continue
        objects.append(mb.to_object())

    for name, loc in c.empties:
        add_empty(name, loc)

    ext_objects, ext_stats = [], {}
    if ext_src is not None:
        # 屋内で目が届く一番高い点。屋根より上から外を見ることはない
        top = max((v.co.z for o in objects for v in o.data.vertices), default=0.0)
        builders, ext_stats = exterior.build(sp, ext_src, top, c.door_gap)
        ext_objects = [mb.to_object() for mb in builders]

    info = {
        "tris": mesh_tris(),
        "ext_tris": mesh_tris(ext=True),
        "ext_stats": ext_stats,
        "objects": [o.name for o in objects],
        "ext_objects": [o.name for o in ext_objects],
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
    if ext_objects:
        meta_extra["ext"] = {
            "objects": info["ext_objects"],
            "triangles": info["ext_tris"],
            "radius": exterior.RADIUS,
            "dz": exterior.DZ,
        }

    # --- FBX（配置メタは FBX と組なので、FBX を書くときだけ書く） ---
    if args.no_export:
        info["fbx"] = None
        info["size"] = 0
    else:
        ispec.dump_sidecar(sp, meta_extra, os.path.join(args.out_dir, "%s.json" % sp.id))
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


def summary_rows(report, summary_path, rendered, preview_dir):
    """_summary.json に書く行。パスはリポジトリからの相対パスにする（作業ツリーの
    場所で中身が変わらないように）。プレビューを撮らなかった回は、前回の一覧のうち
    preview_dir に今もあるものを引き継ぐ。"""
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    def rel(p):
        try:
            return os.path.relpath(p, root).replace("\\", "/")
        except ValueError:  # 別ドライブ
            return p.replace("\\", "/")

    old = {}
    if not rendered and os.path.isfile(summary_path):
        with open(summary_path, encoding="utf-8") as fp:
            old = {b["id"]: b.get("previews", [])
                   for b in json.load(fp).get("buildings", [])}
    rows = []
    for info in report:
        row = dict(info)
        row["fbx"] = rel(info["fbx"]) if info.get("fbx") else None
        if rendered:
            row["previews"] = [rel(p) for p in info["previews"]]
        else:
            kept = [os.path.join(preview_dir,
                                 os.path.basename(p.replace("\\", "/")))
                    for p in old.get(info["id"], [])]
            row["previews"] = [rel(p) for p in kept if os.path.isfile(p)]
        rows.append(row)
    return rows


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


def read_sidecar(out_dir, bid):
    """書き出した <id>.json（外周・壁厚などの配置メタ）。無ければ None。"""
    path = os.path.join(out_dir, "%s.json" % bid)
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as fp:
        return json.load(fp)


def missing_contract(bid, empties, required):
    """棟 bid の Empty 一覧に、データが要求する POI が全部あるか。"""
    have = set(empties)
    return sorted(n for n in required.get(bid, ()) if n not in have)


def verify_fbx(path, expect_empties, meta=None, max_gap=0.25):
    """書き出した FBX を読み直して Empty がそろっているか確かめる。

    meta（<id>.json の中身）を渡すと、外周が人の通れない壁で閉じているかも測る。
    PhysX は三角形を片面でしか受け止めないので、外向きの面しか無いガラスは
    室内から素通りになる。max_gap より広い穴があれば契約違反として返す (#45)。
    """
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    got = {o.name for o in bpy.context.scene.objects if o.type == "EMPTY"}
    # FBX は同名衝突時に連番を足すことがあるので前方一致でも拾う
    missing = []
    for name in expect_empties:
        if name in got or any(g.startswith(name) for g in got):
            continue
        missing.append(name)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    # 近景は外周の外にしか無いが、壁の判定や階の高さに混ぜない
    objs = [o for o in meshes if not o.name.startswith(EXT_PREFIX)]
    holes = []
    if meta:
        holes = [g for g in closure.measure(objs, meta) if g["width"] > max_gap]
    return {"empties_found": len(got), "missing": missing,
            "meshes": len(objs), "ext_meshes": len(meshes) - len(objs),
            "ext_names": sorted(re.sub(r"\.\d{3}$", "", o.name)
                                for o in meshes if o.name.startswith(EXT_PREFIX)),
            "holes": holes}


# --------------------------------------------------------------------------- #
def door_mismatch(specs, data):
    """屋内の入口が、キャンパスの扉（kcd_lib.entrances）と同じ面の同じ位置にあるか。

    屋内の内向き（ローカル +Y）と扉の外向き法線の内積が DOOR_DOT_MAX 未満で、屋内の面に
    沿った向き（ローカル X）の差が DOOR_LATERAL_TOL 以内、かつ原点が扉より外にあれば合格。
    面に直交する向きの差は、屋内の外周が footprint の外接矩形なので棟ごとに違い、
    正であることだけを見る。扉が無い棟は不合格にする（入口の面を推測で置いているため）。
    扉が同じ棟に複数あれば、spec._door_row と同じく最初の 1 つと照合する。
    戻り値は [(bid, 内積, 面に沿った差, 奥行きの差, 合否)]。扉が無い棟は数値が None。
    """
    doors = {}
    for dr in entrances.plan(data, ispec.campus_frame(data), {}):
        doors.setdefault(dr["id"], dr)
    rows = []
    for bid, sp in sorted(specs.items()):
        dr = doors.get(bid)
        if dr is None:
            rows.append((bid, None, None, None, False))
            continue
        ox, oy = sp.to_world_xy((0.0, 0.0))

        def axis(u, v):
            wx, wy = sp.to_world_xy((u, v))
            return wx - ox, wy - oy

        xx, xy = axis(1.0, 0.0)
        yx, yy = axis(0.0, 1.0)
        n = dr["n"]
        dot = yx * n[0] + yy * n[1]
        dx, dy = dr["origin"][0] - ox, dr["origin"][1] - oy
        lateral = dx * xx + dy * xy
        depth = dx * yx + dy * yy
        good = (dot < DOOR_DOT_MAX and abs(lateral) <= DOOR_LATERAL_TOL
                and depth > 0.0)
        rows.append((bid, dot, lateral, depth, good))
    return rows


def over_budget(report, total, total_ext):
    """三角数が上限を超えたものの説明のリスト。"""
    msgs = ["%s の近景 %d > %d" % (i["id"], i["ext_tris"], EXT_BUDGET_ONE)
            for i in report if i["ext_tris"] > EXT_BUDGET_ONE]
    if total > INT_BUDGET:
        msgs.append("屋内の合計 %d > %d" % (total, INT_BUDGET))
    if total_ext > EXT_BUDGET:
        msgs.append("近景の合計 %d > %d" % (total_ext, EXT_BUDGET))
    return msgs


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

    ext_src = None
    if not args.no_ext:
        t0 = time.time()
        ext_src = exterior.load(args.campus_dir, data)
        print("[interiors] 近景の元: 面 %d / 木 %d 本 (%.1f s, %s)"
              % (len(ext_src.polys), len(ext_src.trees), time.time() - t0,
                 args.campus_dir))

    report = []
    for bid in order:
        plan = registry.get(bid)
        if plan is None:
            print("[interiors] %-11s プラン未実装のためスキップ" % bid)
            continue
        t0 = time.time()
        sp = specs[bid]
        info = build_one(sp, plan, args, eng, ext_src)
        info["id"] = bid
        info["label"] = registry.LABELS.get(bid, bid)
        info["sec"] = time.time() - t0
        print("[interiors] %-11s %6.1f x %5.1f m  tris=%7d  ext=%6d  "
              "empties=%3d  %5.1f s  %s"
              % (bid, sp.width, sp.depth, info["tris"], info["ext_tris"],
                 len(info["empties"]), info["sec"],
                 "%.2f MB" % (info["size"] / 1048576.0) if info["size"] else "-"))
        es = info["ext_stats"]
        if es:
            print("             近景: %s" % ", ".join(
                "%s=%d" % kv for kv in sorted(es["tris"].items(), key=lambda kv: -kv[1])))
            print("                   裏向きで除外 %d（うち木 %d）/ 株 %d（簡略形 %d）/ 木 %d 本"
                  "（樹冠が外周にかかり除外 %d 本）"
                  % (sum(es["culled_tris"].values()), es["culled_tris"]["trees"],
                     sum(es["plants"].values()), es["plants"]["simple"],
                     es["trees"]["placed"], es["trees"]["skipped"]))
            print("                   自分の棟: 外装 %d 面・扉 %d 面を除外 / 入口の前: 設備 %d 個"
                  "（%d 面）・木 %d 本を除外"
                  % (es["own"]["bld"], es["own"]["door"], es["entrance"]["props"],
                     es["entrance"]["prop_faces"], es["entrance"]["trees"]))
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
        meta = read_sidecar(args.out_dir, info["id"])
        res = verify_fbx(info["fbx"], info["empties"], meta)
        lacking = missing_contract(info["id"], info["empties"], required)
        holes = res["holes"]
        ext_lost = res["ext_names"] != sorted(info["ext_objects"])
        bad = bool(res["missing"] or lacking or holes or ext_lost)
        mark = "OK " if not bad else "NG "
        if bad:
            ok = False
        print("[verify] %s%-11s meshes=%3d+%d empties=%3d/%3d 必須POI=%d 外周=%s %s%s%s"
              % (mark, info["id"], res["meshes"], res["ext_meshes"],
                 res["empties_found"],
                 len(info["empties"]), len(required.get(info["id"], ())),
                 "閉" if not holes else
                 "穴%d 計%.1fm 最大%.2fm(%s z=%.1f)"
                 % (len(holes), sum(g["width"] for g in holes),
                    max(g["width"] for g in holes),
                    max(holes, key=lambda g: g["width"])["name"],
                    max(holes, key=lambda g: g["width"])["z"]),
                 "" if not res["missing"] else "欠落: %s " % res["missing"][:5],
                 "" if not lacking else
                 "契約違反(データが参照するのに無い): %s" % lacking,
                 "" if not ext_lost else
                 " 近景のメッシュが違う %s != %s" % (res["ext_names"],
                                                    sorted(info["ext_objects"]))))

    for bid, dot, lateral, depth, good in door_mismatch(specs, data):
        if not good:
            ok = False
        if dot is None:
            print("[verify] NG %-11s 入口とキャンパスの扉: kcd_lib.entrances.DOORS に扉が無い"
                  % bid)
            continue
        print("[verify] %s%-11s 入口とキャンパスの扉: 向きの内積 %.3f 面に沿った差 %.3f m"
              " 奥行きの差 %.2f m" % ("OK " if good else "NG ", bid, dot, lateral, depth))

    total = sum(i["tris"] for i in report)
    total_ext = sum(i["ext_tris"] for i in report)
    over = over_budget(report, total, total_ext)
    for msg in over:
        print("[budget] NG %s" % msg)
    if over:
        ok = False
    print("\n[interiors] 合計 %d / %d 三角形 + 近景 %d / %d / %d 棟 / %.1f s  (%s)"
          % (total, INT_BUDGET, total_ext, EXT_BUDGET, len(report), time.time() - t_all,
             "OK" if ok else
             "Empty 欠落 / POI 契約違反 / 外周の穴 / 近景のメッシュ違い / 入口と扉の食い違い"
             " / 予算超過あり"))

    # 集計を JSON で残す（README 生成の材料）。
    # 一部の棟だけを流したときに上書きすると全棟ぶんの集計が失われるので、
    # 全棟を書き出したときだけ更新する。
    summary = os.path.join(args.out_dir, "_summary.json")
    if len(report) == len(registry.ORDER) and not args.no_export:
        os.makedirs(args.out_dir, exist_ok=True)
        rows = summary_rows(report, summary, args.preview, args.preview_dir)
        with open(summary, "w", encoding="utf-8") as fp:
            json.dump({"total_tris": total, "total_ext_tris": total_ext,
                       "buildings": rows}, fp,
                      ensure_ascii=False, indent=1)
        print("[interiors] 集計: %s" % summary)
    else:
        print("[interiors] 集計は据え置き（全棟の書き出しではないため）: %s"
              % summary)

    if not ok:
        # Empty が欠けた FBX は Unity 側の配置が壊れる。外周に穴があると
        # プレイヤーが建物の外の何も無い空間へ出られる。予算超過は棟を増やす前に
        # RADIUS / PLANT_FULL などを見直す合図。どれも失敗として終了する。
        sys.exit(1)


if __name__ == "__main__":
    main()

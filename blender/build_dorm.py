"""葛飾コミュニティハウス（学生寮）を headless で生成する — 裏エンド「寮でぐーたら」(#41)。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/build_dorm.py -- --preview --preview-dir <出力先>

出力:
  unity/KatsushikaCampusDays/Assets/Models/Interiors/dorm.fbx   屋内
  unity/KatsushikaCampusDays/Assets/Models/Interiors/dorm.json  配置メタ（Unity が読む）
  <preview-dir>/dorm_front.png / dorm_oblique.png / interior_dorm*.png

外観は route.fbx に混ぜる前提なので **既定では FBX を書き出さない**（build_route.py が
kcd_route.dorm.build_exterior を呼ぶ）。単体で確認したいときだけ --exterior-fbx を渡す。

既存 9 棟の集計（Assets/Models/Interiors/_summary.json）には触らない。dorm は独立予算。
"""

import argparse
import json
import math
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from kcd_lib import geom, mats, render, uv             # noqa: E402
from kcd_lib.mesh import MeshBuilder                   # noqa: E402
from kcd_interior import closure, imats                # noqa: E402
from kcd_interior import spec as ispec                 # noqa: E402
from kcd_interior.ctx import Ctx                       # noqa: E402
from kcd_route import dorm as D                        # noqa: E402
from kcd_route import dorm_interior as DI              # noqa: E402

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

TOL = 0.01          # footprint の一致とみなす許容差 [m]（要求は 1 cm 以内）
MAX_GAP = 0.25      # 外周の穴とみなす幅 [m]（build_interiors.verify_fbx と同じ）
EXT_BUDGET = 8000   # route.fbx 全体の予算。外観はその一部
INT_BUDGET = 30000  # dorm.fbx の予算


# --------------------------------------------------------------------------- #
#  Blender まわり（build_interiors.py と同じ）
# --------------------------------------------------------------------------- #
def parse_args(argv):
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    p = argparse.ArgumentParser(prog="build_dorm.py")
    p.add_argument("--json", default=os.path.join(root, "data", "osm", "route.json"))
    p.add_argument("--out-dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Models", "Interiors"))
    p.add_argument("--preview", action="store_true")
    p.add_argument("--preview-dir", default=os.path.join(root, "build", "dorm_preview"))
    p.add_argument("--engine", default="auto", choices=["auto", "eevee", "workbench"])
    p.add_argument("--seed", type=int, default=20250923)
    p.add_argument("--mode", default="both", choices=["both", "exterior", "interior"])
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--exterior-fbx", default=None,
                   help="外観を単体 FBX にも書き出す（既定は書き出さない。route.fbx へ入れるため）")
    p.add_argument("--report", default=None, help="測った値を JSON で残す")
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0


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


def obj_tris(o):
    return sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)


def scene_tris():
    return sum(obj_tris(o) for o in bpy.context.scene.objects if o.type == "MESH")


def export_fbx(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    uv.write_scene(bpy.context.scene)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    return os.path.getsize(path)


def max_edge(o):
    """そのオブジェクトの最長の辺 [m]（PhysX の細長い三角形の警告の目安）。"""
    best = 0.0
    vs = o.data.vertices
    for e in o.data.edges:
        a, b = vs[e.vertices[0]].co, vs[e.vertices[1]].co
        best = max(best, (a - b).length)
    return best


# --------------------------------------------------------------------------- #
#  外観の検証
# --------------------------------------------------------------------------- #
def _t_on_edge(p, a, b):
    """点 p を線分 a-b に射影した (沿う距離 t, 線からの距離)。"""
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    vx, vy = p[0] - a[0], p[1] - a[1]
    t = vx * ux + vy * uy
    return t, abs(vx * -uy + vy * ux)


def check_footprint(obj, loop):
    """躯体の頂点が route.json の footprint と一致しているか。

    C1 6 頂点それぞれに TOL 以内の頂点があるか
    C2 footprint の外へ TOL より出ている頂点が無いか
    C3 各辺が端から端まで頂点で張られているか（辺が欠けていないか）
    """
    pts = [(v.co.x, v.co.y) for v in obj.data.vertices]
    n = len(loop)
    corners = []
    for cp in loop:
        d = min(math.hypot(p[0] - cp[0], p[1] - cp[1]) for p in pts)
        corners.append(round(d, 5))
    outside = 0.0
    for p in pts:
        if geom.point_in_poly(p, loop):
            continue
        outside = max(outside, geom.dist_point_poly_edges(p, loop))
    edges = []
    for i in range(n):
        a, b = loop[i], loop[(i + 1) % n]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        ts = [t for t, d in (_t_on_edge(p, a, b) for p in pts)
              if d <= TOL and -TOL <= t <= L + TOL]
        if not ts:
            edges.append({"edge": i, "length": round(L, 3), "covered": False})
            continue
        edges.append({"edge": i, "length": round(L, 3), "covered": True,
                      "t_min": round(min(ts), 4),
                      "t_max_gap": round(L - max(ts), 4)})
    return {
        "corner_dist_max": max(corners), "corner_dist": corners,
        "outside_max": round(outside, 5),
        "edges": edges,
        "ok": (max(corners) <= TOL and outside <= TOL
               and all(e.get("covered") and e["t_min"] <= TOL
                       and e["t_max_gap"] <= TOL for e in edges)),
    }


def check_entrance(dorm, dr):
    """玄関の位置と向きが route.json と合っているか（実測の辺中点とも比べる）。"""
    loop = D.loop_of(dorm)
    ent = dorm["entrance"]
    ei = int(ent["edge_index"])
    a, b = loop[ei], loop[(ei + 1) % len(loop)]
    mid = ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5)
    nrm = geom.outward_normal(a, b)
    bearing = math.degrees(math.atan2(nrm[0], nrm[1])) % 360.0
    want = tuple(ent["point"])
    return {
        "json_point": [round(want[0], 4), round(want[1], 4)],
        "built_origin": [round(dr["origin"][0], 4), round(dr["origin"][1], 4)],
        "d_origin_vs_json": round(math.hypot(dr["origin"][0] - want[0],
                                             dr["origin"][1] - want[1]), 6),
        "edge_midpoint": [round(mid[0], 4), round(mid[1], 4)],
        "d_json_vs_edge_midpoint": round(math.hypot(want[0] - mid[0],
                                                    want[1] - mid[1]), 5),
        "json_bearing": round(float(ent["facing_bearing"]), 3),
        "built_bearing": round(math.degrees(math.atan2(dr["n"][0], dr["n"][1])) % 360.0, 3),
        "edge_bearing": round(bearing, 3),
        "d_bearing_deg": round(abs(bearing - float(ent["facing_bearing"])), 4),
    }


def build_exterior_scene(dorm, args, eng):
    reset_scene()
    mats.build_all()
    imats.build_all()          # 玄関が使う wall_accent_navy / light_panel

    shell_mb = MeshBuilder(D.SHELL_OBJ)
    trim_mb = MeshBuilder(D.TRIM_OBJ)
    _s, _t, info = D.build_exterior(dorm, shell_mb, trim_mb)
    shell = shell_mb.to_object()
    trim = trim_mb.to_object()

    dr = info["door"]
    add_empty("door_%s" % D.ID, (*_pt(dr, 0.0, dr["D"]), 0.12), size=1.2)
    add_empty("entrance_%s" % D.ID, (*_pt(dr, 0.0, dr["D"] + 0.8), 0.12), size=1.2)

    loop = D.loop_of(dorm)
    out = {
        "shell_tris": obj_tris(shell), "trim_tris": obj_tris(trim),
        "tris": scene_tris(),
        "shell_max_edge": round(max_edge(shell), 3),
        "trim_max_edge": round(max_edge(trim), 3),
        "levels": info["levels"], "floor_h_ground": round(info["gf"], 3),
        "floor_h_upper": round(info["up"], 3), "height": info["height"],
        "balcony_edge": info["balcony_edge"],
        "balcony_floors": info["balcony_floors"],
        "balcony_units": info["balcony_units"],
        "footprint": check_footprint(shell, loop),
        "entrance": check_entrance(dorm, dr),
        "fbx": None, "size": 0, "previews": [],
    }

    if args.exterior_fbx and not args.no_export:
        out["size"] = export_fbx(args.exterior_fbx)
        out["fbx"] = args.exterior_fbx

    if args.preview:
        _ground(loop)
        render.setup_world(eng, sky=(0.55, 0.68, 0.86))
        origin, n, _b = D.entrance_of(dorm)
        cx, cy = geom.centroid(loop)
        t = (-n[1], n[0])
        cams = [
            ("dorm_front",
             (origin[0] + n[0] * 52.0 - t[0] * 6.0,
              origin[1] + n[1] * 52.0 - t[1] * 6.0, 15.0),
             (cx * 0.35 + origin[0] * 0.65, cy * 0.35 + origin[1] * 0.65, 8.0), 34.0),
            ("dorm_oblique",
             (cx + 46.0, cy - 40.0, 34.0), (cx, cy, 7.0), 30.0),
        ]
        for i, (stem, loc, tgt, lens) in enumerate(cams):
            png = os.path.join(args.preview_dir, "%s.png" % stem)
            cam = render.make_camera("KCD_ECam_%d" % i, loc, tgt, lens)
            if render.render_to(cam, png):
                out["previews"].append(png)
    return out


def _pt(dr, s, d):
    o, n, t = dr["origin"], dr["n"], dr["t"]
    return (o[0] + t[0] * s + n[0] * d, o[1] + t[1] * s + n[1] * d)


def _ground(loop):
    """プレビュー専用の地面（FBX を書き出したあとに呼ぶ）。"""
    cx, cy = geom.centroid(loop)
    mb = MeshBuilder("preview_ground")
    mb.add_prism([(cx - 140, cy - 140), (cx + 140, cy - 140),
                  (cx + 140, cy + 140), (cx - 140, cy + 140)],
                 -0.40, -0.02, "grass_dark", "grass_dark", None)
    # 水戸街道（玄関の向く側）を 1 本だけ。建物の足元が浮いて見えないように
    mb.add_prism([(cx - 140, cy - 34), (cx + 140, cy - 34),
                  (cx + 140, cy - 20), (cx - 140, cy - 20)],
                 -0.02, 0.02, "asphalt", "asphalt", None)
    mb.to_object()


# --------------------------------------------------------------------------- #
#  屋内
# --------------------------------------------------------------------------- #
def build_interior_scene(dorm, args, eng):
    reset_scene()
    mats.build_all()
    imats.build_all()

    sp = DI.make_spec(dorm)
    c = Ctx(sp, seed=args.seed)
    DI.build(c)
    c.flush_seats()

    objects = [mb.to_object() for mb in c.builders() if mb.faces]
    for name, loc in c.empties:
        add_empty(name, loc)

    tris = scene_tris()
    meta_extra = {
        "spawn": next(({"x": round(p[0], 3), "y": round(p[2], 3), "z": round(p[1], 3)}
                       for n, p in c.empties if n.startswith("spawn_")), None),
        "empties": {n: {"x": round(p[0], 3), "y": round(p[2], 3), "z": round(p[1], 3)}
                    for n, p in c.empties},
        "triangles": tris,
        "seats": len(c.seat_list()),
        "notes": c.notes,
        "objects": [o.name for o in objects],
        # --- 寮だけの追加メタ（Unity 側の実装が読む） ---
        "operator": D.OPERATOR,
        "not_university": D.NOT_UNIVERSITY,
        "off_campus": True,
        "minimap": "black",
        "roles": {"npc_dorm_1": "寮長（裏エンドのトリガー）",
                  "npc_dorm_head": "寮長（同じ位置の別名。Unity の DormStage は "
                                   "npc_ + DormRoute.DialogueId = npc_dorm_head を探す）",
                  "npc_dorm_2": "寮生"},
        "exterior": {
            "source": "data/osm/route.json#dormitory",
            "footprint": [[round(p[0], 3), round(p[1], 3)] for p in D.loop_of(dorm)],
            "height": float(dorm["height"]), "levels": int(dorm["levels"]),
            "door_world": {"x": round(dorm["entrance"]["point"][0], 4),
                           "z": round(dorm["entrance"]["point"][1], 4)},
            "facing_bearing_deg": float(dorm["entrance"]["facing_bearing"]),
        },
    }
    sidecar = None
    if not args.no_export:
        sidecar = os.path.join(args.out_dir, "%s.json" % D.ID)
        ispec.dump_sidecar(sp, meta_extra, sidecar)

    out = {"tris": tris, "objects": [o.name for o in objects],
           "empties": [n for n, _ in c.empties], "notes": c.notes,
           "sidecar": sidecar, "fbx": None, "size": 0, "previews": [],
           "max_edge": round(max(max_edge(o) for o in objects), 3),
           "envelope": {"width": round(sp.width, 3), "depth": round(sp.depth, 3),
                        "y_face": sp.y_face, "y_back": sp.y_back}}

    if not args.no_export:
        path = os.path.join(args.out_dir, "%s.fbx" % D.ID)
        out["size"] = export_fbx(path)
        out["fbx"] = path

    if args.preview and c.cams:
        render.setup_world(eng, sky=(0.55, 0.68, 0.86))
        sun = bpy.data.objects.get("KCD_Sun")
        if sun is not None:
            sun.data.energy = 1.4
        for i, (x, y, z, energy, radius) in enumerate(c.lights):
            add_point_light("KCD_IL_%03d" % i, (x, y, z), energy, radius)
        for idx, (suffix, loc, target, lens) in enumerate(c.cams):
            stem = "interior_%s_%s" % (D.ID, suffix) if suffix else "interior_%s" % D.ID
            png = os.path.join(args.preview_dir, "%s.png" % stem)
            cam = render.make_camera("KCD_ICam_%d" % idx, loc, target, lens)
            if render.render_to(cam, png):
                out["previews"].append(png)
    return out


def verify_interior(path, expect_empties, meta):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    got = {o.name for o in bpy.context.scene.objects if o.type == "EMPTY"}
    missing = [n for n in expect_empties
               if n not in got and not any(g.startswith(n) for g in got)]
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    gaps = closure.measure(objs, meta)
    holes = [g for g in gaps if g["width"] > MAX_GAP]
    return {"empties_found": len(got), "missing": missing, "meshes": len(objs),
            "gaps_all": len(gaps), "gaps": gaps,
            "gaps_total_m": round(sum(g["width"] for g in gaps), 3),
            "holes": holes,
            "holes_total_m": round(sum(g["width"] for g in holes), 3)}


# --------------------------------------------------------------------------- #
def main():
    t0 = time.time()
    args = parse_args(sys.argv)
    with open(args.json, encoding="utf-8") as fp:
        route = json.load(fp)
    dorm = route["dormitory"]
    dorm["footprint"] = [tuple(p) for p in dorm["footprint"]]

    eng = engine_name(args.engine)
    print("[dorm] engine=%s  %s (%s)" % (eng, D.DISPLAY, D.OPERATOR))
    print("[dorm] %s" % D.NOT_UNIVERSITY)

    report = {"engine": eng, "source": args.json}
    ok = True

    if args.mode in ("both", "exterior"):
        ext = build_exterior_scene(dorm, args, eng)
        report["exterior"] = ext
        fp_ = ext["footprint"]
        en = ext["entrance"]
        print("[dorm] 外観  tris=%d (躯体 %d + 付属 %d)  最長辺 %.1f/%.1f m"
              % (ext["tris"], ext["shell_tris"], ext["trim_tris"],
                 ext["shell_max_edge"], ext["trim_max_edge"]))
        print("[dorm] 外観  %d 階 / 階高 %.2f + %.2f x %d / 高さ %.1f m / バルコニー 辺%s x %d 層 x %d 戸"
              % (ext["levels"], ext["floor_h_ground"], ext["floor_h_upper"],
                 ext["levels"] - 1, ext["height"], ext["balcony_edge"],
                 ext["balcony_floors"], ext["balcony_units"]))
        print("[dorm] footprint  頂点ずれ最大 %.4f m / 外へのはみ出し最大 %.4f m / 辺の欠け %s  -> %s"
              % (fp_["corner_dist_max"], fp_["outside_max"],
                 "なし" if all(e.get("covered") for e in fp_["edges"]) else "あり",
                 "OK" if fp_["ok"] else "NG"))
        for e in fp_["edges"]:
            print("           辺%d 長さ %6.3f m  端の欠け %.4f / %.4f m"
                  % (e["edge"], e["length"], e.get("t_min", -1),
                     e.get("t_max_gap", -1)))
        print("[dorm] 玄関  json %s / 生成 %s  ずれ %.4f m、方位 json %.1f 度 / 生成 %.3f 度（実測の辺 %.3f 度、差 %.4f 度）"
              % (en["json_point"], en["built_origin"], en["d_origin_vs_json"],
                 en["json_bearing"], en["built_bearing"], en["edge_bearing"],
                 en["d_bearing_deg"]))
        if not fp_["ok"]:
            ok = False
        if ext["tris"] > EXT_BUDGET:
            print("[dorm] NG 外観が route.fbx の予算 %d を単独で超えた" % EXT_BUDGET)
            ok = False

    if args.mode in ("both", "interior"):
        inr = build_interior_scene(dorm, args, eng)
        report["interior"] = inr
        print("[dorm] 屋内  %.1f x %.1f m  tris=%d / 予算 %d  最長辺 %.1f m  empties=%d  %s"
              % (inr["envelope"]["width"], inr["envelope"]["depth"], inr["tris"],
                 INT_BUDGET, inr["max_edge"], len(inr["empties"]),
                 "%.2f MB" % (inr["size"] / 1048576.0) if inr["size"] else "-"))
        for n in inr["notes"]:
            print("           - %s" % n)
        if inr["tris"] > INT_BUDGET:
            print("[dorm] NG 屋内が予算 %d を超えた" % INT_BUDGET)
            ok = False
        if inr["fbx"]:
            with open(inr["sidecar"], encoding="utf-8") as fp:
                meta = json.load(fp)
            v = verify_interior(inr["fbx"], inr["empties"], meta)
            report["verify"] = v
            print("[dorm] 検証  meshes=%d empties=%d/%d  外周=%s（%.2f m 幅超えの穴 %d 件、"
                  "%.2f m 以下の隙間も含めた合計 %.3f m）%s"
                  % (v["meshes"], v["empties_found"], len(inr["empties"]),
                     "閉" if not v["holes"] else "穴あり",
                     MAX_GAP, len(v["holes"]), MAX_GAP, v["gaps_total_m"],
                     "" if not v["missing"] else " 欠落: %s" % v["missing"][:6]))
            for g in v["gaps"][:16]:
                print("           %s %s z=%.1f 幅 %.3f m  %s-%s"
                      % ("穴" if g["width"] > MAX_GAP else "隙間",
                         g["name"], g["z"], g["width"], g["a"], g["b"]))
            if v["missing"] or v["holes"]:
                ok = False

    report["ok"] = ok
    report["sec"] = round(time.time() - t0, 1)
    if args.report:
        os.makedirs(os.path.dirname(args.report), exist_ok=True)
        with open(args.report, "w", encoding="utf-8") as fp:
            json.dump(report, fp, ensure_ascii=False, indent=1)
        print("[dorm] 測定値: %s" % args.report)
    print("[dorm] %.1f s  %s" % (report["sec"], "OK" if ok else "NG"))
    if not ok:
        sys.exit(1)


if __name__ == "__main__":
    main()

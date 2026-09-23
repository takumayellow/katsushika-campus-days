"""東京理科大学 葛飾キャンパスの 3D モデルを headless で生成する。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/build_campus.py -- \
      --json data/osm/campus.json \
      --out-dir unity/KatsushikaCampusDays/Assets/Models/Campus \
      --preview

座標: campus.json の (x=東, z=北) を Blender の (x, y) に、z を上方向とする。
FBX は axis_forward='-Z' / axis_up='Y' / bake_space_transform=True で書き出すので、
Unity 側では (x, y=Blender z, z=Blender y) = (x, 上, 北) になる。
"""

import argparse
import json
import math
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from kcd_lib import buildings, entrances, geom, mats, props, render, site  # noqa: E402
from kcd_lib.mesh import MeshBuilder  # noqa: E402

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

# プレビュー 4 枚。(名前, 視点 (u, v, z), 注視点 (u, v, z), 焦点距離, 樹木の除外半径)
BG_WINDOW_BUDGET = 24000   # 背景建物の窓（四角 1 枚 = 三角 2）の上限

# site_ground を切り分ける格子の間隔。どの三角形の辺も対角線 42 m 以下になる（#30）
SITE_GRID = 30.0
SITE_MAX_EDGE = 50.0

CAM_SPECS = [
    ("campus_overview", (-30.0, -300.0, 205.0), (28.0, -8.0, 18.0), 35.0, 0.0),
    ("campus_mall", (176.0, -24.5, 1.60), (-20.0, -23.0, 10.0), 28.0, 8.0),
    ("campus_library", (26.0, -47.0, 2.80), (-84.0, -24.0, 17.0), 30.0, 26.0),
    ("campus_lecture", (48.0, -26.0, 1.70), (95.0, -6.0, 11.0), 26.0, 19.0),
]


# --------------------------------------------------------------------------- #
#  ユーティリティ
# --------------------------------------------------------------------------- #
def parse_args(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    p = argparse.ArgumentParser(prog="build_campus.py")
    p.add_argument("--json", default=os.path.join(root, "data", "osm", "campus.json"))
    p.add_argument("--out-dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Models", "Campus"))
    p.add_argument("--preview", action="store_true", help="プレビュー PNG を描画する")
    p.add_argument("--preview-dir", default=os.path.join(root, "docs", "previews"))
    p.add_argument("--trees", type=int, default=900, help="樹木の最大本数")
    p.add_argument("--engine", default="auto", choices=["auto", "eevee", "workbench"])
    p.add_argument("--seed", type=int, default=20250920)
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


def add_empty(name, loc, parent=None, size=2.0, kind="PLAIN_AXES"):
    o = bpy.data.objects.new(name, None)
    o.empty_display_type = kind
    o.empty_display_size = size
    o.location = (loc[0], loc[1], loc[2] if len(loc) > 2 else 0.0)
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o


def load_data(path):
    with open(path, "r", encoding="utf-8") as fp:
        data = json.load(fp)
    # 座標をタプル化（campus.json の [x, z] がそのまま Blender の (x, y)）
    data["campus_boundary"] = [tuple(p) for p in data["campus_boundary"]]
    for b in data["buildings"]:
        b["footprint"] = [tuple(p) for p in b["footprint"]]
    for p in data["paths"]:
        p["points"] = [tuple(q) for q in p["points"]]
    for a in data["areas"]:
        a["polygon"] = [tuple(q) for q in a["polygon"]]
    return data


def make_frame(data):
    """第1研究棟の長辺からキャンパスのローカル軸を決める。"""
    for b in data["buildings"]:
        if b["id"] == "research1":
            loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
            i = geom.longest_edge(loop)
            d = geom.normalize(geom.sub(loop[(i + 1) % len(loop)], loop[i]))
            if d[0] < 0:
                d = (-d[0], -d[1])
            return geom.Frame(d)
    return geom.Frame((1.0, 0.0))


def total_verts():
    n = 0
    for o in bpy.context.scene.objects:
        if o.type == "MESH":
            n += len(o.data.vertices)
    return n


def export_fbx(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    return os.path.getsize(path)


def check_site_ground(obj):
    """site_ground の面の向きと大きさを確かめる。崩れていたらビルドを止める（#30）。

    - 水平な面（|法線 z| > 0.99）はすべて上向き。下向きの床は Unity で描画されず、
      CharacterController の床にもならない（add_ribbon の巻き方向が逆だった不具合の再発防止）
    - 辺の長さは SITE_MAX_EDGE 以下（PhysX の巨大三角形の警告と接地の不安定を防ぐ）"""
    me = obj.data
    down = [p for p in me.polygons if p.normal.z < -0.99]
    if down:
        c = down[0].center
        raise RuntimeError("[site] site_ground に下向きの水平面が %d 枚あります（例: %s @ (%.1f, %.1f, %.3f)）"
                           % (len(down), me.materials[down[0].material_index].name, c.x, c.y, c.z))
    # Unity は多角形を三角形に割って取り込むので、対角線も含めて三角形の辺で測る
    me.calc_loop_triangles()
    longest = 0.0
    for t in me.loop_triangles:
        a, b, c = (me.vertices[i].co for i in t.vertices)
        longest = max(longest, (a - b).length, (b - c).length, (c - a).length)
    if longest > SITE_MAX_EDGE:
        raise RuntimeError("[site] site_ground に %.1f m の辺があります（上限 %.0f m）" % (longest, SITE_MAX_EDGE))
    up = sum(1 for p in me.polygons if p.normal.z > 0.99)
    print("[site] check ok: up-facing %d / %d faces, no down-facing, longest edge %.1f m"
          % (up, len(me.polygons), longest))


# --------------------------------------------------------------------------- #
#  キャンパス本体
# --------------------------------------------------------------------------- #
def build_campus(data, frame, rng, max_trees):
    scene_coll = bpy.context.scene.collection
    occ = site.Occupancy()       # 舗装・道路も含む全占有（散布の抑制）
    hard = site.Occupancy()      # 建物・水盤だけ（列植でも絶対に侵入しない）
    ctx = {"entrance": [], "sign": []}
    objects = []

    # --- 外構 ---
    site_mb = MeshBuilder("site_ground")
    site.build_ground(site_mb, data, frame, occ)
    site.build_paths(site_mb, data, occ)
    site.build_mall(site_mb, frame, occ)
    site.build_basin(site_mb, frame, occ)
    site.build_basin_keepout(frame, hard)
    n_cut, n_faces = site_mb.split_by_grid(SITE_GRID)
    print("[site] split %d faces by %.0f m grid -> %d faces" % (n_cut, SITE_GRID, n_faces))
    site_obj = site_mb.to_object(scene_coll)
    check_site_ground(site_obj)
    objects.append(site_obj)
    water_mb = MeshBuilder("site_water")
    site.build_water(water_mb, frame)
    objects.append(water_mb.to_object(scene_coll))

    # 建物どうしの接触判定（共創棟が第1研究棟に接する辺）と小物配置のために先に集める
    on_campus = []
    for b in data["buildings"]:
        style = b.get("style") or "background"
        loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
        if len(loop) >= 3 and style in buildings.BUILDERS and b.get("on_campus"):
            on_campus.append((b["id"], loop, float(b.get("height") or 10.0)))
    ctx["footprints"] = on_campus
    ctx["uvbb"] = {fid: frame.uv_bbox(fp) for fid, fp, _h in on_campus}
    # 背景建物の窓: 合計が BG_WINDOW_BUDGET 枚に収まるよう間隔を決める
    spacing = 2.4
    n_win = sum(buildings.bg_window_count(b, spacing) for b in data["buildings"]
                if (b.get("style") or "background") not in buildings.BUILDERS
                or not b.get("on_campus"))
    if n_win > BG_WINDOW_BUDGET:
        spacing *= n_win / float(BG_WINDOW_BUDGET)
    ctx["bg_window_spacing"] = spacing

    # --- 建物 ---
    bg_mb = MeshBuilder("bld_background")
    for b in data["buildings"]:
        style = b.get("style") or "background"
        loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
        if len(loop) < 3:
            continue
        if style == "background" or not b.get("on_campus"):
            buildings.build_background(bg_mb, b, ctx)
            occ.stamp_poly(loop, margin=1.5)
            hard.stamp_poly(loop, margin=1.0)
            continue
        builder = buildings.BUILDERS.get(style)
        if builder is None:
            buildings.build_background(bg_mb, b, ctx)
            continue
        mb = MeshBuilder("bld_%s" % b["id"])
        builder(mb, b, frame, ctx)
        occ.stamp_poly(loop, margin=3.0)
        hard.stamp_poly(loop, margin=2.0)
        obj = mb.to_object(scene_coll)
        obj["kcd_id"] = b["id"]
        objects.append(obj)
    objects.append(bg_mb.to_object(scene_coll))
    print("[bg] window quads=%d (spacing %.2f m)"
          % (ctx.get("bg_window_quads", 0), ctx["bg_window_spacing"]))

    # 共創棟はフットプリントを広げているので、そのぶんも占有に反映する
    if "kyoso_rect" in ctx:
        occ.stamp_poly(ctx["kyoso_rect"], margin=3.0)
        hard.stamp_poly(ctx["kyoso_rect"], margin=2.0)

    # --- 入口（風除室・ガラス扉・庇・足元の石張り）---
    # 扉の前の通り道は、ベンチ・照明柱・列植・散布の木より先に空けておく（#39）。
    # 名前が bld_ で始まるので Unity では Building レイヤー（カメラが突き抜けない）になる。
    doors = entrances.plan(data, frame, ctx)
    for dr in doors:
        lane = entrances.corridor(dr)
        occ.stamp_poly(lane, margin=1.0)
        hard.stamp_poly(lane, margin=1.0)
    ent_mb = MeshBuilder("bld_entrances")
    entrances.build(ent_mb, doors)
    objects.append(ent_mb.to_object(scene_coll))
    for dr in doors:
        u, v = frame.uv(dr["origin"])
        print("[entrance] %-10s wall (u%.1f, v%.1f)  facing %+.0f deg"
              % (dr["id"], u, v, math.degrees(dr["yaw"])))

    # --- ベンチ・照明柱 ---
    fur_mb = MeshBuilder("site_furniture")
    site.build_street_furniture(fur_mb, frame, hard)
    objects.append(fur_mb.to_object(scene_coll))

    # --- 看板・花壇・自販機・ゴミ箱 ---
    sign_mb = MeshBuilder("site_props_signs")
    site.build_signs(sign_mb, frame, ctx)
    objects.append(sign_mb.to_object(scene_coll))
    # 入口の駐輪場は実物に無いので置かない（#56）。代わりにモール北側の花壇
    beds_mb = MeshBuilder("site_props_beds")
    site.build_mall_beds(beds_mb, frame, occ, data, ctx)
    objects.append(beds_mb.to_object(scene_coll))
    print("[props] mall beds: %d (%s)" % (len(ctx["mall_beds"]),
                                          ", ".join("u%.0f..%.0f" % b for b in ctx["mall_beds"])))
    vend_mb = MeshBuilder("site_props_vending")
    trash_mb = MeshBuilder("site_props_trash")
    site.build_amenities(vend_mb, trash_mb, frame, ctx)
    objects.append(vend_mb.to_object(scene_coll))
    objects.append(trash_mb.to_object(scene_coll))

    # --- Empty（入口・扉・看板・プレイヤー初期位置）---
    # entrance_<id> は扉の前の床（Unity の入口トリガーの位置）、door_<id> は扉の外面の中心。
    # Unity は door → entrance を「建物の外へ向かう向き」として使う。
    for eid, pos, z in ctx["entrance"]:
        add_empty("entrance_%s" % eid, (pos[0], pos[1], z), kind="ARROWS")
    for eid, pos, z in ctx.get("door", []):
        add_empty("door_%s" % eid, (pos[0], pos[1], z), kind="PLAIN_AXES", size=1.0)
    # 看板 Empty は板の位置に置き、+X が板の正面（法線）になるよう回す
    for sid, pos, z, yaw in ctx.get("sign_placed") or [(s, p, z, 0.0) for s, p, z in ctx["sign"]]:
        add_empty("sign_%s" % sid, (pos[0], pos[1], z), kind="SINGLE_ARROW")
        bpy.data.objects["sign_%s" % sid].rotation_euler = (0.0, 0.0, yaw)
    spawn = frame.xy(site.MALL_U1 - 12.0, site.MALL_V)
    add_empty("spawn_player", (spawn[0], spawn[1], site.Z_MALL), kind="SPHERE", size=1.0)

    # プレビューの視点まわりには木を生やさない（カメラが葉に埋まるのを防ぐ）
    for _name, eye, _tgt, _lens, r in CAM_SPECS:
        if r > 0:
            p = frame.xy(eye[0], eye[1])
            occ.stamp_disc(p[0], p[1], r)
            hard.stamp_disc(p[0], p[1], r)

    trees = site.collect_trees(data, frame, occ, hard, rng, max_trees=max_trees)
    return objects, ctx, trees


def build_tree_meshes(collection=None):
    """3 種のツリーメッシュ（原点にモデリング）を作る。"""
    out = []
    for name, fn in props.TREE_SPECIES:
        mb = fn("tree_mesh_%s" % name)
        obj = mb.to_object(collection)
        out.append((name, obj))
    return out


def place_tree_instances(trees, tree_objs, collection=None):
    """プレビュー用にメッシュを共有した複製を並べる。"""
    coll = collection or bpy.context.scene.collection
    made = []
    for i, (x, y, sp, sc, rot) in enumerate(trees):
        src = tree_objs[sp % len(tree_objs)][1]
        o = bpy.data.objects.new("tree_inst_%04d" % i, src.data)
        o.location = (x, y, 0.0)
        o.rotation_euler = (0.0, 0.0, rot)
        o.scale = (sc, sc, sc)
        coll.objects.link(o)
        made.append(o)
    return made


# --------------------------------------------------------------------------- #
#  プレビュー
# --------------------------------------------------------------------------- #
def setup_cameras(frame):
    cams = {}
    for name, eye_uvz, tgt_uvz, lens, _r in CAM_SPECS:
        a = frame.xy(eye_uvz[0], eye_uvz[1])
        b = frame.xy(tgt_uvz[0], tgt_uvz[1])
        cams[name] = render.make_camera(
            "cam_" + name.replace("campus_", ""),
            (a[0], a[1], eye_uvz[2]), (b[0], b[1], tgt_uvz[2]), lens=lens)
    return cams


def render_previews(frame, out_dir, engine_pref):
    engines = []
    if engine_pref in ("auto", "eevee"):
        engines.append("BLENDER_EEVEE_NEXT")
    if engine_pref in ("auto", "workbench"):
        engines.append("BLENDER_WORKBENCH")

    cams = setup_cameras(frame)
    for engine in engines:
        try:
            render.setup_world(engine)
            ok = True
            for name, cam in cams.items():
                path = os.path.join(out_dir, "%s.png" % name)
                if not render.render_to(cam, path):
                    ok = False
                    break
            if ok:
                print("[preview] engine=%s -> %s" % (engine, out_dir))
                return engine
        except Exception as exc:  # noqa: BLE001
            print("[preview] %s failed: %s" % (engine, exc))
    print("[preview] レンダに失敗しました")
    return None


# --------------------------------------------------------------------------- #
#  main
# --------------------------------------------------------------------------- #
def main():
    args = parse_args(sys.argv)
    t0 = time.time()
    rng = random.Random(args.seed)

    data = load_data(args.json)
    reset_scene()
    mats.build_all()
    frame = make_frame(data)
    print("[frame] u=(%.4f, %.4f)  angle=%.2f deg"
          % (frame.u[0], frame.u[1], math.degrees(math.atan2(frame.u[1], frame.u[0]))))

    objects, ctx, trees = build_campus(data, frame, rng, args.trees)
    t_build = time.time()
    campus_verts = total_verts()
    print("[build] objects=%d  verts=%d  trees=%d  %.1fs"
          % (len(objects), campus_verts, len(trees), t_build - t0))
    for o in sorted(objects, key=lambda x: -len(x.data.vertices)):
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        print("   %-20s %7d verts  %7d faces  %7d tris"
              % (o.name, len(o.data.vertices), len(o.data.polygons), tris))

    campus_fbx = os.path.join(args.out_dir, "campus.fbx")
    size_campus = export_fbx(campus_fbx)
    print("[fbx] %s  %.2f MB" % (campus_fbx, size_campus / 1048576.0))

    # --- 樹木（プレビューにはインスタンスを置く）---
    tree_objs = build_tree_meshes()
    tree_verts = sum(len(o.data.vertices) for _, o in tree_objs)
    insts = place_tree_instances(trees, tree_objs)
    print("[trees] species=%d  mesh_verts=%d  instances=%d"
          % (len(tree_objs), tree_verts, len(insts)))

    engine = None
    if args.preview:
        engine = render_previews(frame, args.preview_dir, args.engine)
    t_preview = time.time()

    # --- trees.fbx（ツリーメッシュ 3 種 + tree_<n> Empty）---
    for o in list(bpy.context.scene.objects):
        if o.name.startswith("tree_mesh_"):
            continue
        bpy.data.objects.remove(o, do_unlink=True)
    groups = {}
    for name, _obj in props.TREE_SPECIES:
        groups[name] = add_empty("trees_%s" % name, (0.0, 0.0, 0.0), size=4.0)
    for i, (x, y, sp, sc, rot) in enumerate(trees):
        sp_name = props.TREE_SPECIES[sp % len(props.TREE_SPECIES)][0]
        e = add_empty("tree_%d" % i, (x, y, 0.0), parent=groups[sp_name],
                      size=1.0, kind="SINGLE_ARROW")
        e.rotation_euler = (0.0, 0.0, rot)
        e.scale = (sc, sc, sc)
        e["species"] = sp_name
    trees_fbx = os.path.join(args.out_dir, "trees.fbx")
    size_trees = export_fbx(trees_fbx)
    print("[fbx] %s  %.2f MB" % (trees_fbx, size_trees / 1048576.0))

    # サイドカー（Unity 側が種別を読めるように）
    meta = {"species": [n for n, _ in props.TREE_SPECIES],
            "trees": [{"i": i, "x": round(t[0], 3), "y": round(t[1], 3),
                       "species": props.TREE_SPECIES[t[2] % 3][0],
                       "scale": round(t[3], 3), "rot": round(t[4], 4)}
                      for i, t in enumerate(trees)]}
    with open(os.path.join(args.out_dir, "trees.json"), "w", encoding="utf-8") as fp:
        json.dump(meta, fp, ensure_ascii=False, indent=1)

    total = time.time()
    print("=" * 64)
    print("campus verts : %d  (limit 2,000,000)" % campus_verts)
    print("tree  verts  : %d x %d instances" % (tree_verts, len(trees)))
    print("campus.fbx   : %.2f MB" % (size_campus / 1048576.0))
    print("trees.fbx    : %.2f MB" % (size_trees / 1048576.0))
    print("entrances    : %d   doors: %d   signs: %d"
          % (len(ctx["entrance"]), len(ctx.get("door", [])), len(ctx["sign"])))
    print("engine       : %s" % engine)
    print("time         : build %.1fs / preview %.1fs / total %.1fs"
          % (t_build - t0, t_preview - t_build, total - t0))
    print("=" * 64)


if __name__ == "__main__":
    main()

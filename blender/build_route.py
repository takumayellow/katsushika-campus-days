"""裏エンド「寮でぐーたら」(#41) の回廊を headless で生成する。

北西門 (x = -340, z ≒ 240) から葛飾コミュニティハウス（寮, 重心 -464.5, 202.0）までの
屋外だけを作る。キャンパスの中は campus.fbx が持っているので、ここでは触らない。
寮の外観（bld_dorm / bld_dorm_trim）はここに含める。屋内だけが別 FBX（build_dorm.py →
Assets/Models/Interiors/dorm.fbx）で、外観は route.fbx に混ぜる約束になっている。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/build_route.py -- \
      --json data/osm/route.json \
      --out-dir unity/KatsushikaCampusDays/Assets/Models/Campus \
      --preview --preview-dir <どこか>

座標は campus.json と同じ (x=東, z=北) → Blender (x, y)、z が上。FBX の軸も campus.fbx と同じ。

出力先が Assets/Models/Campus/ なのは CharacterImporter.IsCampus が
`Assets/Models/Campus/` と `Assets/Models/Interiors/` しかキャンパス用の取り込み設定
（マテリアルの名前引き当て）を通さないため。Assets/Models/route.fbx に置くと
マテリアルが全部灰色 (B0B0AC) になる。
"""

import argparse
import inspect
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from kcd_lib import mats, render, site, uv  # noqa: E402
from kcd_lib.mesh import MeshBuilder  # noqa: E402
from kcd_route import dorm as dorm_mod, ground, props, roads  # noqa: E402

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

# プレビュー。(名前, 視点 (x, y, z), 注視点 (x, y, z), 焦点距離)
CAM_SPECS = [
    # 北西門の外に立って、寮のほうへ西を向いた目線
    ("route_front", (-334.0, 240.0, 1.70), (-455.0, 210.0, 6.0), 28.0),
    # 逆向き（寮の側から門を見る）。門柱と Wall_West の開口を確かめる
    ("route_gate", (-392.0, 236.0, 1.70), (-336.0, 241.0, 3.0), 30.0),
    # 歩ける西区画（Annex）の真上。塀の輪と、空けてある寮の敷地を確かめる
    ("route_annex", (-415.0, 58.0, 165.0), (-418.0, 212.0, 0.0), 30.0),
    # 回廊ぜんたい
    ("route_overhead", (-300.0, 96.0, 250.0), (-440.0, 212.0, 0.0), 32.0),
]


# --------------------------------------------------------------------------- #
#  ユーティリティ（build_campus.py と同じ作り）
# --------------------------------------------------------------------------- #
def parse_args(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    p = argparse.ArgumentParser(prog="build_route.py")
    p.add_argument("--json", default=os.path.join(root, "data", "osm", "route.json"))
    p.add_argument("--out-dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Models", "Campus"))
    p.add_argument("--name", default="route", help="出力名（route.fbx / route.json）")
    # 地面 + 沿道 + 塀 + 街灯で 11,888、寮の外観で 3,210。合わせて 15,098 なので
    # 14,000 のままだと寮を入れた瞬間に必ず落ちる (#41)。build_dorm.py の EXT_BUDGET=8000
    # と足しても余る 16,000 にしておく。
    p.add_argument("--budget", type=int, default=16000, help="三角形の上限")
    p.add_argument("--window-budget", type=int, default=700, help="窓の数の上限")
    p.add_argument("--lamps", action="store_true", default=True)
    p.add_argument("--no-lamps", dest="lamps", action="store_false")
    p.add_argument("--preview", action="store_true")
    p.add_argument("--preview-dir", default=os.path.join(root, "docs", "previews"))
    p.add_argument("--engine", default="auto", choices=["auto", "eevee", "workbench"])
    p.add_argument("--seed", type=int, default=20250923)
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


def export_fbx(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    uv.write_scene(bpy.context.scene)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    return os.path.getsize(path)


def tris_of(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# --------------------------------------------------------------------------- #
#  検算
# --------------------------------------------------------------------------- #
def check_layers():
    """kcd_route/ground.py に写した高さレイヤが kcd_lib/site.py と一致しているか。

    ground.py は bpy を引かずに済むよう定数を写しているので、本家がずれたらここで落とす。
    ずれたまま敷くと回廊の道とキャンパスの道が同じ高さで z ファイティングを起こす。"""
    for name in ("Z_GROUND", "Z_ROAD", "Z_LINE", "Z_FOOT", "PATH_THICKNESS"):
        a = getattr(ground, name)
        b = getattr(site, name)
        if abs(a - b) > 1e-9:
            raise RuntimeError("[route] %s が site.py と違う（route %.4f / site %.4f）" % (name, a, b))
    src = inspect.getsource(site.build_ground)
    want = "E = %.1f" % ground.GROUND_E
    if want not in src:
        raise RuntimeError("[route] site.build_ground の地盤の大きさが %s ではなくなっている。"
                           "ground.GROUND_E を合わせ直すこと" % want)
    print("[route] layers ok: ground=%.3f road=%.3f line=%.3f foot=%.3f thick=%.2f  E=%.1f"
          % (ground.Z_GROUND, ground.Z_ROAD, ground.Z_LINE, ground.Z_FOOT,
             ground.PATH_THICKNESS, ground.GROUND_E))


def check_seam(obj):
    """帯（grass_dark）の東端が既存の地盤の西端とぴったり合っているか（重なり 0・段差 0）。

    route_ground には帯のほかに道路も入っている。道路は継ぎ目をまたいで東（キャンパス側）
    まで続くので、東端は帯の面＝grass_dark の面だけで測る。"""
    me = obj.data
    grass = [i for i, m in enumerate(me.materials) if m.name.startswith("grass_dark")]
    if not grass:
        raise RuntimeError("[route] route_ground に grass_dark が無い")
    vids = set()
    for p in me.polygons:
        if p.material_index in grass:
            vids.update(p.vertices)
    xs = [me.vertices[i].co.x for i in vids]
    east, west = max(xs), min(xs)
    if abs(east - (-ground.GROUND_E)) > 1e-4:
        raise RuntimeError("[route] 帯の東端が %.4f（-%.1f のはず）" % (east, ground.GROUND_E))
    if abs(west - ground.BAND[0]) > 1e-4:
        raise RuntimeError("[route] 帯の西端が %.4f（%.1f のはず）" % (west, ground.BAND[0]))
    # 帯の天面はすべて Z_GROUND ちょうど（＝どこにも段差が無い）
    zs = [me.vertices[i].co.z for i in vids]
    step = max(abs(z - ground.Z_GROUND) for z in zs)
    if step > 1e-4:
        raise RuntimeError("[route] 帯に %.4f m の段差がある" % step)
    on = [i for i in vids if abs(me.vertices[i].co.x + ground.GROUND_E) < 1e-4]
    print("[route] seam ok: 帯 x=%.1f..%.1f, 継ぎ目 x=%.1f の頂点 %d 個, 段差 %.5f m, 重なり 0 m^2"
          % (west, east, east, len(on), step))
    return {"seam_x": east, "band_west_x": west, "seam_verts": len(on),
            "seam_step_m": round(step, 5)}


def check_no_down_faces(obj, max_edge=ground.BAND_MAX_EDGE):
    """地面に下向きの水平面が無いこと／三角形の辺が長すぎないこと（build_campus と同じ #30 対策）。"""
    me = obj.data
    down = [p for p in me.polygons if p.normal.z < -0.99]
    if down:
        c = down[0].center
        raise RuntimeError("[route] %s に下向きの水平面が %d 枚（例 @ (%.1f, %.1f, %.3f)）"
                           % (obj.name, len(down), c.x, c.y, c.z))
    me.calc_loop_triangles()
    longest = 0.0
    for t in me.loop_triangles:
        a, b, c = (me.vertices[i].co for i in t.vertices)
        longest = max(longest, (a - b).length, (b - c).length, (c - a).length)
    if longest > max_edge:
        raise RuntimeError("[route] %s に %.1f m の辺（上限 %.0f m）" % (obj.name, longest, max_edge))
    print("[route] %s ok: 下向きの水平面 0 枚, 最長の辺 %.1f m" % (obj.name, longest))
    return round(longest, 2)


def open_edge_length(obj):
    """1 枚の面にしか使われていない辺（＝穴の縁）の総延長。閉じた立体なら 0 m。

    #45 の再発防止。厚み 0 の板を 1 枚置くと、その 4 辺がそのまま穴の縁として出てくる。"""
    me = obj.data
    count = {}
    for poly in me.polygons:
        vs = list(poly.vertices)
        for i in range(len(vs)):
            k = (min(vs[i], vs[(i + 1) % len(vs)]), max(vs[i], vs[(i + 1) % len(vs)]))
            count[k] = count.get(k, 0) + 1
    total = 0.0
    n = 0
    for (a, b), c in count.items():
        if c == 1:
            total += (me.vertices[a].co - me.vertices[b].co).length
            n += 1
    return total, n


def check_closed(objs, skip=()):
    """当たり判定に使う立体が全部閉じていること。穴の縁の総延長が 0 m でなければ止める。

    skip には寮の外観を渡す。寮の外皮は facade.add_facade の窓割りと窓ガラスの板で、閉じていない。
    閉じているのは 1 階の基壇・2 階以上の本体・角の階段室・屋上スラブ（どれも上下に蓋つきの
    add_prism）で、屋内へ素通りしないのはそちらが担保する。build_campus.py も 9 棟に閉じ検査を掛けていない。外観そのものの検査は
    build_dorm.py（footprint 一致・玄関の向き・穴の幅 MAX_GAP）が持っている。
    """
    report = {}
    bad = []
    for o in objs:
        total, n = open_edge_length(o)
        report[o.name] = {"open_edges": n, "open_edge_m": round(total, 3),
                          "checked": o.name not in skip}
        if n and o.name not in skip:
            bad.append("%s: 穴の縁 %d 本 / %.2f m" % (o.name, n, total))
    if bad:
        raise RuntimeError("[route] 閉じていない立体があります（#45）: " + " / ".join(bad))
    print("[route] closure ok: %s いずれも穴の縁 0 本 / 0.000 m"
          % ", ".join(sorted(k for k in report if report[k]["checked"])))
    if skip:
        print("[route] closure skip: %s（外皮は窓割りの板。閉じているのは基壇・本体・階段室・屋上スラブ）"
              % ", ".join(sorted(skip)))
    return report


def check_apron(dr, kept, margin=0.10):
    """寮の玄関の石張り（天端 0.12 m の段差）が車道に乗り上げていないこと。

    entrances.STANDARD の APRON_D = 7.0 をそのまま使うと、この玄関は前面道路へ
    3.72 m 乗り上げる。kcd_route/dorm.py の DOOR_APRON_D で寮だけ縮めてあるので、
    そこが戻されたらここで止める。
    """
    o, n, t = dr["origin"], dr["n"], dr["t"]
    hs, d1 = dr["APRON_S"], dr["APRON_D"]
    segs = []
    for r in kept:
        hw = ground.road_width(r) * 0.5
        for i in range(len(r["points"]) - 1):
            a, b = r["points"][i], r["points"][i + 1]
            if ground.dist_point_segment(o, a, b) < 60.0:
                segs.append((a, b, hw, r["osm_id"]))
    worst, who = 0.0, None
    for i in range(35):
        s = -hs + 2.0 * hs * i / 34.0
        for j in range(41):
            d = -0.25 + (d1 + 0.25) * j / 40.0
            p = (o[0] + t[0] * s + n[0] * d, o[1] + t[1] * s + n[1] * d)
            for a, b, hw, oid in segs:
                bite = hw - ground.dist_point_segment(p, a, b)
                if bite > worst:
                    worst, who = bite, oid
    out = {"apron_d_m": round(d1, 3), "apron_s_m": round(hs, 3),
           "road_bite_m": round(worst, 3), "road": who}
    if worst > margin:
        raise RuntimeError("[route] 寮の玄関の石張りが車道 osm %s に %.2f m 乗り上げている。"
                           "kcd_route/dorm.py の DOOR_APRON_D を小さくすること" % (who, worst))
    print("[route] apron ok: 奥行き %.2f m、車道への食い込み %.2f m" % (d1, worst))
    return out


# --------------------------------------------------------------------------- #
#  本体
# --------------------------------------------------------------------------- #
def tune_window_spacing(houses, keeps, budget):
    """窓の数が budget に収まる最小の間隔（＝いちばん密）を選ぶ。"""
    best = None
    for spacing in (2.4, 2.8, 3.2, 3.6, 4.2, 5.0, 6.0, 8.0, 12.0):
        n = sum(props.window_count(b, spacing, keeps[b["id"]]) for b in houses)
        best = (spacing, n)
        if n <= budget:
            break
    return best


def build_route(data, args):
    coll = bpy.context.scene.collection
    kept = ground.pick_roads(data)
    houses = ground.pick_houses(data)
    stats = {}

    # --- 地面の帯 + 道路（campus.fbx の site_ground と同じ作りで 1 つにまとめる）---
    gmb = MeshBuilder("route_ground")
    stats["band_area_m2"] = round(ground.build_band(gmb), 1)
    stats["roads"] = roads.build_roads(gmb, kept)
    n_cut, n_faces = gmb.split_by_grid(ground.BAND_GRID)
    print("[route] split %d faces by %.0f m grid -> %d faces" % (n_cut, ground.BAND_GRID, n_faces))
    g_obj = gmb.to_object(coll)

    # --- 沿道の建物 ---
    # 窓は道から見える面にだけ貼る（裏側は見えないので三角形の無駄）
    refs = [r["points"] for r in kept]
    keeps = {b["id"]: props.facing_edges(b, refs) for b in houses}
    spacing, n_win = tune_window_spacing(houses, keeps, args.window_budget)
    stats["window_edges"] = {"total": sum(len(ground.dedup(b["footprint"])) for b in houses),
                             "facing_road": sum(len(v) for v in keeps.values())}
    hmb = MeshBuilder("bld_route_background")
    stats["houses"] = props.build_houses(hmb, houses, spacing, keeps)
    h_obj = hmb.to_object(coll)

    # --- 塀・ガードレール・門柱 ---
    fmb = MeshBuilder("bld_route_fence")
    stats["fence"] = props.build_fence(fmb, ground.ANNEX, ground.GATE_Z, ground.WALL_X,
                                       kept, houses, ground.road_width)
    f_obj = fmb.to_object(coll)
    enc = props.check_enclosure(ground.ANNEX, ground.GATE_Z, ground.WALL_X,
                                kept, houses, ground.road_width)
    if enc["worst_open_m"] >= props.PLAYER_DIAMETER:
        raise RuntimeError("[route] 輪に %.2f m の隙間がある（%s）。プレイヤー直径 %.2f m より広い"
                           % (enc["worst_open_m"], enc["worst_open_at"], props.PLAYER_DIAMETER))
    print("[route] enclosure ok: 塞がっていない所の最大 %.2f m（通れる幅 %.2f m 未満）, 合計 %.2f m"
          % (enc["worst_open_m"], props.PLAYER_DIAMETER, enc["open_total_m"]))
    stats["enclosure"] = enc

    # --- 寮の外観 (#41) ---
    # build_dorm.py は「外観は route.fbx に混ぜる前提なので既定では FBX を書き出さない」と
    # 書いてあるのに、ここが build_exterior を呼んでいなかった。呼ばないと回廊の突き当たりに
    # 建物が無く、DormEntrance のトリガーだけが草の上に浮く。
    d_shell, d_trim, stats["dorm"] = dorm_mod.build_exterior(ground.dorm(data))
    d_objs = [d_shell.to_object(coll), d_trim.to_object(coll)]
    stats["dorm_objects"] = [o.name for o in d_objs]
    stats["apron"] = check_apron(stats["dorm"]["door"], kept)

    # --- 街灯 ---
    solids = [h_obj, f_obj] + d_objs
    if args.lamps:
        lmb = MeshBuilder("route_props_lamp")
        n = 0
        for r in kept:
            if r["kind"] == "tertiary" and ground.polyline_length(r["points"]) > 40.0:
                n += props.place_lamps(lmb, r)
        stats["lamps"] = n
        if n:
            solids.append(lmb.to_object(coll))
    stats["window_spacing_m"] = spacing
    stats["window_estimate"] = n_win
    return g_obj, solids, stats


def render_previews(out_dir, engine_pref):
    engines = []
    if engine_pref in ("auto", "eevee"):
        engines.append("BLENDER_EEVEE_NEXT")
    if engine_pref in ("auto", "workbench"):
        engines.append("BLENDER_WORKBENCH")
    cams = {name: render.make_camera("cam_" + name, eye, tgt, lens=lens)
            for name, eye, tgt, lens in CAM_SPECS}
    for engine in engines:
        try:
            render.setup_world(engine)
            ok = True
            for name, cam in cams.items():
                if not render.render_to(cam, os.path.join(out_dir, "%s.png" % name)):
                    ok = False
                    break
            if ok:
                print("[preview] engine=%s -> %s" % (engine, out_dir))
                return engine
        except Exception as exc:  # noqa: BLE001
            print("[preview] %s failed: %s" % (engine, exc))
    print("[preview] レンダに失敗しました")
    return None


def sidecar(data, stats, extra):
    """Unity の RouteStage.cs が読む寸法。ここと Unity で数字を二重管理しないための橋。

    鍵の名前と型は Runtime/World/DormRoute.cs の JsonShape に合わせる。JsonUtility は
    平たい構造しか読まないので door / centroid / annex は数の配列で書く。知らない鍵は
    黙って飛ばされるので、人が読むぶんは notes にまとめておく。"""
    dm = ground.dorm(data)
    ent = dm.get("entrance") or {}
    door = ent.get("point") or dm.get("centroid")
    nw = [r for r in data["routes"] if r["key"] == "nw_gate"][0]
    x0, z0, x1, z1 = ground.ANNEX
    return {
        # ---- DormRoute.JsonShape が読む鍵。名前も型も変えないこと ----
        "door": [round(door[0], 3), round(door[1], 3)],
        "door_bearing": round(float(ent.get("facing_bearing") or 0.0), 2),
        "centroid": [round(dm["centroid"][0], 3), round(dm["centroid"][1], 3)],
        "gate_z": round(extra["gate_crossing_z"], 2),
        "gate_half_width": ground.GATE_HALF,
        "annex": [x0, z0, x1, z1],
        # ---- 以下は人が読むだけ（JsonUtility は無視する）----
        "notes": {
            "source": "blender/build_route.py",
            "issue": 41,
            "band": {"x0": ground.BAND[0], "z0": ground.BAND[1],
                     "x1": ground.BAND[2], "z1": ground.BAND[3],
                     "note": "x1 は既存の site_ground の西端ちょうど。地面は重ねていない。"},
            "gate_pillars_z": [round(ground.GATE_Z[0], 2), round(ground.GATE_Z[1], 2)],
            "dormitory": {"id": dm["id"], "osm_id": dm["osm_id"],
                          "display": dm.get("display"),
                          "height": dm.get("height"), "levels": dm.get("levels"),
                          "footprint": [[round(p[0], 3), round(p[1], 3)] for p in dm["footprint"]],
                          "note": "外観は bld_dorm / bld_dorm_trim としてこの FBX に入っている。"
                                  "屋内だけ Assets/Models/Interiors/dorm.fbx（build_dorm.py）。"},
            "route_nw_gate": [[round(p[0], 3), round(p[1], 3)] for p in nw["points"]],
            "objects": extra["objects"],
            "stats": stats,
        },
    }


def main():
    args = parse_args(sys.argv)
    t0 = time.time()

    data = ground.load(args.json)
    info = ground.check(data)

    reset_scene()
    mats.build_all()
    check_layers()

    g_obj, solids, stats = build_route(data, args)
    t_build = time.time()

    checks = {}
    checks.update(check_seam(g_obj))
    checks["ground_longest_edge_m"] = check_no_down_faces(g_obj)
    checks["closure"] = check_closed(solids, skip=set(stats.get("dorm_objects", ())))

    objs = [g_obj] + solids
    per_obj = {}
    tris = 0
    for o in sorted(objs, key=lambda x: -len(x.data.vertices)):
        t = tris_of(o)
        tris += t
        per_obj[o.name] = {"verts": len(o.data.vertices), "faces": len(o.data.polygons), "tris": t}
        print("   %-24s %7d verts  %7d faces  %7d tris"
              % (o.name, len(o.data.vertices), len(o.data.polygons), t))
    if tris > args.budget:
        raise RuntimeError("[route] 三角形が %d（上限 %d）。--window-budget を下げること"
                           % (tris, args.budget))
    print("[route] tris %d / budget %d" % (tris, args.budget))

    fbx = os.path.join(args.out_dir, "%s.fbx" % args.name)
    size = export_fbx(fbx)
    print("[fbx] %s  %.2f MB" % (fbx, size / 1048576.0))

    engine = None
    if args.preview:
        engine = render_previews(args.preview_dir, args.engine)
    t_preview = time.time()

    stats.update({"tris": tris, "budget": args.budget, "per_object": per_obj,
                  "checks": checks, "fbx_bytes": size, "select": info})
    meta = sidecar(data, stats, {"gate_crossing_z": info["gate_crossing_z"],
                                 "objects": list(per_obj)})
    meta_path = os.path.join(args.out_dir, "%s.json" % args.name)
    with open(meta_path, "w", encoding="utf-8") as fp:
        json.dump(meta, fp, ensure_ascii=False, indent=1)
    print("[meta] %s" % meta_path)

    total = time.time()
    print("=" * 64)
    print("roads        : %d 本 敷いた / %d 本 除外（in_campus_json %d + 頂点共有 2 以上 %d）"
          % (info["roads_kept"], info["roads_dropped"],
             info["dropped_in_campus_json"], info["dropped_shared_ge2"]))
    print("road length  : %.1f m" % stats["roads"]["length_m"])
    print("houses       : %d 棟 / 窓 %d 個（間隔 %.1f m, 上限 %d）"
          % (stats["houses"]["houses"], stats["houses"]["windows"],
             stats["window_spacing_m"], args.window_budget))
    print("fence        : 塀 %.1f m (%d 枚) / ガードレール %d / 門柱 %d"
          % (stats["fence"]["wall_m"], stats["fence"]["pieces"],
             stats["fence"]["guardrails"], stats["fence"]["gate_pillars"]))
    print("lamps        : %d" % stats.get("lamps", 0))
    print("enclosure    : 塞がっていない所 最大 %.2f m / 合計 %.2f m（通れる幅 %.2f m 未満）"
          % (stats["enclosure"]["worst_open_m"], stats["enclosure"]["open_total_m"],
             props.PLAYER_DIAMETER))
    print("tris         : %d  (budget %d)" % (tris, args.budget))
    cl = checks["closure"]
    print("closure      : 穴の縁 0 本 / 0.000 m（%s）/ 検査対象外 %s"
          % (", ".join(sorted(k for k in cl if cl[k]["checked"])),
             ", ".join(sorted(k for k in cl if not cl[k]["checked"])) or "なし"))
    print("apron        : 奥行き %.2f m / 車道への食い込み %.2f m"
          % (stats["apron"]["apron_d_m"], stats["apron"]["road_bite_m"]))
    print("seam         : x=%.1f 段差 %.5f m" % (checks["seam_x"], checks["seam_step_m"]))
    print("route.fbx    : %.2f MB" % (size / 1048576.0))
    print("engine       : %s" % engine)
    print("time         : build %.1fs / preview %.1fs / total %.1fs"
          % (t_build - t0, t_preview - t_build, total - t0))
    print("=" * 64)


if __name__ == "__main__":
    main()

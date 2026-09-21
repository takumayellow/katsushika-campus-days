"""プレビュー画像のレンダリング。

Eevee Next → Cycles → Workbench の順にフォールバックする（headless で GPU が
無い環境でも必ず絵が出るようにするため）。
"""

from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

ENGINES = ("BLENDER_EEVEE_NEXT", "CYCLES", "BLENDER_WORKBENCH")


def setup_scene(p: dict, *, floor_z: float = 0.0) -> None:
    scene = bpy.context.scene
    # 立ち姿なので縦長。横長だとキャラが画面中央の細い帯にしか写らない。
    scene.render.resolution_x = 960
    scene.render.resolution_y = 1280
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    # Blender 4.x の既定 (AgX) はハイライトを強く脱色するので、肌とアニメ塗りが
    # 白飛びして見える。トゥーン調の見た目は Standard のほうが実物に近い。
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = -0.05
    except Exception:  # noqa: BLE001
        pass
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg is not None:
        bg.inputs[0].default_value = (0.90, 0.92, 0.95, 1.0)
        # 環境光が強いと肌の明度が飽和して、頬の赤みのような淡い差が消える。
        bg.inputs[1].default_value = 0.50

    # 3点照明。size を大きめに取って影の縁を柔らかくする。
    _light("Key", (2.2, -3.4, 2.9), 97.0, p, size=2.2)
    _light("Fill", (-2.8, -2.2, 1.5), 46.0, p, size=3.0)
    _light("Rim", (0.4, 3.2, 2.6), 64.0, p, size=2.4)
    _floor(p, z=floor_z)


def _floor(p: dict, *, z: float = 0.0):
    """柔らかい影を受ける床。キャラの足元より少し下に置く。"""
    name = "PreviewFloor"
    old = bpy.data.objects.get(name)
    if old is not None:
        bpy.data.objects.remove(old, do_unlink=True)
    h = p["height"]
    r = h * 3.2
    me = bpy.data.meshes.new(name)
    me.from_pydata([(-r, -r, 0.0), (r, -r, 0.0), (r, r, 0.0), (-r, r, 0.0)],
                   [], [(0, 1, 2, 3)])
    me.update()
    mat = bpy.data.materials.get("preview_floor")
    if mat is None:
        mat = bpy.data.materials.new("preview_floor")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf is not None:
            bsdf.inputs["Base Color"].default_value = (0.80, 0.82, 0.86, 1.0)
            bsdf.inputs["Roughness"].default_value = 0.92
            if "Specular IOR Level" in bsdf.inputs:
                bsdf.inputs["Specular IOR Level"].default_value = 0.12
    me.materials.append(mat)
    obj = bpy.data.objects.new(name, me)
    obj.location = (0.0, 0.0, z)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def _light(name: str, loc, power: float, p: dict, *, size: float = 2.0):
    data = bpy.data.lights.new(name, type="AREA")
    data.energy = power
    data.size = size
    obj = bpy.data.objects.new(name, data)
    obj.location = Vector(loc) * (p["height"] / 1.6)
    bpy.context.scene.collection.objects.link(obj)
    _aim(obj, Vector((0.0, 0.0, p["height"] * 0.58)))
    return obj


def _aim(obj, target: Vector) -> None:
    d = target - obj.location
    obj.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def make_camera(p: dict):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.lens = 52.0
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 24.0
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam


def _bounds(objs) -> tuple[float, float, float]:
    """(z_lo, z_hi, 水平半径) をワールド座標で返す。"""
    z_lo, z_hi, rad = 1e9, -1e9, 0.0
    for o in objs:
        for c in o.bound_box:
            v = o.matrix_world @ Vector(c)
            z_lo = min(z_lo, v[2])
            z_hi = max(z_hi, v[2])
            rad = max(rad, math.hypot(v[0], v[1]))
    if z_lo > z_hi:
        return 0.0, 1.0, 0.5
    return z_lo, z_hi, rad


def place_camera(cam, p: dict, angle_deg: float, *, margin: float = 1.07,
                 span: tuple[float, float, float] | None = None) -> None:
    """全身を画面いっぱいに収める。

    身長から逆算すると、下駄・リボン・振り袖のぶんだけ実寸がはみ出して
    足元や髪が切れる。実際のバウンディングボックスから画角を決める。
    """
    h = p["height"]
    if span is None:
        z_lo, z_hi, rad = 0.0, h, h * 0.30
    else:
        z_lo, z_hi, rad = span
    center = Vector((0.0, 0.0, (z_lo + z_hi) * 0.5))
    half_v = (z_hi - z_lo) * 0.5 * margin
    scene = bpy.context.scene
    aspect = scene.render.resolution_x / max(1, scene.render.resolution_y)
    # 横幅の制約（sensor_fit=VERTICAL なので横の画角は aspect 倍）
    half_v = max(half_v, rad * margin / max(aspect, 1e-6))
    fov = 2.0 * math.atan(cam.data.sensor_height * 0.5 / cam.data.lens)
    dist = half_v / math.tan(fov * 0.5)
    a = math.radians(angle_deg)
    cam.location = center + Vector((math.sin(a) * dist, -math.cos(a) * dist,
                                    h * 0.03))
    _aim(cam, center)


def place_face_camera(cam, p: dict, *, fill: float = 0.62) -> None:
    """顔アップ。画面の縦 `fill` を頭の高さが占めるように寄る。

    寄りで鼻が伸びないよう、望遠寄り（85mm 相当）にしてから距離を取る。
    """
    hh = p["head_h"]
    center = Vector((0.0, 0.0, p["z"]["chin"] + hh * 0.46))
    half = hh / fill * 0.5
    fov = 2.0 * math.atan(cam.data.sensor_height * 0.5 / cam.data.lens)
    dist = half / math.tan(fov * 0.5)
    cam.location = center + Vector((0.0, -dist, hh * 0.02))
    _aim(cam, center)


def render_to(path: str) -> str:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene = bpy.context.scene
    last = ""
    for engine in ENGINES:
        try:
            scene.render.engine = engine
        except Exception as exc:  # noqa: BLE001
            last = f"{engine}: {exc}"
            continue
        if engine == "CYCLES":
            scene.cycles.samples = 48
            scene.cycles.use_denoising = True
        if engine == "BLENDER_WORKBENCH":
            sh = scene.display.shading
            sh.light = "STUDIO"
            sh.color_type = "MATERIAL"
            sh.show_shadows = True
            sh.show_cavity = True
        scene.render.filepath = path
        try:
            bpy.ops.render.render(write_still=True)
        except Exception as exc:  # noqa: BLE001
            last = f"{engine}: {exc}"
            continue
        if os.path.exists(path) and os.path.getsize(path) > 0:
            return engine
    raise RuntimeError(f"レンダリングに失敗しました: {last}")


def _render_action(arm, action, cam, p, span, frac: float, ang: float,
                   path: str) -> str:
    """Action の frac 位置のフレームを ang 方向から描く。描いたら Action を外す。"""
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    try:
        arm.animation_data.action_slot = arm.animation_data.action_suitable_slots[0]
    except Exception:  # noqa: BLE001 - 4.3 以前はスロットが無い
        pass
    lo, hi = action.frame_range
    bpy.context.scene.frame_set(int(lo + (hi - lo) * frac))
    place_camera(cam, p, ang, span=span)
    render_to(path)
    arm.animation_data.action = None
    bpy.context.scene.frame_set(1)
    return path


def render_previews(p: dict, out_dir: str, *, arm=None, walk_action=None,
                    obj=None, idle_action=None) -> dict:
    """front / side / back / turn / face（＋あれば walk / idle）を書き出す。"""
    floor_z = 0.0
    if obj is not None:
        try:
            floor_z = min(float((obj.matrix_world @ Vector(c))[2])
                          for c in obj.bound_box) - p["height"] * 0.004
        except Exception:  # noqa: BLE001
            floor_z = 0.0
    setup_scene(p, floor_z=floor_z)
    cam = make_camera(p)
    made: dict[str, str] = {}
    cid = p["id"]
    engine = ""

    outs = [o for o in bpy.context.scene.objects
            if o.type == "MESH" and o.name.endswith("_outline")]
    shown = [o for o in bpy.context.scene.objects
             if o.type == "MESH" and o.name != "PreviewFloor"]
    span = _bounds(shown) if shown else None
    for tag, ang in (("front", 0.0), ("side", 90.0), ("back", 180.0),
                     ("turn", 45.0)):
        place_camera(cam, p, ang, span=span)
        path = os.path.join(out_dir, f"{cid}_{tag}.png")
        eng = render_to(path)
        if not engine:
            engine = eng
            # 輪郭シェルは背面カリング前提。Eevee 以外だと本体を黒く覆うので隠す。
            if eng != "BLENDER_EEVEE_NEXT" and outs:
                for o in outs:
                    o.hide_render = True
                render_to(path)
        made[tag] = path

    if arm is not None and walk_action is not None:
        made["walk"] = _render_action(arm, walk_action, cam, p, span, 0.28, 22.0,
                                      os.path.join(out_dir, f"{cid}_walk.png"))
    if arm is not None and idle_action is not None:
        # ゲーム中に一番長く見る姿勢。腕が体側に下りているかをここで確かめる。
        made["idle"] = _render_action(arm, idle_action, cam, p, span, 0.0, 0.0,
                                      os.path.join(out_dir, f"{cid}_idle.png"))

    # 顔アップ（正方形・望遠寄り）
    scene = bpy.context.scene
    res = (scene.render.resolution_x, scene.render.resolution_y)
    lens = cam.data.lens
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 1280
    cam.data.lens = 85.0
    place_face_camera(cam, p)
    face = os.path.join(out_dir, f"{cid}_face.png")
    render_to(face)
    made["face"] = face
    scene.render.resolution_x, scene.render.resolution_y = res
    cam.data.lens = lens

    made["engine"] = engine
    return made

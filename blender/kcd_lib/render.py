"""プレビュー用のライト・カメラ・レンダ設定。"""

import math
import os

import bpy
from mathutils import Vector


def setup_world(engine="BLENDER_EEVEE_NEXT", sky=(0.42, 0.60, 0.85)):
    scene = bpy.context.scene
    scene.render.engine = engine
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    # AgX だと全体が灰色に寝てしまうので、色をそのまま出す
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = 0.0
        scene.view_settings.gamma = 1.0
    except Exception:
        pass

    world = bpy.data.worlds.get("KCD_World") or bpy.data.worlds.new("KCD_World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (sky[0], sky[1], sky[2], 1.0)
        bg.inputs[1].default_value = 0.45
    scene.world = world

    if engine.startswith("BLENDER_EEVEE"):
        ee = scene.eevee
        for attr, val in (("taa_render_samples", 24), ("use_gtao", True),
                          ("use_shadows", True), ("use_raytracing", False)):
            if hasattr(ee, attr):
                try:
                    setattr(ee, attr, val)
                except Exception:
                    pass
    elif engine == "BLENDER_WORKBENCH":
        sh = scene.display.shading
        sh.light = "STUDIO"
        sh.color_type = "MATERIAL"
        sh.show_shadows = True
        sh.show_cavity = True
        scene.display.render_aa = "8"

    sun = bpy.data.objects.get("KCD_Sun")
    if sun is None:
        data = bpy.data.lights.new("KCD_Sun", type="SUN")
        sun = bpy.data.objects.new("KCD_Sun", data)
        scene.collection.objects.link(sun)
    sun.data.energy = 3.0
    sun.data.angle = math.radians(1.5)
    sun.data.color = (1.0, 0.96, 0.88)
    sun.location = (0.0, 0.0, 300.0)
    sun.rotation_euler = (math.radians(48.0), 0.0, math.radians(-125.0))
    return sun


def make_camera(name, loc, target, lens=35.0):
    scene = bpy.context.scene
    cam = bpy.data.objects.get(name)
    if cam is None:
        data = bpy.data.cameras.new(name)
        cam = bpy.data.objects.new(name, data)
        scene.collection.objects.link(cam)
    cam.data.lens = lens
    cam.data.clip_start = 0.1
    cam.data.clip_end = 4000.0
    cam.location = Vector(loc)
    direction = Vector(target) - Vector(loc)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return cam


def render_to(cam, path):
    scene = bpy.context.scene
    scene.camera = cam
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return os.path.exists(path) and os.path.getsize(path) > 0

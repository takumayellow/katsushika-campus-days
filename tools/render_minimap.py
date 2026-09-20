"""Render an orthographic top-down minimap of the campus (campus.fbx + trees.fbx).
Run: blender -b --python tools/render_minimap.py -- [--size 2048]
Writes Assets/Textures/minimap.png and minimap.json (world bounds for UV mapping)."""
import bpy, os, sys, json, math

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Models", "Campus")
OUT_DIR = os.path.join(ROOT, "unity", "KatsushikaCampusDays", "Assets", "Textures")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
size = int(argv[argv.index("--size") + 1]) if "--size" in argv else 2048

bpy.ops.wm.read_homefile(use_empty=True)
for f in ("campus.fbx", "trees.fbx"):
    bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, f))

xs, ys = [], []
for o in bpy.context.scene.objects:
    if o.type != "MESH" or o.name in ("bld_background", "site_ground"):
        continue
    for c in o.bound_box:
        v = o.matrix_world @ __import__("mathutils").Vector(c)
        xs.append(v.x); ys.append(v.y)
cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
half = max(max(xs) - min(xs), max(ys) - min(ys)) / 2 * 1.12

scene = bpy.context.scene
cam_data = bpy.data.cameras.new("MiniCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = half * 2
cam_data.clip_end = 1000
cam = bpy.data.objects.new("MiniCam", cam_data)
cam.location = (cx, cy, 300)
cam.rotation_euler = (0, 0, 0)
scene.collection.objects.link(cam)
scene.camera = cam

sun_data = bpy.data.lights.new("Sun", "SUN")
sun_data.energy = 4.0
sun_data.use_shadow = False
sun = bpy.data.objects.new("Sun", sun_data)
sun.rotation_euler = (math.radians(35), 0, math.radians(30))
scene.collection.objects.link(sun)
world = bpy.data.worlds.new("W"); scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.86, 0.88, 0.84, 1.0)
bg.inputs[1].default_value = 1.0

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = scene.render.resolution_y = size
scene.render.resolution_percentage = 100
scene.render.film_transparent = False
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
os.makedirs(OUT_DIR, exist_ok=True)
scene.render.filepath = os.path.join(OUT_DIR, "minimap.png")
bpy.ops.render.render(write_still=True)

# Blender FBX import maps Unity (x, y, z) -> Blender (x, z, -y)?  With axis_forward=-Z/axis_up=Y export,
# Blender X = Unity X and Blender Y = Unity Z.  Record bounds in Unity terms.
meta = {"center_x": cx, "center_z": cy, "half_extent": half, "size_px": size,
        "note": "u = (x - center_x)/(2*half) + 0.5, v = (z - center_z)/(2*half) + 0.5 (v up = north)"}
with open(os.path.join(OUT_DIR, "minimap.json"), "w", encoding="utf-8") as fh:
    json.dump(meta, fh, indent=2)
print("MINIMAP_OK", meta)

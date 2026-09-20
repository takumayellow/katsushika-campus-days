import bpy, os
root = r"C:\Users\takum\dev\katsushika-campus-days\unity\KatsushikaCampusDays\Assets\Models\Campus"
bpy.ops.wm.read_homefile(use_empty=True)
for f in ("campus.fbx", "trees.fbx"):
    bpy.ops.import_scene.fbx(filepath=os.path.join(root, f))
for a in bpy.context.screen.areas:
    if a.type == 'VIEW_3D':
        for s in a.spaces:
            if s.type == 'VIEW_3D':
                s.shading.type = 'MATERIAL'
                s.clip_end = 5000

import bpy, os, sys
fbx = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_homefile(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
for a in bpy.context.screen.areas:
    if a.type == 'VIEW_3D':
        for s in a.spaces:
            if s.type == 'VIEW_3D':
                s.shading.type = 'MATERIAL'
bpy.context.scene.frame_end = 60
bpy.ops.screen.animation_play()

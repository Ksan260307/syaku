"""
forest_kit.blend の各アセットを Workbench（頂点カラー表示）でレンダリングしてプレビュー画像を作る。
  blender -b blender/forest_kit.blend --python blender/scripts/render_previews.py -- <out_dir> [name_filter]
"""
import bpy
import math
import os
import sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = argv[0] if argv else os.path.join(os.path.dirname(bpy.data.filepath), "previews")
name_filter = argv[1].split(",") if len(argv) > 1 else None
os.makedirs(out_dir, exist_ok=True)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "VERTEX"
scene.display.shading.show_cavity = False
scene.display.shading.show_object_outline = True
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.film_transparent = False
try:
    scene.world.color = (0.75, 0.82, 0.9)
except Exception:
    world = bpy.data.worlds.new("W")
    scene.world = world
    world.color = (0.75, 0.82, 0.9)

cam_data = bpy.data.cameras.new("PreviewCam")
cam = bpy.data.objects.new("PreviewCam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.lens = 50

meshes = [o for o in scene.objects if o.type == "MESH"]
for ob in meshes:
    if name_filter and not any(f in ob.name for f in name_filter):
        continue
    for o in meshes:
        o.hide_render = (o != ob)
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    center = (mn + mx) / 2
    size = (mx - mn).length
    if ob.name in ("GreatTree", "BgTrunk_A", "BgTrunk_B"):
        center.z = mn.z + 25
        size = 120
    d = Vector((1.0, -1.4, 0.9)).normalized()
    cam.location = center + d * size * 1.25
    look = center - cam.location
    cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
    cam_data.clip_end = size * 10
    scene.render.filepath = os.path.join(out_dir, ob.name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", ob.name)

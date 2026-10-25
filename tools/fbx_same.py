"""FBX の中身（頂点・面・色・法線）が同じかを、Blender で読みこんでくらべる。
blender -b --factory-startup --python fbx_same.py -- LISTFILE OUTFILE
LISTFILE の各行: old_path|new_path|repo_path 。OUTFILE に、同じなら "SAME repo_path"、ちがえば "DIFF repo_path" を書く"""
import sys, hashlib
import bpy
args = sys.argv[sys.argv.index("--") + 1:]
lines = [l.strip() for l in open(args[0], encoding="utf-8") if l.strip()]

def sig(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    h = hashlib.sha1()
    for ob in sorted([o for o in bpy.context.scene.objects if o.type == "MESH"], key=lambda o: o.name):
        me = ob.data
        h.update(ob.name.encode())
        for v in me.vertices:
            h.update(("%.5f %.5f %.5f" % tuple(v.co)).encode())
        for p in me.polygons:
            h.update(" ".join(str(i) for i in p.vertices).encode())
        for ca in me.color_attributes:
            for d in ca.data:
                c = d.color
                h.update(("%.3f %.3f %.3f %.3f" % (c[0], c[1], c[2], c[3])).encode())
        for l in me.loops:
            h.update(("%.3f %.3f %.3f" % tuple(l.normal)).encode())
        for uv in me.uv_layers:
            for d in uv.data:
                h.update(("%.4f %.4f" % tuple(d.uv)).encode())
        h.update(("%.4f %.4f %.4f" % tuple(ob.location)).encode())
        h.update(("%.4f %.4f %.4f" % tuple(ob.rotation_euler)).encode())
        h.update(("%.4f %.4f %.4f" % tuple(ob.scale)).encode())
    return h.hexdigest()

with open(args[1], "w", encoding="utf-8") as out:
    for l in lines:
        old, new, repo = l.split("|")
        try:
            same = sig(old) == sig(new)
        except Exception as e:
            same = False
        out.write(("SAME " if same else "DIFF ") + repo + "\n")

"""
海岸のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_coast_kit.py
失敗すると終了コード 1 を返す。
"""
import os
import sys
import unittest

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_coast_kit as coast  # noqa: E402
from parts_check import floating_parts, open_ends  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def size(ob):
    ps = [v.co for v in ob.data.vertices]
    lo = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    hi = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    return lo, hi


class CoastKitTests(unittest.TestCase):
    def setUp(self):
        reset()


    def test_parts_are_joined_without_open_ends(self):
        # 筒の口があいたまま見える所や、宙に浮いた部品がない（前は、ぞうりの鼻緒のはしの口が見えていた）
        ob = coast.make_sandal()
        self.assertEqual(open_ends(ob, 0.1), [], "Coast_Sandal に、口があいたまま見える筒")
        self.assertEqual(floating_parts(ob, 0.02), [], "Coast_Sandal に、浮いた部品")
    def test_every_asset_builds_with_vertex_colors(self):
        names = set()
        for mk in coast.MAKERS:
            ob = mk()
            names.add(ob.name)
            self.assertTrue(ob.name.startswith("Coast_"), ob.name)
            self.assertGreater(len(ob.data.polygons), 4, ob.name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, f"{ob.name} に頂点カラーがない")
            self.assertEqual(len(col.data), len(ob.data.loops), ob.name)
        for need in ("Coast_Conch", "Coast_Driftwood", "Coast_Cape", "Coast_Reef_A", "Coast_Sandal", "Coast_Float", "Coast_Burrow",
                     "Coast_Lighthouse", "Coast_Clam", "Coast_Scallop", "Coast_Spiral", "Coast_SeaGlass", "Coast_Grass_A", "Coast_Hirugao",
                     "Coast_Kelp", "Coast_Wrack", "Coast_Islet"):
            self.assertIn(need, names)

    def test_conch_is_big_and_hollow_so_the_worm_can_walk_in(self):
        ob = coast.make_conch()
        lo, hi = size(ob)
        self.assertGreater(hi.x - lo.x, 8.0, "大きな巻き貝（しゃくとりむしの 8 倍より長い）")
        self.assertAlmostEqual(lo.z, -0.35, delta=0.05, msg="いちばん下は、砂に少しうまる")
        # 口の中に、空どうがある：口の外から、殻の中へ向けて光をとばすと、殻の内がわに当たるまで、しばらく何もない
        bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
        center = (lo + hi) / 2
        found_cavity = False
        for ox in (-2.0, -1.0, 0.0, 1.0, 2.0):
            start = Vector((center.x + ox, lo.y - 1.0, lo.z + 1.6))
            hit = bvh.ray_cast(start, Vector((0, 1, 0)), 40.0)
            if hit[0] is None:
                continue
            nxt = bvh.ray_cast(hit[0] + Vector((0, 0.05, 0)), Vector((0, 1, 0)), 40.0)
            if nxt[0] is not None and (nxt[0] - hit[0]).length > 1.2:
                found_cavity = True
        self.assertTrue(found_cavity, "殻の中に、入れるすき間がない")

    def test_reef_rocks_have_barnacles_and_dark_wet_bottoms(self):
        ob = coast.make_reef("Coast_Reef_T", 701, (3.0, 2.4, 1.6))
        col = ob.data.color_attributes["Col"]
        whites = sum(1 for d in col.data if min(d.color[:3]) > 0.6)
        self.assertGreater(whites, 20, "白いフジツボ")

    def test_float_collider_has_the_same_top_and_no_overhang(self):
        # 浮き玉の当たり判定は、見た目の玉と同じてっぺん（しずくがのる）で、下を向いた面がない（玉の下のえぐれから、中へ入りこまない）
        R = coast.FLOAT_R
        col = coast.make_float(False)
        self.assertEqual(col.name, "Coast_Float_Col")
        lo, hi = size(col)
        self.assertAlmostEqual(hi.z, R * 0.85 + R, delta=0.01)
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(col.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        self.assertEqual(sum(1 for e in bm.edges if len(e.link_faces) == 1), 0, "とじた形（SurfaceProbe.InsideSolid が、中かどうか見られる）")
        bm.free()
        for p in col.data.polygons:
            if p.center.z > lo.z + 0.05:
                self.assertGreater(p.normal.z, -1e-3, f"高さ {p.center.z:.2f} に、下を向いた面")
        # 見た目の玉の面は、当たり判定の中か、面の上
        for v in coast.make_float().data.vertices:
            d = v.co - Vector((0, 0, R * 0.85))
            if abs(d.length - R) < 1e-3 and v.co.z < R * 0.85:
                self.assertLess(v.co.xy.length, R + 0.1 + 1e-3)

    def test_cape_collider_is_closed_with_the_same_top_and_no_overhang_at_the_foot(self):
        # 岬の大岩の当たり判定は、とじた形で、平らな上の面は見た目と同じ（しずくがのる）、岩ごとに、いちばん太い所より下に、下を向いた面がない
        import bmesh
        from mathutils.bvhtree import BVHTree
        col, vis = coast.make_cape(False), coast.make_cape()
        bm = bmesh.new()
        bm.from_mesh(col.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        self.assertEqual(sum(1 for e in bm.edges if len(e.link_faces) == 1), 0, "とじた形")
        bm.free()
        dg = bpy.context.evaluated_depsgraph_get()
        tv, tc = BVHTree.FromObject(vis, dg), BVHTree.FromObject(col, dg)
        for x, y in ((0.0, 0.0), (1.2, -0.4), (-1.0, 1.0)):
            a = tv.ray_cast(Vector((x, y, 20.0)), Vector((0, 0, -1)), 40.0)[0]
            b = tc.ray_cast(Vector((x, y, 20.0)), Vector((0, 0, -1)), 40.0)[0]
            self.assertAlmostEqual(a.z, b.z, delta=0.03, msg=f"平らな上の面 ({x}, {y})")
        for hull, skirt in coast.cape_col_hulls():
            for fc in hull.F:
                ps = [hull.V[i] for i in fc]
                if max(p.z for p in ps) > skirt + 1e-4 or min(p.z for p in ps) < min(p.z for p in hull.V) + 1e-4:
                    continue
                n = Vector((0, 0, 0))
                for i in range(len(ps)):
                    n += ps[i].cross(ps[(i + 1) % len(ps)])
                self.assertGreater(n.normalized().z, -1e-3, f"高さ {ps[0].z:.2f} に、下を向いた面")

    def test_loose_shells_are_small_enough_to_push(self):
        for mk in (coast.make_clam, coast.make_scallop, coast.make_spiral_shell, coast.make_sea_glass):
            ob = mk()
            lo, hi = size(ob)
            self.assertLess(max(hi.x - lo.x, hi.y - lo.y), 1.6, ob.name)
            self.assertGreaterEqual(lo.z, -0.12, ob.name + "（地面の上に置ける）")

    def test_lighthouse_is_tall_with_a_red_band(self):
        ob = coast.make_lighthouse()
        lo, hi = size(ob)
        self.assertGreater(hi.z, 25.0)
        col = ob.data.color_attributes["Col"]
        reds = sum(1 for d in col.data if d.color[0] > 0.4 and d.color[1] < 0.15)
        self.assertGreater(reds, 20, "赤い帯")


if __name__ == "__main__":
    r = unittest.main(argv=["x"], exit=False, verbosity=2)
    sys.exit(0 if r.result.wasSuccessful() else 1)

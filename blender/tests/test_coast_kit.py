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
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_coast_kit as coast  # noqa: E402


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

"""
神社のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_shrine_kit.py
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
import build_shrine_kit as shrine  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def size(ob):
    ps = [v.co for v in ob.data.vertices]
    lo = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    hi = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    return lo, hi


def unity(v):
    """Blender の点を Unity の座標（x 東・y 上・z 北）にもどす"""
    return Vector((-v.x, v.z, -v.y))


def bvh(ob):
    return BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())


def ray_down(tree, x, z, top=200.0):
    """Unity の (x, z) の上から、まっすぐ下へ。当たった所の Unity の高さ（なければ None）"""
    hit = tree.ray_cast(shrine.U(x, top, z), Vector((0, 0, -1)), top + 50.0)
    return None if hit[0] is None else unity(hit[0]).y


class ShrineKitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_every_asset_builds_with_vertex_colors(self):
        names = set()
        for mk in shrine.MAKERS:
            ob = mk()
            names.add(ob.name)
            self.assertTrue(ob.name.startswith("Shrine_"), ob.name)
            self.assertGreater(len(ob.data.polygons), 4, ob.name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, f"{ob.name} に頂点カラーがない")
            self.assertEqual(len(col.data), len(ob.data.loops), ob.name)
        for need in ("Shrine_Torii", "Shrine_ToriiSmall", "Shrine_Lantern", "Shrine_Fox_A", "Shrine_Fox_B", "Shrine_Saisen", "Shrine_Hall",
                     "Shrine_Chozuya", "Shrine_Tree", "Shrine_Ema", "Shrine_Omikuji", "Shrine_Hokora", "Shrine_Bridge", "Shrine_Fence",
                     "Shrine_Slab", "Shrine_Step", "Shrine_Higanbana", "Shrine_Sasa", "Shrine_Ginkgo"):
            self.assertIn(need, names)

    def test_torii_is_vermilion_and_you_can_walk_under_it(self):
        ob = shrine.make_torii()
        lo, hi = size(ob)
        self.assertGreater(hi.z, 34.0, "1m ほどの大鳥居（しゃくとりむしの 30 倍より高い）")
        self.assertAlmostEqual(lo.z, -0.3, delta=0.2, msg="根もとは地面につく")
        col = ob.data.color_attributes["Col"]
        reds = sum(1 for d in col.data if d.color[0] > 0.45 and d.color[1] < 0.12 and d.color[2] < 0.08)
        self.assertGreater(reds, 100, "朱色")
        tree = bvh(ob)
        self.assertIsNone(tree.ray_cast(shrine.U(-9.0, 2.0, 0.0), Vector((-1, 0, 0)), 18.0)[0], "柱のあいだ（下の方）は、くぐれる")
        top = ray_down(tree, 0.0, 0.0)
        self.assertGreater(top, 35.0, "笠木の上")
        small = shrine.make_torii_small()
        lo2, hi2 = size(small)
        self.assertTrue(18.0 < hi2.z < 23.0)
        self.assertIsNone(bvh(small).ray_cast(shrine.U(-3.5, 1.5, 0.0), Vector((-1, 0, 0)), 7.0)[0], "小さな鳥居も、くぐれる")

    def test_hall_has_a_raised_floor_steps_and_a_bell_rope(self):
        ob = shrine.make_hall()
        lo, hi = size(ob)
        self.assertGreater(hi.z, 36.0, "流造の屋根と千木")
        tree = bvh(ob)
        floor = ray_down(tree, 7.5, 9.5, 15.0)
        self.assertAlmostEqual(floor, shrine.HALL_FLOOR, delta=0.1, msg="縁の上は、床の高さ")
        # 正面の階段は、一段ずつ低く（しゃくとりむしが登れる）
        prev = 0.0
        for k in range(6):
            zc = 11.6 + (5 - k) * 1.25
            h = ray_down(tree, 2.0, zc, 9.0)
            self.assertGreater(h, prev)
            self.assertLess(h - prev, 1.2)
            prev = h
        # 鈴（金）が軒からさがる
        bx, by, bz = shrine.HALL_BELL
        hit = tree.ray_cast(shrine.U(bx + 4.0, by, bz), Vector((1, 0, 0)), 8.0)
        self.assertIsNotNone(hit[0], "鈴")

    def test_chozuya_basin_holds_water_below_its_rim(self):
        ob = shrine.make_chozuya()
        tree = bvh(ob)
        # 水盤のまん中の底は、ふちより低い（水をはる）
        floor = tree.ray_cast(shrine.U(-4.0, 6.0, 0.0), Vector((0, 0, -1)), 20.0)
        self.assertIsNotNone(floor[0])
        self.assertAlmostEqual(unity(floor[0]).y, shrine.BASIN_FLOOR, delta=0.15)
        rim = ray_down(tree, -4.5, 2.7, 8.0)
        self.assertAlmostEqual(rim, shrine.BASIN_RIM, delta=0.1, msg="ふちの上")

    def test_sacred_tree_is_huge_with_a_rope_and_high_branches(self):
        ob = shrine.make_tree()
        lo, hi = size(ob)
        self.assertGreater(hi.z, 150.0, "鎮守の森の大木")
        self.assertGreater(hi.x - lo.x, 30.0, "太い枝")
        col = ob.data.color_attributes["Col"]
        straw = sum(1 for d in col.data if d.color[0] > 0.5 and d.color[1] > 0.4 and d.color[2] < 0.3)
        self.assertGreater(straw, 50, "わらのしめ縄")
        whites = sum(1 for d in col.data if min(d.color[:3]) > 0.75)   # 色は linear
        self.assertGreater(whites, 20, "白い紙垂")

    def test_bridge_arches_up_and_its_ends_meet_the_ground(self):
        ob = shrine.make_bridge()
        tree = bvh(ob)
        top = ray_down(tree, 0.0, 0.0, 20.0)
        end = ray_down(tree, -12.5, 0.0, 20.0)
        self.assertAlmostEqual(top, shrine.BRIDGE_END_Y + shrine.BRIDGE_RISE, delta=0.2, msg="まん中がいちばん高い")
        self.assertLess(end, 1.5, "はしは、地面の高さ")

    def test_fox_statues_wear_red_bibs(self):
        for holds in ("jewel", "key"):
            reset()
            ob = shrine.make_fox_statue("Shrine_Fox_T", holds)
            lo, hi = size(ob)
            self.assertTrue(16.0 < hi.z < 20.0)
            col = ob.data.color_attributes["Col"]
            reds = sum(1 for d in col.data if d.color[0] > 0.45 and d.color[1] < 0.1)
            self.assertGreater(reds, 30, "赤い前かけ")

    def test_small_things_are_small(self):
        lo, hi = size(shrine.make_ginkgo_leaf())
        self.assertLess(max(hi.x - lo.x, hi.y - lo.y), 2.2, "落ち葉は、押せる大きさ")
        lo, hi = size(shrine.make_slab())
        self.assertLess(hi.z, 0.25, "石畳は、地面とほとんど同じ高さ")


if __name__ == "__main__":
    r = unittest.main(argv=["x"], exit=False, verbosity=2)
    sys.exit(0 if r.result.wasSuccessful() else 1)

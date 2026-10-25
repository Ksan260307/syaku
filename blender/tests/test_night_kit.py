"""
夜の森のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_night_kit.py
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
import build_night_kit as night  # noqa: E402
from parts_check import floating_parts, open_ends  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def size(ob):
    ps = [v.co for v in ob.data.vertices]
    lo = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    hi = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    return lo, hi


def bvh(ob):
    return BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())


class NightKitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_every_asset_builds_with_vertex_colors(self):
        names = set()
        for mk in night.MAKERS:
            reset()
            ob = mk()
            names.add(ob.name)
            self.assertTrue(ob.name.startswith("Night_"), ob.name)
            self.assertGreater(len(ob.data.polygons), 20, ob.name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, ob.name)
            self.assertEqual(len(col.data), len(ob.data.loops), ob.name)
        self.assertEqual(len(names), len(night.MAKERS))

    def test_bamboo_is_tall_with_nodes_and_its_foot_is_buried(self):
        ob = night.make_bamboo()
        lo, hi = size(ob)
        self.assertGreater(hi.z, 40.0, "竹は、とても高い")
        self.assertLess(lo.z, -1.0, "根もとは地面の中")
        # 節は、まわりよりふくらむ
        self.assertGreater(night.bamboo_radius(night.BAMBOO_GAP * 3), night.bamboo_radius(night.BAMBOO_GAP * 3.5) * 1.05)
        shoot = night.make_bamboo_shoot()
        self.assertTrue(3.5 < size(shoot)[1].z < 7.0, "タケノコは、しゃくとりむしより、ずっと大きい")

    def test_logs_lie_along_y_and_their_tops_are_walkable(self):
        for mk, L, R in ((night.make_log_bridge, night.BRIDGE_LEN, night.BRIDGE_R), (night.make_rot_log, night.ROTLOG_LEN, night.ROTLOG_R)):
            reset()
            ob = mk()
            lo, hi = size(ob)
            self.assertGreater(hi.y - lo.y, L * 0.98, ob.name)
            tree = bvh(ob)
            # まん中のあたりの上は、ほぼ平らに歩ける（上からのレイが、丸太の上の面に当たる）
            for y in (-L * 0.3, 0.0, L * 0.3):
                hit = tree.ray_cast(Vector((0.0, y, 20.0)), Vector((0, 0, -1)), 40.0)
                self.assertIsNotNone(hit[0], f"{ob.name} y={y}")
                self.assertGreater(hit[0].z, R * 0.7, ob.name)
                self.assertGreater(hit[1].z, 0.85, f"{ob.name} の上の面は、上を向く")

    def test_trees_have_branches_to_walk_on_and_hang_from(self):
        import math
        roost = night.make_roost_tree()
        tree = bvh(roost)
        tops = 0
        for a, y, L, rise in night.ROOST_BRANCHES:
            d = Vector((math.cos(a), math.sin(a), 0))
            p = d * (L * 0.55)
            # 枝のすぐ上から（上には、葉のかたまりがかぶさっている）
            hit = tree.ray_cast(Vector((p.x, p.y, y + rise + 2.5)), Vector((0, 0, -1)), 30.0)
            if hit[0] is not None and y < hit[0].z < y + rise + 2.0 and hit[1].z > 0.5:
                tops += 1
        self.assertGreaterEqual(tops, 4, "太い枝の上に、のれる")
        lo, hi = size(roost)
        self.assertTrue(30.0 < hi.z < 42.0, hi.z)
        mino = night.make_mino_tree()
        tree = bvh(mino)
        under = 0
        for a, y, L, rise in night.MINO_BRANCHES:
            d = Vector((math.cos(a), math.sin(a), 0))
            for t in (0.4, 0.55, 0.7):
                p = d * (L * t)
                hit = tree.ray_cast(Vector((p.x, p.y, 0.5)), Vector((0, 0, 1)), 30.0)
                if hit[0] is not None and y - 1.5 < hit[0].z < y + rise + 1.0:
                    under += 1
        self.assertGreaterEqual(under, 10, "低い枝の下（ミノムシがぶらさがる所）")

    def test_star_rock_has_a_flat_top(self):
        ob = night.make_star_rock()
        lo, hi = size(ob)
        self.assertGreater(hi.x - lo.x, 12.0, "大きな岩")
        tree = bvh(ob)
        hs = []
        for x, y in ((0, 0), (2, 1), (-2, -1), (1, -2), (-1.5, 2)):
            hit = tree.ray_cast(Vector((x, y, 30.0)), Vector((0, 0, -1)), 60.0)
            self.assertIsNotNone(hit[0])
            hs.append(hit[0].z)
        self.assertLess(max(hs) - min(hs), 0.8, "てっぺんは平ら")
        self.assertLess(lo.z, -1.5, "すそは地面の中")

    def test_parts_are_joined_without_open_ends(self):
        for mk, r, tol in ((night.make_bamboo, 0.06, 0.02), (night.make_bamboo_shoot, 0.06, 0.02), (night.make_log_bridge, 0.1, 0.02),
                           (night.make_rot_log, 0.1, 0.02), (night.make_roost_tree, 0.15, 0.05), (night.make_mino_tree, 0.15, 0.05),
                           (night.make_star_rock, 0.1, 0.02), (night.make_susuki, 0.05, 0.02)):
            reset()
            ob = mk()
            self.assertEqual(open_ends(ob, r), [], f"{ob.name} に、口があいたまま見える筒")
            self.assertEqual(floating_parts(ob, tol), [], f"{ob.name} に、浮いた部品")

    def test_tree_leaves_cover_the_leaf_clumps(self):
        # 葉のかたまりの表面に、1 まいずつの葉（草の材質で重ねる。まるい玉に見せない）。木の形（当たり判定）は、葉のない、前のまま
        for tree_mk, leaf_mk in ((night.make_roost_tree, night.make_roost_leaves), (night.make_mino_tree, night.make_mino_leaves)):
            reset()
            tree = tree_mk()
            leaves = leaf_mk()
            self.assertGreater(len(leaves.data.polygons), 3000, leaves.name)
            tlo, thi = size(tree)
            llo, lhi = size(leaves)
            self.assertLess(abs(lhi.z - thi.z), 3.0, "葉は、かたまりの表面")
            self.assertGreater(llo.z, tlo.z + 5.0, "葉は、高い所（幹のまわりにはない）")
            bvt = bvh(tree)
            sample = [v.co for v in leaves.data.vertices][::97]
            near = sum(1 for co in sample if bvt.find_nearest(co)[3] < 2.5)
            self.assertGreater(near, len(sample) * 0.9, "葉は、かたまりにくっついている")


def main():
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(NightKitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)


if __name__ == "__main__":
    main()

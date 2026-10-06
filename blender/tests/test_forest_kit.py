"""
Blender アセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_forest_kit.py
失敗すると終了コード 1 を返す。
"""
import math
import os
import sys
import traceback
import unittest

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def bounds(ob):
    xs = [v.co.x for v in ob.data.vertices]
    ys = [v.co.y for v in ob.data.vertices]
    zs = [v.co.z for v in ob.data.vertices]
    return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))


def color_attr(ob):
    return ob.data.color_attributes.get("Col")


class KitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_inchworm_has_deform_uvs(self):
        ob = kit.make_inchworm()
        names = [uv.name for uv in ob.data.uv_layers]
        self.assertEqual(names, ["UVMap", "Deform1", "Deform2", "Deform3"])
        (x0, x1), (y0, y1), (z0, z1) = bounds(ob)
        self.assertAlmostEqual(y0, 0.0, delta=0.02, msg="尾は y=0")
        self.assertAlmostEqual(y1, 1.0, delta=0.03, msg="頭は y=1")
        self.assertLess(x1 - x0, 0.2)
        d1 = ob.data.uv_layers["Deform1"].uv
        s_values = [d1[i].vector.x for i in range(len(d1))]
        self.assertAlmostEqual(min(s_values), 0.0, delta=0.03)
        self.assertAlmostEqual(max(s_values), 1.0, delta=0.05)
        # UV2 (断面の位置) と実際の頂点位置が一致
        d2 = ob.data.uv_layers["Deform2"].uv
        loops = ob.data.loops
        for li in range(0, len(loops), 97):
            co = ob.data.vertices[loops[li].vertex_index].co
            self.assertAlmostEqual(d2[li].vector.x, co.x, places=4)
            self.assertAlmostEqual(d2[li].vector.y, co.z, places=4)

    def test_inchworm_normals_in_uv_are_unit(self):
        ob = kit.make_inchworm()
        d1 = ob.data.uv_layers["Deform1"].uv
        d3 = ob.data.uv_layers["Deform3"].uv
        for li in range(0, len(d1), 53):
            n = math.sqrt(d1[li].vector.y ** 2 + d3[li].vector.x ** 2 + d3[li].vector.y ** 2)
            self.assertAlmostEqual(n, 1.0, delta=0.02)

    def test_inchworm_radius_profile(self):
        self.assertEqual(kit.worm_radius(0.0), 0.0)
        mid = kit.worm_radius(0.5)
        self.assertGreater(mid, 0.04)
        self.assertLess(mid, 0.07)
        self.assertGreater(kit.worm_radius(kit.HEAD_C), mid * 0.95, "頭は胴と同じくらいの太さ")

    def test_every_asset_builds_with_vertex_colors(self):
        makers = {
            "rock": lambda: kit.make_rock("R", 1),
            "red": kit.make_mushroom_red,
            "brown": kit.make_mushroom_brown,
            "cluster": kit.make_mushroom_cluster,
            "glow": kit.make_mushroom_glow,
            "stump": kit.make_stump,
            "log": kit.make_hollow_log,
            "twig": lambda: kit.make_twig("T", 1),
            "oak": lambda: kit.make_oak_leaf("O", kit.hexc("#e8892e"), kit.hexc("#b4501f"), 1),
            "maple": lambda: kit.make_maple_leaf("M", kit.hexc("#e2452f"), kit.hexc("#a3262a"), 4),
            "acorn": kit.make_acorn,
            "cap": kit.make_acorn_cap,
            "pine": kit.make_pinecone,
            "grass": lambda: kit.make_grass("G", 1),
            "clover": lambda: kit.make_clover("C", 1),
            "fern": lambda: kit.make_fern("F", 1),
            "daisy": lambda: kit.make_daisy("D", 1),
            "bell": lambda: kit.make_bellflower("B", 2),
            "dandelion": lambda: kit.make_dandelion("Da", 3),
            "puff": lambda: kit.make_dandelion_puff("P", 4),
            "moss": lambda: kit.make_moss("Mo", 1),
            "sprout": lambda: kit.make_sprout("S", 1),
            "berry": lambda: kit.make_strawberry("St", 1),
            "pad": lambda: kit.make_lilypad("L", 1),
            "lily": lambda: kit.make_water_lily("W"),
            "reed": lambda: kit.make_reed("Re", 1),
            "drop": kit.make_dewdrop,
        }
        for name, mk in makers.items():
            ob = mk()
            self.assertGreater(len(ob.data.vertices), 20, name)
            self.assertGreater(len(ob.data.polygons), 10, name)
            self.assertIsNotNone(color_attr(ob), f"{name} に頂点カラーがない")
            self.assertEqual(len(color_attr(ob).data), len(ob.data.loops), name)

    def test_closed_solids_face_outward(self):
        # 閉じた立体は符号付き体積が正（法線が外向き）
        for mk in (kit.make_acorn, kit.make_dewdrop, lambda: kit.make_rock("R", 2)):
            ob = mk()
            me = ob.data
            vol = 0.0
            for p in me.polygons:
                vs = [me.vertices[i].co for i in p.vertices]
                for k in range(1, len(vs) - 1):
                    vol += vs[0].dot(vs[k].cross(vs[k + 1])) / 6.0
            self.assertGreater(vol, 0.0, ob.name)

    def test_great_tree_is_huge_and_mushroom_scale(self):
        tree = kit.make_great_tree()
        (_, _), (_, _), (z0, z1) = bounds(tree)
        self.assertGreater(z1 - z0, 150)
        red = kit.make_mushroom_red()
        (_, _), (_, _), (z0, z1) = bounds(red)
        self.assertAlmostEqual(z1 - z0, 6.8, delta=0.6, msg="赤キノコは体長の約7倍")

    def test_hollow_log_is_hollow(self):
        log = kit.make_hollow_log()
        me = log.data
        # 中心軸付近に頂点がない（空洞）
        near_axis = [v for v in me.vertices if math.hypot(v.co.y, v.co.z) < 3.0 and 5 < v.co.x < 40]
        self.assertEqual(len(near_axis), 0)

    def test_colors_helpers(self):
        self.assertEqual(kit.hexc("#ff0000"), (1.0, 0.0, 0.0, 1.0))
        self.assertEqual(kit.mixc((0, 0, 0, 1), (1, 1, 1, 1), 0.5), (0.5, 0.5, 0.5, 1.0))
        self.assertAlmostEqual(kit.s2l((0.5, 0.5, 0.5, 1))[0], 0.214, places=2)
        self.assertEqual(kit.sstep(0, 1, -1), 0.0)
        self.assertEqual(kit.sstep(0, 1, 2), 1.0)


def main():
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(KitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)


try:
    main()
except SystemExit:
    raise
except Exception:
    traceback.print_exc()
    sys.exit(1)

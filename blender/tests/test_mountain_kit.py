"""
山のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_mountain_kit.py
失敗すると終了コード 1 を返す。
"""
import os
import sys
import unittest

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_mountain_kit as mtn  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def bounds(ob):
    xs = [v.co.x for v in ob.data.vertices]
    ys = [v.co.y for v in ob.data.vertices]
    zs = [v.co.z for v in ob.data.vertices]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


MAKERS = {
    "Mtn_Pine": mtn.make_pine,
    "Mtn_Hut": mtn.make_hut,
    "Mtn_RockArch": mtn.make_rock_arch,
    "Mtn_Sign": mtn.make_sign,
    "Mtn_SummitPost": mtn.make_summit_post,
    "Mtn_Benchmark": mtn.make_benchmark,
    "Mtn_Spout": mtn.make_spout,
    "Mtn_Slab_A": lambda: mtn.make_slab("Mtn_Slab_A", 41, (3.4, 2.8, 1.5)),
    "Mtn_SnowMound_A": lambda: mtn.make_snow_mound("Mtn_SnowMound_A", 51, (3.2, 2.6, 1.3)),
    "Mtn_Haimatsu": mtn.make_haimatsu,
    "Mtn_CloudPuff": mtn.make_cloud_puff,
    "Mtn_Komakusa": lambda: mtn.make_komakusa("Mtn_Komakusa", 61),
    "Mtn_Chinguruma": lambda: mtn.make_chinguruma("Mtn_Chinguruma", 62),
    "Mtn_ChingurumaSeed": lambda: mtn.make_chinguruma_seed("Mtn_ChingurumaSeed", 63),
    "Mtn_Kurumayuri": lambda: mtn.make_kurumayuri("Mtn_Kurumayuri", 64),
    "Mtn_Plank": mtn.make_plank,
    "Mtn_Log": mtn.make_log,
    "Mtn_SignPost": mtn.make_sign_post,
    "Mtn_SignArrow": mtn.make_sign_arrow,
    "Mtn_StepPillar": mtn.make_step_pillar,
}


class MountainKitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_every_asset_builds_with_vertex_colors(self):
        for name, mk in MAKERS.items():
            ob = mk()
            self.assertEqual(ob.name, name)
            self.assertGreater(len(ob.data.polygons), 10, name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, f"{name} に頂点カラーがない")
            self.assertEqual(len(col.data), len(ob.data.loops), name)

    def test_pine_has_climbable_pads_high_up(self):
        ob = mtn.make_pine()
        (_, _, z0), (_, _, z1) = bounds(ob)
        self.assertGreater(z1, 28.0, "大きな松（30 くらい）")
        high = [c for c, r, h in mtn.PINE_PADS if c.z > 6.5]
        self.assertGreaterEqual(len(high), 10, "枝の先に、乗れる葉のかたまりがたくさん")
        self.assertLess(min(c.z for c, r, h in mtn.PINE_PADS), 9.0, "いちばん下の葉のかたまりは、登りやすい高さ")

    def test_hut_roof_and_door(self):
        ob = mtn.make_hut()
        (x0, y0, z0), (x1, y1, z1) = bounds(ob)
        self.assertAlmostEqual(z1, mtn.HUT_RIDGE + 1.4, delta=0.8, msg="屋根のむね（えんとつ）")
        self.assertGreater(x1 - x0, mtn.HUT_W)
        # 妻かべ（三角の所）は、むねのはし（±X）にある：屋根の坂（±Y）をつきぬけない
        gable = [v.co for v in ob.data.vertices if v.co.z > mtn.HUT_WALL + 1.0 and abs(abs(v.co.y) - (mtn.HUT_D / 2 - 0.1)) < 0.05]
        self.assertEqual(len(gable), 0, "屋根の坂の上に、かべの三角がない")

    def test_rock_arch_can_be_walked_through(self):
        ob = mtn.make_rock_arch()
        hole = [v for v in ob.data.vertices if abs(v.co.x) < 1.6 and 0.2 < v.co.z < 4.0]
        self.assertEqual(len(hole), 0, "アーチの下は、くぐれる")
        (_, _, _), (_, _, z1) = bounds(ob)
        self.assertGreater(z1, 9.0, "上に登ると高い")

    def test_slab_top_is_flat(self):
        ob = mtn.make_slab("Mtn_Slab_T", 41, (3.4, 2.8, 1.5))
        (_, _, _), (_, _, z1) = bounds(ob)
        top = [v.co for v in ob.data.vertices if v.co.z > z1 - 0.05]
        self.assertGreater(len(top), 8, "段の上は平ら（乗りやすい）")

    def test_spout_tip_reaches_out(self):
        ob = mtn.make_spout()
        (_, y0, _), (_, _, _) = bounds(ob)
        self.assertAlmostEqual(y0, mtn.SPOUT_TIP.y, delta=0.3, msg="といの先が、いちばん前")

    def test_alpine_flowers_are_small(self):
        for name in ("Mtn_Komakusa", "Mtn_Chinguruma"):
            ob = MAKERS[name]()
            (_, _, z0), (_, _, z1) = bounds(ob)
            self.assertLess(z1 - z0, 3.5, f"{name} は低い花")
        ob = MAKERS["Mtn_Kurumayuri"]()
        (_, _, z0), (_, _, z1) = bounds(ob)
        self.assertGreater(z1 - z0, 4.0, "クルマユリは背が高い")


class MountainPlayHelpersTests(unittest.TestCase):
    """遊びやすさのための物（ふみ板・倒れた木・道しるべ・石段）"""

    def setUp(self):
        reset()

    def test_plank_is_wide_and_flat(self):
        ob = mtn.make_plank()
        (x0, y0, z0), (x1, y1, z1) = bounds(ob)
        self.assertGreaterEqual(x1 - x0, 2.9, "はばが広い（細いと横へ落ちる）")
        self.assertLess(z1 - z0, 0.3, "横木の段がない、平らな板")
        self.assertAlmostEqual(y1, 0.0, delta=0.05, msg="下のはしが原点")
        self.assertAlmostEqual(y0, -mtn.PLANK_LEN, delta=0.05, msg="Unity の +Z（Blender の -Y）へのびる")
        short = mtn.make_plank("Mtn_PlankShort", 6.5, 3.0)
        (sx0, _, _), (sx1, _, _) = bounds(short)
        self.assertGreaterEqual(sx1 - sx0, 2.9)

    def test_step_pillar_has_no_overhang(self):
        ob = mtn.make_step_pillar()
        (_, _, z0), (_, _, z1) = bounds(ob)
        self.assertAlmostEqual(z1, 0.0, delta=0.01, msg="上の面が原点")
        self.assertAlmostEqual(z0, -mtn.STEP_PILLAR_H, delta=0.01)
        for p in ob.data.polygons:
            if -0.95 < p.normal.z < -0.3:
                self.fail("下を向いた横の面（ひさし）がある")
        top = [p for p in ob.data.polygons if p.normal.z > 0.9]
        self.assertGreater(len(top), 5, "上は平ら")

    def test_sign_arrow_points_from_the_post(self):
        ob = mtn.make_sign_arrow()
        (x0, _, _), (x1, _, _) = bounds(ob)
        self.assertGreater(x1, -0.5, "柱のそばから")
        self.assertLess(x0, -4.0, "Blender の -X（Unity の +X）を指す")

    def test_log_is_long_and_leans_from_its_foot(self):
        ob = mtn.make_log()
        (_, y0, _), (_, y1, _) = bounds(ob)
        self.assertGreater(y1 - y0, mtn.LOG_LEN - 0.5)
        self.assertGreater(y1, -1.0, "下のはしが原点のそば")


if __name__ == "__main__":
    res = unittest.main(argv=["x"], exit=False, verbosity=2).result
    sys.exit(0 if res.wasSuccessful() else 1)

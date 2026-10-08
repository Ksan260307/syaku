"""
公園のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_park_kit.py
失敗すると終了コード 1 を返す。
"""
import os
import re
import sys
import unittest

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_park_kit as park  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def bounds(ob):
    xs = [v.co.x for v in ob.data.vertices]
    ys = [v.co.y for v in ob.data.vertices]
    zs = [v.co.z for v in ob.data.vertices]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


MAKERS = {
    "Park_SlideFrame": park.make_slide_frame,
    "Park_SlideRamp": park.make_slide_ramp,
    "Park_SwingFrame": park.make_swing_frame,
    "Park_SwingSeat": park.make_swing_seat,
    "Park_SwingChain": park.make_swing_chain,
    "Park_SeesawBase": park.make_seesaw_base,
    "Park_SeesawPlank": park.make_seesaw_plank,
    "Park_JungleGym": park.make_jungle_gym,
    "Park_SandboxFrame": park.make_sandbox_frame,
    "Park_SandMound": park.make_sand_mound,
    "Park_Bucket": park.make_bucket,
    "Park_Shovel": park.make_shovel,
    "Park_Dokan": park.make_dokan,
    "Park_Bench": park.make_bench,
    "Park_Fountain": park.make_fountain,
    "Park_FlowerBed": park.make_flower_bed,
    "Tulip_Red": lambda: park.make_tulip("Tulip_Red", kit.hexc("#e83a3a"), kit.hexc("#ff8a7a")),
    "Park_Cabbage": park.make_cabbage,
    "Park_Tire_Red": lambda: park.make_tire("Park_Tire_Red", kit.hexc("#e2483a")),
    "Park_Ball": park.make_ball,
    "Park_Fence": park.make_fence,
    "Park_Lamp": park.make_lamp,
    "Park_Kunugi": park.make_kunugi,
}


class ParkKitTests(unittest.TestCase):
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

    def test_slide_platform_and_ramp(self):
        frame = park.make_slide_frame()
        (_, _, z0), (_, _, z1) = bounds(frame)
        self.assertAlmostEqual(z0, 0.0, delta=0.15, msg="足は地面に少しうまる")
        self.assertGreater(z1, park.SLIDE_TOP + 2.5, "台の上に手すり")
        ramp = park.make_slide_ramp()
        (x0, y0, _), (x1, y1, _) = bounds(ramp)
        self.assertGreater(x1 - x0, park.SLIDE_WIDTH, "すべる面の幅")
        self.assertAlmostEqual(y1, park.SLIDE_RAMP_END.y + 0.0, delta=0.4)
        # 坂はすべるのにちょうどよい角度（25〜40 度）
        d = park.SLIDE_RAMP_LOW - park.SLIDE_RAMP_TOP
        import math
        ang = math.degrees(math.atan2(-d.z, d.y))
        self.assertTrue(25.0 < ang < 40.0, ang)

    def test_slide_ramp_matches_the_game(self):
        """Unity の SlideRide の坂の場所は、Blender の形と同じ（Blender (x, y, z) → Unity (-x, z, -y)）"""
        src = open(os.path.join(REPO, "unity", "Assets", "Scripts", "World", "ParkRides.cs"), encoding="utf-8").read()

        def vec(name):
            m = re.search(name + r" = new Vector3\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\)", src)
            self.assertIsNotNone(m, name)
            return tuple(float(g) for g in m.groups())
        for name, b in (("RampTop", park.SLIDE_RAMP_TOP), ("RampLow", park.SLIDE_RAMP_LOW), ("RampEnd", park.SLIDE_RAMP_END)):
            u = vec(name)
            self.assertAlmostEqual(u[0], -b.x, places=3, msg=name)
            self.assertAlmostEqual(u[1], b.z, places=3, msg=name)
            self.assertAlmostEqual(u[2], -b.y, places=3, msg=name)

    def test_swing_chain_reaches_the_seat(self):
        chain = park.make_swing_chain()
        (_, _, z0), (_, _, z1) = bounds(chain)
        self.assertAlmostEqual(z1, 0.0, delta=0.4)
        self.assertAlmostEqual(-z0, park.SWING_CHAIN, delta=0.8, msg="くさりは座板まで")
        frame = park.make_swing_frame()
        (_, _, _), (_, _, top) = bounds(frame)
        self.assertGreater(top, park.SWING_BAR, "くさりをつるす棒の高さ")

    def test_dokan_is_hollow(self):
        ob = park.make_dokan()
        inner = [v.co for v in ob.data.vertices if (v.co.y ** 2 + v.co.z ** 2) ** 0.5 < park.DOKAN_R - 0.3]
        self.assertGreater(len(inner), 20, "土管の中は空いている（内がわの面がある）")

    def test_tulips_are_short_flowers(self):
        ob = park.make_tulip("Tulip_Test", kit.hexc("#e83a3a"), kit.hexc("#ff8a7a"))
        (_, _, z0), (_, _, z1) = bounds(ob)
        self.assertTrue(3.5 < z1 - z0 < 6.5, z1 - z0)


if __name__ == "__main__":
    res = unittest.main(argv=["x"], exit=False, verbosity=2).result
    sys.exit(0 if res.wasSuccessful() else 1)

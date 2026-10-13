"""
きせかえの小物（ぼうし・めがね）のテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_wear_kit.py
失敗すると終了コード 1 を返す。
"""
import os
import sys
import unittest

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_wear_kit as wk  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


EYES = [wk.eye_dir(sx) * 0.055 for sx in (-1, 1)]   # 頭のまん中から見た、目のまん中（make_inchworm と同じ）


class WearKitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_every_item_builds_with_vertex_colors(self):
        names = set()
        for mk in wk.MAKERS:
            ob = mk()
            names.add(ob.name)
            self.assertTrue(ob.name.startswith("Wear_"), ob.name)
            self.assertGreater(len(ob.data.polygons), 20, ob.name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, f"{ob.name} に頂点カラーがない")
            self.assertEqual(len(col.data), len(ob.data.loops), ob.name)
        self.assertEqual(len(wk.HATS), 10, "ぼうし 10 こ")
        self.assertEqual(len(wk.GLASSES), 4, "めがね 4 こ")
        self.assertEqual(set(wk.MAKER_OF), names, "絵の一覧と、作る物が合う")

    def test_hats_sit_on_the_head_and_do_not_cover_the_eyes(self):
        for mk in wk.HATS:
            reset()
            ob = mk()
            vs = [v.co for v in ob.data.vertices]
            top = max(v.z for v in vs)
            self.assertGreater(top, 0.05, f"{ob.name} は頭の上の方（花かんむりは、頭をぐるりとかこむ）")
            self.assertLess(max(v.length for v in vs), 0.17, f"{ob.name} は大きすぎない")
            for e in EYES:
                near = [v for v in vs if (v - e).length < 0.013]
                self.assertEqual(len(near), 0, f"{ob.name} が目にかぶさる")

    def test_glasses_sit_in_front_of_the_eyes(self):
        for mk in wk.GLASSES:
            reset()
            ob = mk()
            vs = [v.co for v in ob.data.vertices]
            for sx, e in zip((-1, 1), EYES):
                d = wk.eye_dir(sx)
                lens = [v for v in vs if (v - d * 0.073).length < 0.03]
                self.assertGreater(len(lens), 20, f"{ob.name} のレンズが目の前にある")
                # レンズのわくは、目より外がわ（目にめりこまない）
                self.assertGreater(min(v.dot(d) for v in lens), 0.055 + 0.005, f"{ob.name} が目にめりこむ")
            self.assertLess(max(v.length for v in vs), 0.12, f"{ob.name} は頭のまわり")

    def test_hair_grows_from_the_top_of_the_head(self):
        ob = wk.make_hair()
        vs = [v.co for v in ob.data.vertices]
        self.assertGreater(max(v.z for v in vs), 0.1, "ニュッと上へ")
        low = min(vs, key=lambda v: v.z)
        self.assertLess((low - wk.HAIR_ROOT).length, 0.012, "付け根は頭のてっぺん（ゆれるときの支点）")

    def test_icons_cover_every_skin(self):
        self.assertEqual(len(wk.COLORS), 16, "いろ 16 しゅ")
        self.assertEqual(len(wk.WEAR_ICONS), 14)
        self.assertEqual(set(wk.WEAR_ICONS.values()), set(wk.MAKER_OF))
        # わかばは、色を変えない
        c = (0.56, 0.82, 0.31, 1.0)
        out = wk.skin_color(c, 0.5, 0.25, **wk.COLORS["wakaba"])
        for i in range(3):
            self.assertAlmostEqual(out[i], c[i], delta=0.01)
        # しましまは、体の前後でこい帯がある・レインボーは、尾と頭で色がちがう
        sh = wk.COLORS["shima"]
        a = wk.skin_color(c, 0.02, 0.25, **sh)
        b = wk.skin_color(c, 0.06, 0.25, **sh)
        self.assertNotAlmostEqual(sum(a[:3]), sum(b[:3]), delta=0.05)
        rb = wk.COLORS["rainbow"]
        t0 = wk.skin_color(c, 0.1, 0.25, **rb)
        t1 = wk.skin_color(c, 0.5, 0.25, **rb)
        self.assertGreater(max(abs(t0[i] - t1[i]) for i in range(3)), 0.2)


if __name__ == "__main__":
    result = unittest.main(argv=[sys.argv[0]], exit=False).result
    sys.exit(0 if result.wasSuccessful() else 1)

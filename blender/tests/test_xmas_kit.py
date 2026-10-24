"""
クリスマス村のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_xmas_kit.py
失敗すると終了コード 1 を返す。
"""
import math
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
import build_xmas_kit as xmas  # noqa: E402
from build_shrine_kit import U, UV  # noqa: E402
from parts_check import floating_parts, open_ends  # noqa: E402

DOWN = U(0, -1, 0)
NORTH = U(0, 0, 1)


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


def unity(pb):
    """Blender の点を Unity の点に"""
    return Vector((-pb.x, pb.z, -pb.y))


def drop(tree, x, z, top=80.0):
    """Unity の (x, z) の上から下へのレイ。(Unity の当たった点, Unity の法線) か None"""
    hit = tree.ray_cast(U(x, top, z), DOWN, top + 20.0)
    if hit[0] is None:
        return None
    return unity(hit[0]), unity(hit[1])


class XmasKitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_every_asset_builds_with_vertex_colors(self):
        names = set()
        for mk in xmas.MAKERS:
            reset()
            ob = mk()
            names.add(ob.name)
            self.assertTrue(ob.name.startswith("Xmas_"), ob.name)
            self.assertGreater(len(ob.data.polygons), 10, ob.name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, ob.name)
            self.assertEqual(len(col.data), len(ob.data.loops), ob.name)
        self.assertEqual(len(names), len(xmas.MAKERS))
        self.assertTrue(set(xmas.GLOW) <= names, "光る所も、ぜんぶ作る")

    def test_tree_is_tall_and_its_tiers_can_be_climbed(self):
        ob = xmas.make_xmas_tree()
        lo, hi = size(ob)
        self.assertGreater(hi.z, xmas.TREE_TOP, "ツリーは、とても高い")
        self.assertGreater(hi.x - lo.x, xmas.TREE_R0 * 1.9, "すそは広い")
        tree = bvh(ob)
        # 段の上（すそと、上の段のあいだ）は、上を向いた坂（登れる）
        for k in range(6):
            rim_r, rim_z = xmas.tier_rim(k)
            r = (rim_r + xmas.tier_rim(k + 1)[0]) / 2     # 上の段のすそより外
            for a in (0.3, 2.4, 4.4):
                h = drop(tree, math.cos(a) * r, math.sin(a) * r)
                self.assertIsNotNone(h, f"段 {k}")
                self.assertGreater(h[1].y, 0.45, f"段 {k} の上は、上を向く坂")
                self.assertTrue(xmas.TREE_TIERS[k] < h[0].y < xmas.TREE_TIERS[k + 1] + 2.0, f"段 {k}: {h[0].y:.1f}")
        # てっぺんの星は、木の上にのる
        star = xmas.make_xmas_tree_star()
        slo, shi = size(star)
        self.assertGreater(shi.z, xmas.TREE_TOP + 4.0)
        self.assertLess(slo.z, xmas.TREE_TOP, "星の柄は、木のてっぺんの中から")

    def test_present_tops_are_flat_beside_the_bow(self):
        for key in ("A", "B", "C"):
            reset()
            ob = xmas.make_present(key)
            w, h, d = xmas.PRESENTS[key]["size"]
            tree = bvh(ob)
            for (x, z) in ((w * 0.35, d * 0.35), (-w * 0.35, -d * 0.35), (-w * 0.35, d * 0.35)):
                hit = drop(tree, x, z)
                self.assertIsNotNone(hit, key)
                self.assertAlmostEqual(hit[0].y, h, delta=0.2, msg=f"{key} の上の面")
                self.assertGreater(hit[1].y, 0.95, key)

    def test_snowman_stacks_three_balls_and_looks_south(self):
        ob = xmas.make_snowman()
        lo, hi = size(ob)
        self.assertGreater(hi.z, xmas.SNOWMAN_BALLS[2][0].y + xmas.SNOWMAN_BALLS[2][1], "ぼうしまで")
        tree = bvh(ob)
        top = drop(tree, 0.0, 0.0)
        self.assertGreater(top[0].y, 26.0, "バケツのぼうしの上")
        # にんじんの鼻は、南（-z）へ、つき出す
        nose = min(unity(v.co).z for v in ob.data.vertices if abs(unity(v.co).y - xmas.SNOWMAN_BALLS[2][0].y) < 1.0)
        self.assertLess(nose, -xmas.SNOWMAN_BALLS[2][1] - 2.0)

    def test_cottage_has_a_climbable_sill_and_a_clear_window(self):
        ob = xmas.make_cottage()
        tree = bvh(ob)
        hit = drop(tree, xmas.WINDOW_X - 6.0, xmas.HOUSE_FRONT - 2.6, top=xmas.SILL_Y + 6.0)
        self.assertIsNotNone(hit)
        self.assertTrue(xmas.SILL_Y - 0.2 < hit[0].y < xmas.SILL_Y + 1.0, f"窓台の上 {hit[0].y:.2f}")
        self.assertGreater(hit[1].y, 0.7)
        # 窓とびらの前に、丸太がない（前からのレイは、かべ・とびらまでとどく）
        for (x, y, z_min) in ((xmas.WINDOW_X - 4.0, xmas.SILL_Y + 4.0, xmas.HOUSE_FRONT - 0.1),
                              (xmas.WINDOW_X + 4.0, xmas.SILL_Y + 12.0, xmas.HOUSE_FRONT - 0.1),
                              (xmas.DOOR_X - 3.0, 8.0, xmas.HOUSE_FRONT - 1.7),
                              (xmas.DOOR_X + 4.0, 13.0, xmas.HOUSE_FRONT - 1.7)):
            h = tree.ray_cast(U(x, y, xmas.HOUSE_FRONT - 12.0), NORTH, 30.0)
            self.assertIsNotNone(h[0])
            self.assertGreater(unity(h[0]).z, z_min, f"({x}, {y}) の前に、なにかある")
        lo, hi = size(ob)
        self.assertGreater(hi.z, xmas.HOUSE_EAVE + 12.0, "屋根")

    def test_sled_deck_has_no_gaps_to_fall_through(self):
        # すのこの板のあいだに、すき間がない（しゃくとりむしが落ちない）：前からうしろまで、上からのレイは板の上の面に当たる
        ob = xmas.make_sled()
        tree = bvh(ob)
        z0, z1 = xmas.SLED_DECK
        for x in (-4.0, 0.0, 3.0):
            z = z0 + 0.3
            while z < z1 - 0.3:
                h = drop(tree, x, z, top=10.0)
                self.assertIsNotNone(h, f"({x}, {z:.2f}) で、すのこの下へ落ちる")
                self.assertAlmostEqual(h[0].y, xmas.SLED_DECK_TOP, delta=0.15, msg=f"({x}, {z:.2f})")
                z += 0.1

    def test_ice_is_flat_and_at_ground_level(self):
        ob = xmas.make_ice()
        tree = bvh(ob)
        for (x, z) in ((0, 0), (8, 3), (-10, -5), (4, -8)):
            h = drop(tree, x, z, top=5.0)
            self.assertIsNotNone(h)
            self.assertAlmostEqual(h[0].y, 0.0, delta=0.05)
            self.assertGreater(h[1].y, 0.99)
        lo, hi = size(ob)
        self.assertAlmostEqual(hi.x - lo.x, xmas.ICE_RX * 2, delta=0.5)

    def test_warp_ring_has_a_hole_to_pass_through(self):
        ob = xmas.make_warp_ring()
        tree = bvh(ob)
        for (x, y) in ((0, xmas.RING_C.y), (1.5, xmas.RING_C.y + 1.0), (-1.5, xmas.RING_C.y - 1.5)):
            h = tree.ray_cast(U(x, y, -10.0), NORTH, 20.0)
            self.assertIsNone(h[0], f"輪の中 ({x}, {y}) は、通りぬけられる")
        h = tree.ray_cast(U(0, xmas.RING_C.y + xmas.RING_R, -10.0), NORTH, 20.0)
        self.assertIsNotNone(h[0], "輪のふち")

    def test_mitten_has_a_dark_opening(self):
        ob = xmas.make_mitten()
        tree = bvh(ob)
        c = xmas.MITTEN_CUFF
        h = tree.ray_cast(UV(c + Vector((0, 0, 8.0))), U(0, 0, -1), 20.0)
        self.assertIsNotNone(h[0])
        self.assertTrue(c.z < unity(h[0]).z < c.z + 1.0, "口の中の、暗い所")

    def test_closed_parts_face_outward(self):
        # とじた部品の面は外向き（内向きだと、ゲームでは黒く見える）
        import bmesh
        for mk in xmas.MAKERS:
            reset()
            ob = mk()
            bm = bmesh.new()
            bm.from_mesh(ob.data)
            seen = set()
            for f0 in bm.faces:
                if f0.index in seen:
                    continue
                comp, stack = [], [f0]
                seen.add(f0.index)
                while stack:
                    f = stack.pop()
                    comp.append(f)
                    for e in f.edges:
                        for g in e.link_faces:
                            if g.index not in seen:
                                seen.add(g.index)
                                stack.append(g)
                if not all(len(e.link_faces) == 2 for f in comp for e in f.edges):
                    continue
                vol = 0.0
                for f in comp:
                    vs = [v.co for v in f.verts]
                    for i in range(1, len(vs) - 1):
                        vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6.0
                self.assertGreaterEqual(vol, -1e-9, f"{ob.name} に裏返った部品がある（面が {len(comp)} 枚）")
            bm.free()

    def test_parts_are_joined_without_open_ends(self):
        for mk in xmas.MAKERS:
            reset()
            ob = mk()
            if ob.name in xmas.GLOW:
                continue
            self.assertEqual(open_ends(ob, 0.08), [], f"{ob.name} に、口があいたまま見える筒")
            self.assertEqual(floating_parts(ob, 0.03), [], f"{ob.name} に、浮いた部品")


def main():
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(XmasKitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)


if __name__ == "__main__":
    main()

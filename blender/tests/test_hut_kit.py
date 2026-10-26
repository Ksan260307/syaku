"""
山小屋の中のアセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_hut_kit.py
失敗すると終了コード 1 を返す。
"""
import os
import sys
import unittest

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_hut_kit as hut  # noqa: E402
from parts_check import floating_parts, open_ends  # noqa: E402


def open_edges(ob):
    """同じ場所の頂点をまとめてから数えた、面が 1 つしかない辺の数"""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    n = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    bm.free()
    return n


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def ub(ob):
    """Unity の座標での、いちばん小さい点と大きい点"""
    ps = [Vector((-v.co.x, v.co.z, -v.co.y)) for v in ob.data.vertices]
    lo = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    hi = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    return lo, hi


def unity_normal(poly):
    n = poly.normal
    return Vector((-n.x, n.z, -n.y))


def unity_center(poly):
    c = poly.center
    return Vector((-c.x, c.z, -c.y))


class HutKitTests(unittest.TestCase):
    def setUp(self):
        reset()


    def test_parts_are_joined_without_open_ends(self):
        # 筒の口があいたまま見える所や、宙に浮いた部品がない（前は、リュックのかたひもが、体からはなれて浮いていた）
        ob = hut.make_backpack()
        self.assertEqual(open_ends(ob, 0.3), [], "Hut_Backpack に、口があいたまま見える筒")
        self.assertEqual(floating_parts(ob, 0.05), [], "Hut_Backpack に、浮いた部品")
    def test_every_asset_builds_with_vertex_colors(self):
        names = set()
        for mk in hut.MAKERS:
            ob = mk()
            names.add(ob.name)
            self.assertTrue(ob.name.startswith("Hut_"), ob.name)
            self.assertGreater(len(ob.data.polygons), 1, ob.name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, f"{ob.name} に頂点カラーがない")
            self.assertEqual(len(col.data), len(ob.data.loops), ob.name)
        for need in ("Hut_Walls", "Hut_Walls_Col", "Hut_Roof", "Hut_Roof_Col", "Hut_Stove", "Hut_Bunk", "Hut_Table", "Hut_Nest", "Hut_MouseHole"):
            self.assertIn(need, names)

    def test_room_is_human_sized_for_an_inchworm(self):
        lo, hi = ub(hut.make_walls())
        self.assertGreater(hi.x - lo.x, hut.HALF_X * 2, "部屋の幅（かべの丸太の外まで）")
        self.assertAlmostEqual(hi.y, hut.RIDGE_H, delta=4.0, msg="妻かべは、むねまで")
        lo, hi = ub(hut.make_beams())
        self.assertAlmostEqual(lo.y, hut.BEAM_Y, delta=0.01, msg="はりの下")

    def test_walls_collider_is_open_at_the_door_and_windows(self):
        ob = hut.make_walls_col()
        a, b, h = hut.DOOR
        wa, wb, y0, y1 = hut.WIN_S
        for poly in ob.data.polygons:
            c = unity_center(poly)
            if c.y > hut.SILL_STEP + 0.01 and c.y < h - 1.0:
                self.assertFalse(a + 1.0 < c.x < b - 1.0 and c.z < -hut.HALF_Z + 4.0, f"とびらの所に、かべの面 {c}")
            if y0 + 1.0 < c.y < y1 - 1.0:
                self.assertFalse(wa + 1.0 < c.x < wb - 1.0 and -hut.HALF_Z - 4.0 < c.z < -hut.HALF_Z + 1.0, f"窓の所に、かべの面 {c}")

    def test_roof_collider_faces_into_the_room(self):
        ob = hut.make_roof_col()
        inside = Vector((0.0, hut.WALL_H * 0.5, 0.0))
        self.assertGreater(len(ob.data.polygons), 3)
        for poly in ob.data.polygons:
            n = unity_normal(poly)
            self.assertGreater(n.dot(inside - unity_center(poly)), 0.0, "屋根うらの当たり判定は、部屋の方を向く（上からのレイは通りぬける）")

    def test_view_and_flat_pieces_face_the_viewer(self):
        # 窓の外の景色は、部屋の方を向く（うら面は描かれない）
        ob = hut.make_view()
        for poly in ob.data.polygons:
            c = unity_center(poly)
            if c.y < 1.0:
                continue
            self.assertLess(unity_normal(poly).dot(Vector((c.x, 0.0, c.z))), 0.0, "景色の面は、部屋の方を向く")
        # ラグの上の面は、上を向く
        up = [unity_normal(p).y for p in hut.make_rug().data.polygons if unity_center(p).y > 0.3]
        self.assertGreater(sum(up) / len(up), 0.8, "ラグの上の面は、上を向く")
        # ネズミの穴は、前（+z）を向く
        hole = hut.make_mouse_hole()
        dark = [p for p in hole.data.polygons if abs(unity_center(p).z - 0.12) < 0.01]
        self.assertGreater(len(dark), 10)
        for p in dark:
            self.assertGreater(unity_normal(p).z, 0.9, "穴は部屋の方を向く")

    def test_book_spine_faces_out(self):
        # 本のまるい背は、外を向く（うら返ると、ゲームでは見えない）
        from mathutils.bvhtree import BVHTree
        ob = hut.make_book()
        tree = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
        d = (hut.U(1.0, 0.0, 0.0) - hut.U(0.0, 0.0, 0.0)).normalized()
        hit = tree.ray_cast(hut.U(-10.0, 1.2, 0.0), d, 30.0)
        self.assertIsNotNone(hit[0])
        self.assertGreater(hit[1].dot(-d), 0.9, "背表紙が、内を向いている")

    def test_door_opens_into_the_room(self):
        lo, hi = ub(hut.make_door())
        self.assertGreater(hi.z, -hut.HALF_Z + 20.0, "とびらは、部屋の中（北）へひらく")
        self.assertLess(lo.z, -hut.HALF_Z + 4.0)
        self.assertLess(hi.x, hut.DOOR[0] + 6.0, "ちょうつがいは西のはし。とびらの穴をふさがない")

    def test_furniture_sizes(self):
        lo, hi = ub(hut.make_table())
        self.assertAlmostEqual(hi.y, hut.TABLE_H, delta=0.01, msg="テーブルの天板")
        lo, hi = ub(hut.make_bunk())
        self.assertGreater(hi.y, hut.BUNK_UP + 4.0, "上の段の手すり")
        lo, hi = ub(hut.make_stove())
        self.assertGreater(hi.y, hut.RIDGE_H, "えんとつは屋根をつきぬける")
        lo, hi = ub(hut.make_shelf())
        self.assertGreater(hi.y, hut.SHELF_YS[-1])
        for mk in (hut.make_crumb, hut.make_seed, hut.make_match, hut.make_button):
            lo, hi = ub(mk())
            self.assertLess((hi - lo).length, 2.5, "小さな物は、押して動かせる大きさ（本物の大きさ）")
            self.assertAlmostEqual(lo.y, 0.0, delta=0.03, msg="床にのる")

    def test_bucket_and_boots_colliders_have_no_overhang_at_the_floor(self):
        # バケツと長ぐつの当たり判定は、床から少しの高さまで、下を向いた面（ひさし）がない
        # （見た目のとおりだと、根もとのえぐれや、底より外へはり出した筒の口から、しゃくとりむしが中へ入りこんだ）
        foot_z = (hut.BUCKET_FOOT - 6.0) / (7.2 - 6.0) * 10.0
        for ob, top in ((hut.make_bucket(False), foot_z), (hut.make_boots_col(), hut.BOOT_SKIRT)):
            self.assertTrue(ob.name.endswith("_Col"), ob.name)
            lo = min(v.co.z for v in ob.data.vertices)
            self.assertLess(lo, -0.2, f"{ob.name} のすそは、床の下まで（床とのあいだに、すき間がない）")
            for p in ob.data.polygons:
                if p.center.z > lo + 0.05 and max(ob.data.vertices[i].co.z for i in p.vertices) <= top + 1e-4:
                    # （長ぐつの筒は、うしろへ 1 度ほどかたむくだけ。えぐれは、バケツで 7 度、つま先で 27 度あった）
                    self.assertGreater(p.normal.z, -0.03, f"{ob.name} の高さ {p.center.z:.2f} に、下を向いた面")
            # とじた形（あいた辺がない）：足もとの点が物の中か、わかる（SurfaceProbe.InsideSolid は、とじた形だけを見る）
            self.assertEqual(open_edges(ob), 0, f"{ob.name} に、あいた辺")
        # バケツの見た目の外がわは、足より下では、当たり判定の足の中
        for v in hut.make_bucket().data.vertices:
            if v.co.z < foot_z:
                self.assertLess(v.co.xy.length, hut.BUCKET_FOOT + 1e-3)


if __name__ == "__main__":
    result = unittest.main(argv=[sys.argv[0]], exit=False).result
    sys.exit(0 if result.wasSuccessful() else 1)

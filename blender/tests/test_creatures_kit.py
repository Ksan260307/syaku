"""
いきもの・川辺アセット生成スクリプトのテスト（Blender の中で実行する）。
  blender -b --factory-startup --python blender/tests/test_creatures_kit.py
失敗すると終了コード 1 を返す。
"""
import os
import sys
import tempfile
import unittest

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import build_forest_kit as kit  # noqa: E402
import build_creatures as cr  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None


def size(ob):
    xs = [v.co.x for v in ob.data.vertices]
    ys = [v.co.y for v in ob.data.vertices]
    zs = [v.co.z for v in ob.data.vertices]
    return max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)


def zmin(ob):
    return min(v.co.z for v in ob.data.vertices)


MAKERS = {
    "Ladybug": cr.make_ladybug,
    "Snail": cr.make_snail,
    "Ant": cr.make_ant,
    "PillBug": cr.make_pillbug,
    "Butterfly_Body": cr.make_butterfly_body,
    "Butterfly_Wing": cr.make_butterfly_wing,
    "Beetle": cr.make_beetle,
    "WaterStrider": cr.make_water_strider,
    "Dragonfly_Body": cr.make_dragonfly_body,
    "Dragonfly_Wing": cr.make_dragonfly_wing,
    "Frog": cr.make_frog,
    "Crab": cr.make_crab,
    "RiverSnail": cr.make_river_snail,
    "Firefly_Body": cr.make_firefly_body,
    "Firefly_Glow": cr.make_firefly_glow,
    "Grasshopper": cr.make_grasshopper,
    "Otoshibumi": cr.make_otoshibumi,
    "Cradle": cr.make_cradle,
    "AntHill": cr.make_anthill,
    "Sparrow_Body": lambda: cr.make_bird_body("Sparrow_Body", 5.5, False),
    "Sparrow_Wing": lambda: cr.make_bird_wing("Sparrow_Wing", 5.5, False),
    "Crow_Body": lambda: cr.make_bird_body("Crow_Body", 18.0, True),
    "Crow_Wing": lambda: cr.make_bird_wing("Crow_Wing", 18.0, True),
    "RiverStone_A": lambda: cr.make_river_stone("RiverStone_A", 1, (1.6, 1.2, 0.55), (kit.hexc("#8d96a3"), kit.hexc("#b7b2a5"))),
    "Horsetail": cr.make_horsetail,
    "Iris": cr.make_iris,
    "RootArch": cr.make_root_arch,
    "Kamikiri": cr.make_kamikiri,
    "Kuwagata": cr.make_kuwagata,
    "Kamemushi": cr.make_kamemushi,
    "Tokage": cr.make_tokage,
    "Monshiro_Body": cr.make_monshiro_body,
    "Monshiro_Wing": cr.make_monshiro_wing,
    "Raichou_Body": lambda: cr.make_bird_body("Raichou_Body", 15.0, "raichou"),
    "Raichou_Wing": lambda: cr.make_bird_wing("Raichou_Wing", 15.0, "raichou"),
    "Risu": cr.make_risu,
    "Okojo": cr.make_okojo,
    "Okojo_Rocks": cr.make_okojo_rocks,
    "Nakiusagi": cr.make_nakiusagi,
    "Sanshouuo": cr.make_sanshouuo,
    "Asagimadara_Body": cr.make_asagimadara_body,
    "Asagimadara_Wing": cr.make_asagimadara_wing,
    "Maruhanabachi": cr.make_maruhanabachi,
    "Maruhanabachi_Wing": cr.make_maruhanabachi_wing,
    "Oniyanma_Body": cr.make_oniyanma_body,
    "Oniyanma_Wing": cr.make_oniyanma_wing,
    "Higurashi": cr.make_higurashi,
    "Maimaikaburi": cr.make_maimaikaburi,
}


class CreatureKitTests(unittest.TestCase):
    def setUp(self):
        reset()

    def test_every_asset_builds_with_vertex_colors(self):
        for name, mk in MAKERS.items():
            ob = mk()
            self.assertEqual(ob.name, name)
            self.assertGreater(len(ob.data.vertices), 20, name)
            self.assertGreater(len(ob.data.polygons), 10, name)
            col = ob.data.color_attributes.get("Col")
            self.assertIsNotNone(col, f"{name} に頂点カラーがない")
            self.assertEqual(len(col.data), len(ob.data.loops), name)

    def test_portrait_list_covers_all_species(self):
        # ふつうの 40 種と、レアの 6 種
        ids = [c[0] for c in cr.CREATURES]
        self.assertEqual(len(ids), 46)
        self.assertEqual(len(set(ids)), 46)
        for need in ("raichou", "risu", "okojo", "nakiusagi", "sanshouuo", "asagimadara", "maruhanabachi", "oniyanma",
                     "higurashi", "maimaikaburi"):
            self.assertIn(need, ids)
        for need in ("koumori", "mogura", "okera", "nanafushi", "gengorou", "hanakamakiri", "hato",
                     "kameleon", "herakuresu", "flamingo", "harinezumi"):
            self.assertIn(need, ids)
        for need in ("ant", "snail", "butterfly", "otoshibumi", "grasshopper", "frog", "sparrow", "crow", "ladybug",
                     "spider", "mantis", "ant_helmet", "spider_sneaker", "kamikiri", "kuwagata", "kamemushi", "tokage", "monshiro"):
            self.assertIn(need, ids)

    def test_sizes_match_the_inchworm_scale(self):
        # しゃくとりむしの体長 = 1。アリは小さく、カラスはとても大きい
        ant = max(size(cr.make_ant()))
        sparrow = max(size(cr.make_bird_body("Sparrow_Body", 5.5, False)))
        crow = max(size(cr.make_bird_body("Crow_Body", 18.0, True)))
        self.assertLess(ant, 1.0)
        self.assertGreater(sparrow, 3.0)
        self.assertGreater(crow, sparrow * 2.5)

    def test_creatures_stand_on_the_ground(self):
        # 足もと（z の最小）がほぼ 0：地面に置いたときに浮いたり沈んだりしない
        for name in ("Ant", "Ladybug", "Frog", "Crab", "Grasshopper", "Sparrow_Body"):
            ob = MAKERS[name]()
            self.assertAlmostEqual(zmin(ob), 0.0, delta=0.15 * max(size(ob)), msg=name)

    def test_helmet_sits_above_the_eyes(self):
        # ヘルメットは目のてっぺんより上にのる（目がヘルメットをつきぬけない）
        ant = cr.make_ant()
        from mathutils import Vector
        centers = [Vector((0.04 * sx, 0.15, 0.1)) for sx in (-1, 1)]   # make_ant の目（半径 0.02）
        eyes = [v.co for v in ant.data.vertices if any((v.co - c).length < 0.0215 for c in centers)]
        self.assertGreater(len(eyes), 10)
        eye_top = max(p.z for p in eyes)
        helmet = cr.make_ant_helmet()
        self.assertGreater(zmin(helmet), eye_top + 0.001, "ヘルメットのいちばん下が、目より上")

    def test_wings_are_thin(self):
        for name in ("Butterfly_Wing", "Dragonfly_Wing", "Sparrow_Wing", "Crow_Wing", "Asagimadara_Wing", "Oniyanma_Wing", "Maruhanabachi_Wing"):
            ob = MAKERS[name]()
            s = sorted(size(ob))
            self.assertLess(s[0], s[2] * 0.35, f"{name} が厚すぎる")

    def test_mountain_creatures_have_real_sizes(self):
        # 1 単位 = 2.5cm：リスは 20cm くらい（しっぽまで 35cm）、マルハナバチは 2cm、オニヤンマは 10cm
        risu = size(cr.make_risu())
        self.assertTrue(7.0 < risu[1] < 11.0, risu)
        bee = max(size(cr.make_maruhanabachi()))
        self.assertLess(bee, 1.6)
        yanma = size(cr.make_oniyanma_body())
        self.assertTrue(3.2 < yanma[1] < 5.0, yanma)
        raichou = max(size(cr.make_bird_body("Raichou_Body", 15.0, "raichou")))
        self.assertGreater(raichou, max(size(cr.make_bird_body("Sparrow_Body", 5.5, False))) * 2.0, "ライチョウはスズメよりずっと大きい")

    def test_okojo_rises_from_its_den(self):
        # オコジョは立ち上がった形（上へ長い）。すみかの石は、まん中に穴があいている
        ok = size(cr.make_okojo())
        self.assertGreater(ok[2], ok[0] * 2.0, "体はたてに長い")
        den = cr.make_okojo_rocks()
        center = [v for v in den.data.vertices if (v.co.x ** 2 + v.co.y ** 2) ** 0.5 < 1.0 and v.co.z > 0.2]
        self.assertEqual(len(center), 0, "穴の上には石がない（顔を出せる）")

    def test_mountain_walkers_have_leg_rigs(self):
        for name in ("Risu", "Sanshouuo", "Higurashi", "Maimaikaburi"):
            parts = cr.split_legs(MAKERS[name], name)
            self.assertGreaterEqual(len(parts) - 1, 2, f"{name} の右の脚")
            self.assertIn(name, cr.RIG)

    def test_river_stone_is_flat_and_root_arch_is_open(self):
        st = MAKERS["RiverStone_A"]()
        sx, sy, sz = size(st)
        self.assertLess(sz, min(sx, sy) * 0.6, "川石は平たい")
        arch = cr.make_root_arch()
        ax, ay, az = size(arch)
        self.assertGreater(az, 3.0, "しゃくとりむしがくぐれる高さ")
        # 根のアーチの真ん中・下のほうには何もない（くぐれる）
        hole = [v for v in arch.data.vertices if abs(v.co.x) < 1.0 and 0.2 < v.co.z < 1.8 and abs(v.co.y) < 1.0]
        self.assertEqual(len(hole), 0)

    def test_export_writes_fbx(self):
        tmp = tempfile.mkdtemp()
        ob = cr.make_ladybug()
        kit.export_fbx(ob, tmp)
        p = os.path.join(tmp, "Ladybug.fbx")
        self.assertTrue(os.path.isfile(p))
        self.assertGreater(os.path.getsize(p), 2000)


def main():
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(CreatureKitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)


if __name__ == "__main__":
    main()

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
    "Hato_Body": lambda: cr.make_bird_body("Hato_Body", 13.0, "pigeon"),
    "Flamingo_Body": lambda: cr.make_bird_body("Flamingo_Body", 20.0, "flamingo"),
    "Gengorou": cr.make_gengorou,
    "Mogura": cr.make_mogura,
    "Harinezumi": cr.make_harinezumi,
    "Kameleon": cr.make_kameleon,
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
    "Yamori": cr.make_yamori,
    "Mukade_Head": cr.make_mukade_head,
    "Mukade_Seg": cr.make_mukade_seg,
    "Mukade_Tail": cr.make_mukade_tail,
    "Mukade_Leg": cr.make_mukade_leg,
    "Hasamimushi": cr.make_hasamimushi,
    "Hasamimushi_Tail": cr.make_hasamimushi_tail,
    "Hae_Body": cr.make_hae_body,
    "Hae_Arm": cr.make_hae_arm,
    "Hae_Wing": cr.make_hae_wing,
    "Tsubame_Body": lambda: cr.make_bird_body("Tsubame_Body", 7.0, "tsubame"),
    "Tsubame_Wing": lambda: cr.make_bird_wing("Tsubame_Wing", 7.0, "tsubame"),
    "Nezumi": cr.make_nezumi,
    "Yadokari": cr.make_yadokari,
    "Yadokari_Crab": cr.make_yadokari_crab,
    "Shiomaneki": cr.make_shiomaneki,
    "Shiomaneki_Claw": cr.make_shiomaneki_claw,
    "Hitode": cr.make_hitode,
    "Hitode_Arm": cr.make_hitode_arm,
    "Uni": cr.make_uni,
    "Funamushi": cr.make_funamushi,
    "Manta_Body": cr.make_manta,
    "Manta_Fin": cr.make_manta_fin,
    "Kamome_Body": lambda: cr.make_bird_body("Kamome_Body", 19.0, "gull"),
    "Kamome_Wing": lambda: cr.make_bird_wing("Kamome_Wing", 19.0, "gull"),
    "Kitsune": cr.make_kitsune,
    "Tanuki": cr.make_tanuki,
    "Fukurou_Body": cr.make_fukurou_body,
    "Fukurou_Head": cr.make_fukurou_head,
    "Fukurou_WingFolded": cr.make_fukurou_wing_folded,
    "Fukurou_Wing": lambda: cr.make_bird_wing("Fukurou_Wing", 20.0, "owl"),
    "Ga_Body": cr.make_ga_body,
    "Ga_Wing": cr.make_ga_wing,
    "Kanabun": cr.make_kanabun,
    "Kanabun_Wing": cr.make_kanabun_wing,
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
        # ふつうの 57 種と、レアの 7 種
        ids = [c[0] for c in cr.CREATURES]
        self.assertEqual(len(ids), 64)
        self.assertEqual(len(set(ids)), 64)
        for need in ("kitsune", "fukurou", "tanuki", "ga", "kanabun"):
            self.assertIn(need, ids)
        for need in ("yadokari", "shiomaneki", "kamome", "hitode", "uni", "funamushi", "manta"):
            self.assertIn(need, ids)
        for need in ("yamori", "mukade", "hasamimushi", "hae", "tsubame", "nezumi"):
            self.assertIn(need, ids)
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
        for name in ("Butterfly_Wing", "Dragonfly_Wing", "Sparrow_Wing", "Crow_Wing", "Asagimadara_Wing", "Oniyanma_Wing", "Maruhanabachi_Wing",
                     "Hae_Wing", "Tsubame_Wing"):
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

    def test_hut_creatures_have_real_sizes(self):
        # 1 単位 = 2.5cm：ヤモリは 12cm くらい、ネズミはしっぽまで 13cm、ハサミムシは 2cm、ハエは 1cm、ツバメは 17cm
        self.assertTrue(4.0 < size(cr.make_yamori())[1] < 5.5)
        self.assertTrue(4.5 < size(cr.make_nezumi())[1] < 6.5)
        ha = size(cr.make_hasamimushi())[1] + size(cr.make_hasamimushi_tail())[1]
        self.assertTrue(0.7 < ha < 1.6, ha)
        self.assertLess(max(size(cr.make_hae_body())), 0.6)
        tsubame = size(cr.make_bird_body("Tsubame_Body", 7.0, "tsubame"))
        self.assertGreater(tsubame[1], size(cr.make_bird_body("Sparrow_Body", 5.5, False))[1], "ツバメは、尾が長い")
        # ムカデは、体節をならべて 4〜5（10cm くらい）
        n = (cr.MUKADE_N + 2) * cr.MUKADE_SEG
        self.assertTrue(3.8 < n < 5.5, n)

    def test_hut_walkers_have_leg_rigs(self):
        for name in ("Yamori", "Hasamimushi", "Nezumi"):
            reset()
            parts = cr.split_legs(MAKERS[name], name)
            self.assertGreaterEqual(len(parts) - 1, 2, f"{name} の右の脚")
            self.assertTrue(name in cr.RIG or name in cr.LIMBS, name)
        self.assertEqual(len(cr.RIG["Yamori"]), 2, "ヤモリの右の脚 2 本")
        self.assertEqual(len(cr.LIMBS["Nezumi"]), 2, "ネズミの右の手足 2 本")
        # ムカデの脚は、体節の横（右）から外へ、地面までのびる
        leg = cr.make_mukade_leg()
        self.assertAlmostEqual(min(v.co.z for v in leg.data.vertices) + cr.MUKADE_HIP.z, 0.0, delta=0.03)
        self.assertGreater(max(v.co.x for v in leg.data.vertices), 0.15)

    def test_coast_creatures_have_real_sizes(self):
        # 1 単位 = 2.5cm：ヤドカリは殻ごと 2.5cm、ヒトデは 6cm、ウニはとげまで 8cm、フナムシは 4cm（触角まで 5cm）、
        # カモメはカラスくらい、マンタは子どもでも、むなびれを広げると 40cm より大きい
        self.assertTrue(0.7 < max(size(cr.make_yadokari())) < 1.4)
        self.assertTrue(1.8 < size(cr.make_hitode())[0] + 2 * size(cr.make_hitode_arm())[1] < 3.2)
        uni = size(cr.make_uni())[0]
        spine = cr.make_uni_spines(0)
        span = max(v.co.x for v in spine.data.vertices) - min(v.co.x for v in spine.data.vertices)
        self.assertTrue(1.2 < uni < 1.6 and 2.6 < span < 3.8, (uni, span))
        self.assertTrue(1.0 < size(cr.make_funamushi())[1] < 2.4)
        kamome = max(size(cr.make_bird_body("Kamome_Body", 19.0, "gull")))
        self.assertGreater(kamome, max(size(cr.make_bird_body("Sparrow_Body", 5.5, False))) * 2.5)
        fin = size(cr.make_manta_fin())[0]
        self.assertGreater(2 * (fin + cr.MANTA_FIN.x), 14.0, "マンタは、むなびれを広げると大きい")

    def test_shrine_creatures_have_real_sizes(self):
        # 1 単位 = 2.5cm：キツネは体 60cm としっぽで 1m ほど、タヌキは 55cm、フクロウは 50cm、
        # オオミズアオは、はねを広げて 10cm（尾まで、もっと長い）、カナブンは 2.5cm
        fox = size(cr.make_kitsune())
        self.assertTrue(34.0 < fox[1] < 48.0, fox)
        self.assertTrue(14.0 < fox[2] < 24.0, fox)
        tanuki = size(cr.make_tanuki())
        self.assertTrue(20.0 < tanuki[1] < 32.0, tanuki)
        self.assertLess(tanuki[2], fox[2], "タヌキは、キツネより足が短い")
        owl = size(cr.make_fukurou_body())
        self.assertTrue(12.0 < owl[2] < 20.0, owl)   # 頭は別（首でまわる）
        wing = size(cr.make_ga_wing())
        self.assertTrue(1.4 < wing[0] < 2.6 and wing[1] > 3.0, ("はねと、長い尾", wing))
        self.assertTrue(0.8 < size(cr.make_kanabun())[1] < 1.4)

    def test_shrine_walkers_have_limbs_and_the_owl_turns_its_head(self):
        for name in ("Kitsune", "Tanuki", "Fukurou_Body"):
            reset()
            cr.split_legs(MAKERS[name], name)
            self.assertEqual(len(cr.LIMBS[name]), 2 if name != "Fukurou_Body" else 1, f"{name} の右の手足")
        reset()
        cr.split_legs(MAKERS["Kanabun"], "Kanabun")
        self.assertEqual(len(cr.RIG["Kanabun"]), 3, "カナブンの右の脚")
        # フクロウの頭は、首（原点）のまわりでまわる：顔の盤は前（+Y）
        head = cr.make_fukurou_head()
        ys = [v.co.y for v in head.data.vertices]
        self.assertGreater(max(ys), 3.0, "顔は前")
        zs = [v.co.z for v in head.data.vertices]
        self.assertLess(min(zs), 1.0, "首のところから")

    def test_coast_walkers_have_leg_rigs(self):
        for name, n in (("Yadokari", 2), ("Shiomaneki", 4), ("Funamushi", 7)):
            reset()
            parts = cr.split_legs(MAKERS[name], name)
            self.assertEqual(len(cr.RIG[name]), n, f"{name} の右の脚")
            self.assertGreaterEqual(len(parts) - 1, n)
        # ヤドカリの頭とはさみは、殻の口から前へ出る（ひっこむときは、口のまん中へちぢむ）
        crab = cr.make_yadokari_crab()
        mouth = cr.yadokari_mouth()
        self.assertGreater(max(v.co.y for v in crab.data.vertices), mouth.y + 0.3, "はさみは、殻の口より前")

    def test_okojo_rises_from_its_den(self):
        # オコジョは立ち上がった形（上へ長い）。すみかの石は、まん中に穴があいている
        ok = size(cr.make_okojo())
        self.assertGreater(ok[2], ok[0] * 2.0, "体はたてに長い")
        den = cr.make_okojo_rocks()
        center = [v for v in den.data.vertices if (v.co.x ** 2 + v.co.y ** 2) ** 0.5 < 1.0 and v.co.z > 0.2]
        self.assertEqual(len(center), 0, "穴の上には石がない（顔を出せる）")

    def test_okojo_den_has_a_real_hole_in_a_soil_mound(self):
        # 穴の口はオコジョの体が通る広さで、土の山のふちより低い（すみかは地面より 0.15 下に置く。底は地面のすぐ上）。
        # まわりは掘り出した土の山で、すそは地面の下
        den = cr.make_okojo_rocks()
        ok = size(cr.make_okojo())
        hole = [v.co for v in den.data.vertices if (v.co.x ** 2 + v.co.y ** 2) ** 0.5 < cr.OKOJO_HOLE_R - 0.1]
        self.assertTrue(all(0.15 < p.z < 0.25 for p in hole), "穴の底は、地面のすぐ上（地面が穴をふさがない）")
        self.assertGreater(cr.OKOJO_HOLE_R * 2, ok[0] * 0.95, "オコジョの体が穴を通る")
        rim = [v.co.z for v in den.data.vertices if 1.15 < (v.co.x ** 2 + v.co.y ** 2) ** 0.5 < 1.45 and v.co.z < 0.6]
        self.assertTrue(0.15 < max(rim) < 0.5, f"穴のまわりは、土が少しもり上がる {max(rim)}")
        skirt = [v.co.z for v in den.data.vertices if (v.co.x ** 2 + v.co.y ** 2) ** 0.5 > cr.OKOJO_SOIL_R - 0.1 and v.co.z < 0.0]
        self.assertLess(min(skirt), -0.4, "土の山のすそは、地面の下へ")

    def test_mountain_walkers_have_leg_rigs(self):
        for name in ("Risu", "Sanshouuo", "Higurashi", "Maimaikaburi", "Nakiusagi", "Okojo"):
            parts = cr.split_legs(MAKERS[name], name)
            self.assertGreaterEqual(len(parts) - 1, 2, f"{name} の右の脚")
            self.assertTrue(name in cr.RIG or name in cr.LIMBS, name)

    def test_limbs_split_into_bones_at_the_joints(self):
        # 手足が体と一体で動かないいきものがいない：鳥・カエル・けものは、ひざで曲がる手足（上の骨・下の骨・足先）に分かれる
        for name, mk, n in (("Frog", cr.make_frog, 2), ("Sparrow_Body", MAKERS["Sparrow_Body"], 1), ("Nakiusagi", cr.make_nakiusagi, 2),
                            ("Harinezumi", cr.make_harinezumi, 2), ("Risu", cr.make_risu, 2), ("Okojo", cr.make_okojo, 1)):
            reset()
            parts = {o.name: o for o in cr.split_legs(mk, name)}
            self.assertEqual(len(cr.LIMBS[name]), n, f"{name} の右の手足の数")
            for up, lo, ft, hip, knee, ankle in cr.LIMBS[name]:
                for part in (up, lo, ft):
                    self.assertIn(part, parts)
                # どのメッシュも、関節（原点）をつつむ：関節のまわりに回しても、はなれない
                for part in (up, lo, ft):
                    vs = [v.co for v in parts[part].data.vertices]
                    for ax in range(3):
                        self.assertLess(min(v[ax] for v in vs), 0.02, f"{part} が関節からはなれている")
                        self.assertGreater(max(v[ax] for v in vs), -0.02, f"{part} が関節からはなれている")
                self.assertGreater(hip.z, ankle.z, f"{name} の足首は付け根より下")
            body = parts[name]
            # 体だけのメッシュには、手足が残っていない（鳥の体は、足の分だけ地面からはなれる）
            if name == "Sparrow_Body":
                self.assertGreater(zmin(body), 0.5, "スズメの体に足が残っている")

    def test_closed_parts_face_outward(self):
        # とじた部品（体・目・脚）の面は外向き。内向きだと、ゲームでは体が黒く見える（前は鳥の体が裏返っていた）
        import bmesh
        for name, mk in MAKERS.items():
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
                    continue   # 羽や帯のような、とじていない面は見ない
                vol = 0.0
                for f in comp:
                    vs = [v.co for v in f.verts]
                    for i in range(1, len(vs) - 1):
                        vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6.0
                self.assertGreaterEqual(vol, -1e-9, f"{name} に裏返った部品がある（面が {len(comp)} 枚）")
            bm.free()

    def test_pillbug_legs_share_one_mesh(self):
        parts = cr.split_legs(cr.make_pillbug, "PillBug", share=True)
        rig = cr.RIG["PillBug"]
        self.assertEqual(len(rig), 7, "だんごむしの脚は、片側 7 本")
        self.assertEqual(len({n for n, b, t in rig}), 1, "同じ形の脚は、1 つのメッシュを使いまわす")
        self.assertEqual(len(parts), 2, "体と、脚 1 つ")
        for n, b, t in rig:
            self.assertLess(t.z, b.z, "足先は付け根より下")

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

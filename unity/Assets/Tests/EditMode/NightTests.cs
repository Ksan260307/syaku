using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// 夜の森のエリア（枯れ沢の谷と丸木橋・月見の池・ぬた場・竹やぶ・光る倒木・ミノムシの木・ねぐらの木・星見の岩・しずく・名所・いきもの・地図・夜の光）と、
    /// 夜の森の 6 しゅのいきものを、実際に組み立てて確かめる。
    /// </summary>
    public class NightTests
    {
        static WorldGenerator _gen;

        static readonly string[] NightSpecies = { "minomushi", "zatoumushi", "kirigirisu", "inoshishi", "uribou", "shimaenaga" };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Night;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void NightForest_IsTheEighthArea_ReachedFromTheShrine()
        {
            Assert.AreSame(Areas.Night, Areas.All[7]);
            Assert.AreSame(Areas.Night, Areas.Get("night"));
            Assert.AreEqual("夜の森", Areas.Night.DisplayName);
            Assert.AreEqual(7000, Areas.Night.DropIdOffset);
            Assert.AreEqual(30, Areas.Night.DropCount);
            Assert.AreSame(Areas.Night, Areas.AreaOfDrop(7010));
            // 神社の奥の宮のうらのトンネル（あとから作る。神社のしずくは変えない）と、夜の森から神社へもどるトンネル
            var toNight = Areas.Shrine.Gates.Single(g => g.targetArea == "night");
            Assert.IsTrue(toNight.late, "神社のしずくを変えないよう、あとから作る");
            Assert.AreEqual(58.5f, toNight.position.magnitude, 0.5f, "神社のふち");
            Assert.Greater(Vector2.Distance(toNight.position, ShrineLayout.Gate), 30f, "公園へのトンネルとは、はなれている");
            Assert.Greater(Vector2.Distance(toNight.position, ShrineLayout.InnerShrine), 8f, "奥の宮とは、はなれている");
            Assert.Greater(Vector2.Distance(toNight.arrival.from, ShrineLayout.InnerShrine), 6f, "もどってきた所は、奥の宮の祠の外");
            var back = Areas.Night.Gates.Single(g => g.targetArea == "shrine");
            Assert.AreEqual("shrine", back.targetArea);
            Assert.IsTrue(Areas.Shrine.ArrivalFrom("night", out var shrineArrive, out _));
            Assert.Less(Vector2.Distance(shrineArrive, toNight.position), 8f, "夜の森からもどると、トンネルの前に出る");
            var ids = NightLayout.Landmarks.Select(l => l.id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(70, 9).ToList(), ids);
            Assert.IsTrue(NightLayout.Landmarks.All(l => l.areaId == "night"));
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Select(l => l.id).Distinct().Count(), "名所の番号は重ならない");
        }

        [Test]
        public void Ground_HasAGullyAcrossTheMiddle_AHill_APond_AndAShallowWallow()
        {
            // 枯れ沢の谷：まん中は深く、両がわの土手は高い（森の昼とはちがう、東西に横切る谷）
            foreach (float x in new[] { -40f, -10f, 20f, 40f })
            {
                float z = NightLayout.GullyZ(x);
                float bottom = NightLayout.Height(x, z);
                float bank = Mathf.Min(NightLayout.Height(x, z - 11f), NightLayout.Height(x, z + 11f));
                Assert.Greater(bank - bottom, 3.5f, $"谷 x={x}");
            }
            // 丸木橋のたもとは、谷の両がわの土手の上
            Vector2 bc = NightLayout.BridgeCenter;
            Assert.Greater(NightLayout.Height(bc.x, bc.y - NightLayout.BridgeHalf + 1f) - NightLayout.Height(bc.x, bc.y), 3f);
            Assert.Greater(NightLayout.Height(bc.x, bc.y + NightLayout.BridgeHalf - 1f) - NightLayout.Height(bc.x, bc.y), 3f);
            // 星見の丘は、まわりより高い
            Assert.Greater(NightLayout.Height(NightLayout.Hill.x, NightLayout.Hill.y), NightLayout.Height(NightLayout.Hill.x, NightLayout.Hill.y - 24f) + 5f);
            // 池：まん中は深い水、ほとりは陸
            Vector2 pc = NightLayout.Pond;
            Assert.Less(NightLayout.Height(pc.x, pc.y), NightLayout.PondLevel - 1f, "池は深い");
            Assert.IsTrue(Areas.Night.IsUnderwater(new Vector3(pc.x, NightLayout.Height(pc.x, pc.y), pc.y)));
            Assert.IsFalse(Areas.Night.IsUnderwater(NightLayout.Ground(pc.x - NightLayout.PondRadii.x - 3f, pc.y)), "池のほとりは陸");
            // ぬた場：あさいどろ水（しゃくとりむしが入って歩ける）
            Vector2 w = NightLayout.Wallow;
            Assert.AreEqual(NightLayout.WallowLevel, Areas.Night.WaterLevelAt(w.x, w.y));
            Assert.Less(NightLayout.Height(w.x, w.y), NightLayout.WallowLevel, "どろ水がたまる");
            Assert.Less(NightLayout.WallowLevel - NightLayout.Height(w.x, w.y), AreaLayout.WadeDepth + 0.01f, "あさい");
            Assert.IsFalse(Areas.Night.IsUnderwater(NightLayout.Ground(w.x, w.y)));
            // 外周はせり上がる
            Assert.Greater(NightLayout.Height(0f, 90f), NightLayout.Height(0f, 55f) + 4f);
        }

        [Test]
        public void NightThings_AreBuilt()
        {
            var names = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r => r.name).ToList();
            foreach (var n in new[] { "Night_LogBridge", "Night_StarRock", "Night_RoostTree", "Night_MinoTree", "Night_RotLog" })
                CollectionAssert.Contains(names, n, n);
            // 丸木橋の上は、谷の上を歩いてわたれる
            Vector2 bc = NightLayout.BridgeCenter;
            Assert.IsTrue(Physics.Raycast(new Vector3(bc.x, 20f, bc.y), Vector3.down, out var hit, 40f, ShakuConst.SurfaceMask), "橋のまん中");
            Assert.AreEqual("Night_LogBridge", hit.collider.name);
            Assert.Greater(hit.point.y, NightLayout.Height(bc.x, bc.y) + 3f, "谷底より、ずっと上");
            // シマエナガがとまる枝と、ミノムシのぶらさがる枝
            Assert.GreaterOrEqual(_gen.RoostPerches.Count, 3, "ねぐらの木の太い枝");
            foreach (var p in _gen.RoostPerches)
                Assert.Greater(p.y - NightLayout.Height(p.x, p.z), 10f, "高い枝の上");
            Assert.GreaterOrEqual(_gen.Bagworms.Count, 3, "ミノムシの木の低い枝");
            foreach (var (a, len) in _gen.Bagworms)
            {
                float bottom = a.y - len - 2f;   // みのの下のはし（みのは 1.3 倍で、長さ 2 ほど）
                Assert.Greater(bottom, NightLayout.Height(a.x, a.z) + 0.5f, "みのは、地面にとどかない");
                Assert.Less(a.y - NightLayout.Height(a.x, a.z), 14f, "しゃくとりむしが、下から見上げられる高さ");
            }
            // 光る倒木の、ツキヨタケ
            Assert.GreaterOrEqual(_gen.GlowCaps.Count, 6);
            // 水（池とぬた場）は、本物の水（白い板ではない）
            var waters = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.gameObject.layer == ShakuConst.WaterLayer).ToList();
            var pond = waters.Single(r => r.name == "NightPond");
            var mud = waters.Single(r => r.name == "NightWallow");
            var mpb = new MaterialPropertyBlock();
            pond.GetPropertyBlock(mpb);
            Color refl = mpb.GetColor("_ReflectColor");
            Assert.Less(refl.maxColorComponent, 0.5f, "夜の池の照り返しは、夜空の色（白くならない）");
            Assert.Greater(refl.b, refl.r, "藍色");
            mud.GetPropertyBlock(mpb);
            Color deep = mpb.GetColor("_DeepColor");
            Assert.Greater(deep.r, deep.b, "ぬた場は、茶色いどろ水");
        }

        [Test]
        public void Dewdrops_AreOnDryLandAndOnTheNightThings()
        {
            Assert.AreEqual(30, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
            {
                Assert.IsTrue(NightLayout.InPlayArea(d), $"しずく {d} が外");
                Assert.IsFalse(Areas.Night.IsUnderwater(d), $"しずく {d} が水の中");
                Assert.IsFalse(NightLayout.InWallow(d.x, d.z, 0f) && d.y < NightLayout.WallowLevel + 0.3f, $"しずく {d} が、どろの中");
            }
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
                for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                    Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 3f);
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y - NightLayout.Height(d.x, d.z) > 3f), 8, "橋・岩・木の枝・倒木などの上");
            Vector2 bc = NightLayout.BridgeCenter;
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Mathf.Abs(d.x - bc.x) < 2f && Mathf.Abs(d.z - bc.y) < 8f && d.y > NightLayout.Height(bc.x, bc.y) + 3f), "丸木橋の上");
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Vector2.Distance(new Vector2(d.x, d.z), NightLayout.StarRock) < 6f && d.y > NightLayout.Height(NightLayout.StarRock.x, NightLayout.StarRock.y) + 2f), "星見の岩の上");
        }

        [Test]
        public void WarpRing_ToTheXmasVillage_IsBuiltAfterTheDewdrops()
        {
            // クリスマス村への光の輪は、しずくのあとで作る（しずくは 30 こ、光の輪からはなれている）
            var ring = _gen.Gates.Single(g => g.def.targetArea == "xmas");
            Assert.IsTrue(ring.def.warp);
            Assert.IsTrue(ring.def.late);
            foreach (var d in _gen.DewdropPoints)
                Assert.Greater(Vector2.Distance(new Vector2(d.x, d.z), ring.def.position), ring.def.radius + 3f, $"しずく {d} が光の輪に近すぎる");
            var names = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r => r.name).ToList();
            CollectionAssert.Contains(names, "Xmas_WarpRing");
            CollectionAssert.Contains(names, "Xmas_WarpLights");
            Assert.IsFalse(Physics.Raycast(ring.position + Vector3.up * 3f - ring.inward * 3f, ring.inward, 6f, ShakuConst.SurfaceMask), "光の輪の中は、通りぬけられる");
        }

        [Test]
        public void Landmarks_ArriveOnDryLand()
        {
            foreach (var lm in NightLayout.Landmarks)
            {
                Vector3 p = _gen.ArrivalPoint(lm, out var f);
                Assert.IsTrue(NightLayout.InPlayArea(p), lm.name);
                Assert.IsFalse(Areas.Night.IsUnderwater(p + Vector3.down * 0.02f), lm.name + "：水の中に着かない");
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), lm.TravelView.from), 3.5f, lm.name + "：景色の所に着く");
            }
        }

        [Test]
        public void NightCreatures_LiveInTheNightForest()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in NightSpecies)
            {
                CollectionAssert.Contains(ids, id, $"{id} が夜の森にいる");
                var sp = SpeciesCatalog.Get(id);
                Assert.IsTrue(GameUI.LivesIn(sp, "夜の森"), id);
                Assert.IsFalse(sp.IsRare, id);
                Assert.IsNotNull(_gen.assets.TryGet(sp.body), $"{id} の体のメッシュ");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.IsTrue(CreatureRig.HasLegs(sp.rig), $"{id} の脚");
                foreach (var part in sp.parts) Assert.IsNotNull(_gen.assets.TryGet(part.mesh), $"{id} の {part.mesh}");
                Assert.IsTrue(GameUI.LivesIn(sp, "夜の森"), id);
                Assert.IsFalse(GameUI.LivesIn(sp, "森"), id + "：昼の森のしぼりこみには入らない");
            }
            foreach (var id in new[] { "firefly", "koumori", "frog", "snail", "fukurou", "tanuki", "beetle", "korogi" })
            {
                CollectionAssert.Contains(ids, id);
                Assert.IsTrue(GameUI.LivesIn(SpeciesCatalog.Get(id), "夜の森"), id + "：図鑑のすみかに夜の森");
            }
            // ミノムシは、枝から糸でぶらさがる（地面には置かない）
            foreach (var g in _gen.Mobs.Where(m => m.species == "minomushi"))
            {
                Assert.Greater(g.height, 0.5f, "糸の長さ");
                Assert.Greater(g.center.y - NightLayout.Height(g.center.x, g.center.z), 4f, "枝の所");
                Assert.AreEqual(WorldGenerator.CreatureHome.Hanging, _gen.HomeOf(g));
            }
            // イノシシのそばに、ウリボウが 3 びき
            var mom = _gen.Mobs.Single(m => m.species == "inoshishi");
            var kids = _gen.Mobs.Where(m => m.species == "uribou").ToList();
            Assert.AreEqual(3, kids.Sum(k => k.count));
            Assert.Less(kids.Min(k => Vector3.Distance(k.center, mom.center)), 8f, "おかあさんのそば");
            // シマエナガは、ねぐらの木の枝にとまる（1 羽）
            var tit = _gen.Mobs.Single(m => m.species == "shimaenaga");
            Assert.AreEqual(1, tit.count);
            Assert.GreaterOrEqual(tit.path.Count, 3);
            foreach (var g in _gen.Mobs) Assert.IsTrue(NightLayout.InPlayArea(g.center), $"{g.species} {g.center}");
        }

        [Test]
        public void NightCreatures_HaveTheirOwnWaysOfMoving()
        {
            Assert.AreEqual(MobKind.Bird, SpeciesCatalog.Get("shimaenaga").kind);
            Assert.AreEqual("sparrow", SpeciesCatalog.Get("shimaenaga").BehaviorId, "シマエナガは、スズメと同じ動き方");
            Assert.AreEqual(MobKind.Hopper, SpeciesCatalog.Get("kirigirisu").kind);
            Assert.IsTrue(SpeciesCatalog.Get("zatoumushi").climbs, "ザトウムシは、幹や倒木を登る");
            Assert.Greater(SpeciesCatalog.Get("inoshishi").fleeRadius, 4f, "イノシシは、おくびょう");
            foreach (var id in NightSpecies)
            {
                var sp = SpeciesCatalog.Get(id);
                foreach (var b in Friends.BehaviorsOf(id))
                {
                    switch (b.key)
                    {
                        case "fly": Assert.AreEqual(MobKind.Bird, sp.kind, id); break;
                        case "climb": Assert.IsTrue(sp.climbs, id + "：登る"); break;
                        case "hop": Assert.AreEqual(MobKind.Hopper, sp.kind, id); break;
                        case "sway": case "peek": Assert.AreEqual("minomushi", id); break;
                        case "follow": Assert.AreEqual("uribou", id); break;
                    }
                }
            }
            // 大きさ：イノシシとウリボウは、ほかのけものと同じように、ちぢめて描く（虫は、そのまま）
            Assert.Less(SpeciesCatalog.Get("inoshishi").scale, SpeciesCatalog.Get("uribou").scale);
            foreach (var id in new[] { "minomushi", "zatoumushi", "kirigirisu" }) Assert.GreaterOrEqual(SpeciesCatalog.Get(id).scale, 1f, id);
            // 部品の付け方（Blender で作った形から）
            Assert.Less(CreatureRig.KirigirisuWingRoot.x, 0f, "キリギリスの前ばねは、右（-X）");
            Assert.Less(CreatureRig.MinoMouth.y, 0f, "ミノムシの口は、糸の付く所より下");
            Assert.IsNotNull(_gen.assets.TryGet("Shimaenaga_WingFolded"), "とまっているときの、たたんだ羽");
        }

        [Test]
        public void Map_AndLight_ShowTheNight()
        {
            var tex = _gen.MapTexture;
            Assert.IsNotNull(tex);
            Color Px(Vector2 w) => tex.GetPixelBilinear((w.x + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent), (w.y + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent));
            Color pond = Px(NightLayout.Pond);
            Assert.Greater(pond.b, pond.r, "池は青い");
            Color log = Px(NightLayout.GlowLog);
            Assert.Greater(log.g, log.r + 0.2f, "光る倒木は、青緑");
            var look = AreaAtmosphere.For("night");
            Assert.Greater(look.sun.b, look.sun.r + 0.2f, "青い月あかり");
            Assert.Less(look.sunIntensity, AreaAtmosphere.For("forest").sunIntensity, "昼の森より暗い");
            Assert.Greater(look.sunIntensity, 0.7f, "でも、足もとが見えるくらいは明るい");
            Assert.Less(look.fog.maxColorComponent, 0.3f, "こい藍色の霧");
            Assert.IsTrue(AreaAtmosphere.IsNight("night"));
            Assert.IsFalse(AreaAtmosphere.IsNight("shrine"));
        }
    }
}

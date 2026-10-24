using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// 神社のエリア（参道・大鳥居・石灯籠・基壇と石段・お社と鈴・キツネの像・手水舎・ご神木・千本鳥居・池と太鼓橋・しずく・名所・いきもの・地図）と、
    /// 神社の 5 しゅのいきものを、実際に組み立てて確かめる。
    /// </summary>
    public class ShrineTests
    {
        static WorldGenerator _gen;

        static readonly string[] ShrineSpecies = { "kitsune", "fukurou", "tanuki", "ga", "kanabun", "suzumushi", "korogi" };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Shrine;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void Shrine_IsTheSeventhArea_ReachedFromThePark()
        {
            Assert.AreSame(Areas.Shrine, Areas.All[6]);
            Assert.AreSame(Areas.Shrine, Areas.Get("shrine"));
            Assert.AreEqual("神社", Areas.Shrine.DisplayName);
            Assert.AreEqual("神社のしずく", Areas.Shrine.DropName);
            Assert.AreEqual(6000, Areas.Shrine.DropIdOffset);
            Assert.AreEqual(30, Areas.Shrine.DropCount);
            Assert.AreSame(Areas.Shrine, Areas.AreaOfDrop(6010));
            // 公園の北東のさくの所のトンネル（あとから作る。公園のしずくは変えない）と、神社から公園へもどるトンネル
            var toShrine = Areas.Park.Gates.Single(g => g.targetArea == "shrine");
            Assert.IsTrue(toShrine.late, "公園のしずくを変えないよう、あとから作る");
            Assert.IsFalse(toShrine.door);
            Assert.AreEqual(58.5f, toShrine.position.magnitude, 0.5f, "公園のふち");
            Assert.Greater(Vector2.Distance(toShrine.position, ParkLayout.Gate), 30f, "森へのトンネルとは、はなれている");
            Assert.Greater(Vector2.Distance(toShrine.position, ParkLayout.Kunugi), 20f, "クヌギの木とは、はなれている");
            var back = Areas.Shrine.Gates.Single(g => g.targetArea == "park");   // 夜の森へのトンネルは、NightTests で
            Assert.AreEqual("park", back.targetArea);
            Assert.IsTrue(Areas.Park.ArrivalFrom("shrine", out var parkArrive, out _));
            Assert.Less(Vector2.Distance(parkArrive, toShrine.position), 8f, "神社からもどると、トンネルの前に出る");
            var ids = ShrineLayout.Landmarks.Select(l => l.id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(60, 9).ToList(), ids);
            Assert.IsTrue(ShrineLayout.Landmarks.All(l => l.areaId == "shrine"));
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Select(l => l.id).Distinct().Count(), "名所の番号は重ならない");
        }

        [Test]
        public void Ground_HasAFlatApproach_AHighPlatform_StoneSteps_AndAPond()
        {
            // 参道は平ら。基壇は高く、石段の坂でつながる
            for (float z = -54f; z < ShrineLayout.StepsZ0 - 1f; z += 4f)
                Assert.AreEqual(ShrineLayout.FlatHeight, ShrineLayout.Height(0f, z), 0.12f, $"参道 z={z}");
            Assert.AreEqual(ShrineLayout.PlatformY, ShrineLayout.Height(0f, 40f), 0.05f, "基壇の上");
            Assert.AreEqual(ShrineLayout.PlatformY, ShrineLayout.Height(-20f, 30f), 0.05f);
            float prev = -1f;
            for (int i = 0; i < ShrineLayout.StepCount; i++)
            {
                Vector2 st = ShrineLayout.Step(i);
                Assert.Greater(st.x, prev, "石段は、一段ずつ上がる");
                Assert.Less(st.x - Mathf.Max(prev, ShrineLayout.FlatHeight), 0.8f, "一段は低い（しゃくとりむしが登れる）");
                prev = st.x;
            }
            Assert.AreEqual(ShrineLayout.PlatformY, prev, 0.01f, "いちばん上の段は、基壇と同じ高さ");
            // 池：まん中は深い水、ふちは陸
            Vector2 pc = ShrineLayout.Pond;
            Assert.Less(ShrineLayout.Height(pc.x, pc.y), ShrineLayout.PondLevel - 1f, "池は深い");
            Assert.IsTrue(Areas.Shrine.IsUnderwater(new Vector3(pc.x, ShrineLayout.Height(pc.x, pc.y), pc.y)), "池の底は水の中");
            Assert.IsFalse(Areas.Shrine.IsUnderwater(ShrineLayout.Ground(pc.x - ShrineLayout.PondRadii.x - 3f, pc.y + 8f)), "池のほとりは陸");
            Assert.AreEqual(ShrineLayout.PondLevel, Areas.Shrine.WaterLevelAt(pc.x, pc.y));
            // 手水舎の水盤の水は、ふちより低く、底より高い
            Assert.AreEqual(ShrineLayout.BasinWater, Areas.Shrine.WaterLevelAt(ShrineLayout.Chozuya.x, ShrineLayout.Chozuya.y));
            Assert.Less(ShrineLayout.BasinWater, ShrineLayout.ChozuyaFloor + 5f, "ふちより低い");
            Assert.Greater(ShrineLayout.BasinWater, ShrineLayout.ChozuyaFloor + ShrineLayout.BasinFloorUp, "底より高い");
            // 外周（鎮守の森の土手）はせり上がる
            Assert.Greater(ShrineLayout.Height(0f, 90f), ShrineLayout.Height(0f, 50f) + 4f);
        }

        [Test]
        public void ShrineBuildings_AreBuilt()
        {
            var names = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r => r.name).ToList();
            foreach (var n in new[] { "Shrine_Torii", "Shrine_Hall", "Shrine_Chozuya", "Shrine_Tree", "Shrine_Bridge", "Shrine_Hokora", "Shrine_Fox_A", "Shrine_Fox_B" })
                CollectionAssert.Contains(names, n, n);
            Assert.AreEqual(ShrineLayout.Lanterns.Length, names.Count(n => n == "Shrine_Lantern"), "石灯籠");
            Assert.AreEqual(ShrineLayout.Lanterns.Length, _gen.LanternLights.Count, "灯籠のあかり");
            Assert.GreaterOrEqual(_gen.SmallTorii.Count, 8, "千本鳥居（小さな鳥居が、トンネルのようにならぶ）");
            // 鳥居の下はくぐれる（柱のあいだに、物がない）
            Vector3 under = new Vector3(ShrineLayout.GreatTorii.x, ShrineLayout.FlatHeight + 1f, ShrineLayout.GreatTorii.y);
            Assert.IsFalse(Physics.CheckSphere(under, 0.6f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore), "大鳥居の下をくぐれる");
            foreach (var t in _gen.SmallTorii)
                Assert.IsFalse(Physics.CheckSphere(t + Vector3.up * 1.2f, 0.5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore), $"千本鳥居の下をくぐれる {t}");
            // 石段の上は、平らで歩ける
            for (int i = 0; i < ShrineLayout.StepCount; i++)
            {
                Vector2 st = ShrineLayout.Step(i);
                Assert.IsTrue(Physics.Raycast(new Vector3(2f, st.x + 2f, st.y), Vector3.down, out var hit, 4f, ShakuConst.SurfaceMask), $"石段 {i}");
                Assert.AreEqual(st.x, hit.point.y, 0.12f, $"石段 {i} の上の面");
                Assert.Greater(hit.normal.y, 0.95f);
            }
            // 鈴緒：お社の前にたれていて、さわると鈴が鳴る
            var bell = Object.FindObjectsByType<ShrineBell>(FindObjectsSortMode.None).Single();
            Assert.Greater(bell.ropeHigh.y, bell.ropeLow.y + 8f);
            Assert.Greater(bell.bell.y, bell.ropeHigh.y);
            Assert.IsTrue(bell.Touches(Vector3.Lerp(bell.ropeLow, bell.ropeHigh, 0.2f) + Vector3.right * 0.5f));
            Assert.IsFalse(bell.Touches(bell.ropeLow + Vector3.right * 3f));
            Assert.Less(bell.ropeLow.y - ShrineLayout.PlatformY, 7.5f, "鈴緒のはしは、お社の階段の上で、しゃくとりむしの頭がとどく高さ");
            // 水（池と水盤）は、本物の水（白い板ではない）
            var waters = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.gameObject.layer == ShakuConst.WaterLayer).Select(r => r.name).ToList();
            CollectionAssert.Contains(waters, "ShrinePond");
            CollectionAssert.Contains(waters, "ShrineBasin");
        }

        [Test]
        public void Dewdrops_AreOnDryLandAndOnTheShrineThings()
        {
            Assert.AreEqual(30, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
            {
                Assert.IsTrue(ShrineLayout.InPlayArea(d), $"しずく {d} が外");
                Assert.IsFalse(Areas.Shrine.IsUnderwater(d), $"しずく {d} が水の中");
            }
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
                for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                    Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 3f);
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y - ShrineLayout.Height(d.x, d.z) > 3f), 12, "鳥居・灯籠・お社・キツネの像などの上");
            Vector2 torii = ShrineLayout.GreatTorii;
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Mathf.Abs(d.z - torii.y) < 3f && d.y > 30f), "大鳥居の笠木の上");
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Vector2.Distance(new Vector2(d.x, d.z), ShrineLayout.Saisen) < 3f && d.y > ShrineLayout.PlatformY + 3f), "さい銭箱の上");
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Vector2.Distance(new Vector2(d.x, d.z), ShrineLayout.Pond) < 2f && d.y > 4f), "太鼓橋のてっぺん");
        }

        [Test]
        public void Landmarks_ArriveOnDryLand()
        {
            foreach (var lm in ShrineLayout.Landmarks)
            {
                Vector3 p = _gen.ArrivalPoint(lm, out var f);
                Assert.IsTrue(ShrineLayout.InPlayArea(p), lm.name);
                Assert.IsFalse(Areas.Shrine.IsUnderwater(p + Vector3.down * 0.02f), lm.name + "：水の中に着かない");
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), lm.TravelView.from), 3.5f, lm.name + "：景色の所に着く");
            }
            // キツネの像へ移動すると、台座の上（キツネと同じ高さ）に着く
            var foxes = ShrineLayout.Landmarks.Single(l => l.id == 66);
            Vector3 top = _gen.ArrivalPoint(foxes, out _);
            Assert.Greater(top.y, ShrineLayout.PlatformY + 5f, "台座の上");
        }

        [Test]
        public void ShrineCreatures_LiveAroundTheShrine()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in ShrineSpecies)
            {
                CollectionAssert.Contains(ids, id, $"{id} が神社にいる");
                var sp = SpeciesCatalog.Get(id);
                Assert.IsTrue(GameUI.LivesIn(sp, "神社"), id);
                Assert.IsFalse(sp.IsRare, id);
                Assert.IsNotNull(_gen.assets.TryGet(sp.body), $"{id} の体のメッシュ");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.IsTrue(CreatureRig.HasLegs(sp.rig), $"{id} の脚");
                foreach (var part in sp.parts) Assert.IsNotNull(_gen.assets.TryGet(part.mesh), $"{id} の {part.mesh}");
                Assert.IsTrue(GameUI.LivesIn(sp, "神社"), id);
            }
            foreach (var id in new[] { "crow", "dragonfly", "spider", "ladybug" })
            {
                CollectionAssert.Contains(ids, id);
                Assert.IsTrue(GameUI.LivesIn(SpeciesCatalog.Get(id), "神社"), id + "：図鑑のすみかに神社");
            }
            // フクロウは、高い所にとまる（ご神木の枝・鳥居・お社の屋根・奥の宮）
            var owl = _gen.Mobs.Single(m => m.species == "fukurou");
            Assert.GreaterOrEqual(owl.path.Count, 4);
            Assert.Greater(owl.center.y - ShrineLayout.Height(owl.center.x, owl.center.z), 10f, "高い所");
            // オオミズアオは、灯籠のあかりのそば
            foreach (var g in _gen.Mobs.Where(m => m.species == "ga"))
            {
                CollectionAssert.AreEqual(_gen.LanternLights.ToList(), g.path, "あかりの場所");
                Assert.Less(_gen.LanternLights.Min(l => Vector3.Distance(l, g.center)), 1f);
            }
            // カナブンは、ご神木の幹にとまる（幹の面の向き）
            foreach (var g in _gen.Mobs.Where(m => m.species == "kanabun"))
            {
                Assert.Less(Vector2.Distance(new Vector2(g.center.x, g.center.z), ShrineLayout.Tree), 14f, "ご神木の幹");
                Assert.Greater(g.center.y - ShrineLayout.Height(g.center.x, g.center.z), 1.5f, "幹の上（地面ではない）");
            }
            // キツネとタヌキは、陸の上
            foreach (var g in _gen.Mobs.Where(m => m.species == "kitsune" || m.species == "tanuki"))
                Assert.IsTrue(Areas.Shrine.IsLand(g.center.x, g.center.z, 0.1f), $"{g.species} {g.center}");
            foreach (var g in _gen.Mobs) Assert.IsTrue(ShrineLayout.InPlayArea(g.center), $"{g.species} {g.center}");
        }

        [Test]
        public void ShrineCreatures_HaveTheirOwnWaysOfMoving()
        {
            Assert.AreEqual(MobKind.Bird, SpeciesCatalog.Get("fukurou").kind);
            Assert.AreEqual(MobKind.Flutter, SpeciesCatalog.Get("ga").kind);
            Assert.IsTrue(SpeciesCatalog.Get("kanabun").climbs, "カナブンは、幹を登る");
            Assert.Greater(SpeciesCatalog.Get("kitsune").fleeRadius, 4f, "キツネは、近すぎると、はなれる");
            Assert.Greater(SpeciesCatalog.Get("fukurou").discoverRadius, 25f, "高い枝の上のフクロウも、見つけられる");
            foreach (var id in ShrineSpecies)
            {
                var sp = SpeciesCatalog.Get(id);
                Assert.AreEqual(id, sp.BehaviorId, id + "：自分の動き");
                foreach (var b in Friends.BehaviorsOf(id))
                {
                    switch (b.key)
                    {
                        case "fly": Assert.AreEqual(MobKind.Bird, sp.kind, id); break;
                        case "climb": Assert.IsTrue(sp.climbs, id + "：登る"); break;
                        case "rest": case "wings": case "circle": Assert.AreEqual(MobKind.Flutter, sp.kind, id + "：" + b.key); break;
                    }
                }
            }
            // 部品の付け方（Blender で作った形から）
            Assert.Greater(CreatureRig.FukurouNeck.y, 10f, "フクロウの首は、体の上");
            Assert.Less(CreatureRig.KanabunWingRoot.x, 0f, "カナブンの後ろばねは、右（-X）");
            Assert.Less(CreatureRig.GaWingRoot.x, 0f);
            Assert.IsNotNull(_gen.assets.TryGet("Fukurou_WingFolded"), "とまっているときの、たたんだ羽");
        }

        [Test]
        public void Map_ShowsTheShrine()
        {
            var tex = _gen.MapTexture;
            Assert.IsNotNull(tex);
            Color Px(Vector2 w) => tex.GetPixelBilinear((w.x + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent), (w.y + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent));
            Color pond = Px(ShrineLayout.Pond + new Vector2(0f, 6f));
            Assert.Greater(pond.b, pond.r, "池は青い");
            Color torii = Px(ShrineLayout.GreatTorii + new Vector2(6f, 0f));
            Assert.Greater(torii.r, torii.g + 0.2f, "大鳥居は朱色");
            var look = AreaAtmosphere.For("shrine");
            Assert.Greater(look.sun.r, look.sun.b + 0.3f, "夕ぐれの、あたたかい日ざし");
        }
    }
}

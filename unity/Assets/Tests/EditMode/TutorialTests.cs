using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// チュートリアルの「練習の森」（森をもとにした、練習用の小さな森）を、実際に組み立てて確かめる：
    /// 本番のエリアの数に入らない・小道ぞいの練習の場所（道のしずく・登れる切り株・切り株のてっぺんからねらえる赤キノコ・
    /// かさのふちから糸でおりる先・かたつむり・森へのトンネル）。
    /// </summary>
    public class TutorialTests
    {
        static WorldGenerator _gen;

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Tutorial;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        static float Above(Vector3 p) => p.y - TutorialLayout.Height(p.x, p.z);

        [Test]
        public void PracticeForest_IsNotOneOfTheAreas_AndLeadsToTheForest()
        {
            CollectionAssert.DoesNotContain(Areas.All, Areas.Tutorial, "本番のエリアの一覧に入らない（しずく・名所の数、地図、図鑑、デモ映像）");
            Assert.AreSame(Areas.Tutorial, Areas.Get("tutorial"));
            Assert.AreEqual("練習の森", Areas.Tutorial.DisplayName);
            Assert.AreEqual(270, Areas.TotalDrops, "本番のしずくの数は、そのまま");
            Assert.AreEqual(70, Areas.TotalLandmarks, "本番の名所の数は、そのまま");
            var ids = Areas.AllLandmarks().Select(l => l.id).ToList();
            foreach (var lm in TutorialLayout.Landmarks)
            {
                Assert.AreEqual("tutorial", lm.areaId);
                CollectionAssert.DoesNotContain(ids, lm.id, "本番の名所と、番号が重ならない");
            }
            Assert.Greater(Areas.Tutorial.DropIdOffset, Areas.Night.DropIdOffset + Areas.Night.DropCount);
            var gate = Areas.Tutorial.Gates.Single();
            Assert.AreEqual("forest", gate.targetArea, "北のトンネルは、本番の森へ");
            Assert.Greater(gate.position.y, 25f, "トンネルは北のはし");
            Assert.Less(TutorialLayout.Spawn.y, -20f, "はじまりは南のはし");
            Assert.AreEqual(-999f, TutorialLayout.WaterLevelAt(0f, 0f), "水はない");
        }

        [Test]
        public void Stations_LieAlongOnePath_WithShortWalksBetween()
        {
            // はじまり → 道のしずく → 切り株 → 赤キノコ → 糸でおりる先 → 草原 → かたつむり → トンネル（どれも近い）
            Vector2[] order = { TutorialLayout.Spawn, TutorialLayout.FirstDew, TutorialLayout.Stump, TutorialLayout.Mushroom, TutorialLayout.SilkLanding, TutorialLayout.Meadow, TutorialLayout.SnailCap, TutorialLayout.Gate };
            for (int i = 0; i < order.Length - 1; i++)
            {
                float d = Vector2.Distance(order[i], order[i + 1]);
                Assert.Less(d, 22f, $"{i} から {i + 1} まで");
                Assert.Greater(d, 4f, $"{i} と {i + 1} は、別の所");
            }
            foreach (var p in order) Assert.IsTrue(Areas.Tutorial.InPlayArea(TutorialLayout.Ground(p.x, p.y)), $"{p} は遊べる所");
            // 小道は、なだらか（坂で止まらない）
            foreach (var t in TutorialLayout.Trails)
                for (int i = 0; i < t.Length - 1; i++)
                    for (float k = 0f; k <= 1f; k += 0.1f)
                    {
                        Vector2 p = Vector2.Lerp(t[i], t[i + 1], k);
                        Assert.Greater(TutorialLayout.Normal(p.x, p.y).y, 0.9f, $"小道 {p}");
                    }
        }

        [Test]
        public void Dewdrops_AreOnThePath_OnTheStump_AndOnTheMushroomCap()
        {
            Assert.AreEqual(Areas.Tutorial.DropCount, _gen.DewdropPoints.Count, "しずくは 3 つ");
            Vector3 first = TutorialLayout.Ground(TutorialLayout.FirstDew.x, TutorialLayout.FirstDew.y);
            Assert.Less(Vector3.Distance(_gen.DewdropPoints[0], first), 0.6f, "道の上");
            Assert.Less(Vector3.Distance(_gen.DewdropPoints[1], _gen.TutorialStumpTop), 1.6f, "切り株のてっぺん");
            Assert.Less(Vector3.Distance(_gen.DewdropPoints[2], _gen.TutorialCapTop), 1.6f, "赤キノコのかさの上");
        }

        [Test]
        public void Stump_IsTallEnoughToClimb_AndItsEdgeIsACliff()
        {
            float top = Above(_gen.TutorialStumpTop);
            Assert.Greater(top, 3f, "登りがいのある高さ");
            Assert.Less(top, 5f, "練習なので、高すぎない");
            Assert.Greater(top, 2.6f + 0.2f, "てっぺんの名所に入る高さ");
            // ふちの先は、下まで 1.6 より深い（歩いておりられない。糸を出せる）
            Vector3 c = _gen.TutorialStumpTop;
            Vector3 beyond = new Vector3(TutorialLayout.Stump.x, c.y + 0.5f, TutorialLayout.Stump.y + 4.2f);
            Assert.IsTrue(Physics.Raycast(beyond, Vector3.down, out var hit, 20f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore));
            Assert.Greater(c.y - hit.point.y, 1.6f, "ふちの先は、がけ");
        }

        [Test]
        public void MushroomCap_CanBeReachedWithTheAimedSilk_FromTheStumpTop()
        {
            float top = Above(_gen.TutorialCapTop);
            Assert.Greater(top, 4.2f, "かさの上は、切り株より高い");
            Assert.Less(top, 6f);
            Assert.Greater(_gen.TutorialCapTop.y, _gen.TutorialStumpTop.y + 0.4f, "切り株のてっぺんより、少し高い");
            Assert.Less(_gen.TutorialCapTop.y, _gen.TutorialStumpTop.y + 1.6f, "切り株の上のカメラからは、かさの上が見える");
            // 切り株のてっぺんのふち（キノコの方）から、かさの上まで、糸のとどく距離（12）より近く、あいだに物がない
            Vector2 toM = (TutorialLayout.Mushroom - TutorialLayout.Stump).normalized;
            Vector3 edge = new Vector3(TutorialLayout.Stump.x + toM.x * 2.6f, _gen.TutorialStumpTop.y + 0.4f, TutorialLayout.Stump.y + toM.y * 2.6f);
            Vector3 dir = _gen.TutorialCapTop - edge;
            Assert.Less(dir.magnitude, 11f, "糸のとどく距離");
            Assert.IsTrue(Physics.Raycast(edge, dir.normalized, out var hit, dir.magnitude + 1f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore), "かさに当たる");
            StringAssert.StartsWith("Mushroom_Red", hit.collider.name, "とちゅうに、ほかの物がない");
            Assert.Greater(hit.normal.y, 0.5f, "かさの上の面に当たる（うらではない）");
            // 切り株とかさのあいだは、歩いてまたげない（糸でわたる）
            float gap = Vector2.Distance(TutorialLayout.Stump, TutorialLayout.Mushroom) - 3.6f - 2.6f;
            Assert.Greater(gap, 2f, "あいだは、体よりずっと広い");
        }

        [Test]
        public void CapEdge_IsADropForTheSilk_AndTheLandingIsClear()
        {
            // かさのふちの先は、下まで深い（糸を出して、おりる）
            Vector3 c = _gen.TutorialCapTop;
            Vector2 n = (TutorialLayout.SilkLanding - TutorialLayout.Mushroom).normalized;
            Vector3 beyond = new Vector3(TutorialLayout.Mushroom.x + n.x * 3.1f, c.y + 0.5f, TutorialLayout.Mushroom.y + n.y * 3.1f);
            Assert.IsTrue(Physics.Raycast(beyond, Vector3.down, out var hit, 20f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore));
            Assert.Greater(c.y - hit.point.y, 3f, "ふちの先は、高いがけ");
            // おりる先は、ひらけた地面
            Vector3 land = TutorialLayout.Ground(TutorialLayout.SilkLanding.x, TutorialLayout.SilkLanding.y);
            Assert.IsTrue(Physics.Raycast(land + Vector3.up * 10f, Vector3.down, out var g, 20f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore));
            StringAssert.StartsWith("Terrain_", g.collider.name, "おりる先には、物がない");
        }

        [Test]
        public void Snail_LivesAtTheFootOfTheSmallMushroom_AndNoOtherCreatures()
        {
            var snails = _gen.Mobs.Where(m => m.species == "snail").ToList();
            Assert.AreEqual(1, snails.Count, "かたつむりが 1 ぴき");
            Assert.Less(Vector2.Distance(new Vector2(snails[0].center.x, snails[0].center.z), TutorialLayout.SnailCap), 4f, "小さな赤キノコの根もと");
            Assert.AreEqual(1, _gen.Mobs.Count, "練習の森には、ほかのいきものはいない（練習のじゃまをしない）");
        }

        [Test]
        public void Ground_LooksLikeTheForest_AndTheMapIsDrawn()
        {
            // 苔の緑（森と同じ色の組み合わせ）
            int green = 0, n = 0;
            for (float x = -20f; x <= 20f; x += 5f)
                for (float z = -20f; z <= 20f; z += 5f)
                {
                    Color c = TutorialLayout.GroundColor(x, z, TutorialLayout.Height(x, z), TutorialLayout.Normal(x, z));
                    n++;
                    if (c.g > c.r && c.g > c.b) green++;
                }
            Assert.Greater(green, n / 2, "苔の緑が多い");
            Assert.IsNotNull(_gen.MapTexture, "地図");
            Assert.IsTrue(_gen.Gates.Count == 1 && _gen.Gates[0].def.targetArea == "forest", "森へのトンネル");
        }
    }
}

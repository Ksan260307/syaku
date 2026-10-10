using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// いきものの居場所の直し（CREATURE_FIXES.md）：どのエリアでも、地面のいきもの・鳥の降りる場所・水の上のいきもの・
    /// 物の上のいきものが、遊びやすさの決まりを守っている。
    /// </summary>
    [TestFixture("forest")]
    [TestFixture("river")]
    [TestFixture("park")]
    [TestFixture("mountain")]
    public class CreatureFixesTests
    {
        readonly string _areaId;
        WorldGenerator _gen;
        AreaLayout _area;

        public CreatureFixesTests(string areaId) => _areaId = areaId;

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            _area = Areas.Get(_areaId);
            Areas.Current = _area;
            _gen.GenerateNow();
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        static string At(Vector3 p) => $"({p.x:F1}, {p.z:F1})";

        [Test]
        public void EveryCreatureFoundAPlace()
        {
            foreach (var l in _gen.CreatureFixLog)
                StringAssert.DoesNotContain("no spot", l, "直す場所が見つからなかった");
        }

        [Test]
        public void GroundCreatures_KeepOffTrailsDewsAndThings()
        {
            int n = 0;
            foreach (var g in _gen.Mobs.Where(m => _gen.HomeOf(m) == WorldGenerator.CreatureHome.Ground))
            {
                Vector2 c = new Vector2(g.center.x, g.center.z);
                Assert.IsNull(_gen.GroundProblem(g, c, false), $"{g.species} {At(g.center)}");
                Assert.IsFalse(WorldGenerator.InsideSolid(g.center + Vector3.up * 0.3f), $"{g.species} {At(g.center)} は物の中にいない");
                n++;
            }
            Assert.Greater(n, 5);
        }

        [Test]
        public void WaterSideCreatures_StayByTheWater()
        {
            foreach (var g in _gen.Mobs.Where(m => WorldGenerator.WaterSide.Contains(m.species) && _gen.HomeOf(m) == WorldGenerator.CreatureHome.Ground))
                Assert.IsTrue(_gen.WaterWithin(new Vector2(g.center.x, g.center.z), 4f), $"{g.species} {At(g.center)} は水べにいる");
        }

        [Test]
        public void Birds_LandAwayFromTrailsViewsLandmarksAndDews()
        {
            foreach (var g in _gen.Mobs.Where(m => _gen.HomeOf(m) == WorldGenerator.CreatureHome.Bird))
            {
                Assert.Greater(g.path.Count, 0);
                for (int i = 0; i < g.path.Count; i++)
                {
                    var p = g.path[i];
                    Assert.IsNull(_gen.BirdSpotProblem(new Vector2(p.x, p.z), g.path, i, g.radius), $"{g.species} の降りる場所 {i} {At(p)}");
                    Assert.Less(p.y - _area.Height(p.x, p.z), 0.5f, $"{g.species} は地面に下りる（切り株の頂・すべり台の上ではない）");
                }
                Assert.Less((g.center - g.path[0]).sqrMagnitude, 0.01f, "はじめは、1 つ目の降りる場所にいる");
            }
        }

        [Test]
        public void WaterCreatures_CanBeFoundFromTheShore()
        {
            foreach (var g in _gen.Mobs.Where(m => _gen.HomeOf(m) == WorldGenerator.CreatureHome.Water))
            {
                var sp = SpeciesCatalog.Get(g.species);
                Assert.GreaterOrEqual(_gen.SkaterReach(g.center, g.radius, sp.discoverRadius), 0.7f,
                    $"{g.species} {At(g.center)} は、岸・葉・石から見つけられる所にいる");
            }
        }

        [Test]
        public void CreaturesOnThings_DoNotSitOnDews()
        {
            foreach (var g in _gen.Mobs.Where(m => _gen.HomeOf(m) == WorldGenerator.CreatureHome.OnObject))
                foreach (var d in _gen.DewdropPoints)
                    if (Mathf.Abs(d.y - g.center.y) < 1f)
                        Assert.Greater(new Vector2(d.x - g.center.x, d.z - g.center.z).magnitude, 1.3f, $"{g.species} {At(g.center)} はしずくの上にいない");
        }

        [Test]
        public void CirclingSnail_GoesRoundOutsideTheMushroomStem()
        {
            var circling = _gen.Mobs.Where(m => m.path.Count == 1 && m.radius > 0f && _gen.HomeOf(m) == WorldGenerator.CreatureHome.Ground).ToList();
            if (_areaId != "forest")
            {
                Assert.AreEqual(0, circling.Count);
                return;
            }
            Assert.AreEqual(1, circling.Count, "大きな赤キノコの根もとをまわる、かたつむり");
            var g = circling[0];
            Vector3 c = g.path[0];
            Assert.AreEqual(g.radius, new Vector2(g.center.x - c.x, g.center.z - c.z).magnitude, 0.05f, "はじめから、まわる輪の上にいる");
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI / 12f;
                Vector3 q = _area.Ground(c.x + Mathf.Cos(a) * g.radius, c.z + Mathf.Sin(a) * g.radius);
                Assert.IsFalse(WorldGenerator.InsideSolid(q + Vector3.up * 0.3f), $"輪 {At(q)} がキノコの柄の中を通らない");
            }
        }
    }
}

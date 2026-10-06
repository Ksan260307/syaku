using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>川辺の地形（関数）の単体テスト。</summary>
    public class RiverLayoutTests
    {
        static readonly float[] SampleZ = { 50f, 40f, 20f, 10f, -10f, -21f, -35f, -55f };

        [Test]
        public void WaterLevel_StepsDownAtTheFallsAndKeepsFlowingDown()
        {
            Assert.AreEqual(RiverLayout.UpperLevel, RiverLayout.WaterLevel(RiverLayout.FallZ + 5f), 1e-4f);
            Assert.AreEqual(RiverLayout.LowerStart, RiverLayout.WaterLevel(RiverLayout.FallZ - 1f), 0.05f);
            Assert.Greater(RiverLayout.UpperLevel - RiverLayout.WaterLevel(RiverLayout.FallZ - 1f), 2.5f, "滝の段差");
            float prev = RiverLayout.WaterLevel(RiverLayout.FallZ - 1f);
            for (float z = RiverLayout.FallZ - 5f; z > -80f; z -= 5f)
            {
                float w = RiverLayout.WaterLevel(z);
                Assert.LessOrEqual(w, prev + 1e-5f, $"z={z} で水が上流へ流れている");
                prev = w;
            }
        }

        [Test]
        public void RiverBed_IsUnderWaterAlongTheChannel()
        {
            foreach (float z in SampleZ)
            {
                if (Mathf.Abs(z - RiverLayout.IslandZ) < 10f) continue;
                float x = RiverLayout.CenterX(z);
                Assert.Less(RiverLayout.Height(x, z), RiverLayout.WaterLevel(z) - 0.8f, $"z={z} の川底が浅すぎる");
                Assert.IsTrue(RiverLayout.IsUnderwater(new Vector3(x, RiverLayout.Height(x, z), z)));
            }
        }

        [Test]
        public void Banks_AreDryLandOnBothSides()
        {
            foreach (float z in SampleZ)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = RiverLayout.CenterX(z) + side * (RiverLayout.HalfWidth(z) + 8f);
                    float h = RiverLayout.Height(x, z);
                    Assert.Greater(h, RiverLayout.WaterLevel(z) + 0.2f, $"z={z} side={side} の岸が水の下");
                    Assert.IsFalse(RiverLayout.IsUnderwater(new Vector3(x, h, z)));
                }
            }
        }

        [Test]
        public void Island_RisesAboveTheWater()
        {
            Vector2 c = RiverLayout.IslandCenter;
            Assert.AreEqual(0f, RiverLayout.IslandRadius01(c.x, c.y), 1e-5f);
            Assert.Greater(RiverLayout.Height(c.x, c.y), RiverLayout.WaterLevel(c.y) + 0.3f);
            Assert.IsTrue(RiverLayout.InChannel(c.x, c.y, 0f), "中州は川の中にある");
            // 中州と岸のあいだは水
            float bank = WorldGenerator.RiverBankEdgeX(c.y, 1f);
            float isl = WorldGenerator.IslandEdgeX(1f);
            Assert.Greater(bank - isl, 2f, "中州と岸のあいだに水路がある");
            float mid = (bank + isl) * 0.5f;
            Assert.Less(RiverLayout.Height(mid, c.y), RiverLayout.WaterLevel(c.y));
        }

        [Test]
        public void SpawnAndGate_AreOnDryLandInsideThePlayArea()
        {
            Vector3 s = RiverLayout.Ground(RiverLayout.Spawn.x, RiverLayout.Spawn.y);
            Assert.IsTrue(RiverLayout.InPlayArea(s));
            Assert.IsFalse(RiverLayout.IsUnderwater(s));
            Assert.Less(RiverLayout.Gate.magnitude, RiverLayout.PlayRadius);
            Assert.Greater(RiverLayout.Gate.magnitude, RiverLayout.PlayRadius - 12f, "トンネルはエリアのはし");
        }

        [Test]
        public void Landmarks_AreUniqueAndBelongToTheRiver()
        {
            var lms = RiverLayout.Landmarks;
            Assert.AreEqual(6, lms.Count);
            Assert.AreEqual(lms.Count, lms.Select(l => l.id).Distinct().Count());
            foreach (var lm in lms)
            {
                Assert.AreEqual("river", lm.areaId, lm.name);
                Assert.Less(lm.position.magnitude, RiverLayout.PlayRadius, lm.name);
                Assert.IsFalse(string.IsNullOrEmpty(lm.name));
                Assert.IsFalse(string.IsNullOrEmpty(lm.description));
                CollectionAssert.DoesNotContain(ForestLayout.Landmarks.Select(f => f.id).ToList(), lm.id, "森と ID が重ならない");
            }
        }

        [Test]
        public void Normal_IsUnitAndPointsUp()
        {
            for (float x = -50f; x <= 50f; x += 12.5f)
            for (float z = -50f; z <= 50f; z += 12.5f)
            {
                Vector3 n = RiverLayout.Normal(x, z);
                Assert.AreEqual(1f, n.magnitude, 1e-3f);
                Assert.Greater(n.y, 0.2f);
            }
        }

        [Test]
        public void Trails_AreMarkedAndGroundColorsAreValid()
        {
            foreach (var trail in RiverLayout.Trails)
            {
                Vector2 p = trail[trail.Length / 2];
                Assert.Greater(RiverLayout.TrailMask(p.x, p.y), 0.5f);
            }
            Assert.AreEqual(0f, RiverLayout.TrailMask(60f, 60f), 1e-4f);
            for (float x = -40f; x <= 40f; x += 20f)
            {
                float h = RiverLayout.Height(x, 5f);
                Color c = RiverLayout.GroundColor(x, 5f, h, RiverLayout.Normal(x, 5f));
                Assert.That(c.r, Is.InRange(0f, 1f));
                Assert.That(c.g, Is.InRange(0f, 1f));
                Assert.That(c.b, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void WaterLevelAt_IsOnlyDefinedNearTheChannel()
        {
            float z = -10f;
            Assert.AreEqual(RiverLayout.WaterLevel(z), RiverLayout.WaterLevelAt(RiverLayout.CenterX(z), z), 1e-5f);
            Assert.Less(RiverLayout.WaterLevelAt(RiverLayout.CenterX(z) + 40f, z), -100f);
        }
    }

    /// <summary>エリア（森・川辺）の定義とつながりの単体テスト。</summary>
    public class AreasTests
    {
        AreaLayout _saved;

        [SetUp]
        public void SetUp() => _saved = Areas.Current;

        [TearDown]
        public void TearDown() => Areas.Current = _saved;

        [Test]
        public void Get_FindsAreasById()
        {
            Assert.AreSame(Areas.Forest, Areas.Get("forest"));
            Assert.AreSame(Areas.River, Areas.Get("river"));
            Assert.AreSame(Areas.Forest, Areas.Get("nowhere"), "知らない ID は森（壊れたセーブ対策）");
            Assert.AreEqual(2, Areas.All.Length);
        }

        [Test]
        public void Areas_HaveNamesAndTexts()
        {
            foreach (var a in Areas.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(a.DisplayName), a.Id);
                Assert.IsFalse(string.IsNullOrEmpty(a.Subtitle), a.Id);
                Assert.IsFalse(string.IsNullOrEmpty(a.Tagline), a.Id);
                StringAssert.EndsWith("しずく", a.DropName);
            }
        }

        [Test]
        public void DropIds_DoNotOverlapBetweenAreas()
        {
            var ranges = Areas.All.Select(a => (a.DropIdOffset, a.DropIdOffset + a.DropCount)).ToList();
            for (int i = 0; i < ranges.Count; i++)
            for (int j = i + 1; j < ranges.Count; j++)
                Assert.IsTrue(ranges[i].Item2 <= ranges[j].Item1 || ranges[j].Item2 <= ranges[i].Item1);
            Assert.AreEqual(45 + 30, Areas.TotalDrops);
            Assert.AreEqual(ForestLayout.Landmarks.Count + RiverLayout.Landmarks.Count, Areas.TotalLandmarks);
            Assert.AreSame(Areas.Forest, Areas.AreaOfDrop(3));
            Assert.AreSame(Areas.River, Areas.AreaOfDrop(1005));
        }

        [Test]
        public void Gates_ConnectTheAreasBothWays()
        {
            Assert.IsTrue(Areas.Forest.Gates.Any(g => g.targetArea == "river"));
            Assert.IsTrue(Areas.River.Gates.Any(g => g.targetArea == "forest"));
            foreach (var a in Areas.All)
            foreach (var g in a.Gates)
            {
                Assert.Less(g.position.magnitude, a.PlayRadius, $"{a.Id} のトンネルが外");
                Assert.IsFalse(string.IsNullOrEmpty(g.label));
                var land = a.Ground(g.position.x, g.position.y);
                Assert.IsFalse(a.IsUnderwater(land), $"{a.Id} のトンネルが水の中");
            }
        }

        [Test]
        public void ArrivalFrom_PutsTheWormInFrontOfTheTunnelFacingInward()
        {
            foreach (var a in Areas.All)
            foreach (var g in a.Gates)
            {
                Assert.IsTrue(a.ArrivalFrom(g.targetArea, out var p, out var fwd));
                Assert.AreEqual(5.5f, Vector2.Distance(p, g.position), 1e-3f);
                Assert.Less(p.magnitude, g.position.magnitude, "トンネルより内側");
                Assert.Greater(Vector3.Dot(fwd, new Vector3(-g.position.x, 0f, -g.position.y).normalized), 0.99f);
                Vector3 ground = a.Ground(p.x, p.y);
                Assert.IsTrue(a.InPlayArea(ground));
                Assert.IsFalse(a.IsUnderwater(ground), $"{a.Id} の到着地点が水の中");
            }
            Assert.IsFalse(Areas.Forest.ArrivalFrom("nowhere", out var sp, out _));
            Assert.AreEqual(Areas.Forest.Spawn, sp);
        }

        [Test]
        public void ForestArea_MatchesForestLayout()
        {
            var f = Areas.Forest;
            Assert.AreEqual(ForestLayout.Height(3f, 4f), f.Height(3f, 4f), 1e-5f);
            Assert.AreSame(ForestLayout.Landmarks, f.Landmarks);
            Assert.AreEqual(45, f.DropCount);
            Assert.AreEqual(0, f.DropIdOffset);
        }

        [Test]
        public void RiverArea_MatchesRiverLayout()
        {
            var r = Areas.River;
            Assert.AreEqual(RiverLayout.Height(-3f, 7f), r.Height(-3f, 7f), 1e-5f);
            Assert.AreSame(RiverLayout.Landmarks, r.Landmarks);
            Assert.AreEqual(30, r.DropCount);
            Assert.AreEqual(1000, r.DropIdOffset);
            Assert.IsTrue(r.IsLand(RiverLayout.Spawn.x, RiverLayout.Spawn.y, 0.2f));
            Assert.IsFalse(r.IsLand(RiverLayout.CenterX(-10f), -10f, 0f));
        }

        [Test]
        public void LandmarkAreaIds_MatchTheirArea()
        {
            foreach (var a in Areas.All)
            foreach (var lm in a.Landmarks)
                Assert.AreEqual(a.Id, lm.areaId, lm.name);
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Count());
        }
    }

    /// <summary>いきもの図鑑の定義。</summary>
    public class SpeciesCatalogTests
    {
        [Test]
        public void Catalog_Has18SpeciesAnd2RaresWithTexts()
        {
            Assert.AreEqual(18, SpeciesCatalog.Count, "図鑑のコンプリートに必要なのは 18 しゅ");
            Assert.AreEqual(2, SpeciesCatalog.RareCount);
            Assert.AreEqual(20, SpeciesCatalog.All.Select(s => s.id).Distinct().Count());
            foreach (var s in SpeciesCatalog.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(s.name), s.id);
                Assert.IsFalse(string.IsNullOrEmpty(s.description), s.id);
                Assert.IsFalse(string.IsNullOrEmpty(s.hint), s.id);
                Assert.IsFalse(string.IsNullOrEmpty(s.areaLabel), s.id);
                Assert.Greater(s.discoverRadius, 0f, s.id);
                Assert.Greater(s.scale, 0f, s.id);
            }
        }

        [Test]
        public void Catalog_HasEveryRequestedCreature()
        {
            foreach (var id in new[] { "ant", "snail", "butterfly", "otoshibumi", "grasshopper", "frog", "sparrow", "crow", "ladybug", "spider", "mantis" })
                Assert.IsNotNull(SpeciesCatalog.Get(id), id);
            Assert.AreEqual("ant", SpeciesCatalog.Get("ant_helmet").rareOf);
            Assert.AreEqual("spider", SpeciesCatalog.Get("spider_sneaker").rareOf);
            Assert.AreEqual(0.005f, SpeciesCatalog.Get("ant_helmet").rareChance, 1e-6f, "出現率 0.5%");
            Assert.AreEqual(0.005f, SpeciesCatalog.Get("spider_sneaker").rareChance, 1e-6f);
            Assert.IsNull(SpeciesCatalog.Get("dragon"));
        }

        [Test]
        public void Ants_MarchAndBirdsFly()
        {
            Assert.AreEqual(MobKind.Marcher, SpeciesCatalog.Get("ant").kind);
            Assert.AreEqual(MobKind.Bird, SpeciesCatalog.Get("sparrow").kind);
            Assert.AreEqual(MobKind.Bird, SpeciesCatalog.Get("crow").kind);
            Assert.IsTrue(SpeciesCatalog.Get("crab").sideways);
        }

        [Test]
        public void Birds_CanBeRegisteredBeforeTheyFlyAway()
        {
            foreach (var s in SpeciesCatalog.All.Where(s => s.kind == MobKind.Bird))
            {
                Assert.Greater(s.fleeRadius, 0f, s.id);
                Assert.Greater(s.discoverRadius, s.fleeRadius, $"{s.id} は逃げる前に図鑑に登録できる");
            }
        }

        [Test]
        public void EveryBodyAndPartMeshExists()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var s in SpeciesCatalog.All)
            {
                Assert.IsNotNull(a.Get(s.body), $"{s.id} body {s.body}");
                foreach (var p in s.parts)
                    Assert.IsNotNull(a.Get(p.mesh), $"{s.id} part {p.mesh}");
            }
        }

        [Test]
        public void EverySpeciesHasAPortrait()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var s in SpeciesCatalog.All)
            {
                var t = a.Portrait(s.id);
                Assert.IsNotNull(t, s.id);
                Assert.GreaterOrEqual(t.width, 128, s.id);
            }
            Assert.IsNull(a.Portrait("dragon"));
        }

        [Test]
        public void WingedSpeciesHaveFlappingParts()
        {
            foreach (var id in new[] { "butterfly", "dragonfly", "sparrow", "crow" })
            {
                var s = SpeciesCatalog.Get(id);
                Assert.Greater(s.parts.Length, 0, id);
                Assert.IsTrue(s.parts.Any(p => p.flapAmp > 0f && p.flapHz > 0f), id);
            }
            Assert.IsTrue(SpeciesCatalog.Get("firefly").parts.Any(p => p.glow), "ホタルは光る");
        }
    }

    /// <summary>きせかえ（解放条件と色の変え方）。</summary>
    public class SkinsTests
    {
        static ProgressStats Stats(int d, int p, int s) => new ProgressStats { drops = d, places = p, species = s };

        [Test]
        public void Default_IsAlwaysUnlocked()
        {
            Assert.AreEqual("wakaba", Skins.All[0].id);
            Assert.IsTrue(Skins.IsUnlocked(Skins.All[0], Stats(0, 0, 0)));
            Assert.AreSame(Skins.All[0], Skins.Get("no-such-skin"));
        }

        [Test]
        public void Skins_HaveUniqueIdsAndConditions()
        {
            Assert.AreEqual(Skins.All.Count, Skins.All.Select(s => s.id).Distinct().Count());
            foreach (var s in Skins.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(s.name), s.id);
                Assert.IsFalse(string.IsNullOrEmpty(s.condition), s.id);
                Assert.Greater(s.swatch.a, 0f, s.id);
            }
        }

        [Test]
        public void Thresholds_UnlockSkins()
        {
            var kimidori = Skins.Get("kimidori");
            Assert.IsFalse(Skins.IsUnlocked(kimidori, Stats(9, 0, 0)));
            Assert.IsTrue(Skins.IsUnlocked(kimidori, Stats(10, 0, 0)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Get("sakura"), Stats(0, 5, 0)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Get("sorairo"), Stats(0, 0, 4)));
            Assert.IsFalse(Skins.IsUnlocked(Skins.Get("kogane"), Stats(99, 99, 15)));
            Assert.IsFalse(Skins.IsUnlocked(Skins.Get("kogane"), Stats(0, 0, 17)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Get("kogane"), Stats(0, 0, 18)));
        }

        [Test]
        public void Rainbow_NeedsEverything()
        {
            var niji = Skins.Get("niji");
            Assert.IsTrue(niji.rainbow);
            var almost = Stats(Areas.TotalDrops, Areas.TotalLandmarks, SpeciesCatalog.Count - 1);
            var all = Stats(Areas.TotalDrops, Areas.TotalLandmarks, SpeciesCatalog.Count);
            Assert.IsFalse(almost.IsComplete);
            Assert.IsTrue(all.IsComplete);
            Assert.IsFalse(Skins.IsUnlocked(niji, almost));
            Assert.IsTrue(Skins.IsUnlocked(niji, all));
        }

        [Test]
        public void NewlyUnlocked_ReportsOnlyTheSkinsThatJustOpened()
        {
            var list = Skins.NewlyUnlocked(Stats(9, 4, 3), Stats(10, 5, 3));
            CollectionAssert.AreEquivalent(new[] { "kimidori", "sakura" }, list.Select(s => s.id).ToArray());
            Assert.AreEqual(0, Skins.NewlyUnlocked(Stats(10, 5, 3), Stats(10, 5, 3)).Count);
        }

        [Test]
        public void Shift_IdentityAndHueRotation()
        {
            Color c = new Color(0.4f, 0.7f, 0.2f);
            Color same = Skins.Shift(c, 0f, 1f, 1f);
            Assert.AreEqual(c.r, same.r, 1e-3f);
            Assert.AreEqual(c.g, same.g, 1e-3f);
            Assert.AreEqual(c.b, same.b, 1e-3f);
            Color green = Skins.Shift(Color.red, 120f, 1f, 1f);
            Assert.Greater(green.g, 0.9f);
            Assert.Less(green.r, 0.1f);
            Color dark = Skins.Shift(c, 0f, 1f, 0.5f);
            Assert.Less(dark.g, c.g);
        }

        [Test]
        public void Apply_WritesThePropertyBlock()
        {
            TestUtil.NewEmptyScene();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var r = go.GetComponent<Renderer>();
            var sakura = Skins.Get("sakura");
            Skins.Apply(r, sakura, 0f);
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            Assert.AreEqual(sakura.hue, mpb.GetFloat("_HueShift"), 1e-4f);
            Assert.AreEqual(sakura.sat, mpb.GetFloat("_SatMul"), 1e-4f);
            Assert.AreEqual(sakura.val, mpb.GetFloat("_ValMul"), 1e-4f);
            var niji = Skins.Get("niji");
            Skins.Apply(r, niji, 0.5f);
            r.GetPropertyBlock(mpb);
            float h1 = mpb.GetFloat("_HueShift");
            Skins.Apply(r, niji, 1.5f);
            r.GetPropertyBlock(mpb);
            Assert.AreNotEqual(h1, mpb.GetFloat("_HueShift"), "にじいろは時間で色が変わる");
            Assert.DoesNotThrow(() => Skins.Apply(null, sakura, 0f));
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>動く足場（葉っぱの渡し舟）と、その上に立つ点。</summary>
    public class MovingPlatformTests
    {
        [SetUp]
        public void SetUp() => TestUtil.NewEmptyScene();

        [Test]
        public void Ferry_WaitsThenTravelsBetweenDocks()
        {
            var go = new GameObject("ferry");
            var f = go.AddComponent<RiverFerry>();
            f.dockA = new Vector3(0f, 0f, 0f);
            f.dockB = new Vector3(10f, 0f, 0f);
            Assert.AreEqual(2f * (f.waitTime + f.travelTime), f.Cycle, 1e-5f);
            f.SetClock(1f);
            Assert.IsTrue(f.AtA);
            Assert.AreEqual(0f, go.transform.position.x, 0.01f);
            f.SetClock(f.waitTime + f.travelTime * 0.5f);
            Assert.IsFalse(f.AtA);
            Assert.IsFalse(f.AtB);
            Assert.That(go.transform.position.x, Is.InRange(3f, 7f));
            f.SetClock(f.waitTime + f.travelTime + 1f);
            Assert.IsTrue(f.AtB);
            Assert.AreEqual(10f, go.transform.position.x, 0.01f);
            f.SetClock(f.Cycle - 0.01f);
            Assert.AreEqual(0f, go.transform.position.x, 0.05f, "ひとまわりで A へ戻る");
            Assert.That(f.Phase, Is.InRange(0f, 1f));
        }

        [Test]
        public void Ferry_BobsGentlyButStaysLevel()
        {
            var f = new GameObject("ferry").AddComponent<RiverFerry>();
            f.dockA = Vector3.zero;
            f.dockB = Vector3.right * 10f;
            for (float t = 0f; t < f.Cycle; t += 0.7f)
            {
                Vector3 p = f.Evaluate(t, out var rot);
                Assert.Less(Mathf.Abs(p.y), 0.05f);
                Assert.Less(Quaternion.Angle(rot, Quaternion.identity), 5f);
            }
        }

        [Test]
        public void SurfacePoint_OnAPlatformFollowsIt()
        {
            var box = TestUtil.Box(Vector3.zero, new Vector3(4f, 0.5f, 4f));
            box.AddComponent<MovingPlatform>();
            var col = box.GetComponent<Collider>();
            var sp = SurfacePoint.On(new Vector3(0.5f, 0.25f, 0.5f), Vector3.up, col);
            Assert.IsNotNull(sp.platform);
            box.transform.position += new Vector3(3f, 0.5f, -1f);
            box.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var moved = sp.Updated();
            TestUtil.AssertVector(box.transform.TransformPoint(sp.localPoint), moved.point, 1e-4f);
            TestUtil.AssertVector(Vector3.up, moved.normal, 1e-4f);
            Assert.AreNotEqual(sp.point, moved.point);
        }

        [Test]
        public void SurfacePoint_OnStaticGroundDoesNotMove()
        {
            var box = TestUtil.Box(Vector3.zero, new Vector3(4f, 0.5f, 4f));
            var sp = SurfacePoint.On(new Vector3(0.5f, 0.25f, 0.5f), Vector3.up, box.GetComponent<Collider>());
            Assert.IsNull(sp.platform);
            box.transform.position += Vector3.right * 3f;
            TestUtil.AssertVector(sp.point, sp.Updated().point, 1e-6f);
            var none = SurfacePoint.On(Vector3.one, Vector3.up, null);
            Assert.IsNull(none.platform);
        }

        [Test]
        public void Probe_WalkingOntoAPlatformRemembersIt()
        {
            TestUtil.Box(new Vector3(0f, -0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            var raft = TestUtil.Box(new Vector3(4f, -0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            raft.AddComponent<MovingPlatform>();
            Physics.SyncTransforms();
            SurfaceProbe.ClearCache();
            var start = SurfacePoint.On(new Vector3(1.5f, 0f, 0f), Vector3.up, null);
            Assert.IsTrue(SurfaceProbe.Walk(start, Vector3.right, 1.5f, out var end, out _));
            Assert.Greater(end.point.x, 2.5f);
            Assert.IsNotNull(end.platform, "乗り移った先が舟だとわかる");
        }

        [Test]
        public void Probe_StepsDownFromAPlatformOntoLowerGround()
        {
            TestUtil.Box(new Vector3(4f, -0.65f, 0f), new Vector3(4f, 0.5f, 4f));   // 0.4m 低い地面
            var raft = TestUtil.Box(new Vector3(0f, -0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            raft.AddComponent<MovingPlatform>();
            Physics.SyncTransforms();
            var start = SurfacePoint.On(new Vector3(1.6f, 0f, 0f), Vector3.up, raft.GetComponent<Collider>());
            Assert.IsNotNull(start.platform);
            Assert.IsTrue(SurfaceProbe.Walk(start, Vector3.right, 1.0f, out var end, out _), "舟から降りられる");
            Assert.Greater(end.point.x, 2f);
            Assert.AreEqual(-0.4f, end.point.y, 0.05f);
            Assert.IsNull(end.platform, "地面に降りた");
        }

        [Test]
        public void Probe_StepsUpFromGroundOntoAPlatform()
        {
            TestUtil.Box(new Vector3(0f, -0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            var raft = TestUtil.Box(new Vector3(4f, 0.05f, 0f), new Vector3(4f, 0.5f, 4f));   // 0.3m 高い舟
            raft.AddComponent<MovingPlatform>();
            Physics.SyncTransforms();
            var start = new SurfacePoint(new Vector3(1.6f, 0f, 0f), Vector3.up);
            Assert.IsTrue(SurfaceProbe.Walk(start, Vector3.right, 1.0f, out var end, out _), "舟に乗れる");
            Assert.AreEqual(0.3f, end.point.y, 0.05f);
            Assert.IsNotNull(end.platform);
        }
    }

    /// <summary>川辺エリアを実際に生成して検証する。</summary>
    public class RiverGeneratorTests
    {
        static WorldGenerator _gen;
        static Creatures _mobs;
        static AreaLayout _savedArea;

        [OneTimeSetUp]
        public void Generate()
        {
            _savedArea = Areas.Current;
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            _gen.sun = sun;
            Areas.Current = Areas.River;
            _gen.GenerateNow(Areas.River);
            _mobs = go.AddComponent<Creatures>();
            _mobs.assets = _gen.assets;
            _mobs.Build(_gen);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = _savedArea;
        }

        [Test]
        public void IsTheRiverArea()
        {
            Assert.IsTrue(_gen.IsGenerated);
            Assert.AreSame(Areas.River, _gen.Area);
            Assert.IsNotNull(_gen.MapTexture);
        }

        [Test]
        public void Dewdrops_AreAllPlacedOnDryReachableSpots()
        {
            Assert.AreEqual(Areas.River.DropCount, _gen.DewdropPoints.Count);
            foreach (var p in _gen.DewdropPoints)
            {
                Assert.IsTrue(RiverLayout.InPlayArea(p), $"{p}");
                Assert.IsFalse(RiverLayout.IsUnderwater(p), $"{p} が水の中");
                Assert.IsTrue(GameHarnessLike.NearSurface(p, 1.2f), $"{p} の近くに面がない");
            }
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
            for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 1.5f, "しずくが重なっている");
        }

        [Test]
        public void SpawnIsOnDryLand()
        {
            Assert.IsFalse(RiverLayout.IsUnderwater(_gen.SpawnPoint));
            Assert.IsTrue(RiverLayout.InPlayArea(_gen.SpawnPoint));
        }

        [Test]
        public void SteppingStones_CrossTheRiverAboveTheWater()
        {
            var stones = _gen.StepStones;
            Assert.GreaterOrEqual(stones.Count, 5);
            float z = RiverLayout.StonesZ;
            float wl = RiverLayout.WaterLevel(z);
            Assert.Less(stones[0].x, RiverLayout.CenterX(z) - RiverLayout.HalfWidth(z) + 2f, "西の岸から");
            Assert.Greater(stones[stones.Count - 1].x, RiverLayout.CenterX(z) + RiverLayout.HalfWidth(z) - 2f, "東の岸まで");
            for (int i = 0; i < stones.Count; i++)
            {
                Assert.IsTrue(Physics.Raycast(stones[i] + Vector3.up * 3f, Vector3.down, out var h, 6f, ShakuConst.SurfaceMask), $"石 {i}");
                Assert.Greater(h.point.y, wl + 0.1f, $"石 {i} の上が水の下");
                if (i == 0) continue;
                Assert.Less(Vector2.Distance(new Vector2(stones[i].x, stones[i].z), new Vector2(stones[i - 1].x, stones[i - 1].z)), 2.8f, "石の間隔");
                // 石と石のあいだに、しゃくとりむしが届かない水のすきまがない
                float gap = 0f, worst = 0f;
                for (float t = 0f; t <= 1f; t += 0.05f)
                {
                    Vector3 p = Vector3.Lerp(stones[i - 1], stones[i], t) + Vector3.up * 3f;
                    bool dry = Physics.Raycast(p, Vector3.down, out var hit, 6f, ShakuConst.SurfaceMask) && hit.point.y > wl + 0.05f;
                    gap = dry ? 0f : gap + Vector3.Distance(stones[i - 1], stones[i]) * 0.05f;
                    worst = Mathf.Max(worst, gap);
                }
                Assert.Less(worst, 0.5f, $"石 {i - 1} と {i} のあいだ");
            }
        }

        [Test]
        public void Bridge_SpansTheRiverAboveTheWater()
        {
            Vector3 a = _gen.BridgeA, b = _gen.BridgeB;
            float z = RiverLayout.BridgeZ;
            float cx = RiverLayout.CenterX(z);
            float wl = RiverLayout.WaterLevel(z);
            Assert.Less(a.x, cx - RiverLayout.HalfWidth(z));
            Assert.Greater(b.x, cx + RiverLayout.HalfWidth(z));
            // 枝は少し曲がっているので、橋の線の近く（横 1.5m 以内）に水の上の足場が続いていればよい
            Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized);
            for (float t = 0.1f; t <= 0.9f; t += 0.05f)
            {
                Vector3 p = Vector3.Lerp(a, b, t);
                bool found = false;
                for (float o = -1.5f; o <= 1.5f && !found; o += 0.1f)
                {
                    if (Physics.Raycast(p + side * o + Vector3.up * 4f, Vector3.down, out var h, 10f, ShakuConst.SurfaceMask)
                        && h.point.y > wl + 0.2f && !h.collider.name.StartsWith("Terrain"))
                        found = true;
                }
                Assert.IsTrue(found, $"t={t:F2} で橋がとぎれている");
            }
        }

        [Test]
        public void Ferry_LinksTheBankAndTheIsland()
        {
            var f = _gen.Ferry;
            Assert.IsNotNull(f);
            Assert.IsNotNull(f.GetComponent<MovingPlatform>());
            Assert.IsNotNull(f.GetComponent<Collider>());
            Assert.AreEqual(ShakuConst.SurfaceLayer, f.gameObject.layer);
            float bank = WorldGenerator.RiverBankEdgeX(RiverLayout.IslandZ, 1f);
            float isl = WorldGenerator.IslandEdgeX(1f);
            f.SetClock(0f);
            Bounds atA = f.GetComponent<Collider>().bounds;
            Assert.Greater(atA.max.x, bank, "A では岸にとどく");
            f.SetClock(f.waitTime + f.travelTime + 0.5f);
            Bounds atB = f.GetComponent<Collider>().bounds;
            Assert.Less(atB.min.x, isl, "B では中州にとどく");
            Assert.Greater(atA.max.y, RiverLayout.WaterLevel(RiverLayout.IslandZ), "水に浮いている");
            f.SetClock(0f);
        }

        [Test]
        public void Island_IsOnlyReachedByTheFerry()
        {
            // 中州の東西どちらにも、しゃくとりむしがまたげない幅の水があり、足場（舟以外）がない
            Vector2 c = RiverLayout.IslandCenter;
            float wl = RiverLayout.WaterLevel(c.y);
            int rows = 0;
            for (float z = c.y - 6f; z <= c.y + 6f; z += 1f)
            {
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    float x = c.x;
                    bool onIsland = true, inWater = false;
                    float waterStart = 0f, gap = 0f;
                    for (int k = 0; k < 120; k++, x += dir * 0.2f)
                    {
                        bool dry = Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out var h, 60f, ShakuConst.SurfaceMask)
                                   && h.collider.GetComponent<MovingPlatform>() == null
                                   && !RiverLayout.IsUnderwater(h.point);
                        if (onIsland)
                        {
                            if (!dry) { onIsland = false; inWater = true; waterStart = x; }
                            continue;
                        }
                        if (inWater && dry)
                        {
                            gap = Mathf.Abs(x - waterStart);
                            inWater = false;
                            break;
                        }
                    }
                    if (onIsland) continue;
                    Assert.IsFalse(inWater && Mathf.Abs(x - waterStart) < 1.5f, $"z={z} dir={dir}");
                    if (!inWater) Assert.Greater(gap, 1.5f, $"z={z} dir={dir} で中州と岸が {gap:F1}m しか離れていない");
                    rows++;
                }
            }
            Assert.Greater(rows, 10);
        }

        [Test]
        public void PoolPads_FloatAboveTheWater()
        {
            float pz = RiverLayout.PoolZ;
            int pads = 0;
            for (float x = -30f; x <= 30f; x += 1f)
            for (float z = pz - 12f; z <= pz + 12f; z += 1f)
            {
                if (Physics.Raycast(new Vector3(x, 10f, z), Vector3.down, out var h, 20f, ShakuConst.SurfaceMask) && h.collider.name.StartsWith("LilyPad"))
                {
                    pads++;
                    Assert.IsFalse(RiverLayout.IsUnderwater(h.point), $"({x},{z}) の葉が水中扱い");
                }
            }
            Assert.Greater(pads, 30);
        }

        [Test]
        public void Gate_LeadsBackToTheForest()
        {
            Assert.AreEqual(1, _gen.Gates.Count);
            var g = _gen.Gates[0];
            Assert.AreEqual("forest", g.def.targetArea);
            Assert.Greater(Vector3.Dot(g.inward, -new Vector3(g.position.x, 0f, g.position.z).normalized), 0.9f);
            Assert.IsTrue(Physics.Raycast(g.position + Vector3.up * 2f, Vector3.down, out _, 6f, ShakuConst.SurfaceMask), "トンネルの床");
        }

        [Test]
        public void Waterfall_AndRiverWaterAreBuilt()
        {
            var renderers = _gen.Root.GetComponentsInChildren<MeshRenderer>(true);
            Assert.IsTrue(renderers.Any(r => r.sharedMaterial == _gen.assets.river), "川の水面");
            Assert.IsTrue(renderers.Any(r => r.sharedMaterial == _gen.assets.waterfall), "滝");
        }

        [Test]
        public void Creatures_LiveByTheRiver()
        {
            var species = _gen.Mobs.Select(m => m.species).Distinct().ToList();
            foreach (var id in new[] { "waterstrider", "dragonfly", "crab", "riversnail", "frog", "firefly" })
                CollectionAssert.Contains(species, id);
            foreach (var m in _gen.Mobs)
            {
                Assert.IsNotNull(SpeciesCatalog.Get(m.species), m.species);
                Assert.Greater(m.count, 0);
            }
            Assert.Greater(_mobs.MobCount, 30);
            Assert.Greater(_mobs.CountOf("waterstrider"), 3);
        }

        [Test]
        public void WaterStriders_StayOnTheWater()
        {
            for (int i = 0; i < _mobs.CountOf("waterstrider"); i++)
            {
                Vector3 p = _mobs.PositionOf("waterstrider", i);
                float wl = Areas.River.WaterLevelAt(p.x, p.z);
                Assert.Greater(wl, -100f, "川の上にいる");
                Assert.AreEqual(wl, p.y, 0.15f, "水面に立つ");
            }
        }
    }

    /// <summary>森にいるいきもの（生成結果）。</summary>
    public class ForestCreatureTests
    {
        static WorldGenerator _gen;
        static Creatures _mobs;
        static AreaLayout _savedArea;

        [OneTimeSetUp]
        public void Generate()
        {
            _savedArea = Areas.Current;
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            new GameObject("Sun").AddComponent<Light>().type = LightType.Directional;
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Forest;
            _gen.GenerateNow(Areas.Forest);
            _mobs = go.AddComponent<Creatures>();
            _mobs.assets = _gen.assets;
            _mobs.Build(_gen);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = _savedArea;
        }

        [Test]
        public void Forest_HasEveryRequestedCreature()
        {
            var species = _gen.Mobs.Select(m => m.species).Distinct().ToList();
            foreach (var id in new[] { "ant", "snail", "butterfly", "otoshibumi", "grasshopper", "frog", "sparrow", "crow", "ladybug" })
                CollectionAssert.Contains(species, id);
        }

        [Test]
        public void EverySpecies_LivesSomewhere()
        {
            var forest = _gen.Mobs.Select(m => m.species);
            var river = new[] { "waterstrider", "dragonfly", "crab", "riversnail", "frog", "firefly" };
            foreach (var s in SpeciesCatalog.Regular)
                Assert.IsTrue(forest.Contains(s.id) || river.Contains(s.id), $"{s.id} がどこにもいない");
            foreach (var r in SpeciesCatalog.Rares)
                Assert.IsTrue(forest.Contains(r.rareOf), $"{r.id} の元になる {r.rareOf} が森にいる");
        }

        [Test]
        public void Ants_BothMarchInLinesAndWander()
        {
            var ants = _gen.Mobs.Where(m => m.species == "ant").ToList();
            var lines = ants.Where(m => m.path.Count > 1).ToList();
            Assert.GreaterOrEqual(lines.Count, 2, "行列");
            Assert.IsTrue(ants.Any(m => m.path.Count <= 1), "うろうろするアリ");
            foreach (var l in lines)
            {
                Assert.GreaterOrEqual(l.count, 8, "行列は長い");
                float len = 0f;
                for (int i = 1; i < l.path.Count; i++) len += Vector3.Distance(l.path[i - 1], l.path[i]);
                Assert.Greater(len, 6f);
            }
        }

        [Test]
        public void Birds_HaveLandingSpots()
        {
            foreach (var m in _gen.Mobs.Where(m => m.species == "sparrow" || m.species == "crow"))
                Assert.GreaterOrEqual(m.path.Count, 2, m.species);
        }

        [Test]
        public void Gate_LeadsToTheRiver()
        {
            Assert.AreEqual(1, _gen.Gates.Count);
            Assert.AreEqual("river", _gen.Gates[0].def.targetArea);
        }

        [Test]
        public void Creatures_StartOnTheirSurfaces()
        {
            foreach (var id in new[] { "ant", "snail", "ladybug", "pillbug", "beetle" })
            {
                for (int i = 0; i < _mobs.CountOf(id); i++)
                {
                    Vector3 p = _mobs.PositionOf(id, i);
                    Assert.IsTrue(GameHarnessLike.NearSurface(p, 0.6f), $"{id}[{i}] {p} が宙に浮いている");
                    Assert.IsFalse(ForestLayout.IsUnderwater(p), $"{id}[{i}] が水の中");
                }
            }
        }

        [Test]
        public void Discover_RegistersOnceAndRaisesTheEvent()
        {
            string saveBackup = PlayerPrefs.GetString("shakutori.save.v1", null);
            try
            {
                SaveSystem.ResetAllInMemoryForTests();
                int raised = 0;
                _mobs.Discovered += _ => raised++;
                var ant = SpeciesCatalog.Get("ant");
                Assert.IsFalse(Creatures.IsDiscovered("ant"));
                _mobs.Discover(ant);
                _mobs.Discover(ant);
                Assert.IsTrue(Creatures.IsDiscovered("ant"));
                Assert.AreEqual(1, Creatures.DiscoveredCount);
                Assert.AreEqual(1, raised);
            }
            finally
            {
                if (saveBackup != null) PlayerPrefs.SetString("shakutori.save.v1", saveBackup);
                else PlayerPrefs.DeleteKey("shakutori.save.v1");
                PlayerPrefs.Save();
                SaveSystem.ResetAllInMemoryForTests();
            }
        }

        [Test]
        public void Clear_RemovesAllMobs()
        {
            var go = new GameObject("tmp");
            var c = go.AddComponent<Creatures>();
            c.assets = _gen.assets;
            c.Build(_gen);
            Assert.Greater(c.MobCount, 0);
            c.Clear();
            Assert.AreEqual(0, c.MobCount);
            Object.DestroyImmediate(go);
        }
    }

    static class GameHarnessLike
    {
        public static bool NearSurface(Vector3 p, float r) => Physics.CheckSphere(p, r, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>新しいシェーダー・マテリアル・UI の設定。</summary>
    public class AreaAssetTests
    {
        [Test]
        public void NewShaders_CompileWithoutErrors()
        {
            foreach (var name in new[] { "Shakutori/ToonRiver", "Shakutori/Waterfall", "Shakutori/ToonLit" })
            {
                var sh = Shader.Find(name);
                Assert.IsNotNull(sh, name);
                Assert.IsFalse(ShaderUtil.ShaderHasError(sh), name);
            }
        }

        [Test]
        public void Materials_UseTheRightShadersAndSettings()
        {
            var a = TestUtil.LoadWorldAssets();
            Assert.AreEqual("Shakutori/ToonRiver", a.river.shader.name);
            Assert.AreEqual("Shakutori/Waterfall", a.waterfall.shader.name);
            Assert.AreEqual("Shakutori/ToonLit", a.creature.shader.name);
            Assert.AreEqual(0f, a.creatureWing.GetFloat("_Cull"), "羽は両面");
            Assert.IsFalse(a.creatureWing.GetShaderPassEnabled("SRPDefaultUnlit"), "羽には輪郭線を描かない");
            Assert.Greater(a.creatureGlow.GetColor("_EmissionColor").maxColorComponent, 1f, "ホタルが光る");
            Assert.IsTrue(a.worm.IsKeywordEnabled("_HUE_SHIFT"), "きせかえ用の色変え");
            Assert.IsTrue(a.creature.enableInstancing && a.creatureWing.enableInstancing && a.creatureGlow.enableInstancing);
        }

        [Test]
        public void UI_StateClassesUsedInCodeExistInTheStyleSheet()
        {
            string code = System.IO.File.ReadAllText("Assets/Scripts/UI/GameUI.cs");
            string uss = System.IO.File.ReadAllText("Assets/UI/GameUI.uss");
            var names = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, @"(?:EnableInClassList|AddToClassList|RemoveFromClassList|ClassListContains)\(""([\w-]+)"""))
                names.Add(m.Groups[1].Value);
            Assert.Greater(names.Count, 20);
            foreach (var n in names)
                StringAssert.Contains("." + n, uss, $"USS に .{n} がない");
        }

        [Test]
        public void UI_LayoutClassesAreStyled()
        {
            string uss = System.IO.File.ReadAllText("Assets/UI/GameUI.uss");
            foreach (var n in new[] { ".touch-ui .touch-only", ".portrait .map-body", ".short .bigmap", ".compact .minimap", ".move-zone", ".joystick--active" })
                StringAssert.Contains(n, uss);
        }

        [Test]
        public void WebTemplate_PreventsPageZoomOnPhones()
        {
            string html = System.IO.File.ReadAllText("Assets/WebGLTemplates/Shakutori/index.html");
            StringAssert.Contains("user-scalable=no", html);
            StringAssert.Contains("gesturestart", html);
            StringAssert.Contains("maxTouchPoints", html);
            StringAssert.Contains("-webkit-touch-callout: none", html);
        }

        [Test]
        public void NewSounds_Exist()
        {
            foreach (var n in new[] { "ambience_river", "creature", "travel", "unlock", "caw" })
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{n}.wav"), n);
        }
    }
}

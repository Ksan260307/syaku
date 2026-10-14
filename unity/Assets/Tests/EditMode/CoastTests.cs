using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>海岸のエリア（砂浜・海・磯の岩場と潮だまり・干潟・しずく・名所・いきもの・地図）と、海岸の 7 しゅのいきものを、実際に組み立てて確かめる。</summary>
    public class CoastTests
    {
        static WorldGenerator _gen;

        static readonly string[] CoastSpecies = { "yadokari", "shiomaneki", "kamome", "hitode", "uni", "funamushi" };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Coast;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void Coast_IsTheSixthArea_ReachedFromTheRiver()
        {
            Assert.AreSame(Areas.Coast, Areas.All[5]);
            Assert.AreSame(Areas.Coast, Areas.Get("coast"));
            Assert.AreEqual("海岸", Areas.Coast.DisplayName);
            Assert.AreEqual("海のしずく", Areas.Coast.DropName);
            Assert.AreEqual(5000, Areas.Coast.DropIdOffset);
            Assert.AreEqual(30, Areas.Coast.DropCount);
            Assert.AreSame(Areas.Coast, Areas.AreaOfDrop(5010));
            // 川辺の下流のトンネル（あとから作る。川辺のしずくは変えない）と、海辺から川辺へもどるトンネル
            var toCoast = Areas.River.Gates.Single(g => g.targetArea == "coast");
            Assert.IsTrue(toCoast.late, "川辺のしずくを変えないよう、あとから作る");
            Assert.IsFalse(toCoast.door);
            Assert.IsFalse(RiverLayout.InChannel(toCoast.position.x, toCoast.position.y, 6f), "トンネルは川の外の岸");
            var back = Areas.Coast.Gates.Single();
            Assert.AreEqual("river", back.targetArea);
            Assert.IsTrue(Areas.River.ArrivalFrom("coast", out var riverArrive, out var riverFwd));
            Assert.Less(Vector2.Distance(riverArrive, toCoast.position), 7f, "海からもどると、トンネルの前に出る");
            Assert.Greater(Vector2.Dot(new Vector2(riverFwd.x, riverFwd.z), -toCoast.position.normalized), 0.9f, "川の方を向く");
            var ids = CoastLayout.Landmarks.Select(l => l.id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(50, 9).ToList(), ids);
            Assert.IsTrue(CoastLayout.Landmarks.All(l => l.areaId == "coast"));
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Select(l => l.id).Distinct().Count(), "名所の番号は重ならない");
        }

        [Test]
        public void Shore_SlopesFromDunesIntoTheSea()
        {
            for (float x = -60f; x <= 60f; x += 10f)
            {
                float z = CoastLayout.ShoreZ(x);
                bool plain = true;
                for (float dz = -20f; dz <= 30f; dz += 2f)
                    if (CoastLayout.ShelfMask(x, z + dz) > 0.05f || CoastLayout.MudMask(x, z + dz) > 0.05f || CoastLayout.CreekDist(x, z + dz) < 6f) plain = false;
                if (!plain) continue;   // 岩場・干潟・小川のない、ふつうの砂浜だけ
                Assert.AreEqual(0f, CoastLayout.Height(x, z), 0.05f, $"波打ちぎわ x={x}");
                Assert.Less(CoastLayout.Height(x, z - 20f), -1.5f, "沖は深い");
                Assert.Greater(CoastLayout.Height(x, z + 30f), 1.5f, "砂丘は高い");
                Assert.IsTrue(Areas.Coast.IsUnderwater(new Vector3(x, CoastLayout.Height(x, z - 20f), z - 20f)), "沖は水の中");
                Assert.IsFalse(Areas.Coast.IsUnderwater(new Vector3(x, CoastLayout.Height(x, z + 2f), z + 2f)), "ぬれた砂は、水の中ではない");
            }
            // 外周：陸の側はせり上がり、海の側は沖へ深く（水平線まで海）
            Assert.Less(CoastLayout.OuterHeight(0f, -200f), -6f, "沖");
            Assert.Greater(CoastLayout.OuterHeight(0f, 150f), CoastLayout.Height(0f, 60f), "北の陸はせり上がる");
            Assert.AreEqual(CoastLayout.SeaLevel, Areas.Coast.WaterLevelAt(0f, -40f));
            Assert.AreEqual(CoastLayout.PoolLevel, Areas.Coast.WaterLevelAt(CoastLayout.TidePool.x, CoastLayout.TidePool.y));
        }

        [Test]
        public void TidePools_HoldWaterOnTheRockShelf_AndTheCreekCanBeWaded()
        {
            foreach (var (c, r) in CoastLayout.Pools)
            {
                Assert.Greater(CoastLayout.ShelfMask(c.x, c.y), 0.9f, $"潮だまり {c} は岩場の上");
                Assert.Less(CoastLayout.Height(c.x, c.y), CoastLayout.PoolLevel - 0.4f, "まん中は深め");
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI / 12f;
                    Vector2 rim = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 0.6f);
                    Assert.Greater(CoastLayout.Height(rim.x, rim.y), CoastLayout.PoolLevel + 0.05f, $"ふちは水面より高い（水がこぼれない） {rim}");
                }
            }
            // 小川の浅瀬は、歩いてわたれる（しゃくとりむしが沈まない深さ）
            foreach (var f in CoastLayout.Fords)
                Assert.Greater(CoastLayout.Height(f.x, f.y), CoastLayout.SeaLevel - AreaLayout.WadeDepth, $"浅瀬 {f}");
            // 干潟は、水面より少し高い、平らなどろ
            Assert.AreEqual(0.13f, CoastLayout.Height(CoastLayout.Mudflat.x + 5f, CoastLayout.Mudflat.y + 3f), 0.12f);
        }

        [Test]
        public void Dewdrops_AreOnDryLandAndOnTheBigThings()
        {
            Assert.AreEqual(30, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
            {
                Assert.IsTrue(CoastLayout.InPlayArea(d), $"しずく {d} が外");
                Assert.IsFalse(Areas.Coast.IsUnderwater(d), $"しずく {d} が水の中");
            }
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
                for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                    Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 3f);
            // 大きな巻き貝の口の中・岬の大岩の上・流木の上
            Assert.AreNotEqual(Vector3.zero, _gen.ConchMouth, "巻き貝の中の床");
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Vector3.Distance(d, _gen.ConchMouth) < 0.5f), "巻き貝の中のしずく");
            Assert.IsTrue(Physics.Raycast(_gen.ConchMouth + Vector3.up * 0.1f, Vector3.up, 8f, ShakuConst.SurfaceMask), "巻き貝の中（上に殻がある）");
            Vector3 cape = new Vector3(CoastLayout.Cape.x, 0f, CoastLayout.Cape.y);
            Assert.IsTrue(_gen.DewdropPoints.Any(d => new Vector2(d.x - cape.x, d.z - cape.z).magnitude < 6f && d.y > CoastLayout.Height(d.x, d.z) + 2.5f), "岬の大岩の上");
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y - CoastLayout.Height(d.x, d.z) > 1.5f), 5, "物の上");
        }

        [Test]
        public void Landmarks_ArriveOnDryLand_AndHighPlacesCanBeReached()
        {
            foreach (var lm in CoastLayout.Landmarks)
            {
                Vector3 p = _gen.ArrivalPoint(lm, out var f);
                Assert.IsTrue(CoastLayout.InPlayArea(p), lm.name);
                Assert.IsFalse(Areas.Coast.IsUnderwater(p + Vector3.down * 0.02f), lm.name + "：水の中に着かない");
                if (lm.minHeightAboveGround <= 0f) continue;
                bool found = false;
                for (float r = 0f; r <= lm.radius && !found; r += 1f)
                    for (float a = 0f; a < Mathf.PI * 2f && !found; a += 0.4f)
                    {
                        Vector2 q = lm.position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (Physics.Raycast(new Vector3(q.x, 40f, q.y), Vector3.down, out var hit, 80f, ShakuConst.SurfaceMask)
                            && hit.point.y - CoastLayout.Height(q.x, q.y) >= lm.minHeightAboveGround && hit.normal.y > 0.6f) found = true;
                    }
                Assert.IsTrue(found, lm.name + "：登れる所がある");
            }
            // 岬の先には、大岩の上に着いて、沖の灯台を見る
            var capeLm = CoastLayout.Landmarks.Single(l => l.id == 57);
            Vector3 top = _gen.ArrivalPoint(capeLm, out var look);
            Assert.Greater(top.y - CoastLayout.Height(top.x, top.z), 2.5f, "大岩の上");
            Assert.Greater(Vector3.Dot(look, new Vector3(CoastLayout.Lighthouse.x - top.x, 0f, CoastLayout.Lighthouse.y - top.z).normalized), 0.8f, "灯台の方を向く");
        }

        [Test]
        public void CoastCreatures_LiveOnTheBeachRocksPoolsAndMudflat()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in CoastSpecies)
            {
                CollectionAssert.Contains(ids, id, $"{id} が海岸にいる");
                var sp = SpeciesCatalog.Get(id);
                Assert.AreEqual("海岸", sp.areaLabel, id);
                Assert.IsFalse(sp.IsRare, id);
                Assert.IsNotNull(_gen.assets.TryGet(sp.body), $"{id} の体のメッシュ");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.IsTrue(CreatureRig.HasLegs(sp.rig), $"{id} の脚");
                foreach (var part in sp.parts) Assert.IsNotNull(_gen.assets.TryGet(part.mesh), $"{id} の {part.mesh}");
            }
            CollectionAssert.Contains(ids, "butterfly", "砂丘の花のチョウ");
            CollectionAssert.Contains(ids, "grasshopper", "砂丘のバッタ");
            Assert.IsTrue(GameUI.LivesIn(SpeciesCatalog.Get("butterfly"), "海岸"));
            // ヒトデとウニは、潮だまりの水の中
            foreach (var g in _gen.Mobs.Where(m => m.species == "hitode" || m.species == "uni"))
            {
                Assert.GreaterOrEqual(CoastLayout.PoolAt(g.center.x, g.center.z, 0f), 0, $"{g.species} は潮だまりの中");
                Assert.Less(g.center.y, CoastLayout.PoolLevel - 0.3f, "水の底");
            }
            // シオマネキは、干潟の巣穴ごとに 1 ぴき
            var fiddlers = _gen.Mobs.Where(m => m.species == "shiomaneki").ToList();
            Assert.AreEqual(_gen.Burrows.Count, fiddlers.Count);
            foreach (var g in fiddlers)
            {
                Assert.IsTrue(_gen.Burrows.Any(b => new Vector2(b.x - g.center.x, b.z - g.center.z).magnitude < 0.5f), "巣穴のところ");
                Assert.Greater(CoastLayout.MudMask(g.center.x, g.center.z), 0.5f, "どろの上");
            }
            // フナムシは、磯の岩場
            foreach (var g in _gen.Mobs.Where(m => m.species == "funamushi"))
                Assert.Greater(CoastLayout.ShelfMask(g.center.x, g.center.z), 0.5f, "岩場の上");
            // カモメは、水の上には下りない
            var gull = _gen.Mobs.Single(m => m.species == "kamome");
            Assert.GreaterOrEqual(gull.path.Count, 3);
            foreach (var p in gull.path) Assert.IsFalse(Areas.Coast.IsUnderwater(p + Vector3.up * 0.02f), $"カモメの下りる所 {p}");
            foreach (var g in _gen.Mobs) Assert.IsTrue(CoastLayout.InPlayArea(g.center), $"{g.species} {g.center}");
        }

        [Test]
        public void Manta_IsTheGullsRareVariant_AndSwimsOnlyInDeepEnoughSea()
        {
            var manta = SpeciesCatalog.Get("manta");
            Assert.IsTrue(manta.IsRare);
            Assert.AreEqual("kamome", manta.rareOf);
            Assert.AreSame(manta, SpeciesCatalog.RareVariantOf("kamome"));
            Assert.IsNotNull(_gen.assets.TryGet("Manta_Body"));
            Assert.IsNotNull(_gen.assets.TryGet("Manta_Fin"));
            Assert.IsNotNull(_gen.assets.Portrait("manta"));
            for (int k = 0; k < 64; k++)
            {
                float a = k * Mathf.PI / 32f;
                Vector2 p = CoastLayout.MantaBay + new Vector2(Mathf.Cos(a) * CoastLayout.MantaLoop.x, Mathf.Sin(a) * CoastLayout.MantaLoop.y);
                Assert.Less(CoastLayout.Height(p.x, p.y), Creatures.MantaSwimY - 0.8f, $"マンタの泳ぐ所は、底にぶつからない深さ {p}");
                Assert.Less(p.magnitude, CoastLayout.PlayRadius, "遊べる場所の中");
                // 波打ちぎわから見つけられる近さ
                float toShore = CoastLayout.ShoreZ(p.x) - p.y;
                Assert.Less(toShore, 40f);
            }
            Assert.Greater(manta.discoverRadius, 20f, "浜から見つけられる");
        }

        [Test]
        public void CoastCreatures_BorrowTheRightWayOfMoving()
        {
            Assert.AreEqual("crow", SpeciesCatalog.Get("kamome").BehaviorId, "カモメは、カラスのように歩いて飛ぶ");
            Assert.AreEqual("crab", SpeciesCatalog.Get("shiomaneki").BehaviorId, "シオマネキは、カニの横歩き");
            Assert.IsTrue(SpeciesCatalog.Get("shiomaneki").sideways);
            Assert.AreEqual("tokage", SpeciesCatalog.Get("funamushi").BehaviorId, "フナムシは、さっと走って止まる");
            Assert.AreEqual("yadokari", SpeciesCatalog.Get("yadokari").BehaviorId);
            Assert.IsFalse(SpeciesCatalog.Get("yadokari").sideways, "ヤドカリは、前へ歩く");
            Assert.IsTrue(SpeciesCatalog.Get("hitode").aquatic && SpeciesCatalog.Get("uni").aquatic, "水の中を歩く");
            Assert.IsTrue(SpeciesCatalog.Get("hitode").rideable, "ヒトデのせなかに乗れる");
            foreach (var id in CoastSpecies.Append("manta"))
            {
                var sp = SpeciesCatalog.Get(id);
                Assert.AreEqual(3, Friends.BehaviorsOf(id).Length, id);
                Assert.IsFalse(string.IsNullOrEmpty(Friends.Likes(id)), id);
                Assert.IsFalse(string.IsNullOrEmpty(Friends.Secret(id)), id);
                foreach (var b in Friends.BehaviorsOf(id))
                {
                    switch (b.key)
                    {
                        case "fly": Assert.AreEqual(MobKind.Bird, sp.kind, id); break;
                        case "ride": Assert.IsTrue(sp.rideable, id + "：乗れる"); break;
                        case "climb": Assert.IsTrue(sp.climbs, id + "：登る"); break;
                        case "creep": Assert.IsTrue(sp.aquatic, id); break;
                        case "sideways": Assert.AreEqual("crab", sp.BehaviorId, id); break;
                    }
                }
            }
            // 部品の付け方（Blender で作った形から）
            Assert.Less(CreatureRig.ShiomanekiShoulder.x, 0f, "大きなはさみは右（-X）");
            Assert.Less(CreatureRig.MantaFin.x, -1f, "むなびれは、体の横");
            Assert.Greater(CreatureRig.HitodeArmRoot, 0.2f);
        }

        [Test]
        public void Sea_IsRealWater_WithWavesTowardTheShore()
        {
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var sea = renderers.Single(r => r.name == "Sea");
            Assert.AreEqual("Shakutori/ToonSea", sea.sharedMaterial.shader.name, "海の水（白い板ではない）");
            Assert.AreEqual(ShakuConst.WaterLayer, sea.gameObject.layer);
            Assert.IsTrue(renderers.Any(r => r.name == "FarSea"), "沖の海");
            Assert.AreEqual(CoastLayout.Pools.Length, renderers.Count(r => r.name == "TidePool"), "潮だまりの水");
            // 低い所から見ても白くならない：水平に近い映りこみは、海の青
            var mat = sea.sharedMaterial;
            Color horizon = mat.GetColor("_HorizonReflect");
            Assert.Less(horizon.r + horizon.g + horizon.b, 1.5f, "水平の映りこみが明るすぎない");
            Assert.Greater(horizon.b, horizon.r);
            Assert.LessOrEqual(mat.GetFloat("_FresnelStrength"), 0.6f);
            // 波の強さ：海は 1、干潟と小川の中は 0（しずか）
            Assert.AreEqual(1f, WorldGenerator.SeaColor(0f, -40f).r, 0.01f);
            Assert.AreEqual(0f, WorldGenerator.SeaColor(CoastLayout.Fords[0].x, CoastLayout.Fords[0].y).r, 0.01f);
            Assert.Greater(WorldGenerator.SeaColor(0f, CoastLayout.ShoreZ(0f) - 1f).b, 0.8f, "波打ちぎわの近くで、波がくだける");
        }

        [Test]
        public void Map_ShowsSeaSandAndRocks()
        {
            var tex = _gen.MapTexture;
            Assert.IsNotNull(tex);
            Color Px(Vector2 w) => tex.GetPixelBilinear((w.x + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent), (w.y + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent));
            Color sea = Px(new Vector2(0f, -50f));
            Assert.Greater(sea.b, sea.r, "海は青い");
            Color sand = Px(new Vector2(-4f, 2f));
            Assert.Greater(sand.r, sand.b, "砂浜");
            Color shelf = Px(new Vector2(45f, -5f));
            Assert.Less(Mathf.Abs(shelf.r - shelf.b), 0.15f, "磯の岩は灰色");
            var look = AreaAtmosphere.For("coast");
            Assert.Greater(look.fogEnd, 250f, "沖の小島まで見える");
            Assert.IsTrue(GameUI.LivesIn(SpeciesCatalog.Get("kamome"), "海岸"));
        }
    }
}

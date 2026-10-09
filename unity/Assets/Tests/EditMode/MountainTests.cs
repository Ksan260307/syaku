using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>山エリアの生成（地形・名所・湧き水・しずく・いきもの・地図）と、山の 10 しゅのいきものを、実際に組み立てて確かめる。</summary>
    public class MountainTests
    {
        static WorldGenerator _gen;

        static readonly string[] MountainSpecies =
            { "raichou", "risu", "okojo", "nakiusagi", "sanshouuo", "asagimadara", "maruhanabachi", "oniyanma", "higurashi", "maimaikaburi" };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Mountain;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void Mountain_IsTheFourthArea_ConnectedToTheRiver()
        {
            Assert.AreSame(Areas.Mountain, Areas.All[3]);
            Assert.AreSame(Areas.Mountain, Areas.Get("mountain"));
            Assert.AreEqual(3000, Areas.Mountain.DropIdOffset);
            Assert.AreEqual(40, Areas.Mountain.DropCount);
            Assert.AreSame(Areas.Mountain, Areas.AreaOfDrop(3039));
            var toMountain = Areas.River.Gates.Single(g => g.targetArea == "mountain");
            Assert.IsTrue(toMountain.late, "川辺のしずくを変えないよう、あとから作るトンネル");
            Assert.IsTrue(Areas.Mountain.Gates.Any(g => g.targetArea == "river"), "山から川辺へもどれる");
            Assert.IsFalse(Areas.Forest.Gates.Any(g => g.targetArea == "mountain"), "山へは、川辺の上流から");
            var ids = MountainLayout.Landmarks.Select(l => l.id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(30, 9).ToList(), ids);
            Assert.IsTrue(MountainLayout.Landmarks.All(l => l.areaId == "mountain"));
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Select(l => l.id).Distinct().Count(), "名所の番号は重ならない");
        }

        [Test]
        public void Terrain_RisesToTheSummit()
        {
            float summit = MountainLayout.Height(MountainLayout.Summit.x, MountainLayout.Summit.y);
            float foot = MountainLayout.Height(MountainLayout.Spawn.x, MountainLayout.Spawn.y);
            Assert.Greater(summit - foot, 25f, "ふもとから山頂まで、高く登る");
            // 遊べる場所のなかで、山頂のあたりがいちばん高い（まわりの外周も、山頂より低い）
            for (float x = -90f; x <= 90f; x += 3f)
                for (float z = -90f; z <= 90f; z += 3f)
                {
                    if (Vector2.Distance(new Vector2(x, z), MountainLayout.Summit) < 10f) continue;
                    Assert.Less(MountainLayout.Height(x, z), summit + 0.5f, $"({x},{z}) が山頂より高い");
                }
            // ふもとのトンネルのまわりは平ら
            Assert.Greater(MountainLayout.Normal(MountainLayout.Gate.x, MountainLayout.Gate.y + 3f).y, 0.97f);
            // 岩の段（急ながけ）が、ところどころにある
            int steep = 0;
            for (float x = -60f; x <= 60f; x += 1.5f)
                for (float z = -60f; z <= 60f; z += 1.5f)
                    if (new Vector2(x, z).magnitude < 60f && MountainLayout.Normal(x, z).y < 0.8f) steep++;
            Assert.Greater(steep, 30, "岩のがけ");
        }

        [Test]
        public void Spring_IsClearDeepWaterInABasin()
        {
            Vector2 c = MountainLayout.Spring;
            float wl = MountainLayout.SpringLevel;
            Assert.Less(MountainLayout.Height(c.x, c.y), wl - 0.8f, "泉のまん中は深い");
            Assert.Greater(MountainLayout.Height(c.x + MountainLayout.SpringRadius + 2.5f, c.y), wl, "岸は水より高い");
            Assert.IsTrue(MountainLayout.IsUnderwater(new Vector3(c.x, wl - 0.5f, c.y)));
            Assert.IsFalse(MountainLayout.IsUnderwater(new Vector3(c.x + 20f, wl - 0.5f, c.y)));
            // 水は、川や水たまりと同じ描き方（白くならない）
            var water = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "Water").ToList();
            Assert.AreEqual(1, water.Count);
            Assert.AreSame(_gen.assets.pond, water[0].sharedMaterial);
            var mpb = new MaterialPropertyBlock();
            water[0].GetPropertyBlock(mpb);
            Assert.Less(mpb.GetFloat("_FoamDepth"), 0.05f, "岸ぎわの泡は、ふちだけ");
            // かけいの先は、水面の少し上
            Vector3 tip = _gen.SpringSpout;
            Assert.That(tip.y - wl, Is.InRange(0.8f, 3.5f), "かけいの先");
            Assert.Less(Vector2.Distance(new Vector2(tip.x, tip.z), c), MountainLayout.SpringRadius, "水は泉に落ちる");
        }

        [Test]
        public void Landmarks_AreBuiltAndReachable()
        {
            foreach (var name in new[] { "Mtn_Pine", "Mtn_Hut", "Mtn_RockArch", "Mtn_SummitPost" })
                Assert.IsTrue(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r => r.name == name), name);
            foreach (var lm in MountainLayout.Landmarks)
            {
                Vector3 p = _gen.ArrivalPoint(lm, out var f);
                Assert.IsFalse(MountainLayout.IsUnderwater(p), lm.name);
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), lm.position), lm.radius + 14f, lm.name + "：名所のそばに着く");
            }
            // 岩の階段：いちばん上の段は、名所の高さ（地面から 3）より高い
            Assert.AreEqual(6, _gen.StairTops.Count);
            var top = _gen.StairTops[_gen.StairTops.Count - 1];
            var stairs = MountainLayout.Landmarks.First(l => l.id == 31);
            Assert.Greater(top.y - MountainLayout.Height(top.x, top.z), stairs.minHeightAboveGround + 0.5f);
            Assert.Less(Vector2.Distance(new Vector2(top.x, top.z), stairs.position), stairs.radius, "段の上は、名所の中");
            for (int i = 1; i < _gen.StairTops.Count; i++)
                Assert.Less(_gen.StairTops[i].y - _gen.StairTops[i - 1].y, 1.6f, "段は、ひとつずつ登れる高さ");
            // 大きな松：枝の先の葉のかたまりに乗れる（名所の高さより上）
            var pine = MountainLayout.Landmarks.First(l => l.id == 34);
            int pads = 0;
            for (float a = 0f; a < Mathf.PI * 2f; a += 0.3f)
                for (float r = 3f; r < 12f; r += 1.5f)
                {
                    Vector2 p = pine.position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Physics.Raycast(new Vector3(p.x, 80f, p.y), Vector3.down, out var hit, 100f, ShakuConst.SurfaceMask)
                        && hit.point.y - MountainLayout.Height(p.x, p.y) > pine.minHeightAboveGround) pads++;
                }
            Assert.Greater(pads, 10, "登れる枝と葉のかたまり");
            // 岩のアーチは、下をくぐれる
            Vector2 arch = MountainLayout.RockArch;
            Vector3 g = MountainLayout.Ground(arch.x, arch.y);
            Assert.IsTrue(Physics.Raycast(g + Vector3.up * 3f, Vector3.up, 12f, ShakuConst.SurfaceMask), "上に岩がある");
            Assert.IsFalse(Physics.CheckSphere(g + Vector3.up * 1.6f, 1f, ShakuConst.SurfaceMask), "下は、くぐれる");
        }

        [Test]
        public void Dewdrops_AreAllPlacedOnDryLand()
        {
            Assert.AreEqual(40, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
            {
                Assert.IsFalse(MountainLayout.IsUnderwater(d), $"しずく {d} が水の中");
                Assert.IsTrue(MountainLayout.InPlayArea(d), $"しずく {d} が外");
            }
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
                for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                    Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 3f);
            // 高い所（松の葉の上・小屋の屋根・階段の上）にも、しずくがある
            int high = _gen.DewdropPoints.Count(d => d.y - MountainLayout.Height(d.x, d.z) > 3f);
            Assert.GreaterOrEqual(high, 5, "登ると取れるしずく");
        }

        [Test]
        public void MountainCreatures_LiveOnTheMountain()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in MountainSpecies)
            {
                CollectionAssert.Contains(ids, id, $"{id} が山にいる");
                var sp = SpeciesCatalog.Get(id);
                Assert.IsNotNull(sp, id);
                Assert.AreEqual("山", sp.areaLabel, id);
                Assert.IsFalse(sp.IsRare, id);
                Assert.IsNotNull(_gen.assets.TryGet(sp.body), $"{id} の体のメッシュ");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.Greater(CreatureRig.Legs(sp.rig).Length, 0, $"{id} の脚");
                foreach (var part in sp.parts) Assert.IsNotNull(_gen.assets.TryGet(part.mesh), $"{id} の {part.mesh}");
            }
            // オコジョは、岩のすみかのまん中にいる
            Assert.AreEqual(2, _gen.OkojoDens.Count);
            foreach (var d in _gen.OkojoDens)
                Assert.IsTrue(_gen.Mobs.Any(m => m.species == "okojo" && Vector2.Distance(new Vector2(m.center.x, m.center.z), d) < 0.5f));
            // 水辺のサンショウウオは、泉の近く
            foreach (var m in _gen.Mobs.Where(m => m.species == "sanshouuo"))
                Assert.Less(Vector2.Distance(new Vector2(m.center.x, m.center.z), MountainLayout.Spring), MountainLayout.SpringRadius + 4f);
        }

        [Test]
        public void MountainCreatures_BorrowTheRightWayOfMoving()
        {
            Assert.AreEqual("hato", SpeciesCatalog.Get("raichou").BehaviorId);
            Assert.AreEqual("tokage", SpeciesCatalog.Get("risu").BehaviorId);
            Assert.AreEqual("mogura", SpeciesCatalog.Get("okojo").BehaviorId);
            Assert.AreEqual("nakiusagi", SpeciesCatalog.Get("nakiusagi").BehaviorId, "自分の動き");
            Assert.AreEqual("tokage", SpeciesCatalog.Get("kameleon").BehaviorId, "レアは、元のいきものの動き");
            Assert.IsNull(SpeciesCatalog.RareVariantOf("risu"), "リスがカメレオンになることはない");
            // しぐさは、そのいきものにできること
            foreach (var id in MountainSpecies)
            {
                var sp = SpeciesCatalog.Get(id);
                foreach (var b in Friends.BehaviorsOf(id))
                {
                    switch (b.key)
                    {
                        case "hop": Assert.IsTrue(sp.kind == MobKind.Hopper || sp.kind == MobKind.Pouncer, id + "：跳ぶ"); break;
                        case "fly": Assert.AreEqual(MobKind.Bird, sp.kind, id); break;
                        case "rest": case "chase": case "wings": Assert.AreEqual(MobKind.Flutter, sp.kind, id + "：" + b.key); break;
                        case "hover": case "hunt": case "perch": Assert.AreEqual(MobKind.Hover, sp.kind, id + "：" + b.key); break;
                        case "ride": Assert.IsTrue(sp.rideable, id + "：乗れる"); break;
                        case "climb": Assert.IsTrue(sp.climbs, id + "：登る"); break;
                        case "popup": case "dig": case "sniff": Assert.AreEqual("mogura", sp.BehaviorId, id); break;
                    }
                }
            }
        }

        [Test]
        public void MountainFlowers_AreWhereButterfliesLand()
        {
            int alpine = _gen.instanced.Instances().Count(i => i.mesh != null && (i.mesh.name == "Mtn_Chinguruma" || i.mesh.name == "Mtn_Komakusa" || i.mesh.name == "Mtn_Kurumayuri"));
            Assert.Greater(alpine, 60, "高山の花");
            Assert.Greater(_gen.FlowerPoints.Count(p => Vector2.Distance(new Vector2(p.x, p.z), MountainLayout.Meadow) < 16f), 20, "花畑の花に、チョウがとまる");
            // 雪渓の上と、泉の中には、草花を植えない
            foreach (var i in _gen.instanced.Instances())
            {
                if (i.material != _gen.assets.foliage && i.material != _gen.assets.flowers) continue;
                Vector3 p = i.matrix.GetColumn(3);
                Assert.Less(MountainLayout.SnowMask(p.x, p.z), 0.6f, $"雪の上の {i.mesh.name}");
                Assert.IsFalse(MountainLayout.InSpring(p.x, p.z, -1f) && i.mesh.name != "Moss", $"泉の中の {i.mesh.name}");
            }
        }

        [Test]
        public void Map_ShowsTheMountain()
        {
            var tex = _gen.MapTexture;
            Assert.IsNotNull(tex);
            var other = _gen.MapFor(Areas.Park);
            Assert.AreNotSame(tex, other);
            // 泉は水色、山頂は明るい黄色の印
            Color Px(Vector2 w) => tex.GetPixelBilinear((w.x + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent), (w.y + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent));
            Color spring = Px(MountainLayout.Spring);
            Assert.Greater(spring.b, spring.r, "泉");
            Color summit = Px(MountainLayout.Summit);
            Assert.Greater(summit.r, 0.8f, "山頂の印");
        }
    }

    /// <summary>川辺の上流の、山へのトンネル（あとから作る）。</summary>
    public class RiverMountainGateTests
    {
        [Test]
        public void RiverGateToTheMountain_IsBuiltWithoutMovingTheDewdrops()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            var gen = go.AddComponent<WorldGenerator>();
            gen.assets = TestUtil.LoadWorldAssets();
            gen.instanced = go.AddComponent<InstancedRenderer>();
            var saved = Areas.Current;
            Areas.Current = Areas.River;
            try
            {
                gen.GenerateNow();
                Assert.AreEqual(Areas.River.DropCount, gen.DewdropPoints.Count);
                var gate = gen.Gates.SingleOrDefault(g => g.def.targetArea == "mountain");
                Assert.IsNotNull(gate, "山へのトンネル");
                Vector2 gp = new Vector2(gate.position.x, gate.position.z);
                Assert.IsFalse(Areas.River.IsUnderwater(gate.position));
                foreach (var d in gen.DewdropPoints)
                    Assert.Greater(Vector2.Distance(new Vector2(d.x, d.z), gp), 6f, "トンネルは、しずくの上に作らない");
                // トンネルのまわりと、出てきたときの景色の通り道には、草がない
                foreach (var i in gen.instanced.Instances())
                {
                    if (i.material != gen.assets.foliage && i.material != gen.assets.flowers) continue;
                    Vector3 p = i.matrix.GetColumn(3);
                    Assert.Greater(Vector2.Distance(new Vector2(p.x, p.z), gp), 5.4f, $"トンネルの中の {i.mesh.name}");
                }
                // 山からもどると、トンネルの前の、かわいた陸地に出て、滝の上の流れが見える
                var view = Areas.River.ArrivalViewFrom("mountain");
                Assert.IsNotNull(view);
                Assert.IsTrue(gen.ViewPoint(view, out var at, out var f), "着く場所がある");
                Assert.Less(Vector2.Distance(new Vector2(at.x, at.z), gp), 12f);
                Assert.Greater(Vector3.Dot(f, (gate.inward)), 0.2f, "川の方を向く");
            }
            finally
            {
                gen.Clear();
                Areas.Current = saved;
            }
        }
    }
}

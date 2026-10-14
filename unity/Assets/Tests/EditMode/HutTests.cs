using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>山小屋の中のエリア（部屋・家具・しずく・名所・いきもの・地図）と、山小屋の 6 しゅのいきものを、実際に組み立てて確かめる。</summary>
    public class HutTests
    {
        static WorldGenerator _gen;

        static readonly string[] HutSpecies = { "yamori", "mukade", "hasamimushi", "hae", "tsubame", "nezumi" };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Hut;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void Hut_IsTheFifthArea_EnteredThroughTheMountainHutDoor()
        {
            Assert.AreSame(Areas.Hut, Areas.All[4]);
            Assert.AreSame(Areas.Hut, Areas.Get("hut"));
            Assert.AreEqual("山小屋", Areas.Hut.DisplayName);
            Assert.AreEqual(4000, Areas.Hut.DropIdOffset);
            Assert.AreEqual(30, Areas.Hut.DropCount);
            Assert.AreSame(Areas.Hut, Areas.AreaOfDrop(4010));
            // 山の山小屋のとびらから入り、小屋のあいたとびらから山へもどる
            var door = Areas.Mountain.Gates.Single(g => g.targetArea == "hut");
            Assert.IsTrue(door.door, "トンネルではなく、とびら");
            Assert.IsTrue(door.late, "山のしずくを変えないよう、あとから作る");
            Assert.Less(Vector2.Distance(door.position, MountainLayout.Hut), 8f, "山小屋のとびら");
            var back = Areas.Hut.Gates.Single();
            Assert.AreEqual("mountain", back.targetArea);
            Assert.IsTrue(back.door);
            var ids = HutLayout.Landmarks.Select(l => l.id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(40, 9).ToList(), ids);
            Assert.IsTrue(HutLayout.Landmarks.All(l => l.areaId == "hut"));
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Select(l => l.id).Distinct().Count(), "名所の番号は重ならない");
        }

        [Test]
        public void Door_IsEnteredOnlyAtTheDoorway()
        {
            var g = Areas.Hut.Gates.Single();
            Vector3 at = new Vector3(g.position.x, 0f, g.position.y);
            Assert.IsTrue(g.DoorReached(at + new Vector3(0f, 0.3f, -0.5f), at), "しきいの上");
            Assert.IsTrue(g.DoorReached(at + new Vector3(12f, 0.3f, -1f), at), "とびらのはしの方でも");
            Assert.IsFalse(g.DoorReached(at + new Vector3(0f, 0.3f, 4f), at), "部屋の中");
            Assert.IsFalse(g.DoorReached(at + new Vector3(30f, 0.3f, -0.5f), at), "とびらの横のかべ");
            Assert.IsFalse(g.DoorReached(at + new Vector3(0f, 40f, -0.5f), at), "とびらの上（かべを登っている）");
            Assert.IsTrue(g.DoorNear(at + new Vector3(4f, 0.3f, 4f), at), "とびらのすぐ前では、ヒントが出る");
            Vector3 spawn = new Vector3(HutLayout.Spawn.x, 0.3f, HutLayout.Spawn.y);
            Assert.IsFalse(g.DoorNear(spawn, at), "入り口の前に着いたときは、まだヒントを出さない");
            Assert.IsTrue(HutLayout.InPlayArea(at + new Vector3(0f, 0.3f, -2f)), "しきいの上まで行ける");
            // 入り口の前の上には、はりがない（地図から来ても、はりの上ではなく床に着く）
            Assert.Less(_gen.TopSurface(HutLayout.Spawn).y, 2f, "入り口の前は、床");
            Assert.IsFalse(HutLayout.InPlayArea(new Vector3(30f, 0.3f, -HutLayout.HalfZ - 2f)), "かべの外へは出ない");
            Assert.IsFalse(HutLayout.InPlayArea(new Vector3(0f, HutLayout.MaxClimbHeight + 1f, 0f)), "屋根うらの上の方へは行かない");
        }

        [Test]
        public void Room_HasWallsBeamsAndAOneSidedCeiling()
        {
            foreach (var name in new[] { "Hut_Walls", "Hut_Roof", "Hut_Beams", "Hut_Stove", "Hut_Bunk", "Hut_Table", "Hut_View", "Hut_StoveFire", "Hut_LampGlow" })
                Assert.IsTrue(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r => r.name == name), name);
            // かべにぶつかる（窓は、かべの外まで通る）
            Assert.IsTrue(Physics.Raycast(new Vector3(0f, 20f, 0f), Vector3.right, 80f, ShakuConst.SurfaceMask), "東のかべ");
            Assert.IsTrue(Physics.Raycast(new Vector3(0f, 20f, 0f), Vector3.forward, 80f, ShakuConst.SurfaceMask), "北のかべ");
            Assert.IsFalse(Physics.Raycast(new Vector3(-40f, 2f, -40f), Vector3.back, 40f, ShakuConst.SurfaceMask), "とびらは、あいている");
            // はりの上に乗れる
            Assert.IsTrue(Physics.Raycast(new Vector3(0f, 95f, 10f), Vector3.down, out var beam, 30f, ShakuConst.SurfaceMask));
            Assert.AreEqual(HutLayout.BeamTop, beam.point.y, 0.2f, "はりの上");
            // 屋根うら：中から見上げると当たる（カメラが屋根の外へ出ない）。上からのレイは、通りぬける
            Assert.IsTrue(Physics.Raycast(new Vector3(20f, 60f, -20f), Vector3.up, 80f, ShakuConst.SurfaceMask), "天井");
            Assert.IsTrue(Physics.Raycast(new Vector3(20f, 200f, -20f), Vector3.down, out var down, 300f, ShakuConst.SurfaceMask));
            Assert.Less(down.point.y, HutLayout.WallHeight, "上からは、屋根を通りぬけて、部屋の中の物に当たる");
        }

        [Test]
        public void Dewdrops_AreOnTheFloorFurnitureSillsAndBeams()
        {
            Assert.AreEqual(30, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
            {
                Assert.IsTrue(HutLayout.InPlayArea(d), $"しずく {d} が外");
                Assert.IsTrue(HutLayout.InRoom(d.x, d.z), $"しずく {d} が部屋の外");
            }
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
                for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                    Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 3f);
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y > 25f), 12, "家具や窓台の上");
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y > HutLayout.BeamTop - 1f), 3, "はりの上");
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y < 1f), 8, "床の上");
            Assert.AreEqual(0, _gen.DewdropPoints.Count(d => Areas.Hut.IsUnderwater(d)));
        }

        [Test]
        public void Landmarks_ArriveInsideAndHighPlacesCanBeReached()
        {
            foreach (var lm in HutLayout.Landmarks)
            {
                Vector3 p = _gen.ArrivalPoint(lm, out var f);
                Assert.IsTrue(HutLayout.InRoom(p.x, p.z), lm.name + "：部屋の中に着く");
                // 高い所の名所（テーブル・ベッド・棚・窓台・はり・巣）へ移動したときは、その上に着く。ほかは床
                if (lm.TravelView != null && lm.TravelView.onTop) Assert.GreaterOrEqual(p.y, lm.minHeightAboveGround, lm.name + "：高い所の上に着く");
                else Assert.Less(p.y, 1f, lm.name + "：床に着く");
                if (lm.minHeightAboveGround <= 0f) continue;
                // 高い名所：名所の中に、その高さより上の面がある
                bool found = false;
                for (float r = 0f; r <= lm.radius && !found; r += 2f)
                    for (float a = 0f; a < Mathf.PI * 2f && !found; a += 0.4f)
                    {
                        Vector2 q = lm.position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (Physics.Raycast(new Vector3(q.x, HutLayout.MaxClimbHeight - 1f, q.y), Vector3.down, out var hit, 120f, ShakuConst.SurfaceMask)
                            && hit.point.y >= lm.minHeightAboveGround && hit.normal.y > 0.7f) found = true;
                    }
                Assert.IsTrue(found, lm.name + "：登れる所がある");
            }
        }

        [Test]
        public void HutCreatures_LiveInTheHut()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in HutSpecies)
            {
                CollectionAssert.Contains(ids, id, $"{id} が山小屋にいる");
                var sp = SpeciesCatalog.Get(id);
                Assert.IsNotNull(sp, id);
                Assert.AreEqual("山小屋", sp.areaLabel, id);
                Assert.IsFalse(sp.IsRare, id);
                Assert.IsNotNull(_gen.assets.TryGet(sp.body), $"{id} の体のメッシュ");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.IsTrue(CreatureRig.HasLegs(sp.rig), $"{id} の脚");
                foreach (var part in sp.parts) Assert.IsNotNull(_gen.assets.TryGet(part.mesh), $"{id} の {part.mesh}");
            }
            foreach (var mesh in new[] { "Mukade_Seg", "Mukade_Tail", "Mukade_Leg", "Tsubame_WingFolded" })
                Assert.IsNotNull(_gen.assets.TryGet(mesh), mesh);
            // ヤモリは、かべの上（地面ではない）
            foreach (var g in _gen.Mobs.Where(m => m.species == "yamori"))
            {
                Assert.Greater(g.center.y, 20f, "かべの高い所");
                Assert.Greater(g.up.sqrMagnitude, 0.5f, "かべの向き");
                Assert.IsTrue(Physics.Raycast(g.center + g.up * 1f, -g.up, 2f, ShakuConst.SurfaceMask), "うしろは、かべ");
            }
            // ツバメは、はりの上の巣と、はり・窓台に下りる
            var swallow = _gen.Mobs.Single(m => m.species == "tsubame");
            Assert.Greater(swallow.center.y, HutLayout.BeamTop - 0.5f, "巣は、はりの上");
            Assert.GreaterOrEqual(swallow.path.Count(p => p.y > HutLayout.BeamTop - 0.5f), 3, "はりの上に下りる");
            // 小屋のいきものは、みんな部屋の中
            foreach (var g in _gen.Mobs)
                Assert.IsTrue(HutLayout.InPlayArea(g.center), $"{g.species} {g.center}");
        }

        [Test]
        public void HutCreatures_BorrowTheRightWayOfMoving()
        {
            Assert.AreEqual("tokage", SpeciesCatalog.Get("yamori").BehaviorId, "ヤモリは、トカゲのように走って止まる");
            Assert.AreEqual("tokage", SpeciesCatalog.Get("nezumi").BehaviorId);
            Assert.AreEqual("sparrow", SpeciesCatalog.Get("tsubame").BehaviorId);
            Assert.AreEqual("dragonfly", SpeciesCatalog.Get("hae").BehaviorId);
            Assert.AreEqual("mukade", SpeciesCatalog.Get("mukade").BehaviorId, "自分の動き");
            Assert.AreEqual(MobKind.Bird, SpeciesCatalog.Get("tsubame").kind);
            Assert.AreEqual(MobKind.Hover, SpeciesCatalog.Get("hae").kind);
            foreach (var id in HutSpecies)
            {
                var sp = SpeciesCatalog.Get(id);
                Assert.AreEqual(3, Friends.BehaviorsOf(id).Length, id);
                foreach (var b in Friends.BehaviorsOf(id))
                {
                    switch (b.key)
                    {
                        case "fly": Assert.AreEqual(MobKind.Bird, sp.kind, id); break;
                        case "hover": case "perch": Assert.AreEqual(MobKind.Hover, sp.kind, id + "：" + b.key); break;
                        case "ride": Assert.IsTrue(sp.rideable, id + "：乗れる"); break;
                        case "climb": Assert.IsTrue(sp.climbs, id + "：登る"); break;
                    }
                }
            }
            // ムカデの体節と脚の付け方（Blender で作った形から）
            Assert.AreEqual(19, CreatureRig.MukadeSegments);
            Assert.Greater(CreatureRig.MukadeSegment, 0.15f);
            Assert.Less(CreatureRig.MukadeHip.x, 0f, "右の脚の付け根は、体の右（-X）");
        }

        [Test]
        public void Map_ShowsTheRoom()
        {
            var tex = _gen.MapTexture;
            Assert.IsNotNull(tex);
            Color Px(Vector2 w) => tex.GetPixelBilinear((w.x + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent), (w.y + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent));
            Color rug = Px(HutLayout.Rug);
            Assert.Greater(rug.r, rug.b, "ラグ");
            Color stove = Px(HutLayout.Stove);
            Assert.Less(stove.r + stove.g + stove.b, 0.8f, "まきストーブは黒い");
            Color floor = Px(new Vector2(-20f, 20f));
            Assert.Greater(floor.r, floor.b, "床板");
        }

        [Test]
        public void Hut_HasNoWaterAndWarmIndoorLight()
        {
            for (float x = -70f; x <= 70f; x += 10f)
                for (float z = -50f; z <= 50f; z += 10f)
                    Assert.Less(Areas.Hut.WaterLevelAt(x, z), -100f, "小屋の中に水はない（白い水たまりを作らない）");
            var look = AreaAtmosphere.For("hut");
            Assert.Greater(look.sky.r, look.sky.b, "あたたかい色の空気");
            Assert.Greater(AreaAtmosphere.ShadowDistance("hut", 1), 140f, "部屋の奥まで、かべと屋根のかげ");
            Assert.AreEqual(48f, AreaAtmosphere.ShadowDistance("forest", 1));
            Assert.IsTrue(GameUI.LivesIn(SpeciesCatalog.Get("yamori"), "山小屋"));
            Assert.IsFalse(GameUI.LivesIn(SpeciesCatalog.Get("yamori"), "山"), "「山」でしぼりこんでも、山小屋のいきものは出ない");
        }
    }

    /// <summary>山の山小屋のとびら（あとから作る。山のしずくは変えない）。</summary>
    public class MountainHutDoorTests
    {
        [Test]
        public void HutDoor_IsBuiltWithoutMovingTheDewdrops()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            var gen = go.AddComponent<WorldGenerator>();
            gen.assets = TestUtil.LoadWorldAssets();
            gen.instanced = go.AddComponent<InstancedRenderer>();
            var saved = Areas.Current;
            Areas.Current = Areas.Mountain;
            try
            {
                gen.GenerateNow();
                Assert.AreEqual(Areas.Mountain.DropCount, gen.DewdropPoints.Count);
                var door = gen.Gates.SingleOrDefault(g => g.def.targetArea == "hut");
                Assert.IsNotNull(door, "山小屋のとびら");
                Assert.AreEqual(2, gen.Gates.Count, "ふもとのトンネルと、山小屋のとびら");
                Assert.IsFalse(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Count(r => r.name == "RootArch") > 1, "とびらには、木の根のトンネルを作らない");
                // とびらは、山小屋のかべの前（小屋の当たり判定に、とびらの方から当たる）
                Vector3 front = door.position + door.inward * 2f + Vector3.up * 1.6f;
                Assert.IsTrue(Physics.Raycast(front, -door.inward, out var hit, 3f, ShakuConst.SurfaceMask), "とびらの面");
                Assert.AreEqual("Mtn_Hut", hit.collider.name);
                Assert.IsTrue(door.def.DoorReached(hit.point - door.inward * 0.15f, door.position), "とびらにふれると入る");
                Assert.IsFalse(door.def.DoorReached(door.position + door.inward * 3f + Vector3.up * 0.8f, door.position), "石段の下では、まだ");
                // 小屋から出ると、とびらの前の、かわいた陸地に出て、花畑の方を見る
                var view = Areas.Mountain.ArrivalViewFrom("hut");
                Assert.IsNotNull(view);
                Assert.IsTrue(gen.ViewPoint(view, out var at, out var f), "着く場所がある");
                Assert.Less(Vector2.Distance(new Vector2(at.x, at.z), MountainLayout.HutDoor), 8f);
                Assert.Greater(Vector3.Dot(f, door.inward), 0.2f, "小屋から外を向く");
            }
            finally
            {
                gen.Clear();
                Areas.Current = saved;
            }
        }
    }
}

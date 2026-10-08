using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>公園エリアの生成（遊具・水たまり・しずく・名所・いきもの）を、実際に組み立てて確かめる。</summary>
    public class ParkTests
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
            Areas.Current = Areas.Park;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void Park_IsTheThirdArea_ConnectedToTheForest()
        {
            Assert.AreEqual(3, Areas.All.Length);
            Assert.AreSame(Areas.Park, Areas.Get("park"));
            Assert.IsTrue(Areas.Forest.Gates.Any(g => g.targetArea == "park"), "森から公園へのトンネル");
            Assert.IsTrue(Areas.Park.Gates.Any(g => g.targetArea == "forest"), "公園から森へのトンネル");
            Assert.AreEqual(2000, Areas.Park.DropIdOffset);
            Assert.AreSame(Areas.Park, Areas.AreaOfDrop(2005));
            Assert.AreSame(Areas.River, Areas.AreaOfDrop(1005));
        }

        [Test]
        public void Playground_IsBuilt()
        {
            Assert.IsTrue(_gen.IsGenerated);
            Assert.IsNotNull(_gen.Slide, "すべり台のそり");
            Assert.IsNotNull(_gen.Seesaw, "シーソー");
            Assert.AreEqual(2, _gen.Swings.Count, "ブランコの座板 2 つ");
            foreach (var name in new[] { "Park_SlideFrame", "Park_SlideRamp", "Park_SwingFrame", "Park_JungleGym", "Park_Dokan", "Park_Bench", "Park_Fountain", "Park_Kunugi" })
                Assert.IsTrue(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r => r.name == name), name);
            // 遊具には登れる（当たり判定がある）
            Assert.IsTrue(Physics.Raycast(new Vector3(ParkLayout.Slide.x, 30f, ParkLayout.Slide.y), Vector3.down, out var top, 40f, ShakuConst.SurfaceMask));
            Assert.Greater(top.point.y - ParkLayout.Height(ParkLayout.Slide.x, ParkLayout.Slide.y), 9f, "すべり台の台の上");
            Assert.IsTrue(Physics.Raycast(new Vector3(ParkLayout.JungleGym.x, 30f, ParkLayout.JungleGym.y), Vector3.down, 40f, ShakuConst.SurfaceMask), "ジャングルジム");
        }

        [Test]
        public void Puddle_IsWaterDrawnLikeTheRiver()
        {
            Vector2 c = ParkLayout.Puddle;
            Assert.IsTrue(ParkLayout.IsUnderwater(new Vector3(c.x, ParkLayout.Height(c.x, c.y) + 0.1f, c.y)), "水たまりの中は水");
            Assert.IsFalse(ParkLayout.IsUnderwater(ParkLayout.Ground(ParkLayout.Spawn.x, ParkLayout.Spawn.y) + Vector3.up * 0.1f));
            var water = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == "Water");
            Assert.IsNotNull(water);
            Assert.AreSame(_gen.assets.pond, water.sharedMaterial);
            Assert.Greater(WaterView.WaterCount, 0);
        }

        [Test]
        public void Dewdrops_AreAllPlacedOnLand()
        {
            Assert.AreEqual(Areas.Park.DropCount, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
            {
                Assert.IsTrue(ParkLayout.InPlayArea(d));
                Assert.IsFalse(ParkLayout.IsUnderwater(d), "しずくが水の中");
            }
            Assert.IsTrue(_gen.DewdropPoints.Any(d => d.y - ParkLayout.Height(d.x, d.z) > 8f), "遊具の上にもしずくがある");
        }

        [Test]
        public void Landmarks_HaveDryViewpoints()
        {
            Assert.AreEqual(10, ParkLayout.Landmarks.Count);
            foreach (var lm in ParkLayout.Landmarks)
            {
                Assert.AreEqual("park", lm.areaId);
                Assert.IsNotNull(lm.view, lm.name);
                Assert.IsTrue(_gen.ViewPoint(lm.view, out var p, out var f), $"{lm.name}：景色を見る場所に立てる");
                Assert.IsFalse(ParkLayout.IsUnderwater(p), lm.name);
                Assert.Greater(Vector3.Dot(f, lm.view.Forward), 0.6f, lm.name);
            }
            Assert.IsTrue(_gen.ViewPoint(ParkLayout.Landmarks[1].view, out var slideTop, out _));
            Assert.Greater(slideTop.y - ParkLayout.Height(slideTop.x, slideTop.z), 9f, "すべり台の景色は、てっぺんから");
        }

        [Test]
        public void NewCreatures_LiveInThePark()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in new[] { "kamikiri", "kuwagata", "kamemushi", "tokage", "monshiro" })
                CollectionAssert.Contains(ids, id, $"{id} が公園にいる");
            Assert.AreEqual(23, SpeciesCatalog.Count, "図鑑は 23 種");
            foreach (var id in new[] { "kamikiri", "kuwagata", "kamemushi", "tokage", "monshiro" })
            {
                var sp = SpeciesCatalog.Get(id);
                Assert.IsNotNull(sp, id);
                Assert.AreEqual("公園", sp.areaLabel);
                Assert.IsNotNull(_gen.assets.Get(sp.body), $"{id} の体");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.IsTrue(CreatureRig.HasLegs(sp.rig), $"{id} の脚");
            }
            Assert.IsTrue(SpeciesCatalog.Get("kuwagata").rideable && SpeciesCatalog.Get("tokage").rideable, "クワガタとトカゲには乗れる");
        }

        [Test]
        public void Map_IsDrawn()
        {
            Assert.IsNotNull(_gen.MapTexture);
            Assert.AreEqual(1, _gen.Gates.Count);
        }
    }
}

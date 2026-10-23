using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// クリスマス村のエリア（光の輪・大きなツリーとプレゼント・雪だるま・そり・こおった池・あかりのともる家・ポストと手ぶくろ・
    /// ランプ・電球のさく・しずく・名所・いきもの・地図・雪の夜の光）と、クリスマス村の 5 しゅのいきものを、実際に組み立てて確かめる。
    /// </summary>
    public class XmasTests
    {
        static WorldGenerator _gen;

        static readonly string[] XmasSpecies = { "tonakai", "yukiusagi", "robin", "fuyushaku", "yamane" };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Xmas;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        [Test]
        public void XmasVillage_IsTheNinthArea_ReachedByAWarpRingFromTheNightForest()
        {
            Assert.AreSame(Areas.Xmas, Areas.All[8]);
            Assert.AreSame(Areas.Xmas, Areas.Get("xmas"));
            Assert.AreEqual("クリスマス村", Areas.Xmas.DisplayName);
            Assert.AreEqual(8000, Areas.Xmas.DropIdOffset);
            Assert.AreEqual(30, Areas.Xmas.DropCount);
            Assert.AreSame(Areas.Xmas, Areas.AreaOfDrop(8029));
            Assert.AreSame(Areas.Night, Areas.AreaOfDrop(7029));
            // 夜の森の星見の丘の、光の輪（あとから作る。夜の森のしずくは変えない）と、クリスマス村から夜の森へもどる光の輪
            var toXmas = Areas.Night.Gates.Single(g => g.targetArea == "xmas");
            Assert.IsTrue(toXmas.late, "夜の森のしずくを変えないよう、あとから作る");
            Assert.IsTrue(toXmas.warp, "トンネルではなく、光の輪");
            Assert.Less(toXmas.position.magnitude, NightLayout.PlayRadius - 4f, "夜の森の中");
            Assert.Less(Vector2.Distance(toXmas.position, NightLayout.StarRock), 22f, "星見の丘のそば");
            Assert.Greater(Vector2.Distance(toXmas.position, NightLayout.StarRock), 10f, "星見の岩とは、はなれている");
            Vector2 inward = -toXmas.position.normalized;
            Assert.Greater(Vector2.Dot(new Vector2(toXmas.arrival.Forward.x, toXmas.arrival.Forward.z), inward), 0.3f, "もどってきたら、夜の森の中を向く");
            var back = Areas.Xmas.Gates.Single();
            Assert.AreEqual("night", back.targetArea);
            Assert.IsTrue(back.warp);
            Assert.IsTrue(Areas.Night.ArrivalFrom("xmas", out var nightArrive, out _));
            Assert.Less(Vector2.Distance(nightArrive, toXmas.position), 8f, "クリスマス村からもどると、光の輪の前に出る");
            Assert.IsTrue(Areas.Xmas.ArrivalFrom("night", out var xmasArrive, out var f));
            Assert.Less(Vector2.Distance(xmasArrive, XmasLayout.Spawn), 1f);
            Assert.Greater(f.z, 0.9f, "着いたら、北のツリーの方を向く");
            // 光の輪のヒント
            StringAssert.StartsWith("光の輪をくぐると", GameManager.GateHintText(Areas.Xmas, warp: true));
            var ids = XmasLayout.Landmarks.Select(l => l.id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(80, 9).ToList(), ids);
            Assert.IsTrue(XmasLayout.Landmarks.All(l => l.areaId == "xmas"));
            Assert.AreEqual(Areas.TotalLandmarks, Areas.AllLandmarks().Select(l => l.id).Distinct().Count(), "名所の番号は重ならない");
        }

        [Test]
        public void Ground_IsSnowWithAFlatPlaza_AFrozenPond_AndNoWater()
        {
            // 水はない（池はこおっている）
            Assert.AreEqual(-999f, Areas.Xmas.WaterLevelAt(XmasLayout.Pond.x, XmasLayout.Pond.y));
            Assert.IsFalse(Areas.Xmas.IsUnderwater(new Vector3(XmasLayout.Pond.x, -5f, XmasLayout.Pond.y)));
            // ツリーの広場と、家の前は平ら
            foreach (var p in new[] { XmasLayout.Tree + new Vector2(20f, 0f), XmasLayout.Tree + new Vector2(-20f, 5f), XmasLayout.Tree + new Vector2(0f, -22f) })
                Assert.AreEqual(0.15f, XmasLayout.Height(p.x, p.y), 0.25f, $"広場 {p}");
            Vector2 door = XmasLayout.CottageDoor;
            Assert.AreEqual(0f, XmasLayout.Height(door.x, door.y), 0.1f, "家の前");
            // 氷の上：池のまん中は、まわりより低い（氷の板が、上にのる）
            Assert.Less(XmasLayout.Height(XmasLayout.Pond.x, XmasLayout.Pond.y), XmasLayout.IceTop - 0.3f);
            // 雪の色：白っぽい青（白くとびすぎない）。小道は、ふみかためた灰色
            Vector3 n = Vector3.up;
            Color snow = XmasLayout.GroundColor(-40f, 10f, 0f, n);
            Assert.Greater(snow.b, snow.r, "青みのある雪");
            Assert.Greater(snow.grayscale, 0.6f, "雪は明るい");
            Assert.Less(snow.maxColorComponent, 0.98f, "まっ白にはしない");
            Color path = XmasLayout.GroundColor(0f, -40f, 0f, n);
            Assert.Less(path.grayscale, snow.grayscale, "小道は、ふみかためた雪");
            // 外周はせり上がる
            Assert.Greater(XmasLayout.Height(0f, 90f), XmasLayout.Height(0f, 55f) + 4f);
        }

        [Test]
        public void VillageThings_AreBuilt()
        {
            var names = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r => r.name).ToList();
            foreach (var nm in new[] { "Xmas_Tree", "Xmas_TreeLights", "Xmas_TreeStar", "Xmas_Snowman", "Xmas_Cottage", "Xmas_CottageWindow", "Xmas_Mailbox",
                                       "Xmas_Mitten", "Xmas_Ice", "Xmas_WarpRing", "Xmas_WarpLights" })
                CollectionAssert.Contains(names, nm, nm);
            // 光る物は、光る材質
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "Xmas_TreeLights" || r.name == "Xmas_CottageWindow"))
                Assert.AreSame(_gen.assets.lightGlow, r.sharedMaterial, r.name);
            // ツリー：枝の段の上は登れる坂。てっぺんは高い
            Vector2 t = XmasLayout.Tree;
            Assert.IsTrue(Physics.Raycast(new Vector3(t.x + 0.5f, 80f, t.y - 0.5f), Vector3.down, out var top, 90f, ShakuConst.SurfaceMask));
            Assert.AreEqual("Xmas_Tree", top.collider.name);
            Assert.Greater(top.point.y, 45f, "ツリーのてっぺん");
            Assert.IsTrue(Physics.Raycast(new Vector3(t.x, 80f, t.y - 15.5f), Vector3.down, out var tier, 90f, ShakuConst.SurfaceMask));
            Assert.AreEqual("Xmas_Tree", tier.collider.name, "いちばん下の段のすそ");
            Assert.Greater(tier.normal.y, 0.45f, "段の上は、上を向く坂");
            // 家の窓台は、登って乗れる
            Vector2 sill = XmasLayout.CottageLocal(XmasLayout.WindowX - 5f, XmasLayout.CottageFront - 2.4f);
            Assert.IsTrue(Physics.Raycast(new Vector3(sill.x, XmasLayout.SillY + 4f, sill.y), Vector3.down, out var s, 10f, ShakuConst.SurfaceMask));
            Assert.AreEqual("Xmas_Cottage", s.collider.name);
            Assert.AreEqual(XmasLayout.SillY, s.point.y, 1f, "窓台の上");
            // 氷の上は、歩ける（当たり判定がある）。光の輪は、くぐれる（当たり判定がない）
            Assert.IsTrue(Physics.Raycast(new Vector3(XmasLayout.Pond.x, 10f, XmasLayout.Pond.y), Vector3.down, out var ice, 20f, ShakuConst.SurfaceMask));
            Assert.AreEqual("Xmas_Ice", ice.collider.name);
            Assert.AreEqual(XmasLayout.IceTop, ice.point.y, 0.1f);
            var gate = _gen.Gates.Single();
            Assert.IsFalse(Physics.Raycast(gate.position + Vector3.up * 3f - gate.inward * 3f, gate.inward, 6f, ShakuConst.SurfaceMask), "光の輪の中は、通りぬけられる");
            // ランプのあかり・コマドリのとまり木・手ぶくろの口
            Assert.GreaterOrEqual(_gen.XmasLamps.Count, 6);
            Assert.GreaterOrEqual(_gen.XmasPerches.Count, 4, "さくのくい・ポスト・雪だるまのぼうし・ランプの屋根");
            foreach (var p in _gen.XmasPerches) Assert.Greater(p.y - XmasLayout.Height(p.x, p.z), 8f, "高い所");
            Vector3 mouth = _gen.MittenMouth;
            Assert.Less(Vector2.Distance(new Vector2(mouth.x, mouth.z), XmasLayout.Mitten), 6f, "手ぶくろのそば");
        }

        [Test]
        public void Dewdrops_AreOnTheVillageThings_AndTheTreetop()
        {
            Assert.AreEqual(30, _gen.DewdropPoints.Count);
            foreach (var d in _gen.DewdropPoints)
                Assert.IsTrue(XmasLayout.InPlayArea(d), $"しずく {d} が外");
            for (int i = 0; i < _gen.DewdropPoints.Count; i++)
                for (int j = i + 1; j < _gen.DewdropPoints.Count; j++)
                    Assert.Greater(Vector3.Distance(_gen.DewdropPoints[i], _gen.DewdropPoints[j]), 3f);
            Assert.GreaterOrEqual(_gen.DewdropPoints.Count(d => d.y - XmasLayout.Height(d.x, d.z) > 3f), 10, "ツリー・プレゼント・雪だるま・家などの上");
            Vector2 t = XmasLayout.Tree;
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Vector2.Distance(new Vector2(d.x, d.z), t) < 3f && d.y > 45f), "ツリーのてっぺん（星のとなり）");
            Assert.IsTrue(_gen.DewdropPoints.Any(d => Mathf.Abs(d.y - XmasLayout.SillY) < 1.2f && Vector2.Distance(new Vector2(d.x, d.z), XmasLayout.CottageWindow) < 9f), "家の窓台");
            Assert.IsTrue(_gen.DewdropPoints.Any(d => XmasLayout.OnIce(d.x, d.z, 0f) && d.y < 0.5f), "こおった池の上");
        }

        [Test]
        public void Landmarks_ArriveOnLand()
        {
            foreach (var lm in XmasLayout.Landmarks)
            {
                Vector3 p = _gen.ArrivalPoint(lm, out var f);
                Assert.IsTrue(XmasLayout.InPlayArea(p), lm.name);
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), lm.TravelView.from), 3.5f, lm.name + "：景色の所に着く");
                if (lm.TravelView.onTop) Assert.Greater(p.y - XmasLayout.Height(p.x, p.z), lm.minHeightAboveGround, lm.name + "：てっぺんに着く");
            }
        }

        [Test]
        public void XmasCreatures_LiveInTheVillage()
        {
            var ids = _gen.Mobs.Select(m => m.species).ToList();
            foreach (var id in XmasSpecies)
            {
                CollectionAssert.Contains(ids, id, $"{id} がクリスマス村にいる");
                var sp = SpeciesCatalog.Get(id);
                Assert.AreEqual("クリスマス村", sp.areaLabel, id);
                Assert.IsFalse(sp.IsRare, id);
                Assert.IsNotNull(_gen.assets.TryGet(sp.body), $"{id} の体のメッシュ");
                Assert.IsNotNull(_gen.assets.Portrait(id), $"{id} の図鑑の絵");
                if (sp.rig != null) Assert.IsTrue(CreatureRig.HasLegs(sp.rig), $"{id} の脚");
                foreach (var part in sp.parts) Assert.IsNotNull(_gen.assets.TryGet(part.mesh), $"{id} の {part.mesh}");
                Assert.IsTrue(GameUI.LivesIn(sp, "クリスマス村"), id);
                Assert.AreEqual(3, Friends.BehaviorsOf(id).Length, id);
            }
            // ヤマネは、手ぶくろの口で冬眠（決めた所のまま）
            var dor = _gen.Mobs.Single(m => m.species == "yamane");
            Assert.AreEqual(WorldGenerator.CreatureHome.Hanging, _gen.HomeOf(dor));
            Assert.Greater(dor.face.sqrMagnitude, 0.5f, "手ぶくろの外を向く");
            // コマドリは 1 羽で、とまり木をめぐる。フユシャクは、ランプのあかりをめぐる
            var robin = _gen.Mobs.Single(m => m.species == "robin");
            Assert.AreEqual(1, robin.count);
            Assert.GreaterOrEqual(robin.path.Count, 4);
            foreach (var moth in _gen.Mobs.Where(m => m.species == "fuyushaku"))
                Assert.AreEqual(_gen.XmasLamps.Count, moth.path.Count);
            Assert.AreEqual(2, _gen.Mobs.Where(m => m.species == "tonakai").Sum(m => m.count), "トナカイは 2 頭");
            foreach (var g in _gen.Mobs) Assert.IsTrue(XmasLayout.InPlayArea(g.center), $"{g.species} {g.center}");
        }

        [Test]
        public void XmasCreatures_HaveTheirOwnWaysOfMoving()
        {
            Assert.AreEqual("sparrow", SpeciesCatalog.Get("robin").BehaviorId, "コマドリは、スズメと同じ鳥の動き方（シマエナガと同じ、とまり木めぐり）");
            Assert.AreEqual("nakiusagi", SpeciesCatalog.Get("yukiusagi").BehaviorId, "ユキウサギは、ナキウサギと同じ跳び方");
            Assert.AreEqual(MobKind.Hopper, SpeciesCatalog.Get("yukiusagi").kind);
            Assert.AreEqual(MobKind.Flutter, SpeciesCatalog.Get("fuyushaku").kind);
            Assert.AreEqual(0f, SpeciesCatalog.Get("yamane").speed, "冬眠中");
            // 大きさ：トナカイ・ユキウサギ・コマドリ・ヤマネは、ほかのけものや鳥と同じように、ちぢめて描く（フユシャクは、そのまま）
            Assert.Less(SpeciesCatalog.Get("tonakai").scale, SpeciesCatalog.Get("yukiusagi").scale);
            Assert.Less(SpeciesCatalog.Get("yukiusagi").scale, SpeciesCatalog.Get("robin").scale);
            Assert.AreEqual(1f, SpeciesCatalog.Get("fuyushaku").scale);
            Assert.Less(SpeciesCatalog.Get("tonakai").scale * _gen.assets.TryGet("Tonakai").bounds.size.z, 14f, "トナカイは、村の物にくらべて大きすぎない");
            // 部品の付け方（Blender で作った形から）
            Assert.Less(CreatureRig.FuyushakuWingRoot.x, 0f, "フユシャクのはねは、右（-X）");
            Assert.Less(CreatureRig.YamaneEye.z, 0f, "ヤマネの目は、前（-Z）");
            Assert.IsNotNull(_gen.assets.TryGet("Robin_WingFolded"), "とまっているときの、たたんだ羽");
            foreach (var id in XmasSpecies)
                foreach (var b in Friends.BehaviorsOf(id))
                    Assert.IsTrue(Creatures.BehaviorKeys.Contains(b.key), $"{id}：{b.key}");
        }

        [Test]
        public void Map_AndLight_ShowTheSnowyNight()
        {
            var tex = _gen.MapTexture;
            Assert.IsNotNull(tex);
            Color Px(Vector2 w) => tex.GetPixelBilinear((w.x + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent), (w.y + WorldGenerator.MapExtent) / (2f * WorldGenerator.MapExtent));
            Color tree = Px(XmasLayout.Tree + new Vector2(5f, 0f));
            Assert.Greater(tree.g, tree.r, "ツリーは緑");
            Color ice = Px(XmasLayout.Pond + new Vector2(4f, 2f));
            Assert.Greater(ice.b, ice.r, "こおった池は水色");
            var look = AreaAtmosphere.For("xmas");
            Assert.Greater(look.sun.b, look.sun.r + 0.1f, "少し青い月あかり（雪が白く見えるように、夜の森ほど青くしない）");
            Assert.Greater(look.sunIntensity, 0.8f, "雪の照り返しで、夜の森より少し明るい");
            Assert.Less(look.fog.maxColorComponent, 0.35f, "こい藍色の霧");
            Assert.IsTrue(AreaAtmosphere.IsNight("xmas"), "星空");
        }
    }
}

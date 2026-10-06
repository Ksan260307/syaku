using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>いきものの組み立て：Blender からの座標変換・脚の動き・鳥の羽の姿勢。</summary>
    public class CreatureRigTests
    {
        [Test]
        public void FromBlender_ConvertsAxes()
        {
            // Blender (x, y, z) → Unity (-x, z, -y)
            var m = CreatureRig.FromBlender(new Vector3(1f, 2f, 3f), Vector3.zero, Vector3.one);
            TestUtil.AssertVector(new Vector3(-1f, 3f, -2f), m.MultiplyPoint3x4(Vector3.zero), 1e-4f);
            // Blender で Z まわりに 90 度（+X → +Y）は、Unity では -X → -Z（前）
            var r = CreatureRig.FromBlender(Vector3.zero, new Vector3(0f, 0f, Mathf.PI / 2f), Vector3.one);
            TestUtil.AssertVector(new Vector3(0f, 0f, -1f), r.MultiplyVector(new Vector3(-1f, 0f, 0f)), 1e-4f);
        }

        [Test]
        public void BirdWings_FoldBehindTheHeadAndSpreadInFlight([Values("sparrow", "crow")] string id)
        {
            var sp = SpeciesCatalog.Get(id);
            var wing = TestUtil.LoadWorldAssets().Get(sp.parts[0].mesh);
            Assert.IsNotNull(wing);
            float L = sp.birdSize;
            var verts = wing.vertices;
            foreach (bool right in new[] { true, false })
            {
                // たたんだ羽：頭（前は -Z）より前に出ない＝くちばしと重ならない。体の後ろ半分にある
                var folded = CreatureRig.BirdWing(L, right, true, 0f);
                var pts = verts.Select(v => folded.MultiplyPoint3x4(v)).ToArray();
                float mostForward = pts.Min(p => p.z);
                float centroidZ = pts.Average(p => p.z);
                Assert.Greater(mostForward, -0.18f * L, $"{id} の羽がくちばしの方へのびている");
                Assert.Greater(centroidZ, 0f, $"{id} の羽は体の後ろ側");
                Assert.Less(pts.Max(p => Mathf.Abs(p.x)), 0.4f * L, "たたんだ羽は横に広がりすぎない");
                // 広げた羽：その側へ大きく広がる（右の羽は Unity の -X 側）
                var spread = CreatureRig.BirdWing(L, right, false, 0f);
                float cx = verts.Select(v => spread.MultiplyPoint3x4(v).x).Average();
                Assert.Greater(Mathf.Abs(cx), 0.15f * L, "広げた羽が横にのびる");
                Assert.AreEqual(right ? -1f : 1f, Mathf.Sign(cx), "羽はその側にある");
            }
        }

        [Test]
        public void Legs_ComeFromTheGeneratedRig()
        {
            foreach (var body in new[] { "Ant", "Ladybug", "Beetle", "Spider", "Mantis", "Crab", "WaterStrider", "Otoshibumi", "Grasshopper" })
            {
                var legs = CreatureRig.Legs(body);
                Assert.Greater(legs.Length, 0, body);
                var assets = TestUtil.LoadWorldAssets();
                foreach (var l in legs)
                {
                    Assert.IsNotNull(assets.TryGet(l.mesh), l.mesh);
                    Assert.Less(l.hip.x, 0f, "右の脚は Unity の -X 側");
                    Assert.Less(l.tip.y, l.hip.y + 1e-3f, "足先は付け根より下");
                }
            }
            Assert.AreEqual(3, CreatureRig.Legs("Ant").Length, "アリは片側 3 本");
            Assert.AreEqual(4, CreatureRig.Legs("Spider").Length, "クモは片側 4 本");
            Assert.AreEqual(0, CreatureRig.Legs("Snail").Length);
            foreach (var l in CreatureRig.Legs("Spider"))
                Assert.IsNotNull(TestUtil.LoadWorldAssets().TryGet(l.mesh + "_Sneaker"), "スニーカーの脚");
        }

        [Test]
        public void Gait_AlternatesAndSwingsForward()
        {
            var legs = CreatureRig.Legs("Ant");
            // 位相 π/2 で、右の前足（グループ 0）は前へ、右の中足（グループ 1）は後ろへ
            var front = CreatureRig.LegMatrix(legs[0], 0, false, Mathf.PI / 2f, 25f, 15f, GaitKind.Alternate, 3);
            var mid = CreatureRig.LegMatrix(legs[1], 1, false, Mathf.PI / 2f, 25f, 15f, GaitKind.Alternate, 3);
            Vector3 restFront = legs[0].tip - legs[0].hip;
            Vector3 restMid = legs[1].tip - legs[1].hip;
            Vector3 f = front.MultiplyVector(restFront);
            Vector3 m = mid.MultiplyVector(restMid);
            Assert.Less(f.z, restFront.z - 0.01f, "前足は前（-Z）へ");
            Assert.Greater(m.z, restMid.z + 0.01f, "中足は後ろへ");
            // 左右は逆の組（左の前足は右の前足と反対）
            var leftFront = CreatureRig.LegMatrix(legs[0], 0, true, Mathf.PI / 2f, 25f, 15f, GaitKind.Alternate, 3);
            Vector3 lf = leftFront.MultiplyVector(restFront);   // 行列が左右を反転させる
            Assert.Greater(lf.z, restFront.z + 0.01f, "左の前足は後ろへ");
            Assert.Less(leftFront.MultiplyPoint3x4(Vector3.zero).x * front.MultiplyPoint3x4(Vector3.zero).x, 0f, "左右に分かれる");
            // 振り上げるとき（cos > 0）は足先が上がる
            var lifted = CreatureRig.LegMatrix(legs[0], 0, false, 0f, 0f, 20f, GaitKind.Alternate, 3);
            Assert.Greater(lifted.MultiplyVector(restFront).y, restFront.y + 0.005f);
            // 歩かない種類は動かない
            var still = CreatureRig.LegMatrix(legs[0], 0, false, 1.2f, 25f, 15f, GaitKind.None, 3);
            TestUtil.AssertVector(restFront, still.MultiplyVector(restFront), 1e-4f);
        }

        [Test]
        public void UprightUp_TiltsOnlyALittle()
        {
            Vector3 steep = new Vector3(0.9f, 0.3f, 0f).normalized;
            Vector3 u = Creatures.UprightUp(steep);
            Assert.Greater(u.y, Mathf.Cos(12.5f * Mathf.Deg2Rad), "鳥やカエルは急な面でもまっすぐ");
            TestUtil.AssertVector(Vector3.up, Creatures.UprightUp(Vector3.up), 1e-5f);
            TestUtil.AssertVector(Vector3.up, Creatures.UprightUp(Vector3.down), 1e-5f);
        }
    }

    /// <summary>いきもの（生成）：レア・乗れる当たり判定・まっすぐ立つ鳥。</summary>
    public class CreatureSpawnTests
    {
        static WorldGenerator _gen;

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            new GameObject("Sun").AddComponent<Light>().type = LightType.Directional;
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Forest;
            _gen.GenerateNow(Areas.Forest);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            Creatures.RareChanceOverride = null;
            if (_gen != null) _gen.Clear();
        }

        Creatures Build(float? rare)
        {
            Creatures.RareChanceOverride = rare;
            var c = new GameObject("Mobs").AddComponent<Creatures>();
            c.assets = _gen.assets;
            c.Build(_gen);
            return c;
        }

        [Test]
        public void Rares_AppearOnlyAsVariantsOfTheirSpecies()
        {
            var none = Build(0f);
            Assert.AreEqual(0, none.RareCount);
            int ants = none.CountOf("ant"), spiders = none.CountOf("spider");
            Assert.Greater(ants, 10);
            Assert.Greater(spiders, 2);
            var all = Build(1f);
            Assert.AreEqual(ants, all.CountOf("ant_helmet"), "どのアリもレアになれる");
            Assert.AreEqual(spiders, all.CountOf("spider_sneaker"));
            Assert.AreEqual(0, all.CountOf("ant"));
            Object.DestroyImmediate(none.gameObject);
            Object.DestroyImmediate(all.gameObject);
        }

        [Test]
        public void RareChance_IsAboutHalfAPercent()
        {
            // 本来の確率で何度も作ると、だいたい 0.5% になる
            Creatures.RareChanceOverride = null;
            int rare = 0, total = 0;
            for (int i = 0; i < 60; i++)
            {
                var c = Build(null);
                rare += c.CountOf("ant_helmet");
                total += c.CountOf("ant") + c.CountOf("ant_helmet");
                Object.DestroyImmediate(c.gameObject);
            }
            float rate = rare / (float)total;
            Assert.Less(rate, 0.02f, $"レアが多すぎる ({rare}/{total})");
        }

        [Test]
        public void RideableCreatures_HaveMovingColliders()
        {
            var c = Build(0f);
            var cols = c.GetComponentsInChildren<MeshCollider>();
            Assert.Greater(cols.Length, 3);
            foreach (var col in cols)
            {
                Assert.AreEqual(ShakuConst.CreatureLayer, col.gameObject.layer);
                Assert.IsNotNull(col.GetComponent<MovingPlatform>(), "乗ると一緒に動く");
            }
            foreach (var id in new[] { "snail", "beetle", "frog", "mantis" })
                Assert.IsTrue(SpeciesCatalog.Get(id).rideable, id);
            Assert.IsTrue((SurfaceProbe.Mask & ShakuConst.CreatureMask) != 0, "しゃくとりむしはいきものの上を這える");
            Assert.IsTrue((ShakuConst.SurfaceMask & ShakuConst.CreatureMask) == 0, "いきもの同士や地形の配置には使わない");
            c.Clear();
            Assert.AreEqual(0, c.GetComponentsInChildren<MeshCollider>().Length, "片づけると当たり判定も消える");
            Object.DestroyImmediate(c.gameObject);
        }

        [Test]
        public void Birds_StandUpright()
        {
            var c = Build(0f);
            for (int i = 0; i < c.CountOf("sparrow"); i++)
                Assert.Greater(c.Info("sparrow", i).up.y, 0.97f, $"スズメ {i} がかたむいている");
            for (int i = 0; i < c.CountOf("crow"); i++)
                Assert.Greater(c.Info("crow", i).up.y, 0.97f);
            for (int i = 0; i < c.CountOf("mantis"); i++)
                Assert.Greater(c.Info("mantis", i).up.y, 0.97f, "カマキリも壁登りはしない");
            Object.DestroyImmediate(c.gameObject);
        }

        [Test]
        public void Flowers_AreRecordedForButterflies()
        {
            Assert.Greater(_gen.FlowerPoints.Count, 30, "チョウやトンボがとまる花");
            foreach (var f in _gen.FlowerPoints)
                Assert.Greater(f.y - ForestLayout.Height(f.x, f.z), 1f, $"花の頭は地面より高い {f}");
        }

        [Test]
        public void NewSpecies_AreSpawned()
        {
            var c = Build(0f);
            Assert.Greater(c.CountOf("spider"), 2);
            Assert.Greater(c.CountOf("mantis"), 1);
            Object.DestroyImmediate(c.gameObject);
        }
    }

    /// <summary>設定と保存：読み込みの順番に関係なく保存した値が使われる・控え・古いデータ。</summary>
    public class SavePersistenceTests
    {
        string _save, _bak, _settings;

        [SetUp]
        public void SetUp()
        {
            _save = PlayerPrefs.GetString("shakutori.save.v1", null);
            _bak = PlayerPrefs.GetString("shakutori.save.v1.bak", null);
            _settings = PlayerPrefs.GetString("shakutori.settings.v1", null);
        }

        [TearDown]
        public void TearDown()
        {
            void Restore(string key, string v)
            {
                if (v != null) PlayerPrefs.SetString(key, v); else PlayerPrefs.DeleteKey(key);
            }
            Restore("shakutori.save.v1", _save);
            Restore("shakutori.save.v1.bak", _bak);
            Restore("shakutori.settings.v1", _settings);
            PlayerPrefs.Save();
            SaveSystem.ResetAllInMemoryForTests();
        }

        [Test]
        public void Settings_AreLoadedOnFirstUse()
        {
            PlayerPrefs.SetString("shakutori.settings.v1", JsonUtility.ToJson(new SettingsData { sensitivity = 1.9f, music = 0.1f, autosave = false, textSize = 1 }));
            SaveSystem.ForgetLoadedForTests();
            // Load() を呼ぶ前にさわっても、保存した値が読まれる（UI が先に初期化されても上書きしない）
            Assert.AreEqual(1.9f, SaveSystem.Settings.sensitivity, 1e-4f);
            Assert.AreEqual(0.1f, SaveSystem.Settings.music, 1e-4f);
            Assert.IsFalse(SaveSystem.Settings.autosave);
            Assert.AreEqual(1, SaveSystem.Settings.textSize);
        }

        [Test]
        public void BrokenSave_FallsBackToTheBackup()
        {
            var good = new SaveData();
            good.drops.Add(7);
            good.hasPosition = true;
            PlayerPrefs.SetString("shakutori.save.v1.bak", JsonUtility.ToJson(good));
            PlayerPrefs.SetString("shakutori.save.v1", "{ this is not json");
            SaveSystem.Load();
            CollectionAssert.Contains(SaveSystem.Data.drops, 7, "控えから読み直す");
            Assert.IsTrue(SaveSystem.HasSave);
        }

        [Test]
        public void Save_KeepsTheEarlierDataAsBackup()
        {
            SaveSystem.ResetAllInMemoryForTests();
            SaveSystem.Data.drops.Add(1);
            SaveSystem.Save();
            SaveSystem.Data.drops.Add(2);
            SaveSystem.Save();
            var bak = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString("shakutori.save.v1.bak"));
            CollectionAssert.AreEqual(new[] { 1 }, bak.drops);
        }

        [Test]
        public void OldSaves_AreMigrated()
        {
            var old = new SaveData { version = 1, area = "", skin = null };
            old.creatures.Add("ant");
            old.visited = null;
            old.seenCreatures = null;
            SaveSystem.Migrate(old);
            Assert.AreEqual(SaveData.CurrentVersion, old.version);
            Assert.AreEqual("forest", old.area);
            Assert.AreEqual("wakaba", old.skin);
            CollectionAssert.Contains(old.visited, "forest");
            CollectionAssert.Contains(old.seenCreatures, "ant", "前から見つけていたものは NEW にしない");
        }

        [Test]
        public void Saved_EventFires()
        {
            int n = 0;
            System.Action h = () => n++;
            SaveSystem.Saved += h;
            SaveSystem.Save();
            SaveSystem.Saved -= h;
            Assert.AreEqual(1, n);
        }

        [Test]
        public void ResetSettings_RestoresDefaults()
        {
            SaveSystem.Settings.sensitivity = 2.2f;
            SaveSystem.Settings.minimapRotate = true;
            SaveSystem.ResetSettings();
            Assert.AreEqual(1f, SaveSystem.Settings.sensitivity);
            Assert.IsFalse(SaveSystem.Settings.minimapRotate);
        }
    }

    public class WindTests
    {
        [Test]
        public void Wind_IsGentleAndHorizontal()
        {
            for (float t = 0f; t < 300f; t += 7.3f)
            {
                Vector3 w = Wind.At(new Vector3(t, 0f, -t), t);
                Assert.AreEqual(0f, w.y, 1e-6f);
                Assert.That(w.magnitude, Is.InRange(0.2f, 1.5f));
                Assert.That(Wind.Gust(t), Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Publish_DrivesTheGrassShader()
        {
            Wind.Publish(12f);
            Vector4 v = Shader.GetGlobalVector("_ShakuWind");
            Assert.Greater(v.z, 0.5f, "草をゆらす強さ");
            Assert.Greater(v.w, 0.8f, "ゆれる速さ");
            Assert.AreEqual(1f, new Vector2(v.x, v.y).magnitude, 1e-3f, "向きは単位ベクトル");
        }

        [Test]
        public void ProgressStats_CountOnlyRegularSpecies()
        {
            SaveSystem.ResetAllInMemoryForTests();
            SaveSystem.Data.creatures.Add("ant");
            SaveSystem.Data.creatures.Add("ant_helmet");
            Assert.AreEqual(1, ProgressStats.FromSave().species, "レアはコンプリートに数えない");
            Assert.AreEqual(1, Creatures.RareDiscoveredCount);
            Assert.AreEqual(SpeciesCatalog.Count, Skins.Get("kogane").needSpecies, "こがねは図鑑コンプリート");
            SaveSystem.ResetAllInMemoryForTests();
        }
    }
}

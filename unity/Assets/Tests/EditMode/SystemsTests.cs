using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Shakutori.Tests
{
    public class InstancedRendererTests
    {
        [SetUp]
        public void SetUp() => TestUtil.NewEmptyScene();

        [Test]
        public void AddBuildClear_TracksInstanceCount()
        {
            var r = new GameObject("ir").AddComponent<InstancedRenderer>();
            var assets = TestUtil.LoadWorldAssets();
            var mesh = assets.Get("Rock_A");
            for (int i = 0; i < 3000; i++)
                r.Add(mesh, assets.prop, Matrix4x4.TRS(new Vector3(i % 60, 0, i / 60), Quaternion.identity, Vector3.one), true, 100f);
            Assert.AreEqual(3000, r.InstanceCount);
            Assert.DoesNotThrow(() => r.Build());
            r.Clear();
            Assert.AreEqual(0, r.InstanceCount);
        }

        [Test]
        public void Add_IgnoresMissingMeshOrMaterial()
        {
            var r = new GameObject("ir").AddComponent<InstancedRenderer>();
            var assets = TestUtil.LoadWorldAssets();
            r.Add(null, assets.prop, Matrix4x4.identity, false, 10f);
            r.Add(assets.Get("Rock_A"), null, Matrix4x4.identity, false, 10f);
            Assert.AreEqual(0, r.InstanceCount);
        }
    }

    public class WorldAssetsTests
    {
        static readonly string[] Required =
        {
            "Inchworm", "Rock_A", "Rock_B", "Rock_C", "Rock_D", "Mushroom_Red", "Mushroom_Brown", "Mushroom_Cluster", "Mushroom_Glow",
            "Stump", "HollowLog", "GreatTree", "BgTrunk_A", "BgTrunk_B", "Twig_A", "Twig_B",
            "Leaf_Oak_Orange", "Leaf_Oak_Brown", "Leaf_Oak_Green", "Leaf_Maple_Red", "Leaf_Maple_Yellow",
            "Acorn", "AcornCap", "Pinecone", "Grass_A", "Grass_B", "Grass_C", "Clover_A", "Clover_B", "Fern_A", "Fern_B",
            "Daisy", "Bellflower", "Dandelion", "DandelionPuff", "Moss", "Sprout", "Strawberry", "LilyPad", "WaterLily", "Reed", "Dewdrop",
        };

        [Test]
        public void AllMeshesUsedByTheGameExist()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var n in Required)
                Assert.IsNotNull(a.Get(n), n);
        }

        [Test]
        public void Get_ReturnsNullForUnknownName()
        {
            var a = TestUtil.LoadWorldAssets();
            LogAssert.Expect(LogType.Warning, "[WorldAssets] mesh not found: NoSuchMesh");
            Assert.IsNull(a.Get("NoSuchMesh"));
        }

        [Test]
        public void AllMaterialsAreAssigned()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var f in typeof(WorldAssets).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(Material) || f.FieldType == typeof(Texture2D))
                    Assert.IsNotNull(f.GetValue(a), f.Name);
            }
        }
    }

    public class SaveSystemTests
    {
        string _saveBackup, _settingsBackup;
        const string SaveKey = "shakutori.save.v1";
        const string SettingsKey = "shakutori.settings.v1";

        [OneTimeSetUp]
        public void Backup()
        {
            _saveBackup = PlayerPrefs.GetString(SaveKey, null);
            _settingsBackup = PlayerPrefs.GetString(SettingsKey, null);
        }

        [OneTimeTearDown]
        public void Restore()
        {
            if (_saveBackup != null) PlayerPrefs.SetString(SaveKey, _saveBackup); else PlayerPrefs.DeleteKey(SaveKey);
            if (_settingsBackup != null) PlayerPrefs.SetString(SettingsKey, _settingsBackup); else PlayerPrefs.DeleteKey(SettingsKey);
            PlayerPrefs.Save();
            SaveSystem.Load();
        }

        [SetUp]
        public void SetUp() => SaveSystem.ResetAll();

        [Test]
        public void SaveAndLoad_RoundTripsProgress()
        {
            var d = SaveSystem.Data;
            d.drops.Add(3);
            d.drops.Add(17);
            d.places.Add(5);
            d.playTime = 123f;
            d.hasPosition = true;
            d.tail = new Vector3(1, 2, 3);
            d.headNormal = Vector3.up;
            SaveSystem.Save();
            SaveSystem.ResetAllInMemoryForTests();
            SaveSystem.Load();
            Assert.IsTrue(SaveSystem.HasSave);
            CollectionAssert.AreEqual(new[] { 3, 17 }, SaveSystem.Data.drops);
            CollectionAssert.AreEqual(new[] { 5 }, SaveSystem.Data.places);
            Assert.AreEqual(123f, SaveSystem.Data.playTime);
            Assert.AreEqual(new Vector3(1, 2, 3), SaveSystem.Data.tail);
        }

        [Test]
        public void Settings_RoundTrip()
        {
            SaveSystem.Settings.sensitivity = 1.7f;
            SaveSystem.Settings.invertY = true;
            SaveSystem.Settings.music = 0.2f;
            SaveSystem.Settings.quality = 0;
            SaveSystem.SaveSettings();
            SaveSystem.ResetAllInMemoryForTests();
            SaveSystem.Load();
            Assert.AreEqual(1.7f, SaveSystem.Settings.sensitivity);
            Assert.IsTrue(SaveSystem.Settings.invertY);
            Assert.AreEqual(0.2f, SaveSystem.Settings.music);
            Assert.AreEqual(0, SaveSystem.Settings.quality);
        }

        [Test]
        public void ResetProgress_KeepsSettingsButClearsProgress()
        {
            SaveSystem.Data.drops.Add(1);
            SaveSystem.Save();
            SaveSystem.Settings.sfx = 0.3f;
            SaveSystem.SaveSettings();
            SaveSystem.ResetProgress();
            SaveSystem.Load();
            Assert.IsFalse(SaveSystem.HasSave);
            Assert.AreEqual(0, SaveSystem.Data.drops.Count);
            Assert.AreEqual(0.3f, SaveSystem.Settings.sfx);
        }

        [Test]
        public void CorruptData_FallsBackToDefaults()
        {
            PlayerPrefs.SetString(SaveKey, "{ this is not json");
            PlayerPrefs.Save();
            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(SaveSystem.Load);
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNotNull(SaveSystem.Data);
            Assert.IsNotNull(SaveSystem.Settings);
        }

        [Test]
        public void Defaults_AreSensible()
        {
            var s = new SettingsData();
            Assert.AreEqual(1f, s.sensitivity);
            Assert.AreEqual(-1, s.quality, "初期値は自動");
            Assert.IsTrue(s.showHelp);
            var d = new SaveData();
            Assert.IsFalse(d.hasPosition);
            Assert.IsFalse(d.completed);
        }
    }

    public class CollectiblesTests
    {
        [SetUp]
        public void SetUp()
        {
            TestUtil.NewEmptyScene();
            SaveSystem.ResetAllInMemoryForTests();
        }

        [Test]
        public void IsInside_RadiusLandmark()
        {
            var lm = new LandmarkDef { id = 99, position = new Vector2(10, 10), radius = 5f };
            Assert.IsTrue(Collectibles.IsInside(lm, new Vector3(12, 50, 10)));
            Assert.IsFalse(Collectibles.IsInside(lm, new Vector3(16, 0, 10)));
        }

        [Test]
        public void IsInside_RequiresHeightForSummits()
        {
            var lm = new LandmarkDef { id = 99, position = new Vector2(0, 0), radius = 5f, minHeightAboveGround = 4f };
            float g = ForestLayout.Height(0, 0);
            Assert.IsFalse(Collectibles.IsInside(lm, new Vector3(0, g + 1f, 0)));
            Assert.IsTrue(Collectibles.IsInside(lm, new Vector3(0, g + 4.5f, 0)));
        }

        [Test]
        public void IsInside_CapsuleLandmark()
        {
            var lm = new LandmarkDef { id = 99, useCapsule = true, capsuleA = new Vector3(0, 0, 0), capsuleB = new Vector3(0, 0, 10), capsuleRadius = 2f };
            Assert.IsTrue(Collectibles.IsInside(lm, new Vector3(1, 0, 5)));
            Assert.IsTrue(Collectibles.IsInside(lm, new Vector3(0, 1.5f, 10.5f)));
            Assert.IsFalse(Collectibles.IsInside(lm, new Vector3(3, 0, 5)));
            Assert.IsFalse(Collectibles.IsInside(lm, new Vector3(0, 0, 13)));
        }

        [Test]
        public void Build_CreatesDropsAndAppliesSave()
        {
            SaveSystem.Data.drops.Add(1);
            var c = new GameObject("c").AddComponent<Collectibles>();
            c.assets = TestUtil.LoadWorldAssets();
            var pts = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(5, 0, 0), new Vector3(10, 0, 0) };
            c.Build(pts);
            Assert.AreEqual(3, c.TotalDrops);
            Assert.AreEqual(1, c.CollectedDrops);
            Assert.IsTrue(c.IsDropTaken(1));
            Assert.IsFalse(c.IsDropTaken(0));
            Assert.AreEqual(2, new List<Vector3>(c.RemainingDrops()).Count);
            Assert.IsTrue(c.NearestRemaining(new Vector3(9, 0, 0), out var near, out var dist));
            Assert.AreEqual(new Vector3(10, 0, 0), near);
            Assert.AreEqual(1f, dist, 1e-4f);
        }

        [Test]
        public void Discover_RaisesEventOnceAndSaves()
        {
            var c = new GameObject("c").AddComponent<Collectibles>();
            c.assets = TestUtil.LoadWorldAssets();
            c.Build(new List<Vector3>());
            int calls = 0;
            c.LandmarkDiscovered += _ => calls++;
            var lm = ForestLayout.Landmarks[3];
            c.Discover(lm);
            c.Discover(lm);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, c.DiscoveredPlaces);
            Assert.IsTrue(c.IsDiscovered(3));
            CollectionAssert.Contains(SaveSystem.Data.places, 3);
        }
    }

    public class GameInputTests
    {
        GameInput _input;
        MethodInfo _update;

        [SetUp]
        public void SetUp()
        {
            TestUtil.NewEmptyScene();
            _input = new GameObject("input").AddComponent<GameInput>();
            _update = typeof(GameInput).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            GameInput.VirtualMove = Vector2.zero;
            GameInput.VirtualSprint = false;
            GameInput.VirtualStand = false;
            GameInput.SetVirtualSilk(false);
            GameInput.LookSensitivity = 1f;
            GameInput.InvertY = false;
            Tick();
        }

        void Tick() => _update.Invoke(_input, null);

        [Test]
        public void VirtualMove_IsClampedToUnitLength()
        {
            GameInput.VirtualMove = new Vector2(3f, 4f);
            Tick();
            Assert.AreEqual(1f, GameInput.Move.magnitude, 1e-4f);
            GameInput.VirtualMove = new Vector2(0.3f, 0f);
            Tick();
            Assert.AreEqual(0.3f, GameInput.Move.x, 1e-4f);
        }

        [Test]
        public void VirtualButtons_MapToActions()
        {
            GameInput.VirtualSprint = true;
            GameInput.VirtualStand = true;
            Tick();
            Assert.IsTrue(GameInput.Sprint);
            Assert.IsTrue(GameInput.StandHeld);
            GameInput.VirtualSprint = false;
            GameInput.VirtualStand = false;
            Tick();
            Assert.IsFalse(GameInput.Sprint);
            Assert.IsFalse(GameInput.StandHeld);
        }

        [Test]
        public void SilkButton_PressIsOneFrameHoldPersists()
        {
            GameInput.SetVirtualSilk(true);
            Tick();
            Assert.IsTrue(GameInput.SilkPressed);
            Assert.IsTrue(GameInput.SilkHeld);
            Tick();
            Assert.IsFalse(GameInput.SilkPressed, "押した瞬間だけ");
            Assert.IsTrue(GameInput.SilkHeld);
            GameInput.SetVirtualSilk(false);
            Tick();
            Assert.IsFalse(GameInput.SilkHeld);
        }

        [Test]
        public void Look_AppliesSensitivityAndInvert()
        {
            GameInput.AddLook(new Vector2(10f, 10f));
            Tick();
            Vector2 baseLook = GameInput.Look;
            Assert.Greater(baseLook.x, 0f);
            Tick();
            Assert.AreEqual(Vector2.zero, GameInput.Look, "1フレームで消費される");
            GameInput.LookSensitivity = 2f;
            GameInput.InvertY = true;
            GameInput.AddLook(new Vector2(10f, 10f));
            Tick();
            Assert.AreEqual(baseLook.x * 2f, GameInput.Look.x, 1e-4f);
            Assert.AreEqual(-baseLook.y * 2f, GameInput.Look.y, 1e-4f);
        }

        [Test]
        public void Zoom_AccumulatesThenResets()
        {
            GameInput.AddZoom(0.5f);
            GameInput.AddZoom(0.25f);
            Tick();
            Assert.AreEqual(0.75f, GameInput.Zoom, 1e-4f);
            Tick();
            Assert.AreEqual(0f, GameInput.Zoom, 1e-4f);
        }
    }
}

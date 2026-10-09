using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>改善項目のうち、シーンを使わずに確かめられるもの。</summary>
    public class ImprovementTests
    {
        [SetUp]
        public void SetUp() => SaveSystem.ResetAllInMemoryForTests();

        [TearDown]
        public void TearDown() => SaveSystem.ForgetLoadedForTests();

        [Test]
        public void SilkButton_SaysWhatItDoesNow()
        {
            // 平らな所では使えない（うすく）。がけのふちでは「糸で ぶら下がる」（目立たせる）
            Assert.AreEqual(("がけのふちで", "糸", 0), GameUI.SilkButton(false, false, false, false, false, false));
            Assert.AreEqual(("糸で", "ぶら下がる", 2), GameUI.SilkButton(false, false, false, false, true, false));
            Assert.AreEqual(("長押しで", "のぼる", 1), GameUI.SilkButton(false, true, false, false, false, false), "ぶら下がり中");
            Assert.AreEqual(("糸を", "はなす", 1), GameUI.SilkButton(true, true, false, false, false, false), "ねらった糸をたぐっている途中");
            Assert.AreEqual(("糸で", "つかまる", 2), GameUI.SilkButton(false, false, true, true, false, false), "落ちている途中で、糸がとどく");
            Assert.AreEqual(0, GameUI.SilkButton(false, false, true, false, false, false).state, "糸がとどかないほど落ちたら、使えない");
            Assert.AreEqual(("壁から", "はなれる", 1), GameUI.SilkButton(false, false, false, false, false, true));
            Assert.AreEqual("ぶら下がる", GameUI.SilkButton(false, false, false, false, true, true).main, "がけのふちでは、ぶら下がるが先");
        }

        [Test]
        public void Joystick_HasADeadZoneAndReachesFullSpeed()
        {
            Assert.AreEqual(Vector2.zero, GameUI.JoystickValue(new Vector2(0.08f, 0.05f)), "中心付近のぶれでは動かない");
            Assert.AreEqual(1f, GameUI.JoystickValue(Vector2.up).magnitude, 1e-4f, "いっぱいに倒すと最大");
            float prev = 0f;
            for (float m = 0.13f; m <= 1f; m += 0.05f)
            {
                float v = GameUI.JoystickValue(new Vector2(m, 0f)).x;
                Assert.Greater(v, prev - 1e-5f, "倒すほど速くなる");
                prev = v;
            }
            Vector2 diag = GameUI.JoystickValue(new Vector2(0.5f, 0.5f));
            Assert.AreEqual(diag.x, diag.y, 1e-5f, "向きは変わらない");
        }

        [Test]
        public void BlockedMessage_ExplainsWhy()
        {
            StringAssert.Contains("水", GameUI.BlockedMessage("water", false, "Space"));
            StringAssert.Contains("Space", GameUI.BlockedMessage("cliff", true, "Space"), "糸でおりられるときはボタンを教える");
            StringAssert.DoesNotContain("Space", GameUI.BlockedMessage("cliff", false, "Space"));
            StringAssert.Contains("がけ", GameUI.BlockedMessage("nothing-ahead", false, "Space"));
            StringAssert.Contains("つっかえ", GameUI.BlockedMessage("overhang", false, "Space"));
            Assert.IsNotEmpty(GameUI.BlockedMessage("", false, "Space"));
            Assert.IsNotEmpty(GameUI.BlockedMessage(null, false, "Space"));
        }

        [Test]
        public void PlayTimeText_UsesMinutesAndHours()
        {
            Assert.AreEqual("0分", GameUI.PlayTimeText(30f));
            Assert.AreEqual("12分", GameUI.PlayTimeText(12 * 60 + 5));
            Assert.AreEqual("1時間 5分", GameUI.PlayTimeText(3600 + 5 * 60));
            Assert.AreEqual("0分", GameUI.PlayTimeText(-5f));
        }

        [Test]
        public void CompletionPercent_CountsDropsPlacesAndCreatures()
        {
            Assert.AreEqual(0, GameUI.CompletionPercent());
            var d = SaveSystem.Data;
            for (int i = 0; i < Areas.TotalDrops; i++) d.drops.Add(i);
            int total = Areas.TotalDrops + Areas.TotalLandmarks + SpeciesCatalog.Count;
            Assert.AreEqual(100 * Areas.TotalDrops / total, GameUI.CompletionPercent());
            foreach (var sp in SpeciesCatalog.Regular) d.creatures.Add(sp.id);
            for (int i = 0; i < Areas.TotalLandmarks; i++) d.places.Add(i < Areas.Forest.Landmarks.Count ? i : 1000 + i);
            Assert.LessOrEqual(GameUI.CompletionPercent(), 100);
        }

        [Test]
        public void Tips_AreShortAndDistinct()
        {
            Assert.GreaterOrEqual(GameUI.Tips.Length, 8);
            Assert.AreEqual(GameUI.Tips.Length, GameUI.Tips.Distinct().Count());
            foreach (var t in GameUI.Tips) Assert.Less(t.Length, 60, t);
            StringAssert.Contains("Noto Sans JP", GameUI.CreditsText, "フォントのライセンス表記");
        }

        [Test]
        public void FootstepSurface_FromObjectName()
        {
            Assert.AreEqual(AudioManager.Surface.Stone, AudioManager.Classify("RiverStone_A", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Stone, AudioManager.Classify("Rock_C", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Wood, AudioManager.Classify("Stump", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Wood, AudioManager.Classify("FallenLog", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Wood, AudioManager.Classify("RootArch", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Leaf, AudioManager.Classify("Leaf_Oak_Green", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Leaf, AudioManager.Classify("Mushroom_Red", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Ground, AudioManager.Classify("Terrain_3_4", ShakuConst.SurfaceLayer));
            Assert.AreEqual(AudioManager.Surface.Ground, AudioManager.Classify(null, 0));
            Assert.AreEqual(AudioManager.Surface.Creature, AudioManager.Classify("Stump", ShakuConst.CreatureLayer), "いきものの上はいきもの");
        }

        [Test]
        public void GateHint_ShowsDestinationProgress()
        {
            var d = SaveSystem.Data;
            StringAssert.Contains("まだ行ったことがない", GameManager.GateHintText(Areas.River));
            StringAssert.Contains("せせらぎの小川", GameManager.GateHintText(Areas.River));
            d.visited.Add("river");
            StringAssert.Contains($"0 / {Areas.River.DropCount}", GameManager.GateHintText(Areas.River));
            d.completedAreas.Add("river");
            StringAssert.Contains("めぐり終えた", GameManager.GateHintText(Areas.River));
        }

        [Test]
        public void SettingsSaveSoon_WaitsThenSaves()
        {
            const string key = "shakutori.settings.v1";
            string backup = PlayerPrefs.GetString(key, null);
            try
            {
                PlayerPrefs.DeleteKey(key);
                SaveSystem.Settings.sensitivity = 1.7f;
                SaveSystem.SaveSettingsSoon();
                Assert.IsTrue(SaveSystem.SettingsDirty);
                SaveSystem.FlushSettings();
                Assert.IsFalse(PlayerPrefs.HasKey(key), "すぐには書きこまない（まとめて保存）");
                SaveSystem.FlushSettings(true);
                Assert.IsFalse(SaveSystem.SettingsDirty);
                StringAssert.Contains("1.7", PlayerPrefs.GetString(key));
                SaveSystem.FlushSettings(true);   // 変更がなければ何もしない
                Assert.IsFalse(SaveSystem.SettingsDirty);
            }
            finally
            {
                if (backup != null) PlayerPrefs.SetString(key, backup); else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void SaveData_KeepsTipsAndCameraDistance()
        {
            var d = new SaveData();
            d.tipsShown.Add("wall");
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(d));
            CollectionAssert.Contains(back.tipsShown, "wall");
            var s = new SettingsData { cameraDistance = 4.5f, sprintToggle = true, aimToggle = true, showHelp = false };
            var sb = JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(s));
            Assert.AreEqual(4.5f, sb.cameraDistance);
            Assert.IsTrue(sb.sprintToggle);
            Assert.IsTrue(sb.aimToggle);
            Assert.IsFalse(sb.showHelp);
        }
    }
}

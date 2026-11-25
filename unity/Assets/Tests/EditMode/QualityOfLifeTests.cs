using NUnit.Framework;

namespace Shakutori.Tests
{
    /// <summary>遊びやすさの改善（ゲームパッドのボタンの名前・遊んだ日・きせかえの、いまの数）。</summary>
    public class QualityOfLifeTests
    {
        [Test]
        public void PadLabels_FollowTheConnectedController()
        {
            Assert.AreEqual("xbox", GameInput.StyleOf("XInputControllerWindows", "Xbox Controller"));
            Assert.AreEqual("ps", GameInput.StyleOf("DualShock4GamepadHID", "Wireless Controller"));
            Assert.AreEqual("ps", GameInput.StyleOf("WebGLGamepad", "DualSense Wireless Controller"));
            Assert.AreEqual("switch", GameInput.StyleOf("SwitchProControllerHID", "Pro Controller"));
            Assert.AreEqual("A", GameInput.PadLabel("A", "xbox"));
            Assert.AreEqual("×", GameInput.PadLabel("A", "ps"));
            Assert.AreEqual("△（長押し）", GameInput.PadLabel("Y（長押し）", "ps"));
            Assert.AreEqual("L1 / R1", GameInput.PadLabel("LB / RB", "ps"));
            Assert.AreEqual("B", GameInput.PadLabel("A", "switch"), "Switch は、下のボタンが B");
            Assert.AreEqual("A", GameInput.PadLabel("B", "switch"));
            Assert.AreEqual("ZR", GameInput.PadLabel("RT", "switch"));
            Assert.AreEqual("L スティック", GameInput.PadLabel("L スティック", "ps"), "スティックの名前はそのまま");
            Assert.AreEqual("OPTIONS", GameInput.PadLabel("Menu", "ps"));
        }

        [Test]
        public void PlayDays_CountEachDayOnce()
        {
            var d = new SaveData();
            Assert.IsTrue(d.MarkPlayDay(new System.DateTime(2026, 11, 1, 9, 0, 0)));
            Assert.AreEqual("2026-11-01", d.firstDay);
            Assert.IsFalse(d.MarkPlayDay(new System.DateTime(2026, 11, 1, 21, 0, 0)));
            Assert.IsTrue(d.MarkPlayDay(new System.DateTime(2026, 11, 5)));
            Assert.AreEqual(2, d.playDays);
            Assert.AreEqual("2026-11-01", d.firstDay, "はじめて遊んだ日は、かわらない");
        }

        [Test]
        public void WelcomeBack_AfterThreeDaysOrMore()
        {
            var now = new System.DateTime(2026, 11, 10, 12, 0, 0);
            Assert.IsNull(GameManager.WelcomeBack("", now, Areas.Forest), "はじめて");
            Assert.IsNull(GameManager.WelcomeBack("2026-11-09", now, Areas.Forest), "きのうも遊んだ");
            StringAssert.Contains("5 日ぶり", GameManager.WelcomeBack("2026-11-05", now, Areas.Forest));
        }

        [Test]
        public void NextSkin_IsShownInTheMenu()
        {
            SaveSystem.ResetAllInMemoryForTests();
            StringAssert.StartsWith("\nつぎのきせかえ：", GameUI.NextSkinText());
        }

        [Test]
        public void SaveData_IsCleanedOnLoad()
        {
            var d = new SaveData();
            d.drops.AddRange(new[] { 3, 3, 4 });
            d.creatures.AddRange(new[] { "ant", "ant" });
            d.steps = -5;
            d.area = "nowhere";
            d.hasPosition = true;
            d.tail = new UnityEngine.Vector3(float.NaN, 0f, 0f);
            d.playTime = float.PositiveInfinity;
            SaveSystem.SanitizeData(d);
            CollectionAssert.AreEqual(new[] { 3, 4 }, d.drops);
            CollectionAssert.AreEqual(new[] { "ant" }, d.creatures);
            Assert.AreEqual(0, d.steps);
            Assert.AreEqual("forest", d.area);
            Assert.IsFalse(d.hasPosition, "こわれた場所はわすれる");
            Assert.AreEqual(0f, d.playTime);
        }

        [Test]
        public void SaveFile_ExportsAndImports_AndRejectsOtherFiles()
        {
            SaveSystem.ResetAllInMemoryForTests();
            SaveSystem.Data.drops.Add(1001);
            SaveSystem.Data.swims = 7;
            string text = SaveSystem.Export();
            StringAssert.Contains("shakutori-save", text);
            SaveSystem.ResetAllInMemoryForTests();
            Assert.IsFalse(SaveSystem.Import("{\"hello\":1}", out var err), "ほかのファイル");
            Assert.IsNotNull(err);
            Assert.IsFalse(SaveSystem.Import("not json", out _));
            Assert.AreEqual(0, SaveSystem.Data.swims, "読みこめないときは、いまの記録のまま");
            Assert.IsTrue(SaveSystem.Import(text, out err), err);
            Assert.AreEqual(7, SaveSystem.Data.swims);
            CollectionAssert.Contains(SaveSystem.Data.drops, 1001);
            SaveSystem.ResetAllInMemoryForTests();
        }

        [Test]
        public void SkinProgress_ShowsTheFirstMissingCount()
        {
            var s = new SkinDef { needDrops = 30, needSpecies = 5 };
            Assert.AreEqual("（いま 12 / 30）", GameUI.SkinProgress(s, new ProgressStats { drops = 12, species = 1 }));
            Assert.AreEqual("（いま 1 / 5）", GameUI.SkinProgress(s, new ProgressStats { drops = 40, species = 1 }));
            Assert.AreEqual("", GameUI.SkinProgress(s, new ProgressStats { drops = 40, species = 9 }));
        }
    }
}

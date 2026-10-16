using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// タイトルのひみつの操作（ファストトラベルを、ぜんぶ使えるようにする）と、タイトルのデモ映像のえらび方。
    /// </summary>
    public class TitleSecretTests
    {
        string _saveBackup;

        [SetUp]
        public void SetUp()
        {
            _saveBackup = PlayerPrefs.GetString("shakutori.save.v1", null);
            SaveSystem.ResetAllInMemoryForTests();
            SaveSystem.ResetProgress();
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_saveBackup)) PlayerPrefs.SetString("shakutori.save.v1", _saveBackup);
            else PlayerPrefs.DeleteKey("shakutori.save.v1");
            PlayerPrefs.Save();
            SaveSystem.ResetAllInMemoryForTests();
        }

        [Test]
        public void SevenQuickTaps_Unlock_SlowTapsDoNot()
        {
            var s = new TitleSecret();
            int n = 0;
            s.Unlocked += () => n++;
            for (int i = 0; i < 6; i++) s.Tap(i * 0.3f);
            Assert.AreEqual(0, n, "6 回では、まだ");
            s.Tap(1.8f);
            Assert.AreEqual(1, n, "3 秒のうちに 7 回");
            var slow = new TitleSecret();
            int m = 0;
            slow.Unlocked += () => m++;
            for (int i = 0; i < 14; i++) slow.Tap(i * 0.8f);
            Assert.AreEqual(0, m, "ゆっくりたたいても、ひらかない");
        }

        [Test]
        public void ArrowCode_Unlocks_AndAMistakeStartsOver()
        {
            var s = new TitleSecret();
            int n = 0;
            s.Unlocked += () => n++;
            Vector2Int u = Vector2Int.up, d = Vector2Int.down, l = Vector2Int.left, r = Vector2Int.right;
            foreach (var k in new[] { u, u, d, l }) s.Direction(k);   // まちがえた
            foreach (var k in new[] { u, u, d, d, l, r, l }) s.Direction(k);
            Assert.AreEqual(0, n);
            s.Direction(r);
            Assert.AreEqual(1, n, "↑↑↓↓←→←→");
        }

        [Test]
        public void UnlockAll_OpensEveryAreaAndLandmark_ForTravel()
        {
            int opened = TitleSecret.UnlockAll();
            Assert.Greater(opened, 0);
            var d = SaveSystem.Data;
            foreach (var a in Areas.All)
            {
                CollectionAssert.Contains(d.visited, a.Id, $"{a.Id} に、地図から行ける");
                foreach (var lm in a.Landmarks) CollectionAssert.Contains(d.places, lm.id, $"{a.Id} の {lm.name}");
            }
            Assert.AreEqual(0, TitleSecret.UnlockAll(), "2 回目は、もう何もない");
        }

        [Test]
        public void DemoPick_UsesOnlyVisitedAreas_AndDoesNotRepeat()
        {
            var rng = new System.Random(3);
            var choices = new System.Collections.Generic.List<string> { "forest", "park" };
            string last = null;
            for (int i = 0; i < 20; i++)
            {
                string id = TitleDemo.Pick(choices, last, rng);
                CollectionAssert.Contains(choices, id);
                Assert.AreNotEqual(last, id, "同じ映像が、つづけて流れない");
                last = id;
            }
            Assert.AreEqual("forest", TitleDemo.Pick(new System.Collections.Generic.List<string> { "forest" }, "forest", rng), "1 本だけなら、くりかえす");
            Assert.IsNull(TitleDemo.Pick(new System.Collections.Generic.List<string>(), null, rng));
        }

        [Test]
        public void OldSave_DewsAreCollectedAgain_ButDressUpStays()
        {
            var old = JsonUtility.FromJson<SaveData>("{\"version\":2,\"drops\":[1,2,3,1001],\"completedAreas\":[\"forest\"]}");
            SaveSystem.Migrate(old);
            Assert.AreEqual(SaveData.CurrentVersion, old.version);
            Assert.AreEqual(0, old.drops.Count, "しずくの場所が新しくなったので、集めなおす");
            Assert.AreEqual(4, old.legacyDrops, "前に集めた数は、おぼえておく");
            Assert.AreEqual(0, old.completedAreas.Count, "めぐり終えたエリアも、もう一度");
            Assert.IsTrue(SaveSystem.DewsMoved, "つづきから始めたときに、知らせる");
            SaveSystem.DewsMoved = false;
            // 新しいセーブは、そのまま
            var now = new SaveData();
            now.drops.Add(5);
            SaveSystem.Migrate(now);
            Assert.AreEqual(1, now.drops.Count);
            Assert.IsFalse(SaveSystem.DewsMoved);
        }
    }
}

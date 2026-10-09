using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>もう一度会う：なかよし・しぐさの観察・大きさの記録・図鑑の文・きせかえ。</summary>
    public class FriendsTests
    {
        SaveData _saved;

        [SetUp]
        public void SetUp()
        {
            _saved = SaveSystem.Data;
            SaveSystem.ResetAllInMemoryForTests();
        }

        [TearDown]
        public void TearDown() => SaveSystem.ResetAllInMemoryForTests();

        [Test]
        public void EverySpecies_HasThreeBehaviors_Likes_AndASecret()
        {
            foreach (var sp in SpeciesCatalog.All)
            {
                var bs = Friends.BehaviorsOf(sp.id);
                Assert.AreEqual(3, bs.Length, sp.id + "：しぐさは 3 つ");
                Assert.AreEqual(3, bs.Select(b => b.key).Distinct().Count(), sp.id + "：しぐさはちがうもの");
                foreach (var b in bs)
                {
                    Assert.IsTrue(Creatures.BehaviorKeys.Contains(b.key), $"{sp.id}：しぐさ「{b.key}」をいきものが調べられる");
                    Assert.IsFalse(string.IsNullOrEmpty(b.label));
                }
                Assert.IsFalse(string.IsNullOrEmpty(Friends.Likes(sp.id)), sp.id + "：すきなもの");
                Assert.IsFalse(string.IsNullOrEmpty(Friends.Secret(sp.id)), sp.id + "：ひみつ");
            }
            CollectionAssert.IsSubsetOf(Friends.BookIds.ToList(), SpeciesCatalog.All.Select(s => s.id).ToList(), "図鑑にいない種の文はない");
        }

        [Test]
        public void Friendship_RisesOncePerCooldown_UpToFive()
        {
            Assert.AreEqual(0, Friends.Level("ant"));
            Assert.IsTrue(Friends.Befriend("ant", 100f));
            Assert.IsFalse(Friends.Befriend("ant", 100f + Friends.FriendCooldown - 1f), "すぐには上がらない");
            Assert.IsTrue(Friends.Befriend("ant", 100f + Friends.FriendCooldown));
            Assert.AreEqual(2, Friends.Level("ant"));
            float t = 1000f;
            for (int i = 0; i < 10; i++, t += Friends.FriendCooldown) Friends.Befriend("ant", t);
            Assert.AreEqual(Friends.MaxFriend, Friends.Level("ant"), "★5 まで");
            Assert.AreEqual(1, Friends.BestFriendCount);
            Assert.AreEqual("★★★☆☆", Friends.Stars(3));
        }

        [Test]
        public void Observations_CountOncePerBehavior()
        {
            var bs = Friends.BehaviorsOf("pillbug");
            Assert.IsTrue(Friends.Observe("pillbug", bs[0].key));
            Assert.IsFalse(Friends.Observe("pillbug", bs[0].key), "同じしぐさは 1 回");
            Assert.AreEqual(1, Friends.SeenCount("pillbug"));
            Friends.Observe("pillbug", bs[1].key);
            Friends.Observe("pillbug", bs[2].key);
            Assert.IsTrue(Friends.FullyObserved("pillbug"));
            Assert.AreEqual(3, Friends.TotalObserved);
        }

        [Test]
        public void SizeRecords_OnlyCountWhenBeaten()
        {
            Assert.AreEqual(0, Friends.RecordSize("ant", 8f), "はじめての記録は、更新ではない");
            Assert.AreEqual(0, Friends.RecordSize("ant", 8.02f), "ほとんど同じなら、更新しない");
            Assert.AreEqual(1, Friends.RecordSize("ant", 9f), "いちばん大きい");
            Assert.AreEqual(-1, Friends.RecordSize("ant", 7f), "いちばん小さい");
            var n = Friends.Find("ant");
            Assert.AreEqual(9f, n.bigMm, 1e-4f);
            Assert.AreEqual(7f, n.smallMm, 1e-4f);
            Assert.AreEqual("9 mm", Friends.FormatSize(9f));
            Assert.AreEqual("48 cm", Friends.FormatSize(480f));
        }

        [Test]
        public void Notes_SurviveSaving_AndBadDataIsCleaned()
        {
            Friends.Befriend("frog", 50f);
            Friends.Observe("frog", "hop");
            Friends.RecordSize("frog", 30f);
            string json = JsonUtility.ToJson(SaveSystem.Data);
            var back = JsonUtility.FromJson<SaveData>(json);
            SaveSystem.Migrate(back);
            var n = back.notes.Single(x => x.id == "frog");
            Assert.AreEqual(1, n.friend);
            CollectionAssert.Contains(n.seen, "hop");
            Assert.AreEqual(30f, n.bigMm, 1e-4f);
            // 古いセーブ（notes がない）・こわれた記録
            var old = JsonUtility.FromJson<SaveData>("{\"version\":2,\"drops\":[1]}");
            old.notes = null;
            SaveSystem.Migrate(old);
            Assert.IsNotNull(old.notes);
            old.notes.Add(new CreatureNote { id = "", friend = 3 });
            old.notes.Add(new CreatureNote { id = "ant", friend = 99, seen = null });
            SaveSystem.Migrate(old);
            Assert.AreEqual(1, old.notes.Count);
            Assert.AreEqual(Friends.MaxFriend, old.notes[0].friend);
            Assert.IsNotNull(old.notes[0].seen);
        }

        [Test]
        public void NewSkins_UnlockByObservingAndBefriending()
        {
            var ruri = Skins.Get("ruri");
            var fuji = Skins.Get("fuji");
            Assert.AreEqual("ruri", ruri.id);
            Assert.AreEqual("fuji", fuji.id);
            var st = new ProgressStats { observed = 29 };
            Assert.IsFalse(Skins.IsUnlocked(ruri, st));
            st.observed = 30;
            Assert.IsTrue(Skins.IsUnlocked(ruri, st));
            st.bestFriends = 5;
            Assert.IsTrue(Skins.IsUnlocked(fuji, st));
            Assert.IsFalse(st.IsComplete, "なかよしや観察は、ぜんぶ集めたかには入らない");
        }

        [Test]
        public void SizeFactor_IsMostlyNormal_WithSomeBigOnes()
        {
            var r = new System.Random(7);
            int big = 0, small = 0, normal = 0;
            for (int i = 0; i < 2000; i++)
            {
                float k = Creatures.SizeFactor(r, false);
                Assert.That(k, Is.InRange(0.79f, 1.29f));
                if (k > 1.14f) big++;
                else if (k < 0.87f) small++;
                else normal++;
                float ride = Creatures.SizeFactor(r, true);
                Assert.That(ride, Is.InRange(0.9f, 1.15f), "乗れるいきものは、ひかえめ");
            }
            Assert.Greater(normal, 1500, "ほとんどは、ふつうの大きさ");
            Assert.Greater(big, 60, "ときどき大物");
            Assert.Greater(small, 30, "ときどき小さいもの");
        }
    }
}

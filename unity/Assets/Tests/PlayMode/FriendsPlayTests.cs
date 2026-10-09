using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：見つけたいきものに、もう一度会う（あいさつ・なかよし・しぐさの観察・大きさ・あんない・図鑑）。</summary>
    public class FriendsPlayTests
    {
        Creatures C => GM.creatures;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown()
        {
            ResetInput();
            if (UI != null && UI.IsCollectionOpen) UI.ShowCollection(false, "zukan");
        }

        /// <summary>見つけたことにして、なかよしの記録をつくる（はじめて会ったすぐあとではない）。</summary>
        static void Known(string id, int friend = 0)
        {
            if (!SaveSystem.Data.creatures.Contains(id)) SaveSystem.Data.creatures.Add(id);
            var n = Friends.Note(id);
            n.friend = friend;
            n.friendAt = -999f;
        }

        /// <summary>いきもののそば（dx だけはなれた所）に置いて、そちらを向く。</summary>
        IEnumerator NextTo(string id, float dx)
        {
            Vector3 p = C.PositionOf(id);
            Assert.AreNotEqual(Vector3.zero, p, id + " がいる");
            Vector3 at = SurfaceNear(p, dx);
            Vector3 face = (p - at);
            face.y = 0f;
            Worm.Spawn(at, face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward);
            yield return FaceCamera(face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward);
        }

        [UnityTest]
        public IEnumerator Greeting_RaisesFriendship_OncePerCooldown()
        {
            Known("snail");
            int greets = 0, ups = 0;
            C.Greeted += (sp, pos, lv, up) => { if (sp.id == "snail") { greets++; if (up) ups++; } };
            C.ResetGreetForTests("snail");
            yield return NextTo("snail", 1.6f);
            yield return WaitUntil(() => greets > 0, 4f, "見つけたかたつむりが、あいさつしてくれる");
            Assert.AreEqual(1, Friends.Level("snail"), "なかよし ★1");
            Assert.AreEqual(1, ups);
            // すぐにもう一度あいさつしても、なかよしは上がらない
            C.ResetGreetForTests("snail");
            yield return WaitUntil(() => greets > 1, 4f, "もう一度あいさつ");
            Assert.AreEqual(1, Friends.Level("snail"), "同じ種は、少し時間をあけないと上がらない");
            // 時間がたつと、また上がる
            Friends.Note("snail").friendAt -= Friends.FriendCooldown + 1f;
            C.ResetGreetForTests("snail");
            yield return WaitUntil(() => ups > 1, 4f, "時間をあけて、また会う");
            Assert.AreEqual(2, Friends.Level("snail"));
        }

        [UnityTest]
        public IEnumerator Observing_ABehavior_StampsTheZukan()
        {
            Known("pillbug");
            Behavior seen = null;
            C.Observed += (sp, b, pos) => { if (sp.id == "pillbug") seen = b; };
            yield return NextTo("pillbug", 3f);
            yield return Seconds(0.3f);
            Assert.IsTrue(C.ForceBehaviorForTests("pillbug", "curl"));
            yield return WaitUntil(() => Friends.HasSeen("pillbug", "curl"), 3f, "まるくなるのを観察");
            Assert.IsNotNull(seen);
            Assert.AreEqual("curl", seen.key);
            // 図鑑：なかよし・しぐさのスタンプ
            UI.ShowCollection(true, "zukan");
            yield return null;
            UI.SelectSpecies("pillbug");
            yield return null;
            string text = UI.ZukanFriendText;
            StringAssert.Contains("なかよし", text);
            StringAssert.Contains("まるくなる", text, "観察したしぐさの名前");
            StringAssert.Contains("？？？", text, "まだ見ていないしぐさは、かくれている");
            if (Friends.Level("pillbug") >= Friends.LikesAt) StringAssert.Contains("すきなもの：" + Friends.Likes("pillbug"), text, "なかよしになると、すきなものがわかる");
            else StringAssert.Contains("すきなもの：？", text, "なかよしになるまで、すきなものはわからない");
            StringAssert.Contains("ひみつ：？", text, "ひみつは、なかよし ★3 から");
        }

        [UnityTest]
        public IEnumerator SizeIsMeasured_WhenMeetingAgain()
        {
            Known("snail", 3);
            float measured = 0f;
            C.Measured += (sp, mm, pos) => { if (sp.id == "snail") measured = mm; };
            yield return NextTo("snail", 1.5f);
            yield return WaitUntil(() => measured > 0f, 3f, "近くで会うと、大きさをはかる");
            var n = Friends.Find("snail");
            Assert.Greater(n.bigMm, 0f, "大きさの記録");
            Assert.That(measured, Is.InRange(10f, 200f), "かたつむりは、数センチ");
            UI.ShowCollection(true, "zukan");
            yield return null;
            UI.SelectSpecies("snail");
            yield return null;
            string text = UI.ZukanFriendText;
            StringAssert.Contains("大きさ", text);
            StringAssert.Contains("ひみつ：" + Friends.Secret("snail"), text, "なかよし ★3 で、ひみつがわかる");
        }

        [UnityTest]
        public IEnumerator FriendlyButterfly_GuidesTowardADewdrop()
        {
            Known("butterfly", Friends.GuideAt);
            int trail = 0;
            Vector3 guideTo = Vector3.zero;
            C.GuideTrail += p => trail++;
            C.GuideStarted += (sp, from, to) => guideTo = to;
            C.ResetGreetForTests("butterfly");
            // 飛んでいるチョウのそばへ（何度か置きなおす）
            float t = 0f;
            while (guideTo == Vector3.zero && t < 12f)
            {
                yield return NextTo("butterfly", 1.2f);
                yield return Seconds(0.6f);
                t += 0.6f;
            }
            Assert.AreNotEqual(Vector3.zero, guideTo, "なかよしのチョウが、あんないをはじめる");
            Assert.IsTrue(GM.collectibles.NearestDrop(Worm.HeadPosition, 50f, out var drop), "まだ取っていないしずくがある");
            Assert.Less(Vector3.Distance(guideTo, drop), 30f, "しずくのある方へ");
            // しばらくすると、チョウはしゃくとりむしより、しずくの方にいる
            yield return Seconds(4f);
            Assert.IsTrue(C.IsGuiding("butterfly", out var pos, out var to), "あんないしている");
            Vector3 toTarget = to - Worm.HeadPosition;
            toTarget.y = 0f;
            Vector3 toFly = pos - Worm.HeadPosition;
            toFly.y = 0f;
            Assert.Greater(Vector3.Dot(toFly.normalized, toTarget.normalized), 0.3f, "しずくの方で待っている");
            Assert.Less(toFly.magnitude, 8f, "しゃくとりむしの少し先");
            Assert.Greater(trail, 5, "光のあとを残す");
        }

        [UnityTest]
        public IEnumerator BestFriends_ShowTheirSignature()
        {
            Known("frog", Friends.MaxFriend);
            yield return null;
            // ★5：あいさつすると、とっておきのしぐさ（跳ぶかまえ）を見せてくれる
            C.ResetGreetForTests("frog");
            yield return NextTo("frog", 2.2f);
            yield return WaitUntil(() => Friends.HasSeen("frog", "special"), 5f, "★5 のカエルが、とっておきのしぐさを見せてくれる");
        }
    }
}

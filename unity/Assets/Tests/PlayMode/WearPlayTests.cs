using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：きせかえのぼうし・めがねを、しゃくとりむしの頭にのせる（いろといっしょに）。</summary>
    public class WearPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator HatsAndGlasses_SitOnTheHeadAndFollowIt()
        {
            var wear = GM.Wear;
            Assert.IsNotNull(wear);
            Assert.IsNull(wear.Hat, "はじめは、何もかぶっていない");
            Assert.IsFalse(UI.SelectSkin("cap"), "まだしずくを集めていないので、キャップはかぶれない");
            for (int i = 0; i < 10; i++) SaveSystem.Data.drops.Add(i);
            for (int i = 0; i < 3; i++) SaveSystem.Data.places.Add(i);
            Assert.IsTrue(UI.SelectSkin("cap"));
            Assert.IsTrue(UI.SelectSkin("round"));
            Assert.IsTrue(UI.SelectSkin("kimidori"));
            yield return Frames(3);
            Assert.AreEqual("cap", wear.Hat?.id);
            Assert.AreEqual("round", wear.Glasses?.id);
            Assert.AreEqual("kimidori", SaveSystem.Data.skin, "いろも、いっしょに");
            Assert.IsTrue(wear.HatRenderer.enabled && wear.GlassesRenderer.enabled);
            // 頭のまん中にのって、頭と同じ向き
            var body = Worm.body;
            Assert.Less(Vector3.Distance(wear.HatTransform.position, body.HeadCenter), 0.01f);
            Assert.Greater(Vector3.Dot(wear.HatTransform.up, body.HeadUp), 0.95f, "ぼうしの上は、せなかの方");
            Assert.Greater(Vector3.Dot(-wear.HatTransform.forward, body.HeadForward), 0.95f, "ぼうしの前は、頭の前");
            Vector3 hatTop = wear.HatRenderer.bounds.max;
            Assert.Greater(hatTop.y, body.HeadCenter.y + 0.04f, "頭の上に見える");
            // 歩いても、頭についてくる
            GameInput.VirtualMove = Vector2.up;
            yield return Seconds(2f);
            ResetInput();
            // 小物は LateUpdate で頭に合わせるので、止まってからくらべる（テストは Update の後に目をさます）
            yield return Seconds(1f);
            Assert.Less(Vector3.Distance(wear.HatTransform.position, body.HeadCenter), 0.01f, "歩いても頭の上");
            Assert.Less(Vector3.Distance(wear.GlassesTransform.position, body.HeadCenter), 0.01f);
            // もう一度えらぶと、外す
            Assert.IsTrue(UI.SelectSkin("cap"));
            yield return Frames(2);
            Assert.IsNull(wear.Hat, "キャップを外した");
            Assert.AreEqual("", SaveSystem.Data.hat);
            Assert.AreEqual("round", wear.Glasses?.id, "めがねは、そのまま");
            Assert.AreEqual("round", SaveSystem.Data.glasses, "セーブに残る");
        }

        [UnityTest]
        public IEnumerator Hair_BouncesWhenTheWormMoves()
        {
            Assert.IsTrue(UI.SelectSkin("hair"), "ぴょこんげは、はじめから");
            yield return Frames(3);
            Assert.AreEqual("hair", GM.Wear.Hat?.id);
            float swing = 0f;
            GameInput.VirtualMove = Vector2.up;
            for (int i = 0; i < 180; i++)
            {
                yield return null;
                swing = Mathf.Max(swing, GM.Wear.HairSwing);
            }
            ResetInput();
            Assert.Greater(swing, 3f, "歩くと、ぴょこぴょこゆれる");
            Assert.Less(swing, 41f, "ゆれすぎない");
        }

        [UnityTest]
        public IEnumerator SkinPage_ShowsThreeSectionsAndEveryCard()
        {
            UI.ShowCollection(true);
            UI.SelectTab("skin");
            yield return Frames(3);
            Assert.AreEqual(Skins.All.Count, UI.SkinCardCount);
            var grid = UI.Root.Q("skin-grid");
            var heads = grid.Query<Label>(className: "skin-section").ToList();
            Assert.AreEqual(3, heads.Count, "いろ・ぼうし・めがね");
            Assert.AreEqual("ぼうし", heads[1].text);
            UI.ShowCollection(false);
        }
    }
}

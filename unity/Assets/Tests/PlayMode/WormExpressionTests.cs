using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：しゃくとりむしの表情と、休んでいるときのしぐさ：まばたき・目線・口・触角と脚・体節の波・背伸びのつま先立ち・
    /// 眠る（目をとじて「z Z」）・かじる・あくび・日なたぼっこ・がまん・びっくり・ダンス・ほこりの色・光るキノコと寒さ（夜の森）。
    /// </summary>
    public class WormExpressionTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.5f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest, Timeout(120000)]
        public IEnumerator Blinks_LooksAtThings_AndOpensItsMouthWhenHappy()
        {
            // まばたき（ときどき）
            yield return WaitUntil(() => Worm.BlinkAmount > 0.85f, 10f, "まばたき");
            yield return WaitUntil(() => Worm.BlinkAmount < 0.1f, 2f, "目をあける");
            // 目線：見る物の方へ、目の光が動く
            Vector3 right = Vector3.Cross(Worm.body.HeadUp, Worm.body.HeadForward);
            Worm.LookAt(Worm.body.HeadCenter + right * 3f, 3f, false);
            yield return Seconds(0.8f);
            Assert.Greater(Worm.Gaze.x, 0.3f, "右を見る");
            Worm.LookAt(Worm.body.HeadCenter - right * 3f, 3f, false);
            yield return Seconds(0.8f);
            Assert.Less(Worm.Gaze.x, -0.3f, "左を見る");
            // うれしいと、口をあける（あいた口がえがかれる）
            Worm.Cheer(1f);
            yield return Seconds(0.25f);
            Assert.Greater(Worm.MouthOpen, 0.5f, "口をあける");
            Assert.Greater(GM.Wear.MouthShown, 0.5f, "あいた口をえがく");
            yield return Seconds(1.5f);
            Assert.Less(Worm.MouthOpen, 0.1f, "口をとじる");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Walking_GripsWithTheLegs_AndRipplesTheSegments()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            float grip = 0f, stretch = 0f, bulge = 0f;
            yield return Hold(Vector2.up, 3f, () =>
            {
                grip = Mathf.Max(grip, Mathf.Max(Worm.body.tailGrip, Worm.body.headGrip));
                stretch = Mathf.Max(stretch, Worm.body.segmentStretch);
                bulge = Mathf.Max(bulge, Worm.body.segmentBulge);
            });
            Assert.Greater(grip, 0.8f, "脚をついた瞬間に、ぺたっ");
            Assert.Greater(stretch, 0.15f, "体節が、伸び縮みする");
            Assert.Greater(bulge, 0.04f, "体節が、ふくらむ");
            // 背伸び：体のうしろも、少し持ち上げる（つま先立ち）
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Idle, 3f, "止まる");
            yield return Seconds(0.5f);
            GameInput.VirtualStand = true;
            yield return Seconds(1.2f);
            // 尾の先はついたまま、そのすぐ前の体が持ち上がる
            float lift = AboveGround(Worm.Curve.pos[7]) - AboveGround(Worm.Curve.pos[1]);
            GameInput.VirtualStand = false;
            Assert.Greater(lift, 0.012f, "背伸びすると、体のうしろも浮く");
        }

        static float AboveGround(Vector3 p) => p.y - Areas.Current.Height(p.x, p.z);

        [UnityTest, Timeout(120000)]
        public IEnumerator SleepsAfterALongRest_WithClosedEyesAndZzz_AndWakesUp()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(0.5f);
            Worm.DebugSetIdleTime(InchwormController.SleepAfter + 1f);
            yield return WaitUntil(() => Worm.IsSleeping, 6f, "眠る");
            yield return Seconds(1f);
            Assert.Greater(Worm.BlinkAmount, 0.9f, "目をとじる");
            Assert.IsTrue(UI.IsSleepLabelVisible, "頭の上に「z Z」");
            Vector3 before = Worm.CenterPosition;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => !Worm.IsSleeping, 2f, "目をさます");
            yield return Seconds(2f);
            ResetInput();
            Assert.Greater(Vector3.Distance(before, Worm.CenterPosition), 0.3f, "目をさまして、歩く");
            Assert.IsFalse(UI.IsSleepLabelVisible);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Nibbles_Yawns_Sighs_Endures_AndStartles()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(0.5f);
            int bits = 0;
            Worm.Nibbled += _ => bits++;
            Worm.DebugMood("nibble");
            yield return Seconds(1.2f);
            Assert.IsTrue(Worm.IsNibbling, "葉をかじる");
            Assert.Greater(Worm.MouthOpen, 0.1f, "もぐもぐ");
            yield return Seconds(1.5f);
            Assert.Greater(bits, 1, "かじるたびに、葉のかけら");
            Worm.DebugMood("yawn");
            yield return Seconds(0.85f);
            Assert.IsTrue(Worm.IsYawning);
            Assert.Greater(Worm.MouthOpen, 0.7f, "大きなあくび");
            Assert.Greater(Worm.BlinkAmount, 0.4f, "目を細める");
            yield return Seconds(1.5f);
            Worm.DebugMood("sigh");
            yield return Seconds(0.7f);
            Assert.Greater(Worm.MouthOpen, 0.2f, "ふうっと一息");
            yield return Seconds(1.5f);
            // 天敵がそばにいると、小枝のまねで、目をとじてがまん
            Worm.DebugMood("endure");
            yield return Seconds(1f);
            Assert.IsTrue(Worm.IsTwigPose, "小枝のまね");
            Assert.Greater(Worm.EnduringAmount, 0.8f, "がまん");
            Assert.Greater(Worm.BlinkAmount, 0.8f, "目をぎゅっととじる");
            yield return Seconds(3f);
            // びっくり：まばたきして、反射的に一瞬かたまる
            Worm.DebugMood("startle");
            float blink = 0f, mouth = 0f;
            float t0 = Time.time;
            yield return WaitUntil(() =>
            {
                blink = Mathf.Max(blink, Worm.BlinkAmount);
                mouth = Mathf.Max(mouth, Worm.MouthOpen);
                return Time.time - t0 > 0.35f;
            }, 2f, "びっくり");
            Assert.Greater(blink, 0.3f, "まばたき");
            Assert.Greater(mouth, 0.1f, "口をあける");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Dances_Differently_AndGetsDusty()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(0.5f);
            Worm.Cheer(1f, 1);
            Assert.AreEqual(1, Worm.DanceStyle, "ぴょん");
            yield return Seconds(1f);
            Worm.Cheer(1.4f, 2);
            Assert.AreEqual(2, Worm.DanceStyle, "くるり");
            yield return Seconds(0.6f);
            Assert.Greater(Mathf.Abs(Worm.StandLook), 0.3f, "くるりと見まわす");
            yield return Seconds(1f);
            // ほこり：砂の上を歩くと、体がくすむ
            float sat = Worm.BodyTint.saturation;
            Worm.AddDust(0.6f);
            Assert.Less(Worm.BodyTint.saturation, sat * 0.85f, "ほこりで、くすむ");
            Assert.Greater(Worm.Dust, 0.5f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator NightForest_GlowingMushroomsLightTheWorm_AndItShivers()
        {
            SaveSystem.Data.visited.Add("night");
            GM.TravelTo("night");
            yield return WaitUntil(() => Areas.Current == Areas.Night && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, "夜の森へ");
            Assert.Greater(GM.world.GlowSpots.Count, 4, "光るキノコ");
            Vector3 cap = GM.world.GlowSpots[0];
            Worm.Spawn(SurfaceNear(cap, 0.7f), Vector3.left);
            yield return Seconds(2.5f);
            Assert.Greater(Worm.GlowAmount, 0.3f, "光るキノコに、てらされる");
            Assert.Greater(Worm.BodyTint.rim, 0.3f, "体のふちが光る");
            Assert.Greater(Worm.Cold, 0.4f, "夜の森は、寒い");
        }
    }
}

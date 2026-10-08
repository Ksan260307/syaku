using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：公園（トンネルで行く・ブランコ・シーソー・すべり台・新しいいきもの）。</summary>
    public class ParkPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
            SaveSystem.Data.visited.Add("park");
            GM.TravelTo("park");
            yield return WaitUntil(() => Areas.Current == Areas.Park && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, "公園へ");
            yield return Seconds(0.5f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator Arrival_ShowsThePlayground()
        {
            Vector3 w = Worm.CenterPosition;
            Assert.Less(Vector2.Distance(new Vector2(w.x, w.z), ParkLayout.Spawn), 4f, "トンネルを出た所");
            Assert.Less(Vector3.Dot(Worm.Heading, Vector3.right), -0.6f, "公園のまん中（西）を向く");
            Assert.IsTrue(Cam.ShowingView, "景色を写す");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Swing_SwaysBiggerWhenRiddenAndCarriesTheWorm()
        {
            var swing = GM.world.Swings[0];
            float idle = swing.Amplitude;
            // 座板の上に置く
            Vector3 seat = swing.transform.TransformPoint(new Vector3(0f, -13.6f, 0f));
            Worm.Spawn(seat + Vector3.up * 0.05f, Vector3.right);
            yield return Seconds(2.5f);
            Assert.AreSame(swing.transform, Worm.PlatformUnder, "座板に乗っている");
            Assert.IsTrue(swing.Loaded);
            Assert.Greater(swing.Amplitude, idle + 3f, "乗ると大きくゆれる");
            float minZ = float.MaxValue, maxZ = float.MinValue;
            yield return Hold(Vector2.zero, SwingRide.Period, () => { minZ = Mathf.Min(minZ, Worm.CenterPosition.z); maxZ = Mathf.Max(maxZ, Worm.CenterPosition.z); });
            Assert.Greater(maxZ - minZ, 3f, "しゃくとりむしも一緒にゆれる");
            Assert.AreSame(swing.transform, Worm.PlatformUnder, "ゆれても落ちない");
        }

        [UnityTest]
        public IEnumerator Seesaw_TipsToTheSideTheWormIsOn()
        {
            var saw = GM.world.Seesaw;
            saw.SetDown(1f);   // 東（+X）のはしが下
            yield return Frames(2);
            // 上がっている西のはしの上に置く
            Vector3 high = saw.transform.TransformPoint(new Vector3(-8.5f, 0.5f, 0f));
            Worm.Spawn(high, Vector3.left);
            yield return Seconds(4f);
            Assert.AreSame(saw.transform, Worm.PlatformUnder, "板に乗っている");
            Assert.AreEqual(-1f, saw.DownSide, "乗った西のはしが下がる");
            Assert.Greater(saw.Angle, SeesawRide.MaxTilt * 0.8f, "かたむききる");
        }

        [UnityTest]
        public IEnumerator Slide_RidesDownTheRamp()
        {
            var slide = GM.world.Slide;
            Assert.IsNotNull(slide);
            yield return Frames(2);
            // そり（坂の上）に、下を向いて乗る
            Vector3 down = slide.transform.forward;
            Worm.Spawn(slide.transform.position + slide.transform.up * 0.1f, Vector3.ProjectOnPlane(down, Vector3.up).normalized);
            yield return WaitUntil(() => slide.Rides > 0, 4f, "すべりだす");
            float startY = Worm.CenterPosition.y;
            yield return WaitUntil(() => !slide.Sliding, 12f, "下まですべる");
            Assert.Less(Worm.CenterPosition.y, startY - 6f, "下までおりた");
            Vector3 end = slide.slide.TransformPoint(SlideRide.RampEnd);
            Assert.Less(Vector3.Distance(Worm.CenterPosition, end), 5f, "出口に着く");
        }

        [UnityTest]
        public IEnumerator NewCreatures_CanBeFound()
        {
            foreach (var id in new[] { "kamikiri", "kuwagata", "kamemushi", "tokage", "monshiro" })
            {
                Vector3 p = GM.creatures.PositionOf(id, 0);
                Assert.AreNotEqual(Vector3.zero, p, id);
                Worm.Spawn(TopSurface(new Vector2(p.x + 1.2f, p.z)), Vector3.left);
                yield return Seconds(1f);
                CollectionAssert.Contains(SaveSystem.Data.creatures, id, $"{id} を見つけた");
            }
        }

        [UnityTest]
        public IEnumerator StinkBug_LetsOutASmellWhenStartled()
        {
            int stank = 0;
            GM.creatures.Stank += _ => stank++;
            Vector3 p = GM.creatures.PositionOf("kamemushi", 0);
            GM.creatures.Disturb(p, 2f);
            yield return Frames(2);
            Assert.AreEqual(1, stank, "おどろくと、においを出す");
            GM.creatures.Disturb(p, 2f);
            yield return Frames(2);
            Assert.AreEqual(1, stank, "すぐには、またにおいを出さない");
        }

        [UnityTest]
        public IEnumerator Lizard_DartsAwayFromTheWorm()
        {
            Vector3 p = GM.creatures.PositionOf("tokage", 0);
            Worm.Spawn(TopSurface(new Vector2(p.x + 1.6f, p.z)), Vector3.left);
            yield return Seconds(1.5f);
            Vector3 q = GM.creatures.PositionOf("tokage", 0);
            Assert.Greater(Vector2.Distance(new Vector2(q.x, q.z), new Vector2(Worm.HeadPosition.x, Worm.HeadPosition.z)),
                Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Worm.HeadPosition.x, Worm.HeadPosition.z)) + 0.5f, "近づくと、さっと逃げる");
        }
    }
}

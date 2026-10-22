using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：まわりへの反応：なかよしにぺこり・大きな声の方を見る・花のそばでくんくん・
    /// カマキリのそばでは忍び足・しずくが近いと早足。
    /// </summary>
    public class WormReactionTests
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

        [UnityTest, Timeout(120000)]
        public IEnumerator Bows_AndTurnsToALoudCall()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.5f);
            int bows = Worm.Bows;
            Worm.Bow();
            yield return Seconds(0.4f);
            Assert.IsTrue(Worm.IsBowing);
            Assert.Greater(Worm.RearAmount, 0.1f, "頭をちょこんと下げる");
            yield return Seconds(1f);
            Assert.AreEqual(bows + 1, Worm.Bows);
            // 右のほうで大きな声がすると、ふとそちらを見る
            Vector3 right = Vector3.Cross(Worm.body.HeadUp, Worm.body.HeadForward);
            int heard = Worm.HeardSounds;
            Worm.HearSound(Worm.HeadPosition + right * 6f, 0.9f);
            yield return Seconds(0.8f);
            Assert.AreEqual(heard + 1, Worm.HeardSounds);
            Assert.Greater(Worm.Gaze.x, 0.3f, "声の方を見る");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SniffsAFlower()
        {
            // 根もとから見上げられる花（地面から 3 まで）
            Vector3 f = Vector3.zero;
            bool found = false;
            foreach (var p in GM.world.FlowerPoints)
            {
                float h = p.y - Areas.Current.Height(p.x, p.z);
                if (h > 0.05f && h < 3f && !Areas.Current.IsUnderwater(p)) { f = p; found = true; break; }
            }
            Assume.That(found, "見上げられる花がない");
            Worm.Spawn(SurfaceNear(f, 0.55f), Vector3.left);
            yield return WaitUntil(() => Worm.IsSniffing, 20f, "くんくん");
            Assert.Greater(Worm.Sniffs, 0);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SneaksNearAMantis()
        {
            Vector3 m = GM.creatures.PositionOf("mantis", 0);
            Place(new Vector2(m.x + 1.8f, m.z), Vector3.right);
            yield return FaceCamera(Vector3.right);
            bool sneaking = false;
            yield return Hold(Vector2.up, 2f, () => sneaking |= Worm.IsSneaking);
            Assert.IsTrue(sneaking, "カマキリのそばでは、体を低くして、そっと歩く");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HurriesTowardANearbyDewdrop()
        {
            Assume.That(Col.NearestDrop(Worm.HeadPosition, 400f, out var drop), "しずくがない");
            Vector3 from = SurfaceNear(drop, 2.8f);
            Vector3 dir = Vector3.ProjectOnPlane(drop - from, Vector3.up).normalized;
            Worm.Spawn(from, dir);
            yield return FaceCamera(dir);
            yield return Seconds(1.2f);
            bool hurry = false;
            yield return Hold(Vector2.up, 2.5f, () => hurry |= Worm.IsHurrying);
            Assert.IsTrue(hurry, "しずくが近いと、少し早足");
        }
    }
}

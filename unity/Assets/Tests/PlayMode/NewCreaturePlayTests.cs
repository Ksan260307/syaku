using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：新しいいきもの（モグラ・コウモリ・ゲンゴロウ・ナナフシ）の動き。</summary>
    public class NewCreaturePlayTests
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
        public IEnumerator Mole_PeeksOutOfItsHillAndHidesWhenApproached()
        {
            var C = GM.creatures;
            Vector3 hill = C.PositionOf("mogura");
            Assert.AreNotEqual(Vector3.zero, hill, "森にモグラ塚がある");
            Worm.Spawn(TopSurface(new Vector2(hill.x + 9f, hill.z)), Vector3.left);
            yield return WaitUntil(() => C.DigOf("mogura") < 0.2f, 20f, "顔を出す");
            Assert.IsFalse(Creatures.IsDiscovered("mogura"), "遠くからは、まだ見つけない");
            // もぐっているときに近づいても、見つけられない（近くにいるあいだは、ずっともぐったまま）
            yield return WaitUntil(() => C.DigOf("mogura") > 0.95f, 20f, "もぐる");
            Worm.Spawn(TopSurface(new Vector2(hill.x + 1.8f, hill.z)), Vector3.left);
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                yield return null;
                Assert.IsFalse(Creatures.IsDiscovered("mogura"), "もぐっている間は見つけられない");
            }
            Assert.Greater(C.DigOf("mogura"), 0.9f, "近くにいるあいだは、顔を出さない");
            // 少しはなれて待つと、また顔を出して、見つけられる
            Worm.Spawn(TopSurface(new Vector2(hill.x + 4f, hill.z)), Vector3.left);
            yield return WaitUntil(() => Creatures.IsDiscovered("mogura"), 30f, "顔を出したところを見つける");
        }

        [UnityTest]
        public IEnumerator Bat_KeepsFlyingAroundTheCave()
        {
            var C = GM.creatures;
            yield return Seconds(2f);   // 飛びあがるまで
            Vector3 p0 = C.PositionOf("koumori");
            float lowest = 99f, path = 0f;
            Vector3 last = p0;
            for (float t = 0f; t < 6f; t += Time.deltaTime)
            {
                yield return null;
                Vector3 p = C.PositionOf("koumori");
                lowest = Mathf.Min(lowest, p.y - Areas.Current.Height(p.x, p.z));
                path += Vector3.Distance(p, last);
                last = p;
            }
            Assert.Greater(lowest, 0.6f, "とまらずに飛びつづける");
            Assert.Greater(path, 4f, "ひらひら飛びまわる");
        }

        [UnityTest]
        public IEnumerator DivingBeetle_DivesAndComesUpToBreathe()
        {
            var C = GM.creatures;
            Assert.Greater(C.CountOf("gengorou"), 0);
            yield return WaitUntil(() => C.DiveOf("gengorou") > 0.8f, 12f, "深くもぐる");
            yield return WaitUntil(() => C.DiveOf("gengorou") < 0.2f, 12f, "水面へ上がって息つぎ");
        }

        [UnityTest]
        public IEnumerator StickInsect_FreezesWhenApproached()
        {
            var C = GM.creatures;
            Vector3 p = C.PositionOf("nanafushi");
            Worm.Spawn(TopSurface(new Vector2(p.x + 1.5f, p.z)), Vector3.left);
            yield return Seconds(1f);
            Vector3 a = C.PositionOf("nanafushi");
            yield return Seconds(2f);
            Assert.Less(Vector3.Distance(C.PositionOf("nanafushi"), a), 0.05f, "近くでは、小枝のふりをして動かない");
        }
    }
}

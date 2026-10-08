using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：小さな物（松ぼっくり・落ち葉・小石）は、どんぐりと同じように押すと動く。</summary>
    public class ClimbablePropTests
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

        /// <summary>seconds 秒だけ歩いて、進んだ道のりと、いちばん長く止まっていた時間をはかる。</summary>
        static IEnumerator Walk(float seconds, float[] result, System.Func<bool> done = null)
        {
            Vector3 last = Worm.CenterPosition;
            float path = 0f, still = 0f, maxStill = 0f, t = 0f;
            GameInput.VirtualMove = Vector2.up;
            while (t < seconds && (done == null || !done()))
            {
                yield return null;
                t += Time.deltaTime;
                float d = Vector3.Distance(Worm.CenterPosition, last);
                if (d > 0.05f)
                {
                    path += d;
                    last = Worm.CenterPosition;
                    still = 0f;
                }
                else still += Time.deltaTime;
                maxStill = Mathf.Max(maxStill, still);
            }
            GameInput.VirtualMove = Vector2.zero;
            result[0] = path;
            result[1] = maxStill;
        }

        [UnityTest]
        public IEnumerator SmallThings_ArePushedLikeAcorns()
        {
            // 松ぼっくり・落ち葉・小石を、しゃくとりむしの前にならべて、歩いて押す
            var loose = GM.world.loose;
            var assets = GM.world.assets;
            Vector3 start = Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            float y = start.y;
            loose.Add(assets.Get("Pinecone"), assets.prop, new Vector3(start.x, ForestLayout.Height(start.x, start.z + 2.2f) + 0.45f, start.z + 2.2f),
                Quaternion.Euler(0f, 90f, 90f), 1f, LooseProps.Shape.Pinecone, true, 150f);
            int cone = loose.Count - 1;
            loose.Add(assets.Get("Leaf_Oak_Orange"), assets.prop, new Vector3(start.x, ForestLayout.Height(start.x, start.z + 1.1f) + 0.02f, start.z + 1.1f),
                Quaternion.identity, 0.12f, LooseProps.Shape.Leaf, false, 40f);
            int leaf = loose.Count - 1;
            Vector3 cone0 = loose.PositionOf(cone), leaf0 = loose.PositionOf(leaf);
            yield return Frames(3);
            Assert.IsTrue(loose.HasBody(cone) && loose.HasBody(leaf), "近づくと、押せる体を持つ");
            var r = new float[2];
            yield return Walk(5f, r);
            Assert.Greater(Vector3.Distance(loose.PositionOf(leaf), leaf0), 0.15f, "落ち葉は押されて動く");
            Assert.Greater(Vector3.Distance(loose.PositionOf(cone), cone0), 0.3f, "松ぼっくりは押されて転がる");
            Assert.LessOrEqual(loose.BodyCount, LooseProps.MaxBodies);
        }

        [UnityTest]
        public IEnumerator SmallThings_StayWhereTheyWerePushed()
        {
            var loose = GM.world.loose;
            var assets = GM.world.assets;
            Vector3 start = Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            loose.Add(assets.Get("Rock_A"), assets.prop, new Vector3(start.x, ForestLayout.Height(start.x, start.z + 1.4f) + 0.15f, start.z + 1.4f),
                Quaternion.identity, 0.12f, LooseProps.Shape.Pebble, false, 40f);
            int pebble = loose.Count - 1;
            yield return Frames(3);
            var r = new float[2];
            yield return Walk(4f, r);
            Vector3 moved = loose.PositionOf(pebble);
            Assert.Greater(Vector3.Distance(moved, loose.HomeOf(pebble)), 0.15f, "小石は押されて動く");
            // 遠くへ行くと、体はなくなり、絵だけが動いた先にのこる
            Place(ForestLayout.Meadow, Vector3.forward);
            yield return Seconds(2f);
            Assert.IsFalse(loose.HasBody(pebble), "はなれたら、体はもどす");
            Assert.Less(Vector3.Distance(loose.PositionOf(pebble), moved), 0.3f, "押した先にのこる");
            Assert.Less(loose.BodyCount, 60, "体を持つのは、しゃくとりむしのまわりだけ");
        }

    }
}

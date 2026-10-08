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
        public void TearDown()
        {
            ResetInput();
            BigLeaf.WindOverride = null;
        }

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
            yield return WaitUntil(() => loose.HasBody(cone) && loose.HasBody(leaf), 1f, "近づくと、押せる体を持つ");
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
            // しゃくとりむしが押せるくらい軽い小石（0.3g より軽い）
            Assert.Less(LooseProps.MassOf(assets.Get("Rock_A"), LooseProps.Shape.Pebble, 0.09f), LooseBody.WormPushLimit);
            Vector3 at = LooseProps.RestOnGround(assets.Get("Rock_A"), new Vector3(start.x, 0f, start.z + 1.4f), Quaternion.identity, 0.09f, Areas.Current);
            loose.Add(assets.Get("Rock_A"), assets.prop, at, Quaternion.identity, 0.09f, LooseProps.Shape.Pebble, false, 40f);
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

        [UnityTest]
        public IEnumerator BigLeaf_SlidesInAStrongGustButNotUnderTheWorm()
        {
            // はじまりの場所にいちばん近い、しずくのない大きな葉に乗る
            BigLeaf leaf = null;
            Vector3 spawn = Worm.CenterPosition;
            foreach (var l in GM.world.loose.BigLeaves)
                if (!l.Pinned && (leaf == null || Vector3.Distance(l.transform.position, spawn) < Vector3.Distance(leaf.transform.position, spawn))) leaf = l;
            Assert.IsNotNull(leaf);
            BigLeaf.WindOverride = new Vector3(0.3f, 0f, 0f);   // そよ風
            Vector3 top = TopSurface(new Vector2(leaf.transform.position.x, leaf.transform.position.z));
            Worm.Spawn(top, Vector3.forward);
            yield return Seconds(1.5f);
            Assert.IsTrue(leaf.Holds(Worm), "大きな葉に乗っている");
            Vector3 still = leaf.transform.position;
            yield return Seconds(1f);
            Assert.Less(Vector3.Distance(leaf.transform.position, still), 0.01f, "そよ風では動かない（重い）");
            // 乗っているあいだは、強い突風でも、足でつかんでいるのですべらない
            BigLeaf.WindOverride = new Vector3(1.2f, 0f, 0f);
            Vector3 l0 = leaf.transform.position;
            yield return Seconds(1f);
            Assert.Less(Vector3.Distance(leaf.transform.position, l0), 0.01f, "乗っている葉はすべらない");
            // 降りると、強い突風で、まさつに勝って風下へすべる
            Worm.Spawn(TopSurface(new Vector2(l0.x - 12f, l0.z)), Vector3.forward);
            yield return Seconds(1.5f);
            BigLeaf.WindOverride = new Vector3(0.1f, 0f, 0f);
            yield return Seconds(0.5f);
            Assert.Greater(leaf.transform.position.x - l0.x, 0.15f, "強い風で、風下へすべる");
            Vector3 after = leaf.transform.position;
            yield return Seconds(0.5f);
            Assert.Less(Vector3.Distance(leaf.transform.position, after), 0.01f, "風がやむと、まさつで止まる");
        }

        [UnityTest]
        public IEnumerator SmallThings_DoNotRollByThemselves()
        {
            // はじまりの場所でじっとしている：まわりの小石・松ぼっくり・ぼうしは、体を持っても動かない
            var loose = GM.world.loose;
            yield return Seconds(1f);
            var near = new System.Collections.Generic.List<int>();
            for (int i = 0; i < loose.Count; i++)
                if (loose.HasBody(i) && loose.BodyOf(i).Body.mass > 0f && !loose.MeshNameOf(i).StartsWith("Leaf_")) near.Add(i);
            Assert.Greater(near.Count, 0, "まわりに、体を持つ小石などがある");
            var before = new Vector3[near.Count];
            for (int k = 0; k < near.Count; k++)
            {
                before[k] = loose.PositionOf(near[k]);
                loose.BodyOf(near[k]).Body.WakeUp();   // ゆさぶっても
            }
            yield return Seconds(4f);
            for (int k = 0; k < near.Count; k++)
            {
                int i = near[k];
                Assert.Less(Vector3.Distance(loose.PositionOf(i), before[k]), 0.1f, $"{loose.MeshNameOf(i)} が勝手に転がった");
                Vector3 p = loose.PositionOf(i);
                Assert.Greater(p.y, Areas.Current.Height(p.x, p.z) - loose.ScaleOf(i) * 2f, $"{loose.MeshNameOf(i)} が地面にもぐった");
            }
        }

        [UnityTest]
        public IEnumerator SmallThings_ComeBackUpWhenPushedIntoTheGround()
        {
            var loose = GM.world.loose;
            var assets = GM.world.assets;
            Vector3 start = Place(ForestLayout.Spawn, Vector3.forward);
            Vector3 at = LooseProps.RestOnGround(assets.Get("Pinecone"), new Vector3(start.x + 1.5f, 0f, start.z + 1f), Quaternion.Euler(90f, 0f, 0f), 1f, Areas.Current);
            loose.Add(assets.Get("Pinecone"), assets.prop, at, Quaternion.Euler(90f, 0f, 0f), 1f, LooseProps.Shape.Pinecone, true, 150f);
            int cone = loose.Count - 1;
            yield return WaitUntil(() => loose.HasBody(cone), 1f, "近づくと体を持つ");
            var rb = loose.BodyOf(cone).Body;
            rb.position = at + Vector3.down * 1.2f;   // 地面の下へおしこまれた
            rb.WakeUp();
            yield return Seconds(0.5f);
            Assert.Greater(loose.PositionOf(cone).y, at.y - 0.3f, "地面の上へもどる");
        }

    }
}

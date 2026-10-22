using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：落ちる・水：背伸びボタンでふわっと・低い所からは弾まずに「ぽてっ」・強く落ちるとふらふら・
    /// 水に落ちたら泳いで浮いた物に上がる（泳がなければ、しずんで安全な場所へ）・しずくが当たると、ぴくっ。
    /// </summary>
    public class WormFallTests
    {
        readonly List<GameObject> _temp = new List<GameObject>();

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
            foreach (var go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
        }

        GameObject Box(Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestBox";
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>地面から height の高さで、壁（+X 向きの面）にしがみつかせる。</summary>
        IEnumerator OnAWall(float height)
        {
            Vector3 g = TopSurface(ForestLayout.Spawn + new Vector2(4f, 0f));
            Vector3 c = g + new Vector3(0f, height, 0f);
            Box(c, new Vector3(0.4f, height * 2f + 1f, 2f));
            float fx = c.x + 0.2f;
            Worm.Spawn(new Vector3(fx, c.y - 0.4f, c.z), Vector3.right, new Vector3(fx, c.y + 0.4f, c.z), Vector3.right);
            yield return Seconds(0.4f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LowDrop_PlopsWithoutCurling()
        {
            yield return OnAWall(0.6f);
            Worm.LetGo();
            yield return WaitUntil(() => Worm.IsFalling, 1f, "手をはなす");
            Assert.IsTrue(Worm.IsLowDrop, "低い所からは、弾まずに「ぽてっ」");
            yield return WaitUntil(() => !Worm.IsFalling, 3f, "着地");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HoldingStand_Glides_AndAHardLandingMakesItWobbly()
        {
            yield return OnAWall(6f);
            Worm.LetGo();
            yield return WaitUntil(() => Worm.IsFalling, 1f, "手をはなす");
            Assert.IsFalse(Worm.IsLowDrop);
            GameInput.VirtualStand = true;
            bool glided = false;
            yield return WaitUntil(() => { glided |= Worm.IsGliding; return glided; }, 1.5f, "体を広げる");
            GameInput.VirtualStand = false;
            yield return WaitUntil(() => !Worm.IsFalling, 6f, "着地");
            if (Worm.LastImpact > 0.45f) Assert.IsTrue(Worm.IsWobbly, "強く落ちたあとは、ふらふら");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Water_SwimsToAFloatingLeaf()
        {
            float wl = ForestLayout.WaterLevel;
            Vector3 c = Vector3.zero;
            bool found = false;
            for (float dx = -12f; dx <= 12f && !found; dx += 1.5f)
                for (float dz = -12f; dz <= 12f && !found; dz += 1.5f)
                {
                    if (dx * dx + dz * dz > 13f * 13f) continue;
                    Vector2 xz = ForestLayout.Pond + new Vector2(dx, dz);
                    c = new Vector3(xz.x, wl + 1.5f, xz.y);
                    bool deep = ForestLayout.Height(xz.x + 0.8f, xz.y) < wl - 0.8f && ForestLayout.Height(xz.x + 2.5f, xz.y) < wl - 0.5f;
                    bool clear = !Physics.CheckBox(new Vector3(xz.x + 1.4f, wl + 1.1f, xz.y), new Vector3(1.8f, 1.05f, 1.2f), Quaternion.identity,
                        ShakuConst.WalkableMask, QueryTriggerInteraction.Ignore);
                    found = deep && clear;
                }
            Assume.That(found, "水たまりの上に、ひらけた場所がない");
            Box(c, new Vector3(0.4f, 2f, 2f));
            // 浮いた葉（水面のすぐ上の、うすい台）
            Box(new Vector3(c.x + 2.2f, wl + 0.02f, c.z), new Vector3(0.9f, 0.04f, 1.6f));
            float fx = c.x + 0.2f;
            Worm.Spawn(new Vector3(fx, wl + 1.1f, c.z), Vector3.right, new Vector3(fx, wl + 1.9f, c.z), Vector3.right);
            yield return FaceCamera(Vector3.right);
            yield return Seconds(0.4f);
            bool splashed = false;
            Worm.Splashed += () => splashed = true;
            Worm.LetGo();
            yield return WaitUntil(() => splashed, 4f, "水に落ちる");
            yield return WaitUntil(() => Worm.IsSwimming, 2f, "泳ぐ");
            int climbs = Worm.ShoreClimbs;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.ShoreClimbs > climbs || !Worm.IsFalling, 7f, "浮いた葉まで泳ぐ");
            ResetInput();
            Assert.Greater(Worm.ShoreClimbs, climbs, "泳いで、浮いた葉に上がる");
            Assert.IsTrue(Worm.IsWet, "ぬれている");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Drip_MakesTheWormFlinchAndLookUp()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(1f);
            int drips = Worm.Drips;
            Vector3 at = Vector3.zero;
            Worm.Dripped += p => at = p;
            Worm.DebugDrip();
            Assert.AreEqual(drips + 1, Worm.Drips);
            Assert.Less(Vector3.Distance(at, Worm.HeadPosition), 0.3f, "頭に当たる");
            float blink = 0f;
            float t0 = Time.time;
            yield return WaitUntil(() => { blink = Mathf.Max(blink, Worm.BlinkAmount); return Time.time - t0 > 0.4f; }, 2f, "ぴくっ");
            Assert.Greater(blink, 0.3f, "びっくりして、まばたき");
        }
    }
}

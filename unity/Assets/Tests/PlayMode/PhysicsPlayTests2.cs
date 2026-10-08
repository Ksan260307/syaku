using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：物理計算（第 2 弾）。糸のロープ・ねじれ・登る疲れ・しずく・カメラ・演出。</summary>
    public class PhysicsPlayTests2
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

        GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        Vector3 Sky => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.2f, 0f);

        IEnumerator HangFromABox()
        {
            var shelf = Box("TestBox", Sky, new Vector3(2f, 0.3f, 2f));
            Worm.Spawn(shelf.transform.position + new Vector3(0f, 0.15f, -0.3f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 10f, "ふちで糸を出せる");
            GameInput.VirtualMove = Vector2.zero;
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Hang, 1f, "ぶら下がる");
        }

        [UnityTest]
        public IEnumerator Silk_IsARopeThatFallsAwayWhenCut()
        {
            yield return HangFromABox();
            yield return Seconds(0.6f);
            var rope = Worm.SilkRope;
            Assert.IsNotNull(rope);
            Assert.AreEqual(rope.Count, Worm.silk.positionCount, "糸は点をつないだロープ");
            TestUtil2.AssertNear(Worm.SilkAnchor, rope.pos[0], 0.05f, "つけ根");
            float len = rope.CurrentLength();
            Assert.Greater(len, Vector3.Distance(rope.pos[0], rope.pos[rope.Count - 1]) * 0.99f);
            // 背伸びボタンで糸を切ると、はなれた糸はひらひら落ちる
            GameInput.VirtualStand = true;
            yield return Frames(3);
            GameInput.VirtualStand = false;
            Assert.IsTrue(Worm.IsFalling);
            Vector3 end0 = rope.pos[rope.Count - 1];
            yield return Seconds(0.4f);
            Assert.IsTrue(Worm.silk.enabled, "しばらくは見えている");
            // はなした糸のはしは、ひらひら落ちていく（ぴんと張ってたれていたときは、まずゆれてから落ちる）
            yield return WaitUntil(() => rope.pos[rope.Count - 1].y < end0.y - 0.05f || Vector3.Distance(rope.pos[rope.Count - 1], end0) > 0.15f,
                1.2f, "はなした糸のはしが、はなれていく");
        }

        [UnityTest]
        public IEnumerator Hanging_TwistsSlowlyOnTheSilk()
        {
            yield return HangFromABox();
            float maxTwist = 0f;
            Vector3 f0 = Worm.Heading;
            yield return Hold(Vector2.zero, 2.5f, () => maxTwist = Mathf.Max(maxTwist, Mathf.Abs(Worm.TwistSpeed)));
            if (Worm.State == InchwormController.Mode.Hang)
                Assert.Greater(maxTwist, 0.5f, "糸のねじれと風で、体がゆっくり回る");
            Assert.Less(maxTwist, 240f, "ぐるぐる回りすぎない");
        }

        [UnityTest]
        public IEnumerator Climbing_MakesTheWormTired()
        {
            // 高い壁をまっすぐ登る
            Vector3 g = TopSurface(ForestLayout.Spawn);
            Box("TestWall", g + new Vector3(0f, 2f, 1.8f), new Vector3(3f, 4f, 0.6f));
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.5f);
            Assert.Less(Worm.Fatigue, 0.05f);
            bool climbed = false;
            float maxFatigue = 0f;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() =>
            {
                maxFatigue = Mathf.Max(maxFatigue, Worm.Fatigue);
                climbed |= Worm.HeadPoint.y - g.y > 2f;
                return climbed;
            }, 25f, "壁を登る");
            ResetInput();
            Assert.Greater(maxFatigue, 0.15f, "体を持ち上げた分だけ疲れる");
            float tired = Worm.Fatigue;
            yield return Seconds(3f);
            Assert.Less(Worm.Fatigue, tired, "休むともどる");
        }

        [UnityTest]
        public IEnumerator Dewdrop_HopsFromAHardLandingNearby()
        {
            int pick = -1;
            for (int i = 0; i < Col.TotalDrops && pick < 0; i++)
                if (!Col.IsDropTaken(i) && Vector3.Distance(Col.DropPosition(i), Worm.HeadPosition) > 6f) pick = i;
            Assume.That(pick >= 0);
            Col.Impulse(Col.DropPosition(pick), 6f);
            float max = 0f;
            yield return Hold(Vector2.zero, 0.4f, () => max = Mathf.Max(max, Col.DropHop(pick)));
            Assert.Greater(max, 0.03f, "ぴょんとはねる");
            yield return Seconds(2f);
            Assert.AreEqual(0f, Col.DropHop(pick), 1e-3f, "重力で落ちて、止まる");
        }

        [UnityTest]
        public IEnumerator Camera_DipsOnAHardLanding()
        {
            Cam.Dip(1f);
            float min = 0f;
            yield return Hold(Vector2.zero, 0.4f, () => min = Mathf.Min(min, Cam.DipOffset));
            Assert.Less(min, -0.02f, "ぐっと下がる");
            yield return Seconds(2f);
            Assert.Less(Mathf.Abs(Cam.DipOffset), 0.003f, "ばねでもどる");
        }

        [UnityTest]
        public IEnumerator Effects_DustAndSplashAreEmitted()
        {
            var fx = GM.fx;
            Vector3 p = Worm.TailPoint;
            fx.Dust(p, Vector3.up, 1f, 0f);
            fx.Splash(p, 1f, p.y);
            fx.Drip(p);
            yield return null;
            int dust = 0, splash = 0;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.name == "LandingDust") dust = ps.particleCount;
                if (ps.name == "WaterSplash") splash = ps.particleCount;
            }
            Assert.Greater(dust, 10, "強く落ちると土けむり");
            Assert.Greater(splash, 30, "水しぶき");
            fx.Dust(p, Vector3.up, 1f, 1f);
            yield return null;
            int soft = 0;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) if (ps.name == "LandingDust") soft = ps.particleCount;
            Assert.Less(soft - dust, 12, "やわらかい葉っぱの上では、ほとんど出ない");
        }
    }

    static class TestUtil2
    {
        public static void AssertNear(Vector3 a, Vector3 b, float tol, string msg) => Assert.Less(Vector3.Distance(a, b), tol, msg);
    }
}

using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>物理計算（第 2 弾）の単体テスト。</summary>
    public class PhysicsTests2
    {
        [Test]
        public void SpringExact_MatchesAFineSimulation()
        {
            foreach (float zeta in new[] { 0.2f, 0.6f, 1f })
            {
                float xe = 0f, ve = 3f, xs = 0f, vs = 3f;
                for (int i = 0; i < 30; i++) ShakuPhysics.SpringExact(ref xe, ref ve, 1f, 10f, zeta, 1f / 30f);
                for (int i = 0; i < 3000; i++) ShakuPhysics.Spring(ref xs, ref vs, 1f, 10f, zeta, 1f / 3000f);
                Assert.AreEqual(xs, xe, 0.01f, $"ζ={zeta}：式で解いたばねは、細かく計算したばねと同じ");
                Assert.AreEqual(vs, ve, 0.05f);
            }
            float x = 5f, v = 0f;
            ShakuPhysics.SpringExact(ref x, ref v, 0f, 30f, 0.3f, 100f);
            Assert.AreEqual(0f, x, 1e-3f, "とても長い時間でも、こわれずに落ちつく");
        }

        [Test]
        public void Damp_IsTheSameAtAnyFrameRate()
        {
            float a = 0f, b = 0f;
            for (int i = 0; i < 60; i++) a = ShakuPhysics.Damp(a, 1f, 3f, 1f / 60f);
            for (int i = 0; i < 15; i++) b = ShakuPhysics.Damp(b, 1f, 3f, 1f / 15f);
            Assert.AreEqual(a, b, 1e-4f);
            Vector3 d1 = Vector3.forward, d2 = Vector3.forward;
            for (int i = 0; i < 60; i++) d1 = ShakuPhysics.DampDir(d1, Vector3.right, 2f, 1f / 60f);
            for (int i = 0; i < 10; i++) d2 = ShakuPhysics.DampDir(d2, Vector3.right, 2f, 1f / 10f);
            Assert.Less(Vector3.Angle(d1, d2), 0.5f, "向きも同じ");
            Assert.AreEqual(1f, d1.magnitude, 1e-4f);
        }

        [Test]
        public void BankAngle_BalancesGravityAndTheTurn()
        {
            Assert.AreEqual(0f, ShakuPhysics.BankAngle(0f, 90f), 1e-4f, "止まって回るときは、かたむかない");
            float expect = Mathf.Atan(3f * Mathf.PI / 2f / ShakuPhysics.Gravity) * Mathf.Rad2Deg;
            Assert.AreEqual(expect, ShakuPhysics.BankAngle(3f, 90f), 1e-3f, "tanφ = vω / g");
            Assert.Greater(ShakuPhysics.BankAngle(6f, 90f), ShakuPhysics.BankAngle(3f, 90f), "速いほど大きくかたむく");
            Assert.Less(ShakuPhysics.BankAngle(3f, -90f), 0f, "反対に曲がれば反対に");
        }

        [Test]
        public void SolveLaunch_HitsTheTargetWithDrag()
        {
            Vector3 from = new Vector3(1f, 0.5f, 2f), to = new Vector3(4f, 1.2f, -1f);
            foreach (float drag in new[] { 0f, 0.3f, 0.8f })
            {
                const float T = 0.9f;
                Vector3 v = ShakuPhysics.SolveLaunch(from, to, T, drag);
                Vector3 p = from;
                const int n = 9000;
                for (int i = 0; i < n; i++)
                {
                    v += Vector3.down * ShakuPhysics.Gravity * (T / n);
                    v = ShakuPhysics.ApplyDrag(v, Vector3.zero, drag, 0f, T / n);
                    p += v * (T / n);
                }
                Assert.Less(Vector3.Distance(p, to), 0.03f, $"てい抗 {drag}：同じ時間で、ねらった所に着く");
            }
        }

        [Test]
        public void OptimalPeak_LaunchesAt45Degrees()
        {
            const float d = 2f;
            float peak = ShakuPhysics.OptimalPeak(d);
            float T = 2f * Mathf.Sqrt(2f * peak / ShakuPhysics.Gravity);
            Vector3 v = ShakuPhysics.SolveLaunch(Vector3.zero, Vector3.right * d, T, 0f);
            Assert.AreEqual(45f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg, 0.5f, "45 度で跳ぶと、いちばん少ない力で届く");
        }

        [Test]
        public void Buoyancy_BalancesAtTheFloatDepth()
        {
            Assert.AreEqual(0f, ShakuPhysics.BuoyantAccel(-0.1f, 0.03f, 60f), "水の上では浮力はない");
            Assert.AreEqual(ShakuPhysics.Gravity, ShakuPhysics.BuoyantAccel(0.03f, 0.03f, 60f), 1e-4f, "体が半分しずんだ所で、重さとつりあう");
            Assert.Greater(ShakuPhysics.BuoyantAccel(0.3f, 0.03f, 60f), ShakuPhysics.Gravity, "深くしずむと、押しもどされる");
        }

        [Test]
        public void Rope_SagsWhenSlackAndHangsStraightWhenTaut()
        {
            var rope = new VerletRope(12);
            Vector3 a = Vector3.up * 3f, b = new Vector3(2f, 3f, 0f);
            rope.Reset(a, b);
            for (int i = 0; i < 240; i++) rope.Step(1f / 60f, a, b, 2f, Vector3.zero);
            Assert.Less(rope.Sag(), 0.1f, "張った糸は、ほとんどまっすぐ（糸の重さで、ほんの少したわむだけ）");
            for (int i = 0; i < 240; i++) rope.Step(1f / 60f, a, b, 2.6f, Vector3.zero);
            Assert.Greater(rope.Sag(), 0.2f, "長さが余ると、たるむ");
            Assert.Less(rope.CurrentLength(), 2.6f * 1.03f, "のびない");
            Assert.Less(rope.pos[6].y, a.y, "たるみは下へ");
            // 風がふくと、たるんだ糸は風下へ流される
            for (int i = 0; i < 240; i++) rope.Step(1f / 60f, a, b, 2.6f, new Vector3(0f, 0f, 1.5f));
            Assert.Greater(rope.pos[6].z, 0.05f, "風下へ");
            // 片はしをはなすと、ひらひら落ちる
            float before = rope.pos[11].y;
            for (int i = 0; i < 30; i++) rope.Step(1f / 60f, a, null, 2.6f, Vector3.zero);
            Assert.Less(rope.pos[11].y, before - 0.05f, "はなした糸は落ちる");
            Assert.AreEqual(a, rope.pos[0], "つけ根はそのまま");
        }

        [Test]
        public void Rope_DoesNotSinkIntoTheGround()
        {
            var rope = new VerletRope(10) { groundHeight = p => 0f };
            Vector3 a = new Vector3(0f, 0.5f, 0f), b = new Vector3(1f, 0.5f, 0f);
            rope.Reset(a, b);
            for (int i = 0; i < 300; i++) rope.Step(1f / 60f, a, b, 3f, Vector3.zero);
            foreach (var p in rope.pos) Assert.GreaterOrEqual(p.y, -1e-3f, "地面の下にしずまない");
        }

        [Test]
        public void BigCreatures_FallFaster()
        {
            float small = 0f, big = 0f;
            for (int i = 0; i < 300; i++)
            {
                small = Creatures.FallSpeed(small, 0.05f, 0.6f);
                big = Creatures.FallSpeed(big, 0.05f, 2f);
            }
            Assert.Greater(big, small + 0.5f, "大きないきものは、体の重さにくらべて空気のてい抗が小さい");
        }

        [Test]
        public void Updraft_IsOnlyOverTheMeadow()
        {
            Vector2 m = ForestLayout.Meadow;
            Assert.Greater(Wind.Updraft(new Vector3(m.x, 0f, m.y), 3f), 0.05f, "草原の上は、ゆるく上がる");
            Assert.AreEqual(0f, Wind.Updraft(new Vector3(m.x + 60f, 0f, m.y + 60f), 3f), 1e-3f, "森の中は、上がらない");
        }
    }
}

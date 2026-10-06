using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    public class BodyCurveTests
    {
        const int N = 41;

        [Test]
        public void SolveArch_FullLengthIsStraight()
        {
            Assert.AreEqual(0f, BodyCurve.SolveArch(1f), 1e-5f);
            Assert.AreEqual(0f, BodyCurve.SolveArch(1.5f), 1e-5f);
        }

        [Test]
        public void SolveArch_IsMonotonicAndClamped()
        {
            float prev = -1f;
            for (float r = 1f; r >= 0f; r -= 0.05f)
            {
                float a = BodyCurve.SolveArch(r);
                Assert.GreaterOrEqual(a, prev - 1e-5f, $"ratio {r}");
                prev = a;
            }
            Assert.AreEqual(BodyCurve.ArchMax, BodyCurve.SolveArch(0f), 1e-5f);
        }

        [Test]
        public void SolveArch_MatchesBesselJ0()
        {
            // J0(1.0) ≈ 0.7652, J0(2.0) ≈ 0.2239
            Assert.AreEqual(1.0f, BodyCurve.SolveArch(0.7652f), 0.02f);
            Assert.AreEqual(2.0f, BodyCurve.SolveArch(0.2239f), 0.02f);
        }

        [TestCase(0.95f)]
        [TestCase(0.7f)]
        [TestCase(0.45f)]
        [TestCase(0.3f)]
        public void BuildArch_HitsBothEndsAndKeepsLength(float ratio)
        {
            var c = new BodyCurve(N);
            var o = new Vector3(1, 2, 3);
            var e = o + Vector3.right * ratio;
            c.BuildArch(o, e, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            TestUtil.AssertVector(o, c.Tail, 1e-3f, "tail");
            TestUtil.AssertVector(e, c.Head, 1e-3f, "head");
            Assert.AreEqual(1f, TestUtil.ArcLength(c), 0.04f, "体長が保たれる");
        }

        [Test]
        public void BuildArch_ShorterSpanMakesTallerLoop()
        {
            var a = new BodyCurve(N);
            var b = new BodyCurve(N);
            a.BuildArch(Vector3.zero, Vector3.right * 0.8f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            b.BuildArch(Vector3.zero, Vector3.right * 0.3f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            Assert.Greater(MaxY(b), MaxY(a) + 0.1f);
            Assert.Greater(MaxY(b), 0.25f, "Ω 字に高く持ち上がる");
            Assert.GreaterOrEqual(MinY(b), -0.01f, "地面より下にはいかない");
        }

        [Test]
        public void BuildArch_FullSpanIsFlat()
        {
            var c = new BodyCurve(N);
            c.BuildArch(Vector3.zero, Vector3.forward, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            for (int i = 0; i < N; i++) Assert.AreEqual(0f, c.pos[i].y, 1e-3f);
        }

        [Test]
        public void BuildArch_OverStretchStillReachesHead()
        {
            var c = new BodyCurve(N);
            c.BuildArch(Vector3.zero, Vector3.right * 1.3f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            TestUtil.AssertVector(Vector3.right * 1.3f, c.Head, 1e-3f);
        }

        [Test]
        public void BuildArch_FramesAreOrthonormalAndNeverFlip()
        {
            var c = new BodyCurve(N);
            c.BuildArch(Vector3.zero, Vector3.right * 0.22f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            for (int i = 0; i < N; i++)
            {
                Assert.AreEqual(1f, c.tan[i].magnitude, 1e-3f);
                Assert.AreEqual(1f, c.up[i].magnitude, 1e-3f);
                Assert.AreEqual(0f, Vector3.Dot(c.tan[i], c.up[i]), 2e-2f);
                if (i > 0) Assert.Greater(Vector3.Dot(c.up[i - 1], c.up[i]), 0.7f, $"背中の向きが {i} で裏返った");
            }
            // ループの頂上では背中は上を向く
            int top = 0;
            for (int i = 0; i < N; i++) if (c.pos[i].y > c.pos[top].y) top = i;
            Assert.Greater(c.up[top].y, 0.8f);
        }

        [Test]
        public void BuildArch_EndAnglesBendEnds()
        {
            var c = new BodyCurve(N);
            c.BuildArch(Vector3.zero, Vector3.right * 0.9f, Vector3.up, 1f, -0.4f, 0.4f, 0f, 0f);
            float startAngle = Mathf.Atan2(c.tan[0].y, c.tan[0].x);
            Assert.Less(startAngle, -0.1f, "尾は弦より下向き");
            TestUtil.AssertVector(Vector3.right * 0.9f, c.Head, 1e-3f);
        }

        [Test]
        public void BuildArch_SwayAndLiftMoveMiddleOnly()
        {
            var c = new BodyCurve(N);
            c.BuildArch(Vector3.zero, Vector3.right * 0.9f, Vector3.up, 1f, 0f, 0f, 1f, 0.05f);
            TestUtil.AssertVector(Vector3.zero, c.Tail, 1e-3f);
            TestUtil.AssertVector(Vector3.right * 0.9f, c.Head, 1e-3f);
            Assert.Greater(Mathf.Abs(c.Middle.z), 0.05f, "横にゆれる");
        }

        [Test]
        public void BuildRear_LiftsFrontWhileTailStays()
        {
            var c = new BodyCurve(N);
            c.BuildRear(Vector3.zero, Vector3.forward, Vector3.up, 1f, 1f, 0f, 0.5f);
            TestUtil.AssertVector(Vector3.zero, c.Tail, 1e-5f);
            Assert.Greater(c.Head.y, 0.4f);
            Assert.AreEqual(0f, c.pos[8].y, 0.02f, "腹脚のあたりは地面に沿う");
            Assert.AreEqual(1f, TestUtil.ArcLength(c), 0.02f);
            for (int i = 0; i < N; i++) Assert.AreEqual(1f, c.up[i].magnitude, 1e-3f);
        }

        [Test]
        public void BuildRear_ZeroRiseIsFlat()
        {
            var c = new BodyCurve(N);
            c.BuildRear(Vector3.zero, Vector3.forward, Vector3.up, 1f, 0f, 0f, 0f);
            for (int i = 0; i < N; i++) Assert.AreEqual(0f, c.pos[i].y, 1e-4f);
            TestUtil.AssertVector(Vector3.forward, c.Head, 1e-3f);
        }

        [Test]
        public void BuildHang_HeadOnTopTailBelow()
        {
            var c = new BodyCurve(N);
            var head = new Vector3(0, 5, 0);
            c.BuildHang(head, Vector3.forward, 1f, 0.6f, 0.1f);
            TestUtil.AssertVector(head, c.Head, 1e-5f);
            Assert.Less(c.Tail.y, head.y - 0.5f);
            Assert.AreEqual(1f, TestUtil.ArcLength(c), 0.02f);
            Assert.Greater(c.HeadForward.y, 0.5f, "頭は上（糸の方）を向く");
        }

        [Test]
        public void Blend_InterpolatesBetweenPoses()
        {
            var a = new BodyCurve(N);
            var b = new BodyCurve(N);
            var m = new BodyCurve(N);
            a.BuildArch(Vector3.zero, Vector3.right * 0.9f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            b.BuildRear(Vector3.zero, Vector3.right, Vector3.up, 1f, 1f, 0f, 0f);
            m.Blend(a, b, 0f);
            for (int i = 0; i < N; i++) TestUtil.AssertVector(a.pos[i], m.pos[i], 1e-5f);
            m.Blend(a, b, 1f);
            for (int i = 0; i < N; i++) TestUtil.AssertVector(b.pos[i], m.pos[i], 1e-5f);
            m.Blend(a, b, 0.5f);
            Assert.That(m.Head.y, Is.InRange(Mathf.Min(a.Head.y, b.Head.y) - 1e-4f, Mathf.Max(a.Head.y, b.Head.y) + 1e-4f));
        }

        [Test]
        public void CopyFrom_CopiesAllSamples()
        {
            var a = new BodyCurve(N);
            var b = new BodyCurve(N);
            a.BuildArch(Vector3.zero, Vector3.right * 0.5f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            b.CopyFrom(a);
            for (int i = 0; i < N; i++)
            {
                Assert.AreEqual(a.pos[i], b.pos[i]);
                Assert.AreEqual(a.tan[i], b.tan[i]);
                Assert.AreEqual(a.up[i], b.up[i]);
            }
        }

        [Test]
        public void BuildArch_WorksOnWallsAndCeilings()
        {
            var c = new BodyCurve(N);
            c.BuildArch(Vector3.zero, Vector3.up * 0.5f, Vector3.back, 1f, 0f, 0f, 0f, 0f);
            TestUtil.AssertVector(Vector3.up * 0.5f, c.Head, 1e-3f);
            float minZ = 0f;
            for (int i = 0; i < N; i++) minZ = Mathf.Min(minZ, c.pos[i].z);
            Assert.Less(minZ, -0.2f, "壁から離れる向き（-Z）にアーチ");
        }

        static float MaxY(BodyCurve c)
        {
            float m = float.MinValue;
            foreach (var p in c.pos) m = Mathf.Max(m, p.y);
            return m;
        }

        static float MinY(BodyCurve c)
        {
            float m = float.MaxValue;
            foreach (var p in c.pos) m = Mathf.Min(m, p.y);
            return m;
        }
    }
}

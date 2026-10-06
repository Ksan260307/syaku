using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    public class ShakuMathTests
    {
        [Test]
        public void Smooth01_ClampsAndIsMonotonic()
        {
            Assert.AreEqual(0f, ShakuMath.Smooth01(-1f));
            Assert.AreEqual(1f, ShakuMath.Smooth01(2f));
            Assert.AreEqual(0.5f, ShakuMath.Smooth01(0.5f), 1e-5f);
            float prev = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float v = ShakuMath.Smooth01(i / 20f);
                Assert.GreaterOrEqual(v, prev);
                prev = v;
            }
        }

        [Test]
        public void Smoother01_HasFlatEnds()
        {
            Assert.AreEqual(0f, ShakuMath.Smoother01(0f), 1e-6f);
            Assert.AreEqual(1f, ShakuMath.Smoother01(1f), 1e-6f);
            Assert.Less(ShakuMath.Smoother01(0.05f), 0.01f);
            Assert.Greater(ShakuMath.Smoother01(0.95f), 0.99f);
        }

        [Test]
        public void SmoothStep_HandlesReversedAndEqualEdges()
        {
            Assert.AreEqual(1f, ShakuMath.SmoothStep(1f, 0f, 0f), 1e-6f);
            Assert.AreEqual(0f, ShakuMath.SmoothStep(1f, 0f, 1f), 1e-6f);
            Assert.AreEqual(0f, ShakuMath.SmoothStep(2f, 2f, 1f));
            Assert.AreEqual(1f, ShakuMath.SmoothStep(2f, 2f, 3f));
        }

        [Test]
        public void Bump_IsOneAtCenterAndZeroOutside()
        {
            Assert.AreEqual(1f, ShakuMath.Bump(0f, 5f), 1e-6f);
            Assert.AreEqual(0f, ShakuMath.Bump(5f, 5f), 1e-6f);
            Assert.AreEqual(0f, ShakuMath.Bump(9f, 5f), 1e-6f);
            Assert.That(ShakuMath.Bump(2.5f, 5f), Is.InRange(0.01f, 0.99f));
        }

        [Test]
        public void Hash01_IsDeterministicAndInRange()
        {
            for (int i = 0; i < 200; i++)
            {
                float a = ShakuMath.Hash01(i, i * 3, 7);
                Assert.That(a, Is.InRange(0f, 1f));
                Assert.AreEqual(a, ShakuMath.Hash01(i, i * 3, 7));
            }
            Assert.AreNotEqual(ShakuMath.Hash01(1, 2, 3), ShakuMath.Hash01(1, 2, 4));
        }

        [Test]
        public void Fbm_StaysRoughlyInSignedUnitRange()
        {
            for (int i = 0; i < 400; i++)
            {
                float v = ShakuMath.Fbm(i * 0.37f, i * 0.11f, 4);
                Assert.That(v, Is.InRange(-1.2f, 1.2f));
                Assert.IsFalse(float.IsNaN(v));
            }
        }

        [Test]
        public void DistToSegment_CoversInteriorAndEnds()
        {
            var a = new Vector2(0, 0);
            var b = new Vector2(10, 0);
            Assert.AreEqual(3f, ShakuMath.DistToSegment(new Vector2(5, 3), a, b), 1e-5f);
            Assert.AreEqual(5f, ShakuMath.DistToSegment(new Vector2(-3, 4), a, b), 1e-5f);
            Assert.AreEqual(2f, ShakuMath.DistToSegment(new Vector2(12, 0), a, b), 1e-5f);
            Assert.AreEqual(Mathf.Sqrt(2f), ShakuMath.DistToSegment(new Vector2(1, 1), a, a), 1e-3f, "長さ0の線分");
        }

        [Test]
        public void ProjectOnPlaneSafe_UsesFallbackWhenParallel()
        {
            var fb = new Vector3(1, 0, 0);
            Assert.AreEqual(fb, ShakuMath.ProjectOnPlaneSafe(Vector3.up, Vector3.up, fb));
            var p = ShakuMath.ProjectOnPlaneSafe(new Vector3(0, 1, 1), Vector3.up, fb);
            TestUtil.AssertVector(Vector3.forward, p, 1e-5f);
        }

        [Test]
        public void AnyPerpendicular_IsUnitAndOrthogonal()
        {
            foreach (var n in new[] { Vector3.up, Vector3.down, Vector3.right, new Vector3(0.3f, 0.9f, 0.1f).normalized, Vector3.forward })
            {
                var p = ShakuMath.AnyPerpendicular(n);
                Assert.AreEqual(1f, p.magnitude, 1e-4f);
                Assert.AreEqual(0f, Vector3.Dot(p, n), 1e-4f);
            }
        }

        [Test]
        public void DampFactor_IsFrameRateIndependent()
        {
            float one = ShakuMath.DampFactor(5f, 0.1f);
            float two = 1f - (1f - ShakuMath.DampFactor(5f, 0.05f)) * (1f - ShakuMath.DampFactor(5f, 0.05f));
            Assert.AreEqual(one, two, 1e-5f);
            Assert.AreEqual(0f, ShakuMath.DampFactor(5f, 0f), 1e-6f);
        }
    }
}

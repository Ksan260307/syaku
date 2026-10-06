using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>しゃくとりむしの動きの改善のうち、シーンを使わずに確かめられるもの。</summary>
    public class MovementUnitTests
    {
        [Test]
        public void StepTiming_DependsOnTheSurface()
        {
            float flat = InchwormController.SurfaceTimeScale(Vector3.forward, Vector3.up);
            Assert.AreEqual(1f, flat, 1e-5f);
            Vector3 slopeN = new Vector3(0f, 0.9f, -0.44f).normalized;   // 前が上り坂
            Vector3 upDir = Vector3.ProjectOnPlane(Vector3.forward, slopeN).normalized;
            Assert.Greater(InchwormController.SurfaceTimeScale(upDir, slopeN), 1.1f, "上り坂はゆっくり");
            Assert.Less(InchwormController.SurfaceTimeScale(-upDir, slopeN), 1f, "ゆるい下り坂は少しはやい");
            float wallUp = InchwormController.SurfaceTimeScale(Vector3.up, Vector3.back);
            float wallDown = InchwormController.SurfaceTimeScale(Vector3.down, Vector3.back);
            float ceiling = InchwormController.SurfaceTimeScale(Vector3.forward, Vector3.down);
            Assert.Greater(wallUp, flat, "壁はゆっくり");
            Assert.Greater(wallDown, wallUp, "壁を頭から下りるときは、もっと慎重に");
            Assert.Greater(ceiling, wallDown, "天井はいちばんゆっくり");
        }

        [Test]
        public void StepTiming_StartsSlowAndSettles()
        {
            Assert.Greater(InchwormController.StartupTimeScale(0), InchwormController.StartupTimeScale(1));
            Assert.Greater(InchwormController.StartupTimeScale(1), InchwormController.StartupTimeScale(2));
            Assert.AreEqual(1f, InchwormController.StartupTimeScale(5), 1e-5f, "3 歩目からは本来の速さ");
        }

        [Test]
        public void BodyCurve_RotateAroundKeepsTheShape()
        {
            var c = new BodyCurve(11);
            c.BuildHang(new Vector3(1f, 2f, 3f), Vector3.forward, 1f, 0.5f, 0.1f);
            float before = 0f;
            for (int i = 1; i < c.Count; i++) before += Vector3.Distance(c.pos[i - 1], c.pos[i]);
            Vector3 head = c.Head;
            c.RotateAround(head, Quaternion.AngleAxis(30f, Vector3.right));
            float after = 0f;
            for (int i = 1; i < c.Count; i++) after += Vector3.Distance(c.pos[i - 1], c.pos[i]);
            Assert.AreEqual(before, after, 1e-4f, "体の長さは変わらない");
            TestUtil.AssertVector(head, c.Head, 1e-5f);
            Assert.Greater(Vector3.Distance(c.Tail, head), 0.5f);
            for (int i = 0; i < c.Count; i++) Assert.AreEqual(1f, c.tan[i].magnitude, 1e-3f);
        }
    }
}

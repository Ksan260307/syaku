using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>表面に沿って這う探索（床・壁・崖のふち・プレイ範囲・水）。</summary>
    public class SurfaceProbeTests
    {
        [SetUp]
        public void SetUp()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
        }

        [Test]
        public void Walk_OnFlatFloor_MovesExactDistance()
        {
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10));
            bool ok = SurfaceProbe.Walk(new SurfacePoint(Vector3.zero, Vector3.up), Vector3.forward, 0.8f, out var r, out var dir);
            Assert.IsTrue(ok);
            TestUtil.AssertVector(new Vector3(0, 0, 0.8f), r.point, 0.03f);
            TestUtil.AssertVector(Vector3.up, r.normal, 1e-3f);
            TestUtil.AssertVector(Vector3.forward, dir, 1e-3f);
        }

        [Test]
        public void Walk_IntoWall_ClimbsUpTheWall()
        {
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10));
            TestUtil.Box(new Vector3(0, 2f, 1f), new Vector3(4, 4, 1));   // 手前の面 z = 0.5
            bool ok = SurfaceProbe.Walk(new SurfacePoint(Vector3.zero, Vector3.up), Vector3.forward, 1.0f, out var r, out var dir);
            Assert.IsTrue(ok);
            Assert.AreEqual(0.5f, r.point.z, 0.03f, "壁の面にいる");
            Assert.Greater(r.point.y, 0.3f, "壁を登っている");
            TestUtil.AssertVector(Vector3.back, r.normal, 0.05f);
            Assert.Greater(dir.y, 0.9f, "進む向きは上");
        }

        [Test]
        public void Walk_OverVerticalEdge_IsACliffNotAWrap()
        {
            // 80 度より急なふち（垂直な面）へは回り込まず、「がけ」として止まる（そのまま進めば落ちる）
            TestUtil.Box(new Vector3(0, 0.5f, -1f), new Vector3(4, 1, 3));   // 上面 y=1, 前の面 z=0.5
            bool ok = SurfaceProbe.Walk(new SurfacePoint(new Vector3(0, 1, 0), Vector3.up), Vector3.forward, 1.0f, out var r, out _, out bool offEdge);
            Assert.IsFalse(ok, $"垂直な面に回り込んだ n={r.normal}");
            Assert.IsTrue(offEdge);
            Assert.AreEqual("cliff", SurfaceProbe.LastFail);
        }

        [Test]
        public void Walk_UpAWall_GoesOverTheTopEdge()
        {
            // 下から登ってきたときは、垂直な壁から上の面へ回り込める
            TestUtil.Box(new Vector3(0, 0.5f, -1f), new Vector3(4, 1, 3));   // 前の面 z=0.5
            bool ok = SurfaceProbe.Walk(new SurfacePoint(new Vector3(0, 0.6f, 0.5f), Vector3.forward), Vector3.up, 0.8f, out var r, out _);
            Assert.IsTrue(ok, SurfaceProbe.LastFail);
            Assert.Greater(r.normal.y, 0.9f, "上の面に乗った");
        }

        [Test]
        public void Walk_FailsWhenThereIsNoSurface()
        {
            bool ok = SurfaceProbe.Walk(new SurfacePoint(new Vector3(0, 5, 0), Vector3.up), Vector3.forward, 0.5f, out _, out _);
            Assert.IsFalse(ok);
        }

        [Test]
        public void Walk_StopsAtPlayAreaBoundary()
        {
            float r = ForestLayout.PlayRadius;
            TestUtil.Box(new Vector3(r, -0.5f, 0), new Vector3(10, 1, 10));
            Assert.IsTrue(SurfaceProbe.Walk(new SurfacePoint(new Vector3(r - 1.5f, 0, 0), Vector3.up), Vector3.left, 0.8f, out _, out _), "内側へは進める");
            Assert.IsFalse(SurfaceProbe.Walk(new SurfacePoint(new Vector3(r - 0.4f, 0, 0), Vector3.up), Vector3.right, 0.8f, out _, out _), "外側へは進めない");
        }

        [Test]
        public void Walk_CannotEnterWater()
        {
            Vector2 p = ForestLayout.Pond;
            float y = ForestLayout.WaterLevel - 0.6f;
            TestUtil.Box(new Vector3(p.x, y - 0.5f, p.y), new Vector3(10, 1, 10));
            Assert.IsFalse(SurfaceProbe.Walk(new SurfacePoint(new Vector3(p.x, y, p.y), Vector3.up), Vector3.forward, 0.5f, out _, out _));
        }

        [Test]
        public void Walk_IgnoresOtherLayers()
        {
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10), layer: 0);
            Assert.IsFalse(SurfaceProbe.Walk(new SurfacePoint(Vector3.zero, Vector3.up), Vector3.forward, 0.5f, out _, out _));
        }

        [Test]
        public void Walk_UnderALeaningRock_DoesNotEndUpInsideIt()
        {
            // 坂に置かれた大きな岩：手前の下のはしが地面から 0.3 うき、奥ほど低い（くさび形のすき間）。
            // 「乗りこえる」ために上から探すレイが岩の中から始まっても、岩の中の地面へ入りこまない
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10));
            var rock = TestUtil.Box(new Vector3(0f, 0.878f, 2.5f), new Vector3(3f, 2f, 3f));
            rock.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(new Vector3(0f, 0.01f, 0.9f), Vector3.up, out var under, 1f), "岩の下にすき間がある");
            Assert.AreEqual(0.27f, under.distance, 0.05f);
            var box = rock.GetComponent<Collider>();
            var p = new SurfacePoint(Vector3.zero, Vector3.up);
            for (int i = 0; i < 30; i++)
            {
                if (!SurfaceProbe.Walk(p, Vector3.forward, 0.1f, out var r, out _)) break;
                p = r;
                Vector3 q = p.point + p.normal * 0.02f;
                Assert.AreNotEqual(q, box.ClosestPoint(q), $"岩の中に入った {p.point}");
            }
            Assert.Less(p.point.z, 1.7f, "すき間の奥まではもぐりこまない");
            if (Physics.Raycast(p.point + Vector3.up * 0.01f, Vector3.up, out var head, 1f))
                Assert.GreaterOrEqual(head.distance, 0.09f, "体の太さより低いすき間には入らない");
        }

        [Test]
        public void Walk_ToAStoneWithARaisedRim_ClimbsOnTopInsteadOfUnder()
        {
            // 地面から 0.08 だけういた平たい石（体の太さより低いすき間）：下へもぐらずに、上へ乗る
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10));
            TestUtil.Box(new Vector3(0f, 0.155f, 1.5f), new Vector3(2f, 0.15f, 2f));   // 下の面 y=0.08、上の面 y=0.23、手前の面 z=0.5
            var p = new SurfacePoint(Vector3.zero, Vector3.up);
            for (int i = 0; i < 12; i++)
            {
                Assert.IsTrue(SurfaceProbe.Walk(p, Vector3.forward, 0.1f, out var r, out _), SurfaceProbe.LastFail);
                p = r;
            }
            Assert.Greater(p.point.z, 0.6f);
            Assert.AreEqual(0.23f, p.point.y, 0.02f, "石の上にいる");
        }

        [Test]
        public void Walk_UpASlopeFollowsTheSurface()
        {
            var ramp = TestUtil.Box(new Vector3(0, 0, 2f), new Vector3(4, 0.2f, 6));
            ramp.transform.rotation = Quaternion.Euler(-25f, 0f, 0f);
            Physics.SyncTransforms();
            Assert.IsTrue(SurfaceProbe.Snap(new Vector3(0, 0.5f, 1f), Vector3.up, 4f, out var start));
            bool ok = SurfaceProbe.Walk(start, Vector3.forward, 1f, out var r, out _);
            Assert.IsTrue(ok);
            Assert.Greater(r.point.y, start.point.y + 0.3f, "坂を登る");
            Assert.AreEqual(Vector3.Angle(ramp.transform.up, r.normal), 0f, 2f);
        }

        [Test]
        public void SmoothNormal_InterpolatesMeshNormals()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<SphereCollider>());
            var mc = sphere.AddComponent<MeshCollider>();
            mc.sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            sphere.layer = ShakuConst.SurfaceLayer;
            sphere.transform.localScale = Vector3.one * 4f;
            Physics.SyncTransforms();
            var origin = new Vector3(0.7f, 5f, 0.3f);
            Assert.IsTrue(SurfaceProbe.Raycast(origin, Vector3.down, 10f, out var hit));
            Vector3 radial = hit.point.normalized;
            float smoothErr = Vector3.Angle(radial, SurfaceProbe.SmoothNormal(hit));
            Assert.Less(smoothErr, 3f);
            Assert.LessOrEqual(smoothErr, Vector3.Angle(radial, hit.normal) + 0.5f);
        }

        [Test]
        public void SmoothNormal_FallsBackForPrimitiveColliders()
        {
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10));
            Assert.IsTrue(SurfaceProbe.Raycast(Vector3.up, Vector3.down, 3f, out var hit));
            TestUtil.AssertVector(Vector3.up, SurfaceProbe.SmoothNormal(hit), 1e-4f);
        }

        [Test]
        public void Snap_FindsSurfaceAlongNormal()
        {
            TestUtil.Box(new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10));
            Assert.IsTrue(SurfaceProbe.Snap(new Vector3(1, 0.3f, 1), Vector3.up, 2f, out var r));
            TestUtil.AssertVector(new Vector3(1, 0, 1), r.point, 1e-3f);
            Assert.IsFalse(SurfaceProbe.Snap(new Vector3(1, 30f, 1), Vector3.up, 2f, out _));
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>表面探索のルール：登る・裏側・がけのふち（実際のキノコと切り株のメッシュでも確かめる）。</summary>
    public class ClimbingTests
    {
        [SetUp]
        public void SetUp()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            Areas.Current = Areas.Forest;
        }

        /// <summary>プレイヤーが前に倒しつづけたとき（上向きの面では中心へ、壁では上へ、裏側では今の向きのまま）。</summary>
        static float ClimbToward(GameObject obj, out bool reachedTop, out string why)
        {
            Bounds b = obj.GetComponent<Collider>().bounds;
            float top = b.max.y;
            Vector3 c = b.center;
            var p = new SurfacePoint(new Vector3(c.x - (b.extents.x + 2f), -50f, c.z + 0.3f), Vector3.up);
            Vector3 dir = Vector3.right;
            float best = -999f;
            reachedTop = false;
            why = "";
            for (int i = 0; i < 120; i++)
            {
                Vector3 n = p.normal;
                Vector3 want;
                if (n.y > 0.6f) want = new Vector3(c.x - p.point.x, 0f, c.z - p.point.z);
                else if (n.y > -0.2f) want = Vector3.up;
                else want = dir;
                want = ShakuMath.ProjectOnPlaneSafe(want, n, dir);
                bool ok = SurfaceProbe.Walk(p, want, 0.6f, out var r, out var endDir, out _);
                if (!ok)
                {
                    bool any = false;
                    foreach (float turn in new[] { 25f, -25f, 50f, -50f })
                        if (SurfaceProbe.Walk(p, Quaternion.AngleAxis(turn, n) * want, 0.5f, out r, out endDir, out _)) { any = true; break; }
                    if (!any)
                    {
                        why = SurfaceProbe.LastFail;
                        break;
                    }
                }
                dir = endDir;
                p = r;
                best = Mathf.Max(best, p.point.y + 50f);
                if (p.point.y > top - 0.6f && p.normal.y > 0.6f)
                {
                    reachedTop = true;
                    break;
                }
            }
            return best;
        }

        static GameObject Prop(string mesh)
        {
            TestUtil.Box(new Vector3(0f, -50.5f, 0f), new Vector3(60f, 1f, 60f));
            var assets = TestUtil.LoadWorldAssets();
            var go = new GameObject(mesh) { layer = ShakuConst.SurfaceLayer };
            go.transform.position = new Vector3(0f, -50f, 0f);
            go.AddComponent<MeshCollider>().sharedMesh = assets.TryGet(mesh + "_Col") ?? assets.Get(mesh);
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void Mushrooms_CanBeClimbedToTheTopOfTheCap([Values("Mushroom_Red", "Mushroom_Brown", "Mushroom_Glow")] string mesh)
        {
            var go = Prop(mesh);
            float best = ClimbToward(go, out bool top, out string why);
            Assert.IsTrue(top, $"{mesh}: かさの上までとどかない（最高 {best:F2}, {why}）");
        }

        [Test]
        public void Stump_CanBeClimbedToTheTop()
        {
            var go = Prop("Stump");
            float best = ClimbToward(go, out _, out string why);
            // 切り株の上面（根の盛り上がりをのぞいた高さ）までとどく
            Assert.Greater(best, 8.5f, why);
        }

        [Test]
        public void ClimbableMushrooms_HaveSeamlessColliders()
        {
            var a = TestUtil.LoadWorldAssets();
            foreach (var n in new[] { "Mushroom_Red", "Mushroom_Brown", "Mushroom_Glow" })
            {
                var col = a.TryGet(n + "_Col");
                Assert.IsNotNull(col, n);
                Assert.IsTrue(col.isReadable, n);
                // かさの大きさは見た目と同じくらい
                Assert.AreEqual(a.Get(n).bounds.size.y, col.bounds.size.y, 0.3f, n);
            }
            Assert.IsNull(a.TryGet("NoSuchMesh_Col"));
        }

        [Test]
        public void Ground_CannotStickToTheUndersideOfALowOverhang()
        {
            // 地面からいきなり、低いひさしの裏側（天井）へは乗り移らない
            TestUtil.Box(new Vector3(0f, -0.25f, 0f), new Vector3(8f, 0.5f, 8f));
            TestUtil.Box(new Vector3(1.5f, 0.35f, 0f), new Vector3(1f, 0.2f, 3f));   // 地面から少し浮いた板
            Physics.SyncTransforms();
            var start = new SurfacePoint(new Vector3(0f, 0f, 0f), Vector3.up);
            bool ok = SurfaceProbe.Walk(start, Vector3.right, 1.6f, out var end, out _);
            Assert.IsFalse(ok && end.normal.y < -0.35f, "裏側にはりついた");
        }

        [Test]
        public void Wall_CanEnterACeilingThroughAConcaveCorner()
        {
            Assert.IsTrue(SurfaceProbe.CanEnter(Vector3.right, Vector3.down, true), "柄からかさの裏へは行ける");
            Assert.IsFalse(SurfaceProbe.CanEnter(Vector3.up, Vector3.down, true), "地面から天井へは行かない");
            Assert.IsFalse(SurfaceProbe.CanEnter(Vector3.right, Vector3.down, false), "ふちを回りこんで裏側へは行かない");
            Assert.IsTrue(SurfaceProbe.CanEnter(Vector3.down, Vector3.right, false), "天井からは出られる");
        }

        [Test]
        public void SteepEdge_WalksOffInsteadOfWrappingUnder()
        {
            // 薄い板（葉っぱ）の上から前へ進むと、80 度より急なふちなので回り込まずに「落ちる」と判定される
            TestUtil.Box(new Vector3(0f, 2f, 0f), new Vector3(4f, 0.05f, 4f));
            Physics.SyncTransforms();
            var start = new SurfacePoint(new Vector3(1.6f, 2.025f, 0f), Vector3.up);
            bool ok = SurfaceProbe.Walk(start, Vector3.right, 1.0f, out var end, out _, out bool offEdge);
            Assert.IsFalse(ok, $"裏側へ回り込んだ（n={end.normal}）");
            Assert.IsTrue(offEdge, SurfaceProbe.LastFail);
        }

        [Test]
        public void GentleEdge_IsStillFollowed()
        {
            // 80 度までの坂なら、ふちを回り込んで下りられる
            TestUtil.Box(new Vector3(0f, -0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            var ramp = TestUtil.Box(new Vector3(2.9f, -0.9f, 0f), new Vector3(2.2f, 0.4f, 4f));
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, -40f);
            Physics.SyncTransforms();
            var start = new SurfacePoint(new Vector3(1.6f, 0f, 0f), Vector3.up);
            bool ok = SurfaceProbe.Walk(start, Vector3.right, 1.2f, out var end, out _, out bool offEdge);
            Assert.IsTrue(ok, SurfaceProbe.LastFail);
            Assert.IsFalse(offEdge);
            Assert.Less(end.point.y, -0.05f, "坂を下りた");
        }

        [Test]
        public void SmallStep_IsSteppedDownWithoutFalling()
        {
            // 0.5m くらいの段差は、回り込まずにそのまま下りる
            TestUtil.Box(new Vector3(0f, 0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            TestUtil.Box(new Vector3(4f, -0.25f, 0f), new Vector3(4f, 0.5f, 4f));
            Physics.SyncTransforms();
            var start = new SurfacePoint(new Vector3(1.7f, 0.5f, 0f), Vector3.up);
            Assert.IsTrue(SurfaceProbe.Walk(start, Vector3.right, 1.0f, out var end, out _), SurfaceProbe.LastFail);
            Assert.AreEqual(0f, end.point.y, 0.05f);
            Assert.Greater(end.normal.y, 0.9f);
        }

        [Test]
        public void GlancingWall_IsSlidAlongNotPassedThrough()
        {
            // 柱をかすめても、中に入りこまない
            TestUtil.Box(new Vector3(0f, -0.25f, 0f), new Vector3(8f, 0.5f, 8f));
            var pole = TestUtil.Box(new Vector3(1.2f, 1f, 0.35f), new Vector3(0.5f, 2f, 0.5f));
            Physics.SyncTransforms();
            var start = new SurfacePoint(new Vector3(0f, 0f, 0.05f), Vector3.up);
            if (SurfaceProbe.Walk(start, new Vector3(1f, 0f, 0.05f).normalized, 2f, out var end, out _))
                Assert.IsFalse(pole.GetComponent<Collider>().bounds.Contains(end.point + Vector3.up * 0.02f), "柱の中に入った");
        }
    }
}

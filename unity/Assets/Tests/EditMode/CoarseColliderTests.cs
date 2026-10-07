using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>物の大まかな当たり判定（いちばん外側を、なめらかにつつむ）。</summary>
    public class CoarseColliderTests
    {
        static GameObject ColliderOf(Mesh m, string name = "Coarse")
        {
            var go = new GameObject(name) { layer = ShakuConst.SurfaceLayer };
            go.AddComponent<MeshCollider>().sharedMesh = m;
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void Shapes_AreChosenByName()
        {
            Assert.AreEqual(CoarseCollider.Shape.LatheLongest, CoarseCollider.ShapeOf("Pinecone"));
            Assert.AreEqual(CoarseCollider.Shape.Relax, CoarseCollider.ShapeOf("Stump"));
            Assert.AreEqual(CoarseCollider.Shape.Relax, CoarseCollider.ShapeOf("Rock_A"));
            Assert.AreEqual(CoarseCollider.Shape.Relax, CoarseCollider.ShapeOf("RiverStone_B"));
            Assert.AreEqual(CoarseCollider.Shape.Relax, CoarseCollider.ShapeOf("GreatTree"), "大樹は、アーチの根の穴を残したまま、くぼみだけうめる");
            Assert.AreEqual(CoarseCollider.Shape.Blob, CoarseCollider.ShapeOf("Fern_A"));
            // うすい物・中のくぼみが大事な物は、見た目のとおり
            foreach (var n in new[] { "AcornCap", "Twig_A", "LilyPad", "Leaf_Oak_Green" })
                Assert.IsFalse(CoarseCollider.Wants(n), n);
        }

        [Test]
        public void Pinecone_UsesACoarseSmoothCollider()
        {
            TestUtil.NewEmptyScene();
            Mesh src = TestUtil.LoadWorldAssets().Get("Pinecone");
            Assert.IsNotNull(src);
            Mesh env = CoarseCollider.For("Pinecone", src);
            Assert.AreSame(env, CoarseCollider.For("Pinecone", src), "同じメッシュは作り直さない");
            Assert.Less(env.vertexCount, src.vertexCount, "大まかな形");

            Bounds b = src.bounds;
            Vector3 size = b.size;
            int axis = size.y >= size.x && size.y >= size.z ? 1 : (size.x >= size.z ? 0 : 2);
            Vector3 A = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 U = axis == 1 ? Vector3.right : Vector3.up;
            Vector3 V = Vector3.Cross(A, U);
            for (int k = 0; k < 3; k++)
                Assert.GreaterOrEqual(env.bounds.size[k], size[k] * 0.9f, "見た目をつつむ大きさ");

            var go = ColliderOf(env, "Pinecone");
            foreach (float t in new[] { 0.3f, 0.5f, 0.7f })
            {
                Vector3 c = b.center + A * ((t - 0.5f) * size[axis]);
                float tip = 0f;
                foreach (var v in src.vertices)
                {
                    if (Mathf.Abs(Vector3.Dot(v - c, A)) > size[axis] * 0.04f) continue;
                    Vector3 d = v - c;
                    tip = Mathf.Max(tip, (d - A * Vector3.Dot(d, A)).magnitude);
                }
                Vector3 prevN = Vector3.zero;
                for (int i = 0; i < 36; i++)
                {
                    float ang = i * 10f * Mathf.Deg2Rad;
                    Vector3 dir = U * Mathf.Cos(ang) + V * Mathf.Sin(ang);
                    Vector3 from = c + dir * (tip + 3f);
                    Assert.IsTrue(Physics.Raycast(from, -dir, out var hit, tip + 3f, ShakuConst.SurfaceMask), "まわりから当たる");
                    Assert.GreaterOrEqual((hit.point - c).magnitude, tip * 0.85f, "かさのすき間に入りこまない（かさの先をつつむ）");
                    Vector3 n = SurfaceProbe.SmoothNormal(hit);
                    if (prevN != Vector3.zero) Assert.Less(Vector3.Angle(prevN, n), 25f, "なめらかな面");
                    prevN = n;
                    Assert.Greater(Vector3.Dot(n, dir), 0.5f, "外向きの面");
                }
            }
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Stump_IsSmoothWithoutALidOverItsTop()
        {
            TestUtil.NewEmptyScene();
            Mesh src = TestUtil.LoadWorldAssets().Get("Stump");
            Mesh env = CoarseCollider.For("Stump", src);
            Assert.AreNotSame(src, env);
            Bounds b = src.bounds;
            var exact = ColliderOf(src, "Exact");
            exact.transform.position = Vector3.right * 1000f;
            var coarse = ColliderOf(env, "Stump");
            Physics.SyncTransforms();
            float H = b.size.y;
            // 上の面：まん中の高さは、見た目とほとんど同じ（ふたをかぶせない）
            Vector3 top = new Vector3(b.center.x, b.max.y + 5f, b.center.z);
            Assert.IsTrue(Physics.Raycast(top, Vector3.down, out var hc, H + 10f, ShakuConst.SurfaceMask));
            Assert.IsTrue(Physics.Raycast(top + Vector3.right * 1000f, Vector3.down, out var he, H + 10f, ShakuConst.SurfaceMask));
            Assert.Less(Mathf.Abs(hc.point.y - he.point.y), H * 0.06f, "切り株の上に乗れる高さは、見た目とほぼ同じ");
            // 横の面：樹皮のみぞにはさまらないよう、なめらかで、見た目の外をつつむ
            float midY = b.min.y + H * 0.55f;
            Vector3 prevN = Vector3.zero;
            for (int i = 0; i < 48; i++)
            {
                float a = i * 7.5f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 c = new Vector3(b.center.x, midY, b.center.z);
                Assert.IsTrue(Physics.Raycast(c + dir * (b.extents.x * 3f), -dir, out var hs, b.extents.x * 3f, ShakuConst.SurfaceMask));
                Assert.IsTrue(Physics.Raycast(c + dir * (b.extents.x * 3f) + Vector3.right * 1000f, -dir, out var hx, b.extents.x * 3f, ShakuConst.SurfaceMask));
                Assert.GreaterOrEqual((hs.point - c).magnitude, (hx.point - Vector3.right * 1000f - c).magnitude - b.size.x * 0.005f, "見た目の外をつつむ");
                Vector3 n = SurfaceProbe.SmoothNormal(hs);
                if (prevN != Vector3.zero) Assert.Less(Vector3.Angle(prevN, n), 20f, "みぞのない、なめらかな面");
                prevN = n;
            }
            Object.DestroyImmediate(exact);
            Object.DestroyImmediate(coarse);
        }

        [Test]
        public void Relax_FillsDentsButKeepsTheOuterShape()
        {
            TestUtil.NewEmptyScene();
            var assets = TestUtil.LoadWorldAssets();
            foreach (var name in new[] { "Rock_A", "RiverStone_A" })
            {
                Mesh src = assets.Get(name);
                Assume.That(src != null, name);
                Mesh env = CoarseCollider.For(name, src);
                var go = ColliderOf(env, name);
                Bounds b = src.bounds;
                Vector3 c = b.center;
                // 見た目の点は、ほとんど形の中（つつみこむ）
                int outside = 0, total = 0;
                foreach (var v in src.vertices)
                {
                    Vector3 d = v - c;
                    if (d.sqrMagnitude < 1e-6f) continue;
                    total++;
                    float far = b.size.magnitude * 2f;
                    if (Physics.Raycast(c + d.normalized * far, -d.normalized, out var hit, far, ShakuConst.SurfaceMask)
                        && (hit.point - c).magnitude < d.magnitude * 0.97f) outside++;
                }
                Assert.Less(outside, total * 0.02f, $"{name}: でこぼこの山も、ほとんど形の中");
                // 上の面はなめらか
                Vector3 prevN = Vector3.zero;
                for (int i = 0; i <= 20; i++)
                {
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, 0.2f + 0.6f * i / 20f), b.max.y + 5f, c.z);
                    if (!Physics.Raycast(p, Vector3.down, out var h, b.size.y + 10f, ShakuConst.SurfaceMask)) continue;
                    Vector3 n = SurfaceProbe.SmoothNormal(h);
                    if (prevN != Vector3.zero) Assert.Less(Vector3.Angle(prevN, n), 30f, $"{name}: なめらかな面");
                    prevN = n;
                }
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Lathe_FillsTheGapsOfASpikyShape()
        {
            // とげとげの棒（芯のまわりに、すき間のある板がならぶ）
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 40; i++)
            {
                float y = 0.1f + i * 0.05f;
                float a = i * 2.4f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int s = verts.Count;
                verts.Add(new Vector3(0f, y, 0f));
                verts.Add(o * 0.6f + new Vector3(0f, y, 0f));
                verts.Add(o * 0.6f + new Vector3(0f, y + 0.02f, 0f));
                tris.AddRange(new[] { s, s + 1, s + 2 });
            }
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            Mesh env = CoarseCollider.Build(m, CoarseCollider.Shape.LatheLongest);
            float minR = float.MaxValue;
            foreach (var v in env.vertices)
            {
                if (v.y < 0.6f || v.y > 1.6f) continue;
                float r = new Vector2(v.x - m.bounds.center.x, v.z - m.bounds.center.z).magnitude;
                if (r < 0.05f) continue;   // 上下のはしの中心
                minR = Mathf.Min(minR, r);
            }
            Assert.Greater(minR, 0.5f, "とげのすき間をうめて、とげの先をつなぐ");
            Object.DestroyImmediate(m);
            Object.DestroyImmediate(env);
        }
    }
}

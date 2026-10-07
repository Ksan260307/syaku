using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>細かい凹凸のある物（松ぼっくり）の、大まかな当たり判定。</summary>
    public class CoarseColliderTests
    {
        [Test]
        public void Pinecone_UsesACoarseSmoothCollider()
        {
            Assert.IsTrue(CoarseCollider.Wants("Pinecone"));
            Assert.IsFalse(CoarseCollider.Wants("Stump"), "切り株などは、見た目のとおりの当たり判定");
            TestUtil.NewEmptyScene();
            Mesh src = TestUtil.LoadWorldAssets().Get("Pinecone");
            Assert.IsNotNull(src);
            Mesh env = CoarseCollider.Envelope(src);
            Assert.AreSame(env, CoarseCollider.Envelope(src), "同じメッシュは作り直さない");
            Assert.Less(env.vertexCount, src.vertexCount / 3, "大まかな形");

            // いちばん長い向きが軸（松ぼっくりの芯）
            Bounds b = src.bounds;
            Vector3 size = b.size;
            int axis = size.y >= size.x && size.y >= size.z ? 1 : (size.x >= size.z ? 0 : 2);
            Vector3 A = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 U = axis == 1 ? Vector3.right : Vector3.up;
            Vector3 V = Vector3.Cross(A, U);
            for (int k = 0; k < 3; k++)
                Assert.GreaterOrEqual(env.bounds.size[k], size[k] * 0.9f, "見た目をつつむ大きさ");

            var go = new GameObject("Pinecone") { layer = ShakuConst.SurfaceLayer };
            go.AddComponent<MeshCollider>().sharedMesh = env;
            Physics.SyncTransforms();
            float[] heights = { 0.3f, 0.5f, 0.7f };
            foreach (float t in heights)
            {
                Vector3 c = b.center + A * ((t - 0.5f) * size[axis]);
                // その高さの、見た目のいちばん外（かさの先）
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
                    float r = (hit.point - c).magnitude;
                    Assert.GreaterOrEqual(r, tip * 0.85f, "かさのすき間に入りこまない（かさの先をつつむ）");
                    Vector3 n = SurfaceProbe.SmoothNormal(hit);
                    if (prevN != Vector3.zero) Assert.Less(Vector3.Angle(prevN, n), 25f, "なめらかな面");
                    prevN = n;
                    Assert.Greater(Vector3.Dot(n, dir), 0.5f, "外向きの面");
                }
            }
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Envelope_FillsTheGapsOfASpikyShape()
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
            Mesh env = CoarseCollider.Envelope(m, 12, 16);
            Assert.AreEqual(2 + 12 * 16, env.vertexCount);
            float minR = float.MaxValue;
            foreach (var v in env.vertices)
            {
                if (v.y < 0.6f || v.y > 1.6f) continue;
                minR = Mathf.Min(minR, new Vector2(v.x, v.z).magnitude);
            }
            Assert.Greater(minR, 0.5f, "とげのすき間をうめて、とげの先をつなぐ");
            Object.DestroyImmediate(m);
            Object.DestroyImmediate(env);
        }
    }
}

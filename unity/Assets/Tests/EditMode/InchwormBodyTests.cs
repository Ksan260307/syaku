using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>Blender のメッシュを体の中心線に沿って曲げる変形のテスト。</summary>
    public class InchwormBodyTests
    {
        Mesh _src;
        GameObject _go;
        InchwormBody _body;

        [SetUp]
        public void SetUp()
        {
            TestUtil.NewEmptyScene();
            _src = TestUtil.LoadWorldAssets().Get("Inchworm");
            Assert.IsNotNull(_src);
            _go = new GameObject("worm");
            _go.AddComponent<MeshFilter>().sharedMesh = _src;
            _go.AddComponent<MeshRenderer>();
            _body = _go.AddComponent<InchwormBody>();
        }

        /// <summary>取り込み時と同じ向き（尾 z=0 → 頭 z=-1、背中 +Y）のまっすぐな線。</summary>
        static BodyCurve RestCurve(int n)
        {
            var c = new BodyCurve(n);
            for (int i = 0; i < n; i++)
            {
                c.pos[i] = new Vector3(0f, 0f, -i / (float)(n - 1));
                c.tan[i] = Vector3.back;
                c.up[i] = Vector3.up;
            }
            return c;
        }

        [Test]
        public void SourceMesh_HasDeformUVs()
        {
            var uv1 = new System.Collections.Generic.List<Vector2>();
            _src.GetUVs(1, uv1);
            Assert.AreEqual(_src.vertexCount, uv1.Count);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var u in uv1) { min = Mathf.Min(min, u.x); max = Mathf.Max(max, u.x); }
            Assert.That(min, Is.InRange(-0.05f, 0.02f), "尾の s ≈ 0");
            Assert.That(max, Is.InRange(0.98f, 1.06f), "頭の s ≈ 1");
        }

        [Test]
        public void StraightCurve_ReproducesRestMesh()
        {
            var c = RestCurve(41);
            _body.Apply(c);
            var deformed = _go.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreNotSame(_src, deformed, "元のアセットは書き換えない");
            var dv = deformed.vertices;
            var sv = _src.vertices;
            var dn = deformed.normals;
            var sn = _src.normals;
            Vector3 offset = _go.transform.position;
            float maxErr = 0f;
            float minDot = 1f;
            for (int i = 0; i < sv.Length; i++)
            {
                maxErr = Mathf.Max(maxErr, Vector3.Distance(sv[i], dv[i] + offset));
                minDot = Mathf.Min(minDot, Vector3.Dot(sn[i].normalized, dn[i].normalized));
            }
            Assert.Less(maxErr, 2e-3f, "まっすぐなら元の形と一致（左右反転の判定も含めて正しい）");
            Assert.Greater(minDot, 0.97f, "法線も一致");
        }

        [Test]
        public void ArchCurve_KeepsVerticesNearTheCenterline()
        {
            var c = new BodyCurve(41);
            c.BuildArch(Vector3.zero, Vector3.right * 0.35f, Vector3.up, 1f, 0f, 0f, 0f, 0f);
            _body.Apply(c);
            var mesh = _go.GetComponent<MeshFilter>().sharedMesh;
            Vector3 offset = _go.transform.position;
            foreach (var v in mesh.vertices)
            {
                Vector3 w = v + offset;
                float best = float.MaxValue;
                foreach (var p in c.pos) best = Mathf.Min(best, Vector3.Distance(w, p));
                Assert.Less(best, 0.12f, $"頂点 {w} が体から離れすぎ");
            }
        }

        [Test]
        public void Bounds_CoverDeformedMesh()
        {
            var c = new BodyCurve(41);
            c.BuildRear(Vector3.zero, Vector3.forward, Vector3.up, 1f, 1f, 0.5f, 0.5f);
            _body.Apply(c);
            var mesh = _go.GetComponent<MeshFilter>().sharedMesh;
            var b = mesh.bounds;
            foreach (var v in mesh.vertices)
                Assert.IsTrue(b.Contains(v) || b.SqrDistance(v) < 1e-4f, $"{v} がバウンディングボックス外");
        }

        [Test]
        public void Deformation_IsComputedOncePerRing()
        {
            _body.Apply(RestCurve(41));
            Assert.Greater(_body.RingCount, 10);
            Assert.Less(_body.RingCount, _src.vertexCount / 4, "向きの計算は、頂点ごとではなく、体の輪ごとに 1 回（速い）");
        }

        [Test]
        public void DifferentSampleCounts_AreSupported()
        {
            _body.Apply(RestCurve(41));
            Assert.DoesNotThrow(() => _body.Apply(RestCurve(21)));
            Assert.DoesNotThrow(() => _body.Apply(RestCurve(81)));
        }
    }
}

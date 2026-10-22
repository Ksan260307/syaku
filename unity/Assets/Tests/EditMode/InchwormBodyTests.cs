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

        Vector3[] Deformed()
        {
            var m = _go.GetComponent<MeshFilter>().sharedMesh;
            return m.vertices;
        }

        [Test]
        public void SmallParts_AreFound_EyesHighlightsAntennaeAndLegs()
        {
            _body.Apply(RestCurve(41));
            Assert.Greater(_body.PartCount(InchwormBody.Part.Eye), 60, "目（左右）");
            Assert.Greater(_body.PartCount(InchwormBody.Part.Highlight), 20, "目の光");
            Assert.Greater(_body.PartCount(InchwormBody.Part.Antenna), 6, "触角");
            Assert.Greater(_body.PartCount(InchwormBody.Part.ChestLeg), 60, "胸脚 3 対");
            Assert.Greater(_body.PartCount(InchwormBody.Part.Proleg), 40, "腹脚と尾脚");
            Assert.Greater(_body.PartCount(InchwormBody.Part.Body), _src.vertexCount / 2, "ほとんどは、体の輪");
        }

        /// <summary>その部品の頂点の、背中の向き（y）の広がり。</summary>
        float Height(Vector3[] v, InchwormBody.Part part)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                if (_body.PartOf(i) != part) continue;
                lo = Mathf.Min(lo, v[i].y);
                hi = Mathf.Max(hi, v[i].y);
            }
            return hi - lo;
        }

        [Test]
        public void Blink_ClosesTheEyesToAThinLine_AndHidesTheHighlights()
        {
            _body.Apply(RestCurve(41));
            var open = Deformed();
            float h0 = Height(open, InchwormBody.Part.Eye);
            _body.blink = 1f;
            _body.Apply(RestCurve(41));
            var shut = Deformed();
            float h1 = Height(shut, InchwormBody.Part.Eye);
            Assert.Less(h1, h0 * 0.25f, "目をとじると、横一本の線に");
            Assert.Less(Height(shut, InchwormBody.Part.Highlight), Height(open, InchwormBody.Part.Highlight) * 0.5f, "光は目の中へかくれる");
            // 体の輪は、まばたきで動かない
            for (int i = 0; i < open.Length; i++)
                if (_body.PartOf(i) == InchwormBody.Part.Body) Assert.Less(Vector3.Distance(open[i], shut[i]), 1e-5f);
        }

        [Test]
        public void Gaze_MovesTheHighlights_LegGripSpreadsTheLegs()
        {
            _body.Apply(RestCurve(41));
            var a = Deformed();
            _body.gaze = new Vector2(1f, 0f);
            _body.tailGrip = 1f;
            _body.headGrip = 1f;
            _body.Apply(RestCurve(41));
            var b = Deformed();
            float hlMove = 0f, legMove = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                var part = _body.PartOf(i);
                float d = Vector3.Distance(a[i], b[i]);
                if (part == InchwormBody.Part.Highlight) hlMove = Mathf.Max(hlMove, d);
                if (part == InchwormBody.Part.Proleg || part == InchwormBody.Part.ChestLeg) legMove = Mathf.Max(legMove, d);
                if (part == InchwormBody.Part.Body || part == InchwormBody.Part.Eye) Assert.Less(d, 1e-5f, "目と体は動かない");
            }
            Assert.Greater(hlMove, 0.003f, "目の光が、見る方へ動く");
            Assert.Greater(legMove, 0.002f, "脚先が、ぺたっと広がる");
        }

        [Test]
        public void SegmentWaves_BulgeAndStretchTheBody_WithoutChangingItsLength()
        {
            _body.Apply(RestCurve(41));
            var a = Deformed();
            _body.segmentBulge = 0.06f;
            _body.segmentStretch = 0.3f;
            _body.segmentPhase = 1f;
            _body.Apply(RestCurve(41));
            var b = Deformed();
            float moved = 0f, tailMove = 0f, headMove = 0f;
            float minZ0 = float.MaxValue, maxZ0 = float.MinValue, minZ1 = float.MaxValue, maxZ1 = float.MinValue;
            for (int i = 0; i < a.Length; i++)
            {
                moved = Mathf.Max(moved, Vector3.Distance(a[i], b[i]));
                minZ0 = Mathf.Min(minZ0, a[i].z); maxZ0 = Mathf.Max(maxZ0, a[i].z);
                minZ1 = Mathf.Min(minZ1, b[i].z); maxZ1 = Mathf.Max(maxZ1, b[i].z);
            }
            Assert.Greater(moved, 0.002f, "体節が、ふくらんだり、寄ったりする");
            Assert.AreEqual(maxZ0 - minZ0, maxZ1 - minZ1, 0.002f, "体の長さは、そのまま（頭と尾の先は動かない）");
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

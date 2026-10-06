using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// Blender で作った「まっすぐな」しゃくとりむしのメッシュを、体の中心線（BodyCurve）に沿って曲げる。
    /// 変形に必要な情報（体のどこか s・断面内の位置・法線）は Blender 側で UV1〜UV3 に書き込んである。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class InchwormBody : MonoBehaviour
    {
        public float lengthScale = 1f;

        Mesh _mesh;
        Vector3[] _verts;
        Vector3[] _norms;
        int[] _i0;
        float[] _t;
        float[] _ox, _oy, _nf, _ns, _nu;
        float _sideSign = 1f;
        int _samples = -1;
        float[] _s;
        bool _ready;

        public void Init(int samples)
        {
            var mf = GetComponent<MeshFilter>();
            Mesh src = mf.sharedMesh;
            if (src == null) return;
            if (_mesh == null)
            {
                _mesh = Instantiate(src);
                _mesh.name = "InchwormDeformed";
                _mesh.MarkDynamic();
                mf.sharedMesh = _mesh;
            }
            int n = src.vertexCount;
            var uv1 = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var uv3 = new List<Vector2>();
            src.GetUVs(1, uv1);
            src.GetUVs(2, uv2);
            src.GetUVs(3, uv3);
            if (uv1.Count != n || uv2.Count != n || uv3.Count != n)
            {
                Debug.LogError("[InchwormBody] 変形用 UV がありません。Blender の出力を確認してください。");
                return;
            }
            _verts = new Vector3[n];
            _norms = new Vector3[n];
            _s = new float[n];
            _ox = new float[n];
            _oy = new float[n];
            _nf = new float[n];
            _ns = new float[n];
            _nu = new float[n];
            for (int i = 0; i < n; i++)
            {
                _s[i] = uv1[i].x;
                _nf[i] = uv1[i].y;
                _ox[i] = uv2[i].x;
                _oy[i] = uv2[i].y;
                _ns[i] = uv3[i].x;
                _nu[i] = uv3[i].y;
            }

            // FBX の座標変換で左右が反転していないかを「符号付き体積」で判定する
            int[] tris = src.triangles;
            float vOrig = SignedVolume(src.vertices, tris);
            var rec = new Vector3[n];
            for (int i = 0; i < n; i++) rec[i] = new Vector3(_ox[i], _oy[i], _s[i]);
            float vRec = SignedVolume(rec, tris);
            _sideSign = Mathf.Sign(vOrig) == Mathf.Sign(vRec) ? 1f : -1f;

            SetSamples(samples);
            _ready = true;
        }

        void SetSamples(int samples)
        {
            _samples = samples;
            int K = samples - 1;
            int n = _s.Length;
            _i0 = new int[n];
            _t = new float[n];
            for (int i = 0; i < n; i++)
            {
                float f = _s[i] * K;
                int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, K - 1);
                _i0[i] = i0;
                _t[i] = f - i0;
            }
        }

        static float SignedVolume(Vector3[] v, int[] t)
        {
            double vol = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                vol += Vector3.Dot(a, Vector3.Cross(b, c));
            }
            return (float)vol;
        }

        public void Apply(BodyCurve curve)
        {
            if (!_ready) Init(curve.Count);
            if (!_ready) return;
            if (curve.Count != _samples) SetSamples(curve.Count);

            Vector3 center = curve.Middle;
            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = Vector3.one;
            float L = lengthScale;
            var P = curve.pos;
            var T = curve.tan;
            var U = curve.up;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            for (int k = 0; k < P.Length; k++)
            {
                min = Vector3.Min(min, P[k]);
                max = Vector3.Max(max, P[k]);
            }
            int n = _verts.Length;
            for (int i = 0; i < n; i++)
            {
                int a = _i0[i];
                float t = _t[i];
                Vector3 p = Vector3.LerpUnclamped(P[a], P[a + 1], t);
                Vector3 tn = Vector3.Lerp(T[a], T[a + 1], Mathf.Clamp01(t));
                Vector3 up = Vector3.Lerp(U[a], U[a + 1], Mathf.Clamp01(t));
                tn.Normalize();
                up = (up - tn * Vector3.Dot(up, tn)).normalized;
                Vector3 side = Vector3.Cross(up, tn) * _sideSign;
                _verts[i] = p + (side * _ox[i] + up * _oy[i]) * L - center;
                _norms[i] = side * _ns[i] + up * _nu[i] + tn * _nf[i];
            }
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_norms);
            var b = new Bounds((min + max) * 0.5f - center, (max - min) + Vector3.one * 0.3f * L);
            _mesh.bounds = b;
        }
    }
}

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
        // 同じ体の位置（輪）の頂点をまとめて、向きの計算を輪ごとに 1 回だけにする
        int[] _ring;
        int[] _ringA;
        float[] _ringT;
        Vector3[] _rp, _rs, _ru, _rt;

        /// <summary>輪の数（テスト用）。</summary>
        public int RingCount => _ringA != null ? _ringA.Length : 0;

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
            // 同じ体の位置の頂点（体の輪・目のまわりなど）をまとめる
            var map = new Dictionary<int, int>();
            var ra = new List<int>();
            var rt = new List<float>();
            _ring = new int[n];
            for (int i = 0; i < n; i++)
            {
                int key = Mathf.RoundToInt(_s[i] * 100000f);
                if (!map.TryGetValue(key, out int r))
                {
                    r = ra.Count;
                    map[key] = r;
                    ra.Add(_i0[i]);
                    rt.Add(_t[i]);
                }
                _ring[i] = r;
            }
            _ringA = ra.ToArray();
            _ringT = rt.ToArray();
            _rp = new Vector3[_ringA.Length];
            _rs = new Vector3[_ringA.Length];
            _ru = new Vector3[_ringA.Length];
            _rt = new Vector3[_ringA.Length];
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
            // 輪ごとに、中心線の位置と向き（前・上・横）を 1 回だけ計算する
            int rings = _ringA.Length;
            for (int r = 0; r < rings; r++)
            {
                int a = _ringA[r];
                float t = _ringT[r];
                Vector3 p = Vector3.LerpUnclamped(P[a], P[a + 1], t);
                Vector3 tn = Vector3.Lerp(T[a], T[a + 1], Mathf.Clamp01(t));
                Vector3 up = Vector3.Lerp(U[a], U[a + 1], Mathf.Clamp01(t));
                tn.Normalize();
                up = (up - tn * Vector3.Dot(up, tn)).normalized;
                Vector3 side = Vector3.Cross(up, tn) * _sideSign;
                _rp[r] = p - center;
                _rs[r] = side;
                _ru[r] = up;
                _rt[r] = tn;
            }
            // 頂点は、輪の向きに、断面の中の位置を足すだけ
            int n = _verts.Length;
            for (int i = 0; i < n; i++)
            {
                int r = _ring[i];
                Vector3 side = _rs[r], up = _ru[r];
                float ox = _ox[i] * L, oy = _oy[i] * L;
                _verts[i] = new Vector3(_rp[r].x + side.x * ox + up.x * oy, _rp[r].y + side.y * ox + up.y * oy, _rp[r].z + side.z * ox + up.z * oy);
                float ns = _ns[i], nu = _nu[i], nf = _nf[i];
                Vector3 tn = _rt[r];
                _norms[i] = new Vector3(side.x * ns + up.x * nu + tn.x * nf, side.y * ns + up.y * nu + tn.y * nf, side.z * ns + up.z * nu + tn.z * nf);
            }
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_norms);
            var b = new Bounds((min + max) * 0.5f - center, (max - min) + Vector3.one * 0.3f * L);
            _mesh.bounds = b;
        }
    }
}

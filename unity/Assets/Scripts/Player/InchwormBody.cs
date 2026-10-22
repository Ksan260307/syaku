using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// Blender で作った「まっすぐな」しゃくとりむしのメッシュを、体の中心線（BodyCurve）に沿って曲げる。
    /// 変形に必要な情報（体のどこか s・断面内の位置・法線）は Blender 側で UV1〜UV3 に書き込んである。
    /// 体の輪（同じ s に 20 個以上の頂点）と、輪ではない小さな部品（目・目の光・触角・胸脚・腹脚）を見分けて、
    /// まばたき・目線・触角のゆれ・脚のふんばり・脚のこぎ・体節のふくらみと伸び縮み を、頂点を動かしてえがく。
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

        /// <summary>頭のまん中の、体の位置 s（Blender の make_inchworm の HEAD_C）と、せなかの方への高さ。</summary>
        public const float HeadS = 0.935f, HeadLift = 0.007f;
        /// <summary>頭のまん中・頭の前・せなかの向き（ぼうしやめがねをのせる。Apply のたびに新しくなる）。</summary>
        public Vector3 HeadCenter { get; private set; }
        public Vector3 HeadForward { get; private set; } = Vector3.forward;
        public Vector3 HeadUp { get; private set; } = Vector3.up;
        /// <summary>体の大きさ（ぼうしやめがねも、同じだけ大きくする）。</summary>
        public float Scale => lengthScale;
        /// <summary>Apply した回数（頭の向きが新しくなったか）。</summary>
        public int Applied { get; private set; }

        // ---- 表情と体の動き（コントローラーが毎フレーム決める。0 なら、もとの形のまま） ----
        /// <summary>まぶたの閉じぐあい（0 = 開いている、1 = 閉じている）。</summary>
        public float blink;
        /// <summary>目線（-1〜1。x = 右、y = 上）。目の光を、見る方へずらす。</summary>
        public Vector2 gaze;
        /// <summary>触角のゆれ（-1〜1。x = 右、y = 上）。先ほど大きく動く。</summary>
        public Vector2 antenna;
        /// <summary>腹脚（尾の方）・胸脚（頭の方）のふんばり（0〜1）：脚先を面へおしつけて、ぺたっと広げる。</summary>
        public float tailGrip, headGrip;
        /// <summary>胸脚のこぎ（0〜1）と、その位相（糸をたぐる・泳ぐ）。</summary>
        public float legPaddle, legPhase;
        /// <summary>脚をちぢめて、細い物をはさむ（0〜1）。</summary>
        public float legPinch;
        /// <summary>体節のふくらみの波（0〜0.1 くらい）と、伸び縮みの波（0〜0.4 くらい）と、その位相（ラジアン）。</summary>
        public float segmentBulge, segmentStretch, segmentPhase;

        /// <summary>体の部品（テスト用）。</summary>
        public enum Part : byte { Body, Eye, Highlight, Antenna, ChestLeg, Proleg }
        Part[] _part;
        float[] _eyeX, _eyeY;                   // 目のまん中（左 0・右 1）
        float[] _partW;                         // 触角：付け根からの割合。脚：脚先ほど 1
        byte[] _legIndex;                       // 胸脚の何番目か（こぎの位相をずらす）
        bool[] _ringIsBody;
        float[] _ringS;

        /// <summary>体の節のはじめと終わりと長さ（Blender の S0・S1・SEGLEN）。</summary>
        public const float SegS0 = 0.04f, SegS1 = 0.88f, SegLen = (SegS1 - SegS0) / 13f;

        // 口（頭の先の、下がわの茶色い所）のまん中と向き（ふつうの形のとき）
        float _mouthS = -1f, _mouthX, _mouthY, _mouthNf, _mouthNs, _mouthNu;
        /// <summary>口のまん中と、口の向き（WormWear が、あいた口をえがく）。</summary>
        public Vector3 MouthCenter { get; private set; }
        public Vector3 MouthNormal { get; private set; } = Vector3.forward;

        /// <summary>その部品の頂点の数（テスト用）。</summary>
        public int PartCount(Part p)
        {
            if (_part == null) return 0;
            int n = 0;
            foreach (var q in _part) if (q == p) n++;
            return n;
        }

        /// <summary>i 番目の頂点が、どの部品か（テスト用）。</summary>
        public Part PartOf(int i) => _part[i];

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
            _ringS = new float[_ringA.Length];
            for (int i = 0; i < n; i++) _ringS[_ring[i]] = _s[i];
            Classify();
        }

        /// <summary>
        /// 体の輪（同じ s の頂点が 20 個以上）と、小さな部品を見分ける。部品は、体のどこにあるか（s）と、背中か腹か（oy）で分ける。
        /// </summary>
        void Classify()
        {
            int n = _s.Length;
            var count = new int[_ringA.Length];
            for (int i = 0; i < n; i++) count[_ring[i]]++;
            _ringIsBody = new bool[_ringA.Length];
            // 体の輪は、まわりに 22 個（と、つなぎ目の分）。目の光の球の輪は、左右あわせて 16 個まで
            for (int r = 0; r < _ringA.Length; r++) _ringIsBody[r] = count[r] >= 20;
            _part = new Part[n];
            _partW = new float[n];
            _legIndex = new byte[n];
            var src = GetComponent<MeshFilter>().sharedMesh;
            var colors = src != null ? src.colors : null;
            bool hasColors = colors != null && colors.Length == n;
            _eyeX = new float[2];
            _eyeY = new float[2];
            var eyeN = new int[2];
            for (int i = 0; i < n; i++)
            {
                float s = _s[i], oy = _oy[i];
                // 目と目の光は、色で見分ける（体には、まっ白や、こい紺色の所はない。光の球の輪が、体の輪と同じ s になることもある）
                bool head = s > 0.9f && oy > 0.005f;
                if (_ringIsBody[_ring[i]] && !(head && hasColors && (IsWhite(colors[i]) || IsDark(colors[i])))) continue;
                if (head)
                {
                    Color c = hasColors ? colors[i] : Color.black;
                    bool white = IsWhite(c);
                    bool dark = IsDark(c);
                    if (!white && !dark) continue;   // 頭の先の点など（体の色）は、目ではない
                    _part[i] = white ? Part.Highlight : Part.Eye;
                    if (!white)
                    {
                        int e = EyeOf(_ox[i]);
                        _eyeX[e] += _ox[i];
                        _eyeY[e] += oy;
                        eyeN[e]++;
                    }
                }
                else if (s > 0.955f && oy < 0f)
                {
                    _part[i] = Part.Antenna;
                    _partW[i] = Mathf.Clamp01((s - 0.975f) / 0.02f);
                }
                else if (s > 0.65f && s < 0.9f && oy < 0f)
                {
                    _part[i] = Part.ChestLeg;
                    _legIndex[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((s - (SegS0 + SegLen * 10.5f)) / SegLen), 0, 2);
                    _partW[i] = Mathf.Clamp01((-oy - 0.02f) / 0.025f);
                }
                else if (s < 0.42f && oy < 0f)
                {
                    _part[i] = Part.Proleg;
                    _partW[i] = Mathf.Clamp01((-oy - 0.02f) / 0.03f);
                }
            }
            for (int e = 0; e < 2; e++)
            {
                if (eyeN[e] == 0) continue;
                _eyeX[e] /= eyeN[e];
                _eyeY[e] /= eyeN[e];
            }
            // 口：頭の先の、下がわの茶色い所（赤みがあって、青の少ない色。ほっぺの桃色は青が多い）
            if (hasColors)
            {
                float ss = 0f, sx = 0f, sy = 0f, nf = 0f, nsd = 0f, nu = 0f;
                int mn = 0;
                for (int i = 0; i < n; i++)
                {
                    if (_part[i] != Part.Body || _s[i] < 0.955f || _oy[i] > 0f) continue;
                    Color c = colors[i];
                    if (c.r <= c.g || c.b > 0.4f) continue;
                    ss += _s[i]; sx += _ox[i]; sy += _oy[i]; nf += _nf[i]; nsd += _ns[i]; nu += _nu[i];
                    mn++;
                }
                if (mn > 0)
                {
                    _mouthS = ss / mn; _mouthX = sx / mn; _mouthY = sy / mn;
                    _mouthNf = nf / mn; _mouthNs = nsd / mn; _mouthNu = nu / mn;
                }
            }
        }

        int EyeOf(float ox) => ox * _sideSign >= 0f ? 1 : 0;

        static bool IsWhite(Color c) => c.r > 0.8f && c.g > 0.8f && c.b > 0.8f;
        static bool IsDark(Color c) => c.r + c.g + c.b < 1.1f;

        /// <summary>i 番目の頂点の、断面の中の位置（ox・oy）に、表情と体の動きを足す。</summary>
        void Feature(int i, ref float ox, ref float oy)
        {
            switch (_part[i])
            {
                case Part.Body:
                    if (Mathf.Abs(segmentBulge) > 1e-4f)
                    {
                        // 体節のふくらみ：節ごとに、少し太く・細く（体の中の水が動く）
                        float s = _s[i];
                        if (s > SegS0 && s < SegS1)
                        {
                            float f = 1f + segmentBulge * Mathf.Sin(2f * Mathf.PI * (s - SegS0) / SegLen - segmentPhase);
                            ox *= f;
                            oy *= f;
                        }
                    }
                    break;
                case Part.Eye:
                {
                    // まばたき：目を上下からつぶして、横一本の線に
                    int e = EyeOf(_ox[i]);
                    oy = _eyeY[e] + (oy - _eyeY[e]) * (1f - 0.9f * Mathf.Clamp01(blink));
                    break;
                }
                case Part.Highlight:
                {
                    int e = EyeOf(_ox[i]);
                    // 目線：光を、見る方へずらす。まばたきで目をつぶると、光は目の中へかくれる
                    ox += Mathf.Clamp(gaze.x, -1f, 1f) * 0.006f * _sideSign;
                    oy += Mathf.Clamp(gaze.y, -1f, 1f) * 0.005f;
                    // （光は目より前に出ているので、頭の中へしまう）
                    float hide = Mathf.Clamp01((blink - 0.25f) / 0.35f);
                    ox = Mathf.Lerp(ox, _eyeX[e] * 0.3f, hide);
                    oy = Mathf.Lerp(oy, _eyeY[e] * 0.3f, hide);
                    break;
                }
                case Part.Antenna:
                {
                    float w = _partW[i];
                    ox += Mathf.Clamp(antenna.x, -1f, 1f) * 0.007f * w * _sideSign;
                    oy += Mathf.Clamp(antenna.y, -1f, 1f) * 0.006f * w;
                    break;
                }
                case Part.ChestLeg:
                case Part.Proleg:
                {
                    float w = _partW[i];
                    float grip = _part[i] == Part.ChestLeg ? headGrip : tailGrip;
                    // ふんばり：脚先を面へおしつけて、横へ広げる。細い物の上では、脚をちぢめてはさむ
                    ox *= 1f + 0.14f * grip * w - 0.25f * legPinch * w;
                    oy -= 0.003f * grip * w;
                    if (_part[i] == Part.ChestLeg && legPaddle > 1e-3f)
                    {
                        // こぐ：胸脚を、1 対ずつずらして上げ下げ（左右も、たがいちがい）
                        float ph = legPhase + _legIndex[i] * 2.1f + (_ox[i] * _sideSign > 0f ? Mathf.PI : 0f);
                        oy += 0.006f * legPaddle * w * Mathf.Sin(ph);
                        ox *= 1f + 0.08f * legPaddle * w * Mathf.Cos(ph);
                    }
                    break;
                }
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
            // 輪ごとに、中心線の位置と向き（前・上・横）を 1 回だけ計算する
            int rings = _ringA.Length;
            int Kc = P.Length - 1;
            bool stretch = Mathf.Abs(segmentStretch) > 1e-4f;
            for (int r = 0; r < rings; r++)
            {
                int a = _ringA[r];
                float t = _ringT[r];
                if (stretch)
                {
                    // 体節の伸び縮み：節ごとに、輪を少し寄せたり、はなしたりする（体の長さは、そのまま）
                    float rs = _ringS[r];
                    if (rs > SegS0 && rs < SegS1)
                    {
                        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SegS0, SegS0 + SegLen, rs)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SegS1, SegS1 - SegLen, rs));
                        float s2 = rs + segmentStretch * k * SegLen / (2f * Mathf.PI) * Mathf.Sin(2f * Mathf.PI * (rs - SegS0) / SegLen - segmentPhase);
                        float f2 = Mathf.Clamp01(s2) * Kc;
                        a = Mathf.Clamp(Mathf.FloorToInt(f2), 0, Kc - 1);
                        t = f2 - a;
                    }
                }
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
            // 頂点は、輪の向きに、断面の中の位置を足すだけ（表情と体の動きは、断面の中の位置を少し動かす）
            int n = _verts.Length;
            bool features = blink > 1e-3f || gaze.sqrMagnitude > 1e-6f || antenna.sqrMagnitude > 1e-6f || tailGrip > 1e-3f || headGrip > 1e-3f
                            || legPaddle > 1e-3f || legPinch > 1e-3f || Mathf.Abs(segmentBulge) > 1e-4f;
            for (int i = 0; i < n; i++)
            {
                int r = _ring[i];
                Vector3 side = _rs[r], up = _ru[r];
                float ox = _ox[i], oy = _oy[i];
                if (features) Feature(i, ref ox, ref oy);
                ox *= L;
                oy *= L;
                _verts[i] = new Vector3(_rp[r].x + side.x * ox + up.x * oy, _rp[r].y + side.y * ox + up.y * oy, _rp[r].z + side.z * ox + up.z * oy);
                float ns = _ns[i], nu = _nu[i], nf = _nf[i];
                Vector3 tn = _rt[r];
                _norms[i] = new Vector3(side.x * ns + up.x * nu + tn.x * nf, side.y * ns + up.y * nu + tn.y * nf, side.z * ns + up.z * nu + tn.z * nf);
            }
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_norms);
            // 頭のまん中と向き（ぼうし・めがね用）
            {
                int K = P.Length - 1;
                float f = HeadS * K;
                int a = Mathf.Clamp(Mathf.FloorToInt(f), 0, K - 1);
                float t = f - a;
                Vector3 tn = Vector3.Lerp(T[a], T[a + 1], t).normalized;
                Vector3 up = Vector3.Lerp(U[a], U[a + 1], t);
                up = (up - tn * Vector3.Dot(up, tn)).normalized;
                if (ShakuPhysics.IsFinite(tn) && ShakuPhysics.IsFinite(up) && tn.sqrMagnitude > 0.5f && up.sqrMagnitude > 0.5f)
                {
                    HeadForward = tn;
                    HeadUp = up;
                    HeadCenter = Vector3.LerpUnclamped(P[a], P[a + 1], t) + up * (HeadLift * L);
                }
                Applied++;
            }
            // 口のまん中と向き
            if (_mouthS > 0f)
            {
                int K = P.Length - 1;
                float f = _mouthS * K;
                int a = Mathf.Clamp(Mathf.FloorToInt(f), 0, K - 1);
                float t = f - a;
                Vector3 tn = Vector3.Lerp(T[a], T[a + 1], t).normalized;
                Vector3 up = Vector3.Lerp(U[a], U[a + 1], t);
                up = (up - tn * Vector3.Dot(up, tn)).normalized;
                Vector3 side = Vector3.Cross(up, tn) * _sideSign;
                Vector3 c = Vector3.LerpUnclamped(P[a], P[a + 1], t) + side * (_mouthX * L) + up * (_mouthY * L);
                Vector3 nrm = (side * _mouthNs + up * _mouthNu + tn * _mouthNf).normalized;
                if (ShakuPhysics.IsFinite(c) && ShakuPhysics.IsFinite(nrm) && nrm.sqrMagnitude > 0.5f)
                {
                    MouthCenter = c;
                    MouthNormal = nrm;
                }
            }
            var b = new Bounds((min + max) * 0.5f - center, (max - min) + Vector3.one * 0.3f * L);
            _mesh.bounds = b;
        }
    }
}

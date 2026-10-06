using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしの体の中心線（尾 → 頭）。各サンプルに位置・接線・背中方向を持つ。
    /// 体を Ω 字に持ち上げる「尺取り」のアーチは、接線角 θ(u) = a·sin(2πu) の曲線（弾性体に近い形）で作る。
    /// このとき両端の距離 D と体長 L の比は第1種ベッセル関数 J0(a) になるので、表を引いて a を求める。
    /// </summary>
    public class BodyCurve
    {
        public readonly int Count;
        public readonly Vector3[] pos;
        public readonly Vector3[] tan;
        public readonly Vector3[] up;

        public BodyCurve(int samples)
        {
            Count = samples;
            pos = new Vector3[samples];
            tan = new Vector3[samples];
            up = new Vector3[samples];
        }

        public Vector3 Tail => pos[0];
        public Vector3 Head => pos[Count - 1];
        public Vector3 Middle => pos[Count / 2];
        public Vector3 HeadForward => tan[Count - 1];

        public void CopyFrom(BodyCurve o)
        {
            System.Array.Copy(o.pos, pos, Count);
            System.Array.Copy(o.tan, tan, Count);
            System.Array.Copy(o.up, up, Count);
        }

        /// <summary>2つの姿勢を点ごとに補間する（両方とも尾から頭の順）。</summary>
        public void Blend(BodyCurve a, BodyCurve b, float t)
        {
            for (int i = 0; i < Count; i++)
            {
                pos[i] = Vector3.LerpUnclamped(a.pos[i], b.pos[i], t);
                tan[i] = Vector3.Slerp(a.tan[i], b.tan[i], t).normalized;
                Vector3 u = Vector3.Slerp(a.up[i], b.up[i], t);
                up[i] = Vector3.ProjectOnPlane(u, tan[i]).normalized;
            }
        }

        /// <summary>pivot を中心に、体全体を回す。</summary>
        public void RotateAround(Vector3 pivot, Quaternion q)
        {
            for (int i = 0; i < Count; i++)
            {
                pos[i] = pivot + q * (pos[i] - pivot);
                tan[i] = q * tan[i];
                up[i] = q * up[i];
            }
        }

        public void RecomputeTangents()
        {
            int n = Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = pos[Mathf.Min(i + 1, n - 1)] - pos[Mathf.Max(i - 1, 0)];
                if (d.sqrMagnitude > 1e-10f) tan[i] = d.normalized;
                Vector3 u = Vector3.ProjectOnPlane(up[i], tan[i]);
                if (u.sqrMagnitude > 1e-8f) up[i] = u.normalized;
            }
        }

        // ------------------------------------------------------------------
        // J0 の表
        // ------------------------------------------------------------------
        const int TableSize = 256;
        public const float ArchMax = 2.25f;
        static float[] _j0;

        static void EnsureTable()
        {
            if (_j0 != null) return;
            _j0 = new float[TableSize];
            const int m = 160;
            for (int i = 0; i < TableSize; i++)
            {
                float a = ArchMax * i / (TableSize - 1);
                double s = 0;
                for (int k = 0; k < m; k++)
                {
                    double u = (k + 0.5) / m;
                    s += System.Math.Cos(a * System.Math.Sin(2.0 * System.Math.PI * u));
                }
                _j0[i] = (float)(s / m);
            }
        }

        /// <summary>D/L の比から、アーチの強さ a を求める。</summary>
        public static float SolveArch(float ratio)
        {
            EnsureTable();
            if (ratio >= _j0[0]) return 0f;
            if (ratio <= _j0[TableSize - 1]) return ArchMax;
            int lo = 0, hi = TableSize - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_j0[mid] > ratio) lo = mid; else hi = mid;
            }
            float t = Mathf.InverseLerp(_j0[lo], _j0[hi], ratio);
            return ArchMax * (lo + t) / (TableSize - 1);
        }

        static float[] _xs = new float[0];
        static float[] _ys = new float[0];

        /// <summary>
        /// 尾 o と頭 e を結ぶアーチ。upHint の側へ体を持ち上げる。
        /// tailAngle/headAngle は両端の接線を弦からどれだけ傾けるか（地面に沿わせるため）。
        /// sway は横方向のゆれ、lift は全体をふくらませる量（呼吸など）。
        /// </summary>
        public void BuildArch(Vector3 o, Vector3 e, Vector3 upHint, float length, float tailAngle, float headAngle, float sway, float lift)
        {
            int n = Count;
            int K = n - 1;
            if (_xs.Length < n) { _xs = new float[n]; _ys = new float[n]; }

            Vector3 chord = e - o;
            float D = chord.magnitude;
            Vector3 X = D > 1e-5f ? chord / D : ShakuMath.AnyPerpendicular(upHint);
            Vector3 U = upHint - X * Vector3.Dot(upHint, X);
            U = U.sqrMagnitude > 1e-8f ? U.normalized : ShakuMath.AnyPerpendicular(X);
            Vector3 Z = Vector3.Cross(X, U);

            float ratio = Mathf.Clamp01(D / length);
            float a = SolveArch(ratio);
            float stretch = D > length ? D / length : 1f;
            float ds = length * stretch / K;

            float x = 0f, y = 0f;
            _xs[0] = 0f;
            _ys[0] = 0f;
            for (int k = 1; k <= K; k++)
            {
                float u = (k - 0.5f) / K;
                float th = Theta(u, a, tailAngle, headAngle);
                x += Mathf.Cos(th) * ds;
                y += Mathf.Sin(th) * ds;
                _xs[k] = x;
                _ys[k] = y;
            }
            float ex = D - _xs[K];
            float ey = -_ys[K];
            for (int k = 0; k <= K; k++)
            {
                float u = k / (float)K;
                float bump = Mathf.Sin(Mathf.PI * u);
                float lat = sway * length * 0.18f * bump * (0.4f + 0.6f * u);
                Vector3 p = o + X * (_xs[k] + ex * u) + U * (_ys[k] + ey * u + lift * length * bump) + Z * lat;
                pos[k] = p;
            }
            // 接線は差分から、背中方向は「横ベクトル × 接線」で求める（ループで 90° を超えても裏返らない）
            for (int k = 0; k <= K; k++)
            {
                Vector3 d = pos[Mathf.Min(k + 1, K)] - pos[Mathf.Max(k - 1, 0)];
                tan[k] = d.sqrMagnitude > 1e-10f ? d.normalized : X;
                Vector3 uu = Vector3.Cross(Z, tan[k]);
                up[k] = uu.sqrMagnitude > 1e-8f ? uu.normalized : U;
            }
        }

        static float Theta(float u, float a, float tailAngle, float headAngle)
        {
            float w0 = (1f - u);
            w0 = w0 * w0 * w0;
            float w1 = u * u * u;
            return a * Mathf.Sin(2f * Mathf.PI * u) + tailAngle * w0 + headAngle * w1;
        }

        /// <summary>
        /// 背伸び：腹脚（体の後ろ 1/3）で踏んばり、前半身を持ち上げて首を振る。
        /// </summary>
        public void BuildRear(Vector3 tail, Vector3 forward, Vector3 normal, float length, float rise, float swayAngle, float nod)
        {
            int n = Count;
            int K = n - 1;
            Vector3 X = ShakuMath.ProjectOnPlaneSafe(forward, normal, ShakuMath.AnyPerpendicular(normal));
            Vector3 U = normal.normalized;
            Vector3 Z = Vector3.Cross(X, U);
            float ds = length / K;
            Vector3 p = tail;
            pos[0] = p;
            tan[0] = X;
            up[0] = U;
            for (int k = 1; k <= K; k++)
            {
                float u = (k - 0.5f) / K;
                float th = rise * 1.25f * ShakuMath.SmoothStep(0.3f, 0.58f, u) - rise * nod * ShakuMath.SmoothStep(0.8f, 1f, u);
                float ph = swayAngle * ShakuMath.SmoothStep(0.35f, 1f, u);
                Vector3 h = X * Mathf.Cos(ph) + Z * Mathf.Sin(ph);
                Vector3 d = h * Mathf.Cos(th) + U * Mathf.Sin(th);
                p += d * ds;
                pos[k] = p;
                tan[k] = d;
                Vector3 side = Vector3.Cross(h, U);
                up[k] = Vector3.Cross(side, d).normalized;
            }
        }

        /// <summary>
        /// 落ちるとき：おなかを内側にしてくるんと丸まる（C の字）。spin で回転しながら落ちる。
        /// </summary>
        public void BuildCurl(Vector3 center, Vector3 axis, Vector3 facing, float length, float total, float spin)
        {
            int K = Count - 1;
            Vector3 ax = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            Vector3 A = ShakuMath.ProjectOnPlaneSafe(facing, ax, ShakuMath.AnyPerpendicular(ax)).normalized;
            Vector3 B = Vector3.Cross(ax, A);
            total = Mathf.Max(0.5f, total);
            float r = length / total;
            for (int k = 0; k <= K; k++)
            {
                float u = k / (float)K;
                float ang = spin + (u - 0.5f) * total;
                Vector3 radial = A * Mathf.Cos(ang) + B * Mathf.Sin(ang);
                pos[k] = center + radial * r;
                tan[k] = (-A * Mathf.Sin(ang) + B * Mathf.Cos(ang)).normalized;
                up[k] = radial;
            }
        }

        /// <summary>
        /// 糸にぶら下がる：頭を上にして体はたれ下がり、ゆるく丸まってくねる。
        /// </summary>
        public void BuildHang(Vector3 head, Vector3 facing, float length, float curl, float wiggle)
        {
            int n = Count;
            int K = n - 1;
            Vector3 H = ShakuMath.ProjectOnPlaneSafe(facing, Vector3.up, Vector3.forward);
            Vector3 Z = Vector3.Cross(Vector3.up, H);
            float ds = length / K;
            Vector3 p = head;
            pos[K] = p;
            for (int k = K - 1; k >= 0; k--)
            {
                float v = (K - k - 0.5f) / K; // 0=頭 → 1=尾
                float th = curl * v * v + wiggle * Mathf.Sin(v * 5f) * 0.6f;
                Vector3 d = -Vector3.up * Mathf.Cos(th) + H * Mathf.Sin(th);
                p += d * ds;
                pos[k] = p;
            }
            for (int k = 0; k <= K; k++)
            {
                Vector3 d = pos[Mathf.Min(k + 1, K)] - pos[Mathf.Max(k - 1, 0)];
                tan[k] = d.normalized;
                up[k] = Vector3.Cross(Z, tan[k]).normalized;
            }
        }
    }
}

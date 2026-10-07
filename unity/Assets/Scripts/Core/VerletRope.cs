using System;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 糸（ロープ）：点を同じ長さでつないで、重さ・空気のてい抗（風）・地面を計算する（ベルレ積分）。
    /// 両はしをとめれば、長さが余ると自然にたるみ、足りなければまっすぐ張る。片はしをはなすと、ひらひら落ちる。
    /// </summary>
    public class VerletRope
    {
        public readonly Vector3[] pos;
        readonly Vector3[] _prev;
        public int Count => pos.Length;
        /// <summary>糸の重さの効き方（とても軽いので、重さより空気の方がよく効く）。</summary>
        public float gravityScale = 0.45f;
        /// <summary>空気のてい抗（1 秒あたり、風の速さへ近づく割合）。</summary>
        public float airDrag = 3f;
        /// <summary>長さをそろえる計算の回数（多いほど、のびにくい）。</summary>
        public int iterations = 20;
        /// <summary>地面の高さ（null なら地面を気にしない）。</summary>
        public Func<Vector3, float> groundHeight;

        public VerletRope(int points)
        {
            pos = new Vector3[Mathf.Max(2, points)];
            _prev = new Vector3[pos.Length];
        }

        /// <summary>a から b へ、まっすぐな糸にする。</summary>
        public void Reset(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = Vector3.Lerp(a, b, i / (float)(pos.Length - 1));
                _prev[i] = pos[i];
            }
        }

        /// <summary>
        /// dt 秒すすめる。start はとめる点（つけ根）。end はもう片方のはし（null なら、はなれて自由）。
        /// length は糸全体の長さ、wind は風の速さ。
        /// </summary>
        public void Step(float dt, Vector3 start, Vector3? end, float length, Vector3 wind)
        {
            int n = ShakuPhysics.Substeps(dt, out float h);
            if (h <= 0f) return;
            float seg = Mathf.Max(1e-3f, length) / (pos.Length - 1);
            float keep = Mathf.Exp(-airDrag * h);   // 空気のてい抗：風に対する速さが、だんだん小さくなる
            Vector3 g = Vector3.down * (ShakuPhysics.Gravity * gravityScale);
            for (int s = 0; s < n; s++)
            {
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector3 v = (pos[i] - _prev[i]) / h;
                    v = wind + (v - wind) * keep;
                    _prev[i] = pos[i];
                    pos[i] += v * h + g * (h * h);
                }
                for (int k = 0; k < iterations; k++)
                {
                    pos[0] = start;
                    if (end.HasValue) pos[pos.Length - 1] = end.Value;
                    for (int i = 0; i < pos.Length - 1; i++)
                    {
                        Vector3 d = pos[i + 1] - pos[i];
                        float l = d.magnitude;
                        if (l < 1e-6f || l <= seg) continue;   // 糸はちぢまない（押されると、たるむだけ）
                        Vector3 corr = d * ((l - seg) / l);
                        bool aFixed = i == 0, bFixed = end.HasValue && i + 1 == pos.Length - 1;
                        if (aFixed && bFixed) continue;
                        if (aFixed) pos[i + 1] -= corr;
                        else if (bFixed) pos[i] += corr;
                        else
                        {
                            pos[i] += corr * 0.5f;
                            pos[i + 1] -= corr * 0.5f;
                        }
                    }
                }
                pos[0] = start;
                if (end.HasValue) pos[pos.Length - 1] = end.Value;
                // 地面・水面の下にはしずまない
                if (groundHeight != null)
                    for (int i = 1; i < pos.Length - 1 + (end.HasValue ? 0 : 1); i++)
                    {
                        float gy = groundHeight(pos[i]) + 0.01f;
                        if (pos[i].y < gy)
                        {
                            pos[i].y = gy;
                            _prev[i] = Vector3.Lerp(_prev[i], pos[i], 0.5f);   // 地面でこすれて止まる
                        }
                    }
            }
            // 安全：計算がこわれたら、まっすぐにもどす
            for (int i = 0; i < pos.Length; i++)
                if (!ShakuPhysics.IsFinite(pos[i]))
                {
                    Reset(start, end ?? start + Vector3.down * length);
                    return;
                }
        }

        /// <summary>いまの糸の長さ（点をつないだ長さ）。</summary>
        public float CurrentLength()
        {
            float l = 0f;
            for (int i = 0; i < pos.Length - 1; i++) l += Vector3.Distance(pos[i], pos[i + 1]);
            return l;
        }

        /// <summary>まっすぐな線から、いちばんはなれている点の距離（たるみ）。</summary>
        public float Sag()
        {
            Vector3 a = pos[0], b = pos[pos.Length - 1];
            Vector3 ab = b - a;
            float best = 0f;
            for (int i = 1; i < pos.Length - 1; i++)
            {
                Vector3 p = pos[i] - a;
                Vector3 off = ab.sqrMagnitude > 1e-8f ? p - ab * (Vector3.Dot(p, ab) / ab.sqrMagnitude) : p;
                best = Mathf.Max(best, off.magnitude);
            }
            return best;
        }
    }
}

using UnityEngine;

namespace Shakutori
{
    /// <summary>ゲーム全体で使う定数とちょっとした数学関数。</summary>
    public static class ShakuConst
    {
        public const int SurfaceLayer = 6;   // しゃくとりむしが這える面（地形・岩・キノコなど）
        public const int PlayerLayer = 7;
        public const int CreatureLayer = 8;  // 乗れるいきもの（しゃくとりむしは這えるが、いきもの同士や地形の配置には使わない）
        public const int WaterLayer = 4;     // Unity 標準の Water レイヤー
        public const int RollingLayer = 9;   // 押すと転がる物（どんぐり）。しゃくとりむしは這わずに、押す
        public const int WormBodyLayer = 10; // しゃくとりむしの体（転がる物を押すためだけの当たり判定）

        public static int SurfaceMask => 1 << SurfaceLayer;
        public static int CreatureMask => 1 << CreatureLayer;
        /// <summary>しゃくとりむしが這える面（地形・物・乗れるいきもの）。</summary>
        public static int WalkableMask => SurfaceMask | CreatureMask;

        /// <summary>しゃくとりむしの体長（ワールド単位）。世界のスケールはこれを基準にしている。</summary>
        public const float BodyLength = 1.0f;
        public const float BodyRadius = 0.05f;
    }

    public static class ShakuMath
    {
        /// <summary>
        /// 小数を整数にする（切り捨て）。NaN や大きすぎる値は 0 にする。
        /// WebGL では、NaN や int に入らない値をそのまま int にすると、ゲームが止まってしまう（エディタでは止まらないので気づきにくい）。
        /// </summary>
        public static int FloorToIntSafe(float v) => v > -1e9f && v < 1e9f ? Mathf.FloorToInt(v) : 0;

        /// <summary>小数を整数にする（四捨五入）。NaN や大きすぎる値は 0 にする（FloorToIntSafe と同じ理由）。</summary>
        public static int RoundToIntSafe(float v) => v > -1e9f && v < 1e9f ? Mathf.RoundToInt(v) : 0;

        /// <summary>ます目の番号（位置 ÷ ますの大きさ、切り捨て）。位置がこわれていても止まらない。</summary>
        public static int CellIndex(float v, float cell) => FloorToIntSafe(v / cell);

        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static float Smoother01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        public static float SmoothStep(float e0, float e1, float x)
        {
            if (Mathf.Approximately(e0, e1)) return x < e0 ? 0f : 1f;
            return Smooth01((x - e0) / (e1 - e0));
        }

        /// <summary>角のない「0 から先は x と同じだけふえる」形（0 のまわりの幅 w だけ、なめらかに曲がる）。</summary>
        public static float SoftRamp(float x, float w)
        {
            if (x <= 0f) return 0f;
            if (x >= w) return x - w * 0.5f;
            return x * x / (2f * w);
        }

        /// <summary>遊べる所の外の起伏が始まる半径（この内がわは、地形を変えない）。</summary>
        public const float OuterReliefStart = 68f;

        /// <summary>
        /// 遊べる所の外の、高さのちがう丘と、低い切れ目（外まわりが、どこも同じ高さの、まるいすりばちに見えないように）。
        /// r が OuterReliefStart より内がわは 0（しずくの場所や、歩く所は変えない）。gates（トンネルの口）の先のあたりは、低くする。
        /// </summary>
        public static float OuterRelief(float x, float z, float seed, Vector2[] gates, float amp = 1f)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float k = SmoothStep(OuterReliefStart, OuterReliefStart + 18f, r);
            if (k <= 0f) return 0f;
            float a = Mathf.Atan2(z, x);
            float lobes = Mathf.Clamp01(0.5f + 0.32f * Mathf.Sin(a * 3f + seed) + 0.18f * Mathf.Sin(a * 5f + seed * 2.3f));
            float n = Mathf.PerlinNoise(x * 0.03f + seed * 3.1f, z * 0.03f + seed * 1.7f);
            float h = 4f + 20f * lobes * lobes + 7f * (n - 0.5f);
            if (gates != null)
                foreach (var g in gates)
                {
                    float dx = x - g.x * 1.15f, dz = z - g.y * 1.15f;
                    h *= SmoothStep(12f, 26f, Mathf.Sqrt(dx * dx + dz * dz));
                }
            return k * h * amp;
        }

        /// <summary>0..1 のなめらかな山型（中心で 1、半径で 0）。</summary>
        public static float Bump(float dist, float radius)
        {
            float k = Mathf.Clamp01(1f - (dist * dist) / (radius * radius));
            return k * k;
        }

        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        public static float Fbm(float x, float y, int octaves, float lacunarity = 2.03f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Mathf.PerlinNoise(x, y) - 0.5f) * amp;
                norm += amp;
                amp *= gain;
                x = x * lacunarity + 17.13f;
                y = y * lacunarity + 9.71f;
            }
            return sum / norm * 2f; // おおよそ -1..1
        }

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        public static Vector3 ProjectOnPlaneSafe(Vector3 v, Vector3 n, Vector3 fallback)
        {
            Vector3 p = Vector3.ProjectOnPlane(v, n);
            return p.sqrMagnitude > 1e-8f ? p.normalized : fallback;
        }

        public static Vector3 AnyPerpendicular(Vector3 n)
        {
            Vector3 a = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(n, a).normalized;
        }

        public static float DampFactor(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);
    }
}

using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 森をふく風。ゆっくり向きを変え、ときどき突風になる。
    /// チョウやトンボ・糸にぶら下がったしゃくとりむし・草花のゆれ（シェーダーの _ShakuWind）に使う。
    /// </summary>
    public static class Wind
    {
        static readonly int WindId = Shader.PropertyToID("_ShakuWind");

        /// <summary>突風の強さ（0〜1）。</summary>
        public static float Gust(float t)
        {
            float g = 0.5f + 0.5f * Mathf.Sin(t * 0.21f) * Mathf.Sin(t * 0.53f + 1.3f);
            float burst = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 0.11f + 0.4f)), 12f);
            return Mathf.Clamp01(g * 0.7f + burst * 0.6f);
        }

        /// <summary>水平の風向き。</summary>
        public static Vector3 Direction(float t)
        {
            float a = 0.6f + Mathf.Sin(t * 0.017f) * 1.1f;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        /// <summary>ある場所の風（速さ、単位/秒）。場所によって少しずつずらして、ゆらぎを出す。</summary>
        public static Vector3 At(Vector3 p) => At(p, Time.time);

        public static Vector3 At(Vector3 p, float t)
        {
            float local = t + p.x * 0.05f + p.z * 0.03f;
            return Direction(t) * (0.25f + 1.1f * Gust(local));
        }

        /// <summary>シェーダーへ渡す（xy = 風向き(XZ), z = 強さ, w = 速さ）。毎フレーム AmbientFX から呼ぶ。</summary>
        public static void Publish(float t)
        {
            Vector3 d = Direction(t);
            float g = Gust(t);
            Shader.SetGlobalVector(WindId, new Vector4(d.x, d.z, 0.55f + 0.9f * g, 0.9f + 0.6f * g));
        }
    }
}

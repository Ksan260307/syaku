using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 森をふく風。ゆっくり向きを変え、ときどき突風になる。
    /// 突風は風下へ流れていき（風上の草が先にゆれる）、小さなうずで場所ごとに少しずつ乱れる。
    /// 地面の近くは弱く、高い所ほど強い（地面との まさつ）。
    /// チョウやトンボ・糸にぶら下がったしゃくとりむし・草花のゆれ（シェーダーの _ShakuWind）に使う。
    /// </summary>
    public static class Wind
    {
        static readonly int WindId = Shader.PropertyToID("_ShakuWind");

        /// <summary>突風が風下へ流れていく速さ（単位/秒）。</summary>
        public const float GustTravelSpeed = 4f;
        /// <summary>地面のでこぼこの大きさ（草地）。風の高さの分布に使う。</summary>
        public const float Roughness = 0.05f;
        /// <summary>この高さの風が、At(p) の強さ。</summary>
        public const float ReferenceHeight = 1f;

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

        /// <summary>ある場所の風（速さ、単位/秒。高さ 1 くらいの風）。</summary>
        public static Vector3 At(Vector3 p) => At(p, Time.time);

        public static Vector3 At(Vector3 p, float t)
        {
            Vector3 dir = Direction(t);
            float g = GustAt(p, t);
            Vector3 v = dir * (0.25f + 1.1f * g);
            // 小さなうず（乱れ）：場所と時間で、向きと強さが少しずつゆらぐ。強い風ほど乱れも大きい
            float tx = Mathf.Sin(t * 1.7f + p.x * 0.9f + p.z * 0.4f) + 0.5f * Mathf.Sin(t * 2.9f - p.z * 1.3f);
            float tz = Mathf.Sin(t * 1.3f + p.z * 0.8f - p.x * 0.5f) + 0.5f * Mathf.Sin(t * 3.1f + p.x * 1.1f);
            v += new Vector3(tx, 0f, tz) * (0.045f * (0.3f + g));
            return v;
        }

        /// <summary>
        /// その場所の突風の強さ（0〜1）。突風は風下へ流れていく：風上の場所ほど、先に強くなる
        /// （横方向にも少しずらして、ゆらぎを出す）。
        /// </summary>
        public static float GustAt(Vector3 p, float t)
        {
            Vector3 dir = Direction(t);
            float along = p.x * dir.x + p.z * dir.z;
            float across = -p.x * dir.z + p.z * dir.x;
            return Gust(t - along / GustTravelSpeed + across * 0.02f);
        }

        /// <summary>
        /// ひらけた草原の上の、ゆるい上昇気流（日なたで温まった空気が上がる。森の花の草原だけ）。上向きの速さ。
        /// </summary>
        public static float Updraft(Vector3 p, float t)
        {
            if (Areas.Current != Areas.Forest) return 0f;
            float k = ShakuMath.Bump(Vector2.Distance(new Vector2(p.x, p.z), ForestLayout.Meadow), 14f);
            return k * 0.25f * (0.6f + 0.4f * Mathf.Sin(t * 0.37f + p.x * 0.1f));
        }

        /// <summary>地面からの高さを考えた風（地面の近くは弱く、高い所ほど強い）。</summary>
        public static Vector3 At(Vector3 p, float t, float heightAboveGround) => At(p, t) * HeightFactor(heightAboveGround);

        /// <summary>
        /// 高さによる風の強さの倍率（対数の分布：地面とのまさつで、地面の近くほど弱い）。
        /// 高さ 1 で 1 倍、地面すれすれで 0.35 倍、高い所は 1.4 倍まで。
        /// </summary>
        public static float HeightFactor(float heightAboveGround)
        {
            float h = Mathf.Max(0f, heightAboveGround);
            float k = Mathf.Log((h + Roughness) / Roughness) / Mathf.Log((ReferenceHeight + Roughness) / Roughness);
            return Mathf.Clamp(k, 0.35f, 1.4f);
        }

        static float _phase, _lastT = float.NaN;

        /// <summary>草花のゆれの位相（ゆれる速さを時間で積み上げたもの）。</summary>
        public static float Phase => _phase;

        /// <summary>草花がゆれる速さ（1 秒あたりの位相）。突風のときは少し速い。</summary>
        public static float SwaySpeed(float t) => 0.9f + 0.6f * Gust(t);

        /// <summary>
        /// シェーダーへ渡す（xy = 風向き(XZ), z = 強さ, w = ゆれの位相）。毎フレーム AmbientFX から呼ぶ。
        /// 位相は「速さ × 時間」ではなく、速さを少しずつ積み上げる（突風で速さが変わっても、ゆれがとびはねない）。
        /// </summary>
        public static void Publish(float t)
        {
            Vector3 d = Direction(t);
            float g = Gust(t);
            float dt = float.IsNaN(_lastT) ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f);
            _lastT = t;
            _phase += SwaySpeed(t) * dt;
            if (_phase > 12566.37f) _phase -= 12566.37f;   // とても長く遊んでも、数の細かさが落ちないように
            Shader.SetGlobalVector(WindId, new Vector4(d.x, d.z, 0.55f + 0.9f * g, _phase));
        }

        /// <summary>テスト用：位相を最初にもどす。</summary>
        public static void ResetPhase()
        {
            _phase = 0f;
            _lastT = float.NaN;
        }
    }
}

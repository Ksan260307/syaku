using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// エリアごとの空気と光：霧の色と距離・日ざしの色と強さ・環境光（空・地平・地面の 3 色）。
    /// 森は緑がかった霧と、黄みのこもれび。川辺は水色のすずしい霧と、白っぽい明るい日ざし。
    /// 公園はうすい空色の、見通しのよい霧と、ひだまりの日ざし。山は、すんだ遠くまで見える空気と、強い日ざし。
    /// </summary>
    public static class AreaAtmosphere
    {
        public struct Look
        {
            public Color fog;
            public float fogStart, fogEnd;
            public Color sun;
            public float sunIntensity;
            public Color sky, equator, ground;
        }

        public static Look For(string areaId)
        {
            switch (areaId)
            {
                case "river":
                    return new Look
                    {
                        fog = new Color(0.62f, 0.78f, 0.84f), fogStart = 34f, fogEnd = 190f,
                        sun = new Color(1f, 0.97f, 0.9f), sunIntensity = 1.55f,
                        sky = new Color(0.62f, 0.78f, 0.93f), equator = new Color(0.6f, 0.69f, 0.64f), ground = new Color(0.5f, 0.46f, 0.36f),
                    };
                case "mountain":
                    // 山は、すんだ空気（遠くまで見える）。高い所の、白っぽく強い日ざし
                    return new Look
                    {
                        fog = new Color(0.7f, 0.8f, 0.92f), fogStart = 46f, fogEnd = 260f,
                        sun = new Color(1f, 0.98f, 0.92f), sunIntensity = 1.65f,
                        sky = new Color(0.64f, 0.78f, 0.98f), equator = new Color(0.66f, 0.72f, 0.7f), ground = new Color(0.42f, 0.44f, 0.36f),
                    };
                case "hut":
                    // 山小屋の中：まきストーブの、あたたかい空気。窓からの日ざしは、やわらかい金色
                    return new Look
                    {
                        fog = new Color(0.78f, 0.68f, 0.56f), fogStart = 90f, fogEnd = 420f,
                        sun = new Color(1f, 0.88f, 0.68f), sunIntensity = 1.5f,
                        sky = new Color(0.86f, 0.72f, 0.56f), equator = new Color(0.74f, 0.58f, 0.44f), ground = new Color(0.56f, 0.4f, 0.28f),
                    };
                case "coast":
                    // 海辺：すんだ空気と、まぶしい日ざし（沖の小島まで見える）。砂浜の照り返しで、下からも明るい
                    return new Look
                    {
                        fog = new Color(0.7f, 0.85f, 0.93f), fogStart = 60f, fogEnd = 330f,
                        sun = new Color(1f, 0.97f, 0.88f), sunIntensity = 1.65f,
                        sky = new Color(0.62f, 0.8f, 0.98f), equator = new Color(0.68f, 0.77f, 0.78f), ground = new Color(0.62f, 0.57f, 0.44f),
                    };
                case "shrine":
                    // 神社：鎮守の森の、夕ぐれ。こもれびは金色で、木かげは青みのある緑。霧は少しあたたかい
                    return new Look
                    {
                        fog = new Color(0.64f, 0.62f, 0.52f), fogStart = 26f, fogEnd = 165f,
                        sun = new Color(1f, 0.8f, 0.56f), sunIntensity = 1.38f,
                        sky = new Color(0.7f, 0.66f, 0.6f), equator = new Color(0.55f, 0.56f, 0.46f), ground = new Color(0.34f, 0.3f, 0.22f),
                    };
                case "park":
                    return new Look
                    {
                        fog = new Color(0.68f, 0.82f, 0.9f), fogStart = 40f, fogEnd = 210f,
                        sun = new Color(1f, 0.95f, 0.82f), sunIntensity = 1.6f,
                        sky = new Color(0.66f, 0.8f, 0.96f), equator = new Color(0.64f, 0.72f, 0.58f), ground = new Color(0.38f, 0.5f, 0.28f),
                    };
                default:
                    return new Look
                    {
                        fog = new Color(0.6f, 0.77f, 0.66f), fogStart = 22f, fogEnd = 150f,
                        sun = new Color(1f, 0.92f, 0.74f), sunIntensity = 1.42f,
                        sky = new Color(0.62f, 0.78f, 0.62f), equator = new Color(0.52f, 0.62f, 0.46f), ground = new Color(0.36f, 0.31f, 0.22f),
                    };
            }
        }

        static readonly int SkyOpenId = Shader.PropertyToID("_SkyOpen");
        static Texture s_cookie;
        static bool s_cookieSaved;

        /// <summary>
        /// 影を落とす距離（画質 q）。山小屋の中は、かべと屋根のかげの中なので、部屋の奥まで影がとどくように長く
        /// （窓ととびらからさしこむ日だまりだけが明るい）。
        /// </summary>
        public static float ShadowDistance(string areaId, int q) => areaId == "hut" ? (q == 0 ? 120f : 160f) : (q == 0 ? 28f : 48f);

        /// <summary>いまのエリアの空気と光にする（霧・日ざし・環境光・空）。</summary>
        public static void Apply(AreaLayout area, Light sun)
        {
            var l = For(area != null ? area.Id : "forest");
            RenderSettings.fogColor = l.fog;
            RenderSettings.fogStartDistance = l.fogStart;
            RenderSettings.fogEndDistance = l.fogEnd;
            RenderSettings.ambientSkyColor = l.sky;
            RenderSettings.ambientEquatorColor = l.equator;
            RenderSettings.ambientGroundColor = l.ground;
            // 山と海辺は、樹冠のない、ひらけた空（白い雲）
            Shader.SetGlobalFloat(SkyOpenId, area != null && (area.Id == "mountain" || area.Id == "coast") ? 1f : 0f);
            if (sun != null)
            {
                sun.color = l.sun;
                sun.intensity = l.sunIntensity;
                // 木もれ日のもよう（光のクッキー）は、小屋の中ではつかわない（窓の形の日だまりにする）
                if (!s_cookieSaved) { s_cookie = sun.cookie; s_cookieSaved = true; }
                sun.cookie = area != null && (area.Id == "hut" || area.Id == "coast") ? null : s_cookie;   // 海辺も、木のかげはない
            }
            GameManager.ApplyAmbient();
        }

        /// <summary>名所を見つけたときのきらめきの色（森は若葉の緑と金、川辺は水色、公園はひだまりの金色）。</summary>
        public static Color DiscoverTint(string areaId) =>
            areaId == "river" ? new Color(0.55f, 0.9f, 1f) : areaId == "park" ? new Color(1f, 0.86f, 0.42f)
            : areaId == "mountain" ? new Color(0.85f, 0.88f, 1f) : areaId == "hut" ? new Color(1f, 0.78f, 0.5f)
            : areaId == "coast" ? new Color(0.5f, 0.95f, 0.95f) : areaId == "shrine" ? new Color(1f, 0.62f, 0.4f) : new Color(0.7f, 1f, 0.45f);

        /// <summary>しずくを取ったときのきらめきに、まぜる色。</summary>
        public static Color DropTint(string areaId) =>
            areaId == "river" ? new Color(0.6f, 0.92f, 1f) : areaId == "park" ? new Color(1f, 0.9f, 0.55f)
            : areaId == "mountain" ? new Color(0.88f, 0.9f, 1f) : areaId == "hut" ? new Color(1f, 0.82f, 0.58f)
            : areaId == "coast" ? new Color(0.62f, 0.96f, 0.95f) : areaId == "shrine" ? new Color(1f, 0.72f, 0.5f) : new Color(0.72f, 1f, 0.55f);
    }
}

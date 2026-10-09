using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// エリアごとの空気と光：霧の色と距離・日ざしの色と強さ・環境光（空・地平・地面の 3 色）。
    /// 森は緑がかった霧と、黄みのこもれび。川辺は水色のすずしい霧と、白っぽい明るい日ざし。
    /// 公園はうすい空色の、いちばん見通しのよい霧と、ひだまりの日ざし。
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

        /// <summary>いまのエリアの空気と光にする（霧・日ざし・環境光）。</summary>
        public static void Apply(AreaLayout area, Light sun)
        {
            var l = For(area != null ? area.Id : "forest");
            RenderSettings.fogColor = l.fog;
            RenderSettings.fogStartDistance = l.fogStart;
            RenderSettings.fogEndDistance = l.fogEnd;
            RenderSettings.ambientSkyColor = l.sky;
            RenderSettings.ambientEquatorColor = l.equator;
            RenderSettings.ambientGroundColor = l.ground;
            if (sun != null)
            {
                sun.color = l.sun;
                sun.intensity = l.sunIntensity;
            }
            GameManager.ApplyAmbient();
        }

        /// <summary>名所を見つけたときのきらめきの色（森は若葉の緑と金、川辺は水色、公園はひだまりの金色）。</summary>
        public static Color DiscoverTint(string areaId) =>
            areaId == "river" ? new Color(0.55f, 0.9f, 1f) : areaId == "park" ? new Color(1f, 0.86f, 0.42f) : new Color(0.7f, 1f, 0.45f);

        /// <summary>しずくを取ったときのきらめきに、まぜる色。</summary>
        public static Color DropTint(string areaId) =>
            areaId == "river" ? new Color(0.6f, 0.92f, 1f) : areaId == "park" ? new Color(1f, 0.9f, 0.55f) : new Color(0.72f, 1f, 0.55f);
    }
}

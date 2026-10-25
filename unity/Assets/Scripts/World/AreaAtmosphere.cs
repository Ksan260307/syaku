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
            public Vector4 groundDetail;   // 近くの地面のこまかな模様：x つぶ・小石, y 草のすじ, z 砂紋, w 雪のきらめき
            public Vector2 shade;          // かげ：x 明るく（0 でそのまま）, y 色みをうすく
            public Vector4 grade;          // 画面ぜんたいの色の調整（ポストエフェクトに足す）：x 明るさ, y コントラスト, z 彩度, w 色温度
            public Color rimBoost;         // ふちの光（夜の月あかりの逆光）：rgb 色, a 強さ
        }

        public static Look For(string areaId)
        {
            switch (areaId)
            {
                case "river":
                    return new Look
                    {
                        fog = new Color(0.62f, 0.78f, 0.84f), fogStart = 42f, fogEnd = 215f,
                        sun = new Color(1f, 0.97f, 0.9f), sunIntensity = 1.55f,
                        sky = new Color(0.62f, 0.78f, 0.93f), equator = new Color(0.6f, 0.69f, 0.64f), ground = new Color(0.5f, 0.46f, 0.36f),
                        groundDetail = new Vector4(0.35f, 0.4f, 0.6f, 0f),
                        grade = new Vector4(0.04f, 4f, 16f, 7f),   // くすんだ灰色の川原に、水辺の生き生きした色と、日のあたたかさ
                    };
                case "mountain":
                    // 山は、すんだ空気（遠くまで見える）。高い所の、白っぽく強い日ざし
                    return new Look
                    {
                        fog = new Color(0.7f, 0.8f, 0.92f), fogStart = 46f, fogEnd = 260f,
                        sun = new Color(1f, 0.98f, 0.92f), sunIntensity = 1.65f,
                        sky = new Color(0.64f, 0.78f, 0.98f), equator = new Color(0.66f, 0.72f, 0.7f), ground = new Color(0.42f, 0.44f, 0.36f),
                        groundDetail = new Vector4(0.38f, 0.45f, 0f, 0.35f),
                        grade = new Vector4(0f, 4f, 2f, -2f),
                    };
                case "hut":
                    // 山小屋の中：まきストーブの、あたたかい空気。窓からの日ざしは、やわらかい金色
                    return new Look
                    {
                        fog = new Color(0.78f, 0.68f, 0.56f), fogStart = 90f, fogEnd = 420f,
                        sun = new Color(1f, 0.88f, 0.68f), sunIntensity = 1.5f,
                        sky = new Color(0.86f, 0.72f, 0.56f), equator = new Color(0.74f, 0.58f, 0.44f), ground = new Color(0.56f, 0.4f, 0.28f),
                        groundDetail = new Vector4(0.22f, 0f, 0f, 0f),
                        shade = new Vector2(-0.1f, 0f),     // 部屋のかげは少しこく（窓からの日だまりが、うきたつ）
                        grade = new Vector4(0f, 2f, -6f, -7f),     // 木のだいだい一色にならないように、少しすずしく
                    };
                case "coast":
                    // 海辺：すんだ空気と、まぶしい日ざし（沖の小島まで見える）。砂浜の照り返しで、下からも明るい
                    return new Look
                    {
                        fog = new Color(0.7f, 0.85f, 0.93f), fogStart = 60f, fogEnd = 330f,
                        sun = new Color(1f, 0.97f, 0.88f), sunIntensity = 1.65f,
                        sky = new Color(0.62f, 0.8f, 0.98f), equator = new Color(0.68f, 0.77f, 0.78f), ground = new Color(0.62f, 0.57f, 0.44f),
                        groundDetail = new Vector4(0.35f, 0.4f, 1f, 0f),
                        grade = new Vector4(0f, 5f, 10f, 0f),
                    };
                case "shrine":
                    // 神社：鎮守の森の、夕ぐれ。こもれびは金色で、木かげは青みのある緑。霧は少しあたたかい
                    return new Look
                    {
                        fog = new Color(0.72f, 0.64f, 0.48f), fogStart = 36f, fogEnd = 190f,   // 黄緑ではなく、夕ぐれの金色の霞
                        sun = new Color(1f, 0.8f, 0.55f), sunIntensity = 1.45f,
                        sky = new Color(0.7f, 0.68f, 0.64f), equator = new Color(0.56f, 0.58f, 0.5f), ground = new Color(0.36f, 0.33f, 0.26f),
                        groundDetail = new Vector4(0.4f, 0.4f, 0f, 0f),
                        shade = new Vector2(0.32f, 0.2f),    // 参道の石だたみが、かげで暗い紫にしずまない
                        grade = new Vector4(0f, 6f, 8f, 5f),       // 朱を、にごらせない
                    };
                case "night":
                    // 夜の森：青い月あかり。暗すぎない（足もとと、いきものが見える）。霧は、こい藍色
                    return new Look
                    {
                        fog = new Color(0.1f, 0.14f, 0.24f), fogStart = 24f, fogEnd = 155f,
                        sun = new Color(0.64f, 0.75f, 1f), sunIntensity = 1.08f,
                        sky = new Color(0.3f, 0.36f, 0.56f), equator = new Color(0.23f, 0.28f, 0.4f), ground = new Color(0.17f, 0.19f, 0.25f),
                        groundDetail = new Vector4(0.35f, 0.4f, 0f, 0f),
                        shade = new Vector2(0.1f, 0f),
                        grade = new Vector4(0.08f, 20f, -4f, -6f),   // 暗い中間色にしずまない：明るい所は明るく、くらい所はくらく
                        rimBoost = new Color(0.58f, 0.72f, 1f, 0.6f),  // 月あかりの、青白いふちどり
                    };
                case "xmas":
                    // クリスマス村：雪の夜。月あかりと、雪の照り返しで、下からも明るい（足もとが見える）。霧は、こい藍色
                    return new Look
                    {
                        fog = new Color(0.13f, 0.19f, 0.29f), fogStart = 30f, fogEnd = 190f,
                        sun = new Color(0.82f, 0.88f, 1f), sunIntensity = 0.95f,
                        sky = new Color(0.47f, 0.51f, 0.6f), equator = new Color(0.44f, 0.46f, 0.51f), ground = new Color(0.52f, 0.52f, 0.54f),
                        groundDetail = new Vector4(0.18f, 0.3f, 0f, 1f),
                        shade = new Vector2(0.12f, 0.4f),    // 雪のかげは、こい青紫にしない
                        grade = new Vector4(0.03f, 8f, 6f, 6f),    // 雪の青灰色に、あかりの琥珀色が、うきたつ
                        rimBoost = new Color(0.62f, 0.72f, 1f, 0.22f),
                    };
                case "park":
                    return new Look
                    {
                        fog = new Color(0.68f, 0.82f, 0.9f), fogStart = 40f, fogEnd = 210f,
                        sun = new Color(1f, 0.95f, 0.82f), sunIntensity = 1.6f,
                        sky = new Color(0.66f, 0.8f, 0.96f), equator = new Color(0.64f, 0.72f, 0.58f), ground = new Color(0.38f, 0.5f, 0.28f),
                        groundDetail = new Vector4(0.3f, 0.5f, 0.5f, 0f),
                        grade = new Vector4(0f, 3f, 4f, 0f),
                    };
                default:
                    return new Look
                    {
                        fog = new Color(0.6f, 0.77f, 0.66f), fogStart = 30f, fogEnd = 172f,
                        sun = new Color(1f, 0.92f, 0.74f), sunIntensity = 1.42f,
                        sky = new Color(0.62f, 0.78f, 0.62f), equator = new Color(0.52f, 0.62f, 0.46f), ground = new Color(0.36f, 0.31f, 0.22f),
                        groundDetail = new Vector4(0.35f, 0.45f, 0f, 0f),
                        grade = new Vector4(0f, 6f, 2f, 0f),   // 中間の明るさにまとまらないように
                    };
            }
        }

        static readonly int SkyOpenId = Shader.PropertyToID("_SkyOpen");
        static readonly int GroundDetailId = Shader.PropertyToID("_ShakuGround");
        static readonly int ShadeId = Shader.PropertyToID("_ShakuShade");
        static readonly int RimBoostId = Shader.PropertyToID("_ShakuRimBoost");

        /// <summary>いまのエリアの、画面ぜんたいの色の調整（GameManager がポストエフェクトに足す）。</summary>
        public static Vector4 CurrentGrade { get; private set; }
        static readonly int NightId = Shader.PropertyToID("_Night");

        /// <summary>夜のエリアか（空に月と星。水の照り返しも夜の色）。</summary>
        public static bool IsNight(string areaId) => areaId == "night" || areaId == "xmas";
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
            Shader.SetGlobalVector(GroundDetailId, l.groundDetail);
            Shader.SetGlobalVector(ShadeId, new Vector4(l.shade.x, l.shade.y, 0f, 0f));
            Shader.SetGlobalVector(RimBoostId, new Vector4(l.rimBoost.r, l.rimBoost.g, l.rimBoost.b, l.rimBoost.a));
            CurrentGrade = l.grade;
            // 山と海辺とクリスマス村は、樹冠のない、ひらけた空（雲。夜は、うすい雪雲）。公園は、木の間から、青い空と白い雲がのぞく
            Shader.SetGlobalFloat(SkyOpenId, area != null && (area.Id == "mountain" || area.Id == "coast" || area.Id == "xmas") ? 1f : area != null && area.Id == "park" ? 0.7f : 0f);
            // 夜の森は、月と星の夜空（木の葉は、空にうかぶ黒いかげ）
            Shader.SetGlobalFloat(NightId, area != null && IsNight(area.Id) ? 1f : 0f);
            if (sun != null)
            {
                sun.color = l.sun;
                sun.intensity = l.sunIntensity;
                // 木もれ日のもよう（光のクッキー）は、小屋の中ではつかわない（窓の形の日だまりにする）
                if (!s_cookieSaved) { s_cookie = sun.cookie; s_cookieSaved = true; }
                sun.cookie = area != null && (area.Id == "hut" || area.Id == "coast" || area.Id == "xmas") ? null : s_cookie;   // 海辺とクリスマス村も、木のかげはない
            }
            GameManager.ApplyAmbient();
        }

        /// <summary>名所を見つけたときのきらめきの色（森は若葉の緑と金、川辺は水色、公園はひだまりの金色）。</summary>
        public static Color DiscoverTint(string areaId) =>
            areaId == "river" ? new Color(0.55f, 0.9f, 1f) : areaId == "park" ? new Color(1f, 0.86f, 0.42f)
            : areaId == "mountain" ? new Color(0.85f, 0.88f, 1f) : areaId == "hut" ? new Color(1f, 0.78f, 0.5f)
            : areaId == "coast" ? new Color(0.5f, 0.95f, 0.95f) : areaId == "shrine" ? new Color(1f, 0.62f, 0.4f)
            : areaId == "night" ? new Color(0.6f, 0.75f, 1f) : areaId == "xmas" ? new Color(1f, 0.85f, 0.5f) : new Color(0.7f, 1f, 0.45f);

        /// <summary>しずくを取ったときのきらめきに、まぜる色。</summary>
        public static Color DropTint(string areaId) =>
            areaId == "river" ? new Color(0.6f, 0.92f, 1f) : areaId == "park" ? new Color(1f, 0.9f, 0.55f)
            : areaId == "mountain" ? new Color(0.88f, 0.9f, 1f) : areaId == "hut" ? new Color(1f, 0.82f, 0.58f)
            : areaId == "coast" ? new Color(0.62f, 0.96f, 0.95f) : areaId == "shrine" ? new Color(1f, 0.72f, 0.5f)
            : areaId == "night" ? new Color(0.7f, 0.85f, 1f) : areaId == "xmas" ? new Color(0.85f, 0.92f, 1f) : new Color(0.72f, 1f, 0.55f);
    }
}

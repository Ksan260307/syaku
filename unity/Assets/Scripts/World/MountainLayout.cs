using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 山エリア「雲の見える山」のレイアウト。
    /// 南のふもと（川辺へもどる木の根のトンネル）から、北の山頂へむかって、ゆるやかに高くなる。
    /// 岩の階段・湧き水の泉・高山の花畑・大きな松・雪渓・山小屋・岩のトンネル・尾根の石積みを通って、山頂へ。
    /// </summary>
    public static class MountainLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 80f;

        /// <summary>川辺へもどる木の根のトンネル（南の端）</summary>
        public static readonly Vector2 Gate = new Vector2(0f, -58.5f);
        public static readonly Vector2 Spawn = new Vector2(0.5f, -51f);

        public static readonly Vector2 Trailhead = new Vector2(0f, -46f);
        public static readonly Vector2 RockStairs = new Vector2(6f, -24f);
        public static readonly Vector2 Spring = new Vector2(-30f, -12f);
        public static readonly Vector2 Meadow = new Vector2(26f, 4f);
        public static readonly Vector2 Pine = new Vector2(-10f, 14f);
        public static readonly Vector2 SnowPatch = new Vector2(-30f, 30f);
        public static readonly Vector2 Hut = new Vector2(34f, 28f);
        public static readonly Vector2 RockArch = new Vector2(42f, -16f);
        public static readonly Vector2 Ridge = new Vector2(-16f, 46f);
        public static readonly Vector2 Summit = new Vector2(8f, 46f);

        public const float SpringRadius = 8f;
        public const float SpringDepth = 1.1f;
        /// <summary>山頂の高さ（ふもとからの、だいたいの高さ）。</summary>
        public const float SummitRise = 32f;

        static float SmoothStep(float a, float b, float x) => ShakuMath.SmoothStep(a, b, x);

        /// <summary>山の形（泉・小屋の平らな所・雪渓のくぼみなどの前）。</summary>
        static float BaseHeight(float x, float z)
        {
            // 南から北へ、だんだん高くなる（山頂より北は、また下がる）。尾根（まん中の少し東）が高く、東西へ下がる
            float k = SmoothStep(-58f, 50f, z) * (1f - 0.6f * SmoothStep(50f, 110f, z));
            float ridgeX = 4f + 6f * Mathf.Sin(z * 0.05f);
            float ridge = Mathf.Exp(-Mathf.Pow((x - ridgeX) / 46f, 2f));
            float h = SummitRise * 0.78f * Mathf.Pow(k, 1.25f) * (0.55f + 0.45f * ridge);
            // 山頂の、とがった頂
            h += SummitRise * 0.3f * ShakuMath.Bump(Vector2.Distance(new Vector2(x, z), Summit), 18f);
            // 岩っぽい起伏
            h += 1.6f * ShakuMath.Fbm(x * 0.045f + 7.7f, z * 0.045f + 2.1f, 3) * (0.4f + k);
            // 岩の段：ところどころ、平らな所と、急な岩のがけがくり返す（高い所ほど、はっきり）
            const float step = 3.6f;
            float q = h / step;
            float fl = Mathf.Floor(q);
            float terraced = (fl + SmoothStep(0.25f, 0.75f, q - fl)) * step;
            float tk = 0.7f * SmoothStep(3f, 9f, h) * (0.4f + 0.6f * Mathf.PerlinNoise(x * 0.035f + 5f, z * 0.035f + 9f));
            return Mathf.Lerp(h, terraced, tk);
        }

        static float _springLevel = float.NaN;
        /// <summary>湧き水の泉の水面。</summary>
        public static float SpringLevel
        {
            get
            {
                if (float.IsNaN(_springLevel)) _springLevel = BaseHeight(Spring.x, Spring.y) - 0.35f;
                return _springLevel;
            }
        }

        static float _hutLevel = float.NaN;
        /// <summary>山小屋の建つ、平らな所の高さ。</summary>
        public static float HutLevel
        {
            get
            {
                if (float.IsNaN(_hutLevel)) _hutLevel = BaseHeight(Hut.x, Hut.y);
                return _hutLevel;
            }
        }

        public static float Height(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            float h = BaseHeight(x, z);
            // 小道は、すこし平らに（地形の形は、はじめの道すじのまま。しずくの場所を変えないため）
            h -= GenTrailMask(x, z) * 0.12f;
            // 山小屋の平らな所
            h = Mathf.Lerp(h, HutLevel, SmoothStep(11f, 7f, Vector2.Distance(p, Hut)));
            // 雪渓のくぼみ（日かげの谷）
            h -= 1.8f * ShakuMath.Bump(Vector2.Distance(p, SnowPatch), 12f);
            // 湧き水の泉：岸はなだらかで、まん中は深め
            float sd = Vector2.Distance(p, Spring);
            float bed = SpringLevel - SpringDepth * SmoothStep(SpringRadius, SpringRadius * 0.35f, sd) - 0.25f;
            h = Mathf.Lerp(h, bed, SmoothStep(SpringRadius + 2.5f, SpringRadius - 0.5f, sd));
            // ふもとのトンネルのまわりは、平らに
            h = Mathf.Lerp(h, BaseHeight(Gate.x, Gate.y + 6f), SmoothStep(12f, 6f, Vector2.Distance(p, Gate + new Vector2(0f, 4f))));
            // 外周は少しせり上がる（山頂から見ると、まわりは低い）
            h += 10f * Mathf.Pow(SmoothStep(60f, 96f, r), 1.4f);
            return h;
        }

        public static Vector3 Normal(float x, float z)
        {
            const float e = 0.3f;
            float hx = Height(x + e, z) - Height(x - e, z);
            float hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        public static Vector3 Ground(float x, float z) => new Vector3(x, Height(x, z), z);

        /// <summary>泉の中か（margin だけ外まで）。</summary>
        public static bool InSpring(float x, float z, float margin) => Vector2.Distance(new Vector2(x, z), Spring) < SpringRadius + margin;

        public static float WaterLevelAt(float x, float z) => InSpring(x, z, 2f) ? SpringLevel : -999f;

        public static bool IsUnderwater(Vector3 p) => InSpring(p.x, p.z, 1.5f) && p.y < SpringLevel - AreaLayout.WadeDepth;

        public static bool InPlayArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;

        /// <summary>雪の残る所（雪渓）。</summary>
        public static float SnowMask(float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), SnowPatch);
            float wob = (Mathf.PerlinNoise(x * 0.2f + 3f, z * 0.2f + 8f) - 0.5f) * 4f;
            return SmoothStep(9f, 6f, d + wob);
        }

        /// <summary>岩はだ（高い所・急な所）の度合い。</summary>
        public static float RockMask(float x, float z, float h, Vector3 n)
        {
            float high = SmoothStep(SummitRise * 0.55f, SummitRise * 0.95f, h);
            float steep = SmoothStep(0.88f, 0.7f, n.y);
            return Mathf.Clamp01(Mathf.Max(high * 0.8f, steep));
        }

        // ------------------------------------------------------------------
        // 小道
        // ------------------------------------------------------------------
        static List<Vector2[]> _trails, _genTrails;
        static Vector2 Fork => new Vector2(4f, -6f);

        /// <summary>
        /// はじめの道すじ。地形の形と、しずくを置く前の物の置き方（乱数の使い方）は、この道で決まる
        /// （しずくの場所を変えないよう、あとから道を付けかえても、こちらは変えない）。
        /// </summary>
        public static List<Vector2[]> GenTrails
        {
            get
            {
                if (_genTrails == null)
                {
                    Vector2 fork = Fork;
                    _genTrails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0f, -54f), Trailhead, new Vector2(3f, -34f), RockStairs + new Vector2(-1f, -4f), RockStairs + new Vector2(1f, 5f), fork },
                        new[] { fork, new Vector2(-12f, -9f), Spring + new Vector2(9.5f, 1f) },
                        new[] { fork, new Vector2(14f, -2f), Meadow + new Vector2(-6f, -1f) },
                        new[] { Meadow + new Vector2(-6f, -1f), new Vector2(30f, 14f), Hut + new Vector2(-6f, -5f) },
                        new[] { Hut + new Vector2(-6f, -5f), new Vector2(22f, 38f), Summit + new Vector2(4f, -3f) },
                        new[] { fork, new Vector2(-4f, 6f), Pine + new Vector2(3f, -4f) },
                        new[] { Pine + new Vector2(3f, -4f), new Vector2(-18f, 22f), SnowPatch + new Vector2(8f, -2f) },
                        new[] { SnowPatch + new Vector2(8f, -2f), new Vector2(-22f, 40f), Ridge + new Vector2(2f, -2f), Summit + new Vector2(-4f, -1f) },
                        new[] { Meadow + new Vector2(6f, -6f), new Vector2(38f, -8f), RockArch + new Vector2(-4f, 2f) },
                    };
                }
                return _genTrails;
            }
        }

        /// <summary>
        /// 歩く小道（見える道）。遊びやすいように付けかえた道：岩の階段の西を通って階段へは短い道でよる・山小屋のかべをよけて西をまわる・
        /// 岩のトンネルをくぐりぬけて東へ出る。
        /// </summary>
        public static List<Vector2[]> Trails
        {
            get
            {
                if (_trails == null)
                {
                    Vector2 fork = Fork;
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0f, -54f), Trailhead, new Vector2(2f, -34f), new Vector2(1.5f, -27f), new Vector2(2.8f, -19f), fork },
                        new[] { fork, new Vector2(-12f, -9f), Spring + new Vector2(9.5f, 1f) },
                        new[] { fork, new Vector2(14f, -2f), Meadow + new Vector2(-6f, -1f) },
                        new[] { Meadow + new Vector2(-6f, -1f), new Vector2(30f, 14f), Hut + new Vector2(-6f, -5f) },
                        new[] { Hut + new Vector2(-6f, -5f), new Vector2(21f, 26f), new Vector2(18.5f, 32f), new Vector2(21f, 38.5f), Summit + new Vector2(4f, -3f) },
                        new[] { fork, new Vector2(-4f, 6f), Pine + new Vector2(3f, -4f) },
                        new[] { Pine + new Vector2(3f, -4f), new Vector2(-11f, 5.5f), new Vector2(-18f, 9f), new Vector2(-19.5f, 16f), new Vector2(-18f, 22f), SnowPatch + new Vector2(8f, -2f) },   // 松の幹と根をよけて、南西をまわる
                        new[] { SnowPatch + new Vector2(8f, -2f), new Vector2(-22f, 40f), Ridge + new Vector2(2f, -2f), Summit + new Vector2(-4f, -1f) },
                        new[] { Meadow + new Vector2(-6f, -1f), new Vector2(26f, -1f), Meadow + new Vector2(6f, -6f), new Vector2(38f, -8f), RockArch + new Vector2(-4f, 1f), RockArch, RockArch + new Vector2(7f, -1f) },
                        new[] { new Vector2(2f, -34f), StairsFoot },   // 岩の階段へよる、短い道
                    };
                }
                return _trails;
            }
        }

        /// <summary>岩の階段のいちばん下の段の前（短い道の先）。</summary>
        public static Vector2 StairsFoot => new Vector2(7.1f, -35.1f);

        static float MaskOf(List<Vector2[]> trails, float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float best = 99f;
            foreach (var t in trails)
                for (int i = 0; i < t.Length - 1; i++)
                    best = Mathf.Min(best, ShakuMath.DistToSegment(p, t[i], t[i + 1]));
            float wob = (Mathf.PerlinNoise(x * 0.3f + 40f, z * 0.3f + 7f) - 0.5f) * 0.6f;
            return SmoothStep(2.2f, 1.2f, best + wob);
        }

        /// <summary>歩く小道（見える道）の上か。</summary>
        public static float TrailMask(float x, float z) => MaskOf(Trails, x, z);

        /// <summary>はじめの道すじの上か（地形と、しずくを置く前の物の置き方だけに使う）。</summary>
        public static float GenTrailMask(float x, float z) => MaskOf(GenTrails, x, z);

        /// <summary>歩く小道のまん中の線までの距離。</summary>
        public static float DistToTrail(Vector2 p)
        {
            float best = 99f;
            foreach (var t in Trails)
                for (int i = 0; i < t.Length - 1; i++)
                    best = Mathf.Min(best, ShakuMath.DistToSegment(p, t[i], t[i + 1]));
            return best;
        }

        // ------------------------------------------------------------------
        // 名所
        // ------------------------------------------------------------------
        static List<LandmarkDef> _landmarks;

        public static List<LandmarkDef> Landmarks
        {
            get
            {
                if (_landmarks == null)
                {
                    _landmarks = new List<LandmarkDef>
                    {
                        new LandmarkDef { id = 30, areaId = "mountain", name = "山のふもと", english = "Trailhead", description = "木の根のトンネルをぬけると、ひんやりした山の空気。見上げると、雲のむこうに山頂が見える。",
                            position = Trailhead, radius = 9f, mapColor = new Color(0.6f, 0.8f, 0.55f) },
                        new LandmarkDef { id = 31, areaId = "mountain", name = "岩の階段", english = "Rock Stairs", description = "大きな岩が、段々にかさなっている。ひとつずつ登っていこう。",
                            position = RockStairs, radius = 7f, minHeightAboveGround = 3f, mapColor = new Color(0.62f, 0.62f, 0.66f) },
                        new LandmarkDef { id = 32, areaId = "mountain", name = "湧き水の泉", english = "Mountain Spring", description = "岩のすきまから、つめたい水がこんこんとわき出ている。のぞきこむと、底の小石まで見える。",
                            position = Spring, radius = 11f, mapColor = new Color(0.4f, 0.75f, 0.95f) },
                        new LandmarkDef { id = 33, areaId = "mountain", name = "高山の花畑", english = "Alpine Meadow", description = "みじかい夏に、いっせいにさく花たち。コマクサやチングルマが、風にゆれる。",
                            position = Meadow, radius = 12f, mapColor = new Color(0.95f, 0.6f, 0.75f) },
                        new LandmarkDef { id = 34, areaId = "mountain", name = "大きな松", english = "Old Pine", description = "風にたえて、ねじれながら育った松の木。枝の上から、ふもとが見わたせる。",
                            position = Pine, radius = 8f, minHeightAboveGround = 6f, mapColor = new Color(0.3f, 0.5f, 0.35f) },
                        new LandmarkDef { id = 35, areaId = "mountain", name = "雪渓", english = "Snow Patch", description = "夏になっても、とけずに残る雪。つめたい風が、すーっとふいてくる。",
                            position = SnowPatch, radius = 10f, mapColor = new Color(0.92f, 0.95f, 1f) },
                        new LandmarkDef { id = 36, areaId = "mountain", name = "山小屋", english = "Mountain Hut", description = "登山者がひと休みする、小さな木の小屋。屋根の上に登ると、雲が近い。",
                            position = Hut, radius = 8f, mapColor = new Color(0.7f, 0.5f, 0.32f) },
                        new LandmarkDef { id = 37, areaId = "mountain", name = "岩のトンネル", english = "Rock Arch", description = "大きな岩が、アーチのようにかさなってできたトンネル。中は、ひんやり。",
                            position = RockArch, radius = 6f, mapColor = new Color(0.55f, 0.55f, 0.6f) },
                        new LandmarkDef { id = 38, areaId = "mountain", name = "山頂", english = "Summit", description = "ついに山のてっぺん！ 見わたすかぎり、雲の海。小さな体で、よくここまで登ってきたね。",
                            position = Summit, radius = 7f, mapColor = new Color(0.98f, 0.85f, 0.4f) },
                    };
                    // 着いたときの景色（マイナスの角度は、見上げる）
                    _landmarks[0].view = new ArrivalView(new Vector2(0.5f, -50f), new Vector3(Summit.x, 30f, Summit.y), -2f, 4.6f, clear: 40f);   // ふもとから、山頂を見上げる
                    _landmarks[1].view = new ArrivalView(RockStairs + new Vector2(-2f, -10f), new Vector3(RockStairs.x, 8f, RockStairs.y), -4f, 4.6f);
                    _landmarks[2].view = new ArrivalView(Spring + new Vector2(11f, 1f), new Vector3(Spring.x, 2f, Spring.y), 18f, 4.6f);
                    _landmarks[3].view = new ArrivalView(Meadow + new Vector2(-9f, -4f), new Vector3(Meadow.x, 8f, Meadow.y), 6f, 4.4f);
                    _landmarks[4].view = new ArrivalView(Pine + new Vector2(7f, -8f), new Vector3(Pine.x, 18f, Pine.y), -4f, 5f);
                    _landmarks[5].view = new ArrivalView(SnowPatch + new Vector2(10f, -3f), new Vector3(SnowPatch.x, 14f, SnowPatch.y), 10f, 4.6f);
                    _landmarks[6].view = new ArrivalView(Hut + new Vector2(-8f, -9f), new Vector3(Hut.x, 20f, Hut.y), -2f, 5f);
                    _landmarks[7].view = new ArrivalView(RockArch + new Vector2(-6f, 2f), new Vector3(RockArch.x, 12f, RockArch.y), 2f, 4.4f);
                    _landmarks[8].view = new ArrivalView(Summit + new Vector2(0f, -3f), new Vector3(0f, 6f, -40f), 14f, 5f, clear: 40f);   // 山頂から、ふもとを見下ろす
                }
                return _landmarks;
            }
        }

        // ------------------------------------------------------------------
        // 地面の色
        // ------------------------------------------------------------------
        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static readonly Color Grass = C("#8fbf5e");
        static readonly Color GrassDeep = C("#5f9150");
        static readonly Color Alpine = C("#a8bd6a");
        static readonly Color Rock = C("#8c8a88");
        static readonly Color RockDark = C("#6e6c6c");
        static readonly Color Path = C("#bba383");
        static readonly Color Snow = C("#eef3f8");
        static readonly Color SnowShade = C("#c9d6e6");
        static readonly Color WetSoil = C("#5e5a48");
        static readonly Color Meadow1 = C("#9bc865");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float n1 = Mathf.PerlinNoise(x * 0.05f + 2f, z * 0.05f + 11f);
            float n2 = Mathf.PerlinNoise(x * 0.25f + 9f, z * 0.25f + 4f);
            // ふもとは緑、高くなるほど、黄緑の高山の草
            float up = SmoothStep(4f, SummitRise * 0.7f, h);
            Color c = Color.Lerp(Color.Lerp(GrassDeep, Grass, n1), Color.Lerp(Alpine * 0.92f, Alpine, n1), up);
            // 花畑は、明るい緑
            c = Color.Lerp(c, Meadow1, SmoothStep(14f, 8f, Vector2.Distance(new Vector2(x, z), Meadow)) * 0.6f);
            // 岩はだ
            c = Color.Lerp(c, Color.Lerp(RockDark, Rock, n2), RockMask(x, z, h, n) * (0.55f + 0.45f * n1));
            // 小道
            c = Color.Lerp(c, Color.Lerp(Path, Path * 0.9f, n2), TrailMask(x, z) * 0.75f);
            // 泉のまわりは、ぬれた土
            float sd = Vector2.Distance(new Vector2(x, z), Spring);
            c = Color.Lerp(c, WetSoil, SmoothStep(SpringRadius + 3f, SpringRadius + 0.5f, sd) * 0.7f);
            // 雪渓
            c = Color.Lerp(c, Color.Lerp(SnowShade, Snow, n2 * 0.6f + 0.4f), SnowMask(x, z));
            float r = new Vector2(x, z).magnitude;
            c = Color.Lerp(c, Color.Lerp(RockDark, GrassDeep, n1) * 0.85f, SmoothStep(60f, 92f, r));
            c.a = 1f;
            return c;
        }
    }
}

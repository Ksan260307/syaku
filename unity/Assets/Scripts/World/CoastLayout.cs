using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 海岸エリア「潮風の海辺」のレイアウト。
    /// 北のはし（川辺へもどる木の根のトンネル）から、砂丘をこえて、南の海へゆるやかに下る。
    /// 東は海につき出した磯の岩場（潮だまり・岬の先）、西は小川が海へそそぐ干潟。砂浜には大きな巻き貝と、白い流木。
    /// 海の水面は 0。波打ちぎわから沖へ、だんだん深くなる。
    /// </summary>
    public static class CoastLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 40f;

        /// <summary>海の水面の高さ。</summary>
        public const float SeaLevel = 0f;
        /// <summary>潮だまりの水面の高さ（岩場の上のくぼみ）。</summary>
        public const float PoolLevel = 0.9f;

        /// <summary>川辺へもどる木の根のトンネル（北のはし）</summary>
        public static readonly Vector2 Gate = new Vector2(-6f, 58.5f);
        public static readonly Vector2 Spawn = new Vector2(-5.5f, 51f);

        public static readonly Vector2 Entrance = new Vector2(-6f, 44f);
        public static readonly Vector2 DuneFlowers = new Vector2(-30f, 32f);
        public static readonly Vector2 Driftwood = new Vector2(14f, 18f);
        public static readonly Vector2 Conch = new Vector2(-12f, -1f);
        public static readonly Vector2 Surf = new Vector2(4f, -8f);
        public static readonly Vector2 TidePool = new Vector2(36f, -10f);
        public static readonly Vector2 Reef = new Vector2(39f, 3f);
        public static readonly Vector2 Cape = new Vector2(51f, -28f);
        public static readonly Vector2 Mudflat = new Vector2(-42f, -2f);

        /// <summary>マンタが泳ぐ、沖の輪（まん中と、東西・南北の半径）。浅い所には来ない。</summary>
        public static readonly Vector2 MantaBay = new Vector2(6f, -40f);
        public static readonly Vector2 MantaLoop = new Vector2(18f, 9f);

        /// <summary>遠くの灯台の小島（遊べる場所のずっと外の、沖）。</summary>
        public static readonly Vector2 Lighthouse = new Vector2(96f, -165f);

        static float SmoothStep(float a, float b, float x) => ShakuMath.SmoothStep(a, b, x);

        // ------------------------------------------------------------------
        // 海岸線
        // ------------------------------------------------------------------
        /// <summary>波打ちぎわ（水面 0 の線）の z。ゆるく波打って、東西にのびる。</summary>
        public static float ShoreZ(float x) => -14f + 3.5f * Mathf.Sin(x * 0.065f + 0.4f) + 2f * Mathf.Sin(x * 0.16f + 1.9f);

        /// <summary>波打ちぎわから陸の方へのきょり（海の中はマイナス）。</summary>
        public static float Inland(float x, float z) => z - ShoreZ(x);

        // ------------------------------------------------------------------
        // 磯の岩場（東）：陸から海へ、南東につき出した平たい岩の台。上に潮だまり、先は岬
        // ------------------------------------------------------------------
        public static readonly Vector2 ShelfA = new Vector2(28f, 8f);
        public static readonly Vector2 ShelfB = new Vector2(54f, -32f);

        /// <summary>岩場のまん中の線からのきょり（ふちは少しでこぼこ）。</summary>
        public static float ShelfDist(float x, float z)
            => ShakuMath.DistToSegment(new Vector2(x, z), ShelfA, ShelfB) + 1.4f * ShakuMath.Fbm(x * 0.09f + 11f, z * 0.09f + 3f, 2);

        /// <summary>岩場の上か（0..1）。</summary>
        public static float ShelfMask(float x, float z) => SmoothStep(12f, 8.5f, ShelfDist(x, z));

        static float ShelfTop(float x, float z)
            => 1.35f + 0.3f * ShakuMath.Fbm(x * 0.13f + 3f, z * 0.13f + 7f, 3) + 0.12f * (Mathf.PerlinNoise(x * 0.6f + 1f, z * 0.6f + 5f) - 0.5f);

        /// <summary>潮だまり（中心・半径）。</summary>
        public static readonly (Vector2 c, float r)[] Pools =
        {
            (new Vector2(36f, -10f), 3.9f),
            (new Vector2(42f, -20f), 3.0f),
            (new Vector2(31.5f, -2.5f), 2.4f),
        };

        /// <summary>潮だまりの中か（margin だけ外まで）。いちばん近い潮だまりの番号（なければ -1）。</summary>
        public static int PoolAt(float x, float z, float margin)
        {
            for (int i = 0; i < Pools.Length; i++)
                if (Vector2.Distance(new Vector2(x, z), Pools[i].c) < Pools[i].r + margin) return i;
            return -1;
        }

        // ------------------------------------------------------------------
        // 小川と干潟（西）：北西から小川が流れてきて、干潟をうねって、海へそそぐ
        // ------------------------------------------------------------------
        public static readonly Vector2[] Creek =
        {
            new Vector2(-78f, 28f), new Vector2(-58f, 13f), new Vector2(-47f, 1f), new Vector2(-42.5f, -11f), new Vector2(-40f, -26f),
        };

        /// <summary>小川の、歩いてわたれる浅瀬。</summary>
        public static readonly Vector2[] Fords = { new Vector2(-52f, 7f), new Vector2(-44f, -6f) };

        public const float CreekHalfWidth = 2.2f;

        public static float CreekDist(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float best = 99f;
            for (int i = 0; i < Creek.Length - 1; i++)
                best = Mathf.Min(best, ShakuMath.DistToSegment(p, Creek[i], Creek[i + 1]));
            return best;
        }

        /// <summary>干潟のどろの所か（0..1）。</summary>
        public static float MudMask(float x, float z)
        {
            float wob = 2f * ShakuMath.Fbm(x * 0.07f + 5f, z * 0.07f + 1f, 2);
            return SmoothStep(13f, 8f, CreekDist(x, z) + wob) * SmoothStep(30f, 20f, Inland(x, z)) * SmoothStep(-3f, 1f, Inland(x, z));
        }

        // ------------------------------------------------------------------
        // 地形
        // ------------------------------------------------------------------
        static float BaseHeight(float x, float z)
        {
            float d = Inland(x, z);
            float h;
            if (d >= 0f)
            {
                // 砂浜：波打ちぎわから、ゆるやかにのぼる（20 ほどで平らに）
                h = 0.07f * (d - ShakuMath.SoftRamp(d - 20f, 8f));
                // 砂丘：砂浜のうしろに、なだらかな砂の山
                float dune = 2.4f * SmoothStep(16f, 32f, d) * (0.7f + 0.6f * Mathf.PerlinNoise(x * 0.045f + 3f, z * 0.045f + 1f));
                float hum = 0.9f * ShakuMath.Fbm(x * 0.09f + 21f, z * 0.09f + 4f, 3) * SmoothStep(14f, 28f, d);
                h += dune + hum;
                // 砂丘のむこうは、草の原（北へ、すこしずつ高く）
                h += 0.05f * ShakuMath.SoftRamp(d - 40f, 10f);
            }
            else
            {
                // 海の中：沖へ、だんだん深く
                h = -6.5f * (1f - Mathf.Exp(d * 0.11f / 6.5f));
                h += 0.05f * Mathf.Sin(x * 0.9f + Mathf.Sin(z * 0.3f) * 2f) * SmoothStep(0f, -6f, d);   // 砂の波もよう
            }
            return h;
        }

        public static float Height(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            float h = BaseHeight(x, z);
            float d = Inland(x, z);

            // 磯の岩場：平らな岩の台（ふちは海へ、がけになって落ちる）
            float sk = ShelfMask(x, z);
            if (sk > 0f) h = Mathf.Max(h, Mathf.Lerp(h, ShelfTop(x, z), sk));
            // 潮だまり：岩の台のくぼみ。ふちは水面より少し高く、まん中は深め
            for (int i = 0; i < Pools.Length; i++)
            {
                float pd = Vector2.Distance(p, Pools[i].c);
                float pr = Pools[i].r;
                if (pd > pr + 2f) continue;
                float bed = PoolLevel - 0.6f * SmoothStep(pr, pr * 0.3f, pd) - 0.08f;
                h = Mathf.Lerp(h, bed, SmoothStep(pr + 0.2f, pr - 0.5f, pd));
                // ふちは、かならず水面より高く（岩の台が低い所でも、水がこぼれない）
                float rim = SmoothStep(pr - 0.3f, pr + 0.2f, pd) * SmoothStep(pr + 2f, pr + 1.2f, pd);
                if (rim > 0f) h = Mathf.Max(h, Mathf.Lerp(h, PoolLevel + 0.14f, rim));
            }

            // 干潟：小川のまわりの、平らなどろ
            float mk = MudMask(x, z);
            if (mk > 0f) h = Mathf.Lerp(h, 0.13f + 0.05f * ShakuMath.Fbm(x * 0.25f, z * 0.25f + 9f, 2), mk);
            // 小川：まん中は少し深く、浅瀬はわたれる
            float cd = CreekDist(x, z);
            if (cd < CreekHalfWidth + 4f)
            {
                float ford = 0f;
                foreach (var f in Fords) ford = Mathf.Max(ford, SmoothStep(4f, 1.5f, Vector2.Distance(p, f)));
                float depth = Mathf.Lerp(0.34f, 0.05f, ford);
                float bed = SeaLevel - depth * SmoothStep(CreekHalfWidth, CreekHalfWidth * 0.3f, cd) - 0.03f;
                float k = SmoothStep(CreekHalfWidth + 3.5f, CreekHalfWidth - 0.3f, cd);
                h = Mathf.Min(h, Mathf.Lerp(h, bed, k));
            }

            // トンネルのまわりは、平らに
            h = Mathf.Lerp(h, BaseHeight(Gate.x, Gate.y - 6f), SmoothStep(12f, 6f, Vector2.Distance(p, Gate + new Vector2(0f, -4f))));
            // 外周の陸の側は、少しせり上がる（海の側は、そのまま沖へ）
            h += 10f * Mathf.Pow(SmoothStep(60f, 96f, r), 1.4f) * SmoothStep(-2f, 14f, d);
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

        /// <summary>外周リング（地形の外）の高さ：陸の側はせり上がり、海の側は沖へ深くなる。</summary>
        public static float OuterHeight(float x, float z)
        {
            float r = new Vector2(x, z).magnitude;
            float k = 96f / Mathf.Max(r, 96f);
            float edge = Height(x * k, z * k);
            float land = SmoothStep(-6f, 18f, Inland(x, z));
            float rise = (r - 96f) * 0.12f + (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) - 0.5f) * 6f;
            float sea = Mathf.Min(edge, -6f) - (r - 96f) * 0.03f;
            return Mathf.Lerp(sea, edge + rise, land);
        }

        public static float WaterLevelAt(float x, float z) => PoolAt(x, z, 0.8f) >= 0 ? PoolLevel : SeaLevel;

        public static bool IsUnderwater(Vector3 p)
        {
            if (PoolAt(p.x, p.z, 0.6f) >= 0) return p.y < PoolLevel - AreaLayout.WadeDepth;
            return p.y < SeaLevel - AreaLayout.WadeDepth;
        }

        public static bool InPlayArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;

        // ------------------------------------------------------------------
        // 小道（ふみかためた砂の道）
        // ------------------------------------------------------------------
        static List<Vector2[]> _trails;

        public static List<Vector2[]> Trails
        {
            get
            {
                if (_trails == null)
                {
                    Vector2 fork = new Vector2(-7f, 35f);
                    Vector2 low = new Vector2(-11f, 14f);
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(-6f, 54f), Entrance, fork, new Vector2(-9.5f, 25f), low, Conch + new Vector2(2f, 6f) },
                        new[] { fork, new Vector2(4f, 30f), Driftwood + new Vector2(-6f, 1f) },
                        new[] { Driftwood + new Vector2(-6f, 1f), new Vector2(18f, 9f), new Vector2(26f, 6f), Reef + new Vector2(-7f, 1f) },
                        new[] { fork, new Vector2(-18f, 33f), DuneFlowers + new Vector2(6f, 0f) },
                        new[] { low, new Vector2(-24f, 8f), Mudflat + new Vector2(8f, 3f) },
                    };
                }
                return _trails;
            }
        }

        public static float TrailMask(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float best = 99f;
            foreach (var t in Trails)
                for (int i = 0; i < t.Length - 1; i++)
                    best = Mathf.Min(best, ShakuMath.DistToSegment(p, t[i], t[i + 1]));
            float wob = (Mathf.PerlinNoise(x * 0.3f + 50f, z * 0.3f + 3f) - 0.5f) * 0.8f;
            return SmoothStep(2.0f, 1.0f, best + wob);
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
                        new LandmarkDef { id = 50, areaId = "coast", name = "海辺の入り口", english = "Seaside Gate", description = "木の根のトンネルをぬけると、潮のかおり。砂丘のむこうに、青い海が広がっている。",
                            position = Entrance, radius = 9f, mapColor = new Color(0.95f, 0.88f, 0.65f) },
                        new LandmarkDef { id = 51, areaId = "coast", name = "砂丘の花", english = "Dune Blossoms", description = "砂の上を、ハマヒルガオのつるがはう。うすいピンクの花が、海風にゆれる。",
                            position = DuneFlowers, radius = 10f, mapColor = new Color(0.98f, 0.7f, 0.8f) },
                        new LandmarkDef { id = 52, areaId = "coast", name = "流木の丘", english = "Driftwood Hill", description = "遠い海から流れついた、白くて大きな流木。上にのぼると、海がよく見える。",
                            position = Driftwood, radius = 9f, minHeightAboveGround = 1.5f, mapColor = new Color(0.86f, 0.84f, 0.78f) },
                        new LandmarkDef { id = 53, areaId = "coast", name = "大きな巻き貝", english = "Giant Seashell", description = "砂浜にころがる、大きな巻き貝の殻。口から中へ入ると、波の音がひびいて聞こえる。",
                            position = Conch, radius = 7f, mapColor = new Color(0.95f, 0.75f, 0.6f) },
                        new LandmarkDef { id = 54, areaId = "coast", name = "波打ちぎわ", english = "Water's Edge", description = "よせては返す、小さな波。ぬれた砂に、波のあとがのこっている。",
                            position = Surf, radius = 10f, mapColor = new Color(0.55f, 0.85f, 0.95f) },
                        new LandmarkDef { id = 55, areaId = "coast", name = "潮だまり", english = "Tide Pool", description = "岩のくぼみに、海の水がのこった小さな池。ヒトデやウニが、のんびりくらしている。",
                            position = TidePool, radius = 7f, mapColor = new Color(0.35f, 0.7f, 0.8f) },
                        new LandmarkDef { id = 56, areaId = "coast", name = "磯の岩場", english = "Rocky Shore", description = "フジツボと海藻のついた、黒っぽい岩がならぶ。岩のすきまを、フナムシがかけていく。",
                            position = Reef, radius = 8f, mapColor = new Color(0.45f, 0.45f, 0.42f) },
                        new LandmarkDef { id = 57, areaId = "coast", name = "岬の先", english = "Cape Point", description = "海につき出した、岩のはし。まわりは、ぜんぶ海。遠くの小島に、灯台が見える。",
                            position = Cape, radius = 6f, minHeightAboveGround = 2.5f, mapColor = new Color(0.6f, 0.6f, 0.65f) },
                        new LandmarkDef { id = 58, areaId = "coast", name = "干潟", english = "Mudflat", description = "小川が海へそそぐ、どろの広場。小さな穴から、カニがはさみを出している。",
                            position = Mudflat, radius = 10f, mapColor = new Color(0.62f, 0.55f, 0.45f) },
                    };
                    // 着いたときの景色（マイナスの角度は、見上げる）
                    Vector3 sea = new Vector3(0f, 0f, -40f);
                    _landmarks[0].view = new ArrivalView(new Vector2(-5.5f, 49f), sea, 6f, 4.6f, clear: 40f);   // 砂丘のむこうに、海
                    _landmarks[1].view = new ArrivalView(DuneFlowers + new Vector2(8f, -3f), new Vector3(DuneFlowers.x, 3.5f, DuneFlowers.y), 12f, 4.2f);
                    _landmarks[2].view = new ArrivalView(Driftwood + new Vector2(-9f, -5f), new Vector3(Driftwood.x, 4.5f, Driftwood.y), 2f, 4.8f);
                    _landmarks[3].view = new ArrivalView(Conch + new Vector2(3f, 10f), new Vector3(Conch.x, 2.5f, Conch.y), 6f, 4.6f);
                    _landmarks[4].view = new ArrivalView(Surf + new Vector2(0f, 5f), new Vector3(Surf.x, 0f, Surf.y - 30f), 4f, 4.2f, clear: 30f);   // 波打ちぎわから、沖を見る
                    _landmarks[5].view = new ArrivalView(TidePool + new Vector2(-6.5f, 3f), new Vector3(TidePool.x, PoolLevel, TidePool.y), 26f, 4.4f);   // 潮だまりをのぞきこむ
                    _landmarks[6].view = new ArrivalView(Reef + new Vector2(-10f, 3f), new Vector3(Reef.x, 3f, Reef.y), 4f, 4.6f);
                    _landmarks[7].view = new ArrivalView(Cape, new Vector3(Lighthouse.x, 14f, Lighthouse.y), 4f, 4.4f, onTop: true, clear: 0f);   // 岬の上から、沖の灯台
                    _landmarks[8].view = new ArrivalView(Mudflat + new Vector2(9f, 5f), new Vector3(Mudflat.x, 0f, Mudflat.y), 16f, 4.4f);
                    // 地図や図鑑から移動したときに始める所（カメラが地面や物にぶつからず、名所がよく見える、少し上からの景色）
                    _landmarks[3].travel = new ArrivalView(Conch + new Vector2(2f, 13f), new Vector3(Conch.x, 0f, Conch.y), 10f, 5f);
                    _landmarks[5].travel = new ArrivalView(TidePool + new Vector2(-3.8f, 4f), new Vector3(TidePool.x, 0f, TidePool.y), 30f, 4.6f);   // 潮だまりのふちから、のぞきこむ
                    _landmarks[6].travel = new ArrivalView(new Vector2(30.5f, 10.5f), new Vector3(Reef.x, 0f, Reef.y), 8f, 4.6f);
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

        static readonly Color DrySand = C("#e4d2a4");
        static readonly Color DrySand2 = C("#d8c293");
        static readonly Color WetSand = C("#a8916a");
        static readonly Color SeaSand = C("#c9b58c");
        static readonly Color DuneGrass = C("#9db066");
        static readonly Color Grass = C("#7da457");
        static readonly Color Rock = C("#5d5955");
        static readonly Color RockLight = C("#7f7a72");
        static readonly Color Algae = C("#55703a");
        static readonly Color Mud = C("#7b6c57");
        static readonly Color MudWet = C("#5c5144");
        static readonly Color Path = C("#cdb88c");
        static readonly Color PoolBed = C("#c8b496");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float n1 = Mathf.PerlinNoise(x * 0.06f + 2f, z * 0.06f + 11f);
            float n2 = Mathf.PerlinNoise(x * 0.3f + 9f, z * 0.3f + 4f);
            float d = Inland(x, z);
            Color c = Color.Lerp(DrySand2, DrySand, ShakuMath.SmoothStep(0.25f, 0.75f, n1 * 0.7f + n2 * 0.3f));
            // 砂丘の草（ところどころ、緑の株が広がる）と、そのむこうの草の原
            float grassy = ShakuMath.SmoothStep(18f, 30f, d) * ShakuMath.SmoothStep(0.45f, 0.65f, Mathf.PerlinNoise(x * 0.11f + 7f, z * 0.11f + 2f));
            c = Color.Lerp(c, Color.Lerp(DuneGrass, DrySand, n2 * 0.4f), grassy * 0.75f);
            c = Color.Lerp(c, Color.Lerp(Grass, DuneGrass, n1), ShakuMath.SmoothStep(38f, 50f, d));
            // ぬれた砂（波がとどく所）と、海の中の砂
            float wetLine = 2.6f + 0.8f * Mathf.Sin(x * 0.4f + n1 * 3f);
            c = Color.Lerp(c, WetSand, ShakuMath.SmoothStep(wetLine, 0.2f, d) * 0.85f);
            if (h < 0f) c = Color.Lerp(WetSand, SeaSand, ShakuMath.SmoothStep(0f, -1.5f, h));
            // 干潟のどろ
            float mk = MudMask(x, z);
            c = Color.Lerp(c, Color.Lerp(MudWet, Mud, n2), mk);
            // 磯の岩（水ぎわは、海藻で緑がかる）
            float rk = ShelfMask(x, z) * ShakuMath.SmoothStep(-0.6f, 0.2f, h) + ShakuMath.SmoothStep(0.75f, 0.5f, n.y) * ShakuMath.SmoothStep(4f, 0f, ShelfDist(x, z) - 10f);
            Color rock = Color.Lerp(Rock, RockLight, ShakuMath.SmoothStep(0.3f, 0.8f, n2) * 0.7f);
            rock = Color.Lerp(rock, Algae, ShakuMath.SmoothStep(1.0f, 0.35f, h) * 0.7f);
            c = Color.Lerp(c, rock, Mathf.Clamp01(rk));
            // 潮だまりの底は、明るい砂と貝のかけら（底のいきものが見える）
            if (PoolAt(x, z, 0f) >= 0) c = Color.Lerp(c, Color.Lerp(PoolBed, PoolBed * 0.85f, n2), ShakuMath.SmoothStep(PoolLevel - 0.02f, PoolLevel - 0.2f, h));
            // 小道（ふみかためた砂）
            c = Color.Lerp(c, Color.Lerp(Path, DrySand2, n2 * 0.4f), TrailMask(x, z) * 0.5f);
            float r = new Vector2(x, z).magnitude;
            c = Color.Lerp(c, c * 0.8f, ShakuMath.SmoothStep(62f, 92f, r) * ShakuMath.SmoothStep(-2f, 10f, d));
            c.a = 1f;
            return c;
        }
    }
}

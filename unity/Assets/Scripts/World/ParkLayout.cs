using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 公園エリア「ひだまり公園」のレイアウト。
    /// しばふの広場に、すべり台・ブランコ・シーソー・ジャングルジム・砂場・どかん・タイヤ・ベンチ・水飲み場・花だん。
    /// 西のすみに大きなクヌギの木。東の端に、森へもどる木の根のトンネル。
    /// 遊具はしゃくとりむしから見て大きすぎないよう、おもちゃのような大きさにしてある。
    /// </summary>
    public static class ParkLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 70f;

        /// <summary>森へもどる木の根のトンネル（東の端）</summary>
        public static readonly Vector2 Gate = new Vector2(58.5f, -6f);
        public static readonly Vector2 Spawn = new Vector2(51f, -5.5f);

        // 遊具（まん中の場所）
        public static readonly Vector2 Slide = new Vector2(-28f, 12f);        // 台のまん中。すべる先は東（+X）
        public static readonly Vector2 Swing = new Vector2(20f, 28f);         // わくのまん中。棒は東西
        public static readonly Vector2 Seesaw = new Vector2(28f, -18f);       // 板は東西
        public static readonly Vector2 JungleGym = new Vector2(-28f, -18f);
        public static readonly Vector2 Sandbox = new Vector2(-2f, -38f);      // わく 26 × 18
        public static readonly Vector2 FlowerBed = new Vector2(-4f, 38f);     // れんが 22 × 9
        public static readonly Vector2 Kunugi = new Vector2(-48f, 34f);
        public static readonly Vector2 Dokan = new Vector2(20f, -42f);        // 土管は東西
        public static readonly Vector2 Fountain = new Vector2(8f, 4f);
        public static readonly Vector2 Puddle = new Vector2(12f, 10f);
        public static readonly Vector2 Bench = new Vector2(40f, 10f);
        public static readonly Vector2 Tires = new Vector2(36f, -26f);
        public static readonly Vector2 Lamp = new Vector2(-10f, 4f);

        public const float PuddleRadius = 5f;
        /// <summary>水たまりのいちばん深い所（しゃくとりむしが沈まない浅さ）。</summary>
        public const float PuddleDepth = 0.09f;
        public const float WaterLevel = -0.35f;
        public static readonly Vector2 SandboxSize = new Vector2(26f, 18f);
        public static readonly Vector2 BedSize = new Vector2(22f, 9f);

        static float InRect(float x, float z, Vector2 c, Vector2 size, float soft)
        {
            float dx = Mathf.Abs(x - c.x) - size.x * 0.5f;
            float dz = Mathf.Abs(z - c.y) - size.y * 0.5f;
            return ShakuMath.SmoothStep(soft, -soft, Mathf.Max(dx, dz));
        }

        /// <summary>すべり台の下は、平らにならしてある（出口まで地面にのる）。</summary>
        static float _slideBase = float.NaN;

        public static float Height(float x, float z)
        {
            float h = RawHeight(x, z);
            if (float.IsNaN(_slideBase)) _slideBase = RawHeight(Slide.x, Slide.y);
            // すべり台（台・はしご・坂・出口）の下：台のまん中と同じ高さ
            return Mathf.Lerp(h, _slideBase, InRect(x, z, Slide + new Vector2(7.5f, 0f), new Vector2(31f, 7f), 1.2f));
        }

        static float RawHeight(float x, float z)
        {
            float r = new Vector2(x, z).magnitude;
            // ほとんど平らなしばふ（ゆるい起伏）
            float h = 0.25f * (ShakuMath.Fbm(x * 0.025f + 11.1f, z * 0.025f + 3.7f, 3) - 0.5f) * 2f;
            // 砂場の中は少し低い（わくのふちで止まる）
            h -= 0.3f * InRect(x, z, Sandbox, SandboxSize - new Vector2(2f, 2f), 0.4f);
            // 花だんの中は土が盛ってある
            h += 0.75f * InRect(x, z, FlowerBed, BedSize - new Vector2(1.2f, 1.2f), 0.3f);
            // 小道はすこし平らに
            h -= TrailMask(x, z) * 0.05f;
            // 外周はせり上がる（植えこみの土手）
            h += 11f * Mathf.Pow(ShakuMath.SmoothStep(60f, 96f, r), 1.4f);
            // 水たまり（水飲み場のそば）：底は平らで、しゃくとりむしが沈まない浅さ（歩いて入れる）
            float pd = Vector2.Distance(new Vector2(x, z), Puddle);
            float bed = WaterLevel - PuddleDepth * (0.6f + 0.4f * ShakuMath.SmoothStep(PuddleRadius, 0f, pd));
            h = Mathf.Lerp(h, bed, ShakuMath.SmoothStep(PuddleRadius + 1.2f, PuddleRadius - 0.8f, pd));
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

        public static bool InPuddle(float x, float z, float margin) => Vector2.Distance(new Vector2(x, z), Puddle) < PuddleRadius + margin;

        public static float WaterLevelAt(float x, float z) => InPuddle(x, z, 1.5f) ? WaterLevel : -999f;

        /// <summary>水たまりは浅いので、底を歩ける（水の中になるのは、それより深い所だけ）。</summary>
        public static bool IsUnderwater(Vector3 p) => InPuddle(p.x, p.z, 1.5f) && p.y < WaterLevel - AreaLayout.WadeDepth;

        public static bool InPlayArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;

        /// <summary>砂の地面か（砂場の中・ブランコとすべり台とジャングルジムの下）。</summary>
        public static float SandMask(float x, float z)
        {
            float s = InRect(x, z, Sandbox, SandboxSize - new Vector2(2f, 2f), 0.5f);
            s = Mathf.Max(s, ShakuMath.SmoothStep(13f, 10f, Vector2.Distance(new Vector2(x, z), Swing) * new Vector2(1f, 1.6f).magnitude * 0.6f));
            s = Mathf.Max(s, InRect(x, z, Slide + new Vector2(9f, 0f), new Vector2(34f, 9f), 1.5f) * 0.9f);
            s = Mathf.Max(s, ShakuMath.SmoothStep(11f, 8.5f, Vector2.Distance(new Vector2(x, z), JungleGym)) * 0.9f);
            return s;
        }

        // ------------------------------------------------------------------
        // 小道
        // ------------------------------------------------------------------
        static List<Vector2[]> _trails;

        public static List<Vector2[]> Trails
        {
            get
            {
                if (_trails == null)
                {
                    Vector2 hub = new Vector2(4f, -6f);
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(56f, -6f), new Vector2(36f, -4f), new Vector2(20f, -6f), hub },
                        new[] { hub, new Vector2(-6f, 2f), new Vector2(-12f, 14f), new Vector2(-6f, 26f), new Vector2(-4f, 31f) },
                        new[] { hub, new Vector2(-8f, -14f), new Vector2(-6f, -26f) },
                        new[] { hub, new Vector2(14f, 12f), new Vector2(18f, 18f) },
                        new[] { new Vector2(36f, -4f), new Vector2(38f, 4f) },
                        new[] { new Vector2(20f, -6f), new Vector2(24f, -12f) },
                        new[] { new Vector2(-12f, 14f), new Vector2(-34f, 24f), new Vector2(-42f, 30f) },
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
            float wob = (Mathf.PerlinNoise(x * 0.3f + 20f, z * 0.3f + 4f) - 0.5f) * 0.6f;
            return ShakuMath.SmoothStep(2.4f, 1.3f, best + wob);
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
                        new LandmarkDef { id = 20, areaId = "park", name = "公園の入り口", english = "Park Gate", description = "木の根のトンネルをぬけると、ひだまりの公園。人の声が遠くに聞こえる。",
                            position = new Vector2(49f, -5f), radius = 9f, mapColor = new Color(0.6f, 0.9f, 0.5f) },
                        new LandmarkDef { id = 21, areaId = "park", name = "すべり台のてっぺん", english = "Top of the Slide", description = "はしごをのぼって、ぴかぴかの坂をすべりおりよう。",
                            position = Slide, radius = 3.5f, minHeightAboveGround = 8f, mapColor = new Color(0.95f, 0.45f, 0.35f) },
                        new LandmarkDef { id = 22, areaId = "park", name = "ブランコ", english = "Swings", description = "風にゆれる、ふたつの座板。乗ると大きくゆれる。",
                            position = Swing, radius = 11f, mapColor = new Color(0.4f, 0.6f, 0.95f) },
                        new LandmarkDef { id = 23, areaId = "park", name = "シーソー", english = "Seesaw", description = "板の上を歩くと、ぎっこんばったん、かたむく。",
                            position = Seesaw, radius = 9f, mapColor = new Color(0.95f, 0.8f, 0.3f) },
                        new LandmarkDef { id = 24, areaId = "park", name = "ジャングルジムの上", english = "Jungle Gym Summit", description = "色とりどりの棒をのぼりきると、公園がひと目で見わたせる。",
                            position = JungleGym, radius = 6f, minHeightAboveGround = 10f, mapColor = new Color(0.4f, 0.8f, 0.45f) },
                        new LandmarkDef { id = 25, areaId = "park", name = "砂場の砂山", english = "Sandbox Hill", description = "だれかが作った砂の山と、わすれもののバケツ。",
                            position = Sandbox, radius = 12f, mapColor = new Color(0.9f, 0.82f, 0.55f) },
                        new LandmarkDef { id = 26, areaId = "park", name = "チューリップの花だん", english = "Tulip Bed", description = "赤・黄・ピンクのチューリップと、キャベツ。モンシロチョウがやってくる。",
                            position = FlowerBed, radius = 10f, mapColor = new Color(1f, 0.55f, 0.7f) },
                        new LandmarkDef { id = 27, areaId = "park", name = "クヌギの木", english = "Sawtooth Oak", description = "あまい樹液のにおい。夜になると、クワガタやカミキリムシが集まる。",
                            position = Kunugi, radius = 9f, mapColor = new Color(0.55f, 0.4f, 0.28f) },
                        new LandmarkDef { id = 28, areaId = "park", name = "どかんのトンネル", english = "Pipe Tunnel", description = "コンクリートの土管。中を通りぬけると、声がひびく。",
                            position = Dokan, radius = 6f, useCapsule = true, capsuleRadius = 2.6f, mapColor = new Color(0.72f, 0.72f, 0.7f) },
                        new LandmarkDef { id = 29, areaId = "park", name = "水飲み場", english = "Drinking Fountain", description = "じゃぐちからぽたぽた。足もとに小さな水たまりができている。",
                            position = Fountain, radius = 8f, mapColor = new Color(0.5f, 0.8f, 0.95f) },
                    };
                    var pipe = _landmarks[8];
                    pipe.capsuleA = new Vector3(Dokan.x - 4.5f, Height(Dokan.x, Dokan.y) + 1.6f, Dokan.y);
                    pipe.capsuleB = new Vector3(Dokan.x + 4.5f, Height(Dokan.x, Dokan.y) + 1.6f, Dokan.y);

                    // 着いたときの景色（マイナスの角度は、見上げる）
                    _landmarks[0].view = new ArrivalView(new Vector2(49f, -4f), new Vector3(0f, 6f, 4f), 2f, 4.4f, clear: 30f);                     // 入り口から、公園を見わたす
                    _landmarks[1].view = new ArrivalView(Slide, new Vector3(Slide.x + 22f, 0f, Slide.y), 30f, 5.5f, onTop: true);                    // てっぺんから、すべる坂を見下ろす
                    _landmarks[2].view = new ArrivalView(new Vector2(20f, 12f), new Vector3(20f, 9f, 28f), -4f, 4.6f);                                // ブランコを見上げる
                    _landmarks[3].view = new ArrivalView(new Vector2(13f, -13f), new Vector3(28f, 1.5f, -18f), 8f, 4.4f);
                    _landmarks[4].view = new ArrivalView(new Vector2(-14f, -11f), new Vector3(-28f, 6f, -18f), -3f, 4.8f);                            // ジャングルジムを見上げる
                    _landmarks[5].view = new ArrivalView(new Vector2(5f, -35f), new Vector3(-6f, 2f, -37f), 8f, 4.4f);                                // 砂場の中から、砂山とバケツ
                    _landmarks[6].view = new ArrivalView(new Vector2(-6f, 35f), new Vector3(-3f, 3f, 41f), -3f, 4.2f);                               // チューリップの間から見上げる
                    _landmarks[7].view = new ArrivalView(new Vector2(-33f, 24f), new Vector3(-48f, 14f, 34f), -3f, 5.2f);                            // クヌギを見上げる
                    _landmarks[8].view = new ArrivalView(new Vector2(31f, -42f), new Vector3(19f, 2f, -42f), 4f, 4.4f);                                // 土管の入り口
                    _landmarks[9].view = new ArrivalView(new Vector2(18f, 15f), new Vector3(9f, 1f, 5f), 22f, 4.6f);                                 // 水たまりごしに、水飲み場
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

        static readonly Color Lawn = C("#86c95a");
        static readonly Color LawnDeep = C("#5ea445");
        static readonly Color Sand = C("#e4cf98");
        static readonly Color SandDark = C("#cbb27a");
        static readonly Color Path = C("#d2bf96");
        static readonly Color Soil = C("#6e4b34");
        static readonly Color Mud = C("#6a5a44");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float n1 = Mathf.PerlinNoise(x * 0.06f + 4f, z * 0.06f + 9f);
            float n2 = Mathf.PerlinNoise(x * 0.3f + 13f, z * 0.3f + 2f);
            // しばふ（刈りこまれて、少しまだら）
            Color c = Color.Lerp(LawnDeep, Lawn, ShakuMath.SmoothStep(0.3f, 0.75f, n1));
            c = Color.Lerp(c, c * 1.08f, ShakuMath.SmoothStep(0.45f, 0.55f, Mathf.PerlinNoise(x * 0.18f, z * 0.18f)) * 0.5f);
            // 小道
            c = Color.Lerp(c, Color.Lerp(Path, Sand, n2 * 0.4f), TrailMask(x, z) * 0.7f);
            // 砂
            c = Color.Lerp(c, Color.Lerp(Sand, SandDark, n2 * 0.6f), SandMask(x, z));
            // 花だんの土
            c = Color.Lerp(c, Color.Lerp(Soil, Soil * 1.2f, n2), InRect(x, z, FlowerBed, BedSize - new Vector2(1.2f, 1.2f), 0.3f));
            // 水たまりのまわりは、ぬれた土
            float pd = Vector2.Distance(new Vector2(x, z), Puddle);
            c = Color.Lerp(c, Mud, ShakuMath.SmoothStep(PuddleRadius + 2.5f, PuddleRadius, pd) * 0.8f);
            float r = new Vector2(x, z).magnitude;
            c = Color.Lerp(c, LawnDeep * 0.75f, ShakuMath.SmoothStep(60f, 90f, r));
            c.a = 1f;
            return c;
        }
    }
}

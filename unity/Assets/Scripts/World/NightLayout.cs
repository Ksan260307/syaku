using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 夜の森エリア「月あかりの森」のレイアウト。神社の奥の宮のうらの、木の根のトンネルをぬけた先の、秋の夜の雑木林。
    /// 森（昼）のように大きな木のまわりをめぐるのではなく、まん中を東西に枯れ沢の谷が横切り、丸木橋でわたる。
    /// 南は月見の池（ススキの原）とイノシシのぬた場、谷の北は竹やぶ・ミノムシの木・ツキヨタケの光る倒木・シマエナガのねぐら、
    /// いちばん北の丘の上に星見の岩。
    /// 1 単位 = しゃくとりむしの体長（2.5cm）。
    /// </summary>
    public static class NightLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 80f;

        /// <summary>神社へもどる木の根のトンネル（南のはし）</summary>
        public static readonly Vector2 Gate = new Vector2(0f, -58.5f);
        public static readonly Vector2 Spawn = new Vector2(0f, -51f);

        public static readonly Vector2 Entrance = new Vector2(0f, -46f);
        /// <summary>月見の池（だ円）</summary>
        public static readonly Vector2 Pond = new Vector2(30f, -22f);
        public static readonly Vector2 PondRadii = new Vector2(10f, 7.5f);
        public const float PondLevel = -0.5f;
        public const float PondBed = -2.6f;
        /// <summary>イノシシのぬた場（どろ水のくぼみ）</summary>
        public static readonly Vector2 Wallow = new Vector2(-30f, -26f);
        public const float WallowR = 6f;
        public const float WallowLevel = -0.32f;
        public const float WallowBed = -0.4f;     // あさい（しゃくとりむしも、入って歩ける）
        /// <summary>竹やぶ</summary>
        public static readonly Vector2 Bamboo = new Vector2(-38f, 20f);
        public const float BambooR = 15f;
        /// <summary>ツキヨタケの光る倒木（ななめに横たわる）</summary>
        public static readonly Vector2 GlowLog = new Vector2(25f, 17f);
        public const float GlowLogYaw = 28f;
        /// <summary>ミノムシの木・シマエナガのねぐらの木</summary>
        public static readonly Vector2 MinoTree = new Vector2(-14f, 34f);
        public static readonly Vector2 RoostTree = new Vector2(42f, 33f);
        /// <summary>星見の丘と、てっぺんの星見の岩</summary>
        public static readonly Vector2 Hill = new Vector2(8f, 42f);
        public const float HillR = 21f;
        public const float HillTop = 8f;
        public static readonly Vector2 StarRock = new Vector2(8f, 44f);

        // 枯れ沢の谷（西から東へ、ゆるく曲がる）
        public const float GullyFloorHalf = 2.6f;
        public const float GullyTopHalf = 8.5f;
        public const float GullyDepth = 4.6f;
        /// <summary>丸木橋（谷にわたす。北南の向き）</summary>
        public const float BridgeX = -10f;
        public const float BridgeHalf = 10.5f;

        static float SmoothStep(float a, float b, float x) => ShakuMath.SmoothStep(a, b, x);

        /// <summary>谷のまん中の線の z（x の所）。</summary>
        public static float GullyZ(float x) => 3f + 3.5f * Mathf.Sin(x * 0.055f + 0.6f);

        /// <summary>谷の深さの度合い（1 = 谷底、0 = 谷の外）。外周の土手の近くでは、浅くなる。</summary>
        public static float GullyK(float x, float z)
        {
            float d = Mathf.Abs(z - GullyZ(x));
            float k = SmoothStep(GullyTopHalf, GullyFloorHalf, d);
            float r = new Vector2(x, z).magnitude;
            return k * SmoothStep(70f, 58f, r);
        }

        public static Vector2 BridgeCenter => new Vector2(BridgeX, GullyZ(BridgeX));

        public static float PondK(float x, float z)
        {
            float dx = (x - Pond.x) / PondRadii.x, dz = (z - Pond.y) / PondRadii.y;
            return 1f - Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool InPond(float x, float z, float margin)
        {
            float dx = (x - Pond.x) / (PondRadii.x + margin), dz = (z - Pond.y) / (PondRadii.y + margin);
            return dx * dx + dz * dz < 1f;
        }

        public static bool InWallow(float x, float z, float margin) => Vector2.Distance(new Vector2(x, z), Wallow) < WallowR + margin;

        // ------------------------------------------------------------------
        // 地形
        // ------------------------------------------------------------------
        public static float Height(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            // 雑木林の土：ゆるい起伏
            float h = 0.8f * (ShakuMath.Fbm(x * 0.028f + 3.1f, z * 0.028f + 8.7f, 3) - 0.5f) * 2f + 0.2f;
            // 入り口のまわりは、なだらか
            h = Mathf.Lerp(h, 0.1f, SmoothStep(14f, 8f, Vector2.Distance(p, Gate + new Vector2(0f, 5f))));
            // 星見の丘（北）：まるい丘
            float hill = SmoothStep(HillR, 3f, Vector2.Distance(p, Hill));
            h += HillTop * Mathf.Pow(hill, 1.25f);
            // 竹やぶ：少しもりあがった、平らな所
            h += 0.8f * SmoothStep(BambooR + 4f, BambooR - 4f, Vector2.Distance(p, Bamboo));
            // 枯れ沢の谷：両がわの土手から、谷底へ
            float g = GullyK(x, z);
            if (g > 0f) h -= GullyDepth * g;
            // 月見の池：ふちから、まん中へ深く
            float pk = PondK(x, z);
            if (pk > -0.25f)
            {
                float bed = Mathf.Lerp(PondLevel + 0.1f, PondBed, SmoothStep(0f, 0.55f, pk));
                h = Mathf.Lerp(h, bed, SmoothStep(-0.2f, 0.06f, pk));
            }
            // ぬた場：あさい、どろのくぼみ
            float wd = Vector2.Distance(p, Wallow);
            if (wd < WallowR + 2f)
            {
                float bed = Mathf.Lerp(WallowLevel + 0.12f, WallowBed, SmoothStep(WallowR * 0.85f, WallowR * 0.25f, wd));
                h = Mathf.Lerp(h, bed, SmoothStep(WallowR + 1.6f, WallowR - 0.4f, wd));
            }
            // 外周はせり上がる（雑木林の土手）
            h += 12f * Mathf.Pow(SmoothStep(60f, 96f, r), 1.4f);
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

        public static float WaterLevelAt(float x, float z)
        {
            if (InPond(x, z, 1.2f)) return PondLevel;
            if (InWallow(x, z, 0.6f)) return WallowLevel;
            return -999f;
        }

        public static bool IsUnderwater(Vector3 p)
        {
            if (InPond(p.x, p.z, 0.6f)) return p.y < PondLevel - AreaLayout.WadeDepth;
            return false;   // ぬた場は、あさいどろ水（入って歩ける）
        }

        public static bool InPlayArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;

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
                    Vector2 b = BridgeCenter;
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0f, -55f), new Vector2(0f, -36f), new Vector2(-4f, -21f), new Vector2(-9f, -9f), new Vector2(BridgeX, b.y - BridgeHalf + 0.5f) },   // 入り口から、丸木橋へ
                        new[] { new Vector2(BridgeX, b.y + BridgeHalf - 0.5f), new Vector2(-12f, 22f), new Vector2(-13f, 29f), new Vector2(-4f, 34f), new Vector2(2f, 38f) },   // 橋をわたって、ミノムシの木から丘へ
                        new[] { new Vector2(-4f, -21f), new Vector2(8f, -22f), new Vector2(18.5f, -22f) },                                                                         // 月見の池へ
                        new[] { new Vector2(-6f, -28f), new Vector2(-17f, -27f), new Vector2(-23f, -26f) },                                                                       // ぬた場へ
                        new[] { new Vector2(-12f, 22f), new Vector2(-25f, 20f) },                                                                                                  // 竹やぶへ
                        new[] { new Vector2(20f, -14f), new Vector2(21f, -4f), new Vector2(21f, 6f), new Vector2(21f, 12f) },                                                      // 池から谷へおりて、倒木へ
                        new[] { new Vector2(30f, 22f), new Vector2(37f, 28f) },                                                                                                    // ねぐらの木へ
                    };
                }
                return _trails;
            }
        }

        public static float DistToTrail(Vector2 p)
        {
            float best = 99f;
            foreach (var t in Trails)
                for (int i = 0; i < t.Length - 1; i++)
                    best = Mathf.Min(best, ShakuMath.DistToSegment(p, t[i], t[i + 1]));
            return best;
        }

        public static float TrailMask(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float best = DistToTrail(p);
            float wob = (Mathf.PerlinNoise(x * 0.3f + 17f, z * 0.3f + 23f) - 0.5f) * 0.6f;
            return SmoothStep(2.3f, 1.2f, best + wob);
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
                    Vector2 bc = BridgeCenter;
                    _landmarks = new List<LandmarkDef>
                    {
                        new LandmarkDef { id = 70, areaId = "night", name = "夜の森の入り口", english = "Moonlit Gate", description = "木の根のトンネルをぬけると、しんとした夜の森。木のあいだから、月あかり。",
                            position = Entrance, radius = 9f, mapColor = new Color(0.45f, 0.55f, 0.75f) },
                        new LandmarkDef { id = 71, areaId = "night", name = "月見の池", english = "Moon-Viewing Pond", description = "ススキのゆれる、しずかな池。水面に、まるいお月さまがうつっている。",
                            position = Pond, radius = 11f, mapColor = new Color(0.45f, 0.62f, 0.85f) },
                        new LandmarkDef { id = 72, areaId = "night", name = "イノシシのぬた場", english = "Boar Wallow", description = "どろ水のたまった、くぼみ。イノシシが、ごろんとどろあびをする所。",
                            position = Wallow, radius = 9f, mapColor = new Color(0.55f, 0.42f, 0.3f) },
                        new LandmarkDef { id = 73, areaId = "night", name = "丸木橋", english = "Log Bridge", description = "枯れ沢の谷にわたした、1 本の丸太の橋。苔でふかふか。下をのぞくと、ちょっとこわい。",
                            position = bc, radius = 8f, mapColor = new Color(0.62f, 0.48f, 0.32f) },
                        new LandmarkDef { id = 74, areaId = "night", name = "竹やぶ", english = "Bamboo Grove", description = "まっすぐな竹が、空へ、すっとのびる。風がふくと、さわさわ、こつん。",
                            position = Bamboo, radius = 14f, mapColor = new Color(0.5f, 0.68f, 0.38f) },
                        new LandmarkDef { id = 75, areaId = "night", name = "ツキヨタケの倒木", english = "Glowing Log", description = "古い倒木に生えた、ツキヨタケ。暗やみで、ぼうっと青白く光る。",
                            position = GlowLog, radius = 9f, mapColor = new Color(0.5f, 0.95f, 0.75f) },
                        new LandmarkDef { id = 76, areaId = "night", name = "ミノムシの木", english = "Bagworm Tree", description = "低い枝から、ミノムシがいくつもぶらさがっている木。風がふくと、ゆらゆら。",
                            position = MinoTree, radius = 9f, mapColor = new Color(0.58f, 0.52f, 0.36f) },
                        new LandmarkDef { id = 77, areaId = "night", name = "シマエナガのねぐら", english = "Long-tailed Tit Roost", description = "白くてまるい小鳥が、ねむりにくる木。太い枝の上を歩ける。",
                            position = RoostTree, radius = 10f, mapColor = new Color(0.88f, 0.9f, 0.95f) },
                        new LandmarkDef { id = 78, areaId = "night", name = "星見の岩", english = "Stargazing Rock", description = "丘のてっぺんの、平たい大きな岩。上にねころぶと、空いっぱいの星。",
                            position = StarRock, radius = 9f, mapColor = new Color(0.72f, 0.74f, 0.82f) },
                    };
                    // 着いたときの景色
                    float hillTop = HillTop + 0.2f;
                    _landmarks[0].view = new ArrivalView(new Vector2(0f, -50f), new Vector3(0f, 2f, -10f), 4f, 4.6f, clear: 40f);                               // 入り口から、林の奥と谷の方
                    _landmarks[1].view = new ArrivalView(new Vector2(16.5f, -20f), new Vector3(Pond.x, 0f, Pond.y), 8f, 5f);                                     // ススキの間から、池
                    _landmarks[2].view = new ArrivalView(new Vector2(-20f, -27f), new Vector3(Wallow.x, 0f, Wallow.y), 10f, 5f);                                  // ぬた場
                    _landmarks[3].view = new ArrivalView(new Vector2(BridgeX + 9f, bc.y - 10f), new Vector3(BridgeX, 1.5f, bc.y), 6f, 5f);                       // 谷の南の土手から、谷をわたる丸木橋
                    _landmarks[4].view = new ArrivalView(new Vector2(-24f, 18f), new Vector3(Bamboo.x, 6f, Bamboo.y), 3f, 5f);                                    // 竹やぶの入り口から、見上げる
                    _landmarks[5].view = new ArrivalView(new Vector2(16.5f, 22f), new Vector3(GlowLog.x, 1f, GlowLog.y), 7f, 5f);                                 // 倒木の北西の平らな所から、光る倒木
                    _landmarks[6].view = new ArrivalView(new Vector2(-8f, 28f), new Vector3(MinoTree.x, 6f, MinoTree.y), 4f, 5f);                                 // ミノムシのぶらさがる枝
                    _landmarks[7].view = new ArrivalView(new Vector2(37f, 23f), new Vector3(RoostTree.x, 10f, RoostTree.y), 4f, 5.2f);                           // ねぐらの木を見上げる（光る倒木のはしの、東）
                    _landmarks[8].view = new ArrivalView(new Vector2(4f, 31f), new Vector3(StarRock.x, hillTop + 3f, StarRock.y), 6f, 5f);                        // 丘の上の岩
                    // 地図や図鑑から移動したときに始める所
                    _landmarks[8].travel = new ArrivalView(new Vector2(StarRock.x - 1f, StarRock.y - 1.5f), new Vector3(0f, 0f, -12f), 6f, 5f, onTop: true);   // 星見の岩の上から、森を見わたす
                }
                return _landmarks;
            }
        }

        // ------------------------------------------------------------------
        // 地面の色（月あかりの下で、少し青く見える色。暗くしすぎない）
        // ------------------------------------------------------------------
        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static readonly Color Moss = C("#5b7a4a");
        static readonly Color MossDeep = C("#3c5640");
        static readonly Color Soil = C("#6a5648");
        static readonly Color Litter = C("#866444");
        static readonly Color BambooLitter = C("#a8935e");
        static readonly Color Path = C("#9c8c78");
        static readonly Color GullyStone = C("#8a8a88");
        static readonly Color Mud = C("#5a4636");
        static readonly Color PondBank = C("#4a4a40");
        static readonly Color Susuki = C("#a8a070");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float n1 = Mathf.PerlinNoise(x * 0.07f + 13f, z * 0.07f + 4f);
            float n2 = Mathf.PerlinNoise(x * 0.35f + 5f, z * 0.35f + 19f);
            float n3 = Mathf.PerlinNoise(x * 1.3f + 7f, z * 1.3f + 3f);
            Vector2 p = new Vector2(x, z);
            // 苔と土と、落ち葉
            Color c = Color.Lerp(MossDeep, Moss, ShakuMath.SmoothStep(0.3f, 0.75f, n1));
            c = Color.Lerp(c, Soil, ShakuMath.SmoothStep(0.5f, 0.75f, n2) * 0.5f);
            c = Color.Lerp(c, Litter, ShakuMath.SmoothStep(0.55f, 0.8f, n3) * 0.5f);
            // 竹やぶの、かれた竹の葉
            c = Color.Lerp(c, Color.Lerp(BambooLitter, Litter, n2 * 0.5f), SmoothStep(BambooR + 3f, BambooR - 3f, Vector2.Distance(p, Bamboo)) * 0.7f);
            // 谷底の、小石の河原
            float g = GullyK(x, z);
            c = Color.Lerp(c, Color.Lerp(GullyStone, Soil, n3 * 0.5f), SmoothStep(0.7f, 0.95f, g) * 0.8f);
            // 小道（ふみかためた土）
            c = Color.Lerp(c, Color.Lerp(Path, Soil, n2 * 0.4f), TrailMask(x, z) * 0.55f);
            // 池のまわりのススキの原（かれ草色）と、しめった土
            float pk = PondK(x, z);
            c = Color.Lerp(c, Susuki, SmoothStep(-0.9f, -0.4f, pk) * SmoothStep(0.05f, -0.2f, pk) * 0.45f);
            c = Color.Lerp(c, PondBank, ShakuMath.SmoothStep(-0.3f, 0f, pk) * 0.8f);
            // ぬた場の、どろ
            c = Color.Lerp(c, Mud, SmoothStep(WallowR + 2.5f, WallowR - 0.5f, Vector2.Distance(p, Wallow)));
            float r = p.magnitude;
            c = Color.Lerp(c, MossDeep * 0.8f, ShakuMath.SmoothStep(60f, 90f, r));
            c.a = 1f;
            return c;
        }
    }
}

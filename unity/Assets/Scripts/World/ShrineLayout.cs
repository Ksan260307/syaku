using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 神社エリア「鎮守の森のお社」のレイアウト。鎮守の森にかこまれた、村の小さなお稲荷さん。
    /// 南のはし（公園へもどる木の根のトンネル）から、石畳の参道が北へのびる。大鳥居をくぐり、石灯籠のあいだを通って、
    /// 石段を登ると、高い基壇の上にお社。西に手水舎とご神木、東に千本鳥居（ぬけた先に奥の宮）と、池にかかる太鼓橋。
    /// 1 単位 = しゃくとりむしの体長（2.5cm）。大鳥居は 1m ほど。
    /// </summary>
    public static class ShrineLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 80f;

        /// <summary>公園へもどる木の根のトンネル（南のはし）</summary>
        public static readonly Vector2 Gate = new Vector2(0f, -58.5f);
        public static readonly Vector2 Spawn = new Vector2(0f, -51f);

        public static readonly Vector2 Entrance = new Vector2(0f, -47f);
        public static readonly Vector2 GreatTorii = new Vector2(0f, -38f);
        public static readonly Vector2 LanternPath = new Vector2(0f, -16f);
        public static readonly Vector2 Chozuya = new Vector2(-22f, -14f);
        public static readonly Vector2 Tree = new Vector2(-40f, 22f);
        public static readonly Vector2 Hall = new Vector2(0f, 42f);
        public static readonly Vector2 Foxes = new Vector2(0f, 20f);
        public static readonly Vector2 InnerShrine = new Vector2(43f, 28f);
        public static readonly Vector2 Pond = new Vector2(28f, -22f);

        /// <summary>石灯籠（参道に 2 対、基壇の上に 1 対）。</summary>
        public static readonly Vector2[] Lanterns =
        {
            new Vector2(-8f, -24f), new Vector2(8f, -24f), new Vector2(-8f, -6f), new Vector2(8f, -6f), new Vector2(-20f, 26f), new Vector2(20f, 26f),
        };

        /// <summary>キツネの石像（基壇の前のかど。西は玉、東は鍵）。</summary>
        public static readonly Vector2[] FoxStatues = { new Vector2(-11f, 20.5f), new Vector2(11f, 20.5f) };

        /// <summary>絵馬かけ（基壇の東）・おみくじかけ（基壇の西）。お社の横で、まん中を向く。</summary>
        public static readonly Vector2 Ema = new Vector2(20.5f, 44f);
        public static readonly Vector2 Omikuji = new Vector2(-20.5f, 44f);
        /// <summary>さい銭箱（お社の階段の前）。</summary>
        public static readonly Vector2 Saisen = new Vector2(0f, 20.6f);

        // 参道・基壇・石段
        public const float PathHalf = 3.4f;               // 参道の石畳のはばの半分
        public const float PlatformY = 6f;                // 基壇の上の高さ
        public static readonly Vector2 PlatformMin = new Vector2(-26f, 16f), PlatformMax = new Vector2(26f, 56f);
        public const float StepsZ0 = 8f, StepsZ1 = 16f;   // 石段（下と上）
        public const float StepsHalf = 8f;
        public const int StepCount = 8;

        // 池（太鼓橋の下）：だ円
        public static readonly Vector2 PondRadii = new Vector2(9f, 12.5f);
        public const float PondLevel = -0.45f;
        public const float PondBed = -2.4f;

        // 千本鳥居の道（参道から、奥の宮へ）
        public static readonly Vector2[] ToriiPath =
        {
            new Vector2(4f, -2f), new Vector2(12f, 0.6f), new Vector2(20f, 4.2f), new Vector2(27f, 9f), new Vector2(33f, 15f), new Vector2(37.5f, 20.5f),
        };

        static float SmoothStep(float a, float b, float x) => ShakuMath.SmoothStep(a, b, x);

        // ------------------------------------------------------------------
        // 手水舎の水盤（Blender の make_chozuya と同じ大きさ）。手水舎は 90 度まわして置く（水盤の長い向きが南北）
        // ------------------------------------------------------------------
        public const float ChozuyaYaw = 90f;
        public const float BasinHalfX = 2.15f, BasinHalfZ = 4.9f;   // 水盤の内がわ（まわしたあと）
        public const float BasinWaterUp = 4.4f;                      // 水面（手水舎の床から）
        public const float BasinFloorUp = 2.4f;
        public static float ChozuyaFloor => FlatHeight;              // 手水舎のまわりは、平らにならしてある
        public static float BasinWater => ChozuyaFloor + BasinWaterUp;

        public static bool InBasin(float x, float z, float margin)
            => Mathf.Abs(x - Chozuya.x) < BasinHalfX + margin && Mathf.Abs(z - Chozuya.y) < BasinHalfZ + margin;

        /// <summary>参道のまわりの、平らにならした地面の高さ。</summary>
        public const float FlatHeight = 0.1f;

        // ------------------------------------------------------------------
        // 地形
        // ------------------------------------------------------------------
        static float InRect(float x, float z, Vector2 min, Vector2 max, float soft)
        {
            float dx = Mathf.Max(min.x - x, x - max.x);
            float dz = Mathf.Max(min.y - z, z - max.y);
            return SmoothStep(soft, -soft, Mathf.Max(dx, dz));
        }

        /// <summary>池のだ円の中の深さの度合い（1 = まん中、0 = ふち。外は負）。</summary>
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

        public static float DistToToriiPath(Vector2 p)
        {
            float best = 99f;
            for (int i = 0; i < ToriiPath.Length - 1; i++) best = Mathf.Min(best, ShakuMath.DistToSegment(p, ToriiPath[i], ToriiPath[i + 1]));
            return best;
        }

        public static float Height(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            // 森の土：ゆるい起伏
            float h = 0.6f * (ShakuMath.Fbm(x * 0.03f + 7.3f, z * 0.03f + 1.9f, 3) - 0.5f) * 2f + 0.15f;
            // 参道・広場のまわりは、平らにならしてある
            float flat = Mathf.Max(InRect(x, z, new Vector2(-30f, -58f), new Vector2(30f, 12f), 6f), SmoothStep(14f, 9f, Vector2.Distance(p, Gate + new Vector2(0f, 4f))));
            h = Mathf.Lerp(h, FlatHeight, flat);
            // 奥の宮の小山（千本鳥居をのぼっていく）
            float mound = SmoothStep(16f, 6f, Vector2.Distance(p, InnerShrine));
            float pathUp = SmoothStep(6f, 2f, DistToToriiPath(p)) * Mathf.Clamp01((x - 12f) / 26f);
            h = Mathf.Max(h, FlatHeight + 2.2f * Mathf.Max(mound, pathUp));
            // 基壇（石がきの上の、高い広場）
            float plat = InRect(x, z, PlatformMin, PlatformMax, 0.6f);
            if (plat > 0f) h = Mathf.Lerp(h, PlatformY, plat);
            // 石段の下の坂（石段は、この上にのる）
            if (Mathf.Abs(x) < StepsHalf + 0.6f && z > StepsZ0 - 1f && z < StepsZ1 + 0.5f)
            {
                float ramp = FlatHeight + (PlatformY - FlatHeight) * Mathf.Clamp01((z - StepsZ0) / (StepsZ1 - StepsZ0));
                float side = SmoothStep(StepsHalf + 0.6f, StepsHalf - 0.2f, Mathf.Abs(x));
                h = Mathf.Max(h, Mathf.Lerp(h, ramp, side));
            }
            // 池：ふちから、まん中へ深く
            float pk = PondK(x, z);
            if (pk > -0.25f)
            {
                float bed = Mathf.Lerp(PondLevel + 0.1f, PondBed, SmoothStep(0f, 0.55f, pk));
                h = Mathf.Lerp(h, bed, SmoothStep(-0.2f, 0.06f, pk));
            }
            // 外周はせり上がる（鎮守の森の土手）
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

        /// <summary>石段 i 番（0 が下）の、上の面の高さとまん中の z。</summary>
        public static Vector2 Step(int i)
        {
            float run = (StepsZ1 - StepsZ0) / StepCount;
            float rise = (PlatformY - FlatHeight) / StepCount;
            return new Vector2(FlatHeight + rise * (i + 1), StepsZ0 + run * (i + 0.5f));
        }

        public static float WaterLevelAt(float x, float z)
        {
            if (InPond(x, z, 1.2f)) return PondLevel;
            if (InBasin(x, z, 0f)) return BasinWater;
            return -999f;
        }

        public static bool IsUnderwater(Vector3 p)
        {
            if (InPond(p.x, p.z, 0.6f)) return p.y < PondLevel - AreaLayout.WadeDepth;
            if (InBasin(p.x, p.z, -0.1f)) return p.y < BasinWater - AreaLayout.WadeDepth && p.y > ChozuyaFloor + BasinFloorUp - 0.5f;
            return false;
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
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0f, -55f), GreatTorii, LanternPath, new Vector2(0f, StepsZ0) },                         // 参道
                        new[] { new Vector2(0f, StepsZ1), new Vector2(0f, 21f) },                                                   // 基壇の上
                        new[] { new Vector2(0f, -14f), new Vector2(-13f, -14f) },                                                   // 手水舎へ
                        new[] { new Vector2(-2f, 2f), new Vector2(-16f, 7f), new Vector2(-27f, 13f) },                             // ご神木へ
                        ToriiPath,                                                                                                  // 千本鳥居
                        new[] { new Vector2(2f, -30f), new Vector2(12f, -26f), new Vector2(15.5f, -22f) },                          // 太鼓橋の西のたもとへ
                        new[] { new Vector2(40.5f, -22f), new Vector2(45f, -12f), new Vector2(39f, -1f), new Vector2(27f, 9f) },   // 東のたもとから、千本鳥居へ
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
            float wob = (Mathf.PerlinNoise(x * 0.3f + 31f, z * 0.3f + 7f) - 0.5f) * 0.6f;
            return SmoothStep(2.4f, 1.3f, best + wob);
        }

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
                        new LandmarkDef { id = 60, areaId = "shrine", name = "神社の入り口", english = "Shrine Gate", description = "木の根のトンネルをぬけると、しんとした鎮守の森。石畳の参道が、奥へのびている。",
                            position = Entrance, radius = 9f, mapColor = new Color(0.62f, 0.75f, 0.5f) },
                        new LandmarkDef { id = 61, areaId = "shrine", name = "大鳥居", english = "Great Torii", description = "朱色の大きな鳥居。ここから先は、神さまの場所。くぐる前に、ぺこりとおじぎ。",
                            position = GreatTorii, radius = 10f, mapColor = new Color(0.9f, 0.32f, 0.2f) },
                        new LandmarkDef { id = 62, areaId = "shrine", name = "石灯籠の参道", english = "Lantern Path", description = "苔むした石灯籠が、参道の両がわにならぶ。夕方になると、火袋にあかりがともる。",
                            position = LanternPath, radius = 9f, mapColor = new Color(0.7f, 0.7f, 0.66f) },
                        new LandmarkDef { id = 63, areaId = "shrine", name = "手水舎", english = "Purification Fountain", description = "お参りの前に、手と口をきよめる所。竹の筧から、すんだ水がちょろちょろ流れる。",
                            position = Chozuya, radius = 8f, mapColor = new Color(0.55f, 0.75f, 0.85f) },
                        new LandmarkDef { id = 64, areaId = "shrine", name = "ご神木", english = "Sacred Tree", description = "しめ縄をまいた、何百年も生きている大きなクスノキ。幹のうろには、だれかがすんでいるみたい。",
                            position = Tree, radius = 16f, mapColor = new Color(0.4f, 0.5f, 0.3f) },
                        new LandmarkDef { id = 65, areaId = "shrine", name = "お社", english = "Shrine Hall", description = "高い石がきの上の、小さなお社。鈴をからんと鳴らして、手を合わせよう。",
                            position = Hall, radius = 16f, mapColor = new Color(0.36f, 0.62f, 0.54f) },
                        new LandmarkDef { id = 66, areaId = "shrine", name = "キツネの像", english = "Fox Guardians", description = "お稲荷さんのお使いの、キツネの石像。赤い前かけをして、玉と鍵をくわえている。",
                            position = Foxes, radius = 12f, mapColor = new Color(0.9f, 0.88f, 0.8f) },
                        new LandmarkDef { id = 67, areaId = "shrine", name = "千本鳥居", english = "Torii Tunnel", description = "朱色の小さな鳥居が、トンネルのように続く道。ぬけた先には、小さな奥の宮。",
                            position = new Vector2(24f, 7f), radius = 5f, useCapsule = true, capsuleRadius = 5f, mapColor = new Color(0.95f, 0.4f, 0.25f) },
                        new LandmarkDef { id = 68, areaId = "shrine", name = "太鼓橋", english = "Arched Bridge", description = "池にかかる、まるくそった朱の橋。てっぺんから、池の鯉とスイレンが見える。",
                            position = Pond, radius = 9f, mapColor = new Color(0.85f, 0.36f, 0.25f) },
                    };
                    var tunnel = _landmarks[7];
                    tunnel.capsuleA = Ground(ToriiPath[1].x, ToriiPath[1].y) + Vector3.up * 2f;
                    tunnel.capsuleB = Ground(ToriiPath[ToriiPath.Length - 1].x, ToriiPath[ToriiPath.Length - 1].y) + Vector3.up * 2f;
                    // 着いたときの景色（マイナスの角度は、見上げる）
                    Vector3 hall = new Vector3(Hall.x, PlatformY, Hall.y);
                    _landmarks[0].view = new ArrivalView(new Vector2(0f, -50f), hall, 4f, 4.6f, clear: 40f);                                   // 入り口から、参道と大鳥居
                    _landmarks[1].view = new ArrivalView(new Vector2(6f, -49f), new Vector3(0f, 0f, GreatTorii.y), 4f, 5.2f);                     // 大鳥居を、ななめ下から
                    _landmarks[2].view = new ArrivalView(new Vector2(4.5f, -30f), new Vector3(0f, 0f, -8f), 8f, 4.8f);                          // 灯籠のならぶ参道
                    _landmarks[3].view = new ArrivalView(new Vector2(-10f, -9f), new Vector3(Chozuya.x, 0f, Chozuya.y), 10f, 5f);               // 手水舎の水盤
                    _landmarks[4].view = new ArrivalView(new Vector2(-21f, 9f), new Vector3(Tree.x, 0f, Tree.y), 3f, 6f);                       // ご神木の、しめ縄
                    _landmarks[5].view = new ArrivalView(new Vector2(4f, 20f), hall, 8f, 5f);                                                     // 基壇の上から、お社
                    _landmarks[6].view = new ArrivalView(new Vector2(0f, 2.5f), new Vector3(0f, 0f, 20f), 6f, 5f);                              // 石段の下から、キツネの像
                    _landmarks[7].view = new ArrivalView(new Vector2(7.5f, -1.5f), new Vector3(20f, 0f, 4.2f), 4f, 4.6f);                       // 千本鳥居の入り口
                    _landmarks[8].view = new ArrivalView(new Vector2(22f, -6.5f), new Vector3(Pond.x, 0f, Pond.y), 10f, 5f);                    // 池と太鼓橋
                    // 地図や図鑑から移動したときに始める所（カメラが地面や物にぶつからず、名所がよく見える所）
                    _landmarks[1].travel = new ArrivalView(new Vector2(0f, -45f), new Vector3(0f, 0f, 0f), 3f, 5f);                          // 大鳥居の前から、柱のあいだの参道
                    _landmarks[2].travel = new ArrivalView(new Vector2(0f, -32f), new Vector3(0f, 0f, 0f), 10f, 6f);                          // 灯籠のならぶ参道
                    _landmarks[3].travel = new ArrivalView(new Vector2(-12f, 2f), new Vector3(Chozuya.x, 0f, Chozuya.y), 10f, 5.5f);          // 北の広場から、手水舎
                    _landmarks[5].travel = new ArrivalView(new Vector2(0f, -12f), new Vector3(0f, 0f, 70f), 2f, 5f);                         // 参道の先、灯籠のあいだから、石段の上のお社
                    _landmarks[6].travel = new ArrivalView(new Vector2(-14f, 19.4f), new Vector3(-11f, 0f, 20.5f), 4f, 6.5f, onTop: true);    // 西のキツネの像の台座の上（キツネと同じ高さ）
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

        static readonly Color Moss = C("#5d7a38");
        static readonly Color MossDeep = C("#3f5a2a");
        static readonly Color Soil = C("#6a5440");
        static readonly Color Litter = C("#8a6438");
        static readonly Color Gravel = C("#cdc8bb");
        static readonly Color GravelDark = C("#a7a196");
        static readonly Color Wall = C("#8d897f");
        static readonly Color WallDark = C("#6c685f");
        static readonly Color Path = C("#b8a888");
        static readonly Color Ginkgo = C("#e6b53a");
        static readonly Color Momiji = C("#c8442c");
        static readonly Color PondBank = C("#4f4a3c");

        /// <summary>玉砂利をしいた所（参道のまわりと、基壇の上）。</summary>
        public static float GravelMask(float x, float z)
        {
            float g = SmoothStep(7.5f, 5.5f, Mathf.Abs(x)) * SmoothStep(-56f, -52f, z) * SmoothStep(9f, 6f, z);
            g = Mathf.Max(g, InRect(x, z, PlatformMin + new Vector2(1f, 1f), PlatformMax - new Vector2(1f, 1f), 0.8f));
            return g;
        }

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float n1 = Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 8f);
            float n2 = Mathf.PerlinNoise(x * 0.35f + 11f, z * 0.35f + 2f);
            float n3 = Mathf.PerlinNoise(x * 1.3f + 5f, z * 1.3f + 9f);
            // 苔と土と、落ち葉
            Color c = Color.Lerp(MossDeep, Moss, ShakuMath.SmoothStep(0.3f, 0.75f, n1));
            c = Color.Lerp(c, Soil, ShakuMath.SmoothStep(0.55f, 0.75f, n2) * 0.5f);
            c = Color.Lerp(c, Litter, ShakuMath.SmoothStep(0.62f, 0.8f, n3) * 0.45f);
            // ところどころ、イチョウとモミジの落ち葉
            if (n3 > 0.82f) c = Color.Lerp(c, n2 > 0.5f ? Ginkgo : Momiji, 0.55f);
            // 玉砂利
            float gm = GravelMask(x, z);
            Color gravel = Color.Lerp(GravelDark, Gravel, ShakuMath.SmoothStep(0.3f, 0.7f, n3));
            c = Color.Lerp(c, gravel, gm);
            // 小道（ふみかためた土）
            c = Color.Lerp(c, Color.Lerp(Path, Soil, n2 * 0.4f), TrailMask(x, z) * 0.55f * (1f - gm));
            // 石がき（基壇のふちの、急な所）：石をつんだ目地
            float wall = ShakuMath.SmoothStep(0.6f, 0.35f, n.y) * InRect(x, z, PlatformMin - new Vector2(2f, 2f), PlatformMax + new Vector2(2f, 2f), 1f);
            if (wall > 0f)
            {
                bool joint = Mathf.Abs(Mathf.Sin(h * 2.2f)) < 0.12f || Mathf.Abs(Mathf.Sin((x + z) * 0.9f + Mathf.Floor(h * 0.7f) * 1.3f)) < 0.1f;
                Color w = joint ? WallDark : Color.Lerp(Wall, Gravel, n3 * 0.4f);
                w = Color.Lerp(w, Moss, ShakuMath.SmoothStep(0.6f, 0.85f, n1) * 0.5f);
                c = Color.Lerp(c, w, wall);
            }
            // 池のふちは、しめった土
            float pk = PondK(x, z);
            c = Color.Lerp(c, PondBank, ShakuMath.SmoothStep(-0.3f, 0f, pk) * 0.8f);
            float r = new Vector2(x, z).magnitude;
            c = Color.Lerp(c, MossDeep * 0.8f, ShakuMath.SmoothStep(60f, 90f, r));
            c.a = 1f;
            return c;
        }
    }
}

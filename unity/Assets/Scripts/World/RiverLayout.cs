using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 川辺エリア「せせらぎの小川」のレイアウト。
    /// 北（+Z）から南へ小川が流れ、途中に滝（段差）・中州・飛び石・倒れ枝の橋・睡蓮のよどみがある。
    /// </summary>
    public static class RiverLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 70f;

        public const float FallZ = 30f;          // 滝の位置
        public const float UpperLevel = 1.6f;    // 滝の上の水面
        public const float LowerStart = -1.6f;   // 滝つぼの水面
        public const float LowerEnd = -2.2f;     // 下流の水面
        public const float IslandZ = 2f;
        public const float StonesZ = 15f;
        public const float BridgeZ = -21f;
        public const float PoolZ = -45f;

        /// <summary>森へもどる木の根のトンネル（西の端）</summary>
        public static readonly Vector2 Gate = new Vector2(-58.5f, -12f);
        public static readonly Vector2 Spawn = new Vector2(-52f, -11.5f);

        public static float CenterX(float z) => 7f * Mathf.Sin(z * 0.045f + 0.6f) + 3f * Mathf.Sin(z * 0.11f + 1.3f);

        public static float HalfWidth(float z) => 9f + 1.2f * Mathf.Sin(z * 0.08f) + 5f * ShakuMath.Bump(z - PoolZ, 15f);

        static float LowerLevelAt(float z) => Mathf.Lerp(LowerStart, LowerEnd, Mathf.InverseLerp(FallZ, -90f, z));

        /// <summary>流れに沿った水面の高さ（滝のところで段差）。</summary>
        public static float WaterLevel(float z) => z > FallZ - 0.5f ? UpperLevel : LowerLevelAt(z);

        static float Plateau(float z) => ShakuMath.SmoothStep(FallZ - 3.5f, FallZ + 0.5f, z);

        public static Vector2 IslandCenter => new Vector2(CenterX(IslandZ) + 0.5f, IslandZ);

        /// <summary>中州の楕円の中での位置（0 = 中心, 1 = 縁）。</summary>
        public static float IslandRadius01(float x, float z)
        {
            Vector2 c = IslandCenter;
            float ex = (x - c.x) / 4.5f;
            float ez = (z - c.y) / 7.5f;
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        public static float DistToRiver(float x, float z) => Mathf.Abs(x - CenterX(z));

        public static float Height(float x, float z)
        {
            float r = new Vector2(x, z).magnitude;
            float d = DistToRiver(x, z);
            float w = HalfWidth(z);
            float plateau = Plateau(z);
            float noise = ShakuMath.Fbm(x * 0.03f + 31.3f, z * 0.03f + 5.1f, 3) * 0.9f
                        + (Mathf.PerlinNoise(x * 0.4f + 2.2f, z * 0.4f + 8.4f) - 0.5f) * 0.1f;
            float bank = 0.3f + noise * ShakuMath.SmoothStep(w + 2f, w + 10f, d) + 1.4f * ShakuMath.SmoothStep(14f, 40f, d)
                         + plateau * (UpperLevel - LowerStart + 0.3f);
            float level = Mathf.Lerp(LowerLevelAt(z), UpperLevel, plateau);
            float depth = 1.4f + 1.0f * (1f - ShakuMath.SmoothStep(0f, w, d));
            float bed = level - depth;
            float t = ShakuMath.SmoothStep(w - 3f, w + 4f, d);
            float h = Mathf.Lerp(bed, bank, t);
            // 中州
            float isl = IslandRadius01(x, z);
            if (isl < 1.3f)
            {
                float top = WaterLevel(z) + 0.55f + (Mathf.PerlinNoise(x * 0.5f, z * 0.5f) - 0.5f) * 0.2f;
                h = Mathf.Lerp(h, Mathf.Max(h, top - isl * isl * 0.4f), ShakuMath.SmoothStep(1.25f, 0.8f, isl));
            }
            // 小道はすこし平らに
            h -= TrailMask(x, z) * 0.1f;
            // 外周はせり上がる
            h += 11f * Mathf.Pow(ShakuMath.SmoothStep(58f, 96f, r), 1.4f);
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

        public static bool InChannel(float x, float z, float margin) => DistToRiver(x, z) < HalfWidth(z) + margin;

        public static float WaterLevelAt(float x, float z) => InChannel(x, z, 4.5f) ? WaterLevel(z) : -999f;

        public static bool IsUnderwater(Vector3 p)
        {
            if (!InChannel(p.x, p.z, 4.5f)) return false;
            // 浅い水ぎわ（しゃくとりむしが沈まない深さ）は、水の中ではない
            return p.y < WaterLevel(p.z) - AreaLayout.WadeDepth;
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
                    float wx(float z, float side) => CenterX(z) + side * (HalfWidth(z) + 6f);
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(-56f, -12f), new Vector2(-40f, -9f), new Vector2(-28f, -4f), new Vector2(wx(-2f, -1f), -2f) },
                        new[] { new Vector2(wx(-2f, -1f), -2f), new Vector2(wx(StonesZ, -1f), StonesZ), new Vector2(wx(24f, -1f), 24f) },
                        new[] { new Vector2(wx(-2f, -1f), -2f), new Vector2(wx(BridgeZ, -1f), BridgeZ), new Vector2(wx(-36f, -1f), -36f) },
                        new[] { new Vector2(wx(StonesZ, 1f), StonesZ), new Vector2(wx(IslandZ, 1f), IslandZ), new Vector2(wx(BridgeZ, 1f), BridgeZ) },
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
            float wob = (Mathf.PerlinNoise(x * 0.3f + 70f, z * 0.3f) - 0.5f) * 0.9f;
            return ShakuMath.SmoothStep(2.0f, 0.9f, best + wob);
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
                        new LandmarkDef { id = 10, areaId = "river", name = "せせらぎの岸", english = "Babbling Bank", description = "水の音が近い。森とはちがう、ひんやりした風。",
                            position = new Vector2(-48f, -10f), radius = 9f, mapColor = new Color(0.55f, 0.85f, 0.7f) },
                        new LandmarkDef { id = 11, areaId = "river", name = "しぶきの滝", english = "Spray Falls", description = "段差を流れ落ちる水。しぶきに小さな虹がかかる。",
                            position = new Vector2(CenterX(FallZ - 5f), FallZ - 5f), radius = 12f, mapColor = new Color(0.6f, 0.85f, 1f) },
                        new LandmarkDef { id = 12, areaId = "river", name = "小石の中州", english = "Pebble Isle", description = "川のまんなかの小さな島。葉っぱの舟でしか行けない。",
                            position = IslandCenter, radius = 5f, minHeightAboveGround = -0.5f, mapColor = new Color(0.85f, 0.78f, 0.6f) },
                        new LandmarkDef { id = 13, areaId = "river", name = "とびいしの瀬", english = "Stepping Stones", description = "平たい石がならぶ浅瀬。足もとに気をつけて。",
                            position = new Vector2(CenterX(StonesZ), StonesZ), radius = 4f, minHeightAboveGround = 1.2f, mapColor = new Color(0.7f, 0.72f, 0.75f) },
                        new LandmarkDef { id = 14, areaId = "river", name = "倒れ枝の橋", english = "Fallen Branch Bridge", description = "嵐で落ちた枝が、川をまたぐ橋になった。",
                            position = new Vector2(CenterX(BridgeZ), BridgeZ), radius = 4f, minHeightAboveGround = 2f, mapColor = new Color(0.6f, 0.45f, 0.3f) },
                        new LandmarkDef { id = 15, areaId = "river", name = "睡蓮のよどみ", english = "Lily Shallows", description = "流れがゆるやかになる場所。カエルの声が聞こえる。",
                            position = new Vector2(CenterX(PoolZ), PoolZ), radius = 14f, mapColor = new Color(0.9f, 0.6f, 0.8f) },
                    };
                    // 着いたときの景色（マイナスの角度は、見上げる）
                    Vector3 falls = new Vector3(CenterX(FallZ), UpperLevel + 1.5f, FallZ);
                    _landmarks[0].view = new ArrivalView(new Vector2(-40f, -8f), new Vector3(CenterX(-2f), LowerStart + 0.5f, -2f), 6f, 4.4f, clear: 34f);   // 小道の先に、川の流れ
                    _landmarks[1].view = new ArrivalView(new Vector2(-7f, 11f), falls, 0f, 4.6f);                                                // 滝を見上げる
                    _landmarks[2].view = new ArrivalView(IslandCenter + new Vector2(0f, -4f), falls, 4f, 4.2f);                                  // 中州から、上流の滝
                    _landmarks[3].view = new ArrivalView(new Vector2(CenterX(StonesZ) - 14f, StonesZ - 1.5f), new Vector3(CenterX(StonesZ), UpperLevel - 3.2f, StonesZ), 12f, 4.2f);
                    _landmarks[4].view = new ArrivalView(new Vector2(CenterX(BridgeZ) - 15f, BridgeZ - 8f), new Vector3(CenterX(BridgeZ), 1f, BridgeZ), 8f, 4.6f);
                    _landmarks[5].view = new ArrivalView(new Vector2(CenterX(PoolZ) - 19f, PoolZ + 5f), new Vector3(CenterX(PoolZ), LowerEnd, PoolZ - 2f), 14f, 4.4f);
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

        static readonly Color Grass = C("#7cbb52");
        static readonly Color GrassDeep = C("#4f8d3c");
        static readonly Color Sand = C("#d6c49a");
        static readonly Color Pebble = C("#a59d8c");
        static readonly Color Wet = C("#6d6650");
        static readonly Color Bed = C("#5d6a5a");
        static readonly Color Cliff = C("#857a6b");
        static readonly Color Path = C("#c9b48a");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float d = DistToRiver(x, z);
            float w = HalfWidth(z);
            float n1 = Mathf.PerlinNoise(x * 0.07f + 4f, z * 0.07f + 9f);
            float n2 = Mathf.PerlinNoise(x * 0.25f + 13f, z * 0.25f + 2f);
            Color c = Color.Lerp(GrassDeep, Grass, ShakuMath.SmoothStep(0.3f, 0.75f, n1));
            // 岸の砂と小石
            float shore = ShakuMath.SmoothStep(w + 7f, w + 2.5f, d);
            c = Color.Lerp(c, Color.Lerp(Sand, Pebble, ShakuMath.SmoothStep(0.4f, 0.7f, n2)), shore);
            // 水ぎわは濡れて暗い
            float wl = WaterLevel(z);
            float wet = ShakuMath.SmoothStep(wl + 0.5f, wl - 0.05f, h) * ShakuMath.SmoothStep(w + 6f, w, d);
            c = Color.Lerp(c, Wet, wet * 0.7f);
            if (h < wl - 0.3f && d < w + 4f) c = Color.Lerp(c, Bed, ShakuMath.SmoothStep(wl - 0.3f, wl - 1.5f, h));
            // 中州は砂
            float isl = IslandRadius01(x, z);
            if (isl < 1.1f && h > wl) c = Color.Lerp(Sand, Pebble, n2 * 0.6f);
            // 崖（滝の段差）は岩肌
            float steep = ShakuMath.SmoothStep(0.8f, 0.55f, n.y);
            c = Color.Lerp(c, Color.Lerp(Cliff, Pebble, n2 * 0.5f), steep * 0.85f);
            // 小道
            c = Color.Lerp(c, Color.Lerp(Path, Sand, n2 * 0.4f), TrailMask(x, z) * 0.55f);
            float r = new Vector2(x, z).magnitude;
            c = Color.Lerp(c, c * 0.72f, ShakuMath.SmoothStep(60f, 90f, r));
            c.a = 1f;
            return c;
        }
    }
}

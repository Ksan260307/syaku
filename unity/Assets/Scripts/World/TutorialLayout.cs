using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// チュートリアルの「練習の森」のレイアウト。森の苔の原を小さくした、まるい林の中の広場。
    /// 南のはじまりの場所から北の木の根のトンネルまで、小道ぞいに練習の場所がならぶ：
    /// 道のしずく（すすむ）→ 切り株（登る）→ となりの大きな赤キノコ（切り株のてっぺんから糸をねらう・かさのふちから糸でおりる）
    /// → 草原（はやく・背伸び）→ 小さな赤キノコの根もとのかたつむり（いきもの）→ トンネル（森へ）。
    /// 1 単位 = しゃくとりむしの体長（2.5cm）。歩くと 1 秒に 1 くらい進むので、場所のあいだは近くしてある。
    /// </summary>
    public static class TutorialLayout
    {
        public const float PlayRadius = 42f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 40f;

        public static readonly Vector2 Spawn = new Vector2(0f, -27f);
        /// <summary>すすむ練習の、道のしずく。</summary>
        public static readonly Vector2 FirstDew = new Vector2(0f, -20f);
        /// <summary>登る練習の切り株（森の切り株を小さくしたもの）。</summary>
        public static readonly Vector2 Stump = new Vector2(0f, -9f);
        public const float StumpScale = 0.45f;
        /// <summary>
        /// 糸をねらう練習の、大きな赤キノコ（切り株のとなり。かさは切り株のてっぺんより少し高く、あいだは糸でないとわたれない）。
        /// 切り株のてっぺんからは、かさの上が見える（地面からだと、かさのうらしか見えない）。
        /// </summary>
        public static readonly Vector2 Mushroom = new Vector2(8.5f, -3.5f);
        public const float MushroomScale = 0.8f;
        /// <summary>赤キノコのかさのふちから、糸でおりる先（キノコの北）。</summary>
        public static readonly Vector2 SilkLanding = new Vector2(8.5f, 1.5f);
        /// <summary>はやく・背伸びの練習の、草原。</summary>
        public static readonly Vector2 Meadow = new Vector2(3f, 10f);
        /// <summary>かたつむりのいる、小さな赤キノコ（いきものに会う練習）。</summary>
        public static readonly Vector2 SnailCap = new Vector2(-4f, 17.5f);
        public const float SnailCapScale = 0.55f;
        /// <summary>森へ出る木の根のトンネル（北のはし）。</summary>
        public static readonly Vector2 Gate = new Vector2(0f, 31f);
        /// <summary>西の、苔の小山（景色）。</summary>
        public static readonly Vector2 MossMound = new Vector2(-15f, 3f);

        static float SmoothStep(float a, float b, float x) => ShakuMath.SmoothStep(a, b, x);

        // ------------------------------------------------------------------
        // 地形
        // ------------------------------------------------------------------
        public static float Height(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            // 苔の原の、ゆるい起伏（練習の場所のまわりは、平ら）
            float h = (ShakuMath.Fbm(x * 0.05f + 4.3f, z * 0.05f + 9.1f, 3) - 0.5f) * 0.9f;
            float flat = Mathf.Max(SmoothStep(9f, 5f, Vector2.Distance(p, Stump)), SmoothStep(6f, 3f, Vector2.Distance(p, Spawn)));
            flat = Mathf.Max(flat, SmoothStep(6f, 3f, Vector2.Distance(p, Mushroom)));
            flat = Mathf.Max(flat, SmoothStep(4f, 2f, Vector2.Distance(p, SnailCap)));
            h = Mathf.Lerp(h, 0f, flat);
            // 西の苔の小山
            h += 1.6f * ShakuMath.Bump(Vector2.Distance(p, MossMound), 9f);
            // 小道は少しへこんで平ら
            h -= TrailMask(x, z) * 0.1f;
            // 外は、林の土手へせり上がる（トンネルのうしろも）
            h += 14f * Mathf.Pow(SmoothStep(34f, 72f, r), 1.3f);
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

        /// <summary>練習の森には、水はない。</summary>
        public static float WaterLevelAt(float x, float z) => -999f;

        public static bool IsUnderwater(Vector3 p) => false;

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
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0f, -30f), new Vector2(0f, -14.5f) },                                                          // はじまりの場所から、切り株へ
                        new[] { new Vector2(SilkLanding.x, SilkLanding.y), new Vector2(6f, 6f), new Vector2(Meadow.x, Meadow.y) },             // 赤キノコのうらから、草原へ
                        new[] { new Vector2(Meadow.x, Meadow.y), new Vector2(0f, 14f), new Vector2(-1f, 20f), new Vector2(0.5f, 28.5f) },      // 草原から、かたつむりのそばを通って、トンネルへ
                    };
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
            float best = DistToTrail(new Vector2(x, z));
            float wob = (Mathf.PerlinNoise(x * 0.3f + 40f, z * 0.3f) - 0.5f) * 0.8f;
            return SmoothStep(2.1f, 0.9f, best + wob);
        }

        // ------------------------------------------------------------------
        // 名所（練習の森だけのもの。本番の名所の数にはふくめない）
        // ------------------------------------------------------------------
        static List<LandmarkDef> _landmarks;

        public static List<LandmarkDef> Landmarks
        {
            get
            {
                if (_landmarks == null)
                    _landmarks = new List<LandmarkDef>
                    {
                        new LandmarkDef { id = 90, areaId = "tutorial", name = "見はらしの切り株", english = "Lookout Stump", description = "てっぺんまで登ると、練習の森がぐるりと見わたせる。",
                            position = Stump, radius = 4.2f, minHeightAboveGround = 2.6f, mapColor = new Color(0.85f, 0.65f, 0.4f) },
                        new LandmarkDef { id = 91, areaId = "tutorial", name = "赤キノコのてっぺん", english = "Red Cap Top", description = "大きな赤いキノコの、かさの上。落ちても、ぽよんと弾む。",
                            position = Mushroom, radius = 2.8f, minHeightAboveGround = 3.4f, mapColor = new Color(0.95f, 0.4f, 0.35f) },
                    };
                return _landmarks;
            }
        }

        // ------------------------------------------------------------------
        // 地面の色（森と同じ、苔と落ち葉と土）
        // ------------------------------------------------------------------
        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static readonly Color MossDeep = C("#4c8a3a");
        static readonly Color Moss = C("#76b24a");
        static readonly Color MossLight = C("#a3d45e");
        static readonly Color Soil = C("#8a6a49");
        static readonly Color SoilDark = C("#5e4a36");
        static readonly Color Litter = C("#c0823f");
        static readonly Color LitterRed = C("#b45a34");
        static readonly Color PathColor = C("#c4b088");
        static readonly Color MeadowColor = C("#93cf58");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            Vector2 p = new Vector2(x, z);
            float n1 = Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 8f);
            float n2 = Mathf.PerlinNoise(x * 0.17f + 13f, z * 0.17f + 1f);
            float n3 = Mathf.PerlinNoise(x * 0.5f + 7f, z * 0.5f + 21f);
            Color c = Color.Lerp(MossDeep, Moss, SmoothStep(0.25f, 0.75f, n1));
            c = Color.Lerp(c, MossLight, SmoothStep(0.62f, 0.8f, n2) * 0.6f);
            // 落ち葉のじゅうたん
            float litter = SmoothStep(0.55f, 0.68f, Mathf.PerlinNoise(x * 0.06f + 50f, z * 0.06f + 20f) + (n3 - 0.5f) * 0.2f);
            c = Color.Lerp(c, Color.Lerp(Litter, LitterRed, n3), litter * 0.8f);
            // 土
            float soil = SmoothStep(0.6f, 0.75f, Mathf.PerlinNoise(x * 0.09f + 70f, z * 0.09f + 5f));
            c = Color.Lerp(c, Color.Lerp(Soil, SoilDark, n2), soil * 0.6f);
            // 東の草原は明るい緑
            c = Color.Lerp(c, MeadowColor, ShakuMath.Bump(Vector2.Distance(p, Meadow + new Vector2(2f, 0f)), 11f) * 0.8f);
            // 小道
            c = Color.Lerp(c, Color.Lerp(PathColor, Soil, n3 * 0.5f), TrailMask(x, z) * 0.62f);
            // 急な斜面は土が見える
            c = Color.Lerp(c, Soil, SmoothStep(0.85f, 0.6f, n.y) * 0.6f);
            // 外の土手は暗め
            c = Color.Lerp(c, c * 0.72f, SmoothStep(36f, 70f, p.magnitude));
            c.a = 1f;
            return c;
        }
    }
}

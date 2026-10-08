using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>名所（ランドマーク）の定義。</summary>
    public class LandmarkDef
    {
        public int id;
        public string areaId = "forest";
        public string name;
        public string english;
        public string description;
        public Vector2 position;   // XZ
        public float radius;
        public float minHeightAboveGround;  // てっぺんに登らないと発見できない場所用
        public bool useCapsule;             // 丸太の中など
        public Vector3 capsuleA, capsuleB;
        public float capsuleRadius;
        public Color mapColor;
        /// <summary>移動してきたときに見せる景色（立つ場所・見る先・カメラ）。なければ、名所のそばのかわいた陸地に着く。</summary>
        public ArrivalView view;
    }

    /// <summary>
    /// 着いたときの景色：from に立って at（高さもふくむ）の方を向き、カメラは pitch（度、マイナスで見上げる）・distance で写す。
    /// onTop なら、物の上（切り株の頂など）に立つ。見る先までの間の背の高い草は、生成するときに生やさない。
    /// </summary>
    public class ArrivalView
    {
        public Vector2 from;
        public Vector3 at;
        public float pitch = 12f;
        public float distance = 4.2f;
        public bool onTop;
        /// <summary>見る先の方へ、背の高い草をよける長さ（遠くの景色を見せるときは長く）。</summary>
        public float clear = 14f;

        public ArrivalView(Vector2 from, Vector3 at, float pitch = 12f, float distance = 4.2f, bool onTop = false, float clear = 14f)
        {
            this.clear = clear;
            this.from = from;
            this.at = at;
            this.pitch = pitch;
            this.distance = distance;
            this.onTop = onTop;
        }

        /// <summary>見る向き（水平）。</summary>
        public Vector3 Forward
        {
            get
            {
                Vector2 d = new Vector2(at.x, at.z) - from;
                return d.sqrMagnitude > 1e-4f ? new Vector3(d.x, 0f, d.y).normalized : Vector3.forward;
            }
        }
    }

    /// <summary>
    /// 森エリアのレイアウト（地形の高さ・色・名所・小道）。
    /// 1 単位 = しゃくとりむしの体長（実寸で約 2.5cm）。プレイ範囲は半径約 66。
    /// </summary>
    public static class ForestLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float WaterLevel = -1.15f;
        public const float MaxClimbHeight = 70f;

        public static readonly Vector2 Spawn = new Vector2(1.5f, -3f);
        public static readonly Vector2 GreatTree = new Vector2(0f, 76f);
        public static readonly Vector2 ArchTarget = new Vector2(-22f, 50f);   // 大樹のアーチ根が伸びる先
        public static readonly Vector2 Pond = new Vector2(4f, -46f);
        public const float PondRadius = 17f;
        public static readonly Vector2 Stump = new Vector2(40f, -34f);
        public static readonly Vector2 LogCenter = new Vector2(-40f, 6f);
        public static readonly Vector2 LogDir = new Vector2(0.35f, 0.937f).normalized;
        public const float LogLength = 48f;
        public static readonly Vector2 MushroomGrove = new Vector2(46f, 8f);
        public static readonly Vector2 Meadow = new Vector2(34f, 38f);
        public static readonly Vector2 AcornPlaza = new Vector2(-38f, -32f);
        public static readonly Vector2 MossHill = new Vector2(-22f, 20f);
        /// <summary>川辺へ続く木の根のトンネル（森の東の端）</summary>
        public static readonly Vector2 Gate = new Vector2(58.5f, -15f);
        /// <summary>公園へ続く木の根のトンネル（森の西の端）</summary>
        public static readonly Vector2 ParkGate = new Vector2(-57f, 30f);

        static List<LandmarkDef> _landmarks;
        static List<Vector2[]> _trails;

        public static List<LandmarkDef> Landmarks
        {
            get
            {
                if (_landmarks == null) BuildLandmarks();
                return _landmarks;
            }
        }

        static void BuildLandmarks()
        {
            _landmarks = new List<LandmarkDef>
            {
                new LandmarkDef { id = 0, name = "目覚めの苔原", english = "Mossy Cradle", description = "旅のはじまり。ふかふかの苔のゆりかご。",
                    position = new Vector2(0f, 0f), radius = 9f, mapColor = new Color(0.55f, 0.85f, 0.45f) },
                new LandmarkDef { id = 1, name = "大樹の根元", english = "Roots of the Elder Tree", description = "森でいちばん古い木。根っこはまるで山脈のよう。",
                    position = new Vector2(4f, 46f), radius = 12f, mapColor = new Color(0.62f, 0.45f, 0.3f) },
                new LandmarkDef { id = 2, name = "光るキノコの洞", english = "Glowcap Hollow", description = "大樹の根がつくった、ほの暗いほら穴。",
                    position = ArchTarget + new Vector2(1.5f, 2.5f), radius = 7f, mapColor = new Color(0.45f, 0.85f, 1f) },
                new LandmarkDef { id = 3, name = "赤キノコの森", english = "Crimson Cap Grove", description = "見上げるほどの赤いキノコが立ちならぶ林。",
                    position = MushroomGrove, radius = 14f, mapColor = new Color(0.95f, 0.4f, 0.35f) },
                new LandmarkDef { id = 4, name = "花の草原", english = "Sunlit Meadow", description = "木漏れ日が差しこむ、小さな花畑。",
                    position = Meadow, radius = 13f, mapColor = new Color(1f, 0.85f, 0.35f) },
                new LandmarkDef { id = 5, name = "鏡の水たまり", english = "Mirror Pond", description = "空を映す静かな水たまり。葉っぱの舟がうかぶ。",
                    position = Pond, radius = 19f, mapColor = new Color(0.4f, 0.75f, 0.95f) },
                new LandmarkDef { id = 6, name = "古い切り株の頂", english = "Old Stump Summit", description = "てっぺんからは森がぐるりと見わたせる。",
                    position = Stump, radius = 7.5f, minHeightAboveGround = 7f, mapColor = new Color(0.85f, 0.65f, 0.4f) },
                new LandmarkDef { id = 7, name = "どんぐり広場", english = "Acorn Plaza", description = "どんぐりと松ぼっくりがころがる広場。",
                    position = AcornPlaza, radius = 12f, mapColor = new Color(0.8f, 0.55f, 0.3f) },
                new LandmarkDef { id = 8, name = "朽ちた丸太のトンネル", english = "Hollow Log Tunnel", description = "中が空洞になった倒木。くぐり抜けられる。",
                    position = LogCenter, radius = 6f, useCapsule = true, capsuleRadius = 4.2f, mapColor = new Color(0.6f, 0.5f, 0.35f) },
            };
            // 着いたときの景色（マイナスの角度は、見上げる）
            _landmarks[0].view = new ArrivalView(new Vector2(1.5f, -4f), new Vector3(0f, 26f, 76f), -6f, 4.4f);                // 苔原から、大樹を見上げる
            _landmarks[1].view = new ArrivalView(new Vector2(12f, 24f), new Vector3(0f, 40f, 76f), -10f, 5f);                 // 大樹の根元を見上げる
            _landmarks[2].view = new ArrivalView(new Vector2(-12f, 41f), new Vector3(-21f, 2.5f, 52f), 6f, 4f);               // 洞の入り口と、光るキノコ
            _landmarks[3].view = new ArrivalView(new Vector2(30f, 3f), new Vector3(46f, 7f, 8f), -10f, 4.6f);                 // 赤キノコの林を見上げる
            _landmarks[4].view = new ArrivalView(new Vector2(28f, 33f), new Vector3(37f, 1.5f, 41f), 12f, 4f);                // 花畑を見わたす
            _landmarks[5].view = new ArrivalView(new Vector2(4f, -31.5f), new Vector3(4f, -1f, -50f), 18f, 4.4f);             // 水たまりと、空の映りこみ
            _landmarks[6].view = new ArrivalView(Stump, new Vector3(4f, 6f, 6f), 6f, 5.2f, onTop: true);                      // 切り株の頂から、森を見わたす
            _landmarks[7].view = new ArrivalView(new Vector2(-27f, -23f), new Vector3(-38f, 1f, -33f), 16f, 4.2f);            // どんぐり広場
            _landmarks[8].view = new ArrivalView(LogCenter - LogDir * 31f, new Vector3(LogCenter.x, 3f, LogCenter.y), 4f, 4f); // 丸太のトンネルの入り口

            var lm = _landmarks[8];
            Vector2 a = LogCenter - LogDir * (LogLength * 0.42f);
            Vector2 b = LogCenter + LogDir * (LogLength * 0.42f);
            float ya = Height(a.x, a.y) + 1.6f;
            float yb = Height(b.x, b.y) + 1.6f;
            lm.capsuleA = new Vector3(a.x, ya, a.y);
            lm.capsuleB = new Vector3(b.x, yb, b.y);
        }

        public static List<Vector2[]> Trails
        {
            get
            {
                if (_trails == null)
                {
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0, 2), new Vector2(3, 14), new Vector2(-1, 26), new Vector2(3, 38) },
                        new[] { new Vector2(2, 0), new Vector2(14, 4), new Vector2(28, 3), new Vector2(38, 7) },
                        new[] { new Vector2(14, 4), new Vector2(22, 18), new Vector2(30, 30) },
                        new[] { new Vector2(1, -2), new Vector2(-3, -14), new Vector2(2, -26) },
                        new[] { new Vector2(2, -6), new Vector2(16, -18), new Vector2(30, -28) },
                        new[] { new Vector2(-2, -4), new Vector2(-16, -14), new Vector2(-30, -26) },
                        new[] { new Vector2(-3, 1), new Vector2(-14, 6), new Vector2(-24, 4), new Vector2(-33, -1) },
                        new[] { new Vector2(-1, 26), new Vector2(-10, 36), new Vector2(-18, 46) },
                        new[] { new Vector2(16, -18), new Vector2(32, -17), new Vector2(46, -14), new Vector2(55, -15) },
                    };
                }
                return _trails;
            }
        }

        // ------------------------------------------------------------------
        // 地形の高さ
        // ------------------------------------------------------------------
        static float Bowl(float t)
        {
            if (t >= 1f) return 0f;
            float k = 1f - t * t;
            return k * Mathf.Sqrt(k);
        }

        static float BaseHeight(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            float pondD = Vector2.Distance(p, Pond);
            float pondMask = ShakuMath.Bump(pondD, PondRadius * 1.35f);

            float n = ShakuMath.Fbm(x * 0.028f + 11.3f, z * 0.028f + 7.1f, 3) * 1.25f
                    + ShakuMath.Fbm(x * 0.1f + 3.7f, z * 0.1f + 19.2f, 2) * 0.32f
                    + (Mathf.PerlinNoise(x * 0.42f + 5.1f, z * 0.42f + 2.9f) - 0.5f) * 0.1f;
            n *= 1f - pondMask * 0.85f;
            float h = n;
            h += 4.2f * ShakuMath.Bump(Vector2.Distance(p, GreatTree), 52f);
            h += 2.4f * ShakuMath.Bump(Vector2.Distance(p, MossHill), 15f);
            h += 1.2f * ShakuMath.Bump(Vector2.Distance(p, new Vector2(24f, -14f)), 12f);
            h += 0.7f * ShakuMath.Bump(Vector2.Distance(p, Meadow), 17f);
            h -= 1.0f * ShakuMath.Bump(Vector2.Distance(p, AcornPlaza), 14f);
            h -= 3.4f * Bowl(pondD / PondRadius);
            // 外周はゆるやかにせり上がる（自然な壁）
            float edge = ShakuMath.SmoothStep(58f, 96f, r);
            h += 11f * Mathf.Pow(edge, 1.4f);
            return h;
        }

        public static float Height(float x, float z)
        {
            float h = BaseHeight(x, z);
            Vector2 p = new Vector2(x, z);
            // 丸太の下は平らにならす
            Vector2 la = LogCenter - LogDir * (LogLength * 0.5f + 2f);
            Vector2 lb = LogCenter + LogDir * (LogLength * 0.5f + 2f);
            float ld = ShakuMath.DistToSegment(p, la, lb);
            float lm = ShakuMath.SmoothStep(8.5f, 5f, ld);
            if (lm > 0f) h = Mathf.Lerp(h, LogBaseHeight(p), lm);
            // 切り株の周り
            float sd = Vector2.Distance(p, Stump);
            float sm = ShakuMath.SmoothStep(13f, 8f, sd);
            if (sm > 0f) h = Mathf.Lerp(h, BaseHeight(Stump.x, Stump.y), sm);
            // 小道は少しへこんで平ら
            float tr = TrailMask(x, z);
            h -= tr * 0.12f;
            return h;
        }

        static float LogBaseHeight(Vector2 p)
        {
            // 丸太の軸に沿ってなめらかに変化する高さ
            Vector2 d = p - LogCenter;
            float along = Vector2.Dot(d, LogDir);
            Vector2 q = LogCenter + LogDir * Mathf.Clamp(along, -LogLength * 0.5f, LogLength * 0.5f);
            float a = BaseHeight(LogCenter.x - LogDir.x * LogLength * 0.5f, LogCenter.y - LogDir.y * LogLength * 0.5f);
            float b = BaseHeight(LogCenter.x + LogDir.x * LogLength * 0.5f, LogCenter.y + LogDir.y * LogLength * 0.5f);
            float t = Mathf.InverseLerp(-LogLength * 0.5f, LogLength * 0.5f, along);
            return Mathf.Lerp(a, b, t);
        }

        public static Vector3 Normal(float x, float z)
        {
            const float e = 0.3f;
            float hx = Height(x + e, z) - Height(x - e, z);
            float hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        public static Vector3 Ground(float x, float z) => new Vector3(x, Height(x, z), z);

        public static float TrailMask(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float best = 99f;
            foreach (var t in Trails)
                for (int i = 0; i < t.Length - 1; i++)
                    best = Mathf.Min(best, ShakuMath.DistToSegment(p, t[i], t[i + 1]));
            float wob = (Mathf.PerlinNoise(x * 0.3f + 40f, z * 0.3f) - 0.5f) * 0.9f;
            return ShakuMath.SmoothStep(2.1f, 0.9f, best + wob);
        }

        /// <summary>その場所の水面の高さ（水がなければ十分に低い値）。</summary>
        public static float WaterLevelAt(float x, float z)
        {
            return Vector2.Distance(new Vector2(x, z), Pond) < PondRadius * 1.25f ? WaterLevel : -999f;
        }

        public static bool IsUnderwater(Vector3 p)
        {
            // 浅い水ぎわ（しゃくとりむしが沈まない深さ）は、水の中ではない
            return p.y < WaterLevel - AreaLayout.WadeDepth && Vector2.Distance(new Vector2(p.x, p.z), Pond) < PondRadius * 1.25f;
        }

        public static bool InPlayArea(Vector3 p)
        {
            return new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;
        }

        // ------------------------------------------------------------------
        // 地面の色
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
        static readonly Color Path = C("#c4b088");
        static readonly Color Mud = C("#5b5a44");
        static readonly Color Meadow_ = C("#93cf58");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            Vector2 p = new Vector2(x, z);
            float n1 = Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 8f);
            float n2 = Mathf.PerlinNoise(x * 0.17f + 13f, z * 0.17f + 1f);
            float n3 = Mathf.PerlinNoise(x * 0.5f + 7f, z * 0.5f + 21f);
            Color c = Color.Lerp(MossDeep, Moss, ShakuMath.SmoothStep(0.25f, 0.75f, n1));
            c = Color.Lerp(c, MossLight, ShakuMath.SmoothStep(0.62f, 0.8f, n2) * 0.6f);
            // 落ち葉のじゅうたん
            float litter = ShakuMath.SmoothStep(0.55f, 0.68f, Mathf.PerlinNoise(x * 0.045f + 50f, z * 0.045f + 20f) + (n3 - 0.5f) * 0.2f);
            litter = Mathf.Max(litter, ShakuMath.Bump(Vector2.Distance(p, AcornPlaza), 18f) * 0.9f);
            c = Color.Lerp(c, Color.Lerp(Litter, LitterRed, n3), litter * 0.85f);
            // 土
            float soil = ShakuMath.SmoothStep(0.6f, 0.75f, Mathf.PerlinNoise(x * 0.09f + 70f, z * 0.09f + 5f));
            c = Color.Lerp(c, Color.Lerp(Soil, SoilDark, n2), soil * 0.7f);
            // 草原は明るい緑
            float meadow = ShakuMath.Bump(Vector2.Distance(p, Meadow), 20f);
            c = Color.Lerp(c, Meadow_, meadow * 0.85f);
            // 大樹の根元は暗く湿った苔
            float tree = ShakuMath.Bump(Vector2.Distance(p, GreatTree), 46f);
            c = Color.Lerp(c, MossDeep * 0.85f, tree * 0.5f);
            // 水辺
            float pd = Vector2.Distance(p, Pond);
            float wet = ShakuMath.SmoothStep(PondRadius * 1.15f, PondRadius * 0.85f, pd);
            c = Color.Lerp(c, SoilDark, wet * 0.6f);
            if (h < WaterLevel + 0.15f) c = Color.Lerp(c, Mud, ShakuMath.SmoothStep(WaterLevel + 0.15f, WaterLevel - 0.6f, h));
            // 小道
            float tr = TrailMask(x, z);
            c = Color.Lerp(c, Color.Lerp(Path, Soil, n3 * 0.5f), tr * 0.62f);
            // 急な斜面は土が見える
            float steep = ShakuMath.SmoothStep(0.85f, 0.6f, n.y);
            c = Color.Lerp(c, Soil, steep * 0.6f);
            // 外周は暗め
            float r = p.magnitude;
            c = Color.Lerp(c, c * 0.72f, ShakuMath.SmoothStep(60f, 90f, r));
            c.a = 1f;
            return c;
        }
    }
}

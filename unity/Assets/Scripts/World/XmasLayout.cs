using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// クリスマス村エリア「雪あかりのクリスマス村」のレイアウト。夜の森の星見の丘の「光の輪」をくぐった先の、雪のふる夜の村の広場。
    /// まん中に大きなクリスマスツリーとプレゼントの山。西に雪だるま、南西にそり置き場、東にこおった池、
    /// 北東に、あかりのともる丸太の家（村のはし。うしろ半分は村の外）、北西にサンタさんへのポストと、落ちた手ぶくろ。
    /// 南の光の輪から広場へ、キャンディケインとランプの小道。
    /// 1 単位 = しゃくとりむしの体長（2.5cm）。
    /// </summary>
    public static class XmasLayout
    {
        public const float PlayRadius = 66f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 80f;

        /// <summary>夜の森へもどる光の輪（南のはし）</summary>
        public static readonly Vector2 Gate = new Vector2(0f, -58.5f);
        public static readonly Vector2 Spawn = new Vector2(0f, -51f);
        public static readonly Vector2 Entrance = new Vector2(0f, -46f);

        /// <summary>大きなクリスマスツリー（広場のまん中。いちばん下の枝のすそは、半径 17）</summary>
        public static readonly Vector2 Tree = new Vector2(0f, 8f);
        public const float TreeSkirt = 17.3f;
        public const float TreeTop = 48f;          // いちばん上の枝の先（星は、この上）
        public const float PlazaR = 27f;
        /// <summary>プレゼントの山（ツリーの前）</summary>
        public static readonly Vector2 Presents = new Vector2(2f, -12f);
        /// <summary>雪だるま（西）と、そり置き場（南西）</summary>
        public static readonly Vector2 Snowman = new Vector2(-31f, -7f);
        public const float SnowmanYaw = -62f;      // にんじんの鼻を、広場の南へ
        public static readonly Vector2 Sled = new Vector2(-40f, -30f);
        /// <summary>こおった池（東。だ円の氷。水ではないので、上を歩ける）</summary>
        public static readonly Vector2 Pond = new Vector2(33f, -15f);
        public static readonly Vector2 PondRadii = new Vector2(16f, 11f);
        public const float IceTop = 0.02f;
        /// <summary>あかりのともる家（北東。前の面が広場を向く）</summary>
        public static readonly Vector2 Cottage = new Vector2(30f, 56f);
        public const float CottageYaw = 34f;
        public const float CottageFront = -15f;    // 家の前の面（家の中心から）
        public const float CottageHalfW = 32f;
        public const float SillY = 14f;            // 窓台の高さ（登れる）
        public const float WindowX = -12f, DoorX = 16f;
        /// <summary>サンタさんへのポスト（北西）と、落ちた手ぶくろ（ヤマネが冬眠中）</summary>
        public static readonly Vector2 Mailbox = new Vector2(-30f, 34f);
        public const float MailboxYaw = -49f;
        public static readonly Vector2 Mitten = new Vector2(-34.5f, 27.5f);
        public const float MittenYaw = 119f;       // 手ぶくろの口を、広場の方へ
        /// <summary>手ぶくろの口（Blender の MITTEN_CUFF。手ぶくろのローカル座標）</summary>
        public static readonly Vector3 MittenCuff = new Vector3(0f, 1.9f, 4.2f);

        static float SmoothStep(float a, float b, float x) => ShakuMath.SmoothStep(a, b, x);

        /// <summary>家のローカルの (x, z)（x は家の右、z は家の奥）を、村の (x, z) に。</summary>
        public static Vector2 CottageLocal(float x, float z)
        {
            float a = CottageYaw * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return Cottage + new Vector2(c * x + s * z, -s * x + c * z);
        }

        /// <summary>村の (x, z) を、家のローカルの (x, z) に。</summary>
        public static Vector2 ToCottage(Vector2 p)
        {
            float a = CottageYaw * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector2 d = p - Cottage;
            return new Vector2(c * d.x - s * d.y, s * d.x + c * d.y);
        }

        public static Vector2 CottageDoor => CottageLocal(DoorX, CottageFront - 3f);
        public static Vector2 CottageWindow => CottageLocal(WindowX, CottageFront - 2.4f);

        public static float PondK(float x, float z)
        {
            float dx = (x - Pond.x) / PondRadii.x, dz = (z - Pond.y) / PondRadii.y;
            return 1f - Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool OnIce(float x, float z, float margin)
        {
            float dx = (x - Pond.x) / (PondRadii.x + margin), dz = (z - Pond.y) / (PondRadii.y + margin);
            return dx * dx + dz * dz < 1f;
        }

        /// <summary>家の土台の中（家のまわりの、平らにする所）。</summary>
        public static float CottageK(float x, float z)
        {
            Vector2 l = ToCottage(new Vector2(x, z));
            float kx = SmoothStep(CottageHalfW + 10f, CottageHalfW + 2f, Mathf.Abs(l.x));
            float kz = SmoothStep(CottageFront - 14f, CottageFront - 4f, l.y) * SmoothStep(24f, 18f, l.y);
            return kx * kz;
        }

        // ------------------------------------------------------------------
        // 地形
        // ------------------------------------------------------------------
        public static float Height(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float r = p.magnitude;
            // 雪の原：ゆるい、ふんわりした起伏
            float h = 0.7f * (ShakuMath.Fbm(x * 0.03f + 5.3f, z * 0.03f + 2.1f, 3) - 0.5f) * 2f + 0.35f;
            // 村のはしの、雪のもり上がり
            h += 1.6f * SmoothStep(46f, 60f, r) * ShakuMath.Fbm(x * 0.06f + 9.1f, z * 0.06f + 4.4f, 2);
            // 入り口と、ツリーの広場は、なだらか
            h = Mathf.Lerp(h, 0.1f, SmoothStep(14f, 8f, Vector2.Distance(p, Gate + new Vector2(0f, 5f))));
            h = Mathf.Lerp(h, 0.15f, SmoothStep(PlazaR + 6f, PlazaR - 4f, Vector2.Distance(p, Tree)));
            // 家の前と土台は、平ら
            h = Mathf.Lerp(h, 0f, CottageK(x, z));
            // こおった池：氷の下は少しくぼみ、まわりに雪の土手
            float pk = PondK(x, z);
            if (pk > -0.6f)
            {
                h = Mathf.Lerp(h, 0.1f, SmoothStep(-0.6f, -0.25f, pk));
                h += 0.35f * SmoothStep(-0.25f, -0.06f, pk) * SmoothStep(0.06f, -0.02f, pk);   // 氷のふちの、雪の土手
                h = Mathf.Lerp(h, -0.5f, SmoothStep(-0.01f, 0.06f, pk));                        // 氷の下
            }
            // 外周はせり上がる（雪の土手と、モミの森）
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

        /// <summary>水はない（池は、こおっている）。</summary>
        public static float WaterLevelAt(float x, float z) => -999f;

        public static bool IsUnderwater(Vector3 p) => false;

        public static bool InPlayArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;

        // ------------------------------------------------------------------
        // 小道（雪かきした道）
        // ------------------------------------------------------------------
        static List<Vector2[]> _trails;

        public static List<Vector2[]> Trails
        {
            get
            {
                if (_trails == null)
                {
                    var ring = new Vector2[13];
                    for (int i = 0; i < ring.Length; i++)
                    {
                        float a = i / 12f * Mathf.PI * 2f - Mathf.PI * 0.5f;
                        ring[i] = Tree + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 21.5f;
                    }
                    Vector2 door = CottageDoor;
                    _trails = new List<Vector2[]>
                    {
                        new[] { new Vector2(0f, -55f), new Vector2(0f, -36f), new Vector2(0f, -13.5f) },                                     // 光の輪から、ツリーの広場へ
                        ring,                                                                                                              // ツリーのまわりの、まるい道
                        new[] { new Vector2(-18f, -4f), new Vector2(-21.5f, -5.5f) },                                                         // 雪だるまへ
                        new[] { new Vector2(-14f, -10f), new Vector2(-26f, -22f), new Vector2(-33f, -27f) },                              // そり置き場へ
                        new[] { new Vector2(17f, -6f), new Vector2(19f, -9f) },                                                            // こおった池へ
                        new[] { new Vector2(16f, 22f), new Vector2(27f, 30f), door + (door - CottageLocal(DoorX, CottageFront)).normalized * 1f },   // 家のとびらへ
                        new[] { new Vector2(-15f, 24f), new Vector2(-25f, 30f) },                                                         // ポストへ
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
            float wob = (Mathf.PerlinNoise(x * 0.3f + 11f, z * 0.3f + 29f) - 0.5f) * 0.6f;
            return SmoothStep(2.4f, 1.3f, best + wob);
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
                        new LandmarkDef { id = 80, areaId = "xmas", name = "クリスマス村の入り口", english = "Village Gate", description = "光の輪をくぐると、しんしんと雪のふる、しずかな夜の村。あたたかなあかりが見える。",
                            position = Entrance, radius = 9f, mapColor = new Color(0.95f, 0.85f, 0.55f) },
                        new LandmarkDef { id = 81, areaId = "xmas", name = "大きなクリスマスツリー", english = "Christmas Tree", description = "オーナメントと電球で、きらきらにかざったモミの木。枝の段をつたって、登っていける。",
                            position = Tree, radius = 20f, mapColor = new Color(0.35f, 0.7f, 0.45f) },
                        new LandmarkDef { id = 82, areaId = "xmas", name = "プレゼントの山", english = "Pile of Presents", description = "リボンをかけた、大きな箱。上は平らで、ツリーの枝へのふみ台になる。",
                            position = Presents, radius = 9f, mapColor = new Color(0.9f, 0.4f, 0.4f) },
                        new LandmarkDef { id = 83, areaId = "xmas", name = "雪だるま", english = "Snowman", description = "バケツのぼうしに、にんじんの鼻。赤いマフラーをまいた、大きな雪だるま。",
                            position = Snowman, radius = 9f, mapColor = new Color(0.92f, 0.95f, 1f) },
                        new LandmarkDef { id = 84, areaId = "xmas", name = "こおった池", english = "Frozen Pond", description = "池の水が、かちかちにこおっている。氷の上は、つるつる。",
                            position = Pond, radius = 14f, mapColor = new Color(0.6f, 0.8f, 0.95f) },
                        new LandmarkDef { id = 85, areaId = "xmas", name = "あかりのともる家", english = "Lit Cottage", description = "丸太の家の窓から、あたたかなあかり。窓台まで登ると、中からいいにおい。",
                            position = CottageLocal(0f, CottageFront - 4f), radius = 16f, mapColor = new Color(0.85f, 0.55f, 0.35f) },
                        new LandmarkDef { id = 86, areaId = "xmas", name = "サンタさんへのポスト", english = "Letters to Santa", description = "サンタさんへの手紙を入れる、赤いポスト。そばに、片方だけの手ぶくろが落ちている。",
                            position = Mailbox, radius = 9f, mapColor = new Color(0.85f, 0.25f, 0.3f) },
                        new LandmarkDef { id = 87, areaId = "xmas", name = "そり置き場", english = "Sled Yard", description = "木のそりが、ひとつ。トナカイたちが、雪の下の苔をさがしにくる。",
                            position = Sled, radius = 10f, mapColor = new Color(0.75f, 0.35f, 0.3f) },
                        new LandmarkDef { id = 88, areaId = "xmas", name = "ツリーのてっぺん", english = "Treetop Star", description = "大きなツリーのいちばん上。金の星のとなりから、雪の村が見わたせる。",
                            position = Tree, radius = 6f, minHeightAboveGround = 36f, mapColor = new Color(1f, 0.85f, 0.35f) },
                    };
                    // 着いたときの景色
                    Vector2 door = CottageDoor;
                    _landmarks[0].view = new ArrivalView(new Vector2(0f, -50f), new Vector3(Tree.x, 16f, Tree.y), 2f, 4.6f, clear: 40f);                      // 入り口から、小道の先のツリー
                    _landmarks[1].view = new ArrivalView(new Vector2(-21f, -3f), new Vector3(Tree.x, 18f, Tree.y), 3f, 5f);                                    // 広場の西から、ツリーを見上げる
                    _landmarks[2].view = new ArrivalView(new Vector2(-13f, -21f), new Vector3(Presents.x, 4f, Presents.y), 8f, 5f);                             // プレゼントの山
                    _landmarks[3].view = new ArrivalView(new Vector2(-21f, -17f), new Vector3(Snowman.x, 9f, Snowman.y), 3f, 5f);                              // 雪だるまの前
                    _landmarks[4].view = new ArrivalView(new Vector2(17f, -27f), new Vector3(Pond.x, 0f, Pond.y), 9f, 5f);                                     // 池のほとりから、氷の上
                    _landmarks[5].view = new ArrivalView(door + (Tree - door).normalized * 14f, new Vector3(door.x, 12f, door.y), 4f, 5f);                     // 家のとびらと窓
                    _landmarks[6].view = new ArrivalView(new Vector2(-17f, 22f), new Vector3(Mailbox.x, 9f, Mailbox.y), 4f, 5f);                               // ポストと手ぶくろ
                    _landmarks[7].view = new ArrivalView(new Vector2(-30f, -38f), new Vector3(Sled.x, 2f, Sled.y), 8f, 5f);                                    // そり置き場
                    _landmarks[8].view = new ArrivalView(new Vector2(Tree.x, Tree.y - 4.3f), new Vector3(0f, 20f, -30f), 6f, 5f, onTop: true);               // ツリーの上の段から、村を見わたす
                    // 地図や図鑑から移動したときに始める所
                    _landmarks[1].travel = new ArrivalView(new Vector2(-21f, -3f), new Vector3(Tree.x, 10f, Tree.y), 4f, 5f);
                }
                return _landmarks;
            }
        }

        // ------------------------------------------------------------------
        // 地面の色（雪の夜：月あかりの青みと、ランプのあたたかさ。白くとばしすぎない）
        // ------------------------------------------------------------------
        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static readonly Color Snow = C("#dbe6ee");
        static readonly Color SnowShade = C("#bdcdda");
        static readonly Color SnowDeep = C("#a6bacb");
        static readonly Color Trodden = C("#b6b4b2");
        static readonly Color TroddenDark = C("#9a948c");
        static readonly Color Needles = C("#7d8a78");
        static readonly Color IceBank = C("#c8d6e6");

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            float n1 = Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 17f);
            float n2 = Mathf.PerlinNoise(x * 0.3f + 8f, z * 0.3f + 1f);
            float n3 = Mathf.PerlinNoise(x * 1.4f + 2f, z * 1.4f + 6f);
            Vector2 p = new Vector2(x, z);
            // ふんわり積もった雪（風のあとの、うすいかげのもよう）
            Color c = Color.Lerp(SnowShade, Snow, ShakuMath.SmoothStep(0.3f, 0.75f, n1));
            c = Color.Lerp(c, SnowDeep, ShakuMath.SmoothStep(0.6f, 0.85f, n2) * 0.35f);
            c = Color.Lerp(c, Snow * 1.03f, ShakuMath.SmoothStep(0.7f, 0.9f, n3) * 0.4f);
            // 坂は、少しかげ
            c = Color.Lerp(c, SnowDeep, ShakuMath.SmoothStep(0.95f, 0.75f, n.y) * 0.5f);
            // 雪かきした小道（ふみかためた雪と、のぞく土）
            c = Color.Lerp(c, Color.Lerp(Trodden, TroddenDark, n2 * 0.6f), TrailMask(x, z) * 0.6f);
            // ツリーの下は、落ちた葉で少し緑がかる
            c = Color.Lerp(c, Needles, SmoothStep(TreeSkirt * 0.8f, TreeSkirt * 0.35f, Vector2.Distance(p, Tree)) * 0.35f);
            // 池のふちの土手
            float pk = PondK(x, z);
            c = Color.Lerp(c, IceBank, SmoothStep(-0.3f, -0.05f, pk) * 0.6f);
            float r = p.magnitude;
            c = Color.Lerp(c, SnowDeep * 0.85f, ShakuMath.SmoothStep(60f, 90f, r));
            c.a = 1f;
            return c;
        }
    }
}

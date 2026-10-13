using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 山小屋の中のエリア「山小屋」のレイアウト。山の山小屋のとびらから入る、人の大きさの丸太小屋の中。
    /// しゃくとりむしから見ると、ひとつの部屋が大きな広場：床板の上のラグ・まきストーブ・テーブル・二段ベッド・棚・はり・ツバメの巣。
    /// 部屋の大きさは blender/scripts/build_hut_kit.py と同じ数（部屋のまん中の床が原点。x = 東、z = 北）。
    /// </summary>
    public static class HutLayout
    {
        public const float HalfX = 72f, HalfZ = 52f;      // かべの内がわ
        public const float WallHeight = 84f, RidgeHeight = 120f;
        /// <summary>かべの当たり判定の面（丸太の出っぱりの分、内がわ）。</summary>
        public const float WallFace = 0.6f;
        public const float DoorA = -56f, DoorB = -24f, DoorHeight = 76f;   // 南のかべのとびら（x の範囲と高さ）
        public const float SillStep = 0.5f;                                 // とびらのしきいの高さ
        public static readonly float[] BeamsX = { -36f, 0f, 36f };          // はり（南北にわたす）
        public const float BeamY = 78f, BeamW = 6f;
        public const float BeamTop = BeamY + BeamW;
        public const float StoveTop = 29.5f;
        public const float TableHeight = 29f;
        public const float BenchHeight = 17f;
        public const float BunkLow = 19f, BunkUp = 57f;                     // マットレスの上の高さ
        public static readonly float[] ShelfBoards = { 34f, 52f, 70f };
        public const float WindowSillS = 36f, WindowSillE = 36f;           // 窓台の上の高さ

        public const float PlayRadius = 90f;
        public const float TerrainHalf = 96f;
        public const float MaxClimbHeight = 100f;

        /// <summary>山へもどる、とびら（南のかべ）。</summary>
        public static readonly Vector2 DoorGate = new Vector2((DoorA + DoorB) * 0.5f, -HalfZ);
        /// <summary>入り口の前（玄関マットの上。上に、はりのない所）。</summary>
        public static readonly Vector2 Spawn = new Vector2(-46f, -40f);

        // 家具の場所（床の点。向きは度で、モデルの前（+z）が向く方）
        public static readonly Vector2 Stove = new Vector2(-58f, -2f);
        public static readonly Vector2 Firewood = new Vector2(-60f, 28f);
        public static readonly Vector2 Shelf = new Vector2(-54f, 47f);
        public static readonly Vector2 Bunk = new Vector2(4f, 35f);
        public static readonly Vector2 Table = new Vector2(52f, -6f);
        public static readonly Vector2 Bench = new Vector2(24f, -6f);
        public static readonly Vector2 Rug = new Vector2(-4f, -4f);
        public static readonly Vector2 DoorMat = new Vector2(-40f, -42f);
        public static readonly Vector2 Nest = new Vector2(0f, -36f);
        public static readonly Vector2 MouseHole = new Vector2(HalfX - WallFace, 40f);
        public static readonly Vector2 WindowS = new Vector2(34f, -HalfZ);
        public static readonly Vector2 WindowE = new Vector2(HalfX, -6f);

        public struct Item
        {
            public string mesh;
            public Vector3 pos;
            public float yaw;
            public bool collider;
            public bool shadows;
            public Item(string mesh, float x, float y, float z, float yaw, bool collider = true, bool shadows = true)
            {
                this.mesh = mesh;
                pos = new Vector3(x, y, z);
                this.yaw = yaw;
                this.collider = collider;
                this.shadows = shadows;
            }
        }

        /// <summary>部屋の物（モデルは部屋の座標）と家具・小物。</summary>
        public static readonly Item[] Items =
        {
            new Item("Hut_Walls", 0f, 0f, 0f, 0f),
            new Item("Hut_Roof", 0f, 0f, 0f, 0f),
            new Item("Hut_Beams", 0f, 0f, 0f, 0f),
            new Item("Hut_Door", 0f, 0f, 0f, 0f),
            new Item("Hut_View", 0f, 0f, 0f, 0f, false, false),
            new Item("Hut_Stove", Stove.x, 0f, Stove.y, 90f),
            new Item("Hut_Kettle", -53f, StoveTop, -4f, 30f),
            new Item("Hut_Firewood", Firewood.x, 0f, Firewood.y, 90f),
            new Item("Hut_Shelf", Shelf.x, 0f, Shelf.y, 180f),
            new Item("Hut_Bunk", Bunk.x, 0f, Bunk.y, 180f),
            new Item("Hut_Table", Table.x, 0f, Table.y, 0f),
            new Item("Hut_Bench", Bench.x, 0f, Bench.y, 0f),
            new Item("Hut_Rug", Rug.x, 0f, Rug.y, 0f),
            new Item("Hut_DoorMat", DoorMat.x, 0f, DoorMat.y, 0f),
            new Item("Hut_Boots", -18f, 0f, -44f, -20f),
            new Item("Hut_Backpack", -4f, 0f, -45f, 0f),
            new Item("Hut_Mug", 46f, TableHeight, 10f, 40f),
            new Item("Hut_Bread", 58f, TableHeight, -18f, 80f),
            new Item("Hut_Map", 48f, TableHeight, -2f, 15f),
            new Item("Hut_Book", 62f, TableHeight, 16f, -10f),
            new Item("Hut_Lamp", 36f, BeamY, -6f, 0f, false),
            new Item("Hut_Clock", -HalfX + WallFace, 62f, 28f, 90f),
            new Item("Hut_Bucket", -40f, 0f, 20f, 0f),
            new Item("Hut_Broom", -64f, 0f, -49f, 0f),
            new Item("Hut_Herbs", -HalfX + 1f, 64f, 8f, 90f, false),
            new Item("Hut_Nest", Nest.x, BeamTop, Nest.y, 0f),
            new Item("Hut_MouseHole", MouseHole.x, 0f, MouseHole.y, -90f, false, false),
        };

        public static float Height(float x, float z) => 0f;

        public static Vector3 Normal(float x, float z) => Vector3.up;

        /// <summary>部屋の中（かべの内がわ）か。margin だけ内がわへせまくする。</summary>
        public static bool InRoom(float x, float z, float margin = 0f) =>
            Mathf.Abs(x) < HalfX - WallFace - margin && Mathf.Abs(z) < HalfZ - WallFace - margin;

        /// <summary>遊べる所：部屋の中と、とびらのしきいの上（山へ出る所）。</summary>
        public static bool InPlayArea(Vector3 p)
        {
            if (p.y >= MaxClimbHeight || !ShakuPhysics.IsFinite(p)) return false;
            if (Mathf.Abs(p.x) < HalfX - WallFace + 0.4f && Mathf.Abs(p.z) < HalfZ - WallFace + 0.4f) return true;
            return p.x > DoorA + 0.5f && p.x < DoorB - 0.5f && p.z > -HalfZ - 5f && p.z < 0f && p.y < DoorHeight;
        }

        public static float WaterLevelAt(float x, float z) => -999f;

        public static bool IsUnderwater(Vector3 p) => false;

        public static float TrailMask(float x, float z) => 0f;

        /// <summary>小屋の中に小道はない（地図にも描かない）。</summary>
        public static readonly List<Vector2[]> Trails = new List<Vector2[]>();

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
                        new LandmarkDef { id = 40, areaId = "hut", name = "小屋の入り口", english = "Doorway", description = "とびらのすき間から入ると、まきのにおいと、あたたかい空気。見上げると、天井がずっと高い。",
                            position = DoorMat, radius = 14f, mapColor = new Color(0.62f, 0.45f, 0.3f) },
                        new LandmarkDef { id = 41, areaId = "hut", name = "まきストーブ", english = "Wood Stove", description = "ぱちぱち、まきのもえる音。そばにいると、体がぽかぽかしてくる。上には、湯気の出るやかん。",
                            position = Stove + new Vector2(12f, 0f), radius = 14f, mapColor = new Color(0.35f, 0.33f, 0.32f) },
                        new LandmarkDef { id = 42, areaId = "hut", name = "テーブルの上", english = "Table Top", description = "マグカップに、パン、山の地図。登山者のお昼ごはんのあと。パンくずが落ちている。",
                            position = Table, radius = 22f, minHeightAboveGround = 26f, mapColor = new Color(0.85f, 0.66f, 0.42f) },
                        new LandmarkDef { id = 43, areaId = "hut", name = "二段ベッド", english = "Bunk Bed", description = "チェックの毛布の、ふかふかのベッド。上の段まで登ると、部屋じゅうが見わたせる。",
                            position = Bunk, radius = 40f, minHeightAboveGround = 50f, mapColor = new Color(0.72f, 0.3f, 0.26f) },
                        new LandmarkDef { id = 44, areaId = "hut", name = "かべの棚", english = "Wall Shelf", description = "びんや本、ロープの輪がならぶ棚。いちばん上の段は、まるで高い岩だな。",
                            position = Shelf, radius = 16f, minHeightAboveGround = 66f, mapColor = new Color(0.82f, 0.64f, 0.4f) },
                        new LandmarkDef { id = 45, areaId = "hut", name = "日だまりの窓辺", english = "Sunny Window", description = "窓台に、あたたかい日ざし。外には、高山の草原と、遠くの山なみ。",
                            position = WindowS + new Vector2(0f, 3f), radius = 18f, minHeightAboveGround = 33f, mapColor = new Color(0.65f, 0.85f, 0.98f) },
                        new LandmarkDef { id = 46, areaId = "hut", name = "天井のはり", english = "Roof Beam", description = "部屋の上を、太いはりがわたっている。下を見ると、テーブルもベッドも小さく見える。",
                            position = new Vector2(0f, 0f), radius = 60f, minHeightAboveGround = 80f, mapColor = new Color(0.5f, 0.34f, 0.2f) },
                        new LandmarkDef { id = 47, areaId = "hut", name = "ツバメの巣", english = "Swallow's Nest", description = "はりの上の、どろでできたおわん。ツバメが、毎年ここへもどってくる。",
                            position = Nest, radius = 10f, minHeightAboveGround = 80f, mapColor = new Color(0.55f, 0.42f, 0.3f) },
                        new LandmarkDef { id = 48, areaId = "hut", name = "ネズミの穴", english = "Mouse Hole", description = "かべのすそに、かじってあけた小さな穴。中から、チュッと声がする。",
                            position = MouseHole + new Vector2(-4f, 0f), radius = 9f, mapColor = new Color(0.3f, 0.24f, 0.2f) },
                    };
                    // 着いたときの景色
                    _landmarks[0].view = new ArrivalView(Spawn, new Vector3(10f, 30f, 30f), -10f, 4.6f);
                    _landmarks[1].view = new ArrivalView(Stove + new Vector2(24f, -8f), new Vector3(Stove.x, 22f, Stove.y), 2f, 4.6f);
                    _landmarks[2].view = new ArrivalView(Table + new Vector2(-20f, -18f), new Vector3(Table.x, TableHeight + 4f, Table.y), -6f, 4.8f);
                    _landmarks[3].view = new ArrivalView(Bunk + new Vector2(-6f, -26f), new Vector3(Bunk.x, BunkUp, Bunk.y), -10f, 4.8f);
                    _landmarks[4].view = new ArrivalView(Shelf + new Vector2(10f, -18f), new Vector3(Shelf.x, 50f, Shelf.y), -10f, 4.8f);
                    _landmarks[5].view = new ArrivalView(WindowS + new Vector2(-6f, 20f), new Vector3(WindowS.x, 50f, WindowS.y), -10f, 4.8f);
                    _landmarks[6].view = new ArrivalView(new Vector2(-14f, -20f), new Vector3(0f, BeamTop, 0f), -12f, 4.8f);
                    _landmarks[7].view = new ArrivalView(Nest + new Vector2(-12f, 14f), new Vector3(Nest.x, BeamTop + 2f, Nest.y), -14f, 4.8f);
                    _landmarks[8].view = new ArrivalView(MouseHole + new Vector2(-12f, -3f), new Vector3(MouseHole.x, 3f, MouseHole.y), 8f, 4.4f);
                }
                return _landmarks;
            }
        }

        // ------------------------------------------------------------------
        // 床の色：東西にのびる床板（板ごとに色が少しちがい、継ぎ目は暗い）。かべの外は、草
        // ------------------------------------------------------------------
        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static readonly Color Plank = C("#c8915a");
        static readonly Color PlankDark = C("#a8743f");
        static readonly Color PlankLight = C("#d9a76c");
        static readonly Color Seam = C("#6e4826");
        static readonly Color Grass = C("#7fa65a");
        static readonly Color GrassDeep = C("#5f8a48");
        public const float PlankWidth = 6f;

        public static Color GroundColor(float x, float z, float h, Vector3 n)
        {
            if (!InRoom(x, z, -2.5f))
            {
                float g = Mathf.PerlinNoise(x * 0.08f + 3f, z * 0.08f + 7f);
                Color o = Color.Lerp(GrassDeep, Grass, g);
                o.a = 1f;
                return o;
            }
            int row = Mathf.FloorToInt((z + 200f) / PlankWidth);
            float v = (z + 200f) / PlankWidth - row;
            // 板の長さは 40。はしは、となりの列と半分ずらす
            float len = 40f;
            float u = (x + 300f + (row % 3) * len / 3f) / len;
            int col = Mathf.FloorToInt(u);
            float uf = u - col;
            float tone = Mathf.PerlinNoise(row * 1.37f + col * 3.1f, 0.5f);
            Color c = Color.Lerp(PlankDark, PlankLight, tone);
            // 木目：板にそったすじ
            float grain = Mathf.PerlinNoise(x * 0.06f + row * 7f, z * 1.4f + col * 2f);
            c = Color.Lerp(c, c * 0.86f, Mathf.Clamp01(grain * 1.4f - 0.3f));
            // 継ぎ目
            float seam = Mathf.Max(ShakuMath.SmoothStep(0.1f, 0f, v), ShakuMath.SmoothStep(0.9f, 1f, v));
            seam = Mathf.Max(seam, ShakuMath.SmoothStep(0.02f, 0f, Mathf.Min(uf, 1f - uf)));
            c = Color.Lerp(c, Seam, seam * 0.75f);
            // かべぎわは、少し暗い
            float edge = Mathf.Min(HalfX - Mathf.Abs(x), HalfZ - Mathf.Abs(z));
            c *= Mathf.Lerp(0.82f, 1f, ShakuMath.SmoothStep(0f, 10f, edge));
            c = Color.Lerp(c, Plank, 0.15f);
            c.a = 1f;
            return c;
        }
    }
}

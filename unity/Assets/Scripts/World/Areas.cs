using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>エリアどうしをつなぐ木の根のトンネル。</summary>
    public class GateDef
    {
        public Vector2 position;
        public string targetArea;
        public string label;        // 「川辺へ」など
        public float radius = 1.7f;
        /// <summary>このトンネルから出てきたときに見せる景色（なければ、エリアのまん中を向く）。</summary>
        public ArrivalView arrival;
        /// <summary>
        /// あとから足したトンネル：エリアのしずく・いきものを置いたあとに作る（しずくの場所と数を変えないように）。
        /// 景色の通り道も作らない（草や小物の置き方が変わってしまうので）。
        /// </summary>
        public bool late;
        /// <summary>
        /// とびら（山小屋のとびら）：トンネルのかわりに、とびらへ入ると移動する。とびらの面（position）の前、
        /// はば halfWidth・高さ height の中に頭が入ったら。radius は、とびらの面からの近さ。
        /// </summary>
        public bool door;
        /// <summary>とびらへ入る向き（とびらの面の外から中へ）。</summary>
        public Vector2 walk;
        public float halfWidth = 1.3f;
        public float height = 4f;

        /// <summary>とびらの前（ヒントを出す所）：とびらの面から 6 以内、とびらのはばより少し広い所。</summary>
        public bool DoorNear(Vector3 head, Vector3 at)
        {
            Vector3 rel = head - at;
            Vector3 w = new Vector3(walk.x, 0f, walk.y).normalized;
            float along = Vector3.Dot(rel, w);
            float side = Mathf.Abs(Vector3.Dot(rel, new Vector3(-w.z, 0f, w.x)));
            return along > -6f && side < halfWidth + 2f && rel.y > -1.5f && rel.y < height + 2f;
        }

        /// <summary>とびらに入ったか（頭の場所 head、とびらの床の点 at）。</summary>
        public bool DoorReached(Vector3 head, Vector3 at)
        {
            Vector3 rel = head - at;
            Vector3 w = new Vector3(walk.x, 0f, walk.y).normalized;
            float along = Vector3.Dot(rel, w);
            float side = Mathf.Abs(Vector3.Dot(rel, new Vector3(-w.z, 0f, w.x)));
            return along > -radius && along < 6f && side < halfWidth && rel.y > -1.5f && rel.y < height;
        }
    }

    /// <summary>
    /// ひとつのエリア（森・川辺）の地形と配置の決まりごと。
    /// 生成・移動判定・ミニマップなどは現在のエリア（<see cref="Areas.Current"/>）を通して参照する。
    /// </summary>
    public abstract class AreaLayout
    {
        /// <summary>
        /// しゃくとりむしが沈まずに歩ける水の深さ（体の太さくらい）。これより浅い水ぎわや水たまりには入れる。
        /// 「水の中」は、この深さより深い所。
        /// </summary>
        public const float WadeDepth = 0.1f;

        /// <summary>
        /// しずくを置いたあとに足した地面の形（泉や水たまりの下手の土手）をふくめるか。ふだんは true。
        /// エリアを作るときは、しずくを置くまで false にして、もとの形で作る（しずくの場所・数と、乱数の使い方を変えない）。
        /// </summary>
        public static bool LateGround = true;

        /// <summary>しずくを置いたあとに地面の形を足す所（中心と半径）。そこの地形を作りなおし、上の物を持ち上げる。</summary>
        public virtual IEnumerable<(Vector2 center, float radius)> LateGroundZones { get { yield break; } }

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Subtitle { get; }
        public abstract string Tagline { get; }      // エリアに入ったときのひとこと
        public abstract string DropName { get; }     // 「森のしずく」など
        public virtual float PlayRadius => 66f;
        public virtual float TerrainHalf => 96f;
        public virtual float MaxClimbHeight => 70f;
        public virtual float MapExtent => 80f;
        public abstract int DropIdOffset { get; }
        public abstract int DropCount { get; }
        public abstract Vector2 Spawn { get; }
        public abstract Vector3 SpawnForward { get; }
        public abstract List<LandmarkDef> Landmarks { get; }
        public abstract List<GateDef> Gates { get; }

        public abstract float Height(float x, float z);
        public abstract Vector3 Normal(float x, float z);
        public abstract Color GroundColor(float x, float z, float h, Vector3 n);
        public abstract float TrailMask(float x, float z);
        /// <summary>歩く小道のまん中の線（地図や、いきものの居場所を決めるのに使う）。</summary>
        public abstract List<Vector2[]> TrailLines { get; }

        /// <summary>歩く小道のまん中の線までの距離。</summary>
        public float DistToTrail(Vector2 p)
        {
            float best = 99f;
            foreach (var t in TrailLines)
                for (int i = 0; i < t.Length - 1; i++)
                    best = Mathf.Min(best, ShakuMath.DistToSegment(p, t[i], t[i + 1]));
            return best;
        }
        public abstract float WaterLevelAt(float x, float z);
        public abstract bool IsUnderwater(Vector3 p);

        public Vector3 Ground(float x, float z) => new Vector3(x, Height(x, z), z);

        public virtual bool InPlayArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < PlayRadius && p.y < MaxClimbHeight;

        /// <summary>この場所が水の上の陸地か（margin ぶん水面より高いか）。</summary>
        public bool IsLand(float x, float z, float margin) => Height(x, z) > WaterLevelAt(x, z) + margin;

        /// <summary>別のエリアから来たときに出てくる場所（そのエリアへのトンネルの手前）。</summary>
        public bool ArrivalFrom(string fromArea, out Vector2 point, out Vector3 forward)
        {
            var v = ArrivalViewFrom(fromArea);
            if (v != null)
            {
                point = v.from;
                forward = v.Forward;
                return true;
            }
            foreach (var g in Gates)
            {
                if (g.targetArea != fromArea) continue;
                Vector2 inward = (-g.position).normalized;
                point = g.position + inward * 5.5f;
                forward = new Vector3(inward.x, 0f, inward.y);
                return true;
            }
            point = Spawn;
            forward = SpawnForward;
            return false;
        }

        /// <summary>別のエリアから来たときに見せる景色（なければ null）。</summary>
        public ArrivalView ArrivalViewFrom(string fromArea)
        {
            foreach (var g in Gates)
                if (g.targetArea == fromArea) return g.arrival;
            return null;
        }

        public LandmarkDef EntryLandmark => Landmarks.Count > 0 ? Landmarks[0] : null;
    }

    public class ForestArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            new GateDef { position = ForestLayout.Gate, targetArea = "river", label = "川辺へ",
                arrival = new ArrivalView(ForestLayout.Gate + new Vector2(-7f, 0.5f), new Vector3(0f, 26f, 76f), 2f, 4.4f) },   // 森に入ると、大樹が見える
            new GateDef { position = ForestLayout.ParkGate, targetArea = "park", label = "公園へ",
                arrival = new ArrivalView(ForestLayout.ParkGate + new Vector2(7f, -3f), new Vector3(0f, 26f, 76f), 0f, 4.4f) },  // 公園からもどると、大樹が見える
        };

        public override string Id => "forest";
        public override string DisplayName => "森";
        public override string Subtitle => "しゃくとりの森";
        public override string Tagline => "木もれ日のさす、大きな木々の森";
        public override string DropName => "森のしずく";
        public override float PlayRadius => ForestLayout.PlayRadius;
        public override float TerrainHalf => ForestLayout.TerrainHalf;
        public override float MaxClimbHeight => ForestLayout.MaxClimbHeight;
        public override int DropIdOffset => 0;
        public override int DropCount => 45;
        public override Vector2 Spawn => ForestLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.forward;
        public override List<LandmarkDef> Landmarks => ForestLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => ForestLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => ForestLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => ForestLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => ForestLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => ForestLayout.Trails;
        public override float WaterLevelAt(float x, float z) => ForestLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => ForestLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => ForestLayout.InPlayArea(p);
    }

    public class RiverArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            new GateDef { position = RiverLayout.Gate, targetArea = "forest", label = "森へ",
                arrival = new ArrivalView(RiverLayout.Gate + new Vector2(12f, 2f), new Vector3(RiverLayout.CenterX(-2f), RiverLayout.LowerStart + 0.5f, -2f), 6f, 4.4f, clear: 34f) },   // 川辺に出ると、小道の先に川が見える
            new GateDef { position = RiverLayout.MountainGate, targetArea = "mountain", label = "山へ", late = true,
                arrival = new ArrivalView(RiverLayout.MountainGate + new Vector2(-4f, -6f), new Vector3(RiverLayout.CenterX(RiverLayout.FallZ + 4f), RiverLayout.UpperLevel + 0.5f, RiverLayout.FallZ + 4f), 6f, 4.4f) },   // 山からもどると、滝の上の流れが見える
            // 海辺へ（川の下流の、東の岸。あとから足した。しずくの場所は変えない）。
            // 海からもどると、トンネルの前で川の方を向く（景色の通り道を作ると、岸の流木の置き方が変わるので、作らない）
            new GateDef { position = RiverLayout.CoastGate, targetArea = "coast", label = "海辺へ", late = true },
        };

        public override string Id => "river";
        public override string DisplayName => "川辺";
        public override string Subtitle => "せせらぎの小川";
        public override string Tagline => "小石と水草の、すずしい川辺";
        public override string DropName => "川のしずく";
        public override float PlayRadius => RiverLayout.PlayRadius;
        public override float TerrainHalf => RiverLayout.TerrainHalf;
        public override float MaxClimbHeight => RiverLayout.MaxClimbHeight;
        public override int DropIdOffset => 1000;
        public override int DropCount => 30;
        public override Vector2 Spawn => RiverLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.right;
        public override List<LandmarkDef> Landmarks => RiverLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => RiverLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => RiverLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => RiverLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => RiverLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => RiverLayout.Trails;
        public override float WaterLevelAt(float x, float z) => RiverLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => RiverLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => RiverLayout.InPlayArea(p);
    }

    public class ParkArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            new GateDef { position = ParkLayout.Gate, targetArea = "forest", label = "森へ",
                arrival = new ArrivalView(ParkLayout.Spawn, new Vector3(0f, 6f, 4f), 2f, 4.4f, clear: 30f) },   // 公園に出ると、遊具が見わたせる
            // 神社へ（北東のさくの所。あとから足した。しずくの場所は変えない）。神社からもどると、ブランコの方を見る
            new GateDef { position = ParkLayout.ShrineGate, targetArea = "shrine", label = "神社へ", late = true,
                arrival = new ArrivalView(ParkLayout.ShrineGate + new Vector2(-3.5f, -5.5f), new Vector3(ParkLayout.Swing.x, 4f, ParkLayout.Swing.y), 4f, 4.6f) },
        };

        public override string Id => "park";
        public override string DisplayName => "公園";
        public override string Subtitle => "ひだまり公園";
        public override string Tagline => "すべり台にブランコ、しばふの広がる、ひだまりの公園";
        public override string DropName => "公園のしずく";
        public override float PlayRadius => ParkLayout.PlayRadius;
        public override float TerrainHalf => ParkLayout.TerrainHalf;
        public override float MaxClimbHeight => ParkLayout.MaxClimbHeight;
        public override int DropIdOffset => 2000;
        public override int DropCount => 35;
        public override Vector2 Spawn => ParkLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.left;
        public override List<LandmarkDef> Landmarks => ParkLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => ParkLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => ParkLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => ParkLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => ParkLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => ParkLayout.Trails;
        public override float WaterLevelAt(float x, float z) => ParkLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => ParkLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => ParkLayout.InPlayArea(p);
        public override IEnumerable<(Vector2 center, float radius)> LateGroundZones { get { yield return (ParkLayout.Puddle, ParkLayout.PuddleRadius + 3f); } }
    }

    public class MountainArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            new GateDef { position = MountainLayout.Gate, targetArea = "river", label = "川辺へ",
                arrival = new ArrivalView(MountainLayout.Spawn, new Vector3(MountainLayout.Summit.x, 30f, MountainLayout.Summit.y), -2f, 4.6f, clear: 40f) },   // 山に出ると、雲のむこうの山頂を見上げる
            // 山小屋のとびら（あとから足した。しずくの場所は変えない）。小屋から出ると、花畑の方を見下ろす
            new GateDef { position = MountainLayout.HutDoor, targetArea = "hut", label = "山小屋へ", late = true,
                door = true, walk = -MountainLayout.HutDoorDir, radius = 0.8f, halfWidth = 1.4f, height = 4.6f,
                arrival = new ArrivalView(MountainLayout.HutLocal(-3.7f, 8.9f), new Vector3(MountainLayout.Meadow.x, 8f, MountainLayout.Meadow.y), 6f, 4.4f, clear: 4f) },   // 石段の前の小道に出て、花畑への下り道を見る（草をどけるのは、すぐ前だけ。花畑のバッタのすみかにかからない）
        };

        public override string Id => "mountain";
        public override string DisplayName => "山";
        public override string Subtitle => "雲の見える山";
        public override string Tagline => "岩と花と雪の、すずしい風のふく山";
        public override string DropName => "山のしずく";
        public override float PlayRadius => MountainLayout.PlayRadius;
        public override float TerrainHalf => MountainLayout.TerrainHalf;
        public override float MaxClimbHeight => MountainLayout.MaxClimbHeight;
        public override int DropIdOffset => 3000;
        public override int DropCount => 40;
        public override Vector2 Spawn => MountainLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.forward;
        public override List<LandmarkDef> Landmarks => MountainLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => MountainLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => MountainLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => MountainLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => MountainLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => MountainLayout.Trails;
        public override float WaterLevelAt(float x, float z) => MountainLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => MountainLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => MountainLayout.InPlayArea(p);
        public override IEnumerable<(Vector2 center, float radius)> LateGroundZones { get { yield return (MountainLayout.Spring, MountainLayout.SpringRadius + 8f); } }
    }

    public class HutArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            // 南のかべの、あいたとびら（しきいをこえると、山へ）。小屋に入ると、高い天井と部屋の中を見わたす
            new GateDef { position = HutLayout.DoorGate, targetArea = "mountain", label = "山へ",
                door = true, walk = Vector2.down, radius = 1.2f, halfWidth = (HutLayout.DoorB - HutLayout.DoorA) * 0.5f - 0.5f, height = 12f,
                arrival = new ArrivalView(HutLayout.Spawn, new Vector3(10f, 30f, 30f), -10f, 4.6f) },
        };

        public override string Id => "hut";
        public override string DisplayName => "山小屋";
        public override string Subtitle => "あたたかい山小屋";
        public override string Tagline => "まきストーブのぬくもりと、木のにおいの、小屋の中";
        public override string DropName => "小屋のしずく";
        public override float PlayRadius => HutLayout.PlayRadius;
        public override float TerrainHalf => HutLayout.TerrainHalf;
        public override float MaxClimbHeight => HutLayout.MaxClimbHeight;
        public override int DropIdOffset => 4000;
        public override int DropCount => 30;
        public override Vector2 Spawn => HutLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.forward;
        public override List<LandmarkDef> Landmarks => HutLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => HutLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => HutLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => HutLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => HutLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => HutLayout.Trails;
        public override float WaterLevelAt(float x, float z) => HutLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => HutLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => HutLayout.InPlayArea(p);
    }

    public class CoastArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            // 海辺に出ると、砂丘のむこうに海が見える
            new GateDef { position = CoastLayout.Gate, targetArea = "river", label = "川辺へ",
                arrival = new ArrivalView(CoastLayout.Spawn, new Vector3(0f, 0f, -40f), 6f, 4.6f, clear: 40f) },
        };

        public override string Id => "coast";
        public override string DisplayName => "海岸";
        public override string Subtitle => "潮風の海辺";
        public override string Tagline => "波の音と、潮のかおりの、砂浜と磯";
        public override string DropName => "海のしずく";
        public override float PlayRadius => CoastLayout.PlayRadius;
        public override float TerrainHalf => CoastLayout.TerrainHalf;
        public override float MaxClimbHeight => CoastLayout.MaxClimbHeight;
        public override int DropIdOffset => 5000;
        public override int DropCount => 30;
        public override Vector2 Spawn => CoastLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.back;
        public override List<LandmarkDef> Landmarks => CoastLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => CoastLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => CoastLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => CoastLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => CoastLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => CoastLayout.Trails;
        public override float WaterLevelAt(float x, float z) => CoastLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => CoastLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => CoastLayout.InPlayArea(p);
    }

    public class ShrineArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            // 神社に出ると、参道の先に大鳥居と、石段の上のお社が見える
            new GateDef { position = ShrineLayout.Gate, targetArea = "park", label = "公園へ",
                arrival = new ArrivalView(ShrineLayout.Spawn, new Vector3(ShrineLayout.Hall.x, ShrineLayout.PlatformY, ShrineLayout.Hall.y), 4f, 4.6f, clear: 40f) },
            // 夜の森へ（奥の宮のうら。あとから足した。しずくの場所は変えない）。夜の森からもどると、千本鳥居の方を見下ろす
            new GateDef { position = ShrineLayout.NightGate, targetArea = "night", label = "夜の森へ", late = true,
                arrival = new ArrivalView(ShrineLayout.NightGate - ShrineLayout.NightGate.normalized * 7f, new Vector3(30f, 3f, 12f), 4f, 4.6f) },
        };

        public override string Id => "shrine";
        public override string DisplayName => "神社";
        public override string Subtitle => "鎮守の森のお社";
        public override string Tagline => "朱色の鳥居と、苔むした石灯籠の、しずかなお社";
        public override string DropName => "神社のしずく";
        public override float PlayRadius => ShrineLayout.PlayRadius;
        public override float TerrainHalf => ShrineLayout.TerrainHalf;
        public override float MaxClimbHeight => ShrineLayout.MaxClimbHeight;
        public override int DropIdOffset => 6000;
        public override int DropCount => 30;
        public override Vector2 Spawn => ShrineLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.forward;
        public override List<LandmarkDef> Landmarks => ShrineLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => ShrineLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => ShrineLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => ShrineLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => ShrineLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => ShrineLayout.Trails;
        public override float WaterLevelAt(float x, float z) => ShrineLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => ShrineLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => ShrineLayout.InPlayArea(p);
    }

    public class NightArea : AreaLayout
    {
        readonly List<GateDef> _gates = new List<GateDef>
        {
            // 夜の森に出ると、月あかりの林の奥に、谷の方が見える
            new GateDef { position = NightLayout.Gate, targetArea = "shrine", label = "神社へ",
                arrival = new ArrivalView(NightLayout.Spawn, new Vector3(0f, 2f, -10f), 4f, 4.6f, clear: 40f) },
        };

        public override string Id => "night";
        public override string DisplayName => "夜の森";
        public override string Subtitle => "月あかりの森";
        public override string Tagline => "虫の声と、月あかり。ひんやりとした、秋の夜の雑木林";
        public override string DropName => "夜露のしずく";
        public override float PlayRadius => NightLayout.PlayRadius;
        public override float TerrainHalf => NightLayout.TerrainHalf;
        public override float MaxClimbHeight => NightLayout.MaxClimbHeight;
        public override int DropIdOffset => 7000;
        public override int DropCount => 30;
        public override Vector2 Spawn => NightLayout.Spawn;
        public override Vector3 SpawnForward => Vector3.forward;
        public override List<LandmarkDef> Landmarks => NightLayout.Landmarks;
        public override List<GateDef> Gates => _gates;
        public override float Height(float x, float z) => NightLayout.Height(x, z);
        public override Vector3 Normal(float x, float z) => NightLayout.Normal(x, z);
        public override Color GroundColor(float x, float z, float h, Vector3 n) => NightLayout.GroundColor(x, z, h, n);
        public override float TrailMask(float x, float z) => NightLayout.TrailMask(x, z);
        public override List<Vector2[]> TrailLines => NightLayout.Trails;
        public override float WaterLevelAt(float x, float z) => NightLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => NightLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => NightLayout.InPlayArea(p);
        // 池とぬた場のふちの土手は、しずくを置いたあとに盛る（しずくの場所は変えない）
        public override IEnumerable<(Vector2 center, float radius)> LateGroundZones
        {
            get
            {
                yield return (NightLayout.Pond, NightLayout.PondRadii.x * 1.6f + 2f);
                yield return (NightLayout.Wallow, NightLayout.WallowR * 1.7f + 2f);
            }
        }
    }

    /// <summary>エリアの一覧と、いまいるエリア。</summary>
    public static class Areas
    {
        public static readonly ForestArea Forest = new ForestArea();
        public static readonly RiverArea River = new RiverArea();
        public static readonly ParkArea Park = new ParkArea();
        public static readonly MountainArea Mountain = new MountainArea();
        public static readonly HutArea Hut = new HutArea();
        public static readonly CoastArea Coast = new CoastArea();
        public static readonly ShrineArea Shrine = new ShrineArea();
        public static readonly NightArea Night = new NightArea();
        public static readonly AreaLayout[] All = { Forest, River, Park, Mountain, Hut, Coast, Shrine, Night };

        public static AreaLayout Current { get; set; } = Forest;

        public static AreaLayout Get(string id)
        {
            foreach (var a in All)
                if (a.Id == id) return a;
            return Forest;
        }

        public static IEnumerable<LandmarkDef> AllLandmarks()
        {
            foreach (var a in All)
                foreach (var lm in a.Landmarks)
                    yield return lm;
        }

        public static int TotalLandmarks
        {
            get
            {
                int n = 0;
                foreach (var a in All) n += a.Landmarks.Count;
                return n;
            }
        }

        public static int TotalDrops
        {
            get
            {
                int n = 0;
                foreach (var a in All) n += a.DropCount;
                return n;
            }
        }

        /// <summary>保存用のしずく ID がどのエリアのものか。</summary>
        public static AreaLayout AreaOfDrop(int id)
        {
            AreaLayout best = Forest;
            foreach (var a in All)
                if (id >= a.DropIdOffset && a.DropIdOffset >= best.DropIdOffset) best = a;
            return best;
        }
    }
}

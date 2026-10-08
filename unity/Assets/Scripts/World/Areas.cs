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
    }

    /// <summary>
    /// ひとつのエリア（森・川辺）の地形と配置の決まりごと。
    /// 生成・移動判定・ミニマップなどは現在のエリア（<see cref="Areas.Current"/>）を通して参照する。
    /// </summary>
    public abstract class AreaLayout
    {
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
        public override float WaterLevelAt(float x, float z) => ParkLayout.WaterLevelAt(x, z);
        public override bool IsUnderwater(Vector3 p) => ParkLayout.IsUnderwater(p);
        public override bool InPlayArea(Vector3 p) => ParkLayout.InPlayArea(p);
    }

    /// <summary>エリアの一覧と、いまいるエリア。</summary>
    public static class Areas
    {
        public static readonly ForestArea Forest = new ForestArea();
        public static readonly RiverArea River = new RiverArea();
        public static readonly ParkArea Park = new ParkArea();
        public static readonly AreaLayout[] All = { Forest, River, Park };

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

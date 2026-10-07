using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Shakutori
{
    /// <summary>図鑑に出す、いきもののすみか（どのエリアの、どの名所のまわりにいるか・地図の上の場所）。</summary>
    public struct HabitatSpot
    {
        public string species;
        public string area;
        public Vector2 position;   // XZ
        public float radius;
        public string landmark;    // そばの名所（なければ空）
    }

    /// <summary>
    /// いきもののすみか。エリアを作るたびに、そのエリアのいきものの群れの場所を記録し、セーブにのこす
    /// （前に行ったエリアのいきものも、図鑑ですみかが見られるように）。
    /// </summary>
    public static class Habitats
    {
        /// <summary>名所の「まわり」とみなす距離（名所の広さに足す）。</summary>
        public const float NearLandmark = 16f;

        // この起動のあいだに作ったエリアの記録（「最初から」でセーブを消しても、世界のようすは変わらないので残す）
        static readonly Dictionary<string, List<string>> Session = new Dictionary<string, List<string>>();

        /// <summary>このエリアのいきものの群れの場所を記録する（同じエリアの古い記録は入れかえる）。</summary>
        public static void Record(AreaLayout area, IEnumerable<MobGroup> groups)
        {
            if (area == null || groups == null) return;
            var entries = new List<string>();
            foreach (var g in groups)
            {
                if (string.IsNullOrEmpty(g.species)) continue;
                var c = CultureInfo.InvariantCulture;
                entries.Add(string.Join("|", g.species, area.Id, g.center.x.ToString("F1", c), g.center.z.ToString("F1", c),
                    g.radius.ToString("F1", c), NearestLandmark(area, g.center)));
            }
            Session[area.Id] = entries;
            var list = SaveSystem.Data.habitats;
            list.RemoveAll(e => Parse(e, out var h) && h.area == area.Id);
            list.AddRange(entries);
        }

        /// <summary>テスト用：この起動のあいだの記録を消す。</summary>
        public static void ClearSession() => Session.Clear();

        /// <summary>この起動で作ったエリアの記録と、セーブにのこっている、ほかのエリアの記録。</summary>
        static IEnumerable<string> AllEntries()
        {
            foreach (var kv in Session)
                foreach (var e in kv.Value) yield return e;
            foreach (var e in SaveSystem.Data.habitats)
                if (Parse(e, out var h) && !Session.ContainsKey(h.area)) yield return e;
        }

        static string NearestLandmark(AreaLayout area, Vector3 p)
        {
            string best = "";
            float bestD = float.MaxValue;
            foreach (var lm in area.Landmarks)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), lm.position);
                if (d < lm.radius + NearLandmark && d < bestD)
                {
                    bestD = d;
                    best = lm.name;
                }
            }
            return best;
        }

        static bool Parse(string e, out HabitatSpot h)
        {
            h = default;
            if (string.IsNullOrEmpty(e)) return false;
            var f = e.Split('|');
            if (f.Length < 6) return false;
            var c = CultureInfo.InvariantCulture;
            if (!float.TryParse(f[2], NumberStyles.Float, c, out float x) || !float.TryParse(f[3], NumberStyles.Float, c, out float z)
                || !float.TryParse(f[4], NumberStyles.Float, c, out float r)) return false;
            h = new HabitatSpot { species = f[0], area = f[1], position = new Vector2(x, z), radius = r, landmark = f[5] };
            return true;
        }

        /// <summary>そのいきもののすみか（レアないきものは、もとになるいきものと同じ場所）。</summary>
        public static List<HabitatSpot> Of(string species)
        {
            var result = new List<HabitatSpot>();
            var sp = SpeciesCatalog.Get(species);
            string id = sp != null && sp.IsRare ? sp.rareOf : species;
            foreach (var e in AllEntries())
                if (Parse(e, out var h) && h.species == id) result.Add(h);
            return result;
        }

        /// <summary>
        /// すみかの説明：「森：どんぐり広場・花の草原のまわり」のように、エリアごとに、そばの名所をならべる。
        /// まだ記録がなければ、図鑑のエリアの名前だけ。
        /// </summary>
        public static string Describe(string species)
        {
            var spots = Of(species);
            var sp = SpeciesCatalog.Get(species);
            if (spots.Count == 0) return sp != null ? sp.areaLabel : "";
            var byArea = new List<string>();
            var names = new Dictionary<string, List<string>>();
            foreach (var h in spots)
            {
                if (!names.TryGetValue(h.area, out var l))
                {
                    l = new List<string>();
                    names[h.area] = l;
                    byArea.Add(h.area);
                }
                if (!string.IsNullOrEmpty(h.landmark) && !l.Contains(h.landmark)) l.Add(h.landmark);
            }
            var sb = new StringBuilder();
            foreach (var a in byArea)
            {
                if (sb.Length > 0) sb.Append("／");
                var layout = Areas.Get(a);
                sb.Append(layout != null ? layout.DisplayName : a);
                var l = names[a];
                if (l.Count > 0)
                {
                    sb.Append("：");
                    for (int i = 0; i < l.Count && i < 3; i++)
                    {
                        if (i > 0) sb.Append("・");
                        sb.Append(l[i]);
                    }
                    sb.Append(l.Count > 3 ? " など" : "").Append("のまわり");
                }
            }
            return sb.ToString();
        }
    }
}

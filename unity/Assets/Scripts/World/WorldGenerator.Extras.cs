using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace Shakutori
{
    /// <summary>
    /// エリアの小物の追加（エリアの改善 300 の風景・遊びと道）。しずく・いきもの・名所の景色の通り道・トンネル・スタート地点をよけて置く。
    /// しずくの場所と数は変えない（セーブと合うように）ので、ほかのものをすべて置いたあとに、別の乱数で置く。
    /// </summary>
    public partial class WorldGenerator
    {
        Random _xr;
        readonly Dictionary<string, List<Vector3>> _extraSpots = new Dictionary<string, List<Vector3>>();

        float XR(float a, float b) => a + (b - a) * (float)_xr.NextDouble();
        float XR01() => (float)_xr.NextDouble();
        string XPick(params string[] names) => names[_xr.Next(names.Length)];

        /// <summary>種類ごとに覚えておく、追加した小物の場所（地図や音・テストで使う）。</summary>
        public IReadOnlyList<Vector3> ExtraSpots(string kind) => _extraSpots.TryGetValue(kind, out var l) ? l : (IReadOnlyList<Vector3>)System.Array.Empty<Vector3>();

        /// <summary>追加した小物の種類と数（テストの説明用）。</summary>
        public string ExtraSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _extraSpots) sb.Append(kv.Key).Append('=').Append(kv.Value.Count).Append(' ');
            return sb.ToString();
        }

        void Mark(string kind, Vector3 p)
        {
            if (!_extraSpots.TryGetValue(kind, out var l)) _extraSpots[kind] = l = new List<Vector3>();
            l.Add(p);
        }

        void BuildExtras()
        {
            _xr = new Random(seed * 3 + 101 * Area.Id.Length);
            _extraSpots.Clear();
            Physics.SyncTransforms();
            if (Area.Id == "forest") BuildForestExtras();
            else if (Area.Id == "park") BuildParkExtras();
            else BuildRiverExtras();
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 追加の小物を置いてよい所か。当たり判定のある物は、しずく・いきもののすみか・ほかの物・景色の通り道から、はなして置く。
        /// 見た目だけの物（草花など）は、しずくの真上と、物の中だけよける。
        /// </summary>
        bool ExtraOk(Vector2 p, float r, bool solid, bool ignoreMobs = false)
        {
            if (new Vector2(p.x, p.y).magnitude > Area.PlayRadius - 2f) return false;
            foreach (var d in DewdropPoints)
            {
                float dx = d.x - p.x, dz = d.z - p.y;
                float keep = r + (solid ? 2.2f : 0.5f);
                if (dx * dx + dz * dz < keep * keep) return false;
            }
            foreach (var gt in Area.Gates)
                if (Vector2.Distance(gt.position, p) < r + 6f) return false;
            if (Vector2.Distance(Area.Spawn, p) < r + (solid ? 4f : 1.5f)) return false;
            if (!solid) return !InsideOccupied(p);
            foreach (var m in Mobs)
            {
                if (ignoreMobs) break;
                if (!Grounded(m)) continue;   // 飛ぶもの・水の上のものは、じゃまにならない
                float dx = m.center.x - p.x, dz = m.center.z - p.y;
                float keep = Mathf.Min(m.radius, 3f) + r + 1f;   // すみかのまん中だけ、あけておく
                if (dx * dx + dz * dz < keep * keep) return false;
            }
            if (InViewLane(p, r)) return false;
            return IsFree(p, r);
        }

        /// <summary>テスト用：ExtraOk が置けない理由。</summary>
        public string ExtraWhyNot(Vector2 p, float r)
        {
            if (new Vector2(p.x, p.y).magnitude > Area.PlayRadius - 2f) return "edge";
            foreach (var d in DewdropPoints)
                if (Vector2.Distance(new Vector2(d.x, d.z), p) < r + 2.2f) return "dew";
            foreach (var gt in Area.Gates)
                if (Vector2.Distance(gt.position, p) < r + 6f) return "gate";
            if (Vector2.Distance(Area.Spawn, p) < r + 4f) return "spawn";
            foreach (var m in Mobs)
            {
                if (!Grounded(m)) continue;
                if (Vector2.Distance(new Vector2(m.center.x, m.center.z), p) < Mathf.Min(m.radius, 3f) + r + 1f) return "mob:" + m.species;
            }
            if (InViewLane(p, r)) return "lane";
            foreach (var o in _occupied)
                if (Vector2.Distance(new Vector2(o.x, o.y), p) < r + o.z) return $"occ({o.x:0.0},{o.y:0.0},{o.z:0.0})";
            return "ok";
        }

        /// <summary>地面を歩く・跳ぶいきもの（すみかのまん中に物を置かない）。飛ぶもの・水の上のものは、のぞく。</summary>
        static bool Grounded(MobGroup m)
        {
            var kind = SpeciesCatalog.Get(m.species)?.kind;
            return kind != MobKind.Bird && kind != MobKind.Flutter && kind != MobKind.Hover && kind != MobKind.Skater;
        }

        /// <summary>
        /// center のまわり（searchR 以内）で、置ける場所をさがす（近い所から、ぐるぐる外へ）。見つからなければ false。
        /// ok は、ほかの条件（陸地か、など）。
        /// </summary>
        bool FindSpot(Vector2 center, float searchR, float r, bool solid, out Vector2 spot, System.Func<Vector2, bool> ok = null, bool ignoreMobs = false)
        {
            for (int i = 0; i < 48; i++)
            {
                float d = searchR * Mathf.Sqrt(i / 47f);
                float a = i * 2.39996f;
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if ((ok == null || ok(p)) && ExtraOk(p, r, solid, ignoreMobs))
                {
                    spot = p;
                    return true;
                }
            }
            spot = center;
            return false;
        }

        /// <summary>当たり判定のある、追加の小物を置く（置けたら、まわりを使ったことにする）。</summary>
        GameObject PutSolid(string mesh, Material mat, Vector3 pos, Quaternion rot, float scale, float occupy, float maxDistance = 160f)
        {
            var go = Place(mesh, mat, pos, rot, scale, true, true, maxDistance);
            Occupy(new Vector2(pos.x, pos.z), occupy);
            return go;
        }

        /// <summary>見た目だけの草花・小物を置く。</summary>
        void PutDeco(string mesh, Material mat, Vector3 pos, Quaternion rot, float scale, bool shadows = false, float maxDistance = 90f)
        {
            Place(mesh, mat, pos, rot, scale, false, shadows, maxDistance);
        }

        /// <summary>中心のまわりに、見た目だけの草花を count 本まく。</summary>
        int ScatterDeco(Vector2 center, float radius, int count, float smin, float smax, Material mat, float sink, params string[] meshes)
        {
            int n = 0;
            for (int i = 0; i < count * 3 && n < count; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Mathf.Sqrt(XR01()) * radius);
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.2f, false)) continue;
                Vector3 g = Area.Ground(p.x, p.y) + Vector3.down * sink;
                PutDeco(meshes[_xr.Next(meshes.Length)], mat, g, GroundRotation(p, XR(0f, 360f), 0.3f, 5f), XR(smin, smax), true, 110f);
                n++;
            }
            return n;
        }

        /// <summary>押すと動く小物（小石・松ぼっくり）を、中心のまわりに置く。</summary>
        int ScatterLoose(Vector2 center, float radius, int count, float smin, float smax, LooseProps.Shape shape, params string[] meshes)
        {
            int n = 0;
            for (int i = 0; i < count * 3 && n < count; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Mathf.Sqrt(XR01()) * radius);
                if (!IsLand(p, 0.05f) || !ExtraOk(p, 0.3f, false)) continue;
                Vector3 g = Area.Ground(p.x, p.y);
                Quaternion rot = shape == LooseProps.Shape.Pinecone
                    ? Quaternion.Euler(0f, XR(0f, 360f), 0f) * Quaternion.Euler(XR(70f, 95f), 0f, 0f)
                    : Quaternion.Euler(XR(-15f, 15f), XR(0f, 360f), XR(-15f, 15f));
                float s = XR(smin, smax);
                if (shape == LooseProps.Shape.Pinecone) g += Vector3.up * 0.45f * s;
                PlaceLoose(meshes[_xr.Next(meshes.Length)], assets.prop, g, rot, s, shape, s > 0.5f, 60f);
                n++;
            }
            return n;
        }

        /// <summary>小石を積んだ目じるし（ケルン）。登って見わたせる。</summary>
        bool Cairn(Vector2 near)
        {
            if (!FindSpot(near, 5f, 1.4f, true, out var p, q => IsLand(q, 0.2f))) return false;
            Vector3 g = Area.Ground(p.x, p.y);
            float y = 0f;
            float[] sizes = { 1.15f, 0.85f, 0.6f };
            for (int i = 0; i < sizes.Length; i++)
            {
                float s = sizes[i];
                Place(i == 0 ? "Rock_C" : i == 1 ? "Rock_A" : "Rock_D", assets.prop, g + Vector3.up * (y - 0.12f * s), Quaternion.Euler(XR(-6f, 6f), XR(0f, 360f), XR(-6f, 6f)), s, true);
                y += 0.85f * s;
            }
            Occupy(p, 1.6f);
            Mark("cairn", g);
            return true;
        }

        /// <summary>平たい石（休める・水をのぞける）。allowWater = true なら、水ぎわの水の上へつき出してもよい。</summary>
        bool FlatRock(Vector2 near, float scale, string kind, bool allowWater = false, bool ignoreMobs = false)
        {
            if (!FindSpot(near, 2.5f, 1.2f * scale, true, out var p, q => allowWater || IsLand(q, 0.15f), ignoreMobs)) return false;
            float top = Mathf.Max(Area.Height(p.x, p.y), Area.WaterLevelAt(p.x, p.y) + 0.1f);
            Mesh m = assets.Get("RiverStone_C");
            float h = m != null ? m.bounds.max.y * scale : 0.4f;
            PutSolid("RiverStone_C", assets.prop, new Vector3(p.x, top + 0.18f - h * 0.6f, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), scale, 1.3f * scale);
            Mark(kind, new Vector3(p.x, top, p.y));
            return true;
        }

        /// <summary>外周の大岩の手前の、低い石の列（ここから先は行けない目じるし）。</summary>
        void BoundaryStones(float r, int count, System.Func<Vector2, bool> skip)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f + XR(-0.03f, 0.03f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (skip(p) || !IsLand(p, 0.2f) || !ExtraOk(p, 0.8f, true)) continue;
                float s = XR(0.55f, 0.85f);
                Place(XPick("Rock_A", "Rock_B", "Rock_D"), assets.prop, Area.Ground(p.x, p.y) + Vector3.down * 0.25f * s, Quaternion.Euler(XR(-8f, 8f), XR(0f, 360f), XR(-8f, 8f)), s, true, true, 120f);
                Occupy(p, 0.9f * s);
                Mark("boundary", Area.Ground(p.x, p.y));
            }
        }

        /// <summary>遠景の、葉のしげった木（遊べる場所の外がわ、霧の中に見える森）。</summary>
        void DistantCanopy(int count, float r0, float r1, System.Func<Vector2, bool> skip)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + XR(0.1f, 0.9f)) / count * Mathf.PI * 2f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(r0, r1);
                if (skip(p)) continue;
                Vector3 g = Area.Ground(p.x, p.y) + Vector3.down * 2f;
                Place("Park_Kunugi", assets.bark, g, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(1.2f, 1.9f), false, false, 600f, asRenderer: true);
                Mark("canopy", g);
            }
        }

        /// <summary>遊べる場所の外がわの、遠くの大きな木の幹（奥行き）。</summary>
        void MoreBackgroundTrunks(int count, float r0, float r1, System.Func<Vector2, bool> skip)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + XR(0.2f, 0.8f)) / count * Mathf.PI * 2f + 0.21f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(r0, r1);
                if (skip(p)) continue;
                Vector3 g = Area.Ground(p.x, p.y) + Vector3.down * 2f;
                Place(i % 2 == 0 ? "BgTrunk_B" : "BgTrunk_A", assets.bark, g, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.7f, 1.2f), false, true, 500f, asRenderer: true);
                Mark("trunk", g);
            }
        }

        /// <summary>
        /// 小枝のスロープ・はしご・橋：a から b へ、小枝をわたす（登れる）。小枝の長さは 13.2 で、1 倍。
        /// </summary>
        GameObject TwigSpan(Vector3 a, Vector3 b, string kind)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return null;
            Quaternion rot = Quaternion.FromToRotation(Vector3.right, d / len);
            float sc = len / 13.2f;
            var go = Place("Twig_A", assets.prop, (a + b) * 0.5f - rot * Vector3.up * (0.3f * sc), rot, sc, true, true, 200f);
            for (int i = 0; i <= 4; i++) Occupy(new Vector2(Mathf.Lerp(a.x, b.x, i / 4f), Mathf.Lerp(a.z, b.z, i / 4f)), 1.2f);
            Mark(kind, (a + b) * 0.5f);
            return go;
        }

        /// <summary>
        /// 大きな物（切り株・大樹・丸太）の側面に、サルノコシカケを段々に生やす。中心から外へ水平に光線をとばして、側面をさがす。
        /// </summary>
        int ShelfStairs(Vector3 center, float outward, float h0, float h1, float step, float a0Deg, float aStepDeg, float scale, string target, string kind)
        {
            int n = 0;
            float a = a0Deg;
            for (float h = h0; h <= h1; h += step, a += aStepDeg)
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.right;
                Vector3 from = center + dir * outward + Vector3.up * h;
                if (!Physics.Raycast(from, -dir, out var hit, outward + 2f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (!hit.collider.name.StartsWith(target)) continue;
                Vector3 nrm = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (nrm.sqrMagnitude < 0.2f) continue;
                nrm.Normalize();
                Vector3 p = hit.point - nrm * 0.25f * scale;
                Place("ShelfFungus", assets.prop, p, Quaternion.LookRotation(nrm, Vector3.up), scale * XR(0.9f, 1.1f), true, true, 160f);
                Mark(kind, p);
                n++;
            }
            return n;
        }
    }
}

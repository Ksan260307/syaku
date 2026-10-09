using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 置き方の仕上げ：草花と苔を 1 つずつ見なおす。
    /// 岩・柵・砂場の枠・落ち葉などをつきぬけて生えている物、同じ所に 2 つ重なった物、水にしずんだ陸の草花を取りのぞき、
    /// 宙にういた物・うまりすぎた物は、下の地面（や岩の上）へ置きなおして、かたむきにそわせる。
    /// 当たり判定のある物と、しずくの場所は変えない（しずくを置いたあとに行う）。
    /// </summary>
    public partial class WorldGenerator
    {
        /// <summary>仕上げで直した数（理由ごと。テスト用）。</summary>
        public readonly Dictionary<string, int> TidyCounts = new Dictionary<string, int>();
        /// <summary>仕上げで直した物の記録（理由・形・場所。テストや点検用）。</summary>
        public readonly List<(string why, string mesh, Vector3 pos)> TidyLog = new List<(string, string, Vector3)>();

        /// <summary>水ぎわ・水の中に生える草花（水の中にあってもよい）。</summary>
        static readonly HashSet<string> WaterPlants = new HashSet<string> { "Reed", "Cattail", "Horsetail", "Iris", "WaterGrass", "WaterLily", "SasaBune" };
        /// <summary>水面にうく物（葉の上・水の上にのせてある。下へは置きなおさない）。</summary>
        static readonly HashSet<string> Floaters = new HashSet<string> { "WaterGrass", "WaterLily", "SasaBune" };

        void Count(string why, string mesh, Vector3 pos)
        {
            TidyCounts[why] = TidyCounts.TryGetValue(why, out var c) ? c + 1 : 1;
            TidyLog.Add((why, mesh, pos));
        }

        static Vector3 FlowerHead(Mesh mesh, Matrix4x4 m)
        {
            Bounds b = mesh.bounds;
            return m.MultiplyPoint3x4(new Vector3(b.center.x, b.max.y * 0.95f, b.center.z));
        }

        static bool IsGround(Collider c) => c.name.StartsWith("Terrain_");

        void TidyPlacements()
        {
            Physics.SyncTransforms();
            var seen = new HashSet<(Mesh, Vector3Int)>();
            var flowerMoves = new List<(Vector3 from, Vector3? to)>();
            int mask = ShakuConst.SurfaceMask;
            float reach = Area.PlayRadius + 8f;

            instanced.Edit((mesh, mat, m) =>
            {
                if (mat != assets.foliage && mat != assets.flowers) return m;
                string n = mesh.name;
                if (n == "SpiderWeb") return m;
                Vector3 pos = m.GetColumn(3);
                if (new Vector2(pos.x, pos.z).magnitude > reach) return m;   // 遠くの飾り
                bool flower = FlowerMeshes.Contains(n);
                Matrix4x4? Drop(string why, string on = null)
                {
                    Count(why, on != null ? n + "|" + on : n, pos);
                    if (flower) flowerMoves.Add((FlowerHead(mesh, m), null));
                    return null;
                }
                float sc = m.lossyScale.x;
                float h = Mathf.Max(0.05f, mesh.bounds.max.y * sc);

                // 同じ所に、同じ物が 2 つ（ちらついて見える）
                if (!seen.Add((mesh, Vector3Int.RoundToInt(pos * 3f)))) return Drop("duplicate");
                // 水にしずんだ、陸の草花
                if (!WaterPlants.Contains(n) && Area.IsUnderwater(pos + Vector3.up * 0.12f)) return Drop("underwater");
                if (Floaters.Contains(n)) return m;
                // 小道のまん中（ふまれて、草花は生えない）
                if (!WaterPlants.Contains(n) && Area.TrailMask(pos.x, pos.z) > 0.78f) return Drop("onTrail");
                // 公園の砂の地面（砂場・ブランコとすべり台とジャングルジムの下）
                if (Area.Id == "park" && ParkLayout.SandMask(pos.x, pos.z) > 0.55f) return Drop("onSand");
                // 根もとの真上に、ほかの物（岩・柵・枠・落ち葉・丸太）がある：つきぬけて生えている
                float probe = Mathf.Min(h, 2.5f) + 0.65f;   // 草花の先にのった落ち葉もみつける
                Vector3 above = pos + Vector3.up * probe;
                if (Physics.Raycast(above, Vector3.down, out var cover, probe - 0.02f, mask, QueryTriggerInteraction.Ignore) && !IsGround(cover.collider))
                {
                    bool leaf = cover.collider.GetComponentInParent<BigLeaf>() != null;
                    // 岩の上の苔のように、その物の上に置いた物はそのまま
                    if (leaf || cover.point.y - pos.y > 0.12f) return Drop(leaf ? "underLeaf" : "pierced", cover.collider.name);
                }
                // 上に、ほかの物（大樹の根・丸太・ベンチの座面・キノコのかさ）がおおいかぶさっていて、草花の先がつきぬけている
                if (h > 0.3f && Physics.Raycast(pos + Vector3.up * 0.15f, Vector3.up, out var roof, h * 0.85f, mask, QueryTriggerInteraction.Ignore) && !IsGround(roof.collider))
                    return Drop("underRoof", roof.collider.name);
                // 宙にういている・うまりすぎている物は、下の面へ置きなおす
                if (!Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out var below, 3f, mask, QueryTriggerInteraction.Ignore)) return m;
                float gap = pos.y - below.point.y;
                Vector3 upNow = m.MultiplyVector(Vector3.up).normalized;
                bool moss = n == "Moss";
                // 苔が、岩の横や急な坂に、たてにはりついている
                if (moss && below.normal.y < 0.6f) return Drop("steep");
                bool tilted = moss && Vector3.Angle(upNow, below.normal) > 14f;
                bool floating = gap > 0.12f;
                bool sunk = !moss && gap < -Mathf.Max(0.2f, 0.3f * h);
                Matrix4x4 nm = m;
                if (floating || sunk || tilted)
                {
                    Count(floating ? "floating" : sunk ? "sunk" : "tilted", n, pos);
                    Quaternion rot = m.rotation;
                    if (moss)
                    {
                        // 苔は、地面のかたむきにぴったりそわせる
                        Vector3 fwd = Vector3.ProjectOnPlane(rot * Vector3.forward, below.normal);
                        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, below.normal);
                        rot = Quaternion.LookRotation(fwd.normalized, below.normal);
                    }
                    nm = Matrix4x4.TRS(new Vector3(pos.x, below.point.y - (moss ? 0.04f : 0.03f), pos.z), rot, Vector3.one * sc);
                }
                if (moss && RimOverhangs(nm))
                {
                    // 苔のふちが、岩のかどや急な坂から、ひさしのようにはみ出している：小さくする（それでもはみ出すなら、取りのぞく）
                    var small = Matrix4x4.TRS(nm.GetColumn(3), nm.rotation, Vector3.one * sc * 0.55f);
                    if (RimOverhangs(small)) return Drop("overhang");
                    Count("shrunk", n, pos);
                    nm = small;
                }
                if (flower && nm != m) flowerMoves.Add((FlowerHead(mesh, m), FlowerHead(mesh, nm)));
                return nm;

                bool RimOverhangs(Matrix4x4 t)
                {
                    // 苔のふちの 8 か所のうち、3 か所より多くが、下の面から 0.25 より高くういている
                    Bounds bb = mesh.bounds;
                    float rx = bb.extents.x * 0.85f, rz = bb.extents.z * 0.85f;
                    int off = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = i * Mathf.PI / 4f;
                        Vector3 q = t.MultiplyPoint3x4(new Vector3(bb.center.x + Mathf.Cos(ang) * rx, bb.min.y, bb.center.z + Mathf.Sin(ang) * rz));
                        // 上り坂がわのふちは地面の下にあるので、高い所から下へさがす
                        if (!Physics.Raycast(q + Vector3.up * 2.5f, Vector3.down, out var rh, 5.5f, mask, QueryTriggerInteraction.Ignore) || q.y - rh.point.y > 0.3f) off++;
                    }
                    return off > 3;
                }
            });

            // 花の頭の場所（チョウがとまる所）も、いっしょに直す
            foreach (var (from, to) in flowerMoves)
            {
                int best = -1;
                float bd = 0.05f;
                for (int i = 0; i < FlowerPoints.Count; i++)
                {
                    float d = (FlowerPoints[i] - from).sqrMagnitude;
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) continue;
                if (to.HasValue) FlowerPoints[best] = to.Value;
                else FlowerPoints.RemoveAt(best);
            }
        }
    }
}

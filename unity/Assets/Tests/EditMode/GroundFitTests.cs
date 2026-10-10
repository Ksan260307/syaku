using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// 物と地面のなじみ：水のふちが地面からうかない（泉・水たまりの土手）、しずくがうまらない、
    /// 松の根が地面にとどく、オコジョのすみかの土が坂にそう、坂の岩の下に大きなすき間がない。
    /// </summary>
    public class GroundFitTests
    {
        static readonly Dictionary<string, WorldGenerator> Gens = new Dictionary<string, WorldGenerator>();
        static readonly Dictionary<string, List<Vector3>> DewsOf = new Dictionary<string, List<Vector3>>();

        /// <summary>エリアを 1 つ、新しい場面で作る（ほかのエリアの物がまざらないように）。</summary>
        static WorldGenerator Make(string id)
        {
            var area = Areas.Get(id);
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World_" + id);
            var gen = go.AddComponent<WorldGenerator>();
            gen.assets = TestUtil.LoadWorldAssets();
            gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = area;
            gen.GenerateNow(area);
            Physics.SyncTransforms();
            return gen;
        }

        [TearDown]
        public void TearDown() => Areas.Current = Areas.Forest;

        static float TerrainY(Vector3 p)
        {
            float best = -999f;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + 40f, p.z), Vector3.down, 120f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                if (h.collider.name.StartsWith("Terrain_")) best = Mathf.Max(best, h.point.y);
            return best;
        }

        /// <summary>点が物の中にあるか（上へのレイが、物の内側から面に当たる）。</summary>
        static Collider Inside(Vector3 p)
        {
            Physics.queriesHitBackfaces = true;
            bool hit = Physics.Raycast(p, Vector3.up, out var h, 4f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
            Physics.queriesHitBackfaces = false;
            if (!hit || h.normal.y <= 0.05f) return null;
            return Physics.Raycast(h.point + Vector3.up * 0.01f, Vector3.down, out var h2, 0.05f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) && h2.collider == h.collider ? h.collider : null;
        }

        [Test]
        public void WaterEdges_StayUnderTheGround([Values("forest", "river", "park", "mountain")] string id)
        {
            var gen = Make(id);
            try
            {
                int checkedEdges = 0;
                foreach (var mf in gen.Root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.gameObject.layer != ShakuConst.WaterLayer || mf.name.Contains("fall") || mf.name.Contains("Stream")) continue;
                    var mesh = mf.sharedMesh;
                    var v = mesh.vertices;
                    var t = mesh.triangles;
                    var edges = new Dictionary<(int, int), int>();
                    for (int i = 0; i < t.Length; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int a = t[i + e], b = t[i + (e + 1) % 3];
                            var k = a < b ? (a, b) : (b, a);
                            edges[k] = edges.TryGetValue(k, out var c) ? c + 1 : 1;
                        }
                    foreach (var kv in edges)
                    {
                        if (kv.Value != 1) continue;
                        Vector3 a = mf.transform.TransformPoint(v[kv.Key.Item1]), b = mf.transform.TransformPoint(v[kv.Key.Item2]);
                        int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.3f));
                        for (int s = 0; s < n; s++)
                        {
                            Vector3 p = Vector3.Lerp(a, b, s / (float)n);
                            if (new Vector2(p.x, p.z).magnitude > gen.Area.PlayRadius + 4f) continue;
                            if (id == "river" && Mathf.Abs(p.z - RiverLayout.FallZ) < 1.6f) continue;   // 滝の落ち口（滝の水がかくす）
                            Assert.IsTrue(Physics.Raycast(p + Vector3.up * 15f, Vector3.down, out var h, 40f, ShakuConst.SurfaceMask), $"{p} の下に地面がない");   // 水飲み場などの物の上から
                            Assert.LessOrEqual(p.y - h.point.y, 0.03f, $"{id}：{mf.name} の水のふち {p} が地面から {p.y - h.point.y:F2} ういている");
                            checkedEdges++;
                        }
                    }
                }
                Assert.Greater(checkedEdges, 40, "水のふちを調べた");
            }
            finally { gen.Clear(); }
        }

        [Test]
        public void Dewdrops_AreNotBuriedByLaterGroundOrRocks([Values("forest", "river", "park", "mountain")] string id)
        {
            var gen = Make(id);
            try
            {
                for (int i = 0; i < gen.DewdropPoints.Count; i++)
                {
                    Vector3 d = gen.DewdropPoints[i];
                    Assert.LessOrEqual(TerrainY(d), d.y + 0.03f, $"{id} のしずく {i} {d} が地面の下");
                    // 上の面にのったしずくは少し上が、天井にさがったしずく（タイヤの穴の上）は少し下が、物の外
                    var above = Inside(d + Vector3.up * 0.03f);
                    var below = Inside(d - Vector3.up * 0.03f);
                    Assert.IsFalse(above != null && above == below, $"{id} のしずく {i} {d} が {above?.name} の中（あとから動かした・足した物）");
                }
            }
            finally { gen.Clear(); }
        }

        [Test]
        public void MountainSpring_IsHeldByABankAndTheWaterCanBeEntered()
        {
            float lv = MountainLayout.SpringLevel;
            Vector2 c = MountainLayout.Spring;
            for (int k = 0; k < 64; k++)
            {
                float a = k * Mathf.PI * 2f / 64f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 edge = c + d * (MountainLayout.SpringRadius + 2.2f);
                Assert.GreaterOrEqual(MountainLayout.Height(edge.x, edge.y), lv + 0.02f, $"泉の水のふち {edge} は、地面の下");
                // 外から歩いてきて、水ぎわ（足が水につかる、浅い所）に入れる
                float shore = -1f;
                for (float r = 11f; r > 5f; r -= 0.05f)
                {
                    Vector2 p = c + d * r;
                    if (MountainLayout.Height(p.x, p.y) < lv) { shore = r; break; }
                }
                Assert.Greater(shore, MountainLayout.SpringRadius - 0.5f, $"{a * Mathf.Rad2Deg:F0} 度の水ぎわ");
                Assert.Less(shore, MountainLayout.SpringRadius + 1.5f, $"{a * Mathf.Rad2Deg:F0} 度の水ぎわが、水のふちの外にない");
                Vector2 wade = c + d * (shore - 0.15f);
                var prof = new System.Text.StringBuilder();
                for (float r = 7f; r < 11f; r += 0.25f) prof.Append($" {r:F2}:{MountainLayout.Height(c.x + d.x * r, c.y + d.y * r) - lv:F2}");
                Assert.IsFalse(MountainLayout.IsUnderwater(MountainLayout.Ground(wade.x, wade.y)), $"{a * Mathf.Rad2Deg:F0} 度：水ぎわの浅い所に入れる shore={shore:F2} 高さ:{prof}");
            }
        }

        [Test]
        public void ParkPuddle_RimHidesTheWaterEdge()
        {
            Vector2 c = ParkLayout.Puddle;
            for (int k = 0; k < 48; k++)
            {
                float a = k * Mathf.PI * 2f / 48f;
                Vector2 edge = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ParkLayout.PuddleRadius + 1.4f);
                if (Vector2.Distance(edge, ParkLayout.Fountain) < ParkLayout.FountainBaseR + 0.3f) continue;   // 水飲み場の台がかくす所
                Assert.GreaterOrEqual(ParkLayout.Height(edge.x, edge.y), ParkLayout.WaterLevel + 0.01f, $"水たまりのふち {edge}");
            }
        }

        [Test]
        public void MountainPineRoots_ReachTheGround_AndDensHugTheSlope()
        {
            var gen = Make("mountain");
            try
            {
                var pine = gen.Root.GetComponentsInChildren<MeshFilter>(true).First(m => m.name == "Mtn_Pine");
                Vector3 o = pine.transform.position;
                var minGap = new Dictionary<int, float>();
                foreach (var lv in pine.sharedMesh.vertices)
                {
                    Vector3 w = pine.transform.TransformPoint(lv);
                    Vector3 d = w - o;
                    float hd = new Vector2(d.x, d.z).magnitude;
                    if (hd < 4.2f || hd > 10f || d.y > 3f) continue;   // 幹から外の、根のあたり
                    int sector = ((int)(Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg + 360f) / 15) % 24;
                    float gap = w.y - MountainLayout.Height(w.x, w.z);
                    minGap[sector] = minGap.TryGetValue(sector, out var g) ? Mathf.Min(g, gap) : gap;
                }
                Assert.Greater(minGap.Count, 4, "根がある");
                foreach (var kv in minGap)
                    Assert.Less(kv.Value, 0.1f, $"{kv.Key * 15} 度の根が、地面から {kv.Value:F2} ういている");

                // オコジョのすみか：土の山のすそは地面の下、まん中の穴の口は地面の高さ。上に草花が生えていない
                var dens = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(cl => cl.name == "Okojo_Rocks").ToList();
                Assert.AreEqual(2, dens.Count);
                foreach (var den in dens)
                {
                    Vector3 c = den.transform.position;
                    var mesh = ((MeshCollider)den).sharedMesh;
                    var vs = mesh.vertices;
                    var cs = mesh.colors;
                    var lowest = new Dictionary<int, Vector3>();
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 w = den.transform.TransformPoint(vs[i]);
                        float r = new Vector2(w.x - c.x, w.z - c.z).magnitude;
                        bool soil = cs.Length == vs.Length && (cs[i].r - cs[i].g) / Mathf.Max(cs[i].r, 0.02f) > 0.15f;   // 土は茶色（石は灰色）
                        if (!soil || r < 2.85f || r > 3.4f) continue;
                        int sector = ((int)(Mathf.Atan2(w.z - c.z, w.x - c.x) * Mathf.Rad2Deg + 360f) / 30) % 12;
                        if (!lowest.TryGetValue(sector, out var lo) || w.y < lo.y) lowest[sector] = w;
                    }
                    int skirt = lowest.Count;
                    // 土の山のすそ（いちばん低い所）は、坂でも地面の下にかくれる
                    foreach (var kv in lowest)
                        Assert.Less(kv.Value.y, MountainLayout.Height(kv.Value.x, kv.Value.z) + 0.02f, $"すみか {c} の土のすそ {kv.Value} がういている");
                    Assert.GreaterOrEqual(skirt, 10, "土の山のすそを調べた");
                    float ground = MountainLayout.Height(c.x, c.z);
                    Assert.IsTrue(den.Raycast(new Ray(new Vector3(c.x + 1.2f, ground + 3f, c.z), Vector3.down), out var rim, 6f), "穴のふちの土");
                    Assert.AreEqual(ground + 0.15f, rim.point.y, 0.35f, "穴のふちは、地面とほとんど同じ高さ");
                    gen.instanced.Edit((m, mat, mtx) =>
                    {
                        if (mat == gen.assets.foliage || mat == gen.assets.flowers)
                        {
                            Vector3 p = mtx.GetColumn(3);
                            Assert.Greater(new Vector2(p.x - c.x, p.z - c.z).magnitude, 2.6f, $"すみかの土の山に {m.name} が生えている");
                        }
                        return mtx;
                    });
                }
            }
            finally { gen.Clear(); }
        }

        [Test]
        public void RiverFallSlope_RocksHaveNoCaveUnderneath()
        {
            var gen = Make("river");
            try
            {
                int n = 0;
                foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                {
                    if (!(col.name.StartsWith("Rock_") || col.name.StartsWith("RiverStone_"))) continue;
                    var b = col.bounds;
                    if (Mathf.Abs(b.center.z - RiverLayout.FallZ) > 8f || new Vector2(b.center.x, b.center.z).magnitude > RiverLayout.PlayRadius) continue;
                    // しずくをのせた岩（滝のわきの大岩）は動かせない（下を石でうめるだけ）。しゃくとりむしが入りこまないことは、PlayMode で見る
                    if (gen.DewdropPoints.Any(d => b.Contains(d) || Vector3.Distance(d, b.center) < b.extents.magnitude + 0.3f)) continue;
                    // 石の上に積んだ石（坂の石段）は、段のふちが前の段の上にはり出すので見ない
                    if (Physics.RaycastAll(b.center, Vector3.down, b.extents.y + 1.5f, ShakuConst.SurfaceMask).Any(h => h.collider != col && !h.collider.name.StartsWith("Terrain_"))) continue;
                    // 岩の下のふちと地面のすき間（しゃくとりむしの体より高いすき間が、深く続かない）
                    float worst = 0f;
                    for (int k = 0; k < 16; k++)
                    {
                        float a = k * Mathf.PI / 8f;
                        for (float f = 0.35f; f <= 0.96f; f += 0.15f)
                        {
                            float x = b.center.x + Mathf.Cos(a) * b.extents.x * f, z = b.center.z + Mathf.Sin(a) * b.extents.z * f;
                            if (!col.Raycast(new Ray(new Vector3(x, b.min.y - 2f, z), Vector3.up), out var under, b.size.y + 4f)) continue;
                            if (RiverLayout.IsUnderwater(new Vector3(x, RiverLayout.Height(x, z) + 0.05f, z))) continue;   // 水の中（しゃくとりむしは入れない）
                            // 下の地面（下に石をうめてあれば、その石）
                            float floor = RiverLayout.Height(x, z);
                            if (Physics.Raycast(under.point - Vector3.up * 0.01f, Vector3.down, out var below, 10f, ShakuConst.SurfaceMask)) floor = Mathf.Max(floor, below.point.y);
                            worst = Mathf.Max(worst, under.point.y - floor);
                        }
                    }
                    Assert.Less(worst, 1.0f, $"{col.name} {b.center} の下に {worst:F2} のすき間（体長より高い洞にならない）");
                    n++;
                }
                Assert.Greater(n, 3, "滝のまわりの岩を調べた");
            }
            finally { gen.Clear(); }
        }
    }
}

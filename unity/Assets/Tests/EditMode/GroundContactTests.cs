using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// 建てた物の根もとは、地面から浮かない（前は坂に立つ千本鳥居の柱の根もと・石段のはし・玉垣のはしの柱・公園のさく・
    /// 山の石段の平たい石の、低い方の下にすき間があった）。物の形の下の面の点から、まっすぐ下の地面（ほかの物でもよい）までをはかる。
    /// </summary>
    public class GroundContactTests
    {
        static readonly Dictionary<string, string[]> Built = new Dictionary<string, string[]>
        {
            ["shrine"] = new[] { "Shrine_Torii", "Shrine_ToriiSmall", "Shrine_Fence", "Shrine_Step", "Shrine_Saisen", "Shrine_Lantern", "Shrine_Fox_A",
                                 "Shrine_Fox_B", "Shrine_Hall", "Shrine_Chozuya", "Shrine_Hokora", "Shrine_Ema", "Shrine_Omikuji" },
            ["park"] = new[] { "Park_Fence" },
            ["mountain"] = new[] { "Mtn_StairSlab_A", "Mtn_StairSlab_B", "Mtn_StairSlab_C", "Mtn_Slab_A", "Mtn_Slab_B" },
        };

        static Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 100f);

        /// <summary>
        /// 点から、まっすぐ下の地面（ほかの物でもよい）までの高さ。地面の下や、ほかの物の中にうまっている点（石の上につんだ石の下の面など）は 0。
        /// </summary>
        static float Gap(Vector3 p, Collider self)
        {
            // すぐ上に、ほかの物の面がある：地面の下や、ほかの物の中にうまっている
            foreach (var h in Physics.RaycastAll(p, Vector3.up, 1f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                if (h.collider != self) return 0f;
            var hits = Physics.RaycastAll(p + Vector3.up * 0.15f, Vector3.down, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == self) continue;
                if (h.normal.y < 0f) return 0f;   // うらの面に当たった：ほかの物の中にいる
                return Mathf.Max(0f, p.y - h.point.y);
            }
            return 0f;
        }

        [TestCase("shrine")]
        [TestCase("park")]
        [TestCase("mountain")]
        public void BuiltThings_StandOnTheGround(string id)
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            var gen = go.AddComponent<WorldGenerator>();
            gen.assets = TestUtil.LoadWorldAssets();
            gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Get(id);
            try
            {
                gen.GenerateNow();
                Physics.SyncTransforms();
                var names = new HashSet<string>(Built[id]);
                var items = new List<(string name, Mesh mesh, Matrix4x4 m)>();
                foreach (var (mesh, mat, m) in gen.instanced.Instances())
                    if (names.Contains(mesh.name)) items.Add((mesh.name, mesh, m));
                var solids = GameObject.Find("Solids");
                if (solids != null)
                    foreach (var mf in solids.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null && names.Contains(mf.sharedMesh.name)) items.Add((mf.sharedMesh.name, mf.sharedMesh, mf.transform.localToWorldMatrix));
                // 物それぞれの当たり判定（自分の下の面は、はかる相手にしない）
                var own = new Dictionary<(string, Vector3Int), Collider>();
                foreach (var root in new[] { solids, GameObject.Find("PropColliders") })
                    if (root != null)
                        foreach (var c in root.GetComponentsInChildren<MeshCollider>(true)) own[(c.name, Key(c.transform.position))] = c;
                var found = new HashSet<string>(items.Select(i => i.name));
                foreach (var n in names) Assert.IsTrue(found.Contains(n), $"{id} に {n} がある");
                var bad = new List<string>();
                var cache = new Dictionary<Mesh, Vector3[]>();
                bool back = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                try
                {
                    foreach (var (name, mesh, m) in items)
                    {
                        own.TryGetValue((name, Key(m.GetColumn(3))), out var self);
                        if (!cache.TryGetValue(mesh, out var vs)) cache[mesh] = vs = mesh.vertices;
                        float lmin = vs.Min(v => v.y), lmax = vs.Max(v => v.y);
                        float band = Mathf.Clamp((lmax - lmin) * 0.03f, 0.04f, 0.3f);   // いちばん下の面（高い建物の、手すりの下などは入れない）
                        float worst = 0f;
                        Vector3 at = Vector3.zero;
                        foreach (var v in vs)
                        {
                            if (v.y > lmin + band) continue;
                            Vector3 p = m.MultiplyPoint3x4(v);
                            float gap = Gap(p, self);
                            if (gap > worst) { worst = gap; at = p; }
                        }
                        if (worst > 0.12f) bad.Add($"{name}（{(Vector3)m.GetColumn(3)}）の {at} が {worst:0.00} 浮いている");
                    }
                }
                finally { Physics.queriesHitBackfaces = back; }
                CollectionAssert.IsEmpty(bad, $"{id}：根もとが地面から浮いている物");
            }
            finally
            {
                gen.Clear();
                Object.DestroyImmediate(go);
                Areas.Current = Areas.Forest;
            }
        }
    }
}

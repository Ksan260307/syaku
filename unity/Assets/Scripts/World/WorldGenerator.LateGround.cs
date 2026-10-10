using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しずくを置いたあとに足す地面の形（泉・水たまりの下手の土手）。
    /// しずくを置くまでは、もとの形で作り（しずくの場所・数と、乱数の使い方を変えない）、
    /// しずくを置いたら、そのまわりの地形を作りなおして、上に置いた草花・石・小物を、新しい地面まで持ち上げる。
    /// </summary>
    public partial class WorldGenerator
    {
        struct TerrainChunk
        {
            public Mesh mesh;
            public MeshCollider collider;
            public Vector2 min, max;
            public float spacing;
        }

        readonly List<TerrainChunk> _terrainChunks = new List<TerrainChunk>();

        /// <summary>
        /// 前は面がうら返しにできていた形（公園のタイヤ：外から見ると中が見え、しゃくとりむしが中へ入りこんでいた）。
        /// 形はなおしたが、しずくはこの形に当てて置いていたので、しずくを置くまでは、前と同じうら返しの当たり判定にしておき、
        /// しずくを置いたら、正しい向きの当たり判定にもどす（しずくの場所を変えない）。
        /// </summary>
        static readonly HashSet<string> WasInsideOut = new HashSet<string> { "Park_Tire_Red", "Park_Tire_Blue", "Park_Tire_Yellow" };
        readonly List<(MeshCollider col, Mesh mesh)> _insideOut = new List<(MeshCollider, Mesh)>();
        static readonly Dictionary<Mesh, Mesh> s_insideOut = new Dictionary<Mesh, Mesh>();

        /// <summary>面の向きだけを、うら返した形（頂点は同じ）。</summary>
        static Mesh InsideOut(Mesh src)
        {
            if (s_insideOut.TryGetValue(src, out var m) && m != null) return m;
            m = Instantiate(src);
            m.name = src.name + "_InsideOut";
            var t = m.triangles;
            for (int i = 0; i + 2 < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            m.triangles = t;
            s_insideOut[src] = m;
            return m;
        }

        /// <summary>しずくを置いたあと：うら返しにしておいた当たり判定を、正しい向きにもどす。</summary>
        void RestoreTrueColliders()
        {
            foreach (var (col, mesh) in _insideOut)
                if (col != null) col.sharedMesh = mesh;
            _insideOut.Clear();
        }

        /// <summary>土手を盛ったことで、地面が上がった高さ（もとの地面からの差）。</summary>
        float LateRise(float x, float z)
        {
            float now = Area.Height(x, z);
            AreaLayout.LateGround = false;
            float before = Area.Height(x, z);
            AreaLayout.LateGround = true;
            return now - before;
        }

        void ApplyLateGround()
        {
            RestoreTrueColliders();
            var zones = new List<(Vector2 center, float radius)>(Area.LateGroundZones);
            if (zones.Count == 0) return;
            foreach (var (c, r) in zones) RebuildTerrain(c, r);
            SurfaceProbe.ClearCache();

            bool InZone(Vector3 p)
            {
                foreach (var (c, r) in zones)
                    if ((new Vector2(p.x, p.z) - c).sqrMagnitude < r * r) return true;
                return false;
            }
            float Rise(Vector3 p) => InZone(p) ? LateRise(p.x, p.z) : 0f;

            // 当たり判定のある物（絵と同じだけ動かす）。しずくをのせた物と、大きな物（遊具など）は動かさない
            var keep = new HashSet<Vector3>();
            foreach (var root in new[] { _colliderRoot, _solidRoot })
                foreach (Transform t in root)
                {
                    float d = Rise(t.position);
                    if (d < 0.002f) continue;
                    var c = t.GetComponent<Collider>();
                    if (c != null && (HoldsDew(t.gameObject) || c.bounds.extents.magnitude > 4f))
                    {
                        keep.Add(t.position);
                        continue;
                    }
                    t.position += Vector3.up * d;
                }
            // インスタンスで描く物（草花・石の絵）。当たり判定といっしょの物は、当たり判定を動かした物だけ
            instanced.Edit((mesh, mat, m) =>
            {
                Vector3 p = m.GetColumn(3);
                if (keep.Contains(p)) return m;
                float d = Rise(p);
                if (d < 0.002f) return m;
                m.m13 += d;
                Count("lateGround", mesh.name, p);
                return m;
            });
            // 転がる小物・行列の道・花の頭
            if (loose != null) loose.Lift(Rise);
            foreach (var g in Mobs)
            {
                g.center.y += Rise(g.center);
                for (int i = 0; i < g.path.Count; i++) g.path[i] += Vector3.up * Rise(g.path[i]);
            }
            for (int i = 0; i < FlowerPoints.Count; i++) FlowerPoints[i] += Vector3.up * Rise(FlowerPoints[i]);
            for (int i = 0; i < _bigRocks.Count; i++) _bigRocks[i] = (_bigRocks[i].pos + Vector3.up * Rise(_bigRocks[i].pos), _bigRocks[i].scale);
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------
        // 坂で宙にういた岩を、地面になじませる（しずくを置いたあと。しずくをのせた岩は動かさない）
        // ------------------------------------------------------------------
        /// <summary>岩・川石の下のふちと地面のすき間で、いちばん大きいもの（ふちを 16 方向から見る）。</summary>
        float RockGap(Collider col)
        {
            var b = col.bounds;
            float worst = 0f;
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f;
                for (float f = 0.35f; f <= 0.96f; f += 0.15f)
                {
                    float x = b.center.x + Mathf.Cos(a) * b.extents.x * f, z = b.center.z + Mathf.Sin(a) * b.extents.z * f;
                    if (!col.Raycast(new Ray(new Vector3(x, b.min.y - 2f, z), Vector3.up), out var under, b.size.y + 4f)) continue;
                    worst = Mathf.Max(worst, under.point.y - Area.Height(x, z));
                }
            }
            return worst;
        }

        /// <summary>置いた物の向きと位置を変える（インスタンスの絵と当たり判定をいっしょに）。</summary>
        void RePlace(GameObject go, Vector3 pos, Quaternion rot)
        {
            Vector3 from = go.transform.position;
            Mesh m = assets.TryGet(go.name);
            float sc = go.transform.lossyScale.x;
            instanced.Edit((mesh, mat, mtx) =>
            {
                if (mesh != m || ((Vector3)mtx.GetColumn(3) - from).sqrMagnitude > 1e-4f) return mtx;
                return Matrix4x4.TRS(pos, rot, Vector3.one * sc);
            });
            go.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>しずくがこの物の中・下にうまっていないか（物の中からのレイも数える）。</summary>
        bool CoversDew(Collider col)
        {
            var b = col.bounds;
            b.Expand(0.5f);
            bool covered = false;
            Physics.queriesHitBackfaces = true;
            foreach (var d in DewdropPoints)
            {
                if (!b.Contains(d)) continue;
                if (col.Raycast(new Ray(d + Vector3.up * 0.02f, Vector3.up), out _, b.size.y + 1f)) { covered = true; break; }
            }
            Physics.queriesHitBackfaces = false;
            return covered;
        }

        /// <summary>
        /// 坂に置いた岩は、下手のふちが地面からういて、下に大きなすき間ができる（しゃくとりむしが岩の下へもぐりこむ）。
        /// 坂にそって岩をかたむけ、それでものこるすき間の分だけ、地面へしずめる（しずめすぎて岩が小さく見えないよう、高さの半分まで）。
        /// </summary>
        void SettleFloatingRocks()
        {
            Physics.SyncTransforms();
            var rocks = new List<GameObject>();
            foreach (Transform t in _colliderRoot)
                if (t.name.StartsWith("Rock_") || t.name.StartsWith("RiverStone_") || t.name == "DriftLog") rocks.Add(t.gameObject);
            foreach (var go in rocks)
            {
                var col = go.GetComponent<Collider>();
                if (col == null) continue;
                var b = col.bounds;
                if (new Vector2(b.center.x, b.center.z).magnitude > Area.PlayRadius + 2f) continue;   // 外周の大岩
                if (HoldsDew(go)) continue;
                // 水の中に立つ石（川のとびいし・水ぎわの石）は、水面からの高さを変えない
                if (Area.WaterLevelAt(b.center.x, b.center.z) > Area.Height(b.center.x, b.center.z)) continue;
                // 石の上に積んだ石・川のとびいし（下に足場の石がある）は、そのまま
                bool stacked = false;
                foreach (var h in Physics.RaycastAll(b.center, Vector3.down, b.extents.y + 1.5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    if (h.collider != col && !h.collider.name.StartsWith("Terrain_")) stacked = true;
                if (stacked) continue;
                // 上に物がのっている岩（積んだ石の下の石など）も、そのまま
                if (Physics.Raycast(new Vector3(b.center.x, b.max.y + 4f, b.center.z), Vector3.down, out var top, 6f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                    && top.collider != col) continue;
                float gap = RockGap(col);
                if (gap < 0.1f) continue;
                Vector3 pos0 = go.transform.position;
                Quaternion rot0 = go.transform.rotation;
                // 坂にそって、かたむける（岩の下のまん中を軸に）
                Vector3 up = rot0 * Vector3.up;
                Vector3 n = Area.Normal(b.center.x, b.center.z);
                Quaternion tilt = Quaternion.FromToRotation(up, Vector3.Slerp(up, n, 0.85f));
                Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z);
                Vector3 pos = pivot + tilt * (pos0 - pivot);
                Quaternion rot = tilt * rot0;
                RePlace(go, pos, rot);
                Physics.SyncTransforms();
                float gap2 = RockGap(col);
                float sink = Mathf.Min(gap2 + 0.04f, b.size.y * 0.5f);
                if (sink > 0f)
                {
                    RePlace(go, pos + Vector3.down * sink, rot);
                    Physics.SyncTransforms();
                }
                if (CoversDew(col) || RockGap(col) > gap)
                {
                    RePlace(go, pos0, rot0);   // しずくにかぶさる・かえって悪くなるなら、もとのまま
                    Physics.SyncTransforms();
                    continue;
                }
                if (loose != null) loose.PushOut(col.bounds.center, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z), col.bounds.extents.y);
                Count("settleRock", go.name, b.center);
                Mark("settleRock", b.center);
            }
            Physics.SyncTransforms();
        }

        /// <summary>
        /// しずくをのせた岩（動かせない）と、かたむけてしずめても下にすき間がのこる岩は、坂の下手のすき間を、下から石でうめる（滝のわきの大岩など）。
        /// うめた石は、岩の下のふちまでとどき、地面にうまる大きさにする（乱数は使わない）。
        /// </summary>
        void FillUnderDewRocks()
        {
            Physics.SyncTransforms();
            var rocks = new List<GameObject>();
            foreach (Transform t in _colliderRoot)
                if (t.name.StartsWith("Rock_") || t.name.StartsWith("RiverStone_")) rocks.Add(t.gameObject);
            Mesh filler = assets.Get("Rock_D");
            if (filler == null) return;
            foreach (var go in rocks)
            {
                var col = go.GetComponent<Collider>();
                if (col == null) continue;
                var b = col.bounds;
                if (new Vector2(b.center.x, b.center.z).magnitude > Area.PlayRadius + 2f || Mathf.Max(b.size.x, b.size.z) < 1.6f) continue;
                // 岩は動かさないので、上に物がのっていても、下にすき間があれば石でうめる
                if (!HoldsDew(go) && RockGap(col) < 0.45f) continue;
                // 岩のふちを見て、すき間の大きい所に、石を 1 つずつ（となりの石と 1.5 以上はなして）。うめたあと、もう一度見る
                var placed = new List<Vector2>();
                for (int pass = 0; pass < 3; pass++)
                for (int k = 0; k < 32; k++)
                {
                    float a = k * Mathf.PI / 16f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float gap = 0f, under = 0f;
                    Vector2 at = Vector2.zero;
                    for (float f = 0.35f; f <= 0.96f; f += 0.1f)
                    {
                        float x = b.center.x + dir.x * b.extents.x * f, z = b.center.z + dir.y * b.extents.z * f;
                        if (!col.Raycast(new Ray(new Vector3(x, b.min.y - 2f, z), Vector3.up), out var hit, b.size.y + 4f)) continue;
                        // 水の中のすき間（川のとびいしの下など）は、しゃくとりむしが入れないので、うめない
                        if (Area.IsUnderwater(new Vector3(x, Area.Height(x, z) + 0.05f, z))) continue;
                        // もう石でうめた所は、その石の上から見る
                        float floor = Area.Height(x, z);
                        if (Physics.Raycast(hit.point - Vector3.up * 0.01f, Vector3.down, out var below, 10f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                            floor = Mathf.Max(floor, below.point.y);
                        float g = hit.point.y - floor;
                        if (g > gap) { gap = g; under = hit.point.y; at = new Vector2(x, z); }
                    }
                    if (gap < 0.3f || placed.Count >= 8 || placed.Exists(q => (q - at).sqrMagnitude < 1.5f * 1.5f)) continue;
                    // 坂でも石の下にすき間ができないよう、石の下の地面のいちばん低い所から、岩の下の面まで
                    float s = Mathf.Clamp((gap + 0.8f) / filler.bounds.size.y, 0.6f, 3f), low = Area.Height(at.x, at.y);
                    for (int it = 0; it < 3; it++)
                    {
                        float rr = Mathf.Max(filler.bounds.extents.x, filler.bounds.extents.z) * s * 0.9f;
                        low = Area.Height(at.x, at.y);
                        for (int q = 0; q < 8; q++)
                        {
                            float qa = q * Mathf.PI / 4f;
                            low = Mathf.Min(low, Area.Height(at.x + Mathf.Cos(qa) * rr, at.y + Mathf.Sin(qa) * rr));
                        }
                        s = Mathf.Clamp((under + 0.3f - (low - 0.35f)) / filler.bounds.size.y, 0.6f, 3f);   // 大きすぎる石にしない
                    }
                    float yaw = Mathf.Repeat(at.x * 37.1f + at.y * 11.3f, 360f);
                    // 石のてっぺんが、岩の下の面に少しかくれるくらい。坂にそってかたむける
                    Vector3 pos = new Vector3(at.x, under + 0.3f - filler.bounds.max.y * s, at.y);
                    Vector3 n = Area.Normal(at.x, at.y);
                    Quaternion rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.7f)) * Quaternion.Euler(0f, yaw, 0f);
                    var fill = Place("Rock_D", assets.prop, pos, rot, s, true, true, 250f);
                    if (fill == null) continue;
                    Physics.SyncTransforms();
                    var fc = fill.GetComponent<Collider>();
                    // うめた石の下にも、すき間がのこるなら、そのぶん下げる（てっぺんは岩の中なので、少し下げても岩の下にとどく）
                    if (fc != null)
                    {
                        float fg = RockGap(fc);
                        if (fg > 0.1f)
                        {
                            RePlace(fill, pos + Vector3.down * Mathf.Min(fg + 0.05f, fc.bounds.size.y * 0.45f), rot);
                            Physics.SyncTransforms();
                        }
                    }
                    if (fc != null && CoversDew(fc)) { Unplace(fill); Physics.SyncTransforms(); continue; }
                    placed.Add(at);
                    Count("fillUnderRock", go.name, pos);
                    Mark("fillUnderRock", pos);
                }
            }
            Physics.SyncTransforms();
        }

        /// <summary>center のまわりの地形を、いまの地面の形（Area.Height）で作りなおす。</summary>
        void RebuildTerrain(Vector2 center, float radius)
        {
            foreach (var ch in _terrainChunks)
            {
                Vector2 near = new Vector2(Mathf.Clamp(center.x, ch.min.x, ch.max.x), Mathf.Clamp(center.y, ch.min.y, ch.max.y));
                if ((near - center).sqrMagnitude > (radius + 2f) * (radius + 2f)) continue;
                var pos = ch.mesh.vertices;
                var nrm = ch.mesh.normals;
                var col = ch.mesh.colors;
                float e = ch.spacing;
                bool changed = false;
                for (int k = 0; k < pos.Length; k++)
                {
                    float x = pos[k].x, z = pos[k].z;
                    if ((new Vector2(x, z) - center).sqrMagnitude > (radius + 2f * e) * (radius + 2f * e)) continue;
                    float h = Area.Height(x, z);
                    float hl = Area.Height(x - e, z), hr = Area.Height(x + e, z);
                    float hd = Area.Height(x, z - e), hu = Area.Height(x, z + e);
                    Vector3 n = new Vector3(hl - hr, 2f * e, hd - hu).normalized;
                    if (Mathf.Abs(h - pos[k].y) < 1e-5f && (n - nrm[k]).sqrMagnitude < 1e-8f) continue;
                    pos[k].y = h;
                    nrm[k] = n;
                    col[k] = Area.GroundColor(x, z, h, n);
                    changed = true;
                }
                if (!changed) continue;
                ch.mesh.vertices = pos;
                ch.mesh.normals = nrm;
                ch.mesh.colors = col;
                ch.mesh.RecalculateBounds();
                if (ch.collider != null)
                {
                    ch.collider.sharedMesh = null;
                    ch.collider.sharedMesh = ch.mesh;
                }
            }
        }
    }
}

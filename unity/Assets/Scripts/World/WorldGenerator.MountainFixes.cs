using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 山の遊びやすさの直し（MOUNTAIN_FIXES.md）。しずくを置いたあと、いきものを置く前に行う（しずくの場所・数・順番は変えない）。
    /// 小道をふさぐ岩をどける・宙にういた岩を地面へ・山小屋の屋根へのふみ板・岩のトンネルの上への倒れた木・
    /// 泉の石へのとびいし・岩の階段の中段・登りやすいケルン・行き先を指す道しるべ・しずくと景色の前の草花。
    /// </summary>
    public partial class WorldGenerator
    {
        /// <summary>直した所（種類ごと。テストと MOUNTAIN_FIXES.md の点検用）。</summary>
        public readonly Dictionary<string, List<Vector3>> MountainFixes = new Dictionary<string, List<Vector3>>();

        /// <summary>立てかけた物・石段の下に、あとで小物（石積みなど）を置かないように、使ったことにする。</summary>
        void OccupyLine(Vector3 a, Vector3 b, float r)
        {
            for (int i = 0; i <= 8; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 8f);
                Occupy(new Vector2(p.x, p.z), r);
            }
        }

        void Fixed(string kind, Vector3 at)
        {
            if (!MountainFixes.TryGetValue(kind, out var l)) MountainFixes[kind] = l = new List<Vector3>();
            l.Add(at);
            Mark(kind, at);
        }

        void FixMountainForPlay()
        {
            MountainFixes.Clear();
            Physics.SyncTransforms();
            PineRootsToGround();
            MoveStairsDen();
            ClearWalkingTrails();
            GroundFloatingRocks();
            MoveSnowMoundsOffTrail();
            HutFoundation();
            HutRoofPlank();
            ArchLog();
            PineLog();
            SpringSteppingStones();
            SideStairs();
            SteppedCairn();
            ReplaceSigns();
            ClearPlantsAroundDews();
            ClearTallPlantsAlongTrails();
            ClearTallFlowersInViews();
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------
        // 置いた物を、あとからどける・動かす
        // ------------------------------------------------------------------
        IEnumerable<GameObject> PlacedObjects()
        {
            foreach (Transform t in _colliderRoot) yield return t.gameObject;
            foreach (Transform t in _solidRoot) yield return t.gameObject;
        }

        static void Kill(GameObject go)
        {
            go.SetActive(false);   // 当たり判定は、すぐに消す
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        /// <summary>置いた物を取りのぞく（インスタンス描画の絵と、当たり判定）。</summary>
        void Unplace(GameObject go)
        {
            if (go.GetComponent<MeshRenderer>() == null)
            {
                Mesh m = assets.TryGet(go.name);
                Vector3 p = go.transform.position;
                instanced.Edit((mesh, mat, mtx) => mesh == m && ((Vector3)mtx.GetColumn(3) - p).sqrMagnitude < 1e-4f ? (Matrix4x4?)null : mtx);
            }
            Kill(go);
        }

        /// <summary>置いた物を動かす（絵と当たり判定をいっしょに）。</summary>
        void MovePlaced(GameObject go, Vector3 to)
        {
            Vector3 from = go.transform.position;
            if (go.GetComponent<MeshRenderer>() == null)
            {
                Mesh m = assets.TryGet(go.name);
                instanced.Edit((mesh, mat, mtx) =>
                {
                    if (mesh != m || ((Vector3)mtx.GetColumn(3) - from).sqrMagnitude > 1e-4f) return mtx;
                    var r = mtx;
                    r.SetColumn(3, new Vector4(to.x, to.y, to.z, 1f));
                    return r;
                });
            }
            go.transform.position = to;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) return c.bounds;
            var r = go.GetComponent<Renderer>();
            return r != null ? r.bounds : new Bounds(go.transform.position, Vector3.one);
        }

        /// <summary>しずくがのっている物か（しずくの真下にある・しずくがふれている）。しずくの下の物は、動かさない。</summary>
        bool HoldsDew(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) return false;
            var b = c.bounds;
            b.Expand(1f);
            foreach (var d in DewdropPoints)
            {
                if (!b.Contains(d)) continue;
                // しずくの真下の、いちばん上の面がこの物（しずくを支えている）
                if (Physics.Raycast(d + Vector3.up * 0.4f, Vector3.down, out var h, 1.2f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) && h.collider == c) return true;
            }
            return false;
        }

        bool DewNear(Vector2 p, float r)
        {
            foreach (var d in DewdropPoints)
                if ((new Vector2(d.x, d.z) - p).sqrMagnitude < r * r) return true;
            return false;
        }

        /// <summary>そこに、ほかの動かない物（地面のほか）があるか。</summary>
        static bool Crowded(Vector3 p, float r)
        {
            foreach (var c in Physics.OverlapSphere(p, r, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                if (!c.name.StartsWith("Terrain_") && c.attachedRigidbody == null) return true;
            return false;
        }

        static bool IsRock(string n) => n.StartsWith("Rock_") || n.StartsWith("RiverStone_");
        static bool IsLitter(string n) => IsRock(n) || n.StartsWith("Twig_");

        // ------------------------------------------------------------------
        // 1. オコジョのすみか（岩の階段のそば）を、付けかえた小道からはなす
        // ------------------------------------------------------------------
        void MoveStairsDen()
        {
            if (_okojoDens.Count == 0) return;
            Vector2 d = _okojoDens[0];
            if (MountainLayout.DistToTrail(d) > 6f) return;
            var den = PlacedObjects().FirstOrDefault(g => g.name == "Okojo_Rocks" && Vector2.Distance(new Vector2(g.transform.position.x, g.transform.position.z), d) < 0.5f);
            if (den == null || HoldsDew(den)) return;
            Vector2 to = d + new Vector2(-4f, -2.5f);
            MovePlaced(den, MG(to) + Vector3.down * 0.15f);
            _okojoDens[0] = to;
            Fixed("fix_den", MG(to));
        }

        // ------------------------------------------------------------------
        // 2. 歩く小道の上の岩・重い小石（動かない石）をどける
        // ------------------------------------------------------------------
        void ClearWalkingTrails()
        {
            Vector2 cairn = MountainLayout.Summit + new Vector2(-4.5f, 2.5f);
            foreach (var go in PlacedObjects().ToList())
            {
                if (!IsLitter(go.name)) continue;
                var b = BoundsOf(go);
                Vector2 c = new Vector2(b.center.x, b.center.z);
                float r = Mathf.Max(b.extents.x, b.extents.z);
                if (go.name.StartsWith("Twig_"))
                {
                    // 小枝は細長い：枝にそって、道にかかるかを見る
                    float half = (assets.TryGet(go.name)?.bounds.extents.x ?? 6.3f) * go.transform.lossyScale.x;
                    bool cross = false;
                    for (float u = -1f; u <= 1.01f; u += 0.25f)
                    {
                        Vector3 q = go.transform.position + go.transform.right * (half * u);
                        if (MountainLayout.DistToTrail(new Vector2(q.x, q.z)) < 1.3f) cross = true;
                    }
                    if (!cross) continue;
                }
                else if (MountainLayout.DistToTrail(c) > r + 0.9f) continue;
                if (Vector2.Distance(c, cairn) < 3f || MountainLayout.InSpring(c.x, c.y, 1f)) continue;
                if ((b.size.magnitude > 9f && !go.name.StartsWith("Twig_")) || HoldsDew(go)) continue;
                Unplace(go);
                Fixed("fix_trailrock", b.center);
            }
            // 押せる小石や松ぼっくりは、小道のまん中から、わきへ
            if (loose != null)
                foreach (var t in MountainLayout.Trails)
                    for (int i = 0; i < t.Length - 1; i++)
                    {
                        float len = Vector2.Distance(t[i], t[i + 1]);
                        for (float s = 0f; s < len; s += 1.2f)
                        {
                            Vector2 p = Vector2.Lerp(t[i], t[i + 1], s / len);
                            int k = loose.PushOut(MG(p), 0.6f, 1.5f);
                            if (k > 0) Fixed("fix_trailpebble", MG(p));
                        }
                    }
        }

        // ------------------------------------------------------------------
        // 3. 宙にういた岩を、地面へ下ろす（積んだ石・しずくの下の石は、そのまま）
        // ------------------------------------------------------------------
        void GroundFloatingRocks()
        {
            foreach (var go in PlacedObjects().ToList())
            {
                if (!IsRock(go.name)) continue;
                var col = go.GetComponent<Collider>();
                if (col == null) continue;
                var b = col.bounds;
                if (b.size.magnitude > 14f || HoldsDew(go) || MountainLayout.InSpring(b.center.x, b.center.z, 1f)) continue;
                // 下に、ほかの物があれば積んだ石
                bool stacked = false;
                foreach (var h in Physics.RaycastAll(new Vector3(b.center.x, b.center.y, b.center.z), Vector3.down, b.extents.y + 0.6f, ShakuConst.SurfaceMask))
                    if (h.collider != col && !h.collider.name.StartsWith("Terrain_")) stacked = true;
                if (stacked) continue;
                float minGap = 99f, maxGap = -99f;
                foreach (var o in new[] { Vector2.zero, new Vector2(0.25f, 0.25f), new Vector2(-0.25f, 0.25f), new Vector2(0.25f, -0.25f), new Vector2(-0.25f, -0.25f) })
                {
                    float x = b.center.x + o.x * b.size.x, z = b.center.z + o.y * b.size.z;
                    if (!col.Raycast(new Ray(new Vector3(x, b.min.y - 1f, z), Vector3.up), out var hh, b.size.y + 2f)) continue;
                    float g = hh.point.y - MountainLayout.Height(x, z);
                    minGap = Mathf.Min(minGap, g);
                    maxGap = Mathf.Max(maxGap, g);
                }
                if (minGap > 50f) continue;
                float down = 0f;
                if (minGap >= 0.12f) down = minGap + 0.06f;                                       // まるごと、ういている
                else if (maxGap > 0.35f) down = Mathf.Min((maxGap - 0.1f) * 0.7f, b.size.y * 0.4f);   // 坂で、かたがわがういている
                if (down <= 0f) continue;
                MovePlaced(go, go.transform.position + Vector3.down * down);
                Fixed("fix_floatrock", b.center);
            }
        }

        // ------------------------------------------------------------------
        // 4. 雪渓の雪のかたまりが、小道にはみ出さない
        // ------------------------------------------------------------------
        void MoveSnowMoundsOffTrail()
        {
            foreach (var go in PlacedObjects().ToList())
            {
                if (!go.name.StartsWith("Mtn_SnowMound")) continue;
                var b = BoundsOf(go);
                Vector2 c = new Vector2(b.center.x, b.center.z);
                float r = Mathf.Max(b.extents.x, b.extents.z);
                float d = MountainLayout.DistToTrail(c);
                if (d > r + 1.2f || HoldsDew(go)) continue;
                // 小道からはなれる向きへ
                Vector2 away = Vector2.zero;
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    if (MountainLayout.DistToTrail(c + dir) > d + 0.5f) { away = dir; break; }
                }
                if (away == Vector2.zero) continue;
                Vector2 to = c + away * (r + 1.3f - d);
                if (DewNear(to, r + 0.5f)) { Unplace(go); Fixed("fix_snowmound", b.center); continue; }
                MovePlaced(go, new Vector3(to.x, MountainLayout.Height(to.x, to.y) - 0.25f, to.y));
                Fixed("fix_snowmound", b.center);
            }
        }

        // ------------------------------------------------------------------
        // 5. 山小屋：坂の下がわで宙にういた土台に石をつむ・屋根へのぼる、ふみ板
        // ------------------------------------------------------------------
        Transform HutTransform() => PlacedObjects().FirstOrDefault(g => g.name == "Mtn_Hut")?.transform;

        void HutFoundation()
        {
            var hut = HutTransform();
            if (hut == null) return;
            float bottom = hut.position.y - 0.3f;
            Mesh slab = assets.TryGet("Mtn_Slab_A");
            if (slab == null) return;
            float top1 = slab.bounds.max.y;
            for (int side = 0; side < 4; side++)
                for (float u = -1f; u <= 1.001f; u += 0.25f)
                {
                    Vector3 local = side < 2 ? new Vector3(u * 8f, 0f, side == 0 ? -6.2f : 6.2f) : new Vector3(side == 2 ? -8.8f : 8.8f, 0f, u * 5.4f);
                    Vector3 w = hut.TransformPoint(local);
                    float gap = bottom + 0.4f - MountainLayout.Height(w.x, w.z);
                    if (gap < 0.45f) continue;
                    if (DewNear(new Vector2(w.x, w.z), 1.5f)) continue;
                    float s = Mathf.Clamp((gap + 0.4f) / (top1 + 0.35f), 0.35f, 0.9f);
                    Place("Mtn_Slab_A", assets.prop, new Vector3(w.x, bottom + 0.45f - top1 * s, w.z), hut.rotation * Quaternion.Euler(0f, 90f * (side % 2) + 10f * u, 0f), s, true, true, 220f);
                    Fixed("fix_hutbase", w);
                }
        }

        /// <summary>屋根の上の面の点（小屋のローカル座標の x, z の真上から）。</summary>
        bool RoofPoint(Transform hut, float lx, float lz, out Vector3 p)
        {
            Vector3 w = hut.TransformPoint(new Vector3(lx, 30f, lz));
            p = default;
            foreach (var h in Physics.RaycastAll(w, Vector3.down, 40f, ShakuConst.SurfaceMask).OrderBy(h => h.distance))
            {
                if (h.collider.name != "Mtn_Hut") continue;
                p = h.point;
                return true;
            }
            return false;
        }

        /// <summary>下のはしを地面にのせて、上のはし top へ立てかける（長さ len）。地面の点を返す。</summary>
        Vector3 Lean(Vector3 top, Vector3 outward, float len)
        {
            outward.y = 0f;
            outward.Normalize();
            float lo = 0f, hi = len;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Vector3 b = top + outward * mid;
                float y = MountainLayout.Height(b.x, b.z);
                float l = Mathf.Sqrt(mid * mid + (top.y - y) * (top.y - y));
                if (l < len) lo = mid; else hi = mid;
            }
            Vector3 g = top + outward * lo;
            g.y = MountainLayout.Height(g.x, g.z);
            return g;
        }

        void HutRoofPlank()
        {
            var hut = HutTransform();
            Mesh plank = assets.TryGet("Mtn_Plank");
            if (hut == null || plank == null) return;
            // 小道のない、山がわ（入り口のうら）の屋根に立てかける。坂を登るので、ふみ板は短い。
            // えんとつ（x = -4.5）とは反対のはしにかけて、屋根の上でえんとつにぶつからないように
            if (!RoofPoint(hut, 4.2f, -5.2f, out var top)) return;
            Vector3 outward = hut.TransformDirection(Vector3.back);
            top += Vector3.up * 0.1f - outward * 0.25f;
            // 屋根から外へ、35 度で下りる線が、はじめて地面にとどく所に、下のはしをおく（とちゅうで地面にうまらない）
            float tan = Mathf.Tan(35f * Mathf.Deg2Rad), dist = 0.5f;
            while (dist < 16f)
            {
                Vector3 q = top + outward * dist;
                if (MountainLayout.Height(q.x, q.z) >= top.y - dist * tan) break;
                dist += 0.1f;
            }
            Vector3 bottom = top + outward * dist;
            bottom.y = MountainLayout.Height(bottom.x, bottom.z);
            float len = Vector3.Distance(top, bottom) + 1.6f;   // 下のはしは、地面に少しうめる（板の下にもぐりこまない）
            // 短いときは、はばの広い短い板（大きさをかえても、はばがせまくならないように）
            Mesh shortPlank = assets.TryGet("Mtn_PlankShort");
            string name = len < 8.5f && shortPlank != null ? "Mtn_PlankShort" : "Mtn_Plank";
            float s = Mathf.Clamp(len / PlankLength(name == "Mtn_PlankShort" ? shortPlank : plank), 0.8f, 1.25f);
            Vector3 dir = (top - bottom).normalized;
            // メッシュは、下のはしが原点で +Z へのびる
            var go = Place(name, assets.bark, bottom + Vector3.down * 0.05f - dir * 1.2f, Quaternion.LookRotation(dir, Vector3.up), s, true, true, 300f);
            Physics.SyncTransforms();
            if (go != null) ClearUnder(go.GetComponent<Collider>());
            OccupyLine(bottom, top, 1.6f);
            Fixed("fix_hutplank", (top + bottom) * 0.5f);
        }

        static float PlankLength(Mesh m) => m.bounds.size.z;

        /// <summary>立てかけた物の下にある、石や小枝をどける（物にそって登る道をふさがない）。</summary>
        void ClearUnder(Collider c)
        {
            if (c == null) return;
            foreach (var go in PlacedObjects().ToList())
            {
                if (!IsLitter(go.name) || HoldsDew(go)) continue;
                var b = BoundsOf(go);
                if (!b.Intersects(c.bounds)) continue;
                // 物の真上（すぐ上）を、立てかけた物が通っている
                if (c.Raycast(new Ray(new Vector3(b.center.x, b.min.y - 0.2f, b.center.z), Vector3.up), out _, b.size.y + 1.5f))
                {
                    Unplace(go);
                    Fixed("fix_rampclear", b.center);
                }
            }
        }

        // ------------------------------------------------------------------
        // 6. 岩のトンネルの上・大きな松の葉のかたまりへ、倒れた木を立てかける
        // ------------------------------------------------------------------
        bool TopOf(string objName, Vector2 xz, out Vector3 p)
        {
            p = default;
            foreach (var h in Physics.RaycastAll(new Vector3(xz.x, 120f, xz.y), Vector3.down, 140f, ShakuConst.SurfaceMask).OrderBy(h => h.distance))
            {
                if (!h.collider.name.StartsWith(objName)) continue;
                p = h.point;
                return true;
            }
            return false;
        }

        void ArchLog()
        {
            Vector2 a = MountainLayout.RockArch;
            // まぐさ石（南北にのびる）の、北のはし・南のはしの上へ。下のはしが、オコジョのすみか・小道・しずくのそばにならない方
            foreach (float side in new[] { 1f, -1f })
                for (float dz = 5.5f; dz >= 2.5f; dz -= 0.5f)
                {
                    if (!TopOf("Mtn_RockArch", a + new Vector2(0.8f, dz * side), out var top)) continue;
                    if (top.y - MountainLayout.Height(a.x, a.y) < 6f) continue;
                    Vector3 bottom = Lean(top + Vector3.down * 0.2f, new Vector3(0.35f, 0f, side), 13.5f);
                    Vector2 bxz = new Vector2(bottom.x, bottom.z);
                    bool bad = _okojoDens.Any(d => ShakuMath.DistToSegment(d, bxz, new Vector2(top.x, top.z)) < 5.5f)
                               || MountainLayout.DistToTrail(bxz) < 1.8f || DewNear(bxz, 1.5f) || Crowded(bottom + Vector3.up * 1f, 0.8f);
                    if (bad) break;   // このはしはだめ：反対のはしをためす
                    var go = Place("Mtn_Log", assets.bark, bottom + Vector3.down * 0.25f, Quaternion.LookRotation((top - bottom).normalized, Vector3.up), 1f, true, true, 300f);
                    Physics.SyncTransforms();
                    if (go != null) ClearUnder(go.GetComponent<Collider>());
                    OccupyLine(bottom, top, 1.4f);
                    Fixed("fix_archlog", (top + bottom) * 0.5f);
                    return;
                }
        }

        void PineLog()
        {
            // 小道の終わり（松の南東）の方からも、いちばん低い葉のかたまりへ登れるように
            Vector2 pn = MountainLayout.Pine;
            float gy = MountainLayout.Height(pn.x, pn.y);
            Vector3 best = Vector3.zero;
            float bestScore = 99f;
            for (int k = 0; k < 48; k++)
            {
                float a = k * 2.39996f;
                float r = 3f + (k % 8) * 1.1f;
                Vector2 p = pn + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (!TopOf("Mtn_Pine", p, out var top)) continue;
                float h = top.y - MountainLayout.Height(p.x, p.y);
                if (h < 6.2f || h > 9.5f) continue;
                // 南東（小道の来る方）に近いほどよい
                Vector2 toward = (p - pn).normalized;
                float score = Vector2.Distance(toward, new Vector2(0.6f, -0.8f)) + h * 0.05f;
                if (score < bestScore) { bestScore = score; best = top; }
            }
            if (best == Vector3.zero) return;
            Vector3 outward = new Vector3(best.x - pn.x, 0f, best.z - pn.y);
            Vector3 bottom = Lean(best + Vector3.down * 0.15f, outward, 12f);
            if (MountainLayout.DistToTrail(new Vector2(bottom.x, bottom.z)) < 1.5f || DewNear(new Vector2(bottom.x, bottom.z), 1.2f)) return;
            Place("Mtn_Log", assets.bark, bottom + Vector3.down * 0.25f, Quaternion.LookRotation((best - bottom).normalized, Vector3.up), 0.85f, true, true, 300f);
            OccupyLine(bottom, best, 1.3f);
            Fixed("fix_pinelog", (best + bottom) * 0.5f);
        }

        // ------------------------------------------------------------------
        // 7. 泉：岸から、しずくののった石へ、とびいしをつなぐ
        // ------------------------------------------------------------------
        void SpringSteppingStones()
        {
            Vector2 c = MountainLayout.Spring;
            float wl = MountainLayout.SpringLevel;
            Vector2[] chain = { c + new Vector2(9.6f, 0.8f), c + new Vector2(6f, 1f), c + new Vector2(4.5f, -4f), c + new Vector2(-3f, -5f), c + new Vector2(-5.5f, -2f) };
            Mesh m = assets.TryGet("RiverStone_A");
            if (m == null) return;
            const float s = 0.55f;
            float top = m.bounds.max.y * s;
            var placed = new List<Vector2>(chain.Skip(1));   // しずくののった石（岸のはしは、石ではない）
            for (int i = 0; i < chain.Length - 1; i++)
            {
                Vector2 a = chain[i], b = chain[i + 1];
                float len = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.15f));
                for (int k = 1; k < n; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                    if (!MountainLayout.IsUnderwater(new Vector3(p.x, MountainLayout.Height(p.x, p.y), p.y))) continue;   // 水の外は、歩いて行ける
                    if (placed.Any(q => Vector2.Distance(q, p) < 0.7f)) continue;
                    Vector3 pos = new Vector3(p.x, wl + 0.22f - top, p.y);
                    float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg + 90f;
                    Place("RiverStone_A", assets.prop, pos, Quaternion.Euler(0f, yaw, 0f), s, true, true, 160f);
                    StoneFooting(pos, "RiverStone_A", s);
                    placed.Add(p);
                    Fixed("fix_springstone", pos + Vector3.up * top);
                }
            }
        }

        // ------------------------------------------------------------------
        // 8. 岩の階段：高すぎる段のあいだに中段・下の段の前に足がかり
        // ------------------------------------------------------------------
        /// <summary>
        /// 岩の階段の、小道がわ（西）のわきに、横がまっすぐな石段（ひさしのない柱の石）を、0.6 ずつ高くならべる。
        /// 大きな平たい岩は、ふくらんだ横がひさしになって登りにくいので、この石段で、いちばん上の岩まで登れるようにする。
        /// </summary>
        void SideStairs()
        {
            Mesh m = assets.TryGet("Mtn_StepPillar");
            if (m == null || _stairTops.Count < 2) return;
            Vector3 s0 = _stairTops[0], s5 = _stairTops[_stairTops.Count - 1];
            Vector2 u = new Vector2(s5.x - s0.x, s5.z - s0.z).normalized;
            Vector2 w = new Vector2(-u.y, u.x);   // 西（小道がわ）
            Vector2 a = new Vector2(s0.x, s0.z) + w * 3.6f - u * 2.6f;
            Vector2 b = new Vector2(s5.x, s5.z) + w * 2.2f;
            float y0 = MountainLayout.Height(a.x, a.y), y1 = s5.y;
            int n = Mathf.CeilToInt((y1 - y0) / 0.5f) + 1;
            float yaw = Mathf.Atan2(u.x, u.y) * Mathf.Rad2Deg;
            for (int i = 1; i < n; i++)
            {
                float k = i / (float)(n - 1);
                Vector2 p = Vector2.Lerp(a, b, k);
                float top = Mathf.Lerp(y0, y1, k);
                if (top < MountainLayout.Height(p.x, p.y) + 0.2f) continue;   // 地面が石段の高さ（地面を歩ける）
                if (DewNear(p, 0.9f)) continue;
                Place("Mtn_StepPillar", assets.prop, new Vector3(p.x, top, p.y), Quaternion.Euler(0f, yaw + (i % 2) * 8f, 0f), 1.35f, true, true, 200f);
                Occupy(p, 1.2f);
                Fixed("fix_sidestairs", new Vector3(p.x, top, p.y));
            }
        }

        // ------------------------------------------------------------------
        // 9. 山頂のケルン：段々に積みなおして、てっぺんまで登れるように
        // ------------------------------------------------------------------
        void SteppedCairn()
        {
            Vector2 c = MountainLayout.Summit + new Vector2(-4.5f, 2.5f);
            Vector2 to = MountainLayout.Summit + new Vector2(-6f, 4.5f);   // 三角点から、少しはなす（石が三角点の根もとにかからない）
            var old = PlacedObjects().Where(g => IsRock(g.name) && Vector2.Distance(new Vector2(g.transform.position.x, g.transform.position.z), c) < 1.2f).ToList();
            if (old.Count == 0 || old.Any(g => HoldsDew(g))) return;
            foreach (var g in old) Unplace(g);
            string[] layers = { "Mtn_Slab_C", "Mtn_Slab_A", "Mtn_Slab_B", "Mtn_Slab_B" };
            float[] scales = { 0.62f, 0.48f, 0.38f, 0.26f };
            float y = MountainLayout.Height(to.x, to.y) - 0.2f;   // いまの段の上の面
            for (int k = 0; k < layers.Length; k++)
            {
                Mesh m = assets.TryGet(layers[k]);
                if (m == null) continue;
                Vector2 p = to + new Vector2(0.35f * k, 0.25f * k);   // 少しずつずらして、段にする
                float s = scales[k];
                float py = y - m.bounds.min.y * s - 0.15f * s;     // 下の段に、少しめりこませてのせる
                Place(layers[k], assets.prop, new Vector3(p.x, py, p.y), Quaternion.Euler(0f, 37f * k, 0f), s, true, true, 220f);
                y = py + m.bounds.max.y * s;
            }
            Fixed("fix_cairn", MG(to));
        }

        // ------------------------------------------------------------------
        // 10. 道しるべ：分かれ道ごとに、行き先の方へ矢印を向ける
        // ------------------------------------------------------------------
        /// <summary>分かれ道と、そこから出る道の向き。</summary>
        public static List<(Vector2 at, List<Vector2> dirs)> TrailJunctions()
        {
            var points = new List<Vector2>();
            foreach (var t in MountainLayout.Trails)
            {
                points.Add(t[0]);
                points.Add(t[t.Length - 1]);
                for (int i = 1; i < t.Length - 1; i++) points.Add(t[i]);
            }
            var result = new List<(Vector2, List<Vector2>)>();
            foreach (var j in points)
            {
                if (result.Any(r => Vector2.Distance(r.Item1, j) < 2f)) continue;
                var dirs = new List<Vector2>();
                foreach (var t in MountainLayout.Trails)
                    for (int i = 0; i < t.Length; i++)
                    {
                        if (Vector2.Distance(t[i], j) > 0.6f) continue;
                        if (i > 0) dirs.Add((t[i - 1] - j).normalized);
                        if (i < t.Length - 1) dirs.Add((t[i + 1] - j).normalized);
                    }
                // 3 本以上が出会う所と、2 本の道がつながる所（行き先がかわる所）
                var uniq = new List<Vector2>();
                foreach (var d in dirs) if (!uniq.Any(u => Vector2.Dot(u, d) > 0.94f)) uniq.Add(d);
                int ends = MountainLayout.Trails.Count(t => Vector2.Distance(t[0], j) < 0.6f || Vector2.Distance(t[t.Length - 1], j) < 0.6f);
                if (uniq.Count >= 3 || (uniq.Count == 2 && ends >= 2)) result.Add((j, uniq));
            }
            return result;
        }

        void ReplaceSigns()
        {
            if (assets.TryGet("Mtn_SignPost") == null || assets.TryGet("Mtn_SignArrow") == null) return;
            foreach (var go in PlacedObjects().Where(g => g.name == "Mtn_Sign").ToList())
            {
                if (HoldsDew(go)) continue;
                Unplace(go);
            }
            var spots = TrailJunctions().Select(j => j).ToList();
            // ふもとの入り口にも（山頂の方と、トンネルの方）
            var head = MountainLayout.Trails[0];
            spots.Insert(0, (head[1], new List<Vector2> { (head[2] - head[1]).normalized, (head[0] - head[1]).normalized }));
            foreach (var (at, dirs) in spots)
            {
                // 柱は、道のわき（道をふさがない・しずくのそばでない所）
                Vector3 post = Vector3.zero;
                for (int k = 0; k < 24 && post == Vector3.zero; k++)
                {
                    float a = k * Mathf.PI / 12f;
                    Vector2 p = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2.7f + 0.3f * (k / 12));
                    if (MountainLayout.DistToTrail(p) < 2f || DewNear(p, 1.6f) || MountainLayout.InSpring(p.x, p.y, 1f)) continue;
                    if (Crowded(MG(p) + Vector3.up * 1.2f, 0.9f)) continue;
                    post = MG(p);
                }
                if (post == Vector3.zero) continue;
                Place("Mtn_SignPost", assets.bark, post + Vector3.down * 0.3f, Quaternion.Euler(0f, 0f, 0f), 1f, true, true, 250f);
                Occupy(new Vector2(post.x, post.z), 1.5f);
                for (int i = 0; i < dirs.Count && i < 4; i++)
                {
                    float yaw = Mathf.Atan2(-dirs[i].y, dirs[i].x) * Mathf.Rad2Deg;   // メッシュの +X を、行き先の向きへ
                    Place("Mtn_SignArrow", assets.bark, post + Vector3.up * (5.0f - i * 0.95f), Quaternion.Euler(0f, yaw, 0f), 1f, true, true, 250f);
                }
                Fixed("fix_sign", post);
            }
        }

        // ------------------------------------------------------------------
        // 11. しずくのまわりの、しずくをかくす草花・はい松を取りのぞく
        // ------------------------------------------------------------------
        void ClearPlantsAroundDews()
        {
            var cleared = new HashSet<int>();
            instanced.Edit((mesh, mat, m) =>
            {
                if (mat != assets.foliage && mat != assets.flowers) return m;
                Vector3 p = m.GetColumn(3);
                float h = mesh.bounds.max.y * m.lossyScale.y;
                if (h < 0.5f) return m;   // 低い苔やクローバーは、そのまま
                for (int i = 0; i < DewdropPoints.Count; i++)
                {
                    var d = DewdropPoints[i];
                    float r = 0.7f + Mathf.Max(mesh.bounds.extents.x, mesh.bounds.extents.z) * m.lossyScale.x * 0.6f;
                    if (new Vector2(p.x - d.x, p.z - d.z).sqrMagnitude < r * r && p.y + h > d.y && p.y < d.y + 1f)
                    {
                        if (cleared.Add(i)) Fixed("fix_dewplant", d);
                        return null;
                    }
                }
                return m;
            });
        }

        // ------------------------------------------------------------------
        // 12b. 小道のわきの、背の高い草花（低いカメラの前をふさぐ）
        // ------------------------------------------------------------------
        void ClearTallPlantsAlongTrails()
        {
            var trails = MountainLayout.Trails;
            var counts = new int[trails.Count];
            var where = new Vector3[trails.Count];
            instanced.Edit((mesh, mat, m) =>
            {
                if (mat != assets.foliage && mat != assets.flowers) return m;
                float h = mesh.bounds.max.y * m.lossyScale.y;
                if (h < 2.4f) return m;
                Vector3 p = m.GetColumn(3);
                Vector2 xz = new Vector2(p.x, p.z);
                for (int i = 0; i < trails.Count; i++)
                {
                    var t = trails[i];
                    for (int k = 0; k < t.Length - 1; k++)
                        if (ShakuMath.DistToSegment(xz, t[k], t[k + 1]) < 2.2f)
                        {
                            counts[i]++;
                            where[i] = p;
                            return null;
                        }
                }
                return m;
            });
            for (int i = 0; i < trails.Count; i++)
                if (counts[i] > 0) Fixed("fix_trailplants" + i, where[i]);
            TrailPlantCounts = counts;
        }

        /// <summary>小道ごとの、取りのぞいた背の高い草花の数（MOUNTAIN_FIXES.md の点検用）。</summary>
        public int[] TrailPlantCounts { get; private set; } = new int[0];

        // ------------------------------------------------------------------
        // 12. 着いたときの景色の手前（カメラのそば）の、背の高い花
        // ------------------------------------------------------------------
        void ClearTallFlowersInViews()
        {
            var done = new HashSet<int>();
            var lms = Area.Landmarks;
            instanced.Edit((mesh, mat, m) =>
            {
                if (mat != assets.flowers) return m;
                float h = mesh.bounds.max.y * m.lossyScale.y;
                if (h < 2.5f) return m;
                Vector3 p = m.GetColumn(3);
                for (int i = 0; i < lms.Count; i++)
                {
                    var v = lms[i].view;
                    if (v == null) continue;
                    Vector3 f = v.Forward;
                    Vector2 a = v.from - new Vector2(f.x, f.z) * (v.distance + 1.5f);
                    Vector2 b = v.from + new Vector2(f.x, f.z) * 7f;
                    if (ShakuMath.DistToSegment(new Vector2(p.x, p.z), a, b) < 2.4f)
                    {
                        if (done.Add(i)) Fixed("fix_viewflower", MG(v.from));
                        return null;
                    }
                }
                return m;
            });
        }

        // ------------------------------------------------------------------
        // 13. 大きな松の根：坂の下がわで、根が地面から浮いていた
        // ------------------------------------------------------------------
        /// <summary>
        /// 松の根は、平らな地面に合わせた形なので、坂の下がわ（南から北西）では根の先が地面から浮いていた。
        /// 根もとの頂点だけを、その場所の地面の低さに合わせて下げる（坂にそって根がはう）。幹の上・枝・葉のかたまりは動かさない。
        /// 当たり判定も同じ形にする。しずくを置いたあとに行うので、しずくの場所は変わらない。
        /// </summary>
        void PineRootsToGround()
        {
            if (_pine == null) return;
            var t = _pine.transform;
            float centerGround = Area.Height(t.position.x, t.position.z);
            var mf = _pine.GetComponent<MeshFilter>();
            var mc = _pine.GetComponent<MeshCollider>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
            Mesh src = mf.sharedMesh;
            Mesh bent = RootsToGround(src, t, centerGround, out float deepest);
            mf.sharedMesh = bent;
            if (mc != null && mc.sharedMesh != null && mc.sharedMesh.isReadable)
                mc.sharedMesh = mc.sharedMesh == src ? bent : RootsToGround(mc.sharedMesh, t, centerGround, out _);
            PineRootDrop = deepest;
            Fixed("fix_pineroots", t.position);
        }

        /// <summary>地面が低くなったぶんの、どれだけ根を下げるか。</summary>
        const float RootFollow = 0.7f;

        /// <summary>松の根を下げた、いちばん大きな量（テストと MOUNTAIN_FIXES.md の点検用）。</summary>
        public float PineRootDrop { get; private set; }

        /// <summary>根もと（幹の中心から 1.6 より外・高さ 4.5 より下）の頂点を、地面が低い所だけ、その低さぶん下げたメッシュ。</summary>
        Mesh RootsToGround(Mesh src, Transform t, float centerGround, out float deepest)
        {
            var m = Own(Object.Instantiate(src));
            m.name = src.name;
            var v = m.vertices;
            deepest = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i];
                float w = ShakuMath.SmoothStep(1.6f, 3.6f, new Vector2(p.x, p.z).magnitude) * (1f - ShakuMath.SmoothStep(2.2f, 4.5f, p.y));
                if (w <= 0f) continue;
                Vector3 wp = t.TransformPoint(p);
                // 地面が低くなったぶんの 6 わり下げる（根の下がわは、もとの形で地面より 1.1 以上深いので、これで浮かない。
                // ぜんぶ下げると、根が土にうまって見えなくなる）。地面の三角形は高さの式より少し低い所があるので、少し余分に下げる
                float drop = Mathf.Min(0f, (Area.Height(wp.x, wp.z) - centerGround) * RootFollow - 0.1f) * w;
                if (drop >= 0f) continue;
                v[i] = p + t.InverseTransformVector(Vector3.up * drop);
                deepest = Mathf.Min(deepest, drop);
            }
            m.vertices = v;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>小道・泉・景色の通り道からはなれた、いきものの居場所（want のそば）。</summary>
        Vector2 CalmSpot(Vector2 want, float keep = 3.5f)
        {
            for (int i = 0; i < 64; i++)
            {
                float d = 8f * Mathf.Sqrt(i / 63f);
                float a = i * 2.39996f;
                Vector2 p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (MountainLayout.DistToTrail(p) < keep || MountainLayout.InSpring(p.x, p.y, 1.5f) || InViewLane(p) || !IsLand(p, 0.1f)) continue;
                if (new Vector2(p.x, p.y).magnitude > Area.PlayRadius - 4f) continue;
                return p;
            }
            return want;
        }
    }
}

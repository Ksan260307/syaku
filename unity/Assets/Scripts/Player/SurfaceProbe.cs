using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    public struct SurfacePoint
    {
        public Vector3 point;
        public Vector3 normal;
        public Transform platform;     // 動く足場に乗っているとき
        public Vector3 localPoint;
        public Vector3 localNormal;

        public SurfacePoint(Vector3 p, Vector3 n)
        {
            point = p;
            normal = n;
            platform = null;
            localPoint = Vector3.zero;
            localNormal = Vector3.up;
        }

        /// <summary>当たった物が動く足場なら、その上の相対位置も覚えておく。</summary>
        public static SurfacePoint On(Vector3 p, Vector3 n, Collider c)
        {
            var sp = new SurfacePoint(p, n);
            if (c != null)
            {
                var mp = c.GetComponentInParent<MovingPlatform>();
                if (mp != null)
                {
                    sp.platform = mp.transform;
                    sp.localPoint = mp.transform.InverseTransformPoint(p);
                    sp.localNormal = mp.transform.InverseTransformDirection(n);
                }
            }
            return sp;
        }

        /// <summary>足場が動いていれば、いまの位置に更新したもの。</summary>
        public SurfacePoint Updated()
        {
            if (platform == null) return this;
            var sp = this;
            sp.point = platform.TransformPoint(localPoint);
            sp.normal = platform.TransformDirection(localNormal).normalized;
            return sp;
        }
    }

    /// <summary>
    /// 表面に沿って「這って」進んだ先を探す。凹んだ角（床→壁）や出っ張った角（崖のふち）も回り込む。
    /// </summary>
    public static class SurfaceProbe
    {
        struct MeshData
        {
            public Vector3[] normals;
            public int[] triangles;
        }

        static readonly Dictionary<Mesh, MeshData> Cache = new Dictionary<Mesh, MeshData>();

        public static int Mask => ShakuConst.WalkableMask;

        public static void ClearCache() => Cache.Clear();

        /// <summary>メッシュの頂点法線を重心座標で補間した、なめらかな法線。</summary>
        public static Vector3 SmoothNormal(RaycastHit hit)
        {
            var mc = hit.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null || hit.triangleIndex < 0) return hit.normal;
            Mesh mesh = mc.sharedMesh;
            if (!Cache.TryGetValue(mesh, out var data))
            {
                if (!mesh.isReadable) return hit.normal;
                data = new MeshData { normals = mesh.normals, triangles = mesh.triangles };
                Cache[mesh] = data;
            }
            int ti = hit.triangleIndex * 3;
            if (data.normals == null || data.normals.Length == 0 || ti + 2 >= data.triangles.Length) return hit.normal;
            Vector3 b = hit.barycentricCoordinate;
            Vector3 n = data.normals[data.triangles[ti]] * b.x + data.normals[data.triangles[ti + 1]] * b.y + data.normals[data.triangles[ti + 2]] * b.z;
            n = mc.transform.TransformDirection(n);
            if (n.sqrMagnitude < 1e-8f) return hit.normal;
            n.Normalize();
            return Vector3.Dot(n, hit.normal) < 0.3f ? hit.normal : n;
        }

        public static bool Raycast(Vector3 origin, Vector3 dir, float dist, out RaycastHit hit)
        {
            return Physics.Raycast(origin, dir, out hit, dist, Mask, QueryTriggerInteraction.Ignore);
        }

        const float HopHeight = 0.45f;   // 舟の乗り降りで上がれる高さ
        const float HopDrop = 0.5f;      // 舟の乗り降りで下りられる高さ（ふつうより少し大きい）
        public const float CeilingY = -0.35f;      // 法線の y がこれより下なら「天井（裏側）」
        public const float StepDownRange = 0.8f;   // これくらいの段差なら、ふちを回り込まずにそのまま下りる
        /// <summary>ふちを下りるとき、これより急な面（水平から 80 度より急）には回り込まず、前へ進んで落ちる。</summary>
        public const float MaxDescentY = 0.1736f;   // cos(80°)

        /// <summary>
        /// 上向きの面から、80 度より急な面へ「下りる」ように回り込もうとしているか（そのときは落ちる）。
        /// 進む向き d を面 m に移したとき下を向いているかで判断する（柄のふくらみを上る場合は落ちない）。
        /// </summary>
        public static bool TooSteepToDescend(Vector3 n, Vector3 m, Vector3 d)
        {
            if (m.y >= MaxDescentY || n.y < MaxDescentY) return false;
            Vector3 nd = ShakuMath.ProjectOnPlaneSafe(Quaternion.FromToRotation(n, m) * d, m, d);
            return nd.normalized.y < -0.3f;
        }

        public static bool Valid(Vector3 p)
        {
            var area = Areas.Current;
            return area.InPlayArea(p) && !area.IsUnderwater(p);
        }

        public static bool IsCeiling(Vector3 n) => n.y < CeilingY;

        /// <summary>直前の Walk が進めなかった理由（調べもの・テスト用）。</summary>
        public static string LastFail { get; private set; } = "";
        /// <summary>直前の Walk が止まった場所（がけのふちなど）と、そのときの進む向き。</summary>
        public static Vector3 LastStopPoint { get; private set; }
        public static Vector3 LastStopDir { get; private set; }
        static Vector3 _curP, _curD;
        /// <summary>調べもの用：null でなければ、Walk の一歩ごとの様子を書きこむ。</summary>
        public static System.Text.StringBuilder Trace;

        /// <summary>
        /// n の面から m の面へ移ってよいか。地面からいきなり裏側（天井）へ行ったり、
        /// ふちを回り込んで裏側へ入ったりはしない。壁から天井へは、凹んだ角（キノコの柄からかさの裏）なら行ける。
        /// </summary>
        public static bool CanEnter(Vector3 n, Vector3 m, bool concave)
        {
            if (!IsCeiling(m) || IsCeiling(n)) return true;
            return concave && n.y <= 0.5f;
        }

        /// <summary>のりこえられる高さ（体長くらい）。</summary>
        public const float ReachOverHeight = 0.85f;

        /// <summary>ひさしの上の面を、上から探す。</summary>
        static bool TryReachOver(RaycastHit underside, Vector3 d, Vector3 p, out RaycastHit top)
        {
            Vector3 from = underside.point + d * 0.18f + Vector3.up * (ReachOverHeight + 0.15f);
            if (Raycast(from, Vector3.down, ReachOverHeight + 0.3f, out top) && top.normal.y > 0.5f
                && top.point.y > p.y - 0.05f && top.point.y - p.y < ReachOverHeight && Valid(top.point))
                return true;
            return false;
        }

        /// <summary>前の少し先から真下へ、低い所の上向きの面を探す（小さな段差をそのまま下りる）。</summary>
        static bool TryStepDown(Vector3 ahead, Vector3 d, Vector3 p, out RaycastHit hit)
        {
            Vector3 from = ahead + d * 0.04f + Vector3.up * 0.05f;
            return Raycast(from, Vector3.down, StepDownRange + 0.1f, out hit) && hit.normal.y > 0.5f
                   && hit.point.y < p.y - 0.02f && Valid(hit.point);
        }

        /// <summary>そのまま上がれる小さな段（樹皮のふちの盛り上がりなど）の高さ。</summary>
        public const float StepUpHeight = 0.25f;

        /// <summary>
        /// 前の少し先の、少しだけ高い上向きの面を上から探す（先の点が盛り上がりの中にうまったとき用）。
        /// </summary>
        static bool TryStepUp(Vector3 ahead, Vector3 d, Vector3 p, out RaycastHit hit)
        {
            Vector3 from = ahead + d * 0.04f + Vector3.up * (StepUpHeight + 0.08f);
            return Raycast(from, Vector3.down, StepUpHeight + 0.2f, out hit) && hit.normal.y > 0.5f
                   && hit.point.y >= p.y - 0.02f && hit.point.y - p.y < StepUpHeight && Valid(hit.point);
        }

        /// <summary>
        /// start から dir 方向へ、表面に沿って distance だけ進む。
        /// 成功すれば到達点と、そこでの進行方向を返す。
        /// </summary>
        public static bool Walk(SurfacePoint start, Vector3 dir, float distance, out SurfacePoint result, out Vector3 endDir)
            => Walk(start, dir, distance, out result, out endDir, out _);

        /// <summary>offEdge: 急ながけのふちに来て進めなかった（そのまま進むと落ちる）。</summary>
        public static bool Walk(SurfacePoint start, Vector3 dir, float distance, out SurfacePoint result, out Vector3 endDir, out bool offEdge)
        {
            offEdge = false;
            LastFail = "";
            Vector3 p = start.point;
            Vector3 n = start.normal;
            Vector3 d = ShakuMath.ProjectOnPlaneSafe(dir, n, ShakuMath.AnyPerpendicular(n));
            Vector3 d0 = d;
            result = start;
            endDir = d;
            Collider lastCol = null;
            bool onPlatform = start.platform != null;
            float remaining = distance;
            const float step = 0.11f;
            const float lift = 0.05f;
            int guard = 0, slides = 0;
            while (remaining > 1e-4f && guard++ < 64)
            {
                float s = Mathf.Min(step, remaining);
                Vector3 origin = p + n * lift;
                _curP = p;
                _curD = d;

                // 1) 行く手に壁（凹んだ角）があれば、その面へ乗り移る。かすめるだけなら沿ってすべる
                if (Raycast(origin, d, s + 0.02f, out var wall))
                {
                    Vector3 wn = SmoothNormal(wall);
                    // 細かいでこぼこ（樹皮のすじなど）は、なめらかな法線では平らに見えても実際はじゃまをするので、
                    // 面そのものの向きのほうが行く手をふさいでいれば、そちらで判断する
                    if (Vector3.Dot(wall.normal, n) < Vector3.Dot(wn, n) - 0.15f) wn = wall.normal;
                    Trace?.Append($" [wall p={p} n={n} d={d} hit={wall.collider.name} wn={wn} hn={wall.normal}]");
                    if (Vector3.Dot(wn, n) < 0.85f)
                    {
                        // 地面から低いひさし（根・葉っぱのふち・横たわった枝）に当たった：裏側にははりつかず、
                        // 体をのばして上の面へ乗りこえる。とどかない高さなら、そこで止まる
                        if (IsCeiling(wn) && n.y > 0.5f)
                        {
                            if (TryReachOver(wall, d, p, out var over0))
                            {
                                MoveTo(over0, ref p, ref n, ref d, ref lastCol);
                                remaining -= s;
                                continue;
                            }
                            return Fail("overhang");
                        }
                        bool facing = Vector3.Dot(wn, d) < -0.2f;
                        if (facing && CanEnter(n, wn, true))
                        {
                            if (!Valid(wall.point)) return Fail("wall-invalid");
                            Vector3 nd = ShakuMath.ProjectOnPlaneSafe(n, wn, ShakuMath.AnyPerpendicular(wn));
                            remaining -= Mathf.Max(wall.distance, 0.02f);
                            lastCol = wall.collider;
                            p = wall.point;
                            n = wn;
                            d = nd;
                            continue;
                        }
                        // 乗り移らない面（かすめた柄・地面から見た裏側）：めり込まないよう面に沿ってすべる
                        Vector3 slide = Vector3.ProjectOnPlane(d, wn);
                        slide = Vector3.ProjectOnPlane(slide, n);
                        if (slide.sqrMagnitude < 0.05f || ++slides > 3) return Fail("slide-blocked");
                        slide.Normalize();
                        if (Vector3.Dot(slide, d0) < 0.35f) return Fail("slide-turn");   // 大きく向きが変わるなら、ぶつかったことにする
                        d = slide;
                        continue;
                    }
                }

                Vector3 ahead = origin + d * s;

                // 1.5) 動く足場（葉っぱの舟）と地面のあいだの乗り降り：少し高さがちがっても乗り移れる
                if (Raycast(ahead + n * HopHeight, -n, HopHeight + lift + s * 1.2f + HopDrop, out var hop)
                    && Vector3.Dot(hop.normal, n) > 0.5f)
                {
                    bool hopPlatform = hop.collider.GetComponentInParent<MovingPlatform>() != null;
                    if (hopPlatform != onPlatform && Valid(hop.point))
                    {
                        Vector3 hn = SmoothNormal(hop);
                        d = ShakuMath.ProjectOnPlaneSafe(Quaternion.FromToRotation(n, hn) * d, hn, d);
                        lastCol = hop.collider;
                        p = hop.point;
                        n = hn;
                        onPlatform = hopPlatform;
                        remaining -= s;
                        continue;
                    }
                }

                // 2) 少し先で表面に下ろす（坂やでこぼこ）
                if (Raycast(ahead, -n, lift + s * 1.2f + 0.04f, out var down))
                {
                    Vector3 dn = SmoothNormal(down);
                    Trace?.Append($" [down p={p} n={n} d={d} hit={down.collider.name} dn={dn}]");
                    if (!Valid(down.point)) return Fail("slope-invalid");
                    bool concave = Vector3.Dot(dn - n, d) < 0f;
                    if (!concave && TooSteepToDescend(n, dn, d))
                    {
                        // まるいふちの先が 80 度より急：回り込まずに落ちる（小さな段差なら下りる）
                        if (TryStepDown(ahead, d, p, out var drop0))
                        {
                            MoveTo(drop0, ref p, ref n, ref d, ref lastCol);
                            remaining -= s;
                            continue;
                        }
                        offEdge = true;
                        return Fail("cliff");
                    }
                    if (!CanEnter(n, dn, concave))
                    {
                        // まるい物の上から裏側へ回り込みそうなときは、下の地面へ下りる
                        if (!TryStepDown(ahead, d, p, out var drop)) return Fail("no-drop");
                        MoveTo(drop, ref p, ref n, ref d, ref lastCol);
                        remaining -= s;
                        continue;
                    }
                    d = ShakuMath.ProjectOnPlaneSafe(Quaternion.FromToRotation(n, dn) * d, dn, d);
                    lastCol = down.collider;
                    p = down.point;
                    n = dn;
                    remaining -= s;
                    continue;
                }

                // 3) 出っ張った角（ふち）。小さな段差ならそのまま下り、高ければ回り込む
                if (n.y > 0.3f && TryStepDown(ahead, d, p, out var small))
                {
                    MoveTo(small, ref p, ref n, ref d, ref lastCol);
                    remaining -= s;
                    continue;
                }
                Vector3 below = ahead - n * (lift + s * 1.1f + 0.06f);
                Trace?.Append($" [edge? p={p} n={n} d={d}]");
                if (Raycast(below, -d, s * 1.6f + 0.1f, out var edge))
                {
                    Vector3 en = SmoothNormal(edge);
                    Trace?.Append($" [edge hit={edge.collider.name} en={en}]");
                    if (!Valid(edge.point)) return Fail("edge-invalid");
                    if (TooSteepToDescend(n, en, d))
                    {
                        // がけのふち：80 度より急な壁には回り込まない。小さな段差ならそのまま下り、高ければ前へ進んで落ちる
                        if (TryStepDown(ahead, d, p, out var lipDrop))
                        {
                            MoveTo(lipDrop, ref p, ref n, ref d, ref lastCol);
                            remaining -= s;
                            continue;
                        }
                        offEdge = true;
                        return Fail("cliff");
                    }
                    if (!CanEnter(n, en, false))
                    {
                        if (!TryStepDown(ahead, d, p, out var drop)) return Fail("no-drop");
                        MoveTo(drop, ref p, ref n, ref d, ref lastCol);
                        remaining -= s;
                        continue;
                    }
                    d = ShakuMath.ProjectOnPlaneSafe(Quaternion.FromToRotation(n, en) * d, en, -n);
                    lastCol = edge.collider;
                    p = edge.point;
                    n = en;
                    remaining -= s;
                    continue;
                }
                if (TryStepDown(ahead, d, p, out var last))
                {
                    MoveTo(last, ref p, ref n, ref d, ref lastCol);
                    remaining -= s;
                    continue;
                }
                // 先が少し盛り上がっていて、先の点がその中に入ってしまった：上の面へ上がる
                if (n.y > 0.5f && TryStepUp(ahead, d, p, out var up))
                {
                    MoveTo(up, ref p, ref n, ref d, ref lastCol);
                    remaining -= s;
                    continue;
                }
                // 薄い物（葉っぱ）のふちで、その先に何もない：上向きの面なら落ちる
                offEdge = n.y >= MaxDescentY;
                return Fail("nothing-ahead");
            }
            result = lastCol != null ? SurfacePoint.On(p, n, lastCol) : start;
            if (lastCol == null) result = new SurfacePoint(p, n);
            endDir = d;
            return true;
        }

        static bool Fail(string why)
        {
            LastFail = why;
            LastStopPoint = _curP;
            LastStopDir = _curD;
            return false;
        }

        static void MoveTo(RaycastHit hit, ref Vector3 p, ref Vector3 n, ref Vector3 d, ref Collider lastCol)
        {
            Vector3 hn = SmoothNormal(hit);
            d = ShakuMath.ProjectOnPlaneSafe(d, hn, ShakuMath.AnyPerpendicular(hn));
            p = hit.point;
            n = hn;
            lastCol = hit.collider;
        }

        /// <summary>真下（-normal 方向）の表面を探す。</summary>
        public static bool Snap(Vector3 point, Vector3 normal, float range, out SurfacePoint result)
        {
            result = new SurfacePoint(point, normal);
            if (Raycast(point + normal * range * 0.5f, -normal, range, out var hit))
            {
                result = SurfacePoint.On(hit.point, SmoothNormal(hit), hit.collider);
                return true;
            }
            return false;
        }

        public static bool GroundBelow(Vector3 point, float maxDist, out RaycastHit hit)
        {
            return Physics.Raycast(point, Vector3.down, out hit, maxDist, Mask, QueryTriggerInteraction.Ignore);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    public struct SurfacePoint
    {
        public Vector3 point;
        public Vector3 normal;

        public SurfacePoint(Vector3 p, Vector3 n)
        {
            point = p;
            normal = n;
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

        public static int Mask => ShakuConst.SurfaceMask;

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

        static bool Valid(Vector3 p)
        {
            return ForestLayout.InPlayArea(p) && !ForestLayout.IsUnderwater(p);
        }

        /// <summary>
        /// start から dir 方向へ、表面に沿って distance だけ進む。
        /// 成功すれば到達点と、そこでの進行方向を返す。
        /// </summary>
        public static bool Walk(SurfacePoint start, Vector3 dir, float distance, out SurfacePoint result, out Vector3 endDir)
        {
            Vector3 p = start.point;
            Vector3 n = start.normal;
            Vector3 d = ShakuMath.ProjectOnPlaneSafe(dir, n, ShakuMath.AnyPerpendicular(n));
            result = start;
            endDir = d;
            float remaining = distance;
            const float step = 0.11f;
            const float lift = 0.05f;
            int guard = 0;
            while (remaining > 1e-4f && guard++ < 64)
            {
                float s = Mathf.Min(step, remaining);
                Vector3 origin = p + n * lift;

                // 1) 行く手に壁（凹んだ角）があれば、その面へ乗り移る
                if (Raycast(origin, d, s + 0.02f, out var wall))
                {
                    Vector3 wn = SmoothNormal(wall);
                    if (Vector3.Dot(wn, n) < 0.85f && Vector3.Dot(wn, d) < -0.2f)
                    {
                        if (!Valid(wall.point)) return false;
                        Vector3 nd = ShakuMath.ProjectOnPlaneSafe(n, wn, ShakuMath.AnyPerpendicular(wn));
                        remaining -= Mathf.Max(wall.distance, 0.02f);
                        p = wall.point;
                        n = wn;
                        d = nd;
                        continue;
                    }
                }

                // 2) 少し先で表面に下ろす（坂やでこぼこ）
                Vector3 ahead = origin + d * s;
                if (Raycast(ahead, -n, lift + s * 1.2f + 0.04f, out var down))
                {
                    Vector3 dn = SmoothNormal(down);
                    if (!Valid(down.point)) return false;
                    d = ShakuMath.ProjectOnPlaneSafe(Quaternion.FromToRotation(n, dn) * d, dn, d);
                    p = down.point;
                    n = dn;
                    remaining -= s;
                    continue;
                }

                // 3) 出っ張った角（ふち）を回り込む
                Vector3 below = ahead - n * (lift + s * 1.1f + 0.06f);
                if (Raycast(below, -d, s * 1.6f + 0.1f, out var edge))
                {
                    Vector3 en = SmoothNormal(edge);
                    if (!Valid(edge.point)) return false;
                    d = ShakuMath.ProjectOnPlaneSafe(Quaternion.FromToRotation(n, en) * d, en, -n);
                    p = edge.point;
                    n = en;
                    remaining -= s;
                    continue;
                }
                return false;
            }
            result = new SurfacePoint(p, n);
            endDir = d;
            return true;
        }

        /// <summary>真下（-normal 方向）の表面を探す。</summary>
        public static bool Snap(Vector3 point, Vector3 normal, float range, out SurfacePoint result)
        {
            result = new SurfacePoint(point, normal);
            if (Raycast(point + normal * range * 0.5f, -normal, range, out var hit))
            {
                result = new SurfacePoint(hit.point, SmoothNormal(hit));
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

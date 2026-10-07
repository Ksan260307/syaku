using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 細かい凹凸のある物（松ぼっくりのかさなど）の、大まかな当たり判定。
    /// 見た目のとおりの当たり判定だと、登ったときに凹凸のすき間にはさまって動けなくなるので、
    /// 凹凸の先をなめらかにつないだ「回転体（つつみこむ形）」を当たり判定にする。
    /// </summary>
    public static class CoarseCollider
    {
        /// <summary>大まかな当たり判定にする物の名前。</summary>
        public static readonly HashSet<string> Names = new HashSet<string> { "Pinecone" };

        static readonly Dictionary<Mesh, Mesh> Cache = new Dictionary<Mesh, Mesh>();

        public static bool Wants(string meshName) => Names.Contains(meshName);

        /// <summary>
        /// メッシュをつつみこむ、なめらかな回転体。いちばん長い向きを軸にして、軸にそって輪切りにし、
        /// それぞれの輪でいちばん外の点までの半径をとる（となりの輪とも合わせて、なめらかにする）。
        /// </summary>
        public static Mesh Envelope(Mesh src, int bands = 16, int segments = 20)
        {
            if (src == null) return null;
            if (Cache.TryGetValue(src, out var cached) && cached != null) return cached;
            var verts = src.vertices;
            Bounds b = src.bounds;
            Vector3 size = b.size;
            int axis = size.y >= size.x && size.y >= size.z ? 1 : (size.x >= size.z ? 0 : 2);
            Vector3 A = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 U = axis == 1 ? Vector3.right : Vector3.up;
            Vector3 V = Vector3.Cross(A, U);
            float lo = b.min[axis], hi = b.max[axis];
            float len = Mathf.Max(1e-4f, hi - lo);
            Vector3 c = b.center;
            // 輪切りごとの、いちばん外までの半径
            var r = new float[bands];
            foreach (var v in verts)
            {
                int i = Mathf.Clamp(Mathf.FloorToInt((v[axis] - lo) / len * bands), 0, bands - 1);
                Vector3 d = v - c;
                d -= A * Vector3.Dot(d, A);
                r[i] = Mathf.Max(r[i], d.magnitude);
            }
            // となりの輪のでっぱりもつつむ → 平均してなめらかに（でこぼこの谷はうめる）
            var env = new float[bands];
            for (int i = 0; i < bands; i++)
                env[i] = Mathf.Max(r[i], Mathf.Max(i > 0 ? r[i - 1] : 0f, i < bands - 1 ? r[i + 1] : 0f) * 0.92f);
            for (int pass = 0; pass < 2; pass++)
            {
                var tmp = (float[])env.Clone();
                for (int i = 0; i < bands; i++)
                {
                    float a = i > 0 ? env[i - 1] : env[i], bb = i < bands - 1 ? env[i + 1] : env[i];
                    tmp[i] = Mathf.Max(r[i] * 0.97f, (a + env[i] * 2f + bb) * 0.25f);
                }
                env = tmp;
            }

            var pos = new List<Vector3>();
            var tris = new List<int>();
            Vector3 axisCenter = c - A * Vector3.Dot(c, A);
            pos.Add(axisCenter + A * lo);   // 下のはし
            for (int i = 0; i < bands; i++)
            {
                float h = lo + (i + 0.5f) / bands * len;
                for (int s = 0; s < segments; s++)
                {
                    float ang = s * Mathf.PI * 2f / segments;
                    pos.Add(axisCenter + A * h + (U * Mathf.Cos(ang) + V * Mathf.Sin(ang)) * env[i]);
                }
            }
            pos.Add(axisCenter + A * hi);   // 上のはし
            int top = pos.Count - 1;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                tris.Add(0); tris.Add(1 + s1); tris.Add(1 + s);
                for (int i = 0; i < bands - 1; i++)
                {
                    int a0 = 1 + i * segments + s, a1 = 1 + i * segments + s1;
                    int b0 = a0 + segments, b1 = a1 + segments;
                    tris.Add(a0); tris.Add(a1); tris.Add(b0);
                    tris.Add(a1); tris.Add(b1); tris.Add(b0);
                }
                int l0 = 1 + (bands - 1) * segments + s, l1 = 1 + (bands - 1) * segments + s1;
                tris.Add(l0); tris.Add(l1); tris.Add(top);
            }
            var mesh = new Mesh { name = src.name + "_Coarse" };
            mesh.SetVertices(pos);
            mesh.SetTriangles(tris, 0);
            // 外向きの面になっているか確かめて、逆なら裏返す
            mesh.RecalculateNormals();
            var n = mesh.normals;
            int mid = 1 + (bands / 2) * segments;
            if (Vector3.Dot(n[mid], pos[mid] - (axisCenter + A * (lo + (bands / 2 + 0.5f) / bands * len))) < 0f)
            {
                for (int t = 0; t < tris.Count; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            Cache[src] = mesh;
            return mesh;
        }
    }
}

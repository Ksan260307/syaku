using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 物の大まかな当たり判定。見た目のとおりの当たり判定だと、細かい凹凸（松ぼっくりのかさ・切り株の樹皮のみぞ・
    /// 岩のでこぼこ・草の葉のすき間）にはさまって動けなくなるので、いちばん外側（最大外形）をなめらかにつつむ形にする。
    ///  ・Relax（くぼみをうめる）：切り株・大樹・木の幹・丸太・岩・川の石・キノコのかたまり。見た目の形のまま、
    ///    樹皮のみぞや岩のくぼみのような細かいへこみだけを外へ押し出す（出っぱり・穴・トンネル・根の形はそのまま）。
    ///  ・LatheLongest（回転体）：松ぼっくり・ゆりかご。いちばん長い向きを軸に、輪郭を回す。
    ///  ・Blob（まるいかたまり）：草（葉がばらばらの物）。まん中から見た向きごとに、いちばん外までの距離をとる。
    ///  ・いきものの体も、くぼみをうめる。どんぐりのぼうし（中のくぼみが大事）・小枝・葉っぱ（うすい物）は、見た目のとおり。
    /// </summary>
    public static class CoarseCollider
    {
        public enum Shape { Exact, Relax, LatheLongest, Blob }

        static readonly Dictionary<string, Shape> Shapes = new Dictionary<string, Shape>
        {
            { "Pinecone", Shape.LatheLongest },
            { "Cradle", Shape.LatheLongest },
            { "Stump", Shape.Relax },
            { "GreatTree", Shape.Relax },
            { "BgTrunk_A", Shape.Relax },
            { "BgTrunk_B", Shape.Relax },
            { "HollowLog", Shape.Relax },
            { "Mushroom_Cluster", Shape.Relax },
            { "Rock_A", Shape.Relax }, { "Rock_B", Shape.Relax }, { "Rock_C", Shape.Relax }, { "Rock_D", Shape.Relax },
            { "RiverStone_A", Shape.Relax }, { "RiverStone_B", Shape.Relax }, { "RiverStone_C", Shape.Relax },
            { "Fern_A", Shape.Blob }, { "Fern_B", Shape.Blob },
            { "Sprout", Shape.Blob }, { "Reed", Shape.Blob }, { "Horsetail", Shape.Blob }, { "Iris", Shape.Blob },
        };

        static readonly Dictionary<(Mesh, Shape), Mesh> Cache = new Dictionary<(Mesh, Shape), Mesh>();

        /// <summary>大まかな当たり判定にする物の名前（見た目のとおりにする物は false）。</summary>
        public static bool Wants(string meshName) => ShapeOf(meshName) != Shape.Exact;

        public static Shape ShapeOf(string meshName) => meshName != null && Shapes.TryGetValue(meshName, out var s) ? s : Shape.Exact;

        /// <summary>名前で決まる形の、大まかな当たり判定（見た目のとおりにする物は src のまま）。</summary>
        public static Mesh For(string meshName, Mesh src)
        {
            var shape = ShapeOf(meshName);
            return shape == Shape.Exact ? src : Build(src, shape);
        }

        public static Mesh Build(Mesh src, Shape shape)
        {
            if (src == null || shape == Shape.Exact) return src;
            if (Cache.TryGetValue((src, shape), out var cached) && cached != null) return cached;
            Mesh m = shape == Shape.Blob ? BlobMesh(src) : shape == Shape.Relax ? RelaxMesh(src) : LatheMesh(src, -1);
            m.name = src.name + "_Coarse";
            Cache[(src, shape)] = m;
            return m;
        }

        /// <summary>松ぼっくりなどの回転体（いちばん長い向きが軸）。</summary>
        public static Mesh Envelope(Mesh src) => Build(src, Shape.LatheLongest);

        /// <summary>頂点と、三角形のまん中・辺のまん中（大きな三角形の面も、つつみもらさない）。</summary>
        static List<Vector3> SamplePoints(Mesh src)
        {
            var v = src.vertices;
            var t = src.triangles;
            var pts = new List<Vector3>(v.Length + t.Length);
            pts.AddRange(v);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                pts.Add((a + b + c) / 3f);
                pts.Add((a + b) * 0.5f);
                pts.Add((b + c) * 0.5f);
                pts.Add((c + a) * 0.5f);
            }
            return pts;
        }

        /// <summary>となりの値も見て、なめらかにつつむ（谷はうめて、山はけずらない）。</summary>
        static float[] SmoothUpper(float[] r, bool wrap, int passes = 2)
        {
            int n = r.Length;
            var env = new float[n];
            for (int i = 0; i < n; i++)
            {
                float a = wrap ? r[(i - 1 + n) % n] : (i > 0 ? r[i - 1] : r[i]);
                float b = wrap ? r[(i + 1) % n] : (i < n - 1 ? r[i + 1] : r[i]);
                env[i] = Mathf.Max(r[i], Mathf.Max(a, b) * 0.95f);
            }
            for (int p = 0; p < passes; p++)
            {
                var tmp = (float[])env.Clone();
                for (int i = 0; i < n; i++)
                {
                    float a = wrap ? env[(i - 1 + n) % n] : (i > 0 ? env[i - 1] : env[i]);
                    float b = wrap ? env[(i + 1) % n] : (i < n - 1 ? env[i + 1] : env[i]);
                    tmp[i] = Mathf.Max(r[i], (a + env[i] * 2f + b) * 0.25f);
                }
                env = tmp;
            }
            return env;
        }

        // ------------------------------------------------------------------
        // くぼみをうめる（見た目の形のまま、細かいへこみだけ外へ押し出す）
        // ------------------------------------------------------------------
        /// <summary>
        /// 同じ場所の頂点をまとめ、まわりの頂点よりへこんでいる頂点を、外向きに押し出す（何回かくり返す）。
        /// 出っぱりはけずらないので、いちばん外側（最大外形）は変わらない。穴やトンネル・根の形はそのまま残る。
        /// </summary>
        static Mesh RelaxMesh(Mesh src, int iterations = 8, float step = 0.85f)
        {
            var v = src.vertices;
            var t = src.triangles;
            // 同じ場所の頂点をまとめる
            var map = new int[v.Length];
            var index = new Dictionary<Vector3Int, int>();
            var pos = new List<Vector3>();
            float q = Mathf.Max(1e-4f, src.bounds.size.magnitude * 1e-5f);
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x / q), Mathf.RoundToInt(v[i].y / q), Mathf.RoundToInt(v[i].z / q));
                if (!index.TryGetValue(key, out int id))
                {
                    id = pos.Count;
                    index[key] = id;
                    pos.Add(v[i]);
                }
                map[i] = id;
            }
            var tris = new int[t.Length];
            for (int i = 0; i < t.Length; i++) tris[i] = map[t[i]];
            int n = pos.Count;
            var nbr = new List<int>[n];
            for (int i = 0; i < n; i++) nbr[i] = new List<int>(6);
            void Link(int a, int b)
            {
                if (a == b) return;
                if (!nbr[a].Contains(b)) nbr[a].Add(b);
                if (!nbr[b].Contains(a)) nbr[b].Add(a);
            }
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Link(tris[i], tris[i + 1]);
                Link(tris[i + 1], tris[i + 2]);
                Link(tris[i + 2], tris[i]);
            }
            var p = pos.ToArray();
            var normals = new Vector3[n];
            for (int it = 0; it < iterations; it++)
            {
                System.Array.Clear(normals, 0, n);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 fn = Vector3.Cross(p[tris[i + 1]] - p[tris[i]], p[tris[i + 2]] - p[tris[i]]);   // 面積の重み
                    normals[tris[i]] += fn;
                    normals[tris[i + 1]] += fn;
                    normals[tris[i + 2]] += fn;
                }
                var next = (Vector3[])p.Clone();
                for (int i = 0; i < n; i++)
                {
                    if (nbr[i].Count == 0 || normals[i].sqrMagnitude < 1e-12f) continue;
                    Vector3 nn = normals[i].normalized;
                    Vector3 avg = Vector3.zero;
                    foreach (int k in nbr[i]) avg += p[k];
                    avg /= nbr[i].Count;
                    float dip = Vector3.Dot(avg - p[i], nn);
                    if (dip > 0f) next[i] = p[i] + nn * (dip * step);   // へこんでいる所だけ、外へ
                }
                p = next;
            }
            var mesh = new Mesh { indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(p);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        // 回転体
        // ------------------------------------------------------------------
        /// <summary>
        /// axis = 1 なら上向き（Y）、-1 ならいちばん長い向きを軸にする。
        /// 横の形（高さごとの、軸からいちばん外まで）と、上のはしの形（軸からの距離ごとの、いちばん高い所）の
        /// 内側を回した形にする（どの点も、この形の中に入る）。
        /// </summary>
        static Mesh LatheMesh(Mesh src, int axisMode, int bands = 18, int rings = 10, int segments = 24)
        {
            var pts = SamplePoints(src);
            Bounds b = src.bounds;
            Vector3 size = b.size;
            int axis = axisMode == 1 ? 1 : (size.y >= size.x && size.y >= size.z ? 1 : (size.x >= size.z ? 0 : 2));
            Vector3 A = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 U = axis == 1 ? Vector3.right : Vector3.up;
            Vector3 V = Vector3.Cross(A, U);
            Vector3 c = b.center;
            Vector3 axisPoint = c - A * Vector3.Dot(c, A);
            float lo = b.min[axis], hi = b.max[axis];
            float len = Mathf.Max(1e-4f, hi - lo);
            // 高さごとの、いちばん外
            var side = new float[bands];
            float rMax = 0f;
            foreach (var p in pts)
            {
                float h = Vector3.Dot(p - axisPoint, A);
                Vector3 d = p - axisPoint - A * h;
                float r = d.magnitude;
                int i = Mathf.Clamp(Mathf.FloorToInt((h - lo) / len * bands), 0, bands - 1);
                side[i] = Mathf.Max(side[i], r);
                rMax = Mathf.Max(rMax, r);
            }
            side = SmoothUpper(side, false);
            rMax = Mathf.Max(rMax, 1e-3f);
            // 軸からの距離ごとの、いちばん高い所（上のはし）と、いちばん低い所（下のはし）
            var top = new float[rings];
            var bot = new float[rings];
            for (int k = 0; k < rings; k++) { top[k] = lo; bot[k] = hi; }
            foreach (var p in pts)
            {
                float h = Vector3.Dot(p - axisPoint, A);
                float r = (p - axisPoint - A * h).magnitude;
                int k = Mathf.Clamp(Mathf.FloorToInt(r / rMax * rings), 0, rings - 1);
                top[k] = Mathf.Max(top[k], h);
                bot[k] = Mathf.Min(bot[k], h);
            }
            // 外側の高い所は、内側でもその高さまでつつむ（内側に何もない所でも、ふたが下がりすぎない）
            for (int k = rings - 2; k >= 0; k--) if (top[k] < lo + len * 0.02f) top[k] = top[k + 1];
            for (int k = rings - 2; k >= 0; k--) if (bot[k] > hi - len * 0.02f) bot[k] = bot[k + 1];
            // なめらかにするのは、はしからの高さで（下のはしからの高さ・上のはしからの深さ）
            var up = new float[rings];
            var down = new float[rings];
            for (int k = 0; k < rings; k++) { up[k] = top[k] - lo; down[k] = hi - bot[k]; }
            up = SmoothUpper(up, false);
            down = SmoothUpper(down, false);
            for (int k = 0; k < rings; k++) { top[k] = lo + up[k]; bot[k] = hi - down[k]; }

            // 輪郭（下のはしの中心 → 下のふち → 横 → 上のふち → 上のはしの中心）
            var prof = new List<Vector2>();   // (r, h)
            for (int k = 0; k < rings; k++)
            {
                float r = (k + 0.5f) / rings * rMax;
                if (r > side[0]) break;
                prof.Add(new Vector2(r, bot[k]));
            }
            if (prof.Count == 0) prof.Add(new Vector2(side[0] * 0.5f, lo));
            prof.Insert(0, new Vector2(0f, prof[0].y));
            for (int i = 0; i < bands; i++)
            {
                float h = lo + (i + 0.5f) / bands * len;
                prof.Add(new Vector2(side[i], h));
            }
            var topPts = new List<Vector2>();
            for (int k = rings - 1; k >= 0; k--)
            {
                float r = (k + 0.5f) / rings * rMax;
                if (r > side[bands - 1]) continue;
                topPts.Add(new Vector2(r, top[k]));   // 切り株の上のように、まん中が低ければ、少しくぼんだふたになる
            }
            prof.AddRange(topPts);
            prof.Add(new Vector2(0f, prof[prof.Count - 1].y));
            return Revolve(prof, axisPoint, A, U, V, segments);
        }

        static Mesh Revolve(List<Vector2> prof, Vector3 axisPoint, Vector3 A, Vector3 U, Vector3 V, int segments)
        {
            var pos = new List<Vector3>();
            var tris = new List<int>();
            int n = prof.Count;
            for (int i = 0; i < n; i++)
                for (int s = 0; s < segments; s++)
                {
                    float ang = s * Mathf.PI * 2f / segments;
                    pos.Add(axisPoint + A * prof[i].y + (U * Mathf.Cos(ang) + V * Mathf.Sin(ang)) * prof[i].x);
                }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    int a0 = i * segments + s, a1 = i * segments + s1, b0 = a0 + segments, b1 = a1 + segments;
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(pos);
            mesh.SetTriangles(tris, 0);
            FaceOutward(mesh, axisPoint, A);
            return mesh;
        }

        // ------------------------------------------------------------------
        // まるいかたまり
        // ------------------------------------------------------------------
        /// <summary>
        /// まん中から見た向きごとに、いちばん外までの距離をとって、なめらかにつないだ形（大きさで割って、まるい空間で計算する）。
        /// </summary>
        static Mesh BlobMesh(Mesh src, int lat = 12, int lon = 24)
        {
            var pts = SamplePoints(src);
            Bounds b = src.bounds;
            Vector3 c = b.center;
            Vector3 ext = Vector3.Max(b.extents, Vector3.one * 1e-3f);
            var r = new float[lat, lon];
            foreach (var p in pts)
            {
                Vector3 q = p - c;
                q = new Vector3(q.x / ext.x, q.y / ext.y, q.z / ext.z);
                float m = q.magnitude;
                if (m < 1e-5f) continue;
                Vector3 d = q / m;
                float th = Mathf.Acos(Mathf.Clamp(d.y, -1f, 1f));            // 0 = 上
                float ph = Mathf.Atan2(d.z, d.x);
                int i = Mathf.Clamp(Mathf.FloorToInt(th / Mathf.PI * lat), 0, lat - 1);
                int j = ((Mathf.FloorToInt((ph + Mathf.PI) / (2f * Mathf.PI) * lon) % lon) + lon) % lon;
                r[i, j] = Mathf.Max(r[i, j], m);
            }
            // 何もない向きは、まわりの向きでうめる
            for (int pass = 0; pass < 4; pass++)
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                    {
                        if (r[i, j] > 0f) continue;
                        float best = 0f;
                        for (int di = -1; di <= 1; di++)
                            for (int dj = -1; dj <= 1; dj++)
                            {
                                int ii = Mathf.Clamp(i + di, 0, lat - 1), jj = (j + dj + lon) % lon;
                                best = Mathf.Max(best, r[ii, jj]);
                            }
                        r[i, j] = best;
                    }
            // となりの向きも見て、なめらかにつつむ
            var env = new float[lat, lon];
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    float best = r[i, j];
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            int ii = Mathf.Clamp(i + di, 0, lat - 1), jj = (j + dj + lon) % lon;
                            best = Mathf.Max(best, r[ii, jj] * 0.94f);
                        }
                    env[i, j] = best;
                }
            for (int pass = 0; pass < 2; pass++)
            {
                var tmp = (float[,])env.Clone();
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                    {
                        float sum = env[i, j] * 4f, w = 4f;
                        for (int di = -1; di <= 1; di++)
                            for (int dj = -1; dj <= 1; dj++)
                            {
                                if (di == 0 && dj == 0) continue;
                                int ii = Mathf.Clamp(i + di, 0, lat - 1), jj = (j + dj + lon) % lon;
                                sum += env[ii, jj];
                                w += 1f;
                            }
                        tmp[i, j] = Mathf.Max(r[i, j], sum / w);
                    }
                env = tmp;
            }
            // 頂点は、ます目のかどに置く（かどのまわり 4 つのます目の、大きい方）
            var pos = new List<Vector3>();
            var tris = new List<int>();
            float poleTop = 0f, poleBot = 0f;
            for (int j = 0; j < lon; j++) { poleTop = Mathf.Max(poleTop, env[0, j]); poleBot = Mathf.Max(poleBot, env[lat - 1, j]); }
            Vector3 P(float th, float ph, float rad) =>
                c + Vector3.Scale(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * rad, ext);
            pos.Add(P(0f, 0f, poleTop));
            for (int i = 1; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    float th = i * Mathf.PI / lat;
                    float ph = j * 2f * Mathf.PI / lon - Mathf.PI;
                    float rad = Mathf.Max(Mathf.Max(env[i - 1, j], env[i, j]), Mathf.Max(env[i - 1, (j - 1 + lon) % lon], env[i, (j - 1 + lon) % lon]));
                    pos.Add(P(th, ph, rad));
                }
            pos.Add(P(Mathf.PI, 0f, poleBot));
            int bottom = pos.Count - 1;
            for (int j = 0; j < lon; j++)
            {
                int j1 = (j + 1) % lon;
                tris.Add(0); tris.Add(1 + j1); tris.Add(1 + j);
                for (int i = 0; i < lat - 2; i++)
                {
                    int a0 = 1 + i * lon + j, a1 = 1 + i * lon + j1, b0 = a0 + lon, b1 = a1 + lon;
                    tris.Add(a0); tris.Add(a1); tris.Add(b0);
                    tris.Add(a1); tris.Add(b1); tris.Add(b0);
                }
                int l0 = 1 + (lat - 2) * lon + j, l1 = 1 + (lat - 2) * lon + j1;
                tris.Add(l0); tris.Add(l1); tris.Add(bottom);
            }
            var mesh = new Mesh();
            mesh.SetVertices(pos);
            mesh.SetTriangles(tris, 0);
            FaceOutward(mesh, c, Vector3.zero);
            return mesh;
        }

        /// <summary>面が外向きになるように、必要なら裏返す（裏からは当たらないため）。</summary>
        static void FaceOutward(Mesh mesh, Vector3 center, Vector3 axis)
        {
            mesh.RecalculateNormals();
            var v = mesh.vertices;
            var n = mesh.normals;
            float score = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 d = v[i] - center;
                if (axis != Vector3.zero) d -= axis * Vector3.Dot(d, axis);
                score += Vector3.Dot(n[i], d);
            }
            if (score < 0f)
            {
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                mesh.triangles = t;
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// メッシュの「三角形の少ない形」（取り込み時に作った段）を、別のメッシュとして用意する。
    /// 描くときに距離で選んで、そのメッシュで描く（どの環境でも同じように効く）。
    /// 頂点はもとのまま（色・UV・輪郭用の法線も同じ）で、三角形のつなぎ方だけがその段のもの。
    /// 必要になったときに 1 回だけ作り、あとは使いまわす（使っていないアセットの片づけで消えていたら、作りなおす）。
    /// </summary>
    public static class DetailMeshes
    {
        static readonly Dictionary<Mesh, Mesh[]> s_cache = new Dictionary<Mesh, Mesh[]>();
        static readonly Dictionary<Mesh, Mesh> s_collision = new Dictionary<Mesh, Mesh>();
        static readonly List<Vector3> s_v3 = new List<Vector3>();
        static readonly List<Vector2> s_v2 = new List<Vector2>();
        static readonly List<Color> s_col = new List<Color>();

        /// <summary>src の lod 段の形（0 や、段を持たないメッシュは src のまま）。</summary>
        public static Mesh Get(Mesh src, int lod)
        {
            if (src == null || lod <= 0 || src.lodCount <= 1 || src.subMeshCount != 1 || !src.isReadable) return src;
            if (!s_cache.TryGetValue(src, out var arr))
            {
                arr = new Mesh[src.lodCount];
                arr[0] = src;
                s_cache[src] = arr;
            }
            lod = Mathf.Min(lod, arr.Length - 1);
            if (arr[lod] == null) arr[lod] = Make(src, lod);
            return arr[lod];
        }

        /// <summary>
        /// 当たり判定に使う形：いつも、いちばん細かい形そのもの（段を持たないメッシュは src のまま）。
        /// 見た目のために作った少ない形が、当たり判定やすべりの計算にまざらないようにする。三角形の順番ももとのまま。
        /// </summary>
        public static Mesh ForCollision(Mesh src)
        {
            if (src == null || src.lodCount <= 1 || src.subMeshCount != 1 || !src.isReadable) return src;
            // 使っていないアセットの片づけで消えていたら、作りなおす
            if (!s_collision.TryGetValue(src, out var m) || m == null)
            {
                m = Make(src, 0, keepReadable: true);
                m.name = src.name;
                s_collision[src] = m;
            }
            return m;
        }

        static Mesh Make(Mesh src, int lod, bool keepReadable = false)
        {
            var m = new Mesh { name = $"{src.name}_Lod{lod}", indexFormat = src.indexFormat };
            src.GetVertices(s_v3);
            m.SetVertices(s_v3);
            src.GetNormals(s_v3);
            if (s_v3.Count > 0) m.SetNormals(s_v3);
            src.GetColors(s_col);
            if (s_col.Count > 0) m.SetColors(s_col);
            src.GetUVs(0, s_v2);
            if (s_v2.Count > 0) m.SetUVs(0, s_v2);
            src.GetUVs(3, s_v3);
            if (s_v3.Count > 0) m.SetUVs(3, s_v3);   // 輪郭線用の、なめらかな法線
            m.SetTriangles(src.GetTriangles(0, lod, true), 0, false);
            m.bounds = src.bounds;   // 見た目の範囲はもとの形と同じにする（画面に入るかの判定がずれない）
            m.UploadMeshData(!keepReadable);
            return m;
        }
    }
}

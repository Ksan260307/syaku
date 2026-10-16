using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// まとめ描き（インスタンシング）で、1 回に描く数をそろえる（WebGL）。
    /// WebGL では、まとめ描きのシェーダーが、まとめる数（32 きざみ）ごとに別に用意される。はじめて使う数のときにゲームが一瞬止まり、
    /// 読み込み画面で先にぜんぶ用意すると、とても時間がかかった（森で 25 秒ほど）。
    /// そこで、1 回に描く数を 2 とおり（2〜32・224）だけにする：224 ずつに分け、のこりが 33〜223 個のときは、
    /// 頂点の少ない形なら、見えない所（地面のずっと下）に、とても小さく描く物で 224 にうめ、頂点の多い形なら 32 個までずつに分けて描く
    /// （うめた分も GPU は計算するので、多い形をうめると重くなる）。1 個のときも 2 個にする（まとめない描き方にならない）。
    /// （シェーダーでまとめる数を決める forcemaxcount も試したが、1〜数個ずつの描画が多いいきもので重くなった）
    /// </summary>
    public static class InstanceBatch
    {
        public const int Small = 32;
        public const int Full = 224;
        /// <summary>うめてよい頂点の数（うめる物の数 × 形の頂点の数。これより多いなら、分けて描く）。</summary>
        public const int PadBudget = 6000;

        /// <summary>そろえるか（WebGL だけ。テストでは切りかえられる）。</summary>
        public static bool Enabled = Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>1 回に描く数のいちばん大きい数（呼ぶ側は、この数ずつに分ける）。</summary>
        public static int MaxPerCall => Enabled ? Full : 1023;

        static readonly Matrix4x4 Hidden = Matrix4x4.TRS(new Vector3(0f, -10000f, 0f), Quaternion.identity, Vector3.one * 1e-4f);
        static readonly List<(int start, int n)> s_calls = new List<(int, int)>();

        /// <summary>
        /// count 個（224 まで）を、描く回ごとの（はじめ, 数）に分ける。数は 2〜32 か 224。
        /// うめるとき（数をこえる分を、見えない物で 224 にする）は、うめたあとの数を返す。うめないときは count。
        /// </summary>
        public static int Split(int count, int vertexCount, List<(int start, int n)> calls)
        {
            calls.Clear();
            if (count <= 0) return count;
            if (!Enabled || count >= Full || (count > 1 && count <= Small))
            {
                calls.Add((0, count));
                return count;
            }
            if (count == 1)
            {
                calls.Add((0, 2));
                return 2;
            }
            if ((long)(Full - count) * vertexCount <= PadBudget)
            {
                calls.Add((0, Full));
                return Full;
            }
            int parts = (count + Small - 1) / Small;
            int start = 0;
            for (int k = 0; k < parts; k++)
            {
                int n = (count - start) / (parts - k);   // なるべく同じ数ずつ（1 個だけの回ができない）
                calls.Add((start, n));
                start += n;
            }
            return count;
        }

        /// <summary>count 個の行列（arr は 224 以上の大きさ）を、そろえた数で描く。</summary>
        public static void Draw(in RenderParams rp, Mesh mesh, Matrix4x4[] arr, int count)
        {
            if (!Enabled)
            {
                Graphics.RenderMeshInstanced(rp, mesh, 0, arr, count);
                return;
            }
            int to = Split(count, mesh != null ? mesh.vertexCount : 0, s_calls);
            for (int i = count; i < to; i++) arr[i] = Hidden;
            foreach (var (start, n) in s_calls) Graphics.RenderMeshInstanced(rp, mesh, 0, arr, n, start);
        }
    }
}

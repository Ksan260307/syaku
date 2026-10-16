using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しずくの残り（決めた場所に置いたあと）を、探索が楽しくなる所へ置く。
    /// ただ地面にばらまくのではなく、点をつけてえらぶ：
    /// ・登った先（物の上。少し高いほど、うれしい）
    /// ・物のかげ（すぐ上にキノコのかさやベンチや土管や橋がある地面。のぞきこむと見つかる。動く落ち葉の下はのぞく）
    /// ・名所のそば（名所に来たら、ひとつは見つかる）
    /// はじまりの場所のそばは、もう置いてあるので少なめ。方角ごとにかたよらないよう、しずくの少ない方角を先に。
    /// どのしずくも、ほかのしずくから少しはなす（ひとつ見つけたら、次をさがしたくなる間あい）。
    /// </summary>
    public partial class WorldGenerator
    {
        /// <summary>しずくの置き方の版（セーブの移しかえに使う。置き方を変えたら上げる）。</summary>
        public const int DewLayout = 3;

        void FillDewsToExplore(float rMin, float rMax, Func<Vector2, bool> ok = null, float fromHeight = 120f, float maxUp = 24f)
        {
            FillDewsToExplore(() =>
            {
                float a = R(0f, Mathf.PI * 2f);
                float r = Mathf.Sqrt(R(rMin * rMin, rMax * rMax));   // 広さあたり、同じくらいに
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }, ok, fromHeight, maxUp);
        }

        void FillDewsToExplore(Func<Vector2> sample, Func<Vector2, bool> ok, float fromHeight, float maxUp)
        {
            int target = Area.DropCount;
            if (DewdropPoints.Count >= target) return;
            var cands = new List<(Vector3 p, float score)>();
            for (int i = 0; i < 1600 && cands.Count < 800; i++)
            {
                Vector2 xz = sample();
                if (!(ok?.Invoke(xz) ?? IsLand(xz, 0.2f))) continue;
                if (!CastDown(new Vector3(xz.x, fromHeight, xz.y), fromHeight + 20f, out var top)) continue;
                float ground = Area.Height(xz.x, xz.y);
                ConsiderDew(cands, top, ground, maxUp, false);
                // 物のかげ：物の下の地面（すぐ上に、葉やベンチや橋がある）
                if (!IsGroundCollider(top.collider) && !Movable(top.collider) && top.point.y - ground > 1.2f
                    && CastDown(new Vector3(xz.x, ground + 0.9f, xz.y), 2f, out var low) && IsGroundCollider(low.collider) && OpenNook(low.point))
                    ConsiderDew(cands, low, ground, maxUp, true);
            }
            var used = new bool[cands.Count];
            var sectors = new int[8];
            foreach (var d in DewdropPoints) sectors[DewSector(d)]++;
            // 点の高い順に、間あいをとりながら（しずくの少ない方角を先に）。足りなければ、間あいを少しずつつめる
            foreach (float gap in new[] { 9f, 7f, 5.5f, 3.6f })
            {
                while (DewdropPoints.Count < target)
                {
                    int best = -1;
                    float bestScore = float.MinValue;
                    for (int k = 0; k < cands.Count; k++)
                    {
                        if (used[k]) continue;
                        Vector3 p = cands[k].p;
                        float s = cands[k].score - 0.45f * sectors[DewSector(p)];
                        if (s <= bestScore) continue;
                        bool near = false;
                        foreach (var d in DewdropPoints)
                            if ((d - p).sqrMagnitude < gap * gap) { near = true; break; }
                        if (near) continue;
                        best = k;
                        bestScore = s;
                    }
                    if (best < 0) break;
                    used[best] = true;
                    DewdropPoints.Add(cands[best].p);
                    sectors[DewSector(cands[best].p)]++;
                }
            }
        }

        static bool IsGroundCollider(Collider c) => c.name.StartsWith("Terrain_");

        /// <summary>動く物（大きな落ち葉・舟・浮き葉など）。上にも下にも、しずくは置かない（動くと、しずくが宙に浮く・かくれる）。</summary>
        static bool Movable(Collider c) => c.attachedRigidbody != null || c.GetComponentInParent<MovingPlatform>() != null || c.GetComponentInParent<BigLeaf>() != null;

        static int DewSector(Vector3 p) => Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(p.z, p.x) / (Mathf.PI * 2f) + 0.5f) * 8f), 0, 7);

        /// <summary>
        /// 物のかげの地面が、しゃくとりむしの入れる、ひらいた所か：すぐ上が物の下の面（物の中ではない）で、体が入る高さがあり、
        /// 横の 8 方向のうち 2 つ以上がひらいている（とじた箱の中ではない）。
        /// </summary>
        static bool OpenNook(Vector3 p)
        {
            bool back = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                if (!Physics.Raycast(p + Vector3.up * 0.05f, Vector3.up, out var roof, 30f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return false;
                if (roof.normal.y > 0f || roof.distance < 1.0f || roof.distance > 5.5f) return false;   // 物の中（上の面のうらに当たった）・せますぎる・高い木の下（かげではない）
                if (Movable(roof.collider)) return false;
                int open = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    if (!Physics.Raycast(p + Vector3.up * 0.35f, dir, 2f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) open++;
                }
                return open >= 2;
            }
            finally { Physics.queriesHitBackfaces = back; }
        }

        void ConsiderDew(List<(Vector3 p, float score)> cands, RaycastHit hit, float ground, float maxUp, bool under)
        {
            Vector3 p = hit.point;
            if (hit.normal.y < 0.75f || Area.IsUnderwater(p + Vector3.up * 0.05f) || !Area.InPlayArea(p)) return;
            if (Movable(hit.collider)) return;   // 動く物の上には置かない
            float up = p.y - ground;
            if (up > maxUp) return;   // 高すぎる所は、ごほうびより、たいへん
            bool onThing = !IsGroundCollider(hit.collider) && up > 0.8f;
            float s = 0.5f;
            if (onThing) s += 2f + Mathf.Min(up, 12f) / 12f * 1.5f;   // 登った先
            if (under) s += 2.2f;                                       // 物のかげ
            Vector2 xz = new Vector2(p.x, p.z);
            foreach (var lm in Area.Landmarks)
                if (Vector2.Distance(xz, lm.position) < lm.radius) { s += 0.6f; break; }
            if (Vector2.Distance(xz, Area.Spawn) < 12f) s -= 1.2f;   // はじまりの場所のそばには、もうしずくがある
            s += R(0f, 0.6f);   // 同じような所ばかりにならないよう、少しゆらす
            cands.Add((p, s));
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// いきものの居場所を、遊びやすさの面から見なおす（CREATURE_FIXES.md）。森・川辺・公園・山のどれも、いきものを置いたすぐあとに行い
    /// （あとから置く小物が、いきものの居場所をよけるように）、小物を置き終えたあとに、もう一度たしかめる（小物の中に入っていないか）。
    /// しずくを置いたあとなので、しずくの場所は変わらない。乱数も使わない（あとの小物の置き方を変えないように）。
    /// - 地面のいきもの：小道の上・トンネルの出口・しずくの上・物の中や下・外周のそば・ほかのいきもののすぐそばにいない。
    ///   体が大きく乗れるいきもの（カタツムリ・カエル・カブトムシ・カニ・カマキリ・クワガタ・トカゲ・リス・ナキウサギ）は、着いたときの景色の前にもいない
    ///   （小さな虫は景色をふさがず、着いたときに見つかるのは楽しいので、そのまま）
    /// - 鳥の降りる場所：小道・景色の前・名所のまん中・しずく・トンネルの出口をさける
    /// - 物の上のいきもの（葉・石・切り株など）：しずくの上にいない
    /// - いきもののまわりの背の高い草花を取りのぞく（低いカメラから見える）
    /// </summary>
    public partial class WorldGenerator
    {
        /// <summary>直したいきもの（種類・理由・前・あと）。テストと CREATURE_FIXES.md の点検用。</summary>
        public readonly List<string> CreatureFixLog = new List<string>();

        /// <summary>理由ごとの、直した数。</summary>
        public readonly Dictionary<string, int> CreatureFixCounts = new Dictionary<string, int>();

        /// <summary>小道から、地面のいきものをはなす距離（乗れるいきものは、もう少し）。</summary>
        public const float CreatureTrailKeep = 2.6f, RideableTrailKeep = 3.0f;
        /// <summary>トンネルの出口・着いて立つ所から、はなす距離。</summary>
        public const float CreatureArrivalKeep = 6f;
        /// <summary>しずくから、地面のいきものをはなす距離（よこの距離）。</summary>
        public const float CreatureDewKeep = 1.6f;
        /// <summary>ちがう群れどうしを、はなす距離。</summary>
        public const float CreatureCrowdKeep = 2.5f;
        /// <summary>鳥の降りる場所を、小道・しずくからはなす距離。</summary>
        public const float BirdTrailKeep = 3f, BirdDewKeep = 2.5f;
        /// <summary>ちがう種類の鳥の降りる場所から、はなす距離（動かすとき）。</summary>
        public const float OtherBirdKeep = 10f;

        /// <summary>水べのいきもの（もとの居場所が水べなら、直したあとも水べ）。</summary>
        public static readonly HashSet<string> WaterSide = new HashSet<string> { "frog", "crab", "riversnail", "okera", "sanshouuo" };

        void LogCreatureFix(string why, MobGroup g, Vector3 from, Vector3 to)
        {
            CreatureFixCounts[why] = CreatureFixCounts.TryGetValue(why, out var n) ? n + 1 : 1;
            CreatureFixLog.Add($"{Area.Id} {g.species} {why} ({from.x:F1},{from.y:F1},{from.z:F1}) -> ({to.x:F1},{to.y:F1},{to.z:F1})");
        }

        // ------------------------------------------------------------------
        // しらべる道具
        // ------------------------------------------------------------------
        public enum CreatureHome { Ground, OnObject, Bird, Flyer, Water, AntLine, Den }

        public CreatureHome HomeOf(MobGroup g)
        {
            var sp = SpeciesCatalog.Get(g.species);
            if (sp == null) return CreatureHome.Flyer;
            if (sp.kind == MobKind.Bird) return CreatureHome.Bird;
            if (sp.kind == MobKind.Flutter || sp.kind == MobKind.Hover) return CreatureHome.Flyer;
            if (sp.kind == MobKind.Skater) return CreatureHome.Water;
            if (sp.kind == MobKind.Marcher && g.path.Count > 1) return CreatureHome.AntLine;
            if (g.species == "okojo") return CreatureHome.Den;
            float above = g.center.y - Area.Height(g.center.x, g.center.z);
            if (above > 0.5f) return CreatureHome.OnObject;
            // 地面から少しだけ高い所：すぐ下に物（葉っぱなど）があれば、その上にいる
            if (above > 0.1f && Physics.Raycast(g.center + Vector3.up * 0.2f, Vector3.down, 0.4f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                return CreatureHome.OnObject;
            return CreatureHome.Ground;
        }

        /// <summary>景色の通り道の中か（あとから作るトンネルの景色もふくむ）。</summary>
        public bool InAnyViewLane(Vector2 p, float radius = 0f)
        {
            if (InViewLane(p, radius)) return true;
            foreach (var g in Area.Gates)
            {
                if (!g.late || g.arrival == null) continue;
                var (a, b) = LaneOf(g.arrival);
                if (InLane(p, a, b, radius)) return true;
            }
            return false;
        }

        /// <summary>トンネル・トンネルから出て立つ所・はじまりの場所までの、いちばん近い距離。</summary>
        public float DistToArrival(Vector2 p)
        {
            float best = Vector2.Distance(p, Area.Spawn);
            foreach (var g in Area.Gates)
            {
                best = Mathf.Min(best, Vector2.Distance(p, g.position) - 1f);
                if (Area.ArrivalFrom(g.targetArea, out var ap, out _)) best = Mathf.Min(best, Vector2.Distance(p, ap));
            }
            return best;
        }

        /// <summary>よこの距離で r より近い、同じくらいの高さのしずくがあるか。</summary>
        bool DewWithin(Vector3 p, float r, float dy = 2f)
        {
            foreach (var d in DewdropPoints)
                if (Mathf.Abs(d.y - p.y) < dy && new Vector2(d.x - p.x, d.z - p.z).sqrMagnitude < r * r) return true;
            return false;
        }

        /// <summary>地面のこの点に、物（岩・幹・キノコの柄など）がなく、体が入るすき間があるか。</summary>
        bool FreeAt(Vector3 ground, float r = 0.3f, float h = 1f)
            => !Physics.CheckCapsule(ground + Vector3.up * (r + 0.05f), ground + Vector3.up * h, r, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);

        static readonly Vector3[] InsideDirs =
        {
            Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3(0.7071f, 0f, 0.7071f), new Vector3(-0.7071f, 0f, -0.7071f),
        };

        /// <summary>
        /// 閉じた大きな物（キノコの柄・幹・岩）の、すっぽり中か。重なりの判定では、物の面にふれないと分からないので、
        /// まわりへのレイが、同じ物の裏がわ（内がわの面）にほとんど当たるかで見る。
        /// </summary>
        public static bool InsideSolid(Vector3 p)
        {
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Collider seen = null;
            int count = 0;
            foreach (var d in InsideDirs)
                if (Physics.Raycast(p, d, out var hit, 30f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, d) > 0.05f)
                {
                    if (seen == null || hit.collider == seen) { seen = hit.collider; count++; }
                }
            Physics.queriesHitBackfaces = prev;
            return count >= 5;
        }

        /// <summary>まわり r の中に、水があるか。</summary>
        public bool WaterWithin(Vector2 p, float r)
        {
            for (float d = 0.5f; d <= r; d += 0.5f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    if (!Area.IsLand(p.x + Mathf.Cos(a) * d, p.y + Mathf.Sin(a) * d, 0f)) return true;
                }
            return false;
        }

        /// <summary>ほかの群れ（地面・物の上・すみか）のまん中が、すぐそばにあるか。</summary>
        bool CrowdedBy(Vector3 p, MobGroup self)
        {
            foreach (var o in Mobs)
            {
                if (o == self) continue;
                var h = HomeOf(o);
                if (h != CreatureHome.Ground && h != CreatureHome.OnObject && h != CreatureHome.Den) continue;
                if (Mathf.Abs(o.center.y - p.y) < 2f && new Vector2(o.center.x - p.x, o.center.z - p.z).sqrMagnitude < CreatureCrowdKeep * CreatureCrowdKeep) return true;
            }
            return false;
        }

        /// <summary>名所のまん中（鳥が下りると、名所と景色をふさぐ所）か。</summary>
        bool AtLandmarkCenter(Vector2 p)
        {
            foreach (var lm in Area.Landmarks)
                if (Vector2.Distance(p, lm.position) < Mathf.Max(3.5f, lm.radius * 0.35f)) return true;
            return false;
        }

        /// <summary>from から近い順に、うずまきにさがす（乱数は使わない）。</summary>
        static bool Spiral(Vector2 from, float maxR, Func<Vector2, bool> ok, out Vector2 found, int samples = 220)
        {
            for (int i = 0; i < samples; i++)
            {
                float d = maxR * Mathf.Sqrt(i / (samples - 1f));
                float a = i * 2.39996f;
                Vector2 p = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (ok(p))
                {
                    found = p;
                    return true;
                }
            }
            found = from;
            return false;
        }

        // ------------------------------------------------------------------
        // 地面のいきもの
        // ------------------------------------------------------------------
        /// <summary>地面のいきものの居場所の決まり。合わないときは、理由を返す（合えば null）。</summary>
        public string GroundProblem(MobGroup g, Vector2 p, bool needWater)
        {
            var sp = SpeciesCatalog.Get(g.species);
            Vector3 ground = Area.Ground(p.x, p.y);
            if (!Area.IsLand(p.x, p.y, 0.1f)) return "water";
            if (p.magnitude > Area.PlayRadius - Mathf.Max(4f, g.radius + 2f)) return "edge";
            if (Area.DistToTrail(p) < (sp != null && sp.rideable ? RideableTrailKeep : CreatureTrailKeep)) return "trail";
            if (!g.showcase && sp != null && sp.rideable && InAnyViewLane(p, 0.3f)) return "view";
            if (!g.showcase && DistToArrival(p) < CreatureArrivalKeep) return "arrival";
            if (DewWithin(ground, CreatureDewKeep)) return "dew";
            if (Area.Normal(p.x, p.y).y < 0.8f) return "slope";
            if (!FreeAt(ground) || InsideSolid(ground + Vector3.up * 0.3f)) return "solid";
            if (CrowdedBy(ground, g)) return "crowd";
            if (needWater && !WaterWithin(p, 4f)) return "dry";
            return null;
        }

        void FixGroundCreature(MobGroup g)
        {
            Vector2 c = new Vector2(g.center.x, g.center.z);
            bool needWater = WaterSide.Contains(g.species) && WaterWithin(c, 4f);
            string why = GroundProblem(g, c, needWater);
            if (why == null) return;
            if (!Spiral(c, 10f, q => GroundProblem(g, q, needWater) == null, out var to)
                && !Spiral(c, 18f, q => GroundProblem(g, q, needWater) == null, out to))
            {
                CreatureFixLog.Add($"{Area.Id} {g.species} {why} : no spot");
                return;
            }
            Vector3 from = g.center;
            g.center = Area.Ground(to.x, to.y);
            Vector3 delta = g.center - from;
            for (int i = 0; i < g.path.Count; i++) g.path[i] += delta;
            LogCreatureFix(why, g, from, g.center);
        }

        /// <summary>
        /// 中心のまわりをぐるりとまわるいきもの（大きな赤キノコの根もとのかたつむり）：まわる輪が柄の中にかからない半径にし、
        /// はじめの場所も輪の上にする（輪のまん中＝柄の中からはじまると、外から見えない）。
        /// </summary>
        void FixCirclingCreature(MobGroup g)
        {
            Vector3 c = g.path[0];
            bool startsInMiddle = new Vector2(g.center.x - c.x, g.center.z - c.z).sqrMagnitude < 0.25f;
            for (float r = g.radius; r < g.radius + 8f; r += 0.25f)
            {
                int free = 0, first = -1;
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI / 12f;
                    Vector2 q = new Vector2(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r);
                    Vector3 gq = Area.Ground(q.x, q.y);
                    if (!Area.IsLand(q.x, q.y, 0.1f) || !FreeAt(gq, 0.3f, 0.8f) || InsideSolid(gq + Vector3.up * 0.3f) || DewWithin(gq, 1f)) continue;
                    free++;
                    if (first < 0 && Area.DistToTrail(q) >= CreatureTrailKeep) first = k;
                }
                if (free < 23 || first < 0) continue;
                if (Mathf.Abs(r - g.radius) < 0.01f && !startsInMiddle) return;   // 輪も、はじめの場所もよい：そのまま
                Vector3 from = g.center;
                float af = first * Mathf.PI / 12f;
                g.radius = r;
                g.center = Area.Ground(c.x + Mathf.Cos(af) * r, c.z + Mathf.Sin(af) * r);
                LogCreatureFix("circle", g, from, g.center);
                return;
            }
        }

        // ------------------------------------------------------------------
        // 物の上のいきもの（葉・石・切り株・洞の上など）
        // ------------------------------------------------------------------
        void FixCreatureOnObject(MobGroup g)
        {
            if (!DewWithin(g.center, 1.3f, 1f)) return;
            if (!Physics.Raycast(g.center + Vector3.up * 0.5f, Vector3.down, out var under, 1.5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return;
            var col = under.collider;
            Vector2 c = new Vector2(g.center.x, g.center.z);
            Vector3 spot = g.center;
            bool ok = Spiral(c, 3f, q =>
            {
                if (!Physics.Raycast(new Vector3(q.x, g.center.y + 1.5f, q.y), Vector3.down, out var h, 3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return false;
                if (h.collider != col || h.normal.y < 0.7f || Mathf.Abs(h.point.y - g.center.y) > 0.5f || DewWithin(h.point, 1.5f, 1f)) return false;
                spot = h.point;
                return true;
            }, out _, 120);
            if (!ok) return;
            Vector3 from = g.center;
            g.center = spot;
            g.radius = Mathf.Min(g.radius, 0.6f);   // 葉や石の上で、しずくの方へ歩いていかないように
            LogCreatureFix("dew", g, from, g.center);
        }

        // ------------------------------------------------------------------
        // 水の上のいきもの（アメンボ・ゲンゴロウ）
        // ------------------------------------------------------------------
        /// <summary>
        /// 水の上のいきものが、岸・葉・石から見つけられる距離にいる割合（群れのまん中と、まわりの 12 点）。
        /// しゃくとりむしは水に入れないので、岸から遠い所ばかりにいると、見えるのに近づけない。
        /// </summary>
        public float SkaterReach(Vector3 c, float radius, float discoverRadius)
        {
            int reach = 0;
            float rr = discoverRadius * 0.85f;
            for (int k = -1; k < 12; k++)
            {
                Vector3 q = c;
                if (k >= 0)
                {
                    float an = k * Mathf.PI / 6f;
                    q += new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an)) * radius * 0.7f;
                }
                bool ok = false;
                for (float d = 0f; d <= rr && !ok; d += 0.5f)
                    for (int j = 0; j < 16 && !ok; j++)
                    {
                        float an = j * Mathf.PI / 8f;
                        float x = q.x + Mathf.Cos(an) * d, z = q.z + Mathf.Sin(an) * d;
                        if (Area.IsLand(x, z, 0.05f)) ok = true;
                        else if (Physics.Raycast(new Vector3(x, q.y + 3f, z), Vector3.down, out var h, 3.5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                                 && h.normal.y > 0.6f && h.point.y > Area.WaterLevelAt(x, z) + 0.02f) ok = true;
                    }
                if (ok) reach++;
            }
            return reach / 13f;
        }

        /// <summary>水の上で、まわりの輪もほとんど水か。</summary>
        bool OpenWater(Vector2 p, float radius)
        {
            float wl = Area.WaterLevelAt(p.x, p.y);
            if (wl < -100f || Area.Height(p.x, p.y) > wl - 0.15f) return false;
            int wet = 0;
            for (int k = 0; k < 12; k++)
            {
                float an = k * Mathf.PI / 6f;
                float x = p.x + Mathf.Cos(an) * radius * 0.8f, z = p.y + Mathf.Sin(an) * radius * 0.8f;
                if (!Area.IsLand(x, z, 0f) && Mathf.Abs(Area.WaterLevelAt(x, z) - wl) < 0.3f) wet++;
            }
            return wet >= 9;
        }

        void FixSkater(MobGroup g)
        {
            var sp = SpeciesCatalog.Get(g.species);
            if (sp == null || SkaterReach(g.center, g.radius, sp.discoverRadius) >= 0.7f) return;
            Vector2 c = new Vector2(g.center.x, g.center.z);
            Vector3 spot = g.center;
            // せまい川では、群れの広さを小さくしてさがす
            foreach (float r in new[] { g.radius, Mathf.Min(g.radius, 3.5f), Mathf.Min(g.radius, 2.5f) })
            {
                bool ok = Spiral(c, 12f, q =>
                {
                    if (!OpenWater(q, r)) return false;
                    var p = new Vector3(q.x, Area.WaterLevelAt(q.x, q.y), q.y);
                    if (SkaterReach(p, r, sp.discoverRadius) < 0.75f) return false;
                    spot = p;
                    return true;
                }, out _, 90);
                if (!ok) continue;
                Vector3 from = g.center;
                g.center = spot;
                g.radius = r;
                LogCreatureFix("reach", g, from, g.center);
                return;
            }
            CreatureFixLog.Add($"{Area.Id} {g.species} reach : no spot");
        }

        // ------------------------------------------------------------------
        // 鳥の降りる場所
        // ------------------------------------------------------------------
        public string BirdSpotProblem(Vector2 p, IList<Vector3> others, int self, float walk = 2f)
        {
            Vector3 ground = Area.Ground(p.x, p.y);
            if (!Area.IsLand(p.x, p.y, 0.2f)) return "water";
            if (p.magnitude > Area.PlayRadius - 6f) return "edge";
            if (Area.DistToTrail(p) < BirdTrailKeep) return "trail";
            if (InAnyViewLane(p, 0.5f)) return "view";
            if (DistToArrival(p) < CreatureArrivalKeep) return "arrival";
            if (AtLandmarkCenter(p)) return "landmark";
            if (DewWithin(ground, BirdDewKeep, 99f)) return "dew";
            if (Area.Normal(p.x, p.y).y < 0.85f) return "slope";
            if (!FreeAt(ground, Mathf.Clamp(walk, 1.2f, 2f), 2.4f) || InsideSolid(ground + Vector3.up * 0.3f)) return "solid";   // 下りてから歩きまわる所に、物がない
            for (int i = 0; i < others.Count; i++)
                if (i != self && new Vector2(others[i].x - p.x, others[i].z - p.y).sqrMagnitude < 4f * 4f) return "near";
            return null;
        }

        /// <summary>ちがう種類の鳥の降りる場所から、はなれているか。</summary>
        bool FarFromOtherBirds(Vector2 p, MobGroup self)
        {
            foreach (var o in Mobs)
            {
                if (o == self || HomeOf(o) != CreatureHome.Bird) continue;
                foreach (var q in o.path)
                    if (new Vector2(q.x - p.x, q.z - p.y).sqrMagnitude < OtherBirdKeep * OtherBirdKeep) return false;
            }
            return true;
        }

        void FixBird(MobGroup g)
        {
            bool centerIsFirst = g.path.Count > 0 && (g.path[0] - g.center).sqrMagnitude < 0.01f;
            if (g.path.Count == 0) g.path.Add(g.center);
            for (int i = 0; i < g.path.Count; i++)
            {
                Vector3 p = g.path[i];
                Vector2 xz = new Vector2(p.x, p.z);
                // 物の上（切り株の頂・すべり台）に下りる場所は、地面へ
                bool raised = p.y - Area.Height(p.x, p.z) > 0.5f;
                string why = BirdSpotProblem(xz, g.path, i, g.radius);
                if (why == null && !raised) continue;
                if (why == null) why = "raised";
                int self = i;
                // 動かす先は、ちがう種類の鳥の降りる場所からもはなす（ハトとスズメが重ならない）
                if (!Spiral(xz, 12f, q => BirdSpotProblem(q, g.path, self, g.radius) == null && FarFromOtherBirds(q, g), out var to)
                    && !Spiral(xz, 20f, q => BirdSpotProblem(q, g.path, self, g.radius) == null && FarFromOtherBirds(q, g), out to))
                {
                    CreatureFixLog.Add($"{Area.Id} {g.species} land{i} {why} : no spot");
                    continue;
                }
                g.path[i] = Area.Ground(to.x, to.y);
                LogCreatureFix("bird-" + why, g, p, g.path[i]);
            }
            if (centerIsFirst || g.path.Count == 1) g.center = g.path[0];
        }

        // ------------------------------------------------------------------
        // まわりの背の高い草花
        // ------------------------------------------------------------------
        /// <summary>地面のいきものの居場所のまわりの、背の高い草花を取りのぞく（いきものをかくしてしまう）。</summary>
        void ClearPlantsAroundCreatures()
        {
            var spots = new List<Vector3>();
            foreach (var g in Mobs)
            {
                var h = HomeOf(g);
                if (h == CreatureHome.Ground || h == CreatureHome.Den) spots.Add(g.center);
                else if (h == CreatureHome.Bird) spots.AddRange(g.path);   // 鳥の降りる場所も
            }
            if (spots.Count == 0) return;
            int removed = instanced.Edit((mesh, mat, m) =>
            {
                if (mat != assets.foliage && mat != assets.flowers) return m;
                float ht = mesh.bounds.max.y * m.lossyScale.y;
                if (ht < 0.9f) return m;
                Vector3 p = m.GetColumn(3);
                foreach (var s in spots)
                    if (p.y < s.y + 0.6f && p.y + ht > s.y && new Vector2(p.x - s.x, p.z - s.z).sqrMagnitude < 1.1f * 1.1f)
                        return null;
                return m;
            });
            if (removed > 0)
            {
                CreatureFixCounts["plants"] = (CreatureFixCounts.TryGetValue("plants", out var n) ? n : 0) + removed;
                CreatureFixLog.Add($"{Area.Id} plants removed {removed}");
            }
        }

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------
        /// <param name="again">小物を置き終えたあとの、2 度目のたしかめ（記録は消さずに足す）。</param>
        void FixCreaturesForPlay(bool again = false)
        {
            if (!again)
            {
                CreatureFixLog.Clear();
                CreatureFixCounts.Clear();
            }
            Physics.SyncTransforms();
            foreach (var g in Mobs)
            {
                switch (HomeOf(g))
                {
                    case CreatureHome.Ground:
                        if (g.path.Count == 1 && g.radius > 0f) FixCirclingCreature(g);
                        else FixGroundCreature(g);
                        break;
                    case CreatureHome.OnObject: FixCreatureOnObject(g); break;
                    case CreatureHome.Bird: FixBird(g); break;
                    case CreatureHome.Water: FixSkater(g); break;
                }
            }
            ClearPlantsAroundCreatures();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト（遊びやすさ）：どのエリアも、地図を上から見て網の目のように（東西・南北の線にそって。…Diagonal は、ななめ 45 度の線にそって）
    /// 画面スティックと同じ操作で歩いてみる。止まった所はわけ（水・がけ・登れる物・見えないかべ・引っかかり）を分けて数え、
    /// 引っかかったときは、どの向きにも動けない「とじこめ」になっていないかを確かめる。
    /// 地面の下へぬける・数がこわれる（NaN）・とじこめは、あってはいけない。
    /// SHAKU_GRID_OUT に書き出し先があれば、エリアを上から見た絵に歩いた跡と止まった所をかいて残す。
    /// </summary>
    // 時間がかかる（全エリアで 30 分ほど）ので、ふだんのテストでは走らせない。名前を指定したときだけ：
    //   -testFilter Shakutori.Tests.GridWalkTests（エリアだけなら …GridWalkTests.Forest_GridWalk）
    [Explicit("網の目に歩くテストは、たのまれたときだけ走らせる")]
    public class GridWalkTests
    {
        // 網の目の間かく（16 ごとの線の、あいだにも 2 本ずつ。縦横それぞれ 3 倍の本数）。
        // SHAKU_GRID_SPACING で変えられる（たとえば 1 にすると、ずっと細かく歩く。そのかわり全エリアで 3 時間ほど）
        static float Spacing => float.TryParse(Environment.GetEnvironmentVariable("SHAKU_GRID_SPACING"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0.2f ? v : 16f / 3f;
        const float StallTime = 2.5f;     // この時間、線にそって進めなければ「止まった」
        const float Lookahead = 3f;

        enum Kind { Water, Edge, Climb, Overhang, Outside, InvisibleWall, Stuck, Trapped, Under, Broken, Fall, Splash, Cramped }

        struct Event
        {
            public Kind kind;
            public Vector3 pos;
            public string note;
        }

        readonly List<Event> _events = new List<Event>();
        readonly List<Vector3> _trail = new List<Vector3>();
        readonly List<(Vector2 a, Vector2 b)> _skips = new List<(Vector2, Vector2)>();
        float _walked, _skipped, _lineLength, _covered;
        bool _autoCam;
        float _lastYaw = 999f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _events.Clear();
            _trail.Clear();
            _skips.Clear();
            _walked = _skipped = _lineLength = _covered = 0f;
            yield return Boot();
            yield return StartNewGame();
            // カメラはこちらで向ける（自動で後ろへ回らない）
            _autoCam = SaveSystem.Settings.autoCamera;
            SaveSystem.Settings.autoCamera = false;
            Worm.HitGround += OnHitGround;
            Worm.Splashed += OnSplash;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            SaveSystem.Settings.autoCamera = _autoCam;
            if (GM != null && Worm != null)
            {
                Worm.HitGround -= OnHitGround;
                Worm.Splashed -= OnSplash;
            }
            ResetInput();
        }

        void OnHitGround(float speed)
        {
            if (speed > 4f) _events.Add(new Event { kind = Kind.Fall, pos = Worm.CenterPosition, note = $"{speed:0.0}" });
        }

        void OnSplash() => _events.Add(new Event { kind = Kind.Splash, pos = Worm.CenterPosition });

        [UnityTest, Timeout(14400000)] public IEnumerator Forest_GridWalk() => Walk("forest");
        [UnityTest, Timeout(14400000)] public IEnumerator River_GridWalk() => Walk("river");
        [UnityTest, Timeout(14400000)] public IEnumerator Park_GridWalk() => Walk("park");
        [UnityTest, Timeout(14400000)] public IEnumerator Mountain_GridWalk() => Walk("mountain");
        [UnityTest, Timeout(14400000)] public IEnumerator Hut_GridWalk() => Walk("hut");
        [UnityTest, Timeout(14400000)] public IEnumerator Coast_GridWalk() => Walk("coast");
        [UnityTest, Timeout(14400000)] public IEnumerator Shrine_GridWalk() => Walk("shrine");
        [UnityTest, Timeout(14400000)] public IEnumerator Night_GridWalk() => Walk("night");
        // 斜めの網の目（北東・南東へ向かう 45 度の線）。東西・南北の線では、いつも同じ向きから物に当たるので、ななめからも
        [UnityTest, Timeout(14400000)] public IEnumerator Forest_GridWalkDiagonal() => Walk("forest", true);
        [UnityTest, Timeout(14400000)] public IEnumerator River_GridWalkDiagonal() => Walk("river", true);
        [UnityTest, Timeout(14400000)] public IEnumerator Park_GridWalkDiagonal() => Walk("park", true);
        [UnityTest, Timeout(14400000)] public IEnumerator Mountain_GridWalkDiagonal() => Walk("mountain", true);
        [UnityTest, Timeout(14400000)] public IEnumerator Hut_GridWalkDiagonal() => Walk("hut", true);
        [UnityTest, Timeout(14400000)] public IEnumerator Coast_GridWalkDiagonal() => Walk("coast", true);
        [UnityTest, Timeout(14400000)] public IEnumerator Shrine_GridWalkDiagonal() => Walk("shrine", true);
        [UnityTest, Timeout(14400000)] public IEnumerator Night_GridWalkDiagonal() => Walk("night", true);

        IEnumerator Walk(string areaId, bool diagonal = false)
        {
            if (Areas.Current.Id != areaId)
            {
                Assert.IsTrue(GM.TravelTo(areaId));
                yield return WaitUntil(() => Areas.Current.Id == areaId && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, areaId + " へ");
                yield return Seconds(0.5f);
            }
            var area = Areas.Current;
            // 1 フレームを 1/30 秒として進める（テストの実時間を短くする。動きの細かさはふつうの 30fps と同じ）
            Time.captureDeltaTime = 1f / 30f;
            float realStart = Time.realtimeSinceStartup, gameStart = Time.time;

            float ext = area.MapExtent;
            var lines = new List<(Vector2, Vector2)>();
            // 線は ±8, ±24, ±40 … を通る（16 ごとの線を、すべてふくむ）
            float first = -ext + 8f;
            while (first - Spacing > -ext) first -= Spacing;
            var ids = new List<string>();
            if (!diagonal)
            {
                for (float c = first; c < ext; c += Spacing)
                {
                    lines.Add((new Vector2(-ext, c), new Vector2(ext, c)));   // 東西
                    ids.Add($"z{c:0.#}");
                    lines.Add((new Vector2(c, -ext), new Vector2(c, ext)));   // 南北
                    ids.Add($"x{c:0.#}");
                }
            }
            else
            {
                // 斜めの線：線どうしの間かくは、東西・南北と同じ（線にそってはかる長さで）。
                // u = 北東へ（x - z = 一定）、v = 南東へ（x + z = 一定）
                float r = ext * 1.4143f;
                Vector2 u = new Vector2(1f, 1f).normalized, v = new Vector2(1f, -1f).normalized;
                Vector2 nu = new Vector2(1f, -1f).normalized, nv = new Vector2(1f, 1f).normalized;
                float firstD = -r + Spacing * 0.5f;
                for (float c = firstD; c < r; c += Spacing)
                {
                    lines.Add((nu * c - u * r, nu * c + u * r));
                    ids.Add($"d{c:0.#}");
                    lines.Add((nv * c - v * r, nv * c + v * r));
                    ids.Add($"e{c:0.#}");
                }
            }
            string only = Environment.GetEnvironmentVariable("SHAKU_GRID_ONLY");   // 調べる用：たとえば "z40" で z=40 の線だけ（斜めは d・e）
            for (int li = 0; li < lines.Count; li++)
            {
                var (a, b) = lines[li];
                if (!string.IsNullOrEmpty(only) && ids[li] != only) continue;
                ClipToPlay(area, a, b, out var ca, out var cb);
                if (Vector2.Distance(ca, cb) < 2f) continue;
                yield return WalkLine(area, ca, cb);
            }
            ResetInput();
            Time.captureDeltaTime = 0f;

            float real = Time.realtimeSinceStartup - realStart, game = Time.time - gameStart;
            var counts = Enum.GetValues(typeof(Kind)).Cast<Kind>().ToDictionary(k => k, k => _events.Count(e => e.kind == k));
            var sb = new StringBuilder();
            sb.Append($"GRID area={areaId}{(diagonal ? " diagonal" : "")} lines={_lineLength:0}m covered={_covered:0}m ({100f * _covered / Mathf.Max(1f, _lineLength):0}% 歩けた) skipped={_skipped:0}m moved={_walked:0}m game={game:0}s real={real:0}s");
            foreach (var kv in counts) sb.Append($" {kv.Key}={kv.Value}");
            Debug.Log(sb.ToString());
            foreach (var e in _events.Where(e => e.kind != Kind.Splash))
                Debug.Log($"GRIDEV area={areaId}{(diagonal ? "/diag" : "")} {e.kind} x={e.pos.x:0.0} y={e.pos.y:0.0} z={e.pos.z:0.0} {e.note}");
            SaveImage(area, diagonal);

            Assert.AreEqual(0, counts[Kind.Broken], "数がこわれない（NaN）");
            Assert.AreEqual(0, counts[Kind.Under], "地面の下へぬけない");
            Assert.AreEqual(0, counts[Kind.Trapped], "どの向きにも動けない所（とじこめ）がない：" +
                string.Join(" / ", _events.Where(e => e.kind == Kind.Trapped).Select(e => $"({e.pos.x:0},{e.pos.z:0}) {e.note}")));
            Assert.Greater(_covered, 0.6f * _lineLength, "網の目の線の、多くを歩ける");
        }

        /// <summary>線を遊べる範囲（の中の陸）に切りつめる。</summary>
        static void ClipToPlay(AreaLayout area, Vector2 a, Vector2 b, out Vector2 ca, out Vector2 cb)
        {
            float len = Vector2.Distance(a, b);
            Vector2 d = (b - a) / len;
            float s0 = -1f, s1 = -1f;
            for (float s = 0f; s <= len; s += 1f)
            {
                Vector2 p = a + d * s;
                if (!area.InPlayArea(new Vector3(p.x, area.Height(p.x, p.y) + 0.2f, p.y))) continue;
                if (s0 < 0f) s0 = s;
                s1 = s;
            }
            ca = s0 < 0f ? a : a + d * s0;
            cb = s0 < 0f ? a : a + d * s1;
        }

        IEnumerator WalkLine(AreaLayout area, Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            Vector2 dir = (b - a) / len;
            _lineLength += len;
            float s = 0f;
            _lastYaw = 999f;
            while (s < len && !TryPlace(area, a + dir * s, dir)) s += 1f;
            Skip(a, a + dir * Mathf.Min(s, len));
            if (s >= len) yield break;
            yield return null;
            yield return null;

            float progS = s, progT = Time.time, lineStart = Time.time;
            Vector3 last = Worm.CenterPosition;
            float lastGood = s;
            int frame = 0;
            float cramped = 0f;
            while (true)
            {
                Vector3 c = Worm.CenterPosition;
                Vector2 cxz = new Vector2(c.x, c.z);
                float cs = Vector2.Dot(cxz - a, dir);
                float lat = dir.x * (cxz.y - a.y) - dir.y * (cxz.x - a.x);
                if (cs >= len - 0.6f) break;
                // トンネルのそばは、通らずに先へ
                if (NearGate(area, a + dir * Mathf.Min(len, Mathf.Max(cs, progS) + 1.5f)))
                {
                    ResetInput();
                    if (!Respawn(area, a, dir, len, ref s, Mathf.Max(cs, progS) + 1.5f)) break;
                    progS = s; progT = Time.time;
                    last = Worm.CenterPosition;
                    continue;
                }

                // 線の少し先へ向かって、画面スティックを前へ（はやく）。
                // 大きく向きを変えるときは、人と同じように一度スティックをはなす（倒したままだと、面をまたいだときの「まっすぐ進みつづける」が続く）
                Vector2 target = a + dir * Mathf.Min(len, Mathf.Max(cs, progS) + Lookahead);
                Vector2 to = target - cxz;
                float yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                bool regrip = Mathf.Abs(Mathf.DeltaAngle(yaw, _lastYaw)) > 45f;
                _lastYaw = yaw;
                Cam.yaw = yaw;
                Cam.pitch = 22f;
                GameInput.VirtualMove = regrip ? Vector2.zero : Vector2.up;
                GameInput.VirtualSprint = !regrip;
                yield return null;
                frame++;
                Assert.AreSame(area, Areas.Current, "歩いているうちに、ほかのエリアへ移らない");

                c = Worm.CenterPosition;
                if (!ShakuPhysics.IsFinite(c))
                {
                    _events.Add(new Event { kind = Kind.Broken, pos = last });
                    if (!Respawn(area, a, dir, len, ref s, lastGood + 2f)) break;
                    progS = s; progT = Time.time;
                    continue;
                }
                float ground = area.Height(c.x, c.z);
                if (c.y < ground - 1.5f && area.InPlayArea(new Vector3(c.x, ground, c.z)) && Worm.State != InchwormController.Mode.Fall)
                {
                    _events.Add(new Event { kind = Kind.Under, pos = c, note = $"地面 {ground:0.0} " + Around(c) });
                    if (!Respawn(area, a, dir, len, ref s, Vector2.Dot(new Vector2(c.x, c.z) - a, dir) + 2f)) break;
                    progS = s; progT = Time.time;
                    continue;
                }
                if (Vector3.Distance(c, last) > 0.25f)
                {
                    _walked += Vector3.Distance(c, last);
                    _trail.Add(c);
                    last = c;
                }
                // カメラが体に近づきすぎる（まわりがせまくて、見えにくい）
                if (frame % 3 == 0)
                {
                    float camDist = Vector3.Distance(Cam.transform.position, Worm.HeadPosition);
                    cramped = camDist < 1.1f ? cramped + 3f / 30f : 0f;
                    if (cramped > 3f)
                    {
                        _events.Add(new Event { kind = Kind.Cramped, pos = c, note = $"{camDist:0.00}" });
                        cramped = -999f;
                    }
                }

                cs = Vector2.Dot(new Vector2(c.x, c.z) - a, dir);
                lat = dir.x * (c.z - a.y) - dir.y * (c.x - a.x);
                if (cs > progS + 0.3f)
                {
                    _covered += Mathf.Min(cs - progS, 3f);
                    progS = cs;
                    progT = Time.time;
                    if (c.y - ground < 1.2f) lastGood = cs;
                }
                else if (Time.time - progT > StallTime || Mathf.Abs(lat) > 6f)
                {
                    var ev = Classify(area, c, dir);
                    ev.note += " " + Around(c);
                    if (ev.kind == Kind.Stuck)
                    {
                        bool trapped = false;
                        yield return CheckTrapped(r => trapped = r);
                        if (trapped) ev.kind = Kind.Trapped;
                    }
                    _events.Add(ev);
                    float from = Mathf.Max(progS, cs);
                    if (!Respawn(area, a, dir, len, ref s, from + 1.5f)) break;
                    progS = s; progT = Time.time;
                    last = Worm.CenterPosition;
                }
                if (Time.time - lineStart > len * 3f + 30f) break;   // 念のため
            }
            ResetInput();
        }

        bool Respawn(AreaLayout area, Vector2 a, Vector2 dir, float len, ref float s, float from)
        {
            _lastYaw = 999f;   // 置きなおしたら、スティックをいちどはなす
            float ns = Mathf.Max(from, 0f);
            while (ns < len && !TryPlace(area, a + dir * ns, dir)) ns += 1f;
            Skip(a + dir * Mathf.Max(0f, from - 1.5f), a + dir * Mathf.Min(ns, len));
            s = ns;
            return ns < len;
        }

        void Skip(Vector2 p, Vector2 q)
        {
            float d = Vector2.Distance(p, q);
            if (d < 0.01f) return;
            _skipped += d;
            _skips.Add((p, q));
        }

        Event Classify(AreaLayout area, Vector3 c, Vector2 dir)
        {
            float ground = area.Height(c.x, c.z);
            string reason = Worm.IsBlocked ? Worm.BlockReason : "";
            var ev = new Event { pos = c, note = reason };
            if (Worm.SurfaceUp.y < 0.5f || c.y - ground > 1.2f) { ev.kind = Kind.Climb; return ev; }
            switch (reason)
            {
                case "water": ev.kind = Kind.Water; return ev;
                case "overhang": ev.kind = Kind.Overhang; return ev;
                case "outside": ev.kind = Kind.Outside; return ev;
                case "":
                    ev.kind = Kind.Stuck;
                    return ev;
            }
            // がけ・足場がないと言って止まったのに、前が平らな陸で、何もない → 見えないかべ
            Vector2 ahead = new Vector2(c.x, c.z) + dir * 0.9f;
            float gh = area.Height(ahead.x, ahead.y);
            bool flatLand = Mathf.Abs(gh - ground) < 0.35f && area.IsLand(ahead.x, ahead.y, 0.05f)
                            && !area.IsUnderwater(new Vector3(ahead.x, gh + 0.05f, ahead.y));
            bool somethingThere = Physics.CheckSphere(new Vector3(ahead.x, gh + 0.4f, ahead.y), 0.3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
            bool groundUnder = Physics.Raycast(new Vector3(ahead.x, gh + 0.6f, ahead.y), Vector3.down, 1.2f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
            ev.kind = flatLand && !somethingThere && groundUnder ? Kind.InvisibleWall : Kind.Edge;
            return ev;
        }

        /// <summary>しゃくとりむしの状態と、近くの物の名前（なぜ止まったかを調べる手がかり）。</summary>
        static string Around(Vector3 c)
        {
            var names = Physics.OverlapSphere(c, 1.1f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                .OrderBy(k => Vector3.Distance(k.ClosestPoint(c), c))
                .Select(k => k.transform.parent != null ? k.transform.parent.name + "/" + k.name : k.name)
                .Distinct().Take(4);
            return $"[{Worm.State} up={Worm.SurfaceUp.y:0.00} rest={Worm.IsResting} fat={Worm.Fatigue:0.00} peek={Worm.IsPeeking} in={Worm.InputEnabled} step={Worm.LastStepDuration:0.00} head={Worm.Heading.x:0.0},{Worm.Heading.z:0.0} yaw={Cam.yaw:0} {string.Join(", ", names)}]";
        }

        /// <summary>
        /// 引っかかったら、後ろ・左右・前へ少しずつ動いてみる。だめなら、少し待ってから 8 方向へ長めに。
        /// それでもどの向きにも動けなければ、とじこめ（小石のあいだに一時はさまっただけなら、ここで抜けられる）。
        /// </summary>
        static IEnumerator CheckTrapped(Action<bool> result)
        {
            Vector3 start = Worm.CenterPosition;
            float baseYaw = Cam.yaw, best = 0f;
            foreach (var (offs, time) in new[] { (new[] { 180f, 90f, -90f, 0f }, 1.6f), (new[] { 135f, 225f, 45f, 315f, 180f, 90f, 270f, 0f }, 2.5f) })
            {
                if (best > 0.5f) break;
                foreach (float off in offs)
                {
                    // 向きを変えるたびに、人と同じように一度スティックをはなす
                    ResetInput();
                    yield return null;
                    yield return null;
                    Cam.yaw = baseYaw + off;
                    GameInput.VirtualMove = Vector2.up;
                    float t = 0f;
                    while (t < time)
                    {
                        yield return null;
                        t += Time.deltaTime;
                        Vector3 p = Worm.CenterPosition;
                        if (ShakuPhysics.IsFinite(p)) best = Mathf.Max(best, Vector3.Distance(start, p));
                    }
                    if (best > 0.5f) break;
                }
                ResetInput();
                if (best <= 0.5f)
                {
                    float w = 0f;
                    while (w < 1f) { yield return null; w += Time.deltaTime; }
                }
            }
            ResetInput();
            result(best < 0.5f);
        }

        /// <summary>ほかのエリアへのトンネル・とびらのそば（入ると、エリアを移ってしまう）。</summary>
        static bool NearGate(AreaLayout area, Vector2 xz)
        {
            foreach (var g in area.Gates)
            {
                if (Vector2.Distance(xz, g.position) < 9f) return true;
                if (!g.door) continue;
                // とびらは、はばが広いので、とびらの前の四角（ゲームがヒントを出す所より少し広く）
                Vector2 rel = xz - g.position, w = g.walk.normalized;
                float along = Vector2.Dot(rel, w), side = Mathf.Abs(rel.x * -w.y + rel.y * w.x);
                if (along > -9f && side < g.halfWidth + 3f) return true;
            }
            return false;
        }

        /// <summary>xz に、しゃくとりむしを置ける（陸で、上がつかえず、物の中ではない）なら置く。</summary>
        static bool TryPlace(AreaLayout area, Vector2 xz, Vector2 dir)
        {
            if (NearGate(area, xz)) return false;
            float g = area.Height(xz.x, xz.y);
            if (!area.InPlayArea(new Vector3(xz.x, g + 0.2f, xz.y))) return false;
            foreach (float from in new[] { 30f, 1.6f })
            {
                if (!Physics.Raycast(new Vector3(xz.x, g + from, xz.y), Vector3.down, out var hit, from + 1f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y - g > 2f || hit.normal.y < 0.6f) continue;
                if (area.IsUnderwater(hit.point + Vector3.up * 0.1f)) return false;
                if (Inside(hit.point)) continue;
                Worm.Spawn(hit.point, new Vector3(dir.x, 0f, dir.y));
                return true;
            }
            return false;
        }

        /// <summary>物の中か（上へ光線を出して、物の裏がわに当たる・すぐ上がつかえる）。</summary>
        static bool Inside(Vector3 p)
        {
            if (Physics.CheckSphere(p + Vector3.up * 0.35f, 0.18f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return true;
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            bool inside = Physics.Raycast(p + Vector3.up * 0.05f, Vector3.up, out var h, 60f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                          && (h.normal.y > 0.2f || h.distance < 0.8f);
            Physics.queriesHitBackfaces = prev;
            return inside;
        }

        // ------------------------------------------------------------------
        // 上から見た絵
        // ------------------------------------------------------------------
        void SaveImage(AreaLayout area, bool diagonal)
        {
            string dir = Environment.GetEnvironmentVariable("SHAKU_GRID_OUT");
            if (string.IsNullOrEmpty(dir)) return;
            const int N = 512;
            float ext = area.MapExtent;
            var px = new Color32[N * N];
            float lo = float.MaxValue, hi = float.MinValue;
            var hs = new float[N * N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = -ext + (i + 0.5f) * 2f * ext / N, z = -ext + (j + 0.5f) * 2f * ext / N;
                    float h = area.Height(x, z);
                    hs[j * N + i] = h;
                    if (area.InPlayArea(new Vector3(x, h + 0.2f, z))) { lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
                }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = -ext + (i + 0.5f) * 2f * ext / N, z = -ext + (j + 0.5f) * 2f * ext / N;
                    float h = hs[j * N + i];
                    Color col = Color.Lerp(new Color(0.42f, 0.5f, 0.36f), new Color(0.93f, 0.9f, 0.8f), Mathf.InverseLerp(lo, hi, h));
                    if (!area.IsLand(x, z, 0.02f) || area.IsUnderwater(new Vector3(x, h + 0.05f, z))) col = new Color(0.45f, 0.65f, 0.9f);
                    // 物（地面より上に面がある所）は、こい茶色をまぜる
                    int bi = i & ~1, bj = j & ~1;
                    float bx = -ext + (bi + 1f) * 2f * ext / N, bz = -ext + (bj + 1f) * 2f * ext / N;
                    if (Physics.Raycast(new Vector3(bx, area.Height(bx, bz) + 40f, bz), Vector3.down, out var hit, 41f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                        && hit.point.y - area.Height(bx, bz) > 0.6f)
                        col = Color.Lerp(col, new Color(0.35f, 0.24f, 0.14f), 0.55f);
                    if (!area.InPlayArea(new Vector3(x, h + 0.2f, z))) col *= 0.45f;
                    col.a = 1f;
                    px[j * N + i] = col;
                }
            Vector2Int P(Vector3 w) => new Vector2Int(Mathf.RoundToInt((w.x + ext) / (2f * ext) * N), Mathf.RoundToInt((w.z + ext) / (2f * ext) * N));
            void Dot(Vector2Int q, Color32 c, int r)
            {
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = q.x + dx, y = q.y + dy;
                        if (x >= 0 && y >= 0 && x < N && y < N) px[y * N + x] = c;
                    }
            }
            foreach (var (p, q) in _skips)
            {
                float d = Vector2.Distance(p, q);
                for (float t = 0f; t <= d; t += 0.4f)
                {
                    Vector2 m = Vector2.Lerp(p, q, d < 0.01f ? 0f : t / d);
                    Dot(P(new Vector3(m.x, 0f, m.y)), new Color32(150, 150, 150, 255), 0);
                }
            }
            foreach (var t in _trail) Dot(P(t), new Color32(30, 150, 40, 255), 1);
            foreach (var e in _events)
            {
                Color32 c;
                switch (e.kind)
                {
                    case Kind.Water: c = new Color32(20, 60, 200, 255); break;
                    case Kind.Edge: c = new Color32(240, 150, 30, 255); break;
                    case Kind.Climb: c = new Color32(140, 90, 40, 255); break;
                    case Kind.Fall: c = new Color32(200, 60, 200, 255); break;
                    case Kind.Splash: c = new Color32(100, 200, 255, 255); break;
                    case Kind.Cramped: c = new Color32(255, 230, 0, 255); break;
                    case Kind.Overhang:
                    case Kind.Outside: c = new Color32(90, 90, 90, 255); break;
                    default: c = new Color32(230, 20, 20, 255); break;   // 見えないかべ・引っかかり・とじこめ・地面の下
                }
                Dot(P(e.pos), c, e.kind == Kind.Trapped || e.kind == Kind.Under || e.kind == Kind.InvisibleWall ? 5 : 3);
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"grid_{area.Id}{(diagonal ? "_diag" : "")}.png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}

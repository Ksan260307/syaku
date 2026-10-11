using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：糸でいろいろ動きまわっても、エラーが出ない（ねらって発射・カメラでねらう・たぐる・とちゅうで切る・こぐ・のぼる・
    /// 下りる・切ってから糸でつかまる・ぶら下がったまま発射・ふちから糸を出して下りる）。
    /// WebGL では、null の参照や こわれた数（NaN）でゲームが止まってしまうことがあるので、エラーも NaN も出ないことをたしかめる。
    /// </summary>
    public class SilkRobustnessPlayTests
    {
        readonly List<string> _errors = new List<string>();

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                _errors.Add($"[{type}] {msg}\n{stack}");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = false;
            ResetInput();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator SilkEverywhere_NoErrorsAndNoBrokenNumbers()
        {
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (b - a) * (float)rnd.NextDouble();
            string areas = "forest,mountain";
            const int per = 15;
            foreach (var id in areas.Split(','))
            {
                var area = Areas.Get(id);
                if (Areas.Current != area)
                {
                    SaveSystem.Data.visited.Add(id);
                    GM.TravelTo(id);
                    yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, id);
                    yield return Seconds(0.5f);
                }
                int fired = 0, drops = 0;
                for (int it = 0; it < per; it++)
                {
                    // ときどき、別の場所へ（landmark のそば・いきもののそば）
                    if (it % 5 == 0)
                    {
                        Vector3 at;
                        if (rnd.NextDouble() < 0.5 && area.Landmarks.Count > 0)
                        {
                            var lm = area.Landmarks[rnd.Next(area.Landmarks.Count)];
                            at = TopSurface(lm.position + new Vector2(R(-3f, 3f), R(-3f, 3f)));
                        }
                        else
                        {
                            var sp = SpeciesCatalog.All[rnd.Next(SpeciesCatalog.All.Count)];
                            Vector3 c = GM.creatures.PositionOf(sp.id);
                            if (c == Vector3.zero) continue;
                            at = TopSurface(new Vector2(c.x + R(-2f, 2f), c.z + R(-2f, 2f)));
                        }
                        if (!area.InPlayArea(at) || area.IsUnderwater(at)) continue;
                        Worm.Spawn(at, Quaternion.AngleAxis(R(0f, 360f), Vector3.up) * Vector3.forward);
                        yield return Seconds(0.3f);
                    }
                    // ときどき、ふちまで歩いて、糸を出して下りる（崖・キノコのかさ・切り株のふち）
                    if (rnd.NextDouble() < 0.35)
                    {
                        Vector2 dir2 = new Vector2(R(-1f, 1f), R(-1f, 1f)).normalized;
                        GameInput.VirtualMove = dir2;
                        for (float t = 0f; t < 4f && !Worm.CanDropSilk; t += Time.deltaTime) yield return null;
                        GameInput.VirtualMove = Vector2.zero;
                        if (Worm.CanDropSilk)
                        {
                            drops++;
                            GameInput.SetVirtualSilk(true);
                            yield return null;
                            GameInput.SetVirtualSilk(false);
                            int dm = rnd.Next(4);
                            float dl = R(0.5f, 6f);
                            for (float t = 0f; t < dl; t += Time.deltaTime)
                            {
                                GameInput.VirtualMove = dm >= 1 ? new Vector2(Mathf.Sin(t * 2f), Mathf.Cos(t * 3f)) : Vector2.zero;
                                GameInput.VirtualSprint = dm == 2;
                                GameInput.SetVirtualSilk(dm == 3 && t > 1f);
                                GameInput.VirtualStand = dm == 1 && t > dl * 0.8f;
                                yield return null;
                            }
                            ResetInput();
                        }
                    }
                    // いろいろな向きへ、糸を発射できる所をさがす
                    Vector3 head = Worm.HeadPosition;
                    bool ok = false;
                    for (int tries = 0; tries < 30 && !ok; tries++)
                    {
                        Vector3 dir = Quaternion.Euler(R(-75f, 15f), R(0f, 360f), 0f) * Vector3.forward;
                        if (Physics.Raycast(head + Vector3.up * 0.2f, dir, out var hit, Worm.silkRange, ShakuConst.WalkableMask, QueryTriggerInteraction.Ignore))
                            ok = Worm.AimAt(hit.point);
                    }
                    if (!ok) continue;
                    if (rnd.NextDouble() < 0.4)
                    {
                        // 本当のねらい方：カメラをねらう方へ向けて、ねらうボタンを押してはなす
                        Vector3 to = Worm.AimPoint - Worm.HeadPosition;
                        Cam.yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + R(-8f, 8f);
                        Cam.pitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg + R(-6f, 6f), -40f, 70f);
                        GameInput.VirtualAim = true;
                        for (float t = 0f; t < 0.3f; t += Time.deltaTime) yield return null;
                        GameInput.VirtualAim = false;
                        yield return null;
                        if (Worm.State != InchwormController.Mode.Hang) continue;
                    }
                    else Assert.IsTrue(Worm.FireSilk());
                    fired++;
                    // 発射したあと：たぐる・とちゅうで切る・ぶら下がってこぐ・のぼる・下りる・切ってから糸でつかまる・また発射
                    float life = R(0.3f, 5f);
                    int mode = rnd.Next(8);
                    bool chained = false;
                    for (float t = 0f; t < life; t += Time.deltaTime)
                    {
                        GameInput.VirtualMove = mode >= 2 ? new Vector2(Mathf.Sin(t * 3f), Mathf.Cos(t * 2f)) : Vector2.zero;
                        GameInput.VirtualSprint = mode == 3 || mode == 7;
                        bool silkDown = mode == 1 && t > 0.2f && t < 0.3f || mode == 4 && t > 1f
                                        || mode == 5 && (t > 1.2f && t < 1.3f || t > 1.6f && t < 1.7f);
                        GameInput.SetVirtualSilk(silkDown);
                        GameInput.VirtualStand = mode == 2 && t > life * 0.7f || mode == 5 && t > 1f && t < 1.1f;
                        if (mode == 6 && !chained && t > 1f && Worm.State == InchwormController.Mode.Hang)
                        {
                            // ぶら下がったまま、べつの所へ発射
                            chained = true;
                            Vector3 hp = Worm.HeadPosition;
                            for (int tries = 0; tries < 20; tries++)
                            {
                                Vector3 dir = Quaternion.Euler(R(-75f, 15f), R(0f, 360f), 0f) * Vector3.forward;
                                if (Physics.Raycast(hp + Vector3.up * 0.2f, dir, out var hit2, Worm.silkRange, ShakuConst.WalkableMask, QueryTriggerInteraction.Ignore)
                                    && Worm.AimAt(hit2.point)) { Worm.FireSilk(); break; }
                            }
                        }
                        yield return null;
                        Vector3 h2 = Worm.HeadPosition;
                        if (!(float.IsFinite(h2.x) && float.IsFinite(h2.y) && float.IsFinite(h2.z))) _errors.Add($"NaN head in {id} it={it} mode={mode} state={Worm.State}");
                    }
                    ResetInput();
                    // いきものの位置も、こわれた数になっていない（ぶつかって押されたいきものも）
                    foreach (var sp in SpeciesCatalog.All)
                        for (int k = 0; k < GM.creatures.CountOf(sp.id); k++)
                        {
                            Vector3 cp = GM.creatures.Info(sp.id, k).pos;
                            if (!(float.IsFinite(cp.x) && float.IsFinite(cp.y) && float.IsFinite(cp.z))) _errors.Add($"NaN {sp.id} in {id} it={it}");
                        }
                    yield return null;
                }
                Assert.Greater(fired, 5, id + " で糸を発射できた");
                // 落ちつくまで待つ（落ちている途中ならリスポーンするまで）
                yield return Seconds(2f);
            }
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(0, _errors.Count, _errors.Count > 0 ? _errors[0] : "");
        }
    }
}

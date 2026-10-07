using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：軽量化。いきものの間引き・近くさがしの格子・考えごとの上限・画面の細かさ。</summary>
    public class LightweightPlayTests
    {
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
            ResetInput();
            Creatures.ThinksPerFrame = 6;
        }

        /// <summary>何フレームに 1 回動かすかは、しゃくとりむしからの距離で決まり、カメラの向きでは変わらない。</summary>
        void CheckIntervals(string label)
        {
            var cr = GM.creatures;
            Vector3 head = Worm.HeadPosition;
            for (int i = 0; i < cr.MobCount; i++)
            {
                float d = Vector3.Distance(cr.PositionAt(i), head);
                int k = cr.IntervalAt(i);
                Assert.That(k, Is.InRange(1, 6), $"{label}：{i} 番の間引きは 1〜6 フレーム");
                // 境目のすぐ近くは、動いた分で入れかわるので見ない
                if (d < Creatures.NearRange - 0.5f) Assert.AreEqual(1, k, $"{label}：しゃくとりむしの近く（{d:F1} m）は毎フレーム動く");
                else if (d > Creatures.NearRange + 0.5f) Assert.GreaterOrEqual(k, 2, $"{label}：はなれたもの（{d:F1} m）は間引く");
                if (d > 26f) Assert.GreaterOrEqual(k, 3, $"{label}：遠いもの（{d:F1} m）は、もっと間引く");
            }
        }

        [UnityTest]
        public IEnumerator UpdateInterval_FollowsWormDistance_NotCamera()
        {
            var cr = GM.creatures;
            Assert.Greater(cr.MobCount, 10, "いきものがいる");
            // いきもののすぐそばに置く
            Vector3 ant = cr.PositionOf("ant", 0);
            Place(new Vector2(ant.x, ant.z), Vector3.forward);
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                yield return FaceCamera(dir);
                for (int f = 0; f < 5; f++)
                {
                    yield return null;
                    CheckIntervals($"カメラ {dir}");
                }
            }
        }

        [UnityTest]
        public IEnumerator SkippedUpdates_KeepEveryCreaturesClock()
        {
            // 間引いて動かしても、時間はためておいてまとめて進めるので、だれの時間もおくれない
            var cr = GM.creatures;
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Frames(3);
            int n = cr.MobCount;
            var start = new float[n];
            for (int i = 0; i < n; i++) start[i] = cr.ClockAt(i);
            yield return Seconds(2f);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float adv = cr.ClockAt(i) - start[i];
                lo = Mathf.Min(lo, adv);
                hi = Mathf.Max(hi, adv);
            }
            Assert.Greater(lo, 1.5f, "時間が進んでいる");
            Assert.Less(hi - lo, 1e-3f, "近くのものも遠くのものも、同じだけ時間が進む");
        }

        [UnityTest]
        public IEnumerator NeighbourGrid_MatchesFullSearch()
        {
            var cr = GM.creatures;
            yield return Frames(2);
            var rng = new System.Random(7);
            int checks = 0;
            for (int i = 0; i < cr.MobCount; i++)
            {
                foreach (float r in new[] { 0.5f, 1.5f, 4f, 9f, 20f })
                {
                    Vector3 p = cr.PositionAt(i) + new Vector3((float)rng.NextDouble() * 6f - 3f, 0f, (float)rng.NextDouble() * 6f - 3f);
                    var (grid, full) = cr.CountWithin(p, r);
                    Assert.AreEqual(full, grid, $"格子でさがしても、全部を見たときと同じ（半径 {r}）");
                    checks++;
                }
            }
            // ますの境目・遠い所・マイナスの座標
            foreach (var p in new[] { Vector3.zero, new Vector3(-4f, 0f, -4f), new Vector3(3.999f, 0f, -0.001f), new Vector3(-120f, 0f, 90f) })
            {
                var (grid, full) = cr.CountWithin(p, 30f);
                Assert.AreEqual(full, grid);
                checks++;
            }
            Assert.Greater(checks, 50);
        }

        static void CheckContactPair(string species)
        {
            var (order, momentum, bounced) = GM.creatures.CheckContacts(species, 0.6f);
            Assert.IsFalse(float.IsNaN(order), $"{species} の群れがある");
            Assert.Less(order, 1e-5f, $"{species}：並べる順番を入れかえても、ぶつかった結果は同じ");
            Assert.Less(momentum, 1e-4f, $"{species}：運動量は保たれる");
            Assert.IsTrue(bounced, $"{species}：向かい合ってぶつかると、はね返る");
        }

        [UnityTest]
        public IEnumerator Contacts_SameResultInAnyOrder()
        {
            yield return Frames(2);
            CheckContactPair("pillbug");
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            yield return Frames(2);
            CheckContactPair("waterstrider");
        }

        [UnityTest]
        public IEnumerator DecisionCap_SpreadsThinkingOverFrames()
        {
            var cr = GM.creatures;
            Creatures.ThinksPerFrame = 1;
            Place(ForestLayout.Meadow, Vector3.forward);
            yield return Frames(2);
            // みんな、いっせいに次の行き先を考えたくなる
            cr.ExpireAllTimers();
            int total = 0, framesWithThinking = 0;
            for (int f = 0; f < 90; f++)
            {
                yield return null;
                int t = cr.ThinksLastFrame;
                Assert.LessOrEqual(t, 1, "1 フレームで考えるのは、上限まで");
                total += t;
                if (t > 0) framesWithThinking++;
            }
            Assert.GreaterOrEqual(total, 3, "上限をこえた分は、次のフレームで考える");
            Assert.GreaterOrEqual(framesWithThinking, 3, "考えごとが、何フレームかに分かれる");
        }

        [UnityTest]
        public IEnumerator RenderScale_OnlyChangesThePicture()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(urp);
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(1f);
            float baseScale = GM.RenderScaleNow;
            Vector3 wormPos = Worm.CenterPosition;
            float timeScale = Time.timeScale, fixedDt = Time.fixedDeltaTime;
            int target = Application.targetFrameRate;
            try
            {
                // 重いときは、1 段ずつ下げ、下限より下げない
                for (int i = 0; i < 10; i++) GM.StepRenderScale(40f);
                Assert.AreEqual(GameManager.MinRenderScale, GM.RenderScaleNow, 1e-4f, "下限まで下がる");
                Assert.AreEqual(GM.RenderScaleNow, urp.renderScale, 1e-4f, "画面の細かさに反映");
                // 間の重さでは、そのまま
                GM.StepRenderScale(20f);
                Assert.AreEqual(GameManager.MinRenderScale, GM.RenderScaleNow, 1e-4f);
                // 軽くなると、もとの細かさまで戻り、それより上げない
                for (int i = 0; i < 10; i++) GM.StepRenderScale(10f);
                Assert.AreEqual(baseScale, GM.RenderScaleNow, 1e-4f, "もとの細かさに戻る");
                // ゲームの中身は何も変わらない
                Assert.AreEqual(timeScale, Time.timeScale);
                Assert.AreEqual(fixedDt, Time.fixedDeltaTime);
                Assert.AreEqual(target, Application.targetFrameRate);
                Assert.Less(Vector3.Distance(wormPos, Worm.CenterPosition), 1e-4f, "しゃくとりむしは動かない");
            }
            finally
            {
                GM.ApplyQuality(SaveSystem.Settings.quality);
            }
            yield return null;
        }
    }
}

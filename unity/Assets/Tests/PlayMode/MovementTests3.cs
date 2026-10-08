using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：いきもののモーション 100（森）。ひとりでする動き・カマキリとチョウ・カラスの向きの変え方・だんごむし・かたつむり・モグラ。</summary>
    public class MobMotionTests3
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        Creatures C => GM.creatures;

        [UnityTest]
        public IEnumerator IdleActs_HappenWhenStill_AndStopWhenTheWormComes()
        {
            // 止まっているいきものは、ときどき、ひとりでする動き（手入れ・のび・ぶるっ など）をする
            var seen = new HashSet<string>();
            int pick = -1;
            for (float t = 0f; t < 25f && (seen.Count < 3 || pick < 0); t += Time.deltaTime)
            {
                yield return null;
                for (int i = 0; i < C.MobCount; i++)
                {
                    if (C.ActAt(i) == 0) continue;
                    seen.Add(C.SpeciesAt(i));
                    if (pick < 0 && C.ActLeftAt(i) > 1.1f && C.SpeciesAt(i) != "frog" && C.SpeciesAt(i) != "ant_helmet") pick = i;
                }
            }
            Assert.GreaterOrEqual(seen.Count, 3, "いろいろな種類が、ひとりでする動きをする: " + string.Join(",", seen));
            Assert.GreaterOrEqual(pick, 0, "しばらく続く動きがある");
            // しゃくとりむしがすぐそばに来ると、すぐにやめる
            Worm.Spawn(C.PositionAt(pick) + new Vector3(0.8f, 0.05f, 0f), Vector3.left);
            yield return WaitUntil(() => C.ActAt(pick) == 0, 0.5f, "しゃくとりむしが近づくと、やめる");
        }

        [UnityTest]
        public IEnumerator Mantis_RaisesItsArmsAtAButterfly()
        {
            Vector3 mp = C.PositionOf("mantis");
            Place(new Vector2(mp.x + 12f, mp.z), Vector3.left);
            yield return Seconds(0.5f);
            // チョウが目の前を飛ぶと、かまをかまえる（しゃくとりむしは遠い）
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                C.SetPosition("butterfly", 0, mp + Vector3.up * 1.2f + C.Info("mantis").fwd * 0.8f);
                yield return null;
            }
            Assert.IsTrue(C.IsAirborne("butterfly", 0), "チョウは飛んでいる");
            Assert.Greater(C.Info("mantis").raise, 0.5f, "近くを飛ぶチョウにも、かまをかまえる");
            Assert.AreEqual(0f, C.Info("mantis").curSpeed, 1e-3f, "じっと待つ");
        }

        [UnityTest]
        public IEnumerator Crow_TurnsSmoothlyOnTheGround()
        {
            Vector3 c = C.PositionOf("crow");
            Place(new Vector2(c.x + 20f, c.z), Vector3.left);
            Vector3 last = C.Info("crow").fwd;
            bool wasAir = true;
            float maxRate = 0f, turned = 0f, since = 0f;
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                yield return null;
                var info = C.Info("crow");
                float a = Vector3.Angle(last, info.fwd);
                // 遠くのいきものは何フレームかに 1 回動くので、前に向きが変わってからの時間で割る
                since += Time.deltaTime;
                if (a > 0.01f)
                {
                    if (!info.airborne && !wasAir) maxRate = Mathf.Max(maxRate, a / since);
                    if (!info.airborne && !wasAir) turned += a;
                    since = 0f;
                }
                last = info.fwd;
                wasAir = info.airborne;
            }
            Assert.Greater(turned, 20f, "地上で向きを変えている");
            Assert.Less(maxRate, 1200f, "カラスは、一度に向きを変えず、なめらかに回る");
        }

        [UnityTest]
        public IEnumerator Pillbug_ShrinksBeforeItBecomesABall()
        {
            Vector3 p = C.PositionOf("pillbug", 0);
            Worm.Spawn(p + new Vector3(1.0f, 0.05f, 0f), Vector3.left);
            yield return WaitUntil(() => C.CurledOf("pillbug", 0) > 0f, 1f, "まるくなる");
            int between = 0;
            yield return WaitUntil(() =>
            {
                float k = C.Info("pillbug", 0).curlK;
                if (k > 0f && k < 1f) between++;
                return k >= 1f;
            }, 1.5f, "玉になる");
            Assert.Greater(between, 0, "体をちぢめてから、くるんと玉になる（いきなり玉にはならない）");
        }

        [UnityTest]
        public IEnumerator Snail_ShuddersOnceWhenItHides()
        {
            Vector3 p = C.PositionOf("snail", 0);
            C.Disturb(p, 0.5f);
            Assert.Greater(C.Info("snail", 0).retreat, 0f, "殻にひっこむ");
            Assert.Greater(C.Info("snail", 0).flinch, 0.9f, "ひっこむ瞬間に、ぶるっとふるえる");
            yield return Seconds(0.6f);
            Assert.Less(C.Info("snail", 0).flinch, 0.05f, "ふるえは、すぐにおさまる");
            C.Disturb(p, 0.5f);
            Assert.Less(C.Info("snail", 0).flinch, 0.05f, "ひっこんでいる間は、もうふるえない");
        }

        [UnityTest]
        public IEnumerator Mole_SniffsWhenItPeeksOut()
        {
            Vector3 hill = C.PositionOf("mogura");
            Worm.Spawn(TopSurface(new Vector2(hill.x + 9f, hill.z)), Vector3.left);
            yield return WaitUntil(() => C.DigOf("mogura") < 0.02f, 20f, "顔を出す");
            float lo = 0f, hi = 0f;
            for (float t = 0f; t < 1f && C.DigOf("mogura") < 0.02f; t += Time.deltaTime)
            {
                yield return null;
                var info = C.Info("mogura");
                Vector3 bf = -(Vector3)C.BodyMatrixOf("mogura").GetColumn(2);
                float a = Vector3.SignedAngle(Vector3.ProjectOnPlane(info.fwd, Vector3.up), Vector3.ProjectOnPlane(bf, Vector3.up), Vector3.up);
                lo = Mathf.Min(lo, a);
                hi = Mathf.Max(hi, a);
            }
            Assert.Greater(hi - lo, 4f, "顔を出しているとき、鼻をくんくん左右にふる");
        }
    }

    /// <summary>総合テスト：いきもののモーション 100（公園）。トカゲの走り方・カミキリとカメムシのおどろき方。</summary>
    public class ParkMotionTests3
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
            SaveSystem.Data.visited.Add("park");
            GM.TravelTo("park");
            yield return WaitUntil(() => Areas.Current == Areas.Park && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, "公園へ");
            yield return Seconds(0.5f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        Creatures C => GM.creatures;

        [UnityTest]
        public IEnumerator Tokage_BendsItsBodyWhenRunning_AndBugsReactWhenStartled()
        {
            // トカゲは、体をくねらせて走る（体の向きが、進む向きの左右へ交互にふれる）
            float lo = 0f, hi = 0f, nextScare = 0f;
            for (float t = 0f; t < 6f && (hi < 4f || lo > -4f); t += Time.deltaTime)
            {
                if (t >= nextScare)
                {
                    C.Disturb(C.PositionOf("tokage"), 0.3f);
                    nextScare = t + 1f;
                }
                yield return null;
                var info = C.Info("tokage");
                if (info.moveSpeed < 0.5f) continue;
                Vector3 bf = -(Vector3)C.BodyMatrixOf("tokage").GetColumn(2);
                float a = Vector3.SignedAngle(Vector3.ProjectOnPlane(info.fwd, info.up), Vector3.ProjectOnPlane(bf, info.up), info.up);
                lo = Mathf.Min(lo, a);
                hi = Mathf.Max(hi, a);
            }
            Assert.Greater(hi, 4f, "右へくねる");
            Assert.Less(lo, -4f, "左へくねる");

            // カミキリはキイキイ鳴き（頭を上下にふる）、カメムシはにおいを出しておしりを上げる。動いていても、最後までする
            C.Disturb(C.PositionOf("kamikiri"), 0.3f);
            C.Disturb(C.PositionOf("kamemushi"), 0.3f);
            Assert.AreNotEqual(0, C.Info("kamikiri").act, "カミキリがおどろく");
            Assert.AreNotEqual(0, C.Info("kamemushi").act, "カメムシがおどろく");
            yield return Seconds(0.3f);
            Assert.AreNotEqual(0, C.Info("kamemushi").act, "逃げながらも、においを出しつづける");
        }
    }
}

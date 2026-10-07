using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：しゃくとりむしの動きの改善（歩きはじめ・はやく・すき間・足場・小枝のまね・のぞきこみ・糸・落下）。</summary>
    public class WormMovementTests
    {
        readonly List<GameObject> _temp = new List<GameObject>();

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
            foreach (var go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
        }

        /// <summary>しゃくとりむしが這える箱を置く（上の面の中心を返す）。</summary>
        Vector3 Box(Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestBox";
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            return center + Vector3.up * (size.y * 0.5f);
        }

        Vector3 Sky => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.2f, 0f);

        [UnityTest]
        public IEnumerator FirstSteps_AreSlowerThanCruising()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);   // 目をさます伸びが終わるまで
            var durations = new Dictionary<int, float>();
            yield return Hold(Vector2.up, 6f, () =>
            {
                if (Worm.State == InchwormController.Mode.Reach && !durations.ContainsKey(Worm.StepStreak))
                    durations[Worm.StepStreak] = Worm.LastStepDuration;
            });
            Assert.IsTrue(durations.ContainsKey(1) && durations.ContainsKey(5), "何歩も歩いた");
            float cruise = (durations[4] + durations[5]) * 0.5f;
            Assert.Greater(durations[1], cruise * 1.06f, $"歩きはじめの一歩はゆっくり first={durations[1]:0.000} cruise={cruise:0.000}");
        }

        [UnityTest]
        public IEnumerator Sprint_RampsUpOverAFewSteps()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            GameInput.VirtualSprint = true;
            yield return Seconds(0.3f);
            float early = Worm.SprintBlend;
            Assert.Greater(early, 0.05f);
            Assert.Less(early, 0.8f, "はやくは、いきなり最大にならない");
            yield return Seconds(1.2f);
            Assert.AreEqual(1f, Worm.SprintBlend, 0.01f);
            ResetInput();
            yield return Seconds(1f);
            Assert.Less(Worm.SprintBlend, 0.1f, "やめると、もどる");
        }

        [UnityTest]
        public IEnumerator Gap_IsBridgedInsteadOfFallingIn()
        {
            Vector3 c = Sky;
            Vector3 a = Box(c, new Vector3(1.6f, 0.3f, 1.6f));
            Vector3 b = Box(c + new Vector3(0f, 0f, 1.6f + 0.35f), new Vector3(1.6f, 0.3f, 1.6f));
            Worm.Spawn(a + new Vector3(0f, 0f, -0.6f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            bool fell = false;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() =>
            {
                fell |= Worm.IsFalling || Worm.IsAboutToFall;
                return fell || (Worm.TailPoint.z > b.z - 0.7f && Mathf.Abs(Worm.TailPoint.y - b.y) < 0.1f);
            }, 12f, "向こうの台へ渡る");
            ResetInput();
            Assert.IsFalse(fell, "すき間に落ちない");
        }

        [UnityTest]
        public IEnumerator SupportVanishing_MakesTheWormFall()
        {
            Vector3 top = Box(Sky, new Vector3(2f, 0.3f, 2f));
            Worm.Spawn(top, Vector3.forward);
            yield return Seconds(1.2f);
            Assert.AreEqual(InchwormController.Mode.Idle, Worm.State);
            Object.Destroy(_temp[0]);
            yield return null;
            Physics.SyncTransforms();
            yield return WaitUntil(() => Worm.IsFalling, 2f, "足もとがなくなると落ちる");
            yield return WaitUntil(() => !Worm.IsFalling, 6f, "下の地面に着地");
        }

        [UnityTest]
        public IEnumerator LongIdle_BecomesATwig()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(1.2f);
            Worm.DebugSetIdleTime(41f);
            yield return Seconds(1.5f);
            Assert.IsTrue(Worm.IsTwigPose, "40 秒休むと小枝のまね");
            Assert.Greater(Worm.RearAmount, 0.6f, "体を起こしてかたまる");
            Assert.IsFalse(Worm.IsStanding, "背伸び（操作）とはちがう");
            Assert.Greater(Worm.RelaxAmount, 0.1f, "力をぬいている");
            GameInput.VirtualMove = Vector2.up;
            yield return Seconds(0.6f);
            Assert.IsFalse(Worm.IsTwigPose, "動くとやめる");
        }

        [UnityTest]
        public IEnumerator WaterEdge_IsTestedWithTheHead()
        {
            Vector2 p = new Vector2(ForestLayout.Pond.x + 7f, ForestLayout.Pond.y + 17f);
            Place(p, Vector3.back);
            yield return FaceCamera(Vector3.back);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.IsBlocked, 20f, "池のふちで止まる");
            yield return Frames(2);
            Assert.IsTrue(Worm.IsPeeking, "頭を下げて水をたしかめる");
            Assert.AreEqual("water", Worm.PeekKind);
        }

        [UnityTest]
        public IEnumerator CliffEdge_LeansOutBeforeFalling()
        {
            Vector2 s = ForestLayout.Stump;
            Vector3 outward = new Vector3(-1f, 0f, 0.2f).normalized;
            Place(s + new Vector2(outward.x, outward.z) * 5.2f, outward);
            yield return FaceCamera(outward);
            bool leaned = false, fell = false;
            Worm.Fell += () => fell = true;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() =>
            {
                if (!fell && Worm.IsAboutToFall) leaned = true;
                return fell;
            }, 15f, "がけのふちから落ちる");
            Assert.IsTrue(leaned, "落ちる前に身をのり出す");
        }

        [UnityTest]
        public IEnumerator Spawn_WakesUpWithAStretch()
        {
            yield return Seconds(1.5f);   // はじまりの名所を見つけて見わたすのが終わってから
            Place(ForestLayout.Spawn, Vector3.forward);
            float max = 0f;
            float t = 0f;
            while (t < 0.8f)
            {
                t += Time.deltaTime;
                max = Mathf.Max(max, Worm.RearAmount);
                yield return null;
            }
            Assert.Greater(max, 0.15f, "ひと伸びして目をさます");
            yield return Seconds(1.2f);
            Assert.Less(Worm.RearAmount, 0.1f);
        }

        [UnityTest]
        public IEnumerator Silk_DescentEasesIn()
        {
            Vector3 top = Box(Sky, new Vector3(2f, 0.3f, 2f));
            Worm.Spawn(top + new Vector3(0f, 0f, -0.3f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 10f, "ふちで糸を出せる");
            GameInput.VirtualMove = Vector2.zero;
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return null;
            Assert.AreEqual(InchwormController.Mode.Hang, Worm.State);
            Assert.Less(Worm.SilkSpeed, 1.0f, "下りはじめはゆっくり");
            yield return Seconds(0.8f);
            Assert.AreEqual(Worm.silkDescendSpeed, Worm.SilkSpeed, 0.05f, "すぐに一定の速さに");
        }
    }

    /// <summary>総合テスト：いきものの動きの改善（なめらかな向き・加速・群れ・かがむ・水面・反応）。</summary>
    public class MobMovementTests
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
        public IEnumerator Walkers_TurnAndAccelerateSmoothly()
        {
            // オトシブミ：回る速さは 90 度/秒まで、速さは少しずつ変わる（近くで見る）
            Vector3 o = C.PositionOf("otoshibumi", 0);
            Place(new Vector2(o.x + 4f, o.z + 4f), Vector3.left);
            yield return Seconds(0.5f);
            float maxTurn = 0f, maxGain = 0f;
            float prev = C.Info("otoshibumi", 0).curSpeed;
            float t = 0f;
            while (t < 5f)
            {
                yield return null;
                t += Time.deltaTime;
                var info = C.Info("otoshibumi", 0);
                maxTurn = Mathf.Max(maxTurn, Mathf.Abs(info.turnRate));
                maxGain = Mathf.Max(maxGain, (info.curSpeed - prev) / Mathf.Max(Time.deltaTime, 1e-4f));
                prev = info.curSpeed;
            }
            Assert.Less(maxTurn, 90f * 1.25f, "向きはなめらかに変わる");
            Assert.Less(maxGain, 1.5f * 1.1f, "いきなり速くならない");
        }

        [UnityTest]
        public IEnumerator Sparrows_CrouchThenTakeOffTogether()
        {
            Vector3 s = C.PositionOf("sparrow", 0);
            bool crouched = false;
            Place(new Vector2(s.x + 2f, s.z), Vector3.left);
            yield return WaitUntil(() =>
            {
                var info = C.Info("sparrow", 0);
                if (!info.airborne && info.crouch > 0.3f) crouched = true;
                return info.airborne;
            }, 3f, "スズメが飛び立つ");
            Assert.IsTrue(crouched, "飛び立つ前に一瞬かがむ");
            yield return WaitUntil(() => C.IsAirborne("sparrow", 1) && C.IsAirborne("sparrow", 2), 2f, "群れの仲間もいっせいに飛び立つ");
        }

        [UnityTest]
        public IEnumerator Grasshopper_CrouchesBeforeJumping()
        {
            yield return WaitUntil(() => !C.Info("grasshopper", 0).airborne && C.Info("grasshopper", 0).crouch < 0.05f, 8f, "地面で休んでいる");
            Vector3 g = C.PositionOf("grasshopper", 0);
            bool crouched = false;
            Place(new Vector2(g.x + 1.6f, g.z), Vector3.left);
            yield return WaitUntil(() =>
            {
                var info = C.Info("grasshopper", 0);
                if (!info.airborne && info.crouch > 0.3f) crouched = true;
                return info.airborne;
            }, 4f, "バッタが跳ぶ");
            Assert.IsTrue(crouched, "跳ぶ前にかがむ");
        }

        [UnityTest]
        public IEnumerator WaterStriders_RideTheSurface()
        {
            float worst = 0f;
            float t = 0f;
            Vector3 start = C.PositionOf("waterstrider", 0);
            while (t < 3f)
            {
                yield return null;
                t += Time.deltaTime;
                for (int i = 0; i < C.CountOf("waterstrider"); i++)
                {
                    Vector3 p = C.PositionOf("waterstrider", i);
                    worst = Mathf.Max(worst, Mathf.Abs(p.y - Areas.Current.WaterLevelAt(p.x, p.z)));
                }
            }
            Assert.Less(worst, 0.02f, "いつも水面の上");
            Assert.Greater(Vector3.Distance(start, C.PositionOf("waterstrider", 0)), 0.1f, "水をこいで進む");
        }

        [UnityTest]
        public IEnumerator LoudLanding_StartlesBirds()
        {
            Vector3 s = C.PositionOf("sparrow", 0);
            C.Disturb(s, 3f);
            yield return WaitUntil(() => C.IsAirborne("sparrow", 0), 1.5f, "落ちた音におどろいて飛ぶ");
        }

        [UnityTest]
        public IEnumerator Snail_RetreatsWhenTouched()
        {
            Vector3 p = C.PositionOf("snail", 0);
            C.Disturb(p, 1f);
            yield return null;
            Assert.Greater(C.Info("snail", 0).retreat, 0f, "殻にひっこむ");
            Assert.AreEqual(0f, C.Info("snail", 0).curSpeed, 1e-4f, "ひっこんでいる間は動かない");
        }

        [UnityTest]
        public IEnumerator Spider_RaisesItsFrontLegsWhileWatching()
        {
            Vector3 p = C.PositionOf("spider", 2);
            Place(new Vector2(p.x + 2f, p.z), Vector3.left);
            yield return WaitUntil(() => C.Info("spider", 2).raise > 0.8f, 2f, "前脚を上げて見つめる");
        }

        [UnityTest]
        public IEnumerator Mantis_StrikesWhenVeryClose()
        {
            // 体の横（体にのらない所）から、頭を 0.9 まで近づける
            Vector3 m = C.PositionOf("mantis", 0);
            Vector3 side = Vector3.Cross(Vector3.up, C.Info("mantis", 0).fwd).normalized;
            Vector3 tail = m + side * 1.9f;
            Place(new Vector2(tail.x, tail.z), -side);
            float maxStrike = 0f;
            float t = 0f;
            while (t < 2.5f)
            {
                yield return null;
                t += Time.deltaTime;
                maxStrike = Mathf.Max(maxStrike, C.Info("mantis", 0).strike);
            }
            Assert.Greater(maxStrike, 0.5f, "とても近いと、かまをくり出す");
        }

        [UnityTest]
        public IEnumerator Beetle_ShowsItsHorn()
        {
            Vector3 b = C.PositionOf("beetle", 1);
            Vector3 f = C.Info("beetle", 1).fwd;
            Vector3 front = b + Vector3.ProjectOnPlane(f, Vector3.up).normalized * 2.55f;   // 頭が角の先 1.55 に来る
            Place(new Vector2(front.x, front.z), -f);
            yield return WaitUntil(() => C.Info("beetle", 1).display > 0.5f, 2.5f, "角を持ち上げて見せる");
        }

        [UnityTest]
        public IEnumerator Walkers_StayOutOfTheWater()
        {
            yield return Seconds(6f);
            foreach (var id in new[] { "ladybug", "otoshibumi", "pillbug", "beetle", "snail", "spider", "mantis" })
                for (int i = 0; i < C.CountOf(id); i++)
                    Assert.IsFalse(ForestLayout.IsUnderwater(C.PositionOf(id, i)), $"{id}[{i}] が水の中");
        }
    }
}

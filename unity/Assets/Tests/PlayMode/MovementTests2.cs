using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：しゃくとりむしの動きの改善（第 2 弾）。</summary>
    public class WormMovementTests2
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
        public IEnumerator NarrowBeam_IsWalkedCarefully()
        {
            Vector3 top = Box(Sky + new Vector3(0f, 0f, 1.5f), new Vector3(0.2f, 0.2f, 5f));
            Worm.Spawn(top + new Vector3(0f, 0f, -2f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            bool narrow = false, fell = false;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() =>
            {
                narrow |= Worm.IsNarrowFooting;
                fell |= Worm.IsFalling;
                return narrow || fell;
            }, 6f, "細い台の上");
            ResetInput();
            Assert.IsTrue(narrow, "足場がせまいと気づく");
            Assert.IsFalse(fell, "細い台から落ちない");
        }

        [UnityTest]
        public IEnumerator Backward_StartsWithAPivot()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);
            GameInput.VirtualMove = Vector2.down;
            yield return WaitUntil(() => Worm.State != InchwormController.Mode.Idle, 2f, "動きだす");
            Assert.AreEqual(InchwormController.Mode.Reach, Worm.State, "後ろへ歩きだすときは、まず頭をふり向ける");
        }

        [UnityTest]
        public IEnumerator ReverseStick_BrakesTheReach()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);
            GameInput.VirtualMove = Vector2.up;
            // 引き寄せたあとの伸びの、いちばんはじめをつかまえる
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Pull, 3f, "引き寄せ");
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Reach, 3f, "伸び");
            float start = Vector3.Distance(Worm.HeadPoint, Worm.TailPoint);
            GameInput.VirtualMove = Vector2.down;
            yield return Seconds(0.3f);
            Assert.Less(Vector3.Distance(Worm.HeadPoint, Worm.TailPoint), start + 0.3f, "頭を引っこめてブレーキ");
        }

        [UnityTest]
        public IEnumerator Sprinting_TiresAndRecovers()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            float loud = 0f;
            yield return Hold(Vector2.up, 5f, () => loud = Mathf.Max(loud, Worm.StepLoudness), sprint: true);
            Assert.Greater(Worm.Fatigue, 0.2f, "はやくで歩くと息が上がる");
            Assert.Greater(loud, 1.05f, "はやくのときは足音が大きい");
            float tired = Worm.Fatigue;
            yield return Seconds(3f);
            Assert.Less(Worm.Fatigue, tired, "休むともどる");
        }

        [UnityTest]
        public IEnumerator Drop_PullsTheHeadTowardIt()
        {
            // 平らな所にあるしずくを、少し斜めに見て歩く
            int pick = -1;
            for (int i = 0; i < Col.TotalDrops && pick < 0; i++)
            {
                Vector3 d = Col.DropPosition(i);
                if (Col.IsDropTaken(i) || !Physics.Raycast(d + Vector3.up * 3f, Vector3.down, out var h, 6f, ShakuConst.SurfaceMask)) continue;
                if (h.normal.y > 0.97f && h.collider.name.StartsWith("Terrain") && Mathf.Abs(h.point.y - Areas.Current.Height(d.x, d.z)) < 0.1f) pick = i;
            }
            Assume.That(pick >= 0, "平らな所のしずくがない");
            Vector3 drop = Col.DropPosition(pick);
            Vector3 dir = Quaternion.AngleAxis(22f, Vector3.up) * Vector3.forward;
            Vector3 start = drop - Vector3.forward * 2.3f;
            Place(new Vector2(start.x, start.z), dir);
            yield return FaceCamera(dir);
            yield return Seconds(1.2f);
            yield return Hold(Vector2.up, 5f);
            Assert.IsTrue(Col.IsDropTaken(pick), "少しずれていても、頭をしずくへ向けて取れる");
        }

        [UnityTest]
        public IEnumerator VeryLongIdle_RestsFlat()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(1.2f);
            Worm.DebugSetIdleTime(91f);
            yield return Seconds(2.5f);
            Assert.IsTrue(Worm.IsResting, "90 秒休むと、ぺたんと休む");
            Assert.IsFalse(Worm.IsTwigPose, "小枝のまねはやめている");
        }

        [UnityTest]
        public IEnumerator Idle_ReplantsItsHead()
        {
            yield return Seconds(1.5f);
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(1.2f);
            Worm.DebugSetIdleTime(13f);
            bool moved = false;
            float t = 0f;
            while (t < 1.5f && !moved)
            {
                yield return null;
                t += Time.deltaTime;
                moved |= Worm.State == InchwormController.Mode.Reach;
            }
            Assert.IsTrue(moved, "じっとしていると、頭をつきなおす");
        }

        [UnityTest]
        public IEnumerator Landmark_IsSurveyed()
        {
            var grove = ForestLayout.Landmarks[3];
            Place(grove.position + new Vector2(-2f, -11f), Vector3.forward);
            yield return WaitUntil(() => Col.IsDiscovered(3), 3f, "名所の発見");
            yield return WaitUntil(() => Worm.IsSurveying, 2f, "名所を見わたす");
        }

        [UnityTest]
        public IEnumerator StandButton_CutsTheSilk()
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
            yield return Seconds(0.8f);
            Assert.AreEqual(InchwormController.Mode.Hang, Worm.State);
            GameInput.VirtualStand = true;
            yield return Frames(3);
            GameInput.VirtualStand = false;
            Assert.AreEqual(InchwormController.Mode.Fall, Worm.State, "背伸びボタンで糸を切って落ちる");
        }
    }

    /// <summary>総合テスト：いきものの動きの改善（第 2 弾）。</summary>
    public class MobMovementTests2
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
        public IEnumerator Legs_StartOutOfStep()
        {
            yield return null;
            Assert.AreNotEqual(C.Info("ant", 0).gait, C.Info("ant", 1).gait, "脚の動きは一匹ずつずれている");
        }

        [UnityTest]
        public IEnumerator Ladybug_PlaysDead()
        {
            // てんとうむしと同じ高さ（地面）に置く（上に大きな葉などがあっても）
            Vector3 l = C.PositionOf("ladybug", 2);
            Worm.Spawn(l + new Vector3(1.15f, 0.05f, 0f), Vector3.left);
            yield return WaitUntil(() => C.Info("ladybug", 2).playingDead, 2f, "死んだふり");
            Assert.AreEqual(0f, C.Info("ladybug", 2).curSpeed, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Sparrow_GetsAlertBeforeFleeing()
        {
            Vector3 s = C.PositionOf("sparrow", 0);
            Place(new Vector2(s.x + 6f, s.z), Vector3.left);
            yield return WaitUntil(() => C.Info("sparrow", 0).alert > 0.5f, 2f, "頭を上げて警戒する");
            Assert.IsFalse(C.IsAirborne("sparrow", 0), "まだ飛ばない");
        }

        [UnityTest]
        public IEnumerator Ants_HurryWhenStartled()
        {
            Vector3 a = C.PositionOf("ant", 0);
            C.Disturb(a, 0.4f);
            yield return null;
            Assert.IsTrue(C.Info("ant", 0).hurrying, "おどろいたアリはあわてて急ぐ");
            yield return Seconds(0.6f);
            Assert.IsTrue(C.Info("ant", 1).hurrying || C.Info("ant", 2).hurrying || Vector3.Distance(C.PositionOf("ant", 1), a) > 1.5f,
                "となりのアリも、少しおくれておどろく");
        }

        [UnityTest]
        public IEnumerator Spider_BacksAwayWithASafetyLine()
        {
            Vector3 p = C.PositionOf("spider", 2);
            Place(new Vector2(p.x + 1.6f, p.z), Vector3.left);
            bool line = false;
            yield return WaitUntil(() =>
            {
                var info = C.Info("spider", 2);
                line |= info.lineVisible;
                return info.airborne;
            }, 3f, "近づきすぎると後ずさり");
            yield return null;
            Assert.IsTrue(line || C.Info("spider", 2).lineVisible, "跳ぶときは命綱の糸を引く");
        }

        [UnityTest]
        public IEnumerator Beetle_WalksAwayAfterShowingItsHorn()
        {
            Vector3 b = C.PositionOf("beetle", 1);
            Vector3 f = C.Info("beetle", 1).fwd;
            Vector3 front = b + Vector3.ProjectOnPlane(f, Vector3.up).normalized * 2.55f;
            Place(new Vector2(front.x, front.z), -f);
            yield return WaitUntil(() => C.Info("beetle", 1).display > 0.9f, 3f, "角を見せる");
            yield return WaitUntil(() =>
            {
                var info = C.Info("beetle", 1);
                return Vector3.Dot(info.fwd, (Worm.HeadPosition - info.pos).normalized) < 0f;
            }, 8f, "しゃくとりむしからはなれていく");
        }
    }
}

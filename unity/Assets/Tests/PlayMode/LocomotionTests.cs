using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：しゃくとりむしの動き（尺取り歩行・旋回・ダッシュ・背伸び・壁登り・糸・境界・水）。</summary>
    public class LocomotionTests
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

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        [UnityTest]
        public IEnumerator Walk_UsesRealInchwormGait()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            Vector3 start = Worm.CenterPosition;
            var seen = new HashSet<InchwormController.Mode>();
            float maxLoop = 0f, maxSeg = 0f;
            bool nan = false;
            yield return Hold(Vector2.up, 6f, () =>
            {
                seen.Add(Worm.State);
                maxLoop = Mathf.Max(maxLoop, LoopHeight(Worm.Curve, Worm.SurfaceUp));
                maxSeg = Mathf.Max(maxSeg, MaxSegment(Worm.Curve));
                nan |= float.IsNaN(Worm.CenterPosition.x);
            });
            Assert.IsFalse(nan);
            Assert.IsTrue(seen.Contains(InchwormController.Mode.Pull), "尾を引き寄せる段階がある");
            Assert.IsTrue(seen.Contains(InchwormController.Mode.Reach), "前へ伸びる段階がある");
            Assert.Greater(maxLoop, 0.18f, "引き寄せたとき体が Ω 字に持ち上がる");
            Assert.Less(maxSeg, 0.08f, "体がちぎれたり伸びすぎたりしない");
            float moved = Vector3.Dot(Worm.CenterPosition - start, Vector3.forward);
            Assert.Greater(moved, 2.5f, $"6 秒で前へ進む（{moved:0.00}）");
        }

        [UnityTest]
        public IEnumerator Walk_StaysAttachedToTheGround()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            int detached = 0, frames = 0;
            yield return Hold(Vector2.up, 4f, () =>
            {
                frames++;
                if (!NearSurface(Worm.TailPoint, 0.25f)) detached++;
            });
            Assert.Less(detached, frames * 0.05f, "腹脚はいつも面につかまっている");
        }

        [UnityTest]
        public IEnumerator Sprint_IsFasterThanWalking()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            Vector3 a = Worm.CenterPosition;
            yield return Hold(Vector2.up, 4f);
            yield return Seconds(1f);
            float walk = Vector2.Distance(XZ(a), XZ(Worm.CenterPosition));

            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            Vector3 b = Worm.CenterPosition;
            yield return Hold(Vector2.up, 4f, null, sprint: true);
            yield return Seconds(1f);
            float run = Vector2.Distance(XZ(b), XZ(Worm.CenterPosition));
            Assert.Greater(run, walk * 1.3f, $"walk={walk:0.00} run={run:0.00}");
        }

        [UnityTest]
        public IEnumerator Turn_FollowsStickDirection()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.right;
            yield return WaitUntil(() => Worm.Heading.x > 0.6f, 6f, "右へ向きを変える");
            GameInput.VirtualMove = Vector2.zero;
        }

        [UnityTest]
        public IEnumerator Turn_CanReverseDirection()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            float yaw = Cam.yaw;
            GameInput.VirtualMove = Vector2.down;
            yield return WaitUntil(() => Worm.Heading.z < -0.6f, 8f, "後ろを向く");
            yield return Seconds(2.5f);
            GameInput.VirtualMove = Vector2.zero;
            Assert.Less(Worm.Heading.z, -0.5f, $"こちらへ向かって歩き続ける（ぐるぐる回らない） heading={Worm.Heading}");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw, Cam.yaw)), 45f, "カメラが回り込みすぎない");
        }

        [UnityTest]
        public IEnumerator Stand_RearsUpAndComesBackDown()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(0.5f);
            GameInput.VirtualStand = true;
            yield return Seconds(1.5f);
            Assert.IsTrue(Worm.IsStanding);
            float rise = Vector3.Dot(Worm.HeadPosition - Worm.TailPoint, Worm.SurfaceUp);
            Assert.Greater(rise, 0.3f, "前半身を持ち上げる");
            GameInput.VirtualStand = false;
            yield return Seconds(1.5f);
            Assert.IsFalse(Worm.IsStanding);
            Assert.Less(Worm.RearAmount, 0.2f);
        }

        [UnityTest]
        public IEnumerator Idle_SometimesLooksAround()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            float maxRear = 0f;
            float t = 0f;
            while (t < 14f)
            {
                t += Time.deltaTime;
                maxRear = Mathf.Max(maxRear, Worm.RearAmount);
                yield return null;
            }
            Assert.Greater(maxRear, 0.3f, "じっとしていると顔を上げてきょろきょろする");
        }

        [UnityTest]
        public IEnumerator Climb_UpTheStumpWall()
        {
            Vector2 s = ForestLayout.Stump;
            Vector2 from = s + new Vector2(-12.5f, 0f);
            Place(from, Vector3.right);
            yield return FaceCamera(Vector3.right);
            float ground = ForestLayout.Height(s.x, s.y);
            float best = -999f;
            yield return Hold(Vector2.up, 18f, () => best = Mathf.Max(best, Worm.HeadPosition.y - ground));
            Assert.Greater(best, 2f, $"切り株の壁を登る（最高 {best:0.0}）");
        }

        Vector3 StumpEdgeStart(out Vector3 outward)
        {
            Vector2 s = ForestLayout.Stump;
            outward = new Vector3(-1f, 0f, 0.2f).normalized;
            Vector2 p = s + new Vector2(outward.x, outward.z) * 5.2f;
            return Place(p, outward);
        }

        [UnityTest]
        public IEnumerator Silk_DropsFromHighPlaceAndLands()
        {
            Vector3 top = StumpEdgeStart(out var outward);
            yield return FaceCamera(outward);
            bool landed = false;
            Worm.Landed += () => landed = true;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 12f, "ふちで糸を出せるようになる");
            GameInput.VirtualMove = Vector2.zero;
            yield return null;
            yield return null;
            Assert.IsTrue(Worm.CanDropSilk);
            StringAssert.Contains("糸", UI.PromptText, "糸のヒントが表示される");
            GameInput.SetVirtualSilk(true);
            yield return null;
            yield return null;
            GameInput.SetVirtualSilk(false);
            Assert.AreEqual(InchwormController.Mode.Hang, Worm.State);
            Assert.IsTrue(Worm.silk.enabled, "糸が見える");
            yield return WaitUntil(() => Worm.State != InchwormController.Mode.Hang, 30f, "着地");
            Assert.IsTrue(landed);
            Assert.Less(Worm.TailPoint.y, top.y - 5f, "下まで下りた");
            Assert.IsTrue(NearSurface(Worm.TailPoint, 0.3f));
        }

        [UnityTest]
        public IEnumerator Silk_ClimbBackUp()
        {
            Vector3 top = StumpEdgeStart(out var outward);
            yield return FaceCamera(outward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 12f, "ふちで糸を出せるようになる");
            GameInput.VirtualMove = Vector2.zero;
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return Seconds(1.0f);
            Assert.AreEqual(InchwormController.Mode.Hang, Worm.State);
            float low = Worm.HeadPosition.y;
            GameInput.SetVirtualSilk(true);
            yield return WaitUntil(() => Worm.State != InchwormController.Mode.Hang, 10f, "糸をのぼって戻る");
            GameInput.SetVirtualSilk(false);
            Assert.Greater(Worm.HeadPoint.y, low + 0.5f);
            Assert.Greater(Worm.HeadPoint.y, top.y - 1.5f, "元の場所に戻る");
        }

        [UnityTest]
        public IEnumerator Boundary_CannotLeaveTheForest()
        {
            Vector2 p = new Vector2(61.5f, -6f);
            Vector3 outward = new Vector3(p.x, 0f, p.y).normalized;
            Place(p, outward);
            yield return FaceCamera(outward);
            float maxR = 0f;
            yield return Hold(Vector2.up, 10f, () =>
            {
                maxR = Mathf.Max(maxR, XZ(Worm.HeadPoint).magnitude);
                maxR = Mathf.Max(maxR, XZ(Worm.TailPoint).magnitude);
            });
            Assert.Less(maxR, ForestLayout.PlayRadius + 0.05f);
        }

        [UnityTest]
        public IEnumerator Water_CannotBeEntered()
        {
            Vector2 p = new Vector2(ForestLayout.Pond.x + 7f, ForestLayout.Pond.y + 17f);
            Place(p, Vector3.back);
            yield return FaceCamera(Vector3.back);
            bool wet = false;
            yield return Hold(Vector2.up, 9f, () =>
            {
                wet |= ForestLayout.IsUnderwater(Worm.HeadPoint) || ForestLayout.IsUnderwater(Worm.TailPoint);
            });
            Assert.IsFalse(wet, "水の中には入らない");
        }

        [UnityTest]
        public IEnumerator Camera_FollowsWithoutClipping()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            float maxDist = 0f;
            bool below = false;
            var cam = Cam.transform;
            yield return Hold(Vector2.up, 4f, () =>
            {
                maxDist = Mathf.Max(maxDist, Vector3.Distance(cam.position, Worm.CameraFocus));
                below |= cam.position.y < ForestLayout.Height(cam.position.x, cam.position.z);
            });
            Assert.Less(maxDist, Cam.maxDistance + 1f);
            Assert.IsFalse(below, "カメラが地面にもぐらない");
        }

        [UnityTest]
        public IEnumerator Camera_LookAndZoomInput()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            float yaw = Cam.yaw;
            float dist = Cam.distance;
            for (int i = 0; i < 10; i++)
            {
                GameInput.AddLook(new Vector2(30f, 0f));
                GameInput.AddZoom(0.5f);
                yield return null;
            }
            Assert.Greater(Cam.yaw, yaw + 5f, "ドラッグで回る");
            Assert.Less(Cam.distance, dist, "ズームで近づく");
            Assert.GreaterOrEqual(Cam.distance, Cam.minDistance);
        }
    }
}

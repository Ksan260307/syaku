using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：物理計算（はね返り・水・転がる・糸・しずく・カメラ）。</summary>
    public class PhysicsPlayTests
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

        GameObject Box(string name, Vector3 center, Vector3 size, Quaternion rot = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.SetPositionAndRotation(center, rot == default ? Quaternion.identity : rot);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        static Vector3 Top(GameObject box) => box.transform.position + box.transform.up * (box.transform.localScale.y * 0.5f);

        Vector3 Sky => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.2f, 0f);

        Vector3 _floorTop;

        /// <summary>台の上に置いてから台を消し、下の板へ落とす。</summary>
        IEnumerator DropOnto(string floorName, float height)
        {
            var floor = Box(floorName, Sky, new Vector3(3f, 0.4f, 3f));
            _floorTop = Top(floor);
            var shelf = Box("TestBox", Top(floor) + new Vector3(0f, height, 0f), new Vector3(1.6f, 0.2f, 1.6f));
            Worm.Spawn(Top(shelf) + new Vector3(0f, 0f, -0.4f), Vector3.forward);
            yield return Seconds(0.4f);
            Object.Destroy(shelf);
            yield return null;
            Physics.SyncTransforms();
            yield return WaitUntil(() => Worm.IsFalling, 2f, "足場がなくなって落ちる");
        }

        [UnityTest]
        public IEnumerator Mushroom_BouncesTheFallingWorm()
        {
            int bounced = 0;
            Worm.Bounced += v => bounced++;
            yield return DropOnto("TestMushroomCap", 2.6f);
            yield return WaitUntil(() => Worm.BounceCount > 0, 3f, "キノコの上で弾む");
            Assert.AreEqual("mushroom", Worm.LastImpactMaterial);
            Assert.Greater(Worm.AirVelocity.y, 0f, "上へはね返る");
            yield return WaitUntil(() => !Worm.IsFalling, 6f, "弾みがおさまって着地");
            Assert.GreaterOrEqual(bounced, 1);
            Assert.Less(Mathf.Abs(Worm.TailPoint.y - _floorTop.y), 0.3f, "キノコのかさの上に着く");
        }

        [UnityTest]
        public IEnumerator LowFall_OnPlainGroundDoesNotBounce()
        {
            float hit = -1f;
            Worm.HitGround += v => hit = v;
            yield return DropOnto("TestBox", 1.1f);
            yield return WaitUntil(() => !Worm.IsFalling, 4f, "着地");
            Assert.AreEqual(0, Worm.BounceCount, "低い所から地面に落ちても弾まない");
            Assert.Greater(hit, 2f, "落ちた速さ");
            Assert.That(Worm.LastImpact, Is.InRange(0.01f, 0.5f), "衝撃は速さの 2 乗で小さめ");
            Assert.AreEqual("ground", Worm.LastImpactMaterial);
        }

        [UnityTest]
        public IEnumerator Water_SplashDivesThenRespawns()
        {
            // 水たまりの上に壁を立て、そこから手をはなして水に落ちる
            float wl = ForestLayout.WaterLevel;
            Vector3 c = Vector3.zero;
            bool found = false;
            // 睡蓮の葉・とびいしのない、深い所をさがす
            for (float dx = -12f; dx <= 12f && !found; dx += 1.5f)
                for (float dz = -12f; dz <= 12f && !found; dz += 1.5f)
                {
                    if (dx * dx + dz * dz > 13f * 13f) continue;
                    Vector2 xz = ForestLayout.Pond + new Vector2(dx, dz);
                    c = new Vector3(xz.x, wl + 2.5f, xz.y);
                    bool deep = ForestLayout.Height(xz.x + 0.8f, xz.y) < wl - 0.8f;
                    bool clear = !Physics.CheckBox(new Vector3(xz.x + 0.6f, wl + 2.1f, xz.y), new Vector3(1.1f, 2.05f, 1.2f), Quaternion.identity,
                        ShakuConst.WalkableMask, QueryTriggerInteraction.Ignore);
                    found = deep && clear;
                }
            Assume.That(found, "水たまりの上に、ひらけた場所がない");
            Box("TestWall", c, new Vector3(0.4f, 3f, 2f));
            float fx = c.x + 0.2f;
            Worm.Spawn(new Vector3(fx, wl + 2.3f, c.z), Vector3.right, new Vector3(fx, wl + 3.1f, c.z), Vector3.right);
            yield return Seconds(0.4f);
            bool splashed = false;
            Worm.Splashed += () => splashed = true;
            Worm.LetGo();
            yield return WaitUntil(() => splashed, 4f, "水に落ちる");
            Assert.AreEqual("water", Worm.LastImpactMaterial);
            Assert.Greater(Worm.LastImpactSpeed, 3f, "勢いよく落ちた");
            float low = 99f;
            yield return WaitUntil(() =>
            {
                for (int k = 0; k < Worm.Curve.Count; k++) low = Mathf.Min(low, Worm.Curve.pos[k].y);
                return !Worm.IsFalling;
            }, 3f, "安全な場所へもどる");
            Assert.Less(low, wl - 0.05f, "勢いがあるので、体がいったん水にもぐる");
            Assert.IsFalse(ForestLayout.IsUnderwater(Worm.CenterPosition), "水から出ている");
        }

        [UnityTest]
        public IEnumerator Pillbug_RollsDownASlope()
        {
            var slope = Box("TestSlope", Sky + new Vector3(0f, 0f, 4f), new Vector3(4f, 0.3f, 6f), Quaternion.Euler(25f, 0f, 0f));
            Vector3 up = slope.transform.up;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, up).normalized;
            Vector3 start = Top(slope) - downhill * 1.5f;
            var C = GM.creatures;
            C.SetPosition("pillbug", 0, start);
            // 頭を近づけて、まるくならせる
            Worm.Spawn(start + new Vector3(-1.5f, 0.3f, 0f), Vector3.right);
            yield return WaitUntil(() => C.Info("pillbug", 0).curled, 2f, "まるくなる");
            float spin0 = C.Info("pillbug", 0).spin;
            yield return Seconds(1.2f);
            Vector3 moved = C.PositionOf("pillbug", 0) - start;
            Assert.Greater(Vector3.Dot(moved, downhill), 0.25f, "坂を転がり落ちる");
            Assert.Greater(C.Info("pillbug", 0).spin, spin0 + 1f, "転がった分だけ回る");
        }

        [UnityTest]
        public IEnumerator Pillbug_BallRollsOnTopOfTheGroundWithoutSinking()
        {
            var slope = Box("TestSlope", Sky + new Vector3(0f, 0f, 4f), new Vector3(4f, 0.3f, 6f), Quaternion.Euler(25f, 0f, 0f));
            Vector3 up = slope.transform.up;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, up).normalized;
            Vector3 start = Top(slope) - downhill * 1.5f;
            var C = GM.creatures;
            C.SetPosition("pillbug", 0, start);
            Worm.Spawn(start + new Vector3(-1.5f, 0.3f, 0f), Vector3.right);
            yield return WaitUntil(() => C.Info("pillbug", 0).curled && C.Info("pillbug", 0).curlK > 0.99f, 2f, "まるくなる");
            Vector3 c = GM.world.assets.Get("PillBug_Ball").bounds.center;
            float lowest = 9f, spin0 = C.Info("pillbug", 0).spin;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                yield return null;
                var info = C.Info("pillbug", 0);
                if (!info.curled) break;
                Matrix4x4 body = C.BodyMatrixOf("pillbug", 0);
                Vector3 center = body.MultiplyPoint3x4(c);
                float r = c.y * body.lossyScale.y;
                // 玉のまん中は、いつも地面（ふれている点）から玉の半径ぶん上にある（回っても地面にうまらない）
                lowest = Mathf.Min(lowest, Vector3.Dot(center - info.pos, info.up) / r);
            }
            Assert.Greater(C.Info("pillbug", 0).spin, spin0 + 1f, "ころがって回る");
            Assert.Greater(lowest, 0.85f, "回っても、玉は地面の上（まん中が半径ぶん上）");
        }

        [UnityTest]
        public IEnumerator Silk_HoldsTheWormWithTension()
        {
            var shelf = Box("TestBox", Sky, new Vector3(2f, 0.3f, 2f));
            Worm.Spawn(Top(shelf) + new Vector3(0f, 0f, -0.3f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 10f, "ふちで糸を出せる");
            GameInput.VirtualMove = Vector2.zero;
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            float maxTension = 0f;
            yield return Hold(Vector2.zero, 0.8f, () => maxTension = Mathf.Max(maxTension, Worm.SilkTension));
            Assert.AreEqual(InchwormController.Mode.Hang, Worm.State);
            Assert.Greater(maxTension, 1f, "体の重さで糸が張る");
            Assert.Less(Vector3.Distance(Worm.SilkAnchor, Worm.HeadPosition), 6f);
        }

        [UnityTest]
        public IEnumerator Dewdrop_JigglesFromAStepAndSettles()
        {
            int pick = -1;
            for (int i = 0; i < Col.TotalDrops && pick < 0; i++)
                if (!Col.IsDropTaken(i) && Vector3.Distance(Col.DropPosition(i), Worm.HeadPosition) > 6f) pick = i;
            Assume.That(pick >= 0);
            Col.Impulse(Col.DropPosition(pick), 1f);
            float max = 0f;
            yield return Hold(Vector2.zero, 0.3f, () => max = Mathf.Max(max, Mathf.Abs(Col.DropSquash(pick))));
            Assert.Greater(max, 0.02f, "ぷるんとゆれる");
            yield return Seconds(3f);
            Assert.Less(Mathf.Abs(Col.DropSquash(pick)), 0.01f, "ゆれはおさまる");
        }

        [UnityTest]
        public IEnumerator Camera_ShakeRingsDown()
        {
            Cam.Shake(1f);
            float max = 0f;
            yield return Hold(Vector2.zero, 0.2f, () => max = Mathf.Max(max, Cam.ShakeOffset));
            Assert.Greater(max, 0.005f, "ゆれる");
            yield return Seconds(1.5f);
            Assert.Less(Cam.ShakeOffset, 0.002f, "ばねのように、行き来しながらおさまる");
        }

        [UnityTest]
        public IEnumerator StandingUp_OvershootsALittle()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Seconds(1.5f);
            float max = 0f;
            GameInput.VirtualStand = true;
            yield return Hold(Vector2.zero, 1.2f, () => max = Mathf.Max(max, Worm.RearAmount));
            Assert.IsTrue(Worm.IsStanding);
            float settled = Worm.RearAmount;
            GameInput.VirtualStand = false;
            Assert.Greater(max, settled + 0.01f, "体を起こすと、少し行き過ぎてからもどる");
        }
    }

    /// <summary>総合テスト：舟の物理（浮力でしずむ・足場の速さ）。</summary>
    public class FerryPhysicsTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator Ferry_DipsUnderTheWormAndCarriesItsSpeed()
        {
            var f = GM.world.Ferry;
            f.SetClock(0.5f);
            yield return Seconds(1f);
            Assert.Less(Mathf.Abs(f.Dip), 0.008f, "だれも乗っていないときは、ほとんどしずまない");
            Vector3 c = f.GetComponent<Collider>().bounds.center;
            Assert.IsTrue(Physics.Raycast(c + Vector3.up * 5f, Vector3.down, out var hit, 10f, ShakuConst.SurfaceMask));
            Worm.Spawn(hit.point, Vector3.forward);
            yield return Seconds(1.5f);
            Assert.IsTrue(Worm.OnMovingPlatform);
            Assert.Greater(f.Dip, RiverFerry.LoadDip * 0.5f, "乗ると、舟が少ししずむ");
            // 動きだすと、しゃくとりむしも舟の速さで動く
            f.SetClock(f.waitTime + f.travelTime * 0.4f);
            yield return Seconds(0.5f);
            Assert.IsTrue(Worm.OnMovingPlatform);
            Assert.Greater(Worm.PlatformVelocity.magnitude, 0.3f, "足場の速さ");
            Assert.Less(Vector3.Distance(Worm.PlatformVelocity, f.Velocity), 0.6f, "舟と同じ速さ");
        }
    }
}

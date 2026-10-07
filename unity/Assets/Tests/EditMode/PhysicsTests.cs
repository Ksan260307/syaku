using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>物理計算のきまり（ShakuPhysics・風）の単体テスト。</summary>
    public class PhysicsTests
    {
        [Test]
        public void Substeps_SplitLongFrames()
        {
            Assert.AreEqual(4, ShakuPhysics.Substeps(1f / 30f, out float h));
            Assert.AreEqual(1f / 120f, h, 1e-6f);
            Assert.AreEqual(1, ShakuPhysics.Substeps(1f / 120f, out h), "短いフレームは 1 回");
            int n = ShakuPhysics.Substeps(2f, out h);
            Assert.AreEqual(ShakuPhysics.MaxFrame, n * h, 1e-5f, "長すぎるフレームは上限まで");
            Assert.LessOrEqual(h, ShakuPhysics.MaxSubstep + 1e-6f);
            Assert.AreEqual(1, ShakuPhysics.Substeps(0f, out h));
            Assert.AreEqual(0f, h);
        }

        static float SimulateFall(float lin, float quad, float seconds)
        {
            Vector3 v = Vector3.zero;
            int steps = Mathf.RoundToInt(seconds * 120f);
            for (int i = 0; i < steps; i++)
            {
                v += Vector3.down * ShakuPhysics.Gravity / 120f;
                v = ShakuPhysics.ApplyDrag(v, Vector3.zero, lin, quad, 1f / 120f);
            }
            return v.magnitude;
        }

        [Test]
        public void Drag_GivesATerminalSpeed()
        {
            float curled = ShakuPhysics.TerminalSpeed(InchwormController.FallLinearDrag, InchwormController.FallQuadraticDrag);
            float open = ShakuPhysics.TerminalSpeed(InchwormController.OpenLinearDrag, InchwormController.OpenQuadraticDrag);
            Assert.That(curled, Is.InRange(8.5f, 9.6f), "丸まって落ちる最高速度");
            Assert.Less(open, curled - 2f, "体を開くと、ゆっくり落ちる");
            Assert.AreEqual(curled, SimulateFall(InchwormController.FallLinearDrag, InchwormController.FallQuadraticDrag, 20f), curled * 0.02f, "計算しても同じ速さに落ちつく");
            Assert.AreEqual(ShakuPhysics.Gravity / 2f, ShakuPhysics.TerminalSpeed(2f, 0f), 1e-4f, "1 次だけなら g / a");
            float bug = ShakuPhysics.TerminalSpeed(Creatures.FallLinearDrag, Creatures.FallQuadraticDrag);
            Assert.Less(bug, curled, "小さな虫は、しゃくとりむしよりゆっくり落ちる");
            Assert.AreEqual(bug, SimulateCreatureFall(15f), bug * 0.03f, "いきものの落ちる速さも、最高速度に落ちつく");
        }

        static float SimulateCreatureFall(float seconds)
        {
            float v = 0f;
            for (float t = 0f; t < seconds; t += 0.05f) v = Creatures.FallSpeed(v, 0.05f);
            return v;
        }

        [Test]
        public void Drag_CarriesTheBodyWithTheWind()
        {
            Vector3 air = new Vector3(1f, 0f, 0f);
            Vector3 v = Vector3.zero;
            for (int i = 0; i < 1200; i++) v = ShakuPhysics.ApplyDrag(v, air, 0.5f, 0.2f, 1f / 120f);
            TestUtil.AssertVector(air, v, 0.01f, "風といっしょに流される");
        }

        [Test]
        public void Drag_NeverOvershoots()
        {
            // とても大きなてい抗・長い時間でも、向きが逆にならない（半陰的に計算するので安定）
            Vector3 v = ShakuPhysics.ApplyDrag(new Vector3(5f, 0f, 0f), Vector3.zero, 500f, 500f, 0.1f);
            Assert.GreaterOrEqual(v.x, 0f);
            Assert.Less(v.x, 0.05f);
        }

        [Test]
        public void Materials_ComeFromTheName()
        {
            Assert.AreEqual("mushroom", ShakuPhysics.MaterialOf("RedMushroom_3").name);
            Assert.AreEqual("stone", ShakuPhysics.MaterialOf("RiverRock").name);
            Assert.AreEqual("leaf", ShakuPhysics.MaterialOf("LeafFerry").name);
            Assert.AreEqual("leaf", ShakuPhysics.MaterialOf("LilyPad_2").name);
            Assert.AreEqual("wood", ShakuPhysics.MaterialOf("Log_1").name);
            Assert.AreEqual("wood", ShakuPhysics.MaterialOf("Stump").name);
            Assert.AreEqual("ground", ShakuPhysics.MaterialOf("Terrain").name);
            Assert.AreEqual("ground", ShakuPhysics.MaterialOf((string)null).name);
            Assert.Greater(ShakuPhysics.Mushroom.restitution, ShakuPhysics.Stone.restitution, "キノコはよくはねる");
            Assert.Greater(ShakuPhysics.Stone.restitution, ShakuPhysics.Leaf.restitution, "葉っぱはふわっと受けとめる");
            Assert.Greater(ShakuPhysics.Ground.friction, ShakuPhysics.Stone.friction, "石はすべりやすい");
        }

        [Test]
        public void Materials_CreatureLayerIsACreature()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Mob_beetle";
            go.layer = ShakuConst.CreatureLayer;
            Assert.AreEqual("creature", ShakuPhysics.MaterialOf(go.GetComponent<Collider>()).name);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Bounce_UsesRestitutionAndFriction()
        {
            Vector3 after = ShakuPhysics.Bounce(new Vector3(4f, -5f, 0f), Vector3.up, ShakuPhysics.Mushroom, Vector3.zero, out float vn);
            Assert.AreEqual(5f, vn, 1e-4f, "ぶつかった速さ");
            Assert.AreEqual(5f * ShakuPhysics.Mushroom.restitution, after.y, 1e-4f, "反発係数の分だけはね返る");
            Assert.AreEqual(4f - ShakuPhysics.Mushroom.friction * 5f, after.x, 1e-4f, "横の速さは、まさつで弱まる");
            // 横の速さがまさつより小さければ、横には止まる
            after = ShakuPhysics.Bounce(new Vector3(0.5f, -5f, 0f), Vector3.up, ShakuPhysics.Ground, Vector3.zero, out _);
            Assert.AreEqual(0f, after.x, 1e-5f);
            // 動いている面の上では、面から見た速さで計算する
            after = ShakuPhysics.Bounce(new Vector3(2f, -3f, 0f), Vector3.up, ShakuPhysics.Leaf, new Vector3(2f, 0f, 0f), out vn);
            Assert.AreEqual(3f, vn, 1e-4f);
            Assert.AreEqual(2f, after.x, 1e-4f, "舟といっしょに動く");
            // はなれていく向きなら、何もしない
            Vector3 away = new Vector3(1f, 2f, 0f);
            Assert.AreEqual(away, ShakuPhysics.Bounce(away, Vector3.up, ShakuPhysics.Stone, Vector3.zero, out vn));
            Assert.AreEqual(0f, vn);
        }

        [Test]
        public void Slide_And_Impact()
        {
            Assert.AreEqual(1f, ShakuPhysics.SlideDistance(3f, 0.5f), 1e-4f, "v² / 2μg");
            Assert.Greater(ShakuPhysics.SlideDistance(3f, ShakuPhysics.Stone.friction), ShakuPhysics.SlideDistance(3f, ShakuPhysics.Ground.friction), "石の上はよくすべる");
            float a = ShakuPhysics.ImpactStrength(2f, ShakuPhysics.Stone), b = ShakuPhysics.ImpactStrength(4f, ShakuPhysics.Stone);
            Assert.AreEqual(4f, b / a, 1e-3f, "衝撃はエネルギー（速さの 2 乗）");
            Assert.Less(ShakuPhysics.ImpactStrength(4f, ShakuPhysics.Leaf), b, "やわらかい葉っぱは衝撃をすいこむ");
            Assert.AreEqual(1f, ShakuPhysics.ImpactStrength(30f, ShakuPhysics.Stone), 1e-5f);
        }

        static float MaxOfSpring(float zeta, out float final)
        {
            float x = 0f, v = 0f, max = 0f;
            for (int i = 0; i < 240; i++)
            {
                ShakuPhysics.SpringSteps(ref x, ref v, 1f, 12f, zeta, 1f / 60f);
                max = Mathf.Max(max, x);
            }
            final = x;
            return max;
        }

        [Test]
        public void Spring_OvershootsOnlyWhenUnderdamped()
        {
            Assert.Greater(MaxOfSpring(0.3f, out float f1), 1.2f, "減衰が小さいと、行き過ぎてから戻る");
            Assert.AreEqual(1f, f1, 0.01f, "最後はおちつく");
            Assert.LessOrEqual(MaxOfSpring(1f, out float f2), 1.005f, "臨界減衰なら行き過ぎない");
            Assert.AreEqual(1f, f2, 0.01f);
        }

        [Test]
        public void Spring_IsTheSameAtAnyFrameRate()
        {
            float x30 = 0f, v30 = 0f, x120 = 0f, v120 = 0f;
            for (int i = 0; i < 30; i++) ShakuPhysics.SpringSteps(ref x30, ref v30, 1f, 20f, 0.4f, 1f / 30f);
            for (int i = 0; i < 120; i++) ShakuPhysics.SpringSteps(ref x120, ref v120, 1f, 20f, 0.4f, 1f / 120f);
            Assert.AreEqual(x120, x30, 1e-4f, "30fps でも 120fps でも同じ動き");
            Vector3 p = Vector3.zero, pv = Vector3.zero;
            ShakuPhysics.SpringSteps(ref p, ref pv, Vector3.one, 20f, 0.4f, 5f);
            Assert.IsTrue(ShakuPhysics.IsFinite(p), "とても長いフレームでもこわれない");
        }

        [Test]
        public void Rolling_FollowsTheSlope()
        {
            Assert.AreEqual(ShakuPhysics.Gravity * 0.5f * 5f / 7f, ShakuPhysics.RollingAccel(0.5f), 1e-4f, "中のつまった球は g sinθ × 5/7");
            Assert.IsTrue(ShakuPhysics.StartsRolling(0.3f, Creatures.PillbugStaticFriction), "急な坂では転がりだす");
            Assert.IsFalse(ShakuPhysics.StartsRolling(0.1f, Creatures.PillbugStaticFriction), "ゆるい坂では止まったまま");
        }

        [Test]
        public void Silk_PullsOnlyWhenStretched()
        {
            Vector3 radial = Vector3.down;
            Assert.AreEqual(Vector3.zero, ShakuPhysics.SilkForce(radial, 2f, 3f, Vector3.zero, 450f, 14f), "たるんだ糸は引っぱらない");
            Vector3 f = ShakuPhysics.SilkForce(radial, 3.1f, 3f, Vector3.zero, 450f, 14f);
            Assert.Greater(f.y, 0f, "のびた糸は、つけ根の方へ引っぱる");
            Vector3 f2 = ShakuPhysics.SilkForce(radial, 3.2f, 3f, Vector3.zero, 450f, 14f);
            Assert.AreEqual(2f, f2.y / f.y, 1e-3f, "のびた分に比例");
            Vector3 longSilk = ShakuPhysics.SilkForce(radial, 10.1f, 10f, Vector3.zero, 450f, 14f);
            Assert.Less(longSilk.y, f.y, "長い糸ほど、やわらかい");
            Vector3 moving = ShakuPhysics.SilkForce(radial, 3.1f, 3f, Vector3.down * 2f, 450f, 14f);
            Assert.Greater(moving.y, f.y, "のびていく途中は、ダンパーが強く引く");
            // ぶら下がってつりあう所：重さとつりあうだけ、少しのびる
            float rest = 3f;
            float stretch = InchwormController.HangGravity / (InchwormController.SilkStiffness / rest);
            Assert.Less(stretch / rest, 0.03f, "のびは 3% より小さい");
        }

        [Test]
        public void Swing_KeepsAngularMomentum()
        {
            Vector3 v = new Vector3(1f, 0.5f, 0f);
            Vector3 after = ShakuPhysics.ConserveSwing(v, Vector3.up, 2f, 1f);
            Assert.AreEqual(2f, after.x, 1e-4f, "糸を半分にすると、横の速さは 2 倍");
            Assert.AreEqual(0.5f, after.y, 1e-4f, "糸の向きの速さはそのまま");
            Assert.AreEqual(v, ShakuPhysics.ConserveSwing(v, Vector3.up, 1f, 0f), "長さ 0 では何もしない");
        }

        [Test]
        public void PointVelocity_IncludesRotation()
        {
            Matrix4x4 a = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            Matrix4x4 b = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0.6f, 0f), Vector3.one);
            float dt = 0.6f * Mathf.Deg2Rad;   // 1 ラジアン/秒で回る
            Vector3 v = ShakuPhysics.PointVelocity(a, b, new Vector3(2f, 0f, 0f), dt);
            Assert.AreEqual(2f, v.magnitude, 0.01f, "中心から 2 はなれた所は、速さ 2");
            Assert.AreEqual(0f, Vector3.Dot(v, Vector3.right), 0.02f, "回る向きに動く");
            Vector3 c = ShakuPhysics.PointVelocity(a, b, Vector3.zero, dt);
            Assert.AreEqual(0f, c.magnitude, 1e-4f, "中心は動かない");
        }

        [Test]
        public void Sanitize_StopsBadNumbers()
        {
            Assert.AreEqual(Vector3.zero, ShakuPhysics.Sanitize(new Vector3(float.NaN, 0f, 0f), 10f));
            Assert.AreEqual(Vector3.zero, ShakuPhysics.Sanitize(new Vector3(float.PositiveInfinity, 0f, 0f), 10f));
            Assert.AreEqual(10f, ShakuPhysics.Sanitize(new Vector3(0f, 50f, 0f), 10f).magnitude, 1e-4f);
        }

        [Test]
        public void Depenetrate_PushesOutOfABox()
        {
            TestUtil.NewEmptyScene();
            var box = TestUtil.Box(Vector3.zero, new Vector3(2f, 1f, 2f));
            Physics.SyncTransforms();
            Vector3 c = new Vector3(0.2f, 0.45f, 0f);   // 上の面の少し下にめりこんでいる
            Assert.IsTrue(ShakuPhysics.Depenetrate(ref c, 0.1f, ShakuConst.SurfaceMask));
            Assert.GreaterOrEqual(c.y, 0.5f + 0.1f - 1e-3f, "上の面の外へ押し出す");
            Assert.AreEqual(0.2f, c.x, 1e-3f, "近い面の向きへだけ動く");
            Assert.IsFalse(ShakuPhysics.Depenetrate(ref c, 0.1f, ShakuConst.SurfaceMask), "外にいれば何もしない");
            Object.DestroyImmediate(box);
        }

        [Test]
        public void Wind_IsWeakerNearTheGround()
        {
            Assert.AreEqual(1f, Wind.HeightFactor(Wind.ReferenceHeight), 1e-4f);
            Assert.AreEqual(0.35f, Wind.HeightFactor(0f), 1e-4f, "地面すれすれは弱い");
            Assert.AreEqual(1.4f, Wind.HeightFactor(30f), 1e-4f, "高い所は強い（上限まで）");
            float prev = 0f;
            for (float h = 0f; h < 10f; h += 0.25f)
            {
                float k = Wind.HeightFactor(h);
                Assert.GreaterOrEqual(k, prev, "高いほど強い");
                prev = k;
            }
            Vector3 p = new Vector3(3f, 0f, 2f);
            TestUtil.AssertVector(Wind.At(p, 10f) * Wind.HeightFactor(4f), Wind.At(p, 10f, 4f), 1e-5f);
        }

        [Test]
        public void Wind_GustsTravelDownwind()
        {
            for (float t = 5f; t < 300f; t += 13.7f)
            {
                Vector3 p = new Vector3(-2f, 0f, 1.5f);
                Vector3 dir = Wind.Direction(t);
                const float d = 4f;
                float later = Wind.GustAt(p + dir * d, t + d / Wind.GustTravelSpeed);
                Assert.AreEqual(Wind.GustAt(p, t), later, 0.03f, "風上の突風が、少しおくれて風下へとどく");
            }
        }

        [Test]
        public void Wind_HasSmallTurbulence()
        {
            // 同じ時刻・同じ突風でも、場所によって少し向きがちがう
            float t = 42f;
            Vector3 a = Wind.At(new Vector3(0f, 0f, 0f), t), b = Wind.At(new Vector3(0.7f, 0f, -1.1f), t);
            Assert.Greater(Vector3.Angle(a, b), 0.05f);
            Assert.Less(Vector3.Angle(a, Wind.Direction(t)), 25f, "乱れは小さい");
        }

        [Test]
        public void Ferry_VelocityFollowsTheRoute()
        {
            var f = new GameObject("ferry").AddComponent<RiverFerry>();
            f.dockA = Vector3.zero;
            f.dockB = Vector3.right * 10f;
            f.SetClock(1f);
            Assert.Less(Mathf.Abs(f.Velocity.x), 0.05f, "岸で待っているときは止まっている");
            f.SetClock(f.waitTime + f.travelTime * 0.5f);
            Assert.Greater(f.Velocity.x, 0.5f, "中州へ向かって進む");
            Assert.AreEqual(0f, f.Dip, 1e-5f);
            Object.DestroyImmediate(f.gameObject);
        }
    }
}

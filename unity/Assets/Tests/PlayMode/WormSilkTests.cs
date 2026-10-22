using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：糸の手ざわり：糸を出せるふちで口もとがきらり・出すときのきらり・じっとぶら下がると景色をながめる・
    /// 使った糸が光って残る・ゆれの高い所で切るとぴょん・ねらった場所の光る点・引き寄せられている途中でねらい直す・とどかない糸はたれる。
    /// </summary>
    public class WormSilkTests
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

        GameObject Box(Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestBox";
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        // 糸は、のぼらなければ、のびて下りていくので、高い所から
        Vector3 Sky => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 14f, 0f);

        IEnumerator HangFromABox()
        {
            var shelf = Box(Sky, new Vector3(2f, 0.3f, 2f));
            Worm.Spawn(shelf.transform.position + new Vector3(0f, 0.15f, -0.3f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            int hints = Worm.SilkReadyHints;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 10f, "ふちで糸を出せる");
            GameInput.VirtualMove = Vector2.zero;
            yield return Frames(2);
            Assert.Greater(Worm.SilkReadyHints, hints, "糸を出せるふちに来たら、口もとがきらり");
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Hang, 1f, "ぶら下がる");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator DroppingSilk_Glints_Sightsees_AndLeavesAGlowingTrail()
        {
            yield return HangFromABox();
            Assert.IsTrue(Worm.SilkEffects.GlintShown, "糸を出す瞬間に、口もとがきらり");
            // じっとぶら下がっていると、くるりと回って景色をながめる
            bool sightseeing = false;
            yield return Hold(Vector2.zero, 4f, () => sightseeing |= Worm.IsSightseeing);
            Assert.IsTrue(sightseeing, "景色をながめる");
            // 糸を切ると、使った糸がしばらく光って残る
            GameInput.VirtualStand = true;
            yield return Frames(3);
            GameInput.VirtualStand = false;
            Assert.IsTrue(Worm.IsFalling);
            yield return Frames(2);
            Assert.Greater(Worm.SilkEffects.TrailCount, 0, "使った糸が、光って残る");
            yield return Seconds(SilkFX.TrailLife + 0.5f);
            Assert.AreEqual(0, Worm.SilkEffects.TrailCount, "しばらくすると消える");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CuttingAtTheTopOfTheSwing_Hops()
        {
            yield return HangFromABox();
            yield return Seconds(0.6f);
            int before = Worm.SwingJumps;
            // こいで、大きくゆらす
            float best = 0f;
            yield return Hold(Vector2.up, 3f, () =>
            {
                Vector3 off = Worm.HeadPosition - Worm.SilkAnchor;
                best = Mathf.Max(best, Vector3.Angle(Vector3.down, off));
            });
            Assert.Greater(best, 22f, $"こいで、ゆれが大きくなる（{best:0} 度）");
            yield return WaitUntil(() => Vector3.Angle(Vector3.down, Worm.HeadPosition - Worm.SilkAnchor) > 20f, 4f, "高い所");
            GameInput.VirtualStand = true;
            yield return Frames(3);
            GameInput.VirtualStand = false;
            Assert.Greater(Worm.SwingJumps, before, "ゆれの高い所で切ると、ぴょんととぶ");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Aiming_MarksTheTarget_ReaimsMidReel_AndMissesDroop()
        {
            Vector3 start = Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            Box(start + new Vector3(0f, 3f, 5f), new Vector3(4f, 6f, 1f));
            yield return Seconds(0.3f);
            // とどかない所（空）をねらってはなすと、糸がたれ下がる
            Cam.pitch = -60f;
            GameInput.VirtualAim = true;
            yield return Seconds(0.5f);
            Assert.IsFalse(Worm.AimValid);
            Assert.IsFalse(Worm.SilkEffects.MarkerShown);
            int misses = Worm.SilkMisses;
            GameInput.VirtualAim = false;
            yield return Frames(2);
            Assert.Greater(Worm.SilkMisses, misses, "とどかない所へ撃つと、失敗");
            Assert.IsTrue(Worm.SilkEffects.MissShown, "糸がたれ下がる");
            yield return Seconds(SilkFX.MissLife + 0.2f);
            Assert.IsFalse(Worm.SilkEffects.MissShown);
            // とどく所をねらっているあいだは、その場所に光る点
            Cam.pitch = 5f;
            GameInput.VirtualAim = true;
            yield return WaitUntil(() => Worm.AimValid, 2f, "柱をねらう");
            yield return Frames(2);
            Assert.IsTrue(Worm.SilkEffects.MarkerShown, "ねらった場所に光る点");
            ResetInput();
            yield return Frames(2);
            // 引き寄せられている途中で、べつの場所へねらい直せる
            Assert.IsTrue(Worm.AimAt(start + new Vector3(-1f, 2.5f, 4.5f)));
            Assert.IsTrue(Worm.FireSilk());
            yield return Seconds(0.25f);
            Assert.IsTrue(Worm.IsReeling);
            Vector3 second = start + new Vector3(1.2f, 4.5f, 4.5f);
            Assert.IsTrue(Worm.AimAt(second), "引き寄せられている途中でも、ねらえる");
            Assert.IsTrue(Worm.FireSilk());
            Assert.Less(Vector3.Distance(Worm.SilkAnchor, second), 0.4f, "新しい場所へ糸をかけなおす");
        }
    }
}

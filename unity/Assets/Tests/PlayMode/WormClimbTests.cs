using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：登る・下りる：壁を登る一歩の「ため」・疲れたら壁の途中で一休み・高いがけでためらう（引けば下がれる）・
    /// とどかないすき間で迷う・進めないまま押すと首をふって「むり」。
    /// </summary>
    public class WormClimbTests
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

        void Box(Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestBox";
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
        }

        Vector3 SkyTop => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.5f, 0f);

        IEnumerator Begin(Vector3 top)
        {
            Worm.Spawn(top, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ClimbingAWall_GathersEachStep_AndRestsWhenTired()
        {
            Vector3 top = SkyTop;
            Box(top + new Vector3(0f, -0.2f, 1f), new Vector3(4f, 0.4f, 5f));
            Box(top + new Vector3(0f, 3f, 2.2f), new Vector3(4f, 6.4f, 0.4f));
            yield return Begin(top);
            bool gathered = false, rested = false, tired = false;
            yield return Hold(Vector2.up, 12f, () =>
            {
                gathered |= Worm.IsGathering;
                rested |= Worm.IsWallResting;
                // 壁に着いたら、とても疲れたことにする
                if (!tired && Worm.OnSteepSurface && Worm.HeadPosition.y > top.y + 0.8f)
                {
                    tired = true;
                    Worm.DebugSetFatigue(0.95f);
                }
            });
            Assert.IsTrue(tired, "壁を登った");
            Assert.IsTrue(gathered, "壁を登る一歩は、引き寄せる前に「ため」る");
            Assert.IsTrue(rested, "とても疲れたら、壁の途中で一休み");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HighCliff_Teeters_AndPullingBackSavesTheWorm()
        {
            Vector3 top = SkyTop;
            Box(top + new Vector3(0f, -0.2f, 0.5f), new Vector3(3f, 0.4f, 3f));   // はしは z = 2
            yield return Begin(top);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.IsTeetering || Worm.IsFalling, 8f, "がけのふち");
            Assert.IsTrue(Worm.IsTeetering, "高いがけでは、落ちる前にためらう");
            GameInput.VirtualMove = Vector2.down;
            yield return Seconds(0.6f);
            ResetInput();
            Assert.Greater(Worm.TeeterCancels, 0, "スティックを引くと、がけから下がる");
            Assert.IsFalse(Worm.IsFalling, "落ちない");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TooWideGap_StretchesAndHesitates()
        {
            Vector3 top = SkyTop;
            Box(top + new Vector3(0f, -0.2f, 0.5f), new Vector3(3f, 0.4f, 3f));   // はしは z = 2
            Box(top + new Vector3(0f, -0.2f, 4.1f), new Vector3(3f, 0.4f, 2f));   // むこうの台は z = 3.1 から（すき間 1.1）
            yield return Begin(top);
            bool gap = false;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() =>
            {
                gap |= Worm.IsPeeking && Worm.PeekKind == "gap";
                return gap || Worm.IsFalling;
            }, 8f, "すき間のふち");
            Assert.IsTrue(gap, "とどかないすき間では、体を伸ばしきって迷う");
            // 迷っている間にスティックを引けば、もどれる
            GameInput.VirtualMove = Vector2.down;
            yield return Seconds(0.6f);
            ResetInput();
            Assert.IsFalse(Worm.IsFalling, "落ちない");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PushingAgainstTheWater_ShakesTheHead()
        {
            Vector2 p = new Vector2(ForestLayout.Pond.x + 7f, ForestLayout.Pond.y + 17f);
            Place(p, Vector3.back);
            yield return FaceCamera(Vector3.back);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.IsBlocked, 20f, "池のふちで止まる");
            int before = Worm.NoShakes;
            yield return WaitUntil(() => Worm.NoShakes > before, 8f, "首をふって「むり」");
        }
    }
}

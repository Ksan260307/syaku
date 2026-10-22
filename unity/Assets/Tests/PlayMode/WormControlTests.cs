using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：操作の手ざわり：入力の向きが大きく変わると次の一歩ですぐに頭をふり向ける・ゆっくり歩き・
    /// 壁ではスティックの上で登る（設定）・おまかせ歩き（地図で 2 回タップ）・糸をのぼる音と切る音。
    /// </summary>
    public class WormControlTests
    {
        readonly List<GameObject> _temp = new List<GameObject>();
        bool _wallUp;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            _wallUp = SaveSystem.Settings.wallStickUp;
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown()
        {
            ResetInput();
            GameInput.VirtualSlow = false;
            SaveSystem.Settings.wallStickUp = _wallUp;
            GM.AutoWalker?.Stop();
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

        Vector3 SkyTop => TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.5f, 0f);

        IEnumerator OnDeck()
        {
            Vector3 top = SkyTop;
            Box(top + new Vector3(0f, -0.2f, 0f), new Vector3(12f, 0.4f, 12f));
            Worm.Spawn(top + new Vector3(0f, 0f, -3f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TurningTheStickMidWalk_PivotsOnTheNextStep()
        {
            yield return OnDeck();
            yield return Hold(Vector2.up, 1.6f);
            int pivots = Worm.QuickPivots;
            GameInput.VirtualMove = Vector2.right;   // 歩いている途中で、右へ
            yield return Seconds(1.5f);
            ResetInput();
            Assert.Greater(Worm.QuickPivots, pivots, "尾を動かさずに、次の一歩で頭を右へふり向ける");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SlowWalk_TakesSmallCarefulSteps()
        {
            yield return OnDeck();
            GameInput.VirtualSlow = true;
            float probe = 0f, mag = 0f;
            yield return Hold(Vector2.up, 2.5f, () =>
            {
                mag = Mathf.Max(mag, GameInput.Move.magnitude);
                probe = Mathf.Max(probe, Worm.ProbeSway);
            });
            Assert.IsTrue(GameInput.Slow);
            Assert.LessOrEqual(mag, GameInput.SlowMagnitude + 1e-4f, "ゆっくり歩き");
            Assert.Greater(probe, 0.02f, "頭で足場をさぐる");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator WallStickUpSetting_ClimbsWithUp()
        {
            SaveSystem.Settings.wallStickUp = true;
            Vector3 g = TopSurface(ForestLayout.Spawn + new Vector2(4f, 0f));
            Vector3 c = g + new Vector3(0f, 2.5f, 0f);
            Box(c, new Vector3(0.4f, 6f, 4f));
            float fx = c.x + 0.2f;
            // 壁の面に、横向き（+Z）にしがみつく
            Worm.Spawn(new Vector3(fx, c.y, c.z - 0.4f), Vector3.right, new Vector3(fx, c.y, c.z + 0.4f), Vector3.right);
            yield return FaceCamera(Vector3.forward);   // カメラは壁にそって見ている（上を押しても、ふつうなら前へ）
            yield return Seconds(0.4f);
            float y0 = Worm.HeadPosition.y;
            yield return Hold(Vector2.up, 3f);
            Assert.Greater(Worm.HeadPosition.y - y0, 0.4f, "スティックの上で、壁を上へ登る");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator UnknownPlace_DoubleTapOnTheMap_WalksThere()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(0.5f);
            LandmarkDef unknown = null;
            foreach (var lm in ForestLayout.Landmarks)
                if (!Col.IsDiscovered(lm.id)) { unknown = lm; break; }
            Assume.That(unknown != null, "見つけていない名所がない");
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.IsFalse(UI.TapBigMap(GameUI.MapPoint(unknown.position)), "1 回目は、ようすを教えるだけ");
            Assert.IsTrue(UI.IsMapOpen);
            Assert.IsTrue(UI.TapBigMap(GameUI.MapPoint(unknown.position)), "もう一度タップすると、歩いて行く");
            Assert.IsFalse(UI.IsMapOpen);
            Assert.IsTrue(GM.AutoWalker.Active, "おまかせ歩き");
            Vector3 start = Worm.CenterPosition;
            Vector2 goal = unknown.position;
            float d0 = Vector2.Distance(new Vector2(start.x, start.z), goal);
            yield return Seconds(6f);
            Vector3 now = Worm.CenterPosition;
            Assert.Less(Vector2.Distance(new Vector2(now.x, now.z), goal), d0 - 1f, "行き先へ近づいている");
            // 自分でスティックを倒すと、やめる
            GameInput.VirtualMove = Vector2.left;
            yield return Frames(3);
            ResetInput();
            Assert.IsFalse(GM.AutoWalker.Active, "スティックを倒すと、やめる");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SilkClimbing_TicksAndCuttingSnaps()
        {
            var shelf = Box(TopSurface(ForestLayout.Spawn) + new Vector3(0f, 6f, 0f), new Vector3(2f, 0.3f, 2f));
            Worm.Spawn(shelf.transform.position + new Vector3(0f, 0.15f, -0.3f), Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 10f, "ふちで糸を出せる");
            GameInput.VirtualMove = Vector2.zero;
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Hang, 1f, "ぶら下がる");
            yield return Seconds(0.8f);
            int ticks = 0, cuts = 0;
            Worm.SilkClimbTick += () => ticks++;
            Worm.SilkCut += () => cuts++;
            GameInput.SetVirtualSilk(true);
            yield return Seconds(1.2f);
            GameInput.SetVirtualSilk(false);
            Assert.Greater(ticks, 2, "糸をのぼると、たぐるたびに小さな音");
            GameInput.VirtualStand = true;
            yield return Frames(3);
            GameInput.VirtualStand = false;
            Assert.AreEqual(1, cuts, "糸を切ると、ぷつん");
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：名所への移動で水に入らない・どんぐりを押すと転がる（森）。</summary>
    public class ForestFixPlayTests
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

        [UnityTest]
        public IEnumerator FastTravel_ToThePondLandsOnDryGround()
        {
            var lm = ForestLayout.Landmarks.Find(l => l.id == 5);   // 鏡の水たまり
            Col.Discover(lm);
            yield return Frames(2);
            Assert.IsTrue(GM.FastTravel(lm.id));
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 5f, "移動");
            yield return Seconds(0.5f);
            Assert.IsFalse(ForestLayout.IsUnderwater(Worm.HeadPoint), "頭が水の中");
            Assert.IsFalse(ForestLayout.IsUnderwater(Worm.TailPoint), "尾が水の中");
            Assert.IsFalse(Worm.IsFalling);
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), lm.position), lm.radius * 1.6f + 2f, "名所のそば");
        }

        [UnityTest]
        public IEnumerator Zukan_ShowsWhereACreatureLives()
        {
            GM.creatures.Discover(SpeciesCatalog.Get("ant"));
            UI.ShowCollection(true, "zukan");
            yield return Frames(2);
            UI.SelectSpecies("ant");
            yield return Frames(2);
            StringAssert.StartsWith("すみか：森", UI.ZukanHabitatText);
            StringAssert.Contains("のまわり", UI.ZukanHabitatText, "そばの名所");
            Assert.Greater(UI.HabitatDotCount, 0, "地図に、すみかの印");
            // まだ見つけていないいきものは、すみかの場所は出さない
            UI.SelectSpecies("mantis");
            yield return null;
            if (!Creatures.IsDiscovered("mantis")) Assert.AreEqual(0, UI.HabitatDotCount);
            UI.ShowCollection(false);
        }

        [UnityTest]
        public IEnumerator Acorn_RollsWhenPushed()
        {
            // ひらけた所にある、どんぐりをさがす
            RollingProp acorn = null;
            Vector3 dir = Vector3.forward, start = Vector3.zero;
            foreach (var rp in RollingProp.All)
            {
                Vector3 c = rp.transform.position;
                for (int k = 0; k < 8 && acorn == null; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 s = c - d * 1.8f;
                    float g = ForestLayout.Height(s.x, s.z);
                    if (Physics.CheckSphere(new Vector3(s.x, g + 0.6f, s.z), 0.45f, ShakuConst.WalkableMask | (1 << ShakuConst.RollingLayer), QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(g - ForestLayout.Height(c.x, c.z)) > 0.4f) continue;
                    acorn = rp;
                    dir = d;
                    start = s;
                }
                if (acorn != null) break;
            }
            Assume.That(acorn != null, "押せるどんぐりがない");
            Assert.AreEqual(ShakuConst.RollingLayer, acorn.gameObject.layer, "しゃくとりむしは這わずに、押す");
            Vector3 before = acorn.transform.position;
            Place(new Vector2(start.x, start.z), dir);
            yield return FaceCamera(dir);
            yield return Seconds(0.8f);
            yield return Hold(Vector2.up, 5f);
            yield return Seconds(1f);
            Assert.Greater(Vector3.Distance(acorn.transform.position, before), 0.2f, "押されて動いた");
            Assert.Greater(acorn.RolledDegrees, 20f, "転がった");
        }
    }

    /// <summary>総合テスト：川辺の名所へ移動しても、水の中に埋まらない。</summary>
    public class RiverFixPlayTests
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
        public IEnumerator FastTravel_ToEveryRiverLandmarkLandsOnDryGround()
        {
            foreach (var lm in RiverLayout.Landmarks) Col.Discover(lm);
            yield return Frames(2);
            foreach (var lm in RiverLayout.Landmarks)
            {
                Assert.IsTrue(GM.FastTravel(lm.id), lm.name);
                yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 5f, lm.name);
                yield return Seconds(0.6f);
                Assert.IsFalse(RiverLayout.IsUnderwater(Worm.HeadPoint), $"{lm.name}：頭が水の中");
                Assert.IsFalse(RiverLayout.IsUnderwater(Worm.TailPoint), $"{lm.name}：尾が水の中");
                Assert.IsFalse(RiverLayout.IsUnderwater(Worm.CenterPosition), $"{lm.name}：体が水の中");
                Assert.IsFalse(Worm.IsFalling, $"{lm.name}：落ちていない");
                Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), lm.position), lm.radius * 1.6f + 2.5f, $"{lm.name}：名所のそば");
            }
        }
    }
}

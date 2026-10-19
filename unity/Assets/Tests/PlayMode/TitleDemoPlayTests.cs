using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：タイトル画面のうしろに、行ったことのあるエリアのデモ映像が流れる（はじめて遊ぶときは森だけ）。
    /// 映像がすっかり出ているあいだは、うしろのエリアを描かない。ゲームを始めると止まり（エリアをまた描く）、タイトルへもどると、また流れる。
    /// </summary>
    public class TitleDemoPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TitleDemo.AllowInBatch = true;   // テストの実行中でも、映像を流す
            yield return Boot();
        }

        [TearDown]
        public void TearDown() => TitleDemo.AllowInBatch = false;

        [UnityTest]
        public IEnumerator Title_PlaysTheDemoOfAVisitedArea_AndStopsWhenPlaying()
        {
            var demo = GM.GetComponent<TitleDemo>();
            Assert.IsNotNull(demo, "タイトルのデモ映像");
            Assert.IsTrue(System.IO.File.Exists(TitleDemo.UrlFor("forest")), "森のデモ映像のファイル");
            demo.Show(true);
            yield return WaitUntil(() => demo.Showing, 20f, "デモ映像が流れる");
            Assert.AreEqual("forest", demo.Current, "はじめて遊ぶときは、森の映像");
            // 映像がすっかり出たら、うしろのエリアは描かない（映像がかくかくしないように）
            int mask = Camera.main.cullingMask;
            Assert.AreNotEqual(0, mask);
            yield return WaitUntil(() => demo.WorldHidden, 10f, "うしろのエリアを描くのを止める");
            Assert.AreEqual(0, Camera.main.cullingMask, "地形も物も描かない");
            Assert.IsFalse(GM.world.instanced.enabled, "草花も描かない");
            Assert.IsFalse(GM.creatures.enabled, "いきものも描かない");
            yield return StartNewGame();
            Assert.IsFalse(demo.Showing, "ゲームを始めたら、映像はしまう");
            Assert.IsFalse(demo.WorldHidden);
            Assert.AreEqual(mask, Camera.main.cullingMask, "ゲームを始めたら、またエリアを描く");
            Assert.IsTrue(GM.world.instanced.enabled);
            Assert.IsTrue(GM.creatures.enabled);
            GM.GoToTitle();
            yield return WaitUntil(() => demo.Showing, 20f, "タイトルへもどると、また流れる");
        }
    }
}

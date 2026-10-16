using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：タイトル画面のうしろに、行ったことのあるエリアのデモ映像が流れる（はじめて遊ぶときは森だけ）。
    /// ゲームを始めると止まり、タイトルへもどると、また流れる。
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
            yield return StartNewGame();
            Assert.IsFalse(demo.Showing, "ゲームを始めたら、映像はしまう");
            GM.GoToTitle();
            yield return WaitUntil(() => demo.Showing, 20f, "タイトルへもどると、また流れる");
        }
    }
}

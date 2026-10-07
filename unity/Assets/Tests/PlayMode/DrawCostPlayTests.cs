using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：描く量をへらす工夫（水が映るときだけ深さと色の写しを作る）。</summary>
    public class DrawCostPlayTests
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
        public IEnumerator SceneCopies_OnlyWhileWaterIsOnScreen()
        {
            Assert.Greater(WaterView.WaterCount, 0, "森の池が登録されている");
            var data = Camera.main.GetUniversalAdditionalCameraData();
            // 池（スタートの南）に背を向ける
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Frames(3);
            WaterView.Apply(Camera.main);   // 描画の直前と同じ判定（テストの実行中はカメラが描かれないので、ここで呼ぶ）
            Assert.IsFalse(WaterView.CopiesOn, "池が見えないときは、写しを作らない");
            Assert.AreEqual(CameraOverrideOption.Off, data.requiresDepthOption);
            Assert.AreEqual(CameraOverrideOption.Off, data.requiresColorOption);
            // 池のほうを向く
            Place(ForestLayout.Pond + new Vector2(0f, 12f), Vector3.back);
            yield return FaceCamera(Vector3.back);
            yield return Frames(3);
            WaterView.Apply(Camera.main);
            Assert.IsTrue(WaterView.CopiesOn, "池が見えるときは、写しを作る");
            Assert.AreEqual(CameraOverrideOption.On, data.requiresDepthOption);
            Assert.AreEqual(CameraOverrideOption.On, data.requiresColorOption);
        }
    }
}

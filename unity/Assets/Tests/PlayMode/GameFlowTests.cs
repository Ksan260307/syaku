using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：読み込み → タイトル → 探検 → メニュー → 保存と再開。</summary>
    public class GameFlowTests
    {
        [TearDown]
        public void TearDown()
        {
            ResetInput();
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Boot_ShowsTitleThenStartsTheGame()
        {
            yield return Boot();
            Assert.AreEqual(GameManager.GameState.Title, GM.State);
            Assert.IsTrue(UI.IsTitleVisible, "タイトルが表示されている");
            Assert.IsFalse(Worm.InputEnabled, "タイトル中は操作できない");
            Assert.IsTrue(WorldGenerator.Instance.IsGenerated);

            yield return StartNewGame();
            Assert.IsTrue(UI.IsHudVisible);
            Assert.IsTrue(Cam.titleMode == false);
            // スタート地点の名所が自動で発見され、バナーが出る
            yield return WaitUntil(() => Col.IsDiscovered(0), 4f, "目覚めの苔原の発見");
            yield return null;
            Assert.IsTrue(UI.IsBannerVisible);
            Assert.AreEqual(ForestLayout.Landmarks[0].name, UI.BannerTitle);
            Assert.AreEqual("1 / 9", UI.Root.Q<Label>("place-count").text);
            Assert.AreEqual($"0 / {Col.TotalDrops}", UI.Root.Q<Label>("drop-count").text);
        }

        [UnityTest]
        public IEnumerator Pause_FreezesTimeAndResumeRestores()
        {
            yield return Boot();
            yield return StartNewGame();
            GM.Pause();
            yield return null;
            Assert.AreEqual(GameManager.GameState.Paused, GM.State);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(UI.IsPauseOpen);
            Assert.IsFalse(Worm.InputEnabled);
            Vector3 before = Worm.CenterPosition;
            GameInput.VirtualMove = Vector2.up;
            yield return new WaitForSecondsRealtime(0.5f);
            GameInput.VirtualMove = Vector2.zero;
            TestUtilPM.AssertSame(before, Worm.CenterPosition, 1e-4f, "ポーズ中は動かない");

            GM.Resume();
            yield return null;
            Assert.AreEqual(GameManager.GameState.Playing, GM.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(UI.IsPauseOpen);
        }

        [UnityTest]
        public IEnumerator Map_OpensAndBlocksMovement()
        {
            yield return Boot();
            yield return StartNewGame();
            UI.ShowMap(true);
            yield return null;
            yield return null;
            Assert.IsTrue(UI.IsMapOpen);
            Assert.IsFalse(Worm.InputEnabled, "地図を開いている間は動かない");
            Assert.Greater(UI.Root.Q("bigmap").childCount, ForestLayout.Landmarks.Count, "名所のマーカーとラベル");
            UI.ShowMap(false);
            yield return null;
            yield return null;
            Assert.IsTrue(Worm.InputEnabled);
        }

        [UnityTest]
        public IEnumerator Howto_AndConfirm_CloseWithBack()
        {
            yield return Boot();
            yield return StartNewGame();
            UI.ShowHowto(true);
            yield return null;
            Assert.IsTrue(UI.IsHowtoOpen);
            Assert.IsTrue(UI.Back());
            Assert.IsFalse(UI.IsHowtoOpen);
            Assert.IsFalse(UI.Back(), "閉じるものがなければ false");
        }

        [UnityTest]
        public IEnumerator ToTitle_AndContinue_KeepsPosition()
        {
            yield return Boot();
            yield return StartNewGame();
            Vector3 p = Place(new Vector2(-6f, 8f), Vector3.right);
            yield return Frames(3);
            GM.SaveProgress();
            GM.GoToTitle();
            yield return null;
            Assert.AreEqual(GameManager.GameState.Title, GM.State);
            Assert.IsTrue(Cam.titleMode);
            Assert.IsFalse(Worm.InputEnabled);
            yield return ContinueGame();
            Assert.Less(Vector3.Distance(Worm.TailPoint, p), 0.5f, "保存した場所から再開");
        }

        [UnityTest]
        public IEnumerator Progress_PersistsAcrossReload()
        {
            yield return Boot();
            yield return StartNewGame();
            Vector3 p = Place(new Vector2(8f, 12f), Vector3.forward);
            Col.Discover(ForestLayout.Landmarks[4]);
            yield return Frames(3);
            GM.SaveProgress();

            yield return Boot(keepSave: true);
            Assert.IsTrue(SaveSystem.HasSave);
            yield return ContinueGame();
            Assert.Less(Vector3.Distance(Worm.TailPoint, p), 0.5f);
            Assert.IsTrue(Col.IsDiscovered(4), "見つけた名所が記録されている");
        }

        [UnityTest]
        public IEnumerator NewGame_ResetsProgress()
        {
            yield return Boot();
            yield return StartNewGame();
            Col.Discover(ForestLayout.Landmarks[7]);
            GM.SaveProgress();
            yield return Boot(keepSave: true);
            yield return StartNewGame();
            Assert.IsFalse(Col.IsDiscovered(7));
            Assert.AreEqual(0, Col.CollectedDrops);
            Assert.Less(Vector2.Distance(new Vector2(Worm.TailPoint.x, Worm.TailPoint.z), ForestLayout.Spawn), 1f, "はじまりの場所から");
        }

        [UnityTest]
        public IEnumerator Quality_SwitchesRenderSettings()
        {
            yield return Boot();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(urp);
            GM.ApplyQuality(0);
            Assert.AreEqual(0.8f, urp.renderScale, 1e-4f);
            Assert.AreEqual(1, urp.msaaSampleCount);
            GM.ApplyQuality(1);
            Assert.AreEqual(1f, urp.renderScale, 1e-4f);
            Assert.AreEqual(2, urp.msaaSampleCount);
        }

        [UnityTest]
        public IEnumerator Ambient_IsNotBlack()
        {
            yield return Boot();
            var sh = RenderSettings.ambientProbe;
            var dirs = new[] { Vector3.up, Vector3.down };
            var colors = new Color[2];
            sh.Evaluate(dirs, colors);
            Assert.Greater(colors[0].grayscale, 0.2f, "上からの環境光");
            Assert.Greater(colors[0].grayscale, colors[1].grayscale, "空の方が地面より明るい");
        }
    }

    static class TestUtilPM
    {
        public static void AssertSame(Vector3 a, Vector3 b, float tol, string msg)
        {
            Assert.LessOrEqual(Vector3.Distance(a, b), tol, $"{msg}: {a} vs {b}");
        }
    }
}

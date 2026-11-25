using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：遊びやすさの設定（音量・明るさ・ミニマップ・タッチ・字幕・タイトルの設定 など）。</summary>
    public class QualityOfLifePlayTests
    {
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
            SaveSystem.ResetSettings();
            UI.RefreshSettingsUI();
            UI.ApplySettings(false);
        }

        [UnityTest]
        public IEnumerator Settings_VolumeBrightnessMinimapTouchCaptionsAndZoomDirection()
        {
            var s = SaveSystem.Settings;
            s.master = 0.4f;
            float before = GM.PostExposure;
            s.brightness = 1f;
            s.minimapSize = 2;
            s.touchSize = 1;
            s.leftHanded = true;
            s.captions = true;
            UI.ApplySettings(false);
            yield return Frames(2);
            Assert.AreEqual(0.4f, AudioListener.volume, 1e-4f, "全体の音量");
            Assert.Greater(GM.PostExposure, before + 0.3f, "明るさ");
            Assert.AreEqual(1.25f, UI.Root.Q("minimap").resolvedStyle.scale.value.x, 1e-3f, "大きなミニマップ");
            Assert.IsTrue(UI.Root.Q("touch").ClassListContains("touch--large"));
            Assert.IsTrue(UI.Root.Q("touch").ClassListContains("touch--left"));
            StringAssert.Contains("40%", UI.Root.Q<Slider>("set-master").label, "スライダーに、いまの値");
            // 字幕：いきものの声が聞こえると、画面の下に出る（小さすぎる声は出さない）
            AudioManager.Instance.Croak(0.05f);
            Assert.AreEqual("", UI.CaptionText);
            AudioManager.Instance.Croak(0.6f);
            StringAssert.Contains("カエル", UI.CaptionText);
            SaveSystem.Settings.captions = false;
            yield return Seconds(2.8f);
            Assert.AreEqual("", UI.CaptionText, "しばらくすると消える");
            // ズームの向き
            s.invertZoom = true;
            GameInput.AddZoom(0.6f);
            yield return null;
            Assert.Less(GameInput.CameraZoom, 0f, "逆にすると、ホイールの向きが反対");
            AudioListener.volume = 1f;
        }

        [UnityTest]
        public IEnumerator ResetSettings_AsksFirst_AndCameraDistanceResets()
        {
            SaveSystem.Settings.music = 0.1f;
            UI.ShowPause(true);
            yield return Frames(2);
            var reset = UI.Root.Q<Button>("reset-settings");
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = reset; reset.SendEvent(e); }
            yield return Frames(2);
            Assert.IsTrue(UI.IsConfirmOpen, "もどす前に、たしかめる");
            Assert.AreEqual(0.1f, SaveSystem.Settings.music, 1e-4f, "まだもどさない");
            UI.Back();
            UI.ShowPause(false);
            // カメラの距離をもどす
            SaveSystem.Settings.cameraDistance = 6f;
            Cam.distance = 6f;
            var cr = UI.Root.Q<Button>("camera-reset");
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = cr; cr.SendEvent(e); }
            yield return Frames(2);
            Assert.AreEqual(0f, SaveSystem.Settings.cameraDistance, 1e-4f);
            Assert.Less(Cam.distance, 4f);
        }

        [UnityTest]
        public IEnumerator PhotoMode_TakesAPhoto_ChangesColors_AndHidesTheWorm()
        {
            GM.TogglePhoto();
            yield return Frames(2);
            Assert.IsTrue(UI.IsPhotoMode);
            float sat = GM.PostSaturation;
            GM.CyclePhotoFilter();   // セピア
            yield return null;
            Assert.AreEqual(1, GM.PhotoFilter);
            Assert.AreEqual(-100f, GM.PostSaturation, 1e-3f, "セピアは色をぬく");
            GM.TogglePhotoWorm();
            Assert.IsTrue(GM.PhotoWormHidden);
            foreach (var r in Worm.GetComponentsInChildren<Renderer>(true)) Assert.IsTrue(r.forceRenderingOff, "しゃくとりむしをかくす");
            Assert.IsTrue(GM.TakePhoto());
            Assert.IsTrue(System.IO.File.Exists(GM.LastPhotoPath), "写真のファイル");
            Assert.Greater(new System.IO.FileInfo(GM.LastPhotoPath).Length, 1000);
            System.IO.File.Delete(GM.LastPhotoPath);
            // 写真モードをおえると、もとにもどる
            GM.TogglePhoto();
            yield return Frames(2);
            Assert.IsFalse(UI.IsPhotoMode);
            Assert.AreEqual(0, GM.PhotoFilter);
            Assert.AreEqual(sat, GM.PostSaturation, 1e-3f);
            foreach (var r in Worm.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(r.forceRenderingOff);
        }

        [UnityTest]
        public IEnumerator Modes_ShowWhatIsSwitchedOn()
        {
            Assert.AreEqual("", UI.ModesText);
            UI.AutoWalking = true;
            StringAssert.Contains("おまかせ歩き", UI.ModesText);
            UI.AutoWalking = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Hints_NearbyDropsLowDropsStandBeaconAndNextArea()
        {
            var col = GM.collectibles;
            // 名所のまわりの、まだ取っていないしずく
            int near = 0;
            foreach (var lm in Areas.Forest.Landmarks) near = Mathf.Max(near, GM.NearbyDrops(lm));
            Assert.Greater(near, 0, "どこかの名所のそばには、しずくがある");
            // のこりが少ないとき：しずくのおしらせに、のこりの数
            for (int i = 2; i < col.TotalDrops; i++) SaveSystem.Data.drops.Add(col.DropSaveId(i));
            col.ApplySave();
            Place(new Vector2(col.DropPosition(0).x, col.DropPosition(0).z) + new Vector2(0.4f, 0f), Vector3.left);
            yield return WaitUntil(() => col.IsDropTaken(0), 10f, "しずくを取る");
            yield return Frames(2);
            Assert.IsTrue(UI.ToastHistory.Exists(t => t.text.Contains("のこり 1 こ")), "のこりの数を知らせる");
            // 背伸びをつづけると、のこりのしずくの目じるし
            Vector3 last = col.DropPosition(1);
            Place(new Vector2(last.x, last.z) + new Vector2(6f, 0f), Vector3.left);
            yield return Seconds(0.4f);
            GameInput.VirtualStand = true;
            yield return WaitUntil(() => GM.StandHintShown, 6f, "背伸びで目じるし");
            GameInput.VirtualStand = false;
            // めぐり終えたら、つぎのエリアをすすめる
            Assert.IsNotNull(GameManager.NextAreaToVisit(Areas.Forest), "となりのまだ行っていないエリア");
        }

        [UnityTest]
        public IEnumerator Notices_MergeRepeats_AndAreKeptInTheRecords_AndSaveExportImports()
        {
            int n = UI.ToastCount;
            UI.Toast("テストのおしらせ");
            UI.Toast("テストのおしらせ");
            Assert.AreEqual(n + 1, UI.ToastCount, "同じおしらせは、すぐにはふやさない");
            Assert.AreEqual("テストのおしらせ", UI.ToastHistory[0].text);
            UI.ShowCollection(true, "record");
            yield return Frames(3);
            string records = string.Join("|", UI.Root.Q("record-list").Query<Label>().ToList().Select(l => l.text));
            StringAssert.Contains("さいきんのおしらせ", records);
            StringAssert.Contains("テストのおしらせ", records);
            StringAssert.Contains("歩いたきょり", records);
            Assert.IsNotNull(UI.Root.Q<Button>("save-export"));
            UI.ShowCollection(false);
            // 記録の書き出し・読みこみ（エディタでは、ファイルに）
            SaveSystem.Data.swims = 3;
            GM.ExportSave();
            Assert.IsTrue(System.IO.File.Exists(GM.LastExportPath));
            SaveSystem.Data.swims = 0;
            GM.ImportSave();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Title, 60f, "読みこんだら、タイトルへ");
            Assert.AreEqual(3, SaveSystem.Data.swims, "読みこんだ記録");
            System.IO.File.Delete(GM.LastExportPath);
        }

        [UnityTest]
        public IEnumerator Map_PansWithTheRightStick_AndTheCameraStaysStill()
        {
            UI.ShowMap(true);
            yield return Frames(2);
            UI.SetMapZoom(2f);
            Vector2 before = UI.MapOrigin;
            float yaw = Cam.yaw;
            GameInput.AddLook(new Vector2(200f, 0f));
            yield return Frames(2);
            Assert.Greater(UI.MapOrigin.x, before.x, "右へ動かす");
            Assert.AreEqual(yaw, Cam.yaw, 1e-3f, "うしろのカメラは回らない");
            UI.ShowMap(false);
        }

        [UnityTest]
        public IEnumerator Title_OpensSettingsOnly_AndBackCloses()
        {
            GM.GoToTitle();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Title, 60f, "タイトルへ");
            yield return Seconds(0.5f);
            UI.ShowSettingsFromTitle(true);
            yield return Frames(3);
            Assert.IsTrue(UI.IsTitleSettingsOpen);
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q("resume").resolvedStyle.display, "「つづける」は出さない");
            Assert.AreEqual(DisplayStyle.Flex, UI.Root.Q("settings-close").resolvedStyle.display, "「とじる」");
            Assert.IsTrue(UI.Back());
            Assert.IsFalse(UI.IsPauseOpen);
            Assert.AreEqual(GameManager.GameState.Title, GM.State, "タイトルのまま");
        }
    }
}

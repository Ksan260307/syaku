using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：写真モード・ヒント・見出しの順番待ち・地図のヒント・設定の保存など、使い勝手の改善。</summary>
    public class ImprovementPlayTests
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
            Time.timeScale = 1f;
            GameManager.Assists = false;
            if (UI != null) UI.SetPhotoMode(false);
        }

        [UnityTest]
        public IEnumerator PhotoMode_HidesTheHudAndStopsMoving()
        {
            GM.TogglePhoto();
            yield return Frames(2);
            Assert.IsTrue(UI.IsPhotoMode);
            Assert.IsTrue(GM.PhotoMode);
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q("hud").resolvedStyle.display, "表示が消える");
            Assert.IsFalse(Worm.InputEnabled, "写真モード中は動かない");
            Vector3 tail = Worm.TailPoint;
            GameInput.VirtualMove = Vector2.up;
            yield return Seconds(1f);
            Assert.Less(Vector3.Distance(tail, Worm.TailPoint), 0.05f);
            ResetInput();
            // 画面をタップするともどる
            UI.OnLookTap(new Vector2(500, 300), true);
            yield return Frames(2);
            Assert.IsFalse(UI.IsPhotoMode);
            Assert.IsTrue(Worm.InputEnabled);
            Assert.AreEqual(DisplayStyle.Flex, UI.Root.Q("hud").resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator PhotoMode_FromThePauseMenu()
        {
            GM.Pause();
            yield return null;
            Assert.AreEqual(GameManager.GameState.Paused, GM.State);
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = UI.Root.Q<Button>("photo");
                UI.Root.Q<Button>("photo").SendEvent(e);
            }
            yield return Frames(3);
            Assert.AreEqual(GameManager.GameState.Playing, GM.State, "メニューを閉じて");
            Assert.IsTrue(UI.IsPhotoMode, "写真モードになる");
            Assert.IsFalse(UI.IsPauseOpen);
        }

        [UnityTest]
        public IEnumerator DoubleTap_RecentersTheCamera()
        {
            GameInput.VirtualRecenter = false;
            UI.OnLookTap(new Vector2(800, 400), true);
            Assert.IsFalse(GameInput.VirtualRecenter, "1回ではもどさない");
            UI.OnLookTap(new Vector2(805, 402), true);
            Assert.IsTrue(GameInput.VirtualRecenter, "すばやく2回でカメラを後ろへ");
            yield return Frames(2);
            Assert.IsFalse(GameInput.VirtualRecenter, "一度だけ使われる");
            // マウスのクリックでは何もしない
            UI.OnLookTap(new Vector2(800, 400), false);
            UI.OnLookTap(new Vector2(800, 400), false);
            Assert.IsFalse(GameInput.VirtualRecenter);
        }

        [UnityTest]
        public IEnumerator Tips_AreShownOnlyOnce()
        {
            int toasts = UI.ToastCount;
            Assert.IsTrue(UI.Tip("test-tip", "テストのヒント"));
            yield return null;
            Assert.Greater(UI.ToastCount, toasts);
            Assert.IsFalse(UI.Tip("test-tip", "テストのヒント"), "二度目は出さない");
            CollectionAssert.Contains(SaveSystem.Data.tipsShown, "test-tip");
        }

        [UnityTest]
        public IEnumerator WallTip_AppearsWhenClimbingWithAssists()
        {
            GameManager.Assists = true;
            Vector2 s = ForestLayout.Stump;
            Place(s + new Vector2(-12.5f, 0f), Vector3.right);
            yield return FaceCamera(Vector3.right);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.OnSteepSurface, 25f, "切り株の壁にとりつく");
            yield return Seconds(13f);   // はじめてから少したってから出る
            CollectionAssert.Contains(SaveSystem.Data.tipsShown, "wall", "壁のヒントが出た");
        }

        [UnityTest]
        public IEnumerator Banners_WaitTheirTurn()
        {
            var a = ForestLayout.Landmarks[1];
            var b = ForestLayout.Landmarks[2];
            yield return WaitUntil(() => !UI.IsBannerVisible, 8f, "最初の見出しが消える");
            UI.ShowBanner(a);
            UI.ShowBanner(b);
            Assert.AreEqual(b.name, UI.BannerTitle, "新しいものがすぐ出る");
            Assert.AreEqual(1, UI.QueuedBanners, "出たばかりだった見出しは順番待ち");
            yield return WaitUntil(() => UI.BannerTitle == a.name && UI.IsBannerVisible, 6f, "前の見出しがもう一度出る");
            Assert.AreEqual(0, UI.QueuedBanners);
        }

        [UnityTest]
        public IEnumerator CreatureCards_MergeWhenFoundTogether()
        {
            UI.ShowCreature(SpeciesCatalog.Get("ant"));
            UI.ShowCreature(SpeciesCatalog.Get("snail"));
            yield return null;
            Assert.AreEqual(SpeciesCatalog.Get("snail").name, UI.CreatureCardName, "最後に見つけたもの");
            StringAssert.Contains("ほかに", UI.CreatureCardSub);
        }

        [UnityTest]
        public IEnumerator Title_ShowsWhereYouLeftOff()
        {
            SaveSystem.Data.drops.Add(Col.DropSaveId(0));
            GM.GoToTitle();
            yield return Frames(2);
            StringAssert.Contains(Areas.Forest.DisplayName, UI.ContinueText, "つづきの場所");
            StringAssert.Contains($"しずく 1 / {Areas.TotalDrops}", UI.TitleProgressText);
            yield return ContinueGame();
        }

        [UnityTest]
        public IEnumerator PauseMenu_ShowsASummary()
        {
            GM.Pause();
            yield return null;
            StringAssert.Contains("達成度", UI.PauseSummaryText);
            StringAssert.Contains(Areas.Forest.Subtitle, UI.PauseSummaryText);
            GM.Resume();
        }

        [UnityTest]
        public IEnumerator Map_HintsWhereTheLastDropsAre()
        {
            UI.ShowMap(true);
            yield return null;
            Assert.AreEqual(0, UI.MapHintCount, "たくさん残っているうちは出さない");
            UI.ShowMap(false);
            for (int i = 3; i < Col.TotalDrops; i++) SaveSystem.Data.drops.Add(Col.DropSaveId(i));
            Col.ApplySave();
            UI.ShowMap(true);
            yield return null;
            Assert.AreEqual(3, UI.MapHintCount, "のこり3個の場所に丸");
            StringAssert.Contains("のこり 3 個", UI.Root.Q<Label>("map-summary").text);
            UI.ShowMap(false);
        }

        [UnityTest]
        public IEnumerator Zukan_HabitatTextStaysInsideTheWindow()
        {
            // 3 つのエリアの、たくさんの名所のまわりにいる：すみかの文が長くなる
            foreach (var lm in RiverLayout.Landmarks) SaveSystem.Data.habitats.Add($"ant|river|{lm.position.x:F1}|{lm.position.y:F1}|4.0|{lm.name}");
            foreach (var lm in ParkLayout.Landmarks) SaveSystem.Data.habitats.Add($"ant|park|{lm.position.x:F1}|{lm.position.y:F1}|4.0|{lm.name}");
            GM.creatures.Discover(SpeciesCatalog.Get("ant"));
            UI.ShowCollection(true, "zukan");
            UI.SelectSpecies("ant");
            yield return Frames(4);
            var detail = UI.Root.Q("zukan-detail");
            var area = UI.Root.Q<Label>("zukan-area");
            StringAssert.Contains("公園", area.text);
            Rect d = detail.worldBound, a = area.worldBound;
            Assert.GreaterOrEqual(a.xMin, d.xMin - 1f, "すみかの文が左にはみ出さない");
            Assert.LessOrEqual(a.xMax, d.xMax + 1f, "すみかの文が右にはみ出さない");
            Assert.Greater(a.height, area.resolvedStyle.fontSize * 1.8f, "長い文は折り返す");
            foreach (var b in UI.Root.Q("zukan-go").Query<Button>().ToList())
                Assert.LessOrEqual(b.worldBound.xMax, d.xMax + 1f, "移動のボタンも枠の中");
            var desc = UI.Root.Q<Label>("zukan-desc");
            Debug.Log($"ZUKAN detail={d} desc={desc.worldBound} body={detail.parent.worldBound} page={UI.Root.Q("page-zukan").worldBound}");
            Assert.LessOrEqual(desc.worldBound.yMax, detail.parent.worldBound.yMax + 1f, "説明が下にはみ出さない");
            UI.ShowCollection(false);
        }

        [UnityTest]
        public IEnumerator Zukan_CanBeFilteredByHabitat()
        {
            UI.ShowCollection(true, "zukan");
            yield return null;
            Assert.AreEqual(SpeciesCatalog.All.Count, UI.ZukanCardCount);
            UI.SetZukanFilter("川辺");
            int river = SpeciesCatalog.All.Count(sp => sp.areaLabel.Contains("川辺"));
            Assert.AreEqual(river, UI.ZukanCardCount);
            Assert.Less(river, SpeciesCatalog.All.Count);
            UI.SetZukanFilter("");
            Assert.AreEqual(SpeciesCatalog.All.Count, UI.ZukanCardCount);
            UI.ShowCollection(false);
        }

        [UnityTest]
        public IEnumerator Minimap_ShowsNearbyUndiscoveredCreatures()
        {
            Vector3 a = GM.creatures.PositionOf("snail", 0);
            Place(new Vector2(a.x, a.z) + new Vector2(6f, 0f), Vector3.forward);
            yield return Frames(3);
            if (!Creatures.IsDiscovered("snail"))
                Assert.Greater(UI.VisibleCreatureHints, 0, "近くの未発見のいきものに「？」");
            SaveSystem.Data.creatures.AddRange(SpeciesCatalog.All.Select(s => s.id).Where(id => !SaveSystem.Data.creatures.Contains(id)));
            yield return Frames(2);
            Assert.AreEqual(0, UI.VisibleCreatureHints, "全部見つけたら出ない");
        }

        [UnityTest]
        public IEnumerator Settings_SliderChangeIsSavedShortlyAfter()
        {
            const string key = "shakutori.settings.v1";
            var slider = UI.Root.Q<Slider>("set-sens");
            slider.value = 1.5f;
            Assert.AreEqual(1.5f, SaveSystem.Settings.sensitivity, 1e-4f);
            Assert.IsTrue(SaveSystem.SettingsDirty, "少しあとで保存");
            yield return Seconds(0.8f);
            Assert.IsFalse(SaveSystem.SettingsDirty);
            StringAssert.Contains("\"sensitivity\":1.5", PlayerPrefs.GetString(key));
            // 新しい設定項目もメニューにある
            var toggle = UI.Root.Q<Toggle>("set-sprinttoggle");
            Assert.IsNotNull(toggle);
            toggle.value = true;
            Assert.IsTrue(SaveSystem.Settings.sprintToggle);
            toggle.value = false;
            var help = UI.Root.Q<Toggle>("set-help");
            help.value = false;
            Assert.IsFalse(SaveSystem.Settings.showHelp);
            help.value = true;
        }

        [UnityTest]
        public IEnumerator CameraDistance_IsRememberedAcrossStarts()
        {
            SaveSystem.Settings.cameraDistance = 5.2f;
            GM.GoToTitle();
            yield return Frames(2);
            yield return ContinueGame();
            Assert.AreEqual(5.2f, Cam.distance, 0.01f);
        }

        [UnityTest]
        public IEnumerator PageHidden_SavesAndPauses()
        {
            float before = SaveSystem.LastSaveTime;
            GM.OnPageHidden();
            Assert.AreEqual(GameManager.GameState.Paused, GM.State, "タブを切りかえたら止める");
            Assert.Greater(SaveSystem.LastSaveTime, before, "セーブする");
            Assert.IsTrue(AudioListener.pause);
            GM.OnPageVisible();
            Assert.IsFalse(AudioListener.pause);
            GM.Resume();
            yield return null;
            Assert.AreEqual(GameManager.GameState.Playing, GM.State);
        }

        [UnityTest]
        public IEnumerator Credits_OpenFromTitleAndCloseWithBack()
        {
            GM.GoToTitle();
            yield return Frames(2);
            UI.ShowCredits(true);
            Assert.IsTrue(UI.IsCreditsOpen);
            StringAssert.Contains("Noto Sans JP", UI.Root.Q<Label>("credits-text").text);
            Assert.IsTrue(UI.Back());
            Assert.IsFalse(UI.IsCreditsOpen);
            yield return ContinueGame();
        }

        [UnityTest]
        public IEnumerator Blocked_ExplainsTheReason()
        {
            // 森の池のふち：水へ向かって進むと「水の中へは入れない」
            Vector2 p = new Vector2(ForestLayout.Pond.x + 7f, ForestLayout.Pond.y + 17f);
            Place(p, Vector3.back);
            yield return FaceCamera(Vector3.back);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.IsBlocked, 20f, "池のふちで止まる");
            yield return Frames(2);
            Assert.AreEqual("water", Worm.BlockReason);
            StringAssert.Contains("水", UI.PromptText);
        }
    }
}

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：地図のエリアのボタン（地図を切りかえる）と、図鑑のすみかの地図。</summary>
    public class MapZukanTests
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
        public IEnumerator ScrollingTheMapOrZukan_DoesNotZoomTheCamera()
        {
            // 地図・図鑑を開いているあいだの、マウスのホイールや 2 本指のピンチは、画面のスクロールのため（カメラは動かない）
            float before = Cam.distance;
            foreach (bool map in new[] { true, false })
            {
                if (map) UI.ShowMap(true); else UI.ToggleCollection();
                yield return Frames(2);
                Assert.IsTrue(UI.AnyOverlayOpen);
                for (int i = 0; i < 20; i++)
                {
                    GameInput.AddZoom(map ? 0.6f : -0.6f);
                    yield return null;
                }
                Assert.AreEqual(before, Cam.distance, 1e-4f, (map ? "地図" : "図鑑") + "をスクロールしても、カメラは拡大・縮小しない");
                if (map) UI.ShowMap(false); else UI.ToggleCollection();
                yield return Frames(2);
                Assert.IsFalse(UI.AnyOverlayOpen);
            }
            // 画面をとじると、ホイールで、カメラが近づく
            for (int i = 0; i < 10; i++)
            {
                GameInput.AddZoom(0.6f);
                yield return null;
            }
            Assert.Less(Cam.distance, before - 0.2f, "画面をとじたあとは、カメラを拡大できる");
        }

        [UnityTest]
        public IEnumerator Map_ZoomsPansCentersAndShowsProgress()
        {
            var col = GM.collectibles;
            for (int i = 0; i < 4; i++) SaveSystem.Data.drops.Add(col.DropSaveId(i));
            col.ApplySave();
            UI.ShowMap(true);
            yield return Frames(3);
            Assert.AreEqual(1f, UI.MapZoom, 1e-4f, "開いたときは、ぜんぶ見える");
            Assert.AreEqual(4, UI.MapTakenCount, "集めたしずくの場所に点");
            StringAssert.StartsWith("いきもの ", UI.Root.Q<Label>("map-creatures").text, "このエリアのいきものの数");
            Assert.AreEqual(1, UI.AreaChipBarCount, "行ったことのあるエリアのボタンに、達成の棒");
            // 拡大すると、まん中が動かずに大きくなり、ドラッグのかわりに動かせる。拡大は 3 倍まで
            UI.SetMapZoom(2f);
            Assert.AreEqual(2f, UI.MapZoom, 1e-4f);
            Assert.AreEqual(0.25f, UI.MapOrigin.x, 1e-3f, "まん中を中心に拡大");
            UI.PanMap(new Vector2(0.9f, -1f));
            Assert.AreEqual(0.5f, UI.MapOrigin.x, 1e-3f, "地図のはしより外へは動かない");
            Assert.AreEqual(0f, UI.MapOrigin.y, 1e-3f);
            UI.SetMapZoom(10f);
            Assert.AreEqual(GameUI.MapZoomMax, UI.MapZoom, 1e-4f);
            UI.SetMapZoom(0.2f);
            Assert.AreEqual(1f, UI.MapZoom, 1e-4f);
            // いまここ：しゃくとりむしが、まん中に
            UI.CenterMapOnWorm();
            Vector2 me = GameUI.MapPoint(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z));
            Vector2 mid = UI.MapOrigin + Vector2.one * (0.5f / UI.MapZoom);
            Assert.Greater(UI.MapZoom, 1.5f);
            Assert.Less(Vector2.Distance(Vector2.Max(Vector2.Min(me, Vector2.one - Vector2.one * (0.5f / UI.MapZoom)), Vector2.one * (0.5f / UI.MapZoom)), mid), 1e-3f, "いまいる所がまん中（地図のはしでなければ）");
            // 拡大していても、見つけた名所をタップすると移動できる（印の場所で）
            UI.ShowMap(false);
            yield return Frames(2);
            // PageDown・RB で、行ったことのあるエリアの地図へ
            SaveSystem.Data.visited.Add("river");
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.IsTrue(UI.CycleAreaMap(1));
            Assert.AreEqual("river", UI.MapShowing);
            Assert.IsTrue(UI.CycleAreaMap(1));
            Assert.AreEqual("forest", UI.MapShowing, "まわって、もどる");
            UI.ShowMap(false);
        }

        [UnityTest]
        public IEnumerator Zukan_CountsUnfoundFilterStampsNewFirstSkinProgressRecordsAndTabs()
        {
            var ant = SpeciesCatalog.Get("ant");
            GM.creatures.Discover(ant);
            UI.ShowCollection(true, "zukan");
            yield return Frames(3);
            string latestNew = SaveSystem.Data.creatures.Last(id => !SaveSystem.Data.seenCreatures.Contains(id));
            Assert.AreEqual(latestNew, UI.ZukanScrolledTo, "さいごに見つけた NEW のいきもののカードまでスクロール");
            Assert.IsTrue(UI.HasNewBadge(latestNew), "えらぶまでは NEW のまま");
            UI.SelectSpecies("ant");
            var chips = UI.Root.Query<Button>(className: "zukan-filter").ToList();
            StringAssert.StartsWith("すべて ", chips[0].text);
            StringAssert.Contains("/", chips[0].text, "見つけた数 / いる数");
            StringAssert.StartsWith("まだ ", chips[1].text);
            UI.SetZukanFilter(GameUI.Unfound);
            yield return Frames(2);
            Assert.IsFalse(UI.ZukanCardIds.Contains("ant"), "「まだ」には、見つけたいきものは出ない");
            Assert.Greater(UI.ZukanCardIds.Count, 10);
            UI.SetZukanFilter("");
            yield return Frames(2);
            Assert.IsNotNull(UI.Root.Q(className: "zukan-card-stamps"), "見つけたいきものに、しぐさの点");
            // きせかえ：まだ使えないものには、いまの数
            var locked = Skins.All.First(sk => !Skins.IsUnlocked(sk, ProgressStats.FromSave()) && !sk.needAll);
            StringAssert.StartsWith("（いま ", GameUI.SkinProgress(locked, ProgressStats.FromSave()));
            // きろく：あたらしい数
            SaveSystem.Data.swims = 2;
            Assert.IsTrue(SaveSystem.Data.MarkPlayDay(new System.DateTime(2026, 11, 1)) || SaveSystem.Data.playDays > 0);
            Assert.IsFalse(SaveSystem.Data.MarkPlayDay(new System.DateTime(2026, 11, 1, 20, 0, 0)), "同じ日は数えない");
            int days = SaveSystem.Data.playDays;
            Assert.IsTrue(SaveSystem.Data.MarkPlayDay(new System.DateTime(2026, 11, 2)));
            Assert.AreEqual(days + 1, SaveSystem.Data.playDays);
            // PageDown・RB で、きせかえ → きろく
            UI.CycleCollectionTab(1);
            Assert.AreEqual("skin", UI.CollectionTab);
            UI.CycleCollectionTab(1);
            Assert.AreEqual("record", UI.CollectionTab);
            yield return Frames(2);
            string records = string.Join("|", UI.Root.Q("record-list").Query<Label>().ToList().Select(l => l.text));
            StringAssert.Contains("泳いだ回数|2 回", records);
            StringAssert.Contains("森のいきもの", records);
            StringAssert.Contains("遊んだ日", records);
            UI.CycleCollectionTab(1);
            Assert.AreEqual("zukan", UI.CollectionTab, "まわって、もどる");
            UI.ShowCollection(false);
        }

        [UnityTest]
        public IEnumerator WorldNotices_DoNotCoverTheOpenMap()
        {
            // 遊んでいる所のおしらせ（観察など）は、地図を開いている間はかくれて、地図にかぶさらない。地図でした操作への返事は出る
            UI.ShowMap(true);
            yield return Frames(2);
            UI.Toast("かんさつ：テスト", "icon-book", 3f);
            UI.UiToast("地図の返事", "icon-lock");
            yield return Frames(3);
            CollectionAssert.DoesNotContain(UI.VisibleToastTexts, "かんさつ：テスト", "地図の上に、観察のおしらせが出ない");
            CollectionAssert.Contains(UI.VisibleToastTexts, "地図の返事", "地図の操作への返事は出る");
            UI.ShowMap(false);
            yield return Frames(3);
            CollectionAssert.Contains(UI.VisibleToastTexts, "かんさつ：テスト", "地図をとじると、おしらせが見える");
        }

        [UnityTest]
        public IEnumerator HidingThePage_WithTheMapOpen_ShowsOnlyTheMenu()
        {
            // 地図を開いたままタブを切りかえると、地図をとじてからメニューを出す（地図とメニューが重ならない）
            UI.ShowMap(true);
            yield return Frames(2);
            GM.OnPageHidden();
            yield return Frames(2);
            Assert.IsTrue(UI.IsPauseOpen, "メニューが開く");
            Assert.IsFalse(UI.IsMapOpen, "地図はとじる");
            GM.OnPageVisible();
            GM.Resume();
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator AreaButtons_SwitchTheMap_AndLandmarksThereTravel()
        {
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.AreEqual("forest", UI.MapShowing, "開いたときは、いまいるエリアの地図");
            int toasts = UI.ToastCount;
            Assert.IsTrue(UI.PressAreaChip(1));
            Assert.AreEqual("forest", UI.MapShowing, "まだ行っていないエリアの地図は見られない");
            Assert.Greater(UI.ToastCount, toasts);

            // 行ったことのあるエリアのボタンを押すと、そのエリアの地図に切りかわる（移動はしない）
            SaveSystem.Data.visited.Add("river");
            var bank = RiverLayout.Landmarks[0];   // せせらぎの岸
            SaveSystem.Data.places.Add(bank.id);
            Assert.IsTrue(UI.PressAreaChip(1));
            yield return Frames(2);
            Assert.AreEqual("river", UI.MapShowing);
            Assert.AreEqual("川辺の地図", UI.Root.Q<Label>("map-title").text);
            Assert.IsTrue(UI.IsMapOpen, "地図は開いたまま");
            Assert.AreSame(Areas.Forest, Areas.Current, "移動はしない");
            Texture2D river = GM.world.MapFor(Areas.River), forest = GM.world.MapFor(Areas.Forest);
            Assert.IsNotNull(river);
            Assert.AreNotSame(forest, river, "川辺の地図の絵");
            Assert.AreSame(river, GM.world.MapFor(Areas.River), "一度描いた地図は覚えておく");
            // 森のボタンで、もどる
            Assert.IsTrue(UI.PressAreaChip(0));
            Assert.AreEqual("forest", UI.MapShowing);
            // 川辺の地図の、見つけた名所をタップすると、川辺のその名所へ移動する
            Assert.IsTrue(UI.ShowAreaMap("river"));
            Assert.IsTrue(UI.TapBigMap(GameUI.MapPoint(bank.position)), "ほかのエリアの名所へ");
            Assert.IsFalse(UI.IsMapOpen);
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), bank.position), bank.radius * 1.6f + 3f, "名所のそば");
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.AreEqual("river", UI.MapShowing, "移動したあとは、そのエリアの地図");
        }

        [UnityTest]
        public IEnumerator Zukan_ShowsTheHabitatMapOfTheAreaWhereItLives_AndScrolls()
        {
            // 森のいきもの（コウモリ）を、川辺から図鑑で見る：森の地図に、すみかの印
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            GM.creatures.Discover(SpeciesCatalog.Get("koumori"));
            UI.ShowCollection(true, "zukan");
            yield return Frames(2);
            UI.SelectSpecies("koumori");
            yield return Frames(3);
            Assert.AreEqual("森の地図", UI.ZukanHabitatArea, "すんでいるエリアの地図");
            Assert.Greater(UI.HabitatDotCount, 0, "すみかの印");
            // 川辺にもいるいきものは、いまいるエリアの地図で
            GM.creatures.Discover(SpeciesCatalog.Get("frog"));
            UI.SelectSpecies("frog");
            yield return Frames(3);
            Assert.AreEqual("川辺の地図", UI.ZukanHabitatArea);
            // くわしい説明はスクロールする枠の中で、とじるボタンに重ならない
            var detail = UI.Root.Q<ScrollView>("zukan-detail");
            var close = UI.Root.Q<Button>("collection-close");
            Assert.IsNotNull(detail, "スクロールできる");
            yield return Frames(2);
            Assert.IsFalse(detail.worldBound.Overlaps(close.worldBound), "とじるボタンに重ならない");
            // すみかの地図までスクロールすると、枠の中に見える
            var habitat = UI.Root.Q("zukan-habitat");
            detail.ScrollTo(habitat);
            yield return Frames(3);
            Assert.IsTrue(detail.worldBound.Contains(habitat.worldBound.center), "スクロールすると、すみかの地図が見える");
            UI.ShowCollection(false);
        }

        [UnityTest]
        public IEnumerator TouchSilkButton_ShowsWhatItDoes()
        {
            UI.ForceTouch = true;
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Frames(3);
            Assert.IsTrue(UI.SilkButtonIdle, "平らな所では、うすく");
            Assert.AreEqual("がけのふちで糸", UI.SilkButtonText, "どこで使うボタンかを書く");
            var top = UI.Root.Q<Label>("btn-silk-top");
            Assert.AreEqual(DisplayStyle.Flex, top.resolvedStyle.display);
            UI.ForceTouch = null;
        }

        [UnityTest]
        public IEnumerator Zukan_HasNoCountLine_AndFiltersSitBelowTheTabs()
        {
            UI.ShowCollection(true, "zukan");
            yield return Frames(4);
            var page = UI.Root.Q("page-zukan");
            // 「見つけたいきもの ○/○ 近づくと図鑑に登録されます」の行は出さない
            Assert.IsNull(page.Q("zukan-count"));
            page.Query<Label>().ForEach(l => StringAssert.DoesNotContain("近づくと図鑑に登録", l.text));
            // すみかのしぼりこみは、上のタブに重ならない
            var tabs = UI.Root.Q(className: "tabs");
            var filters = page.Q(className: "zukan-filters");
            Assert.IsNotNull(filters);
            Assert.GreaterOrEqual(filters.worldBound.yMin, tabs.worldBound.yMax - 0.5f, "しぼりこみはタブの下");
            tabs.Query<Button>().ForEach(t => Assert.LessOrEqual(t.worldBound.yMax, filters.worldBound.yMin + 0.5f, "タブがしぼりこみにかぶらない: " + t.name));
            UI.ShowCollection(false);
        }

        [UnityTest]
        public IEnumerator Records_StayInsideTheWindow()
        {
            UI.ShowCollection(true, "record");
            yield return Frames(4);
            var list = UI.Root.Q<ScrollView>("record-list");
            Assert.IsNotNull(list, "きろくはスクロールできる");
            Assert.Greater(UI.RecordRowCount, 15);
            var close = UI.Root.Q<Button>("collection-close");
            var window = UI.Root.Q(className: "collection-window");
            Assert.IsFalse(list.worldBound.Overlaps(close.worldBound), "とじるボタンに重ならない");
            Assert.LessOrEqual(list.worldBound.yMax, window.worldBound.yMax + 0.5f, "ウィンドウの背景からはみ出さない");
            UI.ShowCollection(false);
        }
    }
}

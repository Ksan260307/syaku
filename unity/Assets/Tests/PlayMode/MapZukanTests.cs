using System.Collections;
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

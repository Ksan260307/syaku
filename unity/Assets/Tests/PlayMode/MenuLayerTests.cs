using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：メニューから開く画面の重なり順。</summary>
    public class MenuLayerTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.2f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        static bool IsInside(VisualElement e, VisualElement root)
        {
            for (; e != null; e = e.parent) if (e == root) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator Wardrobe_FromTheMenuIsShownInFront()
        {
            GM.Pause();
            yield return null;
            Assert.IsTrue(UI.IsPauseOpen);
            var button = UI.Root.Q<Button>("pause-collection");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
            yield return Frames(3);
            UI.SelectTab("skin");
            yield return Frames(3);
            Assert.IsTrue(UI.IsCollectionOpen);
            Assert.IsTrue(UI.IsPauseOpen, "メニューは後ろに残る");
            var overlay = UI.Root.Q("collection-overlay");
            var window = overlay.Q(className: "collection-window");
            // きせかえの窓の真ん中・四すみの近くをさわると、メニューではなく、きせかえの画面に当たる
            Rect r = window.worldBound;
            foreach (var p in new[] { r.center, r.min + r.size * 0.15f, r.max - r.size * 0.15f })
            {
                var picked = UI.Root.panel.Pick(p);
                Assert.IsTrue(IsInside(picked, overlay), $"きせかえの画面が前に出ている（{p} で {picked?.name} に当たった）");
            }
            var skins = UI.Root.Q("skin-grid");
            Assert.Greater(skins.childCount, 0);
            Assert.IsTrue(IsInside(UI.Root.panel.Pick(skins[0].worldBound.center), skins[0]), "きせかえを選べる");
            // とじると、メニューにもどる
            UI.ShowCollection(false);
            yield return null;
            Assert.IsTrue(UI.IsPauseOpen);
            Assert.IsTrue(IsInside(UI.Root.panel.Pick(UI.Root.Q<Button>("resume").worldBound.center), UI.Root.Q("pause-overlay")));
        }
    }
}

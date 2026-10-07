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

        /// <summary>target を、左上のすみの近くでタップする（押して、はなす）。</summary>
        static void Tap(VisualElement target, Vector2 at)
        {
            var ev = new Event { type = EventType.MouseDown, mousePosition = at, button = 0 };
            using (var d = PointerDownEvent.GetPooled(ev))
            {
                d.target = target;
                target.SendEvent(d);
            }
            ev = new Event { type = EventType.MouseUp, mousePosition = at, button = 0 };
            using (var u = PointerUpEvent.GetPooled(ev))
            {
                u.target = target;
                target.SendEvent(u);
            }
        }

        [UnityTest]
        public IEnumerator Grass_IsNotRecountedWhileTheCameraIsStill()
        {
            var inst = GM.world.instanced;
            Cam.enabled = false;   // カメラを止める
            yield return Frames(3);
            int before = inst.RebuildCount;
            yield return Frames(20);
            Assert.AreEqual(before, inst.RebuildCount, "カメラが止まっていれば、草や小物を数えなおさない");
            Cam.transform.position += Cam.transform.forward * 2f;
            yield return Frames(2);
            Assert.Greater(inst.RebuildCount, before, "カメラが動けば、数えなおす");
            Cam.enabled = true;
        }

        [UnityTest]
        public IEnumerator Windows_CloseWhenTappingOutside()
        {
            var map = UI.Root.Q("map-overlay");
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.IsTrue(UI.IsMapOpen);
            // 窓の中をタップしても閉じない
            var window = map.Q(className: "window");
            Tap(window, window.worldBound.center);
            yield return Frames(2);
            Assert.IsTrue(UI.IsMapOpen, "窓の中では閉じない");
            Tap(map, map.worldBound.position + Vector2.one * 4f);
            yield return Frames(2);
            Assert.IsFalse(UI.IsMapOpen, "地図：窓の外で閉じる");

            UI.ShowCollection(true, "zukan");
            yield return Frames(2);
            Tap(UI.Root.Q("collection-overlay"), Vector2.one * 4f);
            yield return Frames(2);
            Assert.IsFalse(UI.IsCollectionOpen, "図鑑：窓の外で閉じる");

            UI.ShowHowto(true);
            yield return Frames(2);
            Tap(UI.Root.Q("howto-overlay"), Vector2.one * 4f);
            yield return Frames(2);
            Assert.IsFalse(UI.IsHowtoOpen, "あそびかた：窓の外で閉じる");

            GM.Pause();
            yield return Frames(2);
            Assert.IsTrue(UI.IsPauseOpen);
            Tap(UI.Root.Q("pause-overlay"), Vector2.one * 4f);
            yield return Frames(3);
            Assert.IsFalse(UI.IsPauseOpen, "メニュー：窓の外で閉じて、つづける");
            Assert.AreEqual(GameManager.GameState.Playing, GM.State);
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

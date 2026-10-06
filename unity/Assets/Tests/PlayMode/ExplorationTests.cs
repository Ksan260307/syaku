using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：探索の目的（しずく集め・名所の発見・クリア）。</summary>
    public class ExplorationTests
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

        int NearestDrop(Vector3 from)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < Col.TotalDrops; i++)
            {
                if (Col.IsDropTaken(i)) continue;
                float d = Vector3.Distance(from, Col.DropPosition(i));
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        [UnityTest]
        public IEnumerator Dewdrop_IsCollectedByTouching()
        {
            int id = NearestDrop(Worm.CenterPosition);
            Assert.GreaterOrEqual(id, 0);
            Vector3 p = Col.DropPosition(id);
            int before = Col.CollectedDrops;
            int toasts = UI.ToastCount;
            Worm.Spawn(p, Vector3.forward);
            yield return WaitUntil(() => Col.IsDropTaken(id), 3f, "しずくを取る");
            Assert.AreEqual(before + 1, Col.CollectedDrops);
            CollectionAssert.Contains(SaveSystem.Data.drops, id, "自動保存される");
            Assert.Greater(UI.ToastCount, toasts, "取ったことが通知される");
            yield return Frames(2);
            Assert.AreEqual($"{Col.CollectedDrops} / {Col.TotalDrops}", UI.Root.Q<Label>("drop-count").text);
        }

        [UnityTest]
        public IEnumerator Dewdrop_WalkingIntoItCollectsIt()
        {
            // スタート直後の目の前にあるしずくまで歩いて取る
            int id = NearestDrop(Worm.CenterPosition);
            Vector3 drop = Col.DropPosition(id);
            Vector3 dir = Vector3.ProjectOnPlane(drop - Worm.CenterPosition, Vector3.up).normalized;
            Place(new Vector2(drop.x, drop.z) - new Vector2(dir.x, dir.z) * 2.5f, dir);
            yield return FaceCamera(dir);
            yield return Hold(Vector2.up, 8f, () => { });
            Assert.IsTrue(Col.IsDropTaken(id), "歩いてしずくを取れる");
        }

        [UnityTest]
        public IEnumerator Landmark_DiscoveredOnArrival()
        {
            var grove = ForestLayout.Landmarks[3];
            Place(grove.position + new Vector2(-2f, -11f), Vector3.forward);
            yield return WaitUntil(() => Col.IsDiscovered(3), 3f, "赤キノコの森の発見");
            yield return null;
            Assert.IsTrue(UI.IsBannerVisible);
            Assert.AreEqual(grove.name, UI.BannerTitle);
            CollectionAssert.Contains(SaveSystem.Data.places, 3);
        }

        [UnityTest]
        public IEnumerator Summit_RequiresClimbingToTheTop()
        {
            var summit = ForestLayout.Landmarks[6];
            // 切り株の外（地面）では発見されない
            Place(summit.position + new Vector2(0f, -12f), Vector3.forward);
            yield return Seconds(0.5f);
            Assert.IsFalse(Col.IsDiscovered(6));
            // てっぺんに立つと発見
            Place(summit.position + new Vector2(1f, 1f), Vector3.forward);
            yield return WaitUntil(() => Col.IsDiscovered(6), 3f, "切り株の頂の発見");
        }

        [UnityTest]
        public IEnumerator HollowLog_DiscoveredInsideTunnel()
        {
            var lm = ForestLayout.Landmarks[8];
            Vector3 mid = (lm.capsuleA + lm.capsuleB) * 0.5f;
            Assert.IsTrue(Physics.Raycast(mid + Vector3.up * 1.5f, Vector3.down, out var floor, 5f, ShakuConst.SurfaceMask));
            Vector3 along = (lm.capsuleB - lm.capsuleA).normalized;
            Worm.Spawn(floor.point, along);
            yield return WaitUntil(() => Col.IsDiscovered(8), 3f, "丸太のトンネルの発見");
        }

        [UnityTest]
        public IEnumerator Minimap_ShowsNearbyUnknownPlacesAndDrops()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return Frames(5);
            var image = UI.Root.Q("minimap-image");
            int visibleDots = 0;
            foreach (var c in image.Children())
                if (c.ClassListContains("map-dot") && c.resolvedStyle.display == DisplayStyle.Flex) visibleDots++;
            Assert.Greater(visibleDots, 0, "近くのしずくが点で表示される");
        }

        [UnityTest]
        public IEnumerator Everything_LeadsToTheEnding()
        {
            for (int i = 0; i < Col.TotalDrops; i++)
            {
                if (Col.IsDropTaken(i)) continue;
                Worm.Spawn(Col.DropPosition(i), Vector3.forward);
                yield return WaitUntil(() => Col.IsDropTaken(i), 3f, $"しずく #{i}");
            }
            Assert.AreEqual(Col.TotalDrops, Col.CollectedDrops);
            foreach (var lm in ForestLayout.Landmarks) Col.Discover(lm);
            Assert.AreEqual(Col.TotalPlaces, Col.DiscoveredPlaces);
            yield return WaitUntil(() => UI.IsCompleteOpen, 6f, "クリア画面");
            Assert.IsTrue(SaveSystem.Data.completed);
            StringAssert.Contains("45", UI.Root.Q<Label>("complete-text").text);
        }
    }
}

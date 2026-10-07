using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：森と川辺のあいだの移動（トンネル・地図・保存）。</summary>
    public class AreaTravelTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.2f);
        }

        [TearDown]
        public void TearDown()
        {
            ResetInput();
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Tunnel_WalkingInLeadsToTheRiver()
        {
            var gate = GM.world.Gates[0];
            Assert.AreEqual("river", gate.def.targetArea);
            Vector3 outward = -gate.inward;
            Vector3 start = gate.position + gate.inward * 4f;
            Place(new Vector2(start.x, start.z), outward);
            yield return FaceCamera(outward);
            yield return Frames(3);
            StringAssert.Contains("せせらぎの小川", UI.PromptText, "トンネルの近くでヒントが出る");

            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => GM.State == GameManager.GameState.Traveling, 20f, "トンネルに入る");
            ResetInput();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 90f, "川辺に着く");

            Assert.AreSame(Areas.River, Areas.Current);
            Assert.AreSame(Areas.River, Col.Area);
            Assert.AreEqual(30, Col.TotalDrops);
            Assert.AreEqual("river", SaveSystem.Data.area);
            CollectionAssert.Contains(SaveSystem.Data.visited, "river");
            Assert.AreEqual("せせらぎの小川", UI.AreaTitle);
            Assert.IsTrue(UI.IsBannerVisible);
            Assert.AreEqual("せせらぎの小川", UI.BannerTitle);
            Assert.AreEqual("川のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.AreEqual($"0 / {RiverLayout.Landmarks.Count}", UI.Root.Q<Label>("place-count").text);

            Areas.River.ArrivalFrom("forest", out var arrive, out var fwd);
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), arrive), 2.5f, "トンネルの出口に出る");
            Assert.Greater(GM.creatures.CountOf("waterstrider"), 0, "川のいきものがいる");
            Assert.IsTrue(Worm.InputEnabled, "すぐに動ける");

            // 着いた直後に、また戻ってしまわない
            yield return Seconds(1.5f);
            Assert.AreSame(Areas.River, Areas.Current);
        }

        [UnityTest]
        public IEnumerator Map_FastTravelsOnlyToVisitedAreas()
        {
            int toasts = UI.ToastCount;
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.AreEqual(2, UI.AreaChipCount);
            Assert.IsFalse(UI.RequestTravel("river"), "まだ行っていないエリアには地図から行けない");
            Assert.Greater(UI.ToastCount, toasts);
            Assert.IsTrue(UI.IsMapOpen);
            Assert.AreSame(Areas.Forest, Areas.Current);
            Assert.IsFalse(UI.RequestTravel("forest"), "今いるエリア");
            Assert.IsFalse(UI.RequestTravel("moon"), "知らないエリア");

            SaveSystem.Data.visited.Add("river");
            Assert.IsTrue(UI.RequestTravel("river"));
            Assert.IsFalse(UI.IsMapOpen, "移動するときは地図を閉じる");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");

            UI.ShowMap(true);
            yield return Frames(2);
            Assert.AreEqual("川辺の地図", UI.Root.Q<Label>("map-title").text);
            Assert.IsTrue(UI.RequestTravel("forest"));
            yield return WaitUntil(() => Areas.Current == Areas.Forest && GM.State == GameManager.GameState.Playing, 90f, "森へ");
            Areas.Forest.ArrivalFrom("river", out var arrive, out _);
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), arrive), 2.5f);
            Assert.AreEqual("しゃくとりの森", UI.AreaTitle);
            Assert.AreEqual(45, Col.TotalDrops);
        }

        [UnityTest]
        public IEnumerator Continue_ReturnsToTheSavedArea()
        {
            SaveSystem.Data.visited.Add("river");
            Assert.IsTrue(GM.TravelTo("river"));
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            Place(RiverLayout.Spawn + new Vector2(3f, 2f), Vector3.right);
            yield return Seconds(0.3f);
            GM.SaveProgress();
            Vector3 saved = Worm.CenterPosition;

            yield return Boot(keepSave: true);
            Assert.AreSame(Areas.River, Areas.Current, "保存したエリアを読み込む");
            yield return ContinueGame();
            Assert.Less(Vector3.Distance(saved, Worm.CenterPosition), 1.5f);
            Assert.AreEqual("せせらぎの小川", UI.AreaTitle);
        }

        [UnityTest]
        public IEnumerator NewGame_FromTheRiverStartsInTheForest()
        {
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            GM.GoToTitle();
            yield return Frames(2);
            GM.StartGame(false);
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled && Areas.Current == Areas.Forest, 90f, "森からやり直す");
            Assert.AreSame(Areas.Forest, Col.Area);
            CollectionAssert.AreEquivalent(new[] { "forest" }, SaveSystem.Data.visited);
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), ForestLayout.Spawn), 3f);
        }
    }

    /// <summary>総合テスト：川辺の遊び場（舟・とびいし・川のいきもの）。</summary>
    public class RiverPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        [UnityTest]
        public IEnumerator Ferry_CarriesTheWormToTheIsland()
        {
            var f = GM.world.Ferry;
            Assert.IsNotNull(f);
            f.SetClock(0.5f);
            Vector3 c = f.GetComponent<Collider>().bounds.center;
            Assert.IsTrue(Physics.Raycast(c + Vector3.up * 5f, Vector3.down, out var hit, 10f, ShakuConst.SurfaceMask));
            Assert.IsNotNull(hit.collider.GetComponent<MovingPlatform>(), "舟の上に置く");
            Worm.Spawn(hit.point, Vector3.forward);
            yield return Frames(3);
            Assert.IsTrue(Worm.OnMovingPlatform);

            f.SetClock(f.waitTime - 0.2f);
            yield return WaitUntil(() => f.AtB, 12f, "中州に着く");
            yield return Seconds(0.3f);
            Assert.IsTrue(Worm.OnMovingPlatform, "舟に乗ったまま");
            Vector3 fc = f.GetComponent<Collider>().bounds.center;
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), new Vector2(fc.x, fc.z)), 3f, "舟といっしょに動いた");
            Assert.IsFalse(RiverLayout.IsUnderwater(Worm.CenterPosition));

            // 中州へ降りる
            float islandX = WorldGenerator.IslandEdgeX(1f);
            Vector3 toIsland = new Vector3(RiverLayout.IslandCenter.x - Worm.CenterPosition.x, 0f, RiverLayout.IslandCenter.y - Worm.CenterPosition.z).normalized;
            yield return FaceCamera(toIsland);
            GameInput.VirtualMove = Vector2.up;
            GameInput.VirtualSprint = true;
            float waited = 0f;
            var trace = new System.Text.StringBuilder();
            while (!(Worm.CenterPosition.x < islandX - 0.8f && !Worm.OnMovingPlatform))
            {
                waited += Time.deltaTime;
                if (Mathf.FloorToInt(waited * 2f) != Mathf.FloorToInt((waited - Time.deltaTime) * 2f))
                    trace.Append($"[{waited:F1}s tail={Worm.TailPoint} head={Worm.HeadPoint} plat={Worm.OnMovingPlatform} blocked={Worm.IsBlocked} up={Worm.SurfaceUp}] ");
                if (waited > 15f)
                {
                    Bounds fb = f.GetComponent<Collider>().bounds;
                    Assert.Fail($"中州に上がれない islandX={islandX:F2} wl={RiverLayout.WaterLevel(RiverLayout.IslandZ):F2} ferry={fb.min}..{fb.max} " +
                                $"terrain@tail={RiverLayout.Height(Worm.TailPoint.x, Worm.TailPoint.z):F2} {trace}");
                }
                yield return null;
            }
            ResetInput();
            Assert.Less(RiverLayout.IslandRadius01(Worm.CenterPosition.x, Worm.CenterPosition.z), 1.3f);
            Assert.IsFalse(RiverLayout.IsUnderwater(Worm.CenterPosition));
        }

        [UnityTest]
        public IEnumerator SteppingStones_CanBeCrossedOnFoot()
        {
            var stones = GM.world.StepStones;
            Vector3 first = stones[0], last = stones[stones.Count - 1];
            Place(new Vector2(first.x, first.z), Vector3.right);
            yield return FaceCamera(Vector3.right);
            bool wet = false;
            GameInput.VirtualMove = Vector2.up;
            GameInput.VirtualSprint = true;
            float t = 0f;
            while (Worm.CenterPosition.x < last.x - 0.3f)
            {
                if (RiverLayout.IsUnderwater(Worm.CenterPosition)) wet = true;
                // 石の列からそれないようにカメラを向け直す
                Cam.yaw = Mathf.Atan2(1f, (RiverLayout.StonesZ - Worm.CenterPosition.z) * 0.6f) * Mathf.Rad2Deg;
                t += Time.deltaTime;
                if (t > 70f) Assert.Fail($"向こう岸に着かない x={Worm.CenterPosition.x:F1} / {last.x:F1}");
                yield return null;
            }
            ResetInput();
            Assert.IsFalse(wet, "水に落ちない");
            Assert.Greater(Worm.CenterPosition.x, RiverLayout.CenterX(RiverLayout.StonesZ) + RiverLayout.HalfWidth(RiverLayout.StonesZ) - 2f);
        }

        [UnityTest]
        public IEnumerator RiverCreature_IsRegisteredWhenApproached()
        {
            Vector3 snail = GM.creatures.PositionOf("riversnail", 0);
            Vector3 p = TopSurface(new Vector2(snail.x, snail.z) + new Vector2(0.4f, 0f));
            Worm.Spawn(p, Vector3.forward);
            yield return WaitUntil(() => Creatures.IsDiscovered("riversnail"), 4f, "カワニナを見つける");
            yield return Frames(2);
            Assert.IsTrue(UI.IsCreatureCardVisible);
            Assert.AreEqual(SpeciesCatalog.Get("riversnail").name, UI.CreatureCardName);
            Assert.AreEqual($"1 / {SpeciesCatalog.Count}", UI.Root.Q<Label>("creature-count").text);
            CollectionAssert.Contains(SaveSystem.Data.creatures, "riversnail");
        }

        [UnityTest]
        public IEnumerator RiverDewdrop_IsSavedWithTheRiverId()
        {
            int id = 0;
            Worm.Spawn(Col.DropPosition(id), Vector3.forward);
            yield return WaitUntil(() => Col.IsDropTaken(id), 3f, "川のしずく");
            CollectionAssert.Contains(SaveSystem.Data.drops, Areas.River.DropIdOffset + id);
            Assert.AreEqual(1, Collectibles.CollectedIn(Areas.River));
            Assert.AreEqual(0, Collectibles.CollectedIn(Areas.Forest));
        }
    }

    /// <summary>総合テスト：いきもの・図鑑・きせかえ・クリア。</summary>
    public class CreatureCollectionTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.2f);
        }

        [TearDown]
        public void TearDown()
        {
            ResetInput();
            Time.timeScale = 1f;
        }

        static float DistanceToLoop(Vector3 p, System.Collections.Generic.List<Vector3> path)
        {
            float best = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 a = path[i], b = path[(i + 1) % path.Count];
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                Vector3 q = a + ab * t;
                best = Mathf.Min(best, new Vector2(p.x - q.x, p.z - q.z).magnitude);
            }
            return best;
        }

        [UnityTest]
        public IEnumerator Ants_MarchAlongTheirTrail()
        {
            var line = GM.world.Mobs.First(m => m.species == "ant" && m.path.Count > 1);
            var before = Enumerable.Range(0, 4).Select(i => GM.creatures.PositionOf("ant", i)).ToArray();
            yield return Seconds(2.5f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 now = GM.creatures.PositionOf("ant", i);
                Assert.Greater(Vector3.Distance(before[i], now), 0.2f, $"アリ {i} が進んでいない");
                Assert.Less(DistanceToLoop(now, line.path), 0.9f, $"アリ {i} が行列からはずれた");
            }
        }

        [UnityTest]
        public IEnumerator Sparrow_FliesAwayWhenApproached()
        {
            Assert.IsFalse(GM.creatures.IsAirborne("sparrow", 0));
            Vector3 s = GM.creatures.PositionOf("sparrow", 0);
            Place(new Vector2(s.x + 2f, s.z), Vector3.left);
            yield return WaitUntil(() => GM.creatures.IsAirborne("sparrow", 0), 3f, "スズメが飛び立つ");
            Assert.IsTrue(Creatures.IsDiscovered("sparrow"), "飛び立つ前に図鑑に登録される");
            yield return Seconds(1f);
            Assert.Greater(GM.creatures.PositionOf("sparrow", 0).y, s.y + 1f, "空へ");
        }

        [UnityTest]
        public IEnumerator ApproachingAnAnt_ShowsTheCreatureCard()
        {
            Vector3 a = GM.creatures.PositionOf("ant", 0);
            Place(new Vector2(a.x, a.z), Vector3.forward);
            yield return WaitUntil(() => Creatures.IsDiscovered("ant"), 3f, "アリを見つける");
            yield return Frames(2);
            Assert.IsTrue(UI.IsCreatureCardVisible);
            // 近くにいた別のいきものもいっしょに見つかることがある（カードは最後に見つけたもの）
            var found = SaveSystem.Data.creatures.Select(id => SpeciesCatalog.Get(id).name).ToList();
            CollectionAssert.Contains(found, UI.CreatureCardName);
            Assert.AreEqual($"{Creatures.DiscoveredCount} / {SpeciesCatalog.Count}", UI.Root.Q<Label>("creature-count").text);
            // 待っているあいだに、まわりの別のいきものを見つけると、カードがもう一度出る。
            // 新しく見つけずに 5.5 秒たったら、カードは消えている
            for (int tries = 0; tries < 4; tries++)
            {
                int count = Creatures.DiscoveredCount;
                yield return Seconds(5.5f);
                if (Creatures.DiscoveredCount == count) break;
            }
            Assert.IsFalse(UI.IsCreatureCardVisible, "しばらくすると消える");
        }

        [UnityTest]
        public IEnumerator Zukan_ShowsFoundAndUnknownCreatures()
        {
            GM.creatures.Discover(SpeciesCatalog.Get("ant"));
            UI.ShowCollection(true, "zukan");
            yield return Frames(2);
            Assert.IsTrue(UI.IsCollectionOpen);
            Assert.IsFalse(Worm.InputEnabled, "図鑑を開いているあいだは動かない");
            Assert.AreEqual(SpeciesCatalog.All.Count, UI.ZukanCardCount, "レアのページもある");
            UI.SelectSpecies("ant");
            Assert.AreEqual(SpeciesCatalog.Get("ant").name, UI.ZukanDetailName);
            UI.SelectSpecies("crow");
            Assert.AreEqual("？？？", UI.ZukanDetailName, "まだ見つけていないいきものは名前がかくれている");
            StringAssert.Contains("ヒント", UI.Root.Q<Label>("zukan-desc").text);

            UI.SelectTab("record");
            Assert.AreEqual("record", UI.CurrentTab);
            Assert.Greater(UI.RecordRowCount, 5);
            Assert.IsTrue(UI.Back());
            Assert.IsFalse(UI.IsCollectionOpen);
            yield return Frames(2);
            Assert.IsTrue(Worm.InputEnabled);
        }

        [UnityTest]
        public IEnumerator Skins_AreLockedUntilEarned()
        {
            UI.ShowCollection(true, "skin");
            yield return Frames(2);
            Assert.AreEqual(Skins.All.Count, UI.SkinCardCount);
            Assert.IsFalse(UI.SelectSkin("kimidori"), "まだ選べない");
            Assert.AreEqual("wakaba", SaveSystem.Data.skin);
            for (int i = 0; i < 10; i++) SaveSystem.Data.drops.Add(500 + i);
            Assert.IsTrue(UI.SelectSkin("kimidori"));
            Assert.AreEqual("kimidori", SaveSystem.Data.skin);
            yield return Frames(2);
            var r = Worm.body.GetComponent<Renderer>();
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            Assert.AreEqual(Skins.Get("kimidori").hue, mpb.GetFloat("_HueShift"), 1e-3f, "しゃくとりむしの色が変わる");
        }

        [UnityTest]
        public IEnumerator CollectingDrops_UnlocksASkinWithAToast()
        {
            for (int i = 0; i < 9; i++) SaveSystem.Data.drops.Add(500 + i);
            int id = 0;
            for (; id < Col.TotalDrops && Col.IsDropTaken(id); id++) { }
            Worm.Spawn(Col.DropPosition(id), Vector3.forward);
            yield return WaitUntil(() => Col.IsDropTaken(id), 3f, "しずく");
            yield return Frames(2);
            var labels = UI.Root.Q("toasts").Query<Label>().ToList();
            Assert.IsTrue(labels.Any(l => l.text.Contains("きみどり")), "きせかえが増えたことが通知される");
        }

        [UnityTest]
        public IEnumerator EverythingFound_ShowsTheFinalEndingAndRainbowSkin()
        {
            var d = SaveSystem.Data;
            foreach (var area in Areas.All)
                for (int i = 0; i < area.DropCount; i++)
                    if (!(area == Areas.Forest && i == 0)) d.drops.Add(area.DropIdOffset + i);
            foreach (var lm in Areas.AllLandmarks()) d.places.Add(lm.id);
            foreach (var s in SpeciesCatalog.All) d.creatures.Add(s.id);
            Col.ApplySave();
            Assert.IsFalse(Col.IsDropTaken(0));
            Worm.Spawn(Col.DropPosition(0), Vector3.forward);
            yield return WaitUntil(() => UI.IsCompleteOpen, 8f, "クリア画面");
            Assert.IsTrue(d.completed);
            CollectionAssert.Contains(d.completedAreas, "forest");
            Assert.AreEqual("すべてをめぐり終えました！", UI.Root.Q<Label>("complete-title").text);
            Assert.IsTrue(Skins.IsUnlocked(Skins.Get("niji"), ProgressStats.FromSave()));
            Assert.IsTrue(UI.SelectSkin("niji"));
        }
    }

    /// <summary>総合テスト：スマホ・タブレットの画面。</summary>
    public class TouchUiTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.2f);
        }

        [TearDown]
        public void TearDown()
        {
            if (UI != null)
            {
                UI.EndJoystick();
                UI.ForceTouch = null;
            }
            ResetInput();
        }

        [UnityTest]
        public IEnumerator TouchMode_ShowsTouchControls()
        {
            UI.ForceTouch = true;
            yield return Frames(3);
            Assert.IsTrue(UI.IsTouchMode);
            Assert.IsTrue(UI.Root.ClassListContains("touch-ui"));
            Assert.AreEqual(DisplayStyle.Flex, UI.Root.Q("touch").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.Flex, UI.Root.Q("fullscreen-button").resolvedStyle.display, "全画面ボタン");
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q("help-hint").resolvedStyle.display, "キーボードのヒントは出さない");
            var help = UI.Root.Q("help").Query<Label>().ToList();
            Assert.IsTrue(help.Any(l => l.text.Contains("左側をドラッグ")), "タッチ用の操作説明");
            Assert.Less(UI.ReferenceResolution.x, 1920, "タッチ画面では UI を大きく表示");

            UI.ForceTouch = false;
            yield return Frames(3);
            Assert.IsFalse(UI.Root.ClassListContains("touch-ui"));
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q("touch").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q("fullscreen-button").resolvedStyle.display);
            Assert.AreEqual(new Vector2Int(1920, 1080), UI.ReferenceResolution, "PC では元の大きさ");
        }

        [UnityTest]
        public IEnumerator FloatingJoystick_MovesTheWorm()
        {
            UI.ForceTouch = true;
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            Vector3 start = Worm.CenterPosition;
            UI.BeginJoystick(new Vector2(300f, 500f));
            Assert.IsTrue(UI.IsJoystickActive);
            Assert.IsTrue(UI.Root.Q("joystick").ClassListContains("joystick--active"));
            UI.DragJoystick(new Vector2(300f, 420f));
            Assert.AreEqual(1f, GameInput.VirtualMove.y, 0.01f, "上へ倒す = 前へ");
            yield return Seconds(2f);
            Assert.Greater(Vector3.Distance(start, Worm.CenterPosition), 0.3f, "スティックで進む");
            UI.EndJoystick();
            Assert.AreEqual(Vector2.zero, GameInput.VirtualMove);
            Assert.IsFalse(UI.IsJoystickActive);
        }

        [UnityTest]
        public IEnumerator FloatingJoystick_FollowsAFarFinger()
        {
            UI.ForceTouch = true;
            yield return Frames(2);
            UI.BeginJoystick(new Vector2(300f, 500f));
            UI.DragJoystick(new Vector2(700f, 500f));
            Assert.AreEqual(1f, GameInput.VirtualMove.x, 0.01f);
            // 少し戻しても、スティックが指についてきているので右に倒れたまま
            UI.DragJoystick(new Vector2(660f, 500f));
            Assert.Greater(GameInput.VirtualMove.x, 0.4f);
            UI.EndJoystick();
        }

        [UnityTest]
        public IEnumerator LayoutClasses_MatchThePanelShape()
        {
            yield return Frames(3);
            var r = UI.Root.panel.visualTree.layout;
            bool portrait = r.height > r.width * 1.05f;
            Assert.AreEqual(portrait, UI.IsPortrait);
            Assert.AreEqual(!portrait && r.height < 760f, UI.IsShort);
            Assert.AreEqual(UI.IsPortrait || UI.IsShort, UI.IsCompact);
        }
    }
}

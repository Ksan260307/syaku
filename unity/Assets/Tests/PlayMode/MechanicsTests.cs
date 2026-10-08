using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：背伸びで見まわす・はなれて落ちる・糸をねらう・いきものに乗る。</summary>
    public class MechanicsTests
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
        }

        float GroundAt(Vector3 p) => Areas.Current.Height(p.x, p.z);

        [UnityTest]
        public IEnumerator Stand_DirectionTurnsTheUpperBodyWithoutWalking()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualStand = true;
            yield return Seconds(1.0f);
            Vector3 tail = Worm.TailPoint;
            GameInput.VirtualMove = Vector2.right;
            yield return Seconds(1.5f);
            Assert.Less(Vector3.Distance(tail, Worm.TailPoint), 0.05f, "背伸び中は進まない");
            Assert.Greater(Worm.StandLook, 0.6f, "右を向く");
            Assert.Greater(Worm.HeadPosition.x - Worm.TailPoint.x, 0.15f, "頭が右へ向いている");
            // 背伸びをやめると、見ていた方へ向きを変える
            GameInput.VirtualMove = Vector2.zero;
            GameInput.VirtualStand = false;
            yield return Seconds(1.5f);
            Assert.Greater(Worm.Heading.x, 0.4f, $"右へ向きを変えた heading={Worm.Heading}");
        }

        IEnumerator ClimbStumpWall(float height)
        {
            Vector2 s = ForestLayout.Stump;
            Place(s + new Vector2(-12.5f, 0f), Vector3.right);
            yield return FaceCamera(Vector3.right);
            float ground = ForestLayout.Height(s.x, s.y);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.HeadPosition.y - ground > height && Worm.SurfaceUp.y < 0.5f, 25f, "切り株の壁を登る");
            GameInput.VirtualMove = Vector2.zero;
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Idle, 2f, "止まる");
        }

        [UnityTest]
        public IEnumerator LetGo_OnAWall_FallsCurledAndLandsWithABounce()
        {
            yield return ClimbStumpWall(2.5f);
            Assert.IsTrue(Worm.OnSteepSurface);
            StringAssert.Contains("はなれて落ちる", UI.PromptText);
            int falls = SaveSystem.Data.falls;
            float hitSpeed = -1f;
            Worm.HitGround += v => hitSpeed = v;
            GameInput.SetVirtualSilk(true);
            yield return null;
            yield return null;
            GameInput.SetVirtualSilk(false);
            Assert.IsTrue(Worm.IsAboutToFall || Worm.State == InchwormController.Mode.Fall, "体をちぢめて、はなれる準備");
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Fall, 0.4f, "はなれて落ちる");
            // 落ちている間は丸まっている（頭と尾が近い）
            yield return Seconds(0.25f);
            if (Worm.State == InchwormController.Mode.Fall)
                Assert.Less(Vector3.Distance(Worm.Curve.Head, Worm.Curve.Tail), 0.6f, "くるんと丸まる");
            yield return WaitUntil(() => Worm.State != InchwormController.Mode.Fall, 6f, "着地");
            yield return Seconds(0.6f);   // 体を開いた形から、地面に沿った形へもどるまで
            Assert.Greater(hitSpeed, 0.5f, "着地した");
            Assert.AreEqual(falls + 1, SaveSystem.Data.falls, "きろくに残る");
            Assert.Less(Worm.HeadPosition.y - GroundAt(Worm.HeadPosition), 1.2f, "地面に下りた");
        }

        [UnityTest]
        public IEnumerator SteepCliffEdge_WalkingForwardFallsOff()
        {
            Vector2 s = ForestLayout.Stump;
            Vector3 outward = new Vector3(-1f, 0f, 0.2f).normalized;
            Place(s + new Vector2(outward.x, outward.z) * 5.2f, outward);
            yield return FaceCamera(outward);
            bool fell = false;
            Worm.Fell += () => fell = true;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => fell, 15f, "がけのふちから落ちる");
            GameInput.VirtualMove = Vector2.zero;
            yield return WaitUntil(() => Worm.State != InchwormController.Mode.Fall, 6f, "着地");
            Assert.Less(Worm.HeadPosition.y - GroundAt(Worm.HeadPosition), 1.5f, "切り株の下の地面へ");
        }

        [UnityTest]
        public IEnumerator Falling_SilkCatchesTheWorm()
        {
            yield return ClimbStumpWall(4f);
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return Seconds(0.25f);
            Assert.AreEqual(InchwormController.Mode.Fall, Worm.State);
            GameInput.SetVirtualSilk(true);
            yield return null;
            yield return null;
            GameInput.SetVirtualSilk(false);
            Assert.AreEqual(InchwormController.Mode.Hang, Worm.State, "落ちながら糸でぶら下がる");
            Assert.IsTrue(Worm.silk.enabled);
        }

        [UnityTest]
        public IEnumerator AimedSilk_ReelsTheWormUpToTheTarget()
        {
            // 目の前に高い柱を立てて、その横の 4m の所をねらう
            Vector3 start = Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.layer = ShakuConst.SurfaceLayer;
            pillar.transform.position = start + new Vector3(0f, 3f, 5f);
            pillar.transform.localScale = new Vector3(2f, 6f, 1f);
            Physics.SyncTransforms();
            yield return Seconds(0.3f);
            Vector3 target = start + new Vector3(0f, 4f, 4.5f);   // 柱の手前の面
            Assert.IsTrue(Worm.AimAt(target), "とどく");
            Assert.AreEqual(target.y, Worm.AimPoint.y, 0.3f);
            Assert.IsTrue(Worm.FireSilk());
            Assert.IsTrue(Worm.IsReeling);
            yield return WaitUntil(() => !Worm.IsReeling && Worm.State != InchwormController.Mode.Hang, 10f, "たぐり寄せて着く");
            Assert.Greater(Worm.HeadPoint.y - start.y, 3f, "高い所へ上がった");
            Assert.Less(Vector3.Distance(Worm.HeadPoint, Worm.AimPoint), 1.2f, "ねらった場所に着く");
            Assert.IsTrue(Worm.OnSteepSurface, "柱の壁につかまる");
            Object.Destroy(pillar);
        }

        [UnityTest]
        public IEnumerator Roots_AreClimbedOverNotStuckUnder()
        {
            // 地面から少し浮いた低い板（根っこ）に正面から進むと、上へのりこえる
            Vector3 start = Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.layer = ShakuConst.SurfaceLayer;
            root.transform.position = start + new Vector3(0f, 0.45f, 2.2f);
            root.transform.localScale = new Vector3(3f, 0.3f, 1.5f);
            Physics.SyncTransforms();
            bool under = false;
            yield return Hold(Vector2.up, 5f, () => { if (Worm.SurfaceUp.y < -0.35f) under = true; });
            Assert.IsFalse(under, "裏側にはりつかない");
            Assert.Greater(Worm.CenterPosition.z - start.z, 2.2f, "のりこえて進む");
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator Aim_ShowsAReticle()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            GameInput.VirtualAim = true;
            yield return Frames(3);
            Assert.IsTrue(Worm.IsAiming);
            Assert.IsTrue(UI.IsReticleVisible);
            Assert.IsNotEmpty(UI.ReticleText);
            GameInput.VirtualAim = false;
            yield return Frames(3);
            Assert.IsFalse(UI.IsReticleVisible);
        }

        [UnityTest]
        public IEnumerator Creatures_CanBeRiddenLikeAFerry()
        {
            Vector3 m = GM.creatures.PositionOf("mantis", 0);
            Assert.IsTrue(Physics.Raycast(m + Vector3.up * 6f, Vector3.down, out var hit, 10f, ShakuConst.CreatureMask), "カマキリの当たり判定");
            Worm.Spawn(hit.point, Vector3.forward);
            yield return Frames(3);
            Assert.IsTrue(Worm.OnMovingPlatform, "カマキリの上に乗っている");
            Vector3 beforeTail = Worm.TailPoint;
            GM.creatures.SetPosition("mantis", 0, m + new Vector3(1.2f, 0f, 0f));
            yield return Frames(3);
            Assert.Greater(Vector3.Distance(Worm.TailPoint, beforeTail), 0.8f, "いっしょに動く");
        }

        [UnityTest]
        public IEnumerator Mantis_RaisesItsArmsWhenApproached()
        {
            Vector3 m = GM.creatures.PositionOf("mantis", 0);
            Assert.Less(GM.creatures.Info("mantis", 0).raise, 0.2f);
            Place(new Vector2(m.x + 3.5f, m.z), Vector3.left);
            yield return Seconds(1.2f);
            Assert.Greater(GM.creatures.Info("mantis", 0).raise, 0.8f, "かまを上げる");
        }

        [UnityTest]
        public IEnumerator Ants_MoveTheirLegs()
        {
            float g0 = GM.creatures.Info("ant", 0).gait;
            float maxSpeed = 0f;
            float t = 0f;
            while (t < 1f)
            {
                yield return null;
                t += Time.deltaTime;
                maxSpeed = Mathf.Max(maxSpeed, GM.creatures.Info("ant", 0).moveSpeed);   // 食べものの所などで少し止まることもある
            }
            float g1 = GM.creatures.Info("ant", 0).gait;
            Assert.Greater(g1 - g0, 3f, "歩くと脚の位相が進む");
            Assert.Greater(maxSpeed, 0.2f);
        }

        [UnityTest]
        public IEnumerator Pillbug_CurlsUpWhenTouched()
        {
            // どんぐり広場のだんごむし（丸太の中ではない方）に、頭を近づける
            Vector3 p = GM.creatures.PositionOf("pillbug", 1);
            Worm.Spawn(p + new Vector3(-0.9f, 0f, 0f), Vector3.right);
            yield return Seconds(0.5f);
            Assert.IsTrue(GM.creatures.Info("pillbug", 1).curled, "くるんとまるくなる");
        }

        [UnityTest]
        public IEnumerator Settings_SurviveAReload()
        {
            UI.Root.Q<Slider>("set-sens").value = 1.75f;
            UI.Root.Q<Toggle>("set-minimap").value = true;
            yield return null;
            yield return Boot(keepSave: true);
            yield return Frames(3);
            Assert.AreEqual(1.75f, UI.Root.Q<Slider>("set-sens").value, 1e-3f, "感度が残っている");
            Assert.IsTrue(UI.Root.Q<Toggle>("set-minimap").value);
            Assert.AreEqual(1.75f, GameInput.LookSensitivity, 1e-3f, "読み込んだ設定がすぐ使われる");
        }

        [UnityTest]
        public IEnumerator Saving_ShowsTheAutosaveMark()
        {
            yield return Seconds(2.2f);
            Assert.IsFalse(UI.IsSaveIndicatorVisible);
            GM.SaveProgress();
            yield return Frames(3);
            Assert.IsTrue(UI.IsSaveIndicatorVisible);
            yield return Seconds(2.2f);
            Assert.IsFalse(UI.IsSaveIndicatorVisible, "しばらくすると消える");
        }

        [UnityTest]
        public IEnumerator HidingThePage_SavesAndMutes()
        {
            Place(ForestLayout.Spawn + new Vector2(3f, 2f), Vector3.right);
            yield return Seconds(0.3f);
            GM.OnPageHidden();
            Assert.Less(Vector3.Distance(SaveSystem.Data.tail, Worm.TailPoint), 0.3f, "タブをかくす前に保存");
            Assert.IsTrue(AudioListener.pause);
            GM.OnPageVisible();
            Assert.IsFalse(AudioListener.pause);
        }

        [UnityTest]
        public IEnumerator FastTravel_ToADiscoveredLandmark()
        {
            var lm = ForestLayout.Landmarks[4];
            Assert.IsFalse(GM.FastTravel(lm.id), "まだ見つけていない名所へは行けない");
            Col.Discover(lm);
            yield return Frames(2);
            Assert.IsTrue(GM.FastTravel(lm.id));
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 5f, "移動");
            Vector3 w = Worm.CenterPosition;
            Assert.Less(Vector2.Distance(new Vector2(w.x, w.z), lm.position), lm.radius, $"{lm.name} に着く");
            // 景色が見えるように、決めた場所に立って、見る先を向く
            Assert.IsNotNull(lm.view);
            Assert.Less(Vector2.Distance(new Vector2(w.x, w.z), lm.view.from), 3.5f, "景色を見る場所に立つ");
            Assert.Greater(Vector3.Dot(Worm.Heading, lm.view.Forward), 0.6f, "見る先を向く");
            Assert.IsTrue(Cam.ShowingView, "カメラは景色を写す");
            Assert.AreEqual(lm.view.pitch, Cam.pitch, 0.5f);
        }

        [UnityTest]
        public IEnumerator Map_TapOnADiscoveredLandmarkTravelsThere()
        {
            var lm = ForestLayout.Landmarks[4];
            var unknown = ForestLayout.Landmarks[6];
            Col.Discover(lm);
            UI.ShowMap(true);
            yield return Frames(2);
            // 地図そのものがタップを受けとる（名所の印の上でも、何もない所でも）
            var map = UI.Root.Q<VisualElement>("bigmap");
            foreach (var at in new[] { map.worldBound.center, map.worldBound.position + map.worldBound.size * 0.1f })
            {
                var picked = map.panel.Pick(at);
                Assert.IsTrue(picked != null && (picked == map || map.Contains(picked)), "地図をタップできる");
            }
            Assert.IsFalse(UI.TapBigMap(GameUI.MapPoint(unknown.position)), "まだ見つけていない名所には行けない");
            Assert.IsFalse(UI.TapBigMap(new Vector2(0.02f, 0.02f)), "何もない所をタップしても移動しない");
            Assert.IsTrue(UI.IsMapOpen);
            // 名所の少し横をタップしても、その名所へ
            Assert.IsTrue(UI.TapBigMap(GameUI.MapPoint(lm.position) + new Vector2(0.02f, 0f)), "名所をタップすると移動");
            Assert.IsFalse(UI.IsMapOpen, "地図はとじる");
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 5f, "移動");
            Vector3 w = Worm.CenterPosition;
            Assert.Less(Vector2.Distance(new Vector2(w.x, w.z), lm.position), lm.radius, $"{lm.name} に着く");
        }

        [UnityTest]
        public IEnumerator Map_TapOnATunnelGoesToAVisitedArea()
        {
            UI.ShowMap(true);
            yield return Frames(2);
            Vector2 gate = GameUI.MapPoint(ForestLayout.Gate);
            Assert.IsFalse(UI.TapBigMap(gate), "まだ行ったことがないエリアには行けない");
            SaveSystem.Data.visited.Add("river");
            Assert.IsTrue(UI.TapBigMap(gate), "トンネルをタップすると、その先のエリアへ");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 90f, "川辺へ");
        }

        [UnityTest]
        public IEnumerator Map_LegendHasFastTravelButtons()
        {
            UI.ShowMap(true);
            yield return Frames(2);
            int buttons = UI.Root.Q<ScrollView>("map-legend").Query<Button>(className: "legend-go").ToList().Count;
            Assert.AreEqual(Col.DiscoveredPlaces, buttons, "見つけた名所の数だけ「ここへ」がある");
        }

        [UnityTest]
        public IEnumerator Rescue_ReturnsToTheLastSafePlace()
        {
            Place(ForestLayout.Spawn, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Hold(Vector2.up, 1.5f);
            yield return Seconds(0.3f);
            Assert.Greater(Worm.SafePlaceCount, 0);
            Vector3 safe = Worm.SafePlace(Worm.SafePlaceCount - 1);
            // どこか遠くの変な場所へ（物の中）
            Worm.Spawn(new Vector3(ForestLayout.Stump.x, ForestLayout.Height(ForestLayout.Stump.x, ForestLayout.Stump.y) + 2f, ForestLayout.Stump.y), Vector3.forward);
            yield return null;
            GM.Rescue();
            yield return Frames(2);
            Assert.Less(Vector3.Distance(Worm.TailPoint, safe), 0.6f, "少し前の安全な場所へもどる");
        }

        [UnityTest]
        public IEnumerator Zukan_MarksNewCreaturesUntilViewed()
        {
            GM.creatures.Discover(SpeciesCatalog.Get("ladybug"));
            UI.ShowCollection(true, "zukan");
            UI.SelectSpecies("ant");
            yield return Frames(2);
            Assert.IsTrue(UI.HasNewBadge("ladybug"), "NEW がつく");
            UI.SelectSpecies("ladybug");
            Assert.IsFalse(UI.HasNewBadge("ladybug"), "見たら消える");
            Assert.AreEqual(SpeciesCatalog.All.Count, UI.ZukanCardCount, "レアのページもある");
        }
    }

    /// <summary>総合テスト：めったに会えないいきもの（確率を上げて確かめる）。</summary>
    public class RareCreatureTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Creatures.RareChanceOverride = 1f;
            yield return Boot();
            yield return StartNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            Creatures.RareChanceOverride = null;
            ResetInput();
        }

        [UnityTest]
        public IEnumerator HelmetAnt_IsRegisteredWithARareCard()
        {
            Assert.Greater(GM.creatures.CountOf("ant_helmet"), 0, "ヘルメットアリがいる");
            Assert.Greater(GM.creatures.CountOf("spider_sneaker"), 0, "スニーカーグモがいる");
            Vector3 a = GM.creatures.PositionOf("ant_helmet", 0);
            Place(new Vector2(a.x, a.z), Vector3.forward);
            yield return WaitUntil(() => Creatures.IsDiscovered("ant_helmet"), 4f, "ヘルメットアリを見つける");
            yield return Frames(2);
            Assert.IsTrue(UI.Root.Q("creature-card").ClassListContains("creature-card--rare"));
            StringAssert.Contains("レア", UI.Root.Q<Label>("creature-card-sub").text);
            Assert.AreEqual(1, Creatures.RareDiscoveredCount);
            Assert.AreEqual(0, ProgressStats.FromSave().species - Creatures.DiscoveredCount, "レアはコンプリートの数に入らない");
        }
    }
}

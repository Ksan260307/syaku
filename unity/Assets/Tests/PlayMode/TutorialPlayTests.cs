using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：はじめからを選ぶと、練習（チュートリアル）をするか聞かれる。チュートリアルは練習の森で、
    /// 基本の操作をひとつずつ練習し（できたら次へ）、北のトンネルをくぐると本番の森から始まる。
    /// 練習の記録はブラウザに書かず、練習で会ったいきものだけ図鑑にのこる。
    /// </summary>
    public class TutorialPlayTests
    {
        Tutorial Guide => GM.TutorialGuide;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
        }

        [TearDown]
        public void TearDown()
        {
            if (UI != null) UI.ForceTouch = null;
            ResetInput();
            Time.timeScale = 1f;
        }

        static void Press(string button)
        {
            var b = UI.Root.Q<Button>(button);
            Assert.IsNotNull(b, button);
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        IEnumerator StartTutorial()
        {
            GM.StartGame(false, tutorial: true);
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && GM.InTutorial && Worm.InputEnabled, 180f, "練習の森");
            yield return Seconds(0.3f);
        }

        IEnumerator StepDone(Tutorial.Step step, float timeout)
        {
            yield return WaitUntil(() => Guide.Current != step || Guide.ShowingDone, timeout, $"{step} ができる");
            Assert.IsTrue(UI.IsTutorialStepDone || Guide.Current != step, "できた！");
            yield return WaitUntil(() => Guide.Current != step, 8f, $"{step} のつぎへ");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator NewGame_AsksWhetherToPracticeFirst()
        {
            Assert.AreEqual("はじめから", UI.Root.Q<Button>("btn-new").text);
            // 記録がないとき：すぐに、練習するか聞く
            Press("btn-new");
            yield return null;
            Assert.IsTrue(UI.IsTutorialChoiceOpen, "練習するか聞く");
            Assert.IsFalse(UI.IsConfirmOpen);
            Assert.AreEqual("練習してから はじめる", UI.Root.Q<Button>("tutorial-yes").text);
            Assert.AreEqual("練習しないで はじめる", UI.Root.Q<Button>("tutorial-no").text);
            Press("tutorial-back");
            yield return null;
            Assert.IsFalse(UI.IsTutorialChoiceOpen, "もどる");
            Assert.AreEqual(GameManager.GameState.Title, GM.State, "タイトルのまま");
            // 記録があるとき：記録を消してよいか聞いてから、練習するか聞く
            SaveSystem.Data.drops.Add(3);
            SaveSystem.Save();
            Press("btn-new");
            yield return null;
            Assert.IsTrue(UI.IsConfirmOpen, "記録を消してよいか");
            Assert.IsFalse(UI.IsTutorialChoiceOpen);
            Press("confirm-yes");
            yield return null;
            Assert.IsTrue(UI.IsTutorialChoiceOpen, "つぎに、練習するか");
            // 練習しないで はじめる：森から（練習の森には行かない）
            Press("tutorial-no");
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 60f, "森から始まる");
            Assert.AreSame(Areas.Forest, Areas.Current);
            Assert.IsFalse(UI.IsTutorialCardVisible, "チュートリアルのカードは出ない");
            Assert.AreEqual(0, SaveSystem.Data.drops.Count, "記録は消えた");
            // 練習してから はじめる：練習の森から
            GM.GoToTitle();
            yield return null;
            Press("btn-new");
            yield return null;
            if (UI.IsConfirmOpen) Press("confirm-yes");
            yield return null;
            Press("tutorial-yes");
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && GM.InTutorial && Worm.InputEnabled, 180f, "練習の森から始まる");
            Assert.IsTrue(UI.IsTutorialCardVisible, "チュートリアルのカード");
            Assert.AreEqual(Tutorial.Step.Walk, Guide.Current);
            Assert.AreEqual("練習の森", UI.AreaTitle);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Tutorial_TeachesEachControl_ThenTheTunnelLeadsIntoTheForest()
        {
            yield return StartTutorial();
            Assert.IsTrue(SaveSystem.Practice, "練習の記録");
            Assert.AreEqual(3, Col.TotalDrops, "練習の森のしずくは 3 つ");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), TutorialLayout.Spawn), 3f, "はじまりの場所");
            Assert.IsTrue(GM.fx.BeaconShown, "行き先の目じるし");
            Vector3 firstDew = TutorialLayout.Ground(TutorialLayout.FirstDew.x, TutorialLayout.FirstDew.y);
            Assert.Less(Vector3.Distance(GM.fx.BeaconAt, firstDew), 0.5f, "道のしずくに");
            // はじめは、カメラがいちばん近いしずくの方を向いている（しずくが画面のまん中のあたりに見える）
            Vector3 vp = Camera.main.WorldToViewportPoint(firstDew);
            Assert.Greater(vp.z, 0f, "しずくはカメラの前");
            Assert.AreEqual(0.5f, vp.x, 0.12f, "しずくは、画面の左右のまん中");
            Assert.Greater(vp.y, 0.25f, "しずくは、カードの下にかくれない高さ");
            Assert.Less(vp.y, 0.7f, "しずくは、カードの下にかくれない高さ");
            // 押すボタン（キーの形）と、行き先の ▼
            CollectionAssert.AreEqual(new[] { "W", "A", "S", "D" }, UI.TutorialKeys, "押すボタン");
            yield return Frames(2);
            Assert.IsTrue(UI.IsTutorialMarkVisible, "しずくの上の ▼");
            // カメラを横へ向けると、▼ は画面の外。矢印が、しずくの方をさす
            yield return FaceCamera(Vector3.left);
            yield return Frames(2);
            Assert.IsFalse(UI.IsTutorialMarkVisible, "しずくは画面の外");
            Assert.IsTrue(UI.IsTutorialArrowVisible, "行き先をさす矢印");
            Assert.AreEqual(90f, UI.TutorialArrowAngle, 15f, "カメラを左へ向けると、矢印は右（しずくの方）をさす");

            // 1. すすむ：道のしずくまで歩く
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return StepDone(Tutorial.Step.Walk, 30f);
            ResetInput();
            Assert.AreEqual(1, Col.CollectedDrops);
            Assert.IsFalse(SaveSystem.HasSave, "練習の記録は、ブラウザに書かない");

            // 2. 見まわす
            Assert.AreEqual(Tutorial.Step.Look, Guide.Current);
            Assert.IsFalse(UI.IsTutorialArrowVisible, "行き先のない練習では、矢印は出ない");
            for (int i = 0; i < 120 && Guide.Current == Tutorial.Step.Look && !Guide.ShowingDone; i++)
            {
                GameInput.AddLook(new Vector2(12f, 0f));
                yield return null;
            }
            yield return StepDone(Tutorial.Step.Look, 5f);

            // 3. ズーム
            for (int i = 0; i < 60 && !Guide.ShowingDone; i++)
            {
                GameInput.AddZoom(i % 2 == 0 ? 0.3f : -0.3f);
                yield return null;
            }
            yield return StepDone(Tutorial.Step.Zoom, 5f);

            // 4. 登る：切り株の壁にむかってすすみ、てっぺんのしずくを取る
            Assert.AreEqual(Tutorial.Step.Climb, Guide.Current);
            Place(TutorialLayout.Stump + new Vector2(0f, -6f), Vector3.forward);
            yield return FaceCamera(Vector3.right);
            yield return Frames(2);
            Assert.IsTrue(UI.IsTutorialArrowVisible, "切り株をさす矢印");
            Assert.AreEqual(-90f, UI.TutorialArrowAngle, 20f, "カメラを右へ向けると、矢印は左（切り株の方）をさす");
            yield return FaceCamera(Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.HeadPosition.y > TutorialLayout.Height(0f, -9f) + 2.6f, 40f, "切り株を登る");
            yield return StepDone(Tutorial.Step.Climb, 30f);
            ResetInput();

            // 5. 糸をねらう：切り株のてっぺんから、となりの赤キノコのかさへ糸をとばして、かさの上のしずくを取る
            Assert.AreEqual(Tutorial.Step.Aim, Guide.Current);
            Assert.Less(Vector3.Distance(GM.fx.BeaconAt, GM.world.TutorialCapTop), 0.5f, "かさの上に目じるし");
            Vector2 m = TutorialLayout.Mushroom;
            Vector2 toM = (m - TutorialLayout.Stump).normalized;
            Worm.Spawn(GM.world.TopSurface(TutorialLayout.Stump + toM * 1.5f), new Vector3(toM.x, 0f, toM.y));
            yield return FaceCamera(new Vector3(toM.x, 0f, toM.y));
            yield return Frames(5);
            Assert.IsTrue(Worm.AimAt(GM.world.TutorialCapTop + Vector3.down * 0.3f), "かさの上を、ねらえる");
            Assert.IsTrue(Worm.FireSilk(), "糸を発射");
            yield return WaitUntil(() => !Worm.IsReeling && Worm.State != InchwormController.Mode.Hang, 10f, "かさまで、引きよせられる");
            Assert.Greater(Worm.HeadPosition.y - TutorialLayout.Height(m.x, m.y), 3.4f, "かさの上へ");
            Vector3 capDew = GM.world.TutorialCapTop;
            for (float t = 0f; t < 25f && !Guide.ShowingDone && Guide.Current == Tutorial.Step.Aim; t += Time.deltaTime)
            {
                Vector3 d = capDew - Worm.HeadPosition;
                Cam.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                GameInput.VirtualMove = Vector2.up;
                yield return null;
            }
            ResetInput();
            yield return StepDone(Tutorial.Step.Aim, 5f);
            Assert.AreEqual(3, Col.CollectedDrops, "練習の森のしずくを、ぜんぶ取った");

            // 6. 糸でおりる：かさのふちで糸を出して、下へ
            Assert.AreEqual(Tutorial.Step.Silk, Guide.Current);
            CollectionAssert.AreEqual(new[] { "Space" }, UI.TutorialKeys, "キーボードでは Space");
            Assert.IsNull(UI.TutorialHighlight, "キーボードでは、画面のボタンは光らせない");
            UI.ForceTouch = true;   // タッチにすると、押すボタンは「糸」、画面の「糸」ボタンを光らせる
            yield return Frames(3);
            CollectionAssert.AreEqual(new[] { "糸" }, UI.TutorialKeys, "タッチでは「糸」ボタン");
            Assert.AreEqual("btn-silk", UI.TutorialHighlight, "画面の「糸」ボタンを光らせる");
            UI.ForceTouch = null;
            yield return Frames(3);
            Assert.Less(Vector3.Distance(GM.fx.BeaconAt, TutorialLayout.Ground(TutorialLayout.SilkLanding.x, TutorialLayout.SilkLanding.y)), 0.5f, "おりる先に目じるし");
            Vector2 toL = (TutorialLayout.SilkLanding - m).normalized;
            yield return FaceCamera(new Vector3(toL.x, 0f, toL.y));
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.CanDropSilk, 20f, "かさのふちで、糸を出せる");
            GameInput.VirtualMove = Vector2.zero;
            GameInput.SetVirtualSilk(true);
            yield return null;
            GameInput.SetVirtualSilk(false);
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Hang, 3f, "糸でぶら下がる");
            yield return StepDone(Tutorial.Step.Silk, 30f);

            // 7. はやく
            yield return FaceCamera(new Vector3(0.6f, 0f, 0.8f));
            GameInput.VirtualMove = Vector2.up;
            GameInput.VirtualSprint = true;
            yield return StepDone(Tutorial.Step.Sprint, 10f);
            ResetInput();

            // 8. 背伸び
            GameInput.VirtualStand = true;
            yield return StepDone(Tutorial.Step.Stand, 8f);
            ResetInput();

            // 9. いきもの：かたつむりに会う
            Vector3 snail = GM.creatures.PositionOf("snail");
            Worm.Spawn(SurfaceNear(snail, 1.4f), Vector3.left);
            yield return StepDone(Tutorial.Step.Creature, 10f);
            CollectionAssert.Contains(SaveSystem.Data.creatures, "snail");

            // 10. 図鑑・11. 地図（開いて、とじると次へ）
            Assert.AreEqual("collection-button", UI.TutorialHighlight, "図鑑のボタンを光らせる");
            CollectionAssert.AreEqual(new[] { "B" }, UI.TutorialKeys, "B キー");
            UI.ShowCollection(true);
            yield return Frames(2);
            Assert.IsTrue(Guide.ShowingDone, "図鑑を開いた");
            float until = Time.unscaledTime + Tutorial.DoneSeconds + 0.4f;
            yield return WaitUntil(() => Time.unscaledTime > until, 5f, "少し待つ");
            Assert.AreEqual(Tutorial.Step.Zukan, Guide.Current, "とじるまで、つぎへ進まない");
            UI.ShowCollection(false);
            yield return WaitUntil(() => Guide.Current == Tutorial.Step.Map, 5f, "とじたら、つぎへ");
            Assert.AreEqual("map-button", UI.TutorialHighlight, "地図のボタンを光らせる");
            UI.ShowMap(true);
            yield return Frames(2);
            Assert.IsTrue(Guide.ShowingDone, "地図を開いた");
            Assert.IsNull(UI.TutorialHighlight, "できたら、光は消す");
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q("map-areas").resolvedStyle.display, "練習の森では、ほかのエリアの地図は出さない");
            UI.ShowMap(false);
            yield return WaitUntil(() => Guide.Current == Tutorial.Step.Exit, 5f, "さいごの練習");

            // 12. 出発：北のトンネルをくぐると、本番の森へ
            var gate = GM.world.Gates[0];
            Assert.Less(Vector3.Distance(GM.fx.BeaconAt, gate.position), 0.5f, "トンネルに目じるし");
            Vector3 start = gate.position + gate.inward * 4f;
            Place(new Vector2(start.x, start.z), -gate.inward);
            yield return FaceCamera(-gate.inward);
            yield return Frames(3);
            Assert.AreEqual(GameManager.TutorialGateHint, UI.PromptText, "トンネルの前のヒント");
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => GM.State == GameManager.GameState.Traveling, 20f, "トンネルに入る");
            ResetInput();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Areas.Current == Areas.Forest && Worm.InputEnabled, 180f, "森に着く");
            Assert.IsFalse(SaveSystem.Practice, "練習はおしまい");
            Assert.IsFalse(Guide.Running);
            Assert.IsFalse(UI.IsTutorialCardVisible, "カードはしまう");
            Assert.IsFalse(GM.fx.BeaconShown, "目じるしも消える");
            Assert.IsFalse(UI.IsTutorialArrowVisible, "矢印も消える");
            Assert.IsNull(UI.TutorialHighlight);
            Assert.AreEqual(0, SaveSystem.Data.drops.Count, "練習のしずくは、本番の記録にしない");
            Assert.IsFalse(SaveSystem.Data.places.Contains(90) || SaveSystem.Data.places.Contains(91), "練習の名所も");
            CollectionAssert.DoesNotContain(SaveSystem.Data.visited, "tutorial");
            CollectionAssert.Contains(SaveSystem.Data.creatures, "snail", "練習で会ったいきものは、図鑑にのこる");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), ForestLayout.Spawn), 3f, "森のはじまりの場所");
            Assert.AreEqual(Col.TotalDrops, Areas.Forest.DropCount);
            GM.SaveProgress();
            Assert.IsTrue(SaveSystem.HasSave, "本番の記録は、ふつうに書く");
            Assert.AreEqual("forest", SaveSystem.Data.area);
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator Tutorial_EndsFromTheMenu_AndQuittingToTheTitleThrowsThePracticeAway()
        {
            yield return StartTutorial();
            // とちゅうでタイトルへ：練習の記録はすてる（つぎは、はじめから）
            Place(TutorialLayout.FirstDew + new Vector2(0f, -1f), Vector3.forward);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Col.CollectedDrops == 1, 15f, "しずくを取る");
            ResetInput();
            GM.Pause();
            yield return null;
            Assert.AreNotEqual(DisplayStyle.None, UI.Root.Q<Button>("tutorial-end").resolvedStyle.display, "メニューに「チュートリアルをおわって、森へ」");
            GM.GoToTitle();
            yield return null;
            Assert.IsFalse(SaveSystem.Practice);
            Assert.IsFalse(SaveSystem.HasSave, "つづきからは出ない");
            Assert.AreEqual(0, SaveSystem.Data.drops.Count);
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q<Button>("btn-continue").resolvedStyle.display);

            // もう一度練習して、メニューからおわる
            yield return StartTutorial();
            Assert.AreEqual(Tutorial.Step.Walk, Guide.Current, "はじめの練習から");
            Assert.AreEqual(0, Col.CollectedDrops, "しずくも、もとどおり");
            GM.Pause();
            yield return null;
            Press("tutorial-end");
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Areas.Current == Areas.Forest && Worm.InputEnabled, 180f, "森に着く");
            Assert.IsFalse(SaveSystem.Practice);
            Assert.IsFalse(UI.IsTutorialCardVisible);
            Assert.AreEqual(1f, Time.timeScale, "メニューの止まった時間は、もとにもどる");
            GM.Pause();
            yield return null;
            Assert.AreEqual(DisplayStyle.None, UI.Root.Q<Button>("tutorial-end").resolvedStyle.display, "森では、出ない");
            GM.Resume();
        }
    }
}

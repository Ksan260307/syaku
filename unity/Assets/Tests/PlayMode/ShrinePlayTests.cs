using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：神社（公園の北東のトンネルから行く・鈴・神社のいきもの）。</summary>
    public class ShrinePlayTests
    {
        Creatures C => GM.creatures;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        IEnumerator GoTo(AreaLayout area)
        {
            SaveSystem.Data.visited.Add(area.Id);
            GM.TravelTo(area.Id);
            yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, area.Id + "へ");
            yield return Seconds(0.5f);
        }

        /// <summary>トンネルの前に置いて、トンネルへ歩いて入る。</summary>
        IEnumerator WalkInto(GateInstance gate)
        {
            Vector3 outward = -gate.inward;
            Vector3 start = gate.position + gate.inward * 4f;
            Place(new Vector2(start.x, start.z), outward);
            yield return FaceCamera(outward);
            yield return Frames(3);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => GM.State == GameManager.GameState.Traveling, 20f, "トンネルに入る");
            ResetInput();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 180f, "着く");
        }

        [UnityTest]
        public IEnumerator Tunnel_FromTheParkLeadsToTheShrineAndBack()
        {
            yield return GoTo(Areas.Park);
            var gate = GM.world.Gates.Single(g => g.def.targetArea == "shrine");
            Place(new Vector2(gate.position.x + gate.inward.x * 4f, gate.position.z + gate.inward.z * 4f), -gate.inward);
            yield return Frames(3);
            StringAssert.Contains("鎮守の森のお社", UI.PromptText, "トンネルの近くでヒントが出る");
            yield return WalkInto(gate);

            Assert.AreSame(Areas.Shrine, Areas.Current);
            Assert.AreEqual(30, Col.TotalDrops);
            Assert.AreEqual("shrine", SaveSystem.Data.area);
            Assert.AreEqual("鎮守の森のお社", UI.AreaTitle);
            Assert.AreEqual("神社のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.IsNotNull(AudioManager.Instance.musicShrine);
            Assert.AreSame(AudioManager.Instance.musicShrine, AudioManager.Instance.CurrentMusic, "神社の BGM");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyOpen"), "鎮守の森の、木々の空");
            Assert.IsNotNull(GM.world.sun.cookie, "木もれ日のもよう");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_spring"), "手水舎の筧の水音");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), ShrineLayout.Spawn), 3f, "トンネルの出口");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.forward), 0.6f, "参道の方（北）を向く");

            // 南のトンネルから、公園へもどる
            yield return Seconds(1.5f);
            var back = GM.world.Gates.Single(g => g.def.targetArea == "park");
            yield return WalkInto(back);
            Assert.AreSame(Areas.Park, Areas.Current);
            var shrineGate = GM.world.Gates.Single(g => g.def.targetArea == "shrine");
            Assert.Less(Vector3.Distance(Worm.CenterPosition, shrineGate.position), 12f, "神社へのトンネルのそばに出る");
        }

        [UnityTest]
        public IEnumerator Bell_RingsWhenTheWormTouchesTheRope()
        {
            yield return GoTo(Areas.Shrine);
            var bell = ShrineBell.Instance;
            Assert.IsNotNull(bell);
            int before = bell.Rings;
            // お社の階段の、鈴緒のすぐ横に立って、鈴緒の方を向く（屋根の下の、階段の面）
            Assert.IsTrue(Physics.Raycast(bell.ropeLow + new Vector3(1.2f, 0.3f, 0f), Vector3.down, out var step, 4f, ShakuConst.SurfaceMask), "鈴緒の下の階段");
            Assert.Less(bell.ropeLow.y - step.point.y, 1.6f, "鈴緒のはしに、頭がとどく");
            Worm.Spawn(step.point, Vector3.left);
            yield return WaitUntil(() => bell.Rings > before, 6f, "鈴が鳴る");
            Assert.GreaterOrEqual(AreaSounds.Instance.CountOf("suzu"), 1, "鈴の音");
            int rung = bell.Rings;
            yield return Seconds(0.5f);
            Assert.AreEqual(rung, bell.Rings, "つづけては鳴らない");
        }

        [UnityTest]
        public IEnumerator Fox_LooksAtTheWorm_AndMovesAwayWhenTooClose()
        {
            yield return GoTo(Areas.Shrine);
            Vector3 p = C.PositionOf("kitsune");
            Assert.AreNotEqual(Vector3.zero, p);
            // 少しはなれた所に来ると、止まって、こちらを向いて首をかしげる
            Vector3 dir = new Vector3(1f, 0f, 0.3f).normalized;
            Worm.Spawn(TopSurface(new Vector2(p.x + dir.x * 10.5f, p.z + dir.z * 10.5f)), -dir);
            yield return WaitUntil(() => C.Info("kitsune").alert > 0.5f, 6f, "首をかしげて見つめる");
            var info = C.Info("kitsune");
            Vector3 toWorm = Vector3.ProjectOnPlane(Worm.CenterPosition - info.pos, Vector3.up).normalized;
            yield return WaitUntil(() => Vector3.Dot(Vector3.ProjectOnPlane(C.Info("kitsune").fwd, Vector3.up).normalized, toWorm) > 0.7f, 6f, "こちらを向く");
            // 近すぎると、すっとはなれる
            Vector3 now = C.PositionOf("kitsune");
            Worm.Spawn(TopSurface(new Vector2(now.x + dir.x * 4f, now.z + dir.z * 4f)), -dir);
            float start = Vector2.Distance(new Vector2(now.x, now.z), new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z));
            yield return Seconds(2.5f);
            Vector3 after = C.PositionOf("kitsune");
            Assert.Greater(Vector2.Distance(new Vector2(after.x, after.z), new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z)), start + 1.5f, "はなれる");
        }

        [UnityTest]
        public IEnumerator Tanuki_PlaysDead_WhenStartled_AndGetsUpLater()
        {
            yield return GoTo(Areas.Shrine);
            Vector3 p = C.PositionOf("tanuki");
            Worm.Spawn(TopSurface(new Vector2(p.x + 4.5f, p.z)), Vector3.left);
            yield return WaitUntil(() => C.Info("tanuki").playingDead, 4f, "ころんと死んだふり");
            yield return Seconds(1.2f);
            Assert.IsTrue(C.Info("tanuki").playingDead);
            Assert.Less(C.Info("tanuki").moveSpeed, 0.05f, "動かない");
            Vector3 side = ((Vector3)C.BodyMatrixOf("tanuki").GetColumn(1)).normalized;
            Assert.Less(side.y, 0.4f, "横になっている");
            // しばらくすると、起きあがる
            Worm.Spawn(TopSurface(new Vector2(p.x + 20f, p.z + 6f)), Vector3.right);
            yield return WaitUntil(() => !C.Info("tanuki").playingDead, 14f, "起きあがる");
            yield return Seconds(1.5f);
            Vector3 up = ((Vector3)C.BodyMatrixOf("tanuki").GetColumn(1)).normalized;
            Assert.Greater(up.y, 0.8f, "立っている");
        }

        [UnityTest]
        public IEnumerator Owl_FliesOffQuietly_ToAnotherHighPerch()
        {
            yield return GoTo(Areas.Shrine);
            Vector3 p = C.PositionOf("fukurou");
            Assert.Greater(p.y - ShrineLayout.Height(p.x, p.z), 10f, "高い所にとまっている");
            // 近づくと（とまっている所のすぐそば）、ほかの高い所へ飛んでいく
            Vector3 near = Physics.Raycast(p + new Vector3(1.5f, 1.5f, 0f), Vector3.down, out var perch, 4f, ShakuConst.SurfaceMask) ? perch.point : p + Vector3.up * 0.3f;
            Worm.Spawn(near, Vector3.left);
            yield return WaitUntil(() => C.Info("fukurou").airborne, 6f, "飛び立つ");
            ResetInput();
            Worm.Spawn(TopSurface(ShrineLayout.Spawn), Vector3.forward);
            yield return WaitUntil(() => !C.Info("fukurou").airborne, 40f, "下りる");
            Vector3 q = C.PositionOf("fukurou");
            Assert.Greater(Vector3.Distance(q, p), 4f, "ちがう所");
            Assert.Greater(q.y - ShrineLayout.Height(q.x, q.z), 8f, "また、高い所");
        }

        [UnityTest]
        public IEnumerator Moth_StaysAroundTheLanternLights()
        {
            yield return GoTo(Areas.Shrine);
            var lights = GM.world.LanternLights;
            float far = 0f;
            int samples = 0, near = 0;
            for (float t = 0f; t < 12f; t += 0.5f)
            {
                yield return Seconds(0.5f);
                for (int i = 0; i < C.CountOf("ga"); i++)
                {
                    Vector3 m = C.PositionOf("ga", i);
                    float d = lights.Min(l => Vector3.Distance(l, m));
                    far = Mathf.Max(far, d);
                    samples++;
                    if (d < 4.5f) near++;
                }
            }
            Assert.Less(far, 10f, "灯籠から灯籠へ飛ぶときも、遠くへは行かない");
            Assert.Greater(near, samples * 0.6f, "たいてい、灯籠のあかりのそば");
        }

        [UnityTest]
        public IEnumerator Zukan_HasAShrineFilter()
        {
            yield return GoTo(Areas.Shrine);
            UI.ShowCollection(true, "zukan");
            yield return Frames(4);
            var chips = UI.Root.Query<Button>(className: "zukan-filter").ToList();
            Assert.AreEqual(8, chips.Count, "すべて・森・川辺・公園・山・山小屋・海岸・神社");
            Assert.AreEqual("神社", chips.Last().text);
            UI.ShowCollection(false);
        }
    }
}

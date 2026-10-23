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
            StringAssert.Contains("まだ行ったことがない場所", UI.PromptText, "トンネルの近くでヒントが出る");
            StringAssert.DoesNotContain("鎮守の森のお社", UI.PromptText, "まだ行ったことがない場所の名前は出さない");
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

        [UnityTest, Timeout(240000)]
        public IEnumerator Crickets_SingWithRaisedWings_AndHushWhenTheWormComesNear()
        {
            yield return GoTo(Areas.Shrine);
            foreach (var id in new[] { "suzumushi", "korogi" })
            {
                Assert.Greater(C.CountOf(id), 0, id + " が神社にいる");
                // 鳴きやむ近さより遠くで待つと、前ばねを立てて鳴く
                Vector3 p = C.Info(id).pos;
                Worm.Spawn(SurfaceNear(p, 6f), Vector3.left);
                yield return WaitUntil(() => C.Info(id).calling, 40f, id + " が鳴く");
                yield return Seconds(0.4f);
                if (C.Info(id).calling) Assert.Greater(C.Info(id).wingLift, 0.5f, id + "：前ばねを立てる");
                // しゃくとりむしが、すぐそばへ来ると、鳴きやんで、前ばねをたたむ
                Worm.Spawn(SurfaceNear(C.Info(id).pos, 1.2f), Vector3.left);
                yield return WaitUntil(() => !C.Info(id).calling, 3f, id + " が鳴きやむ");
                yield return Seconds(0.5f);
                if (!C.Info(id).calling) Assert.Less(C.Info(id).wingLift, 0.5f, id + "：鳴きやむと、前ばねをたたむ");
                for (float t = 0f; t < 3f; t += Time.deltaTime)
                {
                    var info = C.Info(id);
                    if ((info.pos - Worm.HeadPosition).magnitude < Creatures.CricketHush - 0.3f)
                        Assert.IsFalse(info.calling, id + "：しゃくとりむしが近くにいるあいだは、鳴かない");
                    yield return null;
                }
            }
        }

        static readonly Vector3[] Dirs6 = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        /// <summary>p がキツネの像の中なら（横と上のどの向きへも、像の面のうらに当たる）、いちばん近い面までの深さ。</summary>
        static float InsideFoxStatue(Vector3 p)
        {
            Physics.queriesHitBackfaces = true;
            try
            {
                Collider fox = null;
                float best = 20f;
                foreach (var d in new[] { Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                {
                    if (!Physics.Raycast(p, d, out var h, 20f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return 0f;
                    if (!h.collider.name.StartsWith("Shrine_Fox") || Vector3.Dot(h.normal, d) <= 0f) return 0f;
                    fox = h.collider;
                    best = Mathf.Min(best, h.distance);
                }
                foreach (var d in Dirs6)
                    if (fox.Raycast(new Ray(p, d), out var e, 20f)) best = Mathf.Min(best, e.distance);
                return best;
            }
            finally { Physics.queriesHitBackfaces = false; }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FoxStatue_CanBeClimbedFromThePlatform_WithoutTheBodyGoingInside()
        {
            // 前は、台座の段のすき間や、こしの下のくさびにはさまって登れず、体が像の中に入って、みだれた
            yield return GoTo(Areas.Shrine);
            for (int i = 0; i < 2; i++)
            {
                Vector2 f = ShrineLayout.FoxStatues[i];
                Vector2 dir = i == 0 ? Vector2.right : Vector2.left;   // 参道のがわから
                Vector2 s = f + dir * 6f;
                Assert.IsTrue(Physics.Raycast(new Vector3(s.x, ShrineLayout.PlatformY + 2f, s.y), Vector3.down, out var gh, 6f, ShakuConst.SurfaceMask));
                Vector3 target = new Vector3(f.x, gh.point.y, f.y);
                Vector3 heading = (target - gh.point).normalized;
                Worm.Spawn(gh.point, heading);
                yield return FaceCamera(heading);
                GameInput.VirtualMove = Vector2.up;
                float pedestalTop = ShrineLayout.PlatformY - 0.1f + 7.4f;
                float maxY = 0f, worst = 0f;
                for (float t = 0f; t < 40f && maxY < pedestalTop + 4f; t += Time.deltaTime)
                {
                    Vector3 c = Worm.CenterPosition;
                    Vector3 to = target - c;
                    if (new Vector2(to.x, to.z).magnitude > 0.5f) Cam.yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    yield return null;
                    maxY = Mathf.Max(maxY, Worm.HeadPosition.y);
                    if (Worm.State == InchwormController.Mode.Fall || Worm.State == InchwormController.Mode.Hang) continue;
                    var curve = Worm.Curve;
                    for (int k = 1; k < curve.Count - 1; k++) worst = Mathf.Max(worst, InsideFoxStatue(curve.pos[k]));
                }
                ResetInput();
                Assert.Greater(maxY, pedestalTop + 4f, $"キツネの像 {i} の、台座の上の体まで登れる");
                Assert.Less(worst, 0.35f, $"キツネの像 {i}：体が像の中に深く入らない");
                yield return Seconds(0.3f);
            }
        }

        [UnityTest]
        public IEnumerator Zukan_HasAShrineFilter()
        {
            yield return GoTo(Areas.Shrine);
            UI.ShowCollection(true, "zukan");
            yield return Frames(4);
            var chips = UI.Root.Query<Button>(className: "zukan-filter").ToList();
            Assert.AreEqual(10, chips.Count, "すべて・森・川辺・公園・山・山小屋・海岸・神社・夜の森・クリスマス村");
            Assert.AreEqual("神社", chips[7].text);
            Assert.AreEqual(AreaNames.Unknown, chips[8].text, "まだ行っていない夜の森は「？？？」");
            UI.ShowCollection(false);
        }
    }
}

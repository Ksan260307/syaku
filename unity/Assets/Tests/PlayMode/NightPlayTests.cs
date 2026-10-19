using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：夜の森（神社の奥の宮のうらのトンネルから行く・夜の光・夜の森のいきもの）。</summary>
    public class NightPlayTests
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

        [UnityTest, Timeout(400000)]
        public IEnumerator Tunnel_FromTheShrineLeadsToTheNightForestAndBack()
        {
            yield return GoTo(Areas.Shrine);
            var gate = GM.world.Gates.Single(g => g.def.targetArea == "night");
            Place(new Vector2(gate.position.x + gate.inward.x * 4f, gate.position.z + gate.inward.z * 4f), -gate.inward);
            yield return Frames(3);
            StringAssert.Contains("まだ行ったことがない場所", UI.PromptText, "トンネルの近くでヒントが出る");
            StringAssert.DoesNotContain("月あかりの森", UI.PromptText, "まだ行ったことがない場所の名前は出さない");
            yield return WalkInto(gate);

            Assert.AreSame(Areas.Night, Areas.Current);
            Assert.AreEqual(30, Col.TotalDrops);
            Assert.AreEqual("night", SaveSystem.Data.area);
            Assert.AreEqual("月あかりの森", UI.AreaTitle);
            Assert.AreEqual("夜露のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.IsNotNull(AudioManager.Instance.musicNight);
            Assert.AreSame(AudioManager.Instance.musicNight, AudioManager.Instance.CurrentMusic, "夜の森の BGM");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_Night"), "月と星の夜空");
            Assert.Less(GM.world.sun.intensity, 1.2f, "月あかり");
            Assert.Greater(GM.world.sun.color.b, GM.world.sun.color.r, "青い光");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_frogs"), "月見の池のカエル");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), NightLayout.Spawn), 3f, "トンネルの出口");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.forward), 0.6f, "林の奥（北）を向く");

            // 南のトンネルから、神社へもどる（昼の空と光にもどる）
            yield return Seconds(1.5f);
            var back = GM.world.Gates.Single(g => g.def.targetArea == "shrine");
            yield return WalkInto(back);
            Assert.AreSame(Areas.Shrine, Areas.Current);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Night"), "神社は、夕ぐれの空");
            var nightGate = GM.world.Gates.Single(g => g.def.targetArea == "night");
            Assert.Less(Vector3.Distance(Worm.CenterPosition, nightGate.position), 12f, "夜の森へのトンネルのそばに出る");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Bagworm_HangsFromItsThread_SwaysAndPeeks()
        {
            yield return GoTo(Areas.Night);
            Assert.IsTrue(C.BagwormState(out var anchor, out var top, out _, out _), "ミノムシがいる");
            float len = Vector3.Distance(anchor, top);
            Assert.Greater(len, 0.5f, "糸でぶらさがる");
            Assert.Less(top.y, anchor.y, "みのは、枝の下");
            // しずかに待つと、みのから顔を出す
            Worm.Spawn(TopSurface(new Vector2(anchor.x + 9f, anchor.z)), Vector3.left);
            float emerge = 0f;
            yield return WaitUntil(() => C.BagwormState(out _, out _, out _, out emerge) && emerge > 0.85f, 30f, "顔を出す");
            // 下に来られると、みのにひっこむ（みのの下の地面。枝の上ではなく）
            C.BagwormState(out _, out var topNow, out _, out _);
            Assert.IsTrue(Physics.Raycast(topNow + Vector3.down * 2.3f, Vector3.down, out var below, 10f, ShakuConst.SurfaceMask), "みのの下の面");
            Worm.Spawn(below.point, Vector3.left);
            yield return WaitUntil(() => C.BagwormState(out _, out _, out _, out emerge) && emerge < 0.1f, 6f, "ひっこむ");
            // 押されると、糸ごとゆれる（糸の長さは、そのまま）
            Assert.IsTrue(C.ForceBehaviorForTests("minomushi", "sway"));
            float maxSwing = 0f;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                C.BagwormState(out var a2, out var t2, out var sw, out _);
                maxSwing = Mathf.Max(maxSwing, sw);
                Assert.AreEqual(len, Vector3.Distance(a2, t2), 0.02f, "糸の長さは、かわらない");
                yield return null;
            }
            Assert.Greater(maxSwing, 0.1f, "ゆれる");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Boar_RunsAway_AndPigletsKeepNearTheirMother()
        {
            yield return GoTo(Areas.Night);
            Assert.AreEqual(3, C.CountOf("uribou"));
            // しずかに待つと、ウリボウは、おかあさんのそばにいる
            Worm.Spawn(TopSurface(NightLayout.Spawn), Vector3.forward);
            yield return Seconds(6f);
            Vector3 mom = C.PositionOf("inoshishi");
            for (int i = 0; i < 3; i++)
                Assert.Less(Vector3.Distance(C.PositionOf("uribou", i), mom), 16f, $"ウリボウ {i} は、おかあさんのそば");
            // 近づくと、もうれつないきおいで走って逃げる
            Vector3 dir = new Vector3(1f, 0f, 0.2f).normalized;
            Worm.Spawn(TopSurface(new Vector2(mom.x + dir.x * 4f, mom.z + dir.z * 4f)), -dir);
            float start = Vector2.Distance(new Vector2(mom.x, mom.z), new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z));
            float fastest = 0f;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                fastest = Mathf.Max(fastest, C.Info("inoshishi").moveSpeed);
                yield return null;
            }
            Vector3 after = C.PositionOf("inoshishi");
            Assert.Greater(Vector2.Distance(new Vector2(after.x, after.z), new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z)), start + 2.5f, "はなれる");
            Assert.Greater(fastest, SpeciesCatalog.Get("inoshishi").speed * 1.4f, "走る");
            // ウリボウも、おかあさんについていく
            yield return Seconds(4f);
            mom = C.PositionOf("inoshishi");
            int near = Enumerable.Range(0, 3).Count(i => Vector3.Distance(C.PositionOf("uribou", i), mom) < 18f);
            Assert.GreaterOrEqual(near, 2, "ウリボウは、おかあさんを追いかける");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Katydid_SingsWithRaisedWings_AndHushesWhenTheWormComesNear()
        {
            yield return GoTo(Areas.Night);
            const string id = "kirigirisu";
            Assert.Greater(C.CountOf(id), 0);
            Vector3 p = C.Info(id).pos;
            Worm.Spawn(SurfaceNear(p, 6f), Vector3.left);
            yield return FaceCamera(Vector3.left);   // 前ばねの立ちぐあいは、画面に映っているあいだに動く
            yield return WaitUntil(() => C.Info(id).calling, 50f, "「ギーッ、チョン」と鳴く");
            // 鳴いているあいだに、前ばねを立てる（「ギーッ」と「チョン」のあいだは、少し下がるので、いちばん高い所を見る）
            float lift = 0f;
            for (float t = 0f; t < 1.5f && C.Info(id).calling; t += Time.deltaTime)
            {
                lift = Mathf.Max(lift, C.Info(id).wingLift);
                yield return null;
            }
            Assert.Greater(lift, 0.5f, "前ばねを立てる");
            Worm.Spawn(SurfaceNear(C.Info(id).pos, 1.2f), Vector3.left);
            yield return WaitUntil(() => !C.Info(id).calling, 3f, "鳴きやむ");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Harvestman_BobsItsBody_WhenTheWormComesNear()
        {
            yield return GoTo(Areas.Night);
            Vector3 p = C.Info("zatoumushi").pos;
            Worm.Spawn(SurfaceNear(p, 1.0f), Vector3.left);
            yield return WaitUntil(() => C.Info("zatoumushi").act != 0, 6f, "体を上下にゆらす");
            yield return Seconds(0.6f);
            Assert.AreNotEqual(0, C.Info("zatoumushi").act, "しばらく、ゆらしつづける");
            Assert.Less(C.Info("zatoumushi").moveSpeed, 0.2f, "ゆらすあいだは、止まる");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator LongTailedTit_PerchesOnTheRoostTree_AndFliesWhenApproached()
        {
            yield return GoTo(Areas.Night);
            Vector3 p = C.PositionOf("shimaenaga");
            Assert.Greater(p.y - NightLayout.Height(p.x, p.z), 8f, "ねぐらの木の、高い枝の上");
            Vector3 near = Physics.Raycast(p + new Vector3(1.2f, 1.5f, 0f), Vector3.down, out var perch, 4f, ShakuConst.SurfaceMask) ? perch.point : p + Vector3.up * 0.3f;
            Worm.Spawn(near, Vector3.left);
            yield return WaitUntil(() => C.Info("shimaenaga").airborne, 8f, "飛び立つ");
        }
    }
}

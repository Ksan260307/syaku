using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：クリスマス村（夜の森の光の輪から行く・雪の夜の光・クリスマス村のいきもの）。</summary>
    public class XmasPlayTests
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

        /// <summary>光の輪の前に置いて、輪へ歩いて入る。</summary>
        IEnumerator WalkInto(GateInstance gate)
        {
            Vector3 outward = -gate.inward;
            Vector3 start = gate.position + gate.inward * 4f;
            Place(new Vector2(start.x, start.z), outward);
            yield return FaceCamera(outward);
            yield return Frames(3);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => GM.State == GameManager.GameState.Traveling, 20f, "光の輪に入る");
            ResetInput();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 180f, "着く");
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator WarpRing_FromTheNightForestLeadsToTheVillageAndBack()
        {
            yield return GoTo(Areas.Night);
            var ring = GM.world.Gates.Single(g => g.def.targetArea == "xmas");
            Place(new Vector2(ring.position.x + ring.inward.x * 4f, ring.position.z + ring.inward.z * 4f), -ring.inward);
            yield return Frames(3);
            StringAssert.Contains("光の輪をくぐると", UI.PromptText, "光の輪の近くでヒントが出る");
            StringAssert.Contains("まだ行ったことがない場所", UI.PromptText);
            StringAssert.DoesNotContain("クリスマス村", UI.PromptText, "まだ行ったことがない場所の名前は出さない");
            yield return WalkInto(ring);

            Assert.AreSame(Areas.Xmas, Areas.Current);
            Assert.AreEqual(30, Col.TotalDrops);
            Assert.AreEqual("xmas", SaveSystem.Data.area);
            Assert.AreEqual("雪あかりのクリスマス村", UI.AreaTitle);
            Assert.AreEqual("雪のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.IsNotNull(AudioManager.Instance.musicXmas);
            Assert.AreSame(AudioManager.Instance.musicXmas, AudioManager.Instance.CurrentMusic, "クリスマス村の BGM");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_Night"), "星の夜空");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_SkyOpen"), "ひらけた空（樹冠はない）");
            Assert.Less(GM.world.sun.intensity, 1.2f, "月あかり");
            Assert.IsTrue(GM.fx.HasEffect("Snowfall"), "雪がふる");
            Assert.IsTrue(GM.fx.HasEffect("StarHalo"), "ツリーの星が光る");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_stove"), "家の中の、だんろの音");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), XmasLayout.Spawn), 3f, "光の輪の出口");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.forward), 0.6f, "ツリー（北）の方を向く");

            // 雪の上を歩くと、さくさく（足あとがのこる）
            Vector3 c = Worm.CenterPosition;
            Assert.AreEqual(AudioManager.Surface.Sand, AreaProps.Refine(AudioManager.Surface.Ground, XmasLayout.Ground(c.x + 6f, c.z)), "雪");

            // 南の光の輪から、夜の森へもどる
            yield return Seconds(1.5f);
            var back = GM.world.Gates.Single(g => g.def.targetArea == "night");
            yield return WalkInto(back);
            Assert.AreSame(Areas.Night, Areas.Current);
            var ringAgain = GM.world.Gates.Single(g => g.def.targetArea == "xmas");
            Assert.Less(Vector3.Distance(Worm.CenterPosition, ringAgain.position), 12f, "クリスマス村への光の輪のそばに出る");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyOpen"), "夜の森は、樹冠の夜空");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Hare_FreezesWithEarsUp_ThenLeapsAway()
        {
            yield return GoTo(Areas.Xmas);
            Vector3 hare = C.PositionOf("yukiusagi");
            Assert.AreNotEqual(Vector3.zero, hare, "ユキウサギがいる");
            // 少しはなれた所から、近づく（止まって、耳を立てる）
            Vector3 dir = Vector3.ProjectOnPlane(Vector3.zero - hare, Vector3.up).normalized;
            Worm.Spawn(TopSurface(new Vector2(hare.x + dir.x * 4.5f, hare.z + dir.z * 4.5f)), -dir);
            float frozeAlert = 0f, frozeFor = 0f;
            bool leaped = false;
            Vector3 frozeAt = hare;
            for (float t = 0f; t < 10f && !leaped; t += Time.deltaTime)
            {
                C.NearestOf("yukiusagi", frozeAt, out var p, out var alert, out var air);
                if (!air && alert > 0.5f)
                {
                    // 止まって耳を立てているあいだは、動かない
                    if (frozeFor == 0f) frozeAt = p;
                    frozeFor += Time.deltaTime;
                    frozeAlert = Mathf.Max(frozeAlert, alert);
                }
                if (frozeFor > 0.3f && (air || Vector3.Distance(p, frozeAt) > 2.5f)) leaped = true;
                yield return null;
            }
            Assert.Greater(frozeAlert, 0.5f, "まず、ぴたっと止まって耳を立てる");
            Assert.IsTrue(leaped, "それから、跳んで逃げる");
            Assert.Greater(AreaSounds.Instance.CountOf("hare_thump"), 0, "うしろ足で、雪を「トン」");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Reindeer_RingsItsBell_AndLooksAtTheInchworm()
        {
            yield return GoTo(Areas.Xmas);
            Assert.AreEqual(2, C.CountOf("tonakai"));
            Vector3 deer = C.PositionOf("tonakai");
            // そばで待つと、歩いたり雪をほったりして、首の鈴が「チリン」
            Vector3 side = new Vector3(1f, 0f, 0.4f).normalized;
            Worm.Spawn(TopSurface(new Vector2(deer.x + side.x * 12f, deer.z + side.z * 12f)), -side);
            yield return WaitUntil(() => AreaSounds.Instance.CountOf("reindeer_bell") > 0, 45f, "鈴が鳴る");
            // 近づくと、立ち止まって、こちらを見る（逃げない）
            deer = C.PositionOf("tonakai");
            Worm.Spawn(TopSurface(new Vector2(deer.x + side.x * 5f, deer.z + side.z * 5f)), -side);
            bool looked = false;
            for (float t = 0f; t < 2f && !looked; t += Time.deltaTime)
            {
                C.NearestOf("tonakai", Worm.CenterPosition, out _, out var alert, out _);
                looked = alert > 0.5f;
                yield return null;
            }
            Assert.IsTrue(looked, "顔を上げて見る");
            yield return Seconds(1f);
            C.NearestOf("tonakai", Worm.CenterPosition, out var after, out _, out _);
            Assert.Less(Vector2.Distance(new Vector2(after.x, after.z), new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z)), 9f, "逃げない");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Dormouse_SleepsInTheMitten_RollsOver_AndPeeksWhenTouched()
        {
            yield return GoTo(Areas.Xmas);
            Assert.IsTrue(C.DormouseState(out var pos, out _, out _), "ヤマネがいる");
            Assert.Less(Vector3.Distance(pos, GM.world.MittenMouth), 0.3f, "手ぶくろの口");
            // ねむっている所に、そっとふれると、うす目をあける
            Vector3 face = Quaternion.Euler(0f, XmasLayout.MittenYaw, 0f) * Vector3.forward;
            Worm.Spawn(TopSurface(new Vector2(pos.x + face.x * 1.6f, pos.z + face.z * 1.6f)), -face);
            float emerge = 0f;
            yield return WaitUntil(() => C.DormouseState(out _, out emerge, out _) && emerge > 0.85f, 6f, "うす目をあける");
            // 寝返り（なかよし ★5 のとっておき）
            Assert.IsTrue(C.ForceBehaviorForTests("yamane", "special"));
            yield return Frames(2);
            Assert.IsTrue(C.DormouseState(out var p2, out _, out bool rolling) && rolling, "ころんと寝返り");
            Assert.Less(Vector3.Distance(p2, pos), 0.05f, "冬眠中は、動かない");
            // はなれると、また眠る
            Worm.Spawn(TopSurface(new Vector2(pos.x + face.x * 14f, pos.z + face.z * 14f)), -face);
            yield return WaitUntil(() => C.DormouseState(out _, out emerge, out _) && emerge < 0.1f, 20f, "また眠る");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Robin_PerchesHigh_AndFliesToAnotherPerch()
        {
            yield return GoTo(Areas.Xmas);
            Vector3 p = C.PositionOf("robin");
            Assert.Greater(p.y - XmasLayout.Height(p.x, p.z), 8f, "さくのくい・ポスト・ランプの上");
            Assert.IsTrue(GM.world.XmasPerches.Any(q => Vector3.Distance(q, p) < 2f), "とまり木の上");
            Vector3 near = Physics.Raycast(p + new Vector3(0.8f, 1.5f, 0f), Vector3.down, out var perch, 4f, ShakuConst.SurfaceMask) ? perch.point : p + Vector3.up * 0.3f;
            Worm.Spawn(near, Vector3.left);
            yield return WaitUntil(() => C.Info("robin").airborne, 8f, "飛び立つ");
            yield return WaitUntil(() => !C.Info("robin").airborne, 30f, "とまる");
            Vector3 q2 = C.PositionOf("robin");
            Assert.IsTrue(GM.world.XmasPerches.Any(q => Vector3.Distance(q, q2) < 2f), "ほかのとまり木へ");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator WinterMoth_CirclesTheGardenLamp()
        {
            yield return GoTo(Areas.Xmas);
            Assert.GreaterOrEqual(C.CountOf("fuyushaku"), 3);
            Worm.Spawn(TopSurface(new Vector2(0f, -30f)), Vector3.forward);
            bool circling = false;
            for (float t = 0f; t < 20f && !circling; t += 0.25f)
            {
                for (int i = 0; i < C.CountOf("fuyushaku") && !circling; i++)
                {
                    Vector3 m = C.PositionOf("fuyushaku", i);
                    circling = C.Info("fuyushaku", i).airborne && GM.world.XmasLamps.Any(l => Vector3.Distance(l, m) < 4f);
                }
                yield return Seconds(0.25f);
            }
            Assert.IsTrue(circling, "ランプのあかりのまわりを飛ぶ");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Treetop_IsAPlaceToStandBesideTheStar()
        {
            yield return GoTo(Areas.Xmas);
            var top = XmasLayout.Landmarks.Single(l => l.id == 88);
            Col.Discover(top);   // 地図から行けるように
            Assert.IsTrue(GM.FastTravel(top.id));
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 60f, "ツリーのてっぺんへ");
            yield return Seconds(2f);
            Vector3 c = Worm.CenterPosition;
            Assert.Greater(c.y - XmasLayout.Height(c.x, c.z), top.minHeightAboveGround, "ツリーのてっぺんにいる");
            Assert.IsTrue(Collectibles.IsInside(top, Worm.HeadPosition), "名所「ツリーのてっぺん」の中");
            Assert.IsFalse(Worm.IsFalling, "落ちない");
        }
    }
}

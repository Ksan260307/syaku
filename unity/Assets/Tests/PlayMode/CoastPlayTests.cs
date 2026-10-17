using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：海岸（川辺の下流のトンネルから行く・波の音・海のいきもの・めずらしいマンタ）。</summary>
    public class CoastPlayTests
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
        public void TearDown()
        {
            ResetInput();
            Creatures.RareChanceOverride = null;
        }

        IEnumerator GoTo(AreaLayout area)
        {
            SaveSystem.Data.visited.Add(area.Id);
            GM.TravelTo(area.Id);
            yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, area.Id + "へ");
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
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 120f, "着く");
        }

        [UnityTest]
        public IEnumerator Tunnel_FromTheRiverLeadsDownToTheSeasideAndBack()
        {
            yield return GoTo(Areas.River);
            var gate = GM.world.Gates.Single(g => g.def.targetArea == "coast");
            Place(new Vector2(gate.position.x + gate.inward.x * 4f, gate.position.z + gate.inward.z * 4f), -gate.inward);
            yield return Frames(3);
            StringAssert.Contains("まだ行ったことがない場所", UI.PromptText, "トンネルの近くでヒントが出る");
            StringAssert.DoesNotContain("潮風の海辺", UI.PromptText, "まだ行ったことがない場所の名前は出さない");
            yield return WalkInto(gate);

            Assert.AreSame(Areas.Coast, Areas.Current);
            Assert.AreEqual(30, Col.TotalDrops);
            Assert.AreEqual("coast", SaveSystem.Data.area);
            Assert.AreEqual("潮風の海辺", UI.AreaTitle);
            Assert.AreEqual("海のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.IsNotNull(AudioManager.Instance.musicCoast);
            Assert.AreSame(AudioManager.Instance.musicCoast, AudioManager.Instance.CurrentMusic, "海辺の BGM");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_SkyOpen"), "海辺は、ひらけた空");
            Assert.IsNull(GM.world.sun.cookie, "海辺に、木もれ日のもようはない");
            Assert.IsTrue(AreaSounds.Instance.HasLoop("loop_surf"), "波打ちぎわの音");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), CoastLayout.Spawn), 3f, "トンネルの出口");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.back), 0.6f, "海の方（南）を向く");

            // 北のトンネルから、川辺の下流へもどる
            yield return Seconds(1.5f);
            var back = GM.world.Gates.Single(g => g.def.targetArea == "river");
            yield return WalkInto(back);
            Assert.AreSame(Areas.River, Areas.Current);
            var coastGate = GM.world.Gates.Single(g => g.def.targetArea == "coast");
            Assert.Less(Vector3.Distance(Worm.CenterPosition, coastGate.position), 12f, "海辺へのトンネルのそばに出る");
        }

        [UnityTest]
        public IEnumerator MapTravel_FromTheForest_LooksOutToTheSea()
        {
            // 森（はじめは北を向いている）から地図で移動しても、カメラは、着いた向き（南の海）を見る
            yield return GoTo(Areas.Coast);
            Vector3 camFwd = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            Assert.Greater(Vector3.Dot(camFwd, Vector3.back), 0.6f, "カメラは海の方を見る");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.back), 0.6f, "しゃくとりむしも海の方を向く");
        }

        [UnityTest]
        public IEnumerator HermitCrab_HidesInItsShell_AndComesOutAgain()
        {
            yield return GoTo(Areas.Coast);
            Vector3 p = C.PositionOf("yadokari");
            Assert.AreNotEqual(Vector3.zero, p);
            Worm.Spawn(TopSurface(new Vector2(p.x + 0.9f, p.z)), Vector3.left);
            yield return WaitUntil(() => C.Info("yadokari").retreat > 0.5f, 5f, "殻にひっこむ");
            Worm.Spawn(TopSurface(new Vector2(p.x + 12f, p.z + 3f)), Vector3.right);
            yield return WaitUntil(() => C.Info("yadokari").retreat <= 0f, 10f, "また出てくる");
        }

        [UnityTest]
        public IEnumerator FiddlerCrab_RunsIntoItsBurrow_AndPeeksOutLater()
        {
            yield return GoTo(Areas.Coast);
            Vector3 hole = GM.world.Burrows[0];
            int idx = Enumerable.Range(0, C.CountOf("shiomaneki")).First(i => Vector2.Distance(new Vector2(C.PositionOf("shiomaneki", i).x, C.PositionOf("shiomaneki", i).z), new Vector2(hole.x, hole.z)) < 3f);
            Worm.Spawn(TopSurface(new Vector2(hole.x + 9f, hole.z + 6f)), Vector3.left);
            yield return Seconds(1f);
            Assert.Less(C.DigOf("shiomaneki", idx), 0.1f, "はじめは、巣穴の外");
            Worm.Spawn(TopSurface(new Vector2(hole.x + 1.6f, hole.z)), Vector3.left);
            yield return WaitUntil(() => C.DigOf("shiomaneki", idx) > 0.9f, 8f, "巣穴にもぐる");
            Vector3 at = C.PositionOf("shiomaneki", idx);
            Assert.Less(Vector2.Distance(new Vector2(at.x, at.z), new Vector2(hole.x, hole.z)), 0.8f, "巣穴のところで、もぐる");
            Worm.Spawn(TopSurface(new Vector2(hole.x + 12f, hole.z + 8f)), Vector3.right);
            yield return WaitUntil(() => C.DigOf("shiomaneki", idx) < 0.05f, 25f, "そっと出てくる");
        }

        [UnityTest]
        public IEnumerator StarfishAndUrchins_StayInTheTidePools()
        {
            yield return GoTo(Areas.Coast);
            yield return Seconds(8f);
            foreach (var id in new[] { "hitode", "uni" })
                for (int i = 0; i < C.CountOf(id); i++)
                {
                    Vector3 p = C.PositionOf(id, i);
                    Assert.GreaterOrEqual(CoastLayout.PoolAt(p.x, p.z, 0.3f), 0, $"{id} {i} は潮だまりの中 {p}");
                    Assert.Less(p.y, CoastLayout.PoolLevel, $"{id} {i} は水の中");
                }
        }

        [UnityTest]
        public IEnumerator SeaSlaters_ScurryAwayOnTheRocks()
        {
            yield return GoTo(Areas.Coast);
            Vector3 p = C.PositionOf("funamushi");
            Worm.Spawn(TopSurface(new Vector2(p.x + 1.5f, p.z)), Vector3.left);
            float fastest = 0f;
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                yield return null;
                fastest = Mathf.Max(fastest, C.Info("funamushi").moveSpeed);
            }
            Assert.Greater(fastest, 1f, "さっと走って逃げる");
            Vector3 now = C.PositionOf("funamushi");
            Assert.Greater(Vector2.Distance(new Vector2(now.x, now.z), new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z)), 1.5f, "はなれる");
        }

        [UnityTest]
        public IEnumerator Manta_SometimesSwimsInsteadOfTheGull_AndLeapsOutOfTheSea()
        {
            Creatures.RareChanceOverride = 1f;
            yield return GoTo(Areas.Coast);
            Assert.AreEqual(1, C.CountOf("manta"), "カモメのかわりに、マンタ");
            Assert.AreEqual(0, C.CountOf("kamome"));
            Vector3 start = C.PositionOf("manta");
            Assert.Less(CoastLayout.Height(start.x, start.z), Creatures.MantaSwimY - 0.5f, "沖の海");
            float top = -9f;
            yield return WaitUntil(() =>
            {
                top = Mathf.Max(top, C.PositionOf("manta").y);
                return top > 2f;
            }, 30f, "海からジャンプする");
            Vector3 now = C.PositionOf("manta");
            Assert.Greater(Vector3.Distance(now, start), 3f, "泳いでいる");
            Assert.Less(CoastLayout.Height(now.x, now.z), 0f, "浜には上がらない");
            // 波打ちぎわから、見つけられる
            Worm.Spawn(TopSurface(new Vector2(now.x, CoastLayout.ShoreZ(now.x) + 0.6f)), Vector3.back);
            yield return WaitUntil(() => Creatures.IsDiscovered("manta"), 40f, "浜から見つける");
        }

        [UnityTest]
        public IEnumerator Zukan_HasACoastFilter()
        {
            yield return GoTo(Areas.Coast);
            UI.ShowCollection(true, "zukan");
            yield return Frames(4);
            var chips = UI.Root.Query<Button>(className: "zukan-filter").ToList();
            Assert.AreEqual(9, chips.Count, "すべて・森・川辺・公園・山・山小屋・海岸・神社・夜の森");
            Assert.AreEqual("海岸", chips[6].text);
            UI.ShowCollection(false);
        }
    }
}

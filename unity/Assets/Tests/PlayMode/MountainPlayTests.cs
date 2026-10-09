using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：山（川辺の上流のトンネルから行く・ひらけた空・山頂・山のいきもの）。</summary>
    public class MountainPlayTests
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
        public IEnumerator Tunnel_FromTheRiverLeadsUpToTheMountainAndBack()
        {
            yield return GoTo(Areas.River);
            var gate = GM.world.Gates.Single(g => g.def.targetArea == "mountain");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyOpen"), "川辺は、樹冠の空");
            Place(new Vector2(gate.position.x + gate.inward.x * 4f, gate.position.z + gate.inward.z * 4f), -gate.inward);
            yield return Frames(3);
            StringAssert.Contains("雲の見える山", UI.PromptText, "トンネルの近くでヒントが出る");
            yield return WalkInto(gate);

            Assert.AreSame(Areas.Mountain, Areas.Current);
            Assert.AreEqual(40, Col.TotalDrops);
            Assert.AreEqual("mountain", SaveSystem.Data.area);
            Assert.AreEqual("雲の見える山", UI.AreaTitle);
            Assert.AreEqual("山のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.AreSame(AudioManager.Instance.musicMountain, AudioManager.Instance.CurrentMusic, "山の BGM");
            Assert.IsNotNull(AudioManager.Instance.musicMountain);
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_SkyOpen"), "山は、ひらけた空");
            Areas.Mountain.ArrivalFrom("river", out var arrive, out var fwd);
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), arrive), 2.5f, "ふもとのトンネルの出口");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.forward), 0.6f, "山頂の方（北）を向く");

            // ふもとのトンネルから、川辺の上流へもどる
            yield return Seconds(1.5f);
            var back = GM.world.Gates.Single(g => g.def.targetArea == "river");
            yield return WalkInto(back);
            Assert.AreSame(Areas.River, Areas.Current);
            var mtnGate = GM.world.Gates.Single(g => g.def.targetArea == "mountain");
            Assert.Less(Vector3.Distance(Worm.CenterPosition, mtnGate.position), 12f, "山へのトンネルのそばに出る");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyOpen"));
        }

        [UnityTest]
        public IEnumerator Summit_IsDiscoveredWhenYouGetThere()
        {
            yield return GoTo(Areas.Mountain);
            Assert.IsFalse(SaveSystem.Data.places.Contains(38));
            Place(MountainLayout.Summit + new Vector2(0f, -2.5f), Vector3.back);
            yield return WaitUntil(() => SaveSystem.Data.places.Contains(38), 5f, "山頂を見つける");
            // 岩の階段は、いちばん上の段に登ると見つかる（下では見つからない）
            Place(MountainLayout.RockStairs + new Vector2(-4f, 0f), Vector3.right);
            yield return Seconds(1f);
            Assert.IsFalse(SaveSystem.Data.places.Contains(31), "下にいるあいだは、見つからない");
            var top = GM.world.StairTops[GM.world.StairTops.Count - 1];
            Worm.Spawn(top + Vector3.up * 0.05f, Vector3.forward);
            yield return WaitUntil(() => SaveSystem.Data.places.Contains(31), 5f, "いちばん上の段で見つける");
        }

        [UnityTest]
        public IEnumerator Stoat_PeeksOutOfItsRockDen()
        {
            yield return GoTo(Areas.Mountain);
            Vector3 den = C.PositionOf("okojo");
            Assert.AreNotEqual(Vector3.zero, den, "岩のすみかにオコジョがいる");
            Worm.Spawn(TopSurface(new Vector2(den.x + 10f, den.z)), Vector3.left);
            yield return WaitUntil(() => C.DigOf("okojo") < 0.2f, 25f, "顔を出す");
            Assert.IsFalse(Creatures.IsDiscovered("okojo"), "遠くからは、まだ見つけない");
            // すっかり顔を出すと、体は穴の上に立ち上がっている
            yield return WaitUntil(() => C.DigOf("okojo") < 0.02f, 10f, "立ち上がる");
            Vector3 body = C.BodyMatrixOf("okojo").GetColumn(3);
            Assert.Greater(body.y, MountainLayout.Height(den.x, den.z) - 0.6f, "穴から出ている");
            Worm.Spawn(TopSurface(new Vector2(den.x + 4.5f, den.z)), Vector3.left);
            yield return WaitUntil(() => Creatures.IsDiscovered("okojo"), 30f, "顔を出したところを見つける");
        }

        [UnityTest]
        public IEnumerator PikaAndCicada_Call()
        {
            yield return GoTo(Areas.Mountain);
            foreach (var id in new[] { "nakiusagi", "higurashi" })
            {
                if (!SaveSystem.Data.creatures.Contains(id)) SaveSystem.Data.creatures.Add(id);
                var n = Friends.Note(id);
                n.friendAt = -999f;
                Vector3 p = C.PositionOf(id);
                Assert.AreNotEqual(Vector3.zero, p, id);
                Vector3 at = SurfaceNear(p, 2.2f);
                Vector3 face = p - at;
                face.y = 0f;
                Worm.Spawn(at, face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward);
                yield return Seconds(0.3f);
                Assert.IsTrue(C.ForceBehaviorForTests(id, "call"), id + "：鳴く");
                yield return WaitUntil(() => Friends.HasSeen(id, "call"), 3f, id + " が鳴くのを観察");
            }
        }

        [UnityTest]
        public IEnumerator MountainCreatures_ComeAlive()
        {
            yield return GoTo(Areas.Mountain);
            foreach (var id in new[] { "raichou", "risu", "okojo", "nakiusagi", "sanshouuo", "asagimadara", "maruhanabachi", "oniyanma", "higurashi", "maimaikaburi" })
                Assert.Greater(C.CountOf(id), 0, id + " がいる");
            Vector3 b0 = C.PositionOf("asagimadara"), y0 = C.PositionOf("oniyanma");
            yield return Seconds(4f);
            Assert.Greater(Vector3.Distance(b0, C.PositionOf("asagimadara")), 0.5f, "アサギマダラが舞う");
            Assert.Greater(Vector3.Distance(y0, C.PositionOf("oniyanma")), 1f, "オニヤンマが飛ぶ");
            Vector3 yp = C.PositionOf("oniyanma");
            Assert.Greater(yp.y - Areas.Current.Height(yp.x, yp.z), 0.5f, "オニヤンマは飛んでいる");
            // リスは、近づくとさっと走って逃げる
            Vector3 r0 = C.PositionOf("risu");
            Worm.Spawn(SurfaceNear(r0, 5f), Vector3.forward);
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(r0, C.PositionOf("risu")), 1.5f, "リスが走る");
        }
    }
}

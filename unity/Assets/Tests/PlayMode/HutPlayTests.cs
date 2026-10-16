using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：山小屋（山の山小屋のとびらから入る・部屋の中の光と音・小屋のいきもの・とびらから山へもどる）。</summary>
    public class HutPlayTests
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

        /// <summary>とびらの前に置いて、とびらへ歩いて入る。</summary>
        IEnumerator WalkInto(GateInstance gate)
        {
            Vector3 outward = -gate.inward;
            Vector3 start = gate.position + gate.inward * 4f;
            Place(new Vector2(start.x, start.z), outward);
            yield return FaceCamera(outward);
            yield return Frames(3);
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => GM.State == GameManager.GameState.Traveling, 25f, "とびらに入る");
            ResetInput();
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 120f, "着く");
        }

        [UnityTest]
        public IEnumerator HutDoor_LeadsIntoTheWarmHutAndBackOutToTheMountain()
        {
            yield return GoTo(Areas.Mountain);
            var door = GM.world.Gates.Single(g => g.def.targetArea == "hut");
            Place(new Vector2(door.position.x + door.inward.x * 4f, door.position.z + door.inward.z * 4f), -door.inward);
            yield return Frames(3);
            StringAssert.Contains("まだ入ったことがない場所", UI.PromptText, "とびらの近くでヒントが出る");
            StringAssert.DoesNotContain("あたたかい山小屋", UI.PromptText, "まだ行ったことがない場所の名前は出さない");
            StringAssert.Contains("とびら", UI.PromptText, "トンネルではなく、とびら");
            yield return WalkInto(door);

            Assert.AreSame(Areas.Hut, Areas.Current);
            Assert.AreEqual(30, Col.TotalDrops);
            Assert.AreEqual("hut", SaveSystem.Data.area);
            Assert.AreEqual("あたたかい山小屋", UI.AreaTitle);
            Assert.AreEqual("小屋のしずく", UI.Root.Q<Label>("drop-label").text);
            Assert.IsNotNull(AudioManager.Instance.musicHut);
            Assert.AreSame(AudioManager.Instance.musicHut, AudioManager.Instance.CurrentMusic, "山小屋の BGM");
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_SkyOpen"), "小屋の中に、空はない");
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) Assert.Greater(urp.shadowDistance, 100f, "部屋の奥まで、かべと屋根のかげ");
            Assert.IsNull(GM.world.sun.cookie, "小屋の中に、木もれ日のもようはない");
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), HutLayout.Spawn), 3f, "入り口の前");
            Assert.Greater(Vector3.Dot(Worm.Heading, Vector3.forward), 0.5f, "部屋の奥を向く");
            Assert.IsTrue(HutLayout.InRoom(Worm.CenterPosition.x, Worm.CenterPosition.z));

            // あいたとびらから、山へもどる
            yield return Seconds(1.5f);
            var back = GM.world.Gates.Single(g => g.def.targetArea == "mountain");
            yield return WalkInto(back);
            Assert.AreSame(Areas.Mountain, Areas.Current);
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), MountainLayout.HutDoor), 8f, "山小屋のとびらの前に出る");
            Assert.IsNotNull(GM.world.sun.cookie, "外では、また木もれ日のもよう");
            if (urp != null) Assert.Less(urp.shadowDistance, 60f, "外では、もとの影の距離");
        }

        [UnityTest]
        public IEnumerator HutCreatures_ClimbWallsFollowTheirHeadsAndFly()
        {
            yield return GoTo(Areas.Hut);
            Assert.Less(Worm.CenterPosition.y, 2f, "地図から来ても、床に着く（はりの上ではない）");
            yield return Seconds(4f);
            // ヤモリは、かべにはりついたまま
            for (int i = 0; i < C.CountOf("yamori"); i++)
            {
                var info = C.Info("yamori", i);
                Assert.Less(Mathf.Abs(info.up.y), 0.5f, "ヤモリの体は、かべの向き");
                Assert.Greater(info.pos.y, 10f, "ヤモリは、かべの高い所");
            }
            // ムカデは、体をくねらせて歩きまわる
            Vector3 m0 = C.PositionOf("mukade");
            yield return Seconds(6f);
            Assert.AreNotEqual(m0, C.PositionOf("mukade"), "ムカデが動く");
            // ツバメは、近づくと、巣から飛び立つ
            Assert.Greater(C.PositionOf("tsubame").y, HutLayout.BeamTop - 1f, "ツバメは、はりの上");
            Vector3 nest = C.PositionOf("tsubame");
            Worm.Spawn(TopSurface(new Vector2(nest.x + 2.5f, nest.z)), Vector3.left);
            yield return WaitUntil(() => C.IsAirborne("tsubame"), 10f, "ツバメが飛び立つ");
            yield return WaitUntil(() => !C.IsAirborne("tsubame"), 20f, "ツバメが下りる");
            Vector3 landed = C.PositionOf("tsubame");
            Assert.IsTrue(HutLayout.InPlayArea(landed), "部屋の中に下りる");
            Assert.Greater(landed.y, 20f, "はりの上か、窓台に下りる");
            // ハエは、テーブルの上を飛ぶ
            Vector3 fly = C.PositionOf("hae");
            Assert.Greater(fly.y, HutLayout.TableHeight - 1f);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：地図や図鑑から名所へ移動したとき、どの名所でも、よい所から始まる。
    /// カメラが地面や物にぶつかって、しゃくとりむしのすぐうしろへ寄っていない・しゃくとりむしがカメラから見える・
    /// 名所の方を向いている・水の中ではない・しずくの上に着いていない。
    /// </summary>
    public class TravelViewPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        IEnumerator CheckArea(AreaLayout area, List<string> bad)
        {
            if (Areas.Current != area)
            {
                GM.TravelTo(area.Id);
                yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, area.Id);
                yield return Seconds(0.5f);
            }
            foreach (var lm in area.Landmarks)
            {
                Assert.IsTrue(GM.FastTravel(lm.id), lm.name);
                yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 20f, lm.name);
                yield return Seconds(0.8f);
                var v = lm.TravelView;
                Vector3 focus = Worm.CameraFocus;
                Vector3 cam = Camera.main.transform.position;
                Vector3 c = Worm.CenterPosition;
                string who = $"{area.DisplayName}「{lm.name}」";
                if (v != null && Vector3.Distance(cam, focus) < 0.7f * v.distance)
                {
                    string by = Physics.SphereCast(focus, 0.1f, (cam - focus).normalized, out var back, v.distance, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                        ? $"{back.collider.name} ({back.point.x:0.0},{back.point.y:0.0},{back.point.z:0.0})" : "-";
                    bad.Add($"{who}：カメラが寄りすぎ（{Vector3.Distance(cam, focus):0.0} / {v.distance:0.0}、{by}）");
                }
                if (Physics.Linecast(cam, focus, out var hit, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    bad.Add($"{who}：カメラとしゃくとりむしのあいだに {hit.collider.name}");
                if (v != null)
                {
                    Vector3 f = v.Forward;
                    Vector3 h = Vector3.ProjectOnPlane(Worm.Heading, Vector3.up).normalized;
                    if (Vector3.Dot(h, f) < 0.5f) bad.Add($"{who}：見る先の方を向いていない");
                }
                if (Areas.Current.IsUnderwater(c)) bad.Add($"{who}：水の中");
                foreach (var d in GM.world.DewdropPoints)
                    if (Vector3.Distance(d, c) < 1.2f) bad.Add($"{who}：しずくの上に着く");
            }
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator FastTravel_StartsWithAClearViewOfEveryLandmark()
        {
            foreach (var a in Areas.All)
            {
                SaveSystem.Data.visited.Add(a.Id);
                foreach (var l in a.Landmarks) if (!SaveSystem.Data.places.Contains(l.id)) SaveSystem.Data.places.Add(l.id);
            }
            // 見つけた名所を、読みこみなおす
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing, 180f, "river");
            var bad = new List<string>();
            foreach (var area in Areas.All) yield return CheckArea(area, bad);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FastTravel_OntoACreature_MovesItAsideInsteadOfSidestepping()
        {
            // 古い切り株の頂には、ハエトリグモがすんでいる。着く所（体の下）にいても、しゃくとりむしは着いたとたんに横へよけない。
            // クモのほうが、画面が暗いあいだに少しどいている（よけると、景色から横を向いて、しずくのそばへ寄ってしまう）
            var lm = ForestLayout.Landmarks.Find(l => l.id == 6);
            var pond = ForestLayout.Landmarks.Find(l => l.id == 5);
            Col.Discover(lm);
            Col.Discover(pond);
            yield return Frames(2);
            var C = GM.creatures;
            Vector3 p = GM.world.ArrivalPoint(lm, out var fwd);
            int spider = -1;
            float best = 5f;
            for (int i = 0; i < C.CountOf("spider"); i++)
            {
                float d = Vector3.Distance(C.Info("spider", i).pos, p);
                if (d < best) { best = d; spider = i; }
            }
            Assert.GreaterOrEqual(spider, 0, "切り株の上のハエトリグモ");
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            foreach (float off in new[] { 0f, -0.12f, 0.12f })
            {
                // ほかの所から、クモが体の下にいる切り株の頂へ
                Assert.IsTrue(GM.FastTravel(pond.id));
                yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 20f, pond.name);
                yield return Seconds(1f);
                Vector3 at = p + fwd * 0.35f + side * off;
                Assert.IsTrue(Physics.Raycast(at + Vector3.up, Vector3.down, out var hit, 3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore));
                C.SetPosition("spider", spider, hit.point);
                Assert.IsTrue(GM.FastTravel(lm.id));
                yield return WaitUntil(() => GM.State == GameManager.GameState.Playing, 20f, lm.name);
                Assert.Greater(Vector3.Distance(C.Info("spider", spider).pos, Worm.CenterPosition), 0.6f, $"クモが体の下からどいている（{off}）");
                Vector3 tail = Worm.TailPoint, head = Worm.HeadPoint;
                yield return Seconds(0.8f);
                Assert.Less(Vector3.Distance(Worm.TailPoint, tail) + Vector3.Distance(Worm.HeadPoint, head), 0.05f, $"横へよけない（{off}）");
                Vector3 h = Vector3.ProjectOnPlane(Worm.Heading, Vector3.up).normalized;
                Assert.Greater(Vector3.Dot(h, lm.TravelView.Forward), 0.9f, $"景色の方を向いたまま（{off}）");
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator MapTravel_ToAnAreaWithoutATunnel_StartsAtItsEntranceView()
        {
            // 森から山小屋へ（山小屋につながるトンネルは、山にしかない）：小屋の入り口の景色から
            SaveSystem.Data.visited.Add("hut");
            GM.TravelTo("hut");
            yield return WaitUntil(() => Areas.Current == Areas.Hut && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, "hut");
            yield return Seconds(0.8f);
            var v = HutLayout.Landmarks[0].TravelView;
            Assert.Less(Vector2.Distance(new Vector2(Worm.CenterPosition.x, Worm.CenterPosition.z), v.from), 3.5f, "入り口の景色の所");
            Assert.Greater(Vector3.Distance(Camera.main.transform.position, Worm.CameraFocus), 0.7f * v.distance, "カメラが寄りすぎない");
        }
    }
}

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：いきものの居場所の直し（CREATURE_FIXES.md）。どのエリアでも、ゲームの中で動きだしたいきものが、
    /// 物（キノコの柄・幹・岩・切り株）の中や、水の中にいない（外から見えて、近づける）。
    /// </summary>
    public class CreatureFixPlayTests
    {
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
            if (Areas.Current == area) yield break;
            SaveSystem.Data.visited.Add(area.Id);
            GM.TravelTo(area.Id);
            yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, area.Id + "へ");
        }

        [UnityTest]
        public IEnumerator Creatures_AreNotInsideThingsOrUnderwater()
        {
            foreach (var area in new AreaLayout[] { Areas.Forest, Areas.River, Areas.Park, Areas.Mountain })
            {
                yield return GoTo(area);
                yield return Seconds(2f);
                var cr = GM.creatures;
                int n = 0;
                foreach (var id in cr.SpeciesHere().ToList())
                {
                    var sp = SpeciesCatalog.Get(id);
                    // 飛ぶもの・水の上のもの（葉の下をくぐる）と、巣から出入りするアリは、通りすぎるだけなので見ない
                    if (sp.kind == MobKind.Flutter || sp.kind == MobKind.Hover || sp.kind == MobKind.Bird || sp.kind == MobKind.Skater || sp.kind == MobKind.Marcher) continue;
                    for (int i = 0; i < cr.CountOf(id); i++)
                    {
                        var info = cr.Info(id, i);
                        Assert.IsFalse(WorldGenerator.InsideSolid(info.pos + info.up * 0.12f),
                            $"{area.Id} の {id} #{i} ({info.pos.x:F1}, {info.pos.y:F1}, {info.pos.z:F1}) が物の中にいる");
                        Assert.IsFalse(area.IsUnderwater(info.pos + Vector3.up * 0.08f), $"{area.Id} の {id} #{i} が水の中にいる");
                        n++;
                    }
                }
                Assert.Greater(n, 8, area.Id);
            }
        }

        [UnityTest]
        public IEnumerator MushroomSnail_GoesRoundOutsideTheStem()
        {
            // 大きな赤キノコの根もとをまわる、かたつむり：はじめから柄の外にいて、まわっても柄の中に入らない
            yield return GoTo(Areas.Forest);
            var cr = GM.creatures;
            Vector2 stem = ForestLayout.MushroomGrove;
            int idx = Enumerable.Range(0, cr.CountOf("snail")).OrderBy(i => Vector2.Distance(new Vector2(cr.PositionOf("snail", i).x, cr.PositionOf("snail", i).z), stem)).First();
            for (float t = 0f; t < 6f; t += 0.5f)
            {
                var info = cr.Info("snail", idx);
                Assert.IsFalse(WorldGenerator.InsideSolid(info.pos + info.up * 0.12f), $"{t:F1} 秒：かたつむりがキノコの柄の中にいる");
                Assert.Less(Vector2.Distance(new Vector2(info.pos.x, info.pos.z), stem), 4f, "キノコの根もとから、はなれない");
                yield return Seconds(0.5f);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>山の遊びやすさの直し（MOUNTAIN_FIXES.md）：小道・道しるべ・登る道（ふみ板・倒れた木・石段・とびいし）・草花・いきものの居場所。</summary>
    public class MountainFixesTests
    {
        static WorldGenerator _gen;

        /// <summary>山のしずくの場所（セーブの ID と合うように、変えてはいけない）。</summary>
        static readonly Vector3[] Dews =
        {
            new Vector3(-2.500f, 0.222f, -48.000f),
            new Vector3(4.500f, 0.407f, -45.000f),
            new Vector3(10.490f, 9.443f, -20.388f),
            new Vector3(8.929f, 7.020f, -27.417f),
            new Vector3(34.000f, 30.190f, 28.000f),
            new Vector3(4.400f, 33.958f, 46.400f),
            new Vector3(-35.500f, 6.459f, -14.000f),
            new Vector3(-33.000f, 6.468f, -17.000f),
            new Vector3(-25.500f, 6.669f, -16.000f),
            new Vector3(-24.000f, 6.558f, -11.000f),
            new Vector3(27.009f, 18.690f, 26.079f),
            new Vector3(-30.000f, 15.076f, 30.000f),
            new Vector3(42.000f, 13.636f, -16.000f),
            new Vector3(42.000f, 4.437f, -16.000f),
            new Vector3(-6.000f, 44.964f, 14.000f),
            new Vector3(-9.371f, 30.192f, 6.828f),
            new Vector3(-4.646f, 32.751f, 20.984f),
            new Vector3(-11.038f, 27.040f, 17.863f),
            new Vector3(-30.000f, 9.399f, -1.800f),
            new Vector3(28.000f, 12.432f, 5.000f),
            new Vector3(20.000f, 14.672f, 9.000f),
            new Vector3(-23.000f, 16.217f, 27.000f),
            new Vector3(9.000f, 33.000f, 42.500f),
            new Vector3(-16.000f, 22.296f, 48.000f),
            new Vector3(2.100f, 4.560f, -21.000f),
            new Vector3(43.600f, 2.939f, -25.000f),
            new Vector3(-0.847f, 14.326f, 8.800f),
            new Vector3(-36.177f, 10.691f, 4.597f),
            new Vector3(-39.217f, 2.053f, -31.306f),
            new Vector3(-17.986f, 1.724f, -34.269f),
            new Vector3(5.897f, 1.934f, -34.994f),
            new Vector3(29.626f, 7.699f, -6.338f),
            new Vector3(13.510f, 13.281f, 4.126f),
            new Vector3(23.562f, 19.778f, 25.196f),
            new Vector3(-33.707f, 17.924f, 42.437f),
            new Vector3(-40.803f, 14.920f, 36.307f),
            new Vector3(-17.889f, 5.463f, -16.586f),
            new Vector3(-10.652f, 5.628f, -18.023f),
            new Vector3(15.539f, 2.277f, -32.018f),
            new Vector3(23.617f, 7.683f, -7.712f)
        };

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Mountain;
            _gen.GenerateNow();
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
            Areas.Current = Areas.Forest;
        }

        static int Count(string kind) => _gen.MountainFixes.TryGetValue(kind, out var l) ? l.Count : 0;

        [Test]
        public void Dewdrops_StayExactlyWhereTheyWere()
        {
            Assert.AreEqual(Dews.Length, _gen.DewdropPoints.Count);
            for (int i = 0; i < Dews.Length; i++)
                Assert.Less(Vector3.Distance(Dews[i], _gen.DewdropPoints[i]), 0.01f, $"しずく {i} の場所（直しのあとも同じ）");
        }

        [Test]
        public void WalkingTrails_AreClear()
        {
            // 歩く小道のまん中に、岩・小枝・雪・平たい岩・すみかの石・道しるべ・立てかけた物がない（山小屋の入り口と、松の根は道のはし）
            string[] blockers = { "Rock_", "RiverStone_", "Twig_", "Mtn_Slab", "Okojo_Rocks", "Mtn_SnowMound", "Mtn_Sign", "Mtn_Plank", "Mtn_Log", "Mtn_StepPillar", "Mtn_Haimatsu" };
            foreach (var t in MountainLayout.Trails)
                for (int s = 0; s < t.Length - 1; s++)
                {
                    float len = Vector2.Distance(t[s], t[s + 1]);
                    for (float u = 0f; u < len; u += 0.6f)
                    {
                        Vector2 p = Vector2.Lerp(t[s], t[s + 1], u / len);
                        Vector3 g = MountainLayout.Ground(p.x, p.y);
                        foreach (var c in Physics.OverlapSphere(g + Vector3.up * 0.45f, 0.5f, ShakuConst.SurfaceMask))
                            Assert.IsFalse(blockers.Any(b => c.name.StartsWith(b)), $"小道 {p} を {c.name} がふさぐ");
                    }
                }
            // 山小屋の北の道は、小屋のかべをよけてまわる
            var hut = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r => r.name == "Mtn_Hut").transform;
            var toSummit = MountainLayout.Trails[4];
            for (int s = 1; s < toSummit.Length - 1; s++)
                Assert.Greater(Vector2.Distance(toSummit[s], new Vector2(hut.position.x, hut.position.z)), 11f, "山頂への道は、小屋からはなれて通る");
            // 松への道は、幹と根をよける
            foreach (var p in MountainLayout.Trails[6].Skip(1))
                Assert.Greater(Vector2.Distance(p, MountainLayout.Pine), 7.5f, "松の根をよける");
            // 岩のトンネルの下を、道がくぐりぬける
            Assert.Less(MountainLayout.DistToTrail(MountainLayout.RockArch), 0.5f);
            Assert.Less(MountainLayout.DistToTrail(MountainLayout.RockArch + new Vector2(6f, 0f)), 1.5f, "トンネルの東へ出る");
            // 小道の上の、押せる小石や木の実は、わきへよけてある
            Assert.Greater(Count("fix_trailrock"), 0);
            // 小道のわきの、背の高い草花はない（低いカメラの前をふさがない）
            foreach (var i in _gen.instanced.Instances())
            {
                if (i.material != _gen.assets.foliage && i.material != _gen.assets.flowers) continue;
                if (i.mesh.bounds.max.y * i.matrix.lossyScale.y < 2.4f) continue;
                Vector3 p = i.matrix.GetColumn(3);
                Assert.Greater(MountainLayout.DistToTrail(new Vector2(p.x, p.z)), 2.1f, $"小道のわきの {i.mesh.name}");
            }
        }

        [Test]
        public void Signposts_PointAlongTheTrails()
        {
            Assert.IsFalse(Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Any(c => c.name == "Mtn_Sign"), "前の道しるべ（矢印が反対向きにくっついていた）は使わない");
            var junctions = WorldGenerator.TrailJunctions();
            Assert.GreaterOrEqual(junctions.Count, 6, "分かれ道と、道のつながる所");
            Assert.AreEqual(junctions.Count + 1, Count("fix_sign"), "分かれ道ごとと、ふもとの入り口に道しるべ");
            var arrows = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.name == "Mtn_SignArrow").ToList();
            Assert.GreaterOrEqual(arrows.Count, junctions.Sum(j => j.dirs.Count));
            foreach (var post in _gen.MountainFixes["fix_sign"])
            {
                Assert.Greater(MountainLayout.DistToTrail(new Vector2(post.x, post.z)), 1.9f, "柱は道のわき");
                foreach (var d in _gen.DewdropPoints) Assert.Greater(Vector2.Distance(new Vector2(post.x, post.z), new Vector2(d.x, d.z)), 1.5f, "しずくのそばに立てない");
            }
            // 矢印は、どれも、そのそばの分かれ道から出る道の向きを指す（メッシュの +X）
            foreach (var a in arrows)
            {
                Vector3 x = a.transform.right;
                Vector2 dir = new Vector2(x.x, x.z).normalized;
                Vector2 at = new Vector2(a.transform.position.x, a.transform.position.z);
                var near = junctions.Select(j => (j.at, j.dirs)).Concat(new[] { (MountainLayout.Trails[0][1], new List<Vector2> { (MountainLayout.Trails[0][2] - MountainLayout.Trails[0][1]).normalized, (MountainLayout.Trails[0][0] - MountainLayout.Trails[0][1]).normalized }) })
                    .OrderBy(j => Vector2.Distance(j.Item1, at)).First();
                Assert.IsTrue(near.Item2.Any(d => Vector2.Dot(d, dir) > 0.99f), $"矢印 {at} の向き");
            }
        }

        [Test]
        public void HutRoof_IsReachedByAWidePlankOnTheQuietSide()
        {
            var plank = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Single(c => c.name.StartsWith("Mtn_Plank"));
            var hut = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r => r.name == "Mtn_Hut").transform;
            Assert.GreaterOrEqual(plank.bounds.size.magnitude, 6f);
            Assert.GreaterOrEqual(_gen.assets.TryGet(plank.name).bounds.size.x * plank.transform.lossyScale.x, 2.8f, "はばの広い板（細いと横へ落ちる）");
            // 板は、地面からうき上がらず（下のはしは地面にうまる）、上のはしは屋根の上にのる
            var t = plank.transform;
            Vector3 bottom = t.position, f = t.forward;
            Assert.Less(bottom.y, MountainLayout.Height(bottom.x, bottom.z), "下のはしは、地面にうめてある");
            float len = _gen.assets.TryGet(plank.name).bounds.size.z * t.lossyScale.z;
            Vector3 top = bottom + f * len;
            Assert.IsTrue(Physics.Raycast(top + Vector3.up * 1f, Vector3.down, out var hit, 3f, ShakuConst.SurfaceMask) && (hit.collider.name.StartsWith("Mtn_Plank") || hit.collider.name == "Mtn_Hut"), "上のはしは、屋根の上");
            // 板の上の面は、とちゅうで地面にうまらない
            for (float u = 0.2f; u <= 0.95f; u += 0.05f)
            {
                Vector3 q = Vector3.Lerp(bottom, top, u);
                Assert.IsTrue(Physics.Raycast(new Vector3(q.x, q.y + 3f, q.z), Vector3.down, out var h2, 6f, ShakuConst.SurfaceMask), $"{u}");
                Assert.IsFalse(h2.collider.name.StartsWith("Terrain_"), $"板のとちゅう {u:F2} が地面にうまる");
            }
            // 道のない山がわ（入り口のうら）で、えんとつと反対のはし
            Vector3 local = hut.InverseTransformPoint(top);
            Assert.Less(local.z, 0f, "入り口のうら");
            Assert.Greater(local.x, 0f, "えんとつ（-X のはし）と反対");
            Assert.Greater(MountainLayout.DistToTrail(new Vector2(bottom.x, bottom.z)), 3f, "小道をふさがない");
            // 宙にういた土台には、石がならぶ
            Assert.Greater(Count("fix_hutbase"), 5);
        }

        [Test]
        public void ArchTop_IsReachedByAFallenTrunk_AwayFromTheDens()
        {
            var logs = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.name == "Mtn_Log").ToList();
            Vector3 arch = MountainLayout.Ground(MountainLayout.RockArch.x, MountainLayout.RockArch.y);
            var log = logs.OrderBy(c => Vector3.Distance(c.bounds.center, arch)).First();
            Vector3 bottom = log.transform.position, top = bottom + log.transform.forward * 13.5f;
            Assert.Greater(top.y - arch.y, 7.5f, "上のはしは、アーチの上");
            Assert.IsTrue(Physics.Raycast(top + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ShakuConst.SurfaceMask), "上のはしの下に、アーチ");
            foreach (var d in _gen.OkojoDens)
                Assert.Greater(ShakuMath.DistToSegment(d, new Vector2(bottom.x, bottom.z), new Vector2(top.x, top.z)), 5f, "オコジョのすみかをよける");
            Assert.AreEqual(1, Count("fix_pinelog"), "松の北の葉のかたまりにも、倒れた木");
        }

        [Test]
        public void SpringStones_AreJoinedFromTheShore()
        {
            Vector2 c = MountainLayout.Spring;
            Vector2[] dewStones = { c + new Vector2(6f, 1f), c + new Vector2(4.5f, -4f), c + new Vector2(-3f, -5f), c + new Vector2(-5.5f, -2f) };
            var stones = _gen.MountainFixes["fix_springstone"].Select(p => new Vector2(p.x, p.z)).Concat(dewStones).ToList();
            // 岸（水の外）から、石をつたって、しずくの石まで、すきまが 1.4 より大きい所がない
            var reached = new List<Vector2>();
            // 小道の終わり（岸）から、1 つ目のしずくの石の方へ、水ぎわまで歩く
            Vector2 shore = c + new Vector2(9.8f, 0.8f), edge = shore;
            Assert.IsFalse(MountainLayout.IsUnderwater(MountainLayout.Ground(shore.x, shore.y)), "はじまりは岸");
            for (float u = 0f; u <= 1f; u += 0.02f)
            {
                Vector2 q = Vector2.Lerp(shore, dewStones[0], u);
                if (MountainLayout.IsUnderwater(MountainLayout.Ground(q.x, q.y))) break;
                edge = q;
            }
            var frontier = new List<Vector2> { edge };
            while (frontier.Count > 0)
            {
                var p = frontier[0];
                frontier.RemoveAt(0);
                foreach (var s in stones)
                    if (!reached.Contains(s) && Vector2.Distance(s, p) < 1.4f) { reached.Add(s); frontier.Add(s); }
            }
            foreach (var d in dewStones) CollectionAssert.Contains(reached, d, $"しずくの石 {d} まで、とびいしでつながる");
            foreach (var s in _gen.MountainFixes["fix_springstone"])
                Assert.That(s.y - MountainLayout.SpringLevel, Is.InRange(0.1f, 0.4f), "とびいしの上は、水面の少し上");
        }

        [Test]
        public void SideStairs_ClimbInEasySteps_ToTheTopRock()
        {
            var steps = _gen.MountainFixes["fix_sidestairs"];
            Assert.GreaterOrEqual(steps.Count, 10);
            for (int i = 1; i < steps.Count; i++)
            {
                Assert.Less(steps[i].y - steps[i - 1].y, 0.6f, "ひとつずつ、低い段");
                Assert.Less(Vector2.Distance(new Vector2(steps[i].x, steps[i].z), new Vector2(steps[i - 1].x, steps[i - 1].z)), 1.4f, "となりの石は、すきまなくならぶ");
            }
            var top = _gen.StairTops[_gen.StairTops.Count - 1];
            Assert.AreEqual(top.y, steps[steps.Count - 1].y, 0.15f, "いちばん上の石段は、いちばん上の岩と同じ高さ");
            Assert.Less(Vector2.Distance(new Vector2(steps[steps.Count - 1].x, steps[steps.Count - 1].z), new Vector2(top.x, top.z)), 3f);
            // 石段は、横がまっすぐ（ひさしにならない）：上の面より下の面の方が広いか、同じ
            var pillar = _gen.assets.TryGet("Mtn_StepPillar");
            Assert.IsNotNull(pillar);
            Assert.IsFalse(pillar.normals.Any(n => n.y < -0.3f && n.y > -0.95f), "下を向いた横の面（ひさし）がない");
        }

        [Test]
        public void Cairn_IsSteppedAndClearOfTheBenchmark()
        {
            Assert.AreEqual(1, Count("fix_cairn"));
            var cairn = _gen.MountainFixes["fix_cairn"][0];
            var bm = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).First(c => c.name == "Mtn_Benchmark");
            Assert.Greater(Vector2.Distance(new Vector2(cairn.x, cairn.z), new Vector2(bm.bounds.center.x, bm.bounds.center.z)), 3.5f, "三角点からはなす");
        }

        [Test]
        public void Dewdrops_AreNotHiddenByPlants()
        {
            foreach (var i in _gen.instanced.Instances())
            {
                if (i.material != _gen.assets.foliage && i.material != _gen.assets.flowers) continue;
                float h = i.mesh.bounds.max.y * i.matrix.lossyScale.y;
                if (h < 0.5f) continue;
                Vector3 p = i.matrix.GetColumn(3);
                foreach (var d in _gen.DewdropPoints)
                    if (p.y + h > d.y && p.y < d.y + 1f)
                        Assert.Greater(new Vector2(p.x - d.x, p.z - d.z).magnitude, 0.7f, $"しずく {d} を {i.mesh.name} がかくす");
            }
        }

        [Test]
        public void GroundCreatures_KeepOffTrailsAndViews()
        {
            string[] ground = { "raichou", "risu", "nakiusagi", "sanshouuo", "maimaikaburi", "grasshopper", "ladybug", "spider", "snail" };
            foreach (var m in _gen.Mobs.Where(m => ground.Contains(m.species)))
            {
                Vector2 c = new Vector2(m.center.x, m.center.z);
                Assert.Greater(MountainLayout.DistToTrail(c), 2.4f, $"{m.species} {c} は小道の上にいない");
                Assert.IsFalse(_gen.InViewLane(c), $"{m.species} {c} は着いたときの景色の前にいない");
            }
            var crow = _gen.Mobs.Single(m => m.species == "crow");
            foreach (var p in crow.path)
                Assert.Greater(Vector2.Distance(new Vector2(p.x, p.z), MountainLayout.Summit), 8f, "カラスは、山頂のまん中に下りない");
            var den = _gen.OkojoDens[0];
            Assert.Greater(MountainLayout.DistToTrail(den), 6f, "岩の階段のそばのオコジョのすみかは、小道からはなす");
        }
    }
}

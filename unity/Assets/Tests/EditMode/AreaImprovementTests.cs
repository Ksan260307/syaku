using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>エリアの改善 300：エリアを作って、追加した小物・道・地図・空気と光・音の種類を確かめる。</summary>
    public class AreaImprovementTests
    {
        static readonly Dictionary<string, WorldGenerator> Worlds = new Dictionary<string, WorldGenerator>();
        static readonly Dictionary<string, List<Vector3>> Drops = new Dictionary<string, List<Vector3>>();
        static AreaLayout _savedArea;

        [OneTimeSetUp]
        public void Generate()
        {
            _savedArea = Areas.Current;
            TestUtil.NewEmptyScene();
            new GameObject("Sun").AddComponent<Light>().type = LightType.Directional;
            foreach (var area in Areas.All)
            {
                SurfaceProbe.ClearCache();
                var go = new GameObject("World_" + area.Id);
                var gen = go.AddComponent<WorldGenerator>();
                gen.assets = TestUtil.LoadWorldAssets();
                gen.instanced = go.AddComponent<InstancedRenderer>();
                Areas.Current = area;
                gen.GenerateNow(area);
                Worlds[area.Id] = gen;
                Drops[area.Id] = new List<Vector3>(gen.DewdropPoints);
                // つぎのエリアを作る前に、当たり判定を消しておく（エリアは同じ場所に重なっている）
                gen.Root.gameObject.SetActive(false);
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            foreach (var g in Worlds.Values) if (g != null) g.Clear();
            Worlds.Clear();
            Areas.Current = _savedArea;
        }

        static IReadOnlyList<Vector3> Spots(string area, string kind) => Worlds[area].ExtraSpots(kind);

        [Test]
        public void Forest_HasShelfStairs_Webs_Puffballs_AndAGlowingTrail()
        {
            Assert.GreaterOrEqual(Spots("forest", "shelf_stump").Count, 4, "切り株のサルノコシカケの階段");
            Assert.GreaterOrEqual(Spots("forest", "shelf_tree").Count, 3, "大樹の幹のサルノコシカケ");
            Assert.GreaterOrEqual(Spots("forest", "web").Count, 4, "クモの巣");
            Assert.GreaterOrEqual(Spots("forest", "puffball").Count, 8, "ホコリタケ " + Worlds["forest"].ExtraSummary());
            Assert.GreaterOrEqual(Spots("forest", "glowtrail").Count, 6, "光るキノコの道しるべ");
            Assert.GreaterOrEqual(Spots("forest", "cairn").Count, 2, "小道の分かれ道の目じるし");
            Assert.GreaterOrEqual(Spots("forest", "pondstone").Count, 2, "水たまりの飛び石");
            Assert.GreaterOrEqual(Spots("forest", "boundary").Count, 20, "外周の手前の石の列");
            Assert.AreEqual(1, Spots("forest", "toyacorn").Count, "苔原のそばの、押せるどんぐり");
            // 切り株の階段は、登る順に高くなる（休みながら頂まで）
            var stairs = Spots("forest", "shelf_stump");
            for (int i = 1; i < stairs.Count; i++) Assert.Greater(stairs[i].y, stairs[i - 1].y, "階段は上へ");
            // 切り株の西がわ（壁登りの場所）には、生やさない
            foreach (var p in stairs)
                Assert.Less(Vector2.Dot(new Vector2(p.x, p.z) - ForestLayout.Stump, new Vector2(-1f, 0.2f).normalized), 3f, "西がわ（壁登りの場所）はあけておく");
        }

        [Test]
        public void River_HasRockStairs_UpperStones_Driftwood_AndAWeir()
        {
            Assert.GreaterOrEqual(Spots("river", "stairs").Count, 3, "滝のわきの岩の階段 " + Worlds["river"].ExtraSummary());
            var up = Spots("river", "upstones");
            Assert.GreaterOrEqual(up.Count, 5, "滝の上のとびいし");
            foreach (var s in up) Assert.Greater(s.y, RiverLayout.UpperLevel, "とびいしの上は水面より上");
            Assert.GreaterOrEqual(Spots("river", "drift").Count, 3, "流木");
            Assert.GreaterOrEqual(Spots("river", "weir").Count, 4, "下流の石の列");
            Assert.GreaterOrEqual(Spots("river", "watergrass").Count, 6, "水草");
            // 岩の階段は、下の岸から台地へ、だんだん高くなる
            var st = Spots("river", "stairs");
            for (int i = 1; i < st.Count; i++) Assert.Greater(st[i].y, st[i - 1].y);
            // 台地の小道は、岩の階段からとびいしまで続く
            Assert.AreEqual(5, RiverLayout.Trails.Count);
            Vector3 top = st[st.Count - 1];
            Assert.Greater(RiverLayout.TrailMask(top.x - 0.5f, RiverLayout.FallZ), 0.3f, "岩の階段のそばを小道が通る");
        }

        [Test]
        public void Park_HasACastle_Blocks_Marbles_Clover_AndPaperPlanes()
        {
            Assert.AreEqual(1, Spots("park", "castle").Count, "砂の城 " + Worlds["park"].ExtraSummary());
            Assert.That(ParkLayout.SandMask(Spots("park", "castle")[0].x, Spots("park", "castle")[0].z), Is.GreaterThan(0.5f), "砂場の中");
            Assert.GreaterOrEqual(Spots("park", "blocks").Count, 5, "積み木");
            Assert.GreaterOrEqual(Spots("park", "benchstep").Count, 1, "ベンチへの積み木の階段");
            Assert.GreaterOrEqual(Spots("park", "marble").Count, 5, "ビー玉");
            Assert.GreaterOrEqual(Spots("park", "clover").Count, 3, "シロツメクサ");
            Assert.GreaterOrEqual(Spots("park", "plane").Count, 2, "紙ひこうき");
            Assert.GreaterOrEqual(Spots("park", "puddlestone").Count, 2, "水たまりの飛び石");
            Assert.IsTrue(Worlds["park"].FlowerPoints.Count > 0);
        }

        [Test]
        public void NewSolids_KeepClearOfDewdrops_AndDewdropsStay()
        {
            foreach (var area in Areas.All)
            {
                var g = Worlds[area.Id];
                Assert.AreEqual(area.DropCount, Drops[area.Id].Count, $"{area.Id}：しずくの数はそのまま");
                foreach (var kind in new[] { "cairn", "rest", "view", "froggy", "drift", "castle", "stack", "can", "plane" })
                    foreach (var p in g.ExtraSpots(kind))
                        foreach (var d in Drops[area.Id])
                            Assert.Greater(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(d.x, d.z)), 2f, $"{area.Id} {kind} がしずくに重なる");
            }
        }

        [Test]
        public void Maps_DrawTrails_AndOtherAreasCanBeDrawn()
        {
            var forest = Worlds["forest"];
            var tex = forest.MapTexture;
            Vector2 t = ForestLayout.Trails[0][1];
            float e = WorldGenerator.MapExtent;
            Color c = tex.GetPixelBilinear((t.x + e) / (2f * e), (t.y + e) / (2f * e));
            Color trail = new Color32(214, 194, 150, 255);
            Assert.Less(Mathf.Abs(c.r - trail.r) + Mathf.Abs(c.g - trail.g) + Mathf.Abs(c.b - trail.b), 0.15f, "地図に小道");
            var other = forest.MapFor(Areas.Park);
            Assert.IsNotNull(other);
            Assert.AreNotSame(tex, other, "ほかのエリアの地図も描ける");
        }

        [Test]
        public void Atmosphere_DiffersByArea_AndApplies()
        {
            var f = AreaAtmosphere.For("forest");
            var r = AreaAtmosphere.For("river");
            var p = AreaAtmosphere.For("park");
            Assert.Greater(f.fog.g, f.fog.b, "森の霧は緑がかる");
            Assert.Greater(r.fog.b, r.fog.r, "川辺の霧は水色");
            Assert.Greater(p.fogEnd, f.fogEnd, "公園は見通しがよい");
            Assert.Greater(r.fogStart, f.fogStart);
            foreach (var look in new[] { f, r, p })
                Assert.Less(look.fog.r + look.fog.g + look.fog.b, 2.6f, "霧は白すぎない（水面が白っぽくならないように）");
            var sun = new GameObject("TestSun").AddComponent<Light>();
            AreaAtmosphere.Apply(Areas.River, sun);
            Assert.AreEqual(r.fog, RenderSettings.fogColor);
            Assert.AreEqual(r.sunIntensity, sun.intensity, 1e-4f);
            AreaAtmosphere.Apply(Areas.Forest, sun);
            Object.DestroyImmediate(sun.gameObject);
        }

        [Test]
        public void Footsteps_KnowSand_Metal_AndMoss()
        {
            Assert.AreEqual(AudioManager.Surface.Metal, AudioManager.Classify("Park_JungleGym", 0));
            Assert.AreEqual(AudioManager.Surface.Metal, AudioManager.Classify("Park_SlideRamp", 0));
            Assert.AreEqual(AudioManager.Surface.Sand, AudioManager.Classify("Park_SandCastle", 0));
            Assert.AreEqual(AudioManager.Surface.Wood, AudioManager.Classify("Park_Block_Cube", 0));
            Assert.AreEqual(AudioManager.Surface.Wood, AudioManager.Classify("DriftLog", 0));
            var saved = Areas.Current;
            Areas.Current = Areas.Park;
            Vector2 sb = ParkLayout.Sandbox;
            Assert.AreEqual(AudioManager.Surface.Sand, AreaProps.Refine(AudioManager.Surface.Ground, ParkLayout.Ground(sb.x, sb.y)));
            Areas.Current = Areas.Forest;
            Vector2 m = ForestLayout.MossHill;
            Assert.Greater(AreaProps.SoftGround(ForestLayout.Ground(m.x, m.y)), 0.5f, "苔の丘はやわらかい");
            Assert.AreEqual(AudioManager.Surface.Moss, AreaProps.Refine(AudioManager.Surface.Ground, ForestLayout.Ground(m.x, m.y)));
            Assert.AreEqual(0f, AreaProps.SoftGround(ForestLayout.Ground(ForestLayout.Stump.x, ForestLayout.Stump.y)));
            Areas.Current = saved;
        }

        [Test]
        public void LoadingTips_FavorTheDestinationArea()
        {
            foreach (var area in new[] { "forest", "river", "park" })
            {
                int fromArea = 0;
                for (int i = 0; i < 40; i++)
                    if (GameUI.AreaTips[area].Contains(GameUI.PickTip(area, i))) fromArea++;
                Assert.Greater(fromArea, 20, $"{area} のヒントを多めに");
                Assert.Less(fromArea, 40, "ふつうのヒントもまじる");
            }
        }

        [Test]
        public void AmbientAudio_ClipsForEveryArea()
        {
            var clips = new[] { "ambience_park", "loop_waterfall", "loop_shallows", "loop_frogs", "loop_cave_drip", "loop_canopy", "woodpecker", "knock_acorn",
                "puff", "creak", "clunk", "plink", "bell", "fish_jump", "ting", "sand_step" };
            foreach (var c in clips)
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{c}.wav"), c);
        }
    }
}

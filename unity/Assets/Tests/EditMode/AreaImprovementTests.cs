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
            Assert.GreaterOrEqual(Spots("forest", "web").Count, 5, "クモの巣");
            Assert.AreEqual(2, Spots("forest", "logweb").Count, "丸太のトンネルの入り口のわきのクモの巣");
            Assert.Greater(Spots("forest", "webdew").Count, 5, "クモの巣のつゆの玉");
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
        public void SpiderWebs_AreStrungBetweenRealSupports()
        {
            // クモの巣は宙にうかない：まん中のまわりに、地面や物があって、そこへ糸がとどいている
            var g = Worlds["forest"];
            g.Root.gameObject.SetActive(true);
            try
            {
                Physics.SyncTransforms();
                foreach (var w in Spots("forest", "web"))
                {
                    Assert.IsFalse(Physics.CheckSphere(w, 0.15f, ShakuConst.SurfaceMask), "巣のまん中が物の中");
                    // まわりの方向のうち、近くに支え（地面・柄・幹・岩）がある方向がいくつもある
                    int hits = 0;
                    for (int i = 0; i < 12; i++)
                        for (int j = -2; j <= 2; j++)
                        {
                            Vector3 d = Quaternion.Euler(j * 35f, i * 30f, 0f) * Vector3.forward;
                            if (Physics.Raycast(w, d, 2.7f, ShakuConst.SurfaceMask)) hits++;
                        }
                    Assert.GreaterOrEqual(hits, 12, $"巣のまわりに支えがある {w}");
                    Assert.IsTrue(Physics.Raycast(w, Vector3.down, out var down, 3f, ShakuConst.SurfaceMask), "地面から高すぎない");
                    Assert.Less(down.distance, 2.4f);
                    Assert.Less(ForestLayout.TrailMask(w.x, w.z), 0.36f, "小道の上には張らない");
                }
            }
            finally { g.Root.gameObject.SetActive(false); }
        }

        /// <summary>そのエリアの当たり判定を有効にして、f を行う（エリアは同じ場所に重なっている）。</summary>
        static void WithArea(string id, System.Action<WorldGenerator> f)
        {
            var g = Worlds[id];
            var saved = Areas.Current;
            // ほかのエリアの落ち葉や小石（作った物の外に持っている体）も、いったん消す
            foreach (var o in Worlds.Values) if (o != g) o.gameObject.SetActive(false);
            g.Root.gameObject.SetActive(true);
            Areas.Current = Areas.All.First(a => a.Id == id);
            try { Physics.SyncTransforms(); f(g); }
            finally
            {
                g.Root.gameObject.SetActive(false);
                foreach (var o in Worlds.Values) o.gameObject.SetActive(true);
                Areas.Current = saved;
            }
        }

        [Test]
        public void Plants_DoNotGrowThroughRocks_Leaves_OrWater()
        {
            // 置き方の仕上げ：草花が岩・柵・落ち葉をつきぬけない。同じ所に 2 つ重ならない。陸の草花は水にしずまない。苔は急な坂にはりつかない
            foreach (var area in Areas.All)
                WithArea(area.Id, g =>
                {
                    Assert.Greater(g.TidyCounts.Values.Sum(), 0, $"{area.Id}：仕上げで直した物がある");
                    var seen = new HashSet<(Mesh, Vector3Int)>();
                    foreach (var (mesh, mat, m) in g.instanced.Instances())
                    {
                        if (mat != g.assets.foliage && mat != g.assets.flowers || mesh.name == "SpiderWeb") continue;
                        if (mesh.name == "WaterLily" || mesh.name == "WaterGrass" || mesh.name == "SasaBune") continue;   // 水にうく物
                        Vector3 pos = m.GetColumn(3);
                        if (new Vector2(pos.x, pos.z).magnitude > area.PlayRadius + 8f) continue;
                        Assert.IsTrue(seen.Add((mesh, Vector3Int.RoundToInt(pos * 3f))), $"{area.Id}：{mesh.name} が同じ所に 2 つ {pos}");
                        float h = Mathf.Max(0.05f, mesh.bounds.max.y * m.lossyScale.x);
                        float probe = Mathf.Min(h, 2.5f) + 0.05f;
                        if (Physics.Raycast(pos + Vector3.up * probe, Vector3.down, out var cover, probe - 0.02f, ShakuConst.SurfaceMask) && !cover.collider.name.StartsWith("Terrain_"))
                            Assert.LessOrEqual(cover.point.y - pos.y, 0.12f, $"{area.Id}：{mesh.name} が {cover.collider.name} をつきぬけている {pos}");
                        if (mesh.name == "Moss" && Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out var below, 3f, ShakuConst.SurfaceMask))
                            Assert.Greater(below.normal.y, 0.55f, $"{area.Id}：苔が急な坂にはりついている {pos}");
                    }
                });
        }

        [Test]
        public void SteppingStones_StandOnTheBed_AndPebblesDoNotOverlap()
        {
            // 水の中の平たい石は、川底までとどく石の上にのっている（水の中で宙にうかない）
            Assert.GreaterOrEqual(Spots("river", "footing").Count, 8, "川のとびいしの下の石");
            Assert.GreaterOrEqual(Spots("forest", "footing").Count, 1, "水たまりの飛び石の下の石");
            foreach (var f in Spots("river", "footing"))
                Assert.Less(f.y, RiverLayout.WaterLevel(f.z), "川底");
            // 押せる小石が、ほかの石や物の中にうまらない
            foreach (var area in Areas.All)
                WithArea(area.Id, g =>
                {
                    foreach (var (mesh, pos, home, rot, scale) in g.loose.Items())
                    {
                        if (mesh.name.StartsWith("Leaf")) continue;
                        float half = Mathf.Max(0.05f, mesh.bounds.extents.magnitude * scale);
                        foreach (var c in Physics.OverlapSphere(home + Vector3.up * half * 0.4f, half * 0.3f, ShakuConst.SurfaceMask))
                            Assert.IsTrue(c.name.StartsWith("Terrain_") || c.attachedRigidbody != null, $"{area.Id}：{mesh.name} が {c.name} にめりこむ {home}");
                    }
                });
        }

        [Test]
        public void Park_BucketBirdsBedAndJungleGym_AreTidy()
        {
            var g = Worlds["park"];
            // バケツは、モグラ塚（土の山の半径 2.6）にかさならない
            foreach (var b in Spots("park", "bucket"))
                foreach (var m in g.Mobs.Where(m => m.species == "mogura" || m.species == "okera"))
                    Assert.Greater(Vector2.Distance(new Vector2(b.x, b.z), new Vector2(m.center.x, m.center.z)), 4.5f, "バケツとモグラ塚");
            // ハトとスズメは、はなれた所から（降りる場所も重ねない）
            var hato = g.Mobs.First(m => m.species == "hato");
            var sparrow = g.Mobs.First(m => m.species == "sparrow");
            Assert.Greater(Vector3.Distance(hato.center, sparrow.center), 12f, "ハトとスズメの初めの場所");
            foreach (var a in hato.path)
                foreach (var b in sparrow.path)
                    Assert.Greater(Vector3.Distance(a, b), 10f, "ハトとスズメの降りる場所");
            // 花だん：キャベツ（緑のかたまり）はない。チューリップは土の上に植わる
            int tulips = 0;
            foreach (var (mesh, mat, m) in g.instanced.Instances())
            {
                Assert.AreNotEqual("Park_Cabbage", mesh.name, "花だんのキャベツ");
                if (!mesh.name.StartsWith("Tulip_")) continue;
                Vector3 p = m.GetColumn(3);
                if (Mathf.Abs(p.x - ParkLayout.FlowerBed.x) > ParkLayout.BedSize.x * 0.5f || Mathf.Abs(p.z - ParkLayout.FlowerBed.y) > ParkLayout.BedSize.y * 0.5f) continue;
                tulips++;
                Assert.AreEqual(ParkLayout.BedSoilY, p.y, 0.15f, "チューリップは土の上");
            }
            Assert.GreaterOrEqual(tulips, 20, "花だんのチューリップ（3 列 × 9。景色の通り道の所はのぞく）");
            WithArea("park", w =>
            {
                // 花だんの土は、れんがのふちとほぼ同じ高さ（ふちから土へ段がない）
                Vector2 c = ParkLayout.FlowerBed;
                Assert.IsTrue(Physics.Raycast(new Vector3(c.x + 1f, 30f, c.y), Vector3.down, out var soil, 60f, ShakuConst.SurfaceMask));
                Assert.AreEqual(ParkLayout.BedSoilY, soil.point.y, 0.05f, "土の上の面");
                Assert.IsTrue(Physics.Raycast(new Vector3(c.x + 1f, 30f, c.y - ParkLayout.BedSize.y * 0.5f + 0.3f), Vector3.down, out var rim, 60f, ShakuConst.SurfaceMask));
                Assert.Less(rim.point.y - soil.point.y, 0.06f, "れんがのふちと土の段");
                // ジャングルジムの棒に、石や小石がめりこまない
                foreach (var col in w.Root.GetComponentsInChildren<Collider>())
                {
                    if (col.name.StartsWith("Terrain_") || col.name == "Park_JungleGym") continue;
                    Vector3 p = col.bounds.center;
                    Assert.IsFalse(ParkLayout.InJungleGym(new Vector2(p.x, p.z), 0.6f) && p.y < ParkLayout.Ground(p.x, p.z).y + 1.5f, $"ジャングルジムの中の {col.name} {p}");
                }
                foreach (var (mesh, pos, home, rot, scale) in w.loose.Items())
                    if (!mesh.name.StartsWith("Leaf"))
                        Assert.IsFalse(ParkLayout.InJungleGym(new Vector2(home.x, home.z), 0.4f), $"ジャングルジムの中の小石 {home}");
            });
        }

        [Test]
        public void Park_FenceStopsAtTheKunugi_AndPropsKeepOffTheFixtures()
        {
            WithArea("park", g =>
            {
                // さくがクヌギの幹をつきぬけない（幹の手前で止めて、幹へつなぐ）
                Vector2 k = ParkLayout.Kunugi;
                int fences = 0;
                foreach (Transform t in g.Root.GetComponentsInChildren<Transform>())
                {
                    if (t.name != "Park_Fence") continue;
                    fences++;
                    // 幹にかかるさくは、幹の中で止まる（幹をつきぬけて、反対がわへ出ない）
                    Vector3 c = t.position, half = t.right * 4.1f;
                    Vector2 a2 = new Vector2(c.x - half.x, c.z - half.z), b2 = new Vector2(c.x + half.x, c.z + half.z);
                    Vector2 ab = b2 - a2;
                    float tt = Mathf.Clamp01(Vector2.Dot(k - a2, ab) / ab.sqrMagnitude);
                    if (Vector2.Distance(k, a2 + ab * tt) < 3.3f)
                        Assert.Less(Mathf.Min(Vector2.Distance(a2, k), Vector2.Distance(b2, k)), 3.3f, $"さくがクヌギの幹をつきぬけている {c}");
                }
                Assert.Greater(fences, 40);
                // 水飲み場の台の中に、石がうまらない
                foreach (var t in g.Root.GetComponentsInChildren<Collider>())
                    if (t.name.StartsWith("Rock_"))
                        Assert.Greater(Vector2.Distance(new Vector2(t.transform.position.x, t.transform.position.z), ParkLayout.Fountain), 1.8f, "水飲み場の台の中の石");
                // 土管の口に、段の石がかからない
                foreach (var s in Spots("park", "dokanstep"))
                    Assert.Greater(Mathf.Abs(s.x - ParkLayout.Dokan.x), 6.2f, "土管の口から少しはなす");
                // 小道のまん中に、石を積まない
                foreach (var c in Spots("park", "cairn"))
                    Assert.Less(ParkLayout.TrailMask(c.x, c.z), 0.3f, "小道の上の石積み");
            });
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
        public void Park_HasACastle_Bucket_Steps_Marbles_Clover_AndPaperPlanes()
        {
            Assert.AreEqual(1, Spots("park", "castle").Count, "砂の城 " + Worlds["park"].ExtraSummary());
            Assert.That(ParkLayout.SandMask(Spots("park", "castle")[0].x, Spots("park", "castle")[0].z), Is.GreaterThan(0.5f), "砂場の中");
            Assert.AreEqual(1, Spots("park", "bucket").Count, "砂場のバケツ");
            Assert.AreEqual(0, Spots("park", "blocks").Count, "四角い積み木は置かない");
            foreach (var k in new[] { "benchrest", "seesawstep", "tirestep", "tirerest" })
                Assert.GreaterOrEqual(Spots("park", k).Count, 1, k + "：遊具のそばの平らな石");
            Assert.GreaterOrEqual(Spots("park", "marble").Count, 5, "ビー玉");
            Assert.GreaterOrEqual(Spots("park", "clover").Count, 3, "シロツメクサ");
            Assert.GreaterOrEqual(Spots("park", "plane").Count, 2, "紙ひこうき");
            Assert.GreaterOrEqual(Spots("park", "puddlestone").Count, 2, "水たまりの飛び石");
            // 水飲み場のまん中の飲み口の頭に、しずく（前と同じ場所）
            Vector2 f = ParkLayout.Fountain;
            Vector3 cap = new Vector3(f.x, ParkLayout.Ground(f.x, f.y).y + ParkLayout.BubblerTop, f.y);
            Assert.IsTrue(Drops["park"].Any(d => Vector3.Distance(d, cap) < 0.01f), "飲み口の頭のしずく");
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

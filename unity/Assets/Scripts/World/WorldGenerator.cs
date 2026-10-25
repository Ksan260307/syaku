using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace Shakutori
{
    /// <summary>いきもの（モブ）の群れ。生成時に場所が決まり、Creatures が動かす。</summary>
    public class MobGroup
    {
        public string species;
        public int count = 1;
        public Vector3 center;
        public float radius = 3f;
        public float height;                                // 飛ぶものの高さ（地面から）
        public List<Vector3> path = new List<Vector3>();    // 行列の道（ループ）や、鳥の降りる場所
        /// <summary>名所の見どころとして、わざとそこに置いた（キノコの根もと・丸太の中・松の幹・中州）。景色の通り道の決まりの外。</summary>
        public bool showcase;
        /// <summary>かべにいるいきもの（ヤモリ）：かべの面の向き。ふつうは 0（地面の上）。</summary>
        public Vector3 up;
        /// <summary>向きを決めて置くいきもの（手ぶくろの口のヤマネ）：前の向き。ふつうは 0（ばらばらの向き）。</summary>
        public Vector3 face;
    }

    /// <summary>生成されたエリア間トンネル。</summary>
    public class GateInstance
    {
        public GateDef def;
        public Vector3 position;
        public Vector3 inward;
    }

    /// <summary>
    /// エリア（森・川辺）をシード値から決定的に生成する。
    /// 共通部分（地形・外周・配置ユーティリティ・光の筋・ミニマップ・トンネル）はこのファイル、
    /// エリアごとの中身は WorldGenerator.Forest.cs / WorldGenerator.River.cs。
    /// </summary>
    public partial class WorldGenerator : MonoBehaviour
    {
        public WorldAssets assets;
        public InstancedRenderer instanced;
        /// <summary>押すと動く小さな物（落ち葉・小石・松ぼっくり・どんぐりのぼうし）。</summary>
        public LooseProps loose;
        public Light sun;
        public int seed = 20261006;
        [Range(0.2f, 1f)] public float foliageDensity = 1f;

        public static WorldGenerator Instance { get; private set; }
        public bool IsGenerated { get; private set; }
        public Transform Root { get; private set; }
        public Texture2D MapTexture { get; private set; }
        public AreaLayout Area { get; private set; } = Areas.Forest;
        /// <summary>光るキノコのある所（しゃくとりむしが、その光にてらされる）。</summary>
        public readonly List<Vector3> GlowSpots = new List<Vector3>();
        public readonly List<Vector3> DewdropPoints = new List<Vector3>();
        /// <summary>花の頭の位置（チョウやトンボがとまる）。</summary>
        public readonly List<Vector3> FlowerPoints = new List<Vector3>();
        static readonly HashSet<string> FlowerMeshes = new HashSet<string> { "Daisy", "Bellflower", "Dandelion", "DandelionPuff", "Strawberry", "Iris",
            "Tulip_Red", "Tulip_Yellow", "Tulip_Pink", "Park_WhiteClover", "Mtn_Komakusa", "Mtn_Chinguruma", "Mtn_Kurumayuri", "Coast_Hirugao" };
        public readonly List<MobGroup> Mobs = new List<MobGroup>();
        public readonly List<GateInstance> Gates = new List<GateInstance>();
        public Vector3 SpawnPoint { get; private set; }
        public Vector3 SpawnForward { get; private set; }

        public const float MapExtent = 80f;   // ミニマップがカバーする半径（XZ）

        Random _rng;
        Transform _solidRoot;
        Transform _colliderRoot;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly List<Vector3> _occupied = new List<Vector3>(); // (x, z, r)
        readonly List<(Vector3 pos, float scale)> _bigRocks = new List<(Vector3, float)>();
        readonly List<(Vector3 pos, float scale)> _redCaps = new List<(Vector3, float)>();
        readonly List<Vector3> _lilyPads = new List<Vector3>();
        readonly List<Vector3> _bigLeaves = new List<Vector3>();
        readonly List<Vector3> _twigs = new List<Vector3>();
        Vector3 _specialCap;

        static readonly string[] Rocks = { "Rock_A", "Rock_B", "Rock_C", "Rock_D" };
        static readonly string[] BigLeaves = { "Leaf_Oak_Orange", "Leaf_Oak_Brown", "Leaf_Oak_Green", "Leaf_Maple_Red", "Leaf_Maple_Yellow" };
        static readonly string[] Grass = { "Grass_A", "Grass_B", "Grass_C" };
        static readonly string[] RiverStones = { "RiverStone_A", "RiverStone_B", "RiverStone_C" };

        void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            Clear();
        }

        T Own<T>(T obj) where T : UnityEngine.Object
        {
            _owned.Add(obj);
            return obj;
        }

        // ------------------------------------------------------------------
        // 生成エントリ
        // ------------------------------------------------------------------
        public void GenerateNow() => GenerateNow(Areas.Current);

        public void GenerateNow(AreaLayout area)
        {
            var e = Generate(area, null);
            while (e.MoveNext()) { }
        }

        public IEnumerator Generate(Action<float> progress) => Generate(Areas.Current, progress);

        public IEnumerator Generate(AreaLayout area, Action<float> progress)
        {
            Instance = this;
            if (loose == null)
            {
                loose = GetComponent<LooseProps>();
                if (loose == null) loose = gameObject.AddComponent<LooseProps>();
            }
            Clear();
            Area = area;
            bool forest = area.Id == "forest";
            bool park = area.Id == "park";
            bool mountain = area.Id == "mountain";
            bool hut = area.Id == "hut";
            bool coast = area.Id == "coast";
            bool shrine = area.Id == "shrine";
            bool night = area.Id == "night";
            bool xmas = area.Id == "xmas";
            bool tutorial = area.Id == "tutorial";
            _rng = new Random(forest ? seed : seed + 7919 * area.Id.Length);
            AreaLayout.LateGround = false;   // しずくを置くまでは、もとの地面の形で作る
            SurfaceProbe.ClearCache();
            var rootGo = new GameObject("World (generated)");
            if (!Application.isPlaying) rootGo.hideFlags = HideFlags.DontSave;
            Root = rootGo.transform;
            _solidRoot = new GameObject("Solids").transform;
            _solidRoot.SetParent(Root, false);
            _colliderRoot = new GameObject("PropColliders").transform;
            _colliderRoot.SetParent(Root, false);

            progress?.Invoke(0.05f);
            yield return null;
            LoadClock.Lap("start");
            BuildTerrain();
            LoadClock.Lap("terrain");
            BuildViewLanes();
            progress?.Invoke(0.25f);
            yield return null;
            if (!hut) BuildOuterRing();   // 小屋の中は、かべの外が見えない
            if (forest) BuildForestSolids(); else if (park) BuildParkSolids(); else if (mountain) BuildMountainSolids(); else if (hut) BuildHutSolids(); else if (coast) BuildCoastSolids(); else if (shrine) BuildShrineSolids(); else if (night) BuildNightSolids(); else if (xmas) BuildXmasSolids(); else if (tutorial) BuildTutorialSolids(); else BuildRiverSolids();
            BuildGates();
            LoadClock.Lap("solids");
            progress?.Invoke(0.4f);
            yield return null;
            if (forest)
            {
                PlaceForestMobProps();
                ScatterForestProps();
                BuildForestWater();
            }
            else if (park)
            {
                ScatterParkProps();
                BuildParkWater();
            }
            else if (mountain)
            {
                ScatterMountainProps();
                BuildMountainWater();
            }
            else if (hut) ScatterHutProps();   // 小屋の中には、水はない
            else if (coast)
            {
                ScatterCoastProps();
                BuildCoastWater();
            }
            else if (shrine)
            {
                ScatterShrineProps();
                BuildShrineWater();
            }
            else if (night)
            {
                ScatterNightProps();
                BuildNightWater();
            }
            else if (xmas) ScatterXmasProps();   // クリスマス村の池は、こおっている（水はない）
            else if (tutorial) ScatterTutorialProps();   // 練習の森には、水はない
            else
            {
                ScatterRiverProps();
                BuildRiverWater();
            }
            Physics.SyncTransforms();
            LoadClock.Lap("props");
            progress?.Invoke(0.6f);
            yield return null;
            if (forest) BuildForestFoliage(); else if (park) BuildParkFoliage(); else if (mountain) BuildMountainFoliage(); else if (coast) BuildCoastFoliage(); else if (shrine) BuildShrineFoliage(); else if (night) BuildNightFoliage(); else if (xmas) BuildXmasFoliage(); else if (tutorial) BuildTutorialFoliage(); else if (!hut) BuildRiverFoliage();
            LoadClock.Lap("foliage");
            progress?.Invoke(0.8f);
            yield return null;
            if (forest) PlaceForestDewdrops();
            else if (park) PlaceParkDewdrops();
            else if (mountain) PlaceMountainDewdrops();
            else if (hut) PlaceHutDewdrops();
            else if (coast) PlaceCoastDewdrops();
            else if (shrine) PlaceShrineDewdrops();
            else if (night) PlaceNightDewdrops();
            else if (xmas) PlaceXmasDewdrops();
            else if (tutorial) PlaceTutorialDewdrops();
            else PlaceRiverDewdrops();
            // しずくを置いたあとに、泉や水たまりの土手を盛る（しずくの場所は変えない）
            LoadClock.Lap("dews");
            AreaLayout.LateGround = true;
            ApplyLateGround();
            LoadClock.Lap("lateGround");
            if (forest)
            {
                PlaceForestCreatures();
                FixCreaturesForPlay();      // いきものの居場所の直し（しずくを置いたあと。乱数は使わない）
                BuildLightShafts(ForestShaftSpots);
            }
            else if (park)
            {
                PlaceParkCreatures();
                FixCreaturesForPlay();
                BuildLightShafts(ParkShaftSpots());
            }
            else if (mountain)
            {
                FixMountainForPlay();       // 遊びやすさの直し（しずくを置いたあと。しずくの場所は変えない）
                PlaceMountainCreatures();   // 山はひらけた空なので、木もれ日の光の筋はない
                FixCreaturesForPlay();
            }
            else if (hut)
            {
                PlaceHutCreatures();        // 窓からの日ざしは、部屋を作るときに
                FixCreaturesForPlay();
            }
            else if (coast)
            {
                PlaceCoastCreatures();      // 海辺はひらけた空なので、木もれ日の光の筋はない
                FixCreaturesForPlay();
            }
            else if (shrine)
            {
                PlaceShrineCreatures();
                FixCreaturesForPlay();
                BuildLightShafts(ShrineShaftSpots);   // 鎮守の森の、木もれ日の光の筋
            }
            else if (night)
            {
                PlaceNightCreatures();      // 夜なので、木もれ日の光の筋はない
                FixCreaturesForPlay();
            }
            else if (xmas)
            {
                PlaceXmasCreatures();       // 雪の夜なので、木もれ日の光の筋はない
                FixCreaturesForPlay();
            }
            else if (tutorial)
            {
                PlaceTutorialCreatures();
                FixCreaturesForPlay();
                BuildLightShafts(TutorialShaftSpots);   // 森と同じ、木もれ日の光の筋
            }
            else
            {
                PlaceRiverCreatures();
                FixCreaturesForPlay();
                BuildLightShafts(RiverShaftSpots());
            }
            LoadClock.Lap("creatures");
            BuildLateGates();   // あとから足したトンネル（しずくの場所は変えない）
            BuildExtras();   // エリアの改善：小物・道・遠景（しずくの場所は変えない）
            LoadClock.Lap("extras");
            SettleFloatingRocks();   // 坂で宙にういた岩・流木を、地面になじませる
            FillUnderDewRocks();     // しずくをのせた岩などの下にのこるすき間は、石でうめる
            PinLeavesUnderDew();
            LoadClock.Lap("settle");
            TidyPlacements();   // 置き方の仕上げ：つきぬけ・重なり・うき（草花と苔だけ）
            LoadClock.Lap("tidy");
            ClearTravelLanes();   // 名所へ移動したときに見る景色の前の、背の高い草をどける（しずくを置いたあと）
            FixCreaturesForPlay(again: true);   // あとから置いた小物の中に、いきものが入っていないか、もう一度
            LoadClock.Lap("lanes");
            BuildMap();
            LoadClock.Lap("map");
            instanced.Build();
            LoadClock.Lap("instances");
            ComputeSpawn();
            IsGenerated = true;
            progress?.Invoke(1f);
        }

        public void Clear()
        {
            TidyCounts.Clear();
            TidyLog.Clear();
            IsGenerated = false;
            if (Root != null)
            {
                // Destroy はフレームの終わりなので、先に止めて当たり判定を消しておく
                Root.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(Root.gameObject);
                else DestroyImmediate(Root.gameObject);
            }
            var stale = GameObject.Find("World (generated)");
            if (stale != null)
            {
                if (Application.isPlaying) Destroy(stale);
                else DestroyImmediate(stale);
            }
            foreach (var o in _owned)
            {
                if (o == null) continue;
                if (Application.isPlaying) Destroy(o);
                else DestroyImmediate(o);
            }
            _owned.Clear();
            Root = null;
            MapTexture = null;
            _gridH = null;
            _gridC = null;
            _gridCSet = null;
            _occupied.Clear();
            _terrainChunks.Clear();
            _insideOut.Clear();
            _bigRocks.Clear();
            _redCaps.Clear();
            _lilyPads.Clear();
            _bigLeaves.Clear();
            _twigs.Clear();
            _extraSpots.Clear();
            _riverStones.Clear();
            _stepStones.Clear();
            _poolPads.Clear();
            _fallRocks.Clear();
            Ferry = null;
            ClearPark();
            ClearMountain();
            ClearHut();
            ClearCoast();
            ClearShrine();
            ClearNight();
            ClearXmas();
            _specialCap = Vector3.zero;
            DewdropPoints.Clear();
            FlowerPoints.Clear();
            GlowSpots.Clear();
            _viewLanes.Clear();
            Mobs.Clear();
            Gates.Clear();
            if (instanced != null) instanced.Clear();
            if (loose != null) loose.Clear();
        }

        float R01() => (float)_rng.NextDouble();
        float R(float a, float b) => a + (b - a) * (float)_rng.NextDouble();
        int RI(int a, int bExclusive) => _rng.Next(a, bExclusive);
        T Pick<T>(IList<T> list) => list[_rng.Next(list.Count)];

        // ------------------------------------------------------------------
        // 地形
        // ------------------------------------------------------------------
        void BuildTerrain()
        {
            const float spacing = 0.75f;
            float half = Area.TerrainHalf;
            int cells = Mathf.RoundToInt(half * 2f / spacing); // 256
            int verts = cells + 1;
            float origin = -half;
            var heights = new float[verts, verts];
            for (int j = 0; j < verts; j++)
            for (int i = 0; i < verts; i++)
                heights[i, j] = Area.Height(origin + i * spacing, origin + j * spacing);
            // 地図の絵を描くときにも使う（はかりなおさない）
            _gridH = heights;
            _gridC = new Color[verts * verts];
            _gridCSet = new bool[verts * verts];
            _gridOrigin = origin;
            _gridSpacing = spacing;
            _gridVerts = verts;

            const int chunkCells = 32;
            int chunks = cells / chunkCells;
            var terrainRoot = new GameObject("Terrain").transform;
            terrainRoot.SetParent(Root, false);
            for (int cz = 0; cz < chunks; cz++)
            for (int cx = 0; cx < chunks; cx++)
            {
                float minX = origin + cx * chunkCells * spacing;
                float minZ = origin + cz * chunkCells * spacing;
                float maxX = minX + chunkCells * spacing;
                float maxZ = minZ + chunkCells * spacing;
                // 外周の外側すぎるチャンクは作らない（外周リングが受け持つ）
                float nearest = new Vector2(Mathf.Clamp(0, minX, maxX), Mathf.Clamp(0, minZ, maxZ)).magnitude;
                if (nearest > 100f) continue;

                int n = chunkCells + 1;
                var pos = new Vector3[n * n];
                var nrm = new Vector3[n * n];
                var col = new Color[n * n];
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int gi = cx * chunkCells + i;
                    int gj = cz * chunkCells + j;
                    float x = origin + gi * spacing;
                    float z = origin + gj * spacing;
                    float h = heights[gi, gj];
                    float hl = heights[Mathf.Max(gi - 1, 0), gj];
                    float hr = heights[Mathf.Min(gi + 1, verts - 1), gj];
                    float hd = heights[gi, Mathf.Max(gj - 1, 0)];
                    float hu = heights[gi, Mathf.Min(gj + 1, verts - 1)];
                    Vector3 normal = new Vector3(hl - hr, 2f * spacing, hd - hu).normalized;
                    int k = j * n + i;
                    pos[k] = new Vector3(x, h, z);
                    nrm[k] = normal;
                    col[k] = Area.GroundColor(x, z, h, normal);
                    _gridC[gj * verts + gi] = col[k];
                    _gridCSet[gj * verts + gi] = true;
                }
                var tris = new int[chunkCells * chunkCells * 6];
                int t = 0;
                for (int j = 0; j < chunkCells; j++)
                for (int i = 0; i < chunkCells; i++)
                {
                    int a = j * n + i;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    // 対角線の向きを交互にしてなめらかに
                    if (((i + j) & 1) == 0)
                    {
                        tris[t++] = a; tris[t++] = c; tris[t++] = d;
                        tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    }
                    else
                    {
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
                }
                var mesh = Own(new Mesh { name = $"Terrain_{cx}_{cz}" });
                mesh.SetVertices(pos);
                mesh.SetNormals(nrm);
                mesh.SetColors(col);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();

                var go = new GameObject(mesh.name);
                go.layer = ShakuConst.SurfaceLayer;
                go.transform.SetParent(terrainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.terrain;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                MeshCollider chunkCol = null;
                if (nearest < 80f)
                {
                    chunkCol = go.AddComponent<MeshCollider>();
                    chunkCol.sharedMesh = mesh;
                }
                _terrainChunks.Add(new TerrainChunk { mesh = mesh, collider = chunkCol, min = new Vector2(minX, minZ), max = new Vector2(maxX, maxZ), spacing = spacing });
            }
        }

        void BuildOuterRing()
        {
            float[] radii = { 92f, 100f, 112f, 128f, 150f, 185f, 240f, 320f };
            int seg = 128;
            var pos = new List<Vector3>();
            var col = new List<Color>();
            var tris = new List<int>();
            bool coast = Area.Id == "coast";
            bool snow = Area.Id == "xmas";
            ColorUtility.TryParseHtmlString(Area.Id == "mountain" ? "#5d7550" : coast ? "#8aa35c" : snow ? "#a9b9cf" : "#3f6a35", out var deep);
            ColorUtility.TryParseHtmlString(Area.Id == "mountain" ? "#7c7a78" : coast ? "#d6c08f" : snow ? "#c9d4e3" : "#5c4a38", out var soil);   // 山は、岩の灰色。海辺は、砂と草。クリスマス村は、雪
            for (int r = 0; r < radii.Length; r++)
            {
                for (int s = 0; s < seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    float x = Mathf.Cos(a) * radii[r];
                    float z = Mathf.Sin(a) * radii[r];
                    float h = Area.Height(x, z) - 0.35f;
                    if (radii[r] > 96f) h = coast ? CoastLayout.OuterHeight(x, z)
                                          : Area.Height(x / radii[r] * 96f, z / radii[r] * 96f) + (radii[r] - 96f) * 0.12f
                                            + (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) - 0.5f) * 6f;
                    pos.Add(new Vector3(x, h, z));
                    float k = Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f);
                    Color c = Color.Lerp(deep, soil, k * 0.6f) * Mathf.Lerp(0.85f, 0.6f, r / (float)radii.Length);
                    if (coast && CoastLayout.Inland(x, z) < 6f) c = Color.Lerp(soil, soil * 0.8f, k);   // 海の底と、浜の砂
                    col.Add(c);
                }
            }
            for (int r = 0; r < radii.Length - 1; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = r * seg + s;
                int b = r * seg + (s + 1) % seg;
                int c = (r + 1) * seg + s;
                int d = (r + 1) * seg + (s + 1) % seg;
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);
            }
            var mesh = Own(new Mesh { name = "OuterRing" });
            mesh.SetVertices(pos);
            mesh.SetColors(col);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("OuterRing");
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.terrain;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>背景の幹の形（3 種類から、番号で選ぶ。乱数は使わない：しずくの場所を変えない）。</summary>
        static string BgTrunkFor(int i) => i % 3 == 0 ? "BgTrunk_A" : i % 3 == 1 ? "BgTrunk_B" : "BgTrunk_C";

        /// <summary>背景の幹の形（0〜1 の数で選ぶ。もとの乱数 1 つだけ）。</summary>
        static string BgTrunkPick(float u) => u < 0.36f ? "BgTrunk_A" : u < 0.72f ? "BgTrunk_B" : "BgTrunk_C";

        /// <summary>背景の幹の、少しのかたむき（まっすぐな柱が、ならんで見えない。乱数は使わない）。</summary>
        static Quaternion BgTilt(int i) => Quaternion.Euler(((i * 37) % 7 - 3) * 1.1f, 0f, ((i * 53) % 5 - 2) * 1.2f);

        // 背景の大きな幹（森の奥行き）
        void BuildBackgroundTrunks(Func<Vector2, bool> skip)
        {
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f + R(-0.12f, 0.12f);
                float d = R(100f, 150f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (skip(p)) continue;
                Vector3 g = Area.Ground(p.x, p.y) + Vector3.down * 2f;
                Place(BgTrunkFor(i), assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f) * BgTilt(i), R(0.9f, 1.6f), false, true, 500f, asRenderer: true);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = (i + 0.5f) / 6f * Mathf.PI * 2f + 0.4f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(80f, 88f);
                if (skip(p)) continue;
                Vector3 g = Area.Ground(p.x, p.y) + Vector3.down * 1.5f;
                Place("BgTrunk_B", assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.55f, 0.75f), false, true, 500f, asRenderer: true);
            }
        }

        // ------------------------------------------------------------------
        // 配置ユーティリティ
        // ------------------------------------------------------------------
        bool IsFree(Vector2 p, float r)
        {
            foreach (var o in _occupied)
            {
                float dx = p.x - o.x, dz = p.y - o.y;
                float rr = r + o.z;
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            return true;
        }

        void Occupy(Vector2 p, float r) => _occupied.Add(new Vector3(p.x, p.y, r));

        bool InsideOccupied(Vector2 p)
        {
            foreach (var o in _occupied)
            {
                float dx = p.x - o.x, dz = p.y - o.y;
                if (dx * dx + dz * dz < o.z * o.z * 0.75f) return true;
            }
            return false;
        }

        Vector2 RandomInCircle(Vector2 c, float r)
        {
            float a = R(0f, Mathf.PI * 2f);
            float d = Mathf.Sqrt(R01()) * r;
            return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
        }

        Vector2 RandomInRing(float r0, float r1)
        {
            float a = R(0f, Mathf.PI * 2f);
            float d = Mathf.Sqrt(Mathf.Lerp(r0 * r0, r1 * r1, R01()));
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
        }

        bool IsLand(Vector2 p, float margin = 0.25f) => Area.IsLand(p.x, p.y, margin);

        Quaternion GroundRotation(Vector2 p, float yawDeg, float align, float maxTilt)
        {
            Vector3 n = Area.Normal(p.x, p.y);
            Vector3 up = Vector3.Slerp(Vector3.up, n, align);
            if (maxTilt > 0f)
                up = Quaternion.Euler(R(-maxTilt, maxTilt), 0f, R(-maxTilt, maxTilt)) * up;
            return Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yawDeg, 0f);
        }

        /// <summary>押すと動く小さな物を置く（ふだんは絵だけ。しゃくとりむしが近づくと体を持つ）。</summary>
        /// <summary>しずくがのっている大きな葉は、風ですべらないようにする（しずくが宙に浮かない）。</summary>
        void PinLeavesUnderDew()
        {
            if (loose == null) return;
            foreach (var d in DewdropPoints)
                foreach (var h in Physics.RaycastAll(d + Vector3.up * 0.4f, Vector3.down, 1.2f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    if (h.collider.TryGetComponent<BigLeaf>(out var leaf)) leaf.Pinned = true;
        }

        void PlaceLoose(string meshName, Material mat, Vector3 pos, Quaternion rot, float scale, LooseProps.Shape shape, bool shadows, float maxDistance)
        {
            Mesh m = assets.Get(meshName);
            if (m == null || mat == null || loose == null) return;
            // 落ち葉のほかは、いちばん下が地面にふれるように置く（うまっていると、体を持ったときにはじき出されて転がる）
            if (shape != LooseProps.Shape.Leaf && shape != LooseProps.Shape.BigLeaf) pos = LooseProps.RestOnGround(m, pos, rot, scale, Area);
            // ほかの石や物の中に、めりこんで置かない（同じ所に 2 つ重なった石など）
            if (shape != LooseProps.Shape.Leaf && shape != LooseProps.Shape.BigLeaf)
            {
                float half = Mathf.Max(0.05f, m.bounds.extents.magnitude * scale);
                Physics.SyncTransforms();
                foreach (var c in Physics.OverlapSphere(pos + Vector3.up * half * 0.4f, half * 0.45f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    if (!c.name.StartsWith("Terrain_") && c.attachedRigidbody == null)
                    {
                        Count("looseOverlap", meshName + "|" + c.name, pos);
                        return;
                    }
            }
            // しゃくとりむしには重すぎる小石は、動かない石として置く（よじのぼって越える）
            if (shape == LooseProps.Shape.Pebble && LooseProps.MassOf(m, shape, scale) > LooseBody.WormPushLimit)
            {
                Place(meshName, mat, pos, rot, scale, true, shadows, maxDistance);
                return;
            }
            loose.area = Area;
            loose.Add(m, mat, pos, rot, scale, shape, shadows, maxDistance);
        }

        /// <summary>メッシュを配置する。インスタンシング描画 + 必要ならコライダー専用オブジェクト。</summary>
        GameObject Place(string meshName, Material mat, Vector3 pos, Quaternion rot, float scale, bool collider,
            bool castShadows = true, float maxDistance = 150f, bool asRenderer = false)
        {
            Mesh m = assets.Get(meshName);
            if (m == null || mat == null) return null;
            if (meshName == "Mushroom_Glow") GlowSpots.Add(pos + rot * (m.bounds.center * scale));   // しゃくとりむしをてらす光るキノコ
            if (!collider && !asRenderer && IsTallFoliage(meshName) && InViewLane(new Vector2(pos.x, pos.z), m.bounds.extents.magnitude * scale * 0.5f)) return null;
            var mtx = Matrix4x4.TRS(pos, rot, Vector3.one * scale);
            if (FlowerMeshes.Contains(meshName))
            {
                // 花の頭（いちばん上）の位置を覚えておく
                Bounds b = m.bounds;
                FlowerPoints.Add(pos + rot * (new Vector3(b.center.x, b.max.y * 0.95f, b.center.z) * scale));
            }
            GameObject go = null;
            if (asRenderer)
            {
                go = new GameObject(meshName);
                go.transform.SetParent(_solidRoot, false);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            else
            {
                instanced.Add(m, mat, mtx, castShadows, maxDistance);
            }
            if (collider && loose != null && loose.Count > 0)
            {
                // あとから置いた動かない物の下に、うまってしまう小石や木の実は、まわりへよける
                float r = Mathf.Max(m.bounds.extents.x, m.bounds.extents.z) * scale;
                if (r < 6f)
                    for (int k = loose.PushOut(pos + rot * (m.bounds.center * scale), r, m.bounds.extents.magnitude * scale); k > 0; k--) Count("pushedOut", "Pebble|" + meshName, pos);
            }
            if (collider)
            {
                if (go == null)
                {
                    go = new GameObject(meshName);
                    go.transform.SetParent(_colliderRoot, false);
                    go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = Vector3.one * scale;
                }
                go.layer = ShakuConst.SurfaceLayer;
                // 登りやすいように作った当たり判定用のメッシュがあれば、そちらを使う。
                // ほかの物は、凹凸にはさまらないよう、いちばん外側をなめらかにつつむ大まかな形にする（穴が大事な物は、そのまま）
                Mesh col = assets.TryGet(meshName + "_Col");
                if (col == null && CoarseCollider.Wants(meshName)) col = CoarseCollider.For(meshName, m);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = col != null ? col : DetailMeshes.ForCollision(m);
                if (!AreaLayout.LateGround && WasInsideOut.Contains(meshName))
                {
                    // しずくを置くまでは、前の（面がうら返った）当たり判定で（しずくの場所を変えない）
                    _insideOut.Add((mc, mc.sharedMesh));
                    mc.sharedMesh = InsideOut(mc.sharedMesh);
                }
                else if (!AreaLayout.LateGround && col != null && ColAfterDews.Contains(meshName))
                {
                    // しずくを置くまでは、前の（見た目のとおりの）当たり判定で（しずくの場所を変えない）
                    _insideOut.Add((mc, col));
                    mc.sharedMesh = DetailMeshes.ForCollision(m);
                }
            }
            return go;
        }

        // ------------------------------------------------------------------
        // エリア間のトンネル
        // ------------------------------------------------------------------
        static Mesh _quadMesh;

        static Mesh QuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            _quadMesh = new Mesh { name = "PortalQuad" };
            _quadMesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            _quadMesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _quadMesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            _quadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quadMesh.RecalculateBounds();
            return _quadMesh;
        }

        void BuildGates()
        {
            foreach (var g in Area.Gates)
                if (!g.late) BuildGate(g);
        }

        /// <summary>
        /// あとから足したトンネル（しずく・いきものを置いたあとに作る。しずくの場所と数は変わらない）。
        /// トンネルのまわりと、出てきたときに見る景色の通り道の、草花と小石をどけておく。
        /// </summary>
        void BuildLateGates()
        {
            foreach (var g in Area.Gates)
            {
                if (!g.late) continue;
                BuildGate(g);
                if (g.door)
                {
                    // とびらは、小屋のかべにある：まわりの草花はそのまま（景色の通り道の、背の高い草だけどける）
                    if (g.arrival != null) AddViewLane(g.arrival);
                    continue;
                }
                Vector3 c = Area.Ground(g.position.x, g.position.y);
                int before = _viewLanes.Count;
                if (g.arrival != null) AddViewLane(g.arrival);
                var lanes = _viewLanes.GetRange(before, _viewLanes.Count - before);
                int removed = instanced.Edit((mesh, mat, m) =>
                {
                    if (mat != assets.foliage && mat != assets.flowers) return m;
                    Vector3 p = m.GetColumn(3);
                    Vector2 xz = new Vector2(p.x, p.z);
                    if ((xz - g.position).sqrMagnitude < 5.5f * 5.5f) return null;
                    foreach (var (a, b) in lanes)
                        if (ShakuMath.DistToSegment(xz, a, b) < ViewLaneRadius + 0.5f && IsTallFoliage(mesh.name)) return null;
                    return m;
                });
                if (removed > 0) Count("lateGate", g.targetArea, c);
                if (loose != null) loose.PushOut(c + Vector3.up * 1.5f, 5f, 4f);
                // 公園の神社へのトンネル：うしろのさくを 1 まい、はずす（トンネルの奥が、さくでふさがれて見えないように）
                if (Area.Id == "park" && g.targetArea == "shrine") RemoveSolidsNear("Park_Fence", g.position, 6.5f);
            }
        }

        /// <summary>
        /// しずくを置いたあとに、ある物（名前）を、場所 p のまわり r から取りのぞく（見た目と当たり判定）。
        /// あとから足したトンネルのうしろの、さくなど。
        /// </summary>
        void RemoveSolidsNear(string meshName, Vector2 p, float r)
        {
            int removed = instanced.Edit((mesh, mat, m) =>
            {
                if (mesh.name != meshName) return m;
                Vector3 q = m.GetColumn(3);
                return (new Vector2(q.x, q.z) - p).sqrMagnitude < r * r ? (Matrix4x4?)null : m;
            });
            for (int i = _colliderRoot.childCount - 1; i >= 0; i--)
            {
                var t = _colliderRoot.GetChild(i);
                if (t.name != meshName || (new Vector2(t.position.x, t.position.z) - p).sqrMagnitude >= r * r) continue;
                t.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
            Physics.SyncTransforms();
            if (removed > 0) Count("lateGateClear", meshName, new Vector3(p.x, 0f, p.y));
        }

        void BuildGate(GateDef g)
        {
            if (g.door)
            {
                BuildDoorGate(g);
                return;
            }
            if (g.warp)
            {
                BuildWarpGate(g);
                return;
            }
            {
                Vector2 inward2 = (-g.position).normalized;
                Vector3 inward = new Vector3(inward2.x, 0f, inward2.y);
                Vector3 ground = Area.Ground(g.position.x, g.position.y);
                Quaternion rot = Quaternion.LookRotation(inward, Vector3.up);
                Place("RootArch", assets.bark, ground + Vector3.down * 0.3f, rot, 1.2f, true, true, 300f, asRenderer: true);
                Occupy(g.position, 5f);
                // トンネルの中の光
                var portal = new GameObject("GatePortal");
                portal.transform.SetParent(_solidRoot, false);
                portal.transform.SetPositionAndRotation(ground + Vector3.up * 1.9f - inward * 0.4f, rot);
                portal.transform.localScale = new Vector3(4.6f, 4.2f, 1f);
                portal.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                var mr = portal.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.particle;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_TintColor", g.targetArea == "river" ? new Color(0.55f, 0.95f, 1.4f, 0.55f)
                    : g.targetArea == "park" ? new Color(1.4f, 1.15f, 0.55f, 0.55f)
                    : g.targetArea == "mountain" ? new Color(1.2f, 1.15f, 1.5f, 0.55f)
                    : g.targetArea == "coast" ? new Color(0.6f, 1.2f, 1.45f, 0.55f)
                    : g.targetArea == "shrine" ? new Color(1.45f, 0.75f, 0.5f, 0.55f)
                    : g.targetArea == "night" ? new Color(0.55f, 0.7f, 1.45f, 0.55f) : new Color(0.75f, 1.3f, 0.6f, 0.55f));
                mr.SetPropertyBlock(mpb);
                Gates.Add(new GateInstance { def = g, position = ground, inward = inward });
            }
        }

        /// <summary>
        /// 光の輪（夜の森とクリスマス村をつなぐワープ）：たてに立つ金の輪と、まわりの電球。輪の中は、ほんのり光る。
        /// 輪は、くぐれるように当たり判定なし。inward は、輪から出てくる向き（エリアのまん中の方）。
        /// </summary>
        void BuildWarpGate(GateDef g)
        {
            Vector2 inward2 = (-g.position).normalized;
            Vector3 inward = new Vector3(inward2.x, 0f, inward2.y);
            Vector3 ground = Area.Ground(g.position.x, g.position.y);
            Quaternion rot = Quaternion.LookRotation(inward, Vector3.up);
            const float scale = 1.25f;
            Place("Xmas_WarpRing", assets.prop, ground + Vector3.down * 0.25f, rot, scale, false, true, 400f, asRenderer: true);
            Material glow = assets.lightGlow != null ? assets.lightGlow : assets.warmGlow != null ? assets.warmGlow : assets.glow;
            Place("Xmas_WarpLights", glow, ground + Vector3.down * 0.25f, rot, scale, false, false, 400f, asRenderer: true);
            Occupy(g.position, 5f);
            var portal = new GameObject("GatePortal");
            portal.transform.SetParent(_solidRoot, false);
            portal.transform.SetPositionAndRotation(ground + Vector3.up * (4.4f * scale - 0.25f), rot);
            portal.transform.localScale = new Vector3(8.2f, 8.2f, 1f);
            portal.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var mr = portal.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.particle;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_TintColor", g.targetArea == "xmas" ? new Color(1.45f, 1.05f, 0.6f, 0.6f) : new Color(0.6f, 0.75f, 1.45f, 0.6f));
            mr.SetPropertyBlock(mpb);
            Gates.Add(new GateInstance { def = g, position = ground, inward = inward });
        }

        /// <summary>
        /// とびらの出入り口（山小屋）：トンネルの根は作らない。とびらの前に、ほんのり光る板（あたたかい部屋の明かり・外の明るさ）。
        /// inward は、とびらから出てくる向き。
        /// </summary>
        void BuildDoorGate(GateDef g)
        {
            Vector3 ground = Area.Ground(g.position.x, g.position.y);
            Vector3 walk = new Vector3(g.walk.x, 0f, g.walk.y).normalized;
            Vector3 inward = -walk;
            Occupy(g.position, Mathf.Min(2f, g.halfWidth));
            bool intoHut = g.targetArea == "hut";
            float w = intoHut ? 2.3f : g.halfWidth * 2f;
            float h = intoHut ? 3.6f : HutLayout.DoorHeight - 2f;
            var portal = new GameObject("GatePortal");
            portal.transform.SetParent(_solidRoot, false);
            // 小屋のとびら：とびらの面の少し前。小屋の中のとびら：しきいの外（外の光）
            Vector3 at = intoHut ? ground + Vector3.up * (0.8f + h * 0.5f) + inward * 0.06f : ground + Vector3.up * (h * 0.5f + 0.5f) + walk * 2.5f;
            portal.transform.SetPositionAndRotation(at, Quaternion.LookRotation(inward, Vector3.up));
            portal.transform.localScale = new Vector3(w, h, 1f);
            portal.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var mr = portal.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.particle;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_TintColor", intoHut ? new Color(1.4f, 1.0f, 0.55f, 0.32f) : new Color(1.2f, 1.2f, 1.1f, 0.18f));
            mr.SetPropertyBlock(mpb);
            Gates.Add(new GateInstance { def = g, position = ground, inward = inward });
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        bool CastDown(Vector3 above, float maxDist, out RaycastHit hit)
        {
            return Physics.Raycast(above, Vector3.down, out hit, maxDist, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 名所へ移動したときに着く場所：名所のまん中から外へさがして、いちばん近い、かわいた平らな陸地。
        /// 水の中・川の中の石や枝の上（まわりが水）・急な坂には着かない。体（頭の先）まで陸地にのる向きで、なるべく名所の方を向く。
        /// </summary>
        public Vector3 ArrivalPoint(LandmarkDef lm, out Vector3 forward)
        {
            Vector3 p = FindArrival(lm, out forward);
            return OffDew(p, ref forward);
        }

        Vector3 FindArrival(LandmarkDef lm, out Vector3 forward)
        {
            if (lm.TravelView != null && ViewPoint(lm.TravelView, out var vp, out forward, keepFacing: true)) return vp;
            Vector2 c = lm.position;
            Vector3 home = new Vector3(-c.x, 0f, -c.y);
            Vector3 centerFwd = home.sqrMagnitude > 0.01f ? home.normalized : Vector3.forward;
            float maxR = Mathf.Max(8f, lm.radius * 1.6f);
            for (float r = 0f; r <= maxR; r += 0.75f)
            {
                int n = r < 0.01f ? 1 : Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 0.75f));
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n;
                    Vector2 xz = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    // まん中なら、いちばん上の面（切り株の上など）でよい。まわりをさがすときは、地面の上に着く
                    if (!DryArrival(xz, r > 0.01f, out var p)) continue;
                    Vector3 toC = new Vector3(c.x - xz.x, 0f, c.y - xz.y);
                    Vector3 fwd = r < 0.01f || toC.sqrMagnitude < 1e-4f ? centerFwd : toC.normalized;
                    if (!RoomForBody(p, ref fwd)) continue;
                    forward = fwd;
                    return p;
                }
            }
            forward = centerFwd;
            return TopSurface(c);
        }

        /// <summary>
        /// しずくの上に着くなら、同じ高さのすぐそば（3 まで）へずらす（着いたとたんに拾ってしまわないように）。
        /// 切り株やすべり台のてっぺんなら、てっぺんの上のまま。ずらす所がなければ、そのまま。
        /// </summary>
        Vector3 OffDew(Vector3 p, ref Vector3 fwd)
        {
            if (ClearOfDew(p, fwd)) return p;
            bool onGround = p.y - Area.Height(p.x, p.z) <= 0.6f;
            for (float r = 0.75f; r <= 3f; r += 0.75f)
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI * 2f / 12f;
                    Vector2 xz = new Vector2(p.x, p.z) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!DryArrival(xz, onGround, out var q)) continue;
                    if (!onGround && Mathf.Abs(q.y - p.y) > 0.4f) continue;
                    Vector3 f = fwd;
                    if (!RoomForBody(q, ref f, onGround, 3) || !ClearOfDew(q, f)) continue;
                    fwd = f;
                    return q;
                }
            return p;
        }

        /// <summary>しっぽ・まん中・頭のどれも、しずくから 1.2 より離れているか。</summary>
        bool ClearOfDew(Vector3 p, Vector3 fwd)
        {
            foreach (var d in DewdropPoints)
                for (int i = 0; i <= 2; i++)
                    if (Vector3.Distance(d, p + fwd * (ShakuConst.BodyLength * 0.5f * i)) < 1.2f) return false;
            return true;
        }

        /// <summary>
        /// 景色を見る場所：決めた場所がかわいた平らな陸地ならそこ、だめならすぐそば（3 まで）。
        /// 向きは見る先の方（体がのらないときだけ、少し回す）。
        /// </summary>
        public bool ViewPoint(ArrivalView v, out Vector3 p, out Vector3 forward, bool keepFacing = false)
        {
            // 名所へ移動するときは、見る先の方を向いたまま立てる所を先にさがす（大きく向きを変えると、名所が見えない）
            if (keepFacing)
                for (float r = 0f; r <= 3f; r += 0.75f)
                {
                    int n = r < 0.01f ? 1 : 8;
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * Mathf.PI * 2f / n;
                        Vector2 xz = v.from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (!DryArrival(xz, !v.onTop, out p)) continue;
                        Vector3 f = v.Forward;
                        if (!RoomForBody(p, ref f, !v.onTop, 3)) continue;
                        forward = f;
                        return true;
                    }
                }
            for (float r = 0f; r <= 3f; r += 0.75f)
            {
                int n = r < 0.01f ? 1 : 8;
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n;
                    Vector2 xz = v.from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!DryArrival(xz, !v.onTop, out p)) continue;
                    Vector3 f = v.Forward;
                    if (!RoomForBody(p, ref f, !v.onTop)) continue;
                    forward = f;
                    return true;
                }
            }
            p = default;
            forward = v.Forward;
            return false;
        }

        // ------------------------------------------------------------------
        // 景色の通り道：着いたときにカメラから見る先までのあいだには、背の高い草や小物を置かない
        // ------------------------------------------------------------------
        readonly List<(Vector2 a, Vector2 b)> _viewLanes = new List<(Vector2, Vector2)>();
        public const float ViewLaneRadius = 2.4f;

        // 花は見せたいものなので、よけない
        static readonly string[] TallFoliage = { "Grass_", "Fern_", "Clover_", "Sprout", "Reed", "Iris", "Horsetail", "Coast_Grass", "Shrine_Sasa" };

        static bool IsTallFoliage(string meshName)
        {
            foreach (var t in TallFoliage)
                if (meshName.StartsWith(t)) return true;
            return false;
        }

        void BuildViewLanes()
        {
            _viewLanes.Clear();
            foreach (var lm in Area.Landmarks)
                if (lm.view != null) AddViewLane(lm.view);
            foreach (var g in Area.Gates)
                if (g.arrival != null && !g.late) AddViewLane(g.arrival);
        }

        /// <summary>景色の通り道の、はしとはし（カメラのうしろから、見る先の手前まで）。</summary>
        static (Vector2 a, Vector2 b) LaneOf(ArrivalView v)
        {
            Vector3 f = v.Forward;
            Vector2 d = new Vector2(f.x, f.z);
            float toTarget = Vector2.Distance(v.from, new Vector2(v.at.x, v.at.z));
            return (v.from - d * (v.distance + 1.5f), v.from + d * Mathf.Min(v.clear, toTarget * 0.85f));
        }

        /// <summary>景色の通り道 a→b の中か。先へ行くほど広い。</summary>
        static bool InLane(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 ab = b - a;
            float len = ab.magnitude;
            float t = len > 1e-4f ? Mathf.Clamp(Vector2.Dot(p - a, ab) / (len * len), 0f, 1f) : 0f;
            float width = ViewLaneRadius + Mathf.Min(t * len * 0.3f, 4f);
            return Vector2.Distance(p, a + ab * t) < width + radius;
        }

        /// <summary>
        /// 名所へ移動したときの景色（travel）の通り道から、背の高い草をどけ、立つ所のまわりの小石をよける。
        /// しずくを置いたあとに行うので、しずくの場所は変わらない。
        /// </summary>
        void ClearTravelLanes()
        {
            var lanes = new List<(Vector2 a, Vector2 b)>();
            foreach (var lm in Area.Landmarks)
            {
                if (lm.travel == null) continue;
                lanes.Add(LaneOf(lm.travel));
                if (loose != null && !lm.travel.onTop) loose.PushOut(Area.Ground(lm.travel.from.x, lm.travel.from.y), 2.5f, 3f);
            }
            if (lanes.Count == 0) return;
            int removed = instanced.Edit((mesh, mat, m) =>
            {
                if (mat != assets.foliage && mat != assets.flowers) return m;
                if (!IsTallFoliage(mesh.name)) return m;
                Vector3 p = m.GetColumn(3);
                Vector2 xz = new Vector2(p.x, p.z);
                foreach (var (a, b) in lanes)
                    if (InLane(xz, a, b, 0.5f)) return null;
                return m;
            });
            if (removed > 0) Count("travelLane", Area.Id, Vector3.zero);
        }

        void AddViewLane(ArrivalView v)
        {
            var (a, b) = LaneOf(v);
            _viewLanes.Add((a, b));
            // ちらばる小物（岩・葉など）も、ここには置かない
            for (int i = 0; i <= 10; i++) Occupy(Vector2.Lerp(a, b, i / 10f), 1.6f);
        }

        /// <summary>景色の通り道の中か（背の高い草を生やさない場所）。radius は草の大きさ。先へ行くほど広い。</summary>
        public bool InViewLane(Vector2 p, float radius = 0f)
        {
            foreach (var (a, b) in _viewLanes)
                if (InLane(p, a, b, radius)) return true;
            return false;
        }

        bool DryArrival(Vector2 xz, bool onGround, out Vector3 p)
        {
            p = default;
            if (!CastDown(new Vector3(xz.x, 150f, xz.y), 300f, out var hit)) return false;
            p = hit.point;
            if (!Area.InPlayArea(p) || Area.IsUnderwater(p + Vector3.down * 0.02f) || hit.normal.y < 0.75f) return false;
            // 陸地であること（川の中の石・枝の上は、まわりが水なので着かない）
            if (!Area.IsLand(xz.x, xz.y, 0.08f)) return false;
            float wl = Area.WaterLevelAt(xz.x, xz.y);
            if (wl > -100f && p.y < wl + 0.08f) return false;
            if (onGround && p.y - Area.Height(xz.x, xz.y) > 0.6f) return false;
            return true;
        }

        /// <summary>頭の先（1 体長前）まで、かわいた陸地にのるか。だめなら、ほかの向きをためす。</summary>
        bool RoomForBody(Vector3 p, ref Vector3 fwd, bool onGround = true, int turns = 8)
        {
            for (int i = 0; i < turns; i++)
            {
                float turn = (i + 1) / 2 * 45f * (i % 2 == 0 ? 1f : -1f);
                Vector3 d = Quaternion.AngleAxis(turn, Vector3.up) * fwd;
                Vector3 h = p + d * ShakuConst.BodyLength;
                if (DryArrival(new Vector2(h.x, h.z), onGround, out var hp) && Mathf.Abs(hp.y - p.y) < 0.5f)
                {
                    fwd = d;
                    return true;
                }
            }
            return false;
        }

        void AddDew(Vector3 surfacePoint)
        {
            foreach (var d in DewdropPoints)
                if (Vector3.Distance(d, surfacePoint) < 3.5f) return;
            DewdropPoints.Add(surfacePoint);
        }

        void AddDewFromAbove(Vector2 xz, float fromHeight = 120f)
        {
            if (CastDown(new Vector3(xz.x, fromHeight, xz.y), fromHeight + 20f, out var hit))
            {
                if (Area.IsUnderwater(hit.point)) return;
                AddDew(hit.point);
            }
        }

        MobGroup AddMob(string species, Vector3 center, int count, float radius, float height = 0f)
        {
            var g = new MobGroup { species = species, center = center, count = count, radius = radius, height = height };
            // 鳥は、種類ごとに 1 羽まで（エリアごと）。2 つ目からの群れは置かない（下りる場所の追加は、受けとって捨てる）
            var sp = SpeciesCatalog.Get(species);
            if (sp != null && sp.kind == MobKind.Bird)
            {
                if (Mobs.Exists(o => o.species == species)) return g;
                g.count = Mathf.Min(count, 1);
            }
            Mobs.Add(g);
            return g;
        }

        /// <summary>アリの行列：巣からエサまで行って、少しずれた道で帰ってくるループ。</summary>
        MobGroup AddAntLine(Vector2 nest, Vector2 food, int count)
        {
            Vector2 d = (food - nest).normalized;
            Vector2 perp = new Vector2(-d.y, d.x);
            Vector2 side = perp * 0.45f;
            var g = AddMob("ant", Area.Ground(nest.x, nest.y), count, 0f);
            int n = 10;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector2 p = Vector2.Lerp(nest, food, t) + side + perp * Mathf.Sin(t * Mathf.PI * 2f) * 0.8f;
                g.path.Add(Area.Ground(p.x, p.y));
            }
            for (int i = n; i >= 0; i--)
            {
                float t = i / (float)n;
                Vector2 p = Vector2.Lerp(nest, food, t) - side + perp * Mathf.Sin(t * Mathf.PI * 2f) * 0.8f;
                g.path.Add(Area.Ground(p.x, p.y));
            }
            Place("AntHill", assets.prop, Area.Ground(nest.x, nest.y) + Vector3.down * 0.05f, Quaternion.Euler(0f, R(0, 360), 0f), 1f, true, true, 120f);
            Occupy(nest, 1.6f);
            return g;
        }

        // ------------------------------------------------------------------
        // 光の筋
        // ------------------------------------------------------------------
        static Mesh _shaftMesh;

        static Mesh ShaftMesh()
        {
            if (_shaftMesh != null) return _shaftMesh;
            int seg = 20, rows = 6;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int r = 0; r <= rows; r++)
            {
                float y = r / (float)rows;           // 0 = 上（空側）, 1 = 地面
                float rad = Mathf.Lerp(0.6f, 1.0f, y);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * rad + Vector3.down * y);
                    n.Add(d);
                    uv.Add(new Vector2(s / (float)seg, y));
                }
            }
            for (int r = 0; r < rows; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = r * (seg + 1) + s;
                int b = a + 1;
                int c = a + seg + 1;
                int d = c + 1;
                t.Add(a); t.Add(c); t.Add(b);
                t.Add(b); t.Add(c); t.Add(d);
            }
            _shaftMesh = new Mesh { name = "LightShaft" };
            _shaftMesh.SetVertices(v);
            _shaftMesh.SetNormals(n);
            _shaftMesh.SetUVs(0, uv);
            _shaftMesh.SetTriangles(t, 0);
            _shaftMesh.RecalculateBounds();
            return _shaftMesh;
        }

        void BuildLightShafts(IList<Vector2> spots)
        {
            if (assets.lightShaft == null) return;
            Vector3 dir = sun != null ? sun.transform.forward : new Vector3(0.3f, -0.85f, 0.4f).normalized;
            var parent = new GameObject("LightShafts").transform;
            parent.SetParent(Root, false);
            foreach (var s in spots)
            {
                Vector3 ground = Area.Ground(s.x, s.y);
                float len = 70f;
                var go = new GameObject("Shaft");
                go.transform.SetParent(parent, false);
                go.transform.position = ground - dir * len;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir);
                float w = R(2.5f, 4.5f);
                go.transform.localScale = new Vector3(w, len, w);
                go.AddComponent<MeshFilter>().sharedMesh = ShaftMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.lightShaft;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------
        // スポーン地点
        // ------------------------------------------------------------------
        void ComputeSpawn()
        {
            Vector2 s = Area.Spawn;
            if (CastDown(new Vector3(s.x, 50f, s.y), 100f, out var hit)) SpawnPoint = hit.point;
            else SpawnPoint = Area.Ground(s.x, s.y);
            SpawnForward = Area.SpawnForward;
        }

        /// <summary>その XZ のいちばん上の面（小物の上も含む）。</summary>
        public Vector3 TopSurface(Vector2 xz)
        {
            if (CastDown(new Vector3(xz.x, 150f, xz.y), 300f, out var hit)) return hit.point;
            return Area.Ground(xz.x, xz.y);
        }

        // ------------------------------------------------------------------
        // ミニマップ
        // ------------------------------------------------------------------
        static Vector2 MapToWorld(int i, int j, int size)
        {
            return new Vector2(Mathf.Lerp(-MapExtent, MapExtent, (i + 0.5f) / size), Mathf.Lerp(-MapExtent, MapExtent, (j + 0.5f) / size));
        }

        static void MapDot(Color32[] px, int size, Vector3 pos, float radius, Color32 color)
        {
            int ci = Mathf.RoundToInt((pos.x + MapExtent) / (2f * MapExtent) * size);
            int cj = Mathf.RoundToInt((pos.z + MapExtent) / (2f * MapExtent) * size);
            int rad = Mathf.Max(1, Mathf.RoundToInt(radius));
            for (int dj = -rad; dj <= rad; dj++)
            for (int di = -rad; di <= rad; di++)
            {
                if (di * di + dj * dj > rad * rad) continue;
                int ii = ci + di, jj = cj + dj;
                if (ii < 0 || jj < 0 || ii >= size || jj >= size) continue;
                px[jj * size + ii] = color;
            }
        }

        /// <summary>ほかのエリアの地図（地形と、動かない目印だけで描く。一度描いたら覚えておく）。</summary>
        static readonly Dictionary<string, Texture2D> s_otherMaps = new Dictionary<string, Texture2D>();

        /// <summary>
        /// そのエリアの地図。いまのエリアは、作ったときの地図（小物の場所もふくむ）。
        /// ほかのエリアは、地形と動かない目印（大樹・川・遊具など）だけで描く（そのエリアを作らなくても描ける）。
        /// </summary>
        public Texture2D MapFor(AreaLayout area)
        {
            if (area == null) return MapTexture;
            if (area == Area && MapTexture != null) return MapTexture;
            if (s_otherMaps.TryGetValue(area.Id, out var t) && t != null) return t;
            t = RenderMap(area, false);
            t.name = "AreaMap_" + area.Id;
            s_otherMaps[area.Id] = t;
            return t;
        }

        /// <summary>地図に小道を描く。</summary>
        static void MapTrails(Color32[] px, int size, List<Vector2[]> trails, Color32 color)
        {
            foreach (var t in trails)
                for (int i = 0; i < t.Length - 1; i++)
                {
                    float len = Vector2.Distance(t[i], t[i + 1]);
                    for (float s = 0f; s <= len; s += 0.6f)
                    {
                        Vector2 p = Vector2.Lerp(t[i], t[i + 1], s / len);
                        MapDot(px, size, new Vector3(p.x, 0f, p.y), 0.9f, color);
                    }
                }
        }

        // 地形の高さと色の格子（地形を作ったときのもの。地図の絵を、すばやく描くのに使う）
        float[,] _gridH;
        Color[] _gridC;
        bool[] _gridCSet;
        float _gridOrigin, _gridSpacing;
        int _gridVerts;

        /// <summary>地形の格子から、その場所の高さ・向き・色をとる（まわりの 4 点をまぜる）。</summary>
        void GridSample(Vector2 p, out float h, out Vector3 n, out Color c)
        {
            float gx = (p.x - _gridOrigin) / _gridSpacing, gz = (p.y - _gridOrigin) / _gridSpacing;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 1, _gridVerts - 3), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 1, _gridVerts - 3);
            float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0);
            h = 0f;
            n = Vector3.zero;
            c = Color.clear;
            for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                float w = (dx == 0 ? 1f - fx : fx) * (dz == 0 ? 1f - fz : fz);
                int gi = x0 + dx, gj = z0 + dz;
                h += _gridH[gi, gj] * w;
                Vector3 vn = new Vector3(_gridH[gi - 1, gj] - _gridH[gi + 1, gj], 2f * _gridSpacing, _gridH[gi, gj - 1] - _gridH[gi, gj + 1]).normalized;
                n += vn * w;
                int k = gj * _gridVerts + gi;
                if (!_gridCSet[k])
                {
                    _gridC[k] = Area.GroundColor(_gridOrigin + gi * _gridSpacing, _gridOrigin + gj * _gridSpacing, _gridH[gi, gj], vn);
                    _gridCSet[k] = true;
                }
                c += _gridC[k] * w;
            }
            n.Normalize();
        }

        void BuildMap()
        {
            MapTexture = Own(RenderMap(Area, true));
        }

        /// <summary>地図の絵を描く。generated = true なら、いま作ったエリアの小物（赤キノコなど）の場所も描く。</summary>
        Texture2D RenderMap(AreaLayout area, bool generated)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "AreaMap" };
            var px = new Color32[size * size];
            Vector3 light = new Vector3(-0.5f, 0.75f, 0.45f).normalized;
            ColorUtility.TryParseHtmlString("#5aa9d6", out var water);
            // いま作ったエリアは、地形の格子から（あとから土手を盛った所だけは、その場ではかる）
            bool fromGrid = generated && area == Area && _gridH != null;
            var late = fromGrid ? new List<(Vector2 center, float radius)>(area.LateGroundZones) : null;
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                Vector2 p = MapToWorld(i, j, size);
                float h;
                Vector3 n;
                Color c;
                bool exact = !fromGrid;
                if (fromGrid)
                    foreach (var (zc, zr) in late)
                        if ((p - zc).sqrMagnitude < (zr + 1f) * (zr + 1f)) { exact = true; break; }
                if (exact)
                {
                    h = area.Height(p.x, p.y);
                    n = area.Normal(p.x, p.y);
                    c = area.GroundColor(p.x, p.y, h, n);
                }
                else GridSample(p, out h, out n, out c);
                float shade = Mathf.Clamp01(Vector3.Dot(n, light)) * 0.35f + 0.75f;
                c *= shade;
                float wl = area.WaterLevelAt(p.x, p.y);
                if (h < wl) c = Color.Lerp(water, water * 0.7f, Mathf.Clamp01((wl - h) / 2f));
                float r = p.magnitude;
                if (r > area.PlayRadius) c = Color.Lerp(c, c * 0.45f, ShakuMath.SmoothStep(area.PlayRadius, area.PlayRadius + 4f, r));
                c.a = 1f;
                px[j * size + i] = c;
            }
            if (area.Id == "forest") DrawForestMap(px, size, generated);
            else if (area.Id == "park") DrawParkMap(px, size);
            else if (area.Id == "mountain") DrawMountainMap(px, size, generated);
            else if (area.Id == "hut") DrawHutMap(px, size);
            else if (area.Id == "coast") DrawCoastMap(px, size, generated);
            else if (area.Id == "shrine") DrawShrineMap(px, size);
            else if (area.Id == "night") DrawNightMap(px, size);
            else if (area.Id == "xmas") DrawXmasMap(px, size);
            else if (area.Id == "tutorial") DrawTutorialMap(px, size);
            else DrawRiverMap(px, size, generated);
            tex.SetPixels32(px);
            tex.Apply(false, false);   // 読めるままにしておく（地図の絵を確かめられるように。256×256 なので小さい）
            return tex;
        }
    }
}

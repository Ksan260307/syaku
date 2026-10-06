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
        public Light sun;
        public int seed = 20261006;
        [Range(0.2f, 1f)] public float foliageDensity = 1f;

        public static WorldGenerator Instance { get; private set; }
        public bool IsGenerated { get; private set; }
        public Transform Root { get; private set; }
        public Texture2D MapTexture { get; private set; }
        public AreaLayout Area { get; private set; } = Areas.Forest;
        public readonly List<Vector3> DewdropPoints = new List<Vector3>();
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

        public IEnumerator Generate(Action<float, string> progress) => Generate(Areas.Current, progress);

        public IEnumerator Generate(AreaLayout area, Action<float, string> progress)
        {
            Instance = this;
            Clear();
            Area = area;
            bool forest = area.Id == "forest";
            _rng = new Random(forest ? seed : seed + 7919 * area.Id.Length);
            SurfaceProbe.ClearCache();
            var rootGo = new GameObject("World (generated)");
            if (!Application.isPlaying) rootGo.hideFlags = HideFlags.DontSave;
            Root = rootGo.transform;
            _solidRoot = new GameObject("Solids").transform;
            _solidRoot.SetParent(Root, false);
            _colliderRoot = new GameObject("PropColliders").transform;
            _colliderRoot.SetParent(Root, false);

            progress?.Invoke(0.05f, "地面をならしています");
            yield return null;
            BuildTerrain();
            progress?.Invoke(0.25f, forest ? "大きな木を育てています" : "川の水を流しています");
            yield return null;
            BuildOuterRing();
            if (forest) BuildForestSolids(); else BuildRiverSolids();
            BuildGates();
            progress?.Invoke(0.4f, forest ? "キノコと岩を並べています" : "川石を並べています");
            yield return null;
            if (forest)
            {
                PlaceForestMobProps();
                ScatterForestProps();
                BuildForestWater();
            }
            else
            {
                ScatterRiverProps();
                BuildRiverWater();
            }
            Physics.SyncTransforms();
            progress?.Invoke(0.6f, "草花を植えています");
            yield return null;
            if (forest) BuildForestFoliage(); else BuildRiverFoliage();
            progress?.Invoke(0.8f, "しずくを置いています");
            yield return null;
            if (forest)
            {
                PlaceForestDewdrops();
                PlaceForestCreatures();
                BuildLightShafts(ForestShaftSpots);
            }
            else
            {
                PlaceRiverDewdrops();
                PlaceRiverCreatures();
                BuildLightShafts(RiverShaftSpots());
            }
            BuildMap();
            instanced.Build();
            ComputeSpawn();
            IsGenerated = true;
            progress?.Invoke(1f, "準備完了");
        }

        public void Clear()
        {
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
            _occupied.Clear();
            _bigRocks.Clear();
            _redCaps.Clear();
            _lilyPads.Clear();
            _bigLeaves.Clear();
            _twigs.Clear();
            _riverStones.Clear();
            _stepStones.Clear();
            _poolPads.Clear();
            _fallRocks.Clear();
            Ferry = null;
            _specialCap = Vector3.zero;
            DewdropPoints.Clear();
            Mobs.Clear();
            Gates.Clear();
            if (instanced != null) instanced.Clear();
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
                if (nearest < 80f)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                }
            }
        }

        void BuildOuterRing()
        {
            float[] radii = { 92f, 100f, 112f, 128f, 150f, 185f, 240f, 320f };
            int seg = 128;
            var pos = new List<Vector3>();
            var col = new List<Color>();
            var tris = new List<int>();
            ColorUtility.TryParseHtmlString("#3f6a35", out var deep);
            ColorUtility.TryParseHtmlString("#5c4a38", out var soil);
            for (int r = 0; r < radii.Length; r++)
            {
                for (int s = 0; s < seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    float x = Mathf.Cos(a) * radii[r];
                    float z = Mathf.Sin(a) * radii[r];
                    float h = Area.Height(x, z) - 0.35f;
                    if (radii[r] > 96f) h = Area.Height(x / radii[r] * 96f, z / radii[r] * 96f) + (radii[r] - 96f) * 0.12f
                                            + (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) - 0.5f) * 6f;
                    pos.Add(new Vector3(x, h, z));
                    float k = Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f);
                    col.Add(Color.Lerp(deep, soil, k * 0.6f) * Mathf.Lerp(0.85f, 0.6f, r / (float)radii.Length));
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
                Place(i % 2 == 0 ? "BgTrunk_A" : "BgTrunk_B", assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.6f), false, true, 500f, asRenderer: true);
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

        /// <summary>メッシュを配置する。インスタンシング描画 + 必要ならコライダー専用オブジェクト。</summary>
        GameObject Place(string meshName, Material mat, Vector3 pos, Quaternion rot, float scale, bool collider,
            bool castShadows = true, float maxDistance = 150f, bool asRenderer = false)
        {
            Mesh m = assets.Get(meshName);
            if (m == null || mat == null) return null;
            var mtx = Matrix4x4.TRS(pos, rot, Vector3.one * scale);
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
                // 登りやすいように作った当たり判定用のメッシュがあれば、そちらを使う
                go.AddComponent<MeshCollider>().sharedMesh = assets.TryGet(meshName + "_Col") ?? m;
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
                mpb.SetColor("_TintColor", g.targetArea == "river" ? new Color(0.55f, 0.95f, 1.4f, 0.55f) : new Color(0.75f, 1.3f, 0.6f, 0.55f));
                mr.SetPropertyBlock(mpb);
                Gates.Add(new GateInstance { def = g, position = ground, inward = inward });
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        bool CastDown(Vector3 above, float maxDist, out RaycastHit hit)
        {
            return Physics.Raycast(above, Vector3.down, out hit, maxDist, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
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

        void FillDewdrops(float rMin, float rMax)
        {
            int guard = 0;
            int target = Area.DropCount;
            while (DewdropPoints.Count < target && guard++ < 3000)
            {
                int sector = DewdropPoints.Count % 8;
                float a = (sector + R01()) / 8f * Mathf.PI * 2f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(rMin, rMax);
                if (!IsLand(p, 0.2f)) continue;
                AddDewFromAbove(p);
            }
        }

        MobGroup AddMob(string species, Vector3 center, int count, float radius, float height = 0f)
        {
            var g = new MobGroup { species = species, center = center, count = count, radius = radius, height = height };
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

        void BuildMap()
        {
            const int size = 256;
            var tex = Own(new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "AreaMap" });
            var px = new Color32[size * size];
            Vector3 light = new Vector3(-0.5f, 0.75f, 0.45f).normalized;
            ColorUtility.TryParseHtmlString("#5aa9d6", out var water);
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                Vector2 p = MapToWorld(i, j, size);
                float h = Area.Height(p.x, p.y);
                Vector3 n = Area.Normal(p.x, p.y);
                Color c = Area.GroundColor(p.x, p.y, h, n);
                float shade = Mathf.Clamp01(Vector3.Dot(n, light)) * 0.35f + 0.75f;
                c *= shade;
                float wl = Area.WaterLevelAt(p.x, p.y);
                if (h < wl) c = Color.Lerp(water, water * 0.7f, Mathf.Clamp01((wl - h) / 2f));
                float r = p.magnitude;
                if (r > Area.PlayRadius) c = Color.Lerp(c, c * 0.45f, ShakuMath.SmoothStep(Area.PlayRadius, Area.PlayRadius + 4f, r));
                c.a = 1f;
                px[j * size + i] = c;
            }
            if (Area.Id == "forest") DrawForestMap(px, size);
            else DrawRiverMap(px, size);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            MapTexture = tex;
        }
    }
}

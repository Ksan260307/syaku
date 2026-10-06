using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace Shakutori
{
    /// <summary>
    /// 森エリアをシード値から決定的に生成する。
    /// 地形・大樹・切り株・丸太・キノコ・落ち葉・草花・水たまり・しずく・光の筋・ミニマップ。
    /// </summary>
    public class WorldGenerator : MonoBehaviour
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
        public readonly List<Vector3> DewdropPoints = new List<Vector3>();
        public Vector3 SpawnPoint { get; private set; }
        public Vector3 SpawnForward { get; private set; }

        public const float MapExtent = 80f;   // ミニマップがカバーする半径（XZ）

        Random _rng;
        Transform _solidRoot;
        Transform _colliderRoot;
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

        void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            Instance = this;
        }

        // ------------------------------------------------------------------
        // 生成エントリ
        // ------------------------------------------------------------------
        public void GenerateNow()
        {
            var e = Generate(null);
            while (e.MoveNext()) { }
        }

        public IEnumerator Generate(Action<float, string> progress)
        {
            Instance = this;
            Clear();
            _rng = new Random(seed);
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
            progress?.Invoke(0.25f, "大きな木を育てています");
            yield return null;
            BuildOuterRing();
            BuildLandmarkSolids();
            progress?.Invoke(0.4f, "キノコと岩を並べています");
            yield return null;
            ScatterProps();
            BuildWater();
            Physics.SyncTransforms();
            progress?.Invoke(0.6f, "草花を植えています");
            yield return null;
            BuildFoliage();
            progress?.Invoke(0.8f, "しずくを置いています");
            yield return null;
            PlaceDewdrops();
            BuildLightShafts();
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
                if (Application.isPlaying) Destroy(Root.gameObject);
                else DestroyImmediate(Root.gameObject);
            }
            var stale = GameObject.Find("World (generated)");
            if (stale != null)
            {
                if (Application.isPlaying) Destroy(stale);
                else DestroyImmediate(stale);
            }
            Root = null;
            _occupied.Clear();
            _bigRocks.Clear();
            _redCaps.Clear();
            _lilyPads.Clear();
            _bigLeaves.Clear();
            _twigs.Clear();
            DewdropPoints.Clear();
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
            int cells = Mathf.RoundToInt(ForestLayout.TerrainHalf * 2f / spacing); // 256
            int verts = cells + 1;
            float origin = -ForestLayout.TerrainHalf;
            var heights = new float[verts, verts];
            for (int j = 0; j < verts; j++)
            for (int i = 0; i < verts; i++)
                heights[i, j] = ForestLayout.Height(origin + i * spacing, origin + j * spacing);

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
                    col[k] = ForestLayout.GroundColor(x, z, h, normal);
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
                var mesh = new Mesh { name = $"Terrain_{cx}_{cz}" };
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
            var nrm = new List<Vector3>();
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
                    float h = ForestLayout.Height(x, z) - 0.35f;
                    if (radii[r] > 96f) h = ForestLayout.Height(x / radii[r] * 96f, z / radii[r] * 96f) + (radii[r] - 96f) * 0.12f
                                            + (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) - 0.5f) * 6f;
                    pos.Add(new Vector3(x, h, z));
                    nrm.Add(Vector3.up);
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
            var mesh = new Mesh { name = "OuterRing" };
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

        bool IsLand(Vector2 p, float margin = 0.25f) => ForestLayout.Height(p.x, p.y) > ForestLayout.WaterLevel + margin;

        Quaternion GroundRotation(Vector2 p, float yawDeg, float align, float maxTilt)
        {
            Vector3 n = ForestLayout.Normal(p.x, p.y);
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
                go.AddComponent<MeshCollider>().sharedMesh = m;
            }
            return go;
        }

        // ------------------------------------------------------------------
        // 大きな目印（大樹・切り株・丸太・背景の幹）
        // ------------------------------------------------------------------
        void BuildLandmarkSolids()
        {
            // 大樹: アーチ状の根が「光るキノコの洞」を向くように回転
            Mesh tree = assets.Get("GreatTree");
            if (tree != null)
            {
                Vector3 archLocal = Vector3.zero;
                var v = tree.vertices;
                foreach (var p in v)
                {
                    float rh = new Vector2(p.x, p.z).magnitude;
                    if (rh > 24f && p.y > 6f) archLocal += new Vector3(p.x, 0f, p.z);
                }
                if (archLocal.sqrMagnitude < 1e-3f) archLocal = Vector3.forward;
                Vector2 want = (ForestLayout.ArchTarget - ForestLayout.GreatTree).normalized;
                float yaw = Vector3.SignedAngle(archLocal.normalized, new Vector3(want.x, 0f, want.y), Vector3.up);
                Vector3 tp = ForestLayout.Ground(ForestLayout.GreatTree.x, ForestLayout.GreatTree.y) + Vector3.down * 1.2f;
                Place("GreatTree", assets.bark, tp, Quaternion.Euler(0f, yaw, 0f), 1f, true, true, 400f, asRenderer: true);
                Occupy(ForestLayout.GreatTree, 24f);
            }

            // 古い切り株
            Vector3 sp = ForestLayout.Ground(ForestLayout.Stump.x, ForestLayout.Stump.y) + Vector3.down * 0.6f;
            Place("Stump", assets.bark, sp, Quaternion.Euler(0f, 37f, 0f), 1f, true, true, 300f, asRenderer: true);
            Occupy(ForestLayout.Stump, 10f);

            // 中が空洞の丸太
            Vector2 la = ForestLayout.LogCenter - ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            Vector2 lb = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            Vector3 A = ForestLayout.Ground(la.x, la.y) + Vector3.up * 3.35f;
            Vector3 B = ForestLayout.Ground(lb.x, lb.y) + Vector3.up * 3.35f;
            Mesh logMesh = assets.Get("HollowLog");
            Vector3 logAxis = logMesh != null && logMesh.bounds.center.x < 0f ? Vector3.left : Vector3.right;
            Quaternion logRot = Quaternion.FromToRotation(logAxis, (B - A).normalized);
            // メッシュは x=0..48 に伸びているので端 A に置く
            Place("HollowLog", assets.bark, A, logRot, 1f, true, true, 300f, asRenderer: true);
            for (int i = 0; i <= 8; i++)
                Occupy(Vector2.Lerp(la, lb, i / 8f), 5.5f);

            // 背景の大きな幹（森の奥行き）
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f + R(-0.12f, 0.12f);
                float d = R(100f, 150f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 45f) continue;
                Vector3 g = ForestLayout.Ground(p.x, p.y) + Vector3.down * 2f;
                Place(i % 2 == 0 ? "BgTrunk_A" : "BgTrunk_B", assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.6f), false, true, 500f, asRenderer: true);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = (i + 0.5f) / 6f * Mathf.PI * 2f + 0.4f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(80f, 88f);
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 40f) continue;
                Vector3 g = ForestLayout.Ground(p.x, p.y) + Vector3.down * 1.5f;
                Place("BgTrunk_B", assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.55f, 0.75f), false, true, 500f, asRenderer: true);
            }
        }

        // ------------------------------------------------------------------
        // 小物（コライダーあり）
        // ------------------------------------------------------------------
        void ScatterProps()
        {
            Material prop = assets.prop;

            // --- 赤キノコの森 ---
            Vector2 g = ForestLayout.MushroomGrove;
            Vector2[] bigReds = { new Vector2(0, 0), new Vector2(-7, 5), new Vector2(6, 6), new Vector2(4, -7), new Vector2(-6, -6) };
            float[] bigScales = { 1.6f, 1.2f, 1.0f, 1.35f, 0.9f };
            for (int i = 0; i < bigReds.Length; i++)
            {
                Vector2 p = g + bigReds[i];
                float s = bigScales[i];
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.2f;
                Place("Mushroom_Red", prop, pos, GroundRotation(p, R(0, 360), 0.2f, 4f), s, true);
                Occupy(p, 1.2f * s);
                _redCaps.Add((pos, s));
            }
            for (int i = 0; i < 7; i++)
            {
                Vector2 p = RandomInCircle(g, 13f);
                if (!IsFree(p, 2f) || ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                float s = R(0.7f, 1.3f);
                Place("Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.15f, GroundRotation(p, R(0, 360), 0.3f, 5f), s, true);
                Occupy(p, 1.3f * s);
            }
            for (int i = 0; i < 9; i++)
            {
                Vector2 p = RandomInCircle(g, 14f);
                if (!IsFree(p, 1f)) continue;
                float s = R(0.8f, 1.3f);
                Place("Mushroom_Cluster", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 6f), s, true);
                Occupy(p, 0.8f * s);
            }
            // 妖精の輪
            Vector2 ring = g + new Vector2(8f, -9f);
            for (int i = 0; i < 13; i++)
            {
                float a = i / 13f * Mathf.PI * 2f;
                Vector2 p = ring + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5.5f;
                float s = R(0.35f, 0.5f);
                Place(i % 3 == 0 ? "Mushroom_Cluster" : "Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.5f, 6f), s, true);
            }

            // --- 光るキノコの洞 ---
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = RandomInCircle(ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f), 6.5f);
                float s = R(0.45f, 1.2f);
                Place("Mushroom_Glow", assets.glow, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.1f, GroundRotation(p, R(0, 360), 0.5f, 10f), s, true, true, 150f);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 la = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f + R(1.5f, 4f)) + new Vector2(R(-4f, 4f), R(-4f, 4f));
                Place("Mushroom_Glow", assets.glow, ForestLayout.Ground(la.x, la.y), GroundRotation(la, R(0, 360), 0.5f, 10f), R(0.3f, 0.6f), true);
            }

            // --- あちこちのキノコ ---
            for (int i = 0; i < 26; i++)
            {
                Vector2 p = RandomInRing(8f, 62f);
                if (!IsFree(p, 1.5f) || !IsLand(p) || ForestLayout.TrailMask(p.x, p.y) > 0.25f) continue;
                int k = RI(0, 10);
                if (k < 2)
                {
                    float s = R(0.45f, 0.8f);
                    Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.15f;
                    Place("Mushroom_Red", prop, pos, GroundRotation(p, R(0, 360), 0.3f, 5f), s, true);
                    Occupy(p, 1.1f * s);
                }
                else if (k < 5)
                {
                    float s = R(0.5f, 1.0f);
                    Place("Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.1f, GroundRotation(p, R(0, 360), 0.3f, 6f), s, true);
                    Occupy(p, 1.2f * s);
                }
                else
                {
                    float s = R(0.6f, 1.1f);
                    Place("Mushroom_Cluster", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 6f), s, true);
                    Occupy(p, 0.7f * s);
                }
            }
            // 切り株のまわり
            for (int i = 0; i < 5; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = ForestLayout.Stump + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(9.5f, 12f);
                Place("Mushroom_Cluster", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 8f), R(0.6f, 1.1f), true);
            }

            // --- 岩（小石） ---
            for (int i = 0; i < 60; i++)
            {
                Vector2 p = RandomInRing(6f, 62f);
                float s = R(0.35f, 1.7f);
                if (!IsFree(p, 1.4f * s) || !IsLand(p, 0.0f) || ForestLayout.TrailMask(p.x, p.y) > 0.2f) continue;
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.25f * s;
                Quaternion rot = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
                Place(Pick(Rocks), prop, pos, rot, s, true);
                Occupy(p, 1.3f * s);
                if (s > 1.1f) _bigRocks.Add((pos, s));
            }
            // 外周の大岩（自然な境界）
            for (int i = 0; i < 34; i++)
            {
                float a = i / 34f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(64f, 74f);
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 22f) continue;
                float s = R(2.6f, 5.2f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.35f * s;
                Place(Pick(Rocks), prop, pos, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }

            // --- 大きな落ち葉（足場） ---
            for (int i = 0; i < 130; i++)
            {
                Vector2 p;
                int zone = RI(0, 10);
                if (zone < 2) p = RandomInCircle(ForestLayout.AcornPlaza, 14f);
                else if (zone < 4) p = RandomInCircle(ForestLayout.GreatTree + new Vector2(0f, -28f), 20f);
                else p = RandomInRing(4f, 63f);
                if (!IsLand(p, 0.1f) || InsideOccupied(p)) continue;
                if (ForestLayout.TrailMask(p.x, p.y) > 0.6f && R01() < 0.7f) continue;
                string leaf = Pick(BigLeaves);
                if (Vector2.Distance(p, ForestLayout.Meadow) < 14f) leaf = R01() < 0.7f ? "Leaf_Oak_Green" : leaf;
                float s = R(0.7f, 1.25f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.04f;
                Quaternion rot = GroundRotation(p, R(0, 360), 1f, 3f);
                Place(leaf, prop, pos, rot, s, true, true, 120f);
                if (_bigLeaves.Count < 40) _bigLeaves.Add(pos);
            }

            // --- 小枝 ---
            for (int i = 0; i < 18; i++)
            {
                Vector2 p = RandomInRing(6f, 62f);
                if (!IsFree(p, 3f) || !IsLand(p)) continue;
                float s = R(0.8f, 1.2f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f;
                Place(R01() < 0.5f ? "Twig_A" : "Twig_B", prop, pos, GroundRotation(p, R(0, 360), 0.8f, 2f), s, true, true, 150f);
                _twigs.Add(pos);
            }

            // --- どんぐり広場 ---
            Vector2 ap = ForestLayout.AcornPlaza;
            for (int i = 0; i < 22; i++)
            {
                Vector2 p = i < 14 ? RandomInCircle(ap, 12f) : RandomInRing(8f, 60f);
                if (!IsFree(p, 1f) || !IsLand(p)) continue;
                float s = R(0.9f, 1.25f);
                bool lying = R01() < 0.45f;
                Quaternion rot = lying
                    ? Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(0f, 0f, 88f)
                    : GroundRotation(p, R(0, 360), 0.5f, 8f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + (lying ? Vector3.up * 0.42f * s : Vector3.down * 0.03f);
                Place("Acorn", assets.propGlossy, pos, rot, s, true);
                Occupy(p, 0.7f * s);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = RandomInCircle(ap, 11f);
                if (!IsFree(p, 1f)) continue;
                float s = R(1.0f, 1.3f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.02f;
                Place("AcornCap", prop, pos, GroundRotation(p, R(0, 360), 0.6f, 5f), s, true);
                Occupy(p, 0.8f * s);
                if (i == 0 || _specialCap == Vector3.zero) _specialCap = pos;
            }
            for (int i = 0; i < 10; i++)
            {
                Vector2 p = i < 6 ? RandomInCircle(ap, 12f) : RandomInCircle(ForestLayout.GreatTree + new Vector2(0, -26f), 16f);
                if (!IsFree(p, 1.6f) || !IsLand(p)) continue;
                float s = R(0.9f, 1.3f);
                Quaternion rot = Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(R(70f, 95f), 0f, 0f);
                Place("Pinecone", prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.45f * s, rot, s, true);
                Occupy(p, 1.4f * s);
            }

            // --- 水たまり（葉っぱの舟と橋） ---
            Vector2 pc = ForestLayout.Pond;
            Vector2[] pads =
            {
                new Vector2(-1.5f, 12.5f), new Vector2(1.5f, 8.6f), new Vector2(-2f, 4.6f), new Vector2(2.2f, 1.2f),
                new Vector2(-1f, -2.6f), new Vector2(3.5f, -5.4f), new Vector2(-4.5f, -1.2f), new Vector2(7.2f, -2.2f),
                new Vector2(-6f, 6f), new Vector2(6.5f, 6.5f),
            };
            for (int i = 0; i < pads.Length; i++)
            {
                Vector2 p = pc + pads[i];
                float s = i < 8 ? R(0.85f, 1.05f) : R(0.6f, 0.8f);
                // 葉の舟は厚みが下向きにあるので、上面が水面より少し上に来るよう浮かせる（重なりのちらつき防止に高さも少しずらす）
                Vector3 pos = new Vector3(p.x, ForestLayout.WaterLevel + 0.08f + i * 0.012f, p.y);
                Place("LilyPad", prop, pos, Quaternion.Euler(0f, R(0, 360), 0f), s, true, false, 150f);
                _lilyPads.Add(pos);
            }
            Place("WaterLily", assets.flowers, new Vector3(pc.x + 3.2f, ForestLayout.WaterLevel + 0.1f, pc.y - 5.0f), Quaternion.Euler(0f, 30f, 0f), 1.0f, false);
            Place("WaterLily", assets.flowers, new Vector3(pc.x - 5.8f, ForestLayout.WaterLevel + 0.1f, pc.y + 6.2f), Quaternion.Euler(0f, 80f, 0f), 0.8f, false);
            // 岸から最初の葉っぱへ渡る小枝の橋
            {
                Vector3 shore = ForestLayout.Ground(pc.x - 1.2f, pc.y + 19.5f);
                Vector3 pad0 = _lilyPads[0];
                Vector3 mid = (shore + pad0) * 0.5f + Vector3.up * 0.05f;
                Vector3 dir = pad0 - shore;
                Quaternion rot = Quaternion.FromToRotation(Vector3.right, dir.normalized);
                Place("Twig_A", prop, mid + Vector3.down * 0.2f, rot, dir.magnitude / 13f, true);
            }
            // 飛び石
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = pc + new Vector2(R(-9f, 9f), R(-9f, 9f));
                float s = R(0.7f, 1.1f);
                Place(Pick(Rocks), prop, new Vector3(p.x, ForestLayout.WaterLevel - 0.3f, p.y), Quaternion.Euler(0, R(0, 360), 0), s, true);
            }
        }

        void BuildWater()
        {
            int seg = 96;
            float r = ForestLayout.PondRadius * 1.3f;
            var v = new List<Vector3> { new Vector3(ForestLayout.Pond.x, ForestLayout.WaterLevel, ForestLayout.Pond.y) };
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(ForestLayout.Pond.x + Mathf.Cos(a) * r, ForestLayout.WaterLevel, ForestLayout.Pond.y + Mathf.Sin(a) * r));
            }
            for (int i = 0; i < seg; i++)
            {
                t.Add(0);
                t.Add(1 + (i + 1) % seg);
                t.Add(1 + i);
            }
            var mesh = new Mesh { name = "Water" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Water");
            go.layer = ShakuConst.WaterLayer;
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------
        // 草花（コライダーなし）
        // ------------------------------------------------------------------
        void BuildFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;

            // 草（草原は密、ほかはまばら）
            int grassCount = Mathf.RoundToInt(5200 * dens);
            for (int i = 0; i < grassCount; i++)
            {
                Vector2 p;
                float roll = R01();
                if (roll < 0.35f) p = RandomInCircle(ForestLayout.Meadow, 19f);
                else if (roll < 0.47f) p = RandomInCircle(Vector2.zero, 14f);
                else if (roll < 0.57f) p = RandomInCircle(ForestLayout.Pond, ForestLayout.PondRadius * 1.35f);
                else p = RandomInRing(3f, 72f);
                float patch = Mathf.PerlinNoise(p.x * 0.08f + 100f, p.y * 0.08f);
                bool meadow = Vector2.Distance(p, ForestLayout.Meadow) < 18f;
                if (!meadow && patch < 0.52f) continue;
                if (!IsLand(p, 0.12f) || InsideOccupied(p)) continue;
                if (ForestLayout.TrailMask(p.x, p.y) > 0.35f) continue;
                if (Vector2.Distance(p, ForestLayout.Spawn) < 2.5f) continue;
                float s = R(0.55f, 1.15f) * (meadow ? 1.05f : 0.85f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y);
                Place(Pick(Grass), fol, pos, GroundRotation(p, R(0, 360), 0.5f, 5f), s, false, false, 55f);
            }
            // 水辺の葦
            for (int i = 0; i < 16; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = ForestLayout.Pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(ForestLayout.PondRadius * 0.82f, ForestLayout.PondRadius * 1.05f);
                if (ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                Place("Reed", flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.2f, Quaternion.Euler(0, R(0, 360), 0), R(0.7f, 1.1f), false, true, 120f);
            }
            // クローバー
            int clover = Mathf.RoundToInt(1100 * dens);
            for (int i = 0; i < clover; i++)
            {
                Vector2 p = RandomInRing(2f, 66f);
                float patch = Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 40f);
                if (patch < 0.6f) continue;
                if (!IsLand(p, 0.15f) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.4f) continue;
                Place(R01() < 0.5f ? "Clover_A" : "Clover_B", fol, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.7f, 4f), R(0.7f, 1.2f), false, false, 50f);
            }
            // 苔のかたまり
            int moss = Mathf.RoundToInt(1400 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = R01() < 0.3f ? RandomInCircle(ForestLayout.MossHill, 16f) : RandomInRing(1f, 70f);
                float patch = Mathf.PerlinNoise(p.x * 0.07f + 500f, p.y * 0.07f + 80f);
                if (patch < 0.5f && Vector2.Distance(p, ForestLayout.MossHill) > 14f) continue;
                if (!IsLand(p, 0.1f) || ForestLayout.TrailMask(p.x, p.y) > 0.5f) continue;
                Place("Moss", fol, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 1f, 3f), R(0.5f, 1.4f), false, false, 60f);
            }
            // 落ち葉のかけら
            int litter = Mathf.RoundToInt(2400 * dens);
            for (int i = 0; i < litter; i++)
            {
                Vector2 p = R01() < 0.25f ? RandomInCircle(ForestLayout.AcornPlaza, 18f) : RandomInRing(1f, 72f);
                float patch = Mathf.PerlinNoise(p.x * 0.045f + 50f, p.y * 0.045f + 20f);
                if (patch < 0.5f && Vector2.Distance(p, ForestLayout.AcornPlaza) > 16f) continue;
                if (!IsLand(p, 0.05f)) continue;
                Place(Pick(BigLeaves), assets.prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, GroundRotation(p, R(0, 360), 1f, 6f), R(0.07f, 0.16f), false, false, 40f);
            }
            // 小さな小石
            int pebbles = Mathf.RoundToInt(600 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(1f, 70f);
                if (!IsLand(p, -0.5f)) continue;
                Place(Pick(Rocks), assets.prop, ForestLayout.Ground(p.x, p.y), Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), R(0.06f, 0.2f), false, false, 40f);
            }
            // 芽生え
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = i < 10 ? RandomInCircle(Vector2.zero, 9f) : RandomInRing(4f, 64f);
                if (!IsLand(p) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.4f) continue;
                if (Vector2.Distance(p, ForestLayout.Spawn) < 2.5f) continue;
                Place("Sprout", fol, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.2f), false, true, 70f);
            }
            // 花
            void Flowers(string name, Vector2 center, float radius, int count, float smin, float smax)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = RandomInCircle(center, radius);
                    if (!IsLand(p) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                    if (Vector2.Distance(p, ForestLayout.Spawn) < 3f) continue;
                    Place(name, flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.2f, 5f), R(smin, smax), false, true, 120f);
                }
            }
            Flowers("Daisy", ForestLayout.Meadow, 16f, 22, 0.8f, 1.2f);
            Flowers("Bellflower", ForestLayout.Meadow, 16f, 12, 0.8f, 1.2f);
            Flowers("Dandelion", ForestLayout.Meadow, 17f, 14, 0.8f, 1.2f);
            Flowers("DandelionPuff", ForestLayout.Meadow, 15f, 7, 0.9f, 1.2f);
            Flowers("Strawberry", ForestLayout.Meadow, 15f, 6, 0.9f, 1.3f);
            Flowers("Daisy", Vector2.zero, 60f, 14, 0.7f, 1.1f);
            Flowers("Dandelion", Vector2.zero, 60f, 8, 0.7f, 1.0f);
            Flowers("Bellflower", ForestLayout.MossHill, 14f, 6, 0.8f, 1.1f);
            Flowers("Strawberry", Vector2.zero, 50f, 5, 0.8f, 1.2f);
            Flowers("Daisy", Vector2.zero, 10f, 5, 0.7f, 1.0f);
            // シダ（外周と大樹のまわり）
            for (int i = 0; i < 60; i++)
            {
                Vector2 p;
                if (i < 38) p = RandomInRing(56f, 76f);
                else if (i < 50) p = RandomInCircle(ForestLayout.GreatTree + new Vector2(0f, -22f), 26f);
                else p = RandomInCircle(ForestLayout.LogCenter, 16f);
                if (!IsLand(p) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.2f) continue;
                if (p.magnitude < 50f && Vector2.Distance(p, ForestLayout.Spawn) < 12f) continue;
                Place(R01() < 0.5f ? "Fern_A" : "Fern_B", flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.3f), false, true, 200f);
            }
        }

        // ------------------------------------------------------------------
        // しずく
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
                if (ForestLayout.IsUnderwater(hit.point)) return;
                AddDew(hit.point);
            }
        }

        void PlaceDewdrops()
        {
            DewdropPoints.Clear();
            // はじまりの場所（チュートリアル）
            AddDewFromAbove(ForestLayout.Spawn + new Vector2(0.5f, 4.5f));
            AddDewFromAbove(ForestLayout.Spawn + new Vector2(-4.5f, 1.5f));
            AddDewFromAbove(ForestLayout.Spawn + new Vector2(3.5f, -4.5f));
            // 切り株のてっぺん
            AddDewFromAbove(ForestLayout.Stump);
            AddDewFromAbove(ForestLayout.Stump + new Vector2(4.2f, -2.5f));
            // 丸太の中
            for (int i = -1; i <= 1; i += 2)
            {
                Vector2 c = ForestLayout.LogCenter + ForestLayout.LogDir * (i * 11f);
                Vector3 inside = ForestLayout.Ground(c.x, c.y) + Vector3.up * 3.4f;
                if (CastDown(inside, 6f, out var hit)) AddDew(hit.point);
            }
            // 丸太の上
            AddDewFromAbove(ForestLayout.LogCenter + ForestLayout.LogDir * 4f);
            // 葉っぱの舟
            if (_lilyPads.Count > 5)
            {
                AddDewFromAbove(new Vector2(_lilyPads[4].x, _lilyPads[4].z));
                AddDewFromAbove(new Vector2(_lilyPads[7].x, _lilyPads[7].z));
            }
            // どんぐりの帽子の中
            if (_specialCap != Vector3.zero)
            {
                if (CastDown(_specialCap + Vector3.up * 3f, 5f, out var hit)) DewdropPoints.Add(hit.point);
            }
            // 赤キノコのかさの上
            _redCaps.Sort((a, b) => b.scale.CompareTo(a.scale));
            for (int i = 0; i < Mathf.Min(3, _redCaps.Count); i++)
                AddDewFromAbove(new Vector2(_redCaps[i].pos.x + 0.6f, _redCaps[i].pos.z + 0.4f));
            // 大樹の根の上
            int roots = 0;
            for (int k = 0; k < 80 && roots < 4; k++)
            {
                float a = R(Mathf.PI * 1.05f, Mathf.PI * 1.95f);
                Vector2 p = ForestLayout.GreatTree + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(20f, 36f);
                if (p.magnitude > ForestLayout.PlayRadius - 3f) continue;
                if (CastDown(new Vector3(p.x, 150f, p.y), 200f, out var hit) && hit.point.y > ForestLayout.Height(p.x, p.y) + 1.5f)
                {
                    int before = DewdropPoints.Count;
                    AddDew(hit.point);
                    if (DewdropPoints.Count > before) roots++;
                }
            }
            // 光るキノコの洞
            AddDewFromAbove(ForestLayout.ArchTarget + new Vector2(3f, 4f), 5f + ForestLayout.Height(ForestLayout.ArchTarget.x, ForestLayout.ArchTarget.y));
            AddDewFromAbove(ForestLayout.ArchTarget + new Vector2(-1f, -1f), 5f + ForestLayout.Height(ForestLayout.ArchTarget.x, ForestLayout.ArchTarget.y));
            // 大きな岩の上
            for (int i = 0; i < Mathf.Min(5, _bigRocks.Count); i++)
                AddDewFromAbove(new Vector2(_bigRocks[i].pos.x, _bigRocks[i].pos.z));
            // 小枝・落ち葉の上
            for (int i = 0; i < Mathf.Min(2, _twigs.Count); i++)
                AddDewFromAbove(new Vector2(_twigs[i].x, _twigs[i].z));
            for (int i = 0; i < Mathf.Min(4, _bigLeaves.Count); i++)
                AddDewFromAbove(new Vector2(_bigLeaves[i * 7 % _bigLeaves.Count].x, _bigLeaves[i * 7 % _bigLeaves.Count].z));
            // 草原
            AddDewFromAbove(ForestLayout.Meadow + new Vector2(2f, 1f));
            AddDewFromAbove(ForestLayout.Meadow + new Vector2(-6f, 5f));
            AddDewFromAbove(ForestLayout.Meadow + new Vector2(5f, -7f));
            // 残りは方角ごとに均等に散らす
            int guard = 0;
            int target = 45;
            while (DewdropPoints.Count < target && guard++ < 2000)
            {
                int sector = DewdropPoints.Count % 8;
                float a = (sector + R01()) / 8f * Mathf.PI * 2f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(6f, 60f);
                if (!IsLand(p, 0.2f)) continue;
                AddDewFromAbove(p);
            }
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

        void BuildLightShafts()
        {
            if (assets.lightShaft == null) return;
            Vector3 dir = sun != null ? sun.transform.forward : new Vector3(0.3f, -0.85f, 0.4f).normalized;
            Vector2[] spots =
            {
                new Vector2(2f, 3f), ForestLayout.Meadow, ForestLayout.Meadow + new Vector2(-8f, 6f), ForestLayout.Pond + new Vector2(2f, 2f),
                ForestLayout.Stump + new Vector2(-2f, 2f), ForestLayout.MushroomGrove + new Vector2(-3f, 2f), ForestLayout.AcornPlaza,
                new Vector2(-20f, -8f), new Vector2(18f, 22f),
            };
            var parent = new GameObject("LightShafts").transform;
            parent.SetParent(Root, false);
            foreach (var s in spots)
            {
                Vector3 ground = ForestLayout.Ground(s.x, s.y);
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
            Vector2 s = ForestLayout.Spawn;
            if (CastDown(new Vector3(s.x, 50f, s.y), 100f, out var hit)) SpawnPoint = hit.point;
            else SpawnPoint = ForestLayout.Ground(s.x, s.y);
            SpawnForward = Vector3.forward;
        }

        // ------------------------------------------------------------------
        // ミニマップ
        // ------------------------------------------------------------------
        void BuildMap()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ForestMap" };
            var px = new Color32[size * size];
            Vector3 light = new Vector3(-0.5f, 0.75f, 0.45f).normalized;
            ColorUtility.TryParseHtmlString("#5aa9d6", out var water);
            ColorUtility.TryParseHtmlString("#6b4a32", out var bark);
            ColorUtility.TryParseHtmlString("#c9a46a", out var wood);
            Vector2 la = ForestLayout.LogCenter - ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            Vector2 lb = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = Mathf.Lerp(-MapExtent, MapExtent, (i + 0.5f) / size);
                float z = Mathf.Lerp(-MapExtent, MapExtent, (j + 0.5f) / size);
                float h = ForestLayout.Height(x, z);
                Vector3 n = ForestLayout.Normal(x, z);
                Color c = ForestLayout.GroundColor(x, z, h, n);
                float shade = Mathf.Clamp01(Vector3.Dot(n, light)) * 0.35f + 0.75f;
                c *= shade;
                if (h < ForestLayout.WaterLevel) c = Color.Lerp(water, water * 0.7f, Mathf.Clamp01((ForestLayout.WaterLevel - h) / 2f));
                Vector2 p = new Vector2(x, z);
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 16f) c = bark;
                if (Vector2.Distance(p, ForestLayout.Stump) < 8f) c = Vector2.Distance(p, ForestLayout.Stump) < 6.8f ? wood : bark;
                if (ShakuMath.DistToSegment(p, la, lb) < 5f) c = bark * 1.15f;
                float r = p.magnitude;
                if (r > ForestLayout.PlayRadius) c = Color.Lerp(c, c * 0.45f, ShakuMath.SmoothStep(ForestLayout.PlayRadius, ForestLayout.PlayRadius + 4f, r));
                // ふちのビネット
                c.a = 1f;
                px[j * size + i] = c;
            }
            foreach (var (pos, s) in _redCaps)
            {
                int ci = Mathf.RoundToInt((pos.x + MapExtent) / (2f * MapExtent) * size);
                int cj = Mathf.RoundToInt((pos.z + MapExtent) / (2f * MapExtent) * size);
                int rad = Mathf.Max(2, Mathf.RoundToInt(2.5f * s));
                for (int dj = -rad; dj <= rad; dj++)
                for (int di = -rad; di <= rad; di++)
                {
                    if (di * di + dj * dj > rad * rad) continue;
                    int ii = ci + di, jj = cj + dj;
                    if (ii < 0 || jj < 0 || ii >= size || jj >= size) continue;
                    px[jj * size + ii] = new Color32(230, 70, 60, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            MapTexture = tex;
        }
    }
}

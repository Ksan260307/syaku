using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Shakutori
{
    /// <summary>
    /// たくさんの小物・草花を GPU インスタンシングでまとめて描画する。
    /// 空間をセルに分けて、視野と距離で大まかに選び、見える物は 1 つずつ数えなおす：
    /// 画面に入らない物・シェーダーで完全に消える遠くの物は描かず、影だけを落とす物は影の絵にだけ描く。
    /// 近い物から順に描く（手前の物で奥が隠れるので、GPU が奥の色を計算しなくてすむ）。
    /// </summary>
    [ExecuteAlways]
    public class InstancedRenderer : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Instanced = new Unity.Profiling.ProfilerMarker("Shaku.Instanced");
        const float CellSize = 16f;
        const int MaxPerCall = 1023;
        /// <summary>影の届く距離より、これだけ遠い物までは影を描く（低い太陽の長い影の分）。</summary>
        public const float ShadowMargin = 12f;
        /// <summary>
        /// 三角形の少ない形（メッシュの LOD）に切りかえる、「距離 ÷ 物の半径」。画面での大きさがこれより小さくなると 1 段ずつ。
        /// 12 倍で、物の直径がおよそ 130 ピクセル（720p）。細かい形とのずれは 1 ピクセルに満たない。
        /// </summary>
        public static readonly float[] LodRatio = { 12f, 25f, 50f };

        /// <summary>距離 d・半径 r の物に使う形の段（0 = いちばん細かい）。</summary>
        public static int LodFor(float d, float r, int lodCount)
        {
            float k = d / Mathf.Max(r, 1e-3f);
            int lod = 0;
            while (lod < LodRatio.Length && k >= LodRatio[lod]) lod++;
            return Mathf.Min(lod, Mathf.Max(0, lodCount - 1));
        }

        class Cell
        {
            public Bounds bounds;
            public Vector3 center;
            public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
            public Vector3[] positions;   // 1 つずつの位置と、包む球の半径（Build で計算）
            public float[] radii;
        }

        class Batch
        {
            public Mesh mesh;
            public Material material;
            public bool castShadows;
            public float maxDistance;
            public float fadeEnd = float.MaxValue;   // シェーダーで完全に消える距離（草花の遠くのフェード）
            public readonly List<Matrix4x4> pending = new List<Matrix4x4>();
            public readonly Dictionary<long, Cell> cellMap = new Dictionary<long, Cell>();
            public Cell[] cells;
            public float meshRadius;
        }

        struct Item { public float d; public Matrix4x4 m; public Vector3 p; public float r; }

        sealed class NearFirst : IComparer<Item>
        {
            public static readonly NearFirst Instance = new NearFirst();
            public int Compare(Item a, Item b) => a.d.CompareTo(b.d);
        }

        readonly Dictionary<(Mesh, Material, bool), Batch> _batches = new Dictionary<(Mesh, Material, bool), Batch>();
        readonly List<Batch> _list = new List<Batch>();
        // RenderMeshInstanced はフレームの後半で描画されるので、呼び出しごとに別の配列を使う
        readonly List<Matrix4x4[]> _pool = new List<Matrix4x4[]>();
        int _poolIndex;
        readonly Plane[] _planes = new Plane[6];
        // 数えなおしのときの入れ物（毎回使いまわす）
        readonly List<Item> _lit = new List<Item>(), _unshadowed = new List<Item>(), _shadowOnly = new List<Item>();
        public float distanceScale = 1f;
        public int InstanceCount { get; private set; }

        // 前のフレームの描画の呼び出し（カメラがほとんど動いていなければ、そのまま使う）
        struct Call { public Batch batch; public Matrix4x4[] buffer; public int count; public Bounds bounds; public ShadowCastingMode mode; public int lod; }
        readonly List<Call> _calls = new List<Call>();
        Vector3 _builtPos = new Vector3(float.MaxValue, 0f, 0f);
        Quaternion _builtRot = Quaternion.identity;
        float _builtFov, _builtScale = -1f, _builtShadow = -1f;
        int _builtFrames;
        bool _dirty = true;

        /// <summary>カメラがこれより動いたら、見える物を数えなおす。</summary>
        public const float RebuildMove = 0.6f, RebuildTurn = 4f;
        /// <summary>見える物を数えるときは、視野を少し広めにとる（数えなおすまでのあいだ、はしの物が消えないように）。</summary>
        public const float FovMargin = 12f;

        /// <summary>数えなおした回数（テスト用）。</summary>
        public int RebuildCount { get; private set; }
        /// <summary>直前に数えなおしたときの、描く数（テスト用）：画面に描く物・そのうち影も落とす物・影だけの物。</summary>
        public int DrawnCount { get; private set; }
        public int ShadowedCount { get; private set; }
        public int ShadowOnlyCount { get; private set; }
        /// <summary>三角形の少ない形で描いた数（テスト用）。</summary>
        public int DetailReducedCount { get; private set; }

        public void Clear()
        {
            _batches.Clear();
            _list.Clear();
            _calls.Clear();
            _dirty = true;
            InstanceCount = 0;
        }

        /// <summary>
        /// まだセルに振り分けていない物を、置いた順に 1 つずつ見なおす（置き方の仕上げ）。
        /// 新しい行列を返すと置きなおし、null を返すと取りのぞく。取りのぞいた数を返す。
        /// </summary>
        public int Edit(System.Func<Mesh, Material, Matrix4x4, Matrix4x4?> f)
        {
            int removed = 0;
            var kept = new List<Matrix4x4>();
            foreach (var b in _list)
            {
                kept.Clear();
                foreach (var m in b.pending)
                {
                    var r = f(b.mesh, b.material, m);
                    if (r.HasValue) kept.Add(r.Value);
                    else removed++;
                }
                b.pending.Clear();
                b.pending.AddRange(kept);
            }
            InstanceCount -= removed;
            _dirty = true;
            return removed;
        }

        /// <summary>置いた物を 1 つずつ（テストや、置き方の点検用）。</summary>
        public IEnumerable<(Mesh mesh, Material material, Matrix4x4 matrix)> Instances()
        {
            foreach (var b in _list)
            {
                foreach (var m in b.pending) yield return (b.mesh, b.material, m);
                if (b.cells == null) continue;
                foreach (var c in b.cells)
                    foreach (var m in c.matrices) yield return (b.mesh, b.material, m);
            }
        }

        public void Add(Mesh mesh, Material material, Matrix4x4 matrix, bool castShadows, float maxDistance)
        {
            if (mesh == null || material == null) return;
            var key = (mesh, material, castShadows);
            if (!_batches.TryGetValue(key, out var b))
            {
                b = new Batch { mesh = mesh, material = material, castShadows = castShadows, maxDistance = maxDistance };
                b.meshRadius = mesh.bounds.extents.magnitude + mesh.bounds.center.magnitude;
                if (material.HasProperty("_FarFadeEnd")) b.fadeEnd = material.GetFloat("_FarFadeEnd");
                _batches[key] = b;
                _list.Add(b);
            }
            b.maxDistance = Mathf.Max(b.maxDistance, maxDistance);
            b.pending.Add(matrix);
            InstanceCount++;
            _dirty = true;
        }

        /// <summary>追加済みのインスタンスをセルに振り分ける。</summary>
        public void Build()
        {
            foreach (var b in _list)
            {
                foreach (var m in b.pending)
                {
                    Vector3 pos = m.GetColumn(3);
                    int cx = Mathf.FloorToInt(pos.x / CellSize);
                    int cz = Mathf.FloorToInt(pos.z / CellSize);
                    long key = ((long)cx << 32) ^ (uint)cz;
                    if (!b.cellMap.TryGetValue(key, out var cell))
                    {
                        cell = new Cell();
                        b.cellMap[key] = cell;
                    }
                    float scale = m.lossyScale.x;
                    float r = b.meshRadius * Mathf.Max(scale, 0.01f);
                    var bb = new Bounds(pos, Vector3.one * r * 2f);
                    if (cell.matrices.Count == 0) cell.bounds = bb;
                    else cell.bounds.Encapsulate(bb);
                    cell.matrices.Add(m);
                }
                b.pending.Clear();
                _dirty = true;
                b.cells = new Cell[b.cellMap.Count];
                b.cellMap.Values.CopyTo(b.cells, 0);
                foreach (var c in b.cells)
                {
                    c.center = c.bounds.center;
                    int n = c.matrices.Count;
                    c.positions = new Vector3[n];
                    c.radii = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        var m = c.matrices[i];
                        c.positions[i] = m.GetColumn(3);
                        c.radii[i] = b.meshRadius * Mathf.Max(m.lossyScale.x, 0.01f);
                    }
                }
            }
        }

        /// <summary>いまの画質で、影が届く距離（影がなければ 0）。</summary>
        public static float ShadowDistance()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return urp != null && urp.supportsMainLightShadows ? urp.shadowDistance : 0f;
        }

        static bool SphereInView(Plane[] planes, Vector3 p, float r)
        {
            for (int i = 0; i < 6; i++)
                if (planes[i].GetDistanceToPoint(p) < -r) return false;
            return true;
        }

        void Update()
        {
            using var prof = s_Instanced.Auto();   // 処理時間の計測（パフォーマンスの調整用）
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            Render(cam);
        }

        /// <summary>カメラ cam から見える物を描く（カメラがほとんど動いていなければ、前のフレームの描画をくり返す）。</summary>
        public void Render(Camera cam)
        {
            if (cam == null || _list.Count == 0) return;
            // カメラがほとんど動いていなければ、前のフレームの描画をそのままくり返す（セルを数えなおさない）
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            float shadowDist = ShadowDistance();
            bool still = !_dirty && Application.isPlaying && (camPos - _builtPos).sqrMagnitude < RebuildMove * RebuildMove
                         && Quaternion.Angle(camRot, _builtRot) < RebuildTurn && Mathf.Abs(cam.fieldOfView - _builtFov) < 1f
                         && Mathf.Approximately(distanceScale, _builtScale) && Mathf.Approximately(shadowDist, _builtShadow)
                         && _builtFrames < 120;
            if (still)
            {
                _builtFrames++;
                foreach (var c in _calls) Issue(c);
                return;
            }
            _dirty = false;
            _builtPos = camPos;
            _builtRot = camRot;
            _builtFov = cam.fieldOfView;
            _builtScale = distanceScale;
            _builtShadow = shadowDist;
            _builtFrames = 0;
            RebuildCount++;
            _calls.Clear();
            _poolIndex = 0;
            DrawnCount = ShadowedCount = ShadowOnlyCount = DetailReducedCount = 0;
            // 数えなおすまでのあいだ、はしの物が消えないように、少し広い視野で数える
            var wide = Matrix4x4.Perspective(Mathf.Min(170f, cam.fieldOfView + FovMargin), cam.aspect, cam.nearClipPlane, cam.farClipPlane) * cam.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(Application.isPlaying ? wide : cam.projectionMatrix * cam.worldToCameraMatrix, _planes);
            // 数えなおすまでに、カメラがこれだけ動いても大丈夫なように、1 つずつの判定は少し大きめに見る
            float slack = Application.isPlaying ? RebuildMove : 0f;
            float shadowCut = shadowDist + ShadowMargin;
            foreach (var b in _list)
            {
                if (b.cells == null) continue;
                float maxD = b.maxDistance * distanceScale;
                _lit.Clear();
                _unshadowed.Clear();
                _shadowOnly.Clear();
                foreach (var c in b.cells)
                {
                    float dc = Vector3.Distance(camPos, c.center) - c.bounds.extents.magnitude;
                    if (dc > maxD) continue;
                    bool cellInView = GeometryUtility.TestPlanesAABB(_planes, c.bounds);
                    // 影が視野外から落ちることがあるので、影を落とすものは少し広めに
                    bool shadowCell = b.castShadows && (cellInView || dc <= 30f);
                    if (!cellInView && !shadowCell) continue;
                    var pos = c.positions;
                    var rad = c.radii;
                    for (int i = 0; i < pos.Length; i++)
                    {
                        float r = rad[i];
                        float d = Vector3.Distance(camPos, pos[i]);
                        // 画面に描く：視野に入り、シェーダーで消えてしまう距離より近い
                        bool seen = cellInView && d - r - slack <= b.fadeEnd && SphereInView(_planes, pos[i], r + slack);
                        // 影を落とす：影の届く距離の中
                        bool casts = shadowCell && d - r - slack <= shadowCut;
                        if (!seen && !casts) continue;
                        var item = new Item { d = d, m = c.matrices[i], p = pos[i], r = r };
                        if (seen && casts) _lit.Add(item);
                        else if (seen) _unshadowed.Add(item);
                        else _shadowOnly.Add(item);
                    }
                }
                DrawnCount += _lit.Count + _unshadowed.Count;
                ShadowedCount += _lit.Count;
                ShadowOnlyCount += _shadowOnly.Count;
                Emit(b, _lit, ShadowCastingMode.On);
                Emit(b, _unshadowed, ShadowCastingMode.Off);
                Emit(b, _shadowOnly, ShadowCastingMode.ShadowsOnly);
            }
        }

        Matrix4x4[] NextBuffer()
        {
            if (_poolIndex >= _pool.Count) _pool.Add(new Matrix4x4[MaxPerCall]);
            return _pool[_poolIndex++];
        }

        /// <summary>
        /// 近い物から順に並べて、MaxPerCall ずつの描画にする（それぞれ、その中の物だけを包む範囲で）。
        /// 三角形の少ない形を持つメッシュは、遠くの物ほど少ない形で描く
        /// （段はメッシュの大きさと距離で決めるので、距離の順に並べると、段ごとにひと続きになる）。
        /// </summary>
        void Emit(Batch b, List<Item> items, ShadowCastingMode mode)
        {
            if (items.Count == 0) return;
            items.Sort(NearFirst.Instance);
            int lods = b.mesh.lodCount;
            int start = 0;
            while (start < items.Count)
            {
                int lod = lods > 1 ? LodFor(items[start].d, b.meshRadius, lods) : 0;
                int end = start + 1;
                while (end < items.Count && end - start < MaxPerCall
                       && (lods <= 1 || LodFor(items[end].d, b.meshRadius, lods) == lod)) end++;
                EmitRange(b, items, start, end - start, mode, lods > 1 ? lod : -1);
                start = end;
            }
        }

        void EmitRange(Batch b, List<Item> items, int start, int count, ShadowCastingMode mode, int lod)
        {
            var buffer = NextBuffer();
            var first = items[start];
            var bounds = new Bounds(first.p, Vector3.one * first.r * 2f);
            for (int k = 0; k < count; k++)
            {
                var it = items[start + k];
                buffer[k] = it.m;
                bounds.Encapsulate(new Bounds(it.p, Vector3.one * it.r * 2f));
            }
            var c = new Call { batch = b, buffer = buffer, count = count, bounds = bounds, mode = mode, lod = lod };
            _calls.Add(c);
            Issue(c);
            if (lod > 0) DetailReducedCount += count;
        }

        static void Issue(Call c)
        {
            var rp = new RenderParams(c.batch.material)
            {
                shadowCastingMode = c.mode,
                receiveShadows = true,
                layer = 0,
                worldBounds = c.bounds,
            };
            Graphics.RenderMeshInstanced(rp, DetailMeshes.Get(c.batch.mesh, c.lod), 0, c.buffer, c.count);
        }
    }
}

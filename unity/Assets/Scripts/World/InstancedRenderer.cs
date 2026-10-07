using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// たくさんの小物・草花を GPU インスタンシングでまとめて描画する。
    /// 空間をセルに分けて視錐台カリングと距離カリングを行う。
    /// </summary>
    [ExecuteAlways]
    public class InstancedRenderer : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Instanced = new Unity.Profiling.ProfilerMarker("Shaku.Instanced");
        const float CellSize = 16f;
        const int MaxPerCall = 1023;

        class Cell
        {
            public Bounds bounds;
            public Vector3 center;
            public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        }

        class Batch
        {
            public Mesh mesh;
            public Material material;
            public bool castShadows;
            public float maxDistance;
            public readonly List<Matrix4x4> pending = new List<Matrix4x4>();
            public readonly Dictionary<long, Cell> cellMap = new Dictionary<long, Cell>();
            public Cell[] cells;
            public float meshRadius;
        }

        readonly Dictionary<(Mesh, Material, bool), Batch> _batches = new Dictionary<(Mesh, Material, bool), Batch>();
        readonly List<Batch> _list = new List<Batch>();
        // RenderMeshInstanced はフレームの後半で描画されるので、呼び出しごとに別の配列を使う
        readonly List<Matrix4x4[]> _pool = new List<Matrix4x4[]>();
        int _poolIndex;
        Matrix4x4[] _buffer;
        readonly Plane[] _planes = new Plane[6];
        public float distanceScale = 1f;
        public int InstanceCount { get; private set; }

        // 前のフレームの描画の呼び出し（カメラがほとんど動いていなければ、そのまま使う）
        struct Call { public Batch batch; public Matrix4x4[] buffer; public int count; public Bounds bounds; }
        readonly List<Call> _calls = new List<Call>();
        Vector3 _builtPos = new Vector3(float.MaxValue, 0f, 0f);
        Quaternion _builtRot = Quaternion.identity;
        float _builtFov, _builtScale = -1f;
        int _builtFrames;
        bool _dirty = true;

        /// <summary>カメラがこれより動いたら、見える物を数えなおす。</summary>
        public const float RebuildMove = 0.6f, RebuildTurn = 4f;
        /// <summary>見える物を数えるときは、視野を少し広めにとる（数えなおすまでのあいだ、はしの物が消えないように）。</summary>
        public const float FovMargin = 12f;

        /// <summary>数えなおした回数（テスト用）。</summary>
        public int RebuildCount { get; private set; }

        public void Clear()
        {
            _batches.Clear();
            _list.Clear();
            _calls.Clear();
            _dirty = true;
            InstanceCount = 0;
        }

        public void Add(Mesh mesh, Material material, Matrix4x4 matrix, bool castShadows, float maxDistance)
        {
            if (mesh == null || material == null) return;
            var key = (mesh, material, castShadows);
            if (!_batches.TryGetValue(key, out var b))
            {
                b = new Batch { mesh = mesh, material = material, castShadows = castShadows, maxDistance = maxDistance };
                b.meshRadius = mesh.bounds.extents.magnitude + mesh.bounds.center.magnitude;
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
                foreach (var c in b.cells) c.center = c.bounds.center;
            }
        }

        void Update()
        {
            using var prof = s_Instanced.Auto();   // 処理時間の計測（パフォーマンスの調整用）
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (cam == null || _list.Count == 0) return;
            // カメラがほとんど動いていなければ、前のフレームの描画をそのままくり返す（セルを数えなおさない）
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            bool still = !_dirty && Application.isPlaying && (camPos - _builtPos).sqrMagnitude < RebuildMove * RebuildMove
                         && Quaternion.Angle(camRot, _builtRot) < RebuildTurn && Mathf.Abs(cam.fieldOfView - _builtFov) < 1f
                         && Mathf.Approximately(distanceScale, _builtScale) && _builtFrames < 120;
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
            _builtFrames = 0;
            RebuildCount++;
            _calls.Clear();
            _poolIndex = 0;
            _buffer = NextBuffer();
            // 数えなおすまでのあいだ、はしの物が消えないように、少し広い視野で数える
            var wide = Matrix4x4.Perspective(Mathf.Min(170f, cam.fieldOfView + FovMargin), cam.aspect, cam.nearClipPlane, cam.farClipPlane) * cam.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(Application.isPlaying ? wide : cam.projectionMatrix * cam.worldToCameraMatrix, _planes);
            // 影が視野外から落ちることがあるので、影を落とすものは少し広めに
            foreach (var b in _list)
            {
                if (b.cells == null) continue;
                float maxD = b.maxDistance * distanceScale;
                int count = 0;
                Bounds total = new Bounds();
                bool any = false;
                foreach (var c in b.cells)
                {
                    float d = Vector3.Distance(camPos, c.center) - c.bounds.extents.magnitude;
                    if (d > maxD) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, c.bounds))
                    {
                        if (!b.castShadows || d > 30f) continue;
                    }
                    if (!any) { total = c.bounds; any = true; }
                    else total.Encapsulate(c.bounds);
                    var list = c.matrices;
                    for (int i = 0; i < list.Count; i++)
                    {
                        _buffer[count++] = list[i];
                        if (count == MaxPerCall)
                        {
                            Draw(b, count, total);
                            count = 0;
                        }
                    }
                }
                if (count > 0) Draw(b, count, total);
            }
        }

        Matrix4x4[] NextBuffer()
        {
            if (_poolIndex >= _pool.Count) _pool.Add(new Matrix4x4[MaxPerCall]);
            return _pool[_poolIndex++];
        }

        void Draw(Batch b, int count, Bounds bounds)
        {
            var c = new Call { batch = b, buffer = _buffer, count = count, bounds = bounds };
            _calls.Add(c);
            Issue(c);
            _buffer = NextBuffer();
        }

        static void Issue(Call c)
        {
            var rp = new RenderParams(c.batch.material)
            {
                shadowCastingMode = c.batch.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                layer = 0,
                worldBounds = c.bounds,
            };
            Graphics.RenderMeshInstanced(rp, c.batch.mesh, 0, c.buffer, c.count);
        }
    }
}

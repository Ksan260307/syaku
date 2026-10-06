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

        public void Clear()
        {
            _batches.Clear();
            _list.Clear();
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
                b.cells = new Cell[b.cellMap.Count];
                b.cellMap.Values.CopyTo(b.cells, 0);
                foreach (var c in b.cells) c.center = c.bounds.center;
            }
        }

        void Update()
        {
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (cam == null || _list.Count == 0) return;
            _poolIndex = 0;
            _buffer = NextBuffer();
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            // 影が視野外から落ちることがあるので、影を落とすものは少し広めに
            Vector3 camPos = cam.transform.position;
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
            var rp = new RenderParams(b.material)
            {
                shadowCastingMode = b.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                layer = 0,
                worldBounds = bounds,
            };
            Graphics.RenderMeshInstanced(rp, b.mesh, 0, _buffer, count);
            _buffer = NextBuffer();
        }
    }
}

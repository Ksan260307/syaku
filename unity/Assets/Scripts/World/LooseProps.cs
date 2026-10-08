using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 押すと動く小さな物（落ち葉・小石・松ぼっくり・どんぐりのぼうし）。
    /// ふだんは絵だけ（まとめて描く）。しゃくとりむしが近づくと、そのまわりの物だけが当たり判定のある体を持ち、
    /// どんぐりと同じように、押されると転がる・すべる。はなれて止まったら、動いた先のまま、また絵だけにもどる
    /// （体を持つ物の数を少なくおさえる）。
    /// </summary>
    [ExecuteAlways]
    public class LooseProps : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Loose = new Unity.Profiling.ProfilerMarker("Shaku.Loose");

        public enum Shape { Leaf, Pebble, Pinecone, Cap }

        /// <summary>これより近い物は、体を持つ（押せる）。</summary>
        public const float WakeRadius = 5f;
        /// <summary>これより遠くで止まっている物は、絵だけにもどる。</summary>
        public const float SleepRadius = 9f;
        /// <summary>同時に体を持つ物の数の上限。</summary>
        public const int MaxBodies = 160;
        const float CellSize = 8f;
        const int MaxPerCall = 1023;

        class Batch
        {
            public Mesh mesh;
            public Material material;
            public bool shadows;
            public float maxDistance;
            public float meshRadius;
            public Shape shape;
            public readonly List<Matrix4x4>[] lists = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            public readonly Bounds[] bounds = new Bounds[4];
        }

        struct Item
        {
            public int batch;
            public Vector3 pos, home;
            public Quaternion rot, homeRot;
            public float scale;
            public Matrix4x4 m;
            public int body;      // 体（なければ -1）
            public long cell;
        }

        readonly List<Batch> _batches = new List<Batch>();
        readonly Dictionary<(Mesh, Material, bool, Shape), int> _batchIndex = new Dictionary<(Mesh, Material, bool, Shape), int>();
        Item[] _items = new Item[256];
        int _count;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        readonly Dictionary<long, Bounds> _cellBounds = new Dictionary<long, Bounds>();
        // 体
        readonly List<LooseBody> _bodies = new List<LooseBody>();
        readonly Stack<int> _freeBodies = new Stack<int>();
        readonly List<int> _awake = new List<int>();   // 体を持っている物（item の番号）
        Transform _bodyRoot;
        // 描く
        readonly List<Matrix4x4[]> _pool = new List<Matrix4x4[]>();
        readonly Plane[] _planes = new Plane[6];
        Vector3 _builtPos = new Vector3(float.MaxValue, 0f, 0f);
        Quaternion _builtRot;
        int _builtFrames;
        bool _dirty = true;
        struct Call { public Batch batch; public Mesh mesh; public Matrix4x4[] buffer; public int count; public Bounds bounds; }
        readonly List<Call> _calls = new List<Call>();
        public float distanceScale = 1f;

        public int Count => _count;
        public int BodyCount => _awake.Count;
        /// <summary>直前に数えなおしたときに描いた数（テスト用）。</summary>
        public int DrawnCount { get; private set; }

        // ------------------------------------------------------------------
        // 置く
        // ------------------------------------------------------------------
        public void Clear()
        {
            ReleaseAllBodies();
            _batches.Clear();
            _batchIndex.Clear();
            _count = 0;
            _cells.Clear();
            _cellBounds.Clear();
            _calls.Clear();
            _dirty = true;
        }

        public void Add(Mesh mesh, Material material, Vector3 pos, Quaternion rot, float scale, Shape shape, bool shadows, float maxDistance)
        {
            if (mesh == null || material == null) return;
            var key = (mesh, material, shadows, shape);
            if (!_batchIndex.TryGetValue(key, out int b))
            {
                b = _batches.Count;
                _batches.Add(new Batch
                {
                    mesh = mesh, material = material, shadows = shadows, maxDistance = maxDistance, shape = shape,
                    meshRadius = mesh.bounds.extents.magnitude + mesh.bounds.center.magnitude,
                });
                _batchIndex[key] = b;
            }
            _batches[b].maxDistance = Mathf.Max(_batches[b].maxDistance, maxDistance);
            if (_count == _items.Length) System.Array.Resize(ref _items, _items.Length * 2);
            var it = new Item { batch = b, pos = pos, home = pos, rot = rot, homeRot = rot, scale = scale, body = -1 };
            it.m = Matrix4x4.TRS(pos, rot, Vector3.one * scale);
            it.cell = CellOf(pos);
            _items[_count] = it;
            AddToCell(_count, it);
            _count++;
            _dirty = true;
        }

        static long CellOf(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            return ((long)cx << 32) ^ (uint)cz;
        }

        float RadiusOf(in Item it) => _batches[it.batch].meshRadius * it.scale;

        void AddToCell(int i, in Item it)
        {
            if (!_cells.TryGetValue(it.cell, out var list)) _cells[it.cell] = list = new List<int>();
            list.Add(i);
            var bb = new Bounds(it.pos, Vector3.one * (RadiusOf(it) * 2f + 0.5f));
            if (_cellBounds.TryGetValue(it.cell, out var cb)) { cb.Encapsulate(bb); _cellBounds[it.cell] = cb; }
            else _cellBounds[it.cell] = bb;
        }

        void MoveToCell(int i)
        {
            ref var it = ref _items[i];
            long c = CellOf(it.pos);
            if (c == it.cell)
            {
                // 同じますでも、範囲は広げておく
                var cb = _cellBounds[c];
                cb.Encapsulate(new Bounds(it.pos, Vector3.one * (RadiusOf(it) * 2f + 0.5f)));
                _cellBounds[c] = cb;
                return;
            }
            if (_cells.TryGetValue(it.cell, out var old)) old.Remove(i);
            it.cell = c;
            AddToCell(i, it);
        }

        /// <summary>その場所に、いちばん近い物（テスト用）。メッシュ名で選べる。</summary>
        public int Nearest(Vector3 p, string meshName = null)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                if (meshName != null && _batches[_items[i].batch].mesh.name != meshName) continue;
                float d = (_items[i].pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public Vector3 PositionOf(int i) => _items[i].pos;
        public Vector3 HomeOf(int i) => _items[i].home;
        public bool HasBody(int i) => _items[i].body >= 0;
        public string MeshNameOf(int i) => _batches[_items[i].batch].mesh.name;

        // ------------------------------------------------------------------
        // 体（しゃくとりむしのまわりだけ）
        // ------------------------------------------------------------------
        void FixedUpdate()
        {
            if (!Application.isPlaying || _count == 0) return;
            using var prof = s_Loose.Auto();
            var worm = InchwormController.Instance;
            if (worm == null) return;
            Vector3 p = worm.CenterPosition;
            // 近くの物に体を持たせる
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                if (!_cells.TryGetValue(key, out var list)) continue;
                for (int k = 0; k < list.Count && _awake.Count < MaxBodies; k++)
                {
                    int i = list[k];
                    if (_items[i].body >= 0) continue;
                    if ((_items[i].pos - p).sqrMagnitude > (WakeRadius + RadiusOf(_items[i])) * (WakeRadius + RadiusOf(_items[i]))) continue;
                    Wake(i);
                }
            }
            // 体を持つ物：動いた分を絵にうつす。はなれて止まったら、絵だけにもどる
            var area = Areas.Current;
            for (int k = _awake.Count - 1; k >= 0; k--)
            {
                int i = _awake[k];
                ref var it = ref _items[i];
                var body = _bodies[it.body];
                var t = body.transform;
                Vector3 bp = t.position;
                bool lost = !area.InPlayArea(bp) || bp.y < area.Height(bp.x, bp.z) - 2f || !ShakuPhysics.IsFinite(bp);
                if (lost)
                {
                    // 遊べる場所の外・地面の下へ行ってしまったら、もとの場所へ
                    it.pos = it.home;
                    it.rot = it.homeRot;
                    t.SetPositionAndRotation(it.pos, it.rot);
                    body.Body.linearVelocity = Vector3.zero;
                    body.Body.angularVelocity = Vector3.zero;
                }
                else if ((bp - it.pos).sqrMagnitude > 1e-8f || Quaternion.Angle(t.rotation, it.rot) > 0.05f)
                {
                    it.pos = bp;
                    it.rot = t.rotation;
                }
                else continue;
                it.m = Matrix4x4.TRS(it.pos, it.rot, Vector3.one * it.scale);
                MoveToCell(i);
                _dirty = true;
            }
            for (int k = _awake.Count - 1; k >= 0; k--)
            {
                int i = _awake[k];
                var body = _bodies[_items[i].body];
                float d2 = (_items[i].pos - p).sqrMagnitude;
                if (d2 < SleepRadius * SleepRadius) continue;
                // 止まったら絵だけにもどす。ずっと遠くなら、動いていても（だれにも見えないので）そこで止める
                bool farAway = d2 > 4f * SleepRadius * SleepRadius;
                if (!farAway && !body.Body.IsSleeping() && body.Body.linearVelocity.sqrMagnitude > 0.0025f) continue;
                Release(k);
            }
        }

        void Wake(int i)
        {
            ref var it = ref _items[i];
            var batch = _batches[it.batch];
            int b;
            if (_freeBodies.Count > 0) b = _freeBodies.Pop();
            else
            {
                if (_bodyRoot == null)
                {
                    _bodyRoot = new GameObject("LooseBodies").transform;
                    _bodyRoot.SetParent(transform, false);
                }
                b = _bodies.Count;
                _bodies.Add(LooseBody.Create(_bodyRoot));
            }
            _bodies[b].Setup(batch.mesh, batch.shape, it.scale, it.pos, it.rot);
            it.body = b;
            _awake.Add(i);
        }

        void Release(int awakeIndex)
        {
            int i = _awake[awakeIndex];
            ref var it = ref _items[i];
            _bodies[it.body].Park();
            _freeBodies.Push(it.body);
            it.body = -1;
            _awake.RemoveAt(awakeIndex);
        }

        void ReleaseAllBodies()
        {
            for (int k = _awake.Count - 1; k >= 0; k--) Release(k);
            foreach (var b in _bodies) if (b != null) b.Park();
        }

        /// <summary>しゃくとりむしがワープしたとき（移動・救済）は、体を持つ物をいったん全部もどす。</summary>
        public void ReleaseBodies() => ReleaseAllBodies();

        void OnDestroy()
        {
            foreach (var b in _bodies)
                if (b != null)
                {
                    if (Application.isPlaying) Destroy(b.gameObject);
                    else DestroyImmediate(b.gameObject);
                }
            _bodies.Clear();
        }

        // ------------------------------------------------------------------
        // 描く（カメラがほとんど動かず、何も動いていなければ、前の描画をくり返す）
        // ------------------------------------------------------------------
        void Update()
        {
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            Render(cam);
        }

        public void Render(Camera cam)
        {
            if (cam == null || _count == 0) return;
            using var prof = s_Loose.Auto();
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            bool still = !_dirty && Application.isPlaying && (camPos - _builtPos).sqrMagnitude < InstancedRenderer.RebuildMove * InstancedRenderer.RebuildMove
                         && Quaternion.Angle(camRot, _builtRot) < InstancedRenderer.RebuildTurn && _builtFrames < 120;
            if (still)
            {
                _builtFrames++;
                foreach (var c in _calls) Issue(c);
                return;
            }
            _dirty = false;
            _builtPos = camPos;
            _builtRot = camRot;
            _builtFrames = 0;
            var wide = Matrix4x4.Perspective(Mathf.Min(170f, cam.fieldOfView + InstancedRenderer.FovMargin), cam.aspect, cam.nearClipPlane, cam.farClipPlane) * cam.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(Application.isPlaying ? wide : cam.projectionMatrix * cam.worldToCameraMatrix, _planes);
            foreach (var b in _batches)
                for (int l = 0; l < 4; l++) b.lists[l].Clear();
            DrawnCount = 0;
            float far = 0f;
            foreach (var b in _batches) far = Mathf.Max(far, b.maxDistance * distanceScale);
            foreach (var kv in _cells)
            {
                if (kv.Value.Count == 0) continue;
                var cb = _cellBounds[kv.Key];
                if (Vector3.Distance(camPos, cb.center) - cb.extents.magnitude > far) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, cb)) continue;
                foreach (int i in kv.Value)
                {
                    ref var it = ref _items[i];
                    var b = _batches[it.batch];
                    float d = Vector3.Distance(camPos, it.pos);
                    if (d > b.maxDistance * distanceScale) continue;
                    int lods = b.mesh.lodCount;
                    int lod = lods > 1 ? InstancedRenderer.LodFor(d, b.meshRadius * it.scale, Mathf.Min(lods, 4)) : 0;
                    var list = b.lists[lod];
                    var bb = new Bounds(it.pos, Vector3.one * (b.meshRadius * it.scale * 2f));
                    if (list.Count == 0) b.bounds[lod] = bb; else b.bounds[lod].Encapsulate(bb);
                    list.Add(it.m);
                    DrawnCount++;
                }
            }
            _calls.Clear();
            int pool = 0;
            foreach (var b in _batches)
                for (int l = 0; l < 4; l++)
                {
                    var list = b.lists[l];
                    for (int start = 0; start < list.Count; start += MaxPerCall)
                    {
                        int n = Mathf.Min(MaxPerCall, list.Count - start);
                        if (pool >= _pool.Count) _pool.Add(new Matrix4x4[MaxPerCall]);
                        var arr = _pool[pool++];
                        list.CopyTo(start, arr, 0, n);
                        var c = new Call { batch = b, mesh = DetailMeshes.Get(b.mesh, l), buffer = arr, count = n, bounds = b.bounds[l] };
                        _calls.Add(c);
                        Issue(c);
                    }
                }
        }

        static void Issue(Call c)
        {
            var rp = new RenderParams(c.batch.material)
            {
                shadowCastingMode = c.batch.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = c.bounds,
            };
            Graphics.RenderMeshInstanced(rp, c.mesh, 0, c.buffer, c.count);
        }
    }

    /// <summary>
    /// 小さな物の体（当たり判定と重さ）。使いまわすので、形に合わせて作りなおす。
    /// しゃくとりむしの体に押されて動き、水には浮く（小石はしずむ）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class LooseBody : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        LooseProps.Shape _shape;
        float _radius;
        Collider _col;

        static PhysicsMaterial s_leaf, s_hard;

        public static LooseBody Create(Transform parent)
        {
            var go = new GameObject("LooseBody") { layer = ShakuConst.RollingLayer };
            go.transform.SetParent(parent, false);
            var rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var lb = go.AddComponent<LooseBody>();
            lb.Body = rb;
            go.SetActive(false);
            return lb;
        }

        public void Setup(Mesh mesh, LooseProps.Shape shape, float scale, Vector3 pos, Quaternion rot)
        {
            if (s_leaf == null)
            {
                s_leaf = new PhysicsMaterial("Leaf") { dynamicFriction = 0.9f, staticFriction = 1f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum };
                s_hard = new PhysicsMaterial("Small") { dynamicFriction = 0.55f, staticFriction = 0.7f, bounciness = 0.2f, frictionCombine = PhysicsMaterialCombine.Average };
            }
            if (_col != null && _shape != shape)
            {
                Destroy(_col);
                _col = null;
            }
            _shape = shape;
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = Vector3.one * scale;
            Bounds mb = mesh.bounds;
            Vector3 size = mb.size;
            float big = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * scale;
            switch (shape)
            {
                case LooseProps.Shape.Leaf:
                {
                    // 葉っぱはうすいので、押す面だけ少し高くする（体に上からおさえつけられず、横に押される）
                    var box = _col as BoxCollider ?? gameObject.AddComponent<BoxCollider>();
                    float h = Mathf.Max(size.y, 0.25f / scale);
                    box.center = new Vector3(mb.center.x, mb.min.y + h * 0.5f, mb.center.z);
                    box.size = new Vector3(size.x, h, size.z);
                    box.sharedMaterial = s_leaf;
                    _col = box;
                    Body.mass = Mathf.Max(0.002f, 0.004f * size.x * size.z * scale * scale);
                    Body.linearDamping = 4f;    // 葉っぱは空気でふわっと止まる
                    Body.angularDamping = 4f;
                    break;
                }
                case LooseProps.Shape.Pebble:
                {
                    var sph = _col as SphereCollider ?? gameObject.AddComponent<SphereCollider>();
                    sph.center = mb.center;
                    sph.radius = (size.x + size.y + size.z) / 6f;
                    sph.sharedMaterial = s_hard;
                    _col = sph;
                    Body.mass = 2.5f * big * big * big;
                    Body.linearDamping = 0.2f;
                    Body.angularDamping = 1.5f;
                    break;
                }
                case LooseProps.Shape.Pinecone:
                {
                    var cap = _col as CapsuleCollider ?? gameObject.AddComponent<CapsuleCollider>();
                    cap.center = mb.center;
                    int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
                    cap.direction = axis;
                    float len = axis == 0 ? size.x : axis == 1 ? size.y : size.z;
                    float w = axis == 0 ? Mathf.Max(size.y, size.z) : axis == 1 ? Mathf.Max(size.x, size.z) : Mathf.Max(size.x, size.y);
                    cap.radius = w * 0.45f;
                    cap.height = len;
                    cap.sharedMaterial = s_hard;
                    _col = cap;
                    Body.mass = 0.3f * big * big * big * 0.2f;
                    Body.linearDamping = 0.15f;
                    Body.angularDamping = 1.1f;   // 転がりのてい抗
                    break;
                }
                default:
                {
                    var mc = _col as MeshCollider ?? gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = DetailMeshes.ForCollision(mesh);
                    mc.convex = true;
                    mc.sharedMaterial = s_hard;
                    _col = mc;
                    Body.mass = 0.1f * big * big * big;
                    Body.linearDamping = 0.3f;
                    Body.angularDamping = 1.2f;
                    break;
                }
            }
            _radius = big * 0.5f;
            gameObject.SetActive(true);
            Body.position = pos;
            Body.rotation = rot;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.Sleep();   // 押されるまでは、そのまま
        }

        public void Park()
        {
            gameObject.SetActive(false);
        }

        void FixedUpdate()
        {
            if (Body == null || Body.IsSleeping()) return;
            float dt = Time.fixedDeltaTime;
            Vector3 p = Body.position;
            var area = Areas.Current;
            // 水に浮く（小石はしずむ）。浮いた物は流れにのる
            float wl = area.WaterLevelAt(p.x, p.z);
            if (wl > -100f && _shape != LooseProps.Shape.Pebble)
            {
                float depth = wl - (p.y - _radius * 0.5f);
                if (depth > 0f)
                {
                    Body.AddForce(Vector3.up * ShakuPhysics.BuoyantAccel(depth, Mathf.Max(_radius, 0.05f) * 0.6f, 30f), ForceMode.Acceleration);
                    Vector3 flow = Creatures.WaterFlow(p, area);
                    Body.linearVelocity = flow + (Body.linearVelocity - flow) * Mathf.Exp(-2.5f * dt);
                }
            }
        }
    }
}

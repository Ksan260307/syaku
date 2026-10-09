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
    /// 重さは本物と同じグラムで（1 単位 = 2.5cm の体積に、石・松ぼっくりなどの密度をかける）。
    /// 地面にふれる形で置き（うまっていると、体を持ったときに地面からはじき出されて転がりだす）、
    /// 地面の下へもぐってしまったら、地面の上へもどす。
    /// </summary>
    [ExecuteAlways]
    public class LooseProps : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Loose = new Unity.Profiling.ProfilerMarker("Shaku.Loose");

        /// <summary>BigLeaf は大きな葉：いつも当たり判定があり、しゃくとりむしが乗れる（風ですべる）。</summary>
        public enum Shape { Leaf, Pebble, Pinecone, Cap, BigLeaf }

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
            public float lift;    // 置いたときの、地面からの高さ（もぐったかを見る）
            public float half;    // 物の大きさの半分
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
        readonly List<BigLeaf> _leaves = new List<BigLeaf>();   // 大きな葉（いつも体がある）
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

        /// <summary>置いたエリア（地面の高さ）。なければ、いまのエリア。</summary>
        public AreaLayout area;
        AreaLayout Area => area ?? Areas.Current;

        public int Count => _count;
        public int BodyCount => _awake.Count;
        public IReadOnlyList<BigLeaf> BigLeaves => _leaves;
        /// <summary>置いた物を 1 つずつ（テストや、置き方の点検用）：形・いまの位置・置いた位置・向き・大きさ。</summary>
        public IEnumerable<(Mesh mesh, Vector3 pos, Vector3 home, Quaternion rot, float scale)> Items()
        {
            for (int i = 0; i < _count; i++)
            {
                var it = _items[i];
                yield return (_batches[it.batch].mesh, it.pos, it.home, it.rot, it.scale);
            }
        }
        /// <summary>直前に数えなおしたときに描いた数（テスト用）。</summary>
        public int DrawnCount { get; private set; }

        // ------------------------------------------------------------------
        // 置く
        // ------------------------------------------------------------------
        public void Clear()
        {
            ReleaseAllBodies();
            foreach (var l in _leaves)
                if (l != null)
                {
                    if (Application.isPlaying) Destroy(l.gameObject);
                    else DestroyImmediate(l.gameObject);
                }
            _leaves.Clear();
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
            it.lift = pos.y - Area.Height(pos.x, pos.z);
            it.half = mesh.bounds.extents.magnitude * scale;
            it.m = Matrix4x4.TRS(pos, rot, Vector3.one * scale);
            it.cell = CellOf(pos);
            _items[_count] = it;
            AddToCell(_count, it);
            if (shape == Shape.BigLeaf) _leaves.Add(BigLeaf.Create(BodyRoot(), mesh, pos, rot, scale, Area, _count));
            _count++;
            _dirty = true;
        }

        Transform BodyRoot()
        {
            if (_bodyRoot == null)
            {
                _bodyRoot = new GameObject("LooseBodies").transform;
                _bodyRoot.SetParent(transform, false);
            }
            return _bodyRoot;
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

        /// <summary>
        /// 置いたあとに、そこへ動かない物（石など）を置いたとき、その中にうまる小石や木の実を、まわりへよける（置いたときだけ使う）。
        /// </summary>
        public int PushOut(Vector3 center, float radius, float halfHeight = float.MaxValue)
        {
            int moved = 0;
            bool synced = false;
            for (int i = 0; i < _count; i++)
            {
                ref var it = ref _items[i];
                var b = _batches[it.batch];
                if (it.body >= 0 || b.shape == Shape.Leaf || b.shape == Shape.BigLeaf) continue;
                Vector2 d = new Vector2(it.pos.x - center.x, it.pos.z - center.z);
                float keep = radius + it.half * 0.6f;
                if (d.sqrMagnitude >= keep * keep) continue;
                if (Mathf.Abs(it.pos.y - center.y) > halfHeight + it.half) continue;   // 高い所の物（幹のサルノコシカケなど）の下の地面の物は、そのまま
                Vector2 dir = d.sqrMagnitude > 1e-6f ? d.normalized : new Vector2(Mathf.Cos(i * 2.39996f), Mathf.Sin(i * 2.39996f));
                if (!synced) { Physics.SyncTransforms(); synced = true; }
                // よけた先が、となりの石の中にならないように：まっすぐ外へ、だめなら少し向きと距離を変えてさがす
                float half = Mathf.Max(0.05f, b.mesh.bounds.extents.magnitude * it.scale);
                Vector3 p = default;
                bool found = false;
                for (int ring = 0; ring < 3 && !found; ring++)
                    for (int k = 0; k < 13 && !found; k++)
                    {
                        float turn = (k + 1) / 2 * 30f * (k % 2 == 0 ? 1f : -1f);
                        Vector2 dd = Quaternion.Euler(0f, 0f, turn) * dir;
                        float r = keep + ring * 0.35f;
                        Vector3 q = new Vector3(center.x + dd.x * r, 0f, center.z + dd.y * r);
                        q.y = Area.Height(q.x, q.z);
                        q = RestOnGround(b.mesh, q, it.rot, it.scale, Area);
                        if (ring == 0 && k == 0) p = q;
                        if (Blocked(q, half)) continue;
                        p = q;
                        found = true;
                    }
                it.pos = it.home = p;
                it.lift = p.y - Area.Height(p.x, p.z);
                it.m = Matrix4x4.TRS(p, it.rot, Vector3.one * it.scale);
                MoveToCell(i);
                moved++;
            }
            if (moved > 0) _dirty = true;
            return moved;
        }

        /// <summary>そこに置くと、ほかの動かない物（石など）にめりこむか。</summary>
        static bool Blocked(Vector3 p, float half)
        {
            foreach (var c in Physics.OverlapSphere(p + Vector3.up * half * 0.4f, half * 0.45f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                if (!c.name.StartsWith("Terrain_") && c.attachedRigidbody == null) return true;
            return false;
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
        public Mesh MeshOf(int i) => _batches[_items[i].batch].mesh;
        public Matrix4x4 MatrixOf(int i) => _items[i].m;
        public float ScaleOf(int i) => _items[i].scale;
        public LooseBody BodyOf(int i) => _items[i].body >= 0 ? _bodies[_items[i].body] : null;

        // ------------------------------------------------------------------
        // 地面にのせる
        // ------------------------------------------------------------------
        static readonly Dictionary<Mesh, Vector3[]> s_samples = new Dictionary<Mesh, Vector3[]>();

        /// <summary>形の頂点を間引いた物（置く高さの計算用）。</summary>
        static Vector3[] Samples(Mesh mesh)
        {
            if (s_samples.TryGetValue(mesh, out var arr) && arr != null) return arr;
            Vector3[] v = mesh.isReadable ? mesh.vertices : null;
            if (v == null || v.Length == 0)
            {
                var b = mesh.bounds;
                v = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    v[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            }
            int step = Mathf.Max(1, v.Length / 160);
            var list = new List<Vector3>(v.Length / step + 1);
            for (int i = 0; i < v.Length; i += step) list.Add(v[i]);
            arr = list.ToArray();
            s_samples[mesh] = arr;
            return arr;
        }

        /// <summary>
        /// いちばん下が地面にふれる高さに直した位置（sink だけ、地面に少しめりこませる）。
        /// 坂では、地面のかたむきに合わせて、どの頂点も地面より下にならないようにする。
        /// </summary>
        public static Vector3 RestOnGround(Mesh mesh, Vector3 pos, Quaternion rot, float scale, AreaLayout area, float sink = 0.01f)
        {
            if (mesh == null || area == null) return pos;
            float h = area.Height(pos.x, pos.z);
            Vector3 n = area.Normal(pos.x, pos.z);
            float gx = n.y > 0.1f ? -n.x / n.y : 0f, gz = n.y > 0.1f ? -n.z / n.y : 0f;
            float lift = float.MinValue;
            foreach (var v in Samples(mesh))
            {
                Vector3 d = rot * (v * scale);
                float ground = h + gx * d.x + gz * d.z;
                lift = Mathf.Max(lift, ground - (pos.y + d.y));
            }
            pos.y += lift - sink;
            return pos;
        }

        /// <summary>重さ（グラム）。体積（1 単位 = 2.5cm）に、その物の密度をかける。</summary>
        public static float MassOf(Mesh mesh, Shape shape, float scale) => LooseBody.MassOf(mesh.bounds.size * scale, shape);

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
                    if (_items[i].body >= 0 || _batches[_items[i].batch].shape == Shape.BigLeaf) continue;
                    if ((_items[i].pos - p).sqrMagnitude > (WakeRadius + RadiusOf(_items[i])) * (WakeRadius + RadiusOf(_items[i]))) continue;
                    Wake(i);
                }
            }
            // 大きな葉：風ですべったら、絵もうごかす
            foreach (var leaf in _leaves)
            {
                if (leaf == null || !leaf.Moved) continue;
                leaf.Moved = false;
                ref var li = ref _items[leaf.Index];
                li.pos = leaf.transform.position;
                li.rot = leaf.transform.rotation;
                li.m = Matrix4x4.TRS(li.pos, li.rot, Vector3.one * li.scale);
                MoveToCell(leaf.Index);
                _dirty = true;
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
                if (!lost && bp.y < area.Height(bp.x, bp.z) + it.lift - Mathf.Max(0.05f, it.half * 0.5f))
                {
                    // 地面の下へもぐってしまった：その場で、地面の上へもどす
                    t.position = new Vector3(bp.x, area.Height(bp.x, bp.z) + it.lift + 0.01f, bp.z);
                    body.Body.position = t.position;
                    body.Body.linearVelocity = Vector3.zero;
                    body.Body.angularVelocity = Vector3.zero;
                    bp = t.position;
                }
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
                b = _bodies.Count;
                _bodies.Add(LooseBody.Create(BodyRoot()));
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
    /// 重さは本物と同じグラム。小石・松ぼっくり・ぼうしは、形そのもの（つつみこむ形）で地面にふれ、
    /// でこぼこなので、ゆるい坂では転がらない（転がりのてい抗）。押されて動いても、すぐに止まる。
    /// 落ち葉は軽く、風や少しの力でふわっと動く。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class LooseBody : MonoBehaviour
    {
        /// <summary>1 単位（しゃくとりむしの体長 = 2.5cm）の立方体の体積（cm³）。</summary>
        public const float Cm3PerUnit3 = 15.625f;
        /// <summary>しゃくとりむし（本物は約 0.1g）が押して動かせる重さ（グラム）。これより重い小石は動かない。</summary>
        public const float WormPushLimit = 0.3f;

        public Rigidbody Body { get; private set; }
        LooseProps.Shape _shape;
        float _radius;
        float _rollResist;
        bool _touching;
        Collider _col;

        static PhysicsMaterial s_leaf, s_stone, s_cone;
        static readonly HashSet<EntityId> s_baked = new HashSet<EntityId>();

        /// <summary>
        /// 重さ（グラム）。size は物を包む箱の大きさ（単位）。
        /// 小石は石の密度（2.6 g/cm³）で、箱の半分くらいが石。松ぼっくりとぼうしは、すき間だらけで軽い。
        /// 落ち葉は、うすい葉の面の重さ（1cm² あたり 0.01g）。
        /// </summary>
        public static float MassOf(Vector3 size, LooseProps.Shape shape)
        {
            float box = size.x * size.y * size.z * Cm3PerUnit3;
            switch (shape)
            {
                case LooseProps.Shape.Leaf:
                {
                    float a = Mathf.Max(size.x * size.z, Mathf.Max(size.x * size.y, size.y * size.z)) * 6.25f;   // cm²
                    return Mathf.Max(0.001f, 0.01f * 0.6f * a);
                }
                case LooseProps.Shape.Pebble: return Mathf.Max(0.001f, 2.6f * 0.5f * box);
                case LooseProps.Shape.Pinecone: return Mathf.Max(0.01f, 0.06f * box);
                default: return Mathf.Max(0.005f, 0.06f * box);   // どんぐりのぼうし
            }
        }

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

        /// <summary>つつみこむ形の当たり判定（形ごとに一度だけ作っておく）。</summary>
        MeshCollider Hull(Mesh mesh, PhysicsMaterial mat)
        {
            Mesh cm = DetailMeshes.ForCollision(mesh);
            if (cm != null && s_baked.Add(cm.GetEntityId())) Physics.BakeMesh(cm.GetEntityId(), true);
            var mc = _col as MeshCollider ?? gameObject.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = cm;
            mc.sharedMaterial = mat;
            return mc;
        }

        public void Setup(Mesh mesh, LooseProps.Shape shape, float scale, Vector3 pos, Quaternion rot)
        {
            if (s_leaf == null)
            {
                s_leaf = new PhysicsMaterial("Leaf") { dynamicFriction = 0.9f, staticFriction = 1f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum };
                // 石と松ぼっくりは、ざらざら（すべりにくい）。はね返りは小さい
                s_stone = new PhysicsMaterial("Stone") { dynamicFriction = 0.75f, staticFriction = 0.9f, bounciness = 0.1f, frictionCombine = PhysicsMaterialCombine.Maximum };
                s_cone = new PhysicsMaterial("Cone") { dynamicFriction = 0.7f, staticFriction = 0.85f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Maximum };
            }
            bool wantBox = shape == LooseProps.Shape.Leaf;
            if (_col != null && (_col is BoxCollider) != wantBox)
            {
                DestroyImmediate(_col);
                _col = null;
            }
            _shape = shape;
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = Vector3.one * scale;
            Bounds mb = mesh.bounds;
            Vector3 size = mb.size;
            float big = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * scale;
            Body.mass = MassOf(size * scale, shape);
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
                    Body.linearDamping = 4f;    // 葉っぱは空気でふわっと止まる
                    Body.angularDamping = 4f;
                    Body.sleepThreshold = 0.005f;
                    _rollResist = 0f;
                    break;
                }
                case LooseProps.Shape.Pebble:
                    _col = Hull(mesh, s_stone);
                    Body.linearDamping = 0.2f;
                    Body.angularDamping = 2.5f;
                    Body.sleepThreshold = 0.03f;
                    _rollResist = 0.35f;   // でこぼこの石は、ほとんど転がらない
                    break;
                case LooseProps.Shape.Pinecone:
                    _col = Hull(mesh, s_cone);
                    Body.linearDamping = 0.2f;
                    Body.angularDamping = 2f;
                    Body.sleepThreshold = 0.03f;
                    _rollResist = 0.25f;   // かさが地面にひっかかる
                    break;
                default:
                    _col = Hull(mesh, s_cone);
                    Body.linearDamping = 0.3f;
                    Body.angularDamping = 2f;
                    Body.sleepThreshold = 0.03f;
                    _rollResist = 0.3f;
                    break;
            }
            _radius = big * 0.5f;
            _touching = false;
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

        void OnCollisionStay(Collision c)
        {
            if (c.gameObject.layer != ShakuConst.WormBodyLayer) _touching = true;
        }

        void OnCollisionEnter(Collision c) => OnCollisionStay(c);

        void FixedUpdate()
        {
            if (Body == null || Body.IsSleeping()) return;
            float dt = Time.fixedDeltaTime;
            Vector3 p = Body.position;
            var area = Areas.Current;
            // 水に浮く（小石はしずむ）。浮いた物は流れにのる
            float wl = area.WaterLevelAt(p.x, p.z);
            bool floating = false;
            if (wl > -100f && _shape != LooseProps.Shape.Pebble)
            {
                float depth = wl - (p.y - _radius * 0.5f);
                if (depth > 0f)
                {
                    floating = true;
                    Body.AddForce(Vector3.up * ShakuPhysics.BuoyantAccel(depth, Mathf.Max(_radius, 0.05f) * 0.6f, 30f), ForceMode.Acceleration);
                    Vector3 flow = Creatures.WaterFlow(p, area);
                    Body.linearVelocity = flow + (Body.linearVelocity - flow) * Mathf.Exp(-2.5f * dt);
                }
            }
            // 転がりのてい抗：地面にふれていると、転がり・すべりを重さに見合った力でおさえる
            // （でこぼこの物は、ゆるい坂では止まったまま。押されて動いても、すぐに止まる）
            if (_rollResist > 0f && _touching && !floating)
            {
                float dv = _rollResist * -Physics.gravity.y * dt;
                Vector3 v = Body.linearVelocity;
                float speed = v.magnitude;
                Body.linearVelocity = speed <= dv ? Vector3.zero : v * (1f - dv / speed);
                Vector3 w = Body.angularVelocity;
                float spin = w.magnitude, dw = dv / Mathf.Max(_radius, 0.02f);
                Body.angularVelocity = spin <= dw ? Vector3.zero : w * (1f - dw / spin);
            }
            _touching = false;
        }
    }
}

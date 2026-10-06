using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// フィールドをうろつくいきもの（モブ）。アリの行列・はばたくチョウ・跳ねるバッタ・飛び立つ鳥など。
    /// 数が多いので GameObject は作らず、毎フレーム行列を計算して GPU インスタンシングで描く。
    /// 脚は 1 本ずつ別の部品で、進んだ距離に合わせて交互に動かす。
    /// どれかの個体に近づくと、その種がいきもの図鑑に登録される。
    /// 大きないきもの（かたつむり・カブトムシ・カエル・カニ・カマキリ）には当たり判定があり、登って乗れる。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class Creatures : MonoBehaviour
    {
        public WorldAssets assets;
        public bool Active { get; set; }
        public event Action<SpeciesDef> Discovered;
        /// <summary>見つけたいきものと、その場所（演出用）。</summary>
        public event Action<SpeciesDef, Vector3> DiscoveredAt;

        /// <summary>テスト用：レアのいきものが出る確率を上書きする（null で本来の確率）。</summary>
        public static float? RareChanceOverride;
        /// <summary>小さな体の世界での重力（しゃくとりむしの落下と同じ）。</summary>
        public const float Gravity = 9f;

        class Mob
        {
            public SpeciesDef sp;
            public MobGroup group;
            public Vector3 pos;
            public Vector3 up = Vector3.up;
            public Vector3 fwd = Vector3.forward;
            public Vector3 home;
            public float radius;
            public float height;
            public float timer;
            public float phase;
            public float anim;
            public float speedMul = 1f;
            public float scale = 1f;
            // 跳ねる・飛ぶ
            public bool airborne;
            public Vector3 from, to;
            public float t, dur, arc;
            public int landIndex;
            // 行列
            public float pathS;
            public bool carrying;
            // 水面・空中・跳躍
            public Vector3 vel;
            public Vector3 target;
            public bool resting;
            public float joy;
            // 脚
            public Vector3 prevPos;
            public float gait;
            public float moveSpeed;
            // カマキリ・だんごむし
            public float raise;
            public float curled;
            // 当たり判定（乗れるいきもの）
            public Transform collider;
        }

        public struct MobInfo
        {
            public Vector3 pos, up, fwd;
            public bool airborne, carrying, curled;
            public float raise, gait, moveSpeed;
            public string species;
        }

        readonly List<Mob> _mobs = new List<Mob>();
        readonly Dictionary<(Mesh, Material), List<Matrix4x4>> _draw = new Dictionary<(Mesh, Material), List<Matrix4x4>>();
        readonly List<Matrix4x4[]> _pool = new List<Matrix4x4[]>();
        readonly Dictionary<string, Mesh> _meshCache = new Dictionary<string, Mesh>();
        System.Random _rng = new System.Random(1);
        AreaLayout _area = Areas.Forest;
        Transform _colliderRoot;
        int _frame;

        public int MobCount => _mobs.Count;

        float R(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

        Mesh M(string name)
        {
            if (string.IsNullOrEmpty(name) || assets == null) return null;
            if (!_meshCache.TryGetValue(name, out var m))
            {
                m = assets.Get(name);
                _meshCache[name] = m;
            }
            return m;
        }

        public int CountOf(string species)
        {
            int n = 0;
            foreach (var m in _mobs) if (m.sp.id == species) n++;
            return n;
        }

        /// <summary>レアのいきものが何匹いるか。</summary>
        public int RareCount
        {
            get
            {
                int n = 0;
                foreach (var m in _mobs) if (m.sp.IsRare) n++;
                return n;
            }
        }

        Mob Find(string species, int index)
        {
            int n = 0;
            foreach (var m in _mobs)
            {
                if (m.sp.id != species) continue;
                if (n++ == index) return m;
            }
            return null;
        }

        public Vector3 PositionOf(string species, int index = 0) => Find(species, index)?.pos ?? Vector3.zero;

        public bool IsAirborne(string species, int index = 0) => Find(species, index)?.airborne ?? false;

        public MobInfo Info(string species, int index = 0)
        {
            var m = Find(species, index);
            if (m == null) return default;
            return new MobInfo
            {
                pos = m.pos, up = m.up, fwd = m.fwd, airborne = m.airborne, carrying = m.carrying, curled = m.curled > 0f,
                raise = m.raise, gait = m.gait, moveSpeed = m.moveSpeed, species = m.sp.id,
            };
        }

        /// <summary>テスト用：個体の位置を動かす。</summary>
        public void SetPosition(string species, int index, Vector3 p)
        {
            var m = Find(species, index);
            if (m == null) return;
            m.pos = p;
            m.prevPos = p;
            m.home = p;
        }

        /// <summary>近くにいる、まだ図鑑にのっていないいきものの場所（ミニマップの「？」用）。</summary>
        public int UndiscoveredNear(Vector3 p, float radius, List<Vector3> results, int max = 4)
        {
            results.Clear();
            float r2 = radius * radius;
            foreach (var m in _mobs)
            {
                if (results.Count >= max) break;
                if (IsDiscovered(m.sp.id)) continue;
                Vector3 d = m.pos - p;
                d.y = 0f;
                if (d.sqrMagnitude < r2) results.Add(m.pos);
            }
            return results.Count;
        }

        public IEnumerable<string> SpeciesHere()
        {
            var seen = new HashSet<string>();
            foreach (var m in _mobs)
                if (seen.Add(m.sp.id)) yield return m.sp.id;
        }

        public static bool IsDiscovered(string species) => SaveSystem.Data.creatures.Contains(species);

        /// <summary>見つけたふつうのいきものの数（レアはふくまない）。</summary>
        public static int DiscoveredCount
        {
            get
            {
                int n = 0;
                foreach (var s in SpeciesCatalog.Regular) if (IsDiscovered(s.id)) n++;
                return n;
            }
        }

        public static int RareDiscoveredCount
        {
            get
            {
                int n = 0;
                foreach (var s in SpeciesCatalog.Rares) if (IsDiscovered(s.id)) n++;
                return n;
            }
        }

        public void Clear()
        {
            _mobs.Clear();
            if (_colliderRoot != null)
            {
                if (Application.isPlaying) Destroy(_colliderRoot.gameObject);
                else DestroyImmediate(_colliderRoot.gameObject);
                _colliderRoot = null;
            }
        }

        void OnDestroy() => Clear();

        // ------------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------------
        public void Build(WorldGenerator world)
        {
            Clear();
            _meshCache.Clear();
            _area = world.Area;
            _rng = new System.Random(world.seed + 31 * world.Area.Id.Length);
            // レアは遊ぶたびにちがう（決まった場所にいつもいるわけではない）
            var rareRng = new System.Random(Environment.TickCount ^ world.Area.Id.GetHashCode());
            foreach (var g in world.Mobs)
            {
                var baseSp = SpeciesCatalog.Get(g.species);
                if (baseSp == null) continue;
                var rare = SpeciesCatalog.RareVariantOf(baseSp.id);
                float pathLen = PathLength(g.path);
                for (int i = 0; i < g.count; i++)
                {
                    var sp = baseSp;
                    if (rare != null && rareRng.NextDouble() < (RareChanceOverride ?? rare.rareChance)) sp = rare;
                    var m = new Mob
                    {
                        sp = sp,
                        group = g,
                        home = g.center,
                        radius = g.radius,
                        height = g.height,
                        phase = R(0f, 100f),
                        scale = sp.scale * R(0.9f, 1.1f),
                        timer = R(0.2f, 3f),
                    };
                    float a = R(0f, Mathf.PI * 2f);
                    m.fwd = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    if (sp.kind == MobKind.Marcher && g.path.Count > 1)
                    {
                        m.pathS = pathLen * i / g.count;
                        m.pos = PathPoint(g.path, m.pathS, out m.fwd);
                    }
                    else if (sp.kind == MobKind.Bird)
                    {
                        Vector3 c = g.path.Count > 0 ? g.path[0] : g.center;
                        m.pos = c + new Vector3(R(-2f, 2f), 0f, R(-2f, 2f)) * (g.count > 1 ? 1f : 0f);
                        m.landIndex = 0;
                        m.timer = R(15f, 35f);
                    }
                    else
                    {
                        float d = g.count > 1 ? Mathf.Sqrt(R(0f, 1f)) * g.radius * 0.8f : 0f;
                        float b = R(0f, Mathf.PI * 2f);
                        m.pos = g.center + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * d;
                    }
                    if (sp.kind == MobKind.Skater) m.pos.y = g.center.y;
                    else if (sp.kind != MobKind.Flutter && sp.kind != MobKind.Hover) SnapToSurface(m, 2.5f, 6f);
                    m.prevPos = m.pos;
                    if (sp.rideable) MakeCollider(m);
                    _mobs.Add(m);
                }
            }
            UpdateColliders();
        }

        void MakeCollider(Mob m)
        {
            Mesh mesh = M(m.sp.body);
            if (mesh == null) return;
            if (_colliderRoot == null)
            {
                _colliderRoot = new GameObject("CreatureColliders").transform;
                _colliderRoot.SetParent(transform, false);
            }
            var go = new GameObject("Mob_" + m.sp.id);
            go.transform.SetParent(_colliderRoot, false);
            go.layer = ShakuConst.CreatureLayer;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<MovingPlatform>();
            m.collider = go.transform;
        }

        static float PathLength(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 0; i < path.Count; i++) l += Vector3.Distance(path[i], path[(i + 1) % path.Count]);
            return l;
        }

        static Vector3 PathPoint(List<Vector3> path, float s, out Vector3 dir)
        {
            float total = PathLength(path);
            s = Mathf.Repeat(s, Mathf.Max(total, 0.001f));
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 a = path[i];
                Vector3 b = path[(i + 1) % path.Count];
                float l = Vector3.Distance(a, b);
                if (s <= l || i == path.Count - 1)
                {
                    dir = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector3.forward;
                    return Vector3.Lerp(a, b, l > 1e-5f ? Mathf.Clamp01(s / l) : 0f);
                }
                s -= l;
            }
            dir = Vector3.forward;
            return path[0];
        }

        /// <summary>
        /// 面にのせる。壁登りをしないいきもの（鳥・カエル・カマキリなど）は、体をほとんどかたむけない。
        /// </summary>
        bool SnapToSurface(Mob m, float above, float below)
        {
            Vector3 up = m.sp.climbs && m.up.sqrMagnitude > 0.5f ? m.up : Vector3.up;
            if (Physics.Raycast(m.pos + up * above, -up, out var hit, above + below, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                || Physics.Raycast(m.pos + Vector3.up * above, Vector3.down, out hit, above + below, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                m.pos = hit.point;
                m.up = m.sp.climbs ? Vector3.Slerp(m.up, hit.normal, 0.35f).normalized : UprightUp(hit.normal);
                return true;
            }
            m.pos.y = _area.Height(m.pos.x, m.pos.z);
            m.up = Vector3.Slerp(m.up, Vector3.up, 0.2f);
            return false;
        }

        /// <summary>壁登りをしないいきものの「上」：地面の向きに 12 度までしかかたむけない。</summary>
        public static Vector3 UprightUp(Vector3 groundNormal)
        {
            if (groundNormal.y <= 0f) return Vector3.up;
            return Vector3.RotateTowards(Vector3.up, groundNormal.normalized, 12f * Mathf.Deg2Rad, 0f).normalized;
        }

        float GroundOrWater(Vector3 p)
        {
            float h = _area.Height(p.x, p.z);
            return Mathf.Max(h, _area.WaterLevelAt(p.x, p.z));
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f || _mobs.Count == 0) return;
            _frame++;
            var worm = InchwormController.Instance;
            Vector3 head = worm != null ? worm.HeadPosition : new Vector3(9999f, 0f, 0f);
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            for (int i = 0; i < _mobs.Count; i++)
            {
                var m = _mobs[i];
                float d2 = (m.pos - camPos).sqrMagnitude;
                // 遠くのいきものは、間引いて動かす（行列は毎フレーム描く）
                bool far = d2 > 70f * 70f;
                if (far && (i + _frame) % 3 != 0) continue;
                float sdt = far ? dt * 3f : dt;
                m.anim += sdt;
                bool near = d2 < 45f * 45f;
                switch (m.sp.kind)
                {
                    case MobKind.Marcher:
                        if (m.group.path.Count > 1) UpdateMarcher(m, sdt, near);
                        else UpdateCrawler(m, sdt, near, i, head);
                        break;
                    case MobKind.Crawler: UpdateCrawler(m, sdt, near, i, head); break;
                    case MobKind.Flutter: UpdateFlutter(m, sdt); break;
                    case MobKind.Hopper: UpdateHopper(m, sdt, head); break;
                    case MobKind.Bird: UpdateBird(m, sdt, head); break;
                    case MobKind.Skater: UpdateSkater(m, sdt); break;
                    case MobKind.Hover: UpdateHover(m, sdt); break;
                    case MobKind.Pouncer: UpdatePouncer(m, sdt, near, i, head); break;
                    case MobKind.Stalker: UpdateStalker(m, sdt, head); break;
                }
                if (m.joy > 0f) m.joy = Mathf.Max(0f, m.joy - sdt * 0.8f);
                // 近くのスズメはときどきさえずり、カエルはけろけろ鳴く
                if (near && Application.isPlaying && d2 < 25f * 25f)
                {
                    float vol = Mathf.Clamp01(1f - Mathf.Sqrt(d2) / 25f);
                    if (m.sp.id == "sparrow" && !m.airborne && R(0f, 1f) < sdt * 0.06f) AudioManager.Instance?.Chirp(0.35f * vol);
                    else if (m.sp.id == "frog" && !m.airborne && R(0f, 1f) < sdt * 0.04f) AudioManager.Instance?.Croak(0.4f * vol);
                }

                // 脚：進んだ距離だけ歩く位相を進める
                Vector3 moved = m.pos - m.prevPos;
                moved -= m.up * Vector3.Dot(moved, m.up);
                float dist = moved.magnitude;
                m.moveSpeed = Mathf.Lerp(m.moveSpeed, dist / sdt, 1f - Mathf.Exp(-10f * sdt));
                if (!m.airborne) m.gait += dist / Mathf.Max(0.02f, m.sp.stride) * Mathf.PI;
                m.prevPos = m.pos;

                if (Active && !IsDiscovered(m.sp.id))
                {
                    Vector3 c = m.pos + m.up * (0.3f * m.scale);
                    if ((head - c).sqrMagnitude < m.sp.discoverRadius * m.sp.discoverRadius) Discover(m.sp, m.pos);
                }
            }
            UpdateColliders();
            Draw(camPos);
        }

        void UpdateColliders()
        {
            bool any = false;
            foreach (var m in _mobs)
            {
                if (m.collider == null) continue;
                Vector3 f = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, Vector3.forward);
                m.collider.SetPositionAndRotation(m.pos, Quaternion.LookRotation(-f, m.up));
                m.collider.localScale = Vector3.one * m.scale;
                any = true;
            }
            if (any) Physics.SyncTransforms();
        }

        public void Discover(SpeciesDef sp) => Discover(sp, Vector3.zero);

        void Discover(SpeciesDef sp, Vector3 where)
        {
            if (IsDiscovered(sp.id)) return;
            SaveSystem.Data.creatures.Add(sp.id);
            SaveSystem.Save();
            foreach (var m in _mobs)
                if (m.sp == sp) m.joy = 1f;
            Discovered?.Invoke(sp);
            DiscoveredAt?.Invoke(sp, where);
        }

        /// <summary>近くのしゃくとりむしをよける向き（ぶつからないように少しそれる）。</summary>
        static Vector3 Avoid(Mob m, Vector3 head, float radius)
        {
            Vector3 away = m.pos - head;
            away -= m.up * Vector3.Dot(away, m.up);
            float d = away.magnitude;
            if (d > radius || d < 1e-4f) return Vector3.zero;
            return away / d * (1f - d / radius);
        }

        void UpdateCrawler(Mob m, float dt, bool near, int index, Vector3 head)
        {
            m.timer -= dt;
            bool pillbug = m.sp.id == "pillbug";
            if (pillbug)
            {
                // だんごむし：近づくと、くるんとまるくなる
                if ((head - m.pos).sqrMagnitude < 1.3f * 1.3f) m.curled = 4f;
                if (m.curled > 0f)
                {
                    m.curled -= dt;
                    return;
                }
            }
            if (m.group.path.Count == 1 && m.radius > 0f)
            {
                // 中心のまわりをぐるり（キノコの根元のかたつむり）
                Vector3 c = m.group.path[0];
                Vector3 radial = m.pos - c;
                radial.y = 0f;
                if (radial.sqrMagnitude < 0.01f) radial = Vector3.right;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial.normalized);
                Vector3 desired = c + radial.normalized * m.radius;
                m.fwd = Vector3.Slerp(m.fwd, (tangent + (desired - m.pos) * 0.5f).normalized, dt * 2f);
                m.speedMul = 1f;
            }
            else if (m.timer <= 0f)
            {
                if (R(0f, 1f) < 0.3f)
                {
                    m.speedMul = 0f;
                    m.timer = R(1f, 3f);
                }
                else
                {
                    m.speedMul = R(0.7f, 1.2f);
                    m.timer = R(1.5f, 4f);
                    Vector3 toHome = m.home - m.pos;
                    toHome.y = 0f;
                    float turn = R(-70f, 70f);
                    Vector3 f = Quaternion.AngleAxis(turn, Vector3.up) * m.fwd;
                    if (toHome.magnitude > m.radius) f = Vector3.Slerp(f, toHome.normalized, 0.75f);
                    m.fwd = f.normalized;
                }
            }
            // しゃくとりむしに近いと、少しよけて歩く
            Vector3 avoid = Avoid(m, head, 0.9f);
            if (avoid.sqrMagnitude > 0.0001f)
            {
                m.fwd = Vector3.Slerp(m.fwd, (m.fwd + avoid * 2f).normalized, dt * 4f);
                m.speedMul = Mathf.Max(m.speedMul, 0.8f);
            }
            Vector3 move = m.sp.sideways ? Vector3.Cross(m.up, m.fwd) : m.fwd;
            m.pos += move * (m.sp.speed * m.speedMul * dt);
            if (near || (index + _frame) % 4 == 0) SnapToSurface(m, 0.8f, 2.5f);
            m.fwd = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, m.fwd);
        }

        void UpdateMarcher(Mob m, float dt, bool near)
        {
            float total = PathLength(m.group.path);
            m.pathS += m.sp.speed * dt * (0.9f + 0.2f * Mathf.Sin(m.phase));
            Vector3 p = PathPoint(m.group.path, m.pathS, out var dir);
            // 行列の帰り道では、食べものを運んでいる
            m.carrying = Mathf.Repeat(m.pathS, Mathf.Max(total, 0.01f)) > total * 0.5f;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            p += side * Mathf.Sin(m.anim * 3f + m.phase) * 0.06f;
            m.pos = p;
            m.fwd = Vector3.Slerp(m.fwd, dir, dt * 8f);
            if (near) SnapToSurface(m, 1.2f, 3f);
            else m.pos.y = _area.Height(p.x, p.z);
        }

        void UpdateFlutter(Mob m, float dt)
        {
            float t = m.anim + m.phase;
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                m.resting = !m.resting && R(0f, 1f) < 0.4f;
                m.timer = m.resting ? R(2.5f, 5f) : R(6f, 12f);
                if (!m.resting) m.target = m.home + new Vector3(R(-m.radius, m.radius), 0f, R(-m.radius, m.radius));
                else m.target = m.pos;
            }
            float ground = GroundOrWater(m.pos);
            Vector3 goal;
            if (m.resting) goal = new Vector3(m.target.x, ground + 0.15f, m.target.z);
            else
            {
                goal = m.target + new Vector3(Mathf.Sin(t * 0.9f) * 2.2f, 0f, Mathf.Sin(t * 1.7f) * 1.4f);
                goal.y = ground + m.height + Mathf.Sin(t * 2.3f) * 0.7f;
            }
            Vector3 d = goal - m.pos;
            // 風に流されながら、はばたいて目的地へ（速度に慣性をつける）
            Vector3 want = Vector3.ClampMagnitude(d * 1.5f, m.sp.speed) + (m.resting ? Vector3.zero : Wind.At(m.pos) * 0.5f);
            m.vel = Vector3.Lerp(m.vel, want, 1f - Mathf.Exp(-3f * dt));
            m.pos += m.vel * dt;
            Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
            if (hv.sqrMagnitude > 0.01f) m.fwd = Vector3.Slerp(m.fwd, hv.normalized, dt * 4f);
            m.up = Vector3.up;
            m.airborne = !m.resting || d.magnitude > 0.3f;
        }

        /// <summary>重力で放物線をえがいて跳ぶ。peak は高い方の端から、さらに上がる高さ。</summary>
        void StartJump(Mob m, Vector3 to, float peak)
        {
            float top = Mathf.Max(m.pos.y, to.y) + Mathf.Max(0.05f, peak);
            float vy = Mathf.Sqrt(2f * Gravity * (top - m.pos.y));
            float tUp = vy / Gravity;
            float tDown = Mathf.Sqrt(2f * (top - to.y) / Gravity);
            float T = Mathf.Max(0.15f, tUp + tDown);
            Vector3 h = to - m.pos;
            h.y = 0f;
            m.vel = h / T + Vector3.up * vy;
            m.from = m.pos;
            m.to = to;
            m.dur = T;
            m.t = 0f;
            m.airborne = true;
            if (h.sqrMagnitude > 1e-4f) m.fwd = h.normalized;
        }

        /// <summary>跳んでいる間：重力で落ちて、地面にとどいたら着地する。</summary>
        bool UpdateJump(Mob m, float dt)
        {
            m.t += dt;
            m.vel += Vector3.down * Gravity * dt;
            Vector3 next = m.pos + m.vel * dt;
            if (m.vel.y < 0f && Physics.Raycast(m.pos + Vector3.up * 0.05f, (next - m.pos).normalized, out var hit, (next - m.pos).magnitude + 0.06f,
                    ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                m.pos = hit.point;
                Land(m);
                return true;
            }
            m.pos = next;
            if (m.t > m.dur * 1.6f || m.pos.y < GroundOrWater(m.pos) - 0.3f)
            {
                m.pos = m.to;
                Land(m);
                return true;
            }
            return false;
        }

        void Land(Mob m)
        {
            m.airborne = false;
            m.vel = Vector3.zero;
            SnapToSurface(m, 1f, 3f);
            m.joy = Mathf.Max(m.joy, 0.25f);   // 着地でぽよんと
        }

        void UpdateHopper(Mob m, float dt, Vector3 head)
        {
            bool frog = m.sp.id == "frog";
            if (m.airborne)
            {
                UpdateJump(m, dt);
                return;
            }
            m.timer -= dt;
            bool scared = !frog && (head - m.pos).sqrMagnitude < 2.2f * 2.2f;
            if (m.timer > 0f && !scared) return;
            m.timer = frog ? R(4f, 8f) : R(2f, 6f);
            for (int tries = 0; tries < 6; tries++)
            {
                float a = R(0f, Mathf.PI * 2f);
                float dist = frog ? R(0.8f, 2.2f) : R(2f, 5f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (scared) dir = Vector3.ProjectOnPlane(m.pos - head, Vector3.up).normalized;
                Vector3 target = m.pos + dir * dist;
                Vector3 toHome = m.home - target;
                toHome.y = 0f;
                if (toHome.magnitude > m.radius + 1f && !scared) continue;
                if (!Physics.Raycast(target + Vector3.up * 6f, Vector3.down, out var hit, 14f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (_area.IsUnderwater(hit.point) || !_area.InPlayArea(hit.point) || hit.normal.y < 0.6f) continue;
                StartJump(m, hit.point, frog ? 0.7f : R(1.2f, 2f));
                break;
            }
        }

        void UpdatePouncer(Mob m, float dt, bool near, int index, Vector3 head)
        {
            if (m.airborne)
            {
                UpdateJump(m, dt);
                return;
            }
            Vector3 toWorm = head - m.pos;
            toWorm -= m.up * Vector3.Dot(toWorm, m.up);
            if (toWorm.magnitude < 3f)
            {
                // ハエトリグモ：しゃくとりむしが気になって、じっと見つめる
                m.fwd = Vector3.Slerp(m.fwd, toWorm.normalized, dt * 5f);
                m.speedMul = 0f;
                return;
            }
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                float roll = R(0f, 1f);
                if (roll < 0.25f)
                {
                    // ぴょん
                    Vector3 dir = Quaternion.AngleAxis(R(-60f, 60f), Vector3.up) * Vector3.ProjectOnPlane(m.fwd, Vector3.up).normalized;
                    Vector3 toHome = m.home - m.pos;
                    toHome.y = 0f;
                    if (toHome.magnitude > m.radius) dir = toHome.normalized;
                    Vector3 target = m.pos + dir * R(0.4f, 1.0f);
                    if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var hit, 5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                        && !_area.IsUnderwater(hit.point) && hit.normal.y > 0.3f)
                        StartJump(m, hit.point, R(0.15f, 0.35f));
                    m.timer = R(1.5f, 3f);
                    return;
                }
                // ちょこちょこ歩いては止まる
                m.speedMul = roll < 0.55f ? 0f : R(0.9f, 1.4f);
                m.timer = R(0.4f, 1.4f);
                m.fwd = (Quaternion.AngleAxis(R(-80f, 80f), m.up) * m.fwd).normalized;
            }
            m.pos += m.fwd * (m.sp.speed * m.speedMul * dt);
            if (near || (index + _frame) % 4 == 0) SnapToSurface(m, 0.6f, 2f);
            m.fwd = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, m.fwd);
        }

        void UpdateStalker(Mob m, float dt, Vector3 head)
        {
            Vector3 toWorm = head - m.pos;
            toWorm.y = 0f;
            bool alert = toWorm.magnitude < 6f;
            // カマキリ：近づくと向きを変え、かまを持ち上げる
            m.raise = Mathf.MoveTowards(m.raise, alert ? 1f : 0f, dt * (alert ? 2.5f : 0.8f));
            if (alert)
            {
                m.fwd = Vector3.Slerp(m.fwd, toWorm.normalized, dt * 1.6f);
                m.speedMul = 0f;
                return;
            }
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                bool walk = R(0f, 1f) < 0.35f;
                m.speedMul = walk ? 1f : 0f;
                m.timer = walk ? R(1.5f, 3f) : R(4f, 9f);
                Vector3 toHome = m.home - m.pos;
                toHome.y = 0f;
                Vector3 f = Quaternion.AngleAxis(R(-50f, 50f), Vector3.up) * m.fwd;
                if (toHome.magnitude > m.radius) f = toHome.normalized;
                m.fwd = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
            }
            if (m.speedMul > 0f)
            {
                m.pos += m.fwd * (m.sp.speed * m.speedMul * dt);
                SnapToSurface(m, 1f, 3f);
            }
        }

        void UpdateBird(Mob m, float dt, Vector3 head)
        {
            bool crow = m.sp.id == "crow";
            if (m.airborne)
            {
                m.t += dt / m.dur;
                float k = Mathf.Clamp01(m.t);
                float e = ShakuMath.Smooth01(k);
                Vector3 p = Vector3.Lerp(m.from, m.to, e);
                p.y += m.arc * Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.85f));
                Vector3 dir = p - m.pos;
                m.vel = dir / Mathf.Max(dt, 1e-4f);
                m.pos = p;
                Vector3 hd = new Vector3(dir.x, 0f, dir.z);
                if (hd.sqrMagnitude > 1e-5f) m.fwd = Vector3.Slerp(m.fwd, hd.normalized, dt * 5f);
                m.up = Vector3.up;
                if (k >= 1f)
                {
                    m.airborne = false;
                    m.timer = R(25f, 50f);
                    m.t = 0f;
                    SnapToSurface(m, 3f, 6f);
                }
                return;
            }
            m.timer -= dt;
            float flee = m.sp.fleeRadius;
            bool scared = (head - m.pos).sqrMagnitude < flee * flee;
            if (scared || m.timer <= 0f)
            {
                TakeOff(m, crow);
                return;
            }
            // ちょんちょん跳ねて歩く・ついばむ
            m.t -= dt;
            if (m.t <= 0f)
            {
                m.t = crow ? R(0.8f, 2f) : R(0.4f, 1.2f);
                float a = R(-90f, 90f);
                m.fwd = (Quaternion.AngleAxis(a, Vector3.up) * m.fwd).normalized;
                Vector3 toHome = (m.group.path.Count > 0 ? m.group.path[m.landIndex] : m.home) - m.pos;
                toHome.y = 0f;
                if (toHome.magnitude > 4f * (crow ? 3f : 1f)) m.fwd = toHome.normalized;
                m.target = m.pos + m.fwd * (crow ? R(1.5f, 3f) : R(0.5f, 1.2f));
                // 急な所（キノコのかさのふちなど）へは行かない
                if (Physics.Raycast(m.target + Vector3.up * 3f, Vector3.down, out var th, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                    && (th.normal.y < 0.85f || Mathf.Abs(th.point.y - m.pos.y) > 0.6f * m.scale))
                    m.target = m.pos;
                m.resting = R(0f, 1f) < 0.4f;   // ついばむ
                if (crow && R(0f, 1f) < 0.07f) Caw(m, 0.6f);
            }
            if (!m.resting)
            {
                Vector3 d = m.target - m.pos;
                d.y = 0f;
                float step = (crow ? 2.5f : 2.2f) * dt;
                if (d.magnitude > step) m.pos += d.normalized * step;
                SnapToSurface(m, 3f, 6f);
            }
        }

        /// <summary>鳥が下りられる平らな場所（中心のまわりで、いちばん平らで上を向いた所）。</summary>
        static bool FlatLanding(Vector3 around, float spread, out Vector3 spot)
        {
            spot = around;
            float best = -1f;
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.39996f;
                float r = i == 0 ? 0f : spread * Mathf.Sqrt(i / 8f);
                Vector3 p = around + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 120f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y > best)
                {
                    best = hit.normal.y;
                    spot = hit.point;
                }
                if (best > 0.97f) break;
            }
            return best > 0.85f;
        }

        void TakeOff(Mob m, bool crow)
        {
            Vector3 dest;
            var spots = m.group.path;
            if (spots.Count > 1)
            {
                int next = (m.landIndex + 1 + _rng.Next(spots.Count - 1)) % spots.Count;
                m.landIndex = next;
                dest = spots[next] + new Vector3(R(-1.5f, 1.5f), 0f, R(-1.5f, 1.5f)) * (crow ? 2f : 1f);
            }
            else
            {
                float a = R(0f, Mathf.PI * 2f);
                dest = m.home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(6f, 14f);
            }
            if (FlatLanding(dest, crow ? 3f : 1.5f, out var spot)) dest = spot;
            else if (spots.Count > 0 && FlatLanding(spots[m.landIndex], crow ? 3f : 1.5f, out spot)) dest = spot;
            m.from = m.pos;
            m.to = dest;
            m.t = 0f;
            float dist = Vector3.Distance(m.from, m.to);
            m.dur = Mathf.Max(2.5f, dist / (crow ? 9f : 7f));
            m.arc = crow ? R(22f, 32f) : R(10f, 16f);
            m.airborne = true;
            if (crow) Caw(m, 0.9f);
        }

        /// <summary>カラスの鳴き声（カメラから遠いほど小さく）。</summary>
        void Caw(Mob m, float loud)
        {
            Camera cam = Camera.main;
            if (cam == null || !Application.isPlaying) return;
            float dist = Vector3.Distance(cam.transform.position, m.pos);
            float vol = Mathf.Clamp01(1f - dist / 70f) * loud;
            if (vol > 0.05f) AudioManager.Instance?.Caw(vol);
        }

        void UpdateSkater(Mob m, float dt)
        {
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                m.timer = R(0.7f, 2f);
                float a = R(0f, Mathf.PI * 2f);
                Vector3 imp = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * m.sp.speed;
                Vector3 toHome = m.home - m.pos;
                toHome.y = 0f;
                if (toHome.magnitude > m.radius) imp = toHome.normalized * m.sp.speed;
                m.vel = imp;
            }
            m.vel *= 1f - Mathf.Clamp01(1.8f * dt);
            Vector3 next = m.pos + m.vel * dt;
            float wl = _area.WaterLevelAt(next.x, next.z);
            if (wl < _area.Height(next.x, next.z) + 0.05f)
            {
                m.vel = -m.vel;
                next = m.pos;
            }
            m.pos = new Vector3(next.x, m.home.y, next.z);
            if (m.vel.sqrMagnitude > 0.01f) m.fwd = Vector3.Slerp(m.fwd, m.vel.normalized, dt * 6f);
            m.up = Vector3.up;
        }

        void UpdateHover(Mob m, float dt)
        {
            bool firefly = m.sp.id == "firefly";
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                m.timer = firefly ? R(2f, 4f) : R(1.2f, 3f);
                m.target = m.home + new Vector3(R(-m.radius, m.radius), 0f, R(-m.radius, m.radius));
            }
            float ground = GroundOrWater(m.target);
            Vector3 goal = new Vector3(m.target.x, ground + m.height + Mathf.Sin(m.anim * 1.3f + m.phase) * 0.5f, m.target.z);
            Vector3 d = goal - m.pos;
            float speed = firefly ? m.sp.speed : m.sp.speed * (d.magnitude > 1f ? 1f : 0.3f);
            Vector3 want = Vector3.ClampMagnitude(d * 2f, speed) + Wind.At(m.pos) * (firefly ? 0.3f : 0.15f);
            m.vel = Vector3.Lerp(m.vel, want, 1f - Mathf.Exp(-(firefly ? 2f : 6f) * dt));
            m.pos += m.vel * dt;
            Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
            if (hv.sqrMagnitude > 0.04f) m.fwd = Vector3.Slerp(m.fwd, hv.normalized, dt * 5f);
            m.up = Vector3.up;
            m.airborne = true;
        }

        // ------------------------------------------------------------------
        // 描画
        // ------------------------------------------------------------------
        void Add(Mesh mesh, Material mat, Matrix4x4 mtx)
        {
            if (mesh == null || mat == null) return;
            var key = (mesh, mat);
            if (!_draw.TryGetValue(key, out var list))
            {
                list = new List<Matrix4x4>();
                _draw[key] = list;
            }
            list.Add(mtx);
        }

        void Draw(Vector3 camPos)
        {
            foreach (var l in _draw.Values) l.Clear();
            foreach (var m in _mobs)
            {
                float d2 = (m.pos - camPos).sqrMagnitude;
                if (d2 > 110f * 110f) continue;
                Vector3 f = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, Vector3.forward);
                Quaternion rot = Quaternion.LookRotation(-f, m.up);
                float s = m.scale;
                if (m.joy > 0f) s *= 1f + 0.22f * Mathf.Abs(Mathf.Sin(m.joy * Mathf.PI * 4f)) * m.joy;
                if (m.sp.kind == MobKind.Hopper && !m.airborne) s *= 1f + 0.03f * Mathf.Sin(m.anim * 3f + m.phase);
                Vector3 scale3 = Vector3.one * s;
                if (m.sp.id == "snail" && m.moveSpeed > 0.01f) scale3.z *= 1f + 0.06f * Mathf.Sin(m.anim * 2.4f);   // のびちぢみ
                if (m.sp.kind == MobKind.Hopper && m.airborne) scale3.z *= 1.12f;                                     // 跳ぶときは体がのびる
                if (m.sp.kind == MobKind.Stalker) rot *= Quaternion.Euler(0f, 0f, Mathf.Sin(m.anim * 1.1f + m.phase) * 4f * (1f - m.raise));   // 葉のようにゆらゆら
                if (m.sp.kind == MobKind.Bird && !m.airborne && m.resting)
                    rot *= Quaternion.Euler(-25f * Mathf.Max(0f, Mathf.Sin(m.anim * 9f)), 0f, 0f);
                Vector3 bob = Vector3.zero;
                if (m.sp.kind == MobKind.Bird && !m.airborne && !m.resting) bob = m.up * (Mathf.Abs(Mathf.Sin(m.anim * 9f)) * 0.12f * s);
                else if (m.sp.rig != null && m.moveSpeed > 0.02f) bob = m.up * (Mathf.Abs(Mathf.Sin(m.gait)) * 0.012f * s);   // 歩くと体が少し上下する
                Matrix4x4 body = Matrix4x4.TRS(m.pos + bob, rot, scale3);

                string bodyMesh = m.sp.id == "pillbug" && m.curled > 0f ? "PillBug_Ball" : m.sp.body;
                Add(M(bodyMesh), assets.creature, body);

                // 脚（遠くは省く）
                if (m.sp.rig != null && d2 < 60f * 60f && !(m.sp.id == "pillbug" && m.curled > 0f))
                {
                    var legs = CreatureRig.Legs(m.sp.rig);
                    float moving = Mathf.Clamp01(m.moveSpeed / Mathf.Max(0.02f, m.sp.speed * 0.25f));
                    float swing = m.sp.legSwing * moving;
                    float lift = m.sp.legLift * moving;
                    float gait = m.gait;
                    if (moving < 0.05f)
                    {
                        // 止まっているときも、ときどき足をもぞもぞ
                        swing = 3f;
                        lift = 2f;
                        gait = m.anim * 1.7f + m.phase;
                    }
                    if (m.airborne) { swing = 0f; lift = 10f; gait = 0f; }
                    var kind = m.sp.gait;
                    for (int li = 0; li < legs.Length; li++)
                    {
                        Mesh lm = M(legs[li].mesh + m.sp.legSuffix);
                        if (lm == null) lm = M(legs[li].mesh);
                        for (int side = 0; side < 2; side++)
                            Add(lm, assets.creature, body * CreatureRig.LegMatrix(legs[li], li, side == 1, gait, swing, lift, kind, legs.Length));
                    }
                }

                // レアのいきもののまわりには、光のつぶがくるくる回る（気づきやすいように）
                if (m.sp.IsRare)
                {
                    Mesh spark = M("Firefly_Glow");
                    for (int k = 0; k < 3; k++)
                    {
                        float a = m.anim * 2.2f + k * 2.094f;
                        Vector3 o = new Vector3(Mathf.Cos(a) * 0.35f, 0.25f + 0.08f * Mathf.Sin(a * 1.7f), Mathf.Sin(a) * 0.35f);
                        Add(spark, assets.creatureGlow, Matrix4x4.TRS(m.pos + o * m.scale, Quaternion.identity, Vector3.one * 0.45f));
                    }
                }
                bool farParts = d2 > 75f * 75f;
                for (int pi = 0; pi < m.sp.parts.Length && !farParts; pi++)
                {
                    var part = m.sp.parts[pi];
                    Mesh pm = M(part.mesh);
                    if (pm == null) continue;
                    if (part.glow)
                    {
                        float pulse = 0.55f + 0.6f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(m.anim * 2.4f + m.phase)), 3f);
                        Add(pm, assets.creatureGlow, body * Matrix4x4.Scale(Vector3.one * pulse));
                        continue;
                    }
                    if (part.fixedPart)
                    {
                        Add(pm, assets.creature, body);
                        continue;
                    }
                    if (part.onlyCarrying)
                    {
                        if (m.carrying) Add(pm, assets.creature, body * Matrix4x4.Translate(part.offset));
                        continue;
                    }
                    if (part.mesh == "Mantis_Arm")
                    {
                        // かまを持ち上げる（近づくと高く）
                        float raise = m.raise * 62f + Mathf.Sin(m.anim * 2f + m.phase) * 3f;
                        for (int side = 0; side < 2; side++)
                        {
                            float mm = side == 0 ? 1f : -1f;
                            var local = Matrix4x4.TRS(new Vector3(part.offset.x * mm, part.offset.y, part.offset.z), Quaternion.Euler(raise, 0f, 0f), new Vector3(mm, 1f, 1f));
                            Add(pm, assets.creature, body * local);
                        }
                        continue;
                    }
                    if (part.mesh == "Grasshopper_Hind")
                    {
                        // 跳ぶときは後ろ足をけり出す
                        float kick = m.airborne ? -38f : 0f;
                        for (int side = 0; side < 2; side++)
                        {
                            float mm = side == 0 ? 1f : -1f;
                            var local = Matrix4x4.TRS(new Vector3(part.offset.x * mm, part.offset.y, part.offset.z), Quaternion.Euler(kick, 0f, 0f), new Vector3(mm, 1f, 1f));
                            Add(pm, assets.creature, body * local);
                        }
                        continue;
                    }
                    if (m.sp.kind == MobKind.Bird)
                    {
                        // 鳥の羽：地上では体の横にたたみ、飛ぶときは広げてはばたく（Blender で作った向きのまま変換）
                        float flap = part.flapAmp * Mathf.Sin((m.anim + m.phase) * part.flapHz * Mathf.PI * 2f);
                        if (m.airborne && m.vel.y < -1f) flap = 18f + 6f * Mathf.Sin(m.anim * 4f);   // 下りるときは滑空
                        Add(pm, assets.creatureWing, body * CreatureRig.BirdWing(m.sp.birdSize, true, !m.airborne, flap));
                        Add(pm, assets.creatureWing, body * CreatureRig.BirdWing(m.sp.birdSize, false, !m.airborne, flap));
                        continue;
                    }
                    float yaw = part.restYaw;
                    float roll = part.restRoll;
                    float flapS = Mathf.Sin((m.anim + m.phase) * part.flapHz * Mathf.PI * 2f + pi * 1.3f);
                    switch (m.sp.kind)
                    {
                        case MobKind.Flutter:
                            roll = m.airborne ? part.restRoll + part.flapAmp * flapS : 70f + 12f * Mathf.Sin(m.anim * 2f);
                            break;
                        default:
                            roll = part.flapAmp * flapS;
                            break;
                    }
                    int sides = part.pair ? 2 : 1;
                    for (int k = 0; k < sides; k++)
                    {
                        float side = k == 0 ? 1f : -1f;
                        var local = Matrix4x4.TRS(new Vector3(part.offset.x * side, part.offset.y, part.offset.z),
                                        Quaternion.Euler(0f, yaw * side, 0f) * Quaternion.Euler(0f, 0f, roll * side), new Vector3(side, 1f, 1f));
                        Add(pm, assets.creatureWing, body * local);
                    }
                }
            }
            int pool = 0;
            foreach (var kv in _draw)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                var rp = new RenderParams(kv.Key.Item2)
                {
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = new Bounds(camPos, Vector3.one * 400f),
                };
                for (int start = 0; start < list.Count; start += 1023)
                {
                    int n = Mathf.Min(1023, list.Count - start);
                    if (pool >= _pool.Count) _pool.Add(new Matrix4x4[1023]);
                    var arr = _pool[pool++];
                    list.CopyTo(start, arr, 0, n);
                    Graphics.RenderMeshInstanced(rp, kv.Key.Item1, 0, arr, n);
                }
            }
        }
    }
}

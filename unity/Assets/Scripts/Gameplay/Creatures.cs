using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// フィールドをうろつくいきもの（モブ）。アリの行列・はばたくチョウ・跳ねるバッタ・飛び立つ鳥など。
    /// 数が多いので GameObject は作らず、毎フレーム行列を計算して GPU インスタンシングで描く。
    /// どれかの個体に近づくと、その種がいきもの図鑑に登録される。
    /// </summary>
    public class Creatures : MonoBehaviour
    {
        public WorldAssets assets;
        public bool Active { get; set; }
        public event Action<SpeciesDef> Discovered;

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
            // 水面・空中
            public Vector3 vel;
            public Vector3 target;
            public bool resting;
            public float joy;
        }

        readonly List<Mob> _mobs = new List<Mob>();
        readonly Dictionary<(Mesh, Material), List<Matrix4x4>> _draw = new Dictionary<(Mesh, Material), List<Matrix4x4>>();
        readonly List<Matrix4x4[]> _pool = new List<Matrix4x4[]>();
        System.Random _rng = new System.Random(1);
        AreaLayout _area = Areas.Forest;
        int _frame;

        public int MobCount => _mobs.Count;

        float R(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

        public int CountOf(string species)
        {
            int n = 0;
            foreach (var m in _mobs) if (m.sp.id == species) n++;
            return n;
        }

        public Vector3 PositionOf(string species, int index = 0)
        {
            int n = 0;
            foreach (var m in _mobs)
            {
                if (m.sp.id != species) continue;
                if (n++ == index) return m.pos;
            }
            return Vector3.zero;
        }

        public bool IsAirborne(string species, int index = 0)
        {
            int n = 0;
            foreach (var m in _mobs)
            {
                if (m.sp.id != species) continue;
                if (n++ == index) return m.airborne;
            }
            return false;
        }

        public IEnumerable<string> SpeciesHere()
        {
            var seen = new HashSet<string>();
            foreach (var m in _mobs)
                if (seen.Add(m.sp.id)) yield return m.sp.id;
        }

        public static bool IsDiscovered(string species) => SaveSystem.Data.creatures.Contains(species);

        public static int DiscoveredCount => SaveSystem.Data.creatures.Count;

        public void Clear() => _mobs.Clear();

        // ------------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------------
        public void Build(WorldGenerator world)
        {
            _mobs.Clear();
            _area = world.Area;
            _rng = new System.Random(world.seed + 31 * world.Area.Id.Length);
            foreach (var g in world.Mobs)
            {
                var sp = SpeciesCatalog.Get(g.species);
                if (sp == null) continue;
                float pathLen = PathLength(g.path);
                for (int i = 0; i < g.count; i++)
                {
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
                    _mobs.Add(m);
                }
            }
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

        bool SnapToSurface(Mob m, float above, float below)
        {
            Vector3 up = m.up.sqrMagnitude > 0.5f ? m.up : Vector3.up;
            if (Physics.Raycast(m.pos + up * above, -up, out var hit, above + below, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                || Physics.Raycast(m.pos + Vector3.up * above, Vector3.down, out hit, above + below, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                m.pos = hit.point;
                m.up = Vector3.Slerp(m.up, hit.normal, 0.35f).normalized;
                return true;
            }
            m.pos.y = _area.Height(m.pos.x, m.pos.z);
            m.up = Vector3.Slerp(m.up, Vector3.up, 0.2f);
            return false;
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
                m.anim += dt;
                bool near = (m.pos - camPos).sqrMagnitude < 45f * 45f;
                switch (m.sp.kind)
                {
                    case MobKind.Marcher:
                        if (m.group.path.Count > 1) UpdateMarcher(m, dt, near);
                        else UpdateCrawler(m, dt, near, i);
                        break;
                    case MobKind.Crawler: UpdateCrawler(m, dt, near, i); break;
                    case MobKind.Flutter: UpdateFlutter(m, dt); break;
                    case MobKind.Hopper: UpdateHopper(m, dt, head); break;
                    case MobKind.Bird: UpdateBird(m, dt, head); break;
                    case MobKind.Skater: UpdateSkater(m, dt); break;
                    case MobKind.Hover: UpdateHover(m, dt); break;
                }
                if (m.joy > 0f) m.joy = Mathf.Max(0f, m.joy - dt * 0.8f);

                if (Active && !IsDiscovered(m.sp.id))
                {
                    Vector3 c = m.pos + m.up * (0.3f * m.scale);
                    if ((head - c).sqrMagnitude < m.sp.discoverRadius * m.sp.discoverRadius) Discover(m.sp);
                }
            }
            Draw(camPos);
        }

        public void Discover(SpeciesDef sp)
        {
            if (IsDiscovered(sp.id)) return;
            SaveSystem.Data.creatures.Add(sp.id);
            SaveSystem.Save();
            foreach (var m in _mobs)
                if (m.sp == sp) m.joy = 1f;
            Discovered?.Invoke(sp);
        }

        void UpdateCrawler(Mob m, float dt, bool near, int index)
        {
            m.timer -= dt;
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
            Vector3 move = m.sp.sideways ? Vector3.Cross(m.up, m.fwd) : m.fwd;
            m.pos += move * (m.sp.speed * m.speedMul * dt);
            if (near || (index + _frame) % 4 == 0) SnapToSurface(m, 0.8f, 2.5f);
            m.fwd = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, m.fwd);
        }

        void UpdateMarcher(Mob m, float dt, bool near)
        {
            m.pathS += m.sp.speed * dt * (0.9f + 0.2f * Mathf.Sin(m.phase));
            Vector3 p = PathPoint(m.group.path, m.pathS, out var dir);
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
            Vector3 v = Vector3.ClampMagnitude(d * 1.5f, m.sp.speed);
            m.pos += v * dt;
            Vector3 hv = new Vector3(v.x, 0f, v.z);
            if (hv.sqrMagnitude > 0.01f) m.fwd = Vector3.Slerp(m.fwd, hv.normalized, dt * 4f);
            m.up = Vector3.up;
            m.airborne = !m.resting || d.magnitude > 0.3f;
        }

        void UpdateHopper(Mob m, float dt, Vector3 head)
        {
            bool frog = m.sp.id == "frog";
            if (m.airborne)
            {
                m.t += dt / m.dur;
                float k = Mathf.Clamp01(m.t);
                m.pos = Vector3.Lerp(m.from, m.to, k) + Vector3.up * (m.arc * 4f * k * (1f - k));
                if (k >= 1f)
                {
                    m.airborne = false;
                    m.pos = m.to;
                    SnapToSurface(m, 1f, 3f);
                }
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
                if (_area.IsUnderwater(hit.point) || !_area.InPlayArea(hit.point)) continue;
                m.from = m.pos;
                m.to = hit.point;
                m.t = 0f;
                m.dur = frog ? 0.45f : R(0.6f, 0.85f);
                m.arc = frog ? 0.7f : R(1.2f, 2f);
                m.airborne = true;
                m.fwd = dir;
                break;
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
            if (Physics.Raycast(dest + Vector3.up * 60f, Vector3.down, out var hit, 120f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                dest = hit.point;
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
            Vector3 v = Vector3.ClampMagnitude(d * 2f, speed);
            m.pos += v * dt;
            Vector3 hv = new Vector3(v.x, 0f, v.z);
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
                if ((m.pos - camPos).sqrMagnitude > 110f * 110f) continue;
                Vector3 f = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, Vector3.forward);
                Quaternion rot = Quaternion.LookRotation(-f, m.up);
                float s = m.scale;
                if (m.joy > 0f) s *= 1f + 0.22f * Mathf.Abs(Mathf.Sin(m.joy * Mathf.PI * 4f)) * m.joy;
                if (m.sp.kind == MobKind.Hopper && !m.airborne) s *= 1f + 0.03f * Mathf.Sin(m.anim * 3f + m.phase);
                if (m.sp.kind == MobKind.Bird && !m.airborne && m.resting)
                    rot = rot * Quaternion.Euler(-25f * Mathf.Max(0f, Mathf.Sin(m.anim * 9f)), 0f, 0f);
                Vector3 bob = m.sp.kind == MobKind.Bird && !m.airborne && !m.resting ? m.up * (Mathf.Abs(Mathf.Sin(m.anim * 9f)) * 0.12f * s) : Vector3.zero;
                Matrix4x4 body = Matrix4x4.TRS(m.pos + bob, rot, Vector3.one * s);
                Add(assets.Get(m.sp.body), assets.creature, body);
                for (int pi = 0; pi < m.sp.parts.Length; pi++)
                {
                    var part = m.sp.parts[pi];
                    Mesh pm = assets.Get(part.mesh);
                    if (part.glow)
                    {
                        float pulse = 0.55f + 0.6f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(m.anim * 2.4f + m.phase)), 3f);
                        Add(pm, assets.creatureGlow, body * Matrix4x4.Scale(Vector3.one * pulse));
                        continue;
                    }
                    float yaw = part.restYaw;
                    float roll = part.restRoll;
                    float flap = Mathf.Sin((m.anim + m.phase) * part.flapHz * Mathf.PI * 2f + pi * 1.3f);
                    switch (m.sp.kind)
                    {
                        case MobKind.Flutter:
                            roll = m.airborne ? part.restRoll + part.flapAmp * flap : 70f + 12f * Mathf.Sin(m.anim * 2f);
                            break;
                        case MobKind.Bird:
                            if (m.airborne)
                            {
                                yaw = 0f;
                                roll = part.flapAmp * flap;
                            }
                            break;
                        default:
                            roll = part.flapAmp * flap;
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// フィールドをうろつくいきもの（モブ）。アリの行列・はばたくチョウ・跳ねるバッタ・飛び立つ鳥など。
    /// 数が多いので GameObject は作らず、毎フレーム行列を計算して GPU インスタンシングで描く。
    /// 脚は 1 本ずつ別の部品で、進んだ距離（と、その場で回った分）に合わせて交互に動かす。
    /// どれかの個体に近づくと、その種がいきもの図鑑に登録される。
    /// 大きないきもの（かたつむり・カブトムシ・カエル・カニ・カマキリ）には当たり判定があり、登って乗れる。
    ///
    /// 動きのくふう：向きはなめらかに回り、速さは加速・減速する。前が水・遊べる範囲の外・急な壁（登らない種類）
    /// ならよけ、仲間やしゃくとりむしとぶつからない。種ごとに、本物らしいくせ（跳ぶ前にかがむ、鳥は群れで
    /// 飛び立つ、アメンボは水をこいで進む、ホタルは光をそろえる…）をつけている。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class Creatures : MonoBehaviour
    {
        public WorldAssets assets;
        public bool Active { get; set; }
        public event Action<SpeciesDef> Discovered;
        /// <summary>見つけたいきものと、その場所（演出用）。</summary>
        public event Action<SpeciesDef, Vector3> DiscoveredAt;

        /// <summary>いま動いているいきもの（しゃくとりむしが、まわりのいきものに反応するため）。</summary>
        public static Creatures Instance { get; private set; }

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
            public Vector3 prevFwd = Vector3.forward;
            public float gait;
            public float moveSpeed;
            // カマキリ・クモ・だんごむし
            public float raise;
            public float curled;
            // 当たり判定（乗れるいきもの）
            public Transform collider;

            // ---- 動きのくふう ----
            public float curSpeed;                     // いまの速さ（加速・減速する）
            public Vector3 wantFwd = Vector3.forward;  // 向きたい方向（なめらかに回る）
            public float turnRate;                     // 回っている速さ（度/秒）
            public float pace = 1f;                    // 一匹ずつちがう速さ
            public float pauseUntil;                   // 少し立ち止まる
            public float slowUntil;                    // 少しゆっくり（アリのあいさつ）
            public float greetNext;
            public float glance, glanceTarget, glanceNext;   // 止まっているときに、きょろきょろ
            public float crouch;                       // 跳ぶ前にかがむ・着地でつぶれる
            public float launchAt = -1f;               // かがんでから跳ぶ（飛び立つ）時刻
            public Vector3 launchTo;
            public float launchPeak;
            public float bank, pitch;                  // 飛ぶもののかたむき
            public float strike, strikeNext;           // カマキリのかま
            public float retreat;                      // かたつむりが殻にひっこむ
            public float stray, strayTarget, strayNext;   // 行列から少しはずれる
            public float blink;                        // ホタルの光の位相
            public float fallVel;                      // 足場がなくて落ちる
            public float display;                      // カブトムシの角・オトシブミのうなずき
            public bool carryingWorm;                  // しゃくとりむしを乗せている
            public float alarmAt = -1f;                // 群れの仲間が逃げた（少しおくれて飛び立つ）
            public float rowPhase;                     // アメンボのこぐ位相
            public float probeNext;                    // 前の安全確認
            public float sideSign = 1f;                // カニの横歩きの向き
            public float watchTime;                    // クモがしゃくとりむしを見つめている時間
            public float hopNext;
            public float walkUntil;                    // バッタが少し歩く
            public Mob chase;                          // チョウの追いかけっこの相手
            public float chaseUntil;
            public Vector3 drawPos;
            public float flare;                        // 鳥の着地前のはばたき
            public Vector3 bow;                        // 鳥の飛ぶ道のふくらみ
            public float openWings;                    // チョウが羽を大きく開く
            public float spin;                         // 丸まっただんごむしの回転
            public bool perched;                       // トンボがとまっている
            public int patrol = 1;                     // トンボの見回りの向き
            // ---- 第 2 弾 ----
            public float accel, prevCurSpeed;          // 加速度（体を前後にかたむける）
            public float settleUntil;                  // 止まったあと、脚をそろえる
            public bool wasMoving;
            public float chainAt = -1f;                // となりがおどろいた（少しおくれておどろく）
            public float hurryUntil;                   // あわてて急ぐ（アリ）
            public float lat;                          // 行列の左右のずれ
            public float emerge = 1f;                  // かたつむりが殻から出てくる
            public float wobble;                       // 着地のふらつき
            public float watchUntil;                   // 逃げたあと、しゃくとりむしを見張る
            public bool fledJump;
            public float flapBoost;                    // チョウの飛び立ち・着地のはばたき
            public float jitterNext;
            public Vector3 jitter;
            public float puffUntil, puffNext;          // 鳥が羽をふくらませる
            public float alertK;                       // 鳥の警戒
            public bool sideHopped;                    // カラスの横っとび
            public Vector3 windKick;                   // 鳥は風に向かって飛び立つ
            public float circleR;                      // カラスの旋回
            public float deadUntil;                    // てんとうむしの死んだふり
            public float spinUntil;                    // てんとうむしが高い所でくるくる回る
            public float rock;                         // だんごむしのゆれ
            public bool hesitated;                     // だんごむしのためらい
            public float displayHold;                  // カブトムシが角を見せている時間
            public float groomUntil, groomNext;        // クモ・カマキリの手入れ
            public float backNext;                     // クモの後ずさり
            public Vector3 lineFrom;                   // クモの命綱
            public float lineUntil;
            public int blockCount;
            public Mob prey;                           // トンボが追いかける相手
            public float preyUntil;
            public float droop;                        // とまったトンボの羽がたれる
            public bool wasCarryingWorm;
        }

        public struct MobInfo
        {
            public Vector3 pos, up, fwd, vel;
            public bool airborne, carrying, curled, resting, perched, carryingWorm;
            public float raise, gait, moveSpeed, curSpeed, crouch, strike, bank, retreat, glow, display, turnRate, alert;
            public bool playingDead, hurrying, lineVisible;
            public string species;
        }

        readonly List<Mob> _mobs = new List<Mob>();
        readonly Dictionary<MobGroup, List<Mob>> _groups = new Dictionary<MobGroup, List<Mob>>();
        readonly Dictionary<MobGroup, float> _pathLen = new Dictionary<MobGroup, float>();
        readonly List<Vector3> _flowers = new List<Vector3>();
        readonly Dictionary<(Mesh, Material), List<Matrix4x4>> _draw = new Dictionary<(Mesh, Material), List<Matrix4x4>>();
        readonly List<Matrix4x4[]> _pool = new List<Matrix4x4[]>();
        readonly Dictionary<string, Mesh> _meshCache = new Dictionary<string, Mesh>();
        System.Random _rng = new System.Random(1);
        AreaLayout _area = Areas.Forest;
        Transform _colliderRoot;
        int _frame;
        float _dt;
        Vector3 _head = new Vector3(9999f, 0f, 0f);
        bool _wormStanding;

        public int MobCount => _mobs.Count;

        float R(float a, float b) => a + (b - a) * (float)_rng.NextDouble();
        float Sign() => _rng.NextDouble() < 0.5 ? -1f : 1f;

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

        void OnEnable() => Instance = this;

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
                pos = m.pos, up = m.up, fwd = m.fwd, vel = m.vel, airborne = m.airborne, carrying = m.carrying, curled = m.curled > 0f,
                resting = m.resting, perched = m.perched, carryingWorm = m.carryingWorm,
                raise = m.raise, gait = m.gait, moveSpeed = m.moveSpeed, curSpeed = m.curSpeed, crouch = m.crouch, strike = m.strike,
                bank = m.bank, retreat = m.retreat, glow = FireflyGlow(m), display = m.display, turnRate = m.turnRate, species = m.sp.id,
                alert = m.alertK, playingDead = m.anim < m.deadUntil, hurrying = m.anim < m.hurryUntil,
                lineVisible = m.sp.kind == MobKind.Pouncer && (m.airborne || m.anim < m.lineUntil),
            };
        }

        /// <summary>テスト用：個体の位置を動かす。</summary>
        public void SetPosition(string species, int index, Vector3 p)
        {
            var m = Find(species, index);
            if (m == null) return;
            m.pos = p;
            m.prevPos = p;
            m.drawPos = p;
            m.home = p;
        }

        /// <summary>テスト用：個体の向きを変える。</summary>
        public void SetForward(string species, int index, Vector3 f)
        {
            var m = Find(species, index);
            if (m == null || f.sqrMagnitude < 1e-6f) return;
            m.fwd = m.wantFwd = m.prevFwd = f.normalized;
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

        /// <summary>
        /// いちばん近いいきもの（predatorsOnly なら鳥とカマキリだけ）。しゃくとりむしを乗せているものは数えない。
        /// </summary>
        public bool NearestMob(Vector3 p, float radius, bool predatorsOnly, out Vector3 pos, out float speed, out SpeciesDef sp)
        {
            pos = default;
            speed = 0f;
            sp = null;
            float best = radius * radius;
            foreach (var m in _mobs)
            {
                if (m.carryingWorm) continue;
                if (predatorsOnly && m.sp.kind != MobKind.Bird && m.sp.kind != MobKind.Stalker) continue;
                float d2 = (m.pos - p).sqrMagnitude;
                if (d2 >= best) continue;
                best = d2;
                pos = m.pos;
                speed = Mathf.Max(m.moveSpeed, m.airborne ? m.vel.magnitude : 0f, m.strike * 3f);
                sp = m.sp;
            }
            return sp != null;
        }

        /// <summary>前脚を上げてこちらを見つめているハエトリグモ（しゃくとりむしが見つめ返す）。</summary>
        public bool WatchedBySpider(Vector3 p, float radius, out Vector3 pos)
        {
            pos = default;
            float best = radius * radius;
            bool found = false;
            foreach (var m in _mobs)
            {
                if (m.sp.kind != MobKind.Pouncer || m.raise < 0.5f) continue;
                float d2 = (m.pos - p).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    pos = m.pos;
                    found = true;
                }
            }
            return found;
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
            _groups.Clear();
            _pathLen.Clear();
            _flowers.Clear();
            if (_colliderRoot != null)
            {
                if (Application.isPlaying) Destroy(_colliderRoot.gameObject);
                else DestroyImmediate(_colliderRoot.gameObject);
                _colliderRoot = null;
            }
        }

        void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------------
        public void Build(WorldGenerator world)
        {
            Clear();
            _meshCache.Clear();
            _area = world.Area;
            _rng = new System.Random(world.seed + 31 * world.Area.Id.Length);
            _flowers.AddRange(world.FlowerPoints);
            // レアは遊ぶたびにちがう（決まった場所にいつもいるわけではない）
            var rareRng = new System.Random(Environment.TickCount ^ world.Area.Id.GetHashCode());
            foreach (var g in world.Mobs)
            {
                var baseSp = SpeciesCatalog.Get(g.species);
                if (baseSp == null) continue;
                var rare = SpeciesCatalog.RareVariantOf(baseSp.id);
                float pathLen = PathLength(g.path);
                _pathLen[g] = pathLen;
                var members = new List<Mob>();
                _groups[g] = members;
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
                        pace = R(0.9f, 1.1f),
                        blink = R(0f, Mathf.PI * 2f),
                        gait = R(0f, Mathf.PI * 2f),   // 脚の動きは、一匹ずつずらす
                    };
                    float a = R(0f, Mathf.PI * 2f);
                    m.fwd = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    if (sp.kind == MobKind.Marcher && g.path.Count > 1)
                    {
                        m.pathS = pathLen * i / g.count;
                        m.pos = SplinePoint(g.path, m.pathS, pathLen, out m.fwd);
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
                    m.drawPos = m.pos;
                    m.wantFwd = m.fwd;
                    m.prevFwd = m.fwd;
                    if (sp.rideable) MakeCollider(m);
                    _mobs.Add(m);
                    members.Add(m);
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

        /// <summary>行列の道（ループ）の上の点。角がとがらないよう、なめらかな曲線（Catmull-Rom）でつなぐ。</summary>
        static Vector3 SplinePoint(List<Vector3> path, float s, float total, out Vector3 dir)
        {
            int n = path.Count;
            if (n < 2)
            {
                dir = Vector3.forward;
                return n == 1 ? path[0] : Vector3.zero;
            }
            s = Mathf.Repeat(s, Mathf.Max(total, 0.001f));
            for (int i = 0; i < n; i++)
            {
                Vector3 a = path[i];
                Vector3 b = path[(i + 1) % n];
                float l = Vector3.Distance(a, b);
                if (s <= l || i == n - 1)
                {
                    float u = l > 1e-5f ? Mathf.Clamp01(s / l) : 0f;
                    Vector3 p0 = path[(i - 1 + n) % n], p3 = path[(i + 2) % n];
                    Vector3 p = CatmullRom(p0, a, b, p3, u);
                    Vector3 d = CatmullRomTangent(p0, a, b, p3, u);
                    dir = d.sqrMagnitude > 1e-6f ? d.normalized : ((b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector3.forward);
                    return p;
                }
                s -= l;
            }
            dir = Vector3.forward;
            return path[0];
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            return 0.5f * ((-p0 + p2) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t + 3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t * t);
        }

        /// <summary>
        /// 面にのせる。壁登りをしないいきもの（鳥・カエル・カマキリなど）は、体をほとんどかたむけない。
        /// 小さなでこぼこでは高さをなめらかに合わせ（がたがたしない）、足もとに何もなければ重力で落ちる。
        /// </summary>
        bool SnapToSurface(Mob m, float above, float below, bool canFall = false)
        {
            Vector3 up = m.sp.climbs && m.up.sqrMagnitude > 0.5f ? m.up : Vector3.up;
            if (Physics.Raycast(m.pos + up * above, -up, out var hit, above + below, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                || Physics.Raycast(m.pos + Vector3.up * above, Vector3.down, out hit, above + below, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 delta = hit.point - m.pos;
                float dn = Vector3.Dot(delta, up);
                if (canFall && m.fallVel > 0f && dn < -0.02f)
                {
                    // 落ちている途中：重力で落ちて、面にとどいたら着地
                    m.fallVel += Gravity * _dt;
                    float step = m.fallVel * _dt;
                    if (step < -dn)
                    {
                        m.pos -= up * step;
                        return true;
                    }
                    m.fallVel = 0f;
                    m.crouch = Mathf.Max(m.crouch, 0.5f);
                    m.pos = hit.point;
                }
                else if (Mathf.Abs(dn) < 0.08f * Mathf.Max(1f, m.scale) && _dt > 0f && Application.isPlaying)
                    m.pos += (delta - up * dn) + up * (dn * Mathf.Clamp01(_dt * 18f));   // 小さなでこぼこは、なめらかに
                else if (canFall && dn < -0.6f * Mathf.Max(1f, m.scale) && _dt > 0f && Application.isPlaying)
                {
                    // 段の上から足をふみはずした：落ちはじめる
                    m.fallVel = 0.01f;
                    m.pos -= up * 0.01f;
                    return true;
                }
                else m.pos = hit.point;
                m.fallVel = 0f;
                m.up = m.sp.climbs ? Vector3.Slerp(m.up, hit.normal, 0.35f).normalized : UprightUp(hit.normal);
                return true;
            }
            // 下に何も見つからない：地面まで重力で落ちる
            float ground = _area.Height(m.pos.x, m.pos.z);
            if (canFall && _dt > 0f && Application.isPlaying && m.pos.y - ground > 0.05f)
            {
                m.fallVel += Gravity * _dt;
                m.pos.y = Mathf.Max(ground, m.pos.y - m.fallVel * _dt);
            }
            else
            {
                m.pos.y = ground;
                m.fallVel = 0f;
            }
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

        bool OnWater(Vector3 p) => _area.WaterLevelAt(p.x, p.z) > _area.Height(p.x, p.z) + 0.05f;

        /// <summary>川の流れ（川辺だけ）。北から南（-Z）へ流れ、よどみではほとんど流れない。</summary>
        Vector3 FlowAt(Vector3 p)
        {
            if (_area != Areas.River || !RiverLayout.InChannel(p.x, p.z, 0f)) return Vector3.zero;
            const float dz = 0.5f;
            float dcx = (RiverLayout.CenterX(p.z + dz) - RiverLayout.CenterX(p.z - dz)) / (2f * dz);
            Vector3 down = new Vector3(-dcx, 0f, -1f).normalized;
            float pool = ShakuMath.Bump(p.z - RiverLayout.PoolZ, 15f);
            return down * 0.25f * (1f - 0.8f * pool);
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
            _head = worm != null ? worm.HeadPosition : new Vector3(9999f, 0f, 0f);
            _wormStanding = worm != null && worm.IsStanding;
            Transform wormPlatform = worm != null ? worm.PlatformUnder : null;
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            UpdateFireflySync(dt);
            for (int i = 0; i < _mobs.Count; i++)
            {
                var m = _mobs[i];
                float d2 = (m.pos - camPos).sqrMagnitude;
                // 遠くのいきものは、間引いて動かす（描く位置は毎フレームなめらかに追いつかせる）。
                // しゃくとりむしの近くのものは、カメラが遠くても毎フレーム動かす
                float dh2 = (m.pos - _head).sqrMagnitude;
                bool far = d2 > 70f * 70f && dh2 > 30f * 30f;
                if (far && (i + _frame) % 3 != 0)
                {
                    m.drawPos = Vector3.Lerp(m.drawPos, m.pos, 0.5f);
                    continue;
                }
                float sdt = far ? dt * 3f : dt;
                _dt = sdt;
                m.anim += sdt;
                bool near = d2 < 45f * 45f;
                m.carryingWorm = wormPlatform != null && m.collider == wormPlatform;
                switch (m.sp.kind)
                {
                    case MobKind.Marcher:
                        if (m.group.path.Count > 1) UpdateMarcher(m, sdt, near);
                        else UpdateCrawler(m, sdt, near, i);
                        break;
                    case MobKind.Crawler: UpdateCrawler(m, sdt, near, i); break;
                    case MobKind.Flutter: UpdateFlutter(m, sdt); break;
                    case MobKind.Hopper: UpdateHopper(m, sdt, near, i); break;
                    case MobKind.Bird: UpdateBird(m, sdt); break;
                    case MobKind.Skater: UpdateSkater(m, sdt); break;
                    case MobKind.Hover: UpdateHover(m, sdt); break;
                    case MobKind.Pouncer: UpdatePouncer(m, sdt, near, i); break;
                    case MobKind.Stalker: UpdateStalker(m, sdt, near, i); break;
                }
                if (m.joy > 0f) m.joy = Mathf.Max(0f, m.joy - sdt * 0.8f);
                if (m.launchAt < 0f) m.crouch = Mathf.MoveTowards(m.crouch, 0f, sdt * 3f);
                // となりがおどろくと、少しおくれて自分もおどろく
                if (m.chainAt >= 0f && m.anim >= m.chainAt)
                {
                    m.chainAt = -1f;
                    DisturbOne(m);
                }
                // 乗せていたしゃくとりむしが落ちると、立ち止まってそちらを見る
                if (m.wasCarryingWorm && !m.carryingWorm && worm != null && worm.IsFalling)
                {
                    m.pauseUntil = m.anim + 1.2f;
                    Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, m.up);
                    m.glanceTarget = Mathf.Clamp(Vector3.SignedAngle(m.fwd, toW, m.up), -40f, 40f);
                }
                m.wasCarryingWorm = m.carryingWorm;
                m.wobble = Mathf.MoveTowards(m.wobble, 0f, sdt * 1.5f);
                m.rock = Mathf.MoveTowards(m.rock, 0f, sdt * 0.8f);
                // 近くのスズメはときどきさえずり、カエルはけろけろ鳴く
                if (near && Application.isPlaying && d2 < 25f * 25f)
                {
                    float vol = Mathf.Clamp01(1f - Mathf.Sqrt(d2) / 25f);
                    if (m.sp.id == "sparrow" && !m.airborne && R(0f, 1f) < sdt * 0.06f) AudioManager.Instance?.Chirp(0.35f * vol);
                    else if (m.sp.id == "frog" && !m.airborne && R(0f, 1f) < sdt * 0.04f) AudioManager.Instance?.Croak(0.4f * vol);
                }

                // 回る速さ（脚の動きと、飛ぶもののかたむきに使う）
                // （坂で体が前後にかたむいた分は数えない：面に沿った向きの変化だけ）
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(m.prevFwd, m.up), Vector3.ProjectOnPlane(m.fwd, m.up), m.up);
                m.turnRate = Mathf.Lerp(m.turnRate, yaw / sdt, 1f - Mathf.Exp(-8f * sdt));
                m.prevFwd = m.fwd;
                // 脚：進んだ距離と、その場で回った分だけ、歩く位相を進める
                Vector3 moved = m.pos - m.prevPos;
                moved -= m.up * Vector3.Dot(moved, m.up);
                float dist = moved.magnitude;
                m.moveSpeed = Mathf.Lerp(m.moveSpeed, dist / sdt, 1f - Mathf.Exp(-10f * sdt));
                // 脚のリズムは体の大きさに合わせる（大きいほど、ゆったり）
                if (!m.airborne && m.sp.kind != MobKind.Skater)
                    m.gait += (dist + Mathf.Abs(yaw) * Mathf.Deg2Rad * 0.12f * m.scale) / Mathf.Max(0.02f, m.sp.stride * m.scale) * Mathf.PI;
                // 止まったあとも、少しのあいだ脚をそろえる
                bool movingNow = m.moveSpeed > 0.03f;
                if (m.wasMoving && !movingNow && !m.airborne) m.settleUntil = m.anim + 0.3f;
                m.wasMoving = movingNow;
                if (m.anim < m.settleUntil) m.gait += sdt * 8f;
                // 加速度（加速すると前へ、ブレーキで後ろへ、体がかたむく）
                m.accel = Mathf.Lerp(m.accel, (m.curSpeed - m.prevCurSpeed) / sdt, 1f - Mathf.Exp(-6f * sdt));
                m.prevCurSpeed = m.curSpeed;
                m.prevPos = m.pos;
                UpdateGlance(m, sdt);
                UpdateTilt(m, sdt);
                m.drawPos = far ? Vector3.Lerp(m.drawPos, m.pos, 0.5f) : m.pos;

                if (Active && !IsDiscovered(m.sp.id))
                {
                    Vector3 c = m.pos + m.up * (0.3f * m.scale);
                    if ((_head - c).sqrMagnitude < m.sp.discoverRadius * m.sp.discoverRadius) Discover(m.sp, m.pos);
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
            {
                if (m.sp != sp) continue;
                m.joy = 1f;
                // 見つけられたいきものは、しゃくとりむしの方を向いて、少し止まる
                Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, m.up);
                if (toW.sqrMagnitude < 16f && toW.sqrMagnitude > 1e-4f && !m.airborne)
                {
                    m.wantFwd = toW.normalized;
                    m.pauseUntil = m.anim + 0.8f;
                }
            }
            Discovered?.Invoke(sp);
            DiscoveredAt?.Invoke(sp, where);
        }

        /// <summary>
        /// 大きな音（しゃくとりむしが落ちた・水しぶき）：近くのいきものがおどろく。
        /// 鳥は飛び立ち、バッタやカエルは跳び、チョウは舞い上がり、だんごむしは丸まり、かたつむりはひっこむ。
        /// </summary>
        public void Disturb(Vector3 p, float radius)
        {
            foreach (var m in _mobs)
            {
                if (m.carryingWorm) continue;
                // 水辺のいきもの（カエル・カニ・カワニナ・アメンボ）は、物音により強くおどろく
                bool waterside = m.sp.id == "frog" || m.sp.id == "crab" || m.sp.id == "riversnail" || m.sp.kind == MobKind.Skater;
                float r = radius * (waterside ? 1.5f : 1f);
                if ((m.pos - p).sqrMagnitude > r * r) continue;
                DisturbOne(m);
                // となりの仲間も、少しおくれておどろく
                foreach (var o in _mobs)
                    if (o != m && o.chainAt < 0f && !o.carryingWorm && (o.pos - p).sqrMagnitude > r * r && (o.pos - m.pos).sqrMagnitude < 1.5f * 1.5f)
                        o.chainAt = o.anim + R(0.2f, 0.4f);
            }
        }

        /// <summary>1 匹がおどろく（種ごとの反応）。</summary>
        void DisturbOne(Mob m)
        {
            switch (m.sp.kind)
            {
                case MobKind.Bird:
                    if (!m.airborne && m.launchAt < 0f) m.launchAt = m.anim + R(0.05f, 0.3f);
                    break;
                case MobKind.Hopper:
                    if (!m.airborne) m.timer = Mathf.Min(m.timer, R(0f, 0.3f));
                    break;
                case MobKind.Flutter:
                    if (m.resting) TakeWing(m);
                    break;
                case MobKind.Skater:
                    m.timer = 0f;
                    break;
                case MobKind.Marcher:
                    m.hurryUntil = m.anim + 3f;   // アリは、あわてて急ぐ
                    break;
                default:
                    if (m.sp.id == "pillbug") m.curled = Mathf.Max(m.curled, 3f);
                    else if (m.sp.id == "snail" || m.sp.id == "riversnail") m.retreat = Mathf.Max(m.retreat, 2.5f);
                    else if (m.sp.id == "ladybug") m.deadUntil = m.anim + 3f;   // てんとうむしは死んだふり
                    else m.pauseUntil = m.anim + R(0.5f, 1f);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 共通の動き
        // ------------------------------------------------------------------
        /// <summary>近くのしゃくとりむしをよける向き（ぶつからないように少しそれる）。</summary>
        static Vector3 Avoid(Mob m, Vector3 head, float radius)
        {
            Vector3 away = m.pos - head;
            away -= m.up * Vector3.Dot(away, m.up);
            float d = away.magnitude;
            if (d > radius || d < 1e-4f) return Vector3.zero;
            return away / d * (1f - d / radius);
        }

        /// <summary>同じ群れの仲間とぶつからないように、はなれる向き。</summary>
        Vector3 Separation(Mob m)
        {
            if (!_groups.TryGetValue(m.group, out var list) || list.Count < 2) return Vector3.zero;
            Vector3 push = Vector3.zero;
            float r = 0.35f * m.scale + 0.12f;
            foreach (var o in list)
            {
                if (o == m || o.airborne) continue;
                Vector3 d = m.pos - o.pos;
                d -= m.up * Vector3.Dot(d, m.up);
                float l = d.magnitude;
                if (l < r && l > 1e-4f) push += d / l * (1f - l / r);
            }
            return push;
        }

        /// <summary>前を横切る仲間がいるか。</summary>
        bool Crossing(Mob m)
        {
            if (!_groups.TryGetValue(m.group, out var list) || list.Count < 2) return false;
            float r = 0.5f * m.scale + 0.1f;
            foreach (var o in list)
            {
                if (o == m || o.airborne || o.moveSpeed < 0.05f) continue;
                Vector3 d = Vector3.ProjectOnPlane(o.pos - m.pos, m.up);
                float l = d.magnitude;
                if (l > r || l < 1e-4f) continue;
                if (Vector3.Dot(d / l, m.fwd) > 0.6f && Mathf.Abs(Vector3.Dot(o.fwd, m.fwd)) < 0.6f) return true;
            }
            return false;
        }

        /// <summary>大きないきものが、ほかの種類のいきものともぶつからないようにはなれる向き。</summary>
        Vector3 BigSeparation(Mob m)
        {
            Vector3 push = Vector3.zero;
            float r = 0.7f * m.scale;
            foreach (var o in _mobs)
            {
                if (o == m || o.airborne || o.group == m.group) continue;
                Vector3 d = Vector3.ProjectOnPlane(m.pos - o.pos, m.up);
                float l = d.magnitude;
                if (l < r && l > 1e-4f) push += d / l * (1f - l / r);
            }
            return push;
        }

        /// <summary>向きをなめらかに変える（回る速さに上限がある）。</summary>
        static void Steer(Mob m, Vector3 want, float dt, float maxDegPerSec)
        {
            want = Vector3.ProjectOnPlane(want, m.up);
            if (want.sqrMagnitude < 1e-6f) return;
            float ang = Vector3.SignedAngle(m.fwd, want, m.up);
            float step = Mathf.Clamp(ang, -maxDegPerSec * dt, maxDegPerSec * dt);
            m.fwd = (Quaternion.AngleAxis(step, m.up) * m.fwd).normalized;
        }

        /// <summary>
        /// 前は安全か：遊べる範囲の中で、水の中ではない。壁登りをしない種類は、急な壁や高い段・がけの先へは行かない。
        /// </summary>
        bool SafeAhead(Mob m, Vector3 dir, float dist)
        {
            Vector3 p = m.pos + dir * dist;
            if (!_area.InPlayArea(p)) return false;
            if (_area.IsUnderwater(new Vector3(p.x, _area.Height(p.x, p.z), p.z)) && !OnLandAbove(p)) return false;
            if (m.sp.climbs) return true;
            float s = Mathf.Max(1f, m.scale);
            if (Physics.Raycast(m.pos + m.up * (0.15f * s), dir, out var wall, dist, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                && wall.normal.y < 0.5f) return false;
            if (!Physics.Raycast(p + Vector3.up * (1f * s), Vector3.down, out var g, 2.5f * s, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                return false;
            return g.normal.y > 0.55f && Mathf.Abs(g.point.y - m.pos.y) < 0.6f * s;
        }

        /// <summary>水の上に物（石・葉）があって、その上なら歩ける。</summary>
        bool OnLandAbove(Vector3 p)
        {
            float wl = _area.WaterLevelAt(p.x, p.z);
            return Physics.Raycast(new Vector3(p.x, wl + 3f, p.z), Vector3.down, out var hit, 3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                   && hit.point.y > wl + 0.02f;
        }

        /// <summary>
        /// 地面を歩くいきものの共通の動き。向きはなめらかに回り、速さは加速・減速し、前が危なければ向きを変え、
        /// 仲間やしゃくとりむしとぶつからない。大きないきものは、すぐ前のしゃくとりむしを待ってあげる。
        /// しゃくとりむしを乗せているときは、ゆっくり静かに動く。
        /// </summary>
        void Walk(Mob m, float dt, float wantSpeed, float turnDegPerSec, float accel, bool near, int index, bool curious = true)
        {
            if (m.carryingWorm)
            {
                wantSpeed *= 0.6f;
                turnDegPerSec *= 0.5f;
                accel *= 0.5f;
            }
            if (m.anim < m.pauseUntil) wantSpeed = 0f;
            Vector3 toWorm = _head - m.pos;
            toWorm -= m.up * Vector3.Dot(toWorm, m.up);
            float dw = toWorm.magnitude;
            if (m.sp.rideable && !m.carryingWorm && dw < 1.1f * m.scale && dw > 1e-3f && Vector3.Dot(toWorm / dw, m.fwd) > 0.4f)
                wantSpeed = 0f;   // すぐ前にいるので、ふまないように待つ
            // 背伸びしたしゃくとりむしが近くにいると、気になってそちらを向く
            if (curious && _wormStanding && dw < 2.5f && dw > 0.2f && !m.carryingWorm)
            {
                m.wantFwd = toWorm / dw;
                wantSpeed = 0f;
            }
            Vector3 want = m.wantFwd + Avoid(m, _head, 0.9f * Mathf.Max(1f, m.scale)) * 2f + Separation(m);
            if (m.sp.rideable) want += BigSeparation(m);   // 大きないきものは、ほかのいきものにもぶつからない
            Vector3 move = m.sp.sideways ? Vector3.Cross(m.up, m.fwd) * m.sideSign : m.fwd;
            // 上り坂ではゆっくり、下り坂では少しはやく
            float slope = Vector3.Dot(move, Vector3.up);
            wantSpeed *= slope > 0f ? 1f - 0.4f * Mathf.Clamp01(slope) : 1f + 0.15f * Mathf.Clamp01(-slope);
            // 大きく曲がるときは速さを落とし、止まっているときは向きを変えてから歩きだす
            float turnNeed = Vector3.Angle(m.fwd, Vector3.ProjectOnPlane(want, m.up));
            if (!m.sp.sideways)
            {
                wantSpeed *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(turnNeed / 120f));
                if (m.curSpeed < 0.05f && turnNeed > 50f) wantSpeed = 0f;
            }
            // しゃくとりむしがすぐ後ろにいると、少し急ぐ
            if (!m.sp.rideable && dw < 0.8f && dw > 1e-3f && Vector3.Dot(toWorm / dw, m.fwd) < -0.3f) wantSpeed *= 1.4f;
            // 前を横切る仲間がいたら、ゆずる
            if (Crossing(m)) wantSpeed *= 0.3f;
            // 強い風のときは、小さないきものは止まってしがみつく
            if (!m.sp.rideable && m.scale < 1.35f && Wind.Gust(Time.time) > 0.85f) wantSpeed *= 0.2f;
            // 前の安全確認（0.3 秒ごと）。危なければ、大きく向きを変える
            if (m.anim > m.probeNext && wantSpeed > 0f)
            {
                m.probeNext = m.anim + 0.3f;
                if (!SafeAhead(m, move, 0.45f * Mathf.Max(1f, m.scale) + 0.25f))
                {
                    if (m.sp.sideways) m.sideSign = -m.sideSign;
                    else m.wantFwd = Quaternion.AngleAxis(R(110f, 160f) * Sign(), m.up) * m.fwd;
                    want = m.wantFwd;
                    m.curSpeed *= 0.3f;
                    // 何度も行き止まりになるときは、少し止まって後ろへ向きを変える
                    if (++m.blockCount >= 3)
                    {
                        m.blockCount = 0;
                        m.wantFwd = -m.fwd;
                        m.pauseUntil = m.anim + 0.5f;
                    }
                }
                else m.blockCount = 0;
            }
            Steer(m, want, dt, turnDegPerSec);
            m.curSpeed = Mathf.MoveTowards(m.curSpeed, wantSpeed, accel * dt);
            move = m.sp.sideways ? Vector3.Cross(m.up, m.fwd) * m.sideSign : m.fwd;
            m.pos += move * (m.curSpeed * dt);
            float above = m.sp.id == "beetle" ? 1.4f : 0.8f * Mathf.Max(1f, m.scale);   // カブトムシは小さな物をのりこえる
            if (near || (index + _frame) % 4 == 0 || m.fallVel > 0f) SnapToSurface(m, above, 2.5f, true);
            m.fwd = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, m.fwd).normalized;
        }

        /// <summary>止まっているときは、ときどき左右を見る（体を少しひねる）。</summary>
        void UpdateGlance(Mob m, float dt)
        {
            bool still = m.moveSpeed < 0.02f && !m.airborne && m.sp.kind != MobKind.Hover && m.sp.kind != MobKind.Flutter && m.sp.kind != MobKind.Skater;
            if (!still || m.curled > 0f || m.retreat > 0f) m.glanceTarget = 0f;
            else if (m.anim > m.glanceNext)
            {
                bool bird = m.sp.kind == MobKind.Bird;
                // 鳥は、せわしなくきょろきょろ
                m.glanceNext = m.anim + (bird ? R(0.4f, 1.2f) : R(1.5f, 4f));
                m.glanceTarget = R(0f, 1f) < 0.3f ? 0f : R(-25f, 25f);
                // しゃくとりむしを乗せていると、ときどきふり返って見る
                if (m.carryingWorm && R(0f, 1f) < 0.5f)
                {
                    Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, m.up);
                    if (toW.sqrMagnitude > 1e-4f) m.glanceTarget = Mathf.Clamp(Vector3.SignedAngle(m.fwd, toW, m.up), -35f, 35f);
                }
            }
            m.glance = Mathf.MoveTowards(m.glance, m.glanceTarget, dt * (m.sp.kind == MobKind.Bird ? 240f : 90f));
        }

        /// <summary>飛ぶもののかたむき：曲がるときは内側へかたむき、上り下りで頭が上下する。</summary>
        static void UpdateTilt(Mob m, float dt)
        {
            bool flyer = m.airborne && (m.sp.kind == MobKind.Bird || m.sp.kind == MobKind.Flutter || m.sp.kind == MobKind.Hover);
            float bank = 0f, pitch = 0f;
            if (flyer)
            {
                bank = Mathf.Clamp(-m.turnRate * 0.25f, -35f, 35f);
                Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
                if (m.vel.sqrMagnitude > 0.01f) pitch = Mathf.Clamp(Mathf.Atan2(m.vel.y, hv.magnitude + 0.3f) * Mathf.Rad2Deg * 0.6f, -30f, 30f);
                if (m.flare > 0f) pitch = Mathf.Lerp(pitch, 28f, m.flare);   // 着地の前は、体を起こしてブレーキ
            }
            float k = 1f - Mathf.Exp(-6f * dt);
            m.bank = Mathf.Lerp(m.bank, bank, k);
            m.pitch = Mathf.Lerp(m.pitch, pitch, k);
        }

        /// <summary>種ごとの、向きを変える速さ（度/秒）。</summary>
        static float TurnRateOf(Mob m)
        {
            switch (m.sp.id)
            {
                case "snail": return 25f;
                case "riversnail": return 20f;
                case "beetle": return 45f;
                case "crab": return 160f;
                case "ladybug": return 140f;
                case "otoshibumi": return 90f;
                default: return 120f;
            }
        }

        /// <summary>種ごとの、速さの変わり方。</summary>
        static float AccelOf(Mob m)
        {
            switch (m.sp.id)
            {
                case "snail": case "riversnail": return 0.3f;
                case "beetle": return 0.3f;
                case "crab": return 4f;
                default: return 1.5f;
            }
        }

        // ------------------------------------------------------------------
        // 地面を歩くいきもの
        // ------------------------------------------------------------------
        void UpdateCrawler(Mob m, float dt, bool near, int index)
        {
            string id = m.sp.id;
            if (id == "pillbug" && UpdatePillbugCurl(m, dt)) return;
            if ((id == "snail" || id == "riversnail") && UpdateSnailRetreat(m, dt)) return;
            if (id == "ladybug")
            {
                // てんとうむし：すぐそばに来られると、脚をちぢめて死んだふり
                if ((_head - m.pos).sqrMagnitude < 0.6f * 0.6f && !m.carryingWorm) m.deadUntil = Mathf.Max(m.deadUntil, m.anim + 3f);
                if (m.anim < m.deadUntil)
                {
                    m.curSpeed = 0f;
                    return;
                }
            }
            m.emerge = Mathf.MoveTowards(m.emerge, 1f, dt);   // かたつむりが殻から出てくる
            m.timer -= dt;
            bool circling = m.group.path.Count == 1 && m.radius > 0f;
            if (circling)
            {
                // 中心のまわりをぐるり（キノコの根元のかたつむり）
                Vector3 c = m.group.path[0];
                Vector3 radial = m.pos - c;
                radial.y = 0f;
                if (radial.sqrMagnitude < 0.01f) radial = Vector3.right;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial.normalized);
                Vector3 desired = c + radial.normalized * m.radius;
                m.wantFwd = (tangent + (desired - m.pos) * 0.5f).normalized;
                m.speedMul = 1f;
            }
            else if (m.timer <= 0f) DecideCrawl(m);

            // カニ：しゃくとりむしが近づくと、横歩きで逃げる
            if (id == "crab")
            {
                Vector3 away = m.pos - _head;
                away.y = 0f;
                bool threat = away.sqrMagnitude < 2f * 2f && !m.carryingWorm;
                if (threat)
                {
                    Vector3 side = Vector3.Cross(m.up, m.fwd);
                    m.sideSign = Vector3.Dot(away, side) >= 0f ? 1f : -1f;
                    m.speedMul = 2.2f;
                    m.timer = Mathf.Max(m.timer, 0.6f);
                }
                m.display = Mathf.MoveTowards(m.display, threat ? 1f : 0f, dt * 3f);   // おどされると、体の前（はさみ）を上げる
            }
            // カブトムシ：しゃくとりむしが前に来ると、角を持ち上げて見せる
            if (id == "beetle")
            {
                Vector3 to = _head - m.pos;
                bool show = to.sqrMagnitude < 1.9f * 1.9f && Vector3.Dot(to.normalized, m.fwd) > 0.3f && !m.carryingWorm;
                m.display = Mathf.MoveTowards(m.display, show ? 1f : 0f, dt * (show ? 2f : 0.7f));
                // 角を見せたあとは、しゃくとりむしからはなれていく
                m.displayHold = m.display > 0.9f ? m.displayHold + dt : 0f;
                if (m.displayHold > 1.5f)
                {
                    m.displayHold = 0f;
                    Vector3 away = Vector3.ProjectOnPlane(m.pos - _head, m.up);
                    if (away.sqrMagnitude > 1e-4f) m.wantFwd = away.normalized;
                    m.speedMul = 1f;
                    m.timer = R(2f, 3f);
                }
            }
            // カワニナ：水ぎわが好き。水から遠いと、下り（水の方）へ
            if (id == "riversnail" && !OnWater(m.pos))
            {
                float above = m.pos.y - Mathf.Max(_area.WaterLevelAt(m.pos.x, m.pos.z), -50f);
                if (above > 0.25f) m.wantFwd = Vector3.Slerp(m.wantFwd, Downhill(m.pos), 0.5f).normalized;
            }

            float wantSpeed = m.sp.speed * m.speedMul * m.pace;
            // かたつむり：筋肉の波で、速さが脈打つ
            if (id == "snail" || id == "riversnail")
            {
                wantSpeed *= 0.55f + 0.45f * Mathf.Pow(Mathf.Sin(m.anim * 2.4f), 2f);
                wantSpeed *= Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(m.up.y));   // 壁を登るときは、重さでゆっくり
                wantSpeed *= m.emerge;                                        // 殻から出きるまでは動かない
                // ときどき止まって、あたりを味見するようにうなずく
                if (m.speedMul <= 0f) m.display = 0.3f + 0.3f * Mathf.Sin(m.anim * 3f);
                else m.display = Mathf.MoveTowards(m.display, 0f, dt);
            }
            // カワニナは、水ぎわでは少し速い
            if (id == "riversnail" && _area.WaterLevelAt(m.pos.x, m.pos.z) > -100f && m.pos.y - _area.WaterLevelAt(m.pos.x, m.pos.z) < 0.1f) wantSpeed *= 1.3f;
            // カブトムシ：一歩ずつ、のっしのっし
            if (id == "beetle") wantSpeed *= (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(m.gait))) * (1f - m.display);
            // オトシブミ：少し歩いては止まって、葉をたしかめるようにうなずく
            if (id == "otoshibumi" && m.speedMul <= 0f) m.display = 0.5f + 0.5f * Mathf.Sin(m.anim * 4f);
            else if (id == "otoshibumi") m.display = Mathf.MoveTowards(m.display, 0f, dt * 2f);
            if (id == "ladybug")
            {
                // てんとうむし：高い所では、くるくる回ってあたりをさがす。歩くときは、小きざみに向きを変える
                Vector3 saved = m.wantFwd;
                if (m.anim < m.spinUntil) m.wantFwd = Quaternion.AngleAxis(100f, m.up) * m.fwd;
                else m.wantFwd = Quaternion.AngleAxis(Mathf.Sin(m.anim * 2f + m.phase) * 25f, m.up) * saved;
                Walk(m, dt, m.anim < m.spinUntil ? 0f : wantSpeed, TurnRateOf(m), AccelOf(m), near, index);
                m.wantFwd = saved;
                return;
            }
            Walk(m, dt, wantSpeed, TurnRateOf(m), AccelOf(m), near, index, id != "crab");
        }

        /// <summary>歩く・止まるを決める（種ごとのくせ）。</summary>
        void DecideCrawl(Mob m)
        {
            string id = m.sp.id;
            Vector3 toHome = m.home - m.pos;
            toHome.y = 0f;
            float homeR = id == "pillbug" ? m.radius * 0.6f : m.radius;   // だんごむしは、かくれ場所からあまりはなれない
            // すみかから大きくはなれてしまったら、まっすぐもどる
            if (toHome.magnitude > Mathf.Max(homeR, 0.5f) * 3f && id != "crab")
            {
                m.wantFwd = toHome.normalized;
                m.speedMul = 1.2f;
                m.timer = R(2f, 3f);
                return;
            }
            if (id == "crab")
            {
                // カニ：すばやく横へちょこちょこ → 止まる、をくり返す。ときどき向きを変える
                bool burst = m.speedMul <= 0f;
                m.speedMul = burst ? R(1.2f, 1.7f) : 0f;
                m.timer = burst ? R(0.4f, 0.8f) : R(0.5f, 1.2f);
                if (burst && R(0f, 1f) < 0.5f) m.sideSign = -m.sideSign;
                if (burst && toHome.magnitude > homeR)
                {
                    Vector3 side = Vector3.Cross(m.up, m.fwd);
                    m.sideSign = Vector3.Dot(toHome, side) >= 0f ? 1f : -1f;
                }
                if (burst && toHome.magnitude < homeR * 0.4f) m.speedMul *= 0.7f;   // すみかの近くでは、ゆっくり
                return;
            }
            if (id == "ant" || id == "ant_helmet")
            {
                // 行列のないアリ：ちょこまか、小きざみに向きを変えながら歩きまわる
                m.speedMul = R(0f, 1f) < 0.1f ? 0f : R(0.9f, 1.2f);
                m.timer = R(0.3f, 0.8f);
                Vector3 fa = Quaternion.AngleAxis(R(-50f, 50f), m.up) * m.fwd;
                if (toHome.magnitude > homeR) fa = Vector3.Slerp(fa.normalized, toHome.normalized, 0.6f);
                m.wantFwd = fa.normalized;
                return;
            }
            if (id == "otoshibumi")
            {
                bool walk = m.speedMul <= 0f;
                m.speedMul = walk ? R(0.8f, 1.1f) : 0f;
                m.timer = walk ? R(0.8f, 1.3f) : R(1.2f, 2f);
                // 止まるたびに、少し向きを変える
                if (!walk) m.wantFwd = Quaternion.AngleAxis(R(-30f, 30f), m.up) * m.fwd;
            }
            else if ((id == "snail" || id == "riversnail") && R(0f, 1f) < 0.2f)
            {
                // かたつむり：ときどき止まって、味見
                m.speedMul = 0f;
                m.timer = R(1.2f, 2f);
                return;
            }
            else if (id == "ladybug" && m.pos.y - _area.Height(m.pos.x, m.pos.z) > 0.8f && R(0f, 1f) < 0.4f)
            {
                // てんとうむし：高い所に来ると、くるくる回ってあたりをさがす
                m.spinUntil = m.anim + R(1.2f, 2f);
                m.speedMul = R(0.7f, 1f);
                m.timer = R(1.5f, 3f);
                return;
            }
            else if (id == "ladybug" && R(0f, 1f) < 0.2f)
            {
                // てんとうむし：ときどき止まって、体の手入れ
                m.speedMul = 0f;
                m.timer = R(0.8f, 1.4f);
                m.pauseUntil = m.anim + m.timer;
                return;
            }
            else if (R(0f, 1f) < 0.3f)
            {
                m.speedMul = 0f;
                m.timer = R(1f, 3f);
                return;
            }
            else
            {
                m.speedMul = R(0.7f, 1.2f);
                m.timer = R(1.5f, 4f);
            }
            Vector3 f = Quaternion.AngleAxis(R(-70f, 70f), m.up) * m.fwd;
            // だんごむし：仲間の近くに集まる
            if (id == "pillbug" && _groups.TryGetValue(m.group, out var pals) && pals.Count > 1)
            {
                Vector3 c = Vector3.zero;
                foreach (var o in pals) c += o.pos;
                Vector3 toC = Vector3.ProjectOnPlane(c / pals.Count - m.pos, m.up);
                if (toC.magnitude > 0.8f) f = Vector3.Slerp(f.normalized, toC.normalized, 0.3f);
            }
            // てんとうむし：壁や茎にいるときは、上へ上へと登る
            if (id == "ladybug" && m.up.y < 0.6f && R(0f, 1f) < 0.6f) f = Vector3.ProjectOnPlane(Vector3.up, m.up);
            if (toHome.magnitude > homeR) f = Vector3.Slerp(f.normalized, toHome.normalized, 0.75f);
            if (f.sqrMagnitude > 1e-4f) m.wantFwd = f.normalized;
        }

        /// <summary>地面の下り坂の向き（水の方へ行くときに使う）。</summary>
        Vector3 Downhill(Vector3 p)
        {
            const float e = 0.4f;
            float hx = _area.Height(p.x + e, p.z) - _area.Height(p.x - e, p.z);
            float hz = _area.Height(p.x, p.z + e) - _area.Height(p.x, p.z - e);
            Vector3 d = new Vector3(-hx, 0f, -hz);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>かたつむり：しゃくとりむしがすぐそばに来ると、殻にひっこんでしばらく動かない。</summary>
        bool UpdateSnailRetreat(Mob m, float dt)
        {
            if (!m.carryingWorm && (_head - m.pos).sqrMagnitude < 0.7f * 0.7f * m.scale * m.scale) m.retreat = Mathf.Max(m.retreat, 3f);
            if (m.retreat <= 0f) return false;
            m.retreat = Mathf.Max(0f, m.retreat - dt);
            m.curSpeed = 0f;
            m.emerge = 0f;   // ひっこみ終えたら、ゆっくり出てくる
            return true;
        }

        /// <summary>だんごむし：近づくとくるんとまるくなり、近くにいる間はまるいまま。坂では、まるいままころがる。</summary>
        bool UpdatePillbugCurl(Mob m, float dt)
        {
            float dw = (_head - m.pos).magnitude;
            if (dw < 1.3f)
            {
                m.curled = 4f;
                m.hesitated = false;
            }
            else if (m.curled > 0f && dw < 1.8f) m.curled = Mathf.Max(m.curled, 1.2f);
            // 体をのばしかけて、まだ近くにいたら、もう一度まるくなる（ためらい）
            if (m.curled > 0f && m.curled < 0.6f && !m.hesitated && dw < 2.2f)
            {
                m.hesitated = true;
                m.curled = 1.2f;
            }
            if (m.curled <= 0f) return false;
            m.curled = Mathf.Max(0f, m.curled - dt);
            m.curSpeed = 0f;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, m.up);
            float slope = 1f - m.up.y;
            if (slope > 0.04f && downhill.sqrMagnitude > 1e-4f && m.curled > 0.35f)
            {
                Vector3 v = downhill.normalized * Mathf.Min(1.2f, slope * 4f);
                m.pos += v * dt;
                m.spin += v.magnitude * dt / (0.08f * m.scale);
                m.rock = 1f;   // ころがって止まると、ゆらゆら
                SnapToSurface(m, 0.5f, 1.5f, true);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // アリの行列
        // ------------------------------------------------------------------
        void UpdateMarcher(Mob m, float dt, bool near)
        {
            var path = m.group.path;
            float total = _pathLen.TryGetValue(m.group, out var tl) ? tl : PathLength(path);
            // 一匹ずつちがう速さで、速さもゆらぐ。荷物を運ぶアリは少しゆっくり。ヘルメットアリは安全第一でゆっくり
            float paceK = m.pace * (0.85f + 0.3f * Mathf.PerlinNoise(m.phase, m.anim * 0.35f));
            if (m.carrying) paceK *= 0.9f;
            if (m.sp.IsRare)
            {
                paceK *= 0.85f;
                // ときどき立ち止まって、まわりを確認
                if (m.anim > m.hopNext)
                {
                    m.hopNext = m.anim + R(5f, 8f);
                    m.pauseUntil = m.anim + 0.8f;
                    m.glanceTarget = R(-30f, 30f);
                }
            }
            // 道にしゃくとりむしがいると、用心してゆっくり。あわてているときは急ぐ
            Vector3 toWormNow = _head - m.pos;
            toWormNow.y = 0f;
            if (toWormNow.magnitude < 1.2f) paceK *= 0.7f;
            if (m.anim < m.hurryUntil) paceK *= 1.5f;
            // しゃくとりむしにふれそうになったら、においをかぐように少し止まって見る
            if (toWormNow.magnitude < 0.3f && m.anim > m.slowUntil + 2f)
            {
                m.slowUntil = m.anim + 0.5f;
                m.glanceTarget = Mathf.Clamp(Vector3.SignedAngle(m.fwd, toWormNow, Vector3.up), -40f, 40f);
            }
            // 前のアリに追いつきそうなら、ゆっくり（間をあける）。追いついたら触角でちょんとあいさつ。
            // ヘルメットアリのうしろは、少し多めに間をあける
            var leader = LeaderAhead(m, total, out float gap);
            float respect = leader != null && leader.sp.IsRare ? 1.5f : 1f;
            gap /= respect;
            if (gap < 0.16f)
            {
                paceK *= 0.3f;
                if (m.anim > m.greetNext)
                {
                    m.slowUntil = m.anim + 0.4f;
                    m.greetNext = m.anim + R(3f, 6f);
                }
            }
            else if (gap < 0.3f) paceK *= 0.7f;
            if (m.anim < m.slowUntil) paceK *= 0.3f;
            if (m.anim < m.pauseUntil) paceK = 0f;
            // 上り坂ではゆっくり
            SplinePoint(path, m.pathS, total, out var dir0);
            float slopeK = 1f - 0.35f * Mathf.Clamp01(dir0.y * 3f);
            m.curSpeed = Mathf.MoveTowards(m.curSpeed, m.sp.speed * paceK * slopeK, dt * 4f);
            float before = Mathf.Repeat(m.pathS, Mathf.Max(total, 0.01f));
            m.pathS += m.curSpeed * dt;
            float after = Mathf.Repeat(m.pathS, Mathf.Max(total, 0.01f));
            // 食べものの所では拾うために、巣の所では荷物をおろすために、少し止まる
            if (before < total * 0.5f && after >= total * 0.5f) m.pauseUntil = m.anim + 0.5f;
            else if (after < before) m.pauseUntil = m.anim + 0.3f;
            Vector3 p = SplinePoint(path, m.pathS, total, out var dir);
            // 行列の帰り道では、食べものを運んでいる
            m.carrying = Mathf.Repeat(m.pathS, Mathf.Max(total, 0.01f)) > total * 0.5f;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            // くねくね（なめらかなゆらぎ）
            float lateral = (Mathf.PerlinNoise(m.phase * 3.1f, m.anim * 0.8f) - 0.5f) * 0.14f;
            // ときどき、道から少しはずれて、またもどる
            if (m.anim > m.strayNext)
            {
                m.strayNext = m.anim + R(6f, 14f);
                m.strayTarget = R(0f, 1f) < 0.35f ? R(-0.35f, 0.35f) : 0f;
            }
            if (m.strayTarget != 0f && Mathf.Abs(m.stray - m.strayTarget) < 0.02f) m.strayTarget = 0f;
            m.stray = Mathf.MoveTowards(m.stray, m.strayTarget, dt * 0.25f);
            lateral += m.stray;
            // 前のアリの通ったあとを、少しなぞって歩く
            if (leader != null && gap * respect < 1f) lateral = lateral * 0.6f + leader.lat * 0.4f;
            // 道にしゃくとりむしがいたら、よけて通る
            Vector3 toWorm = _head - p;
            toWorm.y = 0f;
            float dw = toWorm.magnitude;
            if (dw < 0.8f) lateral -= Mathf.Sign(Vector3.Dot(toWorm, side) + 1e-4f) * (0.8f - dw) * 0.6f;
            lateral = Mathf.Clamp(lateral, -0.55f, 0.55f);
            m.lat = lateral;
            p += side * lateral;
            m.pos = p;
            m.fwd = Vector3.Slerp(m.fwd, dir, 1f - Mathf.Exp(-8f * dt)).normalized;
            m.wantFwd = m.fwd;
            if (near) SnapToSurface(m, 1.2f, 3f);
            else m.pos.y = _area.Height(p.x, p.z);
        }

        /// <summary>同じ行列で、すぐ前にいるアリと、そこまでの道のり。</summary>
        Mob LeaderAhead(Mob m, float total, out float gap)
        {
            gap = 999f;
            Mob best = null;
            if (!_groups.TryGetValue(m.group, out var list) || total <= 0f) return null;
            foreach (var o in list)
            {
                if (o == m) continue;
                float d = Mathf.Repeat(o.pathS - m.pathS, total);
                if (d > 1e-4f && d < gap)
                {
                    gap = d;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>同じ行列で、すぐ前にいるアリまでの道のり。</summary>
        float GapAhead(Mob m, float total)
        {
            float best = 999f;
            if (!_groups.TryGetValue(m.group, out var list) || total <= 0f) return best;
            foreach (var o in list)
            {
                if (o == m) continue;
                float d = Mathf.Repeat(o.pathS - m.pathS, total);
                if (d > 1e-4f && d < best) best = d;
            }
            return best;
        }

        // ------------------------------------------------------------------
        // チョウ
        // ------------------------------------------------------------------
        void UpdateFlutter(Mob m, float dt)
        {
            float t = m.anim + m.phase;
            m.timer -= dt;
            // 休んでいるときにしゃくとりむしが近づくと、ぱっと飛び立つ
            if (m.resting && (_head - m.pos).sqrMagnitude < 1.5f * 1.5f) TakeWing(m);
            m.flapBoost = Mathf.MoveTowards(m.flapBoost, 0f, dt * 1.5f);
            if (m.timer <= 0f)
            {
                m.resting = !m.resting && R(0f, 1f) < 0.45f;
                m.timer = m.resting ? R(2.5f, 5f) : R(6f, 12f);
                m.chase = null;
                if (m.resting)
                {
                    m.target = RestSpot(m, out bool onFlower);
                    if (onFlower) m.timer *= 1.6f;   // 花の上では、長く休む
                }
                else
                {
                    m.target = m.home + new Vector3(R(-m.radius, m.radius), 0f, R(-m.radius, m.radius));
                    // ときどき、仲間と追いかけっこ
                    if (R(0f, 1f) < 0.3f) m.chase = Partner(m);
                    m.chaseUntil = m.anim + R(2.5f, 4.5f);
                }
            }
            float ground = GroundOrWater(m.pos);
            Vector3 goal;
            if (m.resting) goal = m.target;
            else if (m.chase != null && m.anim < m.chaseUntil && !m.chase.resting)
            {
                // くるくる回りながら追いかける
                goal = m.chase.pos + new Vector3(Mathf.Cos(t * 3f), Mathf.Sin(t * 2f) * 0.3f, Mathf.Sin(t * 3f)) * 0.6f;
            }
            else
            {
                goal = m.target + new Vector3(Mathf.Sin(t * 0.9f) * 2.2f, 0f, Mathf.Sin(t * 1.7f) * 1.4f);
                goal.y = ground + m.height + Mathf.Sin(t * 2.3f) * 0.7f;
            }
            Vector3 d = goal - m.pos;
            // 風に流されながら、はばたいて目的地へ（突風のときは、よけいに流される）
            float gust = Wind.Gust(Time.time);
            // 花にとまる前は、ゆっくり近づいて、はばたきを速く
            float cap = m.sp.speed;
            if (m.resting && d.magnitude < 1f)
            {
                cap *= 0.6f;
                m.flapBoost = Mathf.Max(m.flapBoost, 0.5f);
            }
            Vector3 want = Vector3.ClampMagnitude(d * 1.5f, cap) + (m.resting ? Vector3.zero : Wind.At(m.pos) * (0.4f + 0.4f * gust));
            m.vel = Vector3.Lerp(m.vel, want, 1f - Mathf.Exp(-3f * dt));
            bool flying = !m.resting || d.magnitude > 0.3f;
            if (flying)
            {
                // ひらひら、気まぐれにジグザグ
                if (m.anim > m.jitterNext)
                {
                    m.jitterNext = m.anim + 0.2f;
                    m.jitter = new Vector3(R(-0.8f, 0.8f), R(-0.3f, 0.3f), R(-0.8f, 0.8f));
                }
                m.vel += m.jitter * dt;
                // 突風では、ふわっと上がる
                m.vel.y += Mathf.Max(0f, gust - 0.6f) * 1.5f * dt;
                // しゃくとりむしの頭にぶつからないよう、よける
                Vector3 away = m.pos - _head;
                if (away.sqrMagnitude < 0.6f * 0.6f && away.sqrMagnitude > 1e-4f) m.vel += away.normalized * (3f * dt);
            }
            // はばたくたびに、ふわっと浮く
            if (flying) m.vel.y += Mathf.Sin(t * 7f * Mathf.PI * 2f) * 1.6f * dt;
            // 前に物があれば、上へよける
            if (flying && m.vel.sqrMagnitude > 0.01f
                && Physics.Raycast(m.pos, m.vel.normalized, 0.8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                m.vel += Vector3.up * (3f * dt);
            m.pos += m.vel * dt;
            if (m.pos.y < ground + 0.05f) m.pos.y = ground + 0.05f;
            // 花の上で休んでいるときは、花といっしょに風でゆれる
            if (!flying) m.pos = m.target + Wind.At(m.target) * (0.02f * Mathf.Sin(m.anim * 2.2f + m.phase));
            Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
            if (hv.sqrMagnitude > 0.01f) m.fwd = Vector3.Slerp(m.fwd, hv.normalized, dt * 4f);
            m.up = Vector3.up;
            m.airborne = flying;
            // 止まっているときは、ときどき羽を大きく開く
            if (!flying && R(0f, 1f) < dt * 0.35f) m.openWings = 1f;
            m.openWings = Mathf.MoveTowards(m.openWings, 0f, dt * 1.2f);
        }

        /// <summary>チョウが飛び立つ。</summary>
        void TakeWing(Mob m)
        {
            m.resting = false;
            m.timer = R(4f, 8f);
            m.target = m.home + new Vector3(R(-m.radius, m.radius), 0f, R(-m.radius, m.radius));
            m.vel += Vector3.up * 2f;   // 強くはばたいて、ぱっと舞い上がる
            m.flapBoost = 1f;
        }

        /// <summary>チョウが休む場所：近くの花の上（なければ地面の少し上）。</summary>
        Vector3 RestSpot(Mob m, out bool onFlower)
        {
            onFlower = false;
            if (_flowers.Count > 0)
            {
                Vector3 best = Vector3.zero;
                float bestScore = float.MaxValue;
                for (int k = 0; k < 6; k++)
                {
                    Vector3 f = _flowers[_rng.Next(_flowers.Count)];
                    float d = Vector2.Distance(new Vector2(f.x, f.z), new Vector2(m.home.x, m.home.z));
                    if (d > m.radius + 3f) continue;
                    if (FlowerTaken(m, f)) continue;   // ほかのチョウが休んでいる花にはとまらない
                    float score = Vector3.Distance(f, m.pos) + R(0f, 2f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = f;
                    }
                }
                if (bestScore < float.MaxValue)
                {
                    onFlower = true;
                    return best + Vector3.up * 0.05f;
                }
            }
            Vector3 p = m.pos;
            return new Vector3(p.x, GroundOrWater(p) + 0.15f, p.z);
        }

        bool FlowerTaken(Mob m, Vector3 f)
        {
            foreach (var o in _mobs)
                if (o != m && o.sp.kind == MobKind.Flutter && o.resting && (o.target - f).sqrMagnitude < 0.3f * 0.3f) return true;
            return false;
        }

        /// <summary>追いかけっこの相手（同じ群れの、飛んでいる仲間）。</summary>
        Mob Partner(Mob m)
        {
            if (!_groups.TryGetValue(m.group, out var list)) return null;
            foreach (var o in list)
                if (o != m && !o.resting && (o.pos - m.pos).sqrMagnitude < 6f * 6f) return o;
            return null;
        }

        // ------------------------------------------------------------------
        // 跳ぶ（バッタ・カエル・クモ）
        // ------------------------------------------------------------------
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
            m.crouch = 0f;
            if (h.sqrMagnitude > 1e-4f) m.fwd = h.normalized;
            if (m.sp.kind == MobKind.Pouncer)
            {
                // ハエトリグモは、跳ぶときに命綱の糸を引く
                m.lineFrom = m.pos;
                m.lineUntil = float.MaxValue;
            }
        }

        /// <summary>跳ぶ準備：跳ぶ方を向いてから、かがむ。delay 秒たったら跳ぶ。</summary>
        void PrepareJump(Mob m, Vector3 to, float peak, float delay)
        {
            m.launchTo = to;
            m.launchPeak = peak;
            m.launchAt = m.anim + delay;
        }

        /// <summary>跳ぶ準備の間（跳ぶ方へ向き、かがむ）。跳んだら false。</summary>
        bool UpdateLaunch(Mob m, float dt)
        {
            Vector3 dir = Vector3.ProjectOnPlane(m.launchTo - m.pos, m.up);
            if (dir.sqrMagnitude > 1e-4f) Steer(m, dir, dt, 720f);
            m.crouch = Mathf.MoveTowards(m.crouch, 1f, dt * 8f);
            if (m.anim < m.launchAt) return true;
            m.launchAt = -1f;
            StartJump(m, m.launchTo, m.launchPeak);
            return false;
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
            m.fallVel = 0f;
            SnapToSurface(m, 1f, 3f);
            m.joy = Mathf.Max(m.joy, 0.25f);   // 着地でぽよんと
            m.crouch = m.sp.id == "frog" ? 1f : 0.7f;   // 着地でぐっとつぶれる（カエルは脚を広げて大きく）
            m.wantFwd = m.fwd;                  // 跳んだ向きのまま、着地する
            if (m.sp.id == "grasshopper" && R(0f, 1f) < 0.2f) m.wobble = 1f;   // ときどき、着地でよろける
            if (m.fledJump)
            {
                m.fledJump = false;
                m.watchUntil = m.anim + 1.5f;   // 逃げたあとは、しゃくとりむしを見張る
            }
            if (m.lineUntil > m.anim) m.lineUntil = m.anim + 0.6f;   // 命綱は、少しして見えなくなる
        }

        /// <summary>跳ぶ先の、あいだにある物の高さ（それをこえる高さで跳ぶ）。</summary>
        static float ObstacleHeight(Vector3 a, Vector3 b)
        {
            float top = Mathf.Max(a.y, b.y);
            float need = 0f;
            for (int i = 1; i <= 4; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 5f);
                if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out var hit, 16f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    need = Mathf.Max(need, hit.point.y - top);
            }
            return need;
        }

        void UpdateHopper(Mob m, float dt, bool near, int index)
        {
            bool frog = m.sp.id == "frog";
            if (m.airborne)
            {
                UpdateJump(m, dt);
                return;
            }
            if (m.launchAt >= 0f && UpdateLaunch(m, dt)) return;
            if (m.airborne) return;
            // カエル：しゃくとりむしを乗せているときは、じっとしている
            if (m.carryingWorm)
            {
                m.timer = Mathf.Max(m.timer, 1f);
                return;
            }
            Vector3 toWorm = _head - m.pos;
            toWorm.y = 0f;
            // 逃げたあとは、しゃくとりむしの方を向いて見張る
            if (m.anim < m.watchUntil)
            {
                if (toWorm.sqrMagnitude > 0.04f) Steer(m, toWorm, dt, 200f);
                return;
            }
            // カエル：近くのしゃくとりむしの方を向いて、じっと見る
            if (frog && toWorm.sqrMagnitude < 4f * 4f && toWorm.sqrMagnitude > 0.04f) Steer(m, toWorm, dt, 90f);
            // バッタは 2.2 まで、カエルは 1.2 まで近づくと逃げる（カエルは水の方へ）
            bool scared = toWorm.sqrMagnitude < (frog ? 1.2f * 1.2f : 2.2f * 2.2f);
            // バッタ：跳ぶ合間に、少し歩く（おどろいたら歩くのをやめて跳ぶ）
            if (!frog && m.anim < m.walkUntil && !scared)
            {
                Walk(m, dt, m.sp.speed, 120f, 2f, near, index);
                return;
            }
            m.curSpeed = 0f;
            m.timer -= dt;
            if (m.timer > 0f && !scared) return;
            m.timer = frog ? R(4f, 8f) : R(2f, 6f);
            if (!frog && !scared && R(0f, 1f) < 0.35f)
            {
                m.walkUntil = m.anim + R(0.6f, 1.5f);
                m.wantFwd = Quaternion.AngleAxis(R(-90f, 90f), Vector3.up) * m.fwd;
                return;
            }
            // 跳ぶ先を選ぶ：逃げるときは、しゃくとりむしから遠い、ひらけた平らな場所へ
            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            for (int tries = 0; tries < 6; tries++)
            {
                float a = R(0f, Mathf.PI * 2f);
                float dist = frog ? R(0.8f, 2.2f) : (scared ? R(3.5f, 6f) : R(2f, 5f));   // 逃げるときは遠くまで
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (scared) dir = Vector3.Slerp(Vector3.ProjectOnPlane(m.pos - _head, Vector3.up).normalized, dir, 0.35f).normalized;
                Vector3 target = m.pos + dir * dist;
                Vector3 toHome = m.home - target;
                toHome.y = 0f;
                if (toHome.magnitude > m.radius + 1f && !scared) continue;
                // 高い物（木の幹など）が前にあるときは、その向きには跳ばない
                if (Physics.Raycast(m.pos + Vector3.up * 2.5f, dir, dist * 0.6f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(target + Vector3.up * 6f, Vector3.down, out var hit, 14f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (_area.IsUnderwater(hit.point) || !_area.InPlayArea(hit.point) || hit.normal.y < 0.6f) continue;
                float score = hit.normal.y + (scared ? Vector3.Distance(hit.point, _head) * 0.5f : R(0f, 0.5f));
                if (frog && scared)
                {
                    // カエルは、水ぎわへ逃げる
                    float wl = _area.WaterLevelAt(hit.point.x, hit.point.z);
                    if (wl > -100f) score += 2f - Mathf.Clamp(hit.point.y - wl, 0f, 2f);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hit.point;
                }
            }
            if (bestScore == float.MinValue) return;
            float jumpDist = Vector3.Distance(new Vector3(m.pos.x, 0f, m.pos.z), new Vector3(best.x, 0f, best.z));
            float peak = frog ? 0.4f + jumpDist * 0.25f : R(1.2f, 2f);   // カエルは遠くへ跳ぶほど高く
            peak = Mathf.Max(peak, ObstacleHeight(m.pos, best) + 0.4f);   // あいだの物をこえる高さで
            m.fledJump = scared;
            PrepareJump(m, best, peak, scared ? 0.12f : (frog ? 0.25f : 0.15f));
        }

        void UpdatePouncer(Mob m, float dt, bool near, int index)
        {
            if (m.airborne)
            {
                UpdateJump(m, dt);
                return;
            }
            if (m.launchAt >= 0f && UpdateLaunch(m, dt)) return;
            if (m.airborne) return;
            Vector3 toWorm = _head - m.pos;
            toWorm -= m.up * Vector3.Dot(toWorm, m.up);
            float dw = toWorm.magnitude;
            if (dw < 3f && dw > 0.05f)
            {
                // ハエトリグモ：しゃくとりむしが気になって、前脚を上げてじっと見つめる。向きは、かくっ、かくっと変える
                m.raise = Mathf.MoveTowards(m.raise, 1f, dt * 4f);
                m.watchTime += dt;
                if (m.anim > m.glanceNext)
                {
                    m.glanceNext = m.anim + R(0.25f, 0.6f);
                    m.wantFwd = toWorm / dw;
                }
                Steer(m, m.wantFwd, dt, 600f);
                m.curSpeed = 0f;
                // 近づきすぎると、後ずさりする
                if (dw < 0.8f && m.anim > m.backNext)
                {
                    m.backNext = m.anim + 4f;
                    Vector3 spotB = m.pos - toWorm / dw * 0.6f;
                    if (Physics.Raycast(spotB + Vector3.up * 2f, Vector3.down, out var hb, 5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                        && !_area.IsUnderwater(hb.point) && hb.normal.y > 0.3f)
                        PrepareJump(m, hb.point, 0.15f, 0.12f);
                    return;
                }
                // しばらく見つめていると、ぴょんと近くへ跳んでくる（上には乗らない）
                if (m.watchTime > 3f && dw > 1.2f && m.anim > m.hopNext)
                {
                    m.hopNext = m.anim + 8f;
                    Vector3 spot = m.pos + toWorm / dw * (dw - 0.8f);
                    if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                        && !_area.IsUnderwater(hit.point) && hit.normal.y > 0.3f)
                        PrepareJump(m, hit.point, 0.25f, 0.2f);
                }
                return;
            }
            m.raise = Mathf.MoveTowards(m.raise, 0f, dt * 3f);
            m.watchTime = 0f;
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                float roll = R(0f, 1f);
                if (roll < 0.25f)
                {
                    // ぴょん（かがんでから）。少し高い所があれば、そこへ跳び上がる
                    Vector3 toHome = m.home - m.pos;
                    toHome.y = 0f;
                    bool found = false;
                    Vector3 bestHop = Vector3.zero;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 dir = Quaternion.AngleAxis(R(-60f, 60f), Vector3.up) * Vector3.ProjectOnPlane(m.fwd, Vector3.up).normalized;
                        if (toHome.magnitude > m.radius) dir = toHome.normalized;
                        Vector3 target = m.pos + dir * R(0.4f, 1.0f);
                        if (!Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var hit, 5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                            || _area.IsUnderwater(hit.point) || hit.normal.y < 0.3f || hit.point.y > m.pos.y + 0.5f) continue;
                        if (!found || hit.point.y > bestHop.y)
                        {
                            bestHop = hit.point;
                            found = true;
                        }
                    }
                    if (found) PrepareJump(m, bestHop, R(0.15f, 0.35f), 0.15f);
                    m.timer = R(1.5f, 3f);
                    return;
                }
                // ときどき、前脚で顔の手入れ
                if (roll > 0.92f)
                {
                    m.groomUntil = m.anim + 1.2f;
                    m.speedMul = 0f;
                    m.timer = 1.2f;
                    return;
                }
                // ちょこちょこ歩いては止まる
                m.speedMul = roll < 0.55f ? 0f : R(0.9f, 1.4f);
                m.timer = R(0.4f, 1.4f);
                m.wantFwd = (Quaternion.AngleAxis(R(-80f, 80f), m.up) * m.fwd).normalized;
            }
            // かくっと向きを変えてから、ちょこちょこ歩く
            Walk(m, dt, m.sp.speed * m.speedMul * m.pace, 540f, 6f, near, index);
        }

        // ------------------------------------------------------------------
        // カマキリ
        // ------------------------------------------------------------------
        void UpdateStalker(Mob m, float dt, bool near, int index)
        {
            Vector3 toWorm = _head - m.pos;
            toWorm.y = 0f;
            float dw = toWorm.magnitude;
            bool alert = dw < 6f && !m.carryingWorm;
            // 近づくと向きを変え、かまを持ち上げる
            m.raise = Mathf.MoveTowards(m.raise, alert ? 1f : 0f, dt * (alert ? 2.5f : 0.8f));
            m.strike = Mathf.MoveTowards(m.strike, 0f, dt * 3f);
            if (alert)
            {
                // 向きは、少しずつ、かくっと変える
                if (m.anim > m.glanceNext && dw > 0.05f)
                {
                    m.glanceNext = m.anim + R(0.3f, 0.7f);
                    m.wantFwd = toWorm / dw;
                }
                Steer(m, m.wantFwd, dt, 160f);
                m.curSpeed = 0f;
                // とても近づくと、かまをすばやくくり出す（あたらない）
                if (dw < 1.1f && m.raise > 0.8f && m.anim > m.strikeNext)
                {
                    m.strike = 1f;
                    m.strikeNext = m.anim + 5f;
                }
                return;
            }
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                bool walk = R(0f, 1f) < 0.35f;
                m.speedMul = walk ? 1f : 0f;
                m.timer = walk ? R(1.5f, 3f) : R(4f, 9f);
                // 止まっているときは、ときどき、かまの手入れ
                if (!walk && R(0f, 1f) < 0.4f) m.groomUntil = m.anim + 1.2f;
                Vector3 toHome = m.home - m.pos;
                toHome.y = 0f;
                Vector3 f = Quaternion.AngleAxis(R(-50f, 50f), Vector3.up) * m.fwd;
                if (toHome.magnitude > m.radius) f = toHome.normalized;
                m.wantFwd = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
            }
            // 歩くときは、風にゆれる葉のように、少し進んでは止まる
            float wantSpeed = m.sp.speed * m.speedMul;
            if (m.speedMul > 0f) wantSpeed *= Mathf.Max(0f, Mathf.Sin(m.anim * 3.2f + m.phase)) * 1.6f;
            Walk(m, dt, wantSpeed, 60f, 1.5f, near, index, false);
        }

        // ------------------------------------------------------------------
        // 鳥
        // ------------------------------------------------------------------
        void UpdateBird(Mob m, float dt)
        {
            bool crow = m.sp.id == "crow";
            if (m.airborne)
            {
                FlyBird(m, dt);
                return;
            }
            if (m.launchAt >= 0f)
            {
                // 飛び立つ前に、一瞬かがむ
                m.crouch = Mathf.MoveTowards(m.crouch, 1f, dt * 10f);
                if (m.anim >= m.launchAt)
                {
                    m.launchAt = -1f;
                    TakeOff(m, crow);
                }
                return;
            }
            m.timer -= dt;
            float flee = m.sp.fleeRadius;
            float dWorm = (_head - m.pos).magnitude;
            bool scared = dWorm < flee;
            bool alarmed = m.alarmAt >= 0f && m.anim >= m.alarmAt;
            // 少し近づかれると、ついばむのをやめて頭を上げ、そちらを向いて警戒する
            bool alert = !scared && dWorm < flee * 1.6f;
            m.alertK = Mathf.MoveTowards(m.alertK, alert ? 1f : 0f, dt * 4f);
            if (dWorm > flee * 2f) m.sideHopped = false;
            // 羽をふくらませる（ときどき）
            if (m.anim > m.puffNext)
            {
                m.puffNext = m.anim + R(6f, 12f);
                m.puffUntil = m.anim + 0.4f;
            }
            if (scared || alarmed || m.timer <= 0f)
            {
                m.alarmAt = -1f;
                m.launchAt = m.anim + (scared ? 0.1f : 0.25f);
                if (scared) AlarmFlock(m);   // 仲間も、少しおくれていっせいに飛び立つ
                return;
            }
            if (alert && !scared && !alarmed)
            {
                m.resting = false;
                Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
                if (toW.sqrMagnitude > 1e-4f) m.fwd = Vector3.Slerp(m.fwd, toW.normalized, dt * 4f).normalized;
                // カラス：もう少し近づかれると、横へぴょんとよける
                if (crow && !m.sideHopped && dWorm < flee * 1.3f)
                {
                    m.sideHopped = true;
                    Vector3 away = Vector3.ProjectOnPlane(m.pos - _head, Vector3.up).normalized;
                    m.target = m.pos + Quaternion.AngleAxis(R(-40f, 40f), Vector3.up) * away * 1.5f;
                    m.t = 0.6f;
                }
                else if (!m.sideHopped || m.t <= 0f) return;
            }
            // ちょんちょん跳ねて歩く（カラスはのしのし歩く）・ついばむ
            m.t -= dt;
            if (m.t <= 0f)
            {
                m.t = crow ? R(0.8f, 2f) : R(0.4f, 1.2f);
                float a = R(-90f, 90f);
                m.fwd = (Quaternion.AngleAxis(a, Vector3.up) * m.fwd).normalized;
                Vector3 toHome = (m.group.path.Count > 0 ? m.group.path[m.landIndex] : m.home) - m.pos;
                toHome.y = 0f;
                if (toHome.magnitude > 4f * (crow ? 3f : 1f)) m.fwd = toHome.normalized;
                // スズメは、仲間からはなれすぎない
                if (!crow && _groups.TryGetValue(m.group, out var flock) && flock.Count > 1)
                {
                    Vector3 c = Vector3.zero;
                    int n = 0;
                    foreach (var o in flock) if (!o.airborne) { c += o.pos; n++; }
                    if (n > 1)
                    {
                        Vector3 toC = c / n - m.pos;
                        toC.y = 0f;
                        if (toC.magnitude > 2.5f) m.fwd = toC.normalized;
                    }
                }
                m.target = m.pos + m.fwd * (crow ? R(1.5f, 3f) : R(0.5f, 1.2f));
                // 急な所（キノコのかさのふちなど）・水の中へは行かない
                if (Physics.Raycast(m.target + Vector3.up * 3f, Vector3.down, out var th, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                    && (th.normal.y < 0.85f || Mathf.Abs(th.point.y - m.pos.y) > 0.6f * m.scale || _area.IsUnderwater(th.point)))
                    m.target = m.pos;
                m.resting = R(0f, 1f) < 0.4f;   // ついばむ
                if (crow && R(0f, 1f) < 0.07f) Caw(m, 0.6f);
            }
            if (!m.resting)
            {
                Vector3 d = m.target - m.pos;
                d.y = 0f;
                float step = (crow ? 2.5f : 2.2f) * m.pace * dt;   // 歩く速さは一羽ずつ少しちがう
                if (d.magnitude > step) m.pos += d.normalized * step;
                SnapToSurface(m, 3f, 6f);
            }
        }

        void FlyBird(Mob m, float dt)
        {
            m.t += dt / m.dur;
            float k = Mathf.Clamp01(m.t);
            float e = ShakuMath.Smooth01(k);
            Vector3 p = Vector3.Lerp(m.from, m.to, e);
            // 飛び立ちは急に上がり、下りはゆるやかに
            p.y += m.arc * Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.7f));
            // 飛ぶ道は少しふくらませる（一直線には飛ばない）
            p += m.bow * Mathf.Sin(Mathf.PI * k);
            // 飛び立つときは、風に向かって
            p += m.windKick * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.35f));
            // カラスは、下りる前にひと回り旋回する
            if (m.circleR > 0f && k > 0.6f)
            {
                float ph = (k - 0.6f) / 0.4f;
                float a = ph * Mathf.PI * 2f;
                p += new Vector3(Mathf.Sin(a), 0f, 1f - Mathf.Cos(a)) * (m.circleR * Mathf.Sin(Mathf.PI * ph));
            }
            Vector3 dir = p - m.pos;
            m.vel = dir / Mathf.Max(dt, 1e-4f);
            m.pos = p;
            Vector3 hd = new Vector3(dir.x, 0f, dir.z);
            if (hd.sqrMagnitude > 1e-5f) m.fwd = Vector3.Slerp(m.fwd, hd.normalized, dt * 5f);
            m.up = Vector3.up;
            // 着地の前は、羽をはげしく動かしてブレーキ
            m.flare = k > 0.85f ? Mathf.InverseLerp(0.85f, 1f, k) : 0f;
            if (k >= 1f)
            {
                m.airborne = false;
                m.flare = 0f;
                m.timer = R(25f, 50f);
                m.t = 0f;
                m.crouch = 0.6f;
                SnapToSurface(m, 3f, 6f);
            }
        }

        /// <summary>群れの仲間に知らせる（少しおくれて、いっせいに飛び立つ）。</summary>
        void AlarmFlock(Mob m)
        {
            if (!_groups.TryGetValue(m.group, out var list)) return;
            foreach (var o in list)
                if (o != m && !o.airborne && o.launchAt < 0f && o.alarmAt < 0f && (o.pos - m.pos).sqrMagnitude < 8f * 8f)
                    o.alarmAt = o.anim + R(0.15f, 0.6f);
        }

        /// <summary>鳥が下りられる平らな場所（中心のまわりで、いちばん平らで上を向いた所）。</summary>
        bool FlatLanding(Vector3 around, float spread, out Vector3 spot)
        {
            spot = around;
            float best = -1f;
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.39996f;
                float r = i == 0 ? 0f : spread * Mathf.Sqrt(i / 8f);
                Vector3 p = around + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 120f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (_area.IsUnderwater(hit.point + Vector3.up * 0.02f)) continue;   // 水の上には下りない
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
            Vector3 dest = m.home;
            var spots = m.group.path;
            float avoid = m.sp.fleeRadius * 1.5f;
            // 下りる場所は、しゃくとりむしから十分はなれた所を選ぶ
            for (int tries = 0; tries < 4; tries++)
            {
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
                if (Vector2.Distance(new Vector2(dest.x, dest.z), new Vector2(_head.x, _head.z)) > avoid) break;
            }
            if (FlatLanding(dest, crow ? 3f : 1.5f, out var spot)) dest = spot;
            else if (spots.Count > 0 && FlatLanding(spots[m.landIndex], crow ? 3f : 1.5f, out spot)) dest = spot;
            m.from = m.pos;
            m.to = dest;
            m.t = 0f;
            float dist = Vector3.Distance(m.from, m.to);
            m.dur = Mathf.Max(2.5f, dist / (crow ? 9f : 7f)) * R(0.85f, 1.15f);   // 飛ぶ速さは毎回少しちがう
            m.arc = crow ? R(22f, 32f) : R(10f, 16f);
            m.windKick = -Wind.Direction(Time.time) * R(1f, 2f);
            m.circleR = crow ? R(2.5f, 4f) : 0f;
            Vector3 flat = Vector3.ProjectOnPlane(m.to - m.from, Vector3.up);
            m.bow = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) * (R(-0.2f, 0.2f) * dist) : Vector3.zero;
            m.airborne = true;
            m.resting = false;
            m.crouch = 0f;
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

        // ------------------------------------------------------------------
        // アメンボ
        // ------------------------------------------------------------------
        void UpdateSkater(Mob m, float dt)
        {
            m.timer -= dt;
            Vector3 away = m.pos - _head;
            away.y = 0f;
            bool scared = away.sqrMagnitude < 2f * 2f;
            Vector3 flowNow = FlowAt(m.pos);
            if (m.timer <= 0f || scared)
            {
                m.timer = R(0.8f, 2.2f);
                float a = R(0f, Mathf.PI * 2f);
                Vector3 f = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                // 流れのある所では、上流を向いて休む
                if (flowNow.sqrMagnitude > 0.0025f && R(0f, 1f) < 0.5f) f = -flowNow.normalized;
                Vector3 toHome = m.home - m.pos;
                toHome.y = 0f;
                if (toHome.magnitude > m.radius) f = toHome.normalized;
                if (scared) f = away.normalized;   // しゃくとりむしから、すいっと逃げる
                m.wantFwd = f;
            }
            // 滝のふち（水面の高さが急に変わる所）には近づかない
            float wlHere = _area.WaterLevelAt(m.pos.x, m.pos.z);
            Vector3 ahead = m.pos + m.fwd * 1.2f;
            if (Mathf.Abs(_area.WaterLevelAt(ahead.x, ahead.z) - wlHere) > 0.3f) m.wantFwd = -m.fwd;
            // 仲間とは、少し間をあける
            if (_groups.TryGetValue(m.group, out var pals))
                foreach (var o in pals)
                {
                    Vector3 d = m.pos - o.pos;
                    d.y = 0f;
                    if (o != m && d.sqrMagnitude < 0.6f * 0.6f && d.sqrMagnitude > 1e-4f) m.vel += d.normalized * (0.8f * dt);
                }
            // 向きを変えてから、足でこいで進む（こいだあとは、すーっとすべる）
            Steer(m, m.wantFwd, dt, 240f);
            float before = m.rowPhase;
            m.rowPhase += dt * (scared ? 5f : 2.2f);   // おどろくと、すばやく何度もこぐ
            if (Mathf.Floor(m.rowPhase) > Mathf.Floor(before)) m.vel += m.fwd * (m.sp.speed * (scared ? 1.6f : 0.8f));
            m.vel *= 1f - Mathf.Clamp01(1.6f * dt);
            // 川では流れに流される
            Vector3 flow = FlowAt(m.pos);
            // 岸が近いと、はね返らずに向きを変える
            if (m.vel.sqrMagnitude > 0.01f && !OnWater(m.pos + m.vel.normalized * 0.6f))
            {
                m.wantFwd = Quaternion.AngleAxis(R(120f, 170f) * Sign(), Vector3.up) * m.fwd;
                m.vel *= 0.5f;
            }
            Vector3 next = m.pos + (m.vel + flow) * dt;
            if (!OnWater(next)) next = m.pos;
            // 水面の高さに合わせる（川は場所によって水面の高さがちがう）
            float wl = _area.WaterLevelAt(next.x, next.z);
            m.pos = new Vector3(next.x, wl > -100f ? wl : m.home.y, next.z);
            m.up = Vector3.up;
            m.gait = m.rowPhase * Mathf.PI * 2f;   // 脚の動きは、こぐ動きに合わせる
        }

        // ------------------------------------------------------------------
        // トンボ・ホタル
        // ------------------------------------------------------------------
        void UpdateHover(Mob m, float dt)
        {
            if (m.sp.id == "firefly")
            {
                UpdateFirefly(m, dt);
                return;
            }
            m.timer -= dt;
            m.droop = m.perched ? Mathf.MoveTowards(m.droop, 1f, dt * 0.5f) : 0f;   // とまっていると、羽が少しずつたれる
            // ときどき、すいっと急に向きを変える
            if (!m.perched && m.vel.sqrMagnitude > 1f && R(0f, 1f) < dt * 0.6f)
                m.target = m.home + new Vector3(R(-m.radius, m.radius), 0f, R(-m.radius, m.radius));
            if (m.timer <= 0f)
            {
                m.perched = false;
                m.prey = null;
                // ときどき、近くを飛ぶ虫を追いかける（つかまえはしない）
                if (R(0f, 1f) < 0.15f)
                {
                    foreach (var o in _mobs)
                        if (o != m && o.sp.kind == MobKind.Flutter && o.airborne && (o.pos - m.pos).sqrMagnitude < 36f) { m.prey = o; break; }
                    if (m.prey != null)
                    {
                        m.preyUntil = m.anim + 2f;
                        m.timer = 2f;
                    }
                }
                if (m.prey != null) { }
                else if (R(0f, 1f) < 0.2f && TryPerch(m, out var spot))
                {
                    // ときどき、花や石の上にとまって休む
                    m.perched = true;
                    m.target = spot;
                    m.timer = R(3f, 6f);
                }
                else
                {
                    // 見回り：なわばりの両はしを、行ったり来たり
                    m.patrol = -m.patrol;
                    Vector3 axis = new Vector3(Mathf.Cos(m.phase), 0f, Mathf.Sin(m.phase));
                    m.target = m.home + axis * (m.radius * 0.7f * m.patrol) + new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)) * (m.radius * 0.3f);
                    m.timer = R(1.2f, 3f);
                }
            }
            Vector3 goal = m.perched ? m.target
                : new Vector3(m.target.x, GroundOrWater(m.target) + m.height + Mathf.Sin(m.anim * 1.3f + m.phase) * 0.5f, m.target.z);
            if (m.prey != null && m.anim < m.preyUntil) goal = m.prey.pos + new Vector3(0.4f, 0.2f, 0.4f);
            Vector3 d = goal - m.pos;
            float dist = d.magnitude;
            // すいっと飛んで、ぴたっと止まる
            bool dart = dist > 0.4f;
            Vector3 want = dart ? d / dist * (m.sp.speed * Mathf.Clamp01(dist / 0.8f + 0.3f)) : Vector3.zero;
            want += Wind.At(m.pos) * 0.15f;
            if (!dart && !m.perched)
                want += new Vector3(Mathf.Sin(m.anim * 13f), Mathf.Sin(m.anim * 11f), Mathf.Cos(m.anim * 12f)) * 0.08f;   // その場でふるえるようにホバリング
            m.vel = Vector3.Lerp(m.vel, want, 1f - Mathf.Exp(-(dart ? 4f : 9f) * dt));
            if (!m.perched)
            {
                // しゃくとりむしの頭や、前の物・地面にぶつからないよう、よける
                Vector3 away = m.pos - _head;
                if (away.sqrMagnitude < 0.6f * 0.6f && away.sqrMagnitude > 1e-4f) m.vel += away.normalized * (3f * dt);
                if (m.vel.sqrMagnitude > 0.01f && Physics.Raycast(m.pos, m.vel.normalized, 0.8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    m.vel += Vector3.up * (3f * dt);
                if (m.pos.y < GroundOrWater(m.pos) + 0.25f) m.vel.y += 2f * dt;
            }
            m.pos += m.vel * dt;
            if (m.perched && dist < 0.05f)
            {
                m.pos = m.target;
                m.vel = Vector3.zero;
            }
            Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
            if (hv.sqrMagnitude > 0.04f) m.fwd = Vector3.Slerp(m.fwd, hv.normalized, dt * 5f);
            m.up = Vector3.up;
            m.airborne = !(m.perched && dist < 0.1f);
            m.resting = !m.airborne;
        }

        /// <summary>トンボがとまる場所：なわばりの中の花の上か、石などの物の上。</summary>
        bool TryPerch(Mob m, out Vector3 spot)
        {
            spot = Vector3.zero;
            for (int k = 0; k < 4 && _flowers.Count > 0; k++)
            {
                Vector3 f = _flowers[_rng.Next(_flowers.Count)];
                if (Vector2.Distance(new Vector2(f.x, f.z), new Vector2(m.home.x, m.home.z)) < m.radius + 2f)
                {
                    spot = f + Vector3.up * 0.05f;
                    return true;
                }
            }
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = m.home + new Vector3(R(-m.radius, m.radius), 6f, R(-m.radius, m.radius));
                if (!Physics.Raycast(p, Vector3.down, out var hit, 14f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.7f || hit.point.y < GroundOrWater(hit.point) + 0.3f || _area.IsUnderwater(hit.point)) continue;
                spot = hit.point + Vector3.up * 0.04f;
                return true;
            }
            return false;
        }

        static float FireflyGlow(Mob m) => m.sp != null && m.sp.id == "firefly" ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(m.blink)), 3f) : 0f;

        void UpdateFirefly(Mob m, float dt)
        {
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                m.timer = R(2f, 4f);
                m.perched = false;
                // ときどき、草や花にとまって休む
                if (R(0f, 1f) < 0.25f && TryPerch(m, out var spot))
                {
                    m.perched = true;
                    m.target = spot;
                    m.timer = R(3f, 5f);
                }
                else m.target = m.home + new Vector3(R(-m.radius, m.radius), 0f, R(-m.radius, m.radius));
            }
            // 光るときに、ふわっと少し上がる
            float glow = FireflyGlow(m);
            float ground = GroundOrWater(m.target);
            Vector3 goal = m.perched ? m.target
                : new Vector3(m.target.x, ground + m.height + Mathf.Sin(m.anim * 1.3f + m.phase) * 0.5f + glow * 0.3f, m.target.z);
            Vector3 d = goal - m.pos;
            // 風が強いほど、大きく流される
            Vector3 want = Vector3.ClampMagnitude(d * 2f, m.sp.speed) + (m.perched ? Vector3.zero : Wind.At(m.pos) * (0.3f * (1f + Wind.Gust(Time.time))));
            m.vel = Vector3.Lerp(m.vel, want, 1f - Mathf.Exp(-2f * dt));
            // 仲間とは、少し間をあける
            if (_groups.TryGetValue(m.group, out var pals))
                foreach (var o in pals)
                {
                    Vector3 sep = m.pos - o.pos;
                    if (o != m && sep.sqrMagnitude < 0.5f * 0.5f && sep.sqrMagnitude > 1e-4f) m.vel += sep.normalized * (0.6f * dt);
                }
            m.pos += m.vel * dt;
            if (m.perched && d.magnitude < 0.05f) { m.pos = m.target; m.vel = Vector3.zero; }
            Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
            if (hv.sqrMagnitude > 0.04f) m.fwd = Vector3.Slerp(m.fwd, hv.normalized, dt * 5f);
            m.up = Vector3.up;
            m.airborne = !(m.perched && d.magnitude < 0.1f);
            m.resting = !m.airborne;
        }

        /// <summary>ホタルの光のリズムは、近くの仲間と少しずつそろっていく（本物のホタルのように）。</summary>
        void UpdateFireflySync(float dt)
        {
            foreach (var m in _mobs)
            {
                if (m.sp.id != "firefly") continue;
                float pull = 0f;
                int n = 0;
                foreach (var o in _mobs)
                {
                    if (o == m || o.sp.id != "firefly" || (o.pos - m.pos).sqrMagnitude > 8f * 8f) continue;
                    pull += Mathf.Sin(o.blink - m.blink);
                    n++;
                }
                m.blink += dt * (2.4f + (n > 0 ? 0.8f * pull / n : 0f));
            }
        }

        // ------------------------------------------------------------------
        // 描画
        // ------------------------------------------------------------------
        static Mesh _lineMesh;

        /// <summary>糸用の細い棒（1x1x1 の箱）。</summary>
        static Mesh LineMesh
        {
            get
            {
                if (_lineMesh != null) return _lineMesh;
                var v = new Vector3[8];
                for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                int[] t =
                {
                    0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
                    2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5,
                };
                _lineMesh = new Mesh { name = "SpiderLine", vertices = v, triangles = t };
                _lineMesh.RecalculateNormals();
                _lineMesh.RecalculateBounds();
                return _lineMesh;
            }
        }

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
                Vector3 right = Vector3.Cross(m.up, f).normalized;
                Quaternion rot = Quaternion.LookRotation(-f, m.up);
                // 止まっているときに、きょろきょろ
                if (Mathf.Abs(m.glance) > 0.01f) rot = Quaternion.AngleAxis(m.glance, m.up) * rot;
                // 飛ぶもの：曲がるときは内側へかたむき、上り下りで頭が上下する
                if (Mathf.Abs(m.bank) > 0.01f || Mathf.Abs(m.pitch) > 0.01f)
                    rot = Quaternion.AngleAxis(m.bank, f) * Quaternion.AngleAxis(-m.pitch, right) * rot;
                float s = m.scale;
                if (m.joy > 0f) s *= 1f + 0.22f * Mathf.Abs(Mathf.Sin(m.joy * Mathf.PI * 4f)) * m.joy;
                Vector3 scale3 = Vector3.one * s;
                bool walker = !m.airborne && (m.sp.kind == MobKind.Crawler || m.sp.kind == MobKind.Marcher || m.sp.kind == MobKind.Pouncer || m.sp.kind == MobKind.Stalker);
                // 加速すると前へ、ブレーキで後ろへ、体がかたむく
                if (walker && Mathf.Abs(m.accel) > 0.01f) rot = Quaternion.AngleAxis(Mathf.Clamp(m.accel * 6f, -8f, 8f), right) * rot;
                // 休んでいるときは、ゆっくり呼吸する
                if (!m.airborne && m.moveSpeed < 0.02f) scale3 *= 1f + 0.012f * Mathf.Sin(m.anim * 2f + m.phase);
                // 鳥が羽をふくらませる
                if (m.sp.kind == MobKind.Bird && !m.airborne && m.anim < m.puffUntil) scale3 *= 1.08f;
                // 食べものを運ぶアリは、少し体を起こす
                if (m.carrying) rot = Quaternion.AngleAxis(-4f, right) * rot;
                // 着地でよろける
                if (m.wobble > 0f) rot = Quaternion.AngleAxis(Mathf.Sin(m.anim * 12f) * 15f * m.wobble, f) * rot;
                // 警戒する鳥は、頭を上げる
                if (m.alertK > 0f) rot = Quaternion.AngleAxis(-8f * m.alertK, right) * rot;
                // トンボは、ホバリング中は風上へ少しかたむく
                if (m.sp.kind == MobKind.Hover && m.sp.id != "firefly" && m.airborne && m.vel.sqrMagnitude < 1f)
                    rot = Quaternion.AngleAxis(Mathf.Clamp(Vector3.Dot(Wind.At(m.pos), right) * 10f, -12f, 12f), f) * rot;
                if (m.sp.kind == MobKind.Stalker)
                {
                    // カマキリは、しゃくとりむしの方へ首をかしげる
                    float sideW = Mathf.Sign(Vector3.Dot(_head - m.pos, right));
                    rot = Quaternion.AngleAxis(-sideW * 8f * m.raise, f) * rot;
                }
                if (m.sp.id == "snail" || m.sp.id == "riversnail")
                {
                    // かたつむりの殻は、歩くとゆれ、坂では下へ少しずれる
                    if (m.moveSpeed > 0.01f) rot = Quaternion.AngleAxis(Mathf.Sin(m.anim * 1.2f) * 3f, f) * rot;
                    Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, m.up);
                    if (downhill.sqrMagnitude > 0.0025f) rot = Quaternion.AngleAxis(-Vector3.Dot(downhill, right) * 6f, f) * rot;
                    if (m.retreat <= 0f && m.emerge < 1f) scale3.y *= Mathf.Lerp(0.9f, 1f, m.emerge);   // ゆっくり出てくる
                }
                if (m.sp.id == "crab" && m.moveSpeed < 0.02f) rot = Quaternion.AngleAxis(Mathf.Sin(m.anim * 3f) * 3f, right) * rot;   // はさみを上げ下げ
                // 跳ぶ前にかがむ・着地でつぶれる
                if (m.crouch > 0f)
                {
                    scale3.y *= 1f - 0.25f * m.crouch;
                    scale3.z *= 1f + 0.08f * m.crouch;
                }
                if (m.sp.id == "frog" && !m.airborne)
                {
                    // のどがふくらむ呼吸（しゃくとりむしが近いと、どきどき速く）。跳ぶ前はぷるぷる
                    float rate = (_head - m.pos).sqrMagnitude < 9f ? 9f : 5f;
                    scale3.y *= 1f + 0.035f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(m.anim * rate + m.phase)), 2f);
                    scale3.y *= 1f + 0.05f * Mathf.Sin(m.anim * 20f) * (m.launchAt >= 0f ? m.crouch : 0f);
                }
                else if (m.sp.kind == MobKind.Hopper && !m.airborne)
                    scale3 *= 1f + 0.03f * Mathf.Sin(m.anim * 3f + m.phase);
                if ((m.sp.id == "snail" || m.sp.id == "riversnail") && m.moveSpeed > 0.01f)
                    scale3.z *= 1f + 0.06f * Mathf.Sin(m.anim * 2.4f);   // のびちぢみ（速さの脈と同じリズム）
                if (m.retreat > 0f) scale3.y *= 0.9f;                    // 殻にひっこむ
                if (m.sp.kind == MobKind.Hopper && m.airborne) scale3.z *= 1.12f;   // 跳ぶときは体がのびる
                if (m.sp.kind == MobKind.Stalker) rot *= Quaternion.Euler(0f, 0f, Mathf.Sin(m.anim * 1.1f + m.phase) * 4f * (1f + Wind.Gust(Time.time)) * (1f - m.raise));   // 葉のようにゆらゆら（風が強いと大きく）
                // カブトムシは角を持ち上げ、オトシブミはうなずく
                if (m.display > 0f) rot = Quaternion.AngleAxis(-(m.sp.id == "beetle" ? 16f : 10f) * m.display, right) * rot;
                if (m.sp.kind == MobKind.Bird && !m.airborne && m.resting)
                {
                    // ついばむ（何回かつついては、ひと休み）
                    float burst = Mathf.Sin(m.anim * 1.3f + m.phase) > -0.2f ? 1f : 0f;
                    rot = Quaternion.AngleAxis(25f * Mathf.Max(0f, Mathf.Sin(m.anim * 9f)) * burst, right) * rot;
                }
                Vector3 bob = Vector3.zero;
                float moving = Mathf.Clamp01(Mathf.Max(m.moveSpeed / Mathf.Max(0.02f, m.sp.speed * 0.25f), Mathf.Abs(m.turnRate) / 90f));
                if (m.sp.kind == MobKind.Bird && !m.airborne && !m.resting)
                {
                    if (m.sp.id == "crow")
                    {
                        // カラスは、頭をふりながら、のしのし歩く
                        bob = m.up * (Mathf.Abs(Mathf.Sin(m.anim * 6f)) * 0.04f * s);
                        rot = Quaternion.AngleAxis(Mathf.Sin(m.anim * 6f) * 5f * moving, right) * rot;
                    }
                    else bob = m.up * (Mathf.Abs(Mathf.Sin(m.anim * 9f)) * 0.12f * s);   // スズメは両足でちょんちょん
                }
                else if (m.sp.rig != null && m.moveSpeed > 0.02f)
                {
                    bob = m.up * (Mathf.Abs(Mathf.Sin(m.gait)) * 0.012f * s);   // 歩くと体が少し上下する
                    // 歩くと体が左右にもゆれる（カブトムシは大きく）
                    float rollAmp = m.sp.id == "beetle" ? 5f : 2.5f;
                    rot = Quaternion.AngleAxis(Mathf.Sin(m.gait) * rollAmp * moving, f) * rot;
                }
                // カマキリ：歩くときは前後にゆれる
                if (m.sp.kind == MobKind.Stalker && m.moveSpeed > 0.01f) bob += f * (Mathf.Sin(m.anim * 6f) * 0.015f * s);
                if (m.retreat > 0f) bob -= m.up * (0.03f * s);
                if (m.sp.id == "beetle")
                {
                    bob += m.up * (0.04f * m.display * s);                                     // 角を見せるときは、体を高く
                    if (m.moveSpeed < 0.02f && m.display < 0.1f) bob -= m.up * (0.02f * s);    // 止まると、どっしり体を下げる
                }
                if (m.sp.kind == MobKind.Skater) bob += Vector3.up * (0.008f * Mathf.Sin(m.anim * 6f + m.phase));   // 波でゆれる
                Vector3 drawAt = m.drawPos + bob;
                bool ball = m.sp.id == "pillbug" && m.curled > 0.35f;
                if (ball) rot = Quaternion.AngleAxis(m.spin * Mathf.Rad2Deg + Mathf.Sin(m.anim * 8f) * 6f * m.rock, right) * rot;   // 坂をころがり、止まるとゆらゆら
                else if (m.sp.id == "pillbug" && m.curled > 0f) scale3.z *= Mathf.Lerp(1f, 0.6f, m.curled / 0.35f);   // ゆっくり体をのばす
                Matrix4x4 body = Matrix4x4.TRS(drawAt, rot, scale3);

                string bodyMesh = ball ? "PillBug_Ball" : m.sp.body;
                Add(M(bodyMesh), assets.creature, body);

                // 脚（遠くは省く）
                if (m.sp.rig != null && d2 < 60f * 60f && !ball)
                {
                    var legs = CreatureRig.Legs(m.sp.rig);
                    // 速く歩くほど、脚を大きくふる
                    float speedK = Mathf.Clamp(m.moveSpeed / Mathf.Max(0.02f, m.sp.speed), 0.5f, 1.4f);
                    float swing = m.sp.legSwing * moving * speedK;
                    float lift = m.sp.legLift * moving;
                    float gait = m.gait;
                    if (moving < 0.05f)
                    {
                        // 止まっているときも、ときどき足をもぞもぞ（止まった直後は、脚をそろえる）
                        bool settling = m.anim < m.settleUntil;
                        swing = settling ? 10f : 3f;
                        lift = settling ? 8f : 2f;
                        gait = settling ? m.gait : m.anim * 1.7f + m.phase;
                    }
                    if (m.airborne) { swing = 0f; lift = 10f; gait = 0f; }
                    // てんとうむしの死んだふり：脚をちぢめて動かない
                    if (m.anim < m.deadUntil) { swing = 0f; lift = 35f; gait = 0f; }
                    var kind = m.sp.gait;
                    for (int li = 0; li < legs.Length; li++)
                    {
                        Mesh lm = M(legs[li].mesh + m.sp.legSuffix);
                        if (lm == null) lm = M(legs[li].mesh);
                        for (int side = 0; side < 2; side++)
                        {
                            bool mirror = side == 1;
                            Matrix4x4 legM;
                            if (li == 0 && m.sp.kind == MobKind.Pouncer && m.anim < m.groomUntil)
                            {
                                // ハエトリグモの顔の手入れ：前脚をすばやく動かす
                                legM = CreatureRig.LegMatrix(legs[li], li, mirror, mirror ? Mathf.PI : 0f, 0f, 18f + 10f * Mathf.Sin(m.anim * 16f + (mirror ? 1.5f : 0f)),
                                    GaitKind.Alternate, legs.Length);
                            }
                            else if (li == 0 && m.sp.kind == MobKind.Pouncer && m.raise > 0.05f)
                            {
                                // ハエトリグモ：見つめるときは、前脚を高く上げる
                                legM = CreatureRig.LegMatrix(legs[li], li, mirror, mirror ? Mathf.PI : 0f, 0f, 38f * m.raise + 4f * Mathf.Sin(m.anim * 5f),
                                    GaitKind.Alternate, legs.Length);
                            }
                            else legM = CreatureRig.LegMatrix(legs[li], li, mirror, gait, swing, lift, kind, legs.Length);
                            Add(lm, assets.creature, body * legM);
                        }
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
                        Add(spark, assets.creatureGlow, Matrix4x4.TRS(m.drawPos + o * m.scale, Quaternion.identity, Vector3.one * 0.45f));
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
                        // ホタルの光は、仲間とそろっていくリズムで。しゃくとりむしが近いと、明るく光ってあいさつ
                        float pulse = 0.55f + 0.6f * FireflyGlow(m) + ((_head - m.pos).sqrMagnitude < 2.5f * 2.5f ? 0.4f : 0f);
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
                        // 運んでいる食べものは、歩くたびに少しゆれる
                        if (m.carrying) Add(pm, assets.creature, body * Matrix4x4.Translate(part.offset + Vector3.up * (0.006f * Mathf.Sin(m.gait * 2f))));
                        continue;
                    }
                    if (part.mesh == "Mantis_Arm")
                    {
                        // かまを持ち上げる（近づくと高く）。とても近いと、すばやくくり出す
                        float raise = m.raise * 62f + Mathf.Sin(m.anim * 2f + m.phase) * 3f - m.strike * 90f;
                        if (m.anim < m.groomUntil && m.raise < 0.3f) raise = 25f + 10f * Mathf.Sin(m.anim * 9f);   // かまの手入れ
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
                        // 跳ぶときは後ろ足をけり出す。かがむときは、たたむ
                        float kick = m.airborne ? -38f : 12f * m.crouch;
                        // 休んでいるときは、後ろ足をこすり合わせて鳴く
                        if (!m.airborne && m.crouch < 0.05f && m.moveSpeed < 0.02f && Mathf.Sin(m.anim * 0.6f + m.phase) > 0.6f)
                            kick += 8f * Mathf.Sin(m.anim * 14f);
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
                        float hz = part.flapHz * (1f + 0.8f * m.flare);   // 着地の前は、はげしく
                        float flap = part.flapAmp * (1f + 0.3f * m.flare) * Mathf.Sin((m.anim + m.phase) * hz * Mathf.PI * 2f);
                        if (m.airborne && m.flare <= 0f)
                        {
                            // 下りるときや、ときどき空の上では、羽を広げたまますべる
                            bool glide = m.vel.y < -1f || Mathf.Sin(m.anim * 0.8f + m.phase) < -0.3f;
                            if (glide) flap = 18f + 6f * Mathf.Sin(m.anim * 4f);
                        }
                        // カラスは、地上でときどき羽を少し広げる
                        bool shrug = m.sp.id == "crow" && !m.airborne && m.anim < m.puffUntil;
                        if (shrug) flap = 8f;
                        Add(pm, assets.creatureWing, body * CreatureRig.BirdWing(m.sp.birdSize, true, !m.airborne && !shrug, flap));
                        Add(pm, assets.creatureWing, body * CreatureRig.BirdWing(m.sp.birdSize, false, !m.airborne && !shrug, flap));
                        continue;
                    }
                    float yaw = part.restYaw;
                    float roll = part.restRoll;
                    // 上るときは速く、下りるときはゆっくりはばたく（チョウは飛び立ち・着地でさらに速く、トンボはダッシュ中に速く）
                    float hzK = 1f + Mathf.Clamp(m.vel.y * 0.15f, -0.3f, 0.4f) + 0.6f * m.flapBoost;
                    if (m.sp.kind == MobKind.Hover) hzK += 0.3f * Mathf.Clamp01(m.vel.magnitude / Mathf.Max(0.1f, m.sp.speed));
                    float flapS = Mathf.Sin((m.anim + m.phase) * part.flapHz * hzK * Mathf.PI * 2f + pi * 1.3f);
                    switch (m.sp.kind)
                    {
                        case MobKind.Flutter:
                            // 止まっているときは羽をゆっくり開いたり閉じたり。ときどき大きく開く
                            roll = m.airborne ? part.restRoll + part.flapAmp * flapS : 70f + 12f * Mathf.Sin(m.anim * 2f) - 55f * m.openWings;
                            // ときどき羽を広げたまま、すーっとすべる
                            if (m.airborne && m.vel.y <= 0.2f && Mathf.Sin(m.anim * 0.7f + m.phase) > 0.85f) roll = part.restRoll + 10f;
                            break;
                        case MobKind.Hover:
                            roll = m.resting ? -10f * m.droop : part.flapAmp * flapS;   // とまっているトンボは、羽を広げたまま（少しずつたれる）
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
            // ハエトリグモの命綱
            foreach (var m in _mobs)
            {
                if (m.sp.kind != MobKind.Pouncer || !(m.airborne || m.anim < m.lineUntil)) continue;
                Vector3 a = m.lineFrom + Vector3.up * 0.03f, b = m.drawPos + Vector3.up * 0.05f;
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 0.05f || (a - camPos).sqrMagnitude > 50f * 50f) continue;
                Add(LineMesh, assets.creatureWing, Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d / len), new Vector3(0.006f, 0.006f, len)));
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

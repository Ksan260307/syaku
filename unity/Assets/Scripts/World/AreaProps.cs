using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// エリアの動く小物とできごと（エリアの改善 300）：
    /// 森　…ホコリタケをふむと胞子と音、どんぐり広場にどんぐりが落ちてくる、苔の上はやわらかい、丸太の中は足音がひびく。
    /// 川辺…川を流れる落ち葉と笹舟（滝を落ち、岸ではね返り、よどみではゆっくり）、魚がはねる、舟が着くと音、橋がきしむ、とびいしで水がはねる。
    /// 公園…ブランコのきしみ、シーソーが地面に当たる音と土けむり、すべり台の音ときらきら、水飲み場のしずくと音、土管の中のひびき、砂と金属の足音。
    /// </summary>
    public class AreaProps : MonoBehaviour
    {
        public static AreaProps Instance { get; private set; }

        WorldGenerator _world;
        AmbientFX _fx;
        AreaLayout _area = Areas.Forest;
        Transform _root;
        readonly System.Random _rng = new System.Random(4242);
        float R(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

        void OnEnable() => Instance = this;

        // ---- 森 ----
        readonly List<Vector3> _puffballs = new List<Vector3>();
        float _puffCool;
        int _puffIndex = -1;
        public int Puffs { get; private set; }
        class FallingAcorn { public Transform tr; public Vector3 vel; public int bounces; public float life; public bool knocked; }
        readonly List<FallingAcorn> _acorns = new List<FallingAcorn>();
        float _acornNext;
        public int AcornsDropped { get; private set; }

        // ---- 川辺 ----
        /// <summary>川を流れていく物（落ち葉・笹舟）。</summary>
        public class Drifter
        {
            public Transform tr;
            public Vector3 pos;
            public float spin, spinVel;
            public bool boat;
            public float hold;        // しゃくとりむしが近いと、少しのあいだ止まる
            public float wait;        // 次に流しはじめるまで（笹舟）
            public float fallT = -1f; // 滝を落ちている途中
            public int bounces;
        }
        readonly List<Drifter> _drift = new List<Drifter>();
        public IReadOnlyList<Drifter> Drifters => _drift;
        float _fishNext;
        public int FishJumps { get; private set; }
        bool _ferryAtA, _ferryAtB;
        float _creakNext;

        // ---- 公園 ----
        float _swingCreak, _dripPhase;
        bool _sliding;
        public int SeesawBumps { get; private set; }

        public void Build(WorldGenerator world, AmbientFX fx)
        {
            Instance = this;
            _world = world;
            _fx = fx;
            _area = world.Area;
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("AreaProps").transform;
            _root.SetParent(transform, false);
            _puffballs.Clear();
            _acorns.Clear();
            _drift.Clear();
            Puffs = AcornsDropped = FishJumps = SeesawBumps = 0;
            _acornNext = Time.time + R(4f, 8f);
            _fishNext = Time.time + R(6f, 12f);
            foreach (var p in world.ExtraSpots("puffball")) _puffballs.Add(p);
            if (_area.Id == "river") BuildDrift();
            if (world.Ferry != null)
            {
                _ferryAtA = world.Ferry.AtA;
                _ferryAtB = world.Ferry.AtB;
            }
            if (world.Seesaw != null) world.Seesaw.Bumped += OnSeesawBump;
        }

        MeshRenderer MakeMesh(string name, Mesh mesh, Material mat, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return mr;
        }

        // ------------------------------------------------------------------
        // 足もと（足音・着地）
        // ------------------------------------------------------------------
        /// <summary>やわらかい地面（森の苔の丘・目覚めの苔原）。1 = とてもやわらかい。</summary>
        public static float SoftGround(Vector3 p)
        {
            if (Areas.Current != Areas.Forest) return 0f;
            Vector2 q = new Vector2(p.x, p.z);
            float k = Mathf.Max(ShakuMath.SmoothStep(15f, 10f, Vector2.Distance(q, ForestLayout.MossHill)), ShakuMath.SmoothStep(9f, 6f, q.magnitude));
            return p.y - ForestLayout.Height(p.x, p.z) < 0.4f ? k : 0f;
        }

        /// <summary>足音がひびく所（丸太のトンネル・土管の中）。</summary>
        public static bool Echo(Vector3 p)
        {
            var area = Areas.Current;
            foreach (var lm in area.Landmarks)
                if (lm.useCapsule && Collectibles.IsInside(lm, p)) return true;
            return false;
        }

        /// <summary>地面の足音の種類を、場所でくわしくする（公園の砂・森の苔）。</summary>
        public static AudioManager.Surface Refine(AudioManager.Surface s, Vector3 p)
        {
            if (s != AudioManager.Surface.Ground) return s;
            if (Areas.Current == Areas.Park && ParkLayout.SandMask(p.x, p.z) > 0.5f && p.y - ParkLayout.Height(p.x, p.z) < 0.4f) return AudioManager.Surface.Sand;
            if (SoftGround(p) > 0.5f) return AudioManager.Surface.Moss;
            return s;
        }

        /// <summary>一歩ごと：川辺のとびいしでは、足もとで水がはねる。</summary>
        public void OnStep(Vector3 p)
        {
            if (_area.Id != "river" || _world == null) return;
            foreach (var s in _world.StepStones)
                if ((new Vector2(s.x, s.z) - new Vector2(p.x, p.z)).sqrMagnitude < 1.4f * 1.4f && Mathf.Abs(p.y - s.y) < 0.6f)
                {
                    AreaSounds.Instance?.PlayAt("plink", p, 0.35f, R(1.2f, 1.6f));
                    _fx?.Splashlet(p);
                    return;
                }
        }

        void Update()
        {
            if (_world == null || !_world.IsGenerated) return;
            var worm = InchwormController.Instance;
            float dt = Time.deltaTime;
            if (_area.Id == "forest") UpdateForest(worm, dt);
            else if (_area.Id == "river") UpdateRiver(worm, dt);
            else UpdatePark(worm, dt);
        }

        // ------------------------------------------------------------------
        // 森
        // ------------------------------------------------------------------
        void UpdateForest(InchwormController worm, float dt)
        {
            // ホコリタケをふむと、ぽふっと胞子のけむりと音
            _puffCool -= dt;
            if (worm != null && _puffCool <= 0f)
            {
                for (int i = 0; i < _puffballs.Count; i++)
                {
                    Vector3 top = _puffballs[i];
                    bool on = (worm.HeadPosition - top).sqrMagnitude < 0.9f * 0.9f || (worm.TailPoint - top).sqrMagnitude < 0.9f * 0.9f;
                    if (on && i != _puffIndex)
                    {
                        Puff(top);
                        _puffIndex = i;
                        _puffCool = 0.6f;
                        break;
                    }
                    if (!on && i == _puffIndex && (worm.CenterPosition - top).sqrMagnitude > 2f * 2f) _puffIndex = -1;
                }
            }
            // どんぐり広場：ときどき上からどんぐりが落ちてきて、はねて止まる（音つき）
            if (Time.time > _acornNext)
            {
                _acornNext = Time.time + R(9f, 18f);
                Vector2 c = ForestLayout.AcornPlaza + Random.insideUnitCircle * 9f;
                DropAcorn(new Vector3(c.x, ForestLayout.Height(c.x, c.y) + R(14f, 20f), c.y));
            }
            for (int i = _acorns.Count - 1; i >= 0; i--)
            {
                var a = _acorns[i];
                a.life += dt;
                if (a.bounces < 3)
                {
                    a.vel += Vector3.down * ShakuPhysics.Gravity * dt;
                    a.vel = ShakuPhysics.ApplyDrag(a.vel, Vector3.zero, 0.1f, 0f, dt);
                    Vector3 next = a.tr.position + a.vel * dt;
                    if (Physics.Raycast(a.tr.position, a.vel.normalized, out var hit, (a.vel * dt).magnitude + 0.3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    {
                        a.tr.position = hit.point + hit.normal * 0.3f;
                        if (!a.knocked || a.bounces < 2)
                            AreaSounds.Instance?.PlayAt("knock_acorn", hit.point, Mathf.Lerp(0.6f, 0.25f, a.bounces / 2f), R(0.9f, 1.15f));
                        a.knocked = true;
                        a.vel = Vector3.Reflect(a.vel, hit.normal) * 0.35f + Random.insideUnitSphere * 0.4f;
                        a.bounces++;
                        if (a.bounces == 1) _fx?.Dust(hit.point, hit.normal, 0.15f, 0.3f);
                    }
                    else a.tr.position = next;
                    a.tr.Rotate(new Vector3(260f, 90f, 0f) * dt, Space.Self);
                }
                if (a.life > 14f)
                {
                    // しばらくしたら、地面にしずんで消える
                    a.tr.position += Vector3.down * dt * 0.3f;
                    if (a.life > 16f)
                    {
                        Destroy(a.tr.gameObject);
                        _acorns.RemoveAt(i);
                    }
                }
            }
        }

        void Puff(Vector3 top)
        {
            Puffs++;
            _fx?.Spores(top);
            AreaSounds.Instance?.PlayAt("puff", top, 0.7f, R(0.9f, 1.1f));
        }

        void DropAcorn(Vector3 from)
        {
            var mesh = _world.assets.Get("Acorn");
            if (mesh == null || _acorns.Count > 4) return;
            var mr = MakeMesh("FallingAcorn", mesh, _world.assets.propGlossy, R(0.9f, 1.15f));
            mr.transform.position = from;
            mr.transform.rotation = Random.rotation;
            _acorns.Add(new FallingAcorn { tr = mr.transform, vel = new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f)) });
            AcornsDropped++;
        }

        // ------------------------------------------------------------------
        // 川辺
        // ------------------------------------------------------------------
        void BuildDrift()
        {
            var leafMeshes = new[] { "Leaf_Oak_Orange", "Leaf_Maple_Red", "Leaf_Oak_Brown", "Leaf_Maple_Yellow", "Leaf_Oak_Green" };
            for (int i = 0; i < 9; i++)
            {
                var mesh = _world.assets.Get(leafMeshes[i % leafMeshes.Length]);
                if (mesh == null) continue;
                var mr = MakeMesh("DriftLeaf", mesh, _world.assets.prop, R(0.22f, 0.34f));
                var d = new Drifter { tr = mr.transform, spinVel = R(-40f, 40f) };
                // はじめは、川のあちこちに
                Respawn(d, R(-60f, 62f));
                _drift.Add(d);
            }
            var boat = _world.assets.Get("SasaBune");
            for (int i = 0; i < 2 && boat != null; i++)
            {
                var mr = MakeMesh("SasaBune_Drift", boat, _world.assets.flowers, 0.9f);
                var d = new Drifter { tr = mr.transform, boat = true, spinVel = R(-10f, 10f) };
                Respawn(d, i == 0 ? R(-20f, 50f) : 62f);
                if (i == 1) d.wait = R(10f, 25f);
                _drift.Add(d);
            }
        }

        void Respawn(Drifter d, float z)
        {
            float x = RiverLayout.CenterX(z) + R(-0.6f, 0.6f) * RiverLayout.HalfWidth(z);
            d.pos = new Vector3(x, RiverLayout.WaterLevel(z) + 0.04f, z);
            d.fallT = -1f;
            d.bounces = 0;
            d.tr.position = d.pos;
        }

        void UpdateRiver(InchwormController worm, float dt)
        {
            foreach (var d in _drift)
            {
                if (d.wait > 0f)
                {
                    d.wait -= dt;
                    d.tr.gameObject.SetActive(false);
                    continue;
                }
                d.tr.gameObject.SetActive(true);
                StepDrifter(d, worm, dt);
            }
            // よどみで、ときどき魚がはねる（波紋と音）
            if (Time.time > _fishNext)
            {
                _fishNext = Time.time + R(14f, 28f);
                float pz = RiverLayout.PoolZ;
                Vector3 p = new Vector3(RiverLayout.CenterX(pz) + R(-8f, 8f), RiverLayout.WaterLevel(pz), pz + R(-8f, 8f));
                if (RiverLayout.Height(p.x, p.z) < p.y - 0.4f) FishJump(p);
            }
            // 葉っぱの舟が岸や中州に着くと「ことん」
            var ferry = _world.Ferry;
            if (ferry != null)
            {
                bool a = ferry.AtA, b = ferry.AtB;
                if ((a && !_ferryAtA) || (b && !_ferryAtB)) AreaSounds.Instance?.PlayAt("clunk", ferry.transform.position, 0.45f, 1.5f);
                _ferryAtA = a;
                _ferryAtB = b;
            }
            // 倒れ枝の橋の上を歩くと、ときどききしむ
            if (worm != null && Time.time > _creakNext && !worm.IsFalling)
            {
                Vector3 c = worm.CenterPosition;
                Vector2 a2 = new Vector2(_world.BridgeA.x, _world.BridgeA.z), b2 = new Vector2(_world.BridgeB.x, _world.BridgeB.z);
                if (ShakuMath.DistToSegment(new Vector2(c.x, c.z), a2, b2) < 1.2f && c.y > RiverLayout.WaterLevel(c.z) + 0.8f && worm.IsMoving)
                {
                    _creakNext = Time.time + R(2.5f, 5f);
                    AreaSounds.Instance?.PlayAt("creak", c, 0.25f, R(0.45f, 0.6f));
                }
            }
        }

        /// <summary>流れていく物を 1 フレームすすめる：流れの速さで流れ、岸ではね返り、滝を落ちて、滝つぼで回る。</summary>
        void StepDrifter(Drifter d, InchwormController worm, float dt)
        {
            if (d.fallT >= 0f)
            {
                // 滝を落ちる（落ちたら、滝つぼでくるりと回る）
                d.fallT += dt;
                float k = Mathf.Clamp01(d.fallT / 0.7f);
                float top = RiverLayout.UpperLevel, bottom = RiverLayout.WaterLevel(RiverLayout.FallZ - 1.5f);
                d.pos.y = Mathf.Lerp(top, bottom, k * k) + 0.04f;
                d.pos.z -= dt * 3.5f;
                d.spinVel += 360f * dt;
                if (k >= 1f)
                {
                    d.fallT = -1f;
                    d.spinVel = (R01() < 0.5f ? -1f : 1f) * R(220f, 320f);
                    _fx?.Splashlet(d.pos);
                }
            }
            else
            {
                Vector3 flow = Creatures.WaterFlow(d.pos, _area);
                float speed = Mathf.Max(0.12f, flow.magnitude);
                Vector3 dir = flow.sqrMagnitude > 1e-4f ? flow.normalized : Vector3.back;
                // 笹舟は、しゃくとりむしが近いと少しのあいだ止まる（のぞいて見られる）
                if (d.boat && worm != null && (worm.HeadPosition - d.pos).sqrMagnitude < 2.6f * 2.6f) d.hold = 2.5f;
                d.hold = Mathf.Max(0f, d.hold - dt);
                float k = d.hold > 0f ? 0.08f : 1f;
                Vector3 next = d.pos + dir * (speed * k * dt) + new Vector3(Mathf.Sin(Time.time * 0.7f + d.spin * 0.01f), 0f, 0f) * (0.1f * dt);
                // 岸や石に当たると、はね返る
                if (!RiverLayout.InChannel(next.x, next.z, -0.6f) || RiverLayout.Height(next.x, next.z) > RiverLayout.WaterLevel(next.z) - 0.05f)
                {
                    float cx = RiverLayout.CenterX(next.z);
                    next.x = d.pos.x + Mathf.Sign(cx - d.pos.x) * Mathf.Max(0.01f, speed * k * dt * 2f);   // 川のまん中の方へ、少しもどる
                    d.spinVel = -d.spinVel + R(-60f, 60f);
                    d.bounces++;
                }
                if (d.pos.z > RiverLayout.FallZ && next.z <= RiverLayout.FallZ) d.fallT = 0f;   // 滝のふち
                next.y = RiverLayout.WaterLevel(next.z) + 0.04f + 0.015f * Mathf.Sin(Time.time * 2f + d.spin);
                d.pos = next;
                d.spinVel = Mathf.MoveTowards(d.spinVel, Mathf.Sign(d.spinVel) * 25f * speed, dt * 30f);
            }
            d.spin += d.spinVel * dt;
            d.tr.position = d.pos;
            d.tr.rotation = Quaternion.Euler(0f, d.spin, 0f) * (d.boat ? Quaternion.identity : Quaternion.Euler(3f * Mathf.Sin(Time.time + d.spin), 0f, 0f));
            if (d.pos.z < -64f || new Vector2(d.pos.x, d.pos.z).magnitude > 70f)
            {
                Respawn(d, 64f);
                if (d.boat) d.wait = R(15f, 35f);
            }
        }

        float R01() => (float)_rng.NextDouble();

        void FishJump(Vector3 p)
        {
            FishJumps++;
            _fx?.Splash(p, 0.25f, p.y);
            AreaSounds.Instance?.PlayAt("fish_jump", p, 0.55f, R(0.9f, 1.15f));
        }

        // ------------------------------------------------------------------
        // 公園
        // ------------------------------------------------------------------
        void UpdatePark(InchwormController worm, float dt)
        {
            // ブランコが大きくゆれると、くさりがきしむ（いちばん端で）・きらっと光る
            _swingCreak -= dt;
            foreach (var s in _world.Swings)
            {
                if (s == null) continue;
                if (s.Amplitude > 9f && Mathf.Abs(s.Angle) > s.Amplitude * 0.97f && _swingCreak <= 0f)
                {
                    _swingCreak = 0.9f;
                    AreaSounds.Instance?.PlayAt("creak", s.transform.position + Vector3.down * 8f, 0.3f + 0.02f * s.Amplitude, R(0.9f, 1.1f));
                    _fx?.Glint(s.transform.position + Vector3.down * R(4f, 11f) + Vector3.right * R(-2f, 2f));
                }
            }
            // すべり台：すべりはじめたら「しゅーっ」、すべっている間は、きらきらの粒がうしろへ
            var slide = _world.Slide;
            if (slide != null)
            {
                if (slide.Sliding && !_sliding) AreaSounds.Instance?.PlayAt("fall", slide.transform.position, 0.45f, 1.45f);
                _sliding = slide.Sliding;
                if (_sliding && Random.value < dt * 30f) _fx?.SlideSparkle(slide.transform.position + Vector3.up * 0.2f, -slide.transform.forward);
            }
            // 水飲み場のじゃぐちのしずく：水たまりの波紋（ToonRiver の _Drip）と同じリズムで「ぽちゃ」
            float before = _dripPhase;
            _dripPhase = Mathf.Repeat(Time.timeSinceLevelLoad * DripRate, 1f);
            if (_dripPhase < before)
            {
                Vector3 drip = ParkDripPoint;
                AreaSounds.Instance?.PlayAt("plink", drip, 0.3f, R(0.95f, 1.1f));
                _fx?.TapDrip(drip);
            }
        }

        /// <summary>水たまりの波紋は、1 秒に 0.9 回（ToonRiver の _Drip の輪と同じ）。</summary>
        public const float DripRate = 0.9f;

        /// <summary>水たまりの、じゃぐちのしずくが落ちる場所。</summary>
        public static Vector3 ParkDripPoint
        {
            get
            {
                Vector2 c = ParkLayout.Puddle;
                Vector2 d = c + (ParkLayout.Fountain - c).normalized * (ParkLayout.PuddleRadius - 1.6f);
                return new Vector3(d.x, ParkLayout.WaterLevel, d.y);
            }
        }

        void OnSeesawBump(float strength)
        {
            if (_world == null || _world.Seesaw == null) return;
            SeesawBumps++;
            var t = _world.Seesaw.transform;
            Vector3 end = t.position + t.right * (8.6f * _world.Seesaw.DownSide);
            end.y = ParkLayout.Height(end.x, end.z);
            AreaSounds.Instance?.PlayAt("clunk", end, Mathf.Clamp(0.3f + strength * 0.08f, 0.3f, 0.8f), R(0.9f, 1.05f));
            _fx?.Dust(end, Vector3.up, Mathf.Clamp01(0.2f + strength * 0.05f), 0f);
        }

        void OnDestroy()
        {
            if (_world != null && _world.Seesaw != null) _world.Seesaw.Bumped -= OnSeesawBump;
        }
    }
}

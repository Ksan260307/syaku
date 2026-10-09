using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしが乗ったまま一緒に動く足場（葉っぱの渡し舟・いきもの）の目印。
    /// 動きを記録して、上の点の速さ（回転もふくむ）を答える。
    /// </summary>
    public class MovingPlatform : MonoBehaviour
    {
        Matrix4x4 _prev, _now;
        float _dt;
        bool _has;

        void OnEnable() => _has = false;

        void LateUpdate()
        {
            Matrix4x4 m = transform.localToWorldMatrix;
            _prev = _has ? _now : m;
            _now = m;
            _dt = Time.deltaTime;
            _has = true;
        }

        /// <summary>足場の上（または近く）の点の速さ。前のフレームの動きから求める。</summary>
        public Vector3 VelocityAt(Vector3 worldPoint)
        {
            if (!_has || _dt <= 1e-5f) return Vector3.zero;
            Vector3 local = _now.inverse.MultiplyPoint3x4(worldPoint);
            return ShakuPhysics.Sanitize(ShakuPhysics.PointVelocity(_prev, _now, local, _dt), ShakuPhysics.SafetySpeed);
        }

        /// <summary>当たった物が動く足場なら、その点の速さ（ちがえば 0）。</summary>
        public static Vector3 VelocityOf(Collider c, Vector3 worldPoint)
        {
            if (c == null) return Vector3.zero;
            var mp = c.GetComponentInParent<MovingPlatform>();
            return mp != null ? mp.VelocityAt(worldPoint) : Vector3.zero;
        }
    }

    /// <summary>
    /// 葉っぱの渡し舟。岸と中州のあいだを、待っては進むをくり返す。
    /// 水にういているので、乗ると少ししずみ、乗った側へかたむき、歩くたびにゆれる（浮力のばね）。
    /// しゃくとりむしより先に動かすため実行順を早めている。
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class RiverFerry : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Ferry = new Unity.Profiling.ProfilerMarker("Shaku.Ferry");
        public Vector3 dockA;
        public Vector3 dockB;
        public Quaternion baseRotation = Quaternion.identity;
        public float waitTime = 8f;   // 乗り降りに十分な時間
        public float travelTime = 7f;

        /// <summary>しゃくとりむしが乗ったときに、しずむ深さ。</summary>
        public const float LoadDip = 0.022f;

        float _clock, _bob;
        // 浮力のばね（しずみ・かたむき）
        float _dip, _dipVel;
        Vector2 _tilt, _tiltVel;   // x: 前後, y: 左右（度）
        // 川の流れで下流へ少し流され、つなぎ綱のばねでもどる
        Vector3 _drift, _driftVel;
        Vector3 _prevRouteVel;
        bool _wasTraveling;

        /// <summary>流れで下流へずれている量。</summary>
        public Vector3 Drift => _drift;

        /// <summary>0..1 = A で待つ / A→B / B で待つ / B→A の位置。</summary>
        public float Phase => Mathf.Repeat(_clock, Cycle) / Cycle;
        public float Cycle => 2f * (waitTime + travelTime);
        public bool AtA => Mathf.Repeat(_clock, Cycle) < waitTime;
        public bool AtB
        {
            get
            {
                float t = Mathf.Repeat(_clock, Cycle);
                return t >= waitTime + travelTime && t < 2f * waitTime + travelTime;
            }
        }

        /// <summary>いま、水にしずんでいる深さ（乗るとしずむ）。</summary>
        public float Dip => _dip;
        /// <summary>いまのかたむき（度）。</summary>
        public float TiltDegrees => _tilt.magnitude;
        /// <summary>舟の速さ（流れにそって進む速さ＋ゆれ）。</summary>
        public Vector3 Velocity
        {
            get
            {
                const float e = 0.02f;
                Vector3 v = (Evaluate(_clock + e, out _) - Evaluate(_clock - e, out _)) / (2f * e);
                return v + Vector3.down * _dipVel;
            }
        }

        /// <summary>道の上の位置。bob は、ゆれの時計（岸で待っている間も、ゆれはつづく）。</summary>
        public Vector3 Evaluate(float clock, out Quaternion rot, float bob = float.NaN)
        {
            if (float.IsNaN(bob)) bob = clock;
            float t = Mathf.Repeat(clock, Cycle);
            float k;
            if (t < waitTime) k = 0f;
            else if (t < waitTime + travelTime) k = ShakuMath.Smoother01((t - waitTime) / travelTime);
            else if (t < 2f * waitTime + travelTime) k = 1f;
            else k = 1f - ShakuMath.Smoother01((t - 2f * waitTime - travelTime) / travelTime);
            Vector3 p = Vector3.Lerp(dockA, dockB, k);
            p.y += Mathf.Sin(bob * 1.7f) * 0.025f;
            rot = baseRotation * Quaternion.Euler(Mathf.Sin(bob * 1.3f) * 1.2f, Mathf.Sin(bob * 0.4f) * 3f * (k > 0f && k < 1f ? 1f : 0.3f), Mathf.Sin(bob * 1.1f) * 1.5f);
            return p;
        }

        void Start() => Apply();

        void Update()
        {
            using var prof = s_Ferry.Auto();   // 処理時間の計測（パフォーマンスの調整用）
            float dt = Time.deltaTime;
            _clock += dt;
            _bob += dt;
            // 乗っているしゃくとりむしの重さで、しずんで、乗った側へかたむく
            var worm = InchwormController.Instance;
            bool loaded = worm != null && worm.PlatformUnder == transform;
            if (worm != null && Application.isPlaying) Steer(worm, dt);
            float dipTarget = loaded ? LoadDip : 0f;
            Vector2 tiltTarget = Vector2.zero;
            if (loaded)
            {
                Vector3 local = transform.InverseTransformPoint(worm.CenterPosition);
                Vector3 c = Vector3.zero;
                var col = GetComponent<Collider>();
                if (col != null) c = transform.InverseTransformPoint(col.bounds.center);
                Vector3 d = Vector3.Scale(local - c, transform.lossyScale);
                // はしに乗るほど、大きくかたむく（てこ）
                tiltTarget = new Vector2(Mathf.Clamp(d.z * 0.9f, -2.5f, 2.5f), Mathf.Clamp(-d.x * 0.9f, -2.5f, 2.5f));
            }
            // 水の浮力はやわらかいばね：少し行き過ぎてから落ちつく
            ShakuPhysics.SpringSteps(ref _dip, ref _dipVel, dipTarget, 6f, 0.3f, dt);
            Vector3 tv = new Vector3(_tilt.x, _tilt.y, 0f), tvel = new Vector3(_tiltVel.x, _tiltVel.y, 0f);
            ShakuPhysics.SpringSteps(ref tv, ref tvel, new Vector3(tiltTarget.x, tiltTarget.y, 0f), 4.5f, 0.25f, dt);
            _tilt = new Vector2(Mathf.Clamp(tv.x, -6f, 6f), Mathf.Clamp(tv.y, -6f, 6f));
            _tiltVel = new Vector2(tvel.x, tvel.y);
            _dip = Mathf.Clamp(_dip, -0.05f, 0.08f);

            // 動きだすと舳先（へさき）が上がり、止まると下がる（水の上の、体の重さ）
            Vector3 routeVel = RouteVelocity();
            Vector3 acc = dt > 1e-5f ? (routeVel - _prevRouteVel) / dt : Vector3.zero;
            _prevRouteVel = routeVel;
            Vector3 accLocal = transform.InverseTransformDirection(acc);
            _tiltVel += new Vector2(-accLocal.z, accLocal.x) * (4f * dt);
            // 岸に着くと、とんと当たって少しゆれる
            bool traveling = !AtA && !AtB;
            if (_wasTraveling && !traveling) _dipVel += 0.08f;
            _wasTraveling = traveling;
            // 川の流れに押されて下流へ少しずれ、つなぎ綱のばねでもどる
            Vector3 flow = Creatures.WaterFlow(transform.position, Areas.Current);
            ShakuPhysics.SpringSteps(ref _drift, ref _driftVel, flow * 0.25f, 1.2f, 0.7f, dt);
            _drift = Vector3.ClampMagnitude(new Vector3(_drift.x, 0f, _drift.z), 0.12f);
            Apply();
        }

        // しゃくとりむしに合わせた動き
        float _aboard, _call;
        bool _arrivedAboard;

        /// <summary>しゃくとりむしが近くの岸（または中州）にいるか。</summary>
        public System.Func<Vector3, bool> NearA, NearB;

        /// <summary>
        /// しゃくとりむしに合わせて、待つ・出る・むかえに行く。
        /// 近づいてくる間は待つ。頭もしっぽも乗ったら、少しして出る。向こう岸で待っていたら、むかえに行く。
        /// 乗ったまま着いたら、降りるまで待つ（しばらく乗ったままなら、また出る）。
        /// </summary>
        void Steer(InchwormController worm, float dt)
        {
            bool atA = AtA, atB = AtB;
            bool aboard = worm.FullyOn(transform);
            bool onIt = worm.PlatformUnder == transform;
            if (!atA && !atB)
            {
                _aboard = 0f;
                _call = 0f;
                _arrivedAboard = onIt;
                return;
            }
            Vector3 wp = worm.CenterPosition;
            bool nearHere = onIt || (atA ? IsNear(NearA, dockA, wp) : IsNear(NearB, dockB, wp));
            bool nearThere = !onIt && (atA ? IsNear(NearB, dockB, wp) : IsNear(NearA, dockA, wp));
            if (aboard)
            {
                _aboard += dt;
                _call = 0f;
                if (_arrivedAboard ? _aboard > ReboardWait : _aboard > BoardWait) { _arrivedAboard = false; Depart(atA); }
                else Hold(atA, dt);
                return;
            }
            _aboard = 0f;
            if (!onIt) _arrivedAboard = false;
            if (nearHere) { Hold(atA, dt); _call = 0f; }
            else if (nearThere) { _call += dt; if (_call > CallWait) { _call = 0f; Depart(atA); } }
            else _call = 0f;
        }

        /// <summary>頭もしっぽも乗ってから、出るまでの時間。</summary>
        public const float BoardWait = 1.2f;
        /// <summary>乗ったまま着いて、そのまま乗っていたら、また出るまでの時間。</summary>
        public const float ReboardWait = 5f;
        /// <summary>向こう岸で待っているのに気づいて、むかえに出るまでの時間。</summary>
        public const float CallWait = 0.8f;

        static bool IsNear(System.Func<Vector3, bool> near, Vector3 dock, Vector3 p)
        {
            if (near != null) return near(p);
            return new Vector2(p.x - dock.x, p.z - dock.z).magnitude < 7f;
        }

        float CycleStart => Mathf.Floor(_clock / Cycle) * Cycle;

        /// <summary>岸で待ちつづける（待ち時間の終わりの手前で、時計を止める）。</summary>
        void Hold(bool atA, float dt)
        {
            float t = Mathf.Repeat(_clock, Cycle);
            float end = atA ? waitTime : 2f * waitTime + travelTime;
            if (t > end - 1f) _clock -= dt;
        }

        /// <summary>すぐに出る。</summary>
        void Depart(bool atA)
        {
            _clock = CycleStart + (atA ? waitTime : 2f * waitTime + travelTime) + 0.001f;
        }

        /// <summary>決まった道を進む速さ（ゆれやしずみはふくめない）。</summary>
        Vector3 RouteVelocity()
        {
            const float e = 0.02f;
            return (Evaluate(_clock + e, out _) - Evaluate(_clock - e, out _)) / (2f * e);
        }

        /// <summary>舟の上で歩く・落ちてくると、その場所がしずんでゆれる。</summary>
        public void Push(Vector3 worldPoint, float strength)
        {
            _dipVel += 0.12f * strength;
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 c = Vector3.zero;
            var col = GetComponent<Collider>();
            if (col != null) c = transform.InverseTransformPoint(col.bounds.center);
            Vector3 d = Vector3.Scale(local - c, transform.lossyScale);
            _tiltVel += new Vector2(d.z, -d.x) * (6f * strength);
        }

        void Apply()
        {
            Vector3 p = Evaluate(_clock, out var r, _bob);
            p.y -= _dip;
            p += _drift;
            // 風が強いと、波で大きくゆれる（波の高さは風の速さの 2 乗）
            if (Application.isPlaying)
            {
                Vector3 w = Wind.At(p);
                p.y += Mathf.Sin(_clock * 2.3f + 0.7f) * 0.012f * Mathf.Min(1.5f, w.sqrMagnitude);
            }
            r = r * Quaternion.Euler(_tilt.x, 0f, _tilt.y);
            transform.SetPositionAndRotation(p, r);
            Physics.SyncTransforms();
        }

        /// <summary>テストや演出用：指定の時刻へ進める。</summary>
        public void SetClock(float t)
        {
            _clock = t;
            _bob = t;
            Apply();
        }
    }
}

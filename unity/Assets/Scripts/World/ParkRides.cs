using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// ブランコ：わくの棒からつるした座板（くさりごと、棒を軸にゆれる）。
    /// ふだんは風で小さくゆれ、しゃくとりむしが乗ると、だんだん大きくこぐ。乗ったまま一緒にゆれる（動く足場）。
    /// しゃくとりむしより先に動かすため、実行順を早めている。
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class SwingRide : MonoBehaviour
    {
        public float phase;
        /// <summary>1 往復の時間（秒）。</summary>
        public const float Period = 4.2f;
        public const float IdleAmplitude = 6f, RideAmplitude = 18f;

        Quaternion _base;
        float _amp = IdleAmplitude;
        float _clock;
        bool _init;

        /// <summary>いまのゆれの角度（度）と、ゆれの大きさ。</summary>
        public float Angle { get; private set; }
        public float Amplitude => _amp;
        public bool Loaded { get; private set; }

        void Init()
        {
            _base = transform.localRotation;
            _init = true;
        }

        void Update()
        {
            if (!_init) Init();
            float dt = Time.deltaTime;
            _clock += dt;
            var worm = InchwormController.Instance;
            Loaded = worm != null && worm.PlatformUnder == transform;
            // 風が強いと、乗っていなくても少し大きくゆれる
            float wind = Application.isPlaying ? Mathf.Min(1.5f, Wind.At(transform.position).magnitude) : 0f;
            float target = Loaded ? RideAmplitude : IdleAmplitude + 3f * wind;
            _amp = Mathf.MoveTowards(_amp, target, dt * (Loaded ? 2.5f : 1.2f));
            Angle = _amp * Mathf.Sin(_clock * 2f * Mathf.PI / Period + phase);
            transform.localRotation = _base * Quaternion.Euler(Angle, 0f, 0f);
            Physics.SyncTransforms();
        }
    }

    /// <summary>
    /// シーソー：板の上をしゃくとりむしが歩くと、乗っている側へゆっくりかたむく（ぎっこん、ばったん）。
    /// 地面に当たると、少しはね返ってとまる。降りても、そのままのかたむき。
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class SeesawRide : MonoBehaviour
    {
        public const float MaxTilt = 11f;
        Quaternion _base;
        float _angle, _vel;
        float _down = 1f;     // +1 なら東（+X）のはしが下
        bool _init;

        public float Angle => _angle;
        /// <summary>地面に当たった（強さ：当たったときの速さ。角度/秒を 20 でわったもの）。</summary>
        public event System.Action<float> Bumped;
        /// <summary>下がっている側（+1 = +X のはし）。</summary>
        public float DownSide => _down;

        void Init()
        {
            _base = transform.localRotation;
            _angle = -MaxTilt * _down;
            _init = true;
        }

        void Update()
        {
            if (!_init) Init();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var worm = InchwormController.Instance;
            if (worm != null && worm.PlatformUnder == transform)
            {
                // 板のまん中から、乗っている側（体のまん中で決める）
                float x = transform.InverseTransformPoint(worm.CenterPosition).x;
                if (Mathf.Abs(x) > 0.6f) _down = Mathf.Sign(x);
            }
            float target = -MaxTilt * _down;
            ShakuPhysics.SpringSteps(ref _angle, ref _vel, target, 2.6f, 0.45f, dt);
            if (Mathf.Abs(_angle) > MaxTilt)
            {
                // 地面に当たって、少しはね返る（音と土けむり）
                _angle = Mathf.Sign(_angle) * MaxTilt;
                if (Mathf.Abs(_vel) > 4f) Bumped?.Invoke(Mathf.Abs(_vel) / 20f);
                _vel = -_vel * 0.25f;
            }
            transform.localRotation = _base * Quaternion.Euler(0f, 0f, _angle);
            Physics.SyncTransforms();
        }

        /// <summary>テスト用：反対がわへかたむけはじめる（ばねで動いて、地面に当たる）。</summary>
        public void Flip()
        {
            if (!_init) Init();
            _down = -_down;
        }

        /// <summary>テスト用：かたむきを決める（+1 = +X のはしが下）。</summary>
        public void SetDown(float side)
        {
            if (!_init) Init();
            _down = Mathf.Sign(side);
            _angle = -MaxTilt * _down;
            _vel = 0f;
            transform.localRotation = _base * Quaternion.Euler(0f, 0f, _angle);
            Physics.SyncTransforms();
        }
    }

    /// <summary>
    /// すべり台をすべる：すべる面の上にある見えない「そり」（動く足場）。坂の上で、しゃくとりむしが下を向いて乗ると
    /// （またはしばらく乗っていると）、坂にそって加速しながらすべりおり、出口の平らな所でとまる。
    /// しゃくとりむしが降りたら、見えないまま坂の上へもどる。下から坂を登ってくるときは、すべらない。
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class SlideRide : MonoBehaviour
    {
        public Transform slide;      // すべり台（ローカル座標で坂の場所を決める）
        // すべり台のローカル座標（Blender で作った形と同じ）。上のはし → 坂の下 → 出口の先
        public static readonly Vector3 RampTop = new Vector3(0f, 10f, -2.3f);
        public static readonly Vector3 RampLow = new Vector3(0f, 0.55f, -17.9f);
        public static readonly Vector3 RampEnd = new Vector3(0f, 0.4f, -20.9f);
        public const float StartAt = 1.6f;     // そりが待つ場所（坂の上から）
        public const float Accel = 1.8f, MaxSpeed = 6f, Brake = 7f;

        enum Phase { Waiting, Sliding, Stopped, Returning }
        Phase _phase = Phase.Waiting;
        float _s = StartAt, _v, _loadedFor, _awayFor;
        BoxCollider _box;

        public bool Sliding => _phase == Phase.Sliding;
        public float Distance => _s;
        public float Speed => _v;
        public float Length => SegLen(0) + SegLen(1);
        public int Rides { get; private set; }

        Vector3 P(int i) => slide.TransformPoint(i == 0 ? RampTop : i == 1 ? RampLow : RampEnd);
        float SegLen(int i) => Vector3.Distance(P(i), P(i + 1));

        void Start()
        {
            _box = GetComponent<BoxCollider>();
            Apply();
        }

        /// <summary>坂にそった場所 s での位置と向き。</summary>
        Vector3 At(float s, out Vector3 dir)
        {
            float l0 = SegLen(0);
            if (s <= l0)
            {
                dir = (P(1) - P(0)).normalized;
                return P(0) + dir * s;
            }
            dir = (P(2) - P(1)).normalized;
            return P(1) + dir * Mathf.Min(s - l0, SegLen(1));
        }

        void Apply()
        {
            if (slide == null) return;
            Vector3 p = At(_s, out var dir);
            Vector3 side = slide.right;
            Vector3 up = Vector3.Cross(dir, side).normalized;
            if (up.y < 0f) up = -up;
            transform.SetPositionAndRotation(p + up * 0.11f, Quaternion.LookRotation(dir, up));
            Physics.SyncTransforms();
        }

        void Update()
        {
            if (slide == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var worm = InchwormController.Instance;
            bool loaded = worm != null && worm.PlatformUnder == transform;
            At(_s, out var dir);
            switch (_phase)
            {
                case Phase.Waiting:
                    // 下を向いて乗ったら、すべりだす（登ってくるときは、すべらない）
                    _loadedFor = loaded ? _loadedFor + dt : 0f;
                    if (loaded && (Vector3.Dot(worm.Heading, dir) > 0.2f || _loadedFor > 1.5f))
                    {
                        _phase = Phase.Sliding;
                        _v = 0f;
                        Rides++;
                        AudioManager.Instance?.Travel();
                    }
                    break;
                case Phase.Sliding:
                    float l0 = SegLen(0);
                    if (_s < l0) _v = Mathf.Min(MaxSpeed, _v + Accel * dt);
                    else _v = Mathf.Max(0.6f, _v - Brake * dt);   // 出口の平らな所で、ぐっと止まる
                    _s += _v * dt;
                    if (_s >= Length)
                    {
                        _s = Length;
                        _v = 0f;
                        _phase = Phase.Stopped;
                        _awayFor = 0f;
                    }
                    break;
                case Phase.Stopped:
                    _awayFor = loaded ? 0f : _awayFor + dt;
                    if (_awayFor > 1f)
                    {
                        // 見えないまま、坂の上へもどる（当たり判定は切っておく）
                        _phase = Phase.Returning;
                        if (_box != null) _box.enabled = false;
                        _s = StartAt;
                    }
                    break;
                case Phase.Returning:
                    // 坂の上に、しゃくとりむしがいなければ、また乗れるようにする
                    Vector3 top = At(StartAt, out _);
                    if (worm == null || Vector3.Distance(worm.CenterPosition, top) > 2.5f)
                    {
                        if (_box != null) _box.enabled = true;
                        _phase = Phase.Waiting;
                        _loadedFor = 0f;
                    }
                    break;
            }
            Apply();
        }
    }
}

using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしの表情と、体の小さな動き（InchwormBody の頂点を動かす値を、毎フレーム決める）：
    /// まばたき・目線・口・触角のゆれ・脚のふんばりとこぎ・体節のふくらみと伸び縮み。
    /// 体の色あい（ぬれたつや・ほこり・日なたと日かげ・光るキノコやホタルの光）も、ここで決める。
    /// </summary>
    public partial class InchwormController
    {
        // ---- まばたき・目線 ----
        float _blinkT = -1f, _nextBlink = 2.5f, _blinkSpeed = 1f;
        bool _doubleBlink;
        float _squint;                   // 目を細める（日なたぼっこ・まぶしい）
        float _eyesShut;                 // 目をとじている（眠る・がまん）
        Vector2 _gaze;
        // ---- 口 ----
        float _mouth, _mouthWant;
        float _yawnUntil, _yawnStart;
        float _chewUntil;
        // ---- 触角・脚・体節 ----
        Vector2 _antennaSway, _antennaVel;
        Vector3 _prevHeadC, _prevHeadV;
        bool _faceReady;
        float _headPulse;                // 頭（胸脚）をついた瞬間に、ぺたっ
        float _segPhase;
        float _paddle;
        // ---- 体の色あい ----
        float _dust;                     // ほこりっぽさ（砂や土の上を歩くと、たまる）
        float _sunlit = 1f, _sunTimer;   // 日なた（1）か日かげ（0）か
        float _glow;                     // 光るキノコ・ホタルのそば
        Color _glowColor = Color.white;
        float _cold;                     // 寒い所（夜の森・山の高い所・ぬれて風にふかれる）

        /// <summary>口のあきぐあい（0〜1）。WormWear が口をえがく。</summary>
        public float MouthOpen => _mouth;
        /// <summary>まぶたの閉じぐあい（テスト用）。</summary>
        public float BlinkAmount => body != null ? body.blink : 0f;
        /// <summary>目線（テスト用）。</summary>
        public Vector2 Gaze => _gaze;
        /// <summary>日なたにいるか（0〜1）。</summary>
        public float Sunlit => _sunlit;
        /// <summary>ほこりっぽさ（0〜1）。</summary>
        public float Dust => _dust;
        /// <summary>光るキノコ・ホタルにてらされている強さ（0〜1）。</summary>
        public float GlowAmount => _glow;
        /// <summary>寒くて、ふるえている（0〜1）。</summary>
        public float Cold => _cold;

        /// <summary>体の色あい（GameManager が、きせかえの色にかける）。</summary>
        public struct Tint
        {
            public float value, saturation, rim;
            public Color rimColor;
        }

        /// <summary>いまの体の色あい：ぬれるとつやが出て少し濃く、ほこりで白っぽくくすみ、日なたで明るく、光るものの近くでは、その色にてらされる。</summary>
        public Tint BodyTint
        {
            get
            {
                float wet = IsWet ? 1f : 0f;
                var t = new Tint
                {
                    value = (1f - 0.08f * wet) * (1f - 0.1f * _dust) * Mathf.Lerp(0.96f, 1.05f, _sunlit),
                    saturation = (1f + 0.12f * wet) * (1f - 0.35f * _dust),
                    rim = 0.35f * wet + 0.1f * _sunlit + 0.75f * _glow,
                    rimColor = Color.Lerp(new Color(1f, 0.97f, 0.88f), _glowColor, Mathf.Clamp01(_glow * 1.5f)),
                };
                return t;
            }
        }

        /// <summary>まばたきをする（びっくりした・まぶしい・着地した）。</summary>
        public void TriggerBlink(float speed = 1f)
        {
            if (_blinkT >= 0f) return;
            _blinkT = 0f;
            _blinkSpeed = speed;
        }

        /// <summary>歩いた所のほこり（砂・土の上を歩くと、少しずつたまる。水にぬれると落ちる）。</summary>
        public void AddDust(float amount) => _dust = Mathf.Clamp01(_dust + amount);

        /// <summary>
        /// 表情と体の小さな動きを決めて、体（InchwormBody）にわたす。体を曲げる直前に呼ぶ。
        /// </summary>
        void UpdateFace(float dt)
        {
            if (body == null) return;
            float t = Time.time;
            // 動きだしたら（休んでいないとき）、眠りと日なたぼっこは、すぐにさめる
            if (State != Mode.Idle || _moveMag > 0.2f || _standRequested || IsAiming)
            {
                _sleep = Mathf.MoveTowards(_sleep, 0f, dt * 4f);
                _bask = Mathf.MoveTowards(_bask, 0f, dt * 3f);
            }

            // ---- まばたき：2〜6 秒ごと（ときどき 2 回つづけて）。びっくりすると、すぐ ----
            if (_blinkT < 0f && t > _nextBlink)
            {
                _blinkT = 0f;
                _blinkSpeed = 1f;
                _doubleBlink = UnityEngine.Random.value < 0.2f;
                _nextBlink = t + UnityEngine.Random.Range(2f, 6f) * (_relax > 0.5f ? 1.6f : 1f);
            }
            float blinkAnim = 0f;
            if (_blinkT >= 0f)
            {
                _blinkT += dt * _blinkSpeed;
                const float dur = 0.26f;
                blinkAnim = Mathf.Sin(Mathf.PI * Mathf.Clamp01(_blinkT / dur));
                if (_blinkT >= dur)
                {
                    if (_doubleBlink) { _doubleBlink = false; _blinkT = -0.06f; }
                    else _blinkT = -1f;
                    if (_blinkT < -0.5f) _blinkT = -1f;
                }
            }
            if (_flinch > 0.8f) TriggerBlink(1.6f);
            // 眠る・がまんで目をとじる。日なたぼっこでは目を細める
            _eyesShut = Mathf.MoveTowards(_eyesShut, EyesShutWant(), dt * 2.5f);
            _squint = Mathf.MoveTowards(_squint, SquintWant(), dt * 1.5f);
            body.blink = Mathf.Clamp01(Mathf.Max(blinkAnim, Mathf.Max(_squint * 0.55f, _eyesShut)));

            // ---- 目線：見ている物（いきもの・しずく・ねらう所・カメラ）の方へ、目の光を動かす ----
            Vector2 gazeWant = Vector2.zero;
            if (GazeTarget(out var target))
            {
                Vector3 d = target - body.HeadCenter;
                if (d.sqrMagnitude > 1e-4f)
                {
                    d.Normalize();
                    Vector3 right = Vector3.Cross(body.HeadUp, body.HeadForward);
                    gazeWant = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, body.HeadUp)) * 1.6f;
                }
            }
            else gazeWant = new Vector2(Mathf.Sin(t * 0.37f) * 0.25f, Mathf.Sin(t * 0.23f + 1f) * 0.15f);   // なにも見ていないときは、ゆっくりきょろきょろ
            gazeWant = Vector2.ClampMagnitude(gazeWant, 1f);
            _gaze = Vector2.Lerp(_gaze, gazeWant, ShakuMath.DampFactor(8f, dt));
            body.gaze = _gaze;

            // ---- 口 ----
            _mouthWant = MouthWant();
            _mouth = Mathf.MoveTowards(_mouth, _mouthWant, dt * 7f);

            // ---- 触角：頭の加速と反対へ残り、風にゆれる（ばね） ----
            Vector3 hc = body.HeadCenter;
            Vector2 push = Vector2.zero;
            if (_faceReady && dt > 1e-4f)
            {
                Vector3 v = (hc - _prevHeadC) / dt;
                Vector3 a = Vector3.ClampMagnitude((v - _prevHeadV) / dt, 40f);
                _prevHeadV = ShakuPhysics.IsFinite(v) ? Vector3.ClampMagnitude(v, 20f) : Vector3.zero;
                Vector3 right = Vector3.Cross(body.HeadUp, body.HeadForward);
                Vector3 w = Wind.At(hc);
                push = new Vector2(Vector3.Dot(-a, right), Vector3.Dot(-a, body.HeadUp)) * 0.02f + new Vector2(Vector3.Dot(w, right), 0.2f * Mathf.Sin(t * 3f)) * 0.5f;
            }
            _prevHeadC = hc;
            _faceReady = true;
            ShakuPhysics.SpringExact(ref _antennaSway.x, ref _antennaVel.x, Mathf.Clamp(push.x, -1f, 1f), 14f, 0.25f, dt);
            ShakuPhysics.SpringExact(ref _antennaSway.y, ref _antennaVel.y, Mathf.Clamp(push.y, -1f, 1f), 14f, 0.25f, dt);
            if (!ShakuPhysics.IsFinite(new Vector3(_antennaSway.x, _antennaSway.y, 0f))) _antennaSway = _antennaVel = Vector2.zero;
            body.antenna = Vector2.ClampMagnitude(_antennaSway, 1f);

            // ---- 脚：尾（腹脚）・頭（胸脚）をついた瞬間に、ぺたっ。踏んばるときは、ぐっと。細い物は、はさむ ----
            _headPulse = Mathf.MoveTowards(_headPulse, 0f, dt * 5f);
            float brace = Mathf.Clamp01(_platformVel.magnitude * 0.5f + Mathf.Max(0f, Wind.Gust(t) - 0.6f) * 1.5f + (IsBlocked && _moveMag > 0.2f ? 0.6f : 0f));
            body.tailGrip = Mathf.Clamp01(Mathf.Max(_gripPulse, brace));
            body.headGrip = Mathf.Clamp01(Mathf.Max(_headPulse, brace * 0.8f));
            body.legPinch = Mathf.MoveTowards(body.legPinch, _narrow && State != Mode.Hang && State != Mode.Fall ? 0.8f : 0f, dt * 3f);
            // 糸をのぼる・泳ぐときは、胸脚でこぐ
            bool paddling = (State == Mode.Hang && !_reeling && _silkSpeed < -0.1f) || IsSwimming;
            _paddle = Mathf.MoveTowards(_paddle, paddling ? 1f : 0f, dt * 4f);
            body.legPaddle = _paddle;
            body.legPhase += dt * (IsSwimming ? 9f : 14f);

            // ---- 体節：歩くと、ふくらみと伸び縮みの波が、尾から頭へ伝わる。止まっていても、息で少しだけ ----
            bool walking = State == Mode.Pull || State == Mode.Reach;
            _segPhase += dt * (walking ? 11f : 2f);
            float k = walking ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t)) : 0f;
            body.segmentPhase = _segPhase;
            body.segmentBulge = walking ? 0.03f + 0.03f * k : 0.012f * (1f + _fatigue);
            body.segmentStretch = State == Mode.Reach ? 0.28f * k : State == Mode.Pull ? 0.2f * k : 0f;

            UpdateBodyLight(dt);
        }

        /// <summary>見ている物があれば、その場所。</summary>
        bool GazeTarget(out Vector3 p)
        {
            p = default;
            if (IsAiming) { p = AimPoint; return true; }
            if (Time.time < _lookAtUntil) { p = _lookAtPoint; return true; }
            var col = Collectibles.Instance;
            if (col != null && col.NearestDrop(_curve.Head, 2.5f, out var drop)) { p = drop; return true; }
            return false;
        }

        /// <summary>目をとじる（眠っている・小枝のまねでがまんしている）。</summary>
        float EyesShutWant() => Mathf.Max(SleepAmount, _endure);

        /// <summary>目を細める（日なたぼっこ・あくび）。</summary>
        float SquintWant() => Mathf.Max(_bask, Time.time < _yawnUntil ? 1f : 0f);

        /// <summary>口のあきぐあい：うれしい（しずくを取った）・はあはあ（疲れた）・あくび・もぐもぐ・一息。</summary>
        float MouthWant()
        {
            float t = Time.time;
            float m = 0f;
            if (t < _cheerUntil) m = Mathf.Max(m, 0.75f);
            if (t < _yawnUntil) m = Mathf.Max(m, Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - _yawnStart) / (_yawnUntil - _yawnStart))));
            if (t < _chewUntil) m = Mathf.Max(m, 0.15f + 0.3f * Mathf.Abs(Mathf.Sin(t * 9f)));
            if (t < _sighUntil) m = Mathf.Max(m, 0.5f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - _sighStart) / (_sighUntil - _sighStart))));
            // はあはあ（疲れて止まっているとき）
            if (_fatigue > 0.45f && State == Mode.Idle) m = Mathf.Max(m, (0.15f + 0.25f * (_fatigue - 0.45f) / 0.55f) * (0.6f + 0.4f * Mathf.Sin(t * 9f)));
            if (Time.time < _flinchUntil) m = Mathf.Max(m, 0.4f);   // びっくりして、口をあける
            return Mathf.Clamp01(m);
        }

        float _flinchUntil;

        /// <summary>びっくりした：口をあけて、まばたきして、反射的に小枝のまねで一瞬かたまる。</summary>
        void Startle()
        {
            _flinchUntil = Time.time + 0.4f;
            TriggerBlink(1.6f);
            _freezeUntil = Mathf.Max(_freezeUntil, Time.time + 0.7f);
        }

        /// <summary>日なたか日かげか（0.4 秒ごとに、日の光の方へレイを飛ばす）、光るキノコ・ホタルのそばか、寒い所か。</summary>
        void UpdateBodyLight(float dt)
        {
            _sunTimer -= dt;
            if (_sunTimer <= 0f)
            {
                _sunTimer = 0.4f;
                var sun = WorldGenerator.Instance != null ? WorldGenerator.Instance.sun : null;
                bool lit = false;
                if (sun != null && !AreaAtmosphere.IsNight(Areas.Current.Id) && Areas.Current != Areas.Hut)
                {
                    Vector3 toSun = -sun.transform.forward;
                    if (toSun.y > 0.05f) lit = !Physics.Raycast(_curve.Middle + toSun * 0.15f, toSun, 80f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
                }
                _sunWant = lit ? 1f : 0f;
                // 光るキノコ・ホタルのむれ
                _glowWant = 0f;
                Vector3 m = _curve.Middle;
                var gen = WorldGenerator.Instance;
                if (gen != null)
                    foreach (var g in gen.GlowSpots)
                    {
                        float k = 1f - Vector3.Distance(g, m) / 2.2f;
                        if (k > _glowWant) { _glowWant = k; _glowColor = new Color(0.5f, 0.95f, 1f); }
                    }
                foreach (var (c, r, col) in AmbientFX.GlowSwarms)
                {
                    float k = (1f - Vector2.Distance(new Vector2(c.x, c.z), new Vector2(m.x, m.z)) / r) * 0.6f;
                    if (k > _glowWant) { _glowWant = k; _glowColor = col; }
                }
                // 寒い所：夜の森・山の高い所（地面から高いだけでなく、山の上の方）・ぬれて風にふかれている
                var area = Areas.Current;
                float cold = AreaAtmosphere.IsNight(area.Id) ? 0.7f : 0f;
                if (area == Areas.Mountain) cold = Mathf.Max(cold, Mathf.InverseLerp(30f, 55f, m.y));
                if (IsWet && Wind.Gust(Time.time) > 0.5f) cold = Mathf.Max(cold, 0.6f);
                _coldWant = cold;
            }
            _sunlit = Mathf.MoveTowards(_sunlit, _sunWant, dt * 1.5f);
            _glow = Mathf.MoveTowards(_glow, Mathf.Clamp01(_glowWant), dt * 2f);
            _cold = Mathf.MoveTowards(_cold, _coldWant, dt * 0.5f);
            // ほこりは、ぬれると落ち、ゆっくり落ちていく
            _dust = Mathf.MoveTowards(_dust, 0f, dt * (IsWet ? 1f : 0.008f));
        }

        float _sunWant = 1f, _glowWant, _coldWant;
    }
}

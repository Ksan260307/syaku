using System;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしの移動。
    ///  1) 引き寄せ（Pull）: 胸脚（頭）で踏んばり、腹脚（尾）を頭のそばまで引き寄せて体を Ω 字に持ち上げる
    ///  2) 伸び（Reach）  : 腹脚で踏んばり、前半身を持ち上げて前へ伸ばし、頭を次の場所に下ろす
    /// をくり返す。表面ならどこでも（壁・裏側も）這える。高い所からは糸を出してぶら下がれる。
    /// </summary>
    public class InchwormController : MonoBehaviour
    {
        public enum Mode { Idle, Pull, Reach, Hang, Fall }

        [Header("References")]
        public InchwormBody body;
        public LineRenderer silk;
        public Transform cameraTransform;

        [Header("Gait")]
        [Range(0.6f, 1f)] public float extendRatio = 1f;   // 伸びるときは体をまっすぐ伸ばし切る
        [Range(0.2f, 0.6f)] public float loopRatio = 0.3f;
        [Range(0.5f, 1f)] public float restRatio = 0.8f;
        public float pullTime = 0.32f;
        public float reachTime = 0.36f;
        [Range(0.3f, 1f)] public float sprintTimeScale = 0.55f;
        public float maxTurnDegrees = 70f;

        [Header("Silk")]
        public float silkDescendSpeed = 1.8f;
        public float silkFastSpeed = 4.5f;
        public float silkClimbSpeed = 1.4f;
        public float silkReelSpeed = 4.2f;     // ねらって出した糸をたぐる速さ
        public float silkRange = 12f;          // 糸をねらって出せる距離
        public float silkMaxLength = 40f;      // 糸はこれ以上のびない

        [Header("Fall")]
        public float fallGravity = 9f;         // 小さな体なので、ゆっくり落ちる
        public float fallTerminal = 9f;

        public static InchwormController Instance { get; private set; }
        public Mode State { get; private set; } = Mode.Idle;
        public bool InputEnabled { get; set; } = true;
        public bool IsBlocked { get; private set; }
        /// <summary>進めなかった理由（"cliff" "water" "overhang" など。HUD のヒント用）。</summary>
        public string BlockReason { get; private set; } = "";
        public bool CanDropSilk { get; private set; }
        public bool IsStanding => _rear > 0.6f && _standRequested;
        public bool IsMoving => State == Mode.Pull || State == Mode.Reach;
        public BodyCurve Curve => _curve;
        public Vector3 HeadPosition => _curve.Head;
        public Vector3 CenterPosition => _curve.Middle;
        public Vector3 SurfaceUp { get; private set; } = Vector3.up;
        public Vector3 Heading { get; private set; } = Vector3.forward;
        /// <summary>カメラが追う向き（Heading をなめらかにしたもの）。</summary>
        public Vector3 CameraHeading { get; private set; } = Vector3.forward;
        /// <summary>小枝のまねをしているか（じっと固まる）。</summary>
        public bool IsTwigPose => _twig > 0.5f;
        /// <summary>いま、のぞきこみ・水をたしかめる・頭を引っこめる動きをしているか。</summary>
        public bool IsPeeking => _peekT < _peekDur;
        public string PeekKind => _peekKind;
        /// <summary>手をはなす・がけから落ちる前の「ため」の最中か。</summary>
        public bool IsAboutToFall => _pending != PendingFall.None;
        /// <summary>続けて歩いた歩数（歩きはじめはゆっくり）。</summary>
        public int StepStreak => _stepStreak;
        public float LastStepDuration => _dur;
        public float SprintBlend => _sprintBlend;
        public float SilkSpeed => _silkSpeed;
        public Vector3 SilkAnchor => _silkAnchor;
        public float RearAmount => _rear;
        public Vector3 TailPoint => _tail.point;
        public Vector3 HeadPoint => _head.point;
        /// <summary>葉っぱの舟など、動く足場に乗っているか。</summary>
        public bool OnMovingPlatform => _tail.platform != null || _head.platform != null;
        /// <summary>乗っている足場（いきもの・舟）。なければ null。</summary>
        public Transform PlatformUnder => State == Mode.Hang || State == Mode.Fall ? null : (_tail.platform != null ? _tail.platform : _head.platform);

        public event Action<Vector3, bool> Stepped;   // 位置, 頭か
        public event Action SilkStarted;
        public event Action Landed;
        public event Action Fell;                  // はなれて落ちはじめた
        public event Action<float> HitGround;      // 落ちて着地した（そのときの速さ）
        public event Action Splashed;              // 水に落ちた

        // ---- ねらって糸を出す ----
        public bool IsAiming { get; private set; }
        public bool AimValid { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public string AimProblem { get; private set; } = "";
        public bool IsReeling => State == Mode.Hang && _reeling;
        public bool IsFalling => State == Mode.Fall;
        /// <summary>壁や裏側にいる（はなれて落ちられる）。</summary>
        public bool OnSteepSurface => State != Mode.Hang && State != Mode.Fall && ((_tail.normal + _head.normal).normalized.y < 0.55f);

        const int Samples = 41;
        float L => ShakuConst.BodyLength;
        float Rad => ShakuConst.BodyRadius;

        SurfacePoint _tail, _head;
        SurfacePoint _from, _to;
        float _t, _dur;
        bool _settling;
        float _reachLift;
        BodyCurve _curve, _target, _snap, _pose;
        float _blendT = 1f, _blendDur = 0.3f;
        float _rear, _idleTime, _lookTimer, _lookUntil;
        bool _standRequested;
        float _swayPhase;
        // 背伸び中に方向キーで上半身の向きを変える
        const float StandLookMax = 1.75f;   // 約 100 度
        float _standLook, _standLookTarget, _lastLookInput = -99f;
        bool _wasStanding;

        // 糸
        Vector3 _silkAnchor;
        SurfacePoint _anchorSurface;
        Vector3 _hangPos, _hangVel, _hangFacing;
        float _silkLen;
        float _silkFade;

        // 糸をねらう・たぐる
        bool _reeling;
        float _shotT = 1f;
        RaycastHit _aimHit;

        // 落下
        Vector3 _fallPos, _fallVel, _fallAxis;
        float _fallSpin, _fallTime, _sinkTimer;
        SurfacePoint _fallFrom;
        float _landBounce, _dazeUntil, _fallStuck;
        float _moveMag = 1f;            // スティックの倒し具合（少し倒すとゆっくり歩く）
        float _cheerUntil;              // うれしいときに体を起こす
        Vector3 _lookAtPoint;
        float _lookAtUntil;
        float _buriedTime;

        Vector3 _lastSafeTail, _lastSafeHead, _lastSafeNormal;
        float _safeTimer;

        // ---- 歩き方のリズム ----
        int _stepStreak;                 // 続けて歩いた歩数
        float _sprintBlend;              // はやく（2 歩ほどかけて切りかえる）
        float _tapAt = -99f;             // 歩いている途中に押された入力（先行入力）
        Vector3 _tapDir;
        bool _wasPushing;
        Vector3 _desiredNow;
        Vector2 _stick;                  // 面をまたぐときに向きを保つため
        Vector3 _stickNormal = Vector3.up;
        float _pullLift;                 // 尾を持ち上げる高さ
        float _turnSign;                 // 曲がる向き（体をかたむける）
        float _arcBlend;                 // 頭を弧をえがいて動かす割合
        bool _convexAhead;               // 出っ張った角へ向かっている
        bool _firstStep;
        // ---- 体の表情 ----
        float _breathPhase;
        float _relax;                    // 長く休むと力をぬく
        float _twig;                     // 小枝のまね
        float _freezeUntil;              // 天敵が近くてかたまる
        float _flinch;                   // びくっ
        float _gripPulse;                // 尾をつけた瞬間にきゅっと
        float _settlePulse;              // 止まったときの、ふうっ
        float _groomUntil, _groomStart, _groomSide = 1f, _nextGroom = 12f;
        float _lookStart;
        float _rearVel;
        float _wakeUntil, _wakeStart;
        float _landSquash;
        Vector3 _platformPrev;
        Transform _platformRef;
        Vector3 _platformVel, _platformLean;
        float _reactTimer, _curiousNext, _flinchNext;
        // ---- がけ・水・障害物 ----
        float _peekT = 9f, _peekDur = 1f;
        string _peekKind = "";
        float _blockedTime, _unstickNext;
        int _unstickSide = 1;
        float _supportTimer;
        int _unsupported;
        enum PendingFall { None, WalkOff, LetGo }
        PendingFall _pending;
        float _pendingUntil, _pendingStart;
        Vector3 _pendingVel, _pendingEdge, _pendingDir;
        SurfacePoint _pendingFrom;
        // ---- 糸・落下 ----
        float _silkSpeed;
        float _hangStart;
        Vector3 _hangLean = Vector3.down;
        float _fallSpinRate = 5f;
        float _uncurl;

        void Awake()
        {
            Instance = this;
            _curve = new BodyCurve(Samples);
            _target = new BodyCurve(Samples);
            _snap = new BodyCurve(Samples);
            _pose = new BodyCurve(Samples);
            if (silk != null) silk.enabled = false;
        }

        // ------------------------------------------------------------------
        // 出現
        // ------------------------------------------------------------------
        void ResetAirState()
        {
            _pending = PendingFall.None;
            _peekT = 9f;
            _blockedTime = 0f;
            _unsupported = 0;
            _silkSpeed = 0f;
            _landSquash = 0f;
            _reeling = false;
            _shotT = 1f;
            _fallTime = 0f;
            _sinkTimer = 0f;
            _landBounce = 0f;
            _dazeUntil = 0f;
            IsAiming = false;
            AimValid = false;
        }

        public void Spawn(Vector3 point, Vector3 forward)
        {
            ResetAirState();
            Vector3 n = Vector3.up;
            bool snapped = SurfaceProbe.Snap(point + Vector3.up * 0.5f, Vector3.up, 3f, out var sp);
            if (snapped) { point = sp.point; n = sp.normal; }
            _tail = snapped ? sp : new SurfacePoint(point, n);   // 舟の上なら舟といっしょに動く
            if (SurfaceProbe.Walk(_tail, forward, extendRatio * L, out var h, out _)) _head = h;
            else _head = new SurfacePoint(point + forward.normalized * extendRatio * L, n);
            State = Mode.Idle;
            IsBlocked = false;
            _rear = 0f;
            _blendT = 1f;
            if (silk != null) silk.enabled = false;
            BuildGroundTarget(0f);
            _curve.CopyFrom(_target);
            body.Apply(_curve);
            MarkSafe();
            WakeUp();
        }

        /// <summary>出てきたとき・もどったとき：ひと伸びして目をさます。</summary>
        void WakeUp()
        {
            _wakeStart = Time.time;
            _wakeUntil = Time.time + 0.9f;
            _idleTime = 0f;
            _twig = 0f;
            _relax = 0f;
            _stepStreak = 0;
        }

        public void Spawn(Vector3 tailPoint, Vector3 tailNormal, Vector3 headPoint, Vector3 headNormal)
        {
            ResetAirState();
            _tail = AttachToPlatform(tailPoint, tailNormal);
            _head = AttachToPlatform(headPoint, headNormal);
            State = Mode.Idle;
            _rear = 0f;
            _blendT = 1f;
            if (silk != null) silk.enabled = false;
            BuildGroundTarget(0f);
            _curve.CopyFrom(_target);
            body.Apply(_curve);
            MarkSafe();
            WakeUp();
        }

        /// <summary>その点の下が動く足場なら、足場に乗った点として返す（位置はそのまま）。</summary>
        static SurfacePoint AttachToPlatform(Vector3 p, Vector3 n)
        {
            if (SurfaceProbe.Snap(p + n * 0.1f, n, 0.3f, out var sp) && sp.platform != null) return sp;
            return new SurfacePoint(p, n);
        }

        public void GetSaveState(out Vector3 tail, out Vector3 tailN, out Vector3 head, out Vector3 headN)
        {
            if (State == Mode.Hang)
            {
                tail = _lastSafeTail; tailN = _lastSafeNormal; head = _lastSafeHead; headN = _lastSafeNormal;
                return;
            }
            tail = _tail.point; tailN = _tail.normal; head = _head.point; headN = _head.normal;
        }

        void MarkSafe()
        {
            _lastSafeTail = _tail.point;
            _lastSafeHead = _head.point;
            _lastSafeNormal = _tail.normal;
            // 動けなくなったときのために、少しずつはなれた安全な場所を覚えておく
            if (_safeHistory.Count == 0 || Vector3.Distance(_safeHistory[_safeHistory.Count - 1].tail, _tail.point) > 1.5f)
            {
                _safeHistory.Add((_tail.point, _head.point, _tail.normal));
                if (_safeHistory.Count > 12) _safeHistory.RemoveAt(0);
            }
        }

        readonly System.Collections.Generic.List<(Vector3 tail, Vector3 head, Vector3 normal)> _safeHistory = new System.Collections.Generic.List<(Vector3, Vector3, Vector3)>();

        // ------------------------------------------------------------------
        // 入力
        // ------------------------------------------------------------------
        Vector3 DesiredDirection(Vector3 n, out float magnitude)
        {
            Vector2 mv = InputEnabled ? GameInput.Move : Vector2.zero;
            magnitude = Mathf.Clamp01(mv.magnitude);
            if (magnitude < 0.01f || cameraTransform == null) return Vector3.zero;
            if (n.y < -0.2f && State != Mode.Hang && State != Mode.Fall)
            {
                // 裏側（天井）では、カメラではなく体の向きを基準にする：前に倒せば、そのまま前へ進みつづける
                Vector3 hf = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, n, Heading).normalized;
                Vector3 hr = Vector3.Cross(n, hf).normalized;
                Vector3 bd = hf * mv.y - hr * mv.x;
                return bd.sqrMagnitude > 1e-6f ? bd.normalized : Vector3.zero;
            }
            Vector3 cf = cameraTransform.forward;
            Vector3 cu = cameraTransform.up;
            Vector3 cr = cameraTransform.right;
            // 壁に向かって見ているときは「前 = 上り」
            float facing = Mathf.Abs(Vector3.Dot(cf, n));
            Vector3 f1 = Vector3.ProjectOnPlane(cf, n);
            Vector3 f2 = Vector3.ProjectOnPlane(cu, n);
            Vector3 fwd = f1 * (1f - facing) + f2 * facing;
            if (fwd.sqrMagnitude < 1e-6f) fwd = f2.sqrMagnitude > 1e-6f ? f2 : ShakuMath.AnyPerpendicular(n);
            fwd.Normalize();
            Vector3 right = ShakuMath.ProjectOnPlaneSafe(cr, n, Vector3.Cross(n, fwd));
            Vector3 d = fwd * mv.y + right * mv.x;
            if (d.sqrMagnitude < 1e-6f) return Vector3.zero;
            d.Normalize();
            // 面をまたいだとき（床→壁→上の面）、スティックをそのまま倒していれば、まっすぐ進みつづける。
            // カメラから見た向きで決めると、ふちを越えたとたんに逆向きになってしまうことがあるため
            bool steady = Vector2.Angle(mv, _stick) < 12f && Mathf.Abs(mv.magnitude - _stick.magnitude) < 0.25f;
            if (!steady)
            {
                _stick = mv;
                _stickNormal = n;
            }
            else if (State != Mode.Hang && State != Mode.Fall && Vector3.Angle(n, _stickNormal) > 30f)
            {
                Vector3 straight = ShakuMath.ProjectOnPlaneSafe(Heading, n, d);
                if (straight.sqrMagnitude > 1e-4f && Vector3.Angle(straight, d) > 60f) d = straight.normalized;
            }
            return d;
        }

        // ------------------------------------------------------------------
        // 更新
        // ------------------------------------------------------------------
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _swayPhase += dt;
            // 動く足場（葉っぱの舟）に乗っていれば一緒に動く
            _tail = _tail.Updated();
            _head = _head.Updated();
            _from = _from.Updated();
            _to = _to.Updated();

            Vector3 avgN = (_tail.normal + _head.normal).normalized;
            if (avgN.sqrMagnitude < 0.5f) avgN = _tail.normal;
            bool airborne = State == Mode.Hang || State == Mode.Fall;
            Vector3 desired = DesiredDirection(airborne ? Vector3.up : _head.normal, out float mag);
            _moveMag = mag;
            _desiredNow = desired;
            // 動きだすのは 0.2 から、動いている間は 0.12 まで（スティックの小さなぶれで止まったり動いたりしない）
            float threshold = IsMoving ? 0.12f : 0.2f;
            bool pushing = mag > threshold;
            bool wantsMove = pushing && Time.time >= _dazeUntil && _pending == PendingFall.None;
            // 歩いている途中にちょんと押された入力は、次の一歩に使う（先行入力）
            if (pushing && !_wasPushing && IsMoving)
            {
                _tapAt = Time.time;
                _tapDir = desired;
            }
            _wasPushing = pushing;
            bool sprint = InputEnabled && GameInput.Sprint;
            // はやく：2 歩ほどかけて、なめらかに速くなる・もどる
            _sprintBlend = Mathf.MoveTowards(_sprintBlend, sprint && wantsMove ? 1f : 0f, dt * (sprint ? 1.25f : 2f));
            _standRequested = InputEnabled && GameInput.StandHeld;
            if (wantsMove || _standRequested) _wakeUntil = 0f;
            UpdatePlatformMotion(dt);
            UpdatePending();

            switch (State)
            {
                case Mode.Idle: UpdateIdle(dt, desired, wantsMove, sprint); break;
                case Mode.Pull: UpdatePull(dt, desired, wantsMove, sprint); break;
                case Mode.Reach: UpdateReach(dt, desired, wantsMove, sprint); break;
                case Mode.Hang: UpdateHang(dt, desired, mag, sprint); break;
                case Mode.Fall: UpdateFall(dt, desired, mag); break;
            }

            if (State != Mode.Hang && State != Mode.Fall)
            {
                UpdateCanDrop();
                if (InputEnabled && GameInput.SilkPressed && !_standRequested && _pending == PendingFall.None)
                {
                    if (CanDropSilk) StartHang();
                    else if (OnSteepSurface) LetGo();   // 壁や裏側では、はなれて落ちる
                }
                UpdateBlocked(dt, desired, wantsMove);
                UpdateSupport(dt);
                UpdateReactions(dt, wantsMove);
            }
            else CanDropSilk = false;
            _peekT += dt;
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 3f);
            _gripPulse = Mathf.MoveTowards(_gripPulse, 0f, dt * 5f);
            _settlePulse = Mathf.MoveTowards(_settlePulse, 0f, dt * 1.4f);
            _landSquash = Mathf.MoveTowards(_landSquash, 0f, dt * 6f);
            UpdateAim();
            _landBounce = Mathf.MoveTowards(_landBounce, 0f, dt * 1.8f);

            if (State == Mode.Hang) BuildHangTarget();
            else if (State == Mode.Fall) BuildFallTarget();
            else BuildGroundTarget(dt);

            if (_blendT < 1f)
            {
                _blendT = Mathf.Min(1f, _blendT + dt / _blendDur);
                _curve.Blend(_snap, _target, ShakuMath.Smooth01(_blendT));
            }
            else _curve.CopyFrom(_target);

            body.Apply(_curve);
            UpdateSilkLine(dt);

            Vector3 c = _curve.Middle;
            Shader.SetGlobalVector("_ShakuPlayerPos", new Vector4(c.x, c.y, c.z, 0.9f));
            SurfaceUp = airborne ? Vector3.up : Vector3.Slerp(SurfaceUp, avgN, ShakuMath.DampFactor(6f, dt));
            Vector3 hd = _curve.Head - _curve.Tail;
            if (hd.sqrMagnitude > 1e-4f) Heading = hd.normalized;
            // カメラが追う向きは、なめらかに（一歩ごとにカメラががくっと動かないように）
            CameraHeading = Vector3.Slerp(CameraHeading, Heading, ShakuMath.DampFactor(4f, dt)).normalized;

            SafetyCheck(dt);
        }

        void BeginTransition(float duration)
        {
            _snap.CopyFrom(_curve);
            _blendT = 0f;
            _blendDur = duration;
        }

        // ---- 待機 ----
        void UpdateIdle(float dt, Vector3 desired, bool wantsMove, bool sprint)
        {
            _idleTime += dt;
            if (_idleTime > 0.3f) _stepStreak = 0;     // しばらく止まると、また歩きはじめから
            float rearTarget = 0f;
            bool standing = _standRequested;
            bool steep = _tail.normal.y < 0.5f;
            // 長く休むと力をぬき（20 秒）、もっと休むと小枝のまねをする（40 秒）。天敵が近くで動いたときもかたまる
            bool calm = !wantsMove && !standing && !IsAiming && _pending == PendingFall.None;
            _relax = Mathf.MoveTowards(_relax, calm && _idleTime > 20f ? 1f : 0f, dt * (calm ? 0.25f : 3f));
            bool twig = calm && !steep && (_idleTime > 40f || Time.time < _freezeUntil);
            _twig = Mathf.MoveTowards(_twig, twig ? 1f : 0f, dt * (twig ? 2.5f : 5f));
            if (standing && wantsMove)
            {
                // 背伸び中は進まずに、上半身をその方向へ向ける（本物のしゃくとりむしもよくやる動き）
                _idleTime = 0f;
                rearTarget = 1f;
                Vector3 fwd = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _tail.normal, Heading);
                Vector3 want = ShakuMath.ProjectOnPlaneSafe(desired, _tail.normal, fwd);
                _standLookTarget = Mathf.Clamp(Vector3.SignedAngle(fwd, want, _tail.normal) * Mathf.Deg2Rad, -StandLookMax, StandLookMax);
                _lastLookInput = Time.time;
            }
            else if (!standing && _wasStanding && _rear > 0.5f && Mathf.Abs(_standLook) > 0.35f)
            {
                // 背伸びをやめたら、見ていた方へ頭を下ろす（その場で向きを変える）
                Vector3 fwd = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _tail.normal, Heading);
                Vector3 look = Quaternion.AngleAxis(_standLook * Mathf.Rad2Deg, _tail.normal) * fwd;
                _standLook = 0f;
                _standLookTarget = 0f;
                _wasStanding = false;
                StartReach(look, true, false);
                return;
            }
            else if (wantsMove)
            {
                _idleTime = 0f;
                _lookUntil = 0f;
                _groomUntil = 0f;
                if (_rear < 0.35f)
                {
                    float D = Vector3.Distance(_tail.point, _head.point);
                    if (D < L * 0.55f) StartReach(desired, false, sprint);
                    else StartPull(sprint);
                }
            }
            else
            {
                if (_standRequested) rearTarget = 1f;
                else
                {
                    // ときどき顔を上げて、左右をきょろきょろ
                    _lookTimer += dt;
                    if (_idleTime > 5f && _lookTimer > 7.5f)
                    {
                        _lookTimer = 0f;
                        _lookStart = Time.time;
                        _lookUntil = Time.time + 2.8f;
                    }
                    if (Time.time < _lookUntil)
                    {
                        rearTarget = steep ? 0.25f : 0.5f;   // 壁の上では小さく
                        _standLookTarget = 0.65f * Mathf.Sin((Time.time - _lookStart) * 2.2f);
                        _lastLookInput = Time.time;
                    }
                    // ときどき体の手入れ（頭を体の横へまわす）
                    if (_idleTime > 8f && Time.time > _nextGroom && Time.time >= _lookUntil && !steep && _twig < 0.1f)
                    {
                        _groomStart = Time.time;
                        _groomUntil = Time.time + 1.6f;
                        _groomSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                        _nextGroom = Time.time + UnityEngine.Random.Range(12f, 20f);
                    }
                    if (Time.time < _groomUntil) rearTarget = 0.4f;
                }
            }
            if (Time.time < _cheerUntil) rearTarget = Mathf.Max(rearTarget, 0.55f);
            if (Time.time < _lookAtUntil && !wantsMove)
            {
                // 見つけたいきもののほうへ顔を向ける（動けば目で追う）
                rearTarget = Mathf.Max(rearTarget, steep ? 0.25f : 0.45f);
                Vector3 fwd = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _tail.normal, Heading);
                Vector3 to = ShakuMath.ProjectOnPlaneSafe(_lookAtPoint - _tail.point, _tail.normal, fwd);
                _standLookTarget = Mathf.Clamp(Vector3.SignedAngle(fwd, to, _tail.normal) * Mathf.Deg2Rad, -StandLookMax, StandLookMax);
                _lastLookInput = Time.time;
            }
            // 糸をねらっているときは、少し体を起こして、ねらう方を向く
            if (IsAiming && !wantsMove)
            {
                rearTarget = Mathf.Max(rearTarget, 0.35f);
                Vector3 fwd = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _tail.normal, Heading);
                Vector3 to = ShakuMath.ProjectOnPlaneSafe(AimPoint - _tail.point, _tail.normal, fwd);
                _standLookTarget = Mathf.Clamp(Vector3.SignedAngle(fwd, to, _tail.normal) * Mathf.Deg2Rad, -StandLookMax, StandLookMax);
                _lastLookInput = Time.time;
            }
            // 出てきたときは、ひと伸びして目をさます
            if (Time.time < _wakeUntil) rearTarget = Mathf.Max(rearTarget, 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((Time.time - _wakeStart) / 0.9f)));
            if (_twig > 0.01f) rearTarget = Mathf.Lerp(rearTarget, 0.8f, _twig);
            // 壁の上では大きく体を起こさない
            if (steep) rearTarget = Mathf.Min(rearTarget, 0.45f);
            // 体の上げ下げは、ばねのようになめらかに
            _rear = Mathf.SmoothDamp(_rear, rearTarget, ref _rearVel, rearTarget > _rear ? 0.22f : 0.16f, Mathf.Infinity, dt);
            if (!standing && _rear < 0.3f && Time.time >= _lookAtUntil && Time.time >= _lookUntil && !IsAiming) _standLookTarget = 0f;
            _wasStanding = standing;
        }

        /// <summary>スティックを少しだけ倒したときは、ゆっくり歩く（最大 1.7 倍の時間）。</summary>
        float SlowFactor => Mathf.Lerp(1.45f, 1f, Mathf.InverseLerp(0.25f, 0.85f, _moveMag));

        /// <summary>
        /// 一歩の時間の倍率。歩きはじめ・はやく・坂（上りはゆっくり、下りは少しはやく、急な下りは慎重に）・
        /// 壁・天井・ほんの少しのゆらぎ。
        /// </summary>
        float StepTimeScale(Vector3 dir, Vector3 n)
        {
            float k = StartupTimeScale(_stepStreak);
            k *= Mathf.Lerp(1f, sprintTimeScale, _sprintBlend);
            k *= SurfaceTimeScale(dir, n);
            k *= 1f + UnityEngine.Random.Range(-0.05f, 0.05f);   // 生きものらしいゆらぎ
            return k;
        }

        /// <summary>歩きはじめの一歩はゆっくり、3 歩目で本来の速さ。</summary>
        public static float StartupTimeScale(int streak) => streak <= 0 ? 1.18f : streak == 1 ? 1.08f : 1f;

        /// <summary>
        /// 面と進む向きによる一歩の時間の倍率：上り坂はゆっくり、ゆるい下りは少しはやく、急な下りは慎重に。
        /// 壁はゆっくり（頭から下りるときはもっと）、天井はさらにゆっくり。
        /// </summary>
        public static float SurfaceTimeScale(Vector3 dir, Vector3 n)
        {
            float slope = dir.sqrMagnitude > 1e-6f ? Vector3.Dot(dir.normalized, Vector3.up) : 0f;
            if (n.y < -0.35f) return 1.3f;                       // 天井
            if (n.y < 0.5f) return slope < -0.7f ? 1.25f : 1.15f; // 壁
            if (slope > 0f) return 1f + 0.45f * slope;           // 上り坂
            if (slope > -0.5f) return 1f + 0.2f * slope;         // ゆるい下り坂
            return 1.1f;                                         // 急な下り
        }

        /// <summary>テスト用：じっとしている時間を進める（長く休んだときの動きを確かめる）。</summary>
        public void DebugSetIdleTime(float seconds) => _idleTime = seconds;
        public float RelaxAmount => _relax;

        /// <summary>a から b へまっすぐ動くとき、あいだにある出っぱりの高さ（n の向き）。高すぎるもの（ひさし）は数えない。</summary>
        static float PathClearance(Vector3 a, Vector3 b, Vector3 n)
        {
            float need = 0f;
            for (int i = 1; i <= 3; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 4f);
                if (SurfaceProbe.Raycast(p + n * 0.32f, -n, 0.34f, out var hit))
                {
                    float c = Vector3.Dot(hit.point - p, n);
                    if (c > need && c < 0.3f) need = c;
                }
            }
            return need;
        }

        static bool IsEdgeFail(string why) => why == "nothing-ahead" || why == "cliff" || why == "no-drop" || why == "edge-invalid";
        static bool IsObstacleFail(string why) => why == "slide-blocked" || why == "slide-turn" || why == "overhang" || why == "wall-invalid";

        /// <summary>歩いている途中に押された入力があれば、それを使う（一度だけ）。</summary>
        bool TakeTap(out Vector3 dir)
        {
            dir = _tapDir;
            bool ok = Time.time - _tapAt < 0.6f && _tapDir.sqrMagnitude > 0.01f;
            _tapAt = -99f;
            return ok;
        }

        /// <summary>うれしいとき（しずくを取った・きせかえを変えた）、体をきゅっと起こす。</summary>
        public void Cheer()
        {
            if (State == Mode.Idle || State == Mode.Pull || State == Mode.Reach) _cheerUntil = Time.time + 0.55f;
        }

        /// <summary>いきものを見つけたとき、少しのあいだそちらを見る。</summary>
        public void LookAt(Vector3 point, float seconds)
        {
            _lookAtPoint = point;
            _lookAtUntil = Time.time + seconds;
        }

        /// <summary>背伸び中の上半身の向き（ラジアン、体の向きからの左右の角度）。</summary>
        public float StandLook => _standLook;

        // ---- 引き寄せ ----
        static readonly float[] PullTry = { 1f, 0.6f, 0.35f };

        void StartPull(bool sprint)
        {
            Vector3 hf = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _head.normal, Heading);
            // はやいときは尾を頭のすぐそばまで引き寄せて、Ω を高く
            float loop = Mathf.Lerp(loopRatio, 0.24f, _sprintBlend) * L;
            SurfacePoint tgt = default;
            bool found = false;
            // 足場の小さい所（とびいしのふちなど）でも止まらないよう、とどかなければ短くして試す
            foreach (float k in PullTry)
            {
                if (SurfaceProbe.Walk(_head, -hf, loop * k, out tgt, out _))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Vector3 mid = Vector3.Lerp(_tail.point, _head.point, 0.6f);
                if (!SurfaceProbe.Snap(mid, (_tail.normal + _head.normal).normalized, 0.6f, out tgt))
                {
                    // あいだに何もない（すき間をまたいだ直後など）：尾を頭のすぐ後ろまで運ぶ
                    tgt = _head;
                    tgt.point = _head.point - hf.normalized * 0.03f;
                }
            }
            if (Vector3.Distance(tgt.point, _tail.point) < 0.04f)
            {
                StartReach(Heading, false, sprint);
                return;
            }
            _from = _tail;
            _to = tgt;
            _t = 0f;
            // はやいときは、引き寄せをいっそうきびきびと
            _dur = pullTime * StepTimeScale(hf, _head.normal) * Mathf.Lerp(1f, 0.88f, _sprintBlend) * SlowFactor;
            // 尾の弧：歩幅と速さに合わせた高さ。通り道に出っぱりがあれば、それもこえる
            Vector3 n = (_from.normal + _to.normal).normalized;
            float span = Vector3.Distance(_from.point, _to.point);
            float baseLift = 0.05f * L * Mathf.Clamp(span / (0.7f * L), 0.5f, 1.4f) * (1f + 0.5f * _sprintBlend);
            _pullLift = Mathf.Max(baseLift, PathClearance(_from.point, _to.point, n) + 0.03f);
            // 次に曲がる方へ、体を少しかたむける
            Vector3 want = _desiredNow.sqrMagnitude > 0.01f ? ShakuMath.ProjectOnPlaneSafe(_desiredNow, _head.normal, hf) : hf;
            _turnSign = Mathf.Clamp(Vector3.SignedAngle(hf, want, _head.normal) / 70f, -1f, 1f);
            State = Mode.Pull;
        }

        void UpdatePull(float dt, Vector3 desired, bool wantsMove, bool sprint)
        {
            _rear = Mathf.MoveTowards(_rear, 0f, dt * 4f);
            _t += dt / _dur;
            // 引き寄せる前に、一瞬だけ後ろへ踏んばる
            float e = ShakuMath.Smoother01(_t) - 0.04f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t / 0.22f));
            Vector3 liftN = (_from.normal + _to.normal).normalized;
            Vector3 p = Vector3.LerpUnclamped(_from.point, _to.point, e) + liftN * Mathf.Sin(Mathf.PI * Mathf.Clamp01(e)) * _pullLift;
            _tail = new SurfacePoint(p, Vector3.Slerp(_from.normal, _to.normal, Mathf.Clamp01(e)).normalized);
            if (_t >= 1f)
            {
                _tail = _to;
                _gripPulse = 1f;   // 腹脚でつかまった瞬間に、きゅっと
                Stepped?.Invoke(_tail.point, false);
                if (wantsMove)
                {
                    _tapAt = -99f;
                    StartReach(desired, false, sprint);
                }
                else if (TakeTap(out var tap)) StartReach(tap, false, sprint);
                else StartReach(Heading, true, sprint);
            }
        }

        // ---- 伸び ----
        static readonly float[] DistTry = { 1f, 0.7f, 0.45f };
        static readonly float[] WideTry = { 45f, -45f, 65f, -65f };

        bool StartReach(Vector3 desired, bool settle, bool sprint)
        {
            Vector3 n = _tail.normal;
            Vector3 heading = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, n, ShakuMath.ProjectOnPlaneSafe(Heading, n, ShakuMath.AnyPerpendicular(n)));
            if (Vector3.Distance(_head.point, _tail.point) < 0.05f)
                heading = ShakuMath.ProjectOnPlaneSafe(Heading, n, ShakuMath.AnyPerpendicular(n));
            Vector3 want = desired.sqrMagnitude > 0.01f ? ShakuMath.ProjectOnPlaneSafe(desired, n, heading) : heading;
            float ang = Vector3.SignedAngle(heading, want, n);
            // 小さな向きのぶれでは曲がらない（まっすぐ歩く）
            if (!settle && Mathf.Abs(ang) < 5f) ang = 0f;
            float turn = Mathf.Clamp(ang, -maxTurnDegrees, maxTurnDegrees);
            float dist = (settle ? restRatio : extendRatio) * L;
            // 曲がるほど、歩幅を少しずつ小さく
            dist *= Mathf.Lerp(1f, 0.8f, Mathf.Abs(turn) / maxTurnDegrees);
            if (!settle)
            {
                // 真後ろへの方向転換は、小さな歩幅で大きく回る
                if (Mathf.Abs(ang) > 120f)
                {
                    turn = Mathf.Sign(ang) * 85f;
                    dist *= 0.75f;
                }
                // スティックを少しだけ倒すと、歩幅も小さく
                dist *= Mathf.Lerp(0.8f, 1f, Mathf.InverseLerp(0.25f, 0.85f, _moveMag));
            }

            float[] turnTry = { turn, turn * 0.5f, turn + Mathf.Sign(turn + 0.01f) * 25f, 0f };
            bool firstTry = true;
            string firstFail = "";
            foreach (float tr in turnTry)
            {
                Vector3 go = Quaternion.AngleAxis(tr, n) * heading;
                foreach (float k in DistTry)
                {
                    bool ok = SurfaceProbe.Walk(_tail, go, dist * k, out var tgt, out _, out bool offEdge);
                    if (!ok && firstTry && !settle && desired.sqrMagnitude > 0.01f)
                    {
                        string why = SurfaceProbe.LastFail;
                        Vector3 stop = SurfaceProbe.LastStopPoint, stopDir = SurfaceProbe.LastStopDir;
                        bool gapAhead = false;
                        // 小さなすき間（石と石のあいだなど）は、体を伸ばしてまたぐ。
                        // 向こう側が近いのにまだとどかないときは、落ちずに、ふちまで近づいてからまたぐ
                        if (IsEdgeFail(why) && TryBridge(go, stop, out var across, out gapAhead))
                        {
                            BeginReach(across, false, go, tr);
                            return true;
                        }
                        // 急ながけのふちで、そのまま前へ進んだ：体をのり出して落ちる。
                        // ただし、頭がまだふちから遠いときは、短い一歩でふちまで近づいてから
                        bool headAtEdge = Vector3.Distance(stop, _head.point) < 0.2f * L;
                        if (offEdge && !gapAhead && headAtEdge && IsRealDrop(why, stop, stopDir))
                        {
                            WalkOff(stop, go);
                            return false;
                        }
                    }
                    if (firstTry && !ok)
                    {
                        firstFail = SurfaceProbe.LastFail;
                        // 行き先が水の中・遊べる範囲の外なら、その理由にする
                        Vector3 ahead = _tail.point + go * dist + n * 0.05f;
                        var area = Areas.Current;
                        if (area.IsUnderwater(ahead) || area.IsUnderwater(SurfaceProbe.LastStopPoint + go * 0.2f)) firstFail = "water";
                        else if (!area.InPlayArea(ahead)) firstFail = "outside";
                    }
                    firstTry = false;
                    if (ok)
                    {
                        if (Vector3.Distance(tgt.point, _tail.point) < 0.15f) continue;
                        BeginReach(tgt, settle, go, tr);
                        return true;
                    }
                }
                if (settle) break;
            }
            // 物にぶつかったときは、もっと広く回り道をさがす（スティックを倒している左右を先に）
            if (!settle && IsObstacleFail(firstFail))
            {
                float side = Mathf.Abs(ang) > 1f ? Mathf.Sign(ang) : _unstickSide;
                foreach (float w in WideTry)
                {
                    float tr = w * side;
                    Vector3 go = Quaternion.AngleAxis(tr, n) * heading;
                    if (SurfaceProbe.Walk(_tail, go, dist * 0.6f, out var tgt, out _) && Vector3.Distance(tgt.point, _tail.point) > 0.15f)
                    {
                        BeginReach(tgt, false, go, tr);
                        return true;
                    }
                }
            }
            bool wasBlocked = IsBlocked;
            IsBlocked = !settle;
            if (!settle)
            {
                BlockReason = firstFail;
                if (!wasBlocked) TriggerPeek(firstFail);
            }
            State = Mode.Idle;
            return false;
        }

        /// <summary>伸びをはじめる：一歩の時間・頭を上げる高さ・弧の動き・体のかたむきを決める。</summary>
        void BeginReach(SurfacePoint tgt, bool settle, Vector3 go, float turnDeg)
        {
            _from = _head;
            _to = tgt;
            _t = 0f;
            Vector3 n = _tail.normal;
            float k = StepTimeScale(go, n);
            if (settle) k *= Mathf.Lerp(1.15f, 0.95f, _sprintBlend);   // はやく のあとの止まる一歩は短く
            else k *= SlowFactor;
            // 段を上るときは、時間をかけて頭を高く上げる
            float rise = Vector3.Dot(tgt.point - _tail.point, n);
            if (rise > 0.05f) k *= 1f + Mathf.Min(rise, 0.6f) * 0.5f;
            _dur = reachTime * k;
            _settling = settle;
            float lift = settle ? 0.12f : 0.24f;
            if (rise > 0.05f) lift += Mathf.Min(rise, 0.6f) * 0.6f / L;
            else if (rise < -0.05f) lift *= 0.7f;                  // 下りは低く
            if (!settle && _stepStreak == 0) lift *= 1.3f;         // 歩きだしの一歩は、頭を高く上げて行き先を見る
            // 頭の通り道に出っぱりがあれば、それをこえる高さに
            Vector3 liftN = (_from.normal + tgt.normal).normalized;
            float clear = PathClearance(_from.point, tgt.point, liftN);
            _reachLift = Mathf.Max(lift, (clear + 0.06f) / L);
            // 曲がるときは、頭が尾のまわりを弧をえがいて動く（平らな所どうしのとき）
            float same = Mathf.Min(Vector3.Dot(_from.normal, tgt.normal), Vector3.Dot(n, tgt.normal));
            _arcBlend = same > 0.9f ? Mathf.Clamp01(Mathf.Abs(turnDeg) / 30f) : 0f;
            _turnSign = Mathf.Clamp(turnDeg / 70f, -1f, 1f);
            // 出っ張った角（上の面→壁）へ向かうときは、早めに頭を下げる
            _convexAhead = Vector3.Dot(tgt.normal, n) < 0.7f && Vector3.Dot(tgt.normal - n, go) > 0f;
            if (!settle) _stepStreak++;
            State = Mode.Reach;
            IsBlocked = false;
            _blockedTime = 0f;
        }

        /// <summary>
        /// 前に小さなすき間があって、その向こうに同じくらいの高さの面があれば、体を伸ばしてまたぐ
        /// （しゃくとりむしは体長の半分くらいのすき間なら渡れる）。
        /// </summary>
        bool TryBridge(Vector3 go, Vector3 stop, out SurfacePoint across, out bool gapAhead)
        {
            across = default;
            gapAhead = false;
            if (_tail.normal.y < 0.6f) return false;                 // 平らな所からだけ
            Vector3 f = Vector3.ProjectOnPlane(go, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) return false;
            f.Normalize();
            // ふちの先で一度落ちこんで（すき間）、体長の 6 割以内にまた同じくらいの高さの面があれば「またげるすき間」
            bool sawDrop = false;
            for (float k = 0.06f; k <= 0.61f; k += 0.06f)
            {
                Vector3 far = stop + f * (k * L) + Vector3.up * 0.5f;
                bool level = SurfaceProbe.Raycast(far, Vector3.down, 1.0f, out var fh) && fh.normal.y > 0.6f
                             && Mathf.Abs(fh.point.y - _tail.point.y) < 0.35f && SurfaceProbe.Valid(fh.point);
                if (!level) sawDrop = true;
                else if (sawDrop)
                {
                    gapAhead = true;
                    break;
                }
            }
            if (!gapAhead) return false;
            Vector3 eye = _tail.point + _tail.normal * 0.18f;
            // 向こう側の、ふちから少し入った所（尾を引き寄せる余地がある所）を、遠い方からさがす
            for (float k = 0.95f; k >= 0.49f; k -= 0.15f)
            {
                Vector3 above = _tail.point + f * (k * L) + Vector3.up * 0.5f;
                if (!SurfaceProbe.Raycast(above, Vector3.down, 1.0f, out var hit)) continue;
                if (hit.normal.y < 0.6f || Mathf.Abs(hit.point.y - _tail.point.y) > 0.35f) continue;
                if (Vector3.Dot(hit.point - stop, f) < 0.1f) continue;  // すき間の向こう側だけ
                if (!SurfaceProbe.Valid(hit.point)) continue;
                Vector3 back = hit.point - f * 0.15f + Vector3.up * 0.5f;
                if (!SurfaceProbe.Raycast(back, Vector3.down, 1.0f, out var bh) || bh.collider != hit.collider
                    || Mathf.Abs(bh.point.y - hit.point.y) > 0.1f) continue;   // ふちぎりぎりは×
                Vector3 to = hit.point + hit.normal * 0.08f - eye;
                if (SurfaceProbe.Raycast(eye, to.normalized, to.magnitude - 0.05f, out _)) continue;   // あいだに物がある
                across = SurfacePoint.On(hit.point, SurfaceProbe.SmoothNormal(hit), hit.collider);
                return true;
            }
            return false;
        }

        /// <summary>のぞきこむ（がけ）・水をたしかめる・頭を引っこめる（ぶつかった）。</summary>
        void TriggerPeek(string why)
        {
            if (why == "water") { _peekKind = "water"; _peekDur = 1.2f; }
            else if (IsEdgeFail(why)) { _peekKind = "cliff"; _peekDur = 1.4f; }
            else { _peekKind = "bump"; _peekDur = 0.45f; }
            _peekT = 0f;
        }

        void UpdateReach(float dt, Vector3 desired, bool wantsMove, bool sprint)
        {
            _rear = Mathf.MoveTowards(_rear, 0f, dt * 4f);
            _t += dt / _dur;
            // 頭を少し上げてから前へ（行き先をさぐる）
            float tm = Mathf.Clamp01((_t - 0.08f) / 0.92f);
            float e = ShakuMath.Smoother01(tm);
            Vector3 liftN = (_from.normal + _to.normal).normalized;
            float lift = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t)), 0.75f) * _reachLift * L;
            Vector3 p = Vector3.Lerp(_from.point, _to.point, e);
            if (_arcBlend > 0f)
            {
                // 曲がるときは、頭が尾のまわりを弧をえがいて動く
                Vector3 pivot = _tail.point;
                Vector3 arc = pivot + Vector3.Slerp(_from.point - pivot, _to.point - pivot, e);
                p = Vector3.Lerp(p, arc, _arcBlend);
            }
            p += liftN * lift;
            _head = new SurfacePoint(p, Vector3.Slerp(_from.normal, _to.normal, e).normalized);
            if (_t >= 1f)
            {
                _head = _to;
                Stepped?.Invoke(_head.point, true);
                MarkSafe();
                if (wantsMove)
                {
                    _tapAt = -99f;
                    StartPull(sprint);
                }
                else if (TakeTap(out _)) StartPull(sprint);
                else
                {
                    State = Mode.Idle;
                    if (!_settling || _stepStreak > 0) _settlePulse = 1f;   // 止まって、ふうっ
                }
            }
        }

        // ------------------------------------------------------------------
        // 地面の上での体の形
        // ------------------------------------------------------------------
        void BuildGroundTarget(float dt)
        {
            Vector3 tc = _tail.point + _tail.normal * Rad;
            Vector3 hc = _head.point + _head.normal * Rad;
            Vector3 upHint = _tail.normal + _head.normal;
            if (upHint.sqrMagnitude < 0.1f) upHint = _tail.normal;
            upHint.Normalize();
            Vector3 chord = hc - tc;
            Vector3 X = chord.sqrMagnitude > 1e-8f ? chord.normalized : Heading;
            Vector3 U = ShakuMath.ProjectOnPlaneSafe(upHint, X, _tail.normal);
            Vector3 Zs = Vector3.Cross(X, U);

            float tailAngle = PlaneAngle(ShakuMath.ProjectOnPlaneSafe(X, _tail.normal, X), X, U);
            float headPlanted = PlaneAngle(ShakuMath.ProjectOnPlaneSafe(X, _head.normal, X), X, U);
            float headAngle = headPlanted;
            float tNow = Mathf.Clamp01(_t);
            if (State == Mode.Reach)
            {
                // 伸びている途中は頭を少し上げて前を見る → 最後に、とんと下ろす。
                // はやいときは頭を低く、出っ張った角へ向かうときは早めに下げる
                float up0 = Mathf.Lerp(0.45f, 0.28f, _sprintBlend);
                float start = _convexAhead ? 0.2f : 0.35f;
                headAngle = Mathf.Lerp(up0, headPlanted, ShakuMath.Smooth01((tNow - start) / (1f - start)));
                headAngle -= 0.12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((tNow - 0.82f) / 0.18f));
            }
            else if (State == Mode.Pull) headAngle += 0.12f * Mathf.Sin(Mathf.PI * tNow);   // 引き寄せながら前を見る

            // のぞきこむ（がけ）・水をたしかめる・頭を引っこめる（ぶつかった）
            if (_peekT < _peekDur && State != Mode.Reach)
            {
                float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01(_peekT / _peekDur));
                Vector3 hf = ShakuMath.ProjectOnPlaneSafe(X, _head.normal, Zs).normalized;
                switch (_peekKind)
                {
                    case "cliff":
                        hc += hf * (0.16f * k) - _head.normal * (0.03f * k);
                        headAngle -= 0.7f * k;
                        break;
                    case "water":
                        float dip = Mathf.Abs(Mathf.Sin(Mathf.PI * 2f * Mathf.Clamp01(_peekT / _peekDur)));   // ちょんちょんと 2 回
                        hc += hf * (0.08f * k) - _head.normal * (0.04f * dip);
                        headAngle -= 0.55f * dip;
                        break;
                    default:
                        hc -= hf * (0.07f * k);
                        headAngle += 0.35f * k;
                        break;
                }
            }
            if (Time.time < _dazeUntil) headAngle += 0.15f * Mathf.Sin(_swayPhase * 9f);   // 目をまわして、ふらふら
            headAngle -= 0.4f * _flinch;
            tailAngle = Mathf.Clamp(tailAngle, -1.4f, 1.4f);
            headAngle = Mathf.Clamp(headAngle, -1.4f, 1.4f);

            // ---- 呼吸・弾み ----
            float breathe = 0f;
            if (State == Mode.Idle)
            {
                // 呼吸のリズムはゆらぎ、長く休むとゆっくり浅く
                _breathPhase += dt * Mathf.Lerp(2.1f, 1.3f, _relax) * (1f + 0.25f * Mathf.Sin(_swayPhase * 0.13f));
                breathe = (Mathf.Sin(_breathPhase) * 0.5f + 0.5f) * 0.012f * (1f - 0.4f * _relax);
            }
            breathe -= 0.012f * _relax;                                       // 力をぬいて低く
            breathe -= 0.03f * _gripPulse;                                    // 尾をつけた瞬間にきゅっ
            breathe += 0.022f * Mathf.Sin(Mathf.PI * _settlePulse);           // 止まって、ふうっ
            // 落ちて着地したあとの、ぽよんと弾む動き（強く落ちたら、まず一瞬ぺたんこ）
            if (_landBounce > 0f) breathe += _landBounce * _landBounce * 0.14f * Mathf.Abs(Mathf.Cos((1f - _landBounce) * 14f));
            breathe -= 0.05f * _landSquash * _landSquash;
            if (_pending == PendingFall.LetGo) breathe -= 0.035f * Mathf.Clamp01((Time.time - _pendingStart) / 0.12f);   // 手をはなす前にちぢめる
            breathe -= Mathf.Min(0.02f, _platformVel.magnitude * 0.01f);      // 乗り物が速いと低くして踏んばる
            breathe -= 0.03f * _flinch;

            // ---- 横のゆれ ----
            float sway;
            float slowK = Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.25f, 0.85f, _moveMag));
            if (State == Mode.Reach) sway = (Mathf.Sin(_swayPhase * 7f) * 0.12f * slowK + _turnSign * 0.15f) * Mathf.Sin(Mathf.PI * tNow);   // 曲がる方へかたむく
            else if (State == Mode.Pull) sway = _turnSign * 0.1f * Mathf.Sin(Mathf.PI * tNow);
            else
            {
                sway = Mathf.Sin(_swayPhase * 1.3f) * 0.04f * (1f - 0.5f * _relax);
                // 風がふくと、体がゆれる
                sway += Vector3.Dot(Wind.At(tc), Zs) * 0.05f * (0.6f + 0.4f * Mathf.Sin(_swayPhase * 2.7f)) * (1f - _twig);
            }
            sway += Vector3.Dot(_platformLean, Zs) * 0.5f;                   // 動く足場の加速で、体が逆へかたむく
            if (Time.time < _cheerUntil) sway += 0.3f * Mathf.Sin(_swayPhase * 14f);   // うれしいと左右にふりふり
            if (Time.time < _dazeUntil) sway += 0.25f * Mathf.Sin(_swayPhase * 12f);
            _target.BuildArch(tc, hc, U, L, tailAngle, headAngle, sway, breathe);
            ConformToSurface(_target);

            if (_rear > 0.001f)
            {
                float rise = Mathf.Clamp01(_rear);
                bool steering = Time.time - _lastLookInput < 1.5f || Time.time < _lookAtUntil;
                if (_standRequested && !steering && cameraTransform != null)
                {
                    // 方向キーを使っていないときは、カメラの向きを見る
                    Vector3 camF = ShakuMath.ProjectOnPlaneSafe(cameraTransform.forward, _tail.normal, X);
                    _standLookTarget = Mathf.Clamp(Vector3.SignedAngle(ShakuMath.ProjectOnPlaneSafe(X, _tail.normal, X), camF, _tail.normal) * Mathf.Deg2Rad, -0.9f, 0.9f);
                }
                _standLook = Mathf.MoveTowards(_standLook, _standLookTarget, dt * 2.6f);
                // 風が強いと、背伸びした体もゆれる
                float swayAmp = (steering ? 0.1f : 0.45f) * (1f + 0.6f * Wind.Gust(Time.time));
                float swayAngle = _standLook + Mathf.Sin(_swayPhase * 1.7f) * swayAmp * rise;
                float nod = 0.5f + 0.2f * Mathf.Sin(_swayPhase * 2.3f);
                // くんくん（ときどき小さくうなずく）
                nod += 0.09f * Mathf.Sin(_swayPhase * 11f) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_swayPhase * 0.9f)), 8f);
                // カメラが上を見ていると、いっしょに見上げる
                if (_standRequested && cameraTransform != null) nod -= 0.45f * Mathf.Clamp01(cameraTransform.forward.y * 2.5f);
                // 体の手入れ：頭を体の横へまわしてうつむく
                if (Time.time < _groomUntil)
                {
                    float g = Mathf.Sin(Mathf.PI * Mathf.Clamp01((Time.time - _groomStart) / 1.6f));
                    swayAngle = Mathf.Lerp(swayAngle, 1.5f * _groomSide, g);
                    nod = Mathf.Lerp(nod, 1.3f, g);
                }
                // 小枝のまね：ゆれずに、まっすぐかたまる
                if (_twig > 0f)
                {
                    swayAngle = Mathf.Lerp(swayAngle, 0.12f, _twig);
                    nod = Mathf.Lerp(nod, -0.15f, _twig);
                }
                // BuildRear の横向きは SignedAngle と逆まわりなので、符号を反転して渡す
                _pose.BuildRear(tc, X, _tail.normal, L, rise, -swayAngle, nod);
                _target.Blend(_target, _pose, ShakuMath.Smooth01(rise));
            }
        }

        static float PlaneAngle(Vector3 v, Vector3 X, Vector3 U)
        {
            return Mathf.Atan2(Vector3.Dot(v, U), Vector3.Dot(v, X));
        }

        readonly Vector3[] _push = new Vector3[Samples];
        readonly Vector3[] _smooth = new Vector3[Samples];

        /// <summary>でこぼこにめり込まないよう、体の各点を表面の外へ押し出す。</summary>
        void ConformToSurface(BodyCurve c)
        {
            int n = c.Count;
            for (int k = 0; k < n; k++) _push[k] = Vector3.zero;
            float minClear = Rad * 0.85f;
            for (int k = 1; k < n - 1; k++)
            {
                Vector3 p = c.pos[k];
                Vector3 u = c.up[k];
                const float probe = 0.3f;
                if (SurfaceProbe.Raycast(p + u * probe, -u, probe + Rad, out var hit))
                {
                    float clear = Vector3.Dot(p - hit.point, hit.normal);
                    if (clear < minClear) _push[k] = hit.normal * (minClear - clear);
                }
            }
            // 隣どうしでならす（でこぼこの押し出しが、体のなめらかさをこわさないように）
            for (int k = 1; k < n - 1; k++) _smooth[k] = (_push[k - 1] + _push[k] * 2f + _push[k + 1]) * 0.25f;
            for (int k = 1; k < n - 1; k++)
                c.pos[k] += _push[k].sqrMagnitude > _smooth[k].sqrMagnitude ? _push[k] : _smooth[k];
            c.RecomputeTangents();
        }

        // ------------------------------------------------------------------
        // 糸
        // ------------------------------------------------------------------
        void UpdateCanDrop()
        {
            CanDropSilk = false;
            if (State == Mode.Hang) return;
            Vector3 hc = _head.point + _head.normal * Rad;
            Vector3 hf = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _head.normal, Heading);
            Vector3 probe = hc + hf * 0.3f + _head.normal * 0.05f;
            if (!SurfaceProbe.Raycast(probe, Vector3.down, 1.6f, out _))
            {
                // ある程度下に地面があること（世界の外へ落ちない）
                if (SurfaceProbe.GroundBelow(probe, 200f, out var g) && !Areas.Current.IsUnderwater(g.point))
                    CanDropSilk = true;
            }
        }

        void StartHang()
        {
            Vector3 hc = _head.point + _head.normal * Rad;
            Vector3 hf = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _head.normal, Heading);
            _silkAnchor = _head.point;
            _anchorSurface = _head;
            _hangFacing = ShakuMath.ProjectOnPlaneSafe(hf, Vector3.up, Heading);
            _hangPos = hc + hf * 0.3f;
            _hangVel = hf * 0.8f;
            _silkLen = Mathf.Max(0.3f, Vector3.Distance(_silkAnchor, _hangPos));
            _rear = 0f;
            _silkSpeed = 0f;
            _hangStart = Time.time;
            _hangLean = Vector3.down;
            State = Mode.Hang;
            BeginTransition(0.45f);
            if (silk != null) silk.enabled = true;
            _silkFade = 1f;
            SilkStarted?.Invoke();
        }

        void UpdateHang(float dt, Vector3 desired, float mag, bool sprint)
        {
            _shotT = Mathf.Min(1f, _shotT + dt / 0.18f);
            // 動く物（舟・いきもの）にかけた糸は、いっしょに動く
            if (_anchorSurface.platform != null)
            {
                _anchorSurface = _anchorSurface.Updated();
                _silkAnchor = _anchorSurface.point;
            }
            if (_reeling)
            {
                // ねらって出した糸：自動でたぐって、ねらった場所へ。Space でやめて落ちる
                if (InputEnabled && GameInput.SilkPressed && _shotT >= 1f)
                {
                    _reeling = false;
                    _fallSpinRate = 3.5f;
                    StartFall(_hangVel, _anchorSurface);
                    return;
                }
                if (_shotT >= 1f) _silkLen -= silkReelSpeed * dt;
                _silkSpeed = -silkReelSpeed;
            }
            else
            {
                // 糸ののぼり下りは、なめらかに速さを変える
                bool climb = InputEnabled && GameInput.SilkHeld && _blendT >= 1f;
                float want = climb ? -silkClimbSpeed : (sprint ? silkFastSpeed : silkDescendSpeed);
                _silkSpeed = Mathf.MoveTowards(_silkSpeed, want, dt * (climb ? 7f : 5f));
                _silkLen += _silkSpeed * dt;
                _silkLen = Mathf.Min(_silkLen, silkMaxLength);
            }

            if (_silkLen < 0.25f)
            {
                // 糸をたぐって元の場所（ねらった場所）へ
                _reeling = false;
                ReattachAt(_anchorSurface, _hangFacing);
                return;
            }

            // 振り子
            Vector3 horiz = Vector3.ProjectOnPlane(desired, Vector3.up) * mag;
            // こぐ：ゆれている向きに合わせて押すと、よくゆれる
            Vector3 flatVel = Vector3.ProjectOnPlane(_hangVel, Vector3.up);
            float pump = 4f;
            if (horiz.sqrMagnitude > 1e-4f && flatVel.sqrMagnitude > 1e-4f) pump += 3f * Mathf.Max(0f, Vector3.Dot(horiz.normalized, flatVel.normalized));
            // 重力・入力でこぐ力・風（糸が長いほど、風にゆられる）
            float windK = 0.2f + Mathf.Min(_silkLen, 15f) * 0.03f;
            _hangVel += (Physics.gravity * 0.6f + horiz * pump + Wind.At(_hangPos) * windK) * dt;
            _hangVel *= 1f - Mathf.Clamp01(1.2f * dt);
            Vector3 prev = _hangPos;
            _hangPos += _hangVel * dt;
            Vector3 off = _hangPos - _silkAnchor;
            if (off.magnitude > _silkLen)
            {
                _hangPos = _silkAnchor + off.normalized * _silkLen;
                Vector3 radial = off.normalized;
                _hangVel -= radial * Mathf.Max(0f, Vector3.Dot(_hangVel, radial));
            }
            // 壁にぶつかったら止める
            Vector3 mv = _hangPos - prev;
            if (mv.sqrMagnitude > 1e-8f && Physics.SphereCast(prev, 0.06f, mv.normalized, out var wall, mv.magnitude, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
            {
                // 壁に向かってスティックを倒していれば、ぶら下がったまま壁につかまる
                if (!_reeling && wall.distance > 1e-4f && wall.normal.y < 0.5f && wall.normal.y > -0.3f && horiz.sqrMagnitude > 0.04f
                    && Vector3.Dot(horiz.normalized, -wall.normal) > 0.5f && _blendT >= 1f && SurfaceProbe.Valid(wall.point))
                {
                    _hangFacing = Vector3.up;
                    Land(SurfacePoint.On(wall.point, SurfaceProbe.SmoothNormal(wall), wall.collider));
                    return;
                }
                if (wall.distance <= 1e-4f)
                {
                    // 壁にめりこんでいた：外へ押し出して、壁にそって動く
                    _hangPos = prev + wall.normal * 0.02f + Vector3.ProjectOnPlane(mv, wall.normal);
                    _hangVel = Vector3.ProjectOnPlane(_hangVel, wall.normal) * 0.7f;
                }
                else if (wall.normal.y < 0.5f)
                {
                    _hangPos = prev + mv.normalized * Mathf.Max(0f, wall.distance - 0.01f) + Vector3.ProjectOnPlane(mv, wall.normal) * 0.5f;
                    _hangVel = Vector3.ProjectOnPlane(_hangVel, wall.normal) * 0.5f;
                }
            }
            if (horiz.sqrMagnitude > 0.01f) _hangFacing = Vector3.Slerp(_hangFacing, horiz.normalized, ShakuMath.DampFactor(2f, dt));
            else if (!_reeling)
            {
                // じっとしていると、糸の上でゆっくり回る
                _hangFacing = (Quaternion.AngleAxis(Mathf.Sin(_swayPhase * 0.31f) * 14f * dt, Vector3.up) * _hangFacing).normalized;
            }

            // 着地：尾が地面にとどいたら（糸をたぐっている間は着地しない）
            if (!_reeling && _blendT >= 1f && SurfaceProbe.Raycast(_hangPos, Vector3.down, L * 0.95f, out var ground))
            {
                Vector3 gn = SurfaceProbe.SmoothNormal(ground);
                if (gn.y > 0.35f && !Areas.Current.IsUnderwater(ground.point)) Land(SurfacePoint.On(ground.point, gn, ground.collider));
                else if (Areas.Current.IsUnderwater(ground.point) && _silkLen > 0.5f)
                {
                    // 水には入れないので、それ以上は下りない
                    _silkLen = Mathf.Min(_silkLen, Vector3.Distance(_silkAnchor, _hangPos));
                }
            }
            // 水面より下には行かせない
            if (Areas.Current.IsUnderwater(_hangPos - Vector3.up * L * 0.9f))
                _silkLen = Mathf.Max(0.3f, _silkLen - (sprint ? silkFastSpeed : silkDescendSpeed) * dt);
        }

        void Land(SurfacePoint tail)
        {
            Vector3 point = tail.point;
            Vector3 normal = tail.normal;
            Vector3 f = ShakuMath.ProjectOnPlaneSafe(_hangFacing, normal, ShakuMath.AnyPerpendicular(normal));
            BeginTransition(0.5f);
            _tail = tail;
            if (SurfaceProbe.Walk(tail, f, restRatio * L, out var h, out _)) _head = h;
            else if (SurfaceProbe.Walk(tail, -f, restRatio * L, out h, out _)) _head = h;
            else _head = new SurfacePoint(point + f * 0.4f, normal);
            State = Mode.Idle;
            _idleTime = 0f;
            _landBounce = Mathf.Max(_landBounce, 0.2f);   // 尾がついて、体が横たわるときに少し弾む
            MarkSafe();
            Landed?.Invoke();
        }

        void ReattachAt(SurfacePoint head, Vector3 facing)
        {
            BeginTransition(0.4f);
            _reeling = false;
            _head = head;
            Vector3 back = -ShakuMath.ProjectOnPlaneSafe(facing, head.normal, ShakuMath.AnyPerpendicular(head.normal));
            if (SurfaceProbe.Walk(head, back, restRatio * L, out var t, out _)) _tail = t;
            else _tail = new SurfacePoint(head.point + back * 0.3f, head.normal);
            State = Mode.Idle;
            _landBounce = 0.3f;   // たぐり終えて、ぽよん
            MarkSafe();
            Landed?.Invoke();
        }

        void BuildHangTarget()
        {
            float fast = Mathf.Clamp01(_silkSpeed / Mathf.Max(0.01f, silkFastSpeed));
            float climbing = _reeling ? 0f : Mathf.Clamp01(-_silkSpeed / Mathf.Max(0.01f, silkClimbSpeed));
            float curl = 0.55f + 0.25f * Mathf.Sin(_swayPhase * 1.3f);
            curl = Mathf.Lerp(curl, 0.18f, fast * 0.8f);                                // 速く下りると、体がまっすぐ
            curl += climbing * 0.35f * Mathf.Sin(_swayPhase * 9f);                       // 糸をのぼるときは、体をくいくい
            // 地面が近いと、体を伸ばして着地にそなえる
            if (SurfaceProbe.Raycast(_hangPos, Vector3.down, L * 2f, out var g))
                curl = Mathf.Lerp(curl, 0.1f, Mathf.Clamp01(1f - (g.distance - L) / L));
            float wiggle = Mathf.Sin(_swayPhase * 2.2f) * 0.25f * (1f + 1.5f * _moveMag);  // スティックを倒すと、もがく
            Vector3 head = _hangPos;
            if (climbing > 0f) head += Vector3.up * (0.025f * climbing * Mathf.Sin(_swayPhase * 9f));
            // 糸をつけた直後は、頭をふって糸をつける
            float since = Time.time - _hangStart;
            if (since < 0.35f) wiggle += Mathf.Sin(since * 40f) * 0.3f * (1f - since / 0.35f);
            _target.BuildHang(head, _hangFacing, L, curl, wiggle);
            // 体は糸の向きにそってたれ下がる（振り子で糸がかたむけば、体も少しおくれてかたむく）
            Vector3 silkDir = _hangPos - _silkAnchor;
            Vector3 want = silkDir.sqrMagnitude > 1e-4f ? silkDir.normalized : Vector3.down;
            if (want.y > -0.2f) want = Vector3.down;
            _hangLean = Vector3.Slerp(_hangLean, want, ShakuMath.DampFactor(6f, Time.deltaTime)).normalized;
            _target.RotateAround(head, Quaternion.FromToRotation(Vector3.down, _hangLean));
        }

        void UpdateSilkLine(float dt)
        {
            if (silk == null) return;
            if (State == Mode.Hang)
            {
                silk.enabled = true;
                _silkFade = 1f;
                silk.positionCount = 2;
                // 発射した糸は、頭から狙った場所へのびていく
                silk.SetPosition(0, _shotT < 1f ? Vector3.Lerp(_curve.Head, _silkAnchor, ShakuMath.Smooth01(_shotT)) : _silkAnchor);
                silk.SetPosition(1, _curve.Head);
            }
            else if (_silkFade > 0f)
            {
                _silkFade -= dt * 1.5f;
                silk.positionCount = 2;
                silk.SetPosition(1, _curve.Head);
                var c = new Color(1f, 1f, 1f, Mathf.Clamp01(_silkFade));
                silk.startColor = c;
                silk.endColor = c;
                if (_silkFade <= 0f) silk.enabled = false;
            }
            if (State == Mode.Hang)
            {
                silk.startColor = Color.white;
                silk.endColor = Color.white;
            }
        }

        // ------------------------------------------------------------------
        // はなれて落ちる
        // ------------------------------------------------------------------
        /// <summary>
        /// 本当にがけか：尾が平らな所にいて、頭の先の下に 1 体長ほど何もない。
        /// （壁の上や、キノコの柄のそばの地面では落ちない）
        /// </summary>
        bool IsRealDrop(string why, Vector3 edge, Vector3 forward)
        {
            // 「がけ」と判定されたふち（上向きの面から 80 度より急に下りる）は、尾がどこにいてもよい。
            // それ以外（先に何もない）は、尾が平らな所にいるときだけ
            bool cliff = why == "cliff";
            if (!cliff && _tail.normal.y < 0.5f) return false;
            Vector3 f = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) return false;
            // ふち（探索が止まった所）の少し先の下に、1 体長ほど何もなければ本当のがけ
            Vector3 probe = edge + f.normalized * 0.35f + Vector3.up * 0.1f;
            if (!SurfaceProbe.Raycast(probe, Vector3.down, L * 0.9f + 0.1f, out var below)) return true;
            // がけのふちのすぐ下が急な面（切り株の外側のふくらんだ樹皮など）なら、そこもがけ
            return cliff && below.normal.y < 0.5f;
        }

        /// <summary>がけのふちから、前へのり出して落ちる（一瞬のり出してから）。</summary>
        void WalkOff(Vector3 edge, Vector3 forward)
        {
            Vector3 f = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = forward;
            f.Normalize();
            _pending = PendingFall.WalkOff;
            _pendingStart = Time.time;
            _pendingUntil = Time.time + 0.16f;
            _pendingVel = f * 1.6f + Vector3.up * 0.5f;
            _pendingFrom = SurfacePoint.On(edge, Vector3.up, null);
            _pendingEdge = edge;
            _pendingDir = f;
            _fallSpinRate = 6.5f;   // 前へのり出して落ちるときは、くるくるよく回る
            _peekKind = "cliff";
            _peekT = 0f;
            _peekDur = 0.5f;
            State = Mode.Idle;
            IsBlocked = false;
        }

        /// <summary>壁や裏側から手をはなして落ちる（体をちぢめてから）。</summary>
        public void LetGo()
        {
            if (State == Mode.Hang || State == Mode.Fall || _pending != PendingFall.None) return;
            Vector3 n = (_tail.normal + _head.normal).normalized;
            if (n.sqrMagnitude < 0.5f) n = _head.normal;
            _pending = PendingFall.LetGo;
            _pendingStart = Time.time;
            _pendingUntil = Time.time + 0.12f;
            // 天井からは、まっすぐ下へ。壁からは、面から少しはなれるように押し出す
            _pendingVel = n.y < -0.5f ? n * 0.6f : n * 1.1f + Vector3.up * 0.4f;
            _pendingFrom = _head;
            _fallSpinRate = 4.5f;
            State = Mode.Idle;
        }

        /// <summary>「ため」が終わったら落ちはじめる。</summary>
        void UpdatePending()
        {
            if (_pending == PendingFall.None || Time.time < _pendingUntil) return;
            var kind = _pending;
            _pending = PendingFall.None;
            if (State == Mode.Hang || State == Mode.Fall) return;
            StartFall(_pendingVel, _pendingFrom);
            // がけのふちから、体をのり出して落ちはじめる
            if (kind == PendingFall.WalkOff) _fallPos = _pendingEdge + _pendingDir * 0.25f + Vector3.up * 0.12f;
        }

        void StartFall(Vector3 velocity, SurfacePoint from)
        {
            _fallPos = _curve.Middle;
            _fallStuck = 0f;
            _fallVel = velocity;
            _fallFrom = from;
            _fallTime = 0f;
            _sinkTimer = 0f;
            _fallSpin = 0f;
            _uncurl = 0f;
            _pending = PendingFall.None;
            Vector3 h = Vector3.ProjectOnPlane(velocity.sqrMagnitude > 0.01f ? velocity : Heading, Vector3.up);
            if (h.sqrMagnitude < 1e-4f) h = Vector3.ProjectOnPlane(Heading, Vector3.up);
            if (h.sqrMagnitude < 1e-4f) h = Vector3.forward;
            _fallAxis = Vector3.Cross(Vector3.up, h.normalized).normalized;
            _hangFacing = h.normalized;
            _rear = 0f;
            _reeling = false;
            IsBlocked = false;
            State = Mode.Fall;
            BeginTransition(0.22f);
            Fell?.Invoke();
        }

        void UpdateFall(float dt, Vector3 desired, float mag)
        {
            _fallTime += dt;
            if (_sinkTimer > 0f)
            {
                // 水に落ちた：少し沈んでから、最後の安全な場所へ戻る
                _sinkTimer += dt;
                // 水面でぷかぷか浮いてから、しずむ
                if (_sinkTimer < 0.45f) _fallPos += Vector3.up * (Mathf.Cos(_sinkTimer * 20f) * 0.25f * dt);
                else _fallPos += Vector3.down * 0.4f * dt;
                if (_sinkTimer > 1.0f) Spawn(_lastSafeTail, _lastSafeNormal, _lastSafeHead, _lastSafeNormal);
                return;
            }
            // 落ちながら Space で糸を出すと、はなれた場所にぶら下がれる
            if (InputEnabled && GameInput.SilkPressed && _fallTime > 0.08f && Vector3.Distance(_fallFrom.point, _fallPos) < silkRange)
            {
                CatchWithSilk();
                return;
            }
            // 重力・風・すこしだけ空中で向きを変えられる
            _fallVel += Vector3.down * fallGravity * dt;
            _fallVel += Vector3.ProjectOnPlane(desired, Vector3.up) * mag * 1.2f * dt;
            _fallVel += Wind.At(_fallPos) * 0.2f * dt;
            // 地面が近づくと体を開いて着地にそなえる（開くと空気をうけて、少しゆっくりに）
            float groundDist = SurfaceProbe.Raycast(_fallPos, Vector3.down, 1.6f, out var belowHit) ? belowHit.distance : 9f;
            _uncurl = Mathf.MoveTowards(_uncurl, groundDist < 1.6f ? 1f - groundDist / 1.6f : 0f, dt * 4f);
            _fallVel *= 1f - Mathf.Clamp01(Mathf.Lerp(0.35f, 0.9f, _uncurl) * dt);
            if (_fallVel.magnitude > fallTerminal) _fallVel = _fallVel.normalized * fallTerminal;
            // 落ち方で回る速さが変わる。体を開くと、ゆっくりに
            _fallSpin += dt * (_fallSpinRate + _fallVel.magnitude * 0.6f) * (1f - 0.6f * _uncurl);
            // 回る軸は、ゆっくり首をふる
            _fallAxis = (Quaternion.AngleAxis(25f * dt, Vector3.up) * _fallAxis).normalized;
            // スティックで、体の向きも少し変えられる
            if (mag > 0.2f)
            {
                Vector3 df = Vector3.ProjectOnPlane(desired, Vector3.up);
                if (df.sqrMagnitude > 1e-4f) _hangFacing = Vector3.Slerp(_hangFacing, df.normalized, ShakuMath.DampFactor(2f, dt)).normalized;
            }

            Vector3 move = _fallVel * dt;
            const float radius = 0.16f;
            // 物の中から落ちはじめたときは、まず外へ出る
            if (_fallTime < 0.05f && Physics.CheckSphere(_fallPos, radius * 0.8f, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore))
                _fallPos += _fallFrom.normal * 0.06f + Vector3.up * 0.04f;
            // ほとんど動けないまま落ちつづけていたら（すき間にはさまった）、安全な場所へ戻す
            _fallStuck = move.magnitude < fallGravity * dt * dt * 0.5f ? _fallStuck + dt : 0f;
            if (_fallStuck > 1.5f || _fallTime > 6f)
            {
                Spawn(_lastSafeTail, _lastSafeNormal, _lastSafeHead, _lastSafeNormal);
                return;
            }
            if (move.sqrMagnitude > 1e-8f && Physics.SphereCast(_fallPos, radius, move.normalized, out var hit, move.magnitude + 0.02f, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore)
                && _fallTime > 0.06f && hit.distance > 1e-4f && Vector3.Dot(hit.normal, move) < 0f)
            {
                Vector3 contact = hit.point;
                Vector3 hn = SurfaceProbe.SmoothNormal(hit);
                if (Areas.Current.IsUnderwater(contact)) { Splash(); return; }
                if (hn.y > -0.2f)
                {
                    LandFromFall(SurfacePoint.On(contact, hn, hit.collider));
                    return;
                }
                // 裏側に当たったら、はね返って落ちつづける
                _fallPos += move.normalized * Mathf.Max(0f, hit.distance - 0.01f);
                // ぶつかる角度で、はね返り方が変わる（まっすぐ当たるほど、よくはねる）
                _fallVel = Vector3.Reflect(_fallVel, hn) * (0.15f + 0.3f * Mathf.Abs(Vector3.Dot(_fallVel.normalized, hn)));
                return;
            }
            _fallPos += move;
            if (Areas.Current.IsUnderwater(_fallPos)) Splash();
        }

        void Splash()
        {
            _sinkTimer = 0.0001f;
            _fallVel = Vector3.zero;
            Splashed?.Invoke();
        }

        void LandFromFall(SurfacePoint tail)
        {
            float speed = _fallVel.magnitude;
            Vector3 f = Vector3.ProjectOnPlane(_fallVel, tail.normal);
            if (f.sqrMagnitude < 0.01f) f = _hangFacing;
            // 坂に落ちたら、下り向きに体をそろえる
            if (tail.normal.y < 0.85f)
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, tail.normal);
                if (downhill.sqrMagnitude > 1e-4f) f = Vector3.Slerp(f.normalized, downhill.normalized, 0.5f);
            }
            _hangFacing = f.normalized;
            Land(tail);
            BeginTransition(0.28f);
            _landBounce = Mathf.Clamp01(0.35f + speed * 0.12f);
            _landSquash = Mathf.Clamp01(speed / 6f);   // 強く落ちるほど、一瞬ぺたんこ
            _dazeUntil = Time.time + Mathf.Lerp(0.15f, 0.6f, Mathf.Clamp01(speed / 7f));   // 強く落ちると少しだけ目をまわす
            HitGround?.Invoke(speed);
        }

        void CatchWithSilk()
        {
            _silkAnchor = _fallFrom.point;
            _anchorSurface = _fallFrom;
            _hangPos = _fallPos;
            _hangVel = _fallVel * 0.5f;
            _silkLen = Mathf.Max(0.3f, Vector3.Distance(_silkAnchor, _hangPos));
            _silkSpeed = 0f;
            _hangStart = Time.time;
            _shotT = 0.4f;
            _reeling = false;
            State = Mode.Hang;
            BeginTransition(0.3f);
            if (silk != null) silk.enabled = true;
            _silkFade = 1f;
            SilkStarted?.Invoke();
        }

        void BuildFallTarget()
        {
            // くるんと丸まって、回りながら落ちる（地面が近づくと体を開く）
            _target.BuildCurl(_fallPos, _fallAxis, _hangFacing, L, Mathf.Lerp(4.3f, 2.0f, _uncurl * _uncurl), _fallSpin);
        }

        // ------------------------------------------------------------------
        // ねらって糸を出す
        // ------------------------------------------------------------------
        void UpdateAim()
        {
            bool canAim = State != Mode.Fall && !_reeling && !_standRequested && cameraTransform != null;
            bool held = InputEnabled && GameInput.AimHeld && canAim;
            if (held)
            {
                IsAiming = true;
                ComputeAim();
                return;
            }
            if (IsAiming)
            {
                IsAiming = false;
                if (AimValid && canAim && InputEnabled) FireSilk();
                AimValid = false;
            }
        }

        void ComputeAim()
        {
            AimValid = false;
            Vector3 head = _curve.Head + SurfaceUp * 0.08f;
            Vector3 origin = cameraTransform.position;
            Vector3 dir = cameraTransform.forward;
            float camToHead = Vector3.Distance(origin, head);
            if (!SurfaceProbe.Raycast(origin, dir, camToHead + silkRange + 2f, out var hit))
            {
                AimPoint = origin + dir * (camToHead + silkRange);
                AimProblem = "とどく所に、つかまる場所がない";
                return;
            }
            AimPoint = hit.point;
            float dist = Vector3.Distance(head, hit.point);
            if (dist > silkRange)
            {
                // 照準アシスト：まん中が少し遠いときは、まわりのとどく所をさがす
                if (AimAssist(origin, dir, head)) return;
                AimProblem = $"遠すぎてとどかない（あと {dist - silkRange:F0} 体長）";
                return;
            }
            if (dist < 1.2f) { AimProblem = "近すぎる"; return; }
            if (hit.point.y < head.y - 0.6f) { AimProblem = "上のほうをねらおう"; return; }
            if (Areas.Current.IsUnderwater(hit.point) || !Areas.Current.InPlayArea(hit.point)) { AimProblem = "そこにはつかまれない"; return; }
            // 頭から見えているか（間にじゃまな物がないか）
            Vector3 toT = hit.point - head;
            if (SurfaceProbe.Raycast(head, toT.normalized, toT.magnitude - 0.15f, out var block) && block.collider != hit.collider)
            {
                AimProblem = "あいだに物がある";
                return;
            }
            _aimHit = hit;
            AimValid = true;
            AimProblem = "";
        }

        /// <summary>まん中のまわり（4〜8 度）に、とどいて上向きにつかまれる場所があれば、そこをねらう。</summary>
        bool AimAssist(Vector3 origin, Vector3 dir, Vector3 head)
        {
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 up = Vector3.Cross(dir, right).normalized;
            for (int ring = 1; ring <= 2; ring++)
            {
                float deg = 4f * ring;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector3 d2 = Quaternion.AngleAxis(deg, right * Mathf.Cos(a) + up * Mathf.Sin(a)) * dir;
                    if (!SurfaceProbe.Raycast(origin, d2, Vector3.Distance(origin, head) + silkRange + 2f, out var h)) continue;
                    if (Vector3.Distance(head, h.point) > silkRange || h.point.y < head.y - 0.6f) continue;
                    if (Areas.Current.IsUnderwater(h.point) || !Areas.Current.InPlayArea(h.point)) continue;
                    Vector3 toT = h.point - head;
                    if (SurfaceProbe.Raycast(head, toT.normalized, toT.magnitude - 0.15f, out var block) && block.collider != h.collider) continue;
                    _aimHit = h;
                    AimPoint = h.point;
                    AimValid = true;
                    AimProblem = "";
                    return true;
                }
            }
            return false;
        }

        /// <summary>ねらった場所へ糸を発射して、たぐり寄せる。</summary>
        public bool FireSilk()
        {
            if (!AimValid) return false;
            var hit = _aimHit;
            Vector3 hn = SurfaceProbe.SmoothNormal(hit);
            Vector3 start = State == Mode.Hang ? _hangPos : _curve.Head + SurfaceUp * 0.1f;
            _silkAnchor = hit.point;
            _anchorSurface = SurfacePoint.On(hit.point, hn, hit.collider);
            _hangPos = start;
            Vector3 to = hit.point - start;
            _hangFacing = ShakuMath.ProjectOnPlaneSafe(to, Vector3.up, Heading).normalized;
            _hangVel = to.normalized * 2.2f + Vector3.up * 1.2f;
            _silkLen = Mathf.Max(0.3f, to.magnitude);
            _silkSpeed = 0f;
            _hangStart = Time.time;
            _shotT = 0f;
            _reeling = true;
            _rear = 0f;
            State = Mode.Hang;
            BeginTransition(0.35f);
            if (silk != null) silk.enabled = true;
            _silkFade = 1f;
            AimValid = false;
            SilkStarted?.Invoke();
            return true;
        }

        /// <summary>テスト用：カメラを使わずに、指定した場所をねらう。</summary>
        public bool AimAt(Vector3 point)
        {
            Vector3 head = _curve.Head + SurfaceUp * 0.08f;
            Vector3 dir = point - head;
            AimValid = false;
            if (!SurfaceProbe.Raycast(head, dir.normalized, dir.magnitude + 0.5f, out var hit)) return false;
            if (Vector3.Distance(head, hit.point) > silkRange || hit.point.y < head.y - 0.6f) return false;
            _aimHit = hit;
            AimPoint = hit.point;
            AimValid = true;
            return true;
        }

        // ------------------------------------------------------------------
        // まわりへの反応・足場
        // ------------------------------------------------------------------
        /// <summary>進めないまま押しつづけたとき：ときどきのぞきこみ、長く続けば横へずれて抜け出す。</summary>
        void UpdateBlocked(float dt, Vector3 desired, bool wantsMove)
        {
            if (!(IsBlocked && wantsMove && State == Mode.Idle))
            {
                if (!wantsMove) _blockedTime = 0f;
                return;
            }
            _blockedTime += dt;
            if (_peekT > _peekDur + 1.6f) TriggerPeek(BlockReason);   // ときどき、もう一度のぞく
            // 物に引っかかったまま 2.5 秒たったら、左右へ一歩ずれてみる
            if (_blockedTime > 2.5f && Time.time > _unstickNext && IsObstacleFail(BlockReason) || (_blockedTime > 2.5f && Time.time > _unstickNext && BlockReason == ""))
            {
                _unstickNext = Time.time + 1.5f;
                _unstickSide = -_unstickSide;
                Vector3 side = Vector3.Cross(_tail.normal, desired);
                if (side.sqrMagnitude > 0.25f) StartReach(side.normalized * _unstickSide, false, false);
            }
        }

        /// <summary>足もとの物がなくなったら（乗っていたものが動いていった・消えた）、落ちる。</summary>
        void UpdateSupport(float dt)
        {
            _supportTimer -= dt;
            if (_supportTimer > 0f) return;
            _supportTimer = 0.3f;
            if (State != Mode.Idle || _pending != PendingFall.None) { _unsupported = 0; return; }
            bool held = Holds(_tail) || Holds(_head);
            _unsupported = held ? 0 : _unsupported + 1;
            if (_unsupported < 2) return;
            _unsupported = 0;
            Vector3 m = _curve.Middle;
            if (m.y - Areas.Current.Height(m.x, m.z) < 0.35f) return;   // 地面のすぐ上なら落ちない
            _fallSpinRate = 4f;
            StartFall(Vector3.zero, _tail);
        }

        static bool Holds(SurfacePoint sp) => SurfaceProbe.Raycast(sp.point + sp.normal * 0.12f, -sp.normal, 0.32f, out _);

        /// <summary>
        /// まわりのいきものへの反応：天敵（鳥・カマキリ）が近くで動くと小枝のまねでかたまり、
        /// すぐそばを通るとびくっとし、近くで動くものには目を向ける。見ているものが動けば目で追う。
        /// </summary>
        void UpdateReactions(float dt, bool wantsMove)
        {
            var cr = Creatures.Instance;
            if (cr == null) return;
            if (Time.time < _lookAtUntil && cr.NearestMob(_lookAtPoint, 1.5f, false, out var tracked, out _, out _)) _lookAtPoint = tracked;
            _reactTimer -= dt;
            if (_reactTimer > 0f) return;
            _reactTimer = 0.25f;
            if (State != Mode.Idle || wantsMove || _standRequested || _pending != PendingFall.None) return;
            Vector3 head = _curve.Head;
            if (cr.NearestMob(head, 7f, true, out var ppos, out float pspeed, out var psp)
                && pspeed > 0.3f && Vector3.Distance(ppos, head) < (psp.id == "crow" ? 7f : 5f))
            {
                _freezeUntil = Time.time + 2.2f;
                return;
            }
            if (!cr.NearestMob(head, 3f, false, out var pos, out float speed, out _) || speed < 0.05f) return;
            float d = Vector3.Distance(pos, head);
            if (d < 0.75f && Time.time > _flinchNext)
            {
                _flinch = 1f;
                _flinchNext = Time.time + 3f;
            }
            else if (Time.time > _curiousNext && Time.time >= _lookAtUntil)
            {
                _curiousNext = Time.time + UnityEngine.Random.Range(5f, 9f);
                LookAt(pos, 1.6f);
            }
        }

        /// <summary>乗っている足場（舟・いきもの）の動き：加速すると体が逆へかたむく。</summary>
        void UpdatePlatformMotion(float dt)
        {
            Transform pf = _tail.platform != null ? _tail.platform : _head.platform;
            Vector3 v = Vector3.zero;
            if (pf != null && pf == _platformRef) v = (pf.position - _platformPrev) / dt;
            v = Vector3.ClampMagnitude(v, 20f);
            _platformRef = pf;
            _platformPrev = pf != null ? pf.position : Vector3.zero;
            Vector3 acc = (v - _platformVel) / dt;
            _platformVel = Vector3.Lerp(_platformVel, v, ShakuMath.DampFactor(8f, dt));
            Vector3 lean = Vector3.ClampMagnitude(-acc * 0.02f, 0.6f);
            _platformLean = Vector3.Lerp(_platformLean, pf != null ? lean : Vector3.zero, ShakuMath.DampFactor(5f, dt));
        }

        // ------------------------------------------------------------------
        // 安全装置
        // ------------------------------------------------------------------
        void SafetyCheck(float dt)
        {
            bool bad = float.IsNaN(_curve.Middle.x) || _curve.Middle.y < -40f || !float.IsFinite(_curve.Head.y);
            if (State == Mode.Hang && _silkLen > 200f) bad = true;
            if (State == Mode.Fall && _fallTime > 8f) bad = true;
            // 地面の中にうまってしまったら（すき間からぬけた）、少しして安全な場所へ
            if (State != Mode.Fall && State != Mode.Hang)
            {
                Vector3 m = _curve.Middle;
                _buriedTime = m.y < Areas.Current.Height(m.x, m.z) - 0.6f ? _buriedTime + dt : 0f;
                if (_buriedTime > 1f)
                {
                    _buriedTime = 0f;
                    Debug.LogWarning("[Inchworm] 地面の中に入ってしまったので、安全な場所へもどします");
                    ReturnToSafety();
                    return;
                }
            }
            if (bad)
            {
                Debug.LogWarning("[Inchworm] 不正な状態になったので最後の安全な場所に戻します");
                Spawn(_lastSafeTail, _lastSafeNormal, _lastSafeHead, _lastSafeNormal);
            }
        }

        public void Teleport(Vector3 point, Vector3 forward)
        {
            Spawn(point, forward);
        }

        /// <summary>最後に安全だった場所（尾の位置）。</summary>
        public Vector3 LastSafePoint => _lastSafeTail;

        /// <summary>
        /// 動けなくなったとき：いまの場所から少しはなれた、最近の安全な場所へもどる
        /// （いまいる場所そのものが「安全」と記録されていることもあるので）。
        /// </summary>
        public void ReturnToSafety()
        {
            Vector3 here = _curve.Middle;
            for (int i = _safeHistory.Count - 1; i >= 0; i--)
            {
                var h = _safeHistory[i];
                if (Vector3.Distance(h.tail, here) < 2.5f) continue;
                Spawn(h.tail, h.normal, h.head, h.normal);
                return;
            }
            if (_safeHistory.Count > 0)
            {
                var h = _safeHistory[0];
                Spawn(h.tail, h.normal, h.head, h.normal);
                return;
            }
            Spawn(_lastSafeTail, _lastSafeNormal, _lastSafeHead, _lastSafeNormal);
        }

        /// <summary>エリアを移動したら、前のエリアの安全な場所は忘れる。</summary>
        public void ClearSafeHistory() => _safeHistory.Clear();

        /// <summary>もどれる安全な場所の数（テスト用）。</summary>
        public int SafePlaceCount => _safeHistory.Count;
        public Vector3 SafePlace(int i) => _safeHistory[i].tail;

        /// <summary>カメラが見る位置。</summary>
        public Vector3 CameraFocus
        {
            get
            {
                if (State == Mode.Hang) return _hangPos - Vector3.up * 0.35f;
                if (State == Mode.Fall) return _fallPos;
                Vector3 c = Vector3.Lerp(_curve.Middle, _curve.Head, 0.35f);
                return c + SurfaceUp * 0.12f + Vector3.up * 0.08f;
            }
        }
    }
}

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
        public Vector3 SilkAnchor => _silkAnchor;
        public float RearAmount => _rear;
        public Vector3 TailPoint => _tail.point;
        public Vector3 HeadPoint => _head.point;
        /// <summary>葉っぱの舟など、動く足場に乗っているか。</summary>
        public bool OnMovingPlatform => _tail.platform != null || _head.platform != null;

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
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
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
            bool wantsMove = mag > 0.2f && Time.time >= _dazeUntil;
            bool sprint = InputEnabled && GameInput.Sprint;
            _standRequested = InputEnabled && GameInput.StandHeld;

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
                if (InputEnabled && GameInput.SilkPressed && !_standRequested)
                {
                    if (CanDropSilk) StartHang();
                    else if (OnSteepSurface) LetGo();   // 壁や裏側では、はなれて落ちる
                }
            }
            else CanDropSilk = false;
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
            float rearTarget = 0f;
            bool standing = _standRequested;
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
                    // ときどき顔を上げてきょろきょろ
                    _lookTimer += dt;
                    if (_idleTime > 5f && _lookTimer > 7.5f)
                    {
                        _lookTimer = 0f;
                        _lookUntil = Time.time + 2.8f;
                    }
                    if (Time.time < _lookUntil) rearTarget = 0.5f;
                }
            }
            if (Time.time < _cheerUntil) rearTarget = Mathf.Max(rearTarget, 0.55f);
            if (Time.time < _lookAtUntil && !wantsMove)
            {
                // 見つけたいきもののほうへ顔を向ける
                rearTarget = Mathf.Max(rearTarget, 0.45f);
                Vector3 fwd = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _tail.normal, Heading);
                Vector3 to = ShakuMath.ProjectOnPlaneSafe(_lookAtPoint - _tail.point, _tail.normal, fwd);
                _standLookTarget = Mathf.Clamp(Vector3.SignedAngle(fwd, to, _tail.normal) * Mathf.Deg2Rad, -StandLookMax, StandLookMax);
                _lastLookInput = Time.time;
            }
            _rear = Mathf.MoveTowards(_rear, rearTarget, dt * (rearTarget > _rear ? 2.2f : 3.5f));
            if (!standing && _rear < 0.3f && Time.time >= _lookAtUntil) _standLookTarget = 0f;
            _wasStanding = standing;
        }

        /// <summary>スティックを少しだけ倒したときは、ゆっくり歩く（最大 1.7 倍の時間）。</summary>
        float SlowFactor => Mathf.Lerp(1.7f, 1f, Mathf.InverseLerp(0.25f, 0.85f, _moveMag));

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
        void StartPull(bool sprint)
        {
            Vector3 hf = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, _head.normal, Heading);
            SurfacePoint tgt;
            if (!SurfaceProbe.Walk(_head, -hf, loopRatio * L, out tgt, out _))
            {
                Vector3 mid = Vector3.Lerp(_tail.point, _head.point, 0.6f);
                if (!SurfaceProbe.Snap(mid, (_tail.normal + _head.normal).normalized, 0.6f, out tgt))
                {
                    StartReach(Heading, false, sprint);
                    return;
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
            _dur = pullTime * (sprint ? sprintTimeScale : 1f) * SlowFactor;
            State = Mode.Pull;
        }

        void UpdatePull(float dt, Vector3 desired, bool wantsMove, bool sprint)
        {
            _rear = Mathf.MoveTowards(_rear, 0f, dt * 4f);
            _t += dt / _dur;
            float e = ShakuMath.Smoother01(_t);
            Vector3 liftN = (_from.normal + _to.normal).normalized;
            Vector3 p = Vector3.Lerp(_from.point, _to.point, e) + liftN * Mathf.Sin(Mathf.PI * e) * 0.05f * L;
            _tail = new SurfacePoint(p, Vector3.Slerp(_from.normal, _to.normal, e).normalized);
            if (_t >= 1f)
            {
                _tail = _to;
                Stepped?.Invoke(_tail.point, false);
                if (wantsMove) StartReach(desired, false, sprint);
                else StartReach(Heading, true, sprint);
            }
        }

        // ---- 伸び ----
        bool StartReach(Vector3 desired, bool settle, bool sprint)
        {
            Vector3 n = _tail.normal;
            Vector3 heading = ShakuMath.ProjectOnPlaneSafe(_head.point - _tail.point, n, ShakuMath.ProjectOnPlaneSafe(Heading, n, ShakuMath.AnyPerpendicular(n)));
            if (Vector3.Distance(_head.point, _tail.point) < 0.05f)
                heading = ShakuMath.ProjectOnPlaneSafe(Heading, n, ShakuMath.AnyPerpendicular(n));
            Vector3 want = desired.sqrMagnitude > 0.01f ? ShakuMath.ProjectOnPlaneSafe(desired, n, heading) : heading;
            float ang = Vector3.SignedAngle(heading, want, n);
            float turn = Mathf.Clamp(ang, -maxTurnDegrees, maxTurnDegrees);
            float dist = (settle ? restRatio : extendRatio) * L * (Mathf.Abs(turn) > 45f ? 0.85f : 1f);

            float[] turnTry = { turn, turn * 0.5f, turn + Mathf.Sign(turn + 0.01f) * 25f, 0f };
            float[] distTry = { 1f, 0.7f, 0.45f };
            bool firstTry = true;
            string firstFail = "";
            foreach (float tr in turnTry)
            {
                Vector3 go = Quaternion.AngleAxis(tr, n) * heading;
                foreach (float k in distTry)
                {
                    bool ok = SurfaceProbe.Walk(_tail, go, dist * k, out var tgt, out _, out bool offEdge);
                    if (!ok && offEdge && firstTry && !settle && desired.sqrMagnitude > 0.01f
                        && IsRealDrop(SurfaceProbe.LastStopPoint, SurfaceProbe.LastStopDir))
                    {
                        // 急ながけのふちで、そのまま前へ進んだ：体をのり出して落ちる
                        WalkOff(SurfaceProbe.LastStopPoint, go);
                        return false;
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
                        _from = _head;
                        _to = tgt;
                        _t = 0f;
                        _dur = reachTime * (sprint ? sprintTimeScale : 1f) * (settle ? 1.15f : 1f) * (settle ? 1f : SlowFactor);
                        _settling = settle;
                        _reachLift = settle ? 0.12f : 0.24f;
                        State = Mode.Reach;
                        IsBlocked = false;
                        return true;
                    }
                }
                if (settle) break;
            }
            IsBlocked = !settle;
            if (!settle) BlockReason = firstFail;
            State = Mode.Idle;
            return false;
        }

        void UpdateReach(float dt, Vector3 desired, bool wantsMove, bool sprint)
        {
            _rear = Mathf.MoveTowards(_rear, 0f, dt * 4f);
            _t += dt / _dur;
            float e = ShakuMath.Smoother01(_t);
            Vector3 liftN = (_from.normal + _to.normal).normalized;
            float lift = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(e)), 0.75f) * _reachLift * L;
            Vector3 p = Vector3.Lerp(_from.point, _to.point, e) + liftN * lift;
            _head = new SurfacePoint(p, Vector3.Slerp(_from.normal, _to.normal, e).normalized);
            if (_t >= 1f)
            {
                _head = _to;
                Stepped?.Invoke(_head.point, true);
                MarkSafe();
                if (wantsMove && !_settling) StartPull(sprint);
                else if (wantsMove) StartPull(sprint);
                else State = Mode.Idle;
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

            float tailAngle = PlaneAngle(ShakuMath.ProjectOnPlaneSafe(X, _tail.normal, X), X, U);
            float headPlanted = PlaneAngle(ShakuMath.ProjectOnPlaneSafe(X, _head.normal, X), X, U);
            float headAngle = headPlanted;
            if (State == Mode.Reach)
            {
                float e = Mathf.Clamp01(_t);
                // 伸びている途中は頭を少し上げて前を見る → 最後に下ろす
                headAngle = Mathf.Lerp(0.45f, headPlanted, ShakuMath.Smooth01((e - 0.35f) / 0.65f));
            }
            tailAngle = Mathf.Clamp(tailAngle, -1.4f, 1.4f);
            headAngle = Mathf.Clamp(headAngle, -1.4f, 1.4f);

            float breathe = State == Mode.Idle ? (Mathf.Sin(_swayPhase * 2.1f) * 0.5f + 0.5f) * 0.012f : 0f;
            // 落ちて着地したあとの、ぽよんと弾む動き
            if (_landBounce > 0f) breathe += _landBounce * _landBounce * 0.14f * Mathf.Abs(Mathf.Cos((1f - _landBounce) * 14f));
            float sway = State == Mode.Reach ? Mathf.Sin(_swayPhase * 7f) * 0.12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t)) : Mathf.Sin(_swayPhase * 1.3f) * 0.04f;
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
                float swayAngle = _standLook + Mathf.Sin(_swayPhase * 1.7f) * (steering ? 0.1f : 0.45f) * rise;
                // BuildRear の横向きは SignedAngle と逆まわりなので、符号を反転して渡す
                _pose.BuildRear(tc, X, _tail.normal, L, rise, -swayAngle, 0.5f + 0.2f * Mathf.Sin(_swayPhase * 2.3f));
                _target.Blend(_target, _pose, ShakuMath.Smooth01(rise));
            }
        }

        static float PlaneAngle(Vector3 v, Vector3 X, Vector3 U)
        {
            return Mathf.Atan2(Vector3.Dot(v, U), Vector3.Dot(v, X));
        }

        readonly Vector3[] _push = new Vector3[Samples];

        /// <summary>でこぼこにめり込まないよう、体の各点を表面の外へ押し出す。</summary>
        void ConformToSurface(BodyCurve c)
        {
            int n = c.Count;
            for (int k = 0; k < n; k++) _push[k] = Vector3.zero;
            float minClear = Rad * 0.85f;
            for (int k = 2; k < n - 2; k += 2)
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
            // 隣どうしでならす
            for (int k = 1; k < n - 1; k += 2)
                _push[k] = (_push[k - 1] + _push[k + 1]) * 0.5f;
            for (int k = 1; k < n - 1; k++)
                c.pos[k] += _push[k];
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
            State = Mode.Hang;
            BeginTransition(0.45f);
            if (silk != null) silk.enabled = true;
            _silkFade = 1f;
            SilkStarted?.Invoke();
        }

        void UpdateHang(float dt, Vector3 desired, float mag, bool sprint)
        {
            _shotT = Mathf.Min(1f, _shotT + dt / 0.18f);
            if (_reeling)
            {
                // ねらって出した糸：自動でたぐって、ねらった場所へ。Space でやめて落ちる
                if (InputEnabled && GameInput.SilkPressed && _shotT >= 1f)
                {
                    _reeling = false;
                    StartFall(_hangVel, _anchorSurface);
                    return;
                }
                if (_shotT >= 1f) _silkLen -= silkReelSpeed * dt;
            }
            else
            {
                bool climb = InputEnabled && GameInput.SilkHeld && _blendT >= 1f;
                if (climb) _silkLen -= silkClimbSpeed * dt;
                else _silkLen += (sprint ? silkFastSpeed : silkDescendSpeed) * dt;
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
            // 重力・入力でこぐ力・風（糸にぶら下がると風にゆられる）
            _hangVel += (Physics.gravity * 0.6f + horiz * 4f + Wind.At(_hangPos) * 0.3f) * dt;
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
            MarkSafe();
            Landed?.Invoke();
        }

        void BuildHangTarget()
        {
            float curl = 0.55f + 0.25f * Mathf.Sin(_swayPhase * 1.3f);
            float wiggle = Mathf.Sin(_swayPhase * 2.2f) * 0.25f;
            _target.BuildHang(_hangPos, _hangFacing, L, curl, wiggle);
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
        bool IsRealDrop(Vector3 edge, Vector3 forward)
        {
            // 「がけ」と判定されたふち（上向きの面から 80 度より急に下りる）は、尾がどこにいてもよい。
            // それ以外（先に何もない）は、尾が平らな所にいるときだけ
            bool cliff = SurfaceProbe.LastFail == "cliff";
            if (!cliff && _tail.normal.y < 0.5f) return false;
            Vector3 f = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) return false;
            // ふち（探索が止まった所）の少し先の下に、1 体長ほど何もなければ本当のがけ
            Vector3 probe = edge + f.normalized * 0.35f + Vector3.up * 0.1f;
            if (!SurfaceProbe.Raycast(probe, Vector3.down, L * 0.9f + 0.1f, out var below)) return true;
            // がけのふちのすぐ下が急な面（切り株の外側のふくらんだ樹皮など）なら、そこもがけ
            return cliff && below.normal.y < 0.5f;
        }

        /// <summary>がけのふちから、前へのり出して落ちる。</summary>
        void WalkOff(Vector3 edge, Vector3 forward)
        {
            Vector3 f = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = forward;
            f.Normalize();
            StartFall(f * 1.6f + Vector3.up * 0.5f, SurfacePoint.On(edge, Vector3.up, null));
            // がけのふちから、体をのり出して落ちはじめる
            _fallPos = edge + f * 0.25f + Vector3.up * 0.12f;
        }

        /// <summary>壁や裏側から手をはなして落ちる。</summary>
        public void LetGo()
        {
            if (State == Mode.Hang || State == Mode.Fall) return;
            Vector3 n = (_tail.normal + _head.normal).normalized;
            if (n.sqrMagnitude < 0.5f) n = _head.normal;
            // 面から少しはなれるように押し出す
            StartFall(n * 1.1f + Vector3.up * 0.4f, _head);
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
                _fallPos += Vector3.down * 0.4f * dt;
                if (_sinkTimer > 0.9f) Spawn(_lastSafeTail, _lastSafeNormal, _lastSafeHead, _lastSafeNormal);
                return;
            }
            // 落ちながら Space で糸を出すと、はなれた場所にぶら下がれる
            if (InputEnabled && GameInput.SilkPressed && _fallTime > 0.08f && Vector3.Distance(_fallFrom.point, _fallPos) < silkRange)
            {
                CatchWithSilk();
                return;
            }
            // 重力・空気のてい抗・すこしだけ空中で向きを変えられる
            _fallVel += Vector3.down * fallGravity * dt;
            _fallVel += Vector3.ProjectOnPlane(desired, Vector3.up) * mag * 1.2f * dt;
            _fallVel *= 1f - Mathf.Clamp01(0.35f * dt);
            if (_fallVel.magnitude > fallTerminal) _fallVel = _fallVel.normalized * fallTerminal;
            _fallSpin += dt * (5f + _fallVel.magnitude * 0.6f);

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
                _fallVel = Vector3.Reflect(_fallVel, hn) * 0.3f;
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
            _hangFacing = f.normalized;
            Land(tail);
            BeginTransition(0.28f);
            _landBounce = Mathf.Clamp01(0.35f + speed * 0.12f);
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
            // くるんと丸まって、回りながら落ちる
            _target.BuildCurl(_fallPos, _fallAxis, _hangFacing, L, 4.3f, _fallSpin);
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

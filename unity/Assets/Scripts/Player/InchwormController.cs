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
        public enum Mode { Idle, Pull, Reach, Hang }

        [Header("References")]
        public InchwormBody body;
        public LineRenderer silk;
        public Transform cameraTransform;

        [Header("Gait")]
        [Range(0.6f, 1f)] public float extendRatio = 0.88f;
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

        public static InchwormController Instance { get; private set; }
        public Mode State { get; private set; } = Mode.Idle;
        public bool InputEnabled { get; set; } = true;
        public bool IsBlocked { get; private set; }
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

        public event Action<Vector3, bool> Stepped;   // 位置, 頭か
        public event Action SilkStarted;
        public event Action Landed;

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

        // 糸
        Vector3 _silkAnchor;
        SurfacePoint _anchorSurface;
        Vector3 _hangPos, _hangVel, _hangFacing;
        float _silkLen;
        float _silkFade;

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
        public void Spawn(Vector3 point, Vector3 forward)
        {
            Vector3 n = Vector3.up;
            if (SurfaceProbe.Snap(point + Vector3.up * 0.5f, Vector3.up, 3f, out var sp)) { point = sp.point; n = sp.normal; }
            _tail = new SurfacePoint(point, n);
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
            _tail = new SurfacePoint(tailPoint, tailNormal);
            _head = new SurfacePoint(headPoint, headNormal);
            State = Mode.Idle;
            _rear = 0f;
            _blendT = 1f;
            if (silk != null) silk.enabled = false;
            BuildGroundTarget(0f);
            _curve.CopyFrom(_target);
            body.Apply(_curve);
            MarkSafe();
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
        }

        // ------------------------------------------------------------------
        // 入力
        // ------------------------------------------------------------------
        Vector3 DesiredDirection(Vector3 n, out float magnitude)
        {
            Vector2 mv = InputEnabled ? GameInput.Move : Vector2.zero;
            magnitude = Mathf.Clamp01(mv.magnitude);
            if (magnitude < 0.01f || cameraTransform == null) return Vector3.zero;
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

            Vector3 avgN = (_tail.normal + _head.normal).normalized;
            if (avgN.sqrMagnitude < 0.5f) avgN = _tail.normal;
            Vector3 desired = DesiredDirection(State == Mode.Hang ? Vector3.up : _head.normal, out float mag);
            bool wantsMove = mag > 0.2f;
            bool sprint = InputEnabled && GameInput.Sprint;
            _standRequested = InputEnabled && GameInput.StandHeld;

            switch (State)
            {
                case Mode.Idle: UpdateIdle(dt, desired, wantsMove, sprint); break;
                case Mode.Pull: UpdatePull(dt, desired, wantsMove, sprint); break;
                case Mode.Reach: UpdateReach(dt, desired, wantsMove, sprint); break;
                case Mode.Hang: UpdateHang(dt, desired, mag, sprint); break;
            }

            if (State != Mode.Hang)
            {
                UpdateCanDrop();
                if (InputEnabled && GameInput.SilkPressed && CanDropSilk && !_standRequested) StartHang();
            }
            else CanDropSilk = false;

            if (State == Mode.Hang) BuildHangTarget();
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
            SurfaceUp = State == Mode.Hang ? Vector3.up : Vector3.Slerp(SurfaceUp, avgN, ShakuMath.DampFactor(6f, dt));
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
            if (wantsMove)
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
            _rear = Mathf.MoveTowards(_rear, rearTarget, dt * (rearTarget > _rear ? 2.2f : 3.5f));
        }

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
            _dur = pullTime * (sprint ? sprintTimeScale : 1f);
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
            foreach (float tr in turnTry)
            {
                Vector3 go = Quaternion.AngleAxis(tr, n) * heading;
                foreach (float k in distTry)
                {
                    if (SurfaceProbe.Walk(_tail, go, dist * k, out var tgt, out _))
                    {
                        if (Vector3.Distance(tgt.point, _tail.point) < 0.15f) continue;
                        _from = _head;
                        _to = tgt;
                        _t = 0f;
                        _dur = reachTime * (sprint ? sprintTimeScale : 1f) * (settle ? 1.15f : 1f);
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
            float sway = State == Mode.Reach ? Mathf.Sin(_swayPhase * 7f) * 0.12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t)) : Mathf.Sin(_swayPhase * 1.3f) * 0.04f;
            _target.BuildArch(tc, hc, U, L, tailAngle, headAngle, sway, breathe);
            ConformToSurface(_target);

            if (_rear > 0.001f)
            {
                float rise = Mathf.Clamp01(_rear);
                float look = 0f;
                if (_standRequested && cameraTransform != null)
                {
                    Vector3 camF = ShakuMath.ProjectOnPlaneSafe(cameraTransform.forward, _tail.normal, X);
                    look = Mathf.Clamp(Vector3.SignedAngle(ShakuMath.ProjectOnPlaneSafe(X, _tail.normal, X), camF, _tail.normal) * Mathf.Deg2Rad, -0.9f, 0.9f);
                }
                float swayAngle = look + Mathf.Sin(_swayPhase * 1.7f) * 0.45f * rise;
                _pose.BuildRear(tc, X, _tail.normal, L, rise, swayAngle, 0.5f + 0.2f * Mathf.Sin(_swayPhase * 2.3f));
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
                if (SurfaceProbe.GroundBelow(probe, 200f, out var g) && !ForestLayout.IsUnderwater(g.point))
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
            bool climb = InputEnabled && GameInput.SilkHeld && _blendT >= 1f;
            if (climb) _silkLen -= silkClimbSpeed * dt;
            else _silkLen += (sprint ? silkFastSpeed : silkDescendSpeed) * dt;

            if (_silkLen < 0.25f)
            {
                // 糸をたぐって元の場所へ戻る
                ReattachAt(_anchorSurface, _hangFacing);
                return;
            }

            // 振り子
            Vector3 horiz = Vector3.ProjectOnPlane(desired, Vector3.up) * mag;
            _hangVel += (Physics.gravity * 0.6f + horiz * 4f) * dt;
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
                if (wall.normal.y < 0.5f)
                {
                    _hangPos = prev + mv.normalized * Mathf.Max(0f, wall.distance - 0.01f);
                    _hangVel = Vector3.ProjectOnPlane(_hangVel, wall.normal) * 0.5f;
                }
            }
            if (horiz.sqrMagnitude > 0.01f) _hangFacing = Vector3.Slerp(_hangFacing, horiz.normalized, ShakuMath.DampFactor(2f, dt));

            // 着地：尾が地面にとどいたら
            if (_blendT >= 1f && SurfaceProbe.Raycast(_hangPos, Vector3.down, L * 0.95f, out var ground))
            {
                Vector3 gn = SurfaceProbe.SmoothNormal(ground);
                if (gn.y > 0.35f && !ForestLayout.IsUnderwater(ground.point)) Land(ground.point, gn);
                else if (ForestLayout.IsUnderwater(ground.point) && _silkLen > 0.5f)
                {
                    // 水には入れないので、それ以上は下りない
                    _silkLen = Mathf.Min(_silkLen, Vector3.Distance(_silkAnchor, _hangPos));
                }
            }
            // 水面より下には行かせない
            if (ForestLayout.IsUnderwater(_hangPos - Vector3.up * L * 0.9f))
                _silkLen = Mathf.Max(0.3f, _silkLen - (sprint ? silkFastSpeed : silkDescendSpeed) * dt);
        }

        void Land(Vector3 point, Vector3 normal)
        {
            var tail = new SurfacePoint(point, normal);
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
            _head = head;
            Vector3 back = -ShakuMath.ProjectOnPlaneSafe(facing, head.normal, ShakuMath.AnyPerpendicular(head.normal));
            if (SurfaceProbe.Walk(head, back, restRatio * L, out var t, out _)) _tail = t;
            else _tail = new SurfacePoint(head.point + back * 0.3f, head.normal);
            State = Mode.Idle;
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
                silk.SetPosition(0, _silkAnchor);
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
        // 安全装置
        // ------------------------------------------------------------------
        void SafetyCheck(float dt)
        {
            bool bad = float.IsNaN(_curve.Middle.x) || _curve.Middle.y < -40f || !float.IsFinite(_curve.Head.y);
            if (State == Mode.Hang && _silkLen > 200f) bad = true;
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

        /// <summary>カメラが見る位置。</summary>
        public Vector3 CameraFocus
        {
            get
            {
                if (State == Mode.Hang) return _hangPos - Vector3.up * 0.35f;
                Vector3 c = Vector3.Lerp(_curve.Middle, _curve.Head, 0.35f);
                return c + SurfaceUp * 0.12f + Vector3.up * 0.08f;
            }
        }
    }
}

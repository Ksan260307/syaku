using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 落ちる・水：背伸びボタンで体を広げて、ふわっと落ちる・強く落ちたあとは、ふらふら歩く・低い所からは弾まずに「ぽてっ」・
    /// 横へ流されて壁に近づいたら、しがみつく・水に落ちたら泳いで、岸や浮いた葉に上がる・葉や枝の下では、しずくがたれてきて、ぴくっ。
    /// </summary>
    public partial class InchwormController
    {
        bool _swimming, _gliding, _lowDrop;
        float _swimIdle, _swimPhase;
        float _wobbleUntil = -99f;
        float _dripNext = 25f;
        int _autoGrabs, _drips, _shoreClimbs;

        /// <summary>泳げる時間（スティックを倒していないと、もっと早く、水をすってしずむ）。</summary>
        public const float SwimTime = 8f, SwimIdleSink = 1.6f;
        /// <summary>泳ぐ力（速さ ≒ 力 / 水のてい抗 6）。</summary>
        public const float SwimPush = 4.5f;

        /// <summary>水の上で泳いでいる。</summary>
        public bool IsSwimming => _swimming;
        /// <summary>落ちている途中で、体を広げて、ふわっと落ちている（背伸びボタン）。</summary>
        public bool IsGliding => State == Mode.Fall && _gliding;
        /// <summary>強く落ちたあとで、ふらふら歩く。</summary>
        public bool IsWobbly => Time.time < _wobbleUntil;
        /// <summary>低い所から落ちている（着地で弾まずに、ぽてっ）。</summary>
        public bool IsLowDrop => State == Mode.Fall && _lowDrop;
        public int AutoGrabs => _autoGrabs;
        public int Drips => _drips;
        /// <summary>泳いで、岸や浮いた物に上がった回数。</summary>
        public int ShoreClimbs => _shoreClimbs;
        /// <summary>葉や枝から、しずくがたれてきて当たった（頭の位置）。</summary>
        public event System.Action<Vector3> Dripped;

        /// <summary>落ちはじめ：低い所から（すぐ下に地面）なら、着地で弾まずに「ぽてっ」。</summary>
        void BeginFallKind()
        {
            _gliding = false;
            _swimming = false;
            _swimIdle = 0f;
            _lowDrop = SurfaceProbe.Raycast(_fallPos, Vector3.down, 0.8f, out var g) && g.normal.y > 0.5f && !Areas.Current.IsUnderwater(g.point);
        }

        /// <summary>背伸びボタンを押しつづけると、体を広げて、ふわっと落ちる。</summary>
        void UpdateGlide(float dt)
        {
            _gliding = InputEnabled && _standRequested && _fallTime > 0.12f && !_lowDrop;
            if (_gliding)
            {
                _uncurl = Mathf.MoveTowards(_uncurl, 1f, dt * 5f);
                _spinL = Mathf.Lerp(_spinL, 1f, ShakuMath.DampFactor(3f, dt));
            }
        }

        /// <summary>
        /// スティックを倒していなくても、横へ流されて壁に近づいたら、体を伸ばしてしがみつく
        /// （落ちはじめた壁と、いきものはのぞく）。
        /// </summary>
        bool TryAutoGrab()
        {
            if (_fallTime < 0.3f || _moveMag > 0.3f || _fallVel.magnitude > 7f) return false;
            Vector3 flat = Vector3.ProjectOnPlane(_fallVel, Vector3.up);
            if (flat.magnitude < 0.5f) return false;
            if (!Physics.SphereCast(_fallPos, 0.12f, flat.normalized, out var grab, 0.25f, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore)) return false;
            if (grab.distance <= 1e-4f || grab.normal.y >= 0.5f || grab.normal.y <= -0.3f || !SurfaceProbe.Valid(grab.point)) return false;
            if (Vector3.Distance(grab.point, _fallFrom.point) < 1.2f || grab.collider.gameObject.layer == ShakuConst.CreatureLayer) return false;
            _autoGrabs++;
            _fallVel = Vector3.up * 0.1f;
            LandFromFall(SurfacePoint.On(grab.point, SurfaceProbe.SmoothNormal(grab), grab.collider));
            return true;
        }

        /// <summary>泳ぐ：スティックの向きへ、体をくねらせて進む。岸や浮いた葉に着いたら上がる。</summary>
        Vector3 SwimPaddle(float dt)
        {
            Vector3 dir = Vector3.ProjectOnPlane(_desiredNow, Vector3.up);
            bool paddling = InputEnabled && _moveMag > 0.2f && dir.sqrMagnitude > 1e-4f && _sinkTimer > 0.35f;
            if (_sinkTimer > 0.35f) _swimIdle = paddling ? 0f : _swimIdle + dt;
            bool was = _swimming;
            _swimming = _sinkTimer > 0.35f && _sinkTimer < SwimTime && _swimIdle < SwimIdleSink;
            if (_swimming && !was) BeginTransition(0.35f);
            _swimPhase += dt * (paddling ? 9f : 2.5f);
            if (!_swimming || !paddling) return Vector3.zero;
            dir.Normalize();
            _hangFacing = Vector3.Slerp(_hangFacing, dir, ShakuMath.DampFactor(4f, dt)).normalized;
            return dir * (SwimPush * Mathf.Clamp01(_moveMag));
        }

        /// <summary>泳いでいる先に、岸（水面と同じくらいの高さの地面）や浮いた葉があれば、そこへ上がる。</summary>
        bool TryClimbOut()
        {
            if (!_swimming || _moveMag < 0.2f) return false;
            Vector3 f = _hangFacing;
            for (int i = 0; i < 3; i++)
            {
                Vector3 d = Quaternion.AngleAxis((i - 1) * 35f, Vector3.up) * f;
                Vector3 probe = _fallPos + d * 0.4f + Vector3.up * 0.6f;
                if (!SurfaceProbe.Raycast(probe, Vector3.down, 1.0f, out var hit)) continue;
                if (hit.normal.y < 0.5f || hit.point.y < _waterLevel - 0.06f || hit.point.y > _waterLevel + 0.45f) continue;
                if (Areas.Current.IsUnderwater(hit.point + Vector3.up * 0.02f) || !SurfaceProbe.Valid(hit.point)) continue;
                _shoreClimbs++;
                _swimming = false;
                _sinkTimer = 0f;
                _fallVel = Vector3.zero;
                _wetUntil = Time.time + 2.5f;      // ぬれて、ぶるぶる
                _wetBodyUntil = Time.time + 7f;    // 体がかわくまで
                LandFromFall(SurfacePoint.On(hit.point, SurfaceProbe.SmoothNormal(hit), hit.collider));
                return true;
            }
            return false;
        }

        /// <summary>水をすって、しずみはじめる割合。</summary>
        float SwimSoak => Mathf.Clamp01(Mathf.Max(_sinkTimer - (SwimTime - 0.4f), _swimIdle - (SwimIdleSink - 0.4f)) / 0.3f);

        /// <summary>しずんで、安全な場所へもどる。</summary>
        bool SwimSunk => _sinkTimer > SwimTime + 0.4f || (_sinkTimer > 0.6f && _swimIdle > SwimIdleSink + 0.4f);

        /// <summary>泳いでいる体：水面にうかんで、くねくね。頭は少し上げる。</summary>
        void BuildSwimTarget()
        {
            Vector3 f = Vector3.ProjectOnPlane(_hangFacing, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 c = new Vector3(_fallPos.x, Mathf.Max(_fallPos.y, _waterLevel - Rad * 0.5f), _fallPos.z);
            Vector3 tc = c - f * (0.46f * L), hc = c + f * (0.46f * L);
            float sway = 0.35f * Mathf.Sin(_swimPhase) * (_swimIdle > 0f ? 0.4f : 1f);
            _target.BuildArch(tc, hc, Vector3.up, L, 0.05f, 0.35f, sway, 0f);
        }

        /// <summary>強く落ちたあとは、しばらくふらふら歩く。</summary>
        void BeginWobble(float impact)
        {
            if (impact > 0.45f) _wobbleUntil = Time.time + 1.5f + 2f * impact;
        }

        /// <summary>ふらふら歩き：体が左右にゆれる。</summary>
        float WobbleSway() => IsWobbly && IsMoving ? 0.22f * Mathf.Sin(_swayPhase * 4.5f) * Mathf.Clamp01(_wobbleUntil - Time.time) : 0f;

        /// <summary>葉や枝の下にいると、ときどき、しずくがたれてきて当たる：ぴくっとして、見上げる。</summary>
        void UpdateDrips()
        {
            if (Time.time < _dripNext || !InputEnabled || State == Mode.Hang || State == Mode.Fall) return;
            _dripNext = Time.time + Random.Range(18f, 34f);
            Vector3 h = _curve.Head;
            if (!Physics.Raycast(h + SurfaceUp * 0.15f, Vector3.up, 4f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return;
            Drip();
        }

        /// <summary>しずくが当たった。</summary>
        void Drip()
        {
            _drips++;
            _flinch = 1f;
            TriggerBlink(1.6f);
            _lookUpUntil = Time.time + 1.6f;
            Dripped?.Invoke(_curve.Head);
        }

        /// <summary>テスト用：しずくを落とす。</summary>
        public void DebugDrip() => Drip();
    }
}

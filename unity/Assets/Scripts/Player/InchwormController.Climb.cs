using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 登る・下りる：壁の一歩ごとの「ため」・疲れたら壁の途中で一休み・天井でたわむ・段を上る Ω・段を下りるときは頭から・
    /// とどかないすき間で迷う・高いがけでためらう・ひさしに手をかけようとする・細い物に巻きつく・進めないと首をふる。
    /// </summary>
    public partial class InchwormController
    {
        int _holdKind;              // 引き寄せる前のひと呼吸（1: 壁に頭をつけた 2: 壁を登る「ため」）
        float _wallRestUntil = -99f, _wallRestNext;
        float _stepDown, _tailDrop; // 段を下りる高さ（頭・尾）
        bool _teeter;               // 高いがけのふちで、ためらっている
        int _teeterCancels, _noShakes;
        float _peekSway;

        // （どちらも、引き寄せのはじめの体の動きだけ。時間はのばさない：壁や角での歩き方の速さは、もとのまま）
        /// <summary>壁に頭をつけて、体を起こしている（引き寄せのはじめ）。</summary>
        public bool IsWallPause => State == Mode.Pull && _t < 0.3f && _holdKind == 1;
        /// <summary>壁を登る一歩の「ため」（引き寄せのはじめ）。</summary>
        public bool IsGathering => State == Mode.Pull && _t < 0.25f && _holdKind == 2;
        /// <summary>疲れて、壁の途中で一休みしている。</summary>
        public bool IsWallResting => Time.time < _wallRestUntil;
        /// <summary>高いがけのふちで、落ちる前にためらっている。</summary>
        public bool IsTeetering => _teeter && _pending == PendingFall.WalkOff;
        /// <summary>ためらっている間に、スティックを引いて、がけから下がった回数。</summary>
        public int TeeterCancels => _teeterCancels;
        /// <summary>進めない所で、首をふって「むり」とした回数。</summary>
        public int NoShakes => _noShakes;

        /// <summary>テスト用：疲れぐあいを決める。</summary>
        public void DebugSetFatigue(float f) => _fatigue = Mathf.Clamp01(f);

        /// <summary>壁を上へ登る一歩：引き寄せる前に、体を壁におしつけて「ため」る。</summary>
        void BeginPullHold(Vector3 hf)
        {
            _holdKind = 0;
            // 頭も尾も、同じ壁にいるときだけ（床と壁の角・根のふくらみでは、ためない）
            bool wall = _tail.normal.y < 0.3f && _tail.normal.y > -0.35f && _head.normal.y < 0.3f && Vector3.Dot(_tail.normal, _head.normal) > 0.9f;
            if (!wall || Vector3.Dot(hf.normalized, Vector3.up) < 0.6f) return;
            _holdKind = 2;
        }

        /// <summary>壁を登っていて、とても疲れたら、壁の途中で一休みする（8 秒に一度まで）。</summary>
        bool ShouldWallRest()
        {
            if (_fatigue < 0.8f || Time.time < _wallRestNext || !OnSteepSurface || _tail.normal.y < -0.35f) return false;
            _wallRestUntil = Time.time + 1.6f;
            _wallRestNext = Time.time + 8f;
            return true;
        }

        /// <summary>
        /// 段を下りるとき：まず前へ出て、それから下へ（ななめに角をつっきらない）。
        /// drop が小さければ、まっすぐ。
        /// </summary>
        static Vector3 StepPath(Vector3 a, Vector3 b, float e, float tm, float drop, Vector3 n)
        {
            if (drop < 0.05f) return Vector3.LerpUnclamped(a, b, e);
            Vector3 d = b - a;
            Vector3 dn = n * Vector3.Dot(d, n);
            float eH = ShakuMath.Smoother01(tm * 1.35f);
            float eV = ShakuMath.Smoother01((tm - 0.25f) / 0.75f);
            return a + (d - dn) * eH + dn * eV;
        }

        /// <summary>
        /// 段を下りるとき、まず前へ出る通り道があいているか（前へ出た角の所が、岩などの中でない）。
        /// あいていなければ、ふつうにまっすぐ動く（角の向こうの物に、体をつっこまない）。
        /// </summary>
        bool StepCornerClear(Vector3 a, Vector3 b, Vector3 n)
        {
            Vector3 lift = n * (1.5f * Rad);
            Vector3 corner = a + Vector3.ProjectOnPlane(b - a, n) + lift;
            Vector3 from = a + lift, to = corner - from;
            if (Physics.CheckSphere(corner, Rad, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore)) return false;
            return to.sqrMagnitude < 1e-6f || !SurfaceProbe.Raycast(from, to.normalized, to.magnitude, out _);
        }

        /// <summary>前のふちの先、体の長さほど先に、同じくらいの高さの面がある（またぐには遠すぎるすき間）。</summary>
        bool FarSideAhead(Vector3 f)
        {
            if (_tail.normal.y < 0.6f || f.sqrMagnitude < 1e-4f) return false;
            f.Normalize();
            for (float k = 0.7f; k <= 1.65f; k += 0.15f)
            {
                Vector3 above = _head.point + f * (k * L) + Vector3.up * 0.5f;
                if (SurfaceProbe.Raycast(above, Vector3.down, 1.0f, out var hit) && hit.normal.y > 0.6f
                    && Mathf.Abs(hit.point.y - _tail.point.y) < 0.35f) return true;
            }
            return false;
        }

        /// <summary>高いがけ（1.5 より高い）から落ちる前は、少しためらう（スティックを引けば、下がれる）。</summary>
        float TeeterTime(Vector3 edge, Vector3 f)
        {
            _teeter = false;
            if (!SurfaceProbe.Raycast(edge + f * 0.3f + Vector3.up * 0.1f, Vector3.down, 12f, out var below) || below.distance > 1.5f)
            {
                _teeter = true;
                return 0.28f;
            }
            return 0f;
        }

        /// <summary>ためらっている間に、スティックを後ろへ引いたら、落ちずに下がる。</summary>
        bool TeeterBackOut()
        {
            if (!IsTeetering || _moveMag < 0.3f || Vector3.Dot(_desiredNow, _pendingDir) > -0.3f) return false;
            _pending = PendingFall.None;
            _teeter = false;
            _teeterCancels++;
            Recoil(0.12f);
            return true;
        }

        /// <summary>進めないまま押しつづけると、首を左右にふって「むり」。</summary>
        void ShakeNo()
        {
            _peekKind = "no";
            _peekDur = 0.8f;
            _peekStrength = 1f;
            _peekT = 0f;
            _noShakes++;
        }

        /// <summary>天井では、体が重さでたわむ（ゆっくり、ゆらゆら）。</summary>
        float CeilingSag() => 0.012f + 0.005f * Mathf.Sin(_swayPhase * 0.9f);

        /// <summary>細い物（茎・ひも）を登るときは、体を左右交互に巻きつけるようにくねらせる。</summary>
        float CoilSway(bool wall, float tNow)
        {
            if (!_narrow || !wall || !IsMoving) return 0f;
            return 0.22f * (_stepStreak % 2 == 0 ? 1f : -1f) * Mathf.Sin(Mathf.PI * tNow);
        }

        /// <summary>壁で一休み・登る「ため」の体の高さ。</summary>
        float ClimbBreathe()
        {
            float b = 0f;
            if (IsGathering) b -= 0.015f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t / 0.25f));
            if (IsWallResting) b -= 0.01f;
            return b;
        }
    }
}

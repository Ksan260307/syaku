using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 操作の手ざわり：入力の向きが大きく変わったら、次の一歩ですぐに頭をふり向ける・壁ではスティックの上で登る（設定）・
    /// 天井では左右をカメラに合わせる・ねらっているときは身がまえて、ぷるぷる・糸をのぼる音と、切る音。
    /// </summary>
    public partial class InchwormController
    {
        int _pivots;
        float _silkTickAt;

        /// <summary>伸びたあと、入力の向きが大きくちがったので、尾を動かさずに頭をふり向けた回数。</summary>
        public int QuickPivots => _pivots;
        /// <summary>糸をのぼるとき、たぐるたびに（小さな音）。</summary>
        public event System.Action SilkClimbTick;
        /// <summary>糸を切った（背伸びボタン・たぐるのをやめる）。</summary>
        public event System.Action SilkCut;

        /// <summary>
        /// 頭も尾も、同じ平らな面の上にいる。床と壁にまたがっているときは、入力の向きを体の面へ写すと向きがくずれるので、
        /// その場でふり向く動きはしない（ふつうに引き寄せる）。
        /// </summary>
        bool FlatFooting => _tail.normal.y >= 0.5f && _head.normal.y >= 0.5f && Vector3.Dot(_tail.normal, _head.normal) >= 0.9f;

        /// <summary>伸びたあと：入力の向きが体の向きから 70 度より大きくちがう（頭も尾も、同じ平らな面の上のときだけ）。</summary>
        bool WantsPivot(Vector3 desired)
        {
            if (!FlatFooting || desired.sqrMagnitude < 0.01f) return false;
            Vector3 hf = BodyDir(_head.point - _tail.point, _tail.normal, Heading);
            return Vector3.Angle(hf, OnTailPlane(desired, hf)) > 70f;
        }

        /// <summary>設定「壁では、スティックの上で登る」：壁の上では、スティックの上 = 上、左右はカメラに合わせる。</summary>
        bool WallStickUp(Vector3 n, Vector2 mv, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!SaveSystem.Settings.wallStickUp || n.y >= 0.5f || n.y <= -0.2f || State == Mode.Hang || State == Mode.Fall) return false;
            Vector3 upw = Vector3.ProjectOnPlane(Vector3.up, n);
            if (upw.sqrMagnitude < 1e-4f) return false;
            upw.Normalize();
            Vector3 rw = Vector3.ProjectOnPlane(ShakuMath.ProjectOnPlaneSafe(cameraTransform.right, n, Vector3.Cross(n, upw)), upw);
            if (rw.sqrMagnitude < 1e-4f) rw = Vector3.Cross(n, upw);
            Vector3 d = upw * mv.y + rw.normalized * mv.x;
            if (d.sqrMagnitude > 1e-6f) dir = d.normalized;
            return true;
        }

        /// <summary>天井の左右：前後は体の向きのまま、左右だけはカメラから見た左右にする（左右が逆に感じないように）。</summary>
        Vector3 CeilingSide(Vector3 n, Vector3 hf, Vector3 hr)
        {
            Vector3 side = Vector3.ProjectOnPlane(ShakuMath.ProjectOnPlaneSafe(cameraTransform.right, n, -hr), hf);
            return side.sqrMagnitude > 1e-4f ? side.normalized : -hr;
        }

        /// <summary>ねらっているときは、体を低くして身がまえる。</summary>
        float AimBreathe() => IsAiming && State == Mode.Idle ? -0.012f : 0f;

        /// <summary>ねらいつづけると、力が入って、ぷるぷる。</summary>
        float AimTremble() => IsAiming && State == Mode.Idle ? 0.025f * Mathf.Sin(_swayPhase * 38f) * Mathf.Clamp01((Time.time - _aimStart - 0.4f) * 2f) : 0f;

        /// <summary>糸をのぼっている間、たぐるたびに小さな音。</summary>
        void UpdateSilkTicks()
        {
            if (State != Mode.Hang || _reeling || _silkSpeed > -0.1f) return;
            if (Time.time < _silkTickAt) return;
            _silkTickAt = Time.time + Mathf.Lerp(0.3f, 0.16f, Mathf.Clamp01(-_silkSpeed / Mathf.Max(0.01f, silkClimbSpeed * 1.6f)));
            SilkClimbTick?.Invoke();
        }
    }
}

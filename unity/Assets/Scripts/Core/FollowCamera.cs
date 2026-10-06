using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしを追う三人称カメラ。ドラッグ／右スティックで回転、ホイールでズーム。
    /// 障害物にめり込まないよう手前に寄り、しばらく操作しないと進行方向の後ろへゆっくり回り込む。
    /// タイトル画面ではゆっくり周回する。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FollowCamera : MonoBehaviour
    {
        public InchwormController target;
        public float distance = 3.6f;
        public float minDistance = 1.3f;
        public float maxDistance = 9f;
        public float pitch = 20f;
        public float yaw;
        public float autoFollowDelay = 2.2f;
        public bool titleMode = true;

        Vector3 _focus;
        Vector3 _focusVel;
        float _currentDist;
        float _sinceManual = 10f;
        bool _init;
        Camera _cam;
        float _aimBlend;
        float _shake;

        /// <summary>着地などで、カメラを少しゆらす（「画面のゆれをへらす」設定なら弱く）。</summary>
        public void Shake(float amount)
        {
            float k = SaveSystem.Settings.reduceMotion ? 0.2f : 1f;
            _shake = Mathf.Max(_shake, Mathf.Clamp01(amount) * k);
        }

        /// <summary>カメラをしゃくとりむしの後ろへ戻す。</summary>
        public void Recenter()
        {
            if (target == null) return;
            Vector3 h = Vector3.ProjectOnPlane(target.Heading, Vector3.up);
            if (h.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
            pitch = 20f;
            _sinceManual = 0f;
        }

        void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            _focus = target.CameraFocus;
            _currentDist = distance;
            Vector3 h = Vector3.ProjectOnPlane(target.Heading, Vector3.up);
            if (h.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
            _init = true;
            Apply(0f, true);
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (!_init) SnapToTarget();

            if (titleMode)
            {
                yaw += dt * 6f;
                pitch = Mathf.Lerp(pitch, 12f, ShakuMath.DampFactor(1.5f, dt));
                distance = Mathf.Lerp(distance, 3.2f, ShakuMath.DampFactor(1.5f, dt));
            }
            else if (Time.timeScale > 0f)
            {
                if (GameInput.RecenterPressed) Recenter();
                Vector2 look = GameInput.Look;
                if (look.sqrMagnitude > 0.0001f)
                {
                    yaw += look.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - look.y * 0.2f, -35f, 80f);
                    _sinceManual = 0f;
                }
                else _sinceManual += dt;

                float z = GameInput.Zoom;
                if (Mathf.Abs(z) > 0.001f)
                {
                    distance = Mathf.Clamp(distance * (1f - z * 0.12f), minDistance, maxDistance);
                    // カメラの距離は覚えておく（次に遊ぶときも同じ距離）
                    SaveSystem.Settings.cameraDistance = distance;
                    SaveSystem.SaveSettingsSoon();
                }
                // 落ちているときは、少し下（落ちる先）を見る
                if (target.IsFalling) pitch = Mathf.Lerp(pitch, 42f, ShakuMath.DampFactor(3f, dt));

                // 歩いているときは、しばらくすると後ろへ回り込む
                // （こちらへ向かって歩いているときに回り込むと、ぐるぐる回り続けてしまうので行わない）
                if (SaveSystem.Settings.autoCamera && _sinceManual > autoFollowDelay && target.IsMoving && target.SurfaceUp.y > 0.6f)
                {
                    Vector3 h = Vector3.ProjectOnPlane(target.CameraHeading, Vector3.up);
                    Vector3 camFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    bool towardCamera = Vector3.Dot(camFlat.normalized, h.normalized) < -0.25f;
                    // 手前へ入力しているあいだも回り込まない（障害物でそれたときに回り続けないように）
                    bool pullingBack = GameInput.Move.y < -0.3f;
                    if (h.sqrMagnitude > 0.01f && !towardCamera && !pullingBack)
                    {
                        float want = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
                        yaw = Mathf.LerpAngle(yaw, want, ShakuMath.DampFactor(0.6f, dt));
                    }
                }
                // 背伸びしたら少し見上げる
                if (target.IsStanding) pitch = Mathf.Lerp(pitch, Mathf.Min(pitch, 6f), ShakuMath.DampFactor(1.5f, dt));
            }
            _aimBlend = Mathf.MoveTowards(_aimBlend, !titleMode && target.IsAiming ? 1f : 0f, dt * 4f);
            if (_cam != null)
            {
                float fov = Mathf.Clamp(SaveSystem.Settings.fov, 40f, 70f) - 6f * _aimBlend;
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, fov, ShakuMath.DampFactor(8f, dt));
            }
            Apply(dt, false);
            if (_shake > 0.001f)
            {
                float t = Time.unscaledTime * 38f;
                transform.position += transform.rotation * new Vector3(Mathf.Sin(t) * 0.04f, Mathf.Sin(t * 1.3f + 1f) * 0.05f, 0f) * _shake;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.5f);
            }
        }

        void Apply(float dt, bool snap)
        {
            Vector3 want = target.CameraFocus;
            // 糸をねらうときは、肩ごしに少し寄る
            if (_aimBlend > 0f) want += Quaternion.Euler(0f, yaw, 0f) * Vector3.right * (0.35f * _aimBlend) + Vector3.up * (0.2f * _aimBlend);
            _focus = snap ? want : Vector3.SmoothDamp(_focus, want, ref _focusVel, 0.12f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rot * Vector3.back;

            float baseDist = Mathf.Lerp(distance, Mathf.Min(distance, 2.2f), _aimBlend);
            float d = baseDist;
            if (Physics.SphereCast(_focus, 0.1f, back, out var hit, baseDist, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                d = Mathf.Max(0.35f, hit.distance - 0.05f);
            if (snap) _currentDist = d;
            else _currentDist = d < _currentDist ? Mathf.Lerp(_currentDist, d, ShakuMath.DampFactor(25f, dt)) : Mathf.Lerp(_currentDist, d, ShakuMath.DampFactor(3f, dt));

            Vector3 pos = _focus + back * _currentDist;
            // 地面より下には行かない
            float ground = Areas.Current.Height(pos.x, pos.z) + 0.12f;
            if (pos.y < ground) pos.y = ground;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_focus - pos, Vector3.up));
        }
    }
}

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
                Vector2 look = GameInput.Look;
                if (look.sqrMagnitude > 0.0001f)
                {
                    yaw += look.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - look.y * 0.2f, -35f, 80f);
                    _sinceManual = 0f;
                }
                else _sinceManual += dt;

                float z = GameInput.Zoom;
                if (Mathf.Abs(z) > 0.001f) distance = Mathf.Clamp(distance * (1f - z * 0.12f), minDistance, maxDistance);

                // 歩いているときは、しばらくすると後ろへ回り込む
                // （こちらへ向かって歩いているときに回り込むと、ぐるぐる回り続けてしまうので行わない）
                if (_sinceManual > autoFollowDelay && target.IsMoving && target.SurfaceUp.y > 0.6f)
                {
                    Vector3 h = Vector3.ProjectOnPlane(target.Heading, Vector3.up);
                    Vector3 camFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    bool towardCamera = Vector3.Dot(camFlat.normalized, h.normalized) < -0.25f;
                    if (h.sqrMagnitude > 0.01f && !towardCamera)
                    {
                        float want = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
                        yaw = Mathf.LerpAngle(yaw, want, ShakuMath.DampFactor(0.6f, dt));
                    }
                }
                // 背伸びしたら少し見上げる
                if (target.IsStanding) pitch = Mathf.Lerp(pitch, Mathf.Min(pitch, 6f), ShakuMath.DampFactor(1.5f, dt));
            }
            Apply(dt, false);
        }

        void Apply(float dt, bool snap)
        {
            Vector3 want = target.CameraFocus;
            _focus = snap ? want : Vector3.SmoothDamp(_focus, want, ref _focusVel, 0.12f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rot * Vector3.back;

            float d = distance;
            if (Physics.SphereCast(_focus, 0.1f, back, out var hit, distance, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                d = Mathf.Max(0.35f, hit.distance - 0.05f);
            if (snap) _currentDist = d;
            else _currentDist = d < _currentDist ? Mathf.Lerp(_currentDist, d, ShakuMath.DampFactor(25f, dt)) : Mathf.Lerp(_currentDist, d, ShakuMath.DampFactor(3f, dt));

            Vector3 pos = _focus + back * _currentDist;
            // 地面より下には行かない
            float ground = ForestLayout.Height(pos.x, pos.z) + 0.12f;
            if (pos.y < ground) pos.y = ground;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_focus - pos, Vector3.up));
        }
    }
}

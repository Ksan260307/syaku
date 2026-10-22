using System;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// おまかせ歩き：決めた場所まで、しゃくとりむしが自分で歩いて行く（地図で見つけていない名所を 2 回タップ・タッチで地面をタップ）。
    /// スティックを倒す・落ちる・糸を出す・長く進めないと、やめる。
    /// </summary>
    public class AutoWalk : MonoBehaviour
    {
        public InchwormController worm;
        public Transform cameraTransform;

        Vector3 _target;
        float _radius, _blocked, _steep, _started;
        bool _active;

        /// <summary>これより長く歩いても着かなければ、あきらめる（秒）。</summary>
        public const float GiveUpTime = 90f;

        public bool Active => _active;
        public Vector3 Target => _target;
        /// <summary>おわった（true: 着いた / false: とちゅうでやめた・進めなかった）。</summary>
        public event Action<bool> Finished;
        /// <summary>進めなくて、あきらめた。</summary>
        public event Action GaveUp;

        public void Begin(Vector3 target, float radius)
        {
            _target = target;
            _radius = Mathf.Max(0.4f, radius);
            _active = true;
            _blocked = _steep = 0f;
            _started = Time.time;
        }

        public void Stop()
        {
            if (!_active) return;
            _active = false;
            GameInput.AutoMove = Vector2.zero;
        }

        void End(bool arrived)
        {
            Stop();
            Finished?.Invoke(arrived);
        }

        void Update()
        {
            if (!_active)
            {
                GameInput.AutoMove = Vector2.zero;
                return;
            }
            float dt = Time.deltaTime;
            if (worm == null || cameraTransform == null || !worm.InputEnabled) { End(false); return; }
            // 自分でスティックを倒したら、そちらにまかせる
            if (GameInput.PlayerMove.sqrMagnitude > 0.04f && Time.time - _started > 0.2f) { End(false); return; }
            if (worm.IsFalling || worm.State == InchwormController.Mode.Hang || worm.IsAiming) { End(false); return; }
            Vector3 d = _target - worm.HeadPosition;
            d.y = 0f;
            if (d.magnitude < _radius) { End(true); return; }
            // 長く進めない・壁を登りつづける・時間がかかりすぎたら、あきらめる
            _blocked = worm.IsBlocked ? _blocked + dt : 0f;
            _steep = worm.OnSteepSurface ? _steep + dt : 0f;
            if (_blocked > 3f || _steep > 5f || Time.time - _started > GiveUpTime)
            {
                End(false);
                GaveUp?.Invoke();
                return;
            }
            // 行き先の向きを、カメラから見たスティックの向きにする
            Vector3 cf = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (cf.sqrMagnitude < 1e-4f) cf = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
            cf.Normalize();
            Vector3 cr = Vector3.Cross(Vector3.up, cf);
            Vector3 dn = d.normalized;
            GameInput.AutoMove = new Vector2(Vector3.Dot(dn, cr), Vector3.Dot(dn, cf)).normalized * 0.9f;
        }
    }
}

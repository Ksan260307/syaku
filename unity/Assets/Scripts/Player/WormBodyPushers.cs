using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしの体の当たり判定（転がる物を押すためだけ）。体にそって細いカプセルをならべ、
    /// 体といっしょに動かす（動かした速さで、どんぐりなどを押して転がす）。
    /// しゃくとりむしが這う面をさがすときには使わない（専用のレイヤー）。
    /// </summary>
    public class WormBodyPushers : MonoBehaviour
    {
        public const int Segments = 5;

        InchwormController _worm;
        Transform _root;
        readonly Rigidbody[] _rbs = new Rigidbody[Segments];
        readonly CapsuleCollider[] _caps = new CapsuleCollider[Segments];

        void Start()
        {
            _worm = GetComponent<InchwormController>();
            _root = new GameObject("WormBodyPushers").transform;
            for (int i = 0; i < Segments; i++)
            {
                var go = new GameObject("WormBody_" + i) { layer = ShakuConst.WormBodyLayer };
                go.transform.SetParent(_root, false);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 2;
                cap.radius = ShakuConst.BodyRadius * 1.2f;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                _rbs[i] = rb;
                _caps[i] = cap;
            }
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }

        void FixedUpdate()
        {
            if (_worm == null || _worm.Curve == null) return;
            var c = _worm.Curve;
            int n = c.Count;
            for (int i = 0; i < Segments; i++)
            {
                Vector3 a = c.pos[i * (n - 1) / Segments];
                Vector3 b = c.pos[(i + 1) * (n - 1) / Segments];
                Vector3 d = b - a;
                float len = d.magnitude;
                Vector3 mid = (a + b) * 0.5f;
                Quaternion rot = len > 1e-4f ? Quaternion.LookRotation(d / len, Vector3.up) : Quaternion.identity;
                _caps[i].height = len + _caps[i].radius * 2f;
                var rb = _rbs[i];
                // ワープ（出現・救済）したときは、そのまま置く（とちゅうの物をはじき飛ばさない）
                if ((rb.position - mid).sqrMagnitude > 1f)
                {
                    rb.position = mid;
                    rb.rotation = rot;
                }
                else
                {
                    rb.MovePosition(mid);
                    rb.MoveRotation(rot);
                }
            }
        }
    }
}

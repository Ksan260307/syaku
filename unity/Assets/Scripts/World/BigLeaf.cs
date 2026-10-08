using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 地面に落ちている大きな葉。しゃくとりむしが登って乗れる足場で、本物と同じ重さ（グラム）がある。
    /// 重いので、しゃくとりむし（約 0.1g）には押せないが、強い突風がふくと、地面とのまさつに勝って少しずつすべり、
    /// 向きも変わる（風の力は、葉の面積と風の速さの 2 乗に比例。重さも面積に比例するので、どの葉も同じくらいの風で動く）。
    /// しゃくとりむしが乗っている葉は、足でしっかりつかんでいるので、すべらない（向かい風で後ろへ流されて進めなくならない）。
    /// しずくがのっている葉も、動かない（pinned）。
    /// しゃくとりむしより先に動かすため、実行順を早めている。
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class BigLeaf : MonoBehaviour
    {
        /// <summary>風が葉をおす力の強さ（グラム重 / (cm² × (単位/秒)²)）。</summary>
        public const float WindGrip = 0.012f;
        public const float StaticFriction = 0.8f, DynamicFriction = 0.6f;
        /// <summary>すべる速さの上限（単位/秒）。</summary>
        public const float MaxSpeed = 0.35f;
        /// <summary>これより遠くの葉は動かさない（だれにも見えないので、計算をはぶく）。</summary>
        public const float ActiveRadius = 30f;

        /// <summary>テスト用：風をこの値にする（null ならふつうの風）。</summary>
        public static Vector3? WindOverride;

        public float Mass { get; private set; }     // グラム
        public float AreaCm2 { get; private set; }  // 葉の面積（cm²）
        public bool Pinned;
        /// <summary>このフレームに動いた（絵を描きなおす）。LooseProps が見て、もどす。</summary>
        public bool Moved;
        public int Index { get; private set; }
        public Vector3 Velocity => _vel;

        AreaLayout _area;
        Collider _col;
        float _lift;
        float _radius;
        Vector3 _vel;
        float _spinSign = 1f;

        public static BigLeaf Create(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, float scale, AreaLayout area, int index)
        {
            var go = new GameObject("BigLeaf") { layer = ShakuConst.SurfaceLayer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = DetailMeshes.ForCollision(mesh);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;   // 動かすのは自分で（まさつと風の力）。足場の当たり判定は、葉の形そのまま
            var leaf = go.AddComponent<BigLeaf>();
            leaf._col = mc;
            Vector3 size = mesh.bounds.size * scale;
            leaf.Mass = LooseBody.MassOf(size, LooseProps.Shape.Leaf);
            leaf.AreaCm2 = Mathf.Max(size.x * size.z, Mathf.Max(size.x * size.y, size.y * size.z)) * 6.25f * 0.6f;
            leaf._area = area ?? Areas.Current;
            leaf._lift = pos.y - leaf._area.Height(pos.x, pos.z);
            leaf._radius = Mathf.Max(size.x, size.z) * 0.5f;
            leaf.Index = index;
            leaf._spinSign = (index % 2 == 0) ? 1f : -1f;
            return leaf;
        }

        void Update()
        {
            if (Pinned) return;
            var worm = InchwormController.Instance;
            Vector3 p = transform.position;
            if (worm == null || ((p - worm.CenterPosition).sqrMagnitude > ActiveRadius * ActiveRadius && _vel == Vector3.zero)) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            Vector3 w = WindOverride ?? Wind.At(p, Time.time, 0.15f);
            w.y = 0f;
            if (Holds(worm))
            {
                // 乗っている：足でつかんで、止める
                _vel = Vector3.zero;
                return;
            }
            float load = Mass;
            float g = -Physics.gravity.y;
            // 風が葉をおす力（グラム重）と、地面とのまさつ（グラム重）
            float push = WindGrip * AreaCm2 * w.sqrMagnitude;
            Vector3 wd = w.sqrMagnitude > 1e-8f ? w.normalized : Vector3.zero;
            float speed = _vel.magnitude;
            if (speed < 1e-4f)
            {
                if (push <= StaticFriction * load) { _vel = Vector3.zero; return; }   // 静止まさつに負けて、動かない
                _vel += wd * ((push - DynamicFriction * load) / load * g * dt);
            }
            else
            {
                Vector3 before = _vel;
                _vel += (wd * push - _vel / speed * DynamicFriction * load) / load * g * dt;
                if (Vector3.Dot(_vel, before) <= 0f) _vel = Vector3.zero;   // まさつで止まった
            }
            _vel = Vector3.ClampMagnitude(_vel, MaxSpeed);
            if (_vel.sqrMagnitude < 1e-8f) return;
            Vector3 next = p + _vel * dt;
            // 水の中・遊べる場所の外へは行かない。ほかの物に当たったら止まる
            if (_area.IsUnderwater(new Vector3(next.x, _area.Height(next.x, next.z), next.z)) || !_area.InPlayArea(next)
                || Blocked(p, _vel * dt))
            {
                _vel = Vector3.zero;
                return;
            }
            next.y = _area.Height(next.x, next.z) + _lift;
            // すべりながら、少しずつ向きも変わる
            Quaternion spin = Quaternion.AngleAxis(_spinSign * _vel.magnitude * 25f * dt, Vector3.up);
            transform.SetPositionAndRotation(next, spin * transform.rotation);
            Physics.SyncTransforms();
            Moved = true;
        }

        /// <summary>しゃくとりむしが、この葉の上にいるか（頭か尾の真下が、この葉）。</summary>
        public bool Holds(InchwormController worm)
        {
            if (worm == null || _col == null) return false;
            foreach (var p in new[] { worm.HeadPosition, worm.CenterPosition, worm.TailPoint })
                if (_col.Raycast(new Ray(p + Vector3.up * 0.4f, Vector3.down), out _, 1.2f)) return true;
            return false;
        }

        /// <summary>すべる先に、ほかの物（岩・キノコなど）があるか。</summary>
        bool Blocked(Vector3 p, Vector3 step)
        {
            float d = step.magnitude;
            if (d < 1e-6f) return false;
            var hits = Physics.RaycastAll(p + Vector3.up * 0.25f, step / d, d + _radius * 0.8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.collider.transform != transform && h.collider.GetComponent<BigLeaf>() == null && !h.collider.name.StartsWith("Terrain_"))
                    return true;
            return false;
        }
    }
}

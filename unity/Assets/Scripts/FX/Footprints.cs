using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 砂・雪の上に残る、しゃくとりむしの足あと（胸脚・腹脚の小さなくぼみが左右に 1 組ずつ）。
    /// しばらくすると、うすくなって消える。全部を 1 つのメッシュにまとめて描く。
    /// </summary>
    public class Footprints : MonoBehaviour
    {
        public const int Max = 64;
        public const float Life = 28f, FadeTime = 10f;
        const int Seg = 8;   // くぼみ 1 つの頂点（まわり）

        struct Print
        {
            public Vector3 pos, normal, side, fwd;
            public float born, size;
            public Color color;
        }

        readonly List<Print> _prints = new List<Print>();
        Mesh _mesh;
        MeshRenderer _mr;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();
        bool _dirty;
        float _nextFade;

        /// <summary>いま見えている足あとの数（テスト用）。</summary>
        public int Count => _prints.Count;

        public void Init(Material material)
        {
            if (_mesh != null || material == null) return;
            var go = new GameObject("Footprints");
            go.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "Footprints" };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = material;
            _mr.shadowCastingMode = ShadowCastingMode.Off;
            _mr.receiveShadows = false;
        }

        /// <summary>砂の足あとの色（こげ茶のかげ）と、雪の足あとの色（青みのかげ）。</summary>
        public static readonly Color SandColor = new Color(0.25f, 0.17f, 0.09f, 0.45f), SnowColor = new Color(0.42f, 0.5f, 0.62f, 0.5f);

        /// <summary>足あとを 1 組つける（head: 胸脚なら小さめ）。</summary>
        public void Add(Vector3 point, Vector3 normal, Vector3 forward, bool head, Color color)
        {
            if (_mesh == null) return;
            Vector3 f = Vector3.ProjectOnPlane(forward, normal);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (f.sqrMagnitude < 1e-6f) return;
            f.Normalize();
            if (_prints.Count >= Max) _prints.RemoveAt(0);
            _prints.Add(new Print
            {
                pos = point + normal * 0.004f,
                normal = normal.normalized,
                fwd = f,
                side = Vector3.Cross(normal, f).normalized,
                born = Time.time,
                size = head ? 0.75f : 1f,
                color = color,
            });
            _dirty = true;
        }

        public void Clear()
        {
            _prints.Clear();
            _dirty = true;
        }

        void LateUpdate()
        {
            if (_mesh == null) return;
            float now = Time.time;
            // 古い足あとは消す
            int dead = 0;
            while (dead < _prints.Count && now - _prints[dead].born > Life) dead++;
            if (dead > 0)
            {
                _prints.RemoveRange(0, dead);
                _dirty = true;
            }
            // うすくなっていく足あとがあれば、ときどき色をぬりなおす
            if (!_dirty && _prints.Count > 0 && now >= _nextFade && now - _prints[0].born > Life - FadeTime)
                _dirty = true;
            if (!_dirty) return;
            _dirty = false;
            _nextFade = now + 0.25f;
            Rebuild(now);
        }

        void Rebuild(float now)
        {
            _v.Clear();
            _c.Clear();
            _t.Clear();
            foreach (var p in _prints)
            {
                float a = Mathf.Clamp01((Life - (now - p.born)) / FadeTime);
                for (int s = -1; s <= 1; s += 2)
                {
                    // 左右の脚の、小さなだ円のくぼみ（まん中ほどこい）
                    Vector3 c = p.pos + p.side * (0.035f * s * p.size);
                    int c0 = _v.Count;
                    _v.Add(c);
                    _c.Add(new Color(p.color.r, p.color.g, p.color.b, p.color.a * a));
                    for (int i = 0; i < Seg; i++)
                    {
                        float ang = i * Mathf.PI * 2f / Seg;
                        _v.Add(c + p.fwd * (Mathf.Cos(ang) * 0.022f * p.size) + p.side * (Mathf.Sin(ang) * 0.014f * p.size));
                        _c.Add(new Color(p.color.r, p.color.g, p.color.b, 0f));
                    }
                    for (int i = 0; i < Seg; i++)
                    {
                        _t.Add(c0);
                        _t.Add(c0 + 1 + i);
                        _t.Add(c0 + 1 + (i + 1) % Seg);
                    }
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
            if (_mr != null) _mr.enabled = _prints.Count > 0;
        }
    }
}

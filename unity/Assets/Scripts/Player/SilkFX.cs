using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 糸の見た目の小さな効果：口もとのきらり・ねらった場所の光る点・使った糸がしばらく光って残る跡・
    /// とどかない所へ撃った糸が、たれ下がって落ちる「失敗」。
    /// </summary>
    public class SilkFX : MonoBehaviour
    {
        public const float TrailLife = 6f, MissLife = 1.1f;

        InchwormController _worm;
        Material _lineMat, _dotMat;
        float _width = 0.01f;

        // ねらった場所の光る点
        Transform _marker;
        MeshRenderer _markerMr;
        // 使った糸の跡（いくつか同時に残る）
        class Trail { public LineRenderer lr; public float born; }
        readonly List<Trail> _trails = new List<Trail>();
        // 失敗した糸
        LineRenderer _miss;
        float _missBorn = -99f;
        Vector3 _missFrom, _missDir;
        float _missLen;
        // きらり
        Transform _glint;
        MeshRenderer _glintMr;
        float _glintBorn = -99f;
        static Mesh s_quad;

        /// <summary>残っている糸の跡の数（テスト用）。</summary>
        public int TrailCount { get { int n = 0; foreach (var t in _trails) if (t.lr.enabled) n++; return n; } }
        public bool MarkerShown => _markerMr != null && _markerMr.enabled;
        public bool MissShown => _miss != null && _miss.enabled;
        public bool GlintShown => _glintMr != null && _glintMr.enabled;

        public void Init(InchwormController worm, LineRenderer silk)
        {
            _worm = worm;
            if (silk == null || silk.sharedMaterial == null) return;
            _lineMat = silk.sharedMaterial;
            _width = Mathf.Max(0.004f, silk.widthMultiplier * (silk.widthCurve != null && silk.widthCurve.length > 0 ? silk.widthCurve.Evaluate(0.5f) : 1f));
            // 丸い光（同じ光の描き方で、形だけ丸く）
            _dotMat = new Material(_lineMat) { name = "SilkDot" };
            _dotMat.SetFloat("_Shape", 0f);
            _dotMat.SetColor("_TintColor", new Color(1.3f, 1.4f, 1.2f, 1f));
            _marker = MakeQuad("SilkAimMarker", out _markerMr);
            _glint = MakeQuad("SilkGlint", out _glintMr);
            _miss = MakeLine("SilkMiss");
        }

        Transform MakeQuad(string name, out MeshRenderer mr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _dotMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            return go.transform;
        }

        LineRenderer MakeLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _lineMat;
            lr.useWorldSpace = true;
            lr.widthMultiplier = _width;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            lr.enabled = false;
            return lr;
        }

        static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "SilkFXQuad" };
            s_quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            s_quad.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            s_quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            s_quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            s_quad.RecalculateBounds();
            return s_quad;
        }

        /// <summary>糸を出す瞬間の、口もとの小さなきらり。</summary>
        public void Glint(Vector3 at)
        {
            if (_glint == null) return;
            _glintBorn = Time.time;
            _glint.position = at;
        }

        /// <summary>使った糸の形を、しばらく光る跡として残す。</summary>
        public void LeaveTrail(VerletRope rope)
        {
            if (_lineMat == null || rope == null || rope.Count < 2) return;
            Trail t = null;
            foreach (var x in _trails) if (!x.lr.enabled) { t = x; break; }
            if (t == null)
            {
                if (_trails.Count >= 4)
                {
                    // いちばん古い跡を使いまわす
                    t = _trails[0];
                    foreach (var x in _trails) if (x.born < t.born) t = x;
                }
                else
                {
                    t = new Trail { lr = MakeLine("SilkTrail") };
                    _trails.Add(t);
                }
            }
            t.born = Time.time;
            t.lr.positionCount = rope.Count;
            for (int i = 0; i < rope.Count; i++) t.lr.SetPosition(i, rope.pos[i]);
            t.lr.widthMultiplier = _width * 0.8f;
            t.lr.enabled = true;
        }

        /// <summary>とどかない所へ撃った糸：前へ少しのびて、たれ下がって落ちる。</summary>
        public void Miss(Vector3 from, Vector3 toward)
        {
            if (_miss == null) return;
            Vector3 d = toward - from;
            if (d.sqrMagnitude < 1e-4f) return;
            _missFrom = from;
            _missDir = d.normalized;
            _missLen = Mathf.Clamp(d.magnitude, 0.8f, 2.6f);
            _missBorn = Time.time;
            _miss.positionCount = 10;
            _miss.enabled = true;
        }

        void LateUpdate()
        {
            if (_worm == null) return;
            float now = Time.time;
            var cam = Camera.main;
            // ねらった場所の光る点（とどく所をねらっているとき）
            if (_markerMr != null)
            {
                bool show = _worm.IsAiming && _worm.AimValid;
                if (_markerMr.enabled != show) _markerMr.enabled = show;
                if (show)
                {
                    Vector3 n = _worm.AimNormal;
                    float pulse = 1f + 0.18f * Mathf.Sin(now * 9f);
                    _marker.SetPositionAndRotation(_worm.AimPoint + n * 0.015f, Quaternion.LookRotation(-n));
                    _marker.localScale = Vector3.one * (0.16f * pulse);
                }
            }
            // きらり
            if (_glintMr != null)
            {
                float g = (now - _glintBorn) / 0.35f;
                bool show = g >= 0f && g < 1f;
                if (_glintMr.enabled != show) _glintMr.enabled = show;
                if (show)
                {
                    if (cam != null) _glint.rotation = Quaternion.LookRotation(_glint.position - cam.transform.position);
                    _glint.localScale = Vector3.one * (0.09f * Mathf.Sin(Mathf.PI * g));
                }
            }
            // 糸の跡：しばらく光って、うすくなって消える
            foreach (var t in _trails)
            {
                if (!t.lr.enabled) continue;
                float age = now - t.born;
                if (age >= TrailLife)
                {
                    t.lr.enabled = false;
                    continue;
                }
                float a = 0.55f * Mathf.Clamp01((TrailLife - age) / 2.5f) * (0.85f + 0.15f * Mathf.Sin(now * 3f + t.born));
                var c = new Color(0.85f, 1f, 0.95f, a);
                t.lr.startColor = c;
                t.lr.endColor = c;
            }
            // 失敗した糸：前へのびてから、たれ下がって落ちる
            if (_miss != null && _miss.enabled)
            {
                float s = (now - _missBorn) / MissLife;
                if (s >= 1f) _miss.enabled = false;
                else
                {
                    float reach = _missLen * ShakuMath.Smooth01(s / 0.25f);
                    float sag = Mathf.Max(0f, s - 0.15f) * 1.6f;
                    for (int i = 0; i < 10; i++)
                    {
                        float u = i / 9f;
                        Vector3 p = _missFrom + _missDir * (reach * u) + Vector3.down * (sag * u * u * _missLen * 0.6f);
                        _miss.SetPosition(i, p);
                    }
                    var c = new Color(1f, 1f, 1f, 0.9f * (1f - ShakuMath.Smooth01((s - 0.5f) / 0.5f)));
                    _miss.startColor = c;
                    _miss.endColor = c;
                }
            }
        }
    }
}

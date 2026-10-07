using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 物理計算のきまり（重力・空気のてい抗・物の材質・ばね・転がり・糸）を一か所にまとめたもの。
    /// しゃくとりむし・いきもの・しずく・葉っぱの舟・カメラが同じ式と値を使う。
    /// </summary>
    public static class ShakuPhysics
    {
        /// <summary>小さな体の世界の重力（しゃくとりむしも、いきものも同じ）。</summary>
        public const float Gravity = 9f;
        /// <summary>1 回の計算でいちばん長い時間。フレームが長いときは分けて計算する（低いフレームレートでも安定）。</summary>
        public const float MaxSubstep = 1f / 120f;
        /// <summary>1 フレームとして計算する時間の上限（タブ切りかえ直後などに、すりぬけないように）。</summary>
        public const float MaxFrame = 0.1f;
        /// <summary>安全のための速さの上限（ふだんは空気のてい抗で自然に決まる）。</summary>
        public const float SafetySpeed = 20f;

        /// <summary>dt を何回に分けて計算するか（h は 1 回の時間）。</summary>
        public static int Substeps(float dt, out float h)
        {
            dt = Mathf.Clamp(dt, 0f, MaxFrame);
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / MaxSubstep - 1e-4f));
            h = dt / n;
            return n;
        }

        // ------------------------------------------------------------------
        // 空気
        // ------------------------------------------------------------------
        /// <summary>
        /// 空気のてい抗：体と空気（風）の速さの差に、1 次（ねばり）と 2 次（形）のてい抗がかかる。
        /// 半陰的に計算するので、てい抗が大きくても行き過ぎない。
        /// </summary>
        public static Vector3 ApplyDrag(Vector3 v, Vector3 air, float linear, float quadratic, float h)
        {
            Vector3 rel = v - air;
            float k = linear + quadratic * rel.magnitude;
            return air + rel / (1f + k * h);
        }

        /// <summary>重力とてい抗がつりあう速さ（最高速度）：g = a v + b v²。</summary>
        public static float TerminalSpeed(float linear, float quadratic)
        {
            if (quadratic < 1e-6f) return linear > 1e-6f ? Gravity / linear : float.PositiveInfinity;
            return (-linear + Mathf.Sqrt(linear * linear + 4f * quadratic * Gravity)) / (2f * quadratic);
        }

        // ------------------------------------------------------------------
        // 物の材質
        // ------------------------------------------------------------------
        public struct Material
        {
            public string name;
            public float restitution;   // はね返り（反発係数）
            public float friction;      // すべりにくさ（まさつ係数）
            public float softness;      // やわらかさ（衝撃をすいこむ）
        }

        public static readonly Material Ground = new Material { name = "ground", restitution = 0.15f, friction = 0.8f, softness = 0.3f };
        public static readonly Material Mushroom = new Material { name = "mushroom", restitution = 0.6f, friction = 0.5f, softness = 0.6f };
        public static readonly Material Leaf = new Material { name = "leaf", restitution = 0.1f, friction = 0.6f, softness = 0.85f };
        public static readonly Material Stone = new Material { name = "stone", restitution = 0.3f, friction = 0.45f, softness = 0f };
        public static readonly Material Wood = new Material { name = "wood", restitution = 0.22f, friction = 0.7f, softness = 0.15f };
        public static readonly Material Creature = new Material { name = "creature", restitution = 0.35f, friction = 0.6f, softness = 0.5f };

        public static Material MaterialOf(Collider c)
        {
            if (c == null) return Ground;
            if (c.gameObject.layer == ShakuConst.CreatureLayer) return Creature;
            return MaterialOf(c.name);
        }

        public static Material MaterialOf(string n)
        {
            if (string.IsNullOrEmpty(n)) return Ground;
            if (n.Contains("Mushroom")) return Mushroom;
            if (n.Contains("Leaf") || n.Contains("Lily") || n.Contains("Clover") || n.Contains("Moss")) return Leaf;
            if (n.Contains("Rock") || n.Contains("Stone") || n.Contains("Pebble")) return Stone;
            if (n.Contains("Trunk") || n.Contains("Stump") || n.Contains("Log") || n.Contains("Root") || n.Contains("Branch")
                || n.Contains("Twig") || n.Contains("Pine") || n.Contains("Acorn") || n.Contains("Bark")) return Wood;
            return Ground;
        }

        /// <summary>
        /// 面に当たったあとの速さ：面に垂直な速さは反発係数で返り、面に沿った速さはまさつで弱まる
        /// （ぶつかる勢いが強いほど、まさつも強くはたらく）。surfaceVel は動いている面（舟・いきもの）の速さ。
        /// </summary>
        public static Vector3 Bounce(Vector3 v, Vector3 n, Material m, Vector3 surfaceVel, out float normalSpeed)
        {
            Vector3 rel = v - surfaceVel;
            float vn = Vector3.Dot(rel, n);
            normalSpeed = Mathf.Max(0f, -vn);
            if (vn >= 0f) return v;   // もうはなれていく向きなら、何もしない
            Vector3 vt = rel - n * vn;
            float tLen = vt.magnitude;
            float loss = m.friction * normalSpeed;
            vt = tLen > loss ? vt * ((tLen - loss) / tLen) : Vector3.zero;
            return surfaceVel + vt + n * (normalSpeed * m.restitution);
        }

        /// <summary>まさつで止まるまでにすべる距離（v² / 2μg）。</summary>
        public static float SlideDistance(float speed, float friction) => speed * speed / (2f * Mathf.Max(0.05f, friction) * Gravity);

        /// <summary>衝撃の強さ（0〜1）：面に垂直な速さの 2 乗（エネルギー）と、面のやわらかさで決まる。</summary>
        public static float ImpactStrength(float normalSpeed, Material m) => Mathf.Clamp01(normalSpeed * normalSpeed / 64f * (1f - 0.6f * m.softness));

        // ------------------------------------------------------------------
        // ばね・転がり・糸
        // ------------------------------------------------------------------
        /// <summary>減衰つきのばね（omega: かたさ、zeta: 減衰比。1 未満だと少し行き過ぎてからおさまる）。</summary>
        public static void Spring(ref float x, ref float v, float target, float omega, float zeta, float h)
        {
            v += (-2f * zeta * omega * v - omega * omega * (x - target)) * h;
            x += v * h;
        }

        public static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float omega, float zeta, float h)
        {
            v += (-2f * zeta * omega * v - omega * omega * (x - target)) * h;
            x += v * h;
        }

        /// <summary>ばねを、長いフレームでも安定するように分けて計算する。</summary>
        public static void SpringSteps(ref float x, ref float v, float target, float omega, float zeta, float dt)
        {
            int n = Substeps(dt, out float h);
            for (int i = 0; i < n; i++) Spring(ref x, ref v, target, omega, zeta, h);
        }

        public static void SpringSteps(ref Vector3 x, ref Vector3 v, Vector3 target, float omega, float zeta, float dt)
        {
            int n = Substeps(dt, out float h);
            for (int i = 0; i < n; i++) Spring(ref x, ref v, target, omega, zeta, h);
        }

        /// <summary>中のつまった球が坂を転がる加速度（g sinθ × 5/7）。</summary>
        public static float RollingAccel(float slopeSin) => Gravity * slopeSin * 5f / 7f;

        /// <summary>静止まさつ：これより急な坂なら、止まっている球が転がりだす。</summary>
        public static bool StartsRolling(float slopeSin, float staticFriction) => slopeSin > staticFriction;

        /// <summary>
        /// 糸の力（引っぱるだけのばね＋ダンパー）。たるんでいるときは力がない。長い糸ほど、よくのびる。
        /// radial はつけ根から体への向き、rest は糸の長さ。
        /// </summary>
        public static Vector3 SilkForce(Vector3 radial, float len, float rest, Vector3 relVel, float stiffness, float damping)
        {
            float stretch = len - rest;
            if (stretch <= 0f) return Vector3.zero;
            float k = stiffness / Mathf.Max(0.5f, rest);
            float f = k * stretch + damping * Mathf.Max(0f, Vector3.Dot(relVel, radial));
            return -radial * f;
        }

        /// <summary>ふりこの糸を短く（長く）したとき：角運動量を保つように、横向きの速さを変える。</summary>
        public static Vector3 ConserveSwing(Vector3 vel, Vector3 radial, float oldLen, float newLen)
        {
            if (newLen < 1e-3f || oldLen < 1e-3f) return vel;
            Vector3 vr = radial * Vector3.Dot(vel, radial);
            return vr + (vel - vr) * Mathf.Clamp(oldLen / newLen, 0.5f, 2f);
        }

        /// <summary>動く物（舟・いきもの）の上の点の速さ（回転もふくむ）。</summary>
        public static Vector3 PointVelocity(Matrix4x4 prev, Matrix4x4 now, Vector3 localPoint, float dt)
            => dt > 1e-5f ? (now.MultiplyPoint3x4(localPoint) - prev.MultiplyPoint3x4(localPoint)) / dt : Vector3.zero;

        // ------------------------------------------------------------------
        // 数の安全
        // ------------------------------------------------------------------
        public static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        /// <summary>NaN や大きすぎる速さを防ぐ。</summary>
        public static Vector3 Sanitize(Vector3 v, float max) => IsFinite(v) ? Vector3.ClampMagnitude(v, max) : Vector3.zero;

        /// <summary>
        /// 球が物にめりこんでいたら、めりこみの深さと向きを計算して外へ押し出す（押し出したら true）。
        /// </summary>
        public static bool Depenetrate(ref Vector3 center, float radius, int mask)
        {
            var hits = Physics.OverlapSphere(center, radius, mask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return false;
            if (_probe == null)
            {
                // 計算用の球（形だけ使う）：遠くに置いた、ほかの物に当たらないトリガー
                var go = new GameObject("ShakuPhysicsProbe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                go.transform.position = new Vector3(0f, -10000f, 0f);
                _probe = go.AddComponent<SphereCollider>();
                _probe.isTrigger = true;
            }
            _probe.radius = radius;
            bool moved = false;
            foreach (var c in hits)
            {
                if (c is MeshCollider mc && !mc.convex) continue;   // 凸でないメッシュは計算できないので、ほかの方法にまかせる
                if (Physics.ComputePenetration(_probe, center, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out float dist))
                {
                    center += dir * (dist + 0.002f);
                    moved = true;
                }
            }
            return moved;
        }

        static SphereCollider _probe;
    }
}

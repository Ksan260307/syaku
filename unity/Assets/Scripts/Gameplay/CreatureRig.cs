using UnityEngine;

namespace Shakutori
{
    /// <summary>脚 1 本の付け方（右側の脚。左側は鏡うつしにして使う）。座標はいきもののローカル（前は -Z）。</summary>
    public class LegMount
    {
        public readonly string mesh;
        public readonly Vector3 hip;   // 付け根
        public readonly Vector3 tip;   // 足先（休んでいるとき）

        public LegMount(string mesh, Vector3 hip, Vector3 tip)
        {
            this.mesh = mesh;
            this.hip = hip;
            this.tip = tip;
        }
    }

    /// <summary>
    /// ひざ（ひじ）で曲がる手足 1 本の付け方（右側の手足。左側は鏡うつしにして使う）。座標はいきもののローカル（前は -Z）。
    /// 上の骨（付け根→ひざ）・下の骨（ひざ→足首）・足先（足首から先）の 3 つのメッシュで、原点はそれぞれ付け根・ひざ・足首。
    /// </summary>
    public class LimbMount
    {
        public readonly string upper, lower, foot;   // foot は null のこともある
        public readonly Vector3 hip, knee, ankle;     // 作ったときの付け根・ひざ・足首
        public readonly float upperLen, lowerLen;     // 前後と上下の面（x を見ない）での骨の長さ

        public LimbMount(string upper, string lower, string foot, Vector3 hip, Vector3 knee, Vector3 ankle)
        {
            this.upper = upper;
            this.lower = lower;
            this.foot = foot;
            this.hip = hip;
            this.knee = knee;
            this.ankle = ankle;
            upperLen = CreatureRig.YZ(knee - hip).magnitude;
            lowerLen = CreatureRig.YZ(ankle - knee).magnitude;
        }

        /// <summary>のばしきったときの長さ。</summary>
        public float Reach => upperLen + lowerLen;
    }

    /// <summary>歩き方。</summary>
    public enum GaitKind
    {
        None,
        Alternate,   // 交互に（6 本なら三脚歩行、8 本なら四脚ずつ）
        Row,         // 左右そろえて水をこぐ（アメンボの中足・後ろ足）
    }

    /// <summary>
    /// いきものの脚の付け根（CreatureRig.g.cs は Blender のスクリプトが書き出す）と、歩く脚の動き。
    /// </summary>
    public static partial class CreatureRig
    {
        static readonly LegMount[] None = new LegMount[0];

        public static LegMount[] Legs(string body)
        {
            return body != null && Generated.TryGetValue(body, out var legs) ? legs : None;
        }

        static readonly LimbMount[] NoLimbs = new LimbMount[0];

        /// <summary>ひざで曲がる手足（前から順に、右側だけ）。</summary>
        public static LimbMount[] Limbs(string body)
        {
            return body != null && GeneratedLimbs.TryGetValue(body, out var limbs) ? limbs : NoLimbs;
        }

        /// <summary>体と別に動く脚か手足がある。</summary>
        public static bool HasLegs(string body) => body != null && (Generated.ContainsKey(body) || GeneratedLimbs.ContainsKey(body));

        /// <summary>前後と上下の面（x を見ない）での位置。手足は、この面の中で曲がる。</summary>
        public static Vector2 YZ(Vector3 v) => new Vector2(v.z, v.y);

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static float SignedAngle(Vector2 a, Vector2 b) => Mathf.Atan2(Cross(a, b), Vector2.Dot(a, b));

        static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>
        /// 手足 1 本を、足首が target（体のローカル。右側の座標で、x は見ない）に来るように、ひざを曲げる（2 本の骨の IK）。
        /// 曲げるのは前後と上下の面の中（x 軸まわり）だけで、ひざの曲がる向きは作ったときと同じ（鳥のかかとはうしろ、けもののひざは前）。
        /// 届かないときは、のばしきって target の方を向く。footPitch は足先の角度（度。0 で作ったとき＝地面と平ら、+ で指先が下がる）。
        /// 行列は、それぞれのメッシュ（上の骨・下の骨・足先）を体のローカルに置くもの。mirror=true で左側。
        /// </summary>
        public static void SolveLimb(LimbMount l, Vector3 target, float footPitch, bool mirror, out Matrix4x4 upper, out Matrix4x4 lower, out Matrix4x4 foot)
        {
            Vector2 H = YZ(l.hip), K = YZ(l.knee), A = YZ(l.ankle), T = YZ(target);
            float a = Mathf.Max(1e-4f, l.upperLen), b = Mathf.Max(1e-4f, l.lowerLen);
            Vector2 D = T - H;
            float d = D.magnitude;
            Vector2 dir = d > 1e-6f ? D / d : (A - H).normalized;
            d = Mathf.Clamp(d, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.9995f);
            // ひざは、作ったときと同じ側へ曲げる
            float bend = Cross(A - H, K - H) >= 0f ? 1f : -1f;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector2 K2 = H + Rotate(dir, Mathf.Acos(cosA) * bend) * a;
            Vector2 T2 = H + dir * d;
            // 2 次元の (z, y) で左回り β は、Unity の x 軸まわり -β
            Quaternion r1 = Quaternion.AngleAxis(-SignedAngle(K - H, K2 - H) * Mathf.Rad2Deg, Vector3.right);
            Quaternion r2 = Quaternion.AngleAxis(-SignedAngle(A - K, T2 - K2) * Mathf.Rad2Deg, Vector3.right);
            Vector3 knee3 = l.hip + r1 * (l.knee - l.hip);
            Vector3 ankle3 = knee3 + r2 * (l.ankle - l.knee);
            Matrix4x4 S = mirror ? Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) : Matrix4x4.identity;
            upper = S * Matrix4x4.TRS(l.hip, r1, Vector3.one);
            lower = S * Matrix4x4.TRS(knee3, r2, Vector3.one);
            foot = S * Matrix4x4.TRS(ankle3, Quaternion.AngleAxis(-footPitch, Vector3.right), Vector3.one);
        }

        /// <summary>
        /// 歩くときの足首の行き先：地面についている間は、足を後ろへ送り（体が前へ進む分だけ）、持ち上げて前へもどす。
        /// phase は脚の位相（ラジアン。cos &gt; 0 のとき足が上がって前へ）、stride はひと足で進む長さ（体のローカル）、lift は上げる高さ。
        /// toes は、足を上げたときに指先がたれる角度。
        /// </summary>
        public static Vector3 StepTarget(LimbMount l, float phase, float stride, float lift, float toes, out float pitch)
        {
            float up = Mathf.Max(0f, Mathf.Cos(phase));
            pitch = toes * up;
            return l.ankle + new Vector3(0f, lift * up, -0.5f * stride * Mathf.Sin(phase));
        }

        /// <summary>
        /// 脚 1 本の行列（体のローカル）。mirror=true で左側。gait は歩く位相（ラジアン）、
        /// swing は前後のふり幅、lift は持ち上げる角度（度）。
        /// </summary>
        public static Matrix4x4 LegMatrix(LegMount leg, int index, bool mirror, float gait, float swing, float lift, GaitKind kind, int legCount)
        {
            float m = mirror ? -1f : 1f;
            Vector3 hip = new Vector3(leg.hip.x * m, leg.hip.y, leg.hip.z);
            float ph;
            if (kind == GaitKind.Row)
            {
                // 前足は休み、中足・後ろ足を左右そろえてこぐ
                ph = index == 0 ? 0f : gait + (index == 2 ? 0.6f : 0f);
                if (index == 0) { swing *= 0.2f; lift *= 0.2f; }
            }
            else
            {
                int group = (index + (mirror ? 1 : 0)) % 2;
                ph = gait + group * Mathf.PI;
            }
            float sw = kind == GaitKind.None ? 0f : swing * Mathf.Sin(ph);
            float li = kind == GaitKind.None ? 0f : lift * Mathf.Max(0f, Mathf.Cos(ph));
            // 元の脚は -X 側に伸びている。前（-Z）へ振るには Y まわりに -swing、持ち上げるには Z まわりに -lift
            float yaw = -sw * m;
            float roll = -li * m;
            return Matrix4x4.TRS(hip, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, roll), new Vector3(m, 1f, 1f));
        }

        /// <summary>
        /// 脚 1 本を、決まった姿勢にする（歩く動きとは別に、前へのばす・持ち上げる）。swing は前へふる角度、lift は持ち上げる角度（度）。
        /// </summary>
        public static Matrix4x4 LegPose(LegMount leg, bool mirror, float swing, float lift)
        {
            float m = mirror ? -1f : 1f;
            Vector3 hip = new Vector3(leg.hip.x * m, leg.hip.y, leg.hip.z);
            return Matrix4x4.TRS(hip, Quaternion.Euler(0f, -swing * m, 0f) * Quaternion.Euler(0f, 0f, -lift * m), new Vector3(m, 1f, 1f));
        }

        /// <summary>
        /// Blender の座標で書いた姿勢（位置・XYZ オイラー角（ラジアン）・拡大）を、Unity のローカル行列にする。
        /// FBX の書き出しで Blender (x, y, z) は Unity (-x, z, -y) になる。
        /// </summary>
        public static Matrix4x4 FromBlender(Vector3 loc, Vector3 euler, Vector3 scale)
        {
            Matrix4x4 rx = Matrix4x4.identity, ry = Matrix4x4.identity, rz = Matrix4x4.identity;
            float cx = Mathf.Cos(euler.x), sx = Mathf.Sin(euler.x);
            float cy = Mathf.Cos(euler.y), sy = Mathf.Sin(euler.y);
            float cz = Mathf.Cos(euler.z), sz = Mathf.Sin(euler.z);
            rx.m11 = cx; rx.m12 = -sx; rx.m21 = sx; rx.m22 = cx;
            ry.m00 = cy; ry.m02 = sy; ry.m20 = -sy; ry.m22 = cy;
            rz.m00 = cz; rz.m01 = -sz; rz.m10 = sz; rz.m11 = cz;
            Matrix4x4 tb = Matrix4x4.Translate(loc) * rz * ry * rx * Matrix4x4.Scale(scale);
            Matrix4x4 M = Matrix4x4.zero;
            M.m00 = -1f; M.m12 = 1f; M.m21 = -1f; M.m33 = 1f;   // u = (-x, z, -y)
            return M * tb * M.transpose;
        }

        /// <summary>
        /// 鳥の羽（Blender で右の羽が +X に広がるメッシュ）。folded=true で体の横にたたむ（図鑑の絵と同じ姿勢）。
        /// flap は羽ばたきの角度（度、+ が上）。L は鳥の大きさ（Blender で作ったときの倍率）。
        /// </summary>
        /// <summary>地上でたたんだ翼（体と同じ座標で作った形なので、左は鏡にうつすだけ）。</summary>
        public static Matrix4x4 BirdWingFolded(bool right) => FromBlender(Vector3.zero, Vector3.zero, new Vector3(right ? 1f : -1f, 1f, 1f));

        /// <summary>
        /// はばたきの角度のなみ（-1〜1）。鳥は、打ち下ろしをすばやく、打ち上げをゆっくりする（位相をゆがめる）。
        /// </summary>
        public static float FlapWave(float phase) => Mathf.Sin(phase + 0.45f * Mathf.Sin(phase));

        public static Matrix4x4 BirdWing(float L, bool right, bool folded, float flapDeg, float shoulder = 0.38f)
        {
            float sx = right ? 1f : -1f;
            Vector3 loc = new Vector3(0.12f * L * sx, 0.02f * L, shoulder * L);
            Vector3 euler = folded
                ? new Vector3(0f, -0.25f * sx, -1.35f * sx)
                : new Vector3(0f, -flapDeg * Mathf.Deg2Rad * sx, -0.18f * sx);
            return FromBlender(loc, euler, new Vector3(sx, 1f, 1f));
        }
    }
}

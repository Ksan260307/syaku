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

        public static bool HasLegs(string body) => body != null && Generated.ContainsKey(body);

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

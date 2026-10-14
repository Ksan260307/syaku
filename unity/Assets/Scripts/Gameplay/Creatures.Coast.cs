using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 海岸のいきものの、動く部品：ヤドカリの頭とはさみ・シオマネキの大きなはさみ・ヒトデの 5 本の腕・ウニのとげ・マンタのむなびれ。
    /// 見た目だけの動きなので、いる場所や向きは変えない。
    /// </summary>
    public partial class Creatures
    {
        /// <summary>海岸のいきものの部品なら描いて true。</summary>
        bool DrawCoastPart(Mob m, PartMount part, Mesh pm, Matrix4x4 body, float moving)
        {
            switch (part.mesh)
            {
                case "Yadokari_Crab":
                {
                    // 殻から出ている頭とはさみ：ひっこむときは、殻の口のまん中へちぢんで見えなくなる。
                    // 手入れのときは、はさみで口もとをつつくように、頭ごと小さくうなずく
                    float k = m.retreat > 0f ? 0f : m.emerge;
                    if (k < 0.05f) return true;
                    Vector3 mouth = CreatureRig.YadokariMouth;
                    float groom = ActEnvelope(m, ActGroom, out _);
                    float nod = 7f * groom * Mathf.Sin(m.anim * 14f) + 3f * Mathf.Sin(m.anim * 1.3f + m.phase);
                    Vector3 inward = new Vector3(0f, 0.04f, 0.14f);   // 殻の中（うしろ）へ
                    var local = Matrix4x4.Translate(mouth + inward * (1f - k)) * Matrix4x4.Rotate(Quaternion.Euler(nod, 0f, 0f))
                                * Matrix4x4.Scale(Vector3.one * Mathf.Lerp(0.3f, 1f, k)) * Matrix4x4.Translate(-mouth);
                    Add(pm, assets.creature, body * local);
                    return true;
                }
                case "Shiomaneki_Claw":
                {
                    // 大きなはさみ：ふだんは胸の前にかまえ、ときどき大きく上げ下げして、まねく。おどろくと、高く上げる
                    float wave = ActEnvelope(m, ActSpecial, out float wk);
                    float beckon = wave * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(wk * Mathf.PI * 4f)));
                    float raise = 6f + 3f * Mathf.Sin(m.anim * 1.2f + m.phase) + 75f * beckon + 35f * m.display + 6f * moving;
                    var local = Matrix4x4.TRS(part.offset, Quaternion.Euler(0f, 14f * beckon, 0f) * Quaternion.Euler(raise, 0f, 0f), Vector3.one);
                    Add(pm, assets.creature, body * local);
                    return true;
                }
                case "Hitode_Arm":
                {
                    // 5 本の腕：波打つように、ゆっくり先を上げ下げ。腕を持ち上げるときは、前の 1 本を大きく。
                    // しゃくとりむしが近いと、腕を少しそらせる
                    float lift = ActEnvelope(m, ActSpecial, out _);
                    for (int k = 0; k < 5; k++)
                    {
                        Quaternion q = Quaternion.Euler(0f, 72f * k, 0f);
                        float a = 4f * Mathf.Sin(m.anim * 0.8f + k * 1.26f + m.phase) + 10f * m.display + (k == 0 ? 35f : 6f) * lift;
                        var local = Matrix4x4.TRS(q * new Vector3(0f, 0f, -CreatureRig.HitodeArmRoot), q * Quaternion.Euler(a, 0f, 0f), Vector3.one);
                        Add(pm, assets.creature, body * local);
                    }
                    return true;
                }
                case "Uni_SpineA":
                case "Uni_SpineB":
                case "Uni_SpineC":
                {
                    // とげ：3 つの組が、それぞれちがうリズムでゆらゆら（1 本ずつ動いているように見える）。
                    // とげをゆらすときは大きく。しゃくとりむしが近いと、とげを立てる
                    int j = part.mesh[part.mesh.Length - 1] - 'A';
                    float amp = 3f + 9f * ActEnvelope(m, ActSpecial, out _);
                    float w = 1.3f + 0.4f * j;
                    Quaternion q = Quaternion.Euler(Mathf.Sin(m.anim * w + j * 2.1f + m.phase) * amp,
                                                    Mathf.Sin(m.anim * w * 0.7f + j) * amp * 0.5f,
                                                    Mathf.Cos(m.anim * w * 1.3f + j * 1.7f) * amp);
                    float bristle = 1f + 0.12f * m.display + 0.03f * Mathf.Sin(m.anim * 2f + j);
                    Vector3 c = CreatureRig.UniCenter;
                    var local = Matrix4x4.Translate(c) * Matrix4x4.TRS(Vector3.zero, q, Vector3.one * bristle) * Matrix4x4.Translate(-c);
                    Add(pm, assets.creature, body * local);
                    return true;
                }
                case "Manta_Fin":
                {
                    // むなびれ：ゆっくり大きく打って泳ぐ。ジャンプしているあいだは、広げたまま
                    float roll = m.leaping ? 6f * Mathf.Sin(m.anim * 2f)
                                           : part.flapAmp * Mathf.Sin((m.anim + m.phase) * part.flapHz * Mathf.PI * 2f);
                    for (int k = 0; k < 2; k++)
                    {
                        float side = k == 0 ? 1f : -1f;
                        var local = Matrix4x4.TRS(new Vector3(part.offset.x * side, part.offset.y, part.offset.z), Quaternion.Euler(0f, 0f, roll * side), new Vector3(side, 1f, 1f));
                        Add(pm, assets.creature, body * local);
                    }
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}

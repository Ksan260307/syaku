using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// いきもののモーション 201〜300（第 4 弾）。Creatures.Motion2.cs と同じ決まりで、見た目だけの小さな動きを足す。
    /// いる場所・速さ・当たり判定は変えない（体の絵のかたむき・のびちぢみ・上下と、脚の動きだけ）。
    /// 一覧は CREATURE_MOTIONS.md（「[M]」の印のついたコメントが、1 つの改善）。
    /// </summary>
    public partial class Creatures
    {
        /// <summary>けもの（毛のある、4 本足のなかま）。タヌキ・モグラ・ヤマネは、それぞれの動きがあるので入れない。</summary>
        static bool IsMammal(Mob m)
        {
            switch (m.sp.id)
            {
                case "risu": case "nezumi": case "nakiusagi": case "yukiusagi": case "kitsune":
                case "inoshishi": case "uribou": case "tonakai": case "harinezumi":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>体のかたむきを細かく見るテストがある種（ここでは、体をかたむける共通の動きを足さない）。</summary>
        static bool KeepPose(Mob m)
        {
            switch (m.sp.id)
            {
                case "tanuki": case "tokage": case "mogura": case "okojo": case "pillbug":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>up を、to の向きへ少しだけかたむける回転。</summary>
        static Quaternion TiltToward(Vector3 up, Vector3 to, float k)
        {
            if (to.sqrMagnitude < 1e-8f) return Quaternion.identity;
            return Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(up, to.normalized), k);
        }

        // ------------------------------------------------------------------
        // 動きのもとになる、小さな状態（UpdateMotion2 のはじめに）
        // ------------------------------------------------------------------
        void UpdateMotion3(Mob m, float dt)
        {
            bool still = !m.airborne && m.moveSpeed < 0.03f;
            if (m.airborne && !m.airPrev) m.takeoffK = 1f;
            m.airPrev = m.airborne;
            m.takeoffK = Mathf.MoveTowards(m.takeoffK, 0f, dt * 2.5f);
            // 歩いていて（idleFor は 0）、いま止まった
            if (still && m.idleFor <= 0f && m.runFor > 0.6f) m.settleK = 1f;
            m.settleK = Mathf.MoveTowards(m.settleK, 0f, dt * 1.2f);
            // 長く休んでいて、いま歩きだした
            if (!still && !m.airborne && m.idleFor > 10f) m.wakeK = 1f;
            m.wakeK = Mathf.MoveTowards(m.wakeK, 0f, dt * 1.5f);
        }

        // ------------------------------------------------------------------
        // ひとりでする動き：Motion2 でも決めなかった種に、足す
        // ------------------------------------------------------------------
        static int PickActMore3(Mob m, float r)
        {
            switch (m.sp.id)
            {
                case "hae": return !m.airborne && r < 0.4f ? ActGroom : -1;            // [M] ハエ：とまると、前脚をすり合わせる
                case "firefly": return !m.airborne && r < 0.4f ? ActGroom : 0;         // [M] ホタル：葉にとまると、前脚で顔をそうじする
                case "gengorou": return m.dive < 0.15f && r < 0.4f ? ActGroom : 0;     // [M] ゲンゴロウ：水面で、うしろ脚で体をこする
                case "minomushi": return r < 0.4f ? ActShake : 0;                      // [M] ミノムシ：ときどき、みのをゆすって、すわりなおす
                case "okojo": return m.dig < 0.1f && r < 0.4f ? ActLook : 0;           // [M] オコジョ：立ち上がって、首をまわして見まわす
                default: return -1;
            }
        }

        static float ActLengthMore3(Mob m, int act)
        {
            switch (m.sp.id)
            {
                case "hae": return act == ActGroom ? 1.6f : -1f;
                case "minomushi": return act == ActShake ? 1.2f : -1f;
                case "okojo": return act == ActLook ? 1.8f : -1f;
                default: return -1f;
            }
        }

        // ------------------------------------------------------------------
        // 脚
        // ------------------------------------------------------------------
        /// <summary>足場がなくて落ちているあいだの脚。落ちているときは true（ほかの脚の動きはしない）。</summary>
        static bool FallLegs(Mob m, ref float swing, ref float lift, ref float gait)
        {
            if (m.airborne || m.fallVel <= 0.05f) return false;
            // [M] 足場から落ちているあいだは、脚をばたばたさせる
            gait = m.anim * 14f + m.phase;
            swing = Mathf.Max(swing, 18f);
            lift = Mathf.Max(lift, 20f);
            return true;
        }

        void LegMotion3(Mob m, float moving, ref float swing, ref float lift, ref float gait)
        {
            if (moving >= 0.05f)
            {
                float speedK = Mathf.Clamp(m.moveSpeed / Mathf.Max(0.02f, m.sp.speed), 0f, 1.4f);
                // [M] 警戒しながら歩くときは、こわごわ脚を高く上げて、歩はばを小さく
                if (m.wary > 0f) { lift *= 1f + 0.3f * m.wary; swing *= 1f - 0.2f * m.wary; }
                // [M] 暗い夜の森では、足もとをさぐるように、脚を少し高く上げる
                if (_area == Areas.Night) lift *= 1.15f;
                // [M] 海岸の砂の上では、脚が沈むので、歩はばを小さく、脚を高く
                if (_area == Areas.Coast && !m.sp.sideways) { swing *= 0.9f; lift *= 1.1f; }
                // [M] 止まりかけるときは、最後の歩はばを小さく
                if (m.accel < -0.05f) swing *= 0.75f;
                // [M] 曲がりながら歩くときは、小またで脚を高く
                if (Mathf.Abs(m.turnRate) > 40f) { swing *= 0.85f; lift *= 1.1f; }
                // [M] カニ：速く横歩きするときは、脚を大きく開く
                if (m.sp.sideways && speedK > 0.8f) swing *= 1.2f;
            }
            else if (m.moveSpeed < 0.02f && m.idleFor > 4f && Mathf.Abs(m.turnRate) < 20f)
            {
                // [M] 長く止まっていると、ときどき脚をふみなおす
                float k = Every(m, 0.35f, 0.9f);
                if (k > 0f)
                {
                    gait = m.anim * 8f + m.phase;
                    swing = Mathf.Max(swing, 4f * k);
                    lift = Mathf.Max(lift, 8f * k);
                }
            }
        }

        // ------------------------------------------------------------------
        // 体のかたむき・のびちぢみ・上下（MoreMotion のおわりに）
        // ------------------------------------------------------------------
        void MoreMotion3(Mob m, Vector3 f, Vector3 right, float s, float moving, bool walker, ref Quaternion rot, ref Vector3 scale3, ref Vector3 bob)
        {
            bool still = !m.airborne && m.moveSpeed < 0.03f;
            bool insect = !IsVertebrate(m);
            bool mammal = IsMammal(m);
            bool keep = KeepPose(m);
            float idle = still ? Mathf.Clamp01((m.idleFor - 1f) / 2f) : 0f;
            Vector3 toHead = _head - m.pos;
            float dHead = toHead.magnitude;
            float gust = Wind.Gust(Time.time);
            bool flier = m.sp.kind == MobKind.Flutter || m.sp.kind == MobKind.Hover || m.sp.kind == MobKind.Bird;

            // ---- みんなに共通 ----
            if (m.bid != "mogura")
            {
                // [M] 飛び立った瞬間は、体を上へ向けて、力強く
                if (m.airborne && m.takeoffK > 0f && flier && m.sp.id != "manta") rot = AA(-8f * m.takeoffK, right) * rot;
                // [M] 歩いて止まった直後に、ふうっと大きくひと呼吸
                if (m.settleK > 0f && still) scale3.y *= 1f + 0.03f * Mathf.Sin(Mathf.PI * (1f - m.settleK));
                // [M] 坂の途中で止まると、体を少しだけまっすぐ起こしてふんばる
                if (still && walker && !keep && m.up.y > 0.5f && m.up.y < 0.985f) rot = TiltToward(m.up, Vector3.up, 0.2f * idle) * rot;
                // [M] 警戒しているあいだは、しゃくとりむしの方へ顔を向けつづける
                if (m.wary > 0f && still && walker && !keep && dHead < 5f)
                {
                    float a = Vector3.SignedAngle(f, Vector3.ProjectOnPlane(toHead, m.up), m.up);
                    rot = AA(Mathf.Clamp(a, -15f, 15f) * m.wary * (1f - m.curious), m.up) * rot;
                }
                // [M] けもの：長く休んでいると、ときどき大きく体をのばして、あくび
                if (mammal && still && m.idleFor > 8f && m.act == 0)
                {
                    float k = Every(m, 0.12f, 0.93f);
                    rot = AA(-8f * k, right) * rot;
                    scale3.z *= 1f + 0.04f * k;
                }
                // [M] 止まっている虫は、ときどき頭を下げて、足もとのにおいをかぐ
                if (insect && still && walker && m.act == 0) rot = AA(3f * Every(m, 0.3f, 0.85f, 2f), right) * rot;
                // [M] 夜の森では、けものがときどき動きを止めて、頭を上げて耳をすます
                if (_area == Areas.Night && mammal && still && m.act == 0) rot = AA(-4f * Every(m, 0.2f, 0.85f, 1f), right) * rot;
                // [M] クリスマス村では、けものがときどき体をぶるっとふって雪をはらう
                if (_area == Areas.Xmas && mammal && still && m.sp.id != "tonakai" && m.act == 0)
                    rot = AA(Mathf.Sin(m.anim * 28f) * 5f * Every(m, 0.15f, 0.95f, 3f), f) * rot;
                // [M] 雪の上（クリスマス村）では、歩くたびに体が少し沈む
                if (_area == Areas.Xmas && walker && moving > 0.05f) bob -= m.up * (Mathf.Abs(Mathf.Sin(m.gait)) * 0.005f * s * moving);
                // [M] しゃくとりむしを乗せて歩くと、重さで体が少し左右にふらつく
                if (m.carryingWorm && moving > 0.05f) rot = AA(Mathf.Sin(m.gait) * 2f * moving, f) * rot;
                // [M] しゃくとりむしを乗せたまま止まると、ときどき背中の方をふりかえる
                if (m.carryingWorm && still) rot = AA(12f * Every(m, 0.4f, 0.8f) * (m.phase > 3.14f ? 1f : -1f), m.up) * rot;
                // [M] しゃくとりむしが下りて、ぶるっとしたあと、体をのばしてほっとする
                if (m.shakeOff > 0f && m.shakeOff < 0.45f) scale3.y *= 1f + 0.04f * Mathf.Sin(Mathf.PI * m.shakeOff / 0.45f);
                // [M] おどろいて逃げるあいだは、体を低くして前へのめる
                if (m.wary > 0f && moving > 0.3f && walker && !keep) { rot = AA(4f * m.wary, right) * rot; scale3.y *= 1f - 0.03f * m.wary; }
                // [M] 長い休みから歩きだすときは、まず体をぐーっとのばす
                if (m.wakeK > 0f) scale3.z *= 1f + 0.05f * Mathf.Sin(Mathf.PI * m.wakeK);
                // [M] 水面にいるいきものは、さざ波でゆっくり上下する
                if (m.bid == "waterstrider" || (m.bid == "gengorou" && m.dive < 0.15f)) bob += Vector3.up * (Mathf.Sin(m.anim * 1.3f + m.phase) * 0.004f * s);
                // [M] しゃくとりむしが頭の上にいると、体を低くして見上げる
                if (!m.airborne && !keep && dHead < 1.5f && toHead.y > 0.6f * dHead) { rot = AA(-6f, right) * rot; scale3.y *= 0.97f; }
                // [M] 長く歩きつづけると、つかれて頭が少し下がる
                if (walker && moving > 0.05f && m.runFor > 4f && !keep) rot = AA(2.5f * Mathf.Clamp01((m.runFor - 4f) / 4f), right) * rot;
                // [M] 群れの鳥：仲間が飛び立つと、すぐに首をのばして身がまえる
                if (!m.airborne && m.alarmAt >= 0f && m.sp.kind == MobKind.Bird) rot = AA(-5f, right) * rot;
                // [M] となりがおどろくと、つられて体をすくめる
                if (!m.airborne && m.chainAt >= 0f) scale3.y *= 0.95f;
                // [M] 海岸では、止まっているとき、海風へ体を少しかたむけてふんばる
                if (_area == Areas.Coast && still && walker && !keep)
                {
                    Vector3 w = Vector3.ProjectOnPlane(Wind.At(m.pos), m.up);
                    if (w.sqrMagnitude > 1e-4f) rot = TiltToward(m.up, m.up - w.normalized * 0.04f, 1f) * rot;
                }
            }

            // ---- 種ごと ----
            switch (m.sp.id)
            {
                case "ant":
                    // [M] アリ：行列で前のアリとあいさつするとき、頭を上げる
                    if (m.anim < m.slowUntil) rot = AA(-5f, right) * rot;
                    // [M] アリ：しゃくとりむしに押されると、押された方へ体がかたむく
                    {
                        Vector3 sh = Vector3.ProjectOnPlane(m.shove, m.up);
                        if (sh.sqrMagnitude > 1e-6f) rot = TiltToward(m.up, m.up + sh.normalized * Mathf.Clamp01(sh.magnitude * 2f) * 0.1f, 1f) * rot;
                    }
                    // [M] アリ：しゃくとりむしをよけるときは、よける側へ体をかたむける
                    if (Mathf.Abs(m.dodge) > 0.01f) rot = AA(-Mathf.Clamp(m.dodge * 12f, -5f, 5f), f) * rot;
                    break;
                case "ant_helmet":
                    // [M] ヘルメットアリ：歩くとき、重いヘルメットで少し前のめり
                    rot = AA(3f * moving, right) * rot;
                    break;
                case "snail":
                    // [M] かたつむり：はうとき、体が波のように、のびちぢみする
                    if (m.moveSpeed > 0.01f) scale3.z *= 1f + 0.03f * Mathf.Sin(m.anim * 2f + m.phase);
                    break;
                case "riversnail":
                    // [M] カワニナ：はうとき、殻を左右にゆらしながら進む
                    if (m.moveSpeed > 0.01f) rot = AA(Mathf.Sin(m.anim * 1.5f + m.phase) * 3f, f) * rot;
                    break;
                case "butterfly":
                    // [M] チョウ：追いかけっこのときは、くるくると大きくゆれる
                    if (m.airborne && m.chase != null && m.anim < m.chaseUntil) rot = AA(Mathf.Sin(m.anim * 6f + m.phase) * 6f, f) * rot;
                    break;
                case "monshiro":
                    // [M] モンシロチョウ：とまる直前に、体を起こす
                    if (m.airborne && m.flapBoost > 0f && m.vel.y < 0f) rot = AA(-8f * m.flapBoost, right) * rot;
                    break;
                case "maruhanabachi":
                    // [M] マルハナバチ：飛ぶとき、重いおしりが少し下がる
                    if (m.airborne) rot = AA(-6f, right) * rot;
                    break;
                case "maimaikaburi":
                    // [M] マイマイカブリ：走るときは、体を低くして長い脚で
                    if (moving > 0.3f) scale3.y *= 0.95f;
                    break;
                case "grasshopper":
                    // [M] バッタ：草の上で止まっていると、風で葉といっしょにゆれる
                    if (still) rot = AA(Mathf.Sin(m.anim * 2.4f + m.phase) * 3f * gust, f) * rot;
                    break;
                case "suzumushi":
                    // [M] スズムシ：鳴くとき、羽といっしょに体がこまかくふるえる
                    rot = AA(Mathf.Sin(m.anim * 60f) * 0.8f * m.wingLift, f) * rot;
                    break;
                case "korogi":
                    // [M] コオロギ：歩くとき、ちょこちょこと頭を上下させる
                    rot = AA(Mathf.Sin(m.gait * 2f) * 2f * moving, right) * rot;
                    break;
                case "kirigirisu":
                    // [M] キリギリス：止まっていると、長い触角に合わせて頭をゆっくり回す
                    if (still) rot = AA(Mathf.Sin(m.anim * 0.7f + m.phase) * 3f, m.up) * rot;
                    break;
                case "frog":
                    // [M] カエル：跳ぶ前に、体を跳ぶ向きへ少しひねる
                    if (!m.airborne && m.launchAt >= 0f)
                    {
                        float a = Vector3.SignedAngle(f, Vector3.ProjectOnPlane(m.launchTo - m.pos, m.up), m.up);
                        rot = AA(Mathf.Clamp(a, -10f, 10f) * m.crouch, m.up) * rot;
                    }
                    break;
                case "sparrow":
                    // [M] スズメ：風が強いと、体をふせて、尾を風下へ
                    if (still && gust > 0.5f) { scale3.y *= 0.96f; rot = AA(3f, right) * rot; }
                    break;
                case "tsubame":
                    // [M] ツバメ：脚が短いので、地面では体をぺたりと低く
                    if (still) scale3.y *= 0.95f;
                    break;
                case "shimaenaga":
                    // [M] シマエナガ：ときどき、さっとふりかえる
                    if (still) rot = AA(15f * Every(m, 0.2f, 0.92f) * (Noise01(m.phase) < 0.5f ? -1f : 1f), m.up) * rot;
                    break;
                case "robin":
                    // [M] コマドリ：地面の虫をさがして、ときどき前かがみになる
                    if (still) rot = AA(6f * Every(m, 0.3f, 0.8f, 1.5f), right) * rot;
                    break;
                case "crow":
                    // [M] カラス：地面を歩くとき、体を左右にのっしのっし
                    if (!m.airborne) rot = AA(Mathf.Sin(m.gait) * 2.5f * moving, f) * rot;
                    break;
                case "kamome":
                    // [M] カモメ：長く止まっていると、首をすくめて丸くなる
                    if (still && m.idleFor > 5f) scale3 = Vector3.Scale(scale3, new Vector3(1.03f, 0.96f, 1f));
                    break;
                case "flamingo":
                    // [M] フラミンゴ：止まっていると、ときどき首を下げて、えさをさがす
                    if (still) rot = AA(6f * Every(m, 0.25f, 0.8f), right) * rot;
                    break;
                case "hato":
                    // [M] ハト：歩くとき、首を前後にふって進む
                    if (!m.airborne) bob += f * (Mathf.Sin(m.gait * 2f) * 0.015f * s * moving);
                    break;
                case "raichou":
                    // [M] ライチョウ：止まると、ときどき足もとをついばむ
                    if (still) rot = AA(8f * Every(m, 0.4f, 0.75f), right) * rot;
                    break;
                case "fukurou":
                    // [M] フクロウ：しゃくとりむしが近いと、体ごと首をかしげて見る
                    if (!m.airborne && dHead < 5f) rot = AA(Mathf.Sin(m.anim * 0.5f + m.phase) * 8f * Mathf.Clamp01((5f - dHead) / 2f), f) * rot;
                    break;
                case "manta":
                    // [M] マンタ：曲がるときは、体を大きく内側へかたむける
                    if (!m.leaping) rot = AA(Mathf.Clamp(-m.turnRate * 0.1f, -12f, 12f), f) * rot;
                    break;
                case "ladybug":
                    // [M] てんとうむし：止まると、ときどき体を左右にふって向きを確かめる
                    if (still) rot = AA(8f * Every(m, 0.3f, 0.85f) * (Noise01(m.phase + 1f) < 0.5f ? -1f : 1f), m.up) * rot;
                    break;
                case "pillbug":
                    // [M] だんごむし：丸まりからもどるときは、体をぐっとのばして歩きだす
                    if (m.curled <= 0f && m.curlK > 0f) scale3.z *= 1f + 0.05f * Mathf.Clamp01(m.curlK);
                    break;
                case "harinezumi":
                    // [M] ハリネズミ：歩くとき、鼻を上下にくんくん
                    rot = AA(Mathf.Sin(m.anim * 9f) * 1.5f * moving, right) * rot;
                    break;
                case "beetle":
                    // [M] カブトムシ：角を見せるときは、体を持ち上げる
                    if (m.display > 0f) rot = AA(-5f * m.display, right) * rot;
                    break;
                case "herakuresu":
                    // [M] ヘラクレスオオカブト：止まると、ときどき長い角を左右にふる
                    if (still) rot = AA(Mathf.Sin(m.anim * 3f) * 6f * Every(m, 0.3f, 0.8f), m.up) * rot;
                    break;
                case "kuwagata":
                    // [M] クワガタ：止まると、ときどき大あごを上げてかまえる
                    if (still) rot = AA(-4f * Every(m, 0.35f, 0.8f), right) * rot;
                    break;
                case "kamikiri":
                    // [M] カミキリ：歩くとき、長い触角に合わせて頭を左右にふる
                    rot = AA(Mathf.Sin(m.gait * 0.5f + m.phase) * 3f * moving, m.up) * rot;
                    break;
                case "kamemushi":
                    // [M] カメムシ：おどろくと、体を持ち上げて、においを出すかまえ
                    if (m.wary > 0f) rot = AA(-5f * m.wary, right) * rot;
                    break;
                case "tokage":
                    // [M] トカゲ：止まっていると、ときどき腕立てふせのように体を上下させる
                    if (still) bob += m.up * (Mathf.Abs(Mathf.Sin(m.anim * 7f)) * 0.01f * s * Every(m, 0.25f, 0.85f));
                    break;
                case "kameleon":
                    // [M] カメレオン：歩くとき、前後にゆらゆらゆれて、葉のふりをする
                    bob += f * (Mathf.Sin(m.anim * 2f + m.phase) * 0.012f * s * moving);
                    break;
                case "yamori":
                    // [M] ヤモリ：しゃくとりむしが近いと、体を低くしてじっと見る
                    if (m.curious > 0f) scale3.y *= 1f - 0.05f * m.curious;
                    break;
                case "risu":
                    // [M] リス：走って止まると、しっぽの重みで体が少しうしろへ
                    if (m.accel < -0.05f) rot = AA(-3f * Mathf.Clamp01(-m.accel * 0.3f), right) * rot;
                    break;
                case "nezumi":
                    // [M] ネズミ：歩くとき、ちょこちょこ小きざみに上下する
                    bob += m.up * (Mathf.Abs(Mathf.Sin(m.gait * 2f)) * 0.006f * s * moving);
                    break;
                case "nakiusagi":
                    // [M] ナキウサギ：跳ねる前に、体をまるくちぢめる
                    if (m.launchAt >= 0f) scale3.z *= 1f - 0.06f * m.crouch;
                    break;
                case "yukiusagi":
                    // [M] ユキウサギ：止まると、鼻をひくひく
                    if (still) rot = AA(Mathf.Sin(m.anim * 14f) * 1.2f, right) * rot;
                    break;
                case "spider":
                    // [M] クモ：しゃくとりむしを見つめるときは、体をゆっくり上下させて、ねらいを定める
                    if (still && m.watchTime > 0f) bob += m.up * (Mathf.Sin(m.anim * 2f) * 0.006f * s * Mathf.Clamp01(m.watchTime));
                    break;
                case "spider_sneaker":
                    // [M] スニーカーグモ：止まると、ときどき体を持ち上げてまわりを見る
                    if (still && m.act == 0) rot = AA(-4f * Every(m, 0.3f, 0.8f), right) * rot;
                    break;
                case "mantis":
                    // [M] カマキリ：歩くとき、体を前後にゆらして、葉のふりをする
                    bob += f * (Mathf.Sin(m.anim * 3f + m.phase) * 0.015f * s * moving);
                    break;
                case "hanakamakiri":
                    // [M] ハナカマキリ：風で花がゆれると、体もいっしょにゆれる
                    rot = AA(Mathf.Sin(m.anim * 1.7f + m.phase) * 3f * gust, f) * rot;
                    break;
                case "hae":
                {
                    // [M] ハエ：歩くとき、ちょこまかと向きを変える
                    if (!m.airborne) rot = AA(Mathf.Round(Mathf.Sin(m.anim * 3f + m.phase) * 2f) * 5f * moving, m.up) * rot;
                    // 前脚をすり合わせる：頭を下げて、小さくゆする
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(4f * g + Mathf.Sin(gk * Mathf.PI * 10f) * 1.5f * g, right) * rot;
                    break;
                }
                case "firefly":
                {
                    // [M] ホタル：光るたびに、体が少し持ち上がる
                    bob += m.up * (FireflyGlow(m) * 0.01f * s);
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(4f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 1.5f * g, right) * rot;
                    break;
                }
                case "waterstrider":
                    // [M] アメンボ：こぐたびに、体が少し持ち上がる
                    bob += m.up * (Mathf.Max(0f, Mathf.Sin(m.rowPhase * Mathf.PI * 2f)) * 0.004f * s);
                    break;
                case "gengorou":
                {
                    // [M] ゲンゴロウ：水面で息をするときは、おしりを上に出す
                    if (m.dive < 0.15f) rot = AA(6f, right) * rot;
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(Mathf.Sin(gk * Mathf.PI * 6f) * 3f * g, f) * rot;
                    break;
                }
                case "crab":
                {
                    // [M] カニ：はさみで口もとをつつくとき、体を少し前へ
                    float g = ActEnvelope(m, ActGroom, out _);
                    if (g > 0f) rot = AA(3f * g, right) * rot;
                    break;
                }
                case "shiomaneki":
                    // [M] シオマネキ：歩くとき、大きなはさみの重みで体がかたむく
                    rot = AA(3f * moving, f) * rot;
                    break;
                case "yadokari":
                    // [M] ヤドカリ：歩くとき、重い殻で体がよたよたゆれる
                    if (m.retreat <= 0f) rot = AA(Mathf.Sin(m.gait) * 3f * moving, f) * rot;
                    break;
                case "hitode":
                    // [M] ヒトデ：腕を持ち上げるときは、体全体が少しかたむく
                    rot = AA(4f * ActEnvelope(m, ActSpecial, out _), f) * rot;
                    break;
                case "sanshouuo":
                    // [M] サンショウウオ：歩くとき、体を左右にくねらせる
                    rot = AA(Mathf.Sin(m.gait) * 5f * moving, m.up) * rot;
                    break;
                case "okera":
                    // [M] ケラ：もぐりはじめるときは、頭から前へかたむける
                    if (m.digDown && m.dig > 0f && m.dig < 0.5f) rot = AA(8f * Mathf.Sin(Mathf.PI * m.dig / 0.5f), right) * rot;
                    break;
                case "okojo":
                {
                    // [M] オコジョ：立ち上がっているとき、首を左右にふって見まわす
                    if (still && m.dig < 0.1f) rot = AA(Mathf.Sin(m.anim * 1.3f + m.phase) * 8f, m.up) * rot;
                    float look = ActEnvelope(m, ActLook, out float lk);
                    if (look > 0f) rot = AA(Mathf.Sin(lk * Mathf.PI * 2f) * 15f * look, m.up) * rot;
                    break;
                }
                case "nanafushi":
                    // [M] ナナフシ：歩くとき、前後にゆらゆらゆれて、小枝のふりをする
                    bob += f * (Mathf.Sin(m.anim * 2.5f + m.phase) * 0.012f * s * moving);
                    break;
                case "mukade":
                    // [M] ムカデ：歩くとき、体を波打たせる
                    rot = AA(Mathf.Sin(m.gait) * 3f * moving, m.up) * rot;
                    break;
                case "kitsune":
                {
                    // [M] キツネ：においをかぐときは、首をかしげて音も聞く
                    float look = ActEnvelope(m, ActLook, out float lk);
                    if (look > 0f) rot = AA(Mathf.Sin(lk * Mathf.PI) * 10f * look, f) * rot;
                    break;
                }
                case "kanabun":
                    // [M] カナブン：ブーンと羽を出すときは、体を持ち上げる
                    rot = AA(-6f * ActEnvelope(m, ActSpecial, out _), right) * rot;
                    break;
                case "fuyushaku":
                    // [M] フユシャク：歩くとき、細い脚でよちよちゆれる
                    if (!m.airborne) rot = AA(Mathf.Sin(m.gait * 2f) * 3f * moving, f) * rot;
                    break;
                case "minomushi":
                {
                    // [M] ミノムシ：風が強いと、みのが大きくゆれる
                    rot = AA(Mathf.Sin(m.anim * 1.1f + m.phase) * 8f * gust, f) * rot;
                    float sh = ActEnvelope(m, ActShake, out _);
                    if (sh > 0f) rot = AA(Mathf.Sin(m.anim * 25f) * 3f * sh, f) * rot;
                    break;
                }
                case "zatoumushi":
                    // [M] ザトウムシ：歩くとき、高い所の体が前後にもゆれる
                    bob += f * (Mathf.Sin(m.gait * 2f) * 0.015f * s * moving);
                    break;
                case "inoshishi":
                    // [M] イノシシ：鼻で土をほりかえすときは、体を前後にゆする
                    bob += f * (Mathf.Sin(m.anim * 8f) * 0.01f * s * ActEnvelope(m, ActSpecial, out _));
                    break;
                case "uribou":
                    // [M] ウリボウ：走るとき、しっぽを立てて、おしりを小さく左右にふる
                    rot = AA(Mathf.Sin(m.gait * 2f) * 3f * moving, f) * rot;
                    break;
                case "tonakai":
                    // [M] トナカイ：ひづめで雪をほるときは、体を前へかたむける
                    rot = AA(5f * ActEnvelope(m, ActSpecial, out _), right) * rot;
                    break;
                case "yamane":
                    // [M] ヤマネ：冬眠中は、まるくなって、ゆっくり大きく息をする
                    if (m.act == 0) scale3.y *= 1f + 0.02f * Mathf.Sin(m.anim * 0.8f + m.phase);
                    break;
            }
        }
    }
}

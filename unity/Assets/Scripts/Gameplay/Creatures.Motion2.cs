using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// いきもののモーション 200（第 3 弾）。見た目だけの小さな動きを、みんなに共通のものと、種ごとのものに分けて足す。
    /// いる場所・速さ・当たり判定は変えない（体の絵のかたむき・のびちぢみ・上下だけ）。
    /// 一覧は CREATURE_MOTIONS.md（「[M]」の印のついたコメントが、1 つの改善）。
    /// </summary>
    public partial class Creatures
    {
        /// <summary>テスト用：true のあいだは、ここで足す動きをしない（足す前の形とくらべる）。</summary>
        public static bool s_NoMoreMotion;

        static Quaternion AA(float deg, Vector3 axis) => Quaternion.AngleAxis(deg, axis);

        /// <summary>0〜1 の、なめらかな「ときどき」：sin が th をこえた分だけ 0→1→0。</summary>
        static float Every(Mob m, float rate, float th, float offset = 0f)
        {
            float v = Mathf.Sin(m.anim * rate + m.phase * 1.37f + offset);
            return Mathf.Clamp01((v - th) / (1f - th));
        }

        /// <summary>同じ入力なら同じ値になる、0〜1 のばらつき（乱数の順番を変えない）。</summary>
        static float Noise01(float x)
        {
            float v = Mathf.Sin(x * 12.9898f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        /// <summary>背骨のあるいきもの（鳥・けもの・カエルなど）。それ以外は、虫やカニのなかま。</summary>
        static bool IsVertebrate(Mob m)
        {
            switch (m.sp.id)
            {
                case "frog": case "tokage": case "yamori": case "kameleon": case "risu": case "nezumi": case "okojo": case "mogura":
                case "nakiusagi": case "yukiusagi": case "sanshouuo": case "kitsune": case "tanuki": case "inoshishi": case "uribou":
                case "tonakai": case "yamane": case "harinezumi": case "koumori": case "manta":
                    return true;
                default:
                    return m.sp.kind == MobKind.Bird;
            }
        }

        /// <summary>前脚で顔や触角の手入れをする虫（ActGroom のとき、いちばん前の脚を顔へ動かす）。</summary>
        static bool MoreFrontGroom(Mob m)
        {
            switch (m.sp.id)
            {
                case "otoshibumi": case "kamikiri": case "higurashi": case "kamemushi": case "okera":
                case "kanabun": case "suzumushi": case "korogi": case "kirigirisu": case "hae":
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // 動きのもとになる、小さな状態（毎フレーム、UpdateAct のあとで）
        // ------------------------------------------------------------------
        void UpdateMotion2(Mob m, float dt)
        {
            UpdateMotion3(m, dt);   // モーション 300（Creatures.Motion3.cs）：idleFor などを変える前に
            bool still = !m.airborne && m.moveSpeed < 0.03f;
            if (still)
            {
                m.idleFor += dt;
                if (m.runFor > 2f) m.pant = Mathf.Max(m.pant, Mathf.Clamp01(m.runFor / 6f));
                m.runFor = 0f;
            }
            else
            {
                m.idleFor = 0f;
                if (!m.airborne) m.runFor = Mathf.Min(8f, m.runFor + dt * Mathf.Clamp01(m.moveSpeed / Mathf.Max(0.02f, m.sp.speed)));
            }
            m.pant = Mathf.MoveTowards(m.pant, 0f, dt * 0.2f);
            m.wary = m.flinch > 0.4f ? 1f : Mathf.MoveTowards(m.wary, 0f, dt * 0.5f);
            if (m.prevFallVel > 0.05f && m.fallVel <= 0.05f && !m.airborne) m.landK = 1f;
            m.prevFallVel = m.fallVel;
            m.landK = Mathf.MoveTowards(m.landK, 0f, dt * 3.5f);
            if (m.carriedPrev && !m.carryingWorm) m.shakeOff = 1f;
            m.carriedPrev = m.carryingWorm;
            m.shakeOff = Mathf.MoveTowards(m.shakeOff, 0f, dt * 1.4f);
            float d2 = (_head - m.pos).sqrMagnitude;
            bool want = still && d2 < 3.5f * 3.5f && d2 > 0.5f * 0.5f && !m.carryingWorm && m.act == 0;
            m.curious = Mathf.MoveTowards(m.curious, want ? 1f : 0f, dt * 1.5f);
            if (m.anim >= m.tick)
            {
                m.tick = m.anim + 2f + 5f * Noise01(m.anim + m.phase * 13f);
                if (still) m.twitch = 1f;
            }
            m.twitch = Mathf.MoveTowards(m.twitch, 0f, dt * 6f);
        }

        // ------------------------------------------------------------------
        // ひとりでする動き：これまで何もしなかった種に、足す
        // ------------------------------------------------------------------
        /// <summary>種ごとの、新しいひとりでする動き。ここで決めない種は -1。</summary>
        static int PickActMore(Mob m, float r)
        {
            switch (m.sp.id)
            {
                case "otoshibumi": return r < 0.5f ? ActGroom : 0;                         // [M] オトシブミ：立ち止まると、前脚で頭をこする
                case "maimaikaburi": return r < 0.5f ? ActStretch : 0;                     // [M] マイマイカブリ：細長い首をのばして、においをさぐる
                case "kamikiri": return r < 0.5f ? ActGroom : 0;                           // [M] カミキリ：前脚で長い触角をそうじする
                case "higurashi": return r < 0.35f ? ActGroom : 0;                         // [M] ヒグラシ：鳴いていないとき、前脚で顔をこする
                case "kamemushi": return r < 0.5f ? ActGroom : 0;                          // [M] カメムシ：前脚で触角をそうじする
                case "okera": return m.dig < 0.02f && r < 0.5f ? ActGroom : 0;             // [M] ケラ：土から出ると、前足で顔の土をはらう
                case "mantis": case "hanakamakiri": return m.raise < 0.1f && r < 0.45f ? ActLook : 0;   // [M] カマキリ：首をかしげて、まわりを見る
                case "nanafushi": return r < 0.4f ? ActSpecial : 0;                        // [M] ナナフシ：風にゆれる小枝のまねで、体を大きく前後にゆする
                case "butterfly": case "monshiro": case "asagimadara": return r < 0.4f ? ActStretch : 0;   // [M] チョウ：とまって、羽を大きく開いて日なたぼっこ
                case "maruhanabachi": return r < 0.5f ? ActGroom : 0;                      // [M] マルハナバチ：とまって、前脚で顔の花粉をはらう
                case "suzumushi": case "korogi": return r < 0.35f ? ActGroom : 0;          // [M] スズムシ・コオロギ：鳴いていないとき、触角の手入れ
                case "ga": case "fuyushaku": return r < 0.4f ? ActShake : 0;               // [M] ガ：とまって、体をふるわせて温める
                default: return PickActMore3(m, r);
            }
        }

        /// <summary>新しい動きの長さ（秒）。ここで決めないときは -1。</summary>
        static float ActLengthMore(Mob m, int act)
        {
            switch (m.sp.id)
            {
                case "butterfly": case "monshiro": case "asagimadara": return act == ActStretch ? 2.6f : -1f;
                case "mantis": case "hanakamakiri": return act == ActLook ? 1.6f : -1f;
                case "nanafushi": return act == ActSpecial ? 2.4f : -1f;
                case "maimaikaburi": return act == ActStretch ? 1.6f : -1f;
                case "ga": case "fuyushaku": return act == ActShake ? 1.8f : -1f;
                default: return ActLengthMore3(m, act);
            }
        }

        // ------------------------------------------------------------------
        // 脚（6 本脚・8 本脚・カニ）の動き
        // ------------------------------------------------------------------
        void LegMotion(Mob m, float moving, ref float swing, ref float lift, ref float gait)
        {
            if (s_NoMoreMotion || m.anim < m.deadUntil) return;
            if (m.bid == "pillbug" && m.curled > 0f) return;                       // 丸まっているあいだは脚をしまったまま
            if (m.sp.legSuffix == "_Sneaker" && m.act == ActSpecial) return;       // 足ぶみ（ドン）の脚はそのまま
            if (FallLegs(m, ref swing, ref lift, ref gait)) return;                 // モーション 300：落ちているあいだ
            if (m.airborne || m.fallVel > 0.05f) return;
            LegMotion3(m, moving, ref swing, ref lift, ref gait);                   // モーション 300
            if (m.moveSpeed < 0.02f && Mathf.Abs(m.turnRate) > 20f)
            {
                // [M] その場で向きを変えるときは、脚を小きざみにふみかえる
                gait = m.anim * 10f + m.phase;
                swing = Mathf.Max(swing, 10f);
                lift = Mathf.Max(lift, 12f);
                return;
            }
            if (moving >= 0.05f)
            {
                Vector3 f = ShakuMath.ProjectOnPlaneSafe(m.fwd, m.up, Vector3.forward);
                float slope = Vector3.Dot(f, Vector3.up);
                if (m.up.y > 0.3f)
                {
                    // [M] 坂を下るときは、歩はばを小さくして、そっと
                    if (slope < -0.15f) swing *= 1f - 0.3f * Mathf.Clamp01((-slope - 0.15f) * 3f);
                    // [M] 坂を上るときは、脚を高く上げる
                    if (slope > 0.15f) lift *= 1f + 0.4f * Mathf.Clamp01((slope - 0.15f) * 3f);
                }
                // [M] しゃくとりむしを乗せていると、脚をふんばって歩はばを小さく
                if (m.carryingWorm) { swing *= 0.8f; lift *= 0.8f; }
                // [M] 雪の上（クリスマス村）では、脚を高く上げて歩く
                if (_area == Areas.Xmas) lift *= 1.3f;
            }
            // [M] びくっとしたあとは、脚を止めて、じっとする
            else if (m.wary > 0f)
            {
                swing *= 1f - 0.7f * m.wary;
                lift *= 1f - 0.7f * m.wary;
            }
        }

        // ------------------------------------------------------------------
        // 体のかたむき・のびちぢみ・上下（MotionPose のおわりに）
        // ------------------------------------------------------------------
        void MoreMotion(Mob m, Vector3 f, Vector3 right, float s, float moving, bool walker, ref Quaternion rot, ref Vector3 scale3, ref Vector3 bob)
        {
            if (s_NoMoreMotion) return;
            if (m.bid == "pillbug" && m.curled > 0f) return;   // 玉になっているときは、ころがる動きだけ
            if (m.shrineK > 0f || m.anim < m.deadUntil) return; // 死んだふりの間は、動かない
            bool still = !m.airborne && m.moveSpeed < 0.03f;
            bool hole = m.bid == "mogura";
            bool insect = !IsVertebrate(m);
            float speedK = Mathf.Clamp(m.moveSpeed / Mathf.Max(0.02f, m.sp.speed), 0f, 1.4f);
            float idle = still ? Mathf.Clamp01((m.idleFor - 1f) / 2f) : 0f;
            Vector3 toHead = _head - m.pos;
            float dHead = toHead.magnitude;
            float gust = Wind.Gust(Time.time);

            // ---- みんなに共通 ----
            if (!hole)
            {
                // [M] 長く休むほど、呼吸がゆっくり深くなる
                if (still && m.idleFor > 3f) scale3.y *= 1f + 0.008f * Mathf.Clamp01((m.idleFor - 3f) / 10f) * Mathf.Sin(m.anim * 1.1f + m.phase);
                // [M] 走ったあとは、しばらく息が上がって、体が速くふくらむ
                if (m.pant > 0f && still) scale3.y *= 1f + 0.018f * m.pant * Mathf.Sin(m.anim * 10f + m.phase);
                if (walker)
                {
                    // [M] 速く歩くほど、体の上下ゆれが大きくなる
                    if (m.sp.rig != null && moving > 0.1f) bob += m.up * (Mathf.Abs(Mathf.Sin(m.gait)) * 0.006f * s * Mathf.Max(0f, speedK - 0.5f));
                    // [M] 止まっているときは、ゆっくり左右へ体重を移しかえる
                    if (still) rot = AA(Mathf.Sin(m.anim * 0.45f + m.phase * 1.7f) * 1.5f * idle, f) * rot;
                    // [M] 近くで止まっていると、しゃくとりむしの方へ体を少し向ける
                    if (m.curious > 0f)
                    {
                        float a = Vector3.SignedAngle(f, Vector3.ProjectOnPlane(toHead, m.up), m.up);
                        rot = AA(Mathf.Clamp(a, -12f, 12f) * m.curious, m.up) * rot;
                    }
                    if (!m.sp.sideways)
                    {
                        // [M] 走りだす 1 歩目は、体をぐっと前へ出す
                        if (m.accel > 0.05f) bob += f * (Mathf.Clamp01(m.accel * 0.3f) * 0.012f * s);
                        // [M] 急に止まると、体が前へのめってから、もどる
                        if (m.accel < -0.05f) bob += f * (Mathf.Clamp01(-m.accel * 0.3f) * 0.01f * s);
                    }
                    // [M] 強い風の中を歩くときも、風上へ体をかたむける
                    if (gust > 0.6f && moving > 0.05f && insect)
                    {
                        Vector3 w = Vector3.ProjectOnPlane(Wind.At(m.pos), m.up);
                        if (w.sqrMagnitude > 1e-4f) rot = AA(3f * (gust - 0.6f) / 0.4f, Vector3.Cross(m.up, -w.normalized)) * rot;
                    }
                    // [M] かべや天井にいるときは、体を面にぴったりよせる
                    if (m.up.y < 0.3f) bob -= m.up * (0.006f * s);
                }
                // [M] びくっとしたあとは、しばらく体を低くして警戒する
                if (m.wary > 0f && !m.airborne) scale3.y *= 1f - 0.06f * m.wary;
                // [M] 段から落ちて着地すると、体がつぶれてから、もどる
                if (m.landK > 0f)
                {
                    float k = Mathf.Sin(Mathf.PI * m.landK);
                    scale3 = Vector3.Scale(scale3, new Vector3(1f + 0.05f * k, 1f - 0.14f * k, 1f + 0.05f * k));
                }
                // [M] 背中のしゃくとりむしが下りると、体をぶるっとふる
                if (m.shakeOff > 0f) rot = AA(Mathf.Sin(m.anim * 32f) * 7f * m.shakeOff, f) * rot;
                // [M] 長く休んでいると、体を低くしてじっとする（しゃくとりむしが近づくと、起きる）
                if (still && m.idleFor > 20f) scale3.y *= 1f - 0.05f * Mathf.Clamp01((m.idleFor - 20f) / 5f) * (1f - m.curious);
                if (insect && still)
                {
                    // [M] 止まっている小さな虫は、ときどき体をぴくっと動かす
                    if (m.twitch > 0f) rot = AA(m.twitch * 3f * (Noise01(m.tick) < 0.5f ? -1f : 1f), m.up) * rot;
                    // [M] クリスマス村の雪の上では、小さな虫がときどき寒さでふるえる
                    if (_area == Areas.Xmas && Every(m, 0.4f, 0.8f) > 0f) rot = AA(Mathf.Sin(m.anim * 50f) * 1.5f, f) * rot;
                }
            }
            if (m.airborne)
            {
                // [M] ホバリングするトンボやハエは、羽音に合わせて体がこまかくふるえる
                if (m.sp.kind == MobKind.Hover && m.bid != "firefly") bob += m.up * (0.004f * s * Mathf.Sin(m.anim * 70f + m.phase));
                // [M] 風が強いと、飛んでいる体が風にあおられてゆれる
                if ((m.sp.kind == MobKind.Flutter || m.sp.kind == MobKind.Hover || m.sp.kind == MobKind.Bird) && gust > 0.5f)
                    rot = AA(Mathf.Sin(m.anim * 3f + m.phase) * 5f * (gust - 0.5f) * 2f, f) * rot;
            }

            // ---- 種ごと ----
            switch (m.sp.id)
            {
                // ===== アリ =====
                case "ant":
                    // [M] アリ：歩くとき、触角でさぐるように、頭を小さく左右にふる
                    rot = AA(Mathf.Sin(m.gait * 0.5f + m.phase) * 3f * moving, m.up) * rot;
                    // [M] アリ：食べものを運ぶときは、重そうに体を左右にゆらす
                    if (m.carrying) rot = AA(Mathf.Sin(m.gait) * 3f * moving, f) * rot;
                    // [M] アリ：坂を上るときは、体を低くしてふんばる
                    if (moving > 0.1f && Vector3.Dot(f, Vector3.up) > 0.2f) scale3.y *= 0.95f;
                    break;
                case "ant_helmet":
                    // [M] ヘルメットアリ：ヘルメットの重みで、ときどき頭がこくりと下がる
                    if (still) rot = AA(4f * Every(m, 0.6f, 0.7f), right) * rot;
                    break;

                // ===== かたつむり =====
                case "snail":
                    // [M] かたつむり：歩くと、殻が前後にもゆっくりゆれる
                    if (m.moveSpeed > 0.01f) rot = AA(Mathf.Sin(m.anim * 1.2f + 1f) * 2f, right) * rot;
                    // [M] かたつむり：首をのばすときは、左右をさがすように向きを変える
                    {
                        float neck = ActEnvelope(m, ActStretch, out float nk);
                        if (neck > 0f) rot = AA(Mathf.Sin(nk * Mathf.PI * 2f) * 10f * neck, m.up) * rot;
                    }
                    break;
                case "riversnail":
                    // [M] カワニナ：止まっていると、水の流れで殻がゆっくり左右にゆれる
                    if (still) rot = AA(Mathf.Sin(m.anim * 0.8f + m.phase) * 2.5f, f) * rot;
                    // [M] カワニナ：殻から出るときは、体を左右にゆすりながら出てくる
                    if (m.retreat <= 0f && m.emerge < 1f) rot = AA(Mathf.Sin(m.anim * 6f) * 4f * (1f - m.emerge), f) * rot;
                    break;

                // ===== チョウ・ハチ =====
                case "butterfly":
                case "monshiro":
                case "asagimadara":
                    if (m.airborne)
                    {
                        if (m.sp.id == "butterfly")
                        {
                            // [M] チョウ：飛ぶとき、ひらひらと左右にもゆれる
                            rot = AA(Mathf.Sin(m.anim * 4f + m.phase) * 8f, f) * rot;
                            // [M] チョウ：上へ上がるときは、体を立ててはばたく
                            if (m.vel.y > 0.3f) rot = AA(-10f * Mathf.Clamp01(m.vel.y - 0.3f), right) * rot;
                            // [M] チョウ：とまる直前は、体を起こして、脚を前へ出すように
                            if (m.flapBoost > 0f && m.vel.y < 0f) rot = AA(-8f * m.flapBoost, right) * rot;
                        }
                        else if (m.sp.id == "monshiro")
                        {
                            // [M] モンシロチョウ：小さくジグザグにゆれながら飛ぶ
                            bob += right * (Mathf.Sin(m.anim * 5f + m.phase) * 0.03f * s);
                        }
                        else
                        {
                            // [M] アサギマダラ：ゆったり滑空して、体がゆっくり上下にかたむく
                            rot = AA(Mathf.Sin(m.anim * 0.9f + m.phase) * 4f, right) * rot;
                        }
                    }
                    else
                    {
                        // [M] モンシロチョウ：とまると、体を少し横へかたむけて日を受ける
                        if (m.sp.id == "monshiro") rot = AA(6f * idle, f) * rot;
                        // [M] アサギマダラ：とまると、頭を下へ向けて羽を立てる
                        if (m.sp.id == "asagimadara") rot = AA(6f * idle, right) * rot;
                        // 日なたぼっこ：体を少しそらす（羽を開くのは Draw で）
                        float bask = ActEnvelope(m, ActStretch, out _);
                        if (bask > 0f) rot = AA(-4f * bask, right) * rot;
                    }
                    break;
                case "maruhanabachi":
                    if (m.airborne)
                    {
                        // [M] マルハナバチ：ずんぐりした体で、重そうに上下しながら飛ぶ
                        bob += m.up * (Mathf.Sin(m.anim * 3f + m.phase) * 0.04f * s);
                    }
                    else
                    {
                        // [M] マルハナバチ：とまると、おなかをのびちぢみさせて息をする
                        scale3.z *= 1f + 0.03f * Mathf.Sin(m.anim * 6f + m.phase);
                        // [M] マルハナバチ：花の上で、ときどき体を左右にゆすって花粉を集める
                        rot = AA(Mathf.Sin(m.anim * 8f) * 4f * Every(m, 0.5f, 0.6f), f) * rot;
                        // 顔の花粉をはらう：頭を小さく上下
                        float g = ActEnvelope(m, ActGroom, out float gk);
                        if (g > 0f) rot = AA(Mathf.Sin(gk * Mathf.PI * 8f) * 4f * g, right) * rot;
                    }
                    break;

                // ===== オトシブミのなかま =====
                case "otoshibumi":
                {
                    // [M] オトシブミ：歩くとき、長い首でうなずくように頭を上下
                    rot = AA(Mathf.Sin(m.gait * 2f) * 3f * moving, right) * rot;
                    // 前脚で頭をこする：頭を下げて、小さくゆする
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(6f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 2f * g, right) * rot;
                    break;
                }
                case "maimaikaburi":
                {
                    // [M] マイマイカブリ：歩くとき、細長い頭を左右にふって、かたつむりをさがす
                    rot = AA(Mathf.Sin(m.gait * 0.5f + m.phase) * 5f * moving, m.up) * rot;
                    // 首をのばして、においをさぐる
                    float st = ActEnvelope(m, ActStretch, out float sk);
                    if (st > 0f)
                    {
                        scale3.z *= 1f + 0.06f * st;
                        rot = AA(-6f * st, right) * AA(Mathf.Sin(sk * Mathf.PI * 2f) * 8f * st, m.up) * rot;
                    }
                    break;
                }

                // ===== バッタ・コオロギのなかま =====
                case "grasshopper":
                    // [M] バッタ：跳ぶ前に、体を左右にふってねらいを定める
                    if (m.launchAt >= 0f) rot = AA(Mathf.Sin(m.anim * 15f) * 3f * m.crouch, m.up) * rot;
                    // [M] バッタ：空中では、体を少しひねる
                    if (m.airborne) rot = AA(Mathf.Sin(Mathf.PI * Mathf.Clamp01(m.t / Mathf.Max(0.05f, m.dur))) * 8f * (m.phase > 3.14f ? 1f : -1f), f) * rot;
                    break;
                case "suzumushi":
                    // [M] スズムシ：鳴くときは、体を少し持ち上げる
                    rot = AA(-4f * m.wingLift, right) * rot;
                    goto case "korogi_groom";
                case "korogi":
                    // [M] コオロギ：鳴くとき、体を左右に小さくゆする
                    rot = AA(Mathf.Sin(m.anim * 8f) * 2f * m.wingLift, f) * rot;
                    // [M] コオロギ：おどろくと、体を低くして固まる
                    if (m.wary > 0f) scale3.y *= 1f - 0.05f * m.wary;
                    goto case "korogi_groom";
                case "korogi_groom":
                {
                    // 触角の手入れ：頭を下げて、小さくゆする
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(5f * g + Mathf.Sin(gk * Mathf.PI * 10f) * 2f * g, right) * rot;
                    break;
                }
                case "kirigirisu":
                    // [M] キリギリス：歩くとき、長い後ろ足で体を高く持ち上げる
                    if (moving > 0.05f) bob += m.up * (0.01f * s * moving);
                    // [M] キリギリス：鳴くとき、体を少し前へかたむける
                    rot = AA(3f * m.wingLift, right) * rot;
                    break;

                // ===== カエル =====
                case "frog":
                    if (m.airborne) break;
                    // [M] カエル：着地した直後は、前足をつっぱって頭を上げる
                    if (m.crouch > 0f && m.launchAt < 0f) rot = AA(-5f * m.crouch, right) * rot;
                    // [M] カエル：ときどき、まばたきするように体をきゅっとすくめる
                    if (still) scale3.y *= 1f - 0.03f * m.twitch;
                    // [M] カエル：横を通るしゃくとりむしを、体ごとゆっくり向いて追う
                    if (m.curious > 0f)
                    {
                        float a = Vector3.SignedAngle(f, Vector3.ProjectOnPlane(toHead, m.up), m.up);
                        rot = AA(Mathf.Clamp(a, -25f, 25f) * m.curious, m.up) * rot;
                    }
                    break;

                // ===== 小鳥 =====
                case "sparrow":
                    if (m.airborne) break;
                    // [M] スズメ：歩くとき、尾をぴこぴこ上下
                    rot = AA(Mathf.Sin(m.anim * 12f) * 2f * moving, right) * rot;
                    // [M] スズメ：着地の瞬間に、尾を上げてバランスをとる
                    if (m.anim < m.wingSettleUntil) rot = AA(6f * Mathf.Clamp01((m.wingSettleUntil - m.anim) / 0.35f), right) * rot;
                    // [M] スズメ：跳ねるたびに、体を少し前へたおす
                    rot = AA(5f * SparrowHop(m, moving), right) * rot;
                    break;
                case "tsubame":
                    if (m.airborne)
                    {
                        // [M] ツバメ：すばやく向きを変えるときは、体を大きくかたむける
                        rot = AA(Mathf.Clamp(-m.turnRate * 0.08f, -15f, 15f), f) * rot;
                        break;
                    }
                    // [M] ツバメ：とまると、長い尾をゆっくり上下させる
                    rot = AA(Mathf.Sin(m.anim * 1.5f + m.phase) * 2f, right) * rot;
                    break;
                case "shimaenaga":
                    if (m.airborne) break;
                    // [M] シマエナガ：ちょこちょこと、体ごと左右に向きを変える
                    if (still) rot = AA(Mathf.Round(Mathf.Sin(m.anim * 0.9f + m.phase) * 2f) * 7f, m.up) * rot;
                    // [M] シマエナガ：寒いときは、羽をふくらませたまま、こまかくふるえる
                    if (_area == Areas.Xmas && m.anim < m.puffUntil) rot = AA(Mathf.Sin(m.anim * 40f) * 1f, f) * rot;
                    // [M] シマエナガ：長い尾を、ときどきぴょこぴょこ動かす
                    rot = AA(Mathf.Sin(m.anim * 6f) * 2f * Every(m, 0.5f, 0.5f), right) * rot;
                    break;
                case "robin":
                    if (m.airborne) break;
                    // [M] コマドリ：止まると、胸を張ってさえずるかまえ
                    rot = AA(-5f * idle, right) * rot;
                    // [M] コマドリ：ときどき尾を上げ下げする
                    rot = AA(Mathf.Sin(m.anim * 3f) * 2.5f * Every(m, 0.7f, 0.4f), right) * rot;
                    break;

                // ===== カラス・カモメ・ハトのなかま =====
                case "crow":
                    if (m.airborne) break;
                    // [M] カラス：歩くとき、首を前後にふって進む
                    bob += f * (Mathf.Sin(m.gait * 2f) * 0.02f * s * moving);
                    // [M] カラス：警戒すると、首をのばして体を細くする
                    if (m.alertK > 0f) scale3 = Vector3.Scale(scale3, new Vector3(1f - 0.03f * m.alertK, 1f, 1f + 0.03f * m.alertK));
                    // [M] カラス：鳴いたあと、首をかしげる
                    {
                        float after = m.anim - m.callUntil;
                        if (m.callUntil > 0f && after > 0f && after < 0.8f) rot = AA(8f * Mathf.Sin(Mathf.PI * after / 0.8f), f) * rot;
                    }
                    break;
                case "kamome":
                    if (m.airborne)
                    {
                        // [M] カモメ：はばたかずに風にのって、体をゆっくり左右にかたむける
                        rot = AA(Mathf.Sin(m.anim * 0.6f + m.phase) * 6f, f) * rot;
                        break;
                    }
                    // [M] カモメ：地上では、胸を張って大またで歩く
                    rot = AA(-4f * moving, right) * rot;
                    // [M] カモメ：風が強いと、体を低くして風にたえる
                    if (gust > 0.6f && still) scale3.y *= 0.95f;
                    break;
                case "flamingo":
                    if (m.airborne) break;
                    {
                        // [M] フラミンゴ：片足で立つときは、体を少しかたむけてバランスをとる
                        float walk = Mathf.Clamp01(moving * 2f);
                        float one = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.6f, Mathf.Sin(m.anim * 0.11f + m.phase))) * (1f - walk);
                        rot = AA(4f * one + Mathf.Sin(m.anim * 1.3f) * 1f * one, f) * rot;
                        // [M] フラミンゴ：歩くとき、ゆっくり大きく体が前後にゆれる
                        rot = AA(Mathf.Sin(m.gait) * 3f * walk, right) * rot;
                    }
                    break;
                case "hato":
                    if (m.airborne) break;
                    // [M] ハト：飛び立つ前に、体を低くして首を上げる
                    if (m.launchAt >= 0f) rot = AA(-6f * m.crouch, right) * rot;
                    // [M] ハト：日なたで長く休むと、羽をふくらませて体を低く
                    if (m.idleFor > 8f) scale3 = Vector3.Scale(scale3, new Vector3(1.04f, 0.95f, 1f));
                    break;
                case "raichou":
                    if (m.airborne) break;
                    // [M] ライチョウ：歩くとき、ずんぐりした体がよちよち左右にゆれる
                    rot = AA(Mathf.Sin(m.gait) * 5f * moving, f) * rot;
                    // [M] ライチョウ：止まると、首をすくめてまるくなる
                    if (m.idleFor > 4f) scale3 = Vector3.Scale(scale3, new Vector3(1.03f, 0.96f, 1f));
                    // [M] ライチョウ：おどろくと、首をのばして固まる
                    if (m.wary > 0f) { rot = AA(-8f * m.wary, right) * rot; scale3.y *= 1f + 0.1f * m.wary; }
                    break;
                case "fukurou":
                    if (m.airborne) break;
                    // [M] フクロウ：止まっているとき、体をゆっくり左右にゆする
                    rot = AA(Mathf.Sin(m.anim * 0.6f + m.phase) * 2f, f) * rot;
                    // [M] フクロウ：しゃくとりむしが近いと、羽をふくらませて体を大きく見せる
                    if (dHead < 4f) scale3 *= 1f + 0.05f * Mathf.Clamp01((4f - dHead) / 2f);
                    break;
                case "manta":
                    if (m.leaping) break;
                    // [M] マンタ：泳ぐとき、体全体をゆっくり波打たせて上下する
                    bob += Vector3.up * (Mathf.Sin(m.anim * 1f + m.phase) * 0.3f * s);
                    // [M] マンタ：むなびれを打つたびに、体が少し前へ出る
                    bob += f * (Mathf.Max(0f, Mathf.Sin((m.anim + m.phase) * 2f)) * 0.15f * s);
                    break;

                // ===== コウモリ =====
                case "koumori":
                    if (!m.airborne) break;
                    // [M] コウモリ：はばたきに合わせて、体が大きく上下する
                    bob -= m.up * (Mathf.Sin((m.anim + m.phase) * 8f * Mathf.PI * 2f) * 0.03f * s);
                    // [M] コウモリ：虫を追って、ジグザグに体をかたむける
                    rot = AA(Mathf.Sin(m.anim * 4f + m.phase) * 10f, f) * rot;
                    break;

                // ===== てんとうむし・だんごむし =====
                case "ladybug":
                    // [M] てんとうむし：歩くとき、丸い体が小きざみに左右へゆれる
                    rot = AA(Mathf.Sin(m.gait * 2f) * 2f * moving, f) * rot;
                    // [M] てんとうむし：高い所でくるくる回るときは、体を上げて飛ぶかまえ
                    if (m.anim < m.spinUntil) rot = AA(-6f, right) * rot;
                    // [M] てんとうむし：死んだふりから起きるときは、体をゆらして起き上がる
                    if (m.deadUntil > 0f && m.anim - m.deadUntil < 0.6f) rot = AA(Mathf.Sin(m.anim * 20f) * 8f * (1f - (m.anim - m.deadUntil) / 0.6f), f) * rot;
                    break;
                case "pillbug":
                    // [M] だんごむし：歩くとき、体節がのびちぢみする
                    scale3.z *= 1f + 0.03f * Mathf.Sin(m.gait * 2f) * moving;
                    // [M] だんごむし：止まると、触角でさぐるように頭を上下させる
                    if (still) rot = AA(Mathf.Sin(m.anim * 4f + m.phase) * 2f, right) * rot;
                    break;
                case "harinezumi":
                    // [M] ハリネズミ：歩くとき、体を左右にゆらして、よちよち
                    rot = AA(Mathf.Sin(m.gait) * 4f * moving, f) * rot;
                    // [M] ハリネズミ：おどろくと、ぎゅっと丸くなりかける
                    if (m.wary > 0f) { scale3.z *= 1f - 0.08f * m.wary; rot = AA(8f * m.wary, right) * rot; }
                    break;

                // ===== カブトムシ・クワガタ =====
                case "beetle":
                    // [M] カブトムシ：歩くとき、重い体がのっしのっしと上下する
                    bob += m.up * (Mathf.Abs(Mathf.Sin(m.gait)) * 0.015f * s * moving);
                    // [M] カブトムシ：止まっていると、ときどき体を持ち上げて、においをかぐ
                    if (still) rot = AA(-4f * Every(m, 0.4f, 0.6f), right) * rot;
                    // [M] カブトムシ：坂を上るときは、角で体をささえるように前へかたむく
                    if (moving > 0.1f && Vector3.Dot(f, Vector3.up) > 0.2f) rot = AA(5f, right) * rot;
                    break;
                case "herakuresu":
                    // [M] ヘラクレスオオカブト：長い角を、歩くたびに上下にゆらす
                    rot = AA(Mathf.Sin(m.gait * 2f) * 2.5f * moving, right) * rot;
                    // [M] ヘラクレスオオカブト：止まると、角の重みで頭を下げてやすむ
                    if (still) rot = AA(4f * idle, right) * rot;
                    break;
                case "kuwagata":
                    // [M] クワガタ：歩くときは、体を低くして進む
                    if (moving > 0.05f) scale3.y *= 0.96f;
                    // [M] クワガタ：おどすときは、少しうしろへ下がって、大あごを向ける
                    if (m.display > 0.3f) bob -= f * (0.03f * m.display * s);
                    break;

                // ===== カミキリ・セミ・カメムシ =====
                case "kamikiri":
                {
                    // [M] カミキリ：止まっているとき、体を左右にゆっくりゆする
                    if (still) rot = AA(Mathf.Sin(m.anim * 0.9f + m.phase) * 2f, f) * rot;
                    // 触角のそうじ：頭を下げて、ゆする
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(7f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 2f * g, right) * rot;
                    break;
                }
                case "higurashi":
                {
                    // [M] ヒグラシ：鳴いている間は、体を少し持ち上げる
                    if (m.anim < m.callUntil) rot = AA(-5f, right) * rot;
                    // [M] ヒグラシ：鳴きおわると、体をぶるっとふるわせる
                    float after = m.anim - m.callUntil;
                    if (m.callUntil > 0f && after > 0f && after < 0.4f) rot = AA(Mathf.Sin(m.anim * 40f) * 4f * (1f - after / 0.4f), f) * rot;
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(5f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 2f * g, right) * rot;
                    break;
                }
                case "kamemushi":
                {
                    // [M] カメムシ：歩くとき、平たい体を左右にふる
                    rot = AA(Mathf.Sin(m.gait * 0.5f + m.phase) * 3f * moving, m.up) * rot;
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(6f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 2f * g, right) * rot;
                    break;
                }

                // ===== トカゲのなかま =====
                case "tokage":
                    // [M] トカゲ：止まると、首をかしげて、ときどきさっとまわりを見る
                    if (still) rot = AA(Mathf.Round(Mathf.Sin(m.anim * 0.7f + m.phase) * 1.5f) * 6f, m.up) * rot;
                    // [M] トカゲ：日なたで長く休むと、体を平たくして地面にふせる
                    if (m.idleFor > 6f) scale3 = Vector3.Scale(scale3, new Vector3(1.04f, 0.93f, 1f));
                    // [M] トカゲ：走りだす瞬間に、体を低くする
                    if (m.accel > 0.5f) bob -= m.up * (0.01f * s);
                    break;
                case "kameleon":
                    // [M] カメレオン：体を左右にゆっくりかたむけて、枝のゆれにまぎれる
                    rot = AA(Mathf.Sin(m.anim * 0.7f + m.phase) * 4f, f) * rot;
                    // [M] カメレオン：おどろくと、体を横に平たくして、大きく見せる
                    if (m.wary > 0f) scale3 = Vector3.Scale(scale3, new Vector3(1f + 0.08f * m.wary, 1f + 0.05f * m.wary, 1f));
                    break;
                case "yamori":
                    // [M] ヤモリ：かべでは、頭を上げて止まる
                    if (still && m.up.y < 0.5f) rot = AA(-6f * idle, right) * rot;
                    // [M] ヤモリ：走りだす前に、しっぽをくねらせる
                    if (m.accel > 0.1f) rot = AA(Mathf.Sin(m.anim * 8f) * 3f, m.up) * rot;
                    break;
                case "risu":
                    // [M] リス：止まると、ときどきしっぽをふるわせる
                    if (still) rot = AA(Mathf.Sin(m.anim * 20f) * 1f * Every(m, 0.6f, 0.7f), f) * rot;
                    // [M] リス：走るとき、跳ねるたびに体を前へたおす
                    rot = AA(Mathf.Sin(m.gait) * 6f * moving, right) * rot;
                    // [M] リス：木の幹にとまると、首を左右にふってまわりを見る
                    if (still && m.up.y < 0.5f) rot = AA(Mathf.Sin(m.anim * 1.1f + m.phase) * 10f, m.up) * rot;
                    break;
                case "nezumi":
                    // [M] ネズミ：止まると、鼻をひくひく
                    if (still) rot = AA(Mathf.Sin(m.anim * 14f) * 1.5f, right) * rot;
                    // [M] ネズミ：走るときは、体を低く平たくする
                    if (moving > 0.2f) scale3.y *= 0.92f;
                    break;
                case "funamushi":
                    // [M] フナムシ：止まると、平たい体を岩にぴったりつける
                    if (still) scale3.y *= 0.95f;
                    // [M] フナムシ：走るとき、すばやく左右にくねる
                    rot = AA(Mathf.Sin(m.gait) * 5f * moving, m.up) * rot;
                    break;

                // ===== ウサギのなかま =====
                case "nakiusagi":
                    // [M] ナキウサギ：止まると、まるくなって、ひげをぴくぴく
                    if (still) rot = AA(Mathf.Sin(m.anim * 12f) * 1f, right) * rot;
                    // [M] ナキウサギ：跳ねて着地すると、体がぷるんとゆれる
                    if (!m.airborne && m.crouch > 0f) scale3.x *= 1f + 0.04f * m.crouch;
                    break;
                case "yukiusagi":
                    // [M] ユキウサギ：耳を立てるときは、首をのばす
                    if (!m.airborne && m.alertK > 0f) rot = AA(-6f * m.alertK, right) * rot;
                    // [M] ユキウサギ：跳ぶ前に、体をぐっとちぢめる
                    if (m.launchAt >= 0f) scale3.y *= 1f - 0.1f * m.crouch;
                    // [M] ユキウサギ：雪の上で休むと、まるくなる
                    if (m.idleFor > 5f && m.alertK <= 0f) scale3 = Vector3.Scale(scale3, new Vector3(1.04f, 0.95f, 0.97f));
                    break;

                // ===== クモ・カマキリ =====
                case "spider":
                    // [M] クモ：歩くときは、体を低くして、すばやく
                    if (moving > 0.05f) scale3.y *= 0.96f;
                    // [M] クモ：止まっていると、おなかをゆっくり上下させて息をする
                    if (still) bob += m.up * (Mathf.Sin(m.anim * 3f + m.phase) * 0.004f * s);
                    break;
                case "spider_sneaker":
                    // [M] スニーカーグモ：歩くとき、体を左右にはずませる
                    rot = AA(Mathf.Sin(m.gait * 2f) * 3f * moving, f) * rot;
                    break;
                case "mantis":
                case "hanakamakiri":
                {
                    if (m.sp.id == "mantis")
                    {
                        // [M] カマキリ：えものをねらうとき、体を左右にゆらして、きょりをはかる
                        if (m.raise > 0.3f) bob += right * (Mathf.Sin(m.anim * 2f + m.phase) * 0.02f * s * m.raise);
                        // [M] カマキリ：かまをくり出したあとは、体を引いてもどす
                        if (m.strike > 0f) bob -= f * (0.02f * s * m.strike);
                    }
                    else
                    {
                        // [M] ハナカマキリ：花のように、体を前後にゆっくりゆらす
                        rot = AA(Mathf.Sin(m.anim * 0.8f + m.phase) * 2f, right) * rot;
                        // [M] ハナカマキリ：しゃくとりむしが近いと、体を低くして待ちぶせる
                        if (m.raise > 0f) scale3.y *= 1f - 0.05f * m.raise;
                    }
                    // 首をかしげて、まわりを見る
                    float look = ActEnvelope(m, ActLook, out float lk);
                    if (look > 0f) rot = AA(Mathf.Sin(lk * Mathf.PI * 2f) * 12f * look, f) * rot;
                    break;
                }

                // ===== トンボ・ハエ・ホタル =====
                case "dragonfly":
                    // [M] トンボ：とまっているとき、風で体がゆれる
                    if (m.perched) rot = AA(Mathf.Sin(m.anim * 2.2f + m.phase) * 2f * (0.3f + gust), f) * rot;
                    // [M] トンボ：えものを追うときは、前へ大きくかたむく
                    if (m.airborne && m.prey != null && m.anim < m.preyUntil) rot = AA(8f, right) * rot;
                    break;
                case "oniyanma":
                    // [M] オニヤンマ：見回りのとき、ときどき空中で止まって、体を小さく上下させる
                    if (m.airborne) bob += m.up * (Mathf.Sin(m.anim * 2.5f + m.phase) * 0.05f * s * Every(m, 0.3f, 0.3f));
                    // [M] オニヤンマ：とまると、体をぶら下げるように、少し下を向く
                    if (m.perched) rot = AA(6f, right) * rot;
                    break;
                case "hae":
                    // [M] ハエ：とまっていると、ときどき、さっと向きを変える
                    if (!m.airborne) rot = AA(Mathf.Round(Mathf.Sin(m.anim * 0.5f + m.phase) * 2f) * 12f, m.up) * rot;
                    // [M] ハエ：飛ぶとき、こまかくジグザグにゆれる
                    if (m.airborne) bob += right * (Mathf.Sin(m.anim * 9f + m.phase) * Mathf.Sin(m.anim * 3.7f) * 0.04f * s);
                    break;
                case "firefly":
                    // [M] ホタル：飛ぶとき、ゆっくり上下にただよう
                    if (m.airborne) bob += Vector3.up * (Mathf.Sin(m.anim * 1.5f + m.phase) * 0.05f * s);
                    // [M] ホタル：光っていないときは、おしりを少し下げる
                    rot = AA(-3f * (1f - FireflyGlow(m)), right) * rot;
                    break;

                // ===== 水のいきもの =====
                case "waterstrider":
                    // [M] アメンボ：止まっているとき、波で体がゆっくり回る
                    rot = AA(Mathf.Sin(m.anim * 0.5f + m.phase) * 4f, m.up) * rot;
                    // [M] アメンボ：水面をたたいて合図するとき、体を少し前へ
                    rot = AA(4f * ActEnvelope(m, ActSpecial, out _), right) * rot;
                    break;
                case "gengorou":
                    // [M] ゲンゴロウ：こぐたびに、体を左右にゆらしながら泳ぐ
                    rot = AA(Mathf.Sin(m.rowPhase * Mathf.PI * 2f) * 4f, f) * rot;
                    // [M] ゲンゴロウ：水面で息つぎしているとき、体がゆっくり回る
                    if (m.dive < 0.15f) rot = AA(Mathf.Sin(m.anim * 0.7f + m.phase) * 5f, m.up) * rot;
                    break;
                case "crab":
                    // [M] カニ：横歩きのとき、体を上下にかくかくゆらす
                    bob += m.up * (Mathf.Abs(Mathf.Sin(m.gait * 2f)) * 0.01f * s * moving);
                    // [M] カニ：おどろくと、体をふせて、はさみをかまえる
                    if (m.wary > 0f) scale3.y *= 1f - 0.1f * m.wary;
                    // [M] カニ：止まっていると、ときどき口もとをぶくぶく動かす
                    if (still) scale3.x *= 1f + 0.01f * Mathf.Sin(m.anim * 20f) * Every(m, 0.5f, 0.6f);
                    break;
                case "shiomaneki":
                    // [M] シオマネキ：はさみをふるとき、体ごと上下する
                    {
                        float wave = ActEnvelope(m, ActSpecial, out float wk);
                        if (wave > 0f) bob += m.up * (Mathf.Abs(Mathf.Sin(wk * Mathf.PI * 4f)) * 0.02f * s * wave);
                    }
                    // [M] シオマネキ：巣穴の近くで止まると、体を低くする
                    if (still) scale3.y *= 0.96f;
                    break;
                case "yadokari":
                    // [M] ヤドカリ：止まると、殻をゆっくり左右にふって、外を見る
                    if (still && m.retreat <= 0f) rot = AA(Mathf.Sin(m.anim * 0.9f + m.phase) * 4f, m.up) * rot;
                    // [M] ヤドカリ：殻から出るときは、殻がぐらっとゆれる
                    if (m.retreat <= 0f && m.emerge > 0.6f && m.emerge < 1f) rot = AA(Mathf.Sin(m.anim * 10f) * 3f * (1f - m.emerge) / 0.4f, f) * rot;
                    break;
                case "hitode":
                    // [M] ヒトデ：体全体が、ゆっくり上下にうねる
                    bob += m.up * (Mathf.Sin(m.anim * 0.5f + m.phase) * 0.01f * s);
                    // [M] ヒトデ：しゃくとりむしが近いと、体を少しちぢめる
                    scale3 *= 1f - 0.03f * m.display;
                    break;
                case "uni":
                    // [M] ウニ：体がゆっくり左右に回る
                    rot = AA(Mathf.Sin(m.anim * 0.3f + m.phase) * 6f, m.up) * rot;
                    // [M] ウニ：波で、体が少しゆれる
                    rot = AA(Mathf.Sin(m.anim * 0.9f + m.phase) * 2f, f) * rot;
                    break;
                case "sanshouuo":
                    // [M] サンショウウオ：止まっていると、のどをひくひく動かす
                    if (still) scale3.y *= 1f + 0.015f * Mathf.Sin(m.anim * 7f + m.phase);
                    // [M] サンショウウオ：止まると、体を地面にぺたりとふせる
                    if (m.idleFor > 3f) scale3.y *= 0.95f;
                    break;

                // ===== 土の中のいきもの =====
                case "okera":
                {
                    // [M] ケラ：歩くときは、体を低くして進む
                    if (moving > 0.05f) scale3.y *= 0.95f;
                    // [M] ケラ：土から出たばかりのときは、体を左右にゆすって土を落とす
                    if (m.dig < 0.02f && still && m.idleFor < 1.5f) rot = AA(Mathf.Sin(m.anim * 18f) * 4f * (1f - m.idleFor / 1.5f), f) * rot;
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(6f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 2f * g, right) * rot;
                    break;
                }
                case "mogura":
                    // [M] モグラ：顔を出しているとき、鼻を上下にもふる
                    if (m.dig < 0.3f) rot = AA(Mathf.Sin(m.anim * 5f) * 4f * (1f - m.dig / 0.3f), right) * rot;
                    // [M] モグラ：もぐる前に、体をぶるっとふるわせる
                    if (m.digDown && m.dig > 0.02f && m.dig < 0.2f) rot = AA(Mathf.Sin(m.anim * 30f) * 4f, f) * rot;
                    break;
                case "okojo":
                    // [M] オコジョ：立ち上がると、体を左右にゆらしてバランスをとる
                    rot = AA(Mathf.Sin(m.anim * 1.5f + m.phase) * 3f * (1f - m.dig), f) * rot;
                    // [M] オコジョ：しゃくとりむしが近いと、首をのばして見つめる
                    if (dHead < 6f) rot = AA(-6f * (1f - m.dig), right) * rot;
                    break;

                // ===== 細長い虫 =====
                case "nanafushi":
                {
                    // [M] ナナフシ：歩くとき、体を高く持ち上げる
                    if (moving > 0.05f) bob += m.up * (0.01f * s);
                    // [M] ナナフシ：おどろくと、体をぴんとのばして固まる
                    if (m.wary > 0f) scale3.z *= 1f + 0.05f * m.wary;
                    float sway = ActEnvelope(m, ActSpecial, out float sk);
                    if (sway > 0f) bob += f * (Mathf.Sin(sk * Mathf.PI * 4f) * 0.05f * s * sway);
                    break;
                }
                case "mukade":
                    // [M] ムカデ：止まっているとき、体をゆっくりうねらせる
                    if (still) rot = AA(Mathf.Sin(m.anim * 0.8f + m.phase) * 3f, m.up) * rot;
                    break;
                case "hasamimushi":
                    // [M] ハサミムシ：歩くとき、体を左右にくねらせる
                    rot = AA(Mathf.Sin(m.gait) * 4f * moving, m.up) * rot;
                    // [M] ハサミムシ：おどすときは、体をうしろへ下げる
                    if (m.display > 0f) bob -= f * (0.02f * m.display * s);
                    break;

                // ===== 神社・夜の森のけもの =====
                case "kitsune":
                    // [M] キツネ：歩くとき、しなやかに体を上下させる
                    bob += m.up * (Mathf.Abs(Mathf.Sin(m.gait)) * 0.02f * s * moving);
                    // [M] キツネ：走るときは、体をのばして低くする
                    if (moving > 0.6f) scale3 = Vector3.Scale(scale3, new Vector3(1f, 0.95f, 1.03f));
                    // [M] キツネ：休むと、すわるように体のうしろを下げる
                    if (m.idleFor > 4f && m.act == 0) rot = AA(-5f * Mathf.Clamp01((m.idleFor - 4f) / 1.5f), right) * rot;
                    break;
                case "tanuki":
                    // [M] タヌキ：歩くとき、ずんぐりした体がよちよち左右にゆれる
                    rot = AA(Mathf.Sin(m.gait) * 4f * moving, f) * rot;
                    // [M] タヌキ：休むと、まるくなって体を低く
                    if (m.idleFor > 6f) scale3.y *= 0.96f;
                    break;
                case "kanabun":
                {
                    // [M] カナブン：歩くとき、つやつやの体を小さく左右にゆらす
                    rot = AA(Mathf.Sin(m.gait * 2f) * 2f * moving, f) * rot;
                    // [M] カナブン：前脚で顔をこするときは、頭を下げてゆする
                    float g = ActEnvelope(m, ActGroom, out float gk);
                    if (g > 0f) rot = AA(6f * g + Mathf.Sin(gk * Mathf.PI * 8f) * 2f * g, right) * rot;
                    break;
                }
                case "ga":
                case "fuyushaku":
                {
                    if (m.airborne)
                    {
                        // [M] ガ：飛ぶとき、ふらふらと不規則にゆれる
                        if (m.sp.id == "ga") rot = AA(Mathf.Sin(m.anim * 2.3f + m.phase) * 7f, f) * AA(Mathf.Sin(m.anim * 1.7f) * 5f, m.up) * rot;
                        // [M] フユシャク：雪の中を、ゆっくりふわふわ上下しながら飛ぶ
                        else bob += Vector3.up * (Mathf.Sin(m.anim * 1.2f + m.phase) * 0.04f * s);
                        break;
                    }
                    // [M] ガ：とまっているときは、体をかべにぴったりよせる
                    if (m.sp.id == "ga") bob -= m.up * (0.004f * s);
                    // [M] フユシャク：とまると、羽をたたんだまま体を小さく上下させる
                    else bob += m.up * (Mathf.Sin(m.anim * 1.6f + m.phase) * 0.003f * s);
                    // 体をふるわせて温める
                    float warm = ActEnvelope(m, ActShake, out _);
                    if (warm > 0f) rot = AA(Mathf.Sin(m.anim * 30f) * 2f * warm, f) * rot;
                    break;
                }
                case "minomushi":
                    // [M] ミノムシ：糸にぶら下がって、風でゆっくり回る
                    rot = AA(Mathf.Sin(m.anim * 0.4f + m.phase) * 15f, Vector3.up) * rot;
                    // [M] ミノムシ：顔を出すときは、みのを少し持ち上げる
                    rot = AA(-5f * Mathf.SmoothStep(0f, 1f, m.emerge), right) * rot;
                    break;
                case "zatoumushi":
                    // [M] ザトウムシ：歩くとき、高い所の体がゆらゆらゆれる
                    bob += m.up * (Mathf.Sin(m.gait * 2f) * 0.02f * s * moving);
                    // [M] ザトウムシ：止まると、体をゆっくり上下にゆらす
                    if (still && m.act == 0) bob += m.up * (Mathf.Sin(m.anim * 1.2f + m.phase) * 0.01f * s);
                    break;
                case "inoshishi":
                    // [M] イノシシ：歩くとき、頭を上下にふる
                    rot = AA(Mathf.Sin(m.gait * 2f) * 3f * moving, right) * rot;
                    // [M] イノシシ：走るときは、体を低くして前へ
                    if (moving > 0.6f) { rot = AA(4f, right) * rot; scale3.y *= 0.96f; }
                    // [M] イノシシ：止まると、鼻をひくひく
                    if (still && m.act == 0) rot = AA(Mathf.Sin(m.anim * 10f) * 1f, right) * rot;
                    break;
                case "uribou":
                    // [M] ウリボウ：おかあさんについて、ちょこちょこ小走り
                    bob += m.up * (Mathf.Abs(Mathf.Sin(m.gait * 2f)) * 0.03f * s * moving);
                    // [M] ウリボウ：止まると、ときどきしっぽをふる（おしりが小さくゆれる）
                    if (still) rot = AA(Mathf.Sin(m.anim * 12f) * 1.5f * Every(m, 0.8f, 0.5f), m.up) * rot;
                    break;

                // ===== クリスマス村 =====
                case "tonakai":
                    // [M] トナカイ：歩くとき、大きな角をゆっくり上下させる
                    rot = AA(Mathf.Sin(m.gait) * 2.5f * moving, right) * rot;
                    // [M] トナカイ：歩くとき、肩が左右にゆれる
                    rot = AA(Mathf.Sin(m.gait) * 2f * moving, f) * rot;
                    // [M] トナカイ：止まると、ときどき首をふって雪をはらう
                    if (still && m.act == 0) rot = AA(Mathf.Sin(m.anim * 6f) * 4f * Every(m, 0.25f, 0.8f), f) * rot;
                    break;
                case "yamane":
                    // [M] ヤマネ：寝ているとき、ときどき鼻をぴくっ
                    if (m.act == 0) rot = AA(2f * m.twitch, right) * rot;
                    break;
            }
            MoreMotion3(m, f, right, s, moving, walker, ref rot, ref scale3, ref bob);   // モーション 300（Creatures.Motion3.cs）
        }

        /// <summary>
        /// テスト用：いきものの形が、ここで足す動きで不自然にならないか。
        /// 足す前と後で、体の向きのちがいが maxAngle 度より小さく、大きさの比が 0.8〜1.2 なら true。
        /// </summary>
        public bool MorePosesNatural(float maxAngle, out string bad, out float biggest)
        {
            bad = null;
            biggest = 0f;
            foreach (var m in _mobs)
            {
                s_NoMoreMotion = true;
                BodyPose(m, out _, out _, out _, out _, out _, out var before);
                s_NoMoreMotion = false;
                BodyPose(m, out _, out _, out _, out _, out _, out var after);
                bool finite = true;
                for (int i = 0; i < 16; i++) if (float.IsNaN(after[i]) || float.IsInfinity(after[i])) finite = false;
                Vector3 b = before.lossyScale, a = after.lossyScale;
                float angle = Quaternion.Angle(before.rotation, after.rotation);
                biggest = Mathf.Max(biggest, angle);
                bool sizeOk = b.x > 1e-5f && b.y > 1e-5f && b.z > 1e-5f
                              && Mathf.Abs(a.x / b.x - 1f) < 0.2f && Mathf.Abs(a.y / b.y - 1f) < 0.2f && Mathf.Abs(a.z / b.z - 1f) < 0.2f;
                if (!finite || angle > maxAngle || !sizeOk)
                {
                    bad = $"{m.sp.id}: angle {angle:0.0} scale {a.x / b.x:0.00},{a.y / b.y:0.00},{a.z / b.z:0.00}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>テスト用：その種が、止まっているときに新しくする動き（PickActMore）を、何回か考えさせて集める。</summary>
        public static int NewActOf(string id, float r)
        {
            var sp = SpeciesCatalog.Get(id);
            if (sp == null) return -1;
            var m = new Mob { sp = sp, bid = sp.BehaviorId };
            return PickActMore(m, r);
        }
    }
}

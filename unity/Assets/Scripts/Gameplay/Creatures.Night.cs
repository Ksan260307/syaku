using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 夜の森のいきもの：ミノムシ（枝から 1 本の糸でぶらさがり、風やしゃくとりむしにふれると、糸ごとゆらゆらゆれる。
    /// しずかなときは、みのから顔を出し、近づかれると、ひっこむ）・ザトウムシ（長い脚で、ふわふわ歩く。そばに来られると、体を上下にゆらす）・
    /// キリギリス（草むらで「ギーッ、チョン」。鳴き方はスズムシ・コオロギと同じしくみ）・
    /// イノシシ（鼻で土をほりかえす。近づかれると、もうれつないきおいで走って逃げる）と、ウリボウ（おかあさんのうしろを、1 列についていく）・
    /// シマエナガ（スズメと同じ動き方。羽をふくらませて、まんまるになる。「ジュリリ」と鳴く）。
    /// </summary>
    public partial class Creatures
    {
        /// <summary>しゃくとりむしがこれより近づくと、ミノムシは、みのにひっこむ。</summary>
        public const float BagwormHush = 3f;

        /// <summary>夜の森の、地面を歩くいきもの（とミノムシ）。この回は、ここで動きをきめたら true。</summary>
        bool UpdateNightCrawler(Mob m, float dt, bool near, int index)
        {
            switch (m.sp.id)
            {
                case "minomushi":
                    UpdateBagworm(m, dt);
                    return true;
                case "zatoumushi":
                    // そばに来られると、止まって、体を上下にゆらす（しばらくは、しない）
                    if (!m.carryingWorm && m.anim > m.hopNext && (_head - m.pos).sqrMagnitude < 1.3f * 1.3f)
                    {
                        m.hopNext = m.anim + R(5f, 8f);
                        StartAct(m, ActSpecial, ActLength(m, ActSpecial), true);
                    }
                    if (m.act == ActSpecial)
                    {
                        m.speedMul = 0f;
                        m.timer = Mathf.Max(m.timer, 0.3f);
                        m.curSpeed = Mathf.MoveTowards(m.curSpeed, 0f, dt * 6f);   // 長い脚で、ぴたりと止まる
                    }
                    return false;
                case "inoshishi":
                    UpdateBoar(m, dt);
                    return false;
                case "uribou":
                    UpdatePiglet(m, dt);
                    return false;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // ミノムシ
        // ------------------------------------------------------------------
        /// <summary>ミノムシをつるす：すみか（枝の所）から、糸の長さ（群れの高さ）だけ下に、みのの上のはし。</summary>
        void StartBagworm(Mob m)
        {
            m.anchor = m.home;
            m.threadLen = m.height > 0f ? m.height : 1.4f;
            m.up = Vector3.up;
            m.pos = m.anchor + Vector3.down * m.threadLen;
            m.emerge = 0.5f;
            m.peekOut = true;
            m.timer = R(3f, 8f);
        }

        /// <summary>
        /// ミノムシ：糸でぶらさがった、ふりこ。風でゆっくりゆれ、しゃくとりむしがふれると、押された向きへ大きくゆれる。
        /// しずかなときは、ときどき、みのの口から顔を出して、きょろきょろ。近づかれると、みのにひっこむ。
        /// </summary>
        void UpdateBagworm(Mob m, float dt)
        {
            m.curSpeed = 0f;
            m.moveSpeed = 0f;
            Vector3 mid = m.pos - m.up * (0.8f * m.scale);   // みののまん中
            float dWorm = (_head - mid).magnitude;
            // ふりこ：ゆれは、糸の向きの横へのずれ（糸の長さを 1 として）。風の向きへ、少しかたむく
            Vector3 w = Wind.At(m.anchor);
            Vector2 rest = new Vector2(w.x, w.z) * 0.04f + new Vector2(Mathf.Sin(m.anim * 0.7f + m.phase), Mathf.Cos(m.anim * 0.53f + m.phase * 1.3f)) * 0.02f;
            const float omega = 2.6f, damping = 0.45f;
            m.swingV += (-(omega * omega) * (m.swingH - rest) - damping * m.swingV) * dt;
            // しゃくとりむしがふれると、押された向きへ
            float touch = 0.45f * m.scale + 0.3f;
            if (dWorm < touch)
            {
                Vector3 push = Vector3.ProjectOnPlane(mid - _head, Vector3.up);
                if (push.sqrMagnitude < 1e-6f) push = new Vector3(Mathf.Cos(m.phase), 0f, Mathf.Sin(m.phase));
                push.Normalize();
                m.swingV += new Vector2(push.x, push.z) * (2.2f * dt / Mathf.Max(0.3f, m.threadLen * 0.5f));
                m.retreat = 1f;
            }
            m.swingH += m.swingV * dt;
            if (m.swingH.magnitude > 0.7f)
            {
                m.swingH = m.swingH.normalized * 0.7f;
                m.swingV *= 0.5f;
            }
            Vector3 dir = new Vector3(m.swingH.x, -Mathf.Sqrt(Mathf.Max(0.05f, 1f - m.swingH.sqrMagnitude)), m.swingH.y).normalized;
            m.pos = m.anchor + dir * m.threadLen;
            m.up = -dir;
            // 糸のまわりに、ゆっくりまわる（糸がねじれて、もどる）
            Vector3 f0 = Quaternion.AngleAxis(Mathf.Sin(m.anim * 0.35f + m.phase) * 80f + m.phase * 57f, Vector3.up) * Vector3.forward;
            m.fwd = ShakuMath.ProjectOnPlaneSafe(f0, m.up, Vector3.right).normalized;
            m.wantFwd = m.fwd;
            // 顔を出す・ひっこむ
            bool forced = m.anim < m.peekUntil;
            if (!forced && dWorm < BagwormHush && !m.carryingWorm) m.retreat = Mathf.Max(m.retreat, 1f);
            else m.retreat = Mathf.MoveTowards(m.retreat, 0f, dt * 0.4f);
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                m.peekOut = !m.peekOut;
                m.timer = m.peekOut ? R(5f, 10f) : R(3f, 7f);
            }
            float want = forced ? 1f : m.retreat > 0.2f ? 0f : m.peekOut ? 1f : 0f;
            m.emerge = Mathf.MoveTowards(m.emerge, want, dt * (want < m.emerge ? 3f : 0.5f));
        }

        // ------------------------------------------------------------------
        // シマエナガ
        // ------------------------------------------------------------------
        /// <summary>
        /// シマエナガ：ねぐらの木の枝にとまったまま、ちょこんと向きを変えたり、羽をふくらませてまんまるになったり、「ジュリリ」と鳴いたりする。
        /// 近づかれると（ときどきは、ひとりでに）、ほかの枝や岩の上へ、ひくく飛んでいく。
        /// </summary>
        void UpdateTit(Mob m, float dt)
        {
            m.turnHop = Mathf.MoveTowards(m.turnHop, 0f, dt * 5f);
            if (m.airborne)
            {
                FlyBird(m, dt);
                return;
            }
            if (m.launchAt >= 0f)
            {
                m.crouch = Mathf.MoveTowards(m.crouch, 1f, dt * 10f);
                if (m.anim >= m.launchAt)
                {
                    m.launchAt = -1f;
                    TakeOff(m, false);
                    m.arc = R(2f, 4f);   // ひくく、ふわふわ
                }
                return;
            }
            m.crouch = Mathf.MoveTowards(m.crouch, 0f, dt * 3f);
            m.resting = false;
            m.timer -= dt;
            float flee = m.sp.fleeRadius * FearScale(m);
            Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
            float dWorm = (_head - m.pos).magnitude;
            bool alert = dWorm < flee * 1.8f;
            m.alertK = Mathf.MoveTowards(m.alertK, alert ? 1f : 0f, dt * 4f);
            if (dWorm < flee || m.timer <= 0f)
            {
                m.launchAt = m.anim + (dWorm < flee ? 0.1f : 0.4f);
                if (m.timer <= 0f) m.timer = R(20f, 40f);
                return;
            }
            if (m.anim > m.puffNext)
            {
                m.puffNext = m.anim + R(6f, 12f);
                m.puffUntil = m.anim + 2.6f;   // しばらく、まんまるのまま
            }
            if (alert && toW.sqrMagnitude > 1e-4f) m.fwd = ShakuPhysics.DampDir(m.fwd, toW, 4f, dt);
            else if (m.anim > m.hopNext)
            {
                // 枝の上で、ちょこんと向きを変える
                m.hopNext = m.anim + R(1.5f, 4f);
                m.fwd = (Quaternion.AngleAxis(R(-70f, 70f), Vector3.up) * m.fwd).normalized;
                m.turnHop = 1f;
            }
        }

        // ------------------------------------------------------------------
        // イノシシとウリボウ
        // ------------------------------------------------------------------
        /// <summary>イノシシ：近づかれると、もうれつないきおいで走って逃げる。鼻で土をほりかえしている間は、その場で。</summary>
        void UpdateBoar(Mob m, float dt)
        {
            Vector3 to = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
            float d = to.magnitude;
            float flee = m.sp.fleeRadius * FearScale(m);
            if (!m.carryingWorm && d < flee && d > 1e-3f)
            {
                if (m.speedMul < 1.5f)
                {
                    m.act = 0;
                    Snort(m, 0.9f);   // 「ブフッ」
                }
                m.wantFwd = -to / d;
                m.speedMul = 2.4f;
                m.timer = R(1.6f, 2.4f);
                return;
            }
            if (m.act == ActSpecial)
            {
                m.speedMul = 0f;
                m.timer = Mathf.Max(m.timer, 0.3f);
                if (R(0f, 1f) < dt * 0.25f) Snort(m, 0.45f);   // 土をほりながら、ときどき鼻をならす
            }
        }

        void Snort(Mob m, float volume)
        {
            if (!Application.isPlaying) return;
            Camera cam = Camera.main;
            float vol = cam != null ? Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, m.pos) / 40f) : 0.5f;
            if (vol > 0.05f) AreaSounds.Instance?.PlayAt(m.sp.id == "uribou" ? "piglet" : "boar", m.pos, volume * (0.3f + 0.7f * vol));
        }

        /// <summary>ウリボウがついていく、おかあさん（いちばん近いイノシシ）。</summary>
        Mob FindMother(Mob m)
        {
            Mob best = null;
            float bestD = 40f * 40f;
            foreach (var o in _mobs)
            {
                if (o.sp.id != "inoshishi") continue;
                float d2 = (o.pos - m.pos).sqrMagnitude;
                if (d2 < bestD) { bestD = d2; best = o; }
            }
            if (best != null)
            {
                int slot = 0;
                foreach (var o in _mobs)
                    if (o != m && o.leader == best) slot++;
                m.followSlot = slot;
            }
            return best;
        }

        /// <summary>
        /// ウリボウ：おかあさんのうしろの、自分の場所（1 列に、左右に少しずつずれて）へ、ちょこちょこついていく。
        /// おかあさんが走りだすと、いっしょに走る。近づかれると、あわてて、おかあさんの方へ逃げる。
        /// </summary>
        void UpdatePiglet(Mob m, float dt)
        {
            if (m.leader == null) m.leader = FindMother(m);
            var mom = m.leader;
            Vector3 to = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
            float d = to.magnitude;
            float flee = m.sp.fleeRadius * FearScale(m);
            bool momRuns = mom != null && mom.speedMul > 1.5f;
            if (!m.carryingWorm && d < flee && d > 1e-3f)
            {
                Vector3 away = -to / d;
                if (mom != null)
                {
                    Vector3 toMom = Vector3.ProjectOnPlane(mom.pos - m.pos, Vector3.up);
                    if (toMom.sqrMagnitude > 1f) away = (away + toMom.normalized * 0.8f).normalized;
                }
                if (m.speedMul < 1.5f)
                {
                    m.act = 0;
                    Snort(m, 0.6f);   // 「キュッ」
                }
                m.wantFwd = away;
                m.speedMul = 2.2f;
                m.timer = R(1f, 1.6f);
                return;
            }
            if (mom == null) return;
            if (m.act == ActSpecial && !momRuns)
            {
                m.speedMul = 0f;
                m.timer = Mathf.Max(m.timer, 0.2f);
                return;
            }
            Vector3 back = -ShakuMath.ProjectOnPlaneSafe(mom.fwd, Vector3.up, Vector3.forward).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            Vector3 spot = mom.pos + back * (5.5f + 3f * m.followSlot) + side * ((m.followSlot % 2 == 0 ? 1f : -1f) * 1.1f);
            Vector3 toSpot = Vector3.ProjectOnPlane(spot - m.pos, Vector3.up);
            float ds = toSpot.magnitude;
            if (ds < 0.9f)
            {
                // 自分の場所にいる：おかあさんと同じ向きで、止まるか、いっしょに歩く
                m.wantFwd = -back;
                m.speedMul = mom.curSpeed > 0.05f ? Mathf.Clamp01(mom.curSpeed / Mathf.Max(0.01f, m.sp.speed)) : 0f;
            }
            else
            {
                m.wantFwd = toSpot / ds;
                m.speedMul = Mathf.Clamp(ds / 3f, 0.5f, 1f) * (momRuns ? 2.6f : 1.4f);
            }
            m.timer = 0.4f;
        }

        // ------------------------------------------------------------------
        // 描く：ザトウムシのボビング・イノシシの土ほり・シマエナガのふくらみ・ミノムシの糸と幼虫
        // ------------------------------------------------------------------
        void NightPose(Mob m, Vector3 f, Vector3 right, float s, ref Quaternion rot, ref Vector3 scale3, ref Vector3 bob)
        {
            switch (m.sp.id)
            {
                case "zatoumushi":
                {
                    // 体を上下にゆらす：長い脚の足先はそのままで、体だけ、はやく上下に
                    float b = ActEnvelope(m, ActSpecial, out _);
                    if (b > 0f) bob += m.up * (Mathf.Sin(m.anim * 24f) * 0.09f * s * b);
                    // 脚を口にとおして、そうじする：体を前へかたむける
                    float g = ActEnvelope(m, ActGroom, out _);
                    if (g > 0f) rot = Quaternion.AngleAxis(10f * g, right) * rot;
                    break;
                }
                case "inoshishi":
                case "uribou":
                {
                    // 鼻で土をほりかえす：頭を下げて、鼻先を小きざみに動かす
                    float dig = ActEnvelope(m, ActSpecial, out _);
                    if (dig > 0f) rot = Quaternion.AngleAxis(9f * dig + 2.5f * dig * Mathf.Sin(m.anim * 9f), right) * rot;
                    // 鼻を上げて、においをかぐ
                    float look = ActEnvelope(m, ActLook, out _);
                    if (look > 0f) rot = Quaternion.AngleAxis(-7f * look, right) * rot;
                    break;
                }
                case "shimaenaga":
                    // 羽をふくらませると、まんまるに
                    if (!m.airborne && m.anim < m.puffUntil) scale3 = Vector3.Scale(scale3, new Vector3(1.1f, 1.06f, 1.02f));
                    break;
            }
        }

        /// <summary>夜の森のいきものの部品なら描いて true（ミノムシの糸と、顔を出す幼虫）。</summary>
        bool DrawNightPart(Mob m, PartMount part, Mesh pm, Matrix4x4 body)
        {
            switch (part.mesh)
            {
                case "Minomushi_Thread":
                {
                    // 糸：みのの上のはしから、枝の所まで（体の大きさで割って、体の座標にする）
                    float len = m.threadLen / Mathf.Max(0.01f, m.scale);
                    Add(pm, assets.creature, body * Matrix4x4.TRS(new Vector3(0f, len, 0f), Quaternion.identity, new Vector3(1f, len, 1f)));
                    return true;
                }
                case "Minomushi_Larva":
                {
                    if (m.emerge < 0.06f) return true;
                    float e = Mathf.SmoothStep(0f, 1f, m.emerge);
                    // 口から前へのびて、首をふって、まわりを見る
                    var q = Quaternion.AngleAxis(Mathf.Sin(m.anim * 1.3f + m.phase) * 25f * e, Vector3.up);
                    Add(pm, assets.creature, body * Matrix4x4.TRS(part.offset, q, Vector3.one * Mathf.Lerp(0.35f, 1f, e)));
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>テスト用：ミノムシの、糸の付く所・みのの上のはし・ゆれ・顔を出している度合い。</summary>
        public bool BagwormState(out Vector3 anchor, out Vector3 top, out float swing, out float emerge)
        {
            foreach (var m in _mobs)
                if (m.sp.id == "minomushi")
                {
                    anchor = m.anchor;
                    top = m.pos;
                    swing = m.swingH.magnitude;
                    emerge = m.emerge;
                    return true;
                }
            anchor = top = Vector3.zero;
            swing = emerge = 0f;
            return false;
        }

        /// <summary>テスト用：ウリボウのおかあさん（いなければ null）までの距離。</summary>
        public float PigletToMother(int i)
        {
            var m = _mobs[i];
            return m.leader != null ? Vector3.Distance(m.pos, m.leader.pos) : -1f;
        }
    }
}

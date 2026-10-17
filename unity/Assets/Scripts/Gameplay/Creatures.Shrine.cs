using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 神社のいきもの：キツネ（近づくと首をかしげて見つめ、近すぎると、すっとはなれる。高くジャンプして、とびかかる）・
    /// タヌキ（びっくりすると、ころんと死んだふり）・フクロウ（高い所にとまり、首をまわしてしゃくとりむしを見る。音もなく飛ぶ。ホーホー）・
    /// オオミズアオ（石灯籠のあかりのまわりを、くるくる飛ぶ。とまると、はねを平らに広げる）・
    /// カナブン（ご神木の幹にいて、ときどき、前ばねをとじたまま後ろばねを出して、ブーン）・
    /// スズムシとエンマコオロギ（草むらで、前ばねを立てて鳴く。しゃくとりむしが近づくと、鳴きやむ）。
    /// </summary>
    public partial class Creatures
    {
        // ------------------------------------------------------------------
        // 地面を歩く：キツネ・タヌキ
        // ------------------------------------------------------------------
        /// <summary>キツネ・タヌキの、近づかれたときの動き。この回は、ここで動きをきめたら true。</summary>
        bool UpdateShrineCrawler(Mob m, float dt, bool near, int index)
        {
            if (m.sp.id == "tanuki")
            {
                bool dead = m.anim < m.deadUntil;
                m.shrineK = Mathf.MoveTowards(m.shrineK, dead ? 1f : 0f, dt * (dead ? 4f : 1.2f));
                Vector3 to = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
                // すぐそばまで来られると、びっくりして、ころんと死んだふり（しばらくは、しない）
                if (!dead && !m.carryingWorm && m.anim > m.hopNext && to.magnitude < 6.5f * FearScale(m)) StartPlayDead(m);
                if (m.anim < m.deadUntil || m.shrineK > 0.02f)
                {
                    m.curSpeed = 0f;
                    m.wasDead = true;
                    return true;
                }
                if (m.wasDead)
                {
                    // 起きたら、とことこはなれていく
                    m.wasDead = false;
                    if (to.sqrMagnitude > 1e-3f) m.wantFwd = -to.normalized;
                    m.speedMul = 1.6f;
                    m.timer = R(2f, 3f);
                }
                return false;
            }
            if (m.sp.id == "kitsune")
            {
                // とびかかっている間は、その場で（ジャンプは、描くときに）
                if (m.act == ActSpecial)
                {
                    m.curSpeed = Mathf.MoveTowards(m.curSpeed, 0f, dt * 6f);
                    return true;
                }
                Vector3 to = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
                float d = to.magnitude;
                float flee = m.sp.fleeRadius * FearScale(m);
                bool curious = !m.carryingWorm && d < flee * 2f && d > flee;
                m.alertK = Mathf.MoveTowards(m.alertK, curious ? 1f : 0f, dt * 2.5f);
                if (m.carryingWorm || d < 1e-3f) return false;
                if (d < flee)
                {
                    // 近すぎると、すっとはなれる
                    m.wantFwd = -to / d;
                    m.speedMul = 1.7f;
                    m.timer = R(1.2f, 1.8f);
                }
                else if (curious)
                {
                    // 少しはなれた所に来ると、止まって、こちらを向いて首をかしげる
                    m.wantFwd = to / d;
                    m.speedMul = 0f;
                    m.timer = Mathf.Max(m.timer, 0.4f);
                }
            }
            return false;
        }

        void StartPlayDead(Mob m)
        {
            m.deadUntil = m.anim + R(6f, 9f);
            m.hopNext = m.deadUntil + R(8f, 14f);
            m.act = 0;
            m.curSpeed = 0f;
        }

        // ------------------------------------------------------------------
        // フクロウ
        // ------------------------------------------------------------------
        /// <summary>
        /// フクロウ：高い所（ご神木の枝・鳥居・お社の屋根・奥の宮）にとまって、じっとしている。
        /// しゃくとりむしが近づくと、ほかの高い所へ、音もなく飛んでいく。ときどき「ホーホー」と鳴く。
        /// </summary>
        void UpdateOwl(Mob m, float dt)
        {
            m.turnHop = 0f;
            if (m.airborne)
            {
                FlyBird(m, dt);
                return;
            }
            if (m.launchAt >= 0f)
            {
                m.crouch = Mathf.MoveTowards(m.crouch, 1f, dt * 6f);
                if (m.anim >= m.launchAt)
                {
                    m.launchAt = -1f;
                    TakeOff(m, false);
                    m.arc = R(5f, 9f);   // ひくく、すーっと
                }
                return;
            }
            m.crouch = Mathf.MoveTowards(m.crouch, 0f, dt * 3f);
            m.resting = false;   // ついばまない
            m.timer -= dt;
            float flee = m.sp.fleeRadius * FearScale(m);
            float dWorm = (_head - m.pos).magnitude;
            if (dWorm < flee || m.timer <= 0f)
            {
                m.launchAt = m.anim + (dWorm < flee ? 0.2f : 0.6f);
                return;
            }
            if (m.hopNext <= 0f) m.hopNext = m.anim + R(4f, 12f);
            if (m.anim > m.hopNext)
            {
                m.hopNext = m.anim + R(16f, 30f);
                Hoot(m);
            }
        }

        void Hoot(Mob m)
        {
            m.callUntil = m.anim + 1.6f;
            if (!Application.isPlaying) return;
            Camera cam = Camera.main;
            float vol = cam != null ? Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, m.pos) / 90f) : 0.5f;
            if (vol > 0.05f) AreaSounds.Instance?.PlayAt("owl", m.pos, 0.8f * vol + 0.2f);
        }

        // ------------------------------------------------------------------
        // スズムシ・コオロギ
        // ------------------------------------------------------------------
        /// <summary>しゃくとりむしがこれより近づくと、鳴きやむ（見つけられる近さよりは、少し近い）。</summary>
        public const float CricketHush = 2.6f;

        static bool IsCricket(Mob m) => m.sp.id == "suzumushi" || m.sp.id == "korogi" || m.sp.id == "kirigirisu";

        /// <summary>
        /// スズムシ・コオロギ：じっとしているとき、前ばねを立てて鳴く（「リーン、リーン」「コロコロリー」）。
        /// しゃくとりむしが近づいたり、歩いたり跳んだりすると、鳴きやむ。
        /// </summary>
        void UpdateCricketSong(Mob m, float sdt, float vol)
        {
            bool close = (m.pos - _head).sqrMagnitude < CricketHush * CricketHush;
            if (close || m.airborne || m.curSpeed > 0.03f)
            {
                if (m.callUntil > m.anim) m.callUntil = m.anim;
                return;
            }
            bool suzu = m.sp.id == "suzumushi", kiri = m.sp.id == "kirigirisu";
            if (m.anim < m.callUntil + (suzu ? 1.0f : kiri ? 2.4f : 1.4f) || R(0f, 1f) > sdt * 0.8f) return;
            m.callUntil = m.anim + (suzu ? 2.2f : kiri ? 1.6f : 1.8f);   // キリギリスは「ギーッ、チョン」
            AreaSounds.Instance?.PlayAt(m.sp.id, m.pos, 0.25f + 0.55f * vol);
        }

        // ------------------------------------------------------------------
        // オオミズアオ（ガ）
        // ------------------------------------------------------------------
        /// <summary>
        /// オオミズアオ：石灯籠のあかり（group.path）のまわりを、くるくる回るように飛ぶ。ときどき、灯籠の笠にとまって、
        /// はねを平らに広げて休む（ときどき、はねをふるわせる）。しゃくとりむしが近づくと、飛び立つ。ときどき、となりの灯籠へ。
        /// </summary>
        void UpdateMoth(Mob m, float dt)
        {
            var lights = m.group.path;
            if (lights.Count == 0)
            {
                m.airborne = true;
                return;
            }
            if (m.lampIdx < 0 || m.lampIdx >= lights.Count)
            {
                float best = float.MaxValue;
                for (int i = 0; i < lights.Count; i++)
                {
                    float d2 = (lights[i] - m.home).sqrMagnitude;
                    if (d2 < best) { best = d2; m.lampIdx = i; }
                }
            }
            float t = m.anim + m.phase;
            m.timer -= dt;
            m.flapBoost = Mathf.MoveTowards(m.flapBoost, 0f, dt * 1.5f);
            if (m.resting && (_head - m.pos).sqrMagnitude < 1.6f * 1.6f)
            {
                m.resting = false;
                m.timer = R(5f, 9f);
                m.vel += Vector3.up * 1.5f;
                m.flapBoost = 1f;
            }
            if (m.timer <= 0f && TakeThink())
            {
                bool rest = !m.resting && R(0f, 1f) < 0.45f;
                if (rest)
                {
                    m.target = MothRestSpot(lights[m.lampIdx]);
                    m.resting = true;
                    m.timer = R(7f, 14f);
                }
                else
                {
                    m.resting = false;
                    m.timer = R(8f, 16f);
                    // ときどき、となりの灯籠のあかりへ
                    if (R(0f, 1f) < 0.3f)
                    {
                        int next = m.lampIdx;
                        float best = float.MaxValue;
                        for (int i = 0; i < lights.Count; i++)
                        {
                            if (i == m.lampIdx) continue;
                            float d = Vector3.Distance(lights[i], lights[m.lampIdx]) + R(0f, 6f);
                            if (d < best && d < 26f) { best = d; next = i; }
                        }
                        m.lampIdx = next;
                    }
                }
            }
            Vector3 lamp = lights[m.lampIdx];
            Vector3 goal;
            if (m.resting) goal = m.target;
            else
            {
                float w = 1.5f + 0.4f * Mathf.Sin(t * 0.31f);
                float rad = 1.7f + 0.6f * Mathf.Sin(t * 0.7f);
                goal = lamp + new Vector3(Mathf.Cos(t * w) * rad, 0.3f + 0.7f * Mathf.Sin(t * 1.3f), Mathf.Sin(t * w) * rad);
            }
            Vector3 dv = goal - m.pos;
            float cap = m.sp.speed * (m.resting && dv.magnitude < 1f ? 0.5f : 1.3f);
            FlyToward(m, Vector3.ClampMagnitude(dv * 2.2f, cap), 3f, FlutterThrust * (1f + m.flapBoost), dt);
            bool flying = !m.resting || dv.magnitude > 0.15f;
            if (flying)
            {
                if (m.anim > m.jitterNext)
                {
                    m.jitterNext = m.anim + 0.18f;
                    m.jitter = new Vector3(R(-1f, 1f), R(-0.4f, 0.4f), R(-1f, 1f));
                }
                m.vel += m.jitter * dt;
                m.vel.y += Mathf.Sin(t * 3.2f * Mathf.PI * 2f) * 1.2f * dt;   // はばたくたびに、ふわっと浮く
                Vector3 away = m.pos - _head;
                if (away.sqrMagnitude < 0.6f * 0.6f && away.sqrMagnitude > 1e-4f) m.vel += away.normalized * (3f * dt);
                // 灯籠の石にぶつからないよう、前に物があれば上へ
                if (m.vel.sqrMagnitude > 0.01f && Physics.Raycast(m.pos, m.vel.normalized, 0.6f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore))
                    m.vel += Vector3.up * (3f * dt);
                m.pos += m.vel * dt;
                float ground = GroundOrWater(m.pos);
                if (m.pos.y < ground + 0.3f) m.pos.y = ground + 0.3f;
            }
            else
            {
                m.pos = m.target;
                m.vel = Vector3.zero;
            }
            Vector3 hv = new Vector3(m.vel.x, 0f, m.vel.z);
            if (hv.sqrMagnitude > 0.01f) m.fwd = ShakuPhysics.DampDir(m.fwd, hv, 4f, dt);
            m.up = Vector3.up;
            m.airborne = flying;
            m.circling = flying && !m.resting && (m.pos - lamp).sqrMagnitude < 4f * 4f;
            if (!flying && R(0f, 1f) < dt * 0.3f) m.openWings = 1f;
            m.openWings = Mathf.MoveTowards(m.openWings, 0f, dt * 1.2f);
        }

        /// <summary>ガが休む所：灯籠の笠の上（上からさがす）。</summary>
        Vector3 MothRestSpot(Vector3 lamp)
        {
            for (int k = 0; k < 6; k++)
            {
                Vector3 from = lamp + new Vector3(R(-1.8f, 1.8f), 7f, R(-1.8f, 1.8f));
                if (Physics.Raycast(from, Vector3.down, out var hit, 9f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                    && hit.point.y > lamp.y + 1f && hit.normal.y > 0.4f)
                    return hit.point + hit.normal * 0.04f;
            }
            return lamp + Vector3.up * 2.4f;
        }

        // ------------------------------------------------------------------
        // 描く：かたむき・ジャンプ・死んだふり・羽
        // ------------------------------------------------------------------
        /// <summary>神社のいきものの体の姿勢（ジャンプ・死んだふり・ホーホー・ブーン）。</summary>
        void ShrinePose(Mob m, Vector3 f, Vector3 right, float s, ref Quaternion rot, ref Vector3 scale3, ref Vector3 bob)
        {
            switch (m.sp.id)
            {
                case "kitsune":
                {
                    // 近づかれると、首をかしげて見つめる（体ごと、少しかたむける）
                    if (m.alertK > 0f) rot = Quaternion.AngleAxis(Mathf.Sin(m.anim * 0.8f + m.phase) * 9f * m.alertK, f) * rot;
                    // 見まわす：鼻先を上げて、あたりのにおいをかぐ
                    float look = ActEnvelope(m, ActLook, out _);
                    if (look > 0f) rot = Quaternion.AngleAxis(-10f * look, right) * rot;
                    // ネズミとりジャンプ：かがんで、高くとび上がり、鼻先から草むらへとびこむ
                    float jump = ActEnvelope(m, ActSpecial, out float k);
                    if (jump > 0f)
                    {
                        if (k < 0.3f)
                        {
                            float c = Mathf.Sin(Mathf.PI * k / 0.3f * 0.5f);
                            scale3.y *= 1f - 0.12f * c;
                            rot = Quaternion.AngleAxis(6f * c, right) * rot;
                        }
                        else
                        {
                            float a = Mathf.Clamp01((k - 0.3f) / 0.6f);
                            bob += Vector3.up * (Mathf.Sin(Mathf.PI * a) * 9f * s);
                            bob += f * (a * 3f * s);
                            rot = Quaternion.AngleAxis(Mathf.Lerp(-35f, 55f, a) * Mathf.Sin(Mathf.PI * Mathf.Min(1f, a * 1.15f)), right) * rot;
                        }
                    }
                    break;
                }
                case "tanuki":
                {
                    // 死んだふり：ころんと横になる（足は、こわばってのびる）
                    if (m.shrineK > 0f)
                    {
                        float e = Mathf.SmoothStep(0f, 1f, m.shrineK);
                        rot = Quaternion.AngleAxis(88f * e, f) * rot;
                        bob -= Vector3.up * (4.3f * e * s);
                        if (m.anim < m.deadUntil && Mathf.Sin(m.anim * 2.7f + m.phase) > 0.97f) scale3 *= 1.01f;   // ときどき、ぴくっ
                    }
                    // においをかぐ：鼻先を下げる
                    float sniff = ActEnvelope(m, ActLook, out _);
                    if (sniff > 0f) rot = Quaternion.AngleAxis(9f * sniff * (0.7f + 0.3f * Mathf.Sin(m.anim * 12f)), right) * rot;
                    break;
                }
                case "fukurou":
                    // 鳴くときは、のどをふくらませて、体を少し前へ
                    if (m.anim < m.callUntil)
                    {
                        float c = Mathf.Abs(Mathf.Sin((m.callUntil - m.anim) * 6f));
                        scale3 *= 1f + 0.04f * c;
                        rot = Quaternion.AngleAxis(5f * c, right) * rot;
                    }
                    break;
                case "kanabun":
                {
                    // ブーン：後ろばねを出して、少し浮く
                    float buzz = ActEnvelope(m, ActSpecial, out _);
                    if (buzz > 0f) bob += m.up * (0.22f * buzz * s + 0.02f * Mathf.Sin(m.anim * 60f) * buzz);
                    break;
                }
            }
        }

        /// <summary>神社のいきものの部品なら描いて true（フクロウの頭・ガのはね・カナブンの後ろばね）。</summary>
        bool DrawShrinePart(Mob m, PartMount part, Mesh pm, Matrix4x4 body)
        {
            switch (part.mesh)
            {
                case "Fukurou_Head":
                {
                    // 首をまわして、しゃくとりむしを見る（ぐるりと後ろまで）。見まわすときは、大きく左右に
                    Vector3 headW = body.MultiplyPoint3x4(part.offset);
                    Vector3 local = body.inverse.MultiplyPoint3x4(_head);
                    Vector3 rel = local - part.offset;
                    float want = 0f, wantPitch = 0f;
                    bool watching = (_head - headW).sqrMagnitude < 40f * 40f && rel.sqrMagnitude > 1e-4f;
                    if (watching)
                    {
                        want = Mathf.Atan2(-rel.x, -rel.z) * Mathf.Rad2Deg;
                        wantPitch = Mathf.Clamp(Mathf.Atan2(-rel.y, new Vector2(rel.x, rel.z).magnitude) * Mathf.Rad2Deg, -40f, 50f);
                    }
                    else want = 35f * Mathf.Sin(m.anim * 0.35f + m.phase);
                    float look = ActEnvelope(m, ActLook, out float lk);
                    if (look > 0f) want = Mathf.Sin(lk * Mathf.PI * 2f) * 160f;
                    want = Mathf.Clamp(Mathf.DeltaAngle(0f, want), -170f, 170f);
                    float dt = Mathf.Min(Time.deltaTime, 0.05f);
                    m.headYaw = Mathf.MoveTowards(m.headYaw, want, dt * 220f);
                    m.headPitch = Mathf.MoveTowards(m.headPitch, wantPitch, dt * 90f);
                    float tilt = (watching ? 14f : 4f) * Mathf.Sin(m.anim * 0.6f + m.phase);   // 首をかしげる
                    var local2 = Matrix4x4.TRS(part.offset, Quaternion.Euler(m.headPitch, m.headYaw, tilt), Vector3.one);
                    Add(pm, assets.creature, body * local2);
                    return true;
                }
                case "Ga_Wing":
                {
                    // とんでいるときは、ゆったり大きくはばたく。とまっているときは、平らに広げる（ときどき、ふるわせる）
                    float roll = m.airborne
                        ? part.restRoll + part.flapAmp * Mathf.Sin((m.anim + m.phase) * part.flapHz * (1f + 0.5f * m.flapBoost) * Mathf.PI * 2f)
                        : 6f + 2f * Mathf.Sin(m.anim * 1.4f + m.phase) + 7f * m.openWings * Mathf.Sin(m.anim * 38f);
                    for (int k = 0; k < 2; k++)
                    {
                        float side = k == 0 ? 1f : -1f;
                        var local = Matrix4x4.TRS(new Vector3(part.offset.x * side, part.offset.y, part.offset.z), Quaternion.Euler(0f, 0f, roll * side), new Vector3(side, 1f, 1f));
                        Add(pm, assets.creatureWing, body * local);
                    }
                    return true;
                }
                case "Suzumushi_Wing":
                case "Korogi_Wing":
                case "Kirigirisu_Wing":
                {
                    // スズムシ・コオロギの前ばね：ふだんはせなかに重ねてたたみ、鳴くときは立てて、こまかくふるわせる（こすりあわせる）
                    float dt = Mathf.Min(Time.deltaTime, 0.05f);
                    m.wingLift = Mathf.MoveTowards(m.wingLift, m.anim < m.callUntil && !m.airborne ? 1f : 0f, dt * 5f);
                    float roll = -m.wingLift * (part.flapAmp + 3f * Mathf.Sin((m.anim + m.phase) * part.flapHz * Mathf.PI * 2f));
                    for (int k = 0; k < 2; k++)
                    {
                        float side = k == 0 ? 1f : -1f;
                        var local = Matrix4x4.TRS(new Vector3(part.offset.x * side, part.offset.y, part.offset.z), Quaternion.Euler(0f, 0f, roll * side), new Vector3(side, 1f, 1f));
                        Add(pm, assets.creatureWing, body * local);
                    }
                    return true;
                }
                case "Kanabun_Wing":
                {
                    // 前ばねはとじたまま、横のすきまから後ろばねを出して、はやくはばたく
                    float buzz = ActEnvelope(m, ActSpecial, out _);
                    if (buzz < 0.05f) return true;
                    float flap = part.flapAmp * Mathf.Sin((m.anim + m.phase) * part.flapHz * Mathf.PI * 2f);
                    for (int k = 0; k < 2; k++)
                    {
                        float side = k == 0 ? 1f : -1f;
                        var local = Matrix4x4.TRS(new Vector3(part.offset.x * side, part.offset.y, part.offset.z), Quaternion.Euler(0f, 0f, flap * side), new Vector3(side * buzz, 1f, 1f));
                        Add(pm, assets.creatureWing, body * local);
                    }
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}

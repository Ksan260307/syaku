using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// クリスマス村のいきもの：トナカイ（のんびり歩き、ひづめで雪をほって苔をさがす。歩くと首の鈴が「チリン」。
    /// 近づかれても逃げずに、立ち止まって顔を上げ、そちらを見る）・
    /// ユキウサギ（ナキウサギと同じ跳び方。近づかれると、ぴたっと止まって耳を立て、うしろ足で雪を「トン」とけって、大きく跳んで逃げる）・
    /// ヨーロッパコマドリ（シマエナガと同じ、とまり木から、とまり木へ。首をかしげて、すんだ声でさえずる）・
    /// フユシャク（オオミズアオと同じ、ランプのあかりのまわり）・
    /// ヤマネ（手ぶくろの口で冬眠。寝息で体がふくらんだり、しぼんだり。ときどき、ころんと寝返りをうち、うす目をあける）。
    /// </summary>
    public partial class Creatures
    {
        /// <summary>しゃくとりむしがこれより近づくと、ユキウサギは、ぴたっと止まって耳を立てる。</summary>
        public const float HareAlert = 5.5f;
        /// <summary>ユキウサギが、止まってから逃げだすまで（秒）。</summary>
        public const float HareFreeze = 0.9f;
        /// <summary>しゃくとりむしがこれより近づくと、トナカイは立ち止まって、そちらを見る。</summary>
        public const float ReindeerWatch = 7f;

        /// <summary>クリスマス村の、地面のいきもの。この回は、ここで動きをきめたら true。</summary>
        bool UpdateXmasCrawler(Mob m, float dt, bool near, int index)
        {
            switch (m.sp.id)
            {
                case "yamane":
                    UpdateDormouse(m, dt);
                    return true;
                case "tonakai":
                    UpdateReindeer(m, dt);
                    return false;
            }
            return false;
        }

        /// <summary>場所から、そのいきものの音を鳴らす（カメラから遠いほど小さい）。</summary>
        void XmasSound(Mob m, string clip, float volume, float maxDist = 40f)
        {
            if (!Application.isPlaying) return;
            Camera cam = Camera.main;
            float vol = cam != null ? Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, m.pos) / maxDist) : 0.5f;
            if (vol > 0.05f) AreaSounds.Instance?.PlayAt(clip, m.pos, volume * (0.3f + 0.7f * vol));
        }

        // ------------------------------------------------------------------
        // トナカイ
        // ------------------------------------------------------------------
        /// <summary>
        /// トナカイ：ふだんは、のんびり歩いて、ときどき立ち止まり、ひづめで雪をほって苔をさがす。
        /// 歩くと首の鈴が「チリン」と鳴る。しゃくとりむしが近づくと、立ち止まって顔を上げ、そちらを見る（逃げない）。
        /// </summary>
        void UpdateReindeer(Mob m, float dt)
        {
            Vector3 to = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
            float d = to.magnitude;
            if (!m.carryingWorm && d < ReindeerWatch && d > 1e-3f)
            {
                if (m.act != ActLook || m.actT > m.actDur * 0.6f) StartAct(m, ActLook, 2.2f, true);
                m.wantFwd = to / d;
                m.speedMul = 0f;
                m.timer = Mathf.Max(m.timer, 0.5f);
            }
            if (m.act == ActSpecial)
            {
                m.speedMul = 0f;
                m.timer = Mathf.Max(m.timer, 0.3f);
            }
            // 首の鈴：歩くと、ときどき「チリン」。雪をほるときも、頭が動いて鳴る
            bool ring = m.moveSpeed > 0.15f || m.act == ActSpecial;
            if (ring && m.anim > m.hopNext)
            {
                m.hopNext = m.anim + R(3.5f, 7f);
                m.callUntil = m.anim + 0.8f;
                XmasSound(m, "reindeer_bell", 0.55f);
            }
        }

        // ------------------------------------------------------------------
        // ユキウサギ
        // ------------------------------------------------------------------
        /// <summary>
        /// ユキウサギ：しゃくとりむしが近づくと、まず、ぴたっと止まって耳を立て、じっと見る（HareFreeze 秒）。
        /// それでも近いと、うしろ足で雪を「トン」とけって、遠くへ大きく跳ぶ。跳ぶ先は、ナキウサギと同じ（Hopper の逃げ方）。
        /// ここで動きをきめたら true。
        /// </summary>
        bool UpdateHare(Mob m, float dt)
        {
            if (m.airborne || m.launchAt >= 0f || m.carryingWorm) return false;
            Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, Vector3.up);
            float d = toW.magnitude;
            if (d > HareAlert * 1.3f)
            {
                m.alertK = Mathf.MoveTowards(m.alertK, 0f, dt * 1.5f);
                m.fledJump = false;
                return false;
            }
            if (d > HareAlert && m.alertK <= 0f) return false;
            // ぴたっと止まって、耳を立て、そちらを見る
            if (m.alertK <= 0f)
            {
                m.pauseUntil = m.anim + HareFreeze;
                StartAct(m, ActLook, HareFreeze + 0.3f, true);
            }
            m.alertK = Mathf.MoveTowards(m.alertK, 1f, dt * 6f);
            m.curSpeed = 0f;
            if (toW.sqrMagnitude > 0.04f) Steer(m, toW, dt, 160f);
            if (m.anim < m.pauseUntil) return true;
            if (d > HareAlert) return true;   // 少しはなれたら、見はったまま
            // 逃げる：うしろ足で「トン」。跳ぶ先は Hopper（逃げるときは遠くへ）
            if (!m.fledJump)
            {
                m.fledJump = true;
                XmasSound(m, "hare_thump", 0.6f, 30f);
            }
            m.timer = 0f;
            return false;
        }

        /// <summary>ユキウサギが逃げるときの、跳ぶ遠さの倍率（大きなうしろ足で、ナキウサギよりずっと遠くへ）。</summary>
        static float HareLeap(Mob m) => m.sp.id == "yukiusagi" ? 1.8f : 1f;

        // ------------------------------------------------------------------
        // ヤマネ
        // ------------------------------------------------------------------
        /// <summary>ヤマネを、手ぶくろの口に置く（地面にそわせない。前は、手ぶくろの外）。</summary>
        void StartDormouse(Mob m)
        {
            m.pos = m.home;
            m.up = Vector3.up;
            m.emerge = 0f;
            m.timer = R(6f, 12f);
            m.peekOut = false;
        }

        /// <summary>
        /// ヤマネ：冬眠中。動かずに、ゆっくりした寝息。ときどき、ころんと寝返り（ActSpecial）。
        /// しずかなときは、たまに、うす目をあける（emerge）。しゃくとりむしにふれられると、うす目をあけて、またねむる。
        /// </summary>
        void UpdateDormouse(Mob m, float dt)
        {
            m.curSpeed = 0f;
            m.moveSpeed = 0f;
            m.speedMul = 0f;
            float dWorm = (_head - (m.pos + Vector3.up * 0.7f * m.scale)).magnitude;
            bool forced = m.anim < m.peekUntil;
            if (dWorm < 1.2f + 0.8f * m.scale && !forced) m.peekUntil = m.anim + R(2f, 3f);   // ふれられると、うす目をあける
            m.timer -= dt;
            if (m.timer <= 0f)
            {
                m.peekOut = !m.peekOut && R(0f, 1f) < 0.5f;
                m.timer = m.peekOut ? R(2.5f, 4f) : R(8f, 16f);
                // 寝返り（ねむったまま、ころん）
                if (!m.peekOut && m.act == 0 && R(0f, 1f) < 0.45f) StartAct(m, ActSpecial, ActLength(m, ActSpecial), true);
            }
            float want = forced || m.peekOut ? 1f : 0f;
            m.emerge = Mathf.MoveTowards(m.emerge, want, dt * (want > m.emerge ? 0.8f : 1.5f));
            // 寝息（ときどき）
            if (m.anim > m.hopNext)
            {
                m.hopNext = m.anim + R(9f, 16f);
                XmasSound(m, "dormouse", 0.35f, 14f);
            }
        }

        // ------------------------------------------------------------------
        // 描く
        // ------------------------------------------------------------------
        void XmasPose(Mob m, Vector3 f, Vector3 right, float s, ref Quaternion rot, ref Vector3 scale3, ref Vector3 bob)
        {
            switch (m.sp.id)
            {
                case "tonakai":
                {
                    // ひづめで雪をほる：頭を下げ、体を前へかたむけて、前足でかくように小きざみに
                    float dig = ActEnvelope(m, ActSpecial, out _);
                    if (dig > 0f) rot = Quaternion.AngleAxis(7f * dig + 2f * dig * Mathf.Sin(m.anim * 7f), right) * rot;
                    // 顔を上げて、見つめる
                    float look = ActEnvelope(m, ActLook, out _);
                    if (look > 0f) rot = Quaternion.AngleAxis(-5f * look, right) * rot;
                    break;
                }
                case "yukiusagi":
                    // 止まって耳を立てると、体をのばして、背が高くなる
                    if (!m.airborne && m.alertK > 0f) scale3 = Vector3.Scale(scale3, new Vector3(1f, 1f + 0.08f * m.alertK, 1f));
                    break;
                case "robin":
                {
                    // 首をかしげる（小さな鳥なので、体ごと左右にかたむける）
                    float look = ActEnvelope(m, ActLook, out float k);
                    if (look > 0f && !m.airborne) rot = Quaternion.AngleAxis(Mathf.Sin(k * Mathf.PI * 2f) * 22f * look, f) * rot;
                    break;
                }
                case "yamane":
                {
                    // 寝息：ゆっくり、ふくらんで、しぼむ
                    float breath = Mathf.Sin(m.anim * 1.4f + m.phase);
                    scale3 = Vector3.Scale(scale3, new Vector3(1f + 0.03f * breath, 1f + 0.045f * breath, 1f + 0.03f * breath));
                    // 寝返り：ころんと横になって、またもどる
                    float roll = ActEnvelope(m, ActSpecial, out _);
                    if (roll > 0f) rot = Quaternion.AngleAxis(38f * roll, f) * rot;
                    break;
                }
            }
        }

        /// <summary>クリスマス村のいきものの部品なら描いて true（ヤマネの、うす目）。</summary>
        bool DrawXmasPart(Mob m, PartMount part, Mesh pm, Matrix4x4 body)
        {
            if (part.mesh != "Yamane_Eye") return false;
            if (m.emerge < 0.05f) return true;   // とじた目は、体のもよう
            float open = Mathf.SmoothStep(0f, 1f, m.emerge);
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? 1f : -1f;
                var local = Matrix4x4.TRS(new Vector3(part.offset.x * side, part.offset.y, part.offset.z), Quaternion.identity,
                    new Vector3(side, Mathf.Lerp(0.15f, 0.55f, open), 1f));   // うす目（半分だけあける）
                Add(pm, assets.creature, body * local);
            }
            return true;
        }

        /// <summary>テスト用：ヤマネの、うす目の度合いと、寝返りをしているか。</summary>
        public bool DormouseState(out Vector3 pos, out float emerge, out bool rolling)
        {
            foreach (var m in _mobs)
                if (m.sp.id == "yamane")
                {
                    pos = m.pos;
                    emerge = m.emerge;
                    rolling = m.act == ActSpecial;
                    return true;
                }
            pos = Vector3.zero;
            emerge = 0f;
            rolling = false;
            return false;
        }

        /// <summary>テスト用：その種の、いちばん近い一匹の場所と、止まって見はっている度合い（ユキウサギ・トナカイ）。</summary>
        public bool NearestOf(string id, Vector3 from, out Vector3 pos, out float alert, out bool airborne)
        {
            pos = Vector3.zero;
            alert = 0f;
            airborne = false;
            float best = float.MaxValue;
            foreach (var m in _mobs)
            {
                if (m.sp.id != id) continue;
                float d2 = (m.pos - from).sqrMagnitude;
                if (d2 >= best) continue;
                best = d2;
                pos = m.pos;
                alert = m.sp.id == "tonakai" ? (m.act == ActLook ? 1f : 0f) : m.alertK;
                airborne = m.airborne;
            }
            return best < float.MaxValue;
        }
    }
}

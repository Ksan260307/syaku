using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 見つけたいきものに、もう一度会ったとき：あいさつ・なかよし・しぐさの観察・大きさの記録・あんない（Friends も見る）。
    /// </summary>
    public partial class Creatures
    {
        /// <summary>あいさつしてくれた（種・場所・なかよしの段・いま上がったか）。</summary>
        public event Action<SpeciesDef, Vector3, int, bool> Greeted;
        /// <summary>はじめて観察したしぐさ（種・しぐさ・場所）。</summary>
        public event Action<SpeciesDef, Behavior, Vector3> Observed;
        /// <summary>会って、大きさをはかった（種・大きさ mm・場所）。</summary>
        public event Action<SpeciesDef, float, Vector3> Measured;
        /// <summary>なかよしの飛ぶいきものが、あんないをはじめた（種・いきものの場所・あんないする先）。</summary>
        public event Action<SpeciesDef, Vector3, Vector3> GuideStarted;
        /// <summary>あんないしているいきものが通った所（光のあとを残す）。</summary>
        public event Action<Vector3> GuideTrail;
        /// <summary>あんないする先（まだ取っていないしずく・まだ見つけていない名所）。なければ null。</summary>
        public Func<Vector3, Vector3?> GuideTarget;

        /// <summary>あいさつしてくれる近さ（体の大きさで少し広がる。飛ぶものは少し遠くから）。</summary>
        public const float GreetRadius = 2.4f, FlyerGreetRadius = 2.8f;
        /// <summary>しぐさを観察できる近さ（画面に見えていること）。</summary>
        public const float ObserveRadius = 10f;
        /// <summary>同じ一匹が、もう一度あいさつするまでの時間（秒）。</summary>
        public const float GreetInterval = 18f;
        /// <summary>あんないは、つぎにするまで少しあける（秒）。いちばん長くて GuideTime 秒。</summary>
        public const float GuideInterval = 45f, GuideTime = 30f;

        /// <summary>しぐさの名前（Friends の key）。</summary>
        public static readonly HashSet<string> BehaviorKeys = new HashSet<string>
        {
            "groom", "stretch", "look", "shake", "special", "alarm", "fly", "hop", "rest", "chase", "wings", "perch", "hunt",
            "curl", "roll", "retreat", "dead", "spin", "display", "raise", "strike", "call", "puff", "sidehop", "turnhop",
            "carry", "antgreet", "dig", "popup", "sniff", "dive", "surface", "row", "dart", "sideways", "watch", "mimic",
            "climb", "ride", "glow", "hover", "swoop", "zigzag", "walk", "run",
        };

        readonly System.Random _friendRng = new System.Random(4242);

        static bool IsFlyer(Mob m) => m.sp.kind == MobKind.Flutter || m.sp.kind == MobKind.Hover;

        /// <summary>なかよしになった鳥は、近づいても、すぐには逃げない（逃げる距離にかける数）。</summary>
        static float FearScale(Mob m)
        {
            if (m.sp.kind != MobKind.Bird) return 1f;
            int lv = Friends.Level(m.sp.id);
            return lv >= 4 ? 0.35f : lv >= 2 ? 0.6f : 1f;
        }

        /// <summary>いま、そのしぐさをしているか。</summary>
        bool Doing(Mob m, string key)
        {
            switch (key)
            {
                case "groom": return m.act == ActGroom || m.anim < m.groomUntil;
                case "stretch": return m.act == ActStretch;
                case "look": return m.act == ActLook;
                case "shake": return m.act == ActShake;
                case "special": return m.act == ActSpecial;
                case "alarm": return m.act == ActAlarm;
                case "fly": return m.sp.kind == MobKind.Bird && m.airborne && m.vel.sqrMagnitude > 0.5f;
                case "hop": return m.airborne && (m.sp.kind == MobKind.Hopper || m.sp.kind == MobKind.Pouncer);
                case "rest": return m.sp.kind == MobKind.Flutter && m.resting && !m.airborne;
                case "chase": return m.airborne && m.chase != null && m.anim < m.chaseUntil;
                case "wings": return m.openWings > 0.5f;
                case "perch": return m.perched && !m.airborne;
                case "hunt": return m.prey != null && m.anim < m.preyUntil;
                case "curl": return m.curled > 0f && m.curlK > 0.8f;
                case "roll": return m.curlK > 0.8f && m.rollVel.sqrMagnitude > 0.04f;
                case "retreat": return m.retreat > 0.5f;
                case "dead": return m.anim < m.deadUntil;
                case "spin": return m.anim < m.spinUntil;
                case "display": return m.display > 0.6f;
                case "raise": return m.raise > 0.6f;
                case "strike": return m.strike > 0.6f;
                case "call": return m.anim < m.callUntil;
                case "puff": return m.anim < m.puffUntil;
                case "sidehop": return m.sideHopped;
                case "turnhop": return m.turnHop > 0.5f;
                case "carry": return m.carrying && m.group.path.Count > 1;
                case "antgreet": return m.anim < m.slowUntil;
                case "dig": return m.digDown && m.dig > 0.1f && m.dig < 0.6f;
                case "popup": return !m.digDown && m.dig > 0.05f && m.dig < 0.55f;
                case "sniff": return !m.digDown && m.dig < 0.05f;
                case "dive": return m.dive > 0.6f;
                case "surface": return !m.digDown && m.dive > 0.05f && m.dive < 0.5f;
                case "row": return m.sp.kind == MobKind.Skater && m.moveSpeed > 0.2f;
                case "dart": return m.sp.kind == MobKind.Skater && m.moveSpeed > 0.9f;
                case "sideways": return m.bid == "crab" && !m.airborne && m.moveSpeed > 0.12f;
                case "watch": return m.watchTime > 1.5f;
                case "mimic": return m.bid == "nanafushi" && m.curSpeed < 0.05f && (_head - m.pos).sqrMagnitude < 2.2f * 2.2f;
                case "climb": return !m.airborne && m.up.y < 0.5f;
                case "ride": return m.carryingWorm;
                case "glow": return FireflyGlow(m) > 0.6f;
                case "hover": return IsFlyer(m) && m.airborne && m.vel.sqrMagnitude > 0.04f;
                case "swoop": return m.airborne && m.vel.y < -0.9f;
                case "zigzag": return m.airborne && Mathf.Abs(m.turnRate) > 150f;
                case "walk": return !m.airborne && m.moveSpeed > 0.08f;
                case "run": return !m.airborne && m.speedMul > 1.3f && m.moveSpeed > 0.3f;
                default: return false;
            }
        }

        /// <summary>なかよし ★5 のとき、とっておきのしぐさをしてみせる（できるものだけ）。</summary>
        bool ForceBehavior(Mob m, string key)
        {
            switch (key)
            {
                case "groom": StartAct(m, ActGroom, ActLength(m, ActGroom), true); return true;
                case "stretch": StartAct(m, ActStretch, ActLength(m, ActStretch), true); return true;
                case "shake": StartAct(m, ActShake, ActLength(m, ActShake), true); return true;
                case "special": StartAct(m, ActSpecial, ActLength(m, ActSpecial), true); return true;
                case "alarm": StartAct(m, ActAlarm, 0.8f, true); return true;
                case "curl": if (m.bid != "pillbug") return false; m.curled = Mathf.Max(m.curled, 3f); return true;
                case "retreat": m.retreat = Mathf.Max(m.retreat, 2.5f); m.curSpeed = 0f; return true;
                case "dead": m.deadUntil = m.anim + 2.5f; return true;
                case "spin": m.spinUntil = m.anim + 1.6f; return true;
                case "display":
                    if (m.bid == "otoshibumi") { m.speedMul = 0f; m.timer = 2f; }
                    m.display = 1f;
                    return true;
                case "raise": m.raise = 1f; return true;
                case "strike": m.strike = 1f; return true;
                case "call": m.callUntil = m.anim + 0.7f; return true;
                case "wings": if (m.airborne) return false; m.openWings = 1f; return true;
                case "antgreet": m.slowUntil = m.anim + 1f; m.bowUntil = m.anim + 0.6f; return true;
                case "popup": case "sniff": case "surface": m.digDown = false; m.digNext = m.anim + 4f; return true;
                case "swoop": if (!m.airborne) return false; m.vel.y = -1.6f; return true;
                case "run": m.speedMul = 1.5f; m.timer = 0.8f; return true;
                default: return false;
            }
        }

        /// <summary>体の長さ（mm）：体の形の長い方 × 一匹ずつの大きさ。1 体長 ＝ 25 mm。</summary>
        public float BodyMm(SpeciesDef sp, float scale)
        {
            Mesh mesh = M(sp.body);
            if (mesh == null) return 0f;
            Vector3 s = mesh.bounds.size;
            return Mathf.Max(s.x, s.z) * scale * Friends.MmPerUnit;
        }

        /// <summary>一匹ずつの大きさのばらつき（ほとんどはふつう。ときどき大物や、小さいもの）。乗れるいきものは、少しひかえめ。</summary>
        public static float SizeFactor(System.Random r, bool rideable)
        {
            double band = r.NextDouble(), v = r.NextDouble();
            float k;
            if (band < 0.7) k = Mathf.Lerp(0.93f, 1.07f, (float)v);
            else if (band < 0.88) k = v < 0.5 ? Mathf.Lerp(0.86f, 0.93f, (float)(v * 2)) : Mathf.Lerp(1.07f, 1.15f, (float)(v * 2 - 1));
            else k = v < 0.4 ? Mathf.Lerp(0.8f, 0.86f, (float)(v / 0.4)) : Mathf.Lerp(1.15f, 1.28f, (float)((v - 0.4) / 0.6));
            return rideable ? Mathf.Clamp(k, 0.9f, 1.15f) : k;
        }

        /// <summary>
        /// 見つけたことのあるいきもの：近くで見ていれば、しぐさを観察して、はじめて会った一匹は大きさをはかる。
        /// しずかに近づくと、あいさつしてくれて、なかよしが上がる。
        /// </summary>
        void UpdateFriendly(Mob m, InchwormController worm)
        {
            if (worm == null || !Visible(m)) return;
            Vector3 c = m.pos + m.up * (0.3f * m.scale);
            float d2 = (_head - c).sqrMagnitude;
            if (d2 > ObserveRadius * ObserveRadius) return;
            string id = m.sp.id;
            // 大きさ：近くで会った一匹ずつ、一度だけはかる
            if (!m.sizeSeen && d2 < m.sp.discoverRadius * m.sp.discoverRadius)
            {
                m.sizeSeen = true;
                float mm = BodyMm(m.sp, m.scale);
                if (mm > 0f) Measured?.Invoke(m.sp, mm, m.pos);
            }
            // しぐさの観察（画面に見えているときだけ）
            bool inView = !_viewReady || GeometryUtility.TestPlanesAABB(_viewPlanes, new Bounds(m.pos, Vector3.one * (0.6f * m.scale + 0.3f)));
            if (inView)
                foreach (var b in Friends.BehaviorsOf(id))
                {
                    if (Friends.HasSeen(id, b.key) || !Doing(m, b.key)) continue;
                    Friends.Observe(id, b.key);
                    Observed?.Invoke(m.sp, b, m.pos);
                    break;
                }
            // あいさつ
            if (m.anim < m.friendNext || worm.IsFalling || _headVel.sqrMagnitude > 2.2f * 2.2f) return;
            if (m.carryingWorm || m.curled > 0f || m.retreat > 0f || m.dig > 0.3f || m.anim < m.deadUntil || m.anim < m.watchUntil) return;
            float r = IsFlyer(m) ? FlyerGreetRadius : GreetRadius + 0.5f * Mathf.Max(0f, m.scale - 1f);
            if (m.sp.kind == MobKind.Bird)
            {
                if (m.airborne) return;
                float flee = m.sp.fleeRadius * FearScale(m);
                r = Mathf.Max(flee * 1.5f, flee + 1.2f);   // 鳥は、逃げる距離の少し外で
            }
            else if (m.airborne && !IsFlyer(m)) return;
            if (d2 > r * r) return;
            Greet(m);
        }

        void Greet(Mob m)
        {
            m.friendNext = m.anim + GreetInterval;
            // しゃくとりむしの方を向いて、少し止まり、ぽよんと弾む
            Vector3 toW = Vector3.ProjectOnPlane(_head - m.pos, m.up);
            if (!m.airborne && toW.sqrMagnitude > 1e-4f)
            {
                m.wantFwd = toW.normalized;
                m.pauseUntil = Mathf.Max(m.pauseUntil, m.anim + 1.2f);
            }
            m.joy = Mathf.Max(m.joy, 0.6f);
            if (m.sp.kind == MobKind.Bird) m.callUntil = m.anim + 0.4f;
            else if (m.sp.kind == MobKind.Flutter) m.flapBoost = Mathf.Max(m.flapBoost, 0.6f);
            string id = m.sp.id;
            bool up = Friends.Befriend(id, SaveSystem.Data.playTime);
            int level = Friends.Level(id);
            Greeted?.Invoke(m.sp, m.pos, level, up);
            // ★5：とっておきのしぐさを見せてくれる（できるものを、とっておきから順に）
            if (level >= Friends.SignatureAt)
            {
                var bs = Friends.BehaviorsOf(id);
                for (int k = bs.Length - 1; k >= 0; k--)
                    if (ForceBehavior(m, bs[k].key)) break;
            }
            // なかよしの飛ぶいきものは、まだ取っていないしずくの方へ、あんないしてくれる
            if (level >= Friends.GuideAt && IsFlyer(m)) TryGuide(m);
        }

        /// <summary>あんないをはじめる（先があれば）。</summary>
        bool TryGuide(Mob m)
        {
            if (m.anim < m.guideNext || GuideTarget == null) return false;
            var to = GuideTarget(_head);
            if (!to.HasValue) return false;
            m.guideTo = to.Value;
            m.guideUntil = m.anim + GuideTime;
            m.guideNext = m.anim + GuideInterval;
            m.resting = m.perched = false;
            m.chase = null;
            GuideStarted?.Invoke(m.sp, m.pos, m.guideTo);
            return true;
        }

        /// <summary>
        /// あんないしているとき、つぎに飛ぶ所：しゃくとりむしの少し先（あんないする先の方向）。
        /// 着いたとき・しゃくとりむしがはなれすぎたとき・時間がたったときは、やめる。
        /// </summary>
        bool GuideGoal(Mob m, out Vector3 goal)
        {
            goal = m.pos;
            if (m.guideUntil < 0f) return false;
            Vector3 to = m.guideTo - _head;
            to.y = 0f;
            float dist = to.magnitude;
            if (m.anim > m.guideUntil || dist < 2.5f || (m.pos - _head).sqrMagnitude > 20f * 20f)
            {
                m.guideUntil = -1f;
                return false;
            }
            Vector3 lead = _head + to / Mathf.Max(dist, 1e-3f) * Mathf.Min(4f, dist);
            lead.y = GroundOrWater(lead) + 1.3f;
            goal = lead;
            if (m.anim >= m.trailNext)
            {
                m.trailNext = m.anim + 0.12f;
                GuideTrail?.Invoke(m.pos);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // テスト用
        // ------------------------------------------------------------------
        /// <summary>その種の一匹が、あんないしているか（テスト用）。</summary>
        public bool IsGuiding(string species, out Vector3 pos, out Vector3 to)
        {
            foreach (var m in _mobs)
                if (m.sp.id == species && m.guideUntil >= 0f && m.anim <= m.guideUntil)
                {
                    pos = m.pos;
                    to = m.guideTo;
                    return true;
                }
            pos = to = Vector3.zero;
            return false;
        }

        /// <summary>その種の i 番目の一匹の、体の長さ（mm。テスト用）。</summary>
        public float SizeOf(string species, int index)
        {
            int k = 0;
            foreach (var m in _mobs)
                if (m.sp.id == species && k++ == index) return BodyMm(m.sp, m.scale);
            return 0f;
        }

        /// <summary>その種の一匹ずつの大きさ（テスト用）。</summary>
        public List<float> ScalesOf(string species)
        {
            var list = new List<float>();
            foreach (var m in _mobs)
                if (m.sp.id == species) list.Add(m.scale / Mathf.Max(1e-4f, m.sp.scale));
            return list;
        }

        /// <summary>その種の一匹に、しぐさをさせる（テスト用）。</summary>
        public bool ForceBehaviorForTests(string species, string key)
        {
            foreach (var m in _mobs)
                if (m.sp.id == species && ForceBehavior(m, key)) return true;
            return false;
        }

        /// <summary>その種の一匹ずつが、あいさつできるまでの時間を 0 にする（テスト用）。</summary>
        public void ResetGreetForTests(string species)
        {
            foreach (var m in _mobs)
                if (m.sp.id == species) m.friendNext = m.guideNext = 0f;
        }
    }
}

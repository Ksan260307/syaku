using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 糸の手ざわり：出す瞬間のきらりと、尾をはね上げる予備動作・ゆれの高い所で切るとぴょん・ゆれて当たった上の面に乗り移る・
    /// 引き寄せられている途中でねらい直す・じっとぶら下がると、くるりと回って景色をながめる・とどかない所へ撃つと糸がたれる・
    /// 糸を出せるふちに来たら、口もとがきらり。
    /// </summary>
    public partial class InchwormController
    {
        SilkFX _silkFx;
        int _swingJumps, _misses, _chainShots, _silkReadyHints;
        float _aimStart = -99f;
        bool _canDropPrev, _hangedLast;
        Collider _anchorCollider;   // 糸を出した物（そのすぐ下には着地しない）
        static readonly Collider[] s_anchorHits = new Collider[4];
        float _silkReadyNext;

        /// <summary>ねらった場所の面の向き（光る点の向き）。</summary>
        public Vector3 AimNormal { get; private set; } = Vector3.up;
        /// <summary>糸の見た目の効果（テスト用）。</summary>
        public SilkFX SilkEffects => _silkFx;
        /// <summary>ゆれの高い所で糸を切って、ぴょんととんだ回数。</summary>
        public int SwingJumps => _swingJumps;
        /// <summary>とどかない所へ撃って、糸がたれ下がった回数。</summary>
        public int SilkMisses => _misses;
        /// <summary>引き寄せられている途中で、ねらい直した回数。</summary>
        public int ChainShots => _chainShots;
        /// <summary>糸を出せるふちに来て、口もとがきらりとした回数。</summary>
        public int SilkReadyHints => _silkReadyHints;
        /// <summary>じっとぶら下がって、くるりと回りながら景色をながめている。</summary>
        public bool IsSightseeing => State == Mode.Hang && !_reeling && Time.time - _hangQuietSince > 2.5f;
        float _hangQuietSince;

        void InitSilkFX()
        {
            if (silk == null) return;
            _silkFx = gameObject.AddComponent<SilkFX>();
            _silkFx.Init(this, silk);
        }

        /// <summary>糸を出した物のすぐ下（1 以内）の面か（キノコのかさの下のふちなど。そこへは着地せず、下りつづける）。</summary>
        bool JustBelowAnchor(RaycastHit ground) => _anchorCollider != null && ground.collider == _anchorCollider && _silkAnchor.y - ground.point.y < 1f;

        /// <summary>糸を出した：口もとがきらり。</summary>
        void OnSilkOut()
        {
            // 糸のつけ根の物（歩いている途中で出すと、つけ根は持ち上げた頭の所なので、まわりの球で調べる）
            int hits = Physics.OverlapSphereNonAlloc(_silkAnchor, 0.35f, s_anchorHits, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore);
            _anchorCollider = hits > 0 ? s_anchorHits[0] : null;
            _silkFx?.Glint(_curve.Head);
            _hangQuietSince = Time.time;
        }

        /// <summary>ゆれのいちばん高い所あたり（糸が 20 度より、かたむいている）で切ると、外へぴょんととぶ。</summary>
        Vector3 SwingHop()
        {
            Vector3 off = _hangPos - _silkAnchor;
            float ang = Vector3.Angle(Vector3.down, off);
            if (ang < 20f) return Vector3.zero;
            Vector3 flat = Vector3.ProjectOnPlane(off, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) return Vector3.zero;
            float k = Mathf.InverseLerp(20f, 50f, ang);
            _swingJumps++;
            return Vector3.up * (0.6f + 1.0f * k) + flat.normalized * (0.4f + 0.6f * k);
        }

        /// <summary>じっとぶら下がっていると、ゆっくり回って、まわりの景色をながめる（ねじりの力）。</summary>
        float SightseeTwist(Vector3 horiz)
        {
            // スティックを倒さず、糸をのぼってもいないとき（ゆっくり下りている間も）
            if (horiz.sqrMagnitude > 0.01f || _silkSpeed < -0.05f || _reeling) _hangQuietSince = Time.time;
            if (!IsSightseeing) return 0f;
            float t = Time.time - _hangQuietSince - 2.5f;
            return 28f * Mathf.Sin(t * 0.45f);
        }

        /// <summary>ねらいはじめ・ねらいおわり：とどく所なら発射（引き寄せられている途中なら、ねらい直し）。とどかなければ、糸がたれ下がる。</summary>
        void ReleaseAim(bool canAim)
        {
            if (AimValid && canAim && InputEnabled)
            {
                if (_reeling) _chainShots++;
                FireSilk();
            }
            else if (canAim && InputEnabled && Time.time - _aimStart > 0.3f)
            {
                _misses++;
                _silkFx?.Miss(_curve.Head + SurfaceUp * 0.08f, AimPoint);
            }
        }

        /// <summary>糸を出せるふちに来たら、口もとをきらりとさせて知らせる（6 秒に一度まで）。</summary>
        void UpdateSilkReady()
        {
            if (CanDropSilk && !_canDropPrev && Time.time > _silkReadyNext)
            {
                _silkReadyNext = Time.time + 6f;
                _silkReadyHints++;
                _silkFx?.Glint(_curve.Head);
                TriggerBlink(1.2f);
            }
            _canDropPrev = CanDropSilk;
        }

        /// <summary>ぶら下がりがおわった：使った糸の形を、光る跡として残す。</summary>
        void UpdateSilkTrail()
        {
            bool hanging = State == Mode.Hang;
            if (_hangedLast && !hanging && _ropeLive) _silkFx?.LeaveTrail(_rope);
            _hangedLast = hanging;
        }
    }
}

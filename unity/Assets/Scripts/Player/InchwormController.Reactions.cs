using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// まわりへの反応：大きないきものの足音にびくっ・天敵のそばでは忍び足・強い風では歩幅を小さく・花のそばでくんくん・
    /// しずくが近いと早足・なかよしにぺこり・転がる物を押すと力む・大きな鳴き声の方へ顔を向ける。
    /// </summary>
    public partial class InchwormController
    {
        bool _sneak, _straining, _hurry;
        float _bowStart = -99f, _sniffNext = 6f, _sniffUntil = -99f, _hearNext, _bigStepNext;
        int _bows, _sniffs, _heard, _bigSteps, _backLooks;

        /// <summary>天敵（カエル・鳥）のそばで、体を低くして、そっと歩いている。</summary>
        public bool IsSneaking => _sneak && IsMoving;
        /// <summary>転がる物を、体ごと押して力んでいる。</summary>
        public bool IsStraining => _straining;
        /// <summary>しずくが近くて、早足になっている。</summary>
        public bool IsHurrying => _hurry && IsMoving;
        /// <summary>花に頭を近づけて、においをかいでいる。</summary>
        public bool IsSniffing => Time.time < _sniffUntil;
        /// <summary>なかよしのいきものに、頭をちょこんと下げている。</summary>
        public bool IsBowing => Time.time - _bowStart < 0.8f;
        public int Bows => _bows;
        public int Sniffs => _sniffs;
        public int HeardSounds => _heard;
        public int BigStepsFelt => _bigSteps;
        /// <summary>うしろに来たいきものに気づいて、ふり返った回数。</summary>
        public int BackLooks => _backLooks;

        /// <summary>止まっているとき：うしろのすぐそばに、いきもの（テントウムシなど）が来たら、ふり返って見る。</summary>
        bool LookBack(Vector3 pos, float d)
        {
            if (d > 1.2f || Time.time < _nextBackLook || Time.time < _lookAtUntil) return false;
            Vector3 back = Vector3.ProjectOnPlane(pos - _curve.Middle, _tail.normal);
            if (Vector3.Dot(back, Heading) > -0.2f * back.magnitude) return false;
            _nextBackLook = Time.time + 6f;
            _backLooks++;
            LookAt(pos, 2f);
            return true;
        }

        /// <summary>なかよしのいきものにあいさつされたら、頭をちょこんと下げて返す。</summary>
        public void Bow()
        {
            _bowStart = Time.time;
            _bows++;
        }

        /// <summary>大きな音（カラス・カモメ・カエルの声）がしたら、ふとその方へ顔を向ける。</summary>
        public void HearSound(Vector3 pos, float volume)
        {
            // 背伸びしているときは、遊んでいる人が見る向きを決めているので、ふり向かない
            if (volume < 0.3f || Time.time < _hearNext || State == Mode.Hang || State == Mode.Fall || _standRequested) return;
            if (Vector3.Distance(pos, _curve.Head) > 30f) return;
            _hearNext = Time.time + 6f;
            _heard++;
            LookAt(pos, 1.8f, false);
        }

        /// <summary>0.25 秒ごと：まわりのようすを見る（動いているときも）。</summary>
        void UpdateSurroundings(Creatures cr, Vector3 head)
        {
            // カエルや鳥のそばでは、自然に体を低くして、そっと歩く
            _sneak = cr.NearestMob(head, 3f, true, out _, out _, out _);
            // 大きないきもの（せぼねのある動物）が近くを歩くと、足音で地面がゆれて、びくっ
            if (Time.time > _bigStepNext && cr.NearestMob(head, 3.5f, false, out _, out float bs, out var big)
                && big.scale < 0.6f && bs > 0.3f && !big.aquatic)
            {
                _bigStepNext = Time.time + 0.55f;
                _flinch = Mathf.Max(_flinch, 0.45f);
                _softKick += SurfaceUp * 0.05f;
                _bigSteps++;
            }
            // 転がる物を押しているときは、体ごと押しこんで力む
            _straining = IsMoving && PushingProp(head);
            // しずくが前の近くにあると、少し早足
            var col = Collectibles.Instance;
            _hurry = col != null && col.NearestDrop(head, 1.6f * L, out var drop) && Vector3.Dot(drop - head, Heading) > 0f;
        }

        /// <summary>頭のすぐ前で、転がる物が動いている（押している）。</summary>
        bool PushingProp(Vector3 head)
        {
            foreach (var r in RollingProp.All)
            {
                if (r == null || r.Body == null) continue;
                Vector3 d = r.transform.position - head;
                if (d.sqrMagnitude < 0.3f * 0.3f && Vector3.Dot(d, Heading) > 0f && r.Body.linearVelocity.sqrMagnitude > 0.0025f) return true;
            }
            return false;
        }

        /// <summary>止まっているとき：花のそばなら、頭を近づけて、くんくん。</summary>
        void SniffFlowers(Vector3 head)
        {
            // いきものを目で追っているときは、そちらが先
            if (Time.time < _sniffNext || (_lookTrack && Time.time < _lookAtUntil) || _idleTime < 0.4f) return;
            var gen = WorldGenerator.Instance;
            if (gen == null) return;
            foreach (var f in gen.FlowerPoints)
            {
                // 花は、しゃくとりむしより高い所に咲いている：根もとのそばなら、見上げて、くんくん
                Vector3 d = f - head;
                if (d.y < -0.5f || d.y > 3.5f || new Vector2(d.x, d.z).sqrMagnitude > 0.8f * 0.8f) continue;
                _sniffNext = Time.time + Random.Range(10f, 16f);
                _sniffUntil = Time.time + 1.8f;
                _sniffs++;
                LookAt(f, 1.8f, false);
                return;
            }
        }

        /// <summary>
        /// 一歩の時間の倍率：忍び足はゆっくり、力むとゆっくり、しずくが近いと早足、とても強い突風（0.8 より強い）ではゆっくり。
        /// （歩幅は変えない：足を下ろす場所が変わると、細い所や段で、前へ進めなくなることがある）
        /// </summary>
        float ReactTimeScale() => (_sneak ? 1.1f : 1f) * (_straining ? 1.15f : 1f) * (_hurry ? 0.92f : 1f)
                                  * (1f + 0.12f * Mathf.Clamp01((Wind.Gust(Time.time) - 0.8f) * 5f));

        /// <summary>体の高さ：忍び足・力むと低く。</summary>
        float ReactBreathe() => (_sneak && IsMoving ? -0.012f : 0f) + (_straining ? -0.01f : 0f);

        /// <summary>背伸びの頭の下げぐあい：ぺこり・くんくん。</summary>
        float ReactNod(float nod)
        {
            if (IsBowing) nod += 0.7f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((Time.time - _bowStart) / 0.8f));
            if (IsSniffing) nod += 0.25f + 0.12f * Mathf.Sin(_swayPhase * 16f);
            return nod;
        }

        /// <summary>止まっているときに体を起こす高さ（ぺこり・くんくん）。</summary>
        float ReactRear() => IsBowing ? 0.35f : IsSniffing ? 0.3f : 0f;

        /// <summary>歩きながらのぺこり（頭を下げる）。</summary>
        float ReactHead() => IsBowing && State != Mode.Idle ? 0.3f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((Time.time - _bowStart) / 0.8f)) : 0f;
    }
}

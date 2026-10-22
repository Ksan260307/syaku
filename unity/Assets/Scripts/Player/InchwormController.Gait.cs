using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 歩き方の味つけ：歩幅のゆらぎ・調子が出てくる・U ターン前のきょろっ・ゆっくりのときの探り歩き・
    /// はなしたときのブレーキ・すべる石の上の慎重な一歩。
    /// </summary>
    public partial class InchwormController
    {
        // U ターン：後ろへ入力されたら、頭をふって見てから回る
        float _uturnAt = -1f, _uturnSide = 1f;
        // 伸びている途中で手をはなしたら、残りを短くしてゆっくり止まる（一歩に一度）
        bool _brakeShort;
        int _brakes;

        /// <summary>続けて歩くと、調子が出てくる（11 歩目から、少しずつ速く・ゆらぎも小さく）。</summary>
        public bool InGroove => _stepStreak > 10;
        /// <summary>U ターンする前に、ふり返って見ている。</summary>
        public bool IsTurningBack => _uturnAt > 0f;
        /// <summary>伸びている途中で止まった回数（テスト用）。</summary>
        public int BrakeCount => _brakes;
        /// <summary>いまの一歩で頭を下ろす所までの距離（歩幅）。</summary>
        public float LastReachDistance { get; private set; }
        /// <summary>いまの一歩で頭を上げる高さ（体長に対する割合）。</summary>
        public float LastReachLift => _reachLift;
        // 手をはなした時刻（すぐにまた押したときは、ブレーキしない）
        float _releasedAt = -1f;
        /// <summary>ゆっくり歩くときの、頭の左右の探り（体長に対する割合）。</summary>
        public float ProbeSway { get; private set; }

        /// <summary>続けて歩いた歩数による一歩の時間の倍率（11 歩目から少しずつ速く、16 歩で 0.95 倍）。</summary>
        public static float GrooveTimeScale(int streak) => Mathf.Lerp(1f, 0.95f, Mathf.InverseLerp(10f, 16f, streak));

        /// <summary>生きものらしいゆらぎの大きさ（調子が出ると小さく）。</summary>
        float JitterScale => InGroove ? 0.5f : 1f;

        // ゆらぎの順番（出現するたびに 0 から。同じ操作なら、同じ歩き方になる：テストで道すじがぶれない）
        int _jitterStep;

        /// <summary>-1〜1 の、決まった順番のゆらぎ。</summary>
        float NextJitter()
        {
            float h = Mathf.Sin(++_jitterStep * 12.9898f + 4.1414f) * 43758.5453f;
            return (h - Mathf.Floor(h)) * 2f - 1f;
        }

        /// <summary>
        /// 頭を上げる高さのゆらぎ（±8%）。足を下ろす場所はゆらさない（細い枝や丸太の上で、足をふみはずさないように。
        /// 一歩の時間のゆらぎは、もとから）。
        /// </summary>
        float LiftJitter() => 1f + 0.08f * NextJitter() * JitterScale;

        /// <summary>
        /// 後ろへ歩きだすとき：その場で頭をふって後ろを見てから、U ターンする（0.35 秒）。
        /// まだ見ている間は true（このフレームは動きださない）。
        /// </summary>
        bool TurnBackFirst(float offDeg, Vector3 hf, Vector3 desired)
        {
            if (offDeg < 130f || _sleep > 0.3f)
            {
                _uturnAt = -1f;
                return false;
            }
            if (_uturnAt < 0f)
            {
                _uturnAt = Time.time + 0.35f;
                _uturnSide = Vector3.SignedAngle(hf, OnTailPlane(desired, hf), _tail.normal) >= 0f ? 1f : -1f;
            }
            if (Time.time < _uturnAt)
            {
                // 頭をさっとふって、後ろを見る
                _standLookTarget = _uturnSide * 1.2f;
                _standLook = Mathf.MoveTowards(_standLook, _standLookTarget, Time.deltaTime * 5f);
                _lastLookInput = Time.time;
                return true;
            }
            _uturnAt = -1f;
            return false;
        }

        /// <summary>伸びている途中で手をはなした（0.1 秒より長く）：残りの距離を短くして、ゆっくり縮めて止まる。</summary>
        void BrakeReach(bool wantsMove)
        {
            if (wantsMove)
            {
                _releasedAt = -1f;
                return;
            }
            if (_releasedAt < 0f) _releasedAt = Time.time;
            if (Time.time - _releasedAt < 0.1f) return;
            // ふちで糸を出せるときは、短くしない（手をはなして糸を出そうとしている）
            if (_brakeShort || _settling || _braking || _t > 0.5f || CanDropSilk) return;
            _brakeShort = true;
            Vector3 mid = Vector3.Lerp(_from.point, _to.point, 0.8f);
            Vector3 n = Vector3.Slerp(_from.normal, _to.normal, 0.8f).normalized;
            if (!SurfaceProbe.Snap(mid + n * 0.1f, n, 0.3f, out var near) || Vector3.Distance(near.point, mid) > 0.12f) return;
            if (Vector3.Distance(near.point, _tail.point) < 0.3f * L) return;
            _to = near;
            _dur *= 1f + 0.25f * (1f - _t);
            _reachLift *= 0.85f;
            _brakes++;
        }

        /// <summary>ゆっくり歩くときは、持ち上げた頭を左右にふって、足場を探ってから下ろす。</summary>
        Vector3 ProbeOffset(float tm, Vector3 liftN)
        {
            ProbeSway = 0f;
            if (_settling || _moveMag > 0.55f || _moveMag < 0.15f || _sprintBlend > 0.2f) return Vector3.zero;
            Vector3 side = Vector3.Cross(liftN, _to.point - _from.point);
            if (side.sqrMagnitude < 1e-6f) return Vector3.zero;
            float k = Mathf.InverseLerp(0.55f, 0.3f, _moveMag);
            float sway = 0.05f * k * Mathf.Sin(Mathf.PI * 4f * tm) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(tm * 1.25f));
            ProbeSway = Mathf.Abs(sway);
            return side.normalized * (sway * L);
        }

        /// <summary>すべりやすい石（ぬれている・水ぎわ）の上へ頭を下ろす。</summary>
        bool SlipperyStone(SurfacePoint tgt)
        {
            if (_stepKind != 2) return false;
            if (IsWet) return true;
            float wl = Areas.Current.WaterLevelAt(tgt.point.x, tgt.point.z);
            return wl > -100f && tgt.point.y - wl < 0.3f;
        }
    }
}

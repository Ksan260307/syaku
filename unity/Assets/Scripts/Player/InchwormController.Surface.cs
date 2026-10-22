using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 面に合わせた体の使い方：細い枝では低くしがみつく・角を回りこむ・壁に頭をつけてひと呼吸・葉のふちの持ちかえ・
    /// やわらかい所で沈む・ぬれた所ですべりかける・動く足場で踏んばる・坂の途中では頭を水平に。
    /// </summary>
    public partial class InchwormController
    {
        bool _flipAhead;       // 葉の表から裏へ（ふちを回りこむ）
        bool _concaveAhead;    // 床から壁へ（へこんだ角）
        bool _onSoft;          // キノコのかさ・苔の上
        float _slipAt = -99f;
        int _slips;
        float _levelTilt;
        const float SlipTime = 0.45f;

        /// <summary>テスト用：ぬれた所ですべりかける確率（0 未満ならふつう）。</summary>
        public static float SlipChanceOverride = -1f;
        /// <summary>ぬれた所で、足をすべらせかけた回数。</summary>
        public int SlipCount => _slips;
        public bool IsSlipping => Time.time - _slipAt < SlipTime;
        /// <summary>やわらかい所（キノコのかさ・苔・葉っぱ）の上にいる。</summary>
        public bool OnSoftFooting => _onSoft || _onLeaf;
        /// <summary>坂の途中で、頭を水平にしようとかたむけている角度（ラジアン）。</summary>
        public float HeadLevelTilt => _levelTilt;

        /// <summary>テスト用：体をぬらす（seconds 秒たつと、かわく）。</summary>
        public void DebugSetWet(float seconds) => _wetBodyUntil = Time.time + seconds;

        /// <summary>伸びる先の角の形を調べる（出っぱった角の裏へ回りこむ・へこんだ角で壁へ）。</summary>
        void ClassifyCorner(SurfacePoint tgt, Vector3 n, Vector3 go)
        {
            float d = Vector3.Dot(tgt.normal, n);
            _flipAhead = _convexAhead && d < -0.2f;
            _concaveAhead = d < 0.7f && Vector3.Dot(tgt.normal - n, go) < 0f;
        }

        /// <summary>葉のふちを回りこむとき、頭を小さく左右にふって、ふちを持ちかえる。</summary>
        Vector3 FlipWiggle(float tm, Vector3 liftN)
        {
            if (!_flipAhead) return Vector3.zero;
            Vector3 side = Vector3.Cross(liftN, _to.point - _from.point);
            if (side.sqrMagnitude < 1e-6f) return Vector3.zero;
            float w = 0.025f * Mathf.Sin(Mathf.PI * 6f * tm) * Mathf.Sin(Mathf.PI * Mathf.Clamp01((tm - 0.3f) / 0.6f));
            return side.normalized * (w * L);
        }

        /// <summary>頭をついた：足もとのやわらかさ、ぬれた所なら、ときどき足をすべらせかける。</summary>
        void OnHeadPlanted()
        {
            _onSoft = _stepKind == 1 || AreaProps.SoftGround(_head.point) > 0.5f;
            if (_settling || _stepStreak < 2 || !WetFooting(_head)) return;
            float chance = SlipChanceOverride >= 0f ? SlipChanceOverride : 0.12f + 0.15f * _sprintBlend;
            if (Random.value >= chance) return;
            _slipAt = Time.time;
            _slips++;
            _headPulse = 1f;           // あわてて、ぎゅっとつかむ
            TriggerBlink(1.5f);
        }

        /// <summary>ぬれた足場（体がぬれている・水ぎわ）。</summary>
        bool WetFooting(SurfacePoint sp)
        {
            if (IsWet) return true;
            float wl = Areas.Current.WaterLevelAt(sp.point.x, sp.point.z);
            return wl > -100f && sp.point.y - wl < 0.25f;
        }

        /// <summary>すべりかけた頭：前へつるっとすべって、あわててもどす。</summary>
        Vector3 SlipOffset(Vector3 X)
        {
            float s = (Time.time - _slipAt) / SlipTime;
            if (s < 0f || s >= 1f) return Vector3.zero;
            float k = s < 0.2f ? s / 0.2f : 1f - (s - 0.2f) / 0.8f;
            return X * (0.07f * L * ShakuMath.Smooth01(k));
        }

        /// <summary>面に合わせた体の高さ（細い所・やわらかい所・踏んばり・すべりかけ・壁でひと呼吸）。</summary>
        float SurfaceBreathe()
        {
            float b = 0f;
            if (_narrow) b -= 0.012f;                                        // 細い枝では、低くしがみつく
            if (_onSoft && State == Mode.Idle) b -= 0.01f;                    // キノコのかさ・苔に、少し沈む
            b -= 0.025f * Mathf.Clamp01(_platformLean.magnitude * 3f);        // 足場が急に動くと、踏んばって低く
            if (IsSlipping) b -= 0.012f;
            if (IsWallPause) b += 0.025f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t / 0.3f));    // 壁に頭をつけて、体を起こす
            return b;
        }

        /// <summary>坂の途中で止まっているときは、頭だけは水平にしようとする。</summary>
        float LevelHead(Vector3 X, Vector3 U, float dt)
        {
            float want = 0f;
            if (State == Mode.Idle && _rear < 0.3f && !IsPeeking && U.y >= 0.5f && U.y < 0.985f)
                want = Mathf.Clamp(-0.6f * Mathf.Asin(Mathf.Clamp(X.y, -1f, 1f)), -0.35f, 0.35f);
            _levelTilt = Mathf.MoveTowards(_levelTilt, want, dt * 0.8f);
            return _levelTilt;
        }
    }
}

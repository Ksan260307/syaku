using System;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// しゃくとりむしの、休んでいるときのしぐさと気分：眠る・葉をかじる・あくび・日なたぼっこ・うしろのいきものに気づく・
    /// 自分の影をのぞく・どんぐりや小石をつつく・高い所でながめる・エリアのしぐさ（水をのぞく・月を見上げる）・
    /// 小枝のまねでがまんする・登りきって一息・よろこびのダンスのちがい。
    /// </summary>
    public partial class InchwormController
    {
        float _sleep;                    // 眠っている（0〜1）
        float _bask;                     // 日なたぼっこ（0〜1）
        float _endure, _endureUntil;     // 小枝のまねで、がまん（天敵がそばにいる）
        float _nextNibble = 18f, _nibbleStart = -99f, _nibbleUntil = -99f;
        float _nextYawn = 30f;
        float _nextShadow = 35f, _nextPoke = 6f, _nextView = 10f, _nextAreaIdle = 14f, _nextBackLook;
        float _sighStart = -99f, _sighUntil = -99f;
        float _lookUpUntil, _danceStart = -99f, _danceUntil = -99f;
        int _danceStyle;
        float _climbedAt = -99f;

        /// <summary>眠っている（0〜1）。眠ると目をとじて、ゆっくり息をする（画面に「z」）。</summary>
        public float SleepAmount => _sleep;
        public bool IsSleeping => _sleep > 0.5f;
        /// <summary>日なたぼっこをしている（0〜1）。</summary>
        public float BaskAmount => _bask;
        /// <summary>葉をかじっている。</summary>
        public bool IsNibbling => Time.time < _nibbleUntil;
        /// <summary>あくびをしている。</summary>
        public bool IsYawning => Time.time < _yawnUntil;
        /// <summary>小枝のまねで、がまんしている（0〜1）。</summary>
        public float EnduringAmount => _endure;
        /// <summary>葉をかじった（もぐもぐの音と、葉のかけら）。</summary>
        public event Action<Vector3> Nibbled;
        /// <summary>よろこびのダンスの種類（0 ふりふり・1 ぴょん・2 くるり。テスト用）。</summary>
        public int DanceStyle => Time.time < _danceUntil ? _danceStyle : -1;

        /// <summary>テスト用：しぐさを、すぐにはじめる（nibble・yawn・sigh・endure・startle）。</summary>
        public void DebugMood(string kind)
        {
            float t = Time.time;
            switch (kind)
            {
                case "nibble":
                    _nibbleStart = t; _nibbleUntil = t + 2.4f; _chewUntil = t + 2.6f; _nibbleAt = _head.point;
                    break;
                case "yawn": _yawnStart = t; _yawnUntil = t + 1.7f; break;
                case "sigh": Sigh(); break;
                case "endure": _freezeUntil = t + 3f; _endureUntil = t + 3f; break;
                case "startle": _flinch = 1f; Startle(); break;
            }
        }

        /// <summary>眠る時間（じっとしてから）。小枝のまね（40 秒）・ぺたん（90 秒）のあと。</summary>
        public const float SleepAfter = 150f;

        /// <summary>うれしいとき：style 0 = ふりふり、1 = ぴょんと弾む、2 = くるりと見まわして、ふりふり（続けてしずくを取ったとき）。</summary>
        public void Cheer(float strength, int style)
        {
            Cheer(strength);
            if (State != Mode.Idle && State != Mode.Pull && State != Mode.Reach) return;
            _danceStyle = Mathf.Clamp(style, 0, 2);
            _danceStart = Time.time;
            _danceUntil = Time.time + (style == 2 ? 1.2f : 0.7f);
            if (style == 1) Bump(0.55f);
        }

        /// <summary>
        /// 休んでいるときのしぐさ（UpdateIdle から、動いていないときに呼ぶ）。体を起こす高さの希望を返す（なければ 0）。
        /// </summary>
        float UpdateMoods(float dt, bool calm, bool steep)
        {
            float t = Time.time;
            float rear = 0f;
            // ---- 眠る：とても長くじっとしていると、まるくなって眠る ----
            bool sleepy = calm && !steep && _idleTime > SleepAfter && Time.time >= _freezeUntil;
            _sleep = Mathf.MoveTowards(_sleep, sleepy ? 1f : 0f, dt * (sleepy ? 0.4f : 4f));
            if (_sleep > 0.05f) return 0f;   // 眠っている間は、ほかのしぐさをしない

            bool free = calm && _twig < 0.1f && _rest < 0.1f && t >= _lookUntil && t >= _groomUntil && t >= _lookAtUntil && t - _stretchStart > 2.2f;

            // ---- 日なたぼっこ：日なたでじっとしていると、体をのばして目を細める ----
            bool bask = calm && !steep && _idleTime > 6f && _sunlit > 0.8f && _twig < 0.1f && !AreaAtmosphere.IsNight(Areas.Current.Id);
            _bask = Mathf.MoveTowards(_bask, bask ? 1f : 0f, dt * (bask ? 0.35f : 3f));

            // ---- 葉をかじる（足もとが葉っぱ、または頭のそばに草花があるとき） ----
            if (free && _idleTime > 15f && t > _nextNibble)
            {
                _nextNibble = t + UnityEngine.Random.Range(20f, 35f);
                if (CanNibble(out var at))
                {
                    _nibbleStart = t;
                    _nibbleUntil = t + 2.4f;
                    _chewUntil = t + 2.6f;
                    _nibbleAt = at;
                }
            }
            if (t < _nibbleUntil)
            {
                // 頭を面へ下げて、かじるたびに、葉のかけらが散る
                float ph = (t - _nibbleStart) * 9f;
                if (Mathf.Floor(ph / (2f * Mathf.PI)) != Mathf.Floor((ph - dt * 9f) / (2f * Mathf.PI))) Nibbled?.Invoke(_nibbleAt);
            }

            // ---- あくび：ねむくなってくると（力をぬいているとき）、ときどき ----
            if (free && _relax > 0.5f && t > _nextYawn && t >= _nibbleUntil)
            {
                _nextYawn = t + UnityEngine.Random.Range(40f, 60f);
                _yawnStart = t;
                _yawnUntil = t + 1.7f;
            }
            if (t < _yawnUntil) rear = Mathf.Max(rear, 0.35f);

            // ---- 自分の影をのぞく（日なたで、日が低いとき） ----
            if (free && _sunlit > 0.8f && t > _nextShadow && t >= _nibbleUntil && WorldGenerator.Instance != null && WorldGenerator.Instance.sun != null)
            {
                _nextShadow = t + UnityEngine.Random.Range(30f, 50f);
                Vector3 toSun = -WorldGenerator.Instance.sun.transform.forward;
                Vector3 shadow = Vector3.ProjectOnPlane(-toSun, _tail.normal);
                if (toSun.y < 0.75f && shadow.sqrMagnitude > 1e-3f) LookAt(_curve.Middle + shadow.normalized * 0.6f - _tail.normal * 0.1f, 2f, false);
            }

            // ---- どんぐりや小石を、頭でつつく ----
            if (free && _idleTime > 4f && t > _nextPoke && t >= _nibbleUntil)
            {
                _nextPoke = t + UnityEngine.Random.Range(7f, 12f);
                if (FindPokeable(out var prop))
                {
                    Vector3 to = ShakuMath.ProjectOnPlaneSafe(prop - _head.point, _head.normal, Heading);
                    if (to.magnitude > 0.05f && SurfaceProbe.Walk(_head, to.normalized, Mathf.Min(0.18f, to.magnitude), out var tgt, out _)
                        && Vector3.Distance(tgt.point, _tail.point) < 0.98f * L)
                    {
                        LookAt(prop, 1.4f);
                        StartSmallReach(tgt, 0.35f, 0.07f);
                        return rear;
                    }
                }
            }

            // ---- 高い所では、ときどき遠くをながめる ----
            float above = _curve.Head.y - Areas.Current.Height(_curve.Head.x, _curve.Head.z);
            if (free && above > 4f && t > _nextView && _idleTime > 3f)
            {
                _nextView = t + UnityEngine.Random.Range(12f, 20f);
                Survey();
            }

            // ---- エリアのしぐさ：水ぎわでは水をのぞき、夜の森では月を見上げる ----
            if (free && _idleTime > 5f && t > _nextAreaIdle && t >= _nibbleUntil)
            {
                _nextAreaIdle = t + UnityEngine.Random.Range(15f, 25f);
                if (NearWater(out var water))
                {
                    LookAt(water, 1.8f, false);
                    TriggerPeek("water");
                }
                else if (AreaAtmosphere.IsNight(Areas.Current.Id) && WorldGenerator.Instance != null && WorldGenerator.Instance.sun != null)
                {
                    Vector3 toMoon = -WorldGenerator.Instance.sun.transform.forward;
                    LookAt(_curve.Head + Vector3.ProjectOnPlane(toMoon, Vector3.up).normalized * 3f, 2.6f, false);
                    _lookUpUntil = t + 2.6f;
                }
            }
            if (t < _lookUpUntil) rear = Mathf.Max(rear, 0.55f);

            // ---- 登りきって、ふうっと一息 ----
            if (t < _sighUntil) rear = Mathf.Max(rear, 0.5f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - _sighStart) / (_sighUntil - _sighStart))));
            // ---- くるりのダンス ----
            if (t < _danceUntil && _danceStyle == 2)
            {
                rear = Mathf.Max(rear, 0.6f);
                _standLookTarget = 1.4f * Mathf.Sin((t - _danceStart) / 1.2f * Mathf.PI * 2f);
                _lastLookInput = t;
            }
            return rear;
        }

        Vector3 _nibbleAt;

        /// <summary>かじれる物があるか（足もとが葉っぱ・頭のそばに花や芽）。</summary>
        bool CanNibble(out Vector3 at)
        {
            at = _head.point;
            if (SurfaceKindAt(_head) == 3 || _onLeaf) return true;
            var gen = WorldGenerator.Instance;
            if (gen == null) return false;
            Vector3 h = _curve.Head;
            foreach (var f in gen.FlowerPoints)
                if ((f - h).sqrMagnitude < 0.7f * 0.7f)
                {
                    at = f;
                    LookAt(f, 2.4f, false);
                    return true;
                }
            // 苔の上でも、苔をはむ
            if (AreaProps.SoftGround(_head.point) > 0.5f) return true;
            return false;
        }

        /// <summary>頭のすぐ前の、押すと動く物（どんぐり・松ぼっくり・小石）。</summary>
        bool FindPokeable(out Vector3 p)
        {
            p = default;
            Vector3 h = _curve.Head;
            Vector3 f = Heading;
            float best = 0.75f;
            bool found = false;
            foreach (var r in RollingProp.All)
            {
                if (r == null) continue;
                Vector3 q = r.transform.position;
                float d = Vector3.Distance(q, h);
                if (d < best && Vector3.Dot((q - h).normalized, f) > 0.2f) { best = d; p = q; found = true; }
            }
            var loose = WorldGenerator.Instance != null ? WorldGenerator.Instance.loose : null;
            if (loose != null && loose.Count > 0)
            {
                int i = loose.Nearest(h);
                if (i >= 0)
                {
                    Vector3 q = loose.PositionOf(i);
                    string mesh = loose.MeshNameOf(i);
                    float d = Vector3.Distance(q, h);
                    if (d < best && !mesh.StartsWith("Leaf") && Vector3.Dot((q - h).normalized, f) > 0.2f) { p = q; found = true; }
                }
            }
            return found;
        }

        /// <summary>すぐそばに水面があるか（水ぎわ）。</summary>
        bool NearWater(out Vector3 water)
        {
            water = default;
            var area = Areas.Current;
            Vector3 h = _curve.Head;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 q = h + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.9f;
                float wl = area.WaterLevelAt(q.x, q.z);
                if (wl > -100f && area.Height(q.x, q.z) < wl - 0.02f && Mathf.Abs(h.y - wl) < 0.8f)
                {
                    water = new Vector3(q.x, wl, q.z);
                    return true;
                }
            }
            return false;
        }

        /// <summary>登りきって、新しい高さに着いたとき：ふうっと一息。</summary>
        void Sigh()
        {
            _sighStart = Time.time;
            _sighUntil = Time.time + 1.5f;
        }

        /// <summary>天敵がそばにいて、小枝のまねでがまんしている（目をとじて、ゆれをとめる）。</summary>
        void UpdateEndure(float dt)
        {
            bool want = Time.time < _endureUntil && _twig > 0.3f;
            _endure = Mathf.MoveTowards(_endure, want ? 1f : 0f, dt * (want ? 3f : 2f));
        }

        // ------------------------------------------------------------------
        // 体の形への足し算（BuildGroundTarget から）
        // ------------------------------------------------------------------
        /// <summary>体の低さ（息・眠り・日なたぼっこ・寒さ・一息）。</summary>
        float MoodBreathe()
        {
            float t = Time.time;
            float b = -0.012f * _sleep - 0.008f * _bask;
            if (_cold > 0.05f && State == Mode.Idle) b -= 0.006f * _cold;                  // 寒くて、体をちぢめる
            if (t < _sighUntil)
            {
                // 大きく吸って、ふうっとはく
                float k = Mathf.Clamp01((t - _sighStart) / (_sighUntil - _sighStart));
                b += 0.02f * Mathf.Sin(Mathf.PI * k * 2f) * (k < 0.5f ? 1f : 0.6f);
            }
            return b;
        }

        /// <summary>横のゆれ（寒さでふるえる・がまんで止まる・ダンス）。</summary>
        float MoodSway(float sway)
        {
            float t = Time.time;
            if (_cold > 0.05f && State == Mode.Idle) sway += 0.025f * _cold * Mathf.Sin(_swayPhase * 55f) * (1f - _sleep);
            if (_endure > 0f) sway = Mathf.Lerp(sway, 0.004f * Mathf.Sin(_swayPhase * 60f), _endure);   // ぴたっと止まって、ちいさくふるえる
            if (t < _danceUntil && _danceStyle == 1) sway += 0.2f * Mathf.Sin(_swayPhase * 18f);
            return sway;
        }

        /// <summary>頭の角度（かじる）。</summary>
        float MoodHead()
        {
            float t = Time.time;
            if (t < _nibbleUntil)
            {
                float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - _nibbleStart) / 2.4f));
                return -(0.25f + 0.12f * Mathf.Sin((t - _nibbleStart) * 9f)) * k;
            }
            return 0f;
        }

        /// <summary>背伸びの形の、うなずき（あくびで頭をそらす・月を見上げる・手入れでこする）。</summary>
        float MoodNod(float nod)
        {
            float t = Time.time;
            if (t < _yawnUntil) nod -= 0.35f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - _yawnStart) / 1.7f));
            if (t < _lookUpUntil) nod -= 0.55f;
            if (t < _groomUntil) nod += 0.06f * Mathf.Sin(t * 22f);   // 体の手入れ：細かくこする
            return nod;
        }
    }
}

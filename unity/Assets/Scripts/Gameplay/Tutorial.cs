using System;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// チュートリアル（練習の森）：基本の操作を、ひとつずつ練習する。
    /// 画面の上のカードに、いまの練習・押すボタン（キーボード・ゲームパッド・タッチで、それぞれのボタン）・めあてを出し、
    /// できたら「できた！」と音で知らせて、次の練習へ。見まわす・はやく などは、どれだけできたかをバーで見せる。
    /// 行き先は、金色の光のつぶと、行き先の上の ▼（名前つき）と、しゃくとりむしのそばの矢印で示す。
    /// 押すボタンが画面にあるとき（タッチのボタン・右上の地図と図鑑のボタン）は、そのボタンを光らせる。
    /// さいごに北のトンネルをくぐると、本番の森へ（とちゅうでくぐっても、メニューの「チュートリアルをおわって、森へ」でも）。
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        public enum Step { Walk, Look, Zoom, Climb, Aim, Silk, Sprint, Stand, Creature, Zukan, Map, Exit }

        public static readonly Step[] Steps = (Step[])Enum.GetValues(typeof(Step));

        /// <summary>「できた！」を出しておく時間（そのあと次の練習へ）。</summary>
        public const float DoneSeconds = 1.3f;
        /// <summary>見まわす練習：これだけ見まわしたら（ドラッグのピクセル。だいたい 90 度）。</summary>
        public const float LookAmount = 420f;
        /// <summary>ズームの練習：これだけズームしたら（ホイール 3 回ぶん）。</summary>
        public const float ZoomAmount = 1.7f;
        /// <summary>はやくの練習：はやくで歩いた時間（秒）。</summary>
        public const float SprintSeconds = 1.6f;
        /// <summary>背伸びの練習：背伸びしていた時間（秒）。</summary>
        public const float StandSeconds = 1f;

        GameManager _gm;
        int _index;
        float _progress;
        float _doneAt = -1f;
        bool _sawHang, _wasHigh;
        string _device = "";

        public bool Running { get; private set; }
        public int Index => _index;
        public Step Current => Steps[Mathf.Clamp(_index, 0, Steps.Length - 1)];
        /// <summary>「できた！」を出しているところ（次の練習へ進む前）。</summary>
        public bool ShowingDone => _doneAt >= 0f;
        /// <summary>いまの練習で、ためた量（見まわした量・はやくで歩いた時間など。テスト用）。</summary>
        public float Progress => _progress;
        /// <summary>練習ができた（テスト用）。</summary>
        public event Action<Step> StepDone;

        public void Begin(GameManager gm)
        {
            _gm = gm;
            _index = 0;
            Running = true;
            FaceNearestDew();
            StartStep();
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            if (_gm == null) return;
            _gm.ui.ShowTutorialCard(false);
            _gm.ui.SetTutorialHighlight(null);
            _gm.ui.TutorialTarget = null;
            _gm.fx.SetBeacon(null);
        }

        /// <summary>テスト用：その練習まで、とばす。</summary>
        public void SkipTo(Step s)
        {
            _index = (int)s;
            StartStep();
        }

        /// <summary>はじめは、いちばん近いしずくの方へ、体とカメラを向けておく（しずくが画面のまん中に見える）。</summary>
        void FaceNearestDew()
        {
            var worm = _gm.worm;
            if (!_gm.collectibles.NearestRemaining(worm.CenterPosition, out var dew, out _)) return;
            Vector3 d = dew - worm.CenterPosition;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            d.Normalize();
            if (Vector3.Angle(worm.Heading, d) > 15f) worm.Spawn(_gm.world.SpawnPoint, d);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            _gm.followCamera.ShowView(yaw, 9f, 3.6f);   // 少し低めから、しずくを正面に（歩きだすと、いつものカメラにもどる）
        }

        void StartStep()
        {
            _progress = 0f;
            _doneAt = -1f;
            _sawHang = _wasHigh = false;
            Refresh();
            UpdateGuide();
        }

        /// <summary>カードの字とボタンを、いまの練習と操作のしかたに合わせる（とちゅうでゲームパッドやタッチに変えても）。</summary>
        void Refresh()
        {
            _device = _gm.ui.InputDevice;
            var (title, text) = Texts(Current, _device);
            var (keys, action) = Keys(Current, _device);
            string note = _index == 0 ? "メニューから、いつでもチュートリアルをおえて、森へ行けます" : "";
            _gm.ui.SetTutorialStep(_index + 1, Steps.Length, title, keys, action, text, note);
            _gm.ui.SetTutorialProgress(ProgressGoal(Current) > 0f ? Mathf.Clamp01(_progress / ProgressGoal(Current)) : (float?)null);
            _gm.ui.SetTutorialHighlight(Highlight(Current, _device));
            _gm.ui.ShowTutorialCard(true);
        }

        void Update()
        {
            if (!Running || _gm == null || _gm.State != GameManager.GameState.Playing) return;
            if (_gm.ui.InputDevice != _device && _doneAt < 0f) Refresh();
            if (_doneAt >= 0f)
            {
                // 図鑑や地図を開いた練習は、とじてから次へ
                if (Time.unscaledTime - _doneAt < DoneSeconds || _gm.ui.AnyOverlayOpen) return;
                _index = Mathf.Min(_index + 1, Steps.Length - 1);
                StartStep();
                return;
            }
            UpdateGuide();
            bool done = Check();
            float goal = ProgressGoal(Current);
            if (goal > 0f) _gm.ui.SetTutorialProgress(Mathf.Clamp01(_progress / goal));
            if (done) Done();
        }

        void Done()
        {
            _doneAt = Time.unscaledTime;
            _gm.ui.TutorialStepDone();
            _gm.ui.SetTutorialHighlight(null);
            _gm.ui.TutorialTarget = null;
            _gm.fx.SetBeacon(null);
            AudioManager.Instance?.Friend();
            StepDone?.Invoke(Current);
        }

        // ------------------------------------------------------------------
        // できたか
        // ------------------------------------------------------------------
        /// <summary>ためる練習の、めやすの量（ためない練習は 0）。</summary>
        static float ProgressGoal(Step s) =>
            s == Step.Look ? LookAmount : s == Step.Zoom ? ZoomAmount : s == Step.Sprint ? SprintSeconds : s == Step.Stand ? StandSeconds : 0f;

        bool Check()
        {
            var worm = _gm.worm;
            float dt = Time.deltaTime;
            bool free = !_gm.ui.AnyOverlayOpen;
            switch (Current)
            {
                case Step.Walk:
                    return DewTaken(TutorialLayout.Ground(TutorialLayout.FirstDew.x, TutorialLayout.FirstDew.y)) ?? Vector2.Distance(Flat(worm.HeadPosition), TutorialLayout.FirstDew) < 1f;
                case Step.Look:
                    if (free) _progress += GameInput.Look.magnitude;
                    return _progress >= LookAmount;
                case Step.Zoom:
                    if (free) _progress += Mathf.Abs(GameInput.Zoom);
                    return _progress >= ZoomAmount;
                case Step.Climb:
                    return DewTaken(_gm.world.TutorialStumpTop) ?? OnStumpTop(worm.HeadPosition);
                case Step.Silk:
                    // 赤キノコのかさ（か、切り株）の上から、糸でぶら下がって、地面へおりた
                    if (worm.State == InchwormController.Mode.Hang && !worm.IsReeling) _sawHang = true;
                    if (OnCapTop(worm.HeadPosition) || OnStumpTop(worm.HeadPosition)) _wasHigh = true;
                    return (_sawHang || _wasHigh) && worm.State != InchwormController.Mode.Hang && worm.State != InchwormController.Mode.Fall
                           && OnGround(worm.HeadPosition) && OnGround(worm.TailPoint);
                case Step.Sprint:
                    if (free && GameInput.SprintAmount > 0.5f && GameInput.Move.sqrMagnitude > 0.1f && worm.IsMoving) _progress += dt;
                    return _progress >= SprintSeconds;
                case Step.Stand:
                    if (worm.IsStanding) _progress += dt;
                    return _progress >= StandSeconds;
                case Step.Aim:
                    return DewTaken(_gm.world.TutorialCapTop) ?? OnCapTop(worm.HeadPosition);
                case Step.Creature:
                    return SaveSystem.Data.creatures.Count > 0;   // 練習の記録は、からっぽから始まる
                case Step.Zukan:
                    return _gm.ui.IsCollectionOpen;
                case Step.Map:
                    return _gm.ui.IsMapOpen;
                default:
                    return false;   // さいごは、トンネルをくぐる（GameManager が本番の森へ）
            }
        }

        /// <summary>p のそばのしずくを取ったか（そこにしずくがなければ null）。</summary>
        bool? DewTaken(Vector3 p)
        {
            var c = _gm.collectibles;
            for (int i = 0; i < c.TotalDrops; i++)
                if (Vector3.Distance(c.DropPosition(i), p) < 1.6f) return c.IsDropTaken(i);
            return null;
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        static float Above(Vector3 p) => p.y - TutorialLayout.Height(p.x, p.z);

        static bool OnStumpTop(Vector3 p) => Vector2.Distance(new Vector2(p.x, p.z), TutorialLayout.Stump) < 3.6f && Above(p) > 2.6f;

        static bool OnCapTop(Vector3 p) => Vector2.Distance(new Vector2(p.x, p.z), TutorialLayout.Mushroom) < 2.9f && Above(p) > 3.4f;

        static bool OnGround(Vector3 p) => Above(p) < 0.6f;

        // ------------------------------------------------------------------
        // 行き先の目じるし
        // ------------------------------------------------------------------
        /// <summary>いまの練習の行き先（なければ null）。</summary>
        public Vector3? Target()
        {
            var w = _gm != null ? _gm.world : null;
            if (w == null) return null;
            switch (Current)
            {
                case Step.Walk: return TutorialLayout.Ground(TutorialLayout.FirstDew.x, TutorialLayout.FirstDew.y);
                case Step.Climb: return w.TutorialStumpTop;
                case Step.Silk: return TutorialLayout.Ground(TutorialLayout.SilkLanding.x, TutorialLayout.SilkLanding.y);
                case Step.Sprint: return TutorialLayout.Ground(TutorialLayout.Meadow.x, TutorialLayout.Meadow.y);
                case Step.Aim: return w.TutorialCapTop;
                case Step.Creature:
                    if (_gm.creatures != null && _gm.creatures.CountOf("snail") > 0)
                    {
                        Vector3 s = _gm.creatures.PositionOf("snail");
                        return new Vector3(s.x, TutorialLayout.Height(s.x, s.z), s.z);
                    }
                    return null;
                case Step.Exit: return w.Gates.Count > 0 ? w.Gates[0].position : (Vector3?)null;
                default: return null;
            }
        }

        /// <summary>行き先の上の ▼ にそえる名前。</summary>
        public static string TargetLabel(Step s)
        {
            switch (s)
            {
                case Step.Walk: return "しずく";
                case Step.Climb: return "切り株のてっぺん";
                case Step.Aim: return "赤キノコのかさ";
                case Step.Silk: return "ここへおりる";
                case Step.Sprint: return "草原";
                case Step.Creature: return "かたつむり";
                case Step.Exit: return "トンネル";
                default: return "";
            }
        }

        void UpdateGuide()
        {
            Vector3? t = _doneAt < 0f ? Target() : null;
            _gm.fx.SetBeacon(t);
            _gm.ui.TutorialTarget = t;
            _gm.ui.TutorialTargetLabel = TargetLabel(Current);
        }

        // ------------------------------------------------------------------
        // カードの字・押すボタン
        // ------------------------------------------------------------------
        /// <summary>押すボタン（キーの形で、カードにならべる）と、押すとどうなるか（device は "kb"・"pad"・"touch"）。</summary>
        public static (string[] keys, string action) Keys(Step s, string device)
        {
            bool pad = device == "pad", touch = device == "touch";
            string[] move = pad ? new[] { "L スティック" } : touch ? new[] { "左がわをドラッグ" } : new[] { "W", "A", "S", "D" };
            switch (s)
            {
                case Step.Walk: return (move, "すすむ");
                case Step.Look: return (pad ? new[] { "R スティック" } : touch ? new[] { "右がわをドラッグ" } : new[] { "マウスでドラッグ" }, "見まわす");
                case Step.Zoom: return (pad ? new[] { "LB", "RB" } : touch ? new[] { "2本指" } : new[] { "ホイール" }, "近づける・はなす");
                case Step.Climb: return (move, "壁にむかって、すすむ");
                case Step.Aim: return (pad ? new[] { "LT" } : touch ? new[] { "ねらう" } : new[] { "F", "右クリック" }, "押したまま ねらい、はなすと発射");
                case Step.Silk: return (pad ? new[] { "A" } : touch ? new[] { "糸" } : new[] { "Space" }, "ふちで押すと、糸でおりる");
                case Step.Sprint: return (pad ? new[] { "RT" } : touch ? new[] { "はやく" } : new[] { "Shift" }, "押しながら、すすむ");
                case Step.Stand: return (pad ? new[] { "Y" } : touch ? new[] { "背伸び" } : new[] { "E" }, "長押しで、背伸び");
                case Step.Creature: return (move, "近づく");
                case Step.Zukan: return (pad ? new[] { "X" } : touch ? new[] { "本のボタン" } : new[] { "B" }, "図鑑を開く");
                case Step.Map: return (pad ? new[] { "View" } : touch ? new[] { "地図のボタン" } : new[] { "M" }, "地図を開く");
                default: return (move, "すすむ");
            }
        }

        /// <summary>光らせる、画面のボタン（タッチのボタン・右上のボタン）。なければ null。</summary>
        public static string Highlight(Step s, string device)
        {
            bool touch = device == "touch";
            switch (s)
            {
                case Step.Aim: return touch ? "btn-aim" : null;
                case Step.Silk: return touch ? "btn-silk" : null;
                case Step.Sprint: return touch ? "btn-dash" : null;
                case Step.Stand: return touch ? "btn-stand" : null;
                case Step.Zukan: return "collection-button";   // マウスで押しても開ける
                case Step.Map: return "map-button";
                default: return null;
            }
        }

        /// <summary>練習の見出しと、めあて（押すボタンは、カードにキーの形でならべる）。</summary>
        public static (string title, string text) Texts(Step s, string device)
        {
            switch (s)
            {
                case Step.Walk: return ("すすむ", "矢印の方へすすんで、光っている所の、しずくを取ってみよう");
                case Step.Look: return ("見まわす", "カメラを回して、まわりを、ぐるっと見まわしてみよう");
                case Step.Zoom: return ("ズーム", "カメラを、近づけたり、はなしたりしてみよう");
                case Step.Climb: return ("登る", "しゃくとりむしは、どんな面でも登れる。切り株の壁にむかってすすんで、てっぺんのしずくを取ろう");
                case Step.Aim:
                    return ("糸をねらう", device == "touch"
                        ? "「ねらう」を押したまま、指をすべらせて、となりの赤キノコのかさにねらいを合わせよう。はなすと糸がとんで、かさの上へ引きよせられる"
                        : "押したまま、となりの赤キノコのかさに、ねらいを合わせよう。はなすと糸がとんで、かさの上へ引きよせられる");
                case Step.Silk: return ("糸でおりる", "かさのふちまで歩いて、糸を出すと、ぶら下がって下へおりられる（長押しで、糸をのぼる）");
                case Step.Sprint: return ("はやく", "はやく歩いて、光っている草原まで、いそごう");
                case Step.Stand: return ("背伸び", "背伸びすると、上半身を持ち上げて、高い所に頭がとどくよ");
                case Step.Creature: return ("いきもの", "いきものに近づくと、いきもの図鑑にのる。小さな赤キノコの根もとの、かたつむりに会いに行こう");
                case Step.Zukan: return ("図鑑", "いきもの図鑑を開いてみよう。会ったいきものが、のっている（とじると、つぎへ）");
                case Step.Map: return ("地図", "地図を開いてみよう。見つけた名所へは、地図からすぐ行ける（とじると、つぎへ）");
                default: return ("出発！", "これで練習はおしまい。矢印の方の、木の根のトンネルをくぐって、しゃくとりの森へ出発しよう");
            }
        }
    }
}

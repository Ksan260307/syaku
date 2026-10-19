using System;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// チュートリアル（練習の森）：基本の操作を、ひとつずつ練習する。
    /// 画面の上のカードに、いまの練習と、その操作（キーボード・ゲームパッド・タッチで、それぞれの言い方）を出し、
    /// できたら「できた！」と音で知らせて、次の練習へ。行き先は、金色の光のつぶと、足もとの光のわで示す。
    /// さいごに北のトンネルをくぐると、本番の森へ（とちゅうでくぐっても、メニューの「チュートリアルをおわる」でも）。
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
            StartStep();
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            if (_gm == null) return;
            _gm.ui.ShowTutorialCard(false);
            _gm.fx.SetBeacon(null);
        }

        /// <summary>テスト用：その練習まで、とばす。</summary>
        public void SkipTo(Step s)
        {
            _index = (int)s;
            StartStep();
        }

        void StartStep()
        {
            _progress = 0f;
            _doneAt = -1f;
            _sawHang = _wasHigh = false;
            Refresh();
            UpdateBeacon();
        }

        /// <summary>カードの字を、いまの練習と操作のしかたに合わせる（とちゅうでゲームパッドやタッチに変えても）。</summary>
        void Refresh()
        {
            _device = _gm.ui.InputDevice;
            var (title, text) = Texts(Current, _device);
            string note = _index == 0 ? "メニューから、いつでもチュートリアルをおえて、森へ行けます" : "";
            _gm.ui.SetTutorialStep(_index + 1, Steps.Length, title, text, note);
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
            UpdateBeacon();
            if (Check()) Done();
        }

        void Done()
        {
            _doneAt = Time.unscaledTime;
            _gm.ui.TutorialStepDone();
            _gm.fx.SetBeacon(null);
            AudioManager.Instance?.Friend();
            StepDone?.Invoke(Current);
        }

        // ------------------------------------------------------------------
        // できたか
        // ------------------------------------------------------------------
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

        void UpdateBeacon() => _gm.fx.SetBeacon(_doneAt < 0f ? Target() : null);

        // ------------------------------------------------------------------
        // カードの字
        // ------------------------------------------------------------------
        /// <summary>操作の言い方（device は "kb"・"pad"・"touch"）。</summary>
        public static string Key(string action, string device)
        {
            bool pad = device == "pad", touch = device == "touch";
            switch (action)
            {
                case "move": return pad ? "左スティック" : touch ? "画面の左がわに指をおいて、すべらせる" : "W A S D（矢印キーでも）";
                case "look": return pad ? "右スティック" : touch ? "画面の右がわをドラッグ" : "マウスでドラッグ（Q・R キーでも）";
                case "zoom": return pad ? "LB・RB" : touch ? "2本の指を、広げたり、せばめたり" : "マウスホイール";
                case "sprint": return pad ? "RT" : touch ? "「はやく」ボタン" : "Shift";
                case "stand": return pad ? "Y" : touch ? "「背伸び」ボタン" : "E";
                case "silk": return pad ? "A" : touch ? "「糸」ボタン" : "Space";
                case "aim": return pad ? "LT" : touch ? "「ねらう」ボタン" : "F（右クリックでも）";
                case "book": return pad ? "X" : touch ? "右上の本のボタン" : "B";
                case "map": return pad ? "View" : touch ? "右上の地図のボタン" : "M";
            }
            return "";
        }

        /// <summary>練習の見出しと説明。</summary>
        public static (string title, string text) Texts(Step s, string device)
        {
            string K(string a) => Key(a, device);
            switch (s)
            {
                case Step.Walk: return ("すすむ", $"{K("move")}で、すすもう。光っている所の、しずくを取ってみよう");
                case Step.Look: return ("見まわす", $"{K("look")}で、まわりを見まわそう");
                case Step.Zoom: return ("ズーム", $"{K("zoom")}で、カメラを近づけたり、はなしたりしよう");
                case Step.Climb: return ("登る", "しゃくとりむしは、どんな面でも登れる。切り株の壁にむかってすすんで、てっぺんのしずくを取ろう");
                case Step.Silk: return ("糸でおりる", $"高い所のふちで {K("silk")} を押すと、糸でぶら下がって、おりられる。キノコのかさのふちから、下へおりよう（長押しで、糸をのぼる）");
                case Step.Sprint: return ("はやく", $"{K("sprint")} を押しながらすすむと、はやく歩ける。光っている草原まで、いそごう");
                case Step.Stand: return ("背伸び", $"{K("stand")} を長押しすると、背伸びして、上半身を持ち上げる。高い所に、頭がとどくよ");
                case Step.Aim:
                    return ("糸をねらう", device == "touch"
                        ? "「ねらう」ボタンを押したまま、指をすべらせて、となりの赤キノコのかさにねらいを合わせよう。はなすと糸がとんで、かさの上へ引きよせられる"
                        : $"{K("aim")} を押したまま、となりの赤キノコのかさにねらいを合わせよう。はなすと糸がとんで、かさの上へ引きよせられる");
                case Step.Creature: return ("いきもの", "いきものに近づくと、いきもの図鑑にのる。小さな赤キノコの根もとの、かたつむりに会いに行こう");
                case Step.Zukan: return ("図鑑", $"{K("book")} で、いきもの図鑑を開いてみよう。会ったいきものが、のっている");
                case Step.Map: return ("地図", $"{K("map")} で、地図を開いてみよう。見つけた名所へは、地図からすぐ行ける");
                default: return ("出発！", "これで練習はおしまい。北の木の根のトンネルをくぐって、しゃくとりの森へ出発しよう");
            }
        }
    }
}

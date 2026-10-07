using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shakutori
{
    /// <summary>UI Toolkit の画面（タイトル・HUD・地図・図鑑・メニュー・タッチ操作）。</summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameUI : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker s_Hud = new Unity.Profiling.ProfilerMarker("Shaku.Hud");
        public Font bodyFont;
        public Font titleFont;

        public event Action ContinuePressed;
        public event Action NewGamePressed;
        public event Action ResumePressed;
        public event Action ToTitlePressed;
        public event Action ResetPressed;
        public event Action<int> QualityChanged;
        public event Action OverlayChanged;
        public event Action<string> TravelRequested;   // 地図からエリア移動
        public event Action<string> SkinSelected;
        public event Action<int> FastTravelRequested;    // 地図から名所へ
        public event Action RescueRequested;             // 動けなくなったら
        public event Action SaveRequested;               // いますぐセーブ
        public event Action PhotoRequested;              // 写真モードの切りかえ

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern float ShakuSafeInset(int side);
#endif

        VisualElement _root, _hud, _look, _loading, _loadingFill, _title, _fade;
        VisualElement _mapOverlay, _pauseOverlay, _howtoOverlay, _confirmOverlay, _completeOverlay, _collectionOverlay;
        VisualElement _banner, _toasts, _prompt, _help, _touch, _joystick, _knob, _moveZone;
        VisualElement _minimapMask, _minimapImage, _minimapPlayer, _bigmap, _bigmapPlayer, _mapAreas;
        VisualElement _creatureCard, _creatureCardImg, _zukanImg, _skinGrid, _recordList;
        Label _dropCount, _placeCount, _creatureCount, _dropLabel, _areaTitle, _areaName, _loadingText;
        Label _bannerSub, _bannerTitle, _bannerDesc, _promptKey, _promptText, _helpHint, _rotateHint;
        Label _mapTitle, _mapSummary, _confirmText, _completeTitle, _completeText;
        Label _creatureCardName, _creatureCardDesc, _zukanCount, _zukanName, _zukanArea, _zukanDesc;
        VisualElement _zukanHabitat;
        Texture2D _mapTexture;
        ScrollView _legend, _zukanGrid;
        Button _continue, _qLow, _qHigh, _tNormal, _tLarge;
        Slider _sens, _music, _sfx, _ambience, _fov;
        Toggle _invert, _invertX, _autoCam, _minimapRot, _autosave, _motion, _vibrate;
        VisualElement _reticle, _saveIndicator, _minimapNorth;
        Label _reticleText, _creatureCardSub, _silkLabel;
        float _saveShowUntil;
        Action _confirmAction;
        VisualElement _creditsOverlay, _photoHint, _bigmapHere;
        Label _titleProgress, _version, _loadingTip, _pauseSummary;
        Toggle _sprintToggle, _aimToggle, _helpToggle;
        readonly Queue<(string sub, string title, string desc)> _bannerQueue = new Queue<(string, string, string)>();
        readonly List<VisualElement> _miniCreatures = new List<VisualElement>();
        readonly List<Vector3> _nearCreatures = new List<Vector3>();
        float _bannerShownAt, _cardShownAt;
        int _cardExtra;
        string _continueBase = "つづきから";
        string _loadingBase = "";
        string _zukanFilter = "";
        bool _photoMode;
        float _lastTapTime = -9f;
        Vector2 _lastTapPos;
        readonly Dictionary<int, (Vector2 pos, float time)> _tapStart = new Dictionary<int, (Vector2, float)>();
        float _tipTimer, _safeTimer;
        int _tipIndex;
        Vector4 _safeInsets = new Vector4(-1f, -1f, -1f, -1f);

        /// <summary>読み込み画面に出すひとことヒント。</summary>
        public static readonly string[] Tips =
        {
            "壁を登っているときに糸ボタンを押すと、はなれて落ちられます。",
            "高いところで糸を出すと、ぶら下がってゆっくりおりられます。",
            "「ねらう」で好きな場所に糸を飛ばして、たぐり寄せられます。",
            "背伸び中にスティックをたおすと、上半身だけで見まわせます。",
            "見つけた名所へは、地図の「ここへ」からすぐ移動できます。",
            "動けなくなったら、メニューの「動けなくなったら」で安全な場所へもどれます。",
            "いきものに近づくと、図鑑に登録されます。",
            "めったに会えないいきものは、光のつぶをまとっています。",
            "しずくを集めると、新しいきせかえが使えるようになります。",
            "木の根のトンネルを抜けると、別のエリアへ行けます。",
            "カタツムリやカブトムシの背中に乗ると、いっしょに運んでもらえます。",
            "スティックを少しだけたおすと、ゆっくり進みます。",
            "F2 キーで写真モード。景色をゆっくりながめられます。",
        };

        /// <summary>クレジット（素材はすべてこのリポジトリのスクリプトで作ったもの）。</summary>
        public const string CreditsText =
            "しゃくとりの森\n\n" +
            "ゲーム・3Dモデル・音楽・効果音：Unity / Blender / Python のスクリプトで生成\n" +
            "フォント：Noto Sans JP（SIL Open Font License 1.1）\n" +
            "エンジン：Unity 6\n\n" +
            "遊んでくれて、ありがとう！";

        readonly Dictionary<int, VisualElement> _miniMarkers = new Dictionary<int, VisualElement>();
        readonly List<VisualElement> _miniGates = new List<VisualElement>();
        readonly List<VisualElement> _miniDots = new List<VisualElement>();
        readonly List<(VisualElement el, Vector2 uv, Vector2 offset)> _bigItems = new List<(VisualElement, Vector2, Vector2)>();
        readonly Dictionary<int, Vector2> _lookPointers = new Dictionary<int, Vector2>();
        readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        readonly Dictionary<string, VisualElement> _pages = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, VisualElement> _zukanCards = new Dictionary<string, VisualElement>();
        int _joyPointer = -1;
        Vector2 _joyCenter;
        float _bannerUntil, _creatureUntil;
        float _helpTimer;
        float _rotateTimer;
        bool _helpVisible = true;
        string _lastHelpDevice = "";
        string _howtoDevice = "";
        int _lastDrops = -1, _lastPlaces = -1, _lastCreatures = -1;
        bool _touchMode;
        string _tab = "zukan";
        string _selectedSpecies;
        string _cardSpecies;
        PanelSettings _panel;
        Vector2Int _baseResolution;

        const float JoyRadius = 115f;   // スティックの外枠の半径
        const float JoyMax = 80f;       // つまみが動ける距離

        public bool IsMapOpen => !_mapOverlay.ClassListContains("hidden");
        public bool IsPauseOpen => !_pauseOverlay.ClassListContains("hidden");
        public bool IsHowtoOpen => !_howtoOverlay.ClassListContains("hidden");
        public bool IsConfirmOpen => !_confirmOverlay.ClassListContains("hidden");
        public bool IsCompleteOpen => !_completeOverlay.ClassListContains("hidden");
        public bool IsCollectionOpen => !_collectionOverlay.ClassListContains("hidden");
        public bool IsCreditsOpen => _creditsOverlay != null && !_creditsOverlay.ClassListContains("hidden");
        public bool AnyOverlayOpen => IsMapOpen || IsPauseOpen || IsHowtoOpen || IsConfirmOpen || IsCompleteOpen || IsCollectionOpen || IsCreditsOpen;
        public bool IsPhotoMode => _photoMode;
        public string TitleProgressText => _titleProgress != null ? _titleProgress.text : "";
        public string ContinueText => _continue != null ? _continue.text : "";
        public string LoadingTipText => _loadingTip != null ? _loadingTip.text : "";
        public string PauseSummaryText => _pauseSummary != null ? _pauseSummary.text : "";
        public int QueuedBanners => _bannerQueue.Count;
        public int VisibleCreatureHints => _miniCreatures.Count(e => e.style.display == DisplayStyle.Flex);
        public int MapHintCount => _bigItems.Count(it => it.el.ClassListContains("map-hint"));
        public string ZukanFilter => _zukanFilter;
        public string CreatureCardSub => _creatureCardSub != null ? _creatureCardSub.text : "";
        public Vector4 SafeInsets => _safeInsets;

        /// <summary>ミニマップに近くの未発見のいきものを出すため（GameManager が設定）。</summary>
        public Creatures CreatureSource { get; set; }
        public bool IsBannerVisible => _banner != null && _banner.ClassListContains("banner--show");
        public string BannerTitle => _bannerTitle != null ? _bannerTitle.text : "";
        public int ToastCount => _toasts != null ? _toasts.childCount : 0;
        public string PromptText => _prompt != null && _prompt.ClassListContains("prompt--show") ? _promptText.text : "";
        public bool IsHudVisible => _hud != null && !_hud.ClassListContains("hud--hidden");
        public bool IsCreatureCardVisible => _creatureCard != null && _creatureCard.ClassListContains("creature-card--show");
        public string CreatureCardName => _creatureCardName != null ? _creatureCardName.text : "";
        public string CurrentTab => _tab;
        public string SelectedSpecies => _selectedSpecies;
        public string ZukanDetailName => _zukanName != null ? _zukanName.text : "";
        public int ZukanCardCount => _zukanCards.Count;
        public int SkinCardCount => _skinGrid != null ? _skinGrid.childCount : 0;
        public int AreaChipCount => _mapAreas != null ? _mapAreas.childCount : 0;
        public string AreaTitle => _areaTitle != null ? _areaTitle.text : "";
        public bool IsTouchMode => _touchMode;
        public bool IsJoystickActive => _joyPointer != -1;
        public VisualElement Root => _root;

        /// <summary>トンネルの近くにいるときのヒント（GameManager が設定）。</summary>
        public string GateHint { get; set; }

        /// <summary>テスト用：タッチ画面かどうかを固定する（null で自動判定）。</summary>
        public bool? ForceTouch { get; set; }

        Collectibles _collect;
        InchwormController _worm;
        Transform _cam;
        WorldAssets _assets;

        void Awake()
        {
            // スマホでは UI を大きくするため基準解像度を変える。終わったら元に戻す（エディタでアセットを書き換えたままにしない）
            var doc = GetComponent<UIDocument>();
            _panel = doc != null ? doc.panelSettings : null;
            if (_panel != null) _baseResolution = _panel.referenceResolution;
        }

        void OnDestroy()
        {
            SaveSystem.Saved -= OnSaved;
            if (_panel != null && _baseResolution.x > 0) _panel.referenceResolution = _baseResolution;
        }

        void OnEnable()
        {
            EnsureBound();
        }

        void Start()
        {
            EnsureBound();
        }

        /// <summary>UIDocument の木ができてから一度だけ結びつける。</summary>
        bool EnsureBound()
        {
            if (_hud != null) return true;
            var doc = GetComponent<UIDocument>();
            _root = doc != null ? doc.rootVisualElement : null;
            if (_root == null || _root.Q("hud") == null) return false;
            if (bodyFont != null) _root.style.unityFontDefinition = FontDefinition.FromFont(bodyFont);
            Bind();
            return true;
        }

        T Q<T>(string name) where T : VisualElement => _root.Q<T>(name);

        void Bind()
        {
            _hud = Q<VisualElement>("hud");
            _look = Q<VisualElement>("look-area");
            _loading = Q<VisualElement>("loading");
            _loadingFill = Q<VisualElement>("loading-fill");
            _loadingText = Q<Label>("loading-text");
            _title = Q<VisualElement>("title-overlay");
            _fade = Q<VisualElement>("fade");
            _mapOverlay = Q<VisualElement>("map-overlay");
            _pauseOverlay = Q<VisualElement>("pause-overlay");
            _howtoOverlay = Q<VisualElement>("howto-overlay");
            _confirmOverlay = Q<VisualElement>("confirm-overlay");
            _completeOverlay = Q<VisualElement>("complete-overlay");
            _collectionOverlay = Q<VisualElement>("collection-overlay");
            _banner = Q<VisualElement>("banner");
            _bannerSub = Q<Label>("banner-sub");
            _bannerTitle = Q<Label>("banner-title");
            _bannerDesc = Q<Label>("banner-desc");
            _creatureCard = Q<VisualElement>("creature-card");
            _creatureCardImg = Q<VisualElement>("creature-card-img");
            _creatureCardName = Q<Label>("creature-card-name");
            _creatureCardDesc = Q<Label>("creature-card-desc");
            _toasts = Q<VisualElement>("toasts");
            _prompt = Q<VisualElement>("prompt");
            _promptKey = Q<Label>("prompt-key");
            _promptText = Q<Label>("prompt-text");
            _help = Q<VisualElement>("help");
            _helpHint = Q<Label>("help-hint");
            _rotateHint = Q<Label>("rotate-hint");
            _touch = Q<VisualElement>("touch");
            _moveZone = Q<VisualElement>("move-zone");
            _joystick = Q<VisualElement>("joystick");
            _knob = Q<VisualElement>("joystick-knob");
            _areaTitle = Q<Label>("area-title");
            _dropLabel = Q<Label>("drop-label");
            _dropCount = Q<Label>("drop-count");
            _placeCount = Q<Label>("place-count");
            _creatureCount = Q<Label>("creature-count");
            _areaName = Q<Label>("area-name");
            _minimapMask = Q<VisualElement>("minimap-mask");
            _minimapImage = Q<VisualElement>("minimap-image");
            _minimapPlayer = Q<VisualElement>("minimap-player");
            _bigmap = Q<VisualElement>("bigmap");
            _bigmapPlayer = Q<VisualElement>("bigmap-player");
            _mapTitle = Q<Label>("map-title");
            _mapSummary = Q<Label>("map-summary");
            _mapAreas = Q<VisualElement>("map-areas");
            _legend = Q<ScrollView>("map-legend");
            _zukanCount = Q<Label>("zukan-count");
            _zukanGrid = Q<ScrollView>("zukan-grid");
            _zukanImg = Q<VisualElement>("zukan-img");
            _zukanName = Q<Label>("zukan-name");
            _zukanArea = Q<Label>("zukan-area");
            _zukanHabitat = Q<VisualElement>("zukan-habitat");
            _zukanDesc = Q<Label>("zukan-desc");
            _skinGrid = Q<VisualElement>("skin-grid");
            _recordList = Q<VisualElement>("record-list");
            _confirmText = Q<Label>("confirm-text");
            _completeTitle = Q<Label>("complete-title");
            _completeText = Q<Label>("complete-text");
            _continue = Q<Button>("btn-continue");
            _qLow = Q<Button>("q-low");
            _qHigh = Q<Button>("q-high");
            _sens = Q<Slider>("set-sens");
            _music = Q<Slider>("set-music");
            _sfx = Q<Slider>("set-sfx");
            _invert = Q<Toggle>("set-invert");
            _invertX = Q<Toggle>("set-invertx");
            _autoCam = Q<Toggle>("set-autocam");
            _minimapRot = Q<Toggle>("set-minimap");
            _autosave = Q<Toggle>("set-autosave");
            _motion = Q<Toggle>("set-motion");
            _vibrate = Q<Toggle>("set-vibrate");
            _ambience = Q<Slider>("set-ambience");
            _fov = Q<Slider>("set-fov");
            _tNormal = Q<Button>("t-normal");
            _tLarge = Q<Button>("t-large");
            _reticle = Q<VisualElement>("reticle");
            _reticleText = Q<Label>("reticle-text");
            _saveIndicator = Q<VisualElement>("save-indicator");
            _creatureCardSub = Q<Label>("creature-card-sub");
            _silkLabel = Q<Label>("btn-silk-label");
            _minimapNorth = _root.Q<Label>(className: "minimap-north");
            _creditsOverlay = Q<VisualElement>("credits-overlay");
            _photoHint = Q<VisualElement>("photo-hint");
            _titleProgress = Q<Label>("title-progress");
            _version = Q<Label>("version");
            _loadingTip = Q<Label>("loading-tip");
            _pauseSummary = Q<Label>("pause-summary");
            _sprintToggle = Q<Toggle>("set-sprinttoggle");
            _aimToggle = Q<Toggle>("set-aimtoggle");
            _helpToggle = Q<Toggle>("set-help");
            if (_continue != null) _continueBase = _continue.text;
            if (_version != null) _version.text = "ver. " + Application.version;
            var credits = Q<Label>("credits-text");
            if (credits != null) credits.text = CreditsText;
            if (_loadingTip != null) _loadingTip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];

            _tabs["zukan"] = Q<Button>("tab-zukan");
            _tabs["skin"] = Q<Button>("tab-skin");
            _tabs["record"] = Q<Button>("tab-record");
            _pages["zukan"] = Q<VisualElement>("page-zukan");
            _pages["skin"] = Q<VisualElement>("page-skin");
            _pages["record"] = Q<VisualElement>("page-record");

            foreach (var e in _root.Query<Label>(className: "logo").ToList()) SetTitleFont(e);
            foreach (var e in _root.Query<Label>(className: "loading-logo").ToList()) SetTitleFont(e);
            SetTitleFont(_bannerTitle);

            Click("btn-continue", () => ContinuePressed?.Invoke());
            Click("btn-new", () =>
            {
                if (SaveSystem.HasSave)
                    Confirm("いまの記録を消して、はじめから遊びますか？", () => NewGamePressed?.Invoke());
                else NewGamePressed?.Invoke();
            });
            Click("btn-howto", () => ShowHowto(true));
            Click("howto-close", () => ShowHowto(false));
            Click("pause-howto", () => ShowHowto(true));
            Click("pause-collection", () => ShowCollection(true));
            Click("resume", () => ResumePressed?.Invoke());
            Click("menu-button", () => TogglePause());
            Click("map-button", () => ToggleMap());
            Click("map-close", () => ShowMap(false));
            Click("collection-button", () => ToggleCollection());
            Click("collection-close", () => ShowCollection(false));
            Click("tab-zukan", () => SelectTab("zukan"));
            Click("tab-skin", () => SelectTab("skin"));
            Click("tab-record", () => SelectTab("record"));
            Click("fullscreen-button", ToggleFullscreen);
            Click("fullscreen", ToggleFullscreen);
            Click("to-title", () => ToTitlePressed?.Invoke());
            Click("reset", () => Confirm("集めたしずく・名所・図鑑の記録を消して、最初からやり直しますか？", () => ResetPressed?.Invoke()));
            Click("confirm-no", () => ShowConfirm(false));
            Click("confirm-yes", () =>
            {
                ShowConfirm(false);
                _confirmAction?.Invoke();
            });
            Click("complete-close", () => ShowComplete(false, ""));
            Click("q-low", () => SetQuality(0));
            Click("q-high", () => SetQuality(1));
            Click("t-normal", () => SetTextSize(0));
            Click("t-large", () => SetTextSize(1));
            Click("save-now", () => SaveRequested?.Invoke());
            Click("rescue", () => Confirm("最後に安全だった場所へもどりますか？（動けなくなったときに使ってください）", () => RescueRequested?.Invoke()));
            Click("btn-credits", () => ShowCredits(true));
            Click("credits-close", () => ShowCredits(false));
            // 窓の外（うす暗い所）をタップすると、閉じる
            CloseOnBackdrop(_mapOverlay, () => ShowMap(false));
            CloseOnBackdrop(_pauseOverlay, () => ResumePressed?.Invoke());
            CloseOnBackdrop(_collectionOverlay, () => ShowCollection(false));
            CloseOnBackdrop(_howtoOverlay, () => ShowHowto(false));
            CloseOnBackdrop(_confirmOverlay, () => ShowConfirm(false));
            CloseOnBackdrop(_creditsOverlay, () => ShowCredits(false));
            CloseOnBackdrop(_completeOverlay, () => ShowComplete(false, ""));
            Click("photo", () => PhotoRequested?.Invoke());
            Click("reset-settings", () =>
            {
                SaveSystem.ResetSettings();
                RefreshSettingsUI();
                ApplySettings();
                QualityChanged?.Invoke(SaveSystem.Settings.quality);
                Toast("設定を最初の状態にもどしました", "icon-menu");
            });
            // いきもののカードをタップすると、図鑑のそのいきもののページを開く
            _creatureCard.RegisterCallback<ClickEvent>(e =>
            {
                if (!IsCreatureCardVisible || _cardSpecies == null) return;
                ShowCollection(true, "zukan");
                SelectSpecies(_cardSpecies);
            });
            SaveSystem.Saved += OnSaved;

            RefreshSettingsUI();
            _sens.RegisterValueChangedCallback(e => { SaveSystem.Settings.sensitivity = e.newValue; ApplySettings(); });
            _music.RegisterValueChangedCallback(e => { SaveSystem.Settings.music = e.newValue; ApplySettings(); });
            _sfx.RegisterValueChangedCallback(e => { SaveSystem.Settings.sfx = e.newValue; ApplySettings(); });
            _ambience.RegisterValueChangedCallback(e => { SaveSystem.Settings.ambience = e.newValue; ApplySettings(); });
            _fov.RegisterValueChangedCallback(e => { SaveSystem.Settings.fov = e.newValue; ApplySettings(); });
            _invert.RegisterValueChangedCallback(e => { SaveSystem.Settings.invertY = e.newValue; ApplySettings(); });
            _invertX.RegisterValueChangedCallback(e => { SaveSystem.Settings.invertX = e.newValue; ApplySettings(); });
            _autoCam.RegisterValueChangedCallback(e => { SaveSystem.Settings.autoCamera = e.newValue; ApplySettings(); });
            _minimapRot.RegisterValueChangedCallback(e => { SaveSystem.Settings.minimapRotate = e.newValue; ApplySettings(); });
            _autosave.RegisterValueChangedCallback(e => { SaveSystem.Settings.autosave = e.newValue; ApplySettings(); });
            _motion.RegisterValueChangedCallback(e => { SaveSystem.Settings.reduceMotion = e.newValue; ApplySettings(); });
            _vibrate.RegisterValueChangedCallback(e => { SaveSystem.Settings.vibration = e.newValue; ApplySettings(); });
            _sprintToggle?.RegisterValueChangedCallback(e =>
            {
                SaveSystem.Settings.sprintToggle = e.newValue;
                if (!e.newValue) GameInput.ClearSprintLatch();
                ApplySettings();
            });
            _aimToggle?.RegisterValueChangedCallback(e => { SaveSystem.Settings.aimToggle = e.newValue; ApplySettings(); });
            _helpToggle?.RegisterValueChangedCallback(e =>
            {
                SaveSystem.Settings.showHelp = e.newValue;
                SetHelpVisible(e.newValue);
                _helpTimer = -999f;
                ApplySettings();
            });

            _bigmap.RegisterCallback<GeometryChangedEvent>(e => LayoutBigMap());
            _root.RegisterCallback<GeometryChangedEvent>(e => UpdateLayoutClasses());

            SetupLookArea();
            SetupTouch();
            BuildHowtoGrid();
            ApplySettings(false);
        }

        /// <summary>保存されている設定を、メニューの見た目にそろえる（値を変えたイベントは出さない）。</summary>
        public void RefreshSettingsUI()
        {
            if (_sens == null) return;
            var s = SaveSystem.Settings;
            _sens.SetValueWithoutNotify(s.sensitivity);
            _music.SetValueWithoutNotify(s.music);
            _sfx.SetValueWithoutNotify(s.sfx);
            _ambience.SetValueWithoutNotify(s.ambience);
            _fov.SetValueWithoutNotify(s.fov);
            _invert.SetValueWithoutNotify(s.invertY);
            _invertX.SetValueWithoutNotify(s.invertX);
            _autoCam.SetValueWithoutNotify(s.autoCamera);
            _minimapRot.SetValueWithoutNotify(s.minimapRotate);
            _autosave.SetValueWithoutNotify(s.autosave);
            _motion.SetValueWithoutNotify(s.reduceMotion);
            _vibrate.SetValueWithoutNotify(s.vibration);
            _sprintToggle?.SetValueWithoutNotify(s.sprintToggle);
            _aimToggle?.SetValueWithoutNotify(s.aimToggle);
            _helpToggle?.SetValueWithoutNotify(s.showHelp);
            ApplySettings(false);
        }

        void OnSaved()
        {
            _saveShowUntil = Time.unscaledTime + 1.6f;
        }

        public bool IsSaveIndicatorVisible => _saveIndicator != null && _saveIndicator.ClassListContains("save-indicator--show");
        public bool IsReticleVisible => _reticle != null && !_reticle.ClassListContains("hidden");
        public string ReticleText => _reticleText != null ? _reticleText.text : "";

        void SetTextSize(int size)
        {
            SaveSystem.Settings.textSize = size;
            ApplySettings();
        }

        void SetTitleFont(VisualElement e)
        {
            if (e != null && titleFont != null) e.style.unityFontDefinition = FontDefinition.FromFont(titleFont);
        }

        void Click(string name, Action a)
        {
            var b = Q<Button>(name);
            if (b == null) return;
            b.clicked += () =>
            {
                AudioManager.Instance?.Click();
                a();
            };
        }

        public void ApplySettings() => ApplySettings(true);

        public void ApplySettings(bool save)
        {
            var s = SaveSystem.Settings;
            GameInput.LookSensitivity = s.sensitivity;
            GameInput.InvertY = s.invertY;
            GameInput.InvertX = s.invertX;
            _qLow.EnableInClassList("chip--on", s.quality == 0);
            _qHigh.EnableInClassList("chip--on", s.quality != 0);
            _tNormal.EnableInClassList("chip--on", s.textSize == 0);
            _tLarge.EnableInClassList("chip--on", s.textSize != 0);
            // スライダーを動かしている間に何度も書きこまないよう、少しあとでまとめて保存する
            if (save) SaveSystem.SaveSettingsSoon();
        }

        void SetQuality(int q)
        {
            SaveSystem.Settings.quality = q;
            ApplySettings();
            QualityChanged?.Invoke(q);
        }

        static void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        /// <summary>エリアを作るたびに呼ぶ（地図・目標・図鑑を今のエリアに合わせる）。</summary>
        public void Init(Collectibles collect, InchwormController worm, Transform cam, Texture2D map, WorldAssets assets = null)
        {
            EnsureBound();
            _collect = collect;
            _worm = worm;
            _cam = cam;
            if (assets != null) _assets = assets;
            _minimapImage.style.backgroundImage = new StyleBackground(map);
            _bigmap.style.backgroundImage = new StyleBackground(map);
            _mapTexture = map;
            var area = collect != null ? collect.Area : Areas.Current;
            _areaTitle.text = area.Subtitle;
            _dropLabel.text = area.DropName;
            _mapTitle.text = area.DisplayName + "の地図";
            _areaName.text = "";
            _lastDrops = _lastPlaces = _lastCreatures = -1;
            BuildMarkers();
            RefreshCounts(true);
        }

        // ------------------------------------------------------------------
        // 画面の切り替え
        // ------------------------------------------------------------------
        public void SetLoading(float progress, string text)
        {
            if (!EnsureBound()) return;
            _loadingFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            if (!string.IsNullOrEmpty(text)) _loadingBase = text;
            int pct = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f);
            _loadingText.text = pct > 0 && pct < 100 ? $"{_loadingBase}　{pct}%" : _loadingBase;
        }

        void NextTip()
        {
            if (_loadingTip == null) return;
            _tipIndex = (_tipIndex + 1 + UnityEngine.Random.Range(0, Tips.Length - 1)) % Tips.Length;
            _loadingTip.text = Tips[_tipIndex];
            _tipTimer = 0f;
        }

        /// <summary>エリア移動のときにもう一度読み込み画面を出す。</summary>
        public void ShowLoading(string text)
        {
            if (!EnsureBound()) return;
            _loading.RemoveFromClassList("hidden");
            _loading.RemoveFromClassList("loading--out");
            _loading.pickingMode = PickingMode.Position;
            NextTip();
            SetLoading(0f, text);
        }

        public bool IsLoadingVisible => _loading != null && !_loading.ClassListContains("hidden") && !_loading.ClassListContains("loading--out");

        public void HideLoading()
        {
            _loading.AddToClassList("loading--out");
            _loading.pickingMode = PickingMode.Ignore;
            _loading.schedule.Execute(() =>
            {
                if (_loading.ClassListContains("loading--out")) _loading.AddToClassList("hidden");
            }).StartingIn(900);
        }

        public void ShowTitle(bool show)
        {
            if (show)
            {
                _title.RemoveFromClassList("hidden");
                _title.RemoveFromClassList("title-overlay--out");
                _continue.style.display = SaveSystem.HasSave ? DisplayStyle.Flex : DisplayStyle.None;
                RefreshTitleProgress();
                SetPhotoMode(false);
                _hud.AddToClassList("hud--hidden");
                _hud.pickingMode = PickingMode.Ignore;
                _continue.Focus();
            }
            else
            {
                _title.AddToClassList("title-overlay--out");
                _title.schedule.Execute(() => _title.AddToClassList("hidden")).StartingIn(800);
                _hud.RemoveFromClassList("hud--hidden");
                _helpTimer = 0f;
                SetHelpVisible(SaveSystem.Settings.showHelp);
            }
        }

        /// <summary>タイトルに、前回の続きの場所と集めた数を出す。</summary>
        public void RefreshTitleProgress()
        {
            if (_titleProgress == null || _continue == null) return;
            if (!SaveSystem.HasSave)
            {
                _continue.text = _continueBase;
                _titleProgress.text = "";
                return;
            }
            var d = SaveSystem.Data;
            var area = Areas.Get(d.area) ?? Areas.Forest;
            _continue.text = $"{_continueBase}（{area.DisplayName}）";
            _titleProgress.text = $"しずく {Collectibles.AllCollectedDrops} / {Areas.TotalDrops}　名所 {Collectibles.AllDiscoveredPlaces} / {Areas.TotalLandmarks}　" +
                                  $"いきもの {Creatures.DiscoveredCount} / {SpeciesCatalog.Count}　（{PlayTimeText(d.playTime)}）";
        }

        /// <summary>あそんだ時間を「1時間 5分」「12分」のように書く。</summary>
        public static string PlayTimeText(float seconds)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return t >= 3600 ? $"{t / 3600}時間 {t / 60 % 60}分" : $"{t / 60}分";
        }

        /// <summary>全体の達成度（しずく・名所・いきもの を合わせた割合、0〜100）。</summary>
        public static int CompletionPercent()
        {
            int total = Areas.TotalDrops + Areas.TotalLandmarks + SpeciesCatalog.Count;
            int got = Collectibles.AllCollectedDrops + Collectibles.AllDiscoveredPlaces + Creatures.DiscoveredCount;
            return total > 0 ? Mathf.Clamp(Mathf.FloorToInt(100f * got / total), 0, 100) : 0;
        }

        public bool IsTitleVisible => !_title.ClassListContains("hidden") && !_title.ClassListContains("title-overlay--out");

        public void Fade(bool on) => _fade.EnableInClassList("fade--on", on);

        public void ShowPause(bool show)
        {
            _pauseOverlay.EnableInClassList("hidden", !show);
            if (show)
            {
                if (_pauseSummary != null)
                {
                    var area = MapArea;
                    string place = _collect != null ? $"{area.DropName} {_collect.CollectedDrops} / {_collect.TotalDrops}　名所 {_collect.DiscoveredPlaces} / {_collect.TotalPlaces}" : "";
                    _pauseSummary.text = $"{area.Subtitle}　{place}\n全体の達成度 {CompletionPercent()}%　あそんだ時間 {PlayTimeText(SaveSystem.Data.playTime)}";
                }
                Q<Button>("resume").Focus();
            }
            OverlayChanged?.Invoke();
        }

        public void TogglePause() => ResumeOrPause();

        void ResumeOrPause()
        {
            if (IsPauseOpen) ResumePressed?.Invoke();
            else ShowPause(true);
        }

        public void ShowMap(bool show)
        {
            _mapOverlay.EnableInClassList("hidden", !show);
            if (show)
            {
                RefreshBigMap();
                Q<Button>("map-close").Focus();
            }
            OverlayChanged?.Invoke();
        }

        public void ToggleMap() => ShowMap(!IsMapOpen);

        public void ShowHowto(bool show)
        {
            if (show) BuildHowtoGrid();
            _howtoOverlay.EnableInClassList("hidden", !show);
            if (show) Q<Button>("howto-close").Focus();
            OverlayChanged?.Invoke();
        }

        public void ShowCredits(bool show)
        {
            if (_creditsOverlay == null) return;
            _creditsOverlay.EnableInClassList("hidden", !show);
            if (show) Q<Button>("credits-close")?.Focus();
            OverlayChanged?.Invoke();
        }

        /// <summary>写真モード：画面の表示をすべて消して、景色だけにする。</summary>
        public void SetPhotoMode(bool on)
        {
            if (!EnsureBound() || _photoMode == on) return;
            _photoMode = on;
            _hud.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            _photoHint?.EnableInClassList("hidden", !on);
            if (on) EndJoystick();
        }

        void Confirm(string text, Action yes)
        {
            _confirmText.text = text;
            _confirmAction = yes;
            ShowConfirm(true);
        }

        void ShowConfirm(bool show)
        {
            _confirmOverlay.EnableInClassList("hidden", !show);
            if (show) Q<Button>("confirm-no").Focus();
            OverlayChanged?.Invoke();
        }

        public void ShowComplete(bool show, string text) => ShowComplete(show, null, text);

        public void ShowComplete(bool show, string title, string text)
        {
            if (show)
            {
                _completeText.text = text;
                if (!string.IsNullOrEmpty(title)) _completeTitle.text = title;
            }
            _completeOverlay.EnableInClassList("hidden", !show);
            if (show) Q<Button>("complete-close").Focus();
            OverlayChanged?.Invoke();
        }

        public void CloseAllOverlays()
        {
            _mapOverlay.AddToClassList("hidden");
            _pauseOverlay.AddToClassList("hidden");
            _howtoOverlay.AddToClassList("hidden");
            _confirmOverlay.AddToClassList("hidden");
            _completeOverlay.AddToClassList("hidden");
            _collectionOverlay.AddToClassList("hidden");
            _creditsOverlay?.AddToClassList("hidden");
            OverlayChanged?.Invoke();
        }

        /// <summary>Esc/戻るボタン: 一番上のウィンドウを閉じる。閉じるものがなければ false。</summary>
        /// <summary>
        /// 窓の外（うしろのうす暗い所）をタップしたら閉じる。押した所と、はなした所の両方が窓の外のときだけ
        /// （窓の中のスライダーを動かして、外ではなしたときは閉じない）。
        /// </summary>
        void CloseOnBackdrop(VisualElement overlay, Action close)
        {
            if (overlay == null) return;
            bool downOutside = false;
            overlay.RegisterCallback<PointerDownEvent>(e => downOutside = e.target == overlay);
            overlay.RegisterCallback<PointerUpEvent>(e =>
            {
                bool outside = e.target == overlay && downOutside;
                downOutside = false;
                if (!outside || overlay.ClassListContains("hidden")) return;
                close();
                e.StopPropagation();
            });
        }

        public bool Back()
        {
            if (IsConfirmOpen) { ShowConfirm(false); return true; }
            if (IsCreditsOpen) { ShowCredits(false); return true; }
            if (IsHowtoOpen) { ShowHowto(false); return true; }
            if (IsCollectionOpen) { ShowCollection(false); return true; }
            if (IsMapOpen) { ShowMap(false); return true; }
            if (IsCompleteOpen) { ShowComplete(false, ""); return true; }
            return false;
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------
        public void RefreshCounts(bool instant = false)
        {
            if (_collect == null) return;
            int d = _collect.CollectedDrops;
            int p = _collect.DiscoveredPlaces;
            int c = Creatures.DiscoveredCount;
            _dropCount.text = $"{d} / {_collect.TotalDrops}";
            _placeCount.text = $"{p} / {_collect.TotalPlaces}";
            int rares = Creatures.RareDiscoveredCount;
            _creatureCount.text = rares > 0 ? $"{c} / {SpeciesCatalog.Count} ★{rares}" : $"{c} / {SpeciesCatalog.Count}";
            var area = MapArea;
            string title = SaveSystem.Data.completedAreas.Contains(area.Id) ? "★ " + area.Subtitle : area.Subtitle;
            if (_areaTitle.text != title) _areaTitle.text = title;
            if (!instant)
            {
                if (d != _lastDrops && _lastDrops >= 0) Pop(_dropCount);
                if (p != _lastPlaces && _lastPlaces >= 0) Pop(_placeCount);
                if (c != _lastCreatures && _lastCreatures >= 0) Pop(_creatureCount);
            }
            _lastDrops = d;
            _lastPlaces = p;
            _lastCreatures = c;
        }

        void Pop(VisualElement e)
        {
            e.AddToClassList("obj-count--pop");
            e.schedule.Execute(() => e.RemoveFromClassList("obj-count--pop")).StartingIn(260);
        }

        public void ShowBanner(LandmarkDef lm)
        {
            ShowBannerText("新たな場所を発見", lm.name, lm.description);
            RefreshMarkers();
            RefreshCounts();
        }

        /// <summary>エリアに入ったときの大きな見出し。</summary>
        public void ShowAreaBanner(AreaLayout area)
        {
            // 前にも来たことがあれば、集めた数も出す
            string desc = area.Tagline;
            int drops = Collectibles.CollectedIn(area), places = Collectibles.DiscoveredIn(area);
            if (drops + places > 0)
                desc += SaveSystem.Data.completedAreas.Contains(area.Id)
                    ? "\n★ このエリアはめぐり終えました"
                    : $"\n{area.DropName} {drops} / {area.DropCount}　名所 {places} / {area.Landmarks.Count}";
            _bannerQueue.Clear();   // 前のエリアの見出しは出さない
            ShowBannerText(area.DisplayName, area.Subtitle, desc);
        }

        void ShowBannerText(string sub, string title, string desc)
        {
            // 新しい見出しはすぐ出す。いまの見出しが出たばかりなら、あとでもう一度出す
            // （エリアに着いた見出しと名所の発見が重なっても、両方読めるように）
            if (IsBannerVisible && _bannerTitle.text != title && Time.unscaledTime - _bannerShownAt < 1.5f && _bannerQueue.Count < 3)
                _bannerQueue.Enqueue((_bannerSub.text, _bannerTitle.text, _bannerDesc.text));
            _bannerShownAt = Time.unscaledTime;
            _bannerSub.text = sub;
            _bannerTitle.text = title;
            _bannerDesc.text = desc;
            _banner.AddToClassList("banner--show");
            _bannerUntil = Time.unscaledTime + 5f;
        }

        public void Toast(string text) => Toast(text, "icon-drop");

        public void Toast(string text, string iconClass) => Toast(text, iconClass, 2.6f);

        /// <summary>一度だけ出すヒント（保存して、二度目は出さない）。出したら true。</summary>
        public bool Tip(string id, string text)
        {
            var shown = SaveSystem.Data.tipsShown;
            if (shown.Contains(id) || !EnsureBound()) return false;
            shown.Add(id);
            Toast(text, "icon-book", 5.5f);
            _toasts[_toasts.childCount - 1].AddToClassList("toast--tip");
            return true;
        }

        public void Toast(string text, string iconClass, float seconds)
        {
            var t = new VisualElement();
            t.AddToClassList("toast");
            t.pickingMode = PickingMode.Ignore;
            var icon = new VisualElement();
            icon.AddToClassList("icon");
            icon.AddToClassList(iconClass);
            icon.pickingMode = PickingMode.Ignore;
            var l = new Label(text);
            l.AddToClassList("toast-label");
            l.pickingMode = PickingMode.Ignore;
            t.Add(icon);
            t.Add(l);
            _toasts.Add(t);
            t.schedule.Execute(() => t.AddToClassList("toast--show")).StartingIn(20);
            long ms = (long)(seconds * 1000f);
            t.schedule.Execute(() => t.RemoveFromClassList("toast--show")).StartingIn(ms);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(ms + 500);
            while (_toasts.childCount > 4) _toasts.RemoveAt(0);
        }

        /// <summary>いきものを見つけたときのカード。</summary>
        public void ShowCreature(SpeciesDef sp)
        {
            if (!EnsureBound() || sp == null) return;
            var tex = _assets != null ? _assets.Portrait(sp.id) : null;
            _creatureCardImg.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            // つづけて見つけたときは、カードを最新のいきものにして「ほかに n 種」と書く
            bool burst = IsCreatureCardVisible && Time.unscaledTime - _cardShownAt < 2.5f && _cardSpecies != sp.id;
            _cardExtra = burst ? _cardExtra + 1 : 0;
            _cardShownAt = Time.unscaledTime;
            _creatureCardName.text = sp.name;
            _creatureCardDesc.text = sp.description;
            _cardSpecies = sp.id;
            string sub = sp.IsRare ? "★ レアないきものを見つけた！" : "いきものを見つけた！";
            if (_cardExtra > 0) sub += $"（ほかに {_cardExtra} 種）";
            _creatureCardSub.text = sub;
            _creatureCard.EnableInClassList("creature-card--rare", sp.IsRare);
            _creatureCard.pickingMode = PickingMode.Position;
            _creatureCard.AddToClassList("creature-card--show");
            _creatureUntil = Time.unscaledTime + 5f;
            RefreshCounts();
        }

        void SetPrompt(string key, string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            _prompt.EnableInClassList("prompt--show", show);
            if (!show) return;
            _promptKey.text = key;
            _promptKey.style.display = string.IsNullOrEmpty(key) ? DisplayStyle.None : DisplayStyle.Flex;
            _promptText.text = text;
        }

        string KeyName(string action)
        {
            bool pad = GameInput.UsingGamepad;
            if (_touchMode && !pad)
            {
                switch (action)
                {
                    case "silk": return "糸";
                    case "sprint": return "はやく";
                    case "stand": return "背伸び";
                    case "aim": return "ねらう";
                }
            }
            switch (action)
            {
                case "silk": return pad ? "A" : "Space";
                case "sprint": return pad ? "RT" : "Shift";
                case "stand": return pad ? "Y" : "E";
                case "aim": return pad ? "LT" : "F（右クリック）";
            }
            return "";
        }

        void SetHelpVisible(bool v)
        {
            _helpVisible = v;
            _help.EnableInClassList("help--hidden", !v);
            _helpHint.style.display = v || _touchMode ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void ToggleHelp()
        {
            SetHelpVisible(!_helpVisible);
            SaveSystem.Settings.showHelp = _helpVisible;
            _helpToggle?.SetValueWithoutNotify(_helpVisible);
            SaveSystem.SaveSettingsSoon();
            _helpTimer = -999f;
        }

        string Device => GameInput.UsingGamepad ? "pad" : (_touchMode ? "touch" : "kb");

        void BuildHelp()
        {
            string device = Device;
            if (device == _lastHelpDevice) return;
            _lastHelpDevice = device;
            _help.Clear();
            (string[] keys, string text)[] rows;
            if (device == "pad")
                rows = new[]
                {
                    (new[] { "L スティック" }, "すすむ"),
                    (new[] { "R スティック" }, "見まわす"),
                    (new[] { "RT" }, "はやく"),
                    (new[] { "Y" }, "背伸び（長押し）"),
                    (new[] { "A" }, "糸でおりる / 壁ではなれる"),
                    (new[] { "LT" }, "糸をねらって発射"),
                    (new[] { "X" }, "図鑑"),
                    (new[] { "View" }, "地図"),
                    (new[] { "Menu" }, "メニュー"),
                };
            else if (device == "touch")
                rows = new[]
                {
                    (new[] { "左側をドラッグ" }, "すすむ"),
                    (new[] { "右側をドラッグ" }, "見まわす"),
                    (new[] { "2本指" }, "ズーム"),
                    (new[] { "ねらう" }, "おして向けて、はなすと糸を発射"),
                };
            else
                rows = new[]
                {
                    (new[] { "W", "A", "S", "D" }, "すすむ"),
                    (new[] { "ドラッグ" }, "見まわす"),
                    (new[] { "ホイール" }, "ズーム"),
                    (new[] { "Shift" }, "はやく"),
                    (new[] { "E" }, "背伸び（長押し）"),
                    (new[] { "Space" }, "糸でおりる / 壁ではなれる"),
                    (new[] { "F" }, "糸をねらって発射（右クリックでも）"),
                    (new[] { "X" }, "カメラを後ろへ"),
                    (new[] { "B" }, "図鑑"),
                    (new[] { "M" }, "地図"),
                    (new[] { "Esc" }, "メニュー"),
                };
            foreach (var r in rows)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("help-row");
                var keys = new VisualElement { pickingMode = PickingMode.Ignore };
                keys.AddToClassList("help-key");
                foreach (var k in r.keys)
                {
                    var kc = new Label(k) { pickingMode = PickingMode.Ignore };
                    kc.AddToClassList("keycap");
                    keys.Add(kc);
                }
                var tx = new Label(r.text) { pickingMode = PickingMode.Ignore };
                tx.AddToClassList("help-text");
                row.Add(keys);
                row.Add(tx);
                _help.Add(row);
            }
        }

        void BuildHowtoGrid()
        {
            string device = Device;
            if (device == _howtoDevice) return;
            _howtoDevice = device;
            var grid = Q<VisualElement>("howto-grid");
            grid.Clear();
            (string key, string text)[] items;
            if (device == "touch")
                items = new[]
                {
                    ("画面の左側をドラッグ", "すすむ（指の下にスティック）"),
                    ("画面の右側をドラッグ", "見まわす"),
                    ("2本指で広げる", "ズーム"),
                    ("右側をすばやく2回タップ", "カメラを後ろへもどす"),
                    ("はやく", "はやく這う（切り替え）"),
                    ("背伸び", "背伸びして見わたす"),
                    ("糸", "糸でぶら下がる / 長押しでのぼる / 壁ではなれる"),
                    ("ねらう", "画面のまん中に糸を発射してたぐる"),
                    ("地図ボタン", "地図・エリア移動"),
                    ("本のボタン", "いきもの図鑑・きせかえ"),
                };
            else if (device == "pad")
                items = new[]
                {
                    ("L スティック", "すすむ（カメラの向き基準）"),
                    ("R スティック", "見まわす"),
                    ("LB / RB", "ズーム"),
                    ("RT", "はやく這う"),
                    ("Y（長押し）", "背伸びして見わたす"),
                    ("A", "糸を出してぶら下がる"),
                    ("A（長押し）", "ぶら下がり中に糸をのぼる"),
                    ("LT", "糸をねらって発射・たぐり寄せる"),
                    ("R スティック押しこみ", "カメラを後ろへもどす"),
                    ("X", "いきもの図鑑・きせかえ"),
                    ("View", "地図・エリア移動"),
                    ("Menu", "メニュー（設定）"),
                };
            else
                items = new[]
                {
                    ("W A S D / 矢印", "すすむ（カメラの向き基準）"),
                    ("マウスでドラッグ", "見まわす"),
                    ("ホイール", "ズーム"),
                    ("Shift", "はやく這う"),
                    ("E（長押し）", "背伸びして見わたす"),
                    ("Space", "糸を出してぶら下がる"),
                    ("Space（長押し）", "ぶら下がり中に糸をのぼる"),
                    ("Space（壁で）", "はなれて落ちる（落ちる途中でもう一度で糸）"),
                    ("F / 右クリック", "糸をねらって発射・たぐり寄せる"),
                    ("X / 中ボタン", "カメラを後ろへもどす"),
                    ("B", "いきもの図鑑・きせかえ"),
                    ("M", "地図・エリア移動"),
                    ("H", "操作説明の表示切り替え"),
                    ("F2", "写真モード（表示を消して景色を見る）"),
                    ("Esc", "メニュー（設定）"),
                };
            foreach (var it in items)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("howto-item");
                var k = new Label(it.key) { pickingMode = PickingMode.Ignore };
                k.AddToClassList("keycap");
                var t = new Label(it.text) { pickingMode = PickingMode.Ignore };
                t.AddToClassList("howto-item-text");
                row.Add(k);
                row.Add(t);
                grid.Add(row);
            }
        }

        // ------------------------------------------------------------------
        // コレクション（図鑑・きせかえ・きろく）
        // ------------------------------------------------------------------
        public void ShowCollection(bool show, string tab = null)
        {
            if (show)
            {
                SelectTab(tab ?? _tab);
                _tabs[_tab].Focus();
                // メニューから開いたときは、メニューの前面に出す
                if (_collectionOverlay.parent == _pauseOverlay.parent
                    && _collectionOverlay.parent.IndexOf(_collectionOverlay) < _collectionOverlay.parent.IndexOf(_pauseOverlay))
                    _collectionOverlay.PlaceInFront(_pauseOverlay);
            }
            _collectionOverlay.EnableInClassList("hidden", !show);
            OverlayChanged?.Invoke();
        }

        public void ToggleCollection() => ShowCollection(!IsCollectionOpen);

        public void SelectTab(string tab)
        {
            if (!_pages.ContainsKey(tab)) tab = "zukan";
            _tab = tab;
            foreach (var kv in _tabs) kv.Value.EnableInClassList("tab--on", kv.Key == tab);
            foreach (var kv in _pages) kv.Value.EnableInClassList("hidden", kv.Key != tab);
            if (tab == "zukan") BuildZukan();
            else if (tab == "skin") BuildSkins();
            else BuildRecords();
        }

        Texture2D Portrait(string id) => _assets != null ? _assets.Portrait(id) : null;

        void BuildZukan()
        {
            _zukanGrid.Clear();
            _zukanCards.Clear();
            int found = Creatures.DiscoveredCount;
            // すみかでしぼりこむ
            var filters = new VisualElement();
            filters.AddToClassList("zukan-filters");
            foreach (var (label, key) in new[] { ("すべて", ""), ("森", "森"), ("川辺", "川辺") })
            {
                var chip = new Button { text = label };
                chip.AddToClassList("chip");
                chip.AddToClassList("zukan-filter");
                chip.EnableInClassList("chip--on", _zukanFilter == key);
                string k = key;
                chip.clicked += () =>
                {
                    AudioManager.Instance?.Click();
                    SetZukanFilter(k);
                };
                filters.Add(chip);
            }
            _zukanGrid.Add(filters);
            void AddCard(SpeciesDef sp)
            {
                if (!string.IsNullOrEmpty(_zukanFilter) && (sp.areaLabel == null || !sp.areaLabel.Contains(_zukanFilter))) return;
                bool known = Creatures.IsDiscovered(sp.id);
                var card = new Button();
                card.AddToClassList("zukan-card");
                card.EnableInClassList("zukan-card--rare", sp.IsRare);
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.AddToClassList("zukan-card-img");
                var tex = Portrait(sp.id);
                if (tex != null) img.style.backgroundImage = new StyleBackground(tex);
                img.EnableInClassList("zukan-card-img--unknown", !known);
                var name = new Label(known ? sp.name : "？？？") { pickingMode = PickingMode.Ignore };
                name.AddToClassList("zukan-card-name");
                card.Add(img);
                card.Add(name);
                if (known && !SaveSystem.Data.seenCreatures.Contains(sp.id))
                {
                    var nw = new Label("NEW") { pickingMode = PickingMode.Ignore };
                    nw.AddToClassList("zukan-new");
                    card.Add(nw);
                }
                string id = sp.id;
                card.clicked += () =>
                {
                    AudioManager.Instance?.Click();
                    SelectSpecies(id);
                };
                _zukanGrid.Add(card);
                _zukanCards[sp.id] = card;
            }
            foreach (var sp in SpeciesCatalog.Regular) AddCard(sp);
            var head = new Label($"めったに会えない いきもの（{Creatures.RareDiscoveredCount} / {SpeciesCatalog.RareCount}）") { pickingMode = PickingMode.Ignore };
            head.AddToClassList("zukan-section");
            _zukanGrid.Add(head);
            foreach (var sp in SpeciesCatalog.Rares) AddCard(sp);
            _zukanCount.text = $"見つけたいきもの {found} / {SpeciesCatalog.Count}　　近づくと図鑑に登録されます";
            if (_selectedSpecies == null || SpeciesCatalog.Get(_selectedSpecies) == null || !_zukanCards.ContainsKey(_selectedSpecies))
            {
                var first = SpeciesCatalog.All.FirstOrDefault(s => _zukanCards.ContainsKey(s.id) && Creatures.IsDiscovered(s.id))
                            ?? SpeciesCatalog.All.FirstOrDefault(s => _zukanCards.ContainsKey(s.id)) ?? SpeciesCatalog.All[0];
                _selectedSpecies = first.id;
            }
            SelectSpecies(_selectedSpecies);
        }

        /// <summary>図鑑をすみかでしぼりこむ（"" = すべて）。</summary>
        public void SetZukanFilter(string key)
        {
            _zukanFilter = key ?? "";
            BuildZukan();
        }

        /// <summary>図鑑のすみかの地図に出ている印の数（テスト用）。</summary>
        public int HabitatDotCount => _zukanHabitat != null ? _zukanHabitat.Query(className: "zukan-habitat-dot").ToList().Count : 0;
        public string ZukanHabitatText => _zukanArea != null ? _zukanArea.text : "";

        /// <summary>いまいるエリアの地図に、すみかの場所の印をつける（このエリアにいないいきものは、地図を出さない）。</summary>
        void ShowHabitatMap(string id, bool known)
        {
            if (_zukanHabitat == null) return;
            _zukanHabitat.Clear();
            var area = _collect != null ? _collect.Area : Areas.Current;
            var spots = known ? Habitats.Of(id).FindAll(h => h.area == area.Id) : new System.Collections.Generic.List<HabitatSpot>();
            bool show = spots.Count > 0 && _mapTexture != null;
            _zukanHabitat.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _zukanHabitat.style.backgroundImage = new StyleBackground(_mapTexture);
            const float size = 150f;
            float w = _zukanHabitat.resolvedStyle.width > 1f && !float.IsNaN(_zukanHabitat.resolvedStyle.width) ? _zukanHabitat.resolvedStyle.width : size;
            foreach (var h in spots)
            {
                Vector2 uv = WorldToMap(new Vector3(h.position.x, 0f, h.position.y));
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("zukan-habitat-dot");
                dot.style.left = new Length(uv.x * 100f, LengthUnit.Percent);
                dot.style.top = new Length(uv.y * 100f, LengthUnit.Percent);
                dot.style.marginLeft = -7f;
                dot.style.marginTop = -7f;
                _zukanHabitat.Add(dot);
            }
            // いまいる場所
            if (_worm != null)
            {
                Vector2 me = WorldToMap(_worm.CenterPosition);
                var here = new VisualElement { pickingMode = PickingMode.Ignore };
                here.AddToClassList("zukan-habitat-here");
                here.style.left = new Length(me.x * 100f, LengthUnit.Percent);
                here.style.top = new Length(me.y * 100f, LengthUnit.Percent);
                here.style.marginLeft = -5f;
                here.style.marginTop = -5f;
                _zukanHabitat.Add(here);
            }
        }

        public bool HasNewBadge(string id) => _zukanCards.TryGetValue(id, out var c) && c.Q<Label>(className: "zukan-new") != null;

        public void SelectSpecies(string id)
        {
            var sp = SpeciesCatalog.Get(id);
            if (sp == null) return;
            _selectedSpecies = id;
            foreach (var kv in _zukanCards) kv.Value.EnableInClassList("zukan-card--selected", kv.Key == id);
            bool known = Creatures.IsDiscovered(id);
            var tex = Portrait(id);
            _zukanImg.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            _zukanImg.EnableInClassList("zukan-card-img--unknown", !known);
            _zukanName.text = known ? sp.name : "？？？";
            if (known && !SaveSystem.Data.seenCreatures.Contains(id))
            {
                // 図鑑で見たら NEW を消す
                SaveSystem.Data.seenCreatures.Add(id);
                if (_zukanCards.TryGetValue(id, out var card)) card.Q<Label>(className: "zukan-new")?.RemoveFromHierarchy();
            }
            // すみか：見つけたいきものは、どの名所のまわりにいるかと、地図の上の場所を見せる
            _zukanArea.text = "すみか：" + (known ? Habitats.Describe(id) : sp.areaLabel);
            _zukanDesc.text = known ? sp.description : "ヒント：" + sp.hint;
            ShowHabitatMap(id, known);
        }

        void BuildSkins()
        {
            _skinGrid.Clear();
            var st = ProgressStats.FromSave();
            var current = Skins.Get(SaveSystem.Data.skin);
            foreach (var skin in Skins.All)
            {
                bool open = Skins.IsUnlocked(skin, st);
                var card = new Button();
                card.AddToClassList("skin-card");
                card.EnableInClassList("skin-card--on", current != null && current.id == skin.id);
                card.EnableInClassList("skin-card--locked", !open);
                var sw = new VisualElement { pickingMode = PickingMode.Ignore };
                sw.AddToClassList("skin-swatch");
                sw.style.backgroundColor = open ? skin.swatch : new Color(0.62f, 0.58f, 0.52f);
                if (!open)
                {
                    var lockIcon = new VisualElement { pickingMode = PickingMode.Ignore };
                    lockIcon.AddToClassList("icon");
                    lockIcon.AddToClassList("icon-lock");
                    lockIcon.style.alignSelf = Align.Center;
                    lockIcon.style.marginTop = 18;
                    sw.Add(lockIcon);
                }
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("skin-text");
                var nm = new Label(open ? skin.name : "？？？") { pickingMode = PickingMode.Ignore };
                nm.AddToClassList("skin-name");
                var cond = new Label(open ? (current != null && current.id == skin.id ? "きがえ中" : "タップしてきがえる") : skin.condition)
                    { pickingMode = PickingMode.Ignore };
                cond.AddToClassList("skin-cond");
                text.Add(nm);
                text.Add(cond);
                card.Add(sw);
                card.Add(text);
                string id = skin.id;
                card.clicked += () =>
                {
                    if (!Skins.IsUnlocked(Skins.Get(id), ProgressStats.FromSave()))
                    {
                        AudioManager.Instance?.Click();
                        return;
                    }
                    SelectSkin(id);
                };
                _skinGrid.Add(card);
            }
        }

        /// <summary>きせかえを選ぶ（解放済みのときだけ）。</summary>
        public bool SelectSkin(string id)
        {
            var skin = Skins.Get(id);
            if (skin == null || !Skins.IsUnlocked(skin, ProgressStats.FromSave())) return false;
            AudioManager.Instance?.Unlock();
            SaveSystem.Data.skin = id;
            SaveSystem.Save();
            SkinSelected?.Invoke(id);
            if (_tab == "skin" && IsCollectionOpen) BuildSkins();
            return true;
        }

        void BuildRecords()
        {
            _recordList.Clear();
            var d = SaveSystem.Data;
            int t = Mathf.FloorToInt(d.playTime);
            AddRecord("全体の達成度", $"{CompletionPercent()}%");
            AddRecord("あそんだ時間", PlayTimeText(d.playTime));
            foreach (var area in Areas.All)
            {
                int got = Collectibles.CollectedIn(area) + Collectibles.DiscoveredIn(area);
                int all = area.DropCount + area.Landmarks.Count;
                AddRecord(area.DisplayName + "の達成度", $"{(all > 0 ? 100 * got / all : 0)}%" + (d.completedAreas.Contains(area.Id) ? " ★" : ""));
                AddRecord(area.DropName, $"{Collectibles.CollectedIn(area)} / {area.DropCount}");
                AddRecord(area.DisplayName + "の名所", $"{Collectibles.DiscoveredIn(area)} / {area.Landmarks.Count}");
            }
            AddRecord("見つけたいきもの", $"{Creatures.DiscoveredCount} / {SpeciesCatalog.Count}");
            AddRecord("レアないきもの", $"{Creatures.RareDiscoveredCount} / {SpeciesCatalog.RareCount}");
            AddRecord("歩いた歩数", $"{d.steps:N0} 歩");
            AddRecord("糸を使った回数", $"{d.silkUses} 回");
            AddRecord("落ちた回数", $"{d.falls} 回");
            AddRecord("いちばん高く登った", d.highest > 0f ? $"{d.highest:F1} 体長（約 {d.highest * 2.5f:F0} cm）" : "まだ");
            var st = ProgressStats.FromSave();
            AddRecord("きせかえ", $"{Skins.All.Count(s => Skins.IsUnlocked(s, st))} / {Skins.All.Count}");
            string done = string.Join("・", Areas.All.Where(a => d.completedAreas.Contains(a.Id)).Select(a => a.DisplayName));
            AddRecord("めぐり終えたエリア", string.IsNullOrEmpty(done) ? "まだ" : done);
        }

        void AddRecord(string label, string value)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("record-row");
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("record-label");
            var v = new Label(value) { pickingMode = PickingMode.Ignore };
            v.AddToClassList("record-value");
            row.Add(l);
            row.Add(v);
            _recordList.Add(row);
        }

        public int RecordRowCount => _recordList != null ? _recordList.childCount : 0;

        // ------------------------------------------------------------------
        // マップ
        // ------------------------------------------------------------------
        AreaLayout MapArea => _collect != null ? _collect.Area : Areas.Current;

        void BuildMarkers()
        {
            foreach (var m in _miniMarkers.Values) m.RemoveFromHierarchy();
            _miniMarkers.Clear();
            foreach (var g in _miniGates) g.RemoveFromHierarchy();
            _miniGates.Clear();
            foreach (var lm in MapArea.Landmarks)
            {
                var mk = new VisualElement { pickingMode = PickingMode.Ignore };
                mk.AddToClassList("map-marker");
                _minimapImage.Add(mk);
                _miniMarkers[lm.id] = mk;
            }
            foreach (var g in MapArea.Gates)
            {
                var mk = new VisualElement { pickingMode = PickingMode.Ignore };
                mk.AddToClassList("map-marker");
                mk.AddToClassList("map-marker--gate");
                Vector2 uv = WorldToMap(new Vector3(g.position.x, 0f, g.position.y));
                mk.style.left = uv.x * 440f - 13f;
                mk.style.top = uv.y * 440f - 13f;
                _minimapImage.Add(mk);
                _miniGates.Add(mk);
            }
            if (_miniDots.Count == 0)
            {
                for (int i = 0; i < 12; i++)
                {
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                    dot.AddToClassList("map-dot");
                    dot.style.display = DisplayStyle.None;
                    _minimapImage.Add(dot);
                    _miniDots.Add(dot);
                }
            }
            if (_miniCreatures.Count == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    var q = new Label("？") { pickingMode = PickingMode.Ignore };
                    q.AddToClassList("map-creature");
                    q.style.display = DisplayStyle.None;
                    _minimapImage.Add(q);
                    _miniCreatures.Add(q);
                }
            }
            foreach (var dot in _miniDots) dot.BringToFront();
            foreach (var q in _miniCreatures) q.BringToFront();
            RefreshMarkers();
        }

        void RefreshMarkers()
        {
            foreach (var lm in MapArea.Landmarks)
            {
                if (!_miniMarkers.TryGetValue(lm.id, out var mk)) continue;
                bool known = _collect != null && _collect.IsDiscovered(lm.id);
                mk.EnableInClassList("map-marker--place", known);
                mk.EnableInClassList("map-marker--unknown", !known);
                Vector2 uv = WorldToMap(new Vector3(lm.position.x, 0f, lm.position.y));
                mk.style.left = uv.x * 440f - 13f;
                mk.style.top = uv.y * 440f - 13f;
            }
        }

        static Vector2 WorldToMap(Vector3 p)
        {
            float e = WorldGenerator.MapExtent;
            return new Vector2((p.x + e) / (2f * e), 1f - (p.z + e) / (2f * e));
        }

        static float Size(VisualElement e, float fallback)
        {
            float w = e.contentRect.width;
            return float.IsNaN(w) || w < 1f ? fallback : w;
        }

        void UpdateMinimap()
        {
            if (_worm == null) return;
            Vector3 p = _worm.CenterPosition;
            Vector2 uv = WorldToMap(p);
            float inner = Size(_minimapMask, 212f);
            _minimapImage.style.left = inner * 0.5f - uv.x * 440f;
            _minimapImage.style.top = inner * 0.5f - uv.y * 440f;
            Vector3 h = _worm.Heading;
            float yaw = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
            // 設定で「ミニマップを回転」なら、カメラの向きが上になるように回す
            float camYaw = 0f;
            if (SaveSystem.Settings.minimapRotate && _cam != null)
            {
                Vector3 cf = _cam.forward;
                camYaw = Mathf.Atan2(cf.x, cf.z) * Mathf.Rad2Deg;
            }
            _minimapMask.style.rotate = new Rotate(new Angle(-camYaw, AngleUnit.Degree));
            _minimapPlayer.style.rotate = new Rotate(new Angle(yaw - camYaw, AngleUnit.Degree));
            if (_minimapNorth != null)
            {
                float a = -camYaw * Mathf.Deg2Rad;
                float half = inner * 0.5f;
                _minimapNorth.style.left = half + Mathf.Sin(a) * (half - 12f) - 6f;
                _minimapNorth.style.top = half - Mathf.Cos(a) * (half - 12f) - 12f;
            }

            // 近くにある未回収のしずくを点で表示
            int i = 0;
            if (_collect != null)
            {
                foreach (var d in _collect.RemainingDrops())
                {
                    if (i >= _miniDots.Count) break;
                    if ((new Vector2(d.x - p.x, d.z - p.z)).sqrMagnitude > 14f * 14f) continue;
                    Vector2 du = WorldToMap(d);
                    var dot = _miniDots[i++];
                    dot.style.display = DisplayStyle.Flex;
                    dot.style.left = du.x * 440f - 4.5f;
                    dot.style.top = du.y * 440f - 4.5f;
                }
            }
            for (; i < _miniDots.Count; i++) _miniDots[i].style.display = DisplayStyle.None;
            // 近くにいる、まだ図鑑にのっていないいきものは「？」で知らせる
            int nc = CreatureSource != null ? CreatureSource.UndiscoveredNear(p, 12f, _nearCreatures, _miniCreatures.Count) : 0;
            for (int k = 0; k < _miniCreatures.Count; k++)
            {
                var q = _miniCreatures[k];
                if (k >= nc)
                {
                    if (q.style.display != DisplayStyle.None) q.style.display = DisplayStyle.None;
                    continue;
                }
                Vector2 cu = WorldToMap(_nearCreatures[k]);
                q.style.display = DisplayStyle.Flex;
                q.style.left = cu.x * 440f - 10f;
                q.style.top = cu.y * 440f - 10f;
                q.style.rotate = new Rotate(new Angle(camYaw, AngleUnit.Degree));
            }
            // 遠くの未発見の名所は表示しない
            foreach (var lm in MapArea.Landmarks)
            {
                if (!_miniMarkers.TryGetValue(lm.id, out var mk)) continue;
                bool known = _collect != null && _collect.IsDiscovered(lm.id);
                float dist = Vector2.Distance(new Vector2(p.x, p.z), lm.position);
                mk.style.display = known || dist < 32f ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void AddBigItem(VisualElement el, Vector2 uv, Vector2 offset)
        {
            _bigmap.Add(el);
            _bigItems.Add((el, uv, offset));
        }

        void RefreshBigMap()
        {
            foreach (var it in _bigItems) it.el.RemoveFromHierarchy();
            _bigItems.Clear();
            var area = MapArea;
            _mapTitle.text = area.DisplayName + "の地図";
            foreach (var lm in area.Landmarks)
            {
                bool known = _collect != null && _collect.IsDiscovered(lm.id);
                Vector2 uv = WorldToMap(new Vector3(lm.position.x, 0f, lm.position.y));
                var mk = new VisualElement { pickingMode = PickingMode.Ignore };
                mk.AddToClassList("map-marker");
                mk.AddToClassList(known ? "map-marker--place" : "map-marker--unknown");
                mk.style.width = 34;
                mk.style.height = 34;
                AddBigItem(mk, uv, new Vector2(-17f, -17f));
                var lbl = new Label(known ? lm.name : "？？？") { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("bigmap-label");
                AddBigItem(lbl, uv, new Vector2(-100f, 15f));
            }
            foreach (var g in area.Gates)
            {
                Vector2 uv = WorldToMap(new Vector3(g.position.x, 0f, g.position.y));
                var mk = new VisualElement { pickingMode = PickingMode.Ignore };
                mk.AddToClassList("map-marker");
                mk.AddToClassList("map-marker--gate");
                mk.style.width = 38;
                mk.style.height = 38;
                AddBigItem(mk, uv, new Vector2(-19f, -19f));
                var lbl = new Label(g.label) { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("bigmap-label");
                AddBigItem(lbl, uv, new Vector2(-100f, 17f));
            }
            // のこりが少ないときは、だいたいの場所を丸で教える（ぴったりではなく、少しずらす）
            if (_collect != null)
            {
                var left = _collect.RemainingDrops().ToList();
                if (left.Count > 0 && left.Count <= 5)
                {
                    for (int k = 0; k < left.Count; k++)
                    {
                        var hint = new VisualElement { pickingMode = PickingMode.Ignore };
                        hint.AddToClassList("map-hint");
                        hint.style.width = 64;
                        hint.style.height = 64;
                        float a = (left[k].x * 12.9898f + left[k].z * 78.233f) % 6.2832f;
                        Vector2 jitter = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 14f;
                        AddBigItem(hint, WorldToMap(left[k]), new Vector2(-32f, -32f) + jitter);
                    }
                }
            }
            if (_bigmapHere == null)
            {
                _bigmapHere = new Label("いまここ") { pickingMode = PickingMode.Ignore };
                _bigmapHere.AddToClassList("legend-here");
                _bigmapHere.style.position = Position.Absolute;
                _bigmap.Add(_bigmapHere);
            }
            _bigmapHere.BringToFront();
            _bigmapPlayer.BringToFront();
            LayoutBigMap();

            _legend.Clear();
            foreach (var lm in area.Landmarks)
            {
                bool known = _collect != null && _collect.IsDiscovered(lm.id);
                var row = new VisualElement();
                row.AddToClassList("legend-row");
                var icon = new VisualElement();
                icon.AddToClassList("icon");
                icon.AddToClassList(known ? "icon-place" : "map-marker--unknown");
                var col = new VisualElement { style = { flexShrink = 1, flexGrow = 1 } };
                var name = new Label(known ? lm.name : "まだ見つけていない場所");
                name.AddToClassList("legend-name");
                if (!known) name.AddToClassList("legend-name--unknown");
                col.Add(name);
                if (known)
                {
                    var desc = new Label(lm.description);
                    desc.AddToClassList("legend-desc");
                    col.Add(desc);
                }
                row.Add(icon);
                row.Add(col);
                if (known)
                {
                    // 見つけた名所へは、地図からすぐ行ける
                    int lmId = lm.id;
                    var go = new Button { text = "ここへ" };
                    go.AddToClassList("legend-go");
                    go.clicked += () =>
                    {
                        AudioManager.Instance?.Click();
                        ShowMap(false);
                        FastTravelRequested?.Invoke(lmId);
                    };
                    row.Add(go);
                }
                _legend.Add(row);
            }
            if (_collect != null)
            {
                int rest = _collect.TotalDrops - _collect.CollectedDrops;
                _mapSummary.text = $"{area.DropName} {_collect.CollectedDrops} / {_collect.TotalDrops}　　名所 {_collect.DiscoveredPlaces} / {_collect.TotalPlaces}" +
                                   (rest > 0 && rest <= 5 ? $"\nのこり {rest} 個：青い丸のあたりをさがしてみよう" : "");
            }
            BuildAreaChips();
        }

        void LayoutBigMap()
        {
            float size = Size(_bigmap, 614f);
            foreach (var it in _bigItems)
            {
                it.el.style.left = it.uv.x * size + it.offset.x;
                it.el.style.top = it.uv.y * size + it.offset.y;
            }
            if (_worm != null)
            {
                Vector2 uv = WorldToMap(_worm.CenterPosition);
                _bigmapPlayer.style.left = uv.x * size - 18f;
                _bigmapPlayer.style.top = uv.y * size - 18f;
                Vector3 h = _worm.Heading;
                _bigmapPlayer.style.rotate = new Rotate(new Angle(Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg, AngleUnit.Degree));
                if (_bigmapHere != null)
                {
                    _bigmapHere.style.left = uv.x * size - 34f;
                    _bigmapHere.style.top = uv.y * size - 46f;
                }
            }
        }

        /// <summary>地図の下の「エリアへ移動」ボタン。一度行ったエリアへはすぐ移動できる。</summary>
        void BuildAreaChips()
        {
            _mapAreas.Clear();
            var here = MapArea;
            foreach (var area in Areas.All)
            {
                bool isHere = area == here;
                bool visited = isHere || SaveSystem.Data.visited.Contains(area.Id);
                var chip = new Button { text = visited ? area.DisplayName : "？？？" };
                chip.AddToClassList("area-chip");
                chip.EnableInClassList("area-chip--here", isHere);
                chip.EnableInClassList("area-chip--locked", !visited);
                if (isHere) chip.text = area.DisplayName + "（いまここ）";
                string id = area.Id;
                chip.clicked += () =>
                {
                    AudioManager.Instance?.Click();
                    RequestTravel(id);
                };
                _mapAreas.Add(chip);
            }
        }

        /// <summary>地図からエリア移動を頼む。まだ行ったことのないエリアにはトンネルを通って行く必要がある。</summary>
        public bool RequestTravel(string areaId)
        {
            var area = Areas.Get(areaId);
            if (area == null || area.Id != areaId || area == MapArea) return false;
            if (!SaveSystem.Data.visited.Contains(areaId))
            {
                Toast("まだ行ったことがない場所。木の根のトンネルを探そう", "icon-lock");
                return false;
            }
            ShowMap(false);
            TravelRequested?.Invoke(areaId);
            return true;
        }

        // ------------------------------------------------------------------
        // ドラッグで視点・タッチ操作
        // ------------------------------------------------------------------
        void SetupLookArea()
        {
            _look.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.pointerType == UnityEngine.UIElements.PointerType.touch) GameInput.MarkTouch();
                _look.CapturePointer(e.pointerId);
                _lookPointers[e.pointerId] = e.position;
                _tapStart[e.pointerId] = (e.position, Time.unscaledTime);
                AudioManager.Instance?.StartMusic();
            });
            _look.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_lookPointers.ContainsKey(e.pointerId)) return;
                Vector2 prev = _lookPointers[e.pointerId];
                if (_lookPointers.Count >= 2)
                {
                    // 2本指でピンチズーム
                    float before = PinchDistance();
                    _lookPointers[e.pointerId] = e.position;
                    float after = PinchDistance();
                    GameInput.AddZoom((after - before) * 0.01f);
                    return;
                }
                _lookPointers[e.pointerId] = e.position;
                Vector2 d = (Vector2)e.position - prev;
                GameInput.AddLook(new Vector2(d.x, -d.y));
            });
            EventCallback<PointerUpEvent> up = e =>
            {
                _lookPointers.Remove(e.pointerId);
                if (_look.HasPointerCapture(e.pointerId)) _look.ReleasePointer(e.pointerId);
                if (_tapStart.TryGetValue(e.pointerId, out var st))
                {
                    _tapStart.Remove(e.pointerId);
                    bool tap = Time.unscaledTime - st.time < 0.3f && Vector2.Distance(st.pos, e.position) < 14f;
                    if (tap) OnLookTap(e.position, e.pointerType == UnityEngine.UIElements.PointerType.touch);
                }
            };
            _look.RegisterCallback(up);
            _look.RegisterCallback<PointerCancelEvent>(e => _lookPointers.Remove(e.pointerId));
        }

        /// <summary>画面をタップしたとき：写真モードならもどる。タッチですばやく 2 回ならカメラを後ろへ。</summary>
        public void OnLookTap(Vector2 pos, bool touch)
        {
            if (_photoMode)
            {
                PhotoRequested?.Invoke();
                return;
            }
            if (!touch) return;
            if (Time.unscaledTime - _lastTapTime < 0.35f && Vector2.Distance(pos, _lastTapPos) < 60f)
            {
                GameInput.VirtualRecenter = true;
                _lastTapTime = -9f;
                return;
            }
            _lastTapTime = Time.unscaledTime;
            _lastTapPos = pos;
        }

        float PinchDistance()
        {
            if (_lookPointers.Count < 2) return 0f;
            var e = _lookPointers.Values.GetEnumerator();
            e.MoveNext();
            Vector2 a = e.Current;
            e.MoveNext();
            Vector2 b = e.Current;
            return Vector2.Distance(a, b);
        }

        void SetupTouch()
        {
            // 画面の左側に指を置くと、そこにスティックが出る
            _moveZone.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_joyPointer != -1) return;
                if (e.pointerType == UnityEngine.UIElements.PointerType.touch) GameInput.MarkTouch();
                _joyPointer = e.pointerId;
                _moveZone.CapturePointer(e.pointerId);
                BeginJoystick(_touch.WorldToLocal(e.position));
                AudioManager.Instance?.StartMusic();
            });
            _moveZone.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId == _joyPointer) DragJoystick(_touch.WorldToLocal(e.position));
            });
            _moveZone.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId == _joyPointer) EndJoystick();
            });
            _moveZone.RegisterCallback<PointerCancelEvent>(e =>
            {
                if (e.pointerId == _joyPointer) EndJoystick();
            });

            HoldButton("btn-silk", held => GameInput.SetVirtualSilk(held));
            HoldButton("btn-stand", held => GameInput.VirtualStand = held);
            HoldButton("btn-aim", held => GameInput.VirtualAim = held);
            var dash = Q<VisualElement>("btn-dash");
            dash.RegisterCallback<PointerDownEvent>(e =>
            {
                GameInput.VirtualSprint = !GameInput.VirtualSprint;
                dash.EnableInClassList("touch-button--on", GameInput.VirtualSprint);
            });
        }

        void HoldButton(string name, Action<bool> set)
        {
            var b = Q<VisualElement>(name);
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                b.CapturePointer(e.pointerId);
                b.AddToClassList("touch-button--on");
                set(true);
            });
            EventCallback<PointerUpEvent> up = e =>
            {
                if (b.HasPointerCapture(e.pointerId)) b.ReleasePointer(e.pointerId);
                b.RemoveFromClassList("touch-button--on");
                set(false);
            };
            b.RegisterCallback(up);
            b.RegisterCallback<PointerCancelEvent>(e =>
            {
                b.RemoveFromClassList("touch-button--on");
                set(false);
            });
        }

        /// <summary>スティックを指の位置（touch 要素の座標）に出す。</summary>
        public void BeginJoystick(Vector2 local)
        {
            if (_joyPointer == -1) _joyPointer = int.MaxValue;   // テストなどポインターなしで呼ばれたとき
            _joyCenter = local;
            _joystick.style.left = local.x - JoyRadius;
            _joystick.style.top = local.y - JoyRadius;
            _joystick.style.bottom = StyleKeyword.Auto;
            _joystick.AddToClassList("joystick--active");
            DragJoystick(local);
        }

        public void DragJoystick(Vector2 local)
        {
            Vector2 d = local - _joyCenter;
            // 指が遠くまで動いたら、スティックごと指についていく
            float follow = JoyMax * 1.5f;
            if (d.magnitude > follow)
            {
                _joyCenter += d.normalized * (d.magnitude - follow);
                _joystick.style.left = _joyCenter.x - JoyRadius;
                _joystick.style.top = _joyCenter.y - JoyRadius;
                d = local - _joyCenter;
            }
            if (d.magnitude > JoyMax) d = d.normalized * JoyMax;
            _knob.style.left = 70 + d.x;
            _knob.style.top = 70 + d.y;
            GameInput.VirtualMove = JoystickValue(new Vector2(d.x, -d.y) / JoyMax);
        }

        /// <summary>スティックのあそび：中心付近の小さなぶれでは動かない（そこから先はなめらかに 0〜1）。</summary>
        public static Vector2 JoystickValue(Vector2 raw)
        {
            const float dead = 0.12f;
            float m = raw.magnitude;
            if (m <= dead) return Vector2.zero;
            float k = Mathf.Clamp01((m - dead) / (1f - dead));
            return raw / m * k;
        }

        public void EndJoystick()
        {
            if (_joyPointer != -1 && _joyPointer != int.MaxValue && _moveZone.HasPointerCapture(_joyPointer))
                _moveZone.ReleasePointer(_joyPointer);
            _joyPointer = -1;
            GameInput.VirtualMove = Vector2.zero;
            _knob.style.left = 70;
            _knob.style.top = 70;
            _joystick.style.left = StyleKeyword.Null;
            _joystick.style.top = StyleKeyword.Null;
            _joystick.style.bottom = StyleKeyword.Null;
            _joystick.RemoveFromClassList("joystick--active");
        }

        // ------------------------------------------------------------------
        // 画面の大きさ・向きに合わせる
        // ------------------------------------------------------------------
        /// <summary>タッチ端末では UI を大きく、縦長・横長の画面ではレイアウトを詰める。</summary>
        void UpdateScale()
        {
            if (_panel == null || _baseResolution.x <= 0) return;
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            float k = 1f;
            if (_touchMode) k = aspect > 1.75f || aspect < 0.6f ? 0.6f : 0.84f;   // スマホ / タブレット
            if (SaveSystem.Settings.textSize != 0) k *= 0.86f;                     // 文字を大きく
            var res = new Vector2Int(Mathf.RoundToInt(_baseResolution.x * k), Mathf.RoundToInt(_baseResolution.y * k));
            if (_panel.referenceResolution != res) _panel.referenceResolution = res;
        }

        /// <summary>スマホのノッチや角の丸みにボタンが隠れないよう、HUD を内側に寄せる。</summary>
        void UpdateSafeArea(bool force = false)
        {
            _safeTimer -= Time.unscaledDeltaTime;
            if (!force && _safeTimer > 0f) return;
            _safeTimer = 1f;
            var vt = _root?.panel?.visualTree;
            if (vt == null) return;
            float w = vt.layout.width, h = vt.layout.height;
            if (float.IsNaN(w) || w < 1f || float.IsNaN(h) || h < 1f) return;
            Vector4 f;   // 左・上・右・下（画面に対する割合）
#if UNITY_WEBGL && !UNITY_EDITOR
            f = new Vector4(ShakuSafeInset(0), ShakuSafeInset(1), ShakuSafeInset(2), ShakuSafeInset(3));
#else
            Rect sa = Screen.safeArea;
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            f = new Vector4(sa.xMin / sw, (sh - sa.yMax) / sh, (sw - sa.xMax) / sw, sa.yMin / sh);
#endif
            var inset = new Vector4(Mathf.Clamp(f.x, 0f, 0.2f) * w, Mathf.Clamp(f.y, 0f, 0.2f) * h,
                                    Mathf.Clamp(f.z, 0f, 0.2f) * w, Mathf.Clamp(f.w, 0f, 0.2f) * h);
            if ((inset - _safeInsets).sqrMagnitude < 0.25f) return;
            _safeInsets = inset;
            _hud.style.left = inset.x;
            _hud.style.top = inset.y;
            _hud.style.right = inset.z;
            _hud.style.bottom = inset.w;
        }

        /// <summary>いまの UI の基準解像度（テスト用）。</summary>
        public Vector2Int ReferenceResolution => _panel != null ? _panel.referenceResolution : Vector2Int.zero;

        void UpdateLayoutClasses()
        {
            var vt = _root?.panel?.visualTree;
            if (vt == null) return;
            float w = vt.layout.width, h = vt.layout.height;
            if (float.IsNaN(w) || w < 1f || float.IsNaN(h) || h < 1f) return;
            bool portrait = h > w * 1.05f;
            bool shortScreen = !portrait && h < 760f;
            _root.EnableInClassList("portrait", portrait);
            _root.EnableInClassList("short", shortScreen);
            _root.EnableInClassList("compact", portrait || shortScreen);
            _root.EnableInClassList("touch-ui", _touchMode);
        }

        public bool IsPortrait => _root != null && _root.ClassListContains("portrait");
        public bool IsShort => _root != null && _root.ClassListContains("short");
        public bool IsCompact => _root != null && _root.ClassListContains("compact");
        public bool IsRotateHintVisible => _rotateHint != null && !_rotateHint.ClassListContains("hidden");

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------
        /// <summary>進めないときに、なぜ進めないかを短く伝える。</summary>
        public static string BlockedMessage(string reason, bool canDropSilk, string silkKey)
        {
            switch (reason)
            {
                case "water": return "水の中へは入れない";
                case "cliff":
                case "nothing-ahead":
                case "no-drop":
                case "edge-invalid":
                    return canDropSilk ? $"がけになっている。{silkKey} で糸を出せばおりられる" : "この先はがけになっている";
                case "overhang": return "上がつっかえて進めない";
                case "outside": return "この先は遠すぎて行けない";
                default: return "この先へは進めないみたい";
            }
        }

        public void TickHud(bool playing)
        {
            using var prof = s_Hud.Auto();   // 処理時間の計測（パフォーマンスの調整用）
            if (!EnsureBound()) return;
            bool touch = ForceTouch ?? (GameInput.TouchDetected || Application.isMobilePlatform);
            if (touch != _touchMode)
            {
                _touchMode = touch;
                _lastHelpDevice = "";
                _howtoDevice = "";
                if (!touch) EndJoystick();
                SetHelpVisible(_helpVisible);
            }
            UpdateScale();
            UpdateLayoutClasses();
            UpdateSafeArea();
            if (IsLoadingVisible)
            {
                _tipTimer += Time.unscaledDeltaTime;
                if (_tipTimer > 4.5f) NextTip();
            }
            _touch.style.display = _touchMode && playing ? DisplayStyle.Flex : DisplayStyle.None;

            // 縦向きのスマホでは、横向きをすすめる（数秒だけ）
            bool phonePortrait = _touchMode && IsPortrait && Screen.width < Screen.height * 0.75f;
            if (!phonePortrait) _rotateTimer = 0f;
            else if (playing) _rotateTimer += Time.unscaledDeltaTime;
            _rotateHint.EnableInClassList("hidden", !(phonePortrait && playing && _rotateTimer < 6f));

            if (!playing) return;

            BuildHelp();
            if (_helpTimer >= 0f)
            {
                _helpTimer += Time.unscaledDeltaTime;
                if (_helpTimer > 40f && _helpVisible)
                {
                    SetHelpVisible(false);
                    _helpTimer = -999f;
                }
            }
            if (_touchMode && _helpVisible && _helpTimer > 12f) SetHelpVisible(false);

            // 次の見出しが待っているときは、今の見出しを少し早めに切りあげる
            float bannerEnd = _bannerQueue.Count > 0 ? Mathf.Min(_bannerUntil, _bannerShownAt + 3f) : _bannerUntil;
            if (_banner.ClassListContains("banner--show") && Time.unscaledTime > bannerEnd)
            {
                _banner.RemoveFromClassList("banner--show");
                if (_bannerQueue.Count > 0)
                {
                    var next = _bannerQueue.Dequeue();
                    _banner.schedule.Execute(() => ShowBannerText(next.sub, next.title, next.desc)).StartingIn(450);
                }
            }
            if (IsCreatureCardVisible && Time.unscaledTime > _creatureUntil)
            {
                _creatureCard.RemoveFromClassList("creature-card--show");
                _creatureCard.pickingMode = PickingMode.Ignore;
            }

            UpdateMinimap();
            RefreshCounts();
            if (IsMapOpen) LayoutBigMap();

            // 糸をねらう照準
            bool aiming = _worm != null && _worm.IsAiming;
            _reticle.EnableInClassList("hidden", !aiming);
            if (aiming)
            {
                _reticle.EnableInClassList("reticle--ok", _worm.AimValid);
                _reticle.EnableInClassList("reticle--far", !_worm.AimValid);
                _reticleText.text = _worm.AimValid ? $"{KeyName("aim")}をはなすと糸を発射" : _worm.AimProblem;
            }
            // オートセーブの表示
            _saveIndicator.EnableInClassList("save-indicator--show", Time.unscaledTime < _saveShowUntil);
            // タッチの「糸」ボタンは、壁にいるときは「はなす」
            if (_silkLabel != null && _worm != null)
            {
                string t = _worm.OnSteepSurface && !_worm.CanDropSilk ? "はなす" : (_worm.IsReeling ? "はなす" : "糸");
                if (_silkLabel.text != t) _silkLabel.text = t;
            }

            // 今いる場所の名前
            if (_worm != null)
            {
                string place = "";
                Vector3 p = _worm.CenterPosition;
                foreach (var lm in MapArea.Landmarks)
                {
                    if (_collect != null && !_collect.IsDiscovered(lm.id)) continue;
                    if (Vector2.Distance(new Vector2(p.x, p.z), lm.position) < lm.radius + 2f) place = "◆ " + lm.name;
                }
                if (_areaName.text != place) _areaName.text = place;

                // 状況に応じたヒント
                if (_worm.IsReeling)
                    SetPrompt(KeyName("silk"), "はなす");
                else if (_worm.IsFalling)
                    SetPrompt(KeyName("silk"), "糸を出してつかまる");
                else if (_worm.State == InchwormController.Mode.Hang)
                    SetPrompt(KeyName("silk"), "長押しで糸をのぼる　／　" + KeyName("sprint") + " ではやく　／　" + KeyName("stand") + " で糸を切る");
                else if (_worm.IsAiming)
                    SetPrompt("", "");
                else if (_worm.OnSteepSurface && !_worm.CanDropSilk)
                    SetPrompt(KeyName("silk"), "はなれて落ちる");
                else if (_worm.CanDropSilk)
                    SetPrompt(KeyName("silk"), "糸を出してぶら下がる");
                else if (!string.IsNullOrEmpty(GateHint))
                    SetPrompt("", GateHint);
                else if (_worm.IsBlocked && GameInput.Move.sqrMagnitude > 0.1f)
                    SetPrompt("", BlockedMessage(_worm.BlockReason, _worm.CanDropSilk, KeyName("silk")));
                else
                    SetPrompt("", "");
            }
        }
    }
}

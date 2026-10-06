using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shakutori
{
    /// <summary>UI Toolkit の画面（タイトル・HUD・地図・図鑑・メニュー・タッチ操作）。</summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameUI : MonoBehaviour
    {
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

        VisualElement _root, _hud, _look, _loading, _loadingFill, _title, _fade;
        VisualElement _mapOverlay, _pauseOverlay, _howtoOverlay, _confirmOverlay, _completeOverlay, _collectionOverlay;
        VisualElement _banner, _toasts, _prompt, _help, _touch, _joystick, _knob, _moveZone;
        VisualElement _minimapMask, _minimapImage, _minimapPlayer, _bigmap, _bigmapPlayer, _mapAreas;
        VisualElement _creatureCard, _creatureCardImg, _zukanImg, _skinGrid, _recordList;
        Label _dropCount, _placeCount, _creatureCount, _dropLabel, _areaTitle, _areaName, _loadingText;
        Label _bannerSub, _bannerTitle, _bannerDesc, _promptKey, _promptText, _helpHint, _rotateHint;
        Label _mapTitle, _mapSummary, _confirmText, _completeTitle, _completeText;
        Label _creatureCardName, _creatureCardDesc, _zukanCount, _zukanName, _zukanArea, _zukanDesc;
        ScrollView _legend, _zukanGrid;
        Button _continue, _qLow, _qHigh;
        Slider _sens, _music, _sfx;
        Toggle _invert;
        Action _confirmAction;

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
        public bool AnyOverlayOpen => IsMapOpen || IsPauseOpen || IsHowtoOpen || IsConfirmOpen || IsCompleteOpen || IsCollectionOpen;
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

            var s = SaveSystem.Settings;
            _sens.value = s.sensitivity;
            _music.value = s.music;
            _sfx.value = s.sfx;
            _invert.value = s.invertY;
            _sens.RegisterValueChangedCallback(e => { SaveSystem.Settings.sensitivity = e.newValue; ApplySettings(); });
            _music.RegisterValueChangedCallback(e => { SaveSystem.Settings.music = e.newValue; ApplySettings(); });
            _sfx.RegisterValueChangedCallback(e => { SaveSystem.Settings.sfx = e.newValue; ApplySettings(); });
            _invert.RegisterValueChangedCallback(e => { SaveSystem.Settings.invertY = e.newValue; ApplySettings(); });

            _bigmap.RegisterCallback<GeometryChangedEvent>(e => LayoutBigMap());
            _root.RegisterCallback<GeometryChangedEvent>(e => UpdateLayoutClasses());

            SetupLookArea();
            SetupTouch();
            BuildHowtoGrid();
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

        public void ApplySettings()
        {
            var s = SaveSystem.Settings;
            GameInput.LookSensitivity = s.sensitivity;
            GameInput.InvertY = s.invertY;
            _qLow.EnableInClassList("chip--on", s.quality == 0);
            _qHigh.EnableInClassList("chip--on", s.quality != 0);
            SaveSystem.SaveSettings();
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
            if (!string.IsNullOrEmpty(text)) _loadingText.text = text;
        }

        /// <summary>エリア移動のときにもう一度読み込み画面を出す。</summary>
        public void ShowLoading(string text)
        {
            if (!EnsureBound()) return;
            _loading.RemoveFromClassList("hidden");
            _loading.RemoveFromClassList("loading--out");
            _loading.pickingMode = PickingMode.Position;
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

        public bool IsTitleVisible => !_title.ClassListContains("hidden") && !_title.ClassListContains("title-overlay--out");

        public void Fade(bool on) => _fade.EnableInClassList("fade--on", on);

        public void ShowPause(bool show)
        {
            _pauseOverlay.EnableInClassList("hidden", !show);
            if (show) Q<Button>("resume").Focus();
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
            OverlayChanged?.Invoke();
        }

        /// <summary>Esc/戻るボタン: 一番上のウィンドウを閉じる。閉じるものがなければ false。</summary>
        public bool Back()
        {
            if (IsConfirmOpen) { ShowConfirm(false); return true; }
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
            _creatureCount.text = $"{c} / {SpeciesCatalog.Count}";
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
            ShowBannerText(area.DisplayName, area.Subtitle, area.Tagline);
        }

        void ShowBannerText(string sub, string title, string desc)
        {
            _bannerSub.text = sub;
            _bannerTitle.text = title;
            _bannerDesc.text = desc;
            _banner.AddToClassList("banner--show");
            _bannerUntil = Time.unscaledTime + 5f;
        }

        public void Toast(string text) => Toast(text, "icon-drop");

        public void Toast(string text, string iconClass)
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
            t.schedule.Execute(() => t.RemoveFromClassList("toast--show")).StartingIn(2600);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(3100);
            while (_toasts.childCount > 4) _toasts.RemoveAt(0);
        }

        /// <summary>いきものを見つけたときのカード。</summary>
        public void ShowCreature(SpeciesDef sp)
        {
            if (!EnsureBound() || sp == null) return;
            var tex = _assets != null ? _assets.Portrait(sp.id) : null;
            _creatureCardImg.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            _creatureCardName.text = sp.name;
            _creatureCardDesc.text = sp.description;
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
                }
            }
            switch (action)
            {
                case "silk": return pad ? "A" : "Space";
                case "sprint": return pad ? "RT" : "Shift";
                case "stand": return pad ? "Y" : "E";
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
            SaveSystem.SaveSettings();
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
                    (new[] { "A" }, "糸でおりる / 長押しでのぼる"),
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
                };
            else
                rows = new[]
                {
                    (new[] { "W", "A", "S", "D" }, "すすむ"),
                    (new[] { "ドラッグ" }, "見まわす"),
                    (new[] { "ホイール" }, "ズーム"),
                    (new[] { "Shift" }, "はやく"),
                    (new[] { "E" }, "背伸び（長押し）"),
                    (new[] { "Space" }, "糸でおりる / 長押しでのぼる"),
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
                    ("はやく", "はやく這う（切り替え）"),
                    ("背伸び", "背伸びして見わたす"),
                    ("糸", "糸でぶら下がる / 長押しでのぼる"),
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
                    ("B", "いきもの図鑑・きせかえ"),
                    ("M", "地図・エリア移動"),
                    ("H", "操作説明の表示切り替え"),
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
            int found = 0;
            foreach (var sp in SpeciesCatalog.All)
            {
                bool known = Creatures.IsDiscovered(sp.id);
                if (known) found++;
                var card = new Button();
                card.AddToClassList("zukan-card");
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.AddToClassList("zukan-card-img");
                var tex = Portrait(sp.id);
                if (tex != null) img.style.backgroundImage = new StyleBackground(tex);
                img.EnableInClassList("zukan-card-img--unknown", !known);
                var name = new Label(known ? sp.name : "？？？") { pickingMode = PickingMode.Ignore };
                name.AddToClassList("zukan-card-name");
                card.Add(img);
                card.Add(name);
                string id = sp.id;
                card.clicked += () =>
                {
                    AudioManager.Instance?.Click();
                    SelectSpecies(id);
                };
                _zukanGrid.Add(card);
                _zukanCards[sp.id] = card;
            }
            _zukanCount.text = $"見つけたいきもの {found} / {SpeciesCatalog.Count}　　近づくと図鑑に登録されます";
            if (_selectedSpecies == null || SpeciesCatalog.Get(_selectedSpecies) == null)
            {
                var first = SpeciesCatalog.All.FirstOrDefault(s => Creatures.IsDiscovered(s.id)) ?? SpeciesCatalog.All[0];
                _selectedSpecies = first.id;
            }
            SelectSpecies(_selectedSpecies);
        }

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
            _zukanArea.text = "すみか：" + sp.areaLabel;
            _zukanDesc.text = known ? sp.description : "ヒント：" + sp.hint;
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
            AddRecord("あそんだ時間", t >= 3600 ? $"{t / 3600}時間 {t / 60 % 60}分" : $"{t / 60}分");
            foreach (var area in Areas.All)
            {
                AddRecord(area.DropName, $"{Collectibles.CollectedIn(area)} / {area.DropCount}");
                AddRecord(area.DisplayName + "の名所", $"{Collectibles.DiscoveredIn(area)} / {area.Landmarks.Count}");
            }
            AddRecord("見つけたいきもの", $"{Creatures.DiscoveredCount} / {SpeciesCatalog.Count}");
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
            foreach (var dot in _miniDots) dot.BringToFront();
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
            _minimapPlayer.style.rotate = new Rotate(new Angle(yaw, AngleUnit.Degree));

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
                _legend.Add(row);
            }
            if (_collect != null)
                _mapSummary.text = $"{area.DropName} {_collect.CollectedDrops} / {_collect.TotalDrops}　　名所 {_collect.DiscoveredPlaces} / {_collect.TotalPlaces}";
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
            };
            _look.RegisterCallback(up);
            _look.RegisterCallback<PointerCancelEvent>(e => _lookPointers.Remove(e.pointerId));
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
            GameInput.VirtualMove = new Vector2(d.x, -d.y) / JoyMax;
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
            var res = new Vector2Int(Mathf.RoundToInt(_baseResolution.x * k), Mathf.RoundToInt(_baseResolution.y * k));
            if (_panel.referenceResolution != res) _panel.referenceResolution = res;
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
        public void TickHud(bool playing)
        {
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

            if (_banner.ClassListContains("banner--show") && Time.unscaledTime > _bannerUntil)
                _banner.RemoveFromClassList("banner--show");
            if (IsCreatureCardVisible && Time.unscaledTime > _creatureUntil)
                _creatureCard.RemoveFromClassList("creature-card--show");

            UpdateMinimap();
            RefreshCounts();
            if (IsMapOpen) LayoutBigMap();

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
                if (_worm.State == InchwormController.Mode.Hang)
                    SetPrompt(KeyName("silk"), "長押しで糸をのぼる　／　" + KeyName("sprint") + " ではやくおりる");
                else if (_worm.CanDropSilk)
                    SetPrompt(KeyName("silk"), "糸を出してぶら下がる");
                else if (!string.IsNullOrEmpty(GateHint))
                    SetPrompt("", GateHint);
                else if (_worm.IsBlocked && GameInput.Move.sqrMagnitude > 0.1f)
                    SetPrompt("", "この先へは進めないみたい");
                else
                    SetPrompt("", "");
            }
        }
    }
}

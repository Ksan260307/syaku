using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shakutori
{
    /// <summary>UI Toolkit の画面（タイトル・HUD・地図・メニュー・タッチ操作）。</summary>
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

        VisualElement _root, _hud, _look, _loading, _loadingFill, _title, _fade;
        VisualElement _mapOverlay, _pauseOverlay, _howtoOverlay, _confirmOverlay, _completeOverlay;
        VisualElement _banner, _toasts, _prompt, _help, _touch, _joystick, _knob;
        VisualElement _minimapImage, _minimapPlayer, _bigmap, _bigmapPlayer;
        Label _dropCount, _placeCount, _areaName, _loadingText, _bannerTitle, _bannerDesc, _promptKey, _promptText, _helpHint, _mapSummary, _confirmText, _completeText;
        ScrollView _legend;
        Button _continue, _qLow, _qHigh;
        Slider _sens, _music, _sfx;
        Toggle _invert;
        Action _confirmAction;

        readonly Dictionary<int, VisualElement> _miniMarkers = new Dictionary<int, VisualElement>();
        readonly List<VisualElement> _miniDots = new List<VisualElement>();
        readonly Dictionary<int, Vector2> _lookPointers = new Dictionary<int, Vector2>();
        int _joyPointer = -1;
        float _bannerUntil;
        float _helpTimer;
        bool _helpVisible = true;
        string _lastHelpDevice = "";
        int _lastDrops = -1, _lastPlaces = -1;
        bool _touchMode;

        public bool IsMapOpen => !_mapOverlay.ClassListContains("hidden");
        public bool IsPauseOpen => !_pauseOverlay.ClassListContains("hidden");
        public bool IsHowtoOpen => !_howtoOverlay.ClassListContains("hidden");
        public bool IsConfirmOpen => !_confirmOverlay.ClassListContains("hidden");
        public bool IsCompleteOpen => !_completeOverlay.ClassListContains("hidden");
        public bool AnyOverlayOpen => IsMapOpen || IsPauseOpen || IsHowtoOpen || IsConfirmOpen || IsCompleteOpen;
        public bool IsBannerVisible => _banner != null && _banner.ClassListContains("banner--show");
        public string BannerTitle => _bannerTitle != null ? _bannerTitle.text : "";
        public int ToastCount => _toasts != null ? _toasts.childCount : 0;
        public string PromptText => _prompt != null && _prompt.ClassListContains("prompt--show") ? _promptText.text : "";
        public bool IsHudVisible => _hud != null && !_hud.ClassListContains("hud--hidden");
        public VisualElement Root => _root;

        Collectibles _collect;
        InchwormController _worm;
        Transform _cam;

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
            _banner = Q<VisualElement>("banner");
            _bannerTitle = Q<Label>("banner-title");
            _bannerDesc = Q<Label>("banner-desc");
            _toasts = Q<VisualElement>("toasts");
            _prompt = Q<VisualElement>("prompt");
            _promptKey = Q<Label>("prompt-key");
            _promptText = Q<Label>("prompt-text");
            _help = Q<VisualElement>("help");
            _helpHint = Q<Label>("help-hint");
            _touch = Q<VisualElement>("touch");
            _joystick = Q<VisualElement>("joystick");
            _knob = Q<VisualElement>("joystick-knob");
            _dropCount = Q<Label>("drop-count");
            _placeCount = Q<Label>("place-count");
            _areaName = Q<Label>("area-name");
            _minimapImage = Q<VisualElement>("minimap-image");
            _minimapPlayer = Q<VisualElement>("minimap-player");
            _bigmap = Q<VisualElement>("bigmap");
            _bigmapPlayer = Q<VisualElement>("bigmap-player");
            _mapSummary = Q<Label>("map-summary");
            _legend = Q<ScrollView>("map-legend");
            _confirmText = Q<Label>("confirm-text");
            _completeText = Q<Label>("complete-text");
            _continue = Q<Button>("btn-continue");
            _qLow = Q<Button>("q-low");
            _qHigh = Q<Button>("q-high");
            _sens = Q<Slider>("set-sens");
            _music = Q<Slider>("set-music");
            _sfx = Q<Slider>("set-sfx");
            _invert = Q<Toggle>("set-invert");

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
            Click("resume", () => ResumePressed?.Invoke());
            Click("menu-button", () => TogglePause());
            Click("map-button", () => ToggleMap());
            Click("map-close", () => ShowMap(false));
            Click("to-title", () => ToTitlePressed?.Invoke());
            Click("reset", () => Confirm("集めたしずくと見つけた名所の記録を消して、最初からやり直しますか？", () => ResetPressed?.Invoke()));
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

        public void Init(Collectibles collect, InchwormController worm, Transform cam, Texture2D map)
        {
            EnsureBound();
            _collect = collect;
            _worm = worm;
            _cam = cam;
            _minimapImage.style.backgroundImage = new StyleBackground(map);
            _bigmap.style.backgroundImage = new StyleBackground(map);
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

        public void HideLoading()
        {
            _loading.AddToClassList("loading--out");
            _loading.pickingMode = PickingMode.Ignore;
            _loading.schedule.Execute(() => _loading.AddToClassList("hidden")).StartingIn(900);
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

        public void ShowComplete(bool show, string text)
        {
            if (show) _completeText.text = text;
            _completeOverlay.EnableInClassList("hidden", !show);
            OverlayChanged?.Invoke();
        }

        public void CloseAllOverlays()
        {
            _mapOverlay.AddToClassList("hidden");
            _pauseOverlay.AddToClassList("hidden");
            _howtoOverlay.AddToClassList("hidden");
            _confirmOverlay.AddToClassList("hidden");
            _completeOverlay.AddToClassList("hidden");
            OverlayChanged?.Invoke();
        }

        /// <summary>Esc/戻るボタン: 一番上のウィンドウを閉じる。閉じるものがなければ false。</summary>
        public bool Back()
        {
            if (IsConfirmOpen) { ShowConfirm(false); return true; }
            if (IsHowtoOpen) { ShowHowto(false); return true; }
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
            _dropCount.text = $"{d} / {_collect.TotalDrops}";
            _placeCount.text = $"{p} / {_collect.TotalPlaces}";
            if (!instant)
            {
                if (d != _lastDrops && _lastDrops >= 0) Pop(_dropCount);
                if (p != _lastPlaces && _lastPlaces >= 0) Pop(_placeCount);
            }
            _lastDrops = d;
            _lastPlaces = p;
        }

        void Pop(VisualElement e)
        {
            e.AddToClassList("obj-count--pop");
            e.schedule.Execute(() => e.RemoveFromClassList("obj-count--pop")).StartingIn(260);
        }

        public void ShowBanner(LandmarkDef lm)
        {
            _bannerTitle.text = lm.name;
            _bannerDesc.text = lm.description;
            _banner.AddToClassList("banner--show");
            _bannerUntil = Time.unscaledTime + 5f;
            RefreshMarkers();
            RefreshCounts();
        }

        public void Toast(string text)
        {
            var t = new VisualElement();
            t.AddToClassList("toast");
            t.pickingMode = PickingMode.Ignore;
            var icon = new VisualElement();
            icon.AddToClassList("icon");
            icon.AddToClassList("icon-drop");
            icon.pickingMode = PickingMode.Ignore;
            var l = new Label(text);
            l.AddToClassList("toast-label");
            l.pickingMode = PickingMode.Ignore;
            t.Add(icon);
            t.Add(l);
            _toasts.Add(t);
            t.schedule.Execute(() => t.AddToClassList("toast--show")).StartingIn(20);
            t.schedule.Execute(() => t.RemoveFromClassList("toast--show")).StartingIn(2200);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(2700);
            while (_toasts.childCount > 4) _toasts.RemoveAt(0);
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

        void BuildHelp()
        {
            string device = GameInput.UsingGamepad ? "pad" : (_touchMode ? "touch" : "kb");
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
                    (new[] { "View" }, "地図"),
                    (new[] { "Menu" }, "メニュー"),
                };
            else if (device == "touch")
                rows = new[]
                {
                    (new[] { "左のスティック" }, "すすむ"),
                    (new[] { "画面をドラッグ" }, "見まわす"),
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
            var grid = Q<VisualElement>("howto-grid");
            grid.Clear();
            (string key, string text)[] items =
            {
                ("W A S D / 矢印", "すすむ（カメラの向き基準）"),
                ("マウスでドラッグ", "見まわす"),
                ("ホイール", "ズーム"),
                ("Shift", "はやく這う"),
                ("E（長押し）", "背伸びして見わたす"),
                ("Space", "糸を出してぶら下がる"),
                ("Space（長押し）", "ぶら下がり中に糸をのぼる"),
                ("M", "森の地図"),
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
        // マップ
        // ------------------------------------------------------------------
        void BuildMarkers()
        {
            foreach (var m in _miniMarkers.Values) m.RemoveFromHierarchy();
            _miniMarkers.Clear();
            foreach (var lm in ForestLayout.Landmarks)
            {
                var mk = new VisualElement { pickingMode = PickingMode.Ignore };
                mk.AddToClassList("map-marker");
                _minimapImage.Add(mk);
                _miniMarkers[lm.id] = mk;
            }
            for (int i = 0; i < 12; i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("map-dot");
                dot.style.display = DisplayStyle.None;
                _minimapImage.Add(dot);
                _miniDots.Add(dot);
            }
            RefreshMarkers();
        }

        void RefreshMarkers()
        {
            foreach (var lm in ForestLayout.Landmarks)
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

        void UpdateMinimap()
        {
            if (_worm == null) return;
            Vector3 p = _worm.CenterPosition;
            Vector2 uv = WorldToMap(p);
            const float inner = 212f;
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
            foreach (var lm in ForestLayout.Landmarks)
            {
                if (!_miniMarkers.TryGetValue(lm.id, out var mk)) continue;
                bool known = _collect != null && _collect.IsDiscovered(lm.id);
                float dist = Vector2.Distance(new Vector2(p.x, p.z), lm.position);
                mk.style.display = known || dist < 32f ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void RefreshBigMap()
        {
            const float size = 614f;
            foreach (var c in _bigmap.Children().ToArray())
                if (c != _bigmapPlayer) c.RemoveFromHierarchy();
            foreach (var lm in ForestLayout.Landmarks)
            {
                bool known = _collect != null && _collect.IsDiscovered(lm.id);
                Vector2 uv = WorldToMap(new Vector3(lm.position.x, 0f, lm.position.y));
                var mk = new VisualElement { pickingMode = PickingMode.Ignore };
                mk.AddToClassList("map-marker");
                mk.AddToClassList(known ? "map-marker--place" : "map-marker--unknown");
                mk.style.width = 34;
                mk.style.height = 34;
                mk.style.left = uv.x * size - 17f;
                mk.style.top = uv.y * size - 17f;
                _bigmap.Add(mk);
                var lbl = new Label(known ? lm.name : "？？？") { pickingMode = PickingMode.Ignore };
                lbl.AddToClassList("bigmap-label");
                lbl.style.left = uv.x * size - 100f;
                lbl.style.top = uv.y * size + 15f;
                _bigmap.Add(lbl);
            }
            _bigmapPlayer.BringToFront();
            if (_worm != null)
            {
                Vector2 uv = WorldToMap(_worm.CenterPosition);
                _bigmapPlayer.style.left = uv.x * size - 18f;
                _bigmapPlayer.style.top = uv.y * size - 18f;
                Vector3 h = _worm.Heading;
                _bigmapPlayer.style.rotate = new Rotate(new Angle(Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg, AngleUnit.Degree));
            }

            _legend.Clear();
            foreach (var lm in ForestLayout.Landmarks)
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
                _mapSummary.text = $"森のしずく {_collect.CollectedDrops} / {_collect.TotalDrops}　　名所 {_collect.DiscoveredPlaces} / {_collect.TotalPlaces}";
        }

        // ------------------------------------------------------------------
        // ドラッグで視点・タッチ操作
        // ------------------------------------------------------------------
        void SetupLookArea()
        {
            _look.RegisterCallback<PointerDownEvent>(e =>
            {
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
            _joystick.RegisterCallback<PointerDownEvent>(e =>
            {
                _joyPointer = e.pointerId;
                _joystick.CapturePointer(e.pointerId);
                UpdateJoystick(e.localPosition);
                AudioManager.Instance?.StartMusic();
            });
            _joystick.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId == _joyPointer) UpdateJoystick(e.localPosition);
            });
            _joystick.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != _joyPointer) return;
                _joyPointer = -1;
                _joystick.ReleasePointer(e.pointerId);
                GameInput.VirtualMove = Vector2.zero;
                _knob.style.left = 70;
                _knob.style.top = 70;
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

        void UpdateJoystick(Vector2 local)
        {
            float r = 115f;
            Vector2 c = new Vector2(r, r);
            Vector2 d = local - c;
            float max = 80f;
            if (d.magnitude > max) d = d.normalized * max;
            _knob.style.left = 70 + d.x;
            _knob.style.top = 70 + d.y;
            GameInput.VirtualMove = new Vector2(d.x, -d.y) / max;
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------
        public void TickHud(bool playing)
        {
            if (!EnsureBound()) return;
            bool touch = GameInput.TouchDetected || Application.isMobilePlatform;
            if (touch != _touchMode)
            {
                _touchMode = touch;
                _lastHelpDevice = "";
            }
            _touch.style.display = _touchMode && playing ? DisplayStyle.Flex : DisplayStyle.None;
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

            UpdateMinimap();
            RefreshCounts();

            // 今いる場所の名前
            if (_worm != null)
            {
                string area = "";
                Vector3 p = _worm.CenterPosition;
                foreach (var lm in ForestLayout.Landmarks)
                {
                    if (_collect != null && !_collect.IsDiscovered(lm.id)) continue;
                    if (Vector2.Distance(new Vector2(p.x, p.z), lm.position) < lm.radius + 2f) area = "◆ " + lm.name;
                }
                if (_areaName.text != area) _areaName.text = area;

                // 状況に応じたヒント
                if (_worm.State == InchwormController.Mode.Hang)
                    SetPrompt(KeyName("silk"), "長押しで糸をのぼる　／　" + KeyName("sprint") + " ではやくおりる");
                else if (_worm.CanDropSilk)
                    SetPrompt(KeyName("silk"), "糸を出してぶら下がる");
                else if (_worm.IsBlocked && GameInput.Move.sqrMagnitude > 0.1f)
                    SetPrompt("", "この先へは進めないみたい");
                else
                    SetPrompt("", "");
            }
        }
    }
}

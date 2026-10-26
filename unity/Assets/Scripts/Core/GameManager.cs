using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Shakutori
{
    /// <summary>ゲーム全体の流れ（読み込み → タイトル → 探検 → メニュー、エリア移動）。</summary>
    public class GameManager : MonoBehaviour
    {
        public enum GameState { Loading, Title, Playing, Paused, Traveling }

        public WorldGenerator world;
        public InchwormController worm;
        public FollowCamera followCamera;
        public GameUI ui;
        public Collectibles collectibles;
        public Creatures creatures;
        public AmbientFX fx;
        public Volume postVolume;

        public GameState State { get; private set; } = GameState.Loading;
        public AreaLayout Area => Areas.Current;
        public event Action<AreaLayout> AreaChanged;

        float _saveTimer;
        float _gateCooldown;
        float _stuckTime, _lastStuckHint = -999f;
        public const float AutosaveInterval = 20f;

        /// <summary>
        /// 一度だけのヒントや、重いときに画質を自動で下げる機能。
        /// 総合テストでは結果が毎回同じになるよう切っておく（ヒントのテストだけ入れる）。
        /// </summary>
        public static bool Assists = true;

        float _playingFor;            // 探検をはじめてからの時間（ヒントを出しはじめるまで）
        float _lastTip = -999f;
        float _assistTimer;
        float _fpsAvg = 60f, _slowTime;
        bool _autoLowered;
        // 画面の細かさ（表示だけ。ゲームの中身・いきものの動きには関係しない）
        public const float MinRenderScale = 0.7f;
        float _frameMsAvg = 16.7f, _scaleTimer;
        float _scaleBase = 1f, _scaleNow = 1f;
        public float RenderScaleNow => _scaleNow;
        int _combo;
        float _lastDropTime = -99f;
        float _rumbleUntil;
        public bool PhotoMode => ui != null && ui.IsPhotoMode;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ShakuRegisterPageEvents();
        [DllImport("__Internal")] static extern void ShakuVibrate(int ms);
#else
        static void ShakuRegisterPageEvents() { }
        static void ShakuVibrate(int ms) { }
#endif
        bool _starting;
        TitleDemo _demo;
        readonly TitleSecret _secret = new TitleSecret();
        bool _travelAll;   // タイトルのひみつの操作をした（「はじめから」でやりなおしても、そのまま）
        Tutorial _tutorial;
        ProgressStats _stats;
        AreaProps _areaProps;
        AreaSounds _areaSounds;
        Footprints _footprints;
        AutoWalk _autoWalk;
        bool _rumbleHead;
        Renderer _wormRenderer;
        WormWear _wear;

        /// <summary>しゃくとりむしの頭のぼうし・めがね（テスト用）。</summary>
        public WormWear Wear => _wear;
        /// <summary>砂・雪の上の足あと。</summary>
        public Footprints Prints => _footprints;
        /// <summary>おまかせ歩き。</summary>
        public AutoWalk AutoWalker => _autoWalk;

        /// <summary>チュートリアルの練習の森にいる。</summary>
        public bool InTutorial => Areas.Current == Areas.Tutorial;
        /// <summary>チュートリアルの進め役（テスト用）。</summary>
        public Tutorial TutorialGuide => _tutorial;

        IEnumerator Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // ブラウザでは 1 秒に 60 回まで描く（120Hz などの画面で、むだに倍の回数を描かない）
            Application.targetFrameRate = 60;
#endif
            SaveSystem.Load();
            ApplyAmbient();
            ApplyQuality(SaveSystem.Settings.quality);
            worm.InputEnabled = false;
            collectibles.Active = false;
            if (creatures != null) creatures.Active = false;
            followCamera.titleMode = true;
            // スマホ・タブレットは草花を少なめにして軽くする
            if (Application.isMobilePlatform || GameInput.TouchCapable) world.foliageDensity = Mathf.Min(world.foliageDensity, 0.65f);
            _wormRenderer = worm.body != null ? worm.body.GetComponent<Renderer>() : null;
            if (worm.body != null)
            {
                _wear = worm.body.GetComponent<WormWear>();
                if (_wear == null) _wear = worm.body.gameObject.AddComponent<WormWear>();
                _wear.body = worm.body;
                _wear.assets = world.assets;
                _wear.material = world.assets != null ? world.assets.wear : null;
            }

            var area = SaveSystem.HasSave ? Areas.Get(SaveSystem.Data.area) ?? Areas.Forest : Areas.Forest;
            if (area == Areas.Tutorial) area = Areas.Forest;   // 練習の森からは、始めない
            ui.LoadingAreaId = area.Id;
            ui.SetLoading(0f);
            yield return null;

            fx.followTarget = followCamera.transform;
            fx.Build();
            // エリアの動く小物と、場所で聞こえる音
            _areaProps = GetComponent<AreaProps>();
            if (_areaProps == null) _areaProps = gameObject.AddComponent<AreaProps>();
            _areaSounds = GetComponent<AreaSounds>();
            if (_areaSounds == null) _areaSounds = gameObject.AddComponent<AreaSounds>();
            _footprints = GetComponent<Footprints>();
            if (_footprints == null) _footprints = gameObject.AddComponent<Footprints>();
            _footprints.Init(world.assets != null ? world.assets.footprint : null);
            _autoWalk = GetComponent<AutoWalk>();
            if (_autoWalk == null) _autoWalk = gameObject.AddComponent<AutoWalk>();
            _autoWalk.worm = worm;
            _autoWalk.cameraTransform = followCamera.transform;
            _autoWalk.GaveUp += () => ui.Toast("ここからは、歩いて行けない…", "icon-map");
            yield return BuildArea(area, p => ui.SetLoading(p * 0.95f));
            PlaceWorm(SaveSystem.HasSave);
            followCamera.target = worm;
            followCamera.SnapToTarget();
            _stats = ProgressStats.FromSave();

            worm.Stepped += (p, head) =>
            {
                // 足音：苔の上はやわらかく、公園の砂はしゃりしゃり、丸太や土管の中はひびく
                var surface = AreaProps.Refine(SurfaceAt(p), p);
                AudioManager.Instance?.Step(head, surface, worm.StepLoudness, AreaProps.Echo(p));
                // 砂や土の上を歩くと、体が少しずつ、ほこりっぽくなる
                if (surface == AudioManager.Surface.Sand)
                {
                    bool snowy = Areas.Current == Areas.Mountain || Areas.Current == Areas.Xmas;
                    if (Areas.Current != Areas.Xmas) worm.AddDust(0.035f);   // 雪の村では、ほこりっぽくならない
                    // 砂・雪の上には、足あとが残る
                    _footprints?.Add(p, worm.SurfaceUp, worm.Heading, head, snowy ? Footprints.SnowColor : Footprints.SandColor);
                }
                else if (surface == AudioManager.Surface.Ground) worm.AddDust(0.004f);
                _areaProps?.OnStep(p);
                StepPuff(p, head, surface);
                StepRumble(head);
                if (head) SaveSystem.Data.steps++;
                // 一歩ごとの小さな力：乗っている舟がゆれ、近くのしずくがふるえる
                PlatformFerry()?.Push(p, 0.25f * worm.StepLoudness);
                collectibles.Impulse(p, 0.6f * worm.StepLoudness);
            };
            ui.CreatureSource = creatures;
            ui.PhotoRequested += TogglePhoto;
            worm.SilkStarted += () =>
            {
                AudioManager.Instance?.Silk();
                SaveSystem.Data.silkUses++;
            };
            worm.SilkClimbTick += () => AudioManager.Instance?.SilkTick();
            worm.SilkCut += () => AudioManager.Instance?.SilkCut();
            // おまかせ歩き：地図で見つけていない名所を 2 回タップ・タッチで地面をタップ
            ui.WalkToRequested += (p, r) =>
            {
                _autoWalk.Begin(p, r);
                ui.Toast("歩いて行くよ（スティックを倒すと、やめる）", "icon-map");
            };
            ui.WalkToScreenPoint += WalkToScreenPoint;
            worm.Fell += () => AudioManager.Instance?.Fall();
            // 葉や枝からたれてきたしずくが、体に当たる
            worm.Dripped += p =>
            {
                fx.Burst(p + Vector3.up * 0.05f, 6, new Color(0.7f, 0.9f, 1f, 1f));
                AudioManager.Instance?.Drip();
            };
            worm.HitGround += OnHitGround;
            worm.Bounced += OnBounced;
            worm.Splashed += OnSplash;
            worm.Landed += () => AudioManager.Instance?.Land();
            // 葉をかじると、葉のかけらが散って、もぐもぐ
            worm.Nibbled += p =>
            {
                fx.Burst(p + Vector3.up * 0.05f, 4, new Color(0.55f, 0.85f, 0.35f, 1f));
                AudioManager.Instance?.Nibble();
            };
            collectibles.DropCollected += OnDrop;
            collectibles.LandmarkDiscovered += OnLandmark;
            if (creatures != null)
            {
                creatures.Discovered += OnCreature;
                creatures.Stank += p => fx.Stink(p);   // カメムシのにおい
                creatures.Splashed += (p, s, loud) =>   // マンタのジャンプの水しぶき
                {
                    fx.Splash(p, s, p.y);
                    if (loud > 0.05f) AudioManager.Instance?.Splash(s * loud);
                };
                creatures.DiscoveredAt += (sp, pos) =>
                {
                    if (pos == Vector3.zero) return;
                    fx.Burst(pos + Vector3.up * 0.3f, sp.IsRare ? 80 : 30);
                    worm.LookAt(pos, 1.6f);   // 見つけたいきもののほうを見る
                };
                // もう一度会う：あいさつ・なかよし・しぐさの観察・大きさの記録・あんない
                creatures.Greeted += OnGreeted;
                creatures.Observed += OnObserved;
                creatures.Measured += OnMeasured;
                creatures.GuideStarted += (sp, from, to) =>
                {
                    worm.LookAt(from, 1.2f);
                    AudioManager.Instance?.Friend();
                    ui.Toast($"{sp.name}が、あんないしてくれる！ ついていこう", "icon-book", 3.5f);
                };
                creatures.GuideTrail += p => fx.Glint(p);
                creatures.GuideTarget = GuideTarget;
            }
            ui.FastTravelRequested += id => FastTravel(id);
            ui.LandmarkTravelRequested += id => TravelToLandmark(id);
            ui.MapProvider = a => world.MapFor(a);   // 地図の下のボタンで、ほかのエリアの地図も見られる
            ui.RescueRequested += Rescue;
            ui.SaveRequested += () =>
            {
                SaveProgress();
                ui.Toast("セーブしました", "icon-book");
            };
            ShakuRegisterPageEvents();
            ui.ContinuePressed += () => StartGame(true);
            ui.NewGamePressed += () => StartGame(false);
            ui.TutorialPressed += () => StartGame(false, tutorial: true);
            ui.TutorialEndPressed += () =>
            {
                Resume();
                FinishTutorial();
            };
            _tutorial = gameObject.AddComponent<Tutorial>();
            ui.ResumePressed += Resume;
            ui.ToTitlePressed += GoToTitle;
            ui.ResetPressed += () => StartGame(false);
            ui.QualityChanged += ApplyQuality;
            ui.TravelRequested += id => TravelTo(id);
            ui.SkinSelected += id =>
            {
                ApplySkin();
                worm.Cheer();
            };

            // タイトルのデモ映像と、ひみつの操作
            _demo = gameObject.AddComponent<TitleDemo>();
            _demo.Bind(ui.TitleDemoView);
            _secret.Unlocked += OnTitleSecret;
            ui.LogoTapped += () => { if (State == GameState.Title) _secret.Tap(Time.unscaledTime); };

            ui.SetLoading(1f);
            yield return new WaitForSecondsRealtime(0.3f);
            ui.HideLoading();
            ui.ShowTitle(true);
            _demo.Show(true);
            State = GameState.Title;
        }

        /// <summary>エリアを作り直し、しずく・いきもの・演出・UI をそのエリアに合わせる。</summary>
        IEnumerator BuildArea(AreaLayout area, Action<float> progress)
        {
            LoadClock.Start();
            Areas.Current = area;
            worm.ClearSafeHistory();
            _footprints?.Clear();
            _autoWalk?.Stop();
            if (creatures != null) creatures.Clear();
            // 読み込み画面のあいだは、うしろのエリアを描かない（作っているとちゅうのフレームが軽くなる。画面は読み込み画面でかくれている）
            var cam = Camera.main;
            int mask = cam != null ? cam.cullingMask : ~0;
            bool hide = cam != null && ui.IsLoadingVisible;
            if (hide) cam.cullingMask = 0;
            yield return world.Generate(area, progress);
            collectibles.Build(world.DewdropPoints, area);
            if (creatures != null) creatures.Build(world);
            LoadClock.Lap("mobs");
            if (area != Areas.Tutorial) Habitats.Record(area, world.Mobs);   // 図鑑で見られるように、いきもののすみかを記録（練習の森はのぞく）
            fx.BuildArea(world);
            AreaAtmosphere.Apply(area, world.sun);   // エリアの空気と光
            ApplyGrade();
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpNow)
                urpNow.shadowDistance = AreaAtmosphere.ShadowDistance(area.Id, _qualityNow);   // 山小屋の中は、部屋の奥まで影
            _areaProps?.Build(world, fx);
            _areaSounds?.Build(world);
            ui.Init(collectibles, worm, followCamera.transform, world.MapTexture, world.assets);
            AudioManager.Instance?.SetArea(area.Id);
            if (area != Areas.Tutorial && !SaveSystem.Data.visited.Contains(area.Id)) SaveSystem.Data.visited.Add(area.Id);
            AreaChanged?.Invoke(area);
            LoadClock.Lap("fxUi");
            if (hide && cam != null) cam.cullingMask = mask;
            // 読み込み画面のあいだに、エリアの物をひととおり描いておく（はじめて画面に入ったときに止まらない）
            yield return RenderWarmup.Run(Camera.main, world, creatures);
            LoadClock.Lap("warmup");
            Debug.Log(LoadClock.Stop(area.Id));
        }

        void PlaceWorm(bool fromSave)
        {
            var d = SaveSystem.Data;
            var area = Areas.Current;
            if (fromSave && d.hasPosition && d.area == area.Id && area.InPlayArea(d.tail) && !area.IsUnderwater(d.tail))
                worm.Spawn(d.tail, d.tailNormal, d.head, d.headNormal);
            else
                worm.Spawn(world.SpawnPoint, world.SpawnForward);
        }

        /// <summary>「つづきから」(true) /「はじめから」(false)。tutorial なら、はじめから、チュートリアルの練習の森で始める。</summary>
        public void StartGame(bool continueGame, bool tutorial = false)
        {
            if (State == GameState.Loading || State == GameState.Traveling || _starting) return;
            StartCoroutine(Begin(continueGame, tutorial && !continueGame));
        }

        IEnumerator Begin(bool continueGame, bool tutorial)
        {
            _starting = true;
            _tutorial?.Stop();
            AudioManager.Instance?.StartMusic();
            ui.Fade(true);
            yield return new WaitForSecondsRealtime(0.65f);
            Time.timeScale = 1f;
            ui.CloseAllOverlays();
            _demo?.Show(false);   // 画面が暗いうちに、デモ映像を止めて、うしろのエリアをまた描く（エリアを作りなおす前に）
            if (!continueGame)
            {
                SaveSystem.ResetProgress();
                // チュートリアルは、まっさらな練習の記録で（ブラウザには書かない）。タイトルのひみつの操作は、本番の森で
                if (tutorial) SaveSystem.BeginPractice();
                else if (_travelAll) TitleSecret.UnlockAll();   // タイトルのひみつの操作は、はじめからでも
                _stats = ProgressStats.FromSave();
                AreaLayout first = tutorial ? Areas.Tutorial : (AreaLayout)Areas.Forest;
                if (Areas.Current != first)
                {
                    // 森（チュートリアルなら、練習の森）から始めなおす
                    ui.LoadingAreaId = first.Id;
                    ui.ShowLoading();
                    yield return BuildArea(first, ui.SetLoading);
                    ui.HideLoading();
                }
                else
                {
                    collectibles.ApplySave();
                    if (!tutorial && !SaveSystem.Data.visited.Contains(Areas.Forest.Id)) SaveSystem.Data.visited.Add(Areas.Forest.Id);
                }
                SaveSystem.Data.area = Areas.Current.Id;
                PlaceWorm(false);
            }
            else
            {
                PlaceWorm(true);
                if (SaveSystem.DewsMoved)
                {
                    SaveSystem.DewsMoved = false;
                    ui.Toast("しずくの場所が新しくなりました。もう一度さがしてみよう", "icon-drop", 4.5f);
                }
            }
            yield return Started(tutorial);
        }

        /// <summary>はじめから・つづきから・チュートリアルのあとに、探検を始める（画面が暗いうちに呼ぶ）。</summary>
        IEnumerator Started(bool tutorial)
        {
            ApplySkin();
            ui.RefreshCounts(true);
            ui.ShowTitle(false);
            _demo?.Show(false);
            followCamera.titleMode = false;
            followCamera.pitch = 20f;
            // 前に合わせたカメラの距離を使う
            float saved = SaveSystem.Settings.cameraDistance;
            followCamera.distance = saved > 0f ? Mathf.Clamp(saved, followCamera.minDistance, followCamera.maxDistance) : 3.1f;
            followCamera.SnapToTarget();
            ui.Fade(false);
            State = GameState.Playing;
            _playingFor = 0f;
            AnnounceRare();
            worm.InputEnabled = true;
            collectibles.Active = true;
            if (creatures != null) creatures.Active = true;
            _gateCooldown = 1.5f;
            _starting = false;
            if (tutorial)
            {
                ui.ShowAreaBanner(Areas.Tutorial);
                _tutorial.Begin(this);
            }
            yield return new WaitForSecondsRealtime(1.0f);
            var first = Areas.Current.EntryLandmark;
            if (first != null && !collectibles.IsDiscovered(first.id) && Collectibles.IsInside(first, worm.HeadPosition))
                collectibles.Discover(first);
        }

        // ------------------------------------------------------------------
        // チュートリアル
        // ------------------------------------------------------------------
        /// <summary>練習の森のトンネルの前のヒント。</summary>
        public const string TutorialGateHint = "トンネルをくぐると、しゃくとりの森へ（チュートリアルをおわる）";

        /// <summary>チュートリアルをおえて、本番の森から始める（練習の記録はすてる。練習で会ったいきものは、図鑑にのこす）。</summary>
        public void FinishTutorial()
        {
            if (!InTutorial || _starting || (State != GameState.Playing && State != GameState.Paused)) return;
            StartCoroutine(EndTutorial());
        }

        IEnumerator EndTutorial()
        {
            _starting = true;
            State = GameState.Traveling;
            Time.timeScale = 1f;
            worm.InputEnabled = false;
            collectibles.Active = false;
            if (creatures != null) creatures.Active = false;
            ui.GateHint = null;
            _tutorial.Stop();
            AudioManager.Instance?.Travel();
            ui.Fade(true);
            yield return new WaitForSecondsRealtime(0.7f);
            ui.CloseAllOverlays();
            SaveSystem.EndPractice(keepCreatures: true);
            if (_travelAll) TitleSecret.UnlockAll();
            _stats = ProgressStats.FromSave();
            ui.LoadingAreaId = Areas.Forest.Id;
            ui.ShowLoading();
            worm.enabled = false;
            yield return BuildArea(Areas.Forest, ui.SetLoading);
            worm.enabled = true;
            SaveSystem.Data.area = Areas.Forest.Id;
            PlaceWorm(false);
            ui.HideLoading();
            yield return new WaitForSecondsRealtime(0.2f);
            ui.ShowAreaBanner(Areas.Forest);
            ui.Toast("チュートリアル、おつかれさま！ ここから、しゃくとりの森のぼうけんが始まります", "icon-place", 4.5f);
            yield return Started(false);
        }

        float _dripNext;

        void Update()
        {
            bool playing = State == GameState.Playing;
            GameInput.OverlayOpen = ui != null && ui.AnyOverlayOpen;   // 地図・図鑑のスクロールで、カメラを拡大・縮小しない
            ui.TickHud(playing && !ui.AnyOverlayOpen);
            // ぬれた体から、しずくがたれる
            if (playing && worm != null && worm.IsWet && Time.time > _dripNext)
            {
                _dripNext = Time.time + UnityEngine.Random.Range(0.12f, 0.35f);
                fx.Drip(worm.CenterPosition);
            }
            SaveSystem.FlushSettings();
            FlushObserved();
            if (Time.unscaledTime > _rumbleUntil && _rumbleUntil > 0f)
            {
                _rumbleUntil = 0f;
                Gamepad.current?.SetMotorSpeeds(0f, 0f);
            }
            switch (State)
            {
                case GameState.Title:
                    if (GameInput.AnyKeyPressed) AudioManager.Instance?.StartMusic();
                    _secret.Poll();   // ↑↑↓↓←→←→
                    if (GameInput.PausePressed) ui.Back();
                    break;
                case GameState.Playing:
                    SaveSystem.Data.playTime += Time.deltaTime;
                    _playingFor += Time.deltaTime;
                    if (ui.IsPhotoMode)
                    {
                        // 写真モード：Esc・F2 でもどる（カメラは動かせる）
                        if (GameInput.PausePressed || GameInput.PhotoPressed) TogglePhoto();
                        worm.InputEnabled = false;
                        break;
                    }
                    if (GameInput.PhotoPressed && !ui.AnyOverlayOpen) TogglePhoto();
                    else if (GameInput.PausePressed)
                    {
                        if (!ui.Back()) Pause();
                    }
                    else if (GameInput.MapPressed) ui.ToggleMap();
                    else if (GameInput.CollectionPressed) ui.ToggleCollection();
                    else if (GameInput.HelpPressed) ui.ToggleHelp();
                    if (ui.IsPauseOpen) Pause();
                    worm.InputEnabled = !ui.AnyOverlayOpen;
                    UpdateGates();
                    UpdateStats();
                    UpdateStuck();
                    UpdateAssists();
                    UpdateRenderScale();
                    if (SaveSystem.Settings.autosave)
                    {
                        _saveTimer += Time.deltaTime;
                        if (_saveTimer > AutosaveInterval)
                        {
                            _saveTimer = 0f;
                            SaveProgress();
                        }
                    }
                    break;
                case GameState.Paused:
                    if (GameInput.PausePressed && !ui.Back()) Resume();
                    break;
            }
            ApplySkin();
        }

        // ------------------------------------------------------------------
        // エリア移動
        // ------------------------------------------------------------------
        /// <summary>木の根のトンネルに入ったら隣のエリアへ。近くにいるときはヒントを出す。</summary>
        void UpdateGates()
        {
            _gateCooldown -= Time.deltaTime;
            string hint = null;
            Vector3 head = worm.HeadPosition;
            foreach (var g in world.Gates)
            {
                float d = new Vector2(head.x - g.position.x, head.z - g.position.z).magnitude;
                var target = Areas.Get(g.def.targetArea);
                if (target == null) continue;
                // とびらは、はばの広いとびらの前（しきいの上）でヒント。入ったかどうかは、とびらの面で見る
                bool near = g.def.door ? g.def.DoorNear(head, g.position) : d < 7f;
                if (near) hint = InTutorial ? TutorialGateHint : GateHintText(target, g.def.door, g.def.warp);
                bool inside = g.def.door ? g.def.DoorReached(head, g.position) : d < g.def.radius;
                if (inside && _gateCooldown <= 0f && worm.State != InchwormController.Mode.Hang && !ui.AnyOverlayOpen)
                {
                    TravelTo(target.Id);
                    break;
                }
            }
            ui.GateHint = hint;
        }

        /// <summary>トンネルの近くのヒント。行ったことがあれば、行き先で集めた数も出す。</summary>
        public static string GateHintText(AreaLayout target, bool door = false, bool warp = false)
        {
            // まだ行ったことがない場所は、名前を出さない
            if (!SaveSystem.Data.visited.Contains(target.Id))
                return door ? (target.Id == "hut" ? "とびらのすき間から、まだ入ったことがない場所へ" : "とびらを出ると、まだ行ったことがない場所へ")
                    : warp ? "光の輪をくぐると、まだ行ったことがない場所へ" : "トンネルを抜けると、まだ行ったことがない場所へ";
            string text = door ? (target.Id == "hut" ? $"とびらのすき間から「{target.Subtitle}」の中へ" : $"とびらを出ると「{target.Subtitle}」へ")
                : warp ? $"光の輪をくぐると「{target.Subtitle}」へ" : $"トンネルを抜けると「{target.Subtitle}」へ";
            if (SaveSystem.Data.completedAreas.Contains(target.Id)) return text + "（★ めぐり終えた）";
            return text + $"（{target.DropName} {Collectibles.CollectedIn(target)} / {target.DropCount}）";
        }

        /// <summary>別のエリアへ移動する（トンネル・地図から）。</summary>
        public bool TravelTo(string areaId)
        {
            // 練習の森のトンネルは、チュートリアルのおわり（本番の森へ）
            if (InTutorial && (State == GameState.Playing || State == GameState.Paused))
            {
                FinishTutorial();
                return true;
            }
            var to = Areas.Get(areaId);
            if (to == null || to.Id != areaId || to == Areas.Current) return false;
            if (State != GameState.Playing && State != GameState.Paused) return false;
            StartCoroutine(Travel(to));
            return true;
        }

        /// <summary>
        /// 見つけた名所へ移動する（図鑑から）。いまのエリアならファストトラベル、
        /// 行ったことのあるほかのエリアなら、そのエリアへ移動して、名所の景色から始める。
        /// </summary>
        public bool TravelToLandmark(int landmarkId)
        {
            if (State != GameState.Playing && State != GameState.Paused) return false;
            if (!SaveSystem.Data.places.Contains(landmarkId)) return false;
            AreaLayout area = null;
            LandmarkDef lm = null;
            foreach (var a in Areas.All)
            {
                lm = a.Landmarks.Find(l => l.id == landmarkId);
                if (lm != null) { area = a; break; }
            }
            if (lm == null) return false;
            if (area != Areas.Current && !SaveSystem.Data.visited.Contains(area.Id)) return false;
            if (State == GameState.Paused) Resume();
            ui.CloseAllOverlays();
            if (area == Areas.Current) return FastTravel(landmarkId);
            StartCoroutine(Travel(area, lm));
            return true;
        }

        IEnumerator Travel(AreaLayout to, LandmarkDef target = null)
        {
            var from = Areas.Current;
            State = GameState.Traveling;
            Time.timeScale = 1f;
            worm.InputEnabled = false;
            collectibles.Active = false;
            if (creatures != null) creatures.Active = false;
            ui.GateHint = null;
            AudioManager.Instance?.Travel();
            ui.Fade(true);
            yield return new WaitForSecondsRealtime(0.7f);
            ui.CloseAllOverlays();
            ui.LoadingAreaId = to.Id;   // 読み込み画面に、行き先のエリアのヒント
            ui.ShowLoading();
            worm.enabled = false;
            yield return BuildArea(to, ui.SetLoading);

            bool viaGate = to.ArrivalFrom(from.Id, out Vector2 xz, out Vector3 fwd);
            var view = to.ArrivalViewFrom(from.Id);
            // つながるトンネルのないエリアへ地図から行くときは、入り口の名所の景色から始める
            if (!viaGate && to.EntryLandmark != null) view = to.EntryLandmark.TravelView;
            Vector3 arrive = world.TopSurface(xz);
            if (target != null)
            {
                // 図鑑から名所へ：トンネルではなく、その名所の景色から始める
                arrive = world.ArrivalPoint(target, out fwd);
                view = target.TravelView;
            }
            else if (view != null && world.ViewPoint(view, out var vp, out var vf)) { arrive = vp; fwd = vf; }
            worm.enabled = true;
            worm.Spawn(arrive, fwd);
            MakeRoomForWorm();
            followCamera.titleMode = false;
            followCamera.pitch = 20f;
            ShowArrivalView(view, fwd);
            SaveSystem.Data.area = to.Id;
            SaveProgress();
            _gateCooldown = 2.5f;

            ui.HideLoading();
            yield return new WaitForSecondsRealtime(0.2f);
            ui.Fade(false);
            State = GameState.Playing;
            worm.InputEnabled = true;
            collectibles.Active = true;
            if (creatures != null) creatures.Active = true;
            ui.ShowAreaBanner(to);
            if (target != null) ui.Toast($"「{target.name}」へ移動しました", "icon-place");
            AudioManager.Instance?.Discover();
            worm.Survey(1f);   // 新しいエリアに着いたら、まわりを見わたす
            AnnounceRare();
            CheckProgress();
        }

        public bool IsTraveling => State == GameState.Traveling;

        // ------------------------------------------------------------------
        // メニュー
        // ------------------------------------------------------------------
        public void Pause()
        {
            if (State != GameState.Playing) return;
            State = GameState.Paused;
            worm.InputEnabled = false;
            ui.SetPhotoMode(false);
            if (!ui.IsPauseOpen) ui.ShowPause(true);
            Time.timeScale = 0f;
            SaveProgress();
        }

        public void Resume()
        {
            ui.ShowPause(false);
            Time.timeScale = 1f;
            if (State == GameState.Paused) State = GameState.Playing;
            SaveSystem.SaveSettings();
        }

        /// <summary>写真モード（表示を消して景色をながめる）を切りかえる。メニューからも呼ばれる。</summary>
        public void TogglePhoto()
        {
            if (State == GameState.Paused) Resume();
            if (State != GameState.Playing) return;
            bool on = !ui.IsPhotoMode;
            if (on && ui.AnyOverlayOpen) ui.CloseAllOverlays();
            ui.SetPhotoMode(on);
            worm.InputEnabled = !on;
            AudioManager.Instance?.Click();
        }

        /// <summary>タイトルのひみつの操作：ファストトラベルを、ぜんぶ使えるようにする（どのエリアへも・どの名所へも、地図から行ける）。</summary>
        void OnTitleSecret()
        {
            _travelAll = true;
            int opened = TitleSecret.UnlockAll();
            collectibles.ApplySave();
            ui.RefreshCounts(true);
            ui.RefreshTitleProgress();
            SaveSystem.Save();
            AudioManager.Instance?.Unlock();
            ui.Toast(opened > 0 ? "すべての場所と名所へ、地図から行けるようになりました" : "すべての場所と名所へ、もう地図から行けます", "icon-place", 3.5f);
        }

        public void GoToTitle()
        {
            SaveProgress();
            if (InTutorial)
            {
                // チュートリアルのとちゅうでタイトルへ：練習の記録はすてる（つぎは、はじめから）
                _tutorial.Stop();
                SaveSystem.EndPractice(keepCreatures: false);
            }
            Time.timeScale = 1f;
            ui.CloseAllOverlays();
            ui.ShowTitle(true);
            _demo?.Show(true);
            followCamera.titleMode = true;
            worm.InputEnabled = false;
            collectibles.Active = false;
            if (creatures != null) creatures.Active = false;
            State = GameState.Title;
        }

        public void SaveProgress()
        {
            var d = SaveSystem.Data;
            d.area = Areas.Current.Id;
            // 動く葉っぱの舟の上では位置を残さない（読み込んだとき舟がいないため）
            if (!worm.OnMovingPlatform)
            {
                worm.GetSaveState(out var t, out var tn, out var h, out var hn);
                d.tail = t;
                d.tailNormal = tn;
                d.head = h;
                d.headNormal = hn;
                d.hasPosition = true;
            }
            SaveSystem.Save();
        }

        void OnApplicationPause(bool pause)
        {
            if (pause && State == GameState.Playing) SaveProgress();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && State == GameState.Playing && SaveSystem.Settings.autosave) SaveProgress();
        }

        /// <summary>ブラウザのタブが隠れたとき（jslib から SendMessage で呼ばれる）：保存して音を止める。</summary>
        public void OnPageHidden()
        {
            if (State == GameState.Playing || State == GameState.Paused) SaveProgress();
            SaveSystem.FlushSettings(true);
            // もどってきたとき、いきなり動いていないようにメニューを開いておく
            if (State == GameState.Playing && !ui.IsPhotoMode) Pause();
            AudioListener.pause = true;
        }

        public void OnPageVisible()
        {
            AudioListener.pause = false;
        }

        // ------------------------------------------------------------------
        // 落下・きろく・救済
        // ------------------------------------------------------------------
        /// <summary>しゃくとりむしが乗っている渡し舟（乗っていなければ null）。</summary>
        RiverFerry PlatformFerry()
        {
            var pf = worm != null ? worm.PlatformUnder : null;
            return pf != null ? pf.GetComponent<RiverFerry>() : null;
        }

        void OnHitGround(float speed)
        {
            SaveSystem.Data.falls++;
            // 衝撃の強さはエネルギー（速さの 2 乗）と、落ちた所のやわらかさで決まる
            float k = worm.LastImpact;
            PlatformFerry()?.Push(worm.CenterPosition, 0.5f + 2f * k);
            collectibles.Impulse(worm.CenterPosition, 1f + 4f * k);
            // 音は、落ちた所の材質でちがう（石は高く、葉っぱはこもって小さく）
            // 苔の丘・目覚めの苔原では、ふわっと着地（土けむりが出ない・音もやわらか）
            float soft = Mathf.Max(worm.LastImpactSoftness, AreaProps.SoftGround(worm.TailPoint));
            AudioManager.Instance?.Thud(0.15f + 0.85f * k, soft > 0.5f && worm.LastImpactMaterial == "ground" ? "leaf" : worm.LastImpactMaterial);
            fx.Burst(worm.CenterPosition, 6 + ShakuMath.RoundToIntSafe(18f * k));
            // 土けむりは、面にそって広がる（やわらかい葉っぱや苔の上では、ほとんど出ない）
            fx.Dust(worm.TailPoint, worm.LastImpactNormal, k, soft);
            if (worm.LastImpactMaterial == "leaf") fx.Burst(worm.TailPoint, 6 + ShakuMath.RoundToIntSafe(10f * k), new Color(0.55f, 0.78f, 0.32f, 1f));   // 葉っぱに、ぽふ
            followCamera.Dip(k);
            if (creatures != null) creatures.Disturb(worm.CenterPosition, 2f + 5f * k);   // 落ちた音に、近くのいきものがおどろく
            followCamera.Shake(0.25f + 0.6f * k, -worm.LastImpactNormal);   // ぶつかった向きにゆれる（壁なら横、床なら下）
            if (k > 0.6f && SaveSystem.Settings.vibration)
            {
                ShakuVibrate(40);
                Rumble(0.6f * k, 0.18f);
            }
        }

        /// <summary>ゲームパッドをふるわせる（設定で「振動」が入っているときだけ）。</summary>
        /// <summary>一歩ごとに、ゲームパッドをごく弱く、頭（右）と尾（左）で交互にふるわせる。</summary>
        void StepRumble(bool head)
        {
            if (!SaveSystem.Settings.vibration || Gamepad.current == null || !GameInput.UsingGamepad || Time.unscaledTime < _rumbleUntil) return;
            _rumbleHead = head;
            Gamepad.current.SetMotorSpeeds(head ? 0f : 0.07f, head ? 0.06f : 0f);
            _rumbleUntil = Time.unscaledTime + 0.05f;
        }

        /// <summary>一歩ごとの、足もとの小さなけむり：砂・雪はさらさら、苔は胞子、花のそばでは花粉。</summary>
        void StepPuff(Vector3 p, bool head, AudioManager.Surface surface)
        {
            Vector3 n = worm.SurfaceUp;
            switch (surface)
            {
                case AudioManager.Surface.Sand:
                    if (Areas.Current == Areas.Xmas) { if (head) fx.Burst(p + n * 0.04f, 2, new Color(0.9f, 0.94f, 1f, 1f)); }   // 雪が、ふわっと
                    else fx.Dust(p, n, 0.02f, 0.2f);
                    return;
                case AudioManager.Surface.Moss:
                    if (head) fx.Burst(p + n * 0.04f, 2, new Color(0.62f, 0.8f, 0.36f, 1f));
                    return;
            }
            if (!head || world == null) return;
            foreach (var f in world.FlowerPoints)
            {
                if ((f - p).sqrMagnitude > 0.6f * 0.6f) continue;
                fx.Burst(p + n * 0.08f, 3, new Color(1f, 0.9f, 0.35f, 1f));   // 花粉
                return;
            }
        }

        /// <summary>タッチで地面をタップ：そこまで歩いて行く。</summary>
        void WalkToScreenPoint(Vector2 screen)
        {
            var cam = followCamera != null ? followCamera.GetComponent<Camera>() : Camera.main;
            if (cam == null || State != GameState.Playing || !worm.InputEnabled) return;
            var ray = cam.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 60f, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore)) return;
            if (Areas.Current.IsUnderwater(hit.point) || hit.normal.y < 0.4f) return;
            _autoWalk.Begin(hit.point, 0.5f);
            fx.Burst(hit.point + hit.normal * 0.05f, 6, new Color(0.8f, 1f, 0.9f, 1f));   // 行き先に、小さな光
        }

        void Rumble(float strength, float seconds)
        {
            if (!SaveSystem.Settings.vibration || Gamepad.current == null) return;
            Gamepad.current.SetMotorSpeeds(strength * 0.6f, strength);
            _rumbleUntil = Time.unscaledTime + seconds;
        }

        /// <summary>足もとの物から、足音の種類を決める。</summary>
        AudioManager.Surface SurfaceAt(Vector3 p)
        {
            Vector3 up = worm.SurfaceUp;
            if (Physics.Raycast(p + up * 0.15f, -up, out var hit, 0.45f, SurfaceProbe.Mask, QueryTriggerInteraction.Ignore))
                return AudioManager.Classify(hit.collider.name, hit.collider.gameObject.layer);
            return AudioManager.Surface.Ground;
        }

        /// <summary>めずらしいいきものがこのエリアにいれば、それとなく知らせる。</summary>
        void AnnounceRare()
        {
            if (creatures != null && creatures.RareCount > 0)
                ui.Toast("どこかで、めずらしいいきものの気配がする…", "icon-book", 4f);
        }

        /// <summary>
        /// 画質が「自動」のときは、重さに合わせて画面の細かさだけを少しずつ変える（1.5 秒ごと）。
        /// 見た目の細かさだけで、ゲームの計算には何も影響しない。
        /// </summary>
        void UpdateRenderScale()
        {
            float ms = Mathf.Clamp(Time.unscaledDeltaTime * 1000f, 1f, 200f);
            _frameMsAvg = Mathf.Lerp(_frameMsAvg, ms, 0.1f);
            _scaleTimer += Time.unscaledDeltaTime;
            if (_scaleTimer < 1.5f) return;
            _scaleTimer = 0f;
            if (SaveSystem.Settings.quality >= 0 || _playingFor < 3f) return;
            StepRenderScale(_frameMsAvg);
        }

        /// <summary>
        /// 1 フレームの時間（ミリ秒）から、画面の細かさを 1 段だけ変える。
        /// 45 fps を下まわると下げ、55 fps 以上なら、もとの細かさまで戻す。
        /// </summary>
        public void StepRenderScale(float frameMs)
        {
            float next = _scaleNow;
            if (frameMs > 22f) next = Mathf.Max(MinRenderScale, _scaleNow - 0.1f);
            else if (frameMs < 18f) next = Mathf.Min(_scaleBase, _scaleNow + 0.1f);
            if (Mathf.Abs(next - _scaleNow) < 1e-4f) return;
            _scaleNow = next;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = next;
        }

        /// <summary>
        /// 一度だけのヒント・川の水音・重いときの画質調整（0.5 秒ごと）。
        /// </summary>
        void UpdateAssists()
        {
            // フレームレートの平均（重さの目安）
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _fpsAvg = Mathf.Lerp(_fpsAvg, 1f / dt, 0.05f);

            _assistTimer -= Time.deltaTime;
            if (_assistTimer > 0f) return;
            _assistTimer = 0.5f;

            // 川辺では、川に近いほど水の音を大きく
            Vector3 c = worm.CenterPosition;
            float near = 0f;
            if (Areas.Current == Areas.River)
            {
                near = 1f - Mathf.Clamp01((RiverLayout.DistToRiver(c.x, c.z) - RiverLayout.HalfWidth(c.z)) / 14f);
                // 滝の上の台地では、川の音が少し小さい（せせらぎが、ゆるやか）
                near *= Mathf.Lerp(1f, 0.6f, ShakuMath.SmoothStep(RiverLayout.FallZ + 3f, RiverLayout.FallZ + 10f, c.z));
            }
            AudioManager.Instance?.SetWaterNearness(near);

            if (!Assists) return;

            // 自動の画質で重いときは、一度だけ軽くする
            if (SaveSystem.Settings.quality < 0 && !_autoLowered && _playingFor > 8f)
            {
                _slowTime = _fpsAvg < 22f ? _slowTime + 0.5f : Mathf.Max(0f, _slowTime - 0.5f);
                if (_slowTime > 10f)
                {
                    _autoLowered = true;
                    ApplyQuality(0);
                    ui.Toast("動きが重いので、画質を「かるい」にしました（メニューで変えられます）", "icon-menu", 4f);
                }
            }

            // 一度だけのヒント（はじめてそうなったとき。チュートリアルの練習の森では、カードが教える）
            if (InTutorial) return;
            if (_playingFor < 12f || Time.unscaledTime - _lastTip < 25f || ui.AnyOverlayOpen) return;
            bool shown = false;
            if (worm.OnSteepSurface)
                shown = ui.Tip("wall", "壁にはりついているときに糸のボタンを押すと、はなれて落ちられます");
            else if (worm.State == InchwormController.Mode.Hang)
                shown = ui.Tip("hang", "糸を長押しするとのぼれます。「はやく」で早くおりられます");
            else if (worm.IsBlocked && worm.CanDropSilk)
                shown = ui.Tip("cliff", "がけの上では、糸を出してぶら下がればおりられます");
            else if (!string.IsNullOrEmpty(ui.GateHint))
                shown = ui.Tip("tunnel", "トンネルやとびらをくぐると、となりの場所へ行けます");
            else if (creatures != null && creatures.UndiscoveredNear(c, 12f, _tipNear, 1) > 0)
                shown = ui.Tip("creature", "ミニマップの「？」は、まだ図鑑にのっていないいきもの。近づいてみよう");
            else if (collectibles.DiscoveredPlaces >= 2)
                shown = ui.Tip("map", "地図を開くと、見つけた名所へ「ここへ」ですぐ移動できます");
            else if (_playingFor > 90f)
                shown = ui.Tip("aim", "「ねらう」で好きな場所に糸を飛ばして、たぐり寄せられます");
            if (shown) _lastTip = Time.unscaledTime;
        }

        readonly System.Collections.Generic.List<Vector3> _tipNear = new System.Collections.Generic.List<Vector3>();

        /// <summary>落ちて、キノコなどの上ではね返った。</summary>
        void OnBounced(float rebound)
        {
            float k = worm.LastImpact;
            AudioManager.Instance?.Thud(0.3f + 0.5f * k, worm.LastImpactMaterial);   // キノコは、ぽよんと高い音
            fx.Burst(worm.CenterPosition, 4 + ShakuMath.RoundToIntSafe(10f * k));
            if (creatures != null) creatures.Disturb(worm.CenterPosition, 1.5f + 3f * k);
            followCamera.Shake(0.15f + 0.35f * k);
        }

        void OnSplash()
        {
            // 勢いよく落ちるほど、大きな水しぶき（音も大きく、粒が高くとぶ）
            float k = worm.LastImpact;
            AudioManager.Instance?.Splash(k);
            float wl = Areas.Current.WaterLevelAt(worm.CenterPosition.x, worm.CenterPosition.z);
            fx.Splash(worm.CenterPosition, k, wl > -100f ? wl : worm.CenterPosition.y);
            fx.Burst(worm.CenterPosition, 24 + ShakuMath.RoundToIntSafe(32f * k));
            if (creatures != null) creatures.Disturb(worm.CenterPosition, 3f + 4f * k);
            ui.Toast("ぽちゃん！ 岸まで泳ごう", "icon-drop");
        }

        void UpdateStats()
        {
            Vector3 h = worm.HeadPosition;
            float above = h.y - Areas.Current.Height(h.x, h.z);
            if (above > SaveSystem.Data.highest && above < 200f) SaveSystem.Data.highest = above;
        }

        /// <summary>前に進もうとしても長く動けないときは、救済方法を教える。</summary>
        void UpdateStuck()
        {
            bool pushing = GameInput.Move.sqrMagnitude > 0.25f && !ui.AnyOverlayOpen;
            _stuckTime = pushing && worm.IsBlocked ? _stuckTime + Time.deltaTime : 0f;
            if (_stuckTime > 6f && Time.time - _lastStuckHint > 60f)
            {
                _lastStuckHint = Time.time;
                ui.Toast("動けないときは、メニューの「動けなくなったら」でもどれます", "icon-menu");
            }
        }

        /// <summary>動けなくなったとき：最後に安全だった場所へ。そこもだめならエリアのはじまりの場所へ。</summary>
        public void Rescue()
        {
            if (State != GameState.Playing && State != GameState.Paused) return;
            Resume();
            var area = Areas.Current;
            worm.ReturnToSafety();
            Vector3 p = worm.TailPoint;
            if (!area.InPlayArea(p) || area.IsUnderwater(p) || p.y < area.Height(p.x, p.z) - 1f)
                worm.Spawn(world.SpawnPoint, world.SpawnForward);
            followCamera.Recenter();
            followCamera.SnapToTarget();
            ui.Toast("安全な場所にもどりました", "icon-place");
            SaveProgress();
        }

        /// <summary>
        /// 着いた所（画面が暗いあいだ）：体のすぐそばにいる、地面を歩くいきものを少しずらす。
        /// いきものの上に着くと、着いたとたんに横へよけて、景色から横を向いてしまう。
        /// </summary>
        void MakeRoomForWorm()
        {
            if (creatures != null) creatures.MakeRoom(worm.TailPoint, worm.HeadPoint, 0.8f);
        }

        /// <summary>着いたときの景色をカメラで写す（景色がなければ、いつもどおりうしろから）。</summary>
        void ShowArrivalView(ArrivalView view, Vector3 fwd)
        {
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (view != null) followCamera.ShowView(yaw, view.pitch, view.distance);
            else followCamera.SnapBehind(yaw);   // 着いた向き（体の向きは、まだ前のエリアのままのことがある）
        }

        /// <summary>地図から、見つけた名所へ移動する。</summary>
        public bool FastTravel(int landmarkId)
        {
            if (State != GameState.Playing) return false;
            var lm = Areas.Current.Landmarks.Find(l => l.id == landmarkId);
            if (lm == null || !collectibles.IsDiscovered(landmarkId)) return false;
            StartCoroutine(FastTravelRoutine(lm));
            return true;
        }

        IEnumerator FastTravelRoutine(LandmarkDef lm)
        {
            State = GameState.Traveling;
            worm.InputEnabled = false;
            AudioManager.Instance?.Travel();
            ui.Fade(true);
            yield return new WaitForSecondsRealtime(0.55f);
            // 名所のまん中が水の中（滝つぼ・水たまり）でも、そばのかわいた陸地に着く
            Vector3 p = world.ArrivalPoint(lm, out var fwd);
            worm.Spawn(p, fwd);
            MakeRoomForWorm();
            ShowArrivalView(lm.TravelView, fwd);
            yield return new WaitForSecondsRealtime(0.15f);
            ui.Fade(false);
            State = GameState.Playing;
            worm.InputEnabled = true;
            ui.Toast($"「{lm.name}」へ移動しました", "icon-place");
            SaveProgress();
        }

        // ------------------------------------------------------------------
        // 集める・見つける
        // ------------------------------------------------------------------
        void OnDrop(int count, Vector3 pos)
        {
            // つづけて取ると「れんぞく」
            _combo = Time.time - _lastDropTime < 4f ? _combo + 1 : 1;
            _lastDropTime = Time.time;
            AudioManager.Instance?.Collect(count + _combo - 1);
            fx.Burst(pos, 40, AreaAtmosphere.DropTint(collectibles.Area.Id));   // エリアの色がまじる
            // 続けて取ると、もっと大きくよろこぶ（2 つ目は、ぴょん。3 つ目からは、くるりと見まわして、ふりふり）
            worm.Cheer(_combo >= 3 ? 1.4f : 1f, _combo >= 3 ? 2 : _combo == 2 ? 1 : 0);
            string text = $"{collectibles.Area.DropName}  {count} / {collectibles.TotalDrops}";
            if (_combo >= 3) text += $"　れんぞく ×{_combo}！";
            ui.Toast(text);
            if (count == collectibles.TotalDrops)
            {
                // このエリアのしずくをぜんぶ集めた
                fx.Burst(worm.CenterPosition + Vector3.up * 0.5f, 120);
                ui.Toast($"この{collectibles.Area.DisplayName}の{collectibles.Area.DropName}を、ぜんぶ集めた！", "icon-drop", 4f);
                AudioManager.Instance?.Unlock();
            }
            ui.RefreshCounts();
            CheckProgress();
        }

        void OnLandmark(LandmarkDef lm)
        {
            AudioManager.Instance?.Discover();
            ui.ShowBanner(lm);
            fx.Burst(worm.HeadPosition + Vector3.up * 0.4f, 50, AreaAtmosphere.DiscoverTint(collectibles.Area.Id));   // エリアの色のきらめき
            worm.Survey(0.3f);   // 名所を見つけると、背伸びして見わたす
            if (collectibles.DiscoveredPlaces == collectibles.TotalPlaces)
                ui.Toast($"{collectibles.Area.DisplayName}の名所を、ぜんぶ見つけた！", "icon-place", 4f);
            CheckProgress();
        }

        void OnCreature(SpeciesDef sp)
        {
            if (sp.IsRare) AudioManager.Instance?.Rare();
            else AudioManager.Instance?.Creature();
            if (SaveSystem.Settings.vibration) ShakuVibrate(sp.IsRare ? 120 : 30);
            if (sp.IsRare) Rumble(0.5f, 0.3f);
            worm.Admire();
            if (sp.IsRare) worm.Cheer(1.6f);   // めずらしいいきものには、とても大きくよろこぶ
            ui.ShowCreature(sp);
            // 図鑑がうまった（レアはべつ）
            var tips = SaveSystem.Data.tipsShown;
            if (!sp.IsRare && Creatures.DiscoveredCount >= SpeciesCatalog.Count && !tips.Contains("zukan-complete"))
            {
                tips.Add("zukan-complete");
                AudioManager.Instance?.Complete();
                fx.Burst(worm.CenterPosition + Vector3.up * 0.5f, 150);
                ui.Toast($"いきもの図鑑の {SpeciesCatalog.Count} 種が、ぜんぶうまった！", "icon-book", 5f);
            }
            SaveProgress();
            CheckProgress();
        }

        // ------------------------------------------------------------------
        // もう一度会う
        // ------------------------------------------------------------------
        void OnGreeted(SpeciesDef sp, Vector3 pos, int level, bool up)
        {
            if (sp.kind == MobKind.Bird)
            {
                if (sp.id == "crow" || sp.id == "flamingo") AudioManager.Instance?.Caw(0.35f);
                else AudioManager.Instance?.Chirp(0.5f);
            }
            else if (sp.id == "frog") AudioManager.Instance?.Croak(0.5f);
            else AudioManager.Instance?.Greet();
            fx.Burst(pos + Vector3.up * 0.35f, 8, new Color(1f, 0.62f, 0.78f, 1f));
            worm.LookAt(pos, 1.2f);
            worm.Bow();   // あいさつを返して、ぺこり
            ui.Tip("friend-first", "見つけたいきものに、しずかに近づくと、あいさつしてくれる。会うたびに、なかよしになれるよ（図鑑で見られる）");
            if (!up) return;
            AudioManager.Instance?.Friend();
            string extra = level == Friends.LikesAt ? "　すきなものが、わかった"
                : level == Friends.GuideAt && (sp.kind == MobKind.Flutter || sp.kind == MobKind.Hover) ? "　しずくのある方へ、あんないしてくれるように"
                : level == Friends.SecretAt ? "　ひみつが、わかった"
                : level == Friends.SignatureAt ? "　とっておきのしぐさを見せてくれた！" : "";
            ui.Toast($"{sp.name}と、なかよし {Friends.Stars(level)}{extra}", "icon-book", 3.4f);
            SaveProgress();
            CheckProgress();
        }

        // しぐさの観察のおしらせは、まとめて出す（つづけて見つけても、何度も出ない）
        readonly List<(SpeciesDef sp, Behavior b)> _observed = new List<(SpeciesDef, Behavior)>();
        readonly List<SpeciesDef> _observedAll = new List<SpeciesDef>();
        float _observedFlushAt = -1f, _observedFirstAt;
        /// <summary>最後に観察してから、この秒数のあいだ何も見つけなければ、まとめて知らせる（いちばん長くても ObserveDigestMax 秒で）。</summary>
        public const float ObserveDigestDelay = 2.5f, ObserveDigestMax = 6f;

        void OnObserved(SpeciesDef sp, Behavior b, Vector3 pos)
        {
            // その場では、いきものの上に小さなスタンプの光と音。図鑑ボタンに数が出る
            AudioManager.Instance?.Stamp();
            fx.Burst(pos + Vector3.up * 0.35f, 6, new Color(1f, 0.86f, 0.5f, 1f));
            ui.AddBookBadge(1);
            if (_observed.Count == 0) _observedFirstAt = Time.unscaledTime;
            _observed.Add((sp, b));
            if (Friends.FullyObserved(sp.id))
            {
                AudioManager.Instance?.Unlock();
                fx.Burst(pos + Vector3.up * 0.4f, 40, new Color(1f, 0.85f, 0.35f, 1f));
                _observedAll.Add(sp);
            }
            _observedFlushAt = Mathf.Min(Time.unscaledTime + ObserveDigestDelay, _observedFirstAt + ObserveDigestMax);
        }

        /// <summary>出した観察のおしらせの数と、さいごの文（テスト用）。</summary>
        public int ObserveNotices { get; private set; }
        public string LastObserveNotice { get; private set; } = "";

        /// <summary>しぐさを観察したことにする（テスト用）。</summary>
        public void ObservedForTests(SpeciesDef sp, Behavior b, Vector3 pos) => OnObserved(sp, b, pos);

        /// <summary>まとめておいた観察を、1 つのおしらせにする（設定で消せる）。</summary>
        void FlushObserved()
        {
            if (_observed.Count == 0 || Time.unscaledTime < _observedFlushAt) return;
            // 保存も、まとめて 1 回（WebGL では、保存のたびにブラウザの記録に書きこむので、つづけて何度も書かない）
            SaveProgress();
            CheckProgress();
            if (SaveSystem.Settings.observeNotice)
            {
                LastObserveNotice = ObserveDigest(_observed, _observedAll);
                ObserveNotices++;
                ui.Toast(LastObserveNotice, "icon-book", _observedAll.Count > 0 ? 4f : 3.2f);
            }
            _observed.Clear();
            _observedAll.Clear();
            _observedFlushAt = -1f;
        }

        /// <summary>
        /// 観察のおしらせの文。1 つなら「かんさつ：アリが「行列」（2 / 3）」、いくつかなら「かんさつ +3：アリ「行列」・スズメ「砂あび」ほか 1」。
        /// ぜんぶ観察した種があれば、うしろに足す。
        /// </summary>
        public static string ObserveDigest(IList<(SpeciesDef sp, Behavior b)> seen, IList<SpeciesDef> completed)
        {
            string text;
            if (seen.Count == 1)
            {
                var (sp, b) = seen[0];
                text = $"かんさつ：{sp.name}が「{b.label}」（{Friends.SeenCount(sp.id)} / {Friends.BehaviorsOf(sp.id).Length}）";
            }
            else
            {
                var parts = new List<string>();
                for (int i = 0; i < Mathf.Min(2, seen.Count); i++) parts.Add($"{seen[i].sp.name}「{seen[i].b.label}」");
                text = $"かんさつ +{seen.Count}：{string.Join("・", parts)}" + (seen.Count > 2 ? $" ほか {seen.Count - 2}" : "");
            }
            if (completed.Count > 0)
            {
                var names = new List<string>();
                foreach (var sp in completed) if (!names.Contains(sp.name)) names.Add(sp.name);
                text += $"　{string.Join("・", names)}のしぐさを、ぜんぶ観察した！";
            }
            return text;
        }

        void OnMeasured(SpeciesDef sp, float mm, Vector3 pos)
        {
            int r = Friends.RecordSize(sp.id, mm);
            if (r == 0) return;
            AudioManager.Instance?.Friend();
            ui.Toast($"{(r > 0 ? "いちばん大きい" : "いちばん小さい")}{sp.name}！ {Friends.FormatSize(mm)}", "icon-book", 3f);
        }

        /// <summary>あんないする先：近くの、まだ取っていないしずく（なければ、まだ見つけていない名所）。</summary>
        Vector3? GuideTarget(Vector3 from)
        {
            if (collectibles.NearestDrop(from, 45f, out var drop)) return drop;
            Vector3? best = null;
            float bd = 60f * 60f;
            foreach (var lm in collectibles.Area.Landmarks)
            {
                if (collectibles.IsDiscovered(lm.id)) continue;
                Vector3 p = collectibles.Area.Ground(lm.position.x, lm.position.y);
                float d = (p - from).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>きせかえの解放・エリアのクリア・ぜんぶのクリアを調べる。</summary>
        void CheckProgress()
        {
            if (InTutorial) return;   // 練習の森で集めた物は、本番の記録にしない（きせかえも、めぐり終えたも）
            var now = ProgressStats.FromSave();
            foreach (var s in Skins.NewlyUnlocked(_stats, now))
            {
                ui.Toast($"きせかえ「{s.name}」が使えるようになった！", "icon-book");
                AudioManager.Instance?.Unlock();
            }
            _stats = now;

            var d = SaveSystem.Data;
            var area = collectibles.Area;
            bool areaDone = !d.completedAreas.Contains(area.Id) && Collectibles.IsAreaComplete(area);
            bool allDone = !d.completed && now.IsComplete;
            if (!areaDone && !allDone) return;
            if (areaDone) d.completedAreas.Add(area.Id);
            if (allDone) d.completed = true;
            SaveProgress();
            StartCoroutine(ShowComplete(area, allDone));
        }

        IEnumerator ShowComplete(AreaLayout area, bool all)
        {
            yield return new WaitForSecondsRealtime(3f);
            AudioManager.Instance?.Complete();
            int min = Mathf.Max(1, ShakuMath.RoundToIntSafe(SaveSystem.Data.playTime / 60f));
            if (all)
            {
                ui.ShowComplete(true, "すべてをめぐり終えました！",
                    $"しずく {Areas.TotalDrops} 個、名所 {Areas.TotalLandmarks} か所、いきもの {SpeciesCatalog.Count} 種を、すべて見つけました。\n" +
                    $"探検した時間：約 {min} 分\n\nごほうびに、きせかえ「にじいろ」が使えるようになりました。");
            }
            else
            {
                string rest = Creatures.DiscoveredCount < SpeciesCatalog.Count || Collectibles.AllCollectedDrops < Areas.TotalDrops
                    ? "\nまだ見ぬ場所や、いきものたちが待っています。"
                    : "";
                ui.ShowComplete(true, area.DisplayName + "をめぐり終えました！",
                    $"{area.DropCount} 個の{area.DropName}と、{area.Landmarks.Count} か所の名所をすべて見つけました。\n探検した時間：約 {min} 分{rest}");
            }
        }

        /// <summary>選んでいるきせかえ（いろ・ぼうし・めがね）をしゃくとりむしに反映（にじいろ・レインボーは毎フレーム色が変わる）。</summary>
        void ApplySkin()
        {
            if (_wormRenderer == null) return;
            var skin = Skins.Worn(WearKind.Color) ?? Skins.Get("wakaba");
            Skins.Apply(_wormRenderer, skin, Time.unscaledTime);
            // ぬれたつや・ほこり・日なた・光るものの光を、きせかえの色にかける
            var tint = worm.BodyTint;
            Skins.ApplyTint(_wormRenderer, tint.value, tint.saturation, tint.rim, tint.rimColor);
            if (_wear != null) _wear.Set(Skins.Worn(WearKind.Hat), Skins.Worn(WearKind.Glasses));
        }

        float _gradeExposure = float.NaN, _gradeContrast, _gradeSaturation, _gradeTemperature;

        /// <summary>エリアごとの、画面ぜんたいの色の調整（もとのポストエフェクトの値に、エリアの分を足す）。</summary>
        void ApplyGrade()
        {
            if (postVolume == null) return;
            var profile = postVolume.profile;   // このボリュームだけのコピー（アセットは書きかえない）
            if (profile == null) return;
            profile.TryGet<ColorAdjustments>(out var ca);
            profile.TryGet<WhiteBalance>(out var wb);
            if (float.IsNaN(_gradeExposure))
            {
                _gradeExposure = ca != null ? ca.postExposure.value : 0f;
                _gradeContrast = ca != null ? ca.contrast.value : 0f;
                _gradeSaturation = ca != null ? ca.saturation.value : 0f;
                _gradeTemperature = wb != null ? wb.temperature.value : 0f;
            }
            Vector4 g = AreaAtmosphere.CurrentGrade;
            if (ca != null)
            {
                ca.postExposure.Override(_gradeExposure + g.x);
                ca.contrast.Override(_gradeContrast + g.y);
                ca.saturation.Override(_gradeSaturation + g.z);
            }
            if (wb != null) wb.temperature.Override(_gradeTemperature + g.w);
        }

        /// <summary>
        /// 環境光（空・地平・地面の3色）を球面調和関数に直接書き込む。
        /// ライティングを焼いていない WebGL ビルドでも、影の中が真っ黒にならないようにするため。
        /// </summary>
        public static void ApplyAmbient()
        {
            Color sky = RenderSettings.ambientSkyColor.linear;
            Color eq = RenderSettings.ambientEquatorColor.linear;
            Color gr = RenderSettings.ambientGroundColor.linear;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(eq);
            sh.AddDirectionalLight(Vector3.up, sky - eq, 1f);
            sh.AddDirectionalLight(Vector3.down, gr - eq, 1f);
            RenderSettings.ambientProbe = sh;
        }

        // ------------------------------------------------------------------
        // 画質
        // ------------------------------------------------------------------
        int _qualityNow = 1;

        public void ApplyQuality(int q)
        {
            if (q < 0) q = Application.isMobilePlatform ? 0 : 1;
            _qualityNow = q;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.renderScale = q == 0 ? 0.8f : 1f;
                _scaleBase = _scaleNow = urp.renderScale;
                urp.msaaSampleCount = q == 0 ? 1 : 2;
                urp.shadowDistance = AreaAtmosphere.ShadowDistance(Areas.Current.Id, q);
                // かるい：影は 1 段・小さめの影の絵（影の計算を半分以下に）
                urp.shadowCascadeCount = q == 0 ? 1 : 2;
                urp.mainLightShadowmapResolution = q == 0 ? 1024 : 2048;
            }
            if (world != null && world.instanced != null) world.instanced.distanceScale = q == 0 ? 0.6f : 1f;
            if (world != null && world.loose != null) world.loose.distanceScale = q == 0 ? 0.6f : 1f;
            // かるい：いきものは近くだけ描き、落ち葉は地面との当たり判定をしない
            Creatures.DrawDistance = q == 0 ? 70f : 110f;
            if (fx != null) fx.SetQuality(q);
            var camData = followCamera != null ? followCamera.GetComponent<UniversalAdditionalCameraData>() : null;
            if (camData != null) camData.renderPostProcessing = true;
            if (postVolume != null) postVolume.weight = q == 0 ? 0.85f : 1f;
        }
    }
}

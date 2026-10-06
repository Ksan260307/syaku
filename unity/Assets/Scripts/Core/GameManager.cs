using System;
using System.Collections;
using UnityEngine;
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
        bool _starting;
        ProgressStats _stats;
        Renderer _wormRenderer;

        IEnumerator Start()
        {
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

            var area = SaveSystem.HasSave ? Areas.Get(SaveSystem.Data.area) ?? Areas.Forest : Areas.Forest;
            ui.SetLoading(0f, area.DisplayName + "を準備しています");
            yield return null;

            fx.followTarget = followCamera.transform;
            fx.Build();
            yield return BuildArea(area, (p, t) => ui.SetLoading(p * 0.95f, t));
            PlaceWorm(SaveSystem.HasSave);
            followCamera.target = worm;
            followCamera.SnapToTarget();
            _stats = ProgressStats.FromSave();

            worm.Stepped += (p, head) => AudioManager.Instance?.Step(head);
            worm.SilkStarted += () => AudioManager.Instance?.Silk();
            worm.Landed += () => AudioManager.Instance?.Land();
            collectibles.DropCollected += OnDrop;
            collectibles.LandmarkDiscovered += OnLandmark;
            if (creatures != null) creatures.Discovered += OnCreature;
            ui.ContinuePressed += () => StartGame(true);
            ui.NewGamePressed += () => StartGame(false);
            ui.ResumePressed += Resume;
            ui.ToTitlePressed += GoToTitle;
            ui.ResetPressed += () => StartGame(false);
            ui.QualityChanged += ApplyQuality;
            ui.TravelRequested += id => TravelTo(id);
            ui.SkinSelected += id => ApplySkin();

            ui.SetLoading(1f, "準備ができました");
            yield return new WaitForSecondsRealtime(0.3f);
            ui.HideLoading();
            ui.ShowTitle(true);
            State = GameState.Title;
        }

        /// <summary>エリアを作り直し、しずく・いきもの・演出・UI をそのエリアに合わせる。</summary>
        IEnumerator BuildArea(AreaLayout area, Action<float, string> progress)
        {
            Areas.Current = area;
            if (creatures != null) creatures.Clear();
            yield return world.Generate(area, progress);
            collectibles.Build(world.DewdropPoints, area);
            if (creatures != null) creatures.Build(world);
            fx.BuildArea(world);
            ui.Init(collectibles, worm, followCamera.transform, world.MapTexture, world.assets);
            AudioManager.Instance?.SetArea(area.Id);
            if (!SaveSystem.Data.visited.Contains(area.Id)) SaveSystem.Data.visited.Add(area.Id);
            AreaChanged?.Invoke(area);
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

        /// <summary>「つづきから」(true) /「はじめる」(false)。</summary>
        public void StartGame(bool continueGame)
        {
            if (State == GameState.Loading || State == GameState.Traveling || _starting) return;
            StartCoroutine(Begin(continueGame));
        }

        IEnumerator Begin(bool continueGame)
        {
            _starting = true;
            AudioManager.Instance?.StartMusic();
            ui.Fade(true);
            yield return new WaitForSecondsRealtime(0.65f);
            Time.timeScale = 1f;
            ui.CloseAllOverlays();
            if (!continueGame)
            {
                SaveSystem.ResetProgress();
                _stats = ProgressStats.FromSave();
                if (Areas.Current != Areas.Forest)
                {
                    // 森から始めなおす
                    ui.ShowLoading("森を準備しています");
                    yield return BuildArea(Areas.Forest, (p, t) => ui.SetLoading(p, t));
                    ui.HideLoading();
                }
                else
                {
                    collectibles.ApplySave();
                    if (!SaveSystem.Data.visited.Contains(Areas.Forest.Id)) SaveSystem.Data.visited.Add(Areas.Forest.Id);
                }
                SaveSystem.Data.area = Areas.Current.Id;
                PlaceWorm(false);
            }
            else PlaceWorm(true);
            ApplySkin();
            ui.RefreshCounts(true);
            ui.ShowTitle(false);
            followCamera.titleMode = false;
            followCamera.pitch = 20f;
            followCamera.distance = 3.1f;
            followCamera.SnapToTarget();
            ui.Fade(false);
            State = GameState.Playing;
            worm.InputEnabled = true;
            collectibles.Active = true;
            if (creatures != null) creatures.Active = true;
            _gateCooldown = 1.5f;
            _starting = false;
            yield return new WaitForSecondsRealtime(1.0f);
            var first = Areas.Current.EntryLandmark;
            if (first != null && !collectibles.IsDiscovered(first.id) && Collectibles.IsInside(first, worm.HeadPosition))
                collectibles.Discover(first);
        }

        void Update()
        {
            bool playing = State == GameState.Playing;
            ui.TickHud(playing && !ui.AnyOverlayOpen);
            switch (State)
            {
                case GameState.Title:
                    if (GameInput.AnyKeyPressed) AudioManager.Instance?.StartMusic();
                    if (GameInput.PausePressed) ui.Back();
                    break;
                case GameState.Playing:
                    SaveSystem.Data.playTime += Time.deltaTime;
                    if (GameInput.PausePressed)
                    {
                        if (!ui.Back()) Pause();
                    }
                    else if (GameInput.MapPressed) ui.ToggleMap();
                    else if (GameInput.CollectionPressed) ui.ToggleCollection();
                    else if (GameInput.HelpPressed) ui.ToggleHelp();
                    if (ui.IsPauseOpen) Pause();
                    worm.InputEnabled = !ui.AnyOverlayOpen;
                    UpdateGates();
                    _saveTimer += Time.deltaTime;
                    if (_saveTimer > 8f)
                    {
                        _saveTimer = 0f;
                        SaveProgress();
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
                if (d < 7f) hint = $"トンネルを抜けると「{target.Subtitle}」へ";
                if (d < g.def.radius && _gateCooldown <= 0f && worm.State != InchwormController.Mode.Hang && !ui.AnyOverlayOpen)
                {
                    TravelTo(target.Id);
                    break;
                }
            }
            ui.GateHint = hint;
        }

        /// <summary>別のエリアへ移動する（トンネル・地図から）。</summary>
        public bool TravelTo(string areaId)
        {
            var to = Areas.Get(areaId);
            if (to == null || to.Id != areaId || to == Areas.Current) return false;
            if (State != GameState.Playing && State != GameState.Paused) return false;
            StartCoroutine(Travel(to));
            return true;
        }

        IEnumerator Travel(AreaLayout to)
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
            ui.ShowLoading(to.Subtitle + "へ向かっています");
            worm.enabled = false;
            yield return BuildArea(to, (p, t) => ui.SetLoading(p, t));

            to.ArrivalFrom(from.Id, out Vector2 xz, out Vector3 fwd);
            worm.enabled = true;
            worm.Spawn(world.TopSurface(xz), fwd);
            followCamera.titleMode = false;
            followCamera.yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            followCamera.pitch = 20f;
            followCamera.SnapToTarget();
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
            AudioManager.Instance?.Discover();
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

        public void GoToTitle()
        {
            SaveProgress();
            Time.timeScale = 1f;
            ui.CloseAllOverlays();
            ui.ShowTitle(true);
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

        // ------------------------------------------------------------------
        // 集める・見つける
        // ------------------------------------------------------------------
        void OnDrop(int count, Vector3 pos)
        {
            AudioManager.Instance?.Collect(count);
            fx.Burst(pos);
            ui.Toast($"{collectibles.Area.DropName}  {count} / {collectibles.TotalDrops}");
            ui.RefreshCounts();
            CheckProgress();
        }

        void OnLandmark(LandmarkDef lm)
        {
            AudioManager.Instance?.Discover();
            ui.ShowBanner(lm);
            CheckProgress();
        }

        void OnCreature(SpeciesDef sp)
        {
            AudioManager.Instance?.Creature();
            ui.ShowCreature(sp);
            CheckProgress();
        }

        /// <summary>きせかえの解放・エリアのクリア・ぜんぶのクリアを調べる。</summary>
        void CheckProgress()
        {
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
            int min = Mathf.Max(1, Mathf.RoundToInt(SaveSystem.Data.playTime / 60f));
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

        /// <summary>選んでいるきせかえの色をしゃくとりむしに反映（にじいろは毎フレーム色が変わる）。</summary>
        void ApplySkin()
        {
            if (_wormRenderer == null) return;
            var skin = Skins.Get(SaveSystem.Data.skin) ?? Skins.Get("wakaba");
            Skins.Apply(_wormRenderer, skin, Time.unscaledTime);
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
        public void ApplyQuality(int q)
        {
            if (q < 0) q = Application.isMobilePlatform ? 0 : 1;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.renderScale = q == 0 ? 0.8f : 1f;
                urp.msaaSampleCount = q == 0 ? 1 : 2;
                urp.shadowDistance = q == 0 ? 28f : 48f;
            }
            if (world != null && world.instanced != null) world.instanced.distanceScale = q == 0 ? 0.6f : 1f;
            var camData = followCamera != null ? followCamera.GetComponent<UniversalAdditionalCameraData>() : null;
            if (camData != null) camData.renderPostProcessing = true;
            if (postVolume != null) postVolume.weight = q == 0 ? 0.85f : 1f;
        }
    }
}

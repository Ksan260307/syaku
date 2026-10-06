using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Shakutori
{
    /// <summary>ゲーム全体の流れ（読み込み → タイトル → 探検 → メニュー）。</summary>
    public class GameManager : MonoBehaviour
    {
        public enum GameState { Loading, Title, Playing, Paused }

        public WorldGenerator world;
        public InchwormController worm;
        public FollowCamera followCamera;
        public GameUI ui;
        public Collectibles collectibles;
        public AmbientFX fx;
        public Volume postVolume;

        public GameState State { get; private set; } = GameState.Loading;
        float _saveTimer;

        IEnumerator Start()
        {
            SaveSystem.Load();
            ApplyAmbient();
            ApplyQuality(SaveSystem.Settings.quality);
            worm.InputEnabled = false;
            collectibles.Active = false;
            followCamera.titleMode = true;
            ui.SetLoading(0f, "森を準備しています");
            yield return null;

            yield return world.Generate((p, t) => ui.SetLoading(p * 0.95f, t));
            collectibles.Build(world.DewdropPoints);
            fx.followTarget = followCamera.transform;
            fx.Build();
            ui.Init(collectibles, worm, followCamera.transform, world.MapTexture);
            PlaceWorm(SaveSystem.HasSave);
            followCamera.target = worm;
            followCamera.SnapToTarget();

            worm.Stepped += (p, head) => AudioManager.Instance?.Step(head);
            worm.SilkStarted += () => AudioManager.Instance?.Silk();
            worm.Landed += () => AudioManager.Instance?.Land();
            collectibles.DropCollected += OnDrop;
            collectibles.LandmarkDiscovered += OnLandmark;
            ui.ContinuePressed += () => StartGame(true);
            ui.NewGamePressed += () => StartGame(false);
            ui.ResumePressed += Resume;
            ui.ToTitlePressed += GoToTitle;
            ui.ResetPressed += () => StartGame(false);
            ui.QualityChanged += ApplyQuality;

            ui.SetLoading(1f, "準備ができました");
            yield return new WaitForSecondsRealtime(0.3f);
            ui.HideLoading();
            ui.ShowTitle(true);
            State = GameState.Title;
        }

        void PlaceWorm(bool fromSave)
        {
            var d = SaveSystem.Data;
            if (fromSave && d.hasPosition && ForestLayout.InPlayArea(d.tail))
                worm.Spawn(d.tail, d.tailNormal, d.head, d.headNormal);
            else
                worm.Spawn(world.SpawnPoint, world.SpawnForward);
        }

        /// <summary>「つづきから」(true) /「はじめる」(false)。</summary>
        public void StartGame(bool continueGame)
        {
            if (State == GameState.Loading || _starting) return;
            StartCoroutine(Begin(continueGame));
        }

        bool _starting;

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
                collectibles.ApplySave();
                PlaceWorm(false);
            }
            else PlaceWorm(true);
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
            _starting = false;
            yield return new WaitForSecondsRealtime(1.0f);
            var first = ForestLayout.Landmarks[0];
            if (!collectibles.IsDiscovered(first.id) && Collectibles.IsInside(first, worm.HeadPosition))
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
                    else if (GameInput.HelpPressed) ui.ToggleHelp();
                    if (ui.IsPauseOpen) Pause();
                    worm.InputEnabled = !ui.AnyOverlayOpen;
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
        }

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
            State = GameState.Title;
        }

        public void SaveProgress()
        {
            worm.GetSaveState(out var t, out var tn, out var h, out var hn);
            var d = SaveSystem.Data;
            d.tail = t;
            d.tailNormal = tn;
            d.head = h;
            d.headNormal = hn;
            d.hasPosition = true;
            SaveSystem.Save();
        }

        void OnApplicationPause(bool pause)
        {
            if (pause && State == GameState.Playing) SaveProgress();
        }

        void OnDrop(int count, Vector3 pos)
        {
            AudioManager.Instance?.Collect(count);
            fx.Burst(pos);
            ui.Toast($"森のしずく  {count} / {collectibles.TotalDrops}");
            ui.RefreshCounts();
            CheckComplete();
        }

        void OnLandmark(LandmarkDef lm)
        {
            AudioManager.Instance?.Discover();
            ui.ShowBanner(lm);
            CheckComplete();
        }

        void CheckComplete()
        {
            var d = SaveSystem.Data;
            if (d.completed) return;
            if (collectibles.CollectedDrops < collectibles.TotalDrops || collectibles.DiscoveredPlaces < collectibles.TotalPlaces) return;
            d.completed = true;
            SaveProgress();
            StartCoroutine(ShowComplete());
        }

        IEnumerator ShowComplete()
        {
            yield return new WaitForSecondsRealtime(3f);
            AudioManager.Instance?.Complete();
            int min = Mathf.Max(1, Mathf.RoundToInt(SaveSystem.Data.playTime / 60f));
            ui.ShowComplete(true, $"{collectibles.TotalDrops} 個の森のしずくと、{collectibles.TotalPlaces} か所の名所をすべて見つけました。\n探検した時間：約 {min} 分\n\n小さな体で、大きな森をめぐる旅でした。");
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

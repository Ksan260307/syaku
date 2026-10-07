using System;
using System.Collections;
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

            worm.Stepped += (p, head) =>
            {
                AudioManager.Instance?.Step(head, SurfaceAt(p), worm.StepLoudness);
                if (head) SaveSystem.Data.steps++;
            };
            ui.CreatureSource = creatures;
            ui.PhotoRequested += TogglePhoto;
            worm.SilkStarted += () =>
            {
                AudioManager.Instance?.Silk();
                SaveSystem.Data.silkUses++;
            };
            worm.Fell += () => AudioManager.Instance?.Fall();
            worm.HitGround += OnHitGround;
            worm.Splashed += OnSplash;
            worm.Landed += () => AudioManager.Instance?.Land();
            collectibles.DropCollected += OnDrop;
            collectibles.LandmarkDiscovered += OnLandmark;
            if (creatures != null)
            {
                creatures.Discovered += OnCreature;
                creatures.DiscoveredAt += (sp, pos) =>
                {
                    if (pos == Vector3.zero) return;
                    fx.Burst(pos + Vector3.up * 0.3f, sp.IsRare ? 80 : 30);
                    worm.LookAt(pos, 1.6f);   // 見つけたいきもののほうを見る
                };
            }
            ui.FastTravelRequested += id => FastTravel(id);
            ui.RescueRequested += Rescue;
            ui.SaveRequested += () =>
            {
                SaveProgress();
                ui.Toast("セーブしました", "icon-book");
            };
            ShakuRegisterPageEvents();
            ui.ContinuePressed += () => StartGame(true);
            ui.NewGamePressed += () => StartGame(false);
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
            worm.ClearSafeHistory();
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
            yield return new WaitForSecondsRealtime(1.0f);
            var first = Areas.Current.EntryLandmark;
            if (first != null && !collectibles.IsDiscovered(first.id) && Collectibles.IsInside(first, worm.HeadPosition))
                collectibles.Discover(first);
        }

        void Update()
        {
            bool playing = State == GameState.Playing;
            ui.TickHud(playing && !ui.AnyOverlayOpen);
            SaveSystem.FlushSettings();
            if (Time.unscaledTime > _rumbleUntil && _rumbleUntil > 0f)
            {
                _rumbleUntil = 0f;
                Gamepad.current?.SetMotorSpeeds(0f, 0f);
            }
            switch (State)
            {
                case GameState.Title:
                    if (GameInput.AnyKeyPressed) AudioManager.Instance?.StartMusic();
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
                if (d < 7f) hint = GateHintText(target);
                if (d < g.def.radius && _gateCooldown <= 0f && worm.State != InchwormController.Mode.Hang && !ui.AnyOverlayOpen)
                {
                    TravelTo(target.Id);
                    break;
                }
            }
            ui.GateHint = hint;
        }

        /// <summary>トンネルの近くのヒント。行ったことがあれば、行き先で集めた数も出す。</summary>
        public static string GateHintText(AreaLayout target)
        {
            string text = $"トンネルを抜けると「{target.Subtitle}」へ";
            if (!SaveSystem.Data.visited.Contains(target.Id)) return text + "（まだ行ったことがない場所）";
            if (SaveSystem.Data.completedAreas.Contains(target.Id)) return text + "（★ めぐり終えた）";
            return text + $"（{target.DropName} {Collectibles.CollectedIn(target)} / {target.DropCount}）";
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
        void OnHitGround(float speed)
        {
            SaveSystem.Data.falls++;
            float k = Mathf.Clamp01(speed / 8f);
            AudioManager.Instance?.Thud(k);
            fx.Burst(worm.CenterPosition, 6 + Mathf.RoundToInt(18f * k));
            if (creatures != null) creatures.Disturb(worm.CenterPosition, 2f + 5f * k);   // 落ちた音に、近くのいきものがおどろく
            followCamera.Shake(0.25f + 0.6f * k);
            if (k > 0.6f && SaveSystem.Settings.vibration)
            {
                ShakuVibrate(40);
                Rumble(0.6f * k, 0.18f);
            }
        }

        /// <summary>ゲームパッドをふるわせる（設定で「振動」が入っているときだけ）。</summary>
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
                near = 1f - Mathf.Clamp01((RiverLayout.DistToRiver(c.x, c.z) - RiverLayout.HalfWidth(c.z)) / 14f);
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

            // 一度だけのヒント（はじめてそうなったとき）
            if (_playingFor < 12f || Time.unscaledTime - _lastTip < 25f || ui.AnyOverlayOpen) return;
            bool shown = false;
            if (worm.OnSteepSurface)
                shown = ui.Tip("wall", "壁にはりついているときに糸のボタンを押すと、はなれて落ちられます");
            else if (worm.State == InchwormController.Mode.Hang)
                shown = ui.Tip("hang", "糸を長押しするとのぼれます。「はやく」で早くおりられます");
            else if (worm.IsBlocked && worm.CanDropSilk)
                shown = ui.Tip("cliff", "がけの上では、糸を出してぶら下がればおりられます");
            else if (!string.IsNullOrEmpty(ui.GateHint))
                shown = ui.Tip("tunnel", "木の根のトンネルを抜けると、別のエリアへ行けます");
            else if (creatures != null && creatures.UndiscoveredNear(c, 12f, _tipNear, 1) > 0)
                shown = ui.Tip("creature", "ミニマップの「？」は、まだ図鑑にのっていないいきもの。近づいてみよう");
            else if (collectibles.DiscoveredPlaces >= 2)
                shown = ui.Tip("map", "地図を開くと、見つけた名所へ「ここへ」ですぐ移動できます");
            else if (_playingFor > 90f)
                shown = ui.Tip("aim", "「ねらう」で好きな場所に糸を飛ばして、たぐり寄せられます");
            if (shown) _lastTip = Time.unscaledTime;
        }

        readonly System.Collections.Generic.List<Vector3> _tipNear = new System.Collections.Generic.List<Vector3>();

        void OnSplash()
        {
            AudioManager.Instance?.Splash();
            fx.Burst(worm.CenterPosition, 40);
            if (creatures != null) creatures.Disturb(worm.CenterPosition, 5f);
            ui.Toast("ぽちゃん！ 水に落ちてしまった…", "icon-drop");
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
            Vector3 p = world.TopSurface(lm.position);
            Vector3 toCenter = new Vector3(-lm.position.x, 0f, -lm.position.y);
            Vector3 fwd = toCenter.sqrMagnitude > 0.01f ? toCenter.normalized : Vector3.forward;
            worm.Spawn(p, fwd);
            followCamera.yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            followCamera.SnapToTarget();
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
            fx.Burst(pos);
            worm.Cheer(_combo >= 3 ? 1.4f : 1f);   // 続けて取ると、もっと大きくよろこぶ
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
            fx.Burst(worm.HeadPosition + Vector3.up * 0.4f, 50);
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

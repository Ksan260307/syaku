using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 3;   // 3：しずくの場所が新しくなった
        public int version = CurrentVersion;
        public List<int> drops = new List<int>();
        public int legacyDrops;                                    // しずくを置きなおす前に集めた数（きせかえは、そのまま使える）
        public List<int> places = new List<int>();
        public float playTime;
        public bool hasPosition;
        public Vector3 tail, tailNormal, head, headNormal;
        public bool completed;                                     // ぜんぶ集めた
        public string area = "forest";                             // いまいるエリア
        public List<string> creatures = new List<string>();        // 見つけたいきもの
        public List<string> seenCreatures = new List<string>();    // 図鑑で見たいきもの（NEW の表示用）
        public List<string> visited = new List<string>();          // 行ったことのあるエリア
        public List<string> completedAreas = new List<string>();   // しずくと名所をぜんぶ見つけたエリア
        public List<string> tipsShown = new List<string>();        // 一度だけ出すヒント
        public List<string> habitats = new List<string>();         // いきもののすみか（図鑑用。"種|エリア|x|z|広さ|名所"）
        public List<CreatureNote> notes = new List<CreatureNote>(); // いきものとのなかよし・観察したしぐさ・大きさの記録
        public string skin = "wakaba";
        public string hat = "";                                    // かぶっているぼうし（"" = なし）
        public string glasses = "";                                // かけているめがね（"" = なし）
        // きろく
        public int steps;
        public int silkUses;
        public int falls;
        public float highest = -999f;
        public int swims;                                          // 水に落ちて泳いだ回数
        public int travels;                                        // 地図・図鑑から名所へ移動した回数
        public int rescues;                                        // 「動けなくなったら」でもどった回数
        public int photos;                                         // 写真モードでとった写真の数
        public string firstDay = "";                               // はじめて遊んだ日（yyyy-MM-dd）
        public string lastDay = "";                                // さいごに遊んだ日
        public int playDays;                                       // 遊んだ日数

        /// <summary>今日も遊んだことを記録する（遊んだ日数・はじめて遊んだ日）。新しい日なら true。</summary>
        public bool MarkPlayDay(DateTime now)
        {
            string today = now.ToString("yyyy-MM-dd");
            if (string.IsNullOrEmpty(firstDay)) firstDay = today;
            if (lastDay == today) return false;
            lastDay = today;
            playDays++;
            return true;
        }
    }

    [Serializable]
    public class SettingsData
    {
        public float sensitivity = 1f;
        public bool invertY;
        public bool invertX;
        public float music = 0.55f;
        public float sfx = 0.8f;
        public float ambience = 0.8f;
        public int quality = -1;   // -1 = 自動, 0 = かるい, 1 = きれい
        public bool showHelp = true;
        public bool autosave = true;
        public int textSize;       // 0 = ふつう, 1 = 大きい
        public bool autoCamera = true;
        public bool minimapRotate;
        public bool reduceMotion;
        public bool vibration = true;
        public float fov = 50f;
        public bool sprintToggle;     // Shift を押すたびに「はやく」を切りかえる
        public bool aimToggle;        // F / 右クリックを押すたびに「ねらう」を切りかえる
        public bool wallStickUp;      // 壁では、カメラの向きに関係なく、スティックの上で登る
        public float cameraDistance;  // 0 = 最初の距離
        public bool observeNotice = true;   // しぐさを観察したことを、画面の上に知らせる（つづけて見つけたときは、まとめて 1 回）
        public float master = 1f;           // 全体の音量
        public float brightness;            // 画面の明るさ（-1〜1。0 = ふつう）
        public int minimapSize = 1;         // ミニマップの大きさ（0 = 小さい, 1 = ふつう, 2 = 大きい）
        public bool captions;               // いきものの声を、字幕で出す
        public bool invertZoom;             // ホイール・ピンチのズームの向きを逆に
        public int touchSize;               // タッチのボタンの大きさ（0 = ふつう, 1 = 大きい）
        public bool leftHanded;             // 左きき：タッチのスティックを右、ボタンを左に
        public bool tapToWalk = true;       // 地面をタップすると、そこまで歩く（タッチ）
        public bool pauseOnBlur = true;     // ほかのアプリを見ているときは、止める
        public string zukanFilter = "";     // 図鑑のしぼりこみ（つぎに開いたときも同じ）
        public bool longNotices;            // おしらせを長めに出す（ゆっくり読める）
        public int newsSeen;                // 「あたらしくなったこと」を見た版
    }

    /// <summary>
    /// 進行状況と設定の保存（WebGL ではブラウザの IndexedDB に保存される）。
    /// 最初に Data / Settings にさわったときに読み込むので、どの順番で呼ばれても保存済みの値が使われる。
    /// </summary>
    public static class SaveSystem
    {
        const string SaveKey = "shakutori.save.v1";
        const string BackupKey = "shakutori.save.v1.bak";
        const string SettingsKey = "shakutori.settings.v1";

        static SaveData _data = new SaveData();
        static SettingsData _settings = new SettingsData();
        static bool _loaded;

        public static SaveData Data
        {
            get { EnsureLoaded(); return _data; }
            private set => _data = value;
        }

        public static SettingsData Settings
        {
            get { EnsureLoaded(); return _settings; }
            private set => _settings = value;
        }

        public static bool HasSave { get; private set; }
        public static bool IsLoaded => _loaded;
        /// <summary>保存したとき（オートセーブの表示用）。</summary>
        public static event Action Saved;
        public static float LastSaveTime { get; private set; } = -999f;

        static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static void Load()
        {
            _loaded = true;
            try
            {
                if (!TryRead(SaveKey, out var data) && !TryRead(BackupKey, out data)) data = null;
                _data = data ?? new SaveData();
                Migrate(_data);
                SanitizeData(_data);
                HasSave = data != null && (_data.hasPosition || _data.drops.Count > 0 || _data.places.Count > 0 || _data.creatures.Count > 0);
                if (PlayerPrefs.HasKey(SettingsKey))
                    _settings = JsonUtility.FromJson<SettingsData>(PlayerPrefs.GetString(SettingsKey)) ?? new SettingsData();
                else _settings = new SettingsData();
                SanitizeSettings(_settings);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 読み込みに失敗しました: " + e.Message);
                _data = new SaveData();
                _settings = new SettingsData();
            }
        }

        static bool TryRead(string key, out SaveData data)
        {
            data = null;
            if (!PlayerPrefs.HasKey(key)) return false;
            try
            {
                data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(key));
                return data != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] {key} が読めませんでした: {e.Message}");
                data = null;
                return false;
            }
        }

        /// <summary>古い形式のセーブデータを今の形式にそろえる。</summary>
        public static void Migrate(SaveData d)
        {
            d.drops ??= new List<int>();
            d.places ??= new List<int>();
            d.creatures ??= new List<string>();
            d.seenCreatures ??= new List<string>();
            d.visited ??= new List<string>();
            d.completedAreas ??= new List<string>();
            d.tipsShown ??= new List<string>();
            d.habitats ??= new List<string>();
            d.notes ??= new List<CreatureNote>();
            d.notes.RemoveAll(n => n == null || string.IsNullOrEmpty(n.id));
            foreach (var n in d.notes)
            {
                n.seen ??= new List<string>();
                n.friend = Mathf.Clamp(n.friend, 0, Friends.MaxFriend);
            }
            if (!float.IsFinite(d.playTime) || d.playTime < 0f) d.playTime = 0f;
            if (!float.IsFinite(d.highest)) d.highest = -999f;
            d.steps = Mathf.Max(0, d.steps);
            d.silkUses = Mathf.Max(0, d.silkUses);
            d.falls = Mathf.Max(0, d.falls);
            // 体の位置がこわれていたら、エリアのはじめの場所から
            if (!ShakuPhysics.IsFinite(d.tail) || !ShakuPhysics.IsFinite(d.head) || !ShakuPhysics.IsFinite(d.tailNormal) || !ShakuPhysics.IsFinite(d.headNormal))
                d.hasPosition = false;
            foreach (var n in d.notes)
            {
                if (!float.IsFinite(n.bigMm) || n.bigMm < 0f) n.bigMm = 0f;
                if (!float.IsFinite(n.smallMm) || n.smallMm < 0f) n.smallMm = 0f;
                if (!float.IsFinite(n.friendAt)) n.friendAt = -999f;
            }
            if (string.IsNullOrEmpty(d.area) || d.area == "tutorial") d.area = "forest";   // 練習の森は、記録しない
            if (string.IsNullOrEmpty(d.skin)) d.skin = "wakaba";
            if (d.hat == null) d.hat = "";
            if (d.glasses == null) d.glasses = "";
            if (d.version < 2)
            {
                // v1 にはなかった：最初のエリアは行ったことがある・図鑑は全部見たことにする
                if (!d.visited.Contains("forest")) d.visited.Add("forest");
                foreach (var c in d.creatures)
                    if (!d.seenCreatures.Contains(c)) d.seenCreatures.Add(c);
                if (d.highest < -900f) d.highest = -999f;
            }
            if (d.version < WorldGenerator.DewLayout && d.drops.Count > 0)
            {
                // v3 で、しずくを探索が楽しくなる所へ置きなおした：前の場所で集めた印は使えないので、集めなおす。
                // 集めた数で使えるようになったきせかえは、そのまま（前の数をおぼえておく）。めぐり終えたエリアも、もう一度
                d.legacyDrops = Mathf.Max(d.legacyDrops, d.drops.Count);
                d.drops.Clear();
                d.completedAreas.Clear();
                DewsMoved = true;
            }
            d.version = SaveData.CurrentVersion;
        }

        /// <summary>読みこんだセーブの、しずくの場所が新しくなった（つづきから始めたときに、一度だけ知らせる）。</summary>
        public static bool DewsMoved;

        /// <summary>
        /// 設定の値がこわれていたり（NaN など）、範囲の外だったりしたら、ふつうの値にもどす（書きかえられた保存データなど）。
        /// そのまま使うと、カメラの見える広さや感度がこわれて、画面が出なくなることがある。
        /// </summary>
        public static void SanitizeSettings(SettingsData s)
        {
            var d = new SettingsData();
            s.sensitivity = Fix(s.sensitivity, 0.3f, 2.5f, d.sensitivity);
            s.music = Fix(s.music, 0f, 1f, d.music);
            s.sfx = Fix(s.sfx, 0f, 1f, d.sfx);
            s.ambience = Fix(s.ambience, 0f, 1f, d.ambience);
            s.fov = Fix(s.fov, 40f, 70f, d.fov);
            s.cameraDistance = float.IsFinite(s.cameraDistance) && s.cameraDistance > 0f ? Mathf.Min(s.cameraDistance, 50f) : 0f;
            if (s.quality < -1 || s.quality > 1) s.quality = -1;
            if (s.textSize < 0 || s.textSize > 1) s.textSize = 0;
            s.master = Fix(s.master, 0f, 1f, d.master);
            s.brightness = Fix(s.brightness, -1f, 1f, d.brightness);
            if (s.minimapSize < 0 || s.minimapSize > 2) s.minimapSize = 1;
            if (s.touchSize < 0 || s.touchSize > 1) s.touchSize = 0;
        }

        static float Fix(float v, float lo, float hi, float def) => float.IsFinite(v) ? Mathf.Clamp(v, lo, hi) : def;

        /// <summary>
        /// 読みこんだ記録をととのえる：同じものが 2 回入っていたら 1 つに、数はマイナスにしない、いまいるエリアが分からなければ森、
        /// しゃくとりむしの場所がこわれていたら（NaN）、場所はわすれて、エリアのはじまりから。
        /// </summary>
        public static void SanitizeData(SaveData d)
        {
            void Dedupe<T>(List<T> list)
            {
                if (list == null) return;
                var seen = new HashSet<T>();
                list.RemoveAll(x => !seen.Add(x));
            }
            d.drops ??= new List<int>();
            d.places ??= new List<int>();
            d.creatures ??= new List<string>();
            d.seenCreatures ??= new List<string>();
            d.visited ??= new List<string>();
            d.completedAreas ??= new List<string>();
            d.tipsShown ??= new List<string>();
            d.habitats ??= new List<string>();
            d.notes ??= new List<CreatureNote>();
            Dedupe(d.drops);
            Dedupe(d.places);
            Dedupe(d.creatures);
            Dedupe(d.seenCreatures);
            Dedupe(d.visited);
            Dedupe(d.completedAreas);
            d.steps = Mathf.Max(0, d.steps);
            d.silkUses = Mathf.Max(0, d.silkUses);
            d.falls = Mathf.Max(0, d.falls);
            d.swims = Mathf.Max(0, d.swims);
            d.travels = Mathf.Max(0, d.travels);
            d.rescues = Mathf.Max(0, d.rescues);
            d.photos = Mathf.Max(0, d.photos);
            d.playDays = Mathf.Max(0, d.playDays);
            d.legacyDrops = Mathf.Max(0, d.legacyDrops);
            if (!float.IsFinite(d.playTime) || d.playTime < 0f) d.playTime = 0f;
            if (!float.IsFinite(d.highest)) d.highest = -999f;
            if (string.IsNullOrEmpty(d.area) || Areas.Get(d.area) == null || Areas.Get(d.area).Id != d.area) d.area = "forest";
            bool Ok(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
            if (d.hasPosition && !(Ok(d.tail) && Ok(d.head) && Ok(d.tailNormal) && Ok(d.headNormal))) d.hasPosition = false;
        }

        /// <summary>記録のファイルの目じるし（ほかのファイルを読みこまないように）。</summary>
        const string ExportMark = "shakutori-save";

        [Serializable]
        class ExportFile
        {
            public string app = "";   // 書き出すときに ExportMark を入れる（ほかの JSON を読みこまないように、初めは空）
            public int version = SaveData.CurrentVersion;
            public string saved = "";
            public SaveData data;
        }

        /// <summary>いまの記録を、ファイルに書き出す文字にする（JSON）。</summary>
        public static string Export() => JsonUtility.ToJson(new ExportFile { app = ExportMark, data = Data, saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm") }, true);

        /// <summary>
        /// 書き出したファイルの文字から、記録を読みこむ。しゃくとりの森の記録でなければ、読みこまずに false（いまの記録は、そのまま）。
        /// </summary>
        public static bool Import(string text, out string error)
        {
            error = null;
            ExportFile f = null;
            try { f = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<ExportFile>(text); }
            catch (Exception) { f = null; }
            if (f == null || f.app != ExportMark || f.data == null)
            {
                error = "しゃくとりの森の記録のファイルではありません";
                return false;
            }
            if (f.version > SaveData.CurrentVersion)
            {
                error = "新しいバージョンの記録なので、読みこめません";
                return false;
            }
            EnsureLoaded();
            Migrate(f.data);
            SanitizeData(f.data);
            _data = f.data;
            Practice = false;
            Save();
            HasSave = true;
            return true;
        }

        /// <summary>ブラウザへの保存に失敗した（容量がいっぱい など）。1 回のプレイで 1 度だけ知らせる。</summary>
        public static event Action SaveFailed;
        static bool _failNotified;

        /// <summary>
        /// 練習中（チュートリアルの練習の森）：進み具合は、ブラウザに書かない（メモリの中だけ。練習が終わったら消す）。設定は、いつもどおり書く。
        /// </summary>
        public static bool Practice { get; private set; }

        /// <summary>練習をはじめる：まっさらな記録で練習する（ブラウザには書かない）。</summary>
        public static void BeginPractice()
        {
            EnsureLoaded();
            _data = new SaveData();
            Practice = true;
        }

        /// <summary>練習をおえる：練習の記録をすてて、まっさらな記録にもどす（練習で見つけたいきものは、keepCreatures なら図鑑にのこす）。</summary>
        public static void EndPractice(bool keepCreatures)
        {
            var practice = _data;
            Practice = false;
            _data = new SaveData();
            HasSave = false;
            if (!keepCreatures || practice == null) return;
            foreach (var c in practice.creatures) if (!_data.creatures.Contains(c)) _data.creatures.Add(c);
            foreach (var c in practice.seenCreatures) if (!_data.seenCreatures.Contains(c)) _data.seenCreatures.Add(c);
            foreach (var n in practice.notes)
            {
                if (n == null || _data.notes.Exists(m => m.id == n.id)) continue;
                n.friendAt = -999f;   // 遊んだ時間は 0 からなので、練習の時間は使わない
                _data.notes.Add(n);
            }
        }

        public static void Save()
        {
            if (Practice) return;   // 練習の森の記録は、書かない
            try
            {
                string json = JsonUtility.ToJson(Data);
                // ひとつ前の正しいデータを控えに残しておく（書き込み中に壊れても戻せるように）
                if (PlayerPrefs.HasKey(SaveKey)) PlayerPrefs.SetString(BackupKey, PlayerPrefs.GetString(SaveKey));
                PlayerPrefs.SetString(SaveKey, json);
                PlayerPrefs.Save();
                HasSave = true;
                LastSaveTime = Time.unscaledTime;
                Saved?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 保存に失敗しました: " + e.Message);
                if (!_failNotified)
                {
                    _failNotified = true;
                    SaveFailed?.Invoke();
                }
            }
        }

        static bool _settingsDirty;
        static float _settingsDirtyAt;

        /// <summary>
        /// 設定を少しあとでまとめて保存する（スライダーを動かすたびにブラウザへ書きこむと重いので）。
        /// GameManager が毎フレーム FlushSettings を呼ぶ。
        /// </summary>
        public static void SaveSettingsSoon()
        {
            _settingsDirty = true;
            _settingsDirtyAt = Time.unscaledTime;
        }

        public static bool SettingsDirty => _settingsDirty;

        public static void FlushSettings(bool force = false)
        {
            if (!_settingsDirty) return;
            if (!force && Time.unscaledTime - _settingsDirtyAt < 0.5f) return;
            SaveSettings();
        }

        public static void SaveSettings()
        {
            _settingsDirty = false;
            try
            {
                PlayerPrefs.SetString(SettingsKey, JsonUtility.ToJson(Settings));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 設定の保存に失敗しました: " + e.Message);
            }
        }

        /// <summary>設定だけを最初の状態に戻す（進行状況はそのまま）。</summary>
        public static void ResetSettings()
        {
            _settings = new SettingsData();
            SaveSettings();
        }

        /// <summary>テスト用：保存データと設定をすべて消して初期状態に戻す。</summary>
        public static void ResetAll()
        {
            Practice = false;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.DeleteKey(BackupKey);
            PlayerPrefs.DeleteKey(SettingsKey);
            PlayerPrefs.Save();
            _data = new SaveData();
            _settings = new SettingsData();
            _loaded = true;
            HasSave = false;
        }

        /// <summary>テスト用：保存先には触らず、メモリ上の状態だけ初期化する。</summary>
        public static void ResetAllInMemoryForTests()
        {
            Practice = false;
            _data = new SaveData();
            _settings = new SettingsData();
            _loaded = true;
            HasSave = false;
        }

        /// <summary>テスト用：次にさわったときに保存先から読み直す。</summary>
        public static void ForgetLoadedForTests() => _loaded = false;

        public static void ResetProgress()
        {
            Practice = false;
            _data = new SaveData();
            _loaded = true;
            HasSave = false;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.DeleteKey(BackupKey);
            PlayerPrefs.Save();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 2;
        public int version = CurrentVersion;
        public List<int> drops = new List<int>();
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
        // きろく
        public int steps;
        public int silkUses;
        public int falls;
        public float highest = -999f;
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
        public float cameraDistance;  // 0 = 最初の距離
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
                HasSave = data != null && (_data.hasPosition || _data.drops.Count > 0 || _data.places.Count > 0 || _data.creatures.Count > 0);
                if (PlayerPrefs.HasKey(SettingsKey))
                    _settings = JsonUtility.FromJson<SettingsData>(PlayerPrefs.GetString(SettingsKey)) ?? new SettingsData();
                else _settings = new SettingsData();
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
            if (string.IsNullOrEmpty(d.area)) d.area = "forest";
            if (string.IsNullOrEmpty(d.skin)) d.skin = "wakaba";
            if (d.version < 2)
            {
                // v1 にはなかった：最初のエリアは行ったことがある・図鑑は全部見たことにする
                if (!d.visited.Contains("forest")) d.visited.Add("forest");
                foreach (var c in d.creatures)
                    if (!d.seenCreatures.Contains(c)) d.seenCreatures.Add(c);
                if (d.highest < -900f) d.highest = -999f;
            }
            d.version = SaveData.CurrentVersion;
        }

        public static void Save()
        {
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
            _data = new SaveData();
            _settings = new SettingsData();
            _loaded = true;
            HasSave = false;
        }

        /// <summary>テスト用：次にさわったときに保存先から読み直す。</summary>
        public static void ForgetLoadedForTests() => _loaded = false;

        public static void ResetProgress()
        {
            _data = new SaveData();
            _loaded = true;
            HasSave = false;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.DeleteKey(BackupKey);
            PlayerPrefs.Save();
        }
    }
}

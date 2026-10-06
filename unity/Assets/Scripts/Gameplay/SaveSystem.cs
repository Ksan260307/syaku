using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public List<int> drops = new List<int>();
        public List<int> places = new List<int>();
        public float playTime;
        public bool hasPosition;
        public Vector3 tail, tailNormal, head, headNormal;
        public bool completed;                                     // ぜんぶ集めた
        public string area = "forest";                             // いまいるエリア
        public List<string> creatures = new List<string>();        // 見つけたいきもの
        public List<string> visited = new List<string>();          // 行ったことのあるエリア
        public List<string> completedAreas = new List<string>();   // しずくと名所をぜんぶ見つけたエリア
        public string skin = "wakaba";
    }

    [Serializable]
    public class SettingsData
    {
        public float sensitivity = 1f;
        public bool invertY;
        public float music = 0.55f;
        public float sfx = 0.8f;
        public int quality = -1;   // -1 = 自動, 0 = かるい, 1 = きれい
        public bool showHelp = true;
    }

    /// <summary>進行状況と設定の保存（WebGL ではブラウザの IndexedDB に保存される）。</summary>
    public static class SaveSystem
    {
        const string SaveKey = "shakutori.save.v1";
        const string SettingsKey = "shakutori.settings.v1";

        public static SaveData Data { get; private set; } = new SaveData();
        public static SettingsData Settings { get; private set; } = new SettingsData();
        public static bool HasSave { get; private set; }

        public static void Load()
        {
            try
            {
                if (PlayerPrefs.HasKey(SaveKey))
                {
                    Data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey)) ?? new SaveData();
                    HasSave = Data.hasPosition || Data.drops.Count > 0 || Data.places.Count > 0 || Data.creatures.Count > 0;
                }
                if (PlayerPrefs.HasKey(SettingsKey))
                    Settings = JsonUtility.FromJson<SettingsData>(PlayerPrefs.GetString(SettingsKey)) ?? new SettingsData();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 読み込みに失敗しました: " + e.Message);
                Data = new SaveData();
                Settings = new SettingsData();
            }
        }

        public static void Save()
        {
            try
            {
                PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
                PlayerPrefs.Save();
                HasSave = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 保存に失敗しました: " + e.Message);
            }
        }

        public static void SaveSettings()
        {
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

        /// <summary>テスト用：保存データと設定をすべて消して初期状態に戻す。</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.DeleteKey(SettingsKey);
            PlayerPrefs.Save();
            Data = new SaveData();
            Settings = new SettingsData();
            HasSave = false;
        }

        /// <summary>テスト用：保存先には触らず、メモリ上の状態だけ初期化する。</summary>
        public static void ResetAllInMemoryForTests()
        {
            Data = new SaveData();
            Settings = new SettingsData();
            HasSave = false;
        }

        public static void ResetProgress()
        {
            Data = new SaveData();
            HasSave = false;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }
    }
}

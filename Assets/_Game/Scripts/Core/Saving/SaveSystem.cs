using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnimalFarm.Core.Saving
{
    /// <summary>
    /// Single-slot JSON save. Scans the scene for MonoBehaviours implementing
    /// ISaveable — no manual registration. Loads automatically on Start if a
    /// save file exists.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        public static SaveSystem Instance { get; private set; }

        private const int CurrentVersion = 1;
        private const string FileName = "save0.json";

        public bool SaveExists => File.Exists(SavePath);

        private string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        [Serializable]
        private class SaveFile
        {
            public int version;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private class Entry
        {
            public string key;
            public string json;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            TryLoad();
        }

        public void Save()
        {
            var file = new SaveFile { version = CurrentVersion };

            foreach (var saveable in FindSaveables())
            {
                file.entries.Add(new Entry { key = saveable.SaveKey, json = saveable.Capture() });
            }

            File.WriteAllText(SavePath, JsonUtility.ToJson(file, prettyPrint: true));
            Debug.Log($"[SaveSystem] Saved {file.entries.Count} entries to {SavePath}");
        }

        public bool TryLoad()
        {
            if (!SaveExists) return false;

            SaveFile file;
            try
            {
                file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Failed to read save at {SavePath}: {e.Message}");
                return false;
            }

            if (file == null || file.entries == null)
            {
                Debug.LogWarning($"[SaveSystem] Save at {SavePath} is empty or malformed.");
                return false;
            }

            var byKey = new Dictionary<string, string>(file.entries.Count);
            foreach (var entry in file.entries)
            {
                if (!string.IsNullOrEmpty(entry.key))
                    byKey[entry.key] = entry.json;
            }

            int restored = 0;
            foreach (var saveable in FindSaveables())
            {
                if (byKey.TryGetValue(saveable.SaveKey, out var json))
                {
                    saveable.Restore(json);
                    restored++;
                }
            }

            Debug.Log($"[SaveSystem] Loaded {restored} entries from {SavePath}");
            return true;
        }

        private static List<ISaveable> FindSaveables()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var result = new List<ISaveable>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour is ISaveable saveable)
                    result.Add(saveable);
            }
            return result;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}

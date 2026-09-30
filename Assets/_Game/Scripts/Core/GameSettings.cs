using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Player-facing difficulty/comfort toggles (GDD 4.4: labelled easy mode
    /// from day one). Persisted with the save.
    /// </summary>
    public class GameSettings : MonoBehaviour, ISaveable
    {
        public static GameSettings Instance { get; private set; }

        /// <summary>
        /// "Gentle Passage": no Repo-man, gentler pressure. The hard systems stay
        /// in the game; this is the signposted door out (research 5.7).
        /// </summary>
        public bool GentlePassage { get; set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [System.Serializable]
        private struct State { public bool gentlePassage; }

        public string SaveKey => "settings";
        public string Capture() => JsonUtility.ToJson(new State { gentlePassage = GentlePassage });
        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            GentlePassage = JsonUtility.FromJson<State>(json).gentlePassage;
        }
    }
}

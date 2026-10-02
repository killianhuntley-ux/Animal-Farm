using System;
using AnimalFarm.Core.Saving;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AnimalFarm.Core
{
    /// <summary>
    /// Top-level game state: pause handling and save-and-quit. Pausing sets
    /// Time.timeScale to 0 (which also halts the GameClock) and blocks gameplay input.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public bool IsPaused { get; private set; }

        public event Action<bool> OnPauseChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.PausePressed += TogglePause;
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;

            if (GameInput.Instance != null)
            {
                // Resuming must not unblock input while another modal or a ceremony
                // still owns it (journal open under the pause menu, naming/Styx running).
                bool otherOwner = UIInputLock.AnyOwnerHolds;
                if (paused || !otherOwner)
                    GameInput.Instance.SetGameplayBlocked(paused);
            }

            OnPauseChanged?.Invoke(paused);
        }

        public void SaveAndQuit()
        {
            if (SaveSystem.Instance != null)
                SaveSystem.Instance.Save();

            Application.Quit();
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#endif
        }

        private void TogglePause()
        {
            SetPaused(!IsPaused);
        }

        private void OnApplicationQuit()
        {
            if (SaveSystem.Instance != null)
                SaveSystem.Instance.Save();
        }

        private void OnDestroy()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.PausePressed -= TogglePause;

            if (Instance == this) Instance = null;
        }
    }
}

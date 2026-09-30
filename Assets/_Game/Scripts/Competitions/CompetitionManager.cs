using System;
using AnimalFarm.Core;
using AnimalFarm.Player;
using AnimalFarm.Spirits;
using UnityEngine;

namespace AnimalFarm.Competitions
{
    /// <summary>Outcome of one competition run.</summary>
    public struct CompetitionResult
    {
        public string eventName;
        public int placement;   // 1 = won
        public int entrants;
        public bool Won => placement == 1;
    }

    /// <summary>
    /// Orchestrates competition events (slice 05). One event at a time:
    /// blocks gameplay input, points the camera at the arena, locks the entered
    /// spirit via its ceremony lock, runs the event, then restores everything
    /// and reports the result. Entry is free until the economy slice prices it.
    /// </summary>
    public class CompetitionManager : MonoBehaviour
    {
        public static CompetitionManager Instance { get; private set; }

        /// <summary>Arena centre, far outside the farm fence.</summary>
        public static readonly Vector3 ArenaOrigin = new Vector3(120f, 0f, 0f);

        [SerializeField] private Material spriteMaterial;
        public Material SpriteMaterial => spriteMaterial;

        public bool EventRunning { get; private set; }

        /// <summary>(entered spirit, result) — fired after the arena has been torn down.</summary>
        public event Action<SpiritAgent, CompetitionResult> EventFinished;

        /// <summary>
        /// Returns the live manager, creating one on the fly if the scene predates
        /// slice 05 — the arena needs no scene setup, so runtime creation is safe.
        /// </summary>
        public static CompetitionManager GetOrCreate()
        {
            if (Instance == null)
                new GameObject("CompetitionManager (runtime)").AddComponent<CompetitionManager>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Starts the Boulder Trial with the given spirit. difficulty 0..2.</summary>
        public void StartBoulderTrial(SpiritAgent spirit, int difficulty)
        {
            if (EventRunning || spirit == null) return;
            EventRunning = true;

            if (GameInput.Instance != null) GameInput.Instance.SetGameplayBlocked(true);

            var cam = UnityEngine.Object.FindFirstObjectByType<CameraFollow>();
            if (cam != null) cam.SetOverrideTarget(transform, ArenaOrigin);

            // Events are always well-lit regardless of time of day (playtest: a
            // midnight trial was unreadable under the night tint).
            _dayNight = UnityEngine.Object.FindFirstObjectByType<DayNightLight>();
            if (_dayNight != null)
            {
                _eventLight = _dayNight.GetComponent<UnityEngine.Rendering.Universal.Light2D>();
                _dayNight.enabled = false;
                if (_eventLight != null)
                {
                    _eventLight.color = new Color(1f, 0.98f, 0.94f);
                    _eventLight.intensity = 1f;
                }
            }

            var eventGo = new GameObject("BoulderTrial");
            var trial = eventGo.AddComponent<BoulderTrialEvent>();
            trial.Run(this, spirit, Mathf.Clamp(difficulty, 0, 2));
        }

        /// <summary>Called by the running event when it is fully torn down.</summary>
        private DayNightLight _dayNight;
        private UnityEngine.Rendering.Universal.Light2D _eventLight;

        public void ReportFinished(SpiritAgent spirit, CompetitionResult result)
        {
            EventRunning = false;

            if (_dayNight != null) { _dayNight.enabled = true; _dayNight = null; }
            _eventLight = null;

            var cam = UnityEngine.Object.FindFirstObjectByType<CameraFollow>();
            if (cam != null) cam.ClearOverrideTarget();

            bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
            if (GameInput.Instance != null && !paused) GameInput.Instance.SetGameplayBlocked(false);

            EventFinished?.Invoke(spirit, result);
        }
    }
}

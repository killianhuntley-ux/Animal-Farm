using System;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Muscle 02: rain temporarily floods pond edges a little. A game-hour or
    /// so into a downpour the ponds swell -- Scrub/Grass/Sand ground within one
    /// cell of any water is drawn as shallows (TerrainGrid.SetFloodActive) --
    /// and a few game-hours after the rain stops the water recedes. While
    /// flooded, tilled soil touching a pond is kept soaked (free watering).
    /// Strictly non-destructive (owner law: no large/irrecoverable losses): no
    /// surface changes, nothing is blocked, the biome census does not count the
    /// flood, no plant or home is ever touched.
    /// Listens to WeatherManager.IsRaining (polled; no audio coupling). State
    /// persists ("pondflood" key) so a reload mid-flood resumes it. Self-spawns
    /// at runtime (WeatherManager pattern) - no scene setup.
    /// </summary>
    public class PondFlood : MonoBehaviour, ISaveable
    {
        public static PondFlood Instance { get; private set; }

        private const float RiseDelayHours = 1.0f;    // game-hours of rain before the ponds swell
        private const float RecedeDelayHours = 3.0f;  // game-hours after the rain before they recede
        private const float SoakInterval = 5f;        // real seconds between edge-soak sweeps

        /// <summary>The ponds are swollen right now.</summary>
        public bool IsFlooded { get; private set; }

        private float _riseAt = -1f;     // TotalHours the flood begins; -1 = none pending
        private float _recedeAt = -1f;   // TotalHours the flood recedes; -1 = none pending
        private bool _prevRaining;
        private float _nextSoak;

        /// <summary>Returns the live flood driver, creating one if the scene predates it.</summary>
        public static PondFlood GetOrCreate()
        {
            if (Instance == null)
                new GameObject("PondFlood (runtime)").AddComponent<PondFlood>();
            return Instance;
        }

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
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

        private void Update()
        {
            var clock = GameClock.Instance;
            var grid = TerrainGrid.Instance;
            if (clock == null || grid == null) return;

            float now = clock.TotalHours;
            var weather = WeatherManager.Instance;
            bool raining = weather != null && weather.IsRaining;

            if (raining != _prevRaining)
            {
                _prevRaining = raining;
                if (raining)
                {
                    _recedeAt = -1f; // rain again: the water stays up
                    if (!IsFlooded && _riseAt < 0f) _riseAt = now + RiseDelayHours;
                }
                else
                {
                    _riseAt = -1f; // dried out before the ponds swelled
                    if (IsFlooded) _recedeAt = now + RecedeDelayHours;
                }
            }

            if (_riseAt >= 0f && now >= _riseAt)
            {
                _riseAt = -1f;
                IsFlooded = true;
            }

            // Heal: a flood with no rain and no pending recede (forced / odd save) still ends.
            if (IsFlooded && !raining && _recedeAt < 0f) _recedeAt = now + RecedeDelayHours;

            if (_recedeAt >= 0f && now >= _recedeAt)
            {
                _recedeAt = -1f;
                IsFlooded = false;
            }

            if (grid.FloodActive != IsFlooded) grid.SetFloodActive(IsFlooded);

            if (IsFlooded && Time.time >= _nextSoak)
            {
                _nextSoak = Time.time + SoakInterval;
                grid.SoakWaterEdges();
            }
        }

        /// <summary>Console helper: force the flood on (recedes a few game-hours after
        /// any rain stops) or off right now.</summary>
        public void Debug_SetFlood(bool on)
        {
            _riseAt = -1f;
            _recedeAt = -1f;
            IsFlooded = on;
        }

        // ---- ISaveable ----

        [Serializable]
        private struct FloodState
        {
            public bool flooded;
            public float riseAt;
            public float recedeAt;
        }

        public string SaveKey => "pondflood";

        public string Capture()
        {
            return JsonUtility.ToJson(new FloodState { flooded = IsFlooded, riseAt = _riseAt, recedeAt = _recedeAt });
        }

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var state = JsonUtility.FromJson<FloodState>(json);
            IsFlooded = state.flooded;
            _riseAt = state.riseAt;
            _recedeAt = state.recedeAt;
            // Update pushes the restored state onto the grid (and heals a stale one).
        }
    }
}

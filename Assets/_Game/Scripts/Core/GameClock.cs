using System;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Central in-game clock. One full day = <see cref="DayLengthRealMinutes"/> real
    /// minutes (scaled time, so pausing via Time.timeScale = 0 halts the clock).
    /// New game starts on Day 1 at 08:00.
    /// </summary>
    public class GameClock : MonoBehaviour, ISaveable
    {
        public static GameClock Instance { get; private set; }

        public const float DayLengthRealMinutes = 20f;
        public const float FastForwardMultiplier = 8f;

        /// <summary>0..1 through the current day; 0 = midnight.</summary>
        public float NormalizedTime => _normalizedTime;

        /// <summary>0..24 hours.</summary>
        public float Hours => _normalizedTime * 24f;

        /// <summary>Total game-hours elapsed since Day 1, 00:00. Monotonic; use for growth timers.</summary>
        public float TotalHours => (_day - 1) * 24f + _normalizedTime * 24f;

        /// <summary>Current day, starting at 1.</summary>
        public int Day => _day;

        /// <summary>Night spans 18:00 → 06:00.</summary>
        public bool IsNight => _normalizedTime < 0.25f || _normalizedTime >= 0.75f;

        public bool FastForward { get; set; }

        /// <summary>"HH:MM", 24-hour.</summary>
        public string TimeString
        {
            get
            {
                int totalMinutes = Mathf.FloorToInt(_normalizedTime * 24f * 60f);
                totalMinutes = Mathf.Clamp(totalMinutes, 0, 24 * 60 - 1);
                return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
            }
        }

        public event Action<int> OnDayChanged;

        private float _normalizedTime = 8f / 24f; // 08:00
        private int _day = 1;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.FastForwardPressed += ToggleFastForward;
        }

        private void Update()
        {
            float daysPerSecond = 1f / (DayLengthRealMinutes * 60f);
            float speed = FastForward ? FastForwardMultiplier : 1f;
            _normalizedTime += Time.deltaTime * daysPerSecond * speed;

            while (_normalizedTime >= 1f)
            {
                _normalizedTime -= 1f;
                _day++;
                OnDayChanged?.Invoke(_day);
            }
        }

        /// <summary>Set the time of day in hours; wraps into 0..24.</summary>
        public void SetTimeHours(float hours)
        {
            _normalizedTime = Mathf.Repeat(hours, 24f) / 24f;
        }

        /// <summary>
        /// Debug/console: jump to an absolute day (clamped to >= 1). Time of
        /// day is kept; OnDayChanged fires so the calendar and weather follow.
        /// </summary>
        public void SetDay(int day)
        {
            day = Mathf.Max(1, day);
            if (day == _day) return;
            _day = day;
            OnDayChanged?.Invoke(_day);
        }

        private void ToggleFastForward()
        {
            FastForward = !FastForward;
        }

        private void OnDestroy()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.FastForwardPressed -= ToggleFastForward;

            if (Instance == this) Instance = null;
        }

        // ---- ISaveable ----

        [Serializable]
        private struct ClockState
        {
            public int day;
            public float normalizedTime;
        }

        public string SaveKey => "clock";

        public string Capture()
        {
            return JsonUtility.ToJson(new ClockState { day = _day, normalizedTime = _normalizedTime });
        }

        public void Restore(string json)
        {
            var state = JsonUtility.FromJson<ClockState>(json);
            _day = Mathf.Max(1, state.day);
            _normalizedTime = Mathf.Repeat(state.normalizedTime, 1f);
        }
    }
}

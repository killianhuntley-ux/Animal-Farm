using System;
using AnimalFarm.Core.Saving;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AnimalFarm.Core
{
    /// <summary>One underworld season: 15 days of a particular mood.</summary>
    [Serializable]
    public struct SeasonDef
    {
        public string name;

        [Tooltip("Multiplied onto the DayNightLight color every frame. Keep near white - subtle.")]
        public Color tint;

        [Tooltip("Chance (0..1) that any given day of this season is a rain day.")]
        [Range(0f, 1f)] public float rainWeight;

        [Tooltip("One-line flavor shown on the calendar page.")]
        public string flavor;
    }

    /// <summary>
    /// The underworld calendar: 15-day seasons cycling forever on top of
    /// GameClock's day counter (time works differently down here). Purely
    /// derived from GameClock.Day, so it can never drift from the clock;
    /// the save fragment only remembers what it last announced, keeping
    /// DayChanged/SeasonChanged from re-firing on load. Self-spawns at
    /// runtime (GetOrCreate pattern) - no scene setup required.
    /// </summary>
    public class GameCalendar : MonoBehaviour, ISaveable
    {
        public static GameCalendar Instance { get; private set; }

        public const int DaysPerSeason = 15;

        [SerializeField] private SeasonDef[] seasons = DefaultSeasons();

        /// <summary>Fired when the absolute day changes (argument: GameClock.Day).</summary>
        public event Action<int> DayChanged;

        /// <summary>Fired when the season rolls over (argument: season index).</summary>
        public event Action<int> SeasonChanged;

        private int _lastDay = -1;
        private int _lastSeason = -1;
        private bool _subscribed;

        public int SeasonCount => seasons != null && seasons.Length > 0 ? seasons.Length : 1;

        /// <summary>Index into the season table for today.</summary>
        public int SeasonIndex
        {
            get
            {
                int day = GameClock.Instance != null ? GameClock.Instance.Day : 1;
                return ((day - 1) / DaysPerSeason) % SeasonCount;
            }
        }

        /// <summary>1..15 within the current season.</summary>
        public int DayOfSeason
        {
            get
            {
                int day = GameClock.Instance != null ? GameClock.Instance.Day : 1;
                return ((day - 1) % DaysPerSeason) + 1;
            }
        }

        public SeasonDef CurrentSeason => GetSeason(SeasonIndex);

        /// <summary>HUD line, e.g. "Day 7 - The Weep".</summary>
        public string DateLine => "Day " + DayOfSeason + " - " + CurrentSeason.name;

        public SeasonDef GetSeason(int index)
        {
            if (seasons == null || seasons.Length == 0)
                return new SeasonDef { name = "The Unseason", tint = Color.white, rainWeight = 0f, flavor = "" };
            index = Mathf.Clamp(index, 0, seasons.Length - 1);
            return seasons[index];
        }

        public string GetSeasonName(int index) => GetSeason(index).name;

        /// <summary>
        /// Returns the live calendar, creating one on the fly if the scene
        /// predates the calendar system - it needs no scene setup, so runtime
        /// creation is safe (CompetitionManager pattern).
        /// </summary>
        public static GameCalendar GetOrCreate()
        {
            if (Instance == null)
                new GameObject("GameCalendar (runtime)").AddComponent<GameCalendar>();
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

            if (GetComponent<SeasonAmbience>() == null)
                gameObject.AddComponent<SeasonAmbience>();
        }

        private void Start()
        {
            var clock = GameClock.Instance;
            if (clock != null)
            {
                clock.OnDayChanged += OnClockDayChanged;
                _subscribed = true;

                // First run (no save restored yet): adopt today silently so
                // nothing fires a "change" on scene start.
                if (_lastDay < 0)
                {
                    _lastDay = clock.Day;
                    _lastSeason = SeasonIndex;
                }
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && GameClock.Instance != null)
                GameClock.Instance.OnDayChanged -= OnClockDayChanged;

            if (Instance == this) Instance = null;
        }

        private void OnClockDayChanged(int day)
        {
            if (day == _lastDay) return;
            _lastDay = day;
            DayChanged?.Invoke(day);

            int season = SeasonIndex;
            if (season != _lastSeason)
            {
                _lastSeason = season;
                SeasonChanged?.Invoke(season);
            }
        }

        /// <summary>
        /// Debug/console: jump to season <paramref name="index"/>, keeping the
        /// current day-of-season (Day 7 of The Hush becomes Day 7 of The Weep).
        /// </summary>
        public void Debug_SetSeason(int index)
        {
            var clock = GameClock.Instance;
            if (clock == null) return;

            index = Mathf.Clamp(index, 0, SeasonCount - 1);
            int cycleLength = DaysPerSeason * SeasonCount;
            int cycleBase = ((clock.Day - 1) / cycleLength) * cycleLength;
            clock.SetDay(cycleBase + index * DaysPerSeason + DayOfSeason);
        }

        private static SeasonDef[] DefaultSeasons() => new[]
        {
            new SeasonDef
            {
                name = "The Hush",
                tint = new Color(0.95f, 0.97f, 1.00f),
                rainWeight = 0.10f,
                flavor = "The underworld holds its breath. Nobody asks for whom."
            },
            new SeasonDef
            {
                name = "The Weep",
                tint = new Color(0.86f, 0.90f, 1.00f),
                rainWeight = 0.55f,
                flavor = "The sky remembers everyone it ever swallowed. Loudly."
            },
            new SeasonDef
            {
                name = "The Smolder",
                tint = new Color(1.00f, 0.92f, 0.84f),
                rainWeight = 0.05f,
                flavor = "Warm, dry, and faintly smug about it."
            },
            new SeasonDef
            {
                name = "The Long Dim",
                tint = new Color(0.84f, 0.84f, 0.95f),
                rainWeight = 0.30f,
                flavor = "The lanterns burn low. The dark is just being friendly."
            }
        };

        // ---- ISaveable ----

        [Serializable]
        private struct CalendarState
        {
            public int lastDay;
            public int lastSeason;
        }

        public string SaveKey => "calendar";

        public string Capture()
        {
            return JsonUtility.ToJson(new CalendarState { lastDay = _lastDay, lastSeason = _lastSeason });
        }

        public void Restore(string json)
        {
            var state = JsonUtility.FromJson<CalendarState>(json);
            _lastDay = Mathf.Max(1, state.lastDay);
            _lastSeason = Mathf.Clamp(state.lastSeason, 0, SeasonCount - 1);
        }
    }

    /// <summary>
    /// Multiplies the current season's tint (plus a grey-blue dim while it
    /// rains) onto the global Light2D every frame, AFTER DayNightLight has
    /// written its time-of-day color - execution order 60 guarantees that.
    /// DayNightLight rewrites the color from scratch each LateUpdate, so the
    /// multiply never accumulates. Steps aside whenever DayNightLight is
    /// disabled (competitions pin the light to full day).
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class SeasonAmbience : MonoBehaviour
    {
        private static readonly Color RainDim = new Color(0.80f, 0.84f, 0.94f);

        private DayNightLight _dayNight;
        private Light2D _light;
        private float _nextSearch;
        private Color _tint = Color.white;
        private bool _snapped;

        private void LateUpdate()
        {
            var calendar = GameCalendar.Instance;
            if (calendar == null) return;

            if (_dayNight == null || _light == null)
            {
                if (Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + 2f;

                _dayNight = FindFirstObjectByType<DayNightLight>();
                if (_dayNight != null)
                    _light = _dayNight.GetComponent<Light2D>();
                if (_light == null)
                    _light = FindGlobalLight();
                if (_dayNight == null || _light == null) return;
            }

            Color target = calendar.CurrentSeason.tint;
            var weather = AnimalFarm.World.WeatherManager.Instance;
            if (weather != null && weather.IsRaining)
                target *= RainDim;

            // Ease across season boundaries / rain starts (snap on first frame).
            _tint = _snapped ? Color.Lerp(_tint, target, Time.deltaTime * 2f) : target;
            _snapped = true;

            // Competitions disable DayNightLight and pin the light - stay out.
            // Also stay out when the clock is missing: DayNightLight early-outs
            // then WITHOUT rewriting the color, and multiplying an un-rewritten
            // color every frame compounds toward black.
            if (!_dayNight.enabled || !_dayNight.isActiveAndEnabled) return;
            if (GameClock.Instance == null) return;

            _light.color *= _tint;
        }

        private static Light2D FindGlobalLight()
        {
            var lights = FindObjectsByType<Light2D>(FindObjectsSortMode.None);
            foreach (var light in lights)
            {
                if (light.lightType == Light2D.LightType.Global)
                    return light;
            }
            return null;
        }
    }
}

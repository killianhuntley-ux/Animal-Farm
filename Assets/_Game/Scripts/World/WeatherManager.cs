using System;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>
    /// Simple weather: at the start of each day it rolls against the current
    /// season's rain weight; a hit makes the WHOLE day a rain day. Rain is
    /// steady procedural streaks (a code-built particle system riding the
    /// camera - no assets), a synthesized rain-hiss loop (Bleeps approach:
    /// AudioClip.Create, kept soft), and a free soaking - every usable Dirt
    /// cell gets TerrainGrid.SetWatered refreshed every few seconds while it
    /// pours. Day detection polls GameClock.Day, so console day/season jumps
    /// and save restores all re-roll correctly with no event-order worries.
    /// Self-spawns at runtime (GetOrCreate pattern) - no scene setup required.
    /// </summary>
    public class WeatherManager : MonoBehaviour, ISaveable
    {
        public static WeatherManager Instance { get; private set; }

        private const float WaterInterval = 5f;     // real seconds between soak sweeps
        private const float RainSoundVolume = 0.8f; // clip itself is synthesized quiet

        /// <summary>Today rolled as a rain day (rain runs all day).</summary>
        public bool IsRainDay { get; private set; }

        /// <summary>Rain is actually falling right now (visuals + sound live).</summary>
        public bool IsRaining { get; private set; }

        public event Action<bool> RainChanged;

        private int _rolledDay = -1;
        private ParticleSystem _rainParticles;
        private AudioSource _rainAudio;
        private float _nextWaterAt;
        private bool _pauseSubscribed;
        private bool _rainLoopStarted;
        private float _nextRainTryAt;

        /// <summary>
        /// Returns the live manager, creating one on the fly if the scene
        /// predates the weather system (CompetitionManager pattern).
        /// </summary>
        public static WeatherManager GetOrCreate()
        {
            if (Instance == null)
                new GameObject("WeatherManager (runtime)").AddComponent<WeatherManager>();
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

        private void Start()
        {
            GameCalendar.GetOrCreate(); // season weights must exist before rolls

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnPauseChanged += OnPauseChanged;
                _pauseSubscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_pauseSubscribed && GameManager.Instance != null)
                GameManager.Instance.OnPauseChanged -= OnPauseChanged;

            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;

            // New day (natural rollover, console jump, or save restore): roll.
            if (clock.Day != _rolledDay)
                RollForDay(clock.Day);

            // Converge actual rain onto today's verdict (also heals the case
            // where Restore ran before/after other systems were ready).
            if (IsRaining != IsRainDay)
                SetRaining(IsRainDay);

            if (!IsRaining) return;

            if (_rainAudio != null)
            {
                // Ambience bus mute/solo/kill and the Ambience volume apply every frame.
                _rainAudio.mute = Bleeps.Muted || !AudioGuard.IsAudible(AudioBus.Ambience);
                _rainAudio.volume = RainSoundVolume * AudioGuard.AmbienceVolume;

                // Loop start was refused by the guard (muted at the time)? Retry every 2 s.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!_rainLoopStarted && !paused && Time.unscaledTime >= _nextRainTryAt)
                    TryStartRainLoop();
            }

            if (Time.time >= _nextWaterAt)
            {
                _nextWaterAt = Time.time + WaterInterval;
                SoakTheLand();
            }
        }

        /// <summary>Console helper: force today's weather (persists for the day).</summary>
        public void Debug_ForceRain(bool rain)
        {
            if (GameClock.Instance != null) _rolledDay = GameClock.Instance.Day;
            IsRainDay = rain;
        }

        // -------------------------------------------------------------- rolls

        private void RollForDay(int day)
        {
            _rolledDay = day;

            float weight = GameCalendar.Instance != null
                ? GameCalendar.Instance.CurrentSeason.rainWeight
                : 0f;
            IsRainDay = Random.value < weight;
        }

        private void SetRaining(bool on)
        {
            IsRaining = on;

            if (on)
            {
                EnsureRainRig();
                if (_rainParticles != null)
                {
                    _rainParticles.gameObject.SetActive(true);
                    _rainParticles.Play();
                }
                if (_rainAudio != null && !(GameManager.Instance != null && GameManager.Instance.IsPaused))
                    TryStartRainLoop();

                _nextWaterAt = Time.time; // first soak lands immediately
            }
            else
            {
                if (_rainParticles != null)
                    _rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (_rainAudio != null)
                    _rainAudio.Stop();
                _rainLoopStarted = false;
            }

            RainChanged?.Invoke(on);
        }

        /// <summary>Starts the rain loop through the AudioGuard (Ambience bus).</summary>
        private void TryStartRainLoop()
        {
            _nextRainTryAt = Time.unscaledTime + 2f;
            if (_rainAudio == null || _rainLoopStarted) return;
            if (!AudioGuard.TryPlay(AudioBus.Ambience, "rain", RainSoundVolume, 0f)) return;
            _rainAudio.Play();
            _rainLoopStarted = true;
        }

        private void OnPauseChanged(bool paused)
        {
            // Particles freeze on their own (scaled time); the loop must not.
            if (_rainAudio == null) return;
            if (paused) _rainAudio.Pause();
            else if (IsRaining) _rainAudio.UnPause();
        }

        // ----------------------------------------------------------- watering

        /// <summary>Free watering: every usable Dirt cell gets its soak refreshed.
        /// SetWatered itself rejects locked/non-Dirt cells, so a blind sweep is safe.</summary>
        private void SoakTheLand()
        {
            var grid = TerrainGrid.Instance;
            if (grid == null) return;

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                grid.SetWatered(new Vector2Int(x, y));
        }

        // -------------------------------------------------------------- rig

        /// <summary>Builds the particle streaks + audio loop once, lazily.</summary>
        private void EnsureRainRig()
        {
            if (_rainParticles == null)
            {
                var go = new GameObject("Rain_Particles");

                // Ride the camera so rain covers the view wherever the shepherd goes.
                var cam = Camera.main;
                if (cam != null)
                {
                    go.transform.SetParent(cam.transform, false);
                    // Above the view top; +10 z cancels the camera's -10 so
                    // particles live at world z ~0 with the sprites.
                    go.transform.localPosition = new Vector3(0f, 10f, 10f);
                }

                _rainParticles = go.AddComponent<ParticleSystem>();
                _rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = _rainParticles.main;
                main.loop = true;
                main.startLifetime = 1.4f;
                main.startSpeed = 0f; // all motion via velocityOverLifetime
                main.startSize = 0.05f;
                main.startColor = new Color(0.62f, 0.70f, 0.90f, 0.45f);
                main.maxParticles = 800;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = _rainParticles.emission;
                emission.rateOverTime = 220f;

                var shape = _rainParticles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(30f, 0.5f, 0.5f);

                var velocity = _rainParticles.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(-3f, -2f);
                velocity.y = new ParticleSystem.MinMaxCurve(-18f, -15f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f);

                // Velocity-stretched billboards turn a white square into
                // slanted rain lines - no sprite asset needed.
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 0f;
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.mainTexture = Texture2D.whiteTexture;
                renderer.material = mat;
                renderer.sortingOrder = 450; // above terrain, homes and spirits
            }

            if (_rainAudio == null)
            {
                _rainAudio = gameObject.AddComponent<AudioSource>();
                _rainAudio.clip = GenRainLoop();
                _rainAudio.loop = true;
                _rainAudio.playOnAwake = false;
                _rainAudio.spatialBlend = 0f; // ambient 2D
                _rainAudio.volume = RainSoundVolume;
            }
        }

        /// <summary>
        /// Synthesized ambient rain: low-passed white noise with a slow swell,
        /// 3 s, seamlessly cross-faded into itself (Bleeps approach - the game
        /// ships no audio assets). Peak kept at or below 0.3.
        /// </summary>
        private static AudioClip GenRainLoop()
        {
            const int sampleRate = 44100;
            const float seconds = 3f;
            int count = Mathf.CeilToInt(seconds * sampleRate);
            int fade = Mathf.CeilToInt(0.15f * sampleRate);
            var data = new float[count];
            var n = new float[count + fade]; // extra noise past the end feeds the seam cross-fade

            var rng = new System.Random(4242); // deterministic noise
            float hiss = 0f;   // dulled patter layer
            float rumble = 0f; // low wash layer
            for (int i = 0; i < count + fade; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                hiss += 0.12f * (white - hiss);
                rumble += 0.025f * (white - rumble);

                // Slow swell; exactly periodic over `count` samples so the loop
                // seam carries no level jump.
                float swell = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * i / count);

                float sample = (0.9f * hiss + 1.6f * rumble) * 0.45f * swell;
                n[i] = Mathf.Clamp(sample, -0.3f, 0.3f);
            }

            // Head blends from the continuation of the tail (n[count + i]) into the
            // original noise, so the last sample flows straight into the first.
            for (int i = 0; i < count; i++)
            {
                if (i < fade)
                {
                    float a = i / (float)fade;
                    data[i] = n[i] * a + n[count + i] * (1f - a);
                }
                else
                {
                    data[i] = n[i];
                }
            }

            var clip = AudioClip.Create("RainLoop", count, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---- ISaveable ----

        [Serializable]
        private struct WeatherState
        {
            public int rolledDay;
            public bool rainDay;
        }

        public string SaveKey => "weather";

        public string Capture()
        {
            return JsonUtility.ToJson(new WeatherState { rolledDay = _rolledDay, rainDay = IsRainDay });
        }

        public void Restore(string json)
        {
            var state = JsonUtility.FromJson<WeatherState>(json);
            _rolledDay = state.rolledDay;
            IsRainDay = state.rainDay;
            // Update converges visuals/audio, and re-rolls if the restored
            // clock day no longer matches rolledDay.
        }
    }
}

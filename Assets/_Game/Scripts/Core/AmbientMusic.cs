using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Generative ambient music bed, fully synthesized (AudioClip.Create -
    /// Bleeps' quieter sibling; the game still ships no audio assets).
    /// Two layers:
    ///   - PADS: long soft chord clips (12 s, 22.05 kHz mono) crossfaded
    ///     between two AudioSources, with occasional rests between phrases -
    ///     silence is part of the music.
    ///   - PLUCKS: sparse pentatonic sine notes at random-ish intervals
    ///     (seeded off time/phrase count - purely cosmetic).
    /// Mood is keyed to time of day (day = brighter major-leaning voicings,
    /// night = darker, lower, sparser) and season (GameCalendar.SeasonIndex
    /// transposes the whole bed a few semitones). Deliberately QUIET: pads
    /// peak around 0.15, plucks lower still.
    ///
    /// Pause: the bed KEEPS PLAYING while paused, just softer - audio runs
    /// on real time and the pause menu feels less dead with a faint hum
    /// under it. Respects Bleeps.Muted. MusicVolume (0-1, default 0.8)
    /// persists via PlayerPrefs (device setting, not game state).
    /// Self-spawns at runtime (GameCalendar pattern) - no scene setup.
    /// </summary>
    public class AmbientMusic : MonoBehaviour
    {
        public static AmbientMusic Instance { get; private set; }

        // ---- synthesis / mix constants ----
        private const int PadSampleRate = 22050;   // pads are low and soft; half rate halves synth cost
        private const float PadSeconds = 12f;
        private const float PadAttack = 3f;
        private const float PadRelease = 4f;
        private const float PadPeak = 0.15f;       // summed chord peak
        private const float CrossfadeSeconds = 4f;
        private const float PluckPeak = 0.10f;
        private const float PausedLevel = 0.4f;    // bed volume multiplier while paused
        private const float DuckLevel = 0.25f;     // bed volume multiplier while ducked
        private const string MusicVolumePrefKey = "af_music_volume";

        // Semitone offsets from the mood root. Day: major-leaning voicings.
        private static readonly int[][] DayChords =
        {
            new[] { 0, 4, 7, 11 },    // Imaj7
            new[] { 5, 9, 12, 16 },   // IVmaj
            new[] { 7, 11, 14 },      // V
            new[] { 9, 12, 16, 19 },  // vi7
            new[] { 2, 5, 9, 12 },    // ii7
        };

        // Night: minor, hollow, fewer notes.
        private static readonly int[][] NightChords =
        {
            new[] { 0, 3, 7, 10 },    // i7
            new[] { 5, 8, 12 },       // iv
            new[] { 8, 12, 15 },      // VI
            new[] { 0, 7, 12 },       // bare fifth + octave
            new[] { 10, 14, 17 },     // VII
        };

        // Pentatonic degrees for the pluck layer.
        private static readonly int[] DayPentatonic = { 0, 2, 4, 7, 9 };
        private static readonly int[] NightPentatonic = { 0, 3, 5, 7, 10 };

        // Per-season transpose in semitones (indexed by SeasonIndex, wrapped).
        private static readonly int[] SeasonTranspose = { 0, -3, 2, -5 };

        private const float RootHz = 146.83f; // D3 - low enough to stay out of the bleeps' way

        // ---- volume state ----
        private static float _musicVolume = 0.8f;
        private static bool _musicVolumeLoaded;

        /// <summary>Master music volume (0-1, default 0.8). PlayerPrefs-persisted.</summary>
        public static float MusicVolume
        {
            get { LoadMusicVolumeOnce(); return _musicVolume; }
            set
            {
                LoadMusicVolumeOnce();
                _musicVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicVolumePrefKey, _musicVolume);
            }
        }

        private static void LoadMusicVolumeOnce()
        {
            if (_musicVolumeLoaded) return;
            _musicVolumeLoaded = true;
            _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefKey, 0.8f));
        }

        // ---- runtime state ----
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        private AudioSource[] _padSources;          // two, crossfaded
        private readonly float[] _padFade = new float[2];
        private readonly float[] _padFadeTarget = new float[2];
        private int _activePad;
        private AudioSource _pluckSource;

        private float _nextPhraseAt;
        private float _nextPluckAt;
        private int _phraseCount;
        private int _lastChordIndex = -1;
        private int _lastMoodKey = int.MinValue;

        private float _duckUntil;
        private float _duck = 1f;
        private float _pauseSoft = 1f;

        // ------------------------------------------------------- Lifecycle

        /// <summary>Returns the live system, creating one on the fly (GameCalendar pattern).</summary>
        public static AmbientMusic GetOrCreate()
        {
            if (Instance == null)
                new GameObject("AmbientMusic (runtime)").AddComponent<AmbientMusic>();
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

            _padSources = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                _padSources[i] = gameObject.AddComponent<AudioSource>();
                _padSources[i].playOnAwake = false;
                _padSources[i].spatialBlend = 0f; // pure 2D bed
                _padSources[i].volume = 0f;
            }

            _pluckSource = gameObject.AddComponent<AudioSource>();
            _pluckSource.playOnAwake = false;
            _pluckSource.spatialBlend = 0f;

            // Short grace period so the scene's boot flood settles first.
            _nextPhraseAt = Time.unscaledTime + 2f;
            _nextPluckAt = Time.unscaledTime + 7f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Lower the bed for <paramref name="seconds"/> (ceremony stings etc.).</summary>
        public void DuckFor(float seconds)
        {
            _duckUntil = Mathf.Max(_duckUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
        }

        // ---------------------------------------------------------- Update

        private void Update()
        {
            float dt = Time.unscaledDeltaTime; // music runs on real time (plays through pause)

            // Master gain: user volume x duck x pause-soften x mute.
            float duckTarget = Time.unscaledTime < _duckUntil ? DuckLevel : 1f;
            _duck = Mathf.MoveTowards(_duck, duckTarget, dt / 0.8f);

            bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
            _pauseSoft = Mathf.MoveTowards(_pauseSoft, paused ? PausedLevel : 1f, dt / 0.6f);

            float master = (Bleeps.Muted ? 0f : 1f) * (AudioGuard.IsAudible(AudioBus.Music) ? 1f : 0f)
                * MusicVolume * _duck * _pauseSoft;

            // A mood flip (dawn/dusk, season roll) pulls the next phrase in
            // close so the bed answers within a couple of seconds.
            int mood = MoodKey();
            if (mood != _lastMoodKey)
            {
                if (_lastMoodKey != int.MinValue)
                    _nextPhraseAt = Mathf.Min(_nextPhraseAt, Time.unscaledTime + 2f);
                _lastMoodKey = mood;
            }

            if (Time.unscaledTime >= _nextPhraseAt)
                StartNextPhrase();

            // Crossfades + master applied every frame.
            for (int i = 0; i < 2; i++)
            {
                _padFade[i] = Mathf.MoveTowards(_padFade[i], _padFadeTarget[i], dt / CrossfadeSeconds);
                _padSources[i].volume = _padFade[i] * master;
            }
            _pluckSource.volume = master;

            if (Time.unscaledTime >= _nextPluckAt)
                PlayPluck();
        }

        // ----------------------------------------------------------- Pads

        /// <summary>Compact signature of the current mood: night flag + season.</summary>
        private int MoodKey()
        {
            bool night = GameClock.Instance != null && GameClock.Instance.IsNight;
            int season = GameCalendar.Instance != null ? GameCalendar.Instance.SeasonIndex : 0;
            return (night ? 1000 : 0) + season;
        }

        private void StartNextPhrase()
        {
            // Central gate (muted/soloed-away bus: retry in a couple of seconds).
            if (!AudioGuard.TryPlay(AudioBus.Music, "pad", PadPeak, 0f))
            {
                _nextPhraseAt = Time.unscaledTime + 2f;
                return;
            }

            bool night = GameClock.Instance != null && GameClock.Instance.IsNight;
            int season = GameCalendar.Instance != null ? GameCalendar.Instance.SeasonIndex : 0;
            int transpose = SeasonTranspose[((season % SeasonTranspose.Length)
                + SeasonTranspose.Length) % SeasonTranspose.Length];
            if (night) transpose -= 3; // night sits a touch lower as well as darker

            var chords = night ? NightChords : DayChords;

            // Cosmetic randomness: seed off real time + phrase count.
            var rng = new System.Random(_phraseCount * 131
                + (int)(Time.unscaledTime * 7f) + (night ? 17 : 0));

            int pick = rng.Next(chords.Length);
            if (pick == _lastChordIndex) pick = (pick + 1) % chords.Length; // never repeat
            _lastChordIndex = pick;

            float rootHz = RootHz * Mathf.Pow(2f, transpose / 12f);
            var clip = GetPadClip(chords[pick], rootHz);

            int next = 1 - _activePad;
            _padSources[next].clip = clip;
            _padSources[next].Play();
            _padFadeTarget[next] = 1f;
            _padFadeTarget[_activePad] = 0f;
            _activePad = next;
            _phraseCount++;

            // Next phrase overlaps this clip's release... unless we rest.
            // Rests are more common at night; the clip's built-in release
            // closes the phrase gracefully and the gap just breathes.
            float wait = PadSeconds - CrossfadeSeconds;
            float restChance = night ? 0.4f : 0.25f;
            if (rng.NextDouble() < restChance)
                wait += CrossfadeSeconds + 3f + (float)rng.NextDouble() * 5f;

            _nextPhraseAt = Time.unscaledTime + wait;
        }

        /// <summary>
        /// Synthesizes (or returns the cached) 12 s pad clip for a chord:
        /// per note a sine, a slightly detuned twin for warmth, and a soft
        /// octave harmonic, under a slow attack/release envelope. Clips are
        /// cached per (chord, root), so each mood costs its synth hitch once.
        /// </summary>
        private AudioClip GetPadClip(int[] chord, float rootHz)
        {
            string key = "pad_" + Mathf.RoundToInt(rootHz * 10f) + "_" + string.Join(",", chord);
            if (_clipCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            int samples = Mathf.CeilToInt(PadSeconds * PadSampleRate);
            var d = new float[samples];

            // Keep the summed peak at PadPeak: each note contributes
            // 1 + 0.25 (octave) of raw amplitude across its oscillator pair.
            float noteGain = PadPeak / (chord.Length * 1.25f);

            for (int n = 0; n < chord.Length; n++)
            {
                float f = rootHz * Mathf.Pow(2f, chord[n] / 12f);
                // Phase accumulators in double, wrapped modulo 2*PI (no float drift over 12 s).
                const double TwoPi = 2.0 * System.Math.PI;
                double p0 = 0.0, p1 = 0.0, p2 = 0.0;
                double w0 = TwoPi * f / PadSampleRate;
                double w1 = TwoPi * f * 1.004 / PadSampleRate; // gentle detune
                double w2 = TwoPi * f * 2.0 / PadSampleRate;   // octave shimmer

                for (int i = 0; i < samples; i++)
                {
                    p0 += w0; if (p0 >= TwoPi) p0 -= TwoPi;
                    p1 += w1; if (p1 >= TwoPi) p1 -= TwoPi;
                    p2 += w2; if (p2 >= TwoPi) p2 -= TwoPi;
                    d[i] += noteGain * (float)(0.5 * System.Math.Sin(p0)
                                               + 0.5 * System.Math.Sin(p1)
                                               + 0.25 * System.Math.Sin(p2));
                }
            }

            // Slow envelope: smooth attack in, long release out.
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)PadSampleRate;
                float attack = Mathf.SmoothStep(0f, 1f, t / PadAttack);
                float release = Mathf.SmoothStep(0f, 1f, (PadSeconds - t) / PadRelease);
                d[i] *= attack * release;
            }

            var clip = AudioClip.Create(key, samples, 1, PadSampleRate, false);
            clip.SetData(d, 0);
            _clipCache[key] = clip;
            return clip;
        }

        // --------------------------------------------------------- Plucks

        private void PlayPluck()
        {
            bool night = GameClock.Instance != null && GameClock.Instance.IsNight;
            int season = GameCalendar.Instance != null ? GameCalendar.Instance.SeasonIndex : 0;
            int transpose = SeasonTranspose[((season % SeasonTranspose.Length)
                + SeasonTranspose.Length) % SeasonTranspose.Length];
            if (night) transpose -= 3;

            var scale = night ? NightPentatonic : DayPentatonic;
            var rng = new System.Random((int)(Time.unscaledTime * 1000f) + _phraseCount);

            // One or two octaves above the pad root, on the mood's pentatonic.
            int degree = scale[rng.Next(scale.Length)];
            int octave = 1 + rng.Next(2);
            float f = RootHz * Mathf.Pow(2f, (transpose + degree) / 12f) * Mathf.Pow(2f, octave);

            float pluckVol = 0.5f + (float)rng.NextDouble() * 0.3f;
            if (AudioGuard.TryPlay(AudioBus.Music, "pluck", pluckVol, 0f))
                _pluckSource.PlayOneShot(GetPluckClip(f), pluckVol);

            // Sparser at night. Silence between notes is good.
            float wait = night
                ? 9f + (float)rng.NextDouble() * 11f
                : 5f + (float)rng.NextDouble() * 7f;
            _nextPluckAt = Time.unscaledTime + wait;
        }

        /// <summary>Soft 1.2 s sine pluck with a quiet octave harmonic, cached per pitch.</summary>
        private AudioClip GetPluckClip(float f)
        {
            string key = "pluck_" + Mathf.RoundToInt(f);
            if (_clipCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            const float seconds = 1.2f;
            int samples = Mathf.CeilToInt(seconds * PadSampleRate);
            var d = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)PadSampleRate;
                float decay = Mathf.Exp(-6f * t / 0.9f); // Bleeps.Decay shape
                d[i] = PluckPeak * (Mathf.Sin(2f * Mathf.PI * f * t)
                                    + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t)) * decay;
            }

            var clip = AudioClip.Create(key, samples, 1, PadSampleRate, false);
            clip.SetData(d, 0);
            _clipCache[key] = clip;
            return clip;
        }
    }
}

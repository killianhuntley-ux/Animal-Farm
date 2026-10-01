using System.Collections.Generic;
using AnimalFarm.Spirits;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>The flavors of placeholder bleep <see cref="Bleeps"/> can play.</summary>
    public enum BleepKind
    {
        Click,
        Plant,
        Harvest,
        Feed,
        Soothe,
        Build,
        Coin,
        Alarm,
        Ascend,
        Weave,
        Denied
    }

    /// <summary>
    /// Procedural placeholder audio kit. The game ships no audio assets, so
    /// every clip is synthesized once, lazily, with AudioClip.Create (44.1 kHz
    /// mono) and played through a small pool of 2D AudioSources on a hidden,
    /// on-demand host object. All sounds are deliberately SOFT and short —
    /// peak amplitude is kept at or below 0.35.
    ///
    /// Usage: Bleeps.Play(BleepKind.Click); from anywhere, any time.
    /// </summary>
    public static class Bleeps
    {
        /// <summary>Global kill switch (debug console toggles this later).</summary>
        public static bool Muted;

        // ------------------------------------------------------- Volume bus

        private const string SfxVolumePrefKey = "af_sfx_volume";
        private static float _sfxVolume = 1f;
        private static bool _sfxVolumeLoaded;

        /// <summary>
        /// Master SFX volume (0-1), multiplied into every Play. A device
        /// setting, not game state, so it persists via PlayerPrefs rather
        /// than the save file.
        /// </summary>
        public static float SfxVolume
        {
            get { LoadSfxVolumeOnce(); return _sfxVolume; }
            set
            {
                LoadSfxVolumeOnce();
                _sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(SfxVolumePrefKey, _sfxVolume);
            }
        }

        private static void LoadSfxVolumeOnce()
        {
            if (_sfxVolumeLoaded) return;
            _sfxVolumeLoaded = true;
            _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, 1f));
        }

        private const int SampleRate = 44100;
        private const int KindCount = 11; // keep in sync with BleepKind
        private const int SourceCount = 3;

        private static readonly AudioClip[] _clips = new AudioClip[KindCount];
        private static Host _host;
        private static AudioSource[] _sources;
        private static int _nextSource;

        // ------------------------------------------------------------- Playing

        public static void Play(BleepKind kind, float volume = 1f)
        {
            if (Muted || volume <= 0f || !Application.isPlaying) return;
            if (SfxVolume <= 0f) return; // bus turned all the way down

            var clip = GetClip(kind);
            if (clip == null) return;

            var source = NextSource();
            if (source != null)
                source.PlayOneShot(clip, Mathf.Clamp01(volume) * SfxVolume);
        }

        // ---------------------------------------------------------------- Host

        /// <summary>Invisible scene object owning the pooled AudioSources.</summary>
        private class Host : MonoBehaviour { }

        private static AudioSource NextSource()
        {
            if (_host == null)
            {
                var go = new GameObject("Bleeps_Audio");
                go.hideFlags = HideFlags.HideInHierarchy;
                _host = go.AddComponent<Host>();

                _sources = new AudioSource[SourceCount];
                for (int i = 0; i < SourceCount; i++)
                {
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f; // pure 2D UI-style audio
                    _sources[i] = s;
                }
                // Single-scene game: no DontDestroyOnLoad needed. If the scene
                // ever reloads, the host simply gets rebuilt on next Play.
            }

            var src = _sources[_nextSource];
            _nextSource = (_nextSource + 1) % SourceCount;
            return src;
        }

        // ----------------------------------------------------------- Synthesis

        private static AudioClip GetClip(BleepKind kind)
        {
            int i = (int)kind;
            if (_clips[i] == null) _clips[i] = Generate(kind);
            return _clips[i];
        }

        private static AudioClip Generate(BleepKind kind)
        {
            float[] data;
            switch (kind)
            {
                case BleepKind.Click:   data = GenClick(); break;
                case BleepKind.Plant:   data = GenChirp(300f, 500f, 0.08f, 0.28f); break;
                case BleepKind.Harvest: data = GenChirp(700f, 400f, 0.09f, 0.28f); break;
                case BleepKind.Feed:    data = GenFeed(); break;
                case BleepKind.Soothe:  data = GenSoothe(); break;
                case BleepKind.Build:   data = GenBuild(); break;
                case BleepKind.Coin:    data = GenCoin(); break;
                case BleepKind.Alarm:   data = GenAlarm(); break;
                case BleepKind.Ascend:  data = GenArpeggio(false); break;
                case BleepKind.Weave:   data = GenArpeggio(true); break;
                case BleepKind.Denied:  data = GenDenied(); break;
                default:                data = GenClick(); break;
            }

            var clip = AudioClip.Create("Bleep_" + kind, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float[] NewBuffer(float seconds) =>
            new float[Mathf.CeilToInt(seconds * SampleRate)];

        /// <summary>Exponential decay: 1 at t=0, ~0.002 at t=life.</summary>
        private static float Decay(float t, float life) =>
            Mathf.Exp(-6f * t / Mathf.Max(life, 0.001f));

        /// <summary>30 ms soft square blip at 880 Hz, fast decay.</summary>
        private static float[] GenClick()
        {
            var d = NewBuffer(0.03f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float sq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 880f * t)); // raw square
                d[i] = 0.18f * sq * Decay(t, 0.025f);                        // soft + fast fade
            }
            return d;
        }

        /// <summary>Two low 160 Hz pulses — a polite "no".</summary>
        private static float[] GenDenied()
        {
            var d = NewBuffer(0.24f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                // Pulse 1 at t=0, pulse 2 at t=0.13; each ~80 ms.
                float lt = t < 0.13f ? t : t - 0.13f;
                if (lt > 0.08f) continue;
                d[i] = 0.3f * Mathf.Sin(2f * Mathf.PI * 160f * lt) * Decay(lt, 0.08f);
            }
            return d;
        }

        /// <summary>Short sine chirp sweeping f0 -&gt; f1 over <paramref name="dur"/> seconds.</summary>
        private static float[] GenChirp(float f0, float f1, float dur, float amp)
        {
            var d = NewBuffer(dur);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = Mathf.Lerp(f0, f1, t / dur);      // linear sweep
                phase += 2f * Mathf.PI * f / SampleRate;    // integrate frequency
                d[i] = amp * Mathf.Sin(phase) * Decay(t, dur);
            }
            return d;
        }

        /// <summary>Double sine blip: 520 Hz then 660 Hz, ~60 ms each.</summary>
        private static float[] GenFeed()
        {
            var d = NewBuffer(0.16f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                bool second = t >= 0.08f;
                float lt = second ? t - 0.08f : t;
                if (lt > 0.06f) continue;
                float f = second ? 660f : 520f;
                d[i] = 0.26f * Mathf.Sin(2f * Mathf.PI * f * lt) * Decay(lt, 0.06f);
            }
            return d;
        }

        /// <summary>Soft 440 Hz swell: short attack, slow ~200 ms release.</summary>
        private static float[] GenSoothe()
        {
            var d = NewBuffer(0.3f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float attack = Mathf.Clamp01(t / 0.08f);          // ease in
                float release = t < 0.1f ? 1f : Decay(t - 0.1f, 0.2f);
                d[i] = 0.22f * Mathf.Sin(2f * Mathf.PI * 440f * t) * attack * release;
            }
            return d;
        }

        /// <summary>Thunk: 120 Hz sine for 60 ms plus a tiny opening noise burst.</summary>
        private static float[] GenBuild()
        {
            var d = NewBuffer(0.08f);
            var rng = new System.Random(1234); // deterministic noise
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float thunk = t < 0.06f
                    ? 0.32f * Mathf.Sin(2f * Mathf.PI * 120f * t) * Decay(t, 0.06f) : 0f;
                float noise = t < 0.015f
                    ? 0.1f * ((float)rng.NextDouble() * 2f - 1f) * Decay(t, 0.012f) : 0f;
                d[i] = thunk + noise;
            }
            return d;
        }

        /// <summary>Bright 1320 Hz ping, 120 ms, with a quiet octave harmonic.</summary>
        private static float[] GenCoin()
        {
            var d = NewBuffer(0.12f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float fund = Mathf.Sin(2f * Mathf.PI * 1320f * t);
                float harm = 0.35f * Mathf.Sin(2f * Mathf.PI * 2640f * t);
                d[i] = 0.24f * (fund + harm) * Decay(t, 0.12f);
            }
            return d;
        }

        /// <summary>Minor-second wobble: 330/349 Hz alternating every 100 ms, 400 ms total.</summary>
        private static float[] GenAlarm()
        {
            var d = NewBuffer(0.4f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float f = ((int)(t / 0.1f) & 1) == 0 ? 330f : 349f; // E4 vs F4 - uneasy
                d[i] = 0.26f * Mathf.Sin(2f * Mathf.PI * f * t) * Decay(t, 0.45f);
            }
            return d;
        }

        /// <summary>
        /// Gentle major arpeggio: C5/E5/G5 sines starting 0/0.12/0.24 s in and
        /// ringing out over 600 ms total. <paramref name="reversed"/> plays
        /// G5/E5/C5 instead (the Weave variant).
        /// </summary>
        private static float[] GenArpeggio(bool reversed)
        {
            float[] freqs = reversed
                ? new[] { 784f, 659f, 523f }
                : new[] { 523f, 659f, 784f };
            var d = NewBuffer(0.6f);
            for (int n = 0; n < 3; n++)
            {
                float start = n * 0.12f;
                for (int i = Mathf.CeilToInt(start * SampleRate); i < d.Length; i++)
                {
                    float lt = i / (float)SampleRate - start;
                    d[i] += 0.11f * Mathf.Sin(2f * Mathf.PI * freqs[n] * lt) * Decay(lt, 0.45f);
                }
            }
            return d; // 3 notes x 0.11 peak keeps the sum under 0.35
        }
    }

    /// <summary>
    /// Minimal, safe wiring from cheap global signals to bleeps. Added by the
    /// bootstrapper; standalone — every subscription is optional and null-safe.
    /// Everything else (Click, Denied, Alarm, ...) stays available for explicit
    /// Bleeps.Play calls at the call sites that know the context.
    /// </summary>
    public class BleepsWireup : MonoBehaviour
    {
        // Last-seen counts so inventory bleeps fire only on GAINS.
        private readonly Dictionary<string, int> _lastCounts = new Dictionary<string, int>();

        private bool _invSubscribed;
        private bool _terrainSubscribed;
        private bool _spiritSubscribed;
        private float _armedAt;

        private void Start()
        {
            // Brief warm-up so save-restore floods (Inventory.Restore re-fires
            // OnChanged per item) and world-gen terrain paints stay silent.
            _armedAt = Time.unscaledTime + 0.75f;

            if (Inventory.Instance != null)
            {
                foreach (var pair in Inventory.Instance.All)
                    _lastCounts[pair.Key] = pair.Value;
                Inventory.Instance.OnChanged += OnInventoryChanged;
                _invSubscribed = true;
            }

            SpiritManager.NamingRequested += OnNamingRequested; // static event
            _spiritSubscribed = true;

            if (TerrainGrid.Instance != null)
            {
                TerrainGrid.Instance.OnSurfaceChanged += OnSurfaceChanged;
                _terrainSubscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_invSubscribed && Inventory.Instance != null)
                Inventory.Instance.OnChanged -= OnInventoryChanged;

            if (_spiritSubscribed)
                SpiritManager.NamingRequested -= OnNamingRequested;

            if (_terrainSubscribed && TerrainGrid.Instance != null)
                TerrainGrid.Instance.OnSurfaceChanged -= OnSurfaceChanged;
        }

        private bool Armed => Time.unscaledTime >= _armedAt;

        private void OnInventoryChanged(string id, int count)
        {
            _lastCounts.TryGetValue(id, out int prev);
            _lastCounts[id] = count;

            if (!Armed || count <= prev) return; // only gains make a sound

            if (id == "coin") Bleeps.Play(BleepKind.Coin);
            else if (id == "essence") Bleeps.Play(BleepKind.Coin, 0.6f);
            else Bleeps.Play(BleepKind.Harvest, 0.5f);
        }

        private void OnNamingRequested(SpiritAgent agent)
        {
            if (Armed) Bleeps.Play(BleepKind.Ascend, 0.4f); // a happy little chord
        }

        private void OnSurfaceChanged(Vector2Int cell, Surface surface)
        {
            if (Armed) Bleeps.Play(BleepKind.Plant, 0.35f);
        }
    }
}

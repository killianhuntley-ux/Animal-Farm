using System.Threading.Tasks;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// The sit-down theme (muscle 01 "signature moment"): a gentle 24 s loop
    /// of a pentatonic pluck melody over four slow pad chords (D - Bm - G - A),
    /// fully synthesized like Bleeps/AmbientMusic (no audio assets). The
    /// samples are rendered on a worker thread at start-up so the first sit
    /// never hitches; the loop tails wrap around so it is seamless.
    /// While <see cref="Active"/> it fades in over ~3 s, ducks the ambient bed
    /// underneath (AmbientMusic.DuckFor) and fades back out after standing.
    /// Respects Bleeps.Muted and the music volume; softens while paused.
    /// Lives on the shepherd, added by ShepherdRest.
    /// </summary>
    public class RestTheme : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const float StepSeconds = 1.5f;       // 16 steps = a 24 s loop
        private const float TargetPeak = 0.38f;
        private const float FadeInSeconds = 3f;
        private const float FadeOutSeconds = 2.5f;
        private const float ThemeLevel = 0.8f;        // x music volume
        private const float PausedLevel = 0.4f;
        private const float RootHz = 146.83f;         // D3

        // Chords per 4 steps, semitones above D3 (Dmaj7, Bm7, Gmaj7, A).
        private static readonly int[][] Chords =
        {
            new[] { 0, 4, 7, 11 },
            new[] { -3, 0, 4, 7 },
            new[] { -7, -3, 0, 4 },
            new[] { -5, -1, 2, 7 },
        };

        // Melody per step, semitones above D3; Rest = silence.
        private const int Rest = -99;
        private static readonly int[] Melody =
        {
            19, 21, 24, 21,
            19, 16, 19, Rest,
            21, 19, 16, 14,
            16, 19, 23, Rest,
        };

        /// <summary>True while the shepherd is seated; the theme fades toward this.</summary>
        public bool Active;

        private AudioSource _src;
        private AudioClip _clip;
        private volatile float[] _data;
        private Task _task;
        private float _gain;
        private float _pauseSoft = 1f;

        /// <summary>Adds the theme to a host object (once) and returns it.</summary>
        public static RestTheme Ensure(GameObject host)
        {
            var t = host.GetComponent<RestTheme>();
            return t != null ? t : host.AddComponent<RestTheme>();
        }

        private void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = true;
            _src.spatialBlend = 0f; // pure 2D
            _src.volume = 0f;

            // Render off-thread (pure math, no Unity API); sync fallback in Update.
            try { _task = Task.Run(() => { _data = Synthesize(); }); }
            catch (System.Exception) { _task = null; }
        }

        private void OnDestroy()
        {
            if (_src != null) _src.Stop();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime; // music runs on real time, like AmbientMusic

            _gain = Mathf.MoveTowards(_gain, Active ? 1f : 0f,
                dt / (Active ? FadeInSeconds : FadeOutSeconds));

            bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
            _pauseSoft = Mathf.MoveTowards(_pauseSoft, paused ? PausedLevel : 1f, dt / 0.6f);

            float master = (Bleeps.Muted || !AudioGuard.IsAudible(AudioBus.Music) ? 0f : 1f)
                * AmbientMusic.MusicVolume * ThemeLevel;
            _src.volume = _gain * master * _pauseSoft;

            if (_gain <= 0.001f)
            {
                if (_src.isPlaying) _src.Stop();
                return;
            }

            // Duck the generative bed while the theme carries the moment.
            if (AmbientMusic.Instance != null && _gain > 0.02f)
                AmbientMusic.Instance.DuckFor(0.5f);

            if (_clip == null && !TryBuildClip()) return; // still rendering
            if (!_src.isPlaying) _src.Play();
        }

        private bool TryBuildClip()
        {
            var data = _data;
            if (data == null)
            {
                // Worker never finished (or never started): render here, once.
                if (_task == null || _task.IsFaulted || _task.IsCanceled)
                {
                    _data = data = Synthesize();
                }
                else return false;
            }

            _clip = AudioClip.Create("rest_theme", data.Length, 1, SampleRate, false);
            _clip.SetData(data, 0);
            _src.clip = _clip;
            return true;
        }

        /// <summary>Renders the whole loop. Pure math: safe off the main thread.</summary>
        private static float[] Synthesize()
        {
            int steps = Melody.Length;
            int total = Mathf.CeilToInt(steps * StepSeconds * SampleRate);
            var d = new float[total];

            // ---- pads: one soft chord per 4 steps, tails wrap into the next loop ----
            const float padGain = 0.11f;
            float chordSeconds = 4f * StepSeconds;
            for (int c = 0; c < Chords.Length; c++)
            {
                int start = Mathf.RoundToInt(c * chordSeconds * SampleRate);
                int len = Mathf.RoundToInt((chordSeconds + 1.5f) * SampleRate); // + release tail
                float noteGain = padGain / (Chords[c].Length * 1.5f);
                for (int n = 0; n < Chords[c].Length; n++)
                {
                    float f = RootHz * System.MathF.Pow(2f, Chords[c][n] / 12f);
                    // Double phase accumulators wrapped modulo 2*PI: no float drift over the tail.
                    const double TwoPi = 2.0 * System.Math.PI;
                    double w0 = TwoPi * f / SampleRate;
                    double w1 = w0 * 1.004; // gentle detune
                    double p0 = 0.0, p1 = 0.0;
                    for (int i = 0; i < len; i++)
                    {
                        p0 += w0; if (p0 >= TwoPi) p0 -= TwoPi;
                        p1 += w1; if (p1 >= TwoPi) p1 -= TwoPi;
                        float t = i / (float)SampleRate;
                        float attack = Smooth01(t / 1.2f);
                        float release = Smooth01((chordSeconds + 1.5f - t) / 1.5f);
                        d[(start + i) % total] +=
                            noteGain * attack * release * (float)(System.Math.Sin(p0) + 0.5 * System.Math.Sin(p1));
                    }
                }
            }

            // ---- melody: soft plucks on the step grid ----
            const float pluckGain = 0.17f;
            int pluckLen = Mathf.RoundToInt(3f * SampleRate);
            for (int s = 0; s < steps; s++)
            {
                if (Melody[s] == Rest) continue;
                int start = Mathf.RoundToInt(s * StepSeconds * SampleRate);
                float f = RootHz * System.MathF.Pow(2f, Melody[s] / 12f);
                double w = 2.0 * System.Math.PI * f / SampleRate; // double: w * i stays exact
                for (int i = 0; i < pluckLen; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Min(1f, t / 0.006f) * System.MathF.Exp(-t / 0.9f);
                    d[(start + i) % total] += pluckGain * env
                        * (float)(System.Math.Sin(w * i) + 0.25 * System.Math.Sin(2.0 * w * i));
                }
            }

            // ---- normalize to the target peak ----
            float peak = 0.0001f;
            for (int i = 0; i < total; i++)
            {
                float a = System.MathF.Abs(d[i]);
                if (a > peak) peak = a;
            }
            float scale = TargetPeak / peak;
            for (int i = 0; i < total; i++) d[i] *= scale;
            return d;
        }

        private static float Smooth01(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }
    }
}

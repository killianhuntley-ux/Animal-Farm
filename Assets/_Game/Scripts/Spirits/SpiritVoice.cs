using System.Collections.Generic;
using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>What a spirit chirp is trying to say.</summary>
    public enum VoiceIntent { Greet, Happy, Grumpy, Startle, Sleep }

    /// <summary>
    /// Per-species synth voice (muscle 03), extending the Bleeps pattern:
    /// every clip is AudioClip.Create'd once from the species' voice fields
    /// (base pitch / contour / chirp length) and cached per species+intent,
    /// so each species has a recognizable call. Owner mic gibberish replaces
    /// these at skin phase. Honors Bleeps.Muted so the one debug kill switch
    /// silences everything. Amplitudes stay at or below 0.3 (Bleeps house rule).
    ///
    /// Usage: SpiritVoice.Play(species, VoiceIntent.Greet); from anywhere.
    /// </summary>
    public static class SpiritVoice
    {
        private const int SampleRate = 44100;
        private const int SourceCount = 2;
        private const float MaxHz = 2500f; // synthesized voices never go above this
        private const float MinHz = 60f;

        private static readonly Dictionary<string, AudioClip> _clips =
            new Dictionary<string, AudioClip>();
        private static Host _host;
        private static AudioSource[] _sources;
        private static int _nextSource;

        // ------------------------------------------------------------- Playing

        public static void Play(SpiritSpeciesDefinition species, VoiceIntent intent, float volume = 1f)
        {
            if (Bleeps.Muted || volume <= 0f || !Application.isPlaying) return;
            if (AudioGuard.VoiceVolume <= 0f) return; // voice bus turned all the way down

            // Central gate: key = species id + intent. The Voice bus also has a
            // global cap (one call every 0.12 s, 6 a second) so a crowd of
            // spirits startling or greeting together cannot stack into a shriek.
            string id = species != null && !string.IsNullOrEmpty(species.id) ? species.id : "_default";
            if (!AudioGuard.TryPlay(AudioBus.Voice, "voice:" + id + ":" + intent, volume, 0.25f)) return;

            var clip = GetClip(species, intent);
            if (clip == null) return;

            var source = NextSource();
            if (source != null)
                source.PlayOneShot(clip, Mathf.Clamp01(volume) * AudioGuard.VoiceVolume);
        }

        // ---------------------------------------------------------------- Host

        /// <summary>Invisible scene object owning the pooled AudioSources.</summary>
        private class Host : MonoBehaviour { }

        private static AudioSource NextSource()
        {
            if (_host == null)
            {
                var go = new GameObject("SpiritVoice_Audio");
                go.hideFlags = HideFlags.HideInHierarchy;
                _host = go.AddComponent<Host>();

                _sources = new AudioSource[SourceCount];
                for (int i = 0; i < SourceCount; i++)
                {
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f; // 2D, like Bleeps
                    _sources[i] = s;
                }
            }

            var src = _sources[_nextSource];
            _nextSource = (_nextSource + 1) % SourceCount;
            return src;
        }

        // ----------------------------------------------------------- Synthesis

        private static AudioClip GetClip(SpiritSpeciesDefinition species, VoiceIntent intent)
        {
            string id = species != null && !string.IsNullOrEmpty(species.id) ? species.id : "_default";
            string key = id + ":" + (int)intent;
            if (!_clips.TryGetValue(key, out var clip) || clip == null)
            {
                clip = Generate(species, intent, key);
                _clips[key] = clip;
            }
            return clip;
        }

        private static AudioClip Generate(SpiritSpeciesDefinition species, VoiceIntent intent, string key)
        {
            // Voice profile with safe fallbacks for null/unset species.
            float f0 = Mathf.Clamp(
                species != null && species.voiceBasePitch > 0f ? species.voiceBasePitch : 520f,
                80f, 2000f);
            float contour = Mathf.Clamp(species != null ? species.voiceContour : 0.5f, -1f, 1f);
            float len = Mathf.Clamp(
                species != null && species.voiceChirpSeconds > 0f ? species.voiceChirpSeconds : 0.09f,
                0.03f, 0.4f);
            float slide = Mathf.Pow(2f, 0.5f * contour); // up to half an octave, signed by contour

            float[] d;
            switch (intent)
            {
                case VoiceIntent.Greet:
                    // Two quick chirps, the second a touch higher: "oh, it's you!"
                    d = NewBuffer(len * 2f + 0.09f);
                    AddChirp(d, 0f, f0, f0 * slide, len, 0.2f);
                    AddChirp(d, len + 0.05f, f0 * 1.12f, f0 * 1.12f * slide, len, 0.2f);
                    break;

                case VoiceIntent.Happy:
                    d = NewBuffer(len + 0.04f);
                    AddChirp(d, 0f, f0, f0 * slide, len, 0.22f);
                    break;

                case VoiceIntent.Grumpy:
                {
                    // Lower and always sliding down, whatever the species contour.
                    float dur = len * 1.5f;
                    d = NewBuffer(dur + 0.04f);
                    AddChirp(d, 0f, f0 * 0.72f, f0 * 0.72f * 0.76f, dur, 0.24f);
                    break;
                }

                case VoiceIntent.Startle:
                {
                    // Very short, high, sharp attack: an "eep!".
                    float dur = Mathf.Min(len, 0.06f);
                    d = NewBuffer(dur + 0.03f);
                    AddChirp(d, 0f, f0 * 1.5f, f0 * 1.9f, dur, 0.26f, attack: 0.003f);
                    break;
                }

                default: // VoiceIntent.Sleep
                {
                    // Soft, slow settle-down sigh.
                    float dur = len * 2.2f;
                    d = NewBuffer(dur + 0.05f);
                    AddChirp(d, 0f, f0 * 0.8f, f0 * 0.55f, dur, 0.12f, attack: 0.04f);
                    break;
                }
            }

            var clip = AudioClip.Create("Voice_" + key, d.Length, 1, SampleRate, false);
            clip.SetData(d, 0);
            return clip;
        }

        private static float[] NewBuffer(float seconds) =>
            new float[Mathf.CeilToInt(Mathf.Max(0.01f, seconds) * SampleRate)];

        /// <summary>Exponential decay: 1 at t=0, ~0.002 at t=life (Bleeps match).</summary>
        private static float Decay(float t, float life) =>
            Mathf.Exp(-6f * t / Mathf.Max(life, 0.001f));

        /// <summary>Adds one sine chirp sweeping fStart to fEnd into the buffer.</summary>
        private static void AddChirp(float[] d, float startSec, float fStart, float fEnd,
            float dur, float amp, float attack = 0.008f)
        {
            int start = Mathf.CeilToInt(startSec * SampleRate);
            int len = Mathf.CeilToInt(dur * SampleRate);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx >= d.Length) break;
                float t = i / (float)SampleRate;
                float f = Mathf.Clamp(Mathf.Lerp(fStart, fEnd, t / dur), MinHz, MaxHz); // never shrill
                phase += 2f * Mathf.PI * f / SampleRate; // integrate frequency
                float env = Mathf.Clamp01(t / Mathf.Max(0.001f, attack)) * Decay(t, dur);
                d[idx] += amp * Mathf.Sin(phase) * env;
            }
        }
    }
}

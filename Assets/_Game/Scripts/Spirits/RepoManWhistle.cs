using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The Repo-man's off-key whistle (muscle 07): one synthesized clip, built
    /// lazily (Bleeps pattern, no audio assets). A jaunty little tune where
    /// half the notes miss their pitch by a quarter-tone or so, each note
    /// scooping up into place with a wobbly vibrato and a breath of noise.
    /// RepoManAgent owns playback (and routes it through AudioGuard); this
    /// class only makes the clip. Peak amplitude stays at or below 0.25.
    /// </summary>
    public static class RepoManWhistle
    {
        private const int SampleRate = 44100;

        // Hz per note, deliberately a touch sharp or flat of a clean tune.
        private static readonly float[] Freqs =
            { 660f, 748f, 905f, 782f, 662f, 690f, 583f, 650f, 771f, 612f };
        // Seconds per note (the last one is a sad long one).
        private static readonly float[] Durs =
            { 0.20f, 0.20f, 0.30f, 0.18f, 0.22f, 0.22f, 0.34f, 0.18f, 0.22f, 0.50f };
        private const float NoteGap = 0.03f;

        private static AudioClip _clip;

        /// <summary>The cached whistle clip (rebuilt if Unity destroyed it).</summary>
        public static AudioClip Clip
        {
            get
            {
                if (_clip == null) _clip = Build();
                return _clip;
            }
        }

        private static AudioClip Build()
        {
            float total = 0f;
            for (int n = 0; n < Durs.Length; n++) total += Durs[n] + NoteGap;

            var data = new float[Mathf.CeilToInt(total * SampleRate)];
            var rng = new System.Random(707); // deterministic breath noise
            int cursor = 0;

            for (int n = 0; n < Freqs.Length; n++)
            {
                int len = Mathf.CeilToInt(Durs[n] * SampleRate);
                float phase = 0f;
                for (int i = 0; i < len && cursor + i < data.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    // Scoop up into the pitch, then a shaky vibrato.
                    float f = Freqs[n] * (1f - 0.07f * Mathf.Exp(-t / 0.03f));
                    if (t > 0.08f) f *= 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
                    phase += 2f * Mathf.PI * f / SampleRate;

                    float attack = Mathf.Clamp01(t / 0.02f);
                    float release = Mathf.Clamp01((Durs[n] - t) / 0.05f);
                    float env = attack * release;

                    float tone = Mathf.Sin(phase) + 0.08f * Mathf.Sin(phase * 2f);
                    float breath = 0.03f * ((float)rng.NextDouble() * 2f - 1f);
                    data[cursor + i] = 0.2f * (tone + breath) * env;
                }
                cursor += len + Mathf.CeilToInt(NoteGap * SampleRate);
            }

            var clip = AudioClip.Create("RepoManWhistle", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

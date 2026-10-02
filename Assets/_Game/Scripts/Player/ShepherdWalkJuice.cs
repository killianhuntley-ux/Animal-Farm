using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Walk juice (muscle 01, procedural, no art/audio assets): a footstep per
    /// stride that changes with the surface underfoot (grass swish, dirt
    /// crunch, scrub rustle, sand hiss, mud squelch, water slosh on the
    /// wadeable shallow rim with a splash puff) plus a little leaf
    /// scatter when you walk through grass. Strides are distance-based, so
    /// sprinting naturally quickens the rhythm. Clips are synthesized once per
    /// surface (3 variants, seeded) like Bleeps. Respects Bleeps.Muted and the
    /// SFX bus. Self-spawns onto the Player at scene load.
    /// </summary>
    public class ShepherdWalkJuice : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const float StrideLength = 0.95f;     // world units per footstep
        private const float MinMoveSpeed = 0.6f;
        private const float BaseVolume = 0.2f;
        private const int Variants = 3;

        private static readonly Color LeafGreen = new Color(0.45f, 0.72f, 0.34f, 0.9f);
        private static readonly Color SplashBlue = new Color(0.62f, 0.80f, 0.90f, 0.95f);
        private static readonly Color MudBrown = new Color(0.31f, 0.23f, 0.16f, 0.95f);

        private ShepherdController _ctrl;
        private AudioSource _srcLeft;   // one source per foot: a step never retunes the other's tail
        private AudioSource _srcRight;
        private float _stride;
        private bool _leftFoot;
        private readonly Dictionary<Surface, AudioClip[]> _clips = new Dictionary<Surface, AudioClip[]>();

        /// <summary>Self-spawn: finds the shepherd after the scene loads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null || player.GetComponent<ShepherdWalkJuice>() != null) return;
            player.AddComponent<ShepherdWalkJuice>();
        }

        private void Awake()
        {
            _ctrl = GetComponent<ShepherdController>();
            _srcLeft = gameObject.AddComponent<AudioSource>();
            _srcLeft.playOnAwake = false;
            _srcLeft.spatialBlend = 0f;
            _srcRight = gameObject.AddComponent<AudioSource>();
            _srcRight.playOnAwake = false;
            _srcRight.spatialBlend = 0f;
        }

        private void Update()
        {
            if (_ctrl == null || Time.timeScale == 0f) return;

            float speed = _ctrl.Velocity.magnitude;
            if (speed < MinMoveSpeed)
            {
                _stride = StrideLength * 0.55f; // the first step lands soon after setting off
                return;
            }

            _stride += speed * Time.deltaTime;
            if (_stride < StrideLength) return;
            _stride -= StrideLength;
            Step();
        }

        private void Step()
        {
            Vector3 feet = transform.position + Vector3.down * 0.3f;

            Surface surface = Surface.Scrub;
            bool wading = false;
            var grid = TerrainGrid.Instance;
            if (grid != null && grid.TryWorldToCell(feet, out var cell))
            {
                surface = grid.GetSurface(cell);
                // The shallow rim is walkable Water: slosh + splash (GetSurface alone
                // would miss it in a locked parcel, so ask the grid directly).
                if (grid.IsShallowRim(cell)) { surface = Surface.Water; wading = true; }
            }

            _leftFoot = !_leftFoot;

            if (!Bleeps.Muted && Bleeps.SfxVolume > 0f
                && AudioGuard.TryPlay(AudioBus.Sfx, "footstep", BaseVolume, 0.08f))
            {
                var clips = GetClips(surface);
                float vol = BaseVolume * Bleeps.SfxVolume * (_ctrl.IsSprinting ? 1.25f : 1f);
                var src = _leftFoot ? _srcLeft : _srcRight;
                // Pitch is per-source, so only retune while this foot's last step has died away.
                if (!src.isPlaying)
                    src.pitch = (_leftFoot ? 0.94f : 1.06f) * Random.Range(0.96f, 1.04f);
                src.PlayOneShot(clips[Random.Range(0, clips.Length)], vol);
            }

            // Walking through grass leaves a small living trail of leaves.
            if (surface == Surface.Grass)
                Puffs.Burst(feet, LeafGreen, 2, 0.6f, 0.4f, 0.06f);
            else if (wading) // a small splash kicked up at each wading step
                Puffs.Burst(feet, SplashBlue, 4, 1.1f, 0.4f, 0.07f);
            else if (surface == Surface.Mud)
                Puffs.Burst(feet, MudBrown, 2, 0.5f, 0.35f, 0.06f);
        }

        // ------------------------------------------------------- Synthesis

        private AudioClip[] GetClips(Surface s)
        {
            if (_clips.TryGetValue(s, out var cached)) return cached;

            var set = new AudioClip[Variants];
            for (int v = 0; v < Variants; v++)
                set[v] = Synthesize(s, v);
            _clips[s] = set;
            return set;
        }

        /// <summary>Short noise-based foot sound; the shape per surface is the whole identity.</summary>
        private static AudioClip Synthesize(Surface s, int variant)
        {
            float seconds = s == Surface.Water ? 0.28f : s == Surface.Mud ? 0.2f : s == Surface.Grass ? 0.16f : 0.12f;
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var d = new float[n];
            var rng = new System.Random(7000 + (int)s * 31 + variant * 7);

            float low = 0f; // one-pole lowpass state
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float sample;

                switch (s)
                {
                    case Surface.Grass: // swish: soft band-ish noise, slow attack, quick fade
                    {
                        low += (noise - low) * 0.55f;
                        float hi = noise - low * 0.6f;
                        float env = Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t / 0.05f);
                        sample = hi * env * 0.55f;
                        break;
                    }
                    case Surface.Dirt: // crunch: two or three gritty clicks
                    {
                        low += (noise - low) * 0.35f;
                        float env = Click(t, 0f, 0.012f) + 0.7f * Click(t, 0.028f + variant * 0.006f, 0.014f)
                                    + 0.4f * Click(t, 0.055f, 0.016f);
                        sample = (noise * 0.6f + low * 0.7f) * env;
                        break;
                    }
                    case Surface.Sand: // hiss: dark, longer, no clicks
                    {
                        low += (noise - low) * 0.18f;
                        float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t / 0.06f);
                        sample = low * env * 1.6f;
                        break;
                    }
                    case Surface.Water: // slosh: low wobbling noise + a bubble blip
                    {
                        low += (noise - low) * 0.12f;
                        float wob = 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * (22f + variant * 4f));
                        float env = Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t / 0.1f);
                        float blip = Mathf.Sin(t * 2f * Mathf.PI * (380f + variant * 60f)) * Mathf.Exp(-t / 0.025f) * 0.25f;
                        sample = low * wob * env * 1.8f + blip;
                        break;
                    }
                    case Surface.Mud: // squelch: dull wet noise with a low sucking pop
                    {
                        low += (noise - low) * 0.2f;
                        float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t / 0.07f);
                        float pop = Mathf.Sin(t * 2f * Mathf.PI * (150f + variant * 25f) * (1f + t * 6f))
                                    * Mathf.Exp(-t / 0.04f) * 0.35f;
                        sample = low * env * 1.7f + pop;
                        break;
                    }
                    default: // Scrub: dry crackle, quieter and finer than dirt
                    {
                        low += (noise - low) * 0.5f;
                        float env = Click(t, 0f, 0.01f) + 0.5f * Click(t, 0.02f + variant * 0.005f, 0.012f);
                        sample = (noise - low * 0.4f) * env * 0.7f;
                        break;
                    }
                }

                d[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create("step_" + s + "_" + variant, n, 1, SampleRate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>Exponential click envelope starting at <paramref name="start"/>.</summary>
        private static float Click(float t, float start, float decay) =>
            t < start ? 0f : Mathf.Exp(-(t - start) / decay);
    }
}

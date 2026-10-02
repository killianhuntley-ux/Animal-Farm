using System;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Mood-at-a-glance animation state (muscle 03, verdict 8). Maps onto
    /// SpiritAgent.MoodBand, with one extra way in: a starving resident reads
    /// as sick even when its Spirit is still high.
    /// </summary>
    public enum SpiritAnimState { Happy, Neutral, SadSick }

    /// <summary>How the body moves while it hovers: the "bob style".</summary>
    public enum SpiritBobStyle
    {
        Float,   // gentle sine hover (the original ghost float)
        Bounce,  // springy hops (rabbit-ish)
        Waddle,  // side-to-side rocking steps
        Sway,    // slow lateral sway with a lazy figure-eight
        Flutter  // quick layered wobble (moth-ish)
    }

    /// <summary>
    /// One posture + pace set: how a species carries itself in a single mood
    /// state. Pure data; SpiritAgent.TickVisuals / TickWander read it.
    /// Defaults are a neutral Float so an unauthored pose changes nothing.
    /// </summary>
    [Serializable]
    public class SpiritPose
    {
        [Tooltip("Shape of the hover motion.")]
        public SpiritBobStyle bobStyle = SpiritBobStyle.Float;
        [Tooltip("Multiplies the base bob frequency (higher = livelier).")]
        public float bobFreqMul = 1f;
        [Tooltip("Multiplies the base bob amplitude (higher = bouncier).")]
        public float bobAmpMul = 1f;
        [Tooltip("Multiplies wander speed (below 1 = dragging).")]
        public float speedMul = 1f;
        [Tooltip("World-unit offset of the whole body; negative = drooped.")]
        public float bodyYOffset;
        [Tooltip("Body squash: x = width, y = height. Upright = taller, drooped = wider and shorter.")]
        public Vector2 bodyScale = Vector2.one;
        [Tooltip("Static lean in degrees (positive leans left / counter-clockwise).")]
        public float tiltDegrees;
        [Tooltip("Slow rocking amplitude in degrees on top of the lean.")]
        public float swayDegrees;
        [Tooltip("Colour saturation: 1 = authored tint, lower = washed out toward grey.")]
        public float saturation = 1f;

        /// <summary>Neutral float with no posture change; also used for non-expressive spirits.</summary>
        public static readonly SpiritPose Plain = new SpiritPose();

        /// <summary>General grammar default: upright and bouncy.</summary>
        public static SpiritPose DefaultHappy() => new SpiritPose
        {
            bobFreqMul = 1.35f, bobAmpMul = 1.3f,
            bodyScale = new Vector2(0.97f, 1.04f)
        };

        public static SpiritPose DefaultNeutral() => new SpiritPose();

        /// <summary>General grammar default: drooped, dragging, washed out.</summary>
        public static SpiritPose DefaultSadSick() => new SpiritPose
        {
            bobFreqMul = 0.6f, bobAmpMul = 0.7f, speedMul = 0.75f,
            bodyYOffset = -0.08f,
            bodyScale = new Vector2(1.05f, 0.9f),
            saturation = 0.6f
        };

        /// <summary>
        /// Positional bob for this pose's style at a given time. freq (Hz) and
        /// amp (world units) arrive already scaled by the caller; this only
        /// shapes the motion. styleTilt is extra rotation in degrees.
        /// </summary>
        public Vector2 Bob(float time, float freq, float amp, out float styleTilt)
        {
            float phase = time * freq * 2f * Mathf.PI;
            styleTilt = 0f;
            switch (bobStyle)
            {
                case SpiritBobStyle.Bounce:
                    // Hop arcs: |sin| gives a sharp floor-touch and a rounded top.
                    return new Vector2(0f, Mathf.Abs(Mathf.Sin(phase * 0.5f)) * amp * 2f - amp);

                case SpiritBobStyle.Waddle:
                    styleTilt = Mathf.Sin(phase) * 6f;
                    return new Vector2(Mathf.Sin(phase) * 0.035f, Mathf.Abs(Mathf.Cos(phase)) * amp * 0.9f);

                case SpiritBobStyle.Sway:
                    styleTilt = Mathf.Cos(phase) * 4f;
                    return new Vector2(Mathf.Sin(phase) * 0.07f, Mathf.Sin(phase * 2f) * amp * 0.4f);

                case SpiritBobStyle.Flutter:
                    return new Vector2(Mathf.Sin(phase * 2.3f) * 0.03f,
                        Mathf.Sin(phase) * amp + Mathf.Sin(phase * 3.7f) * amp * 0.45f);

                default: // Float
                    return new Vector2(0f, Mathf.Sin(phase) * amp);
            }
        }
    }

    /// <summary>
    /// A species' three-state animation set (muscle 03 verdict 8): happy,
    /// neutral and sad/sick poses. Same grammar for everyone (upright and
    /// bouncy vs drooped and dragging) but species-dependent expression -
    /// authored per species in ContentBootstrapper.
    /// </summary>
    [Serializable]
    public class SpiritAnimProfile
    {
        public SpiritPose happy = SpiritPose.DefaultHappy();
        public SpiritPose neutral = SpiritPose.DefaultNeutral();
        public SpiritPose sadSick = SpiritPose.DefaultSadSick();

        /// <summary>Pose for a state; falls back to Plain if a slot was nulled.</summary>
        public SpiritPose Get(SpiritAnimState state)
        {
            SpiritPose p;
            switch (state)
            {
                case SpiritAnimState.Happy: p = happy; break;
                case SpiritAnimState.SadSick: p = sadSick; break;
                default: p = neutral; break;
            }
            return p ?? SpiritPose.Plain;
        }
    }
}

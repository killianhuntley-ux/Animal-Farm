using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Drives the scene's global Light2D from the GameClock: color gradient and
    /// intensity curve over one day (0 = midnight). Defaults are built in code
    /// but serialized, so they can be tweaked in the Inspector.
    /// </summary>
    public class DayNightLight : MonoBehaviour
    {
        [SerializeField] private Light2D targetLight;

        [Tooltip("Light color across the day. Time 0 = midnight, 0.5 = noon.")]
        [SerializeField] private Gradient colorOverDay = DefaultColorGradient();

        [Tooltip("Light intensity across the day. Time 0 = midnight, 0.5 = noon.")]
        [SerializeField] private AnimationCurve intensityOverDay = DefaultIntensityCurve();

        private void Awake()
        {
            if (targetLight == null)
                targetLight = FindGlobalLight();

            if (targetLight == null)
                Debug.LogWarning("[DayNightLight] No global Light2D found in scene.", this);
        }

        private void LateUpdate()
        {
            if (targetLight == null || GameClock.Instance == null) return;

            float t = GameClock.Instance.NormalizedTime;
            targetLight.color = colorOverDay.Evaluate(t);
            targetLight.intensity = intensityOverDay.Evaluate(t);
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

        private static Gradient DefaultColorGradient()
        {
            // #1B2340 midnight deep blue → warm peach dawn → near-white noon →
            // orange dusk → back to midnight blue.
            Color midnight = new Color(0x1B / 255f, 0x23 / 255f, 0x40 / 255f);
            Color dawn = new Color(1.00f, 0.78f, 0.63f); // warm peach
            Color noon = new Color(1.00f, 0.98f, 0.94f); // near-white
            Color dusk = new Color(1.00f, 0.60f, 0.35f); // orange

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(midnight, 0.00f),
                    new GradientColorKey(dawn,     0.25f),
                    new GradientColorKey(noon,     0.50f),
                    new GradientColorKey(dusk,     0.75f),
                    new GradientColorKey(midnight, 1.00f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return gradient;
        }

        private static AnimationCurve DefaultIntensityCurve()
        {
            var curve = new AnimationCurve(
                new Keyframe(0.00f, 0.35f),
                new Keyframe(0.25f, 0.90f),
                new Keyframe(0.50f, 1.00f),
                new Keyframe(0.75f, 0.90f),
                new Keyframe(1.00f, 0.35f));

            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);

            return curve;
        }
    }
}

using UnityEngine;

namespace AnimalFarm.Onboarding
{
    /// <summary>
    /// The tutor (slice 10): a disembodied shining light - "a god", unnamed -
    /// that teaches by POINTING, never lecturing (GDD 4.4). A soft glowing orb
    /// built entirely in code: a warm core sprite plus a larger, fainter halo
    /// child. It drifts (SmoothDamp) to hover above whatever the current
    /// onboarding step wants the shepherd to look at, or idles lazily near the
    /// shepherd when there is nothing specific to point at.
    /// </summary>
    public class GuideLight : MonoBehaviour
    {
        private enum Mode { Idle, PointPosition, PointTransform, Follow }

        // ---- tuning ----------------------------------------------------------

        private const float HoverHeight = 1.2f;          // hover above the pointed target
        private const float PointSmoothTime = 0.6f;      // SmoothDamp; settles in ~2s
        private const float FollowSmoothTime = 1.1f;     // lazier drift while idling
        private const float BobAmplitude = 0.12f;
        private const float BobFrequency = 0.6f;         // Hz
        private const float PulseFrequency = 0.5f;       // Hz, gentle alpha breathing
        private const float PulseDepth = 0.18f;          // fraction of alpha the pulse removes
        private const float FadeSpeed = 1.5f;            // visibility fade, per second
        private const float CoreAlpha = 0.9f;
        private const float HaloAlpha = 0.25f;
        private static readonly Vector3 FollowOffset = new Vector3(1.5f, 1.5f, 0f);
        private static readonly Color WarmWhite = new Color(1f, 0.95f, 0.8f, 1f);

        // ---- serialized ------------------------------------------------------

        [Tooltip("Soft radial glow sprite; the OnboardingManager passes this in via Init.")]
        [SerializeField] private Sprite glowSprite;

        [Tooltip("Sprite material (e.g. Sprite-Lit/Unlit-Default); null is fine.")]
        [SerializeField] private Material spriteMaterial;

        // ---- state -----------------------------------------------------------

        private SpriteRenderer _core;
        private SpriteRenderer _halo;
        private bool _built;

        private Mode _mode = Mode.Follow;
        private Vector3 _pointPosition;
        private Transform _pointTransform;
        private Transform _playerTransform;

        private Vector3 _velocity;         // SmoothDamp scratch
        private float _fade = 1f;          // current visibility 0..1
        private float _fadeTarget = 1f;

        // ---- setup -----------------------------------------------------------

        /// <summary>Spawn-time configuration (the manager creates us in code).</summary>
        public void Init(Sprite sprite, Material material)
        {
            glowSprite = sprite;
            spriteMaterial = material;
            BuildVisuals();
        }

        private void Start()
        {
            // If placed in a scene by hand (inspector-assigned fields), still build.
            BuildVisuals();
        }

        private void BuildVisuals()
        {
            if (_built) return;
            _built = true;

            _core = MakeOrb("Core", 0.8f, CoreAlpha, 150);
            _halo = MakeOrb("Halo", 1.6f, HaloAlpha, 149);
        }

        private SpriteRenderer MakeOrb(string childName, float scale, float alpha, int sortingOrder)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = glowSprite;
            renderer.sortingOrder = sortingOrder;
            if (spriteMaterial != null) renderer.material = spriteMaterial;

            var color = WarmWhite;
            color.a = alpha;
            renderer.color = color;
            return renderer;
        }

        // ---- public surface ----------------------------------------------------

        /// <summary>Drift over and hover above a fixed world position.</summary>
        public void PointAt(Vector3 worldPos)
        {
            _mode = Mode.PointPosition;
            _pointPosition = new Vector3(worldPos.x, worldPos.y, 0f);
        }

        /// <summary>Drift over and hover above a moving target (live-updating).</summary>
        public void PointAt(Transform target)
        {
            if (target == null) { FollowShepherd(); return; }
            _mode = Mode.PointTransform;
            _pointTransform = target;
        }

        /// <summary>Idle near the shepherd (up-right offset, lazy drift).</summary>
        public void FollowShepherd()
        {
            _mode = Mode.Follow;
            _pointTransform = null;
        }

        /// <summary>Fades the light in or out (it never pops).</summary>
        public void SetVisible(bool visible)
        {
            _fadeTarget = visible ? 1f : 0f;
        }

        // ---- per-frame -----------------------------------------------------------

        private void Update()
        {
            TickFade();
            if (_fade <= 0f) return; // fully faded out: hold position, skip motion

            Vector3 target;
            float smoothTime;
            if (!ResolveTarget(out target, out smoothTime)) return;

            // Bob rides on top of the smoothed hover point.
            target.y += Mathf.Sin(Time.time * BobFrequency * 2f * Mathf.PI) * BobAmplitude;
            target.z = 0f;

            transform.position = Vector3.SmoothDamp(
                transform.position, target, ref _velocity, smoothTime);

            TickPulse();
        }

        /// <summary>Where the orb wants to hover right now. False if nowhere to go.</summary>
        private bool ResolveTarget(out Vector3 target, out float smoothTime)
        {
            switch (_mode)
            {
                case Mode.PointPosition:
                    target = _pointPosition + Vector3.up * HoverHeight;
                    smoothTime = PointSmoothTime;
                    return true;

                case Mode.PointTransform:
                    if (_pointTransform == null)
                    {
                        // Target despawned under us; fall back to idling.
                        FollowShepherd();
                        return ResolveTarget(out target, out smoothTime);
                    }
                    target = _pointTransform.position + Vector3.up * HoverHeight;
                    smoothTime = PointSmoothTime;
                    return true;

                case Mode.Follow:
                    var player = FindPlayer();
                    if (player == null)
                    {
                        target = transform.position;
                        smoothTime = FollowSmoothTime;
                        return false;
                    }
                    target = player.position + FollowOffset;
                    smoothTime = FollowSmoothTime;
                    return true;

                default:
                    target = transform.position;
                    smoothTime = FollowSmoothTime;
                    return false;
            }
        }

        private Transform FindPlayer()
        {
            if (_playerTransform != null) return _playerTransform;
            var go = GameObject.FindWithTag("Player");
            if (go != null) _playerTransform = go.transform;
            return _playerTransform;
        }

        // ---- visuals ----------------------------------------------------------

        private void TickFade()
        {
            if (Mathf.Approximately(_fade, _fadeTarget))
            {
                ApplyRendererState();
                return;
            }
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, FadeSpeed * Time.deltaTime);
            ApplyRendererState();
            TickPulse();
        }

        private void ApplyRendererState()
        {
            bool on = _fade > 0f;
            if (_core != null && _core.gameObject.activeSelf != on) _core.gameObject.SetActive(on);
            if (_halo != null && _halo.gameObject.activeSelf != on) _halo.gameObject.SetActive(on);
        }

        /// <summary>Gentle always-on alpha breathing, scaled by the fade.</summary>
        private void TickPulse()
        {
            float pulse = 1f - PulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * PulseFrequency * 2f * Mathf.PI));
            float mul = pulse * _fade;

            SetAlpha(_core, CoreAlpha * mul);
            SetAlpha(_halo, HaloAlpha * mul);
        }

        private static void SetAlpha(SpriteRenderer renderer, float a)
        {
            if (renderer == null) return;
            var c = renderer.color;
            c.a = Mathf.Clamp01(a);
            renderer.color = c;
        }
    }
}

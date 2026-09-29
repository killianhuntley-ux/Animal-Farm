using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Procedural walk/idle animation over swappable skins. Skins are pure data
    /// (SkinDefinition); the same bob/squash animation drives any skin, which
    /// proves the skin/animation separation for the slice DoD.
    /// </summary>
    public class ShepherdVisual : MonoBehaviour
    {
        [Header("Skins")]
        [SerializeField] private SkinDefinition skin;
        [SerializeField] private SkinDefinition alternateSkin;

        [Header("Renderers (fallback: children named Body / Head)")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer headRenderer;

        [Header("Walk Feel")]
        [SerializeField] private float bobAmplitude = 0.035f;
        [SerializeField] private float bobFrequency = 0.55f;     // bob cycles scale with speed (~2.5Hz at walk)
        [SerializeField] private float squashAmount = 0.035f;
        [SerializeField] private float idleBobAmplitude = 0.015f;
        [SerializeField] private float idleBobSpeed = 1.2f;      // slow breathing

        private ShepherdController _controller;
        private Vector3 _bodyInitialPos;
        private Vector3 _headInitialPos;
        private Vector3 _bodyInitialScale;
        private float _phase;

        private void Awake()
        {
            _controller = GetComponentInParent<ShepherdController>();

            if (bodyRenderer == null) bodyRenderer = FindChildRenderer("Body");
            if (headRenderer == null) headRenderer = FindChildRenderer("Head");

            if (bodyRenderer != null)
            {
                _bodyInitialPos = bodyRenderer.transform.localPosition;
                _bodyInitialScale = bodyRenderer.transform.localScale;
            }
            if (headRenderer != null)
                _headInitialPos = headRenderer.transform.localPosition;
        }

        private void Start()
        {
            ApplySkin(skin);
        }

        private SpriteRenderer FindChildRenderer(string childName)
        {
            var direct = transform.Find(childName);
            if (direct != null)
            {
                var sr = direct.GetComponent<SpriteRenderer>();
                if (sr != null) return sr;
            }
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.name == childName) return sr;
            return null;
        }

        /// <summary>Sets sprites and tint from a skin. Animation is untouched.</summary>
        public void ApplySkin(SkinDefinition s)
        {
            if (s == null) return;
            skin = s;

            if (bodyRenderer != null)
            {
                bodyRenderer.sprite = s.body;
                bodyRenderer.color = s.tint;
            }
            if (headRenderer != null)
            {
                headRenderer.sprite = s.head;
                headRenderer.color = s.tint;
            }
        }

        [ContextMenu("Toggle Skin")]
        private void ToggleSkin()
        {
            if (alternateSkin == null) return;
            (skin, alternateSkin) = (alternateSkin, skin);
            ApplySkin(skin);
        }

        private void Update()
        {
            float speed = _controller != null ? _controller.Velocity.magnitude : 0f;
            bool moving = speed > 0.1f;

            // Walk bob: frequency scales with speed. Idle: slow gentle breathing.
            float frequency = moving ? bobFrequency * speed : idleBobSpeed;
            float amplitude = moving ? bobAmplitude : idleBobAmplitude;

            _phase += frequency * Mathf.PI * 2f * Time.deltaTime;
            float wave = Mathf.Sin(_phase);
            float bob = wave * amplitude;

            if (bodyRenderer != null)
            {
                var bodyT = bodyRenderer.transform;
                bodyT.localPosition = _bodyInitialPos + Vector3.up * bob;

                float squash = moving ? 1f - Mathf.Abs(wave) * squashAmount : 1f;
                bodyT.localScale = new Vector3(
                    _bodyInitialScale.x,
                    _bodyInitialScale.y * squash,
                    _bodyInitialScale.z);
            }

            if (headRenderer != null)
                headRenderer.transform.localPosition = _headInitialPos + Vector3.up * (bob * 1.15f);

            // Facing: flip both renderers when looking left; hold flip on pure up/down.
            if (_controller != null)
            {
                float faceX = _controller.FacingDir.x;
                if (Mathf.Abs(faceX) > 0.01f)
                {
                    bool flip = faceX < 0f;
                    if (bodyRenderer != null) bodyRenderer.flipX = flip;
                    if (headRenderer != null) headRenderer.flipX = flip;
                }
            }
        }
    }
}

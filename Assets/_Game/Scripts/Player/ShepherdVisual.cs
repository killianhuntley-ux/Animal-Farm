using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Procedural walk/idle animation over swappable skins. Skins are pure data
    /// (SkinDefinition); the same bob/squash animation drives any skin, which
    /// proves the skin/animation separation for the slice DoD.
    ///
    /// Muscle 01 additions:
    /// - Four-direction facing: the pose (up/down/side) is picked from the
    ///   dominant axis of FacingDir. Skins may provide up/down sprites; a null
    ///   slot falls back to the side sprites, so old skins keep working (the
    ///   side pose + flipX carries the direction then, exactly as before).
    /// - Characterful sprint: a slightly exaggerated scamper bob plus little
    ///   procedural dust puffs at the feet while sprinting.
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

        [Header("Sprint Feel")]
        [SerializeField] private float sprintBobMultiplier = 1.45f;   // exaggerated scamper
        [SerializeField] private float sprintSquashMultiplier = 1.5f;
        [SerializeField] private float dustPuffInterval = 0.16f;      // seconds between foot puffs

        // Seated pose (sit & rest): body 56px = 0.875u, squashed to 72% with the
        // feet kept planted (drop = half the lost height); the head follows the top.
        private const float SitEaseSeconds = 0.35f;
        private const float SitBodyScaleY = 0.72f;
        private const float SitBodyDrop = 0.12f;
        private const float SitHeadDrop = 0.25f;

        private static readonly Color DustColor = new Color(0.76f, 0.68f, 0.54f, 0.75f);

        private ShepherdController _controller;
        private Vector3 _bodyInitialPos;
        private Vector3 _headInitialPos;
        private Vector3 _bodyInitialScale;
        private float _phase;
        private float _nextDustTime;
        private float _sit01;
        private FacingPose _pose = FacingPose.Down; // FacingDir defaults to down

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

            if (bodyRenderer != null) bodyRenderer.color = s.tint;
            if (headRenderer != null) headRenderer.color = s.tint;
            RefreshPoseSprites();
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
            bool sprinting = _controller != null && _controller.IsSprinting;

            // Walk bob: frequency scales with speed. Idle: slow gentle breathing.
            // Sprinting exaggerates the whole scamper (muscle 01).
            float frequency = moving ? bobFrequency * speed : idleBobSpeed;
            float amplitude = moving ? bobAmplitude : idleBobAmplitude;
            float squash = squashAmount;
            if (sprinting)
            {
                amplitude *= sprintBobMultiplier;
                squash *= sprintSquashMultiplier;
            }

            _phase += frequency * Mathf.PI * 2f * Time.deltaTime;
            float wave = Mathf.Sin(_phase);
            float bob = wave * amplitude;

            // Seated pose (muscle 01 sit & rest): ease the body into a squat --
            // squashed, feet planted, head dropped -- and back up on standing.
            _sit01 = Mathf.MoveTowards(_sit01, ShepherdRest.IsSeated ? 1f : 0f, Time.deltaTime / SitEaseSeconds);

            if (bodyRenderer != null)
            {
                var bodyT = bodyRenderer.transform;
                bodyT.localPosition = _bodyInitialPos + Vector3.up * bob + Vector3.down * (SitBodyDrop * _sit01);

                float squashScale = moving ? 1f - Mathf.Abs(wave) * squash : 1f;
                squashScale *= Mathf.Lerp(1f, SitBodyScaleY, _sit01);
                bodyT.localScale = new Vector3(
                    _bodyInitialScale.x,
                    _bodyInitialScale.y * squashScale,
                    _bodyInitialScale.z);
            }

            if (headRenderer != null)
                headRenderer.transform.localPosition = _headInitialPos
                    + Vector3.up * (bob * 1.15f) + Vector3.down * (SitHeadDrop * _sit01);

            UpdateFacing();
            UpdateSprintDust(sprinting);
        }

        // ---- facing ----------------------------------------------------------

        /// <summary>
        /// Picks the pose from FacingDir's dominant axis: side when |x| wins
        /// (flipX carries left, as before), up/down otherwise. Up/down slots
        /// missing on the skin simply fall back to the side sprites.
        /// </summary>
        private void UpdateFacing()
        {
            if (_controller == null) return;

            Vector2 facing = _controller.FacingDir;
            FacingPose pose;
            if (Mathf.Abs(facing.x) >= Mathf.Abs(facing.y))
                pose = Mathf.Abs(facing.x) > 0.01f ? FacingPose.Side : _pose;
            else
                pose = facing.y > 0f ? FacingPose.Up : FacingPose.Down;

            if (pose != _pose)
            {
                _pose = pose;
                RefreshPoseSprites();
            }

            // Flip only while the side pose is active; up/down hold their flip
            // so a fallback (side) sprite keeps its last horizontal lean.
            if (_pose == FacingPose.Side && Mathf.Abs(facing.x) > 0.01f)
            {
                bool flip = facing.x < 0f;
                if (bodyRenderer != null) bodyRenderer.flipX = flip;
                if (headRenderer != null) headRenderer.flipX = flip;
            }
        }

        /// <summary>Pushes the current pose's sprites (with side fallback).</summary>
        private void RefreshPoseSprites()
        {
            if (skin == null) return;
            if (bodyRenderer != null) bodyRenderer.sprite = skin.BodyFor(_pose);
            if (headRenderer != null) headRenderer.sprite = skin.HeadFor(_pose);
        }

        // ---- sprint dust -----------------------------------------------------

        /// <summary>Little dust puffs at the feet while scampering.</summary>
        private void UpdateSprintDust(bool sprinting)
        {
            if (!sprinting || Time.time < _nextDustTime) return;
            _nextDustTime = Time.time + dustPuffInterval;

            Vector3 feet = (_controller != null ? _controller.transform.position : transform.position)
                           + Vector3.down * 0.25f;
            Puffs.Burst(feet, DustColor, 4, 0.9f, 0.3f, 0.08f);
        }
    }
}

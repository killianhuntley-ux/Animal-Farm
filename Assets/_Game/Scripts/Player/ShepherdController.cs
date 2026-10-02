using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Top-down shepherd movement with a tuned accelerate/decelerate feel.
    /// Input is sampled in Update; physics is applied in FixedUpdate.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class ShepherdController : MonoBehaviour, ISaveable
    {
        [Header("Movement Feel")]
        [SerializeField] private float maxSpeed = 4.5f;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float acceleration = 40f;
        [SerializeField] private float deceleration = 50f;

        [Header("Shallow-water wading")]
        [Tooltip("Speed multiplier on a pond's shallow rim, on foot (deep water stays solid).")]
        [SerializeField] private float wadeSpeedFactor = 0.6f;
        [Tooltip("Same, while riding the mount (it wades along, so it slows less).")]
        [SerializeField] private float wadeMountedFactor = 0.8f;

        /// <summary>Current rigidbody velocity.</summary>
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

        /// <summary>True while the shepherd is actually moving.</summary>
        public bool IsMoving => Velocity.sqrMagnitude > 0.01f;

        /// <summary>True while sprint is held and the shepherd is moving.</summary>
        public bool IsSprinting => _sprintHeld && IsMoving;

        /// <summary>Last non-zero input direction (normalized). Defaults to down (toward camera).</summary>
        public Vector2 FacingDir { get; private set; } = Vector2.down;

        /// <summary>True while the feet are over a shallow-rim cell (slowed; sloshing footsteps).</summary>
        public bool IsWading => _wadeBlend > 0.5f;

        private Rigidbody2D _rb;
        private Vector2 _moveInput;
        private bool _sprintHeld;
        private float _wadeBlend; // 0 = dry, 1 = fully wading (eased so the slowdown never snaps)

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            var input = GameInput.Instance;
            if (input == null)
            {
                _moveInput = Vector2.zero;
                _sprintHeld = false;
                return;
            }

            _moveInput = input.Move;
            if (_moveInput.sqrMagnitude > 1f)
                _moveInput.Normalize();

            _sprintHeld = input.SprintHeld;

            // A weighty tool action commits the shepherd in place for the
            // beat (muscle 01) -- ignore movement until the swing releases.
            // Sitting to rest (muscle 01) holds the shepherd still too; the
            // move input pans the camera instead (see ShepherdRest).
            if (ToolController.MovementLocked || ShepherdRest.IsSeated)
            {
                _moveInput = Vector2.zero;
                _sprintHeld = false;
                return; // facing stays pinned on the swing target too
            }

            if (_moveInput.sqrMagnitude > 0.0001f)
                FacingDir = _moveInput.normalized;
        }

        private void FixedUpdate()
        {
            // Wading: the shallow rim is walkable but sluggish. Eased in/out, and it
            // multiplies with the sprint and mount factors (never replaces them).
            var grid = TerrainGrid.Instance;
            bool wet = grid != null && grid.IsWadingAt(_rb.position + Vector2.down * 0.3f); // feet, like the footstep probe
            _wadeBlend = Mathf.MoveTowards(_wadeBlend, wet ? 1f : 0f, 8f * Time.fixedDeltaTime);
            float wade = Mathf.Lerp(1f, PoutyMount.IsRiding ? wadeMountedFactor : wadeSpeedFactor, _wadeBlend);

            float speedCap = maxSpeed * (_sprintHeld ? sprintMultiplier : 1f) * PoutyMount.SpeedFactor(_sprintHeld) * wade; // mount boost (muscle 08)
            Vector2 targetVelocity = _moveInput * speedCap;

            bool hasInput = _moveInput.sqrMagnitude > 0.0001f;
            float rate = hasInput ? acceleration : deceleration;

            _rb.linearVelocity = Vector2.MoveTowards(
                _rb.linearVelocity, targetVelocity, rate * Time.fixedDeltaTime);
        }

        // ---- ISaveable -----------------------------------------------------

        [System.Serializable]
        private struct SaveState
        {
            public float posX, posY;
            public float faceX, faceY;
        }

        public string SaveKey => "shepherd";

        public string Capture()
        {
            var rb = _rb != null ? _rb : GetComponent<Rigidbody2D>();
            var state = new SaveState
            {
                posX = rb.position.x,
                posY = rb.position.y,
                faceX = FacingDir.x,
                faceY = FacingDir.y
            };
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<SaveState>(json);
            var rb = _rb != null ? _rb : GetComponent<Rigidbody2D>();

            rb.position = new Vector2(state.posX, state.posY);
            rb.linearVelocity = Vector2.zero;

            var facing = new Vector2(state.faceX, state.faceY);
            FacingDir = facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector2.down;
        }
    }
}

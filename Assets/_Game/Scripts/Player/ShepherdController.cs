using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
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

        /// <summary>Current rigidbody velocity.</summary>
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

        /// <summary>True while the shepherd is actually moving.</summary>
        public bool IsMoving => Velocity.sqrMagnitude > 0.01f;

        /// <summary>True while sprint is held and the shepherd is moving.</summary>
        public bool IsSprinting => _sprintHeld && IsMoving;

        /// <summary>Last non-zero input direction (normalized). Defaults to down (toward camera).</summary>
        public Vector2 FacingDir { get; private set; } = Vector2.down;

        private Rigidbody2D _rb;
        private Vector2 _moveInput;
        private bool _sprintHeld;

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
            if (ToolController.MovementLocked)
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
            float speedCap = maxSpeed * (_sprintHeld ? sprintMultiplier : 1f);
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

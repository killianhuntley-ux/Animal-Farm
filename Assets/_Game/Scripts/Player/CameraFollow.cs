using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Smooth-follow camera with a velocity lead and scroll zoom. Lives on the
    /// Camera. Freezes entirely while the game is paused (Time.timeScale == 0).
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Transform target;          // fallback: tag "Player"
        [SerializeField] private float smoothTime = 0.25f;
        [SerializeField] private float leadFactor = 0.35f;  // offset toward shepherd velocity

        [Header("Zoom")]
        [SerializeField] private float zoomMin = 3.5f;
        [SerializeField] private float zoomMax = 9f;
        [SerializeField] private float zoomStep = 0.55f;    // ortho units per zoom tick
        [SerializeField] private float zoomTickInterval = 0.09f; // min seconds between ticks
        [SerializeField] private float zoomSmooth = 8f;
        [SerializeField] private float startSize = 6f;

        private const float MaxLead = 1.5f;
        private const float CameraZ = -10f;

        private Camera _cam;
        private ShepherdController _shepherd;
        private Vector3 _followVelocity;
        private float _targetSize;
        private float _lastZoomTick;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _targetSize = Mathf.Clamp(startSize, zoomMin, zoomMax);
            if (_cam != null) _cam.orthographicSize = _targetSize;
        }

        private void Start()
        {
            ResolveTarget();
        }

        // ---- override (competitions point the camera at the arena) ----------

        private bool _overridden;
        private Vector3 _overridePos;

        /// <summary>Camera snaps its follow target to a fixed world position (arena).</summary>
        public void SetOverrideTarget(Transform ignored, Vector3 worldPos)
        {
            _overridden = true;
            _overridePos = worldPos;
        }

        public void ClearOverrideTarget() => _overridden = false;

        private void ResolveTarget()
        {
            if (target == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) target = player.transform;
            }
            _shepherd = target != null ? target.GetComponentInParent<ShepherdController>() : null;
        }

        private void LateUpdate()
        {
            if (Time.timeScale == 0f) return;   // fully frozen while paused
            if (_cam == null) return;

            if (target == null)
            {
                ResolveTarget();
                if (target == null && !_overridden) return;
            }

            // Position: follow the target plus a small lead in the direction of travel.
            Vector2 lead = Vector2.zero;
            if (!_overridden && _shepherd != null)
                lead = Vector2.ClampMagnitude(_shepherd.Velocity * leadFactor, MaxLead);

            Vector3 followPos = _overridden ? _overridePos
                : new Vector3(target.position.x, target.position.y, 0f);
            Vector3 desired = new Vector3(followPos.x + lead.x, followPos.y + lead.y, CameraZ);
            Vector3 pos = Vector3.SmoothDamp(transform.position, desired, ref _followVelocity, smoothTime);
            pos.z = CameraZ;
            transform.position = pos;

            // Zoom: positive delta zooms IN (smaller orthographic size).
            // Rate-limited fixed steps so a one-frame mouse-wheel impulse and a
            // held gamepad shoulder both produce a sane zoom speed.
            float delta = GameInput.Instance != null ? GameInput.Instance.ZoomDelta : 0f;
            if (!Mathf.Approximately(delta, 0f) && Time.unscaledTime - _lastZoomTick >= zoomTickInterval)
            {
                _targetSize = Mathf.Clamp(_targetSize - Mathf.Sign(delta) * zoomStep, zoomMin, zoomMax);
                _lastZoomTick = Time.unscaledTime;
            }

            _cam.orthographicSize = Mathf.Lerp(
                _cam.orthographicSize, _targetSize, 1f - Mathf.Exp(-zoomSmooth * Time.deltaTime));
        }
    }
}

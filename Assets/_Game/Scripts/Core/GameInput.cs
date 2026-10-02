using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Single hub over the Controls.inputactions asset. All gameplay code reads
    /// input from here — nothing else touches the Input System directly.
    /// Polled values are refreshed in Update; discrete presses are C# events.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameInput : MonoBehaviour
    {
        public static GameInput Instance { get; private set; }

        [SerializeField] private InputActionAsset actions;

        // Polled state
        public Vector2 Move { get; private set; }
        public bool SprintHeld { get; private set; }
        public float ZoomDelta { get; private set; }
        public bool UseToolHeld { get; private set; }

        // Discrete presses
        public event Action InteractPressed;
        public event Action FastForwardPressed;
        public event Action PausePressed;
        public event Action ConsoleToggled;
        public event Action UseToolPressed;
        public event Action CycleToolPressed;
        public event Action ToolbeltPressed; // stays live while gameplay is blocked (like Pause/Console)
        public event Action InspectPressed;
        public event Action BuildPressed;
        public event Action RestPressed; // sit & rest toggle (muscle 01)
        public event Action RidePressed; // mount / dismount toggle (muscle 08 pouty mount)

        public InputActionAsset Actions => actions;

        private InputAction _move, _sprint, _zoom, _interact, _fastForward, _pause, _console, _useTool, _cycleTool, _toolbelt, _inspect, _build, _rest, _ride;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var map = actions.FindActionMap("Player", throwIfNotFound: true);
            _move = map.FindAction("Move", true);
            _sprint = map.FindAction("Sprint", true);
            _zoom = map.FindAction("Zoom", true);
            _interact = map.FindAction("Interact", true);
            _fastForward = map.FindAction("FastForward", true);
            _pause = map.FindAction("Pause", true);
            _console = map.FindAction("Console", true);
            _useTool = map.FindAction("UseTool", true);
            _cycleTool = map.FindAction("CycleTool", true);
            _toolbelt = map.FindAction("Toolbelt", true);
            _inspect = map.FindAction("Inspect", true);
            _build = map.FindAction("Build", true);
            _rest = map.FindAction("Rest", true);
            _ride = map.FindAction("Ride", true);

            _interact.performed += _ => InteractPressed?.Invoke();
            _fastForward.performed += _ => FastForwardPressed?.Invoke();
            _pause.performed += _ => PausePressed?.Invoke();
            _console.performed += _ => ConsoleToggled?.Invoke();
            _useTool.performed += _ => UseToolPressed?.Invoke();
            _cycleTool.performed += _ => CycleToolPressed?.Invoke();
            _toolbelt.performed += _ => ToolbeltPressed?.Invoke();
            _inspect.performed += _ => InspectPressed?.Invoke();
            _build.performed += _ => BuildPressed?.Invoke();
            _rest.performed += _ => RestPressed?.Invoke();
            _ride.performed += _ => RidePressed?.Invoke();
        }

        private void OnEnable() => actions.Enable();
        private void OnDisable() => actions.Disable();

        private void Update()
        {
            Move = _move.ReadValue<Vector2>();
            SprintHeld = _sprint.IsPressed();
            ZoomDelta = _zoom.ReadValue<float>();
            UseToolHeld = _useTool.IsPressed();
        }

        /// <summary>
        /// True on the frame any control bound to Interact (honours rebinds) was
        /// pressed. Works while gameplay is blocked (the action is disabled then),
        /// so keyboard/gamepad menus can confirm with the same button.
        /// </summary>
        public bool InteractPressedThisFrame()
        {
            if (_interact == null) return false;
            foreach (var control in _interact.controls)
            {
                if (control is UnityEngine.InputSystem.Controls.ButtonControl button && button.wasPressedThisFrame)
                    return true;
            }
            return false;
        }

        /// <summary>Blocks gameplay actions (movement etc.) while menus/console are up. Pause/Console stay live.</summary>
        public void SetGameplayBlocked(bool blocked)
        {
            if (blocked)
            {
                _move.Disable(); _sprint.Disable(); _zoom.Disable();
                _interact.Disable(); _fastForward.Disable();
                _useTool.Disable(); _cycleTool.Disable(); _inspect.Disable(); _build.Disable(); _rest.Disable(); _ride.Disable();
            }
            else
            {
                _move.Enable(); _sprint.Enable(); _zoom.Enable();
                _interact.Enable(); _fastForward.Enable();
                _useTool.Enable(); _cycleTool.Enable(); _inspect.Enable(); _build.Enable(); _rest.Enable(); _ride.Enable();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}

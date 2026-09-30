using AnimalFarm.Core;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Contextual planting on the shepherd (sits next to ToolController and
    /// InteractionSensor). Pressing Interact while facing an empty dirt cell
    /// with no interactable in focus opens the seed picker. Also shows its own
    /// "[E] Plant..." hint just above the interact prompt's spot and raises
    /// PlantPromptChanged so other UI can react.
    /// </summary>
    public class PlantingInteractor : MonoBehaviour
    {
        /// <summary>Fired when the plant prompt becomes available/unavailable.</summary>
        public static event System.Action<bool> PlantPromptChanged;

        private ToolController _tools;
        private InteractionSensor _sensor;
        private InputAction _interactAction; // enabled-state doubles as "gameplay blocked" probe
        private Text _hint;
        private bool _promptActive;
        private bool _subscribed;

        private void Awake()
        {
            _tools = GetComponent<ToolController>();
            _sensor = GetComponent<InteractionSensor>();
        }

        private void Start()
        {
            var input = GameInput.Instance;
            if (input != null)
            {
                input.InteractPressed += OnInteractPressed;
                _subscribed = true;

                // Interact is disabled while gameplay is blocked (menus/console),
                // so its enabled flag tells us when to hide the hint.
                if (input.Actions != null)
                {
                    var map = input.Actions.FindActionMap("Player");
                    if (map != null) _interactAction = map.FindAction("Interact");
                }
            }

            BuildHint();
        }

        private void OnDestroy()
        {
            if (_subscribed && GameInput.Instance != null)
                GameInput.Instance.InteractPressed -= OnInteractPressed;

            if (_promptActive)
            {
                _promptActive = false;
                PlantPromptChanged?.Invoke(false);
            }

            if (_hint != null)
                Destroy(_hint.gameObject);
        }

        private void Update()
        {
            bool active = EvaluatePrompt();
            if (active == _promptActive) return;

            _promptActive = active;
            if (_hint != null) _hint.gameObject.SetActive(active);
            PlantPromptChanged?.Invoke(active);
        }

        // ---- interact ---------------------------------------------------------

        private void OnInteractPressed()
        {
            if (!TryGetPlantableCell(out var cell)) return;

            if (SeedPickerUI.Instance != null)
                SeedPickerUI.Instance.Open(cell);
        }

        // ---- prompt -----------------------------------------------------------

        private bool EvaluatePrompt()
        {
            // Hidden while gameplay input is blocked (menus/console up).
            if (_interactAction != null && !_interactAction.enabled) return false;

            return TryGetPlantableCell(out _);
        }

        /// <summary>
        /// True when planting is the contextual interact action: no interactable
        /// in focus, and the tool target cell is empty dirt.
        /// </summary>
        private bool TryGetPlantableCell(out Vector2Int cell)
        {
            cell = default;

            // An interactable in focus wins; planting is the fallback action.
            if (_sensor != null && _sensor.Current != null) return false;

            if (_tools == null || !_tools.TryGetTargetCell(out cell)) return false;

            var grid = TerrainGrid.Instance;
            if (grid == null || grid.GetSurface(cell) != Surface.Dirt) return false;

            return PlantManager.Instance != null && !PlantManager.Instance.HasPlantAt(cell);
        }

        // ------------------------------------------------------------------ UI

        private void BuildHint()
        {
            var root = UIRoot.GetRoot();

            _hint = UIRoot.MakeText(root, "PlantPrompt", 28, TextAnchor.MiddleCenter,
                new Color(0.95f, 0.95f, 0.92f, 1f));
            _hint.text = "[E] Plant...";

            var rt = _hint.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 130f); // just above InteractPromptUI (y 90)
            rt.sizeDelta = new Vector2(700f, 40f);

            var shadow = _hint.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            _hint.gameObject.SetActive(false);
        }
    }
}

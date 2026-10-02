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
            // The press that just dismounted the mount is spent (handler order is not fixed).
            if (Time.frameCount == PoutyMount.DismountFrame) return;
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

            // Interact dismounts first while riding: no plant hint (or picker) then.
            if (PoutyMount.IsRiding) return false;

            // An interactable in focus wins; planting is the fallback action.
            if (_sensor != null && _sensor.Current != null) return false;

            if (_tools == null || !_tools.TryGetTargetCell(out cell)) return false;

            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.IsUsable(cell)) return false; // locked land is no planting/painting spot

            Surface surface = grid.GetSurface(cell);
            if (surface == Surface.Water)
            {
                // Water species: only offered once the shepherd actually holds
                // water seeds that can actually root here (shallowOnly species
                // refuse deep water; keeps the prompt off every pond edge).
                if (!HoldsWaterSeeds(grid.IsShallowRim(cell))) return false;
            }
            else if (surface == Surface.Scrub || surface == Surface.Grass)
            {
                // Open ground: only a load-spreading spot (sand or rich mud in the satchel, no home on it).
                var inv = Inventory.Instance;
                if (inv == null || (inv.Count(VendorUI.SandLoadId) <= 0 && inv.Count(SwampVendor.MudLoadId) <= 0)) return false;
                if (AnimalFarm.Spirits.Home.AnyAtCell(cell)) return false;
            }
            else if (surface != Surface.Dirt && surface != Surface.Mud) return false; // Sand: nothing to offer (till it first)

            return PlantManager.Instance != null && !PlantManager.Instance.HasPlantAt(cell);
        }

        /// <summary>True if a water-species seed packet that can be sown on this cell is in the satchel.</summary>
        private bool HoldsWaterSeeds(bool shallowCell)
        {
            var inv = Inventory.Instance;
            var species = _tools != null ? _tools.SeedSpecies : null;
            if (inv == null || species == null) return false;

            for (int i = 0; i < species.Length; i++)
            {
                var s = species[i];
                if (s != null && s.requiredSurface == Surface.Water && (!s.shallowOnly || shallowCell)
                    && inv.Count("seed_" + s.id) > 0)
                    return true;
            }
            return false;
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

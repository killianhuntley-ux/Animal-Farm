using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Embodied terraform/plant tool system (slice 02). The shepherd works the
    /// tile in front of them (dominant-axis snap of FacingDir) — gamepad-first,
    /// no mouse targeting. Cycle tools with CycleTool; apply with UseTool.
    /// Holding UseTool re-applies as the target cell changes ("brush feel").
    /// Belt: Hands, Shovel (-> Dirt), Water Pail (Dirt -> Water), Hammer
    /// (opens the build menu; B does too, with any tool held).
    /// Sits on the shepherd next to ShepherdController.
    /// </summary>
    public class ToolController : MonoBehaviour
    {
        private enum ToolKind { None, Till, SowGrass, DigWater, Seed, Home, Hammer }

        private sealed class ToolDef
        {
            public string name;
            public ToolKind kind;
            public PlantSpecies species;              // Seed tools only
            public SpiritSpeciesDefinition homeSpecies; // Home tools only
        }

        [Header("Seeds (assigned by bootstrapper; read by the contextual seed picker)")]
        [SerializeField] private PlantSpecies[] seedSpecies;

        [Header("Homes (assigned by bootstrapper; one home tool per spirit species)")]
        [SerializeField] private SpiritSpeciesDefinition[] homeSpecies;

        [Header("Reticle (assigned by bootstrapper; a white square sprite)")]
        [SerializeField] private SpriteRenderer reticle;

        [Header("Feel")]
        [SerializeField] private float reach = 1.1f;               // world units in front of the shepherd
        [SerializeField] private float brushRepeatSeconds = 0.18f; // held re-apply cadence on the same cell

        /// <summary>Display name of the currently selected tool.</summary>
        public string CurrentToolName =>
            _tools.Count > 0 ? _tools[_toolIndex].name : string.Empty;

        /// <summary>Display names of every tool, in selection order.</summary>
        public IReadOnlyList<string> ToolNames
        {
            get
            {
                var names = new List<string>(_tools.Count);
                for (int i = 0; i < _tools.Count; i++)
                    names.Add(_tools[i].name);
                return names;
            }
        }

        /// <summary>Index of the currently selected tool.</summary>
        public int CurrentToolIndex => _toolIndex;

        /// <summary>Plantable species available to the contextual seed picker.</summary>
        public PlantSpecies[] SeedSpecies => seedSpecies;

        /// <summary>Fired whenever the selected tool changes (payload = tool name).</summary>
        public event Action<string> OnToolChanged;

        /// <summary>Selects a tool by index (out-of-range values wrap around).</summary>
        public void SelectTool(int index)
        {
            if (_tools.Count == 0) return;
            _toolIndex = ((index % _tools.Count) + _tools.Count) % _tools.Count;
            OnToolChanged?.Invoke(CurrentToolName);
        }

        /// <summary>Current target cell in front of the shepherd; false if none.</summary>
        public bool TryGetTargetCell(out Vector2Int cell)
        {
            cell = _targetCell;
            return _hasTarget;
        }

        private static readonly Color ValidColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color InvalidColor = new Color(1f, 0.25f, 0.25f, 0.35f);
        private static readonly Color NeutralColor = new Color(1f, 1f, 1f, 0.35f); // hammer: no cell action

        private readonly List<ToolDef> _tools = new List<ToolDef>();
        private int _toolIndex;

        private ShepherdController _shepherd;

        private bool _hasTarget;
        private Vector2Int _targetCell;

        private bool _hasAppliedCell;
        private Vector2Int _lastAppliedCell;
        private float _lastApplyTime = float.NegativeInfinity;

        private void Awake()
        {
            _shepherd = GetComponent<ShepherdController>();
        }

        private void Start()
        {
            BuildTools();

            var input = GameInput.Instance;
            if (input != null)
            {
                input.CycleToolPressed += HandleCycleTool;
                input.UseToolPressed += HandleUseToolPressed;
                input.BuildPressed += HandleBuildPressed;
            }

            OnToolChanged?.Invoke(CurrentToolName);
        }

        private void OnDestroy()
        {
            var input = GameInput.Instance;
            if (input != null)
            {
                input.CycleToolPressed -= HandleCycleTool;
                input.UseToolPressed -= HandleUseToolPressed;
                input.BuildPressed -= HandleBuildPressed;
            }
        }

        private void BuildTools()
        {
            _tools.Clear();
            // Default: empty hands — tools are only "out" when deliberately selected.
            _tools.Add(new ToolDef { name = "Hands", kind = ToolKind.None });
            _tools.Add(new ToolDef { name = "Shovel", kind = ToolKind.Till });
            _tools.Add(new ToolDef { name = "Water Pail", kind = ToolKind.DigWater });

            // The Hammer opens the build menu (HomePickerUI); placement then
            // happens in SelectionController's ghost mode, not per-swing.
            _tools.Add(new ToolDef { name = "Hammer", kind = ToolKind.Hammer });

            // Sow Grass moved into the seed picker ("Grass" option on dirt).
            // Seeds are contextual too (Interact on empty dirt opens the seed
            // picker). ToolKind.SowGrass/Seed/Home and their rules stay for
            // safety; seedSpecies is read via SeedSpecies.

            _toolIndex = 0;
        }

        private void Update()
        {
            RefreshTargetCell();
            UpdateReticle();
            UpdateHeldBrush();
        }

        // ---- targeting -------------------------------------------------------

        /// <summary>
        /// Target cell = shepherd position + dominant-axis snap of FacingDir * reach.
        /// A diagonal facing resolves to its stronger axis so the reticle never
        /// sits ambiguous between two cells.
        /// </summary>
        private void RefreshTargetCell()
        {
            _hasTarget = false;

            var grid = TerrainGrid.Instance;
            if (grid == null || _shepherd == null) return;

            Vector2 facing = _shepherd.FacingDir;
            Vector2 dir = Mathf.Abs(facing.x) >= Mathf.Abs(facing.y)
                ? new Vector2(Mathf.Sign(facing.x), 0f)
                : new Vector2(0f, Mathf.Sign(facing.y));

            Vector3 probe = transform.position + (Vector3)(dir * reach);
            _hasTarget = grid.TryWorldToCell(probe, out _targetCell);
        }

        // ---- reticle ---------------------------------------------------------

        private void UpdateReticle()
        {
            if (reticle == null) return;

            var grid = TerrainGrid.Instance;
            ToolKind kind = _tools.Count > 0 ? _tools[_toolIndex].kind : ToolKind.None;
            if (!_hasTarget || grid == null || kind == ToolKind.None)
            {
                reticle.enabled = false;
                return;
            }

            reticle.enabled = true;
            reticle.transform.position = grid.CellCenterWorld(_targetCell);

            // Hammer never acts on the cell itself — neutral white on any
            // in-bounds cell instead of the valid/invalid verdict.
            reticle.color = kind == ToolKind.Hammer
                ? NeutralColor
                : CanApplyCurrentTool(_targetCell) ? ValidColor : InvalidColor;
        }

        // ---- applying --------------------------------------------------------

        private void HandleCycleTool()
        {
            if (_tools.Count == 0) return;
            _toolIndex = (_toolIndex + 1) % _tools.Count;
            OnToolChanged?.Invoke(CurrentToolName);
        }

        private void HandleUseToolPressed()
        {
            // A selection click (or move-mode placement) must not also swing the tool.
            if (SelectionController.ConsumedClickFrame == Time.frameCount) return;
            if (SelectionController.Instance != null && SelectionController.Instance.IsMoving) return;
            TryApply();
        }

        /// <summary>
        /// While UseTool is held, re-apply whenever the target cell changes or
        /// the repeat interval elapses — dragging paints trenches/rows.
        /// </summary>
        private void UpdateHeldBrush()
        {
            var input = GameInput.Instance;
            if (input == null || !input.UseToolHeld || !_hasTarget) return;

            // Selection clicks and home move mode suppress the held brush too.
            if (SelectionController.ConsumedClickFrame == Time.frameCount) return;
            if (SelectionController.Instance != null && SelectionController.Instance.IsMoving) return;

            bool cellChanged = !_hasAppliedCell || _targetCell != _lastAppliedCell;
            bool intervalElapsed = Time.time - _lastApplyTime >= brushRepeatSeconds;
            if (cellChanged || intervalElapsed)
                TryApply();
        }

        private void TryApply()
        {
            if (_tools.Count == 0) return;

            // Hammer: opens the build menu regardless of target validity —
            // it never acts on the cell directly.
            if (_tools[_toolIndex].kind == ToolKind.Hammer)
            {
                _hasAppliedCell = _hasTarget;
                _lastAppliedCell = _targetCell;
                _lastApplyTime = Time.time;
                OpenBuildMenu();
                return;
            }

            if (!_hasTarget) return;
            if (!CanApplyCurrentTool(_targetCell)) return;

            var grid = TerrainGrid.Instance;
            var tool = _tools[_toolIndex];

            switch (tool.kind)
            {
                case ToolKind.Till:
                    grid.SetSurface(_targetCell, Surface.Dirt);
                    break;
                case ToolKind.SowGrass:
                    grid.SetSurface(_targetCell, Surface.Grass);
                    break;
                case ToolKind.DigWater:
                    grid.SetSurface(_targetCell, Surface.Water);
                    break;
                case ToolKind.Seed:
                    // Seeds are FREE this slice — no inventory consumption.
                    PlantManager.Instance.PlantSeed(tool.species, _targetCell);
                    break;
                case ToolKind.Home:
                    // Legacy kind (never built onto the belt anymore): homes are
                    // placed via the build menu -> SelectionController ghost mode.
                    AnimalFarm.UI.HomePickerUI.Instance?.Open();
                    break;
            }

            _hasAppliedCell = true;
            _lastAppliedCell = _targetCell;
            _lastApplyTime = Time.time;
        }

        // ---- build menu --------------------------------------------------------

        /// <summary>B key: open the build menu with any tool held.</summary>
        private void HandleBuildPressed()
        {
            if (UIInputLock.BlockDirectKeys) return;
            OpenBuildMenu();
        }

        private void OpenBuildMenu()
        {
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return;
            AnimalFarm.UI.HomePickerUI.Instance?.Open();
        }

        // ---- rules -----------------------------------------------------------

        /// <summary>True if the current tool could apply to the cell right now.</summary>
        private bool CanApplyCurrentTool(Vector2Int cell)
        {
            if (_tools.Count == 0) return false;

            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.InBounds(cell)) return false;

            var tool = _tools[_toolIndex];

            // Never dig water under our own feet — don't strand the player.
            if (tool.kind == ToolKind.DigWater &&
                grid.TryWorldToCell(transform.position, out var standingCell) &&
                standingCell == cell)
                return false;

            Surface surface = grid.GetSurface(cell);
            bool hasPlant = HasPlantAt(cell);

            switch (tool.kind)
            {
                case ToolKind.Till:
                    // Shovel: applies on Scrub/Grass/Water; blocked over plants
                    // and homes — ground-change kills only when deliberate.
                    return surface != Surface.Dirt && !hasPlant && !Home.AnyAtCell(cell);

                case ToolKind.SowGrass:
                    return surface == Surface.Dirt && !hasPlant;

                case ToolKind.DigWater:
                    return surface == Surface.Dirt && !hasPlant;

                case ToolKind.Seed:
                    return tool.species != null
                        && PlantManager.Instance != null
                        && surface == tool.species.requiredSurface
                        && !hasPlant;

                case ToolKind.Home:
                    return HomeManager.Instance != null
                        && surface != Surface.Water
                        && !hasPlant
                        && !Home.AnyAtCell(cell);

                case ToolKind.Hammer:
                    // No cell action of its own — any in-bounds cell reads
                    // neutral (bounds were already checked above).
                    return true;

                default:
                    return false;
            }
        }

        private static bool HasPlantAt(Vector2Int cell)
        {
            var plants = PlantManager.Instance;
            return plants != null && plants.HasPlantAt(cell);
        }
    }
}

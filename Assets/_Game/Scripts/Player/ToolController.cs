using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Embodied terraform/plant tool system (slice 02). The shepherd works the
    /// tile in front of them (dominant-axis snap of FacingDir) — gamepad-first,
    /// no mouse targeting. Cycle tools with CycleTool; apply with UseTool.
    /// Holding UseTool re-applies as the target cell changes ("brush feel").
    /// Sits on the shepherd next to ShepherdController.
    /// </summary>
    public class ToolController : MonoBehaviour
    {
        private enum ToolKind { Till, SowGrass, DigWater, Seed }

        private sealed class ToolDef
        {
            public string name;
            public ToolKind kind;
            public PlantSpecies species; // Seed tools only
        }

        [Header("Seeds (assigned by bootstrapper; one seed tool per species)")]
        [SerializeField] private PlantSpecies[] seedSpecies;

        [Header("Reticle (assigned by bootstrapper; a white square sprite)")]
        [SerializeField] private SpriteRenderer reticle;

        [Header("Feel")]
        [SerializeField] private float reach = 1.1f;               // world units in front of the shepherd
        [SerializeField] private float brushRepeatSeconds = 0.18f; // held re-apply cadence on the same cell

        /// <summary>Display name of the currently selected tool.</summary>
        public string CurrentToolName =>
            _tools.Count > 0 ? _tools[_toolIndex].name : string.Empty;

        /// <summary>Fired whenever the selected tool changes (payload = tool name).</summary>
        public event Action<string> OnToolChanged;

        private static readonly Color ValidColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color InvalidColor = new Color(1f, 0.25f, 0.25f, 0.35f);

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
            }
        }

        private void BuildTools()
        {
            _tools.Clear();
            _tools.Add(new ToolDef { name = "Till", kind = ToolKind.Till });
            _tools.Add(new ToolDef { name = "Sow Grass", kind = ToolKind.SowGrass });
            _tools.Add(new ToolDef { name = "Dig Water", kind = ToolKind.DigWater });

            if (seedSpecies != null)
            {
                foreach (var species in seedSpecies)
                {
                    if (species == null) continue;
                    string label = !string.IsNullOrEmpty(species.displayName)
                        ? species.displayName
                        : species.id;
                    _tools.Add(new ToolDef
                    {
                        name = "Seed: " + label,
                        kind = ToolKind.Seed,
                        species = species
                    });
                }
            }

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
            if (!_hasTarget || grid == null)
            {
                reticle.enabled = false;
                return;
            }

            reticle.enabled = true;
            reticle.transform.position = grid.CellCenterWorld(_targetCell);
            reticle.color = CanApplyCurrentTool(_targetCell) ? ValidColor : InvalidColor;
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

            bool cellChanged = !_hasAppliedCell || _targetCell != _lastAppliedCell;
            bool intervalElapsed = Time.time - _lastApplyTime >= brushRepeatSeconds;
            if (cellChanged || intervalElapsed)
                TryApply();
        }

        private void TryApply()
        {
            if (!_hasTarget || _tools.Count == 0) return;
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
            }

            _hasAppliedCell = true;
            _lastAppliedCell = _targetCell;
            _lastApplyTime = Time.time;
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
                    // Applies on Scrub/Grass/Water; blocked over plants —
                    // plants die via ground-change only when deliberate.
                    return surface != Surface.Dirt && !hasPlant;

                case ToolKind.SowGrass:
                    return surface == Surface.Dirt && !hasPlant;

                case ToolKind.DigWater:
                    return surface == Surface.Dirt && !hasPlant;

                case ToolKind.Seed:
                    return tool.species != null
                        && PlantManager.Instance != null
                        && surface == tool.species.requiredSurface
                        && !hasPlant;

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

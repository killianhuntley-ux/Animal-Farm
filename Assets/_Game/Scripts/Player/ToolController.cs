using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
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
    /// Belt: Hands, Hoe (tills -> Dirt), Water Pail (waters tilled soil),
    /// Shovel (digs Dirt -> Water), Hammer (opens the build menu; B does too,
    /// with any tool held — once the Hammer is unlocked).
    /// Tools unlock over the run (drip-fed): Hands + Hoe from the start; the
    /// Water Pail, Shovel and Hammer arrive via UnlockTool. Locked tools are
    /// invisible to the belt/cycle and persist through saves ("tools" key).
    /// Sits on the shepherd next to ShepherdController.
    ///
    /// Muscle 01: cell verbs (Till/Sow/Water/Dig) are WEIGHTY -- a committed
    /// ~0.5s action with a wind-up, movement locked (ShepherdController reads
    /// MovementLocked), the effect landing at the contact beat (~60% through)
    /// with a bleep + puff + arcing swing sprite (ToolSwingVisual). Holding
    /// UseTool chains actions rhythmically. Hands/Hammer stay instant.
    /// Per-tool tiers 1-3 ("tools" save key): tier 2 is x0.65 action time,
    /// tier 3 is x0.5 AND area-of-effect (Hoe/Pail hit a 3-cell row across
    /// the facing axis; the Shovel is speed-only). Each landed verb drips
    /// shepherd XP (ShepherdProgress).
    /// </summary>
    public class ToolController : MonoBehaviour, ISaveable
    {
        private enum ToolKind { None, Till, SowGrass, DigWater, Water, Seed, Home, Hammer }

        private sealed class ToolDef
        {
            public string name;
            public ToolKind kind;
            public PlantSpecies species;              // Seed tools only
            public SpiritSpeciesDefinition homeSpecies; // Home tools only
        }

        // Canonical tool names (UnlockTool/IsToolUnlocked use these exactly).
        public const string ToolHands = "Hands";
        public const string ToolHoe = "Hoe";
        public const string ToolWaterPail = "Water Pail";
        public const string ToolShovel = "Shovel";
        public const string ToolHammer = "Hammer";

        [Header("Seeds (assigned by bootstrapper; read by the contextual seed picker)")]
        [SerializeField] private PlantSpecies[] seedSpecies;

        [Header("Homes (assigned by bootstrapper; one home tool per spirit species)")]
        [SerializeField] private SpiritSpeciesDefinition[] homeSpecies;

        [Header("Reticle (assigned by bootstrapper; a white square sprite)")]
        [SerializeField] private SpriteRenderer reticle;

        [Header("Feel")]
        [SerializeField] private float reach = 1.1f;               // world units in front of the shepherd
        [SerializeField] private float brushRepeatSeconds = 0.18f; // held re-apply cadence on the same cell
        [SerializeField] private float weightyActionSeconds = 0.5f; // base committed-action time (tier 1)

        private const float ContactFraction = 0.6f;   // effect lands this far through the action
        private const float ChainGapSeconds = 0.12f;  // breath between held-chained actions
        public const int MaxToolTier = 3;

        /// <summary>True while a weighty tool action is in progress.</summary>
        public bool IsUsingTool => _acting;

        /// <summary>
        /// Movement hook for ShepherdController: true while any weighty tool
        /// action commits the shepherd in place. Static because there is one
        /// shepherd; cleared defensively in OnDisable.
        /// </summary>
        public static bool MovementLocked { get; private set; }

        /// <summary>Display name of the currently selected tool.</summary>
        public string CurrentToolName =>
            _tools.Count > 0 ? _tools[_toolIndex].name : string.Empty;

        /// <summary>Display names of every UNLOCKED tool, in selection order.</summary>
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

        /// <summary>Index of the currently selected tool (within the unlocked belt).</summary>
        public int CurrentToolIndex => _toolIndex;

        /// <summary>Plantable species available to the contextual seed picker.</summary>
        public PlantSpecies[] SeedSpecies => seedSpecies;

        /// <summary>Fired whenever the selected tool changes (payload = tool name).</summary>
        public event Action<string> OnToolChanged;

        /// <summary>Selects a tool by belt index (out-of-range values wrap around).
        /// The belt only ever contains unlocked tools, so locked tools cannot be
        /// selected by any index.</summary>
        public void SelectTool(int index)
        {
            if (_tools.Count == 0) return;
            _toolIndex = ((index % _tools.Count) + _tools.Count) % _tools.Count;
            OnToolChanged?.Invoke(CurrentToolName);
        }

        /// <summary>True if the named tool has been acquired.</summary>
        public bool IsToolUnlocked(string toolName) =>
            !string.IsNullOrEmpty(toolName) && _unlocked.Contains(toolName);

        /// <summary>
        /// Unlocks a tool by its exact belt name ("Water Pail", "Shovel",
        /// "Hammer", ...). Idempotent — unlocking twice is a silent no-op.
        /// Drip-fed progression (owner): faster than VP but not instant.
        /// </summary>
        public void UnlockTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return;
            if (!IsKnownToolName(toolName))
            {
                Debug.LogWarning("[Tools] UnlockTool: unknown tool '" + toolName + "'.");
                return;
            }
            if (_unlocked.Contains(toolName)) return; // idempotent

            _unlocked.Add(toolName);
            RebuildBelt();

            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.9f,
                toolName + " acquired!",
                AcquiredGold);
            Bleeps.Play(BleepKind.Build, 0.6f);

            // Belt contents changed; let the HUD/belt re-read names + index.
            OnToolChanged?.Invoke(CurrentToolName);
        }

        /// <summary>
        /// Current tier (1-3) of a tool by its exact belt name. Unknown names
        /// and untouched tools read as tier 1.
        /// </summary>
        public int GetToolTier(string toolName)
        {
            if (!string.IsNullOrEmpty(toolName) && _tiers.TryGetValue(toolName, out int tier))
                return Mathf.Clamp(tier, 1, MaxToolTier);
            return 1;
        }

        /// <summary>
        /// Sets a tool's tier (clamped 1-3). The Blacksmith purchase UI calls
        /// this later; persists through the "tools" save key. Returns false
        /// for unknown tool names.
        /// </summary>
        public bool SetToolTier(string toolName, int tier)
        {
            if (!IsKnownToolName(toolName))
            {
                Debug.LogWarning("[Tools] SetToolTier: unknown tool '" + toolName + "'.");
                return false;
            }

            _tiers[toolName] = Mathf.Clamp(tier, 1, MaxToolTier);
            OnToolChanged?.Invoke(CurrentToolName); // let belt/HUD re-read if they care
            return true;
        }

        /// <summary>Action-time multiplier for a tier (1 -> x1, 2 -> x0.65, 3 -> x0.5).</summary>
        public static float TierSpeedMultiplier(int tier) =>
            tier >= 3 ? 0.5f : tier == 2 ? 0.65f : 1f;

        /// <summary>Current target cell in front of the shepherd; false if none.</summary>
        public bool TryGetTargetCell(out Vector2Int cell)
        {
            cell = _targetCell;
            return _hasTarget;
        }

        private static readonly Color ValidColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color InvalidColor = new Color(1f, 0.25f, 0.25f, 0.35f);
        private static readonly Color NeutralColor = new Color(1f, 1f, 1f, 0.35f); // hammer: no cell action
        private static readonly Color AcquiredGold = new Color(1f, 0.84f, 0.25f, 1f);
        private static readonly Color SplashBlue = new Color(0.45f, 0.75f, 1f, 1f);

        // Master belt order; _tools is the unlocked subset, in this order.
        private readonly List<ToolDef> _allTools = new List<ToolDef>();
        private readonly List<ToolDef> _tools = new List<ToolDef>();
        private readonly HashSet<string> _unlocked = new HashSet<string>();
        private int _toolIndex;

        private ShepherdController _shepherd;

        private bool _hasTarget;
        private Vector2Int _targetCell;

        private bool _hasAppliedCell;
        private Vector2Int _lastAppliedCell;
        private float _lastApplyTime = float.NegativeInfinity;

        // Per-tool upgrade tiers (1-3); only entries above 1 are persisted.
        private readonly Dictionary<string, int> _tiers = new Dictionary<string, int>();

        // Weighty action state (one committed swing/pour at a time).
        private bool _acting;
        private float _actionStartTime;
        private float _actionDuration;
        private bool _contactDone;
        private ToolDef _actionTool;
        private Vector2Int _actionCell;
        private Vector2 _actionFacing;            // snapshot for the tier-3 AoE row
        private float _nextChainTime = float.NegativeInfinity;
        private ToolSwingVisual _swing;

        private static readonly Color DirtPuff = new Color(0.55f, 0.4f, 0.25f, 0.9f);
        private static readonly Color GrassPuff = new Color(0.45f, 0.75f, 0.35f, 0.9f);

        private void Awake()
        {
            _shepherd = GetComponent<ShepherdController>();

            _swing = GetComponent<ToolSwingVisual>();
            if (_swing == null) _swing = gameObject.AddComponent<ToolSwingVisual>();

            // Spawn the XP tracker during Awake so SaveSystem's Start-time
            // ISaveable scan finds its "progress" key on the initial auto-load.
            ShepherdProgress.GetOrCreate();
        }

        private void OnDisable()
        {
            // Never leave the shepherd frozen if we vanish mid-swing.
            if (_acting) CancelAction();
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
            _allTools.Clear();
            // Default: empty hands — tools are only "out" when deliberately selected.
            _allTools.Add(new ToolDef { name = ToolHands, kind = ToolKind.None });
            _allTools.Add(new ToolDef { name = ToolHoe, kind = ToolKind.Till });
            _allTools.Add(new ToolDef { name = ToolWaterPail, kind = ToolKind.Water });
            _allTools.Add(new ToolDef { name = ToolShovel, kind = ToolKind.DigWater });

            // The Hammer opens the build menu (HomePickerUI); placement then
            // happens in SelectionController's ghost mode, not per-swing.
            _allTools.Add(new ToolDef { name = ToolHammer, kind = ToolKind.Hammer });

            // Sow Grass moved into the seed picker ("Grass" option on dirt).
            // Seeds are contextual too (Interact on empty dirt opens the seed
            // picker). ToolKind.SowGrass/Seed/Home and their rules stay for
            // safety; seedSpecies is read via SeedSpecies.

            // Starting unlocks: Hands + Hoe. The Water Pail, Shovel and Hammer
            // arrive via UnlockTool (and persist through the "tools" save key).
            if (_unlocked.Count == 0)
            {
                _unlocked.Add(ToolHands);
                _unlocked.Add(ToolHoe);
            }

            RebuildBelt();
        }

        private bool IsKnownToolName(string toolName)
        {
            for (int i = 0; i < _allTools.Count; i++)
                if (_allTools[i].name == toolName) return true;
            return false;
        }

        /// <summary>Rebuilds the visible belt from the unlocked set, keeping the
        /// current selection by name when it survives (falls back to Hands).</summary>
        private void RebuildBelt()
        {
            string keep = _tools.Count > 0 ? _tools[_toolIndex].name : null;

            _tools.Clear();
            for (int i = 0; i < _allTools.Count; i++)
                if (_unlocked.Contains(_allTools[i].name))
                    _tools.Add(_allTools[i]);

            _toolIndex = 0;
            if (keep != null)
            {
                for (int i = 0; i < _tools.Count; i++)
                    if (_tools[i].name == keep) { _toolIndex = i; break; }
            }
        }

        private void Update()
        {
            RefreshTargetCell();
            UpdateReticle();
            UpdateAction();
            UpdateHeldBrush();
        }

        // ---- weighty actions -------------------------------------------------

        /// <summary>
        /// Ticks the committed swing/pour: the effect lands at the contact beat
        /// (~60% through), the action releases the shepherd at the end.
        /// </summary>
        private void UpdateAction()
        {
            if (!_acting) return;

            float t = (Time.time - _actionStartTime) / _actionDuration;

            if (!_contactDone && t >= ContactFraction)
            {
                _contactDone = true;
                ApplyContact();
            }

            if (t >= 1f)
            {
                _acting = false;
                MovementLocked = false;
                _nextChainTime = Time.time + ChainGapSeconds;
            }
        }

        /// <summary>Begins the committed action on the current target cell.</summary>
        private void StartWeightyAction(ToolDef tool)
        {
            _acting = true;
            MovementLocked = true;
            _contactDone = false;
            _actionTool = tool;
            _actionCell = _targetCell;
            _actionFacing = _shepherd != null ? _shepherd.FacingDir : Vector2.down;
            _actionStartTime = Time.time;
            _actionDuration = weightyActionSeconds * TierSpeedMultiplier(GetToolTier(tool.name));

            var grid = TerrainGrid.Instance;
            if (_swing != null && grid != null)
                _swing.Play(tool.name,
                    transform.position + Vector3.up * 0.55f,
                    grid.CellCenterWorld(_actionCell),
                    _actionDuration);
        }

        /// <summary>Aborts the in-flight action without applying (disable path).</summary>
        private void CancelAction()
        {
            _acting = false;
            MovementLocked = false;
            if (_swing != null) _swing.Cancel();
        }

        /// <summary>
        /// The contact beat: applies the tool to the target cell -- plus the
        /// tier-3 row across the facing axis for the Hoe/Water Pail -- with a
        /// bleep and a puff per landed cell. Cells are revalidated here.
        /// </summary>
        private void ApplyContact()
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || _actionTool == null) return;

            int applied = 0;
            applied += ApplyToolToCell(_actionTool, _actionCell) ? 1 : 0;

            // Tier 3 AoE: a 3-cell row perpendicular to the facing axis
            // (facing up/down sweeps left-center-right, and vice versa).
            bool aoe = GetToolTier(_actionTool.name) >= 3
                       && (_actionTool.kind == ToolKind.Till || _actionTool.kind == ToolKind.Water);
            if (aoe)
            {
                Vector2Int across = Mathf.Abs(_actionFacing.x) >= Mathf.Abs(_actionFacing.y)
                    ? new Vector2Int(0, 1)
                    : new Vector2Int(1, 0);
                applied += ApplyToolToCell(_actionTool, _actionCell + across) ? 1 : 0;
                applied += ApplyToolToCell(_actionTool, _actionCell - across) ? 1 : 0;
            }

            if (applied > 0)
            {
                // One contact bleep per swing; the pour reads lighter.
                if (_actionTool.kind == ToolKind.Water) Bleeps.Play(BleepKind.Click, 0.6f);
                else Bleeps.Play(BleepKind.Build, 0.45f);
            }

            _hasAppliedCell = true;
            _lastAppliedCell = _actionCell;
            _lastApplyTime = Time.time;
        }

        /// <summary>
        /// Applies one weighty tool to one cell if it is still valid: terrain
        /// change + puff + shepherd XP. Returns true if the effect landed.
        /// </summary>
        private bool ApplyToolToCell(ToolDef tool, Vector2Int cell)
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || !CanApply(tool, cell)) return false;

            Vector3 center = grid.CellCenterWorld(cell);

            switch (tool.kind)
            {
                case ToolKind.Till:
                    grid.SetSurface(cell, Surface.Dirt);
                    Puffs.Burst(center, DirtPuff);
                    ShepherdProgress.Grant("till");
                    return true;

                case ToolKind.SowGrass:
                    grid.SetSurface(cell, Surface.Grass);
                    Puffs.Burst(center, GrassPuff);
                    ShepherdProgress.Grant("sow");
                    return true;

                case ToolKind.Water:
                    // Water Pail: wet the tilled soil (~24 game-hours; the
                    // grid owns the timer) + a small splash for feel.
                    if (!grid.SetWatered(cell)) return false;
                    AnimalFarm.UI.FloatingText.Show(center, "splash", SplashBlue);
                    Puffs.Burst(center, SplashBlue, 5, 1.1f);
                    ShepherdProgress.Grant("water");
                    return true;

                case ToolKind.DigWater:
                    grid.SetSurface(cell, Surface.Water);
                    Puffs.Burst(center, DirtPuff, 7, 1.6f);
                    ShepherdProgress.Grant("dig");
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>The cell verbs that carry weight; Hands/Hammer stay instant.</summary>
        private static bool IsWeighty(ToolKind kind) =>
            kind == ToolKind.Till || kind == ToolKind.SowGrass
            || kind == ToolKind.Water || kind == ToolKind.DigWater;

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
            // The belt only holds unlocked tools, so cycling skips locked ones.
            _toolIndex = (_toolIndex + 1) % _tools.Count;
            OnToolChanged?.Invoke(CurrentToolName);
        }

        private void HandleUseToolPressed()
        {
            // A committed swing cannot be interrupted or double-started.
            if (_acting) return;

            // A selection click (or move-mode placement) must not also swing the tool.
            if (SelectionController.ConsumedClickFrame == Time.frameCount) return;
            if (SelectionController.Instance != null && SelectionController.Instance.IsMoving) return;
            TryApply();
        }

        /// <summary>
        /// While UseTool is held, re-apply whenever the target cell changes or
        /// the repeat interval elapses -- dragging paints trenches/rows. For
        /// weighty tools this is the rhythmic chain: the next committed action
        /// starts a short breath after the previous one releases.
        /// </summary>
        private void UpdateHeldBrush()
        {
            if (_acting) return; // one committed action at a time

            var input = GameInput.Instance;
            if (input == null || !input.UseToolHeld || !_hasTarget) return;

            // Selection clicks and home move mode suppress the held brush too.
            if (SelectionController.ConsumedClickFrame == Time.frameCount) return;
            if (SelectionController.Instance != null && SelectionController.Instance.IsMoving) return;

            // The chain breath applies after a weighty action; instant tools
            // keep the original brush cadence (both gates pass trivially then).
            if (Time.time < _nextChainTime) return;

            bool cellChanged = !_hasAppliedCell || _targetCell != _lastAppliedCell;
            bool intervalElapsed = Time.time - _lastApplyTime >= brushRepeatSeconds;
            if (cellChanged || intervalElapsed)
                TryApply();
        }

        private void TryApply()
        {
            if (_tools.Count == 0 || _acting) return;

            // Hammer: opens the build menu regardless of target validity —
            // it never acts on the cell directly. (If it's selected, it's unlocked.)
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

            var tool = _tools[_toolIndex];

            // Cell verbs are committed actions now -- the effect itself lands
            // at the contact beat inside UpdateAction/ApplyContact.
            if (IsWeighty(tool.kind))
            {
                StartWeightyAction(tool);
                return;
            }

            switch (tool.kind)
            {
                case ToolKind.Seed:
                    // Seeds are FREE this slice — no inventory consumption.
                    // (Legacy kind -- the belt never carries seeds anymore.)
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

        /// <summary>B key: open the build menu with any tool held — Hammer-gated.</summary>
        private void HandleBuildPressed()
        {
            if (UIInputLock.BlockDirectKeys) return;
            OpenBuildMenu();
        }

        private void OpenBuildMenu()
        {
            // Building is the Hammer's job — no Hammer, no menu.
            if (!IsToolUnlocked(ToolHammer)) return;

            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return;
            AnimalFarm.UI.HomePickerUI.Instance?.Open();
        }

        // ---- rules -----------------------------------------------------------

        /// <summary>True if the current tool could apply to the cell right now.</summary>
        private bool CanApplyCurrentTool(Vector2Int cell)
        {
            if (_tools.Count == 0) return false;
            return CanApply(_tools[_toolIndex], cell);
        }

        /// <summary>True if the given tool could apply to the cell right now
        /// (also used to revalidate each cell at the contact beat).</summary>
        private bool CanApply(ToolDef tool, Vector2Int cell)
        {
            if (tool == null) return false;

            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.InBounds(cell)) return false;
            if (!grid.IsUsable(cell)) return false; // locked parcel (slice 08b)

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
                    // Hoe: applies on Scrub/Grass/Water; blocked over plants
                    // and homes — ground-change kills only when deliberate.
                    return surface != Surface.Dirt && !hasPlant && !Home.AnyAtCell(cell);

                case ToolKind.SowGrass:
                    return surface == Surface.Dirt && !hasPlant;

                case ToolKind.Water:
                    // Water Pail: only dry tilled soil. Plants don't block —
                    // watering a planted row is the whole point.
                    return surface == Surface.Dirt && !grid.IsWatered(cell);

                case ToolKind.DigWater:
                    // Shovel: digs ponds on empty tilled soil.
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

        // ---- ISaveable ------------------------------------------------------

        [Serializable]
        private class ToolsState
        {
            public string[] unlocked;
            // Parallel arrays (JsonUtility has no dictionaries): tools whose
            // tier is above the default 1.
            public string[] tierNames;
            public int[] tierValues;
        }

        public string SaveKey => "tools";

        public string Capture()
        {
            var list = new List<string>(_unlocked.Count);
            // Save in belt order for stable, readable files.
            for (int i = 0; i < _allTools.Count; i++)
                if (_unlocked.Contains(_allTools[i].name))
                    list.Add(_allTools[i].name);

            var tierNames = new List<string>();
            var tierValues = new List<int>();
            for (int i = 0; i < _allTools.Count; i++)
            {
                int tier = GetToolTier(_allTools[i].name);
                if (tier <= 1) continue;
                tierNames.Add(_allTools[i].name);
                tierValues.Add(tier);
            }

            return JsonUtility.ToJson(new ToolsState
            {
                unlocked = list.ToArray(),
                tierNames = tierNames.ToArray(),
                tierValues = tierValues.ToArray()
            });
        }

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<ToolsState>(json);
            if (state == null || state.unlocked == null) return;

            _unlocked.Clear();
            for (int i = 0; i < state.unlocked.Length; i++)
            {
                string name = state.unlocked[i];
                if (!string.IsNullOrEmpty(name)) _unlocked.Add(name);
            }

            // Tool tiers (older saves simply have no entries -> everything 1).
            _tiers.Clear();
            if (state.tierNames != null && state.tierValues != null)
            {
                int n = Mathf.Min(state.tierNames.Length, state.tierValues.Length);
                for (int i = 0; i < n; i++)
                {
                    if (string.IsNullOrEmpty(state.tierNames[i])) continue;
                    _tiers[state.tierNames[i]] = Mathf.Clamp(state.tierValues[i], 1, MaxToolTier);
                }
            }

            // The starting pair can never be lost to a stale/foreign save.
            _unlocked.Add(ToolHands);
            _unlocked.Add(ToolHoe);

            // Restore may land before Start(); BuildTools() then respects the
            // already-populated set (it only seeds defaults when empty).
            if (_allTools.Count > 0)
            {
                RebuildBelt();
                OnToolChanged?.Invoke(CurrentToolName);
            }
        }
    }
}

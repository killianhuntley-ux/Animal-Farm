using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Player;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.Onboarding
{
    /// <summary>
    /// Slice 10 core, reworked: the guide-light onboarding is now the TOOL-
    /// GRANTING tutorial (owner: "if the beginning scene is the tutorial,
    /// items are drip fed"). A short, skippable, data-driven step list (GDD
    /// 4.4) - the unnamed light POINTS at things and a single quiet HUD line
    /// says what it wants; no lecturing, no popups. The shepherd starts with
    /// Hands + Hoe only; the light hands over the Water Pail, Hammer and
    /// Shovel as steps complete (ToolController.UnlockTool). A one-time
    /// starter kit (grass + palewheat seed packets, a few obols) is granted
    /// at step 0 so the first plantings are payable. One step is active at a
    /// time; conditions are polled on a half-second tick; completion, the
    /// current step and the kit flag persist via ISaveable ("onboarding").
    /// The light's VOICE (Muscle 10 verdict 1): a minor god on onboarding
    /// duty as community service, mildly embarrassed about it, dryly fond of
    /// the shepherd. Each step has an intro (the HUD line), an optional nudge
    /// (floats up once if the player lingers 90s), and a completion beat;
    /// tool grants get their own line. After CompleteAll the guide is silent
    /// forever - tutorial-type beats elsewhere go through GuideMoments.
    /// Old saves that already have residents complete silently AND receive
    /// every tool; Skip() unlocks everything too. Save versioning: a save
    /// from the old step list restarts the tutorial (step indexes shifted -
    /// accepted trade-off).
    /// </summary>
    public class OnboardingManager : MonoBehaviour, ISaveable
    {
        public static OnboardingManager Instance { get; private set; }

        private const float TickInterval = 0.5f;        // condition polling, scaled
        private const float FinalLingerRealSeconds = 20f;
        private const float GrassPercentGoal = 8f;
        private const float NudgeAfterSeconds = 90f;    // linger before the one nudge
        private const float GrantLineDelay = 1.3f;      // completion beat first, then the handover

        // The natural-completion send-off; Skip and legacy auto-complete stay silent.
        private const string SendoffLine = "Hours served. Tell no one I enjoyed this.";

        private static readonly Color ApproveGold = new Color(1f, 0.84f, 0.45f);

        // Starter kit (fresh games only; persisted flag so it never re-grants).
        private const int KitGrassSeeds = 6;
        private const int KitPalewheatSeeds = 3;
        private const int KitCoins = 12;

        // ---- serialized ------------------------------------------------------

        [Tooltip("Soft radial glow sprite for the guide light (bootstrapper-assigned).")]
        [SerializeField] private Sprite glowSprite;

        [Tooltip("Sprite material for the guide light (e.g. Sprite-Lit-Default); null is fine.")]
        [SerializeField] private Material spriteMaterial;

        // ---- step data ---------------------------------------------------------

        // objectiveText is the standing HUD intro; nudgeLine floats up once
        // after a long linger (empty = never nudge); doneLine is the beat on
        // completion. Pointing behavior lives in UpdateGuideTarget.
        private struct Step
        {
            public string id;
            public string objectiveText;
            public string nudgeLine;
            public string doneLine;

            public Step(string id, string objectiveText, string nudgeLine, string doneLine)
            {
                this.id = id;
                this.objectiveText = objectiveText;
                this.nudgeLine = nudgeLine;
                this.doneLine = doneLine;
            }
        }

        private static readonly Step[] Steps =
        {
            new Step("till",
                "Orientation duty. Lucky me. Till the earth; the hoe knows how.",
                "The hoe. The dirt. Introduce them.",
                "Good furrows. I have seen worse from professionals."),
            new Step("plant",
                "Plant a seed. Most buried things stay down. This one is the exception.",
                "Seeds prefer the tilled rows. Trust me.",
                "Planted. Burial is a skill. You have it."),
            new Step("water",
                "Water the seedling. Even the dead-adjacent get thirsty.",
                "Tip the pail. Gravity does the rest.",
                "Watered. Somewhere a river god nods."),
            new Step("grass",
                "Sow grass. The dead follow green like gossip.",
                "More grass. The dead avoid bald lawns.",
                "Green enough. Word spreads downstairs."),
            new Step("stirs",
                "Something stirs at the fence. Watch. Screaming reads as rude here.",
                "It is shy. You are new. Wait with me.",
                "It sees you. Congratulations, mostly."),
            new Step("stay",
                "Keep the crops coming. It will decide to stay on its own. The dead resent being pushed.",
                "Ripe wheat, a green lawn, patience. Staying is its idea, not yours.",
                "It chose you. Nobody pushed. Very respectable."),
            new Step("name",
                "Give them a name. The unnamed drift, and drift means paperwork.",
                "Any name will do. They will grow into it.",
                "Named. That one is yours now."),
            new Step("home",
                "Build them a home (B). Even the dead like a door to close.",
                "Press B. Walls, roof, done. They are not picky.",
                "A roof for the dead. Well done, shepherd."),
            new Step("done",
                "My shift ends soon. J journal, T tools, B build, Z sit; town lies east. The rest is yours.",
                "",
                "")
        };

        // Steps whose COMPLETION hands the shepherd a tool (drip-feed).
        private const int PlantStepIndex = 1; // done -> Water Pail
        private const int NameStepIndex = 6;  // done -> Hammer
        private const int HomeStepIndex = 7;  // done -> Shovel

        private int FinalStepIndex => Steps.Length - 1;

        // ---- state -------------------------------------------------------------

        private GuideLight _guide;
        private Text _objectiveLine;

        private bool _complete;
        private int _stepIndex;
        private bool _kitGranted;           // starter kit handed out (persisted)

        private float _tickTimer;
        private float _finalTimer;          // unscaled seconds on the final step
        private float _stepLingerTimer;     // scaled seconds on the current step (nudge)
        private bool _nudgeShown;           // one nudge per step, max
        private bool _legacyChecked;        // old-save auto-complete ran once

        // event-driven flags the tick consumes
        private bool _anyTilled;
        private bool _namingRequested;
        private bool _hasFirstDirt;
        private Vector3 _firstDirtWorld;

        private bool _terrainSubscribed;

        // Tool grants queued until a ToolController exists (it always does
        // in play, but Restore can land before the scene is fully up).
        private readonly List<string> _pendingToolUnlocks = new List<string>();

        // ---- public surface ------------------------------------------------------

        public bool IsComplete => _complete;

        /// <summary>The guide-light (always spawned, hidden once onboarding completes). Ceremonies borrow it.</summary>
        public GuideLight Guide => _guide;

        /// <summary>Current objective text ("complete" once done); console-friendly.</summary>
        public string CurrentObjective
        {
            get
            {
                if (_complete) return "complete";
                int i = Mathf.Clamp(_stepIndex, 0, Steps.Length - 1);
                return Steps[i].objectiveText;
            }
        }

        /// <summary>Skips the rest of the onboarding (console / accessibility).
        /// Unlocks every drip-fed tool so skipping never locks the player out.</summary>
        public void Skip()
        {
            if (_complete) return;
            CompleteAll();
        }

        // ---- lifecycle -------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            SpiritManager.NamingRequested += HandleNamingRequested;
        }

        private void OnDisable()
        {
            SpiritManager.NamingRequested -= HandleNamingRequested;
        }

        private void Start()
        {
            SpawnGuide();
            BuildHud();

            if (TerrainGrid.Instance != null)
            {
                TerrainGrid.Instance.OnSurfaceChanged += HandleSurfaceChanged;
                _terrainSubscribed = true;
            }

            // Old save with residents already in the field: nothing to teach.
            // Only before the spirit steps - a mid-tutorial resident is normal.
            if (!_complete && _stepIndex < 4 && HasAnyResident())
            {
                CompleteAll();
                _legacyChecked = true;
            }

            ApplyState();
        }

        private void OnDestroy()
        {
            if (_terrainSubscribed && TerrainGrid.Instance != null)
                TerrainGrid.Instance.OnSurfaceChanged -= HandleSurfaceChanged;

            if (Instance == this) Instance = null;
        }

        // ---- build -------------------------------------------------------------------

        private void SpawnGuide()
        {
            if (_guide != null) return;
            var go = new GameObject("GuideLight");
            _guide = go.AddComponent<GuideLight>();
            _guide.Init(glowSprite, spriteMaterial);
        }

        private void BuildHud()
        {
            if (_objectiveLine != null) return;

            var root = UIRoot.GetRoot();
            if (root == null) return;

            _objectiveLine = UIRoot.MakeText(
                root, "GuideObjective", 24, TextAnchor.MiddleCenter, UIStyle.GoldDim);

            var rt = _objectiveLine.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -18f);
            rt.sizeDelta = new Vector2(1100f, 36f);
        }

        // ---- event handlers --------------------------------------------------------------

        private void HandleSurfaceChanged(Vector2Int cell, Surface surface)
        {
            if (surface != Surface.Dirt) return;
            _anyTilled = true;
            if (!_hasFirstDirt && TerrainGrid.Instance != null)
            {
                _hasFirstDirt = true;
                _firstDirtWorld = TerrainGrid.Instance.CellCenterWorld(cell);
            }
        }

        private void HandleNamingRequested(SpiritAgent agent)
        {
            _namingRequested = true;
        }

        // ---- tool drip-feed ----------------------------------------------------------------

        /// <summary>Queues a tool grant; applied as soon as a ToolController exists.</summary>
        private void QueueToolUnlock(string toolName)
        {
            if (string.IsNullOrEmpty(toolName) || _pendingToolUnlocks.Contains(toolName)) return;
            _pendingToolUnlocks.Add(toolName);
            TryFlushToolUnlocks();
        }

        private void TryFlushToolUnlocks()
        {
            if (_pendingToolUnlocks.Count == 0) return;

            var tools = FindFirstObjectByType<ToolController>();
            if (tools == null) return; // retried every Update until it exists

            for (int i = 0; i < _pendingToolUnlocks.Count; i++)
            {
                if (!tools.IsToolUnlocked(_pendingToolUnlocks[i]))
                    tools.UnlockTool(_pendingToolUnlocks[i]);
            }
            _pendingToolUnlocks.Clear();
        }

        /// <summary>Re-derives every earned tool grant from current progress.
        /// Idempotent and self-healing: covers natural completion, Skip, the
        /// old-save auto-complete, restored-complete saves, and a quit that
        /// landed between a step completing and the unlock applying.</summary>
        private void SyncToolUnlocks()
        {
            if (_complete || _stepIndex > PlantStepIndex) QueueToolUnlock(ToolController.ToolWaterPail);
            if (_complete || _stepIndex > NameStepIndex) QueueToolUnlock(ToolController.ToolHammer);
            if (_complete || _stepIndex > HomeStepIndex) QueueToolUnlock(ToolController.ToolShovel);
        }

        /// <summary>The guide hands over a tool: unlock now, line a beat later
        /// so it does not land on top of the step's completion line.</summary>
        private void GrantTool(string toolName, string guideLine)
        {
            QueueToolUnlock(toolName);
            StartCoroutine(ShowGuideLineDelayed(guideLine, GrantLineDelay));
        }

        /// <summary>A short voice line floating up at the light; no-op without a light.</summary>
        private void ShowGuideLine(string line)
        {
            if (_guide == null || string.IsNullOrEmpty(line)) return;
            FloatingText.Show(_guide.transform.position, line, ApproveGold);
        }

        private System.Collections.IEnumerator ShowGuideLineDelayed(string line, float delay)
        {
            yield return new WaitForSeconds(delay);
            ShowGuideLine(line);
        }

        // ---- starter kit -----------------------------------------------------------------

        /// <summary>Fresh-game starter kit, granted once at step 0 (the flag
        /// persists). Runs on the tick so save Restores have already landed;
        /// veterans auto-completed by the legacy check never reach it.</summary>
        private void TryGrantStarterKit()
        {
            if (_kitGranted || _complete || _stepIndex != 0) return;

            var inv = Inventory.Instance;
            if (inv == null) return; // retried next tick

            inv.Add("seed_grass", KitGrassSeeds);
            inv.Add("seed_palewheat", KitPalewheatSeeds);
            inv.Add("coin", KitCoins);
            _kitGranted = true;
        }

        // ---- tick --------------------------------------------------------------------------

        private void Update()
        {
            // Pending tool grants apply even after completion (legacy unlock-all).
            TryFlushToolUnlocks();

            if (_complete) return;

            // Final step runs a REAL-time linger, independent of the tick.
            if (_stepIndex >= FinalStepIndex)
            {
                _finalTimer += Time.unscaledDeltaTime;
                if (_finalTimer >= FinalLingerRealSeconds)
                {
                    // Natural completion only: the proper send-off. Skip and
                    // the legacy auto-complete go quiet without ceremony.
                    ShowGuideLine(SendoffLine);
                    CompleteAll();
                }
                return;
            }

            _stepLingerTimer += Time.deltaTime; // scaled: pausing is not lingering

            _tickTimer += Time.deltaTime;
            if (_tickTimer < TickInterval) return;
            _tickTimer = 0f;

            // One late legacy check: SaveSystem may restore residents after our Start.
            if (!_legacyChecked)
            {
                _legacyChecked = true;
                if (_stepIndex < 4 && HasAnyResident())
                {
                    CompleteAll();
                    return;
                }
            }

            TryGrantStarterKit();

            if (IsStepSatisfied(_stepIndex))
            {
                AdvanceStep();
            }
            else if (!_nudgeShown && _stepLingerTimer >= NudgeAfterSeconds)
            {
                // One gentle nudge per step if the shepherd lingers; never
                // in the same tick as a completion beat.
                _nudgeShown = true;
                ShowGuideLine(Steps[Mathf.Clamp(_stepIndex, 0, Steps.Length - 1)].nudgeLine);
            }

            UpdateGuideTarget();
        }

        private void AdvanceStep()
        {
            int completed = _stepIndex;
            _stepIndex++;

            // Every step gets its completion beat; tool-granting steps add
            // the handover line a moment later (GrantTool delays it).
            ShowGuideLine(Steps[completed].doneLine);
            switch (completed)
            {
                case PlantStepIndex:
                    GrantTool(ToolController.ToolWaterPail, "Your Water Pail. Baptisms are another department.");
                    break;
                case NameStepIndex:
                    GrantTool(ToolController.ToolHammer, "Your Hammer. Smiting not included.");
                    break;
                case HomeStepIndex:
                    GrantTool(ToolController.ToolShovel, "Your Shovel. Rebury anything that waves.");
                    break;
            }

            if (_stepIndex >= FinalStepIndex)
                _finalTimer = 0f;

            ApplyState();
        }

        /// <summary>Everything done: light out, HUD line gone, never shown again.
        /// Always unlocks every drip-fed tool (Skip / legacy saves included).</summary>
        private void CompleteAll()
        {
            _complete = true;
            _stepIndex = Steps.Length;
            ApplyState(); // ApplyState -> SyncToolUnlocks hands over everything
        }

        // ---- step conditions -------------------------------------------------------------------

        private bool IsStepSatisfied(int index)
        {
            switch (index)
            {
                case 0: // Till the dry earth (the Hoe is a starting tool)
                    return _anyTilled;

                case 1: // Plant a seed
                    return PlantManager.Instance != null
                        && PlantManager.Instance.AllPlants != null
                        && PlantManager.Instance.AllPlants.Count > 0;

                case 2: // Water the seedling
                    return AnyPlantWatered();

                case 3: // Sow grass
                    return TerrainGrid.Instance != null
                        && TerrainGrid.Instance.SurfacePercent(Surface.Grass) >= GrassPercentGoal;

                case 4: // Something stirs (any live spirit: Silhouette or better)
                    return FindSpirit(SpiritState.Silhouette, orBetter: true) != null;

                case 5: // It decides to stay (any discovery at Resident, or the naming modal fired)
                    return _namingRequested || AnyDiscoveryAtResident();

                case 6: // Give them a name
                    return HasNamedResident();

                case 7: // Build them a home
                    return Home.All != null && Home.All.Count > 0;

                default:
                    return false;
            }
        }

        private static bool AnyPlantWatered()
        {
            var plants = PlantManager.Instance;
            var grid = TerrainGrid.Instance;
            if (plants == null || grid == null || plants.AllPlants == null) return false;

            var all = plants.AllPlants;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p != null && grid.IsWatered(p.Cell))
                    return true;
            }
            return false;
        }

        private static bool HasAnyResident()
        {
            return FindSpirit(SpiritState.Resident, orBetter: false) != null;
        }

        private static bool HasNamedResident()
        {
            var manager = SpiritManager.Instance;
            if (manager == null || manager.AllSpirits == null) return false;

            var spirits = manager.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a != null && a.State == SpiritState.Resident && !string.IsNullOrEmpty(a.GivenName))
                    return true;
            }
            return false;
        }

        private static bool AnyDiscoveryAtResident()
        {
            var manager = SpiritManager.Instance;
            if (manager == null) return false;

            var known = manager.KnownSpecies;
            if (known != null)
            {
                for (int i = 0; i < known.Count; i++)
                {
                    var species = known[i];
                    if (species == null || string.IsNullOrEmpty(species.id)) continue;
                    if (manager.GetDiscovery(species.id) >= SpiritManager.DiscoveryLevel.Resident)
                        return true;
                }
            }
            // Belt and braces: a live resident counts even if discovery lagged.
            return HasAnyResident();
        }

        /// <summary>First live spirit in exactly <paramref name="state"/> (or any state
        /// at/above it when orBetter). Null-safe; null when none.</summary>
        private static SpiritAgent FindSpirit(SpiritState state, bool orBetter)
        {
            var manager = SpiritManager.Instance;
            if (manager == null || manager.AllSpirits == null) return null;

            var spirits = manager.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null) continue;
                if (orBetter ? a.State >= state : a.State == state)
                    return a;
            }
            return null;
        }

        // ---- guide pointing ----------------------------------------------------------------------

        /// <summary>Re-aims the light every tick so moving targets stay pointed at.</summary>
        private void UpdateGuideTarget()
        {
            if (_guide == null) return;

            switch (_stepIndex)
            {
                case 1: // point at the first tilled cell, if we saw one
                    if (_hasFirstDirt) _guide.PointAt(_firstDirtWorld);
                    else _guide.FollowShepherd();
                    break;

                case 2: // point at a thirsty plant
                {
                    var plant = FindUnwateredPlant();
                    if (plant != null) _guide.PointAt(plant.transform);
                    else _guide.FollowShepherd();
                    break;
                }

                case 4: // point AT the first silhouette, live-updating
                {
                    var target = FindSpirit(SpiritState.Silhouette, orBetter: false);
                    if (target == null) target = FindSpirit(SpiritState.Silhouette, orBetter: true);
                    if (target != null) _guide.PointAt(target.transform);
                    else _guide.FollowShepherd();
                    break;
                }

                case 5: // point at the silhouette/visitor deciding whether to stay
                {
                    var target = FindSpirit(SpiritState.Visitor, orBetter: false);
                    if (target == null) target = FindSpirit(SpiritState.Silhouette, orBetter: false);
                    if (target != null) _guide.PointAt(target.transform);
                    else _guide.FollowShepherd();
                    break;
                }

                case 6: // point at the unnamed resident
                {
                    var target = FindUnnamedResident();
                    if (target != null) _guide.PointAt(target.transform);
                    else _guide.FollowShepherd();
                    break;
                }

                case 7: // point at the (homeless) resident who needs the roof
                {
                    var target = FindSpirit(SpiritState.Resident, orBetter: false);
                    if (target != null) _guide.PointAt(target.transform);
                    else _guide.FollowShepherd();
                    break;
                }

                default: // till / plant / grass / final: keep the shepherd company
                    _guide.FollowShepherd();
                    break;
            }
        }

        private static Plant FindUnwateredPlant()
        {
            var plants = PlantManager.Instance;
            var grid = TerrainGrid.Instance;
            if (plants == null || plants.AllPlants == null) return null;

            var all = plants.AllPlants;
            Plant first = null;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;
                if (first == null) first = p;
                if (grid != null && !grid.IsWatered(p.Cell)) return p;
            }
            return first; // all watered (or no grid): still show the crop
        }

        private static SpiritAgent FindUnnamedResident()
        {
            var manager = SpiritManager.Instance;
            if (manager == null || manager.AllSpirits == null) return null;

            var spirits = manager.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a != null && a.State == SpiritState.Resident && string.IsNullOrEmpty(a.GivenName))
                    return a;
            }
            return null;
        }

        // ---- presentation ----------------------------------------------------------------------------

        /// <summary>Syncs the HUD line, the light, and earned tool grants with
        /// the current step/completion.</summary>
        private void ApplyState()
        {
            // Fresh step (or load): the linger clock and its one nudge reset.
            _stepLingerTimer = 0f;
            _nudgeShown = false;

            if (_objectiveLine != null)
            {
                if (_complete)
                {
                    _objectiveLine.gameObject.SetActive(false);
                }
                else
                {
                    int i = Mathf.Clamp(_stepIndex, 0, Steps.Length - 1);
                    _objectiveLine.gameObject.SetActive(true);
                    _objectiveLine.text = i == FinalStepIndex
                        ? Steps[i].objectiveText // the send-off reads verbatim
                        : "The light hums: " + Steps[i].objectiveText;
                }
            }

            if (_guide != null)
                _guide.SetVisible(!_complete);

            SyncToolUnlocks();
        }

        // ---- ISaveable ----------------------------------------------------------------------------------

        // Version 2 = the tool-granting step list. Version 0/1 saves carry
        // step indexes from the OLD list; rather than guess a mapping we
        // restart the tutorial (completion is honored either way).
        private const int SaveVersion = 2;

        [Serializable]
        private struct OnboardingState
        {
            public int version;
            public bool complete;
            public int step;
            public bool kit;
        }

        public string SaveKey => "onboarding";

        public string Capture()
        {
            return JsonUtility.ToJson(new OnboardingState
            {
                version = SaveVersion,
                complete = _complete,
                step = _stepIndex,
                kit = _kitGranted
            });
        }

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<OnboardingState>(json);
            _complete = state.complete;
            _kitGranted = state.kit;

            if (_complete)
            {
                _stepIndex = Steps.Length;
                _kitGranted = true; // done players never get a late kit
            }
            else if (state.version != SaveVersion)
            {
                // Old step list: indexes shifted, so restart the tutorial.
                _stepIndex = 0;
            }
            else
            {
                _stepIndex = Mathf.Clamp(state.step, 0, Steps.Length);
            }

            if (_stepIndex >= FinalStepIndex) _finalTimer = 0f; // re-linger after load
            _legacyChecked = _complete; // a mid-run save already passed the check

            ApplyState(); // Restore can land before or after Start; both are safe
        }
    }
}

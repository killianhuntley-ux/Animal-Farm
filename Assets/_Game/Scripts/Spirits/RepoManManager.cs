using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The Repo-man (slice 07): the comically officious collector of neglected
    /// spirits. Runaway is the warning state; the Repo-man is the escalation.
    /// A spirit that stays Runaway for more than 2 game-hours gets flagged; the
    /// Repo-man walks in from the town gate -- slow, visible, plenty of time --
    /// and takes it to the Holding Office unless stopped. The FIRST dispatch
    /// ever is a warning visit: he arrives, lectures, and takes nothing (kills
    /// the ignorance-window problem, research 5.2). Counters: soothe the
    /// runaway back before he arrives (he turns around), or bribe him mid-walk
    /// (2x the target's favored food). GameSettings.GentlePassage disables
    /// dispatch entirely.
    /// </summary>
    public class RepoManManager : MonoBehaviour, ISaveable
    {
        public static RepoManManager Instance { get; private set; }

        private const float TickInterval = 1f;               // scaled seconds
        private const float RunawayHoursBeforeDispatch = 2f; // game-hours
        /// <summary>Bribes and reclaims both cost 2x the favored food.</summary>
        public const int BribeFoodCount = 2;

        /// <summary>World position of the town gate the Repo-man walks in from.</summary>
        public static readonly Vector3 GatePosition = new Vector3(21.5f, 0f, 0f);

        private static readonly Color PaperGrey = new Color(0.75f, 0.75f, 0.78f);

        [SerializeField] private Sprite repoSprite;
        [Tooltip("Shared sprite material (same soft URP material the spirits use).")]
        [SerializeField] private Material spriteMaterial;

        // agent -> GameClock.TotalHours stamped when first seen as Runaway.
        private readonly Dictionary<SpiritAgent, float> _runawaySince =
            new Dictionary<SpiritAgent, float>();
        private readonly List<SpiritAgent> _scratchKeys = new List<SpiritAgent>();

        private readonly List<SpiritSaveRecord> _held = new List<SpiritSaveRecord>();
        private bool _firstVisitDone;

        private bool _dispatchActive;
        private bool _activeWasWarning;
        private RepoManAgent _activeAgent;
        private float _tickTimer;

        /// <summary>Spirits currently sitting in the Holding Office ledger.</summary>
        public IReadOnlyList<SpiritSaveRecord> Held => _held;

        /// <summary>True while a Repo-man is walking (dispatches are one at a time).</summary>
        public bool DispatchActive => _dispatchActive;

        // ---- lifecycle --------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- flagging tick ----------------------------------------------------

        private void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < TickInterval) return;
            _tickTimer = 0f;
            Tick();
        }

        private void Tick()
        {
            // Gentle Passage: no Repo-man, ever. Drop any pending timers so
            // toggling it off later starts everyone with a fresh window.
            if (GameSettings.Instance != null && GameSettings.Instance.GentlePassage)
            {
                _runawaySince.Clear();
                return;
            }

            if (_dispatchActive) return;

            var spirits = SpiritManager.Instance;
            var clock = GameClock.Instance;
            if (spirits == null || clock == null) return;

            float now = clock.TotalHours;

            // Stamp newly-seen runaways.
            var all = spirits.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;
                if (a.State == SpiritState.Runaway)
                {
                    if (!_runawaySince.ContainsKey(a)) _runawaySince[a] = now;
                }
            }

            // Clear entries that are gone or no longer runaway; find the worst offender.
            _scratchKeys.Clear();
            SpiritAgent overdue = null;
            foreach (var pair in _runawaySince)
            {
                var a = pair.Key;
                if (a == null || a.State != SpiritState.Runaway)
                {
                    _scratchKeys.Add(a);
                    continue;
                }
                if (now - pair.Value >= RunawayHoursBeforeDispatch && overdue == null)
                    overdue = a;
            }
            for (int i = 0; i < _scratchKeys.Count; i++)
                _runawaySince.Remove(_scratchKeys[i]);

            if (overdue != null) Dispatch(overdue);
        }

        // ---- dispatch -----------------------------------------------------------

        private void Dispatch(SpiritAgent target)
        {
            if (target == null || _dispatchActive) return;

            // Fresh window if this one somehow runs away again afterwards.
            _runawaySince.Remove(target);

            bool warning = !_firstVisitDone;

            var go = new GameObject("RepoMan");
            go.transform.position = GatePosition;
            go.transform.localScale = Vector3.one * 1.5f;

            // Body renderer on a child so the officious bob never fights root movement.
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            var sr = body.AddComponent<SpriteRenderer>();
            sr.sprite = repoSprite;
            sr.sortingOrder = 2;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.6f;

            AnimalFarm.UI.WorldLabel.Attach(go, "The Repo-man");

            var agent = go.AddComponent<RepoManAgent>();
            agent.Init(this, target, warning);

            _dispatchActive = true;
            _activeWasWarning = warning;
            _activeAgent = agent;

            string name = DisplayNameOf(target);
            AnimalFarm.UI.AlarmBannerUI.Show(warning
                ? "SOMEONE OFFICIAL APPROACHES..."
                : $"THE REPO-MAN COMES FOR {name.ToUpperInvariant()}");
            Debug.Log(warning
                ? "[Repo] First dispatch: a warning visit approaches the gate."
                : $"[Repo] Dispatch: the Repo-man comes for {name}.");
        }

        /// <summary>
        /// Console cheat: bypasses the 2-hour wait and dispatches at the first
        /// runaway found, using the normal warning/take logic. False if Gentle
        /// Passage is on, a dispatch is already active, or nobody is runaway.
        /// </summary>
        public bool Debug_DispatchNow()
        {
            if (GameSettings.Instance != null && GameSettings.Instance.GentlePassage) return false;
            if (_dispatchActive || SpiritManager.Instance == null) return false;

            var all = SpiritManager.Instance.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a != null && a.State == SpiritState.Runaway)
                {
                    Dispatch(a);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Called by the walking agent when its errand ends (took someone,
        /// lectured, was soothed away, or was bribed).
        /// </summary>
        public void OnRepoResolved(bool tookSomeone)
        {
            _dispatchActive = false;
            _activeAgent = null;
            AnimalFarm.UI.AlarmBannerUI.Hide();

            // The one free warning has now been spent.
            if (_activeWasWarning) _firstVisitDone = true;
            _activeWasWarning = false;

            if (tookSomeone) Debug.Log("[Repo] The Repo-man departs with his paperwork in order.");
        }

        // ---- holding office -------------------------------------------------------

        /// <summary>Snapshots the spirit into the holding ledger and removes it from the field.</summary>
        public void TakeIntoHolding(SpiritAgent a)
        {
            if (a == null || a.Species == null) return;

            _held.Add(a.ToRecord());
            _runawaySince.Remove(a);

            // Free its home (same pattern Ascend uses).
            var home = a.CurrentHome;
            if (home != null)
            {
                home.Release(a);
                a.NotifyHomeLost(home);
            }

            string name = DisplayNameOf(a);
            if (SpiritManager.Instance != null) SpiritManager.Instance.Despawn(a);
            else Destroy(a.gameObject);

            Debug.Log($"[Repo] {name} has been repossessed. Forms were filed.");
        }

        /// <summary>
        /// Buys a held spirit back for 2x its species' favored food. On success
        /// it respawns (shaken: spirit 35, half hungry) at the Holding Office
        /// drop point, or the gate if no office exists.
        /// </summary>
        public bool TryReclaim(int index, out string error)
        {
            error = "";
            if (index < 0 || index >= _held.Count) { error = "No such record."; return false; }

            var rec = _held[index];

            if (SpiritManager.Instance == null) { error = "SpiritManager not available."; return false; }
            var species = SpiritManager.Instance.FindSpecies(rec.speciesId);
            if (species == null) { error = $"Unknown species '{rec.speciesId}'."; return false; }

            string food = species.favoredFoodId;
            if (Inventory.Instance == null || !Inventory.Instance.Consume(food, BribeFoodCount))
            {
                error = $"(needs {BribeFoodCount}x {food})";
                return false;
            }

            _held.RemoveAt(index);

            Vector3 pos = HoldingOffice.Instance != null
                ? HoldingOffice.Instance.DropPoint
                : GatePosition;

            // Released shaken: low spirit, half hungry, standing at the drop point.
            rec.spirit = 35f;
            rec.hunger01 = 0.5f;
            rec.x = pos.x;
            rec.y = pos.y;

            SpiritManager.Instance.SpawnFromRecord(rec, pos);

            AnimalFarm.UI.FloatingText.Show(
                pos + Vector3.up * 1.0f, "Released. Do keep better records.", PaperGrey);
            return true;
        }

        private static string DisplayNameOf(SpiritAgent a)
        {
            if (a == null) return "A spirit";
            if (!string.IsNullOrEmpty(a.GivenName)) return a.GivenName;
            if (a.Species != null && !string.IsNullOrEmpty(a.Species.displayName))
                return a.Species.displayName;
            return "A spirit";
        }

        // ---- ISaveable --------------------------------------------------------------

        [Serializable]
        private class RepoState
        {
            public List<SpiritSaveRecord> held = new List<SpiritSaveRecord>();
            public bool firstVisitDone;
        }

        public string SaveKey => "repoman";

        public string Capture()
        {
            var state = new RepoState { firstVisitDone = _firstVisitDone };
            state.held.AddRange(_held);
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            // Cancel any mid-walk Repo-man from the previous session state.
            if (_activeAgent != null) _activeAgent.CancelSilently();
            _activeAgent = null;
            _dispatchActive = false;
            _activeWasWarning = false;
            _runawaySince.Clear();
            AnimalFarm.UI.AlarmBannerUI.Hide();

            _held.Clear();
            _firstVisitDone = false;
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<RepoState>(json);
            if (state == null) return;

            _firstVisitDone = state.firstVisitDone;
            if (state.held != null) _held.AddRange(state.held);
        }
    }
}

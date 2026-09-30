using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Requirements;
using AnimalFarm.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Owns every live <see cref="SpiritAgent"/> (slice 03): spawns silhouettes
    /// at the field border when a species' Appear gate opens, tracks per-species
    /// discovery, feeds resident counts back into the requirement engine, and
    /// persists the lot.
    /// </summary>
    public class SpiritManager : MonoBehaviour, ISaveable
    {
        public static SpiritManager Instance { get; private set; }

        private const float SpawnCheckInterval = 2f; // scaled seconds
        private const float BorderInset = 1.5f;

        /// <summary>How much of a species the player has uncovered. Monotonic.</summary>
        public enum DiscoveryLevel { Unseen = 0, Seen = 1, Visited = 2, Resident = 3 }

        [SerializeField] private SpiritSpeciesDefinition[] knownSpecies;
        [Tooltip("Assigned to every spirit's SpriteRenderer after spawn (e.g. a soft URP lit/unlit ghost material).")]
        [SerializeField] private Material spriteMaterial;
        [Tooltip("Weaving recipes (slice 06): two max-Spirit residents -> one cryptid.")]
        [SerializeField] private WeaveRecipe[] recipes;

        /// <summary>Fired when a visitor becomes a resident and wants a name. UI subscribes.</summary>
        public static event Action<SpiritAgent> NamingRequested;

        private readonly List<SpiritAgent> _spirits = new List<SpiritAgent>();
        private readonly Dictionary<string, DiscoveryLevel> _discovery = new Dictionary<string, DiscoveryLevel>();
        private readonly Dictionary<string, int> _ascended = new Dictionary<string, int>();

        private Func<string, int> _residentCounter; // the delegate we installed, so OnDestroy only clears our own
        private float _spawnTimer;

        public IReadOnlyList<SpiritAgent> AllSpirits => _spirits;

        /// <summary>All authored species (journal / console listings).</summary>
        public IReadOnlyList<SpiritSpeciesDefinition> KnownSpecies =>
            knownSpecies ?? System.Array.Empty<SpiritSpeciesDefinition>();

        /// <summary>All authored weave recipes (loom UI / console listings).</summary>
        public IReadOnlyList<WeaveRecipe> Recipes =>
            recipes ?? System.Array.Empty<WeaveRecipe>();

        // ---- lifecycle --------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _residentCounter = CountResidents;
            ResidentPresentCondition.ResidentCounter = _residentCounter;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(ResidentPresentCondition.ResidentCounter, _residentCounter))
                ResidentPresentCondition.ResidentCounter = null;
            if (Instance == this) Instance = null;
        }

        // ---- queries ----------------------------------------------------------

        public SpiritSpeciesDefinition FindSpecies(string id)
        {
            if (knownSpecies == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < knownSpecies.Length; i++)
            {
                var s = knownSpecies[i];
                if (s != null && s.id == id) return s;
            }
            return null;
        }

        public int CountResidents(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId)) return 0;
            int n = 0;
            for (int i = 0; i < _spirits.Count; i++)
            {
                var a = _spirits[i];
                if (a != null && a.State == SpiritState.Resident
                    && a.Species != null && a.Species.id == speciesId)
                    n++;
            }
            return n;
        }

        public DiscoveryLevel GetDiscovery(string speciesId) =>
            !string.IsNullOrEmpty(speciesId) && _discovery.TryGetValue(speciesId, out var lvl)
                ? lvl : DiscoveryLevel.Unseen;

        /// <summary>How many spirits of a species have ascended (slice 04).</summary>
        public int AscendedCount(string speciesId) =>
            !string.IsNullOrEmpty(speciesId) && _ascended.TryGetValue(speciesId, out int n) ? n : 0;

        /// <summary>
        /// A fulfilled spirit moves on: bump the species' ascended count, free
        /// its home, and remove it from the field.
        /// </summary>
        public void Ascend(SpiritAgent a)
        {
            if (a == null || a.Species == null) return;

            string speciesId = a.Species.id;
            if (!string.IsNullOrEmpty(speciesId))
                _ascended[speciesId] = AscendedCount(speciesId) + 1;

            var home = a.CurrentHome;
            if (home != null)
            {
                home.Release(a);
                a.NotifyHomeLost(home);
            }

            string who = !string.IsNullOrEmpty(a.GivenName) ? a.GivenName
                : !string.IsNullOrEmpty(a.Species.displayName) ? a.Species.displayName
                : "A spirit";
            Debug.Log($"[Spirits] {who} has moved on.");

            Despawn(a);
        }

        private void BumpDiscovery(string speciesId, DiscoveryLevel to)
        {
            if (string.IsNullOrEmpty(speciesId)) return;
            if (to > GetDiscovery(speciesId)) _discovery[speciesId] = to; // monotonic
        }

        // ---- notifications from agents -----------------------------------------

        public void NotifyBecameVisitor(SpiritAgent a)
        {
            if (a == null || a.Species == null) return;
            BumpDiscovery(a.Species.id, DiscoveryLevel.Visited);
        }

        public void NotifyBecameResident(SpiritAgent a)
        {
            if (a == null || a.Species == null) return;
            BumpDiscovery(a.Species.id, DiscoveryLevel.Resident);
            NamingRequested?.Invoke(a); // naming UI + discovery journal
        }

        // ---- spawning -----------------------------------------------------------

        private void Update()
        {
            _spawnTimer += Time.deltaTime;
            if (_spawnTimer < SpawnCheckInterval) return;
            _spawnTimer = 0f;

            var evaluator = RequirementEvaluator.Instance;
            if (evaluator == null || knownSpecies == null) return;

            for (int i = 0; i < knownSpecies.Length; i++)
            {
                var species = knownSpecies[i];
                if (species == null || string.IsNullOrEmpty(species.id) || species.gateChain == null)
                    continue;
                if (HasLiveNonResident(species.id)) continue;
                if (CountResidents(species.id) >= species.maxResidents) continue;
                if (!evaluator.IsGateOpen(species.gateChain.chainId, Gate.Appear)) continue;

                Spawn(species, SpiritState.Silhouette, RandomBorderPoint());
                BumpDiscovery(species.id, DiscoveryLevel.Seen);
                Debug.Log("[Spirits] Something stirs at the border...");
            }
        }

        private bool HasLiveNonResident(string speciesId)
        {
            for (int i = 0; i < _spirits.Count; i++)
            {
                var a = _spirits[i];
                if (a != null && a.Species != null && a.Species.id == speciesId
                    && (a.State == SpiritState.Silhouette || a.State == SpiritState.Visitor))
                    return true;
            }
            return false;
        }

        private SpiritAgent Spawn(SpiritSpeciesDefinition species, SpiritState state, Vector3 pos)
        {
            var go = new GameObject("Spirit");
            var agent = go.AddComponent<SpiritAgent>();
            agent.Init(species, state, pos);
            if (spriteMaterial != null && agent.Renderer != null)
                agent.Renderer.sharedMaterial = spriteMaterial;
            _spirits.Add(agent);
            return agent;
        }

        /// <summary>Console helper: spawns a Visitor near the shepherd (tag "Player") or origin.</summary>
        public SpiritAgent ForceSpawn(string speciesId)
        {
            var species = FindSpecies(speciesId);
            if (species == null)
            {
                Debug.LogWarning($"[Spirits] ForceSpawn: unknown species '{speciesId}'.");
                return null;
            }

            Vector3 pos = Vector3.zero;
            var player = GameObject.FindWithTag("Player");
            if (player != null)
                pos = player.transform.position + (Vector3)(Random.insideUnitCircle.normalized * 2f);

            var agent = Spawn(species, SpiritState.Visitor, pos);
            BumpDiscovery(species.id, DiscoveryLevel.Seen);
            NotifyBecameVisitor(agent);
            return agent;
        }

        /// <summary>
        /// Console cheat: spawns a fully-converted named Resident near the shepherd.
        /// Skips the naming UI; discovery jumps straight to Resident.
        /// </summary>
        public SpiritAgent ForceSpawnResident(string speciesId, string givenName)
        {
            var species = FindSpecies(speciesId);
            if (species == null)
            {
                Debug.LogWarning($"[Spirits] ForceSpawnResident: unknown species '{speciesId}'.");
                return null;
            }

            Vector3 pos = Vector3.zero;
            var player = GameObject.FindWithTag("Player");
            if (player != null)
                pos = player.transform.position + (Vector3)(Random.insideUnitCircle.normalized * 2f);

            var agent = Spawn(species, SpiritState.Resident, pos);
            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;
            string name = string.IsNullOrWhiteSpace(givenName) ? species.displayName : givenName.Trim();
            agent.ApplyLoadedState(new SpiritSaveRecord
            {
                givenName = name,
                spirit = 70f,
                hunger01 = 0f,
                fedCount = species.residencyFoodCount,
                lastFed = now,
                timesFed = species.residencyFoodCount,
                taskProgress = 0,
                taskDone = false,
                residentSince = now,
                compEntries = 0,
                compWins = 0,
                vigor = 0, grace = 0, gleam = 0 // 0 -> ApplyLoadedState rerolls fresh stats
            });
            BumpDiscovery(species.id, DiscoveryLevel.Resident);
            return agent;
        }

        /// <summary>
        /// Respawns one spirit from a save record at an explicit position
        /// (save-file Restore, Repo-man reclaims). Returns null (with a warning)
        /// if the species id is unknown.
        /// </summary>
        public SpiritAgent SpawnFromRecord(SpiritSaveRecord rec, Vector3 pos)
        {
            var species = FindSpecies(rec.speciesId);
            if (species == null)
            {
                Debug.LogWarning($"[Spirits] Skipping saved spirit of unknown species '{rec.speciesId}'.");
                return null;
            }

            var spiritState = (SpiritState)Mathf.Clamp(rec.state, 0, (int)SpiritState.Runaway);
            var agent = Spawn(species, spiritState, pos);
            agent.ApplyLoadedState(rec);
            return agent;
        }

        // ---- weaving (slice 06) --------------------------------------------------

        /// <summary>First recipe matching the (order-independent) species pair, or null.</summary>
        public WeaveRecipe FindRecipe(SpiritSpeciesDefinition a, SpiritSpeciesDefinition b)
        {
            if (recipes == null || a == null || b == null) return null;
            for (int i = 0; i < recipes.Length; i++)
            {
                var r = recipes[i];
                if (r != null && r.Matches(a, b)) return r;
            }
            return null;
        }

        /// <summary>
        /// Consumes two max-Spirit residents at the Loom and spawns the recipe's
        /// cryptid as a fresh Resident (slice 06). Returns the woven spirit, or
        /// null if the pair is invalid or no recipe matches.
        /// </summary>
        public SpiritAgent Weave(SpiritAgent a, SpiritAgent b, Vector3 spawnPos)
        {
            if (a == null || b == null || a == b) return null;
            if (a.State != SpiritState.Resident || b.State != SpiritState.Resident) return null;
            // "a sad spirit won't ascend to a higher creature plane" - GDD
            if (a.Spirit < 100f || b.Spirit < 100f) return null;

            var recipe = FindRecipe(a.Species, b.Species);
            if (recipe == null || recipe.result == null) return null;

            var result = recipe.result;
            string nameA = WeaveParentName(a);
            string nameB = WeaveParentName(b);
            string newName = PortmanteauName(nameA, nameB);

            var woven = Spawn(result, SpiritState.Resident, spawnPos);
            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;
            woven.ApplyLoadedState(new SpiritSaveRecord
            {
                spirit = 80f,
                hunger01 = 0f,
                fedCount = result.residencyFoodCount,
                lastFed = now,
                timesFed = result.residencyFoodCount,
                taskProgress = 0,
                taskDone = false,
                residentSince = now,
                compEntries = 0,
                compWins = 0,
                // Stat inheritance: the woven takes the best of each parent stat.
                // Parents always roll >= 2, so vigor is nonzero and
                // ApplyLoadedState won't reroll.
                vigor = Mathf.Max(a.Vigor, b.Vigor),
                grace = Mathf.Max(a.Grace, b.Grace),
                gleam = Mathf.Max(a.Gleam, b.Gleam)
            });
            woven.SetGivenName(newName);

            // The parents are consumed: free their homes, then remove them.
            ReleaseHomeOf(a);
            ReleaseHomeOf(b);
            Despawn(a);
            Despawn(b);

            BumpDiscovery(result.id, DiscoveryLevel.Resident);

            string resultName = !string.IsNullOrEmpty(result.displayName) ? result.displayName : result.id;
            Debug.Log($"[Weave] {nameA} and {nameB} became {newName} the {resultName}!");
            return woven;
        }

        private static void ReleaseHomeOf(SpiritAgent a)
        {
            var home = a.CurrentHome;
            if (home == null) return;
            home.Release(a);
            a.NotifyHomeLost(home);
        }

        /// <summary>Given name, falling back to the species name for the unnamed.</summary>
        private static string WeaveParentName(SpiritAgent a)
        {
            if (!string.IsNullOrEmpty(a.GivenName)) return a.GivenName;
            if (a.Species != null && !string.IsNullOrEmpty(a.Species.displayName))
                return a.Species.displayName;
            return "Spirit";
        }

        /// <summary>
        /// Portmanteau: first ceil(n/2) chars of the first name + last floor(m/2)
        /// chars of the second, first letter capitalized.
        /// </summary>
        private static string PortmanteauName(string nameA, string nameB)
        {
            string head = nameA.Substring(0, (nameA.Length + 1) / 2);
            string tail = nameB.Substring(nameB.Length - nameB.Length / 2);
            string merged = (head + tail).Trim();
            if (merged.Length == 0) return "Woven";
            return char.ToUpperInvariant(merged[0]) + merged.Substring(1);
        }

        public void Despawn(SpiritAgent a)
        {
            if (a == null) return;
            _spirits.Remove(a);
            Destroy(a.gameObject);
        }

        private void DespawnAll()
        {
            for (int i = _spirits.Count - 1; i >= 0; i--)
                if (_spirits[i] != null) Destroy(_spirits[i].gameObject);
            _spirits.Clear();
        }

        /// <summary>Random point on the border ring, 1.5 units inside the walls.</summary>
        private static Vector3 RandomBorderPoint()
        {
            var grid = TerrainGrid.Instance;
            float hw = (grid != null ? grid.Width : 80) * 0.5f - BorderInset;
            float hh = (grid != null ? grid.Height : 50) * 0.5f - BorderInset;

            switch (Random.Range(0, 4))
            {
                case 0: return new Vector3(Random.Range(-hw, hw), hh, 0f);  // top
                case 1: return new Vector3(Random.Range(-hw, hw), -hh, 0f); // bottom
                case 2: return new Vector3(-hw, Random.Range(-hh, hh), 0f); // left
                default: return new Vector3(hw, Random.Range(-hh, hh), 0f); // right
            }
        }

        // ---- ISaveable -----------------------------------------------------------

        [Serializable]
        private class SpiritsState
        {
            public List<SpiritSaveRecord> spirits = new List<SpiritSaveRecord>();
            public string[] discoveredIds;
            public int[] discoveryLevels;
            public string[] ascendedIds;   // may be missing on old saves
            public int[] ascendedCounts;
        }

        public string SaveKey => "spirits";

        public string Capture()
        {
            var state = new SpiritsState();
            for (int i = 0; i < _spirits.Count; i++)
            {
                var a = _spirits[i];
                if (a != null && a.Species != null)
                    state.spirits.Add(a.ToRecord());
            }

            state.discoveredIds = new string[_discovery.Count];
            state.discoveryLevels = new int[_discovery.Count];
            int d = 0;
            foreach (var pair in _discovery)
            {
                state.discoveredIds[d] = pair.Key;
                state.discoveryLevels[d] = (int)pair.Value;
                d++;
            }

            state.ascendedIds = new string[_ascended.Count];
            state.ascendedCounts = new int[_ascended.Count];
            int k = 0;
            foreach (var pair in _ascended)
            {
                state.ascendedIds[k] = pair.Key;
                state.ascendedCounts[k] = pair.Value;
                k++;
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            DespawnAll();
            _discovery.Clear();
            _ascended.Clear();
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<SpiritsState>(json);
            if (state == null) return;

            if (state.discoveredIds != null && state.discoveryLevels != null)
            {
                int n = Mathf.Min(state.discoveredIds.Length, state.discoveryLevels.Length);
                for (int i = 0; i < n; i++)
                {
                    if (string.IsNullOrEmpty(state.discoveredIds[i])) continue;
                    _discovery[state.discoveredIds[i]] =
                        (DiscoveryLevel)Mathf.Clamp(state.discoveryLevels[i], 0, (int)DiscoveryLevel.Resident);
                }
            }

            // Ascended counts (tolerate missing arrays on pre-slice-04 saves).
            if (state.ascendedIds != null && state.ascendedCounts != null)
            {
                int m = Mathf.Min(state.ascendedIds.Length, state.ascendedCounts.Length);
                for (int i = 0; i < m; i++)
                {
                    if (string.IsNullOrEmpty(state.ascendedIds[i]) || state.ascendedCounts[i] <= 0) continue;
                    _ascended[state.ascendedIds[i]] = state.ascendedCounts[i];
                }
            }

            if (state.spirits == null) return;

            for (int i = 0; i < state.spirits.Count; i++)
            {
                var rec = state.spirits[i];
                SpawnFromRecord(rec, new Vector3(rec.x, rec.y, 0f));
            }
        }
    }
}

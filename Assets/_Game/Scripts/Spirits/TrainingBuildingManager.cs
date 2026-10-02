using System;
using System.Collections.Generic;
using AnimalFarm.Core.Saving;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Owns the training buildings' persistence and the spirit-to-building
    /// matchmaking (muscle 05). Self-spawning runtime singleton (AscensionPadManager
    /// pattern, created after scene load so SaveSystem's first scan finds the
    /// "training" key). Also holds the one-way unlock flag for the build-menu
    /// rows: training grounds appear once the first RESIDENT exists.
    /// </summary>
    public class TrainingBuildingManager : MonoBehaviour, ISaveable
    {
        public static TrainingBuildingManager Instance { get; private set; }

        /// <summary>Max copies of each training structure.</summary>
        public const int MaxPerKind = 3;

        private const float UnlockScanInterval = 2f; // scaled seconds
        private const float StandOffset = 1.1f;      // where a spirit stands beside a building

        private bool _unlocked;
        private float _scanTimer;

        /// <summary>True once the training build rows should show (first resident ever seen, or a building exists).</summary>
        public static bool Unlocked =>
            Instance != null && (Instance._unlocked || TrainingBuilding.All.Count > 0);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Only scenes that actually run spirits need the manager.
            if (Instance == null && FindFirstObjectByType<SpiritManager>() != null)
                new GameObject("TrainingBuildingManager (runtime)").AddComponent<TrainingBuildingManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static int CountOf(string specId)
        {
            int n = 0;
            var all = TrainingBuilding.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Spec != null && all[i].Spec.id == specId) n++;
            return n;
        }

        // ---- unlock watch -----------------------------------------------------------

        private void Update()
        {
            if (_unlocked) return;

            _scanTimer += Time.deltaTime;
            if (_scanTimer < UnlockScanInterval) return;
            _scanTimer = 0f;

            var mgr = SpiritManager.Instance;
            if (mgr == null) return;

            var species = mgr.KnownSpecies;
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                if (s != null && mgr.GetDiscovery(s.id) >= SpiritManager.DiscoveryLevel.Resident)
                {
                    _unlocked = true;
                    var player = GameObject.FindWithTag("Player");
                    AnimalFarm.UI.FloatingText.Show(
                        player != null ? player.transform.position + Vector3.up * 1.1f : Vector3.zero,
                        "(the hammer learns: training grounds)", new Color(0.75f, 0.85f, 1f));
                    return;
                }
            }
        }

        // ---- matchmaking ---------------------------------------------------------------

        /// <summary>
        /// Chooses a training building for a spirit that fancies a workout.
        /// Rolls once against the combined appetite of every building with
        /// room, then picks one weighted by its own appetite. Returns a stand
        /// point beside the pick.
        /// </summary>
        public bool TryPickBuilding(SpiritAgent agent, out TrainingBuilding building, out Vector3 point)
        {
            building = null;
            point = default;
            if (agent == null) return false;

            var all = TrainingBuilding.All;
            var weights = new float[all.Count];
            float none = 1f, sum = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                float p = all[i].Appetite(agent);
                weights[i] = p;
                sum += p;
                none *= 1f - p;
            }
            if (sum <= 0f || Random.value >= 1f - none) return false;

            float roll = Random.value * sum;
            for (int i = 0; i < all.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                building = all[i];
                roll -= weights[i];
                if (roll < 0f) break;
            }
            if (building == null) return false;

            Vector2 ring = Random.insideUnitCircle;
            if (ring.sqrMagnitude < 0.01f) ring = Vector2.down;
            point = building.transform.position + (Vector3)(ring.normalized * StandOffset);
            point.z = 0f;
            return true;
        }

        // ---- ISaveable ---------------------------------------------------------------------

        [Serializable]
        private class TrainingState
        {
            public bool unlocked;
            public string[] kinds;
            public float[] x;
            public float[] y;
            public string[] baitIds;
            public int[] baitCharges;
        }

        public string SaveKey => "training";

        public string Capture()
        {
            var list = new List<TrainingBuilding>();
            var all = TrainingBuilding.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Spec != null) list.Add(all[i]);

            var state = new TrainingState
            {
                unlocked = _unlocked,
                kinds = new string[list.Count],
                x = new float[list.Count],
                y = new float[list.Count],
                baitIds = new string[list.Count],
                baitCharges = new int[list.Count]
            };
            for (int i = 0; i < list.Count; i++)
            {
                state.kinds[i] = list[i].Spec.id;
                state.x[i] = list[i].transform.position.x;
                state.y[i] = list[i].transform.position.y;
                state.baitIds[i] = list[i].BaitId ?? "";
                state.baitCharges[i] = list[i].BaitCharges;
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            // Copy first: destroying mutates the registry.
            var existing = new List<TrainingBuilding>(TrainingBuilding.All);
            for (int i = 0; i < existing.Count; i++)
                if (existing[i] != null) Destroy(existing[i].gameObject);

            if (string.IsNullOrEmpty(json)) return;
            var state = JsonUtility.FromJson<TrainingState>(json);
            if (state == null) return;

            _unlocked = state.unlocked;
            if (state.kinds == null || state.x == null || state.y == null) return;

            int n = Mathf.Min(state.kinds.Length, Mathf.Min(state.x.Length, state.y.Length));
            for (int i = 0; i < n; i++)
            {
                var spec = TrainingBuilding.FindSpec(state.kinds[i]);
                if (spec == null) continue;

                string bait = state.baitIds != null && i < state.baitIds.Length ? state.baitIds[i] : "";
                int charges = state.baitCharges != null && i < state.baitCharges.Length ? state.baitCharges[i] : 0;
                TrainingBuilding.Create(spec, new Vector3(state.x[i], state.y[i], 0f), bait, charges);
            }
        }
    }
}

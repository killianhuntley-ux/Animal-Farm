using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Owns every live plant (slice 02). All planting/removal goes through here
    /// so occupancy, save data, and the ground-change death rule stay coherent.
    /// </summary>
    public class PlantManager : MonoBehaviour, ISaveable
    {
        public static PlantManager Instance { get; private set; }

        [SerializeField] private PlantSpecies[] knownSpecies;

        [Tooltip("Assigned by bootstrapper (Sprite-Lit-Default); null is fine.")]
        [SerializeField] private Material spriteMaterial;

        private readonly List<Plant> _plants = new List<Plant>();
        private readonly Dictionary<Vector2Int, Plant> _byCell = new Dictionary<Vector2Int, Plant>();
        private bool _subscribed;

        public IReadOnlyList<Plant> AllPlants => _plants;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TerrainGrid.Instance != null)
            {
                TerrainGrid.Instance.OnSurfaceChanged += HandleSurfaceChanged;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && TerrainGrid.Instance != null)
                TerrainGrid.Instance.OnSurfaceChanged -= HandleSurfaceChanged;

            if (Instance == this) Instance = null;
        }

        // ---- queries ----------------------------------------------------------

        public PlantSpecies FindSpecies(string id)
        {
            if (knownSpecies == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < knownSpecies.Length; i++)
            {
                if (knownSpecies[i] != null && knownSpecies[i].id == id)
                    return knownSpecies[i];
            }
            return null;
        }

        public bool HasPlantAt(Vector2Int cell) => _byCell.ContainsKey(cell);

        /// <summary>The plant on a cell, or null.</summary>
        public Plant GetPlantAt(Vector2Int cell) =>
            _byCell.TryGetValue(cell, out var p) ? p : null;

        public int CountPlants(string speciesId, int minStage)
        {
            int count = 0;
            for (int i = 0; i < _plants.Count; i++)
            {
                var p = _plants[i];
                if (p != null && p.Species != null && p.Species.id == speciesId && p.Stage >= minStage)
                    count++;
            }
            return count;
        }

        // ---- planting / removal ------------------------------------------------

        /// <summary>Null if species is null, the cell is occupied, or the surface doesn't match.</summary>
        public Plant PlantSeed(PlantSpecies s, Vector2Int cell)
        {
            if (s == null || HasPlantAt(cell)) return null;
            if (VillainHoles.BlocksCell(cell)) return null;
            if (TerrainGrid.Instance == null || !s.GrowsOn(TerrainGrid.Instance.GetSurface(cell)))
                return null;
            if (s.shallowOnly && !TerrainGrid.Instance.IsShallowRim(cell)) return null; // water species: rim only

            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;
            var plant = Spawn(s, cell, now, 0f);

            // Soil enriched with compost before sowing carries into this crop.
            if (plant != null && plant.HasQuality && CompostManager.Instance != null
                && CompostManager.Instance.TakeEnrichment(cell))
                plant.ApplyCompost();
            return plant;
        }

        private Plant Spawn(PlantSpecies s, Vector2Int cell, float plantedAtTotalHours, float growthHours,
            float elapsedHours = 0f, float wateredHours = 0f, bool composted = false)
        {
            var go = new GameObject("Plant_" + s.id);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 0;
            if (spriteMaterial != null) renderer.material = spriteMaterial;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.4f;

            var plant = go.AddComponent<Plant>();
            plant.Init(s, cell, plantedAtTotalHours, growthHours, elapsedHours, wateredHours, composted);

            _plants.Add(plant);
            _byCell[cell] = plant;
            return plant;
        }

        public void RemovePlant(Plant p)
        {
            if (p == null) return;

            _plants.Remove(p);
            if (_byCell.TryGetValue(p.Cell, out var tracked) && tracked == p)
                _byCell.Remove(p.Cell);

            Destroy(p.gameObject);
        }

        // ---- ground-change death ------------------------------------------------

        private void HandleSurfaceChanged(Vector2Int cell, Surface surface)
        {
            if (!_byCell.TryGetValue(cell, out var plant) || plant == null) return;
            if (plant.Species != null && plant.Species.GrowsOn(surface)) return;

            string label = plant.Species != null ? plant.Species.displayName : "Plant";
            RemovePlant(plant);
            Debug.Log("[Plants] " + label + " died (ground changed).");
        }

        // ---- ISaveable -----------------------------------------------------------

        [Serializable]
        private struct PlantRecord
        {
            public string speciesId;
            public int cx, cy;
            public float plantedAt;
            public float growthHours; // accumulated effective growth (0 in old saves)
            public float elapsedHours; // quality cycle: game-hours since cycle start (0 in old saves)
            public float wateredHours; // quality cycle: of which the soil was wet
            public bool composted;
        }

        [Serializable]
        private class PlantsState
        {
            public List<PlantRecord> plants = new List<PlantRecord>();
        }

        public string SaveKey => "plants";

        public string Capture()
        {
            var state = new PlantsState();
            for (int i = 0; i < _plants.Count; i++)
            {
                var p = _plants[i];
                if (p == null || p.Species == null) continue;
                state.plants.Add(new PlantRecord
                {
                    speciesId = p.Species.id,
                    cx = p.Cell.x,
                    cy = p.Cell.y,
                    plantedAt = p.PlantedAtTotalHours,
                    growthHours = p.GrowthHours,
                    elapsedHours = p.ElapsedHours,
                    wateredHours = p.WateredHours,
                    composted = p.Composted
                });
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            // Clear whatever exists, then respawn from the save.
            for (int i = _plants.Count - 1; i >= 0; i--)
            {
                if (_plants[i] != null) Destroy(_plants[i].gameObject);
            }
            _plants.Clear();
            _byCell.Clear();

            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<PlantsState>(json);
            if (state == null || state.plants == null) return;

            for (int i = 0; i < state.plants.Count; i++)
            {
                var record = state.plants[i];
                var species = FindSpecies(record.speciesId);
                if (species == null)
                {
                    Debug.LogWarning("[Plants] Unknown species '" + record.speciesId + "' in save; skipped.");
                    continue;
                }

                var cell = new Vector2Int(record.cx, record.cy);
                if (_byCell.ContainsKey(cell)) continue;

                // OLD-SAVE MIGRATION: records written before watered growth
                // carry no growthHours (JsonUtility default 0). Seed progress
                // from elapsed time since planting — the old full-speed
                // derivation — so loading never sets anyone's crops back.
                float growthHours = record.growthHours;
                float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : record.plantedAt;
                if (growthHours == 0f && record.plantedAt < now)
                    growthHours = now - record.plantedAt;

                // Records from before crop quality carry no quality cycle: seed a
                // half-watered one (lands on Normal) so nothing reads as perfect for free.
                float elapsed = record.elapsedHours;
                float wetHours = record.wateredHours;
                if (elapsed <= 0f && growthHours > 0f) { elapsed = growthHours; wetHours = growthHours * 0.5f; }

                Spawn(species, cell, record.plantedAt, growthHours, elapsed, wetHours, record.composted);
            }
        }
    }
}

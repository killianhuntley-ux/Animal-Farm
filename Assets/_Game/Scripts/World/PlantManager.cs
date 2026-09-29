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
            if (TerrainGrid.Instance == null || TerrainGrid.Instance.GetSurface(cell) != s.requiredSurface)
                return null;

            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;
            return Spawn(s, cell, now);
        }

        private Plant Spawn(PlantSpecies s, Vector2Int cell, float plantedAtTotalHours)
        {
            var go = new GameObject("Plant_" + s.id);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 0;
            if (spriteMaterial != null) renderer.material = spriteMaterial;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.4f;

            var plant = go.AddComponent<Plant>();
            plant.Init(s, cell, plantedAtTotalHours);

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
            if (plant.Species != null && plant.Species.requiredSurface == surface) return;

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
                    plantedAt = p.PlantedAtTotalHours
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
                Spawn(species, cell, record.plantedAt);
            }
        }
    }
}

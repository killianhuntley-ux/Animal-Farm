using System;
using System.Collections.Generic;
using AnimalFarm.Core.Saving;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Owns placement and persistence of spirit homes (slice 04). Homes are
    /// free this slice (economy prices them in slice 09).
    /// </summary>
    public class HomeManager : MonoBehaviour, ISaveable
    {
        public static HomeManager Instance { get; private set; }

        [SerializeField] private Material spriteMaterial;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Places a home for a species at a cell. Null if occupied/invalid.</summary>
        public Home PlaceHome(SpiritSpeciesDefinition species, Vector2Int cell)
        {
            var grid = TerrainGrid.Instance;
            if (species == null || grid == null || !grid.InBounds(cell)) return null;
            if (grid.GetSurface(cell) == Surface.Water) return null;
            if (Home.AnyAtCell(cell)) return null;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return null;

            return Spawn(species, cell);
        }

        private Home Spawn(SpiritSpeciesDefinition species, Vector2Int cell)
        {
            var go = new GameObject("Home_" + species.id);
            go.transform.localScale = new Vector3(2.2f, 2.2f, 1f); // scale pass 2: homes must read BIGGER than the shepherd
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = species.homeSprite;
            sr.sortingOrder = 0;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            // Click-selection needs a Collider2D (trigger: homes never block).
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 1f);

            var home = go.AddComponent<Home>();
            home.Init(species.id, cell, TerrainGrid.Instance.CellCenterWorld(cell), species.displayName);
            AnimalFarm.UI.WorldLabel.Attach(go, species.displayName + " Home", -0.65f);
            return home;
        }

        // ---- ISaveable -------------------------------------------------------

        [Serializable]
        private struct HomeRecord { public string speciesId; public int cx, cy; }

        [Serializable]
        private class HomesState { public List<HomeRecord> homes = new List<HomeRecord>(); }

        public string SaveKey => "homes";

        public string Capture()
        {
            var state = new HomesState();
            foreach (var h in Home.All)
            {
                if (h == null) continue;
                state.homes.Add(new HomeRecord { speciesId = h.SpeciesId, cx = h.Cell.x, cy = h.Cell.y });
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            for (int i = Home.All.Count - 1; i >= 0; i--)
                if (Home.All[i] != null) Destroy(Home.All[i].gameObject);

            if (string.IsNullOrEmpty(json)) return;
            var state = JsonUtility.FromJson<HomesState>(json);
            if (state?.homes == null) return;

            var spirits = SpiritManager.Instance;
            foreach (var record in state.homes)
            {
                var species = spirits != null ? spirits.FindSpecies(record.speciesId) : null;
                if (species == null) continue;
                Spawn(species, new Vector2Int(record.cx, record.cy));
            }
        }
    }
}

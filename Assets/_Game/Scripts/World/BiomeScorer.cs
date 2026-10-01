using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>The biome identity a base (parcel cluster) can express.</summary>
    public enum BiomeType : byte
    {
        Barren = 0,    // default: no dominant character yet
        Grassland = 1, // grass-dominant
        Swamp = 2,     // standing water + plants at the water's edge
        Desert = 3     // sand-dominant
    }

    /// <summary>
    /// Muscle 02: the biome census. Every BASE (parcel cluster, per
    /// ParcelManager) is one biome canvas -- this ticks at 4 Hz (the
    /// RequirementEvaluator cadence), counts the base's OWNED terrain mix
    /// (locked cells are skipped via the usable mask), classifies it into a
    /// BiomeType, and scores it 0..100. Pure derived state: nothing to save,
    /// and events fire only on transitions, so gates and moods can subscribe
    /// without polling. Requirement atoms live in BiomeIsCondition.
    /// </summary>
    public class BiomeScorer : MonoBehaviour
    {
        public static BiomeScorer Instance { get; private set; }

        private const float TickInterval = 0.25f; // 4 Hz, like RequirementEvaluator

        // Classification thresholds (percent of the base's USABLE cells).
        // Checked in priority order: Desert, Swamp, Grassland, else Barren.
        private const float DesertSandPercent = 40f;
        private const float SwampWaterPercent = 12f;
        private const int SwampWetPlantCount = 2;   // plants standing beside water
        private const float GrasslandGrassPercent = 40f;

        /// <summary>(baseId, newBiome) -- fired only when a base's type flips.</summary>
        public event Action<int, BiomeType> OnBiomeChanged;

        /// <summary>(baseId, newScore) -- fired when a score moves a whole point.</summary>
        public event Action<int, float> OnScoreChanged;

        private BiomeType[] _biomes;
        private float[] _scores;
        private float _timer;
        private readonly List<Rect> _rectScratch = new List<Rect>();
        private readonly int[] _countScratch = new int[5]; // one slot per Surface

        public int BaseCount => _biomes != null ? _biomes.Length : 0;

        public BiomeType GetBiome(int baseId) =>
            _biomes != null && baseId >= 0 && baseId < _biomes.Length
                ? _biomes[baseId] : BiomeType.Barren;

        public float GetBiomeScore(int baseId) =>
            _scores != null && baseId >= 0 && baseId < _scores.Length
                ? _scores[baseId] : 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            EvaluateAll(); // establish initial state (initial flips fire as transitions from Barren)
        }

        private void Update()
        {
            _timer += Time.deltaTime; // scaled: pause freezes the census
            if (_timer < TickInterval) return;
            _timer -= TickInterval;
            EvaluateAll();
        }

        private void EvaluateAll()
        {
            var parcels = ParcelManager.Instance;
            var grid = TerrainGrid.Instance;
            if (parcels == null || grid == null) return;

            if (_biomes == null || _biomes.Length != parcels.BaseCount)
            {
                _biomes = new BiomeType[parcels.BaseCount];
                _scores = new float[parcels.BaseCount];
            }

            for (int baseId = 0; baseId < _biomes.Length; baseId++)
            {
                CensusBase(baseId, grid, parcels, out BiomeType biome, out float score);

                if (biome != _biomes[baseId])
                {
                    _biomes[baseId] = biome;
                    Debug.Log("[Biome] base " + baseId + " -> " + biome);
                    OnBiomeChanged?.Invoke(baseId, biome);
                }

                int before = Mathf.RoundToInt(_scores[baseId]);
                _scores[baseId] = score;
                if (Mathf.RoundToInt(score) != before)
                    OnScoreChanged?.Invoke(baseId, score);
            }
        }

        /// <summary>Counts the base's usable cells by surface plus its plants
        /// standing beside water, then classifies the mix.</summary>
        private void CensusBase(int baseId, TerrainGrid grid, ParcelManager parcels,
            out BiomeType biome, out float score)
        {
            parcels.GetBaseParcelRects(baseId, _rectScratch);
            for (int i = 0; i < _countScratch.Length; i++) _countScratch[i] = 0;
            int total = 0;

            for (int r = 0; r < _rectScratch.Count; r++)
            {
                var rect = _rectScratch[r];
                if (!grid.TryWorldToCell(new Vector3(rect.xMin + 0.5f, rect.yMin + 0.5f), out var min) ||
                    !grid.TryWorldToCell(new Vector3(rect.xMax - 0.5f, rect.yMax - 0.5f), out var max))
                    continue;

                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!grid.IsUsable(cell)) continue; // locked land never skews the mix
                    total++;
                    _countScratch[(int)grid.GetSurface(cell)]++;
                }
            }

            int wetPlants = CountWetPlants(grid);

            if (total == 0) { biome = BiomeType.Barren; score = 0f; return; }

            float grassPct = _countScratch[(int)Surface.Grass] * 100f / total;
            float waterPct = _countScratch[(int)Surface.Water] * 100f / total;
            float sandPct = _countScratch[(int)Surface.Sand] * 100f / total;

            if (sandPct >= DesertSandPercent)
            {
                biome = BiomeType.Desert;
                score = sandPct;
            }
            else if (waterPct >= SwampWaterPercent && wetPlants >= SwampWetPlantCount)
            {
                biome = BiomeType.Swamp;
                score = Mathf.Min(100f, waterPct * 2f + wetPlants * 8f);
            }
            else if (grassPct >= GrasslandGrassPercent)
            {
                biome = BiomeType.Grassland;
                score = grassPct;
            }
            else
            {
                biome = BiomeType.Barren;
                score = 0f;
            }
        }

        /// <summary>Plants inside this base (rects already in _rectScratch)
        /// whose cell touches water on a 4-neighbour -- the swamp's lifeblood.</summary>
        private int CountWetPlants(TerrainGrid grid)
        {
            var plantManager = PlantManager.Instance;
            if (plantManager == null) return 0;

            int wet = 0;
            var all = plantManager.AllPlants;
            for (int i = 0; i < all.Count; i++)
            {
                var plant = all[i];
                if (plant == null) continue;

                Vector3 world = grid.CellCenterWorld(plant.Cell);
                bool inBase = false;
                for (int r = 0; r < _rectScratch.Count && !inBase; r++)
                    inBase = _rectScratch[r].Contains(world);
                if (!inBase) continue;

                var c = plant.Cell;
                if (grid.GetSurface(new Vector2Int(c.x - 1, c.y)) == Surface.Water ||
                    grid.GetSurface(new Vector2Int(c.x + 1, c.y)) == Surface.Water ||
                    grid.GetSurface(new Vector2Int(c.x, c.y - 1)) == Surface.Water ||
                    grid.GetSurface(new Vector2Int(c.x, c.y + 1)) == Surface.Water)
                    wet++;
            }
            return wet;
        }
    }
}

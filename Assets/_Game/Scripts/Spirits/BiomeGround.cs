using System.Collections.Generic;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Muscle 02 glue between where a spirit stands and the biome of the base
    /// (parcel cluster) under it: base lookup by position, the base's censused
    /// biome, and the affinity-weighted silhouette spawn table. Pure static
    /// reads over ParcelManager + BiomeScorer; nothing to save. Positions off
    /// every base (the road corridor, the plaza) read as base -1 / Barren, and
    /// callers treat that as "no opinion".
    /// </summary>
    public static class BiomeGround
    {
        /// <summary>Legacy bias while a base is still Barren: a species whose native
        /// ground is on the other side of the road is 4x less likely to show up.</summary>
        private const float WrongRegionWeightMul = 0.25f;

        private static readonly List<Rect> _rects = new List<Rect>();

        /// <summary>Base id (parcel cluster) containing a world point, or -1.</summary>
        public static int BaseAt(Vector3 pos)
        {
            var parcels = ParcelManager.Instance;
            if (parcels == null) return -1;

            var p = new Vector2(pos.x, pos.y);
            int count = parcels.BaseCount;
            for (int b = 0; b < count; b++)
            {
                parcels.GetBaseParcelRects(b, _rects);
                for (int r = 0; r < _rects.Count; r++)
                    if (_rects[r].Contains(p)) return b;
            }
            return -1;
        }

        /// <summary>The censused biome of a base (Barren when unknown).</summary>
        public static BiomeType BiomeOfBase(int baseId)
        {
            var scorer = BiomeScorer.Instance;
            return scorer != null && baseId >= 0 ? scorer.GetBiome(baseId) : BiomeType.Barren;
        }

        /// <summary>Biome under a world point (Barren off every base).</summary>
        public static BiomeType BiomeAt(Vector3 pos) => BiomeOfBase(BaseAt(pos));

        /// <summary>True once at least one field of the base is owned (usable ground).</summary>
        public static bool BaseHasLand(int baseId)
        {
            var parcels = ParcelManager.Instance;
            var grid = TerrainGrid.Instance;
            if (parcels == null || grid == null) return false;

            parcels.GetBaseParcelRects(baseId, _rects);
            for (int r = 0; r < _rects.Count; r++)
                if (grid.IsWorldUsable(new Vector3(_rects[r].center.x, _rects[r].center.y, 0f))) return true;
            return false;
        }

        /// <summary>
        /// Relative weight for this species' silhouette appearing at a base,
        /// from its feeling about the base's CURRENT biome (Love 6 / Like 3 /
        /// Neutral 1 / Dislike 0.2 / HardNo 0). While the base is still Barren
        /// (no affinity information) the old homeland rule applies: swamp-native
        /// species favour the swamp base, everyone else the home base.
        /// </summary>
        public static float SpawnWeight(SpiritSpeciesDefinition species, int baseId)
        {
            var biome = BiomeOfBase(baseId);
            float w = BiomeAffinity.SpawnWeight(BiomeAffinity.For(species, biome));
            if (w <= 0f) return 0f;

            if (biome == BiomeType.Barren)
            {
                bool nativeSwamp = BiomeAffinity.NativeBiome(species) == BiomeType.Swamp;
                bool swampBase = baseId == ParcelManager.SwampBaseId;
                if (nativeSwamp != swampBase) w *= WrongRegionWeightMul;
            }
            return w;
        }
    }
}

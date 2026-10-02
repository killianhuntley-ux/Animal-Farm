using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Frontier half of the SpiritManager (muscle 08): wild silhouettes appear
    /// at the base their species feels best about. Muscle 02 verdict 1 made
    /// that a 5-step affinity weighting per BASE, read from the base's current
    /// censused biome (BiomeGround.SpawnWeight): Love strongly favoured, Like
    /// favoured, Neutral normal, Dislike rare, Hard No never. The species'
    /// requirement gates stay authoritative - the Update loop only asks for a
    /// spawn point once the Appear gate is open; affinity only decides WHERE
    /// (and refuses when every owned base is a Hard No).
    /// </summary>
    public partial class SpiritManager
    {
        private const int SpawnPointTries = 40;

        /// <summary>
        /// Picks a border point for a new silhouette of this species by
        /// affinity-weighted base choice. False when no owned base would have
        /// it (all Hard No / no land): the caller skips the spawn this tick.
        /// Degrades to the old swamp/non-swamp split when the biome systems
        /// are missing (old scenes).
        /// </summary>
        private static bool TryPickSpawnPoint(SpiritSpeciesDefinition species, out Vector3 point)
        {
            var parcels = ParcelManager.Instance;
            if (parcels == null || BiomeScorer.Instance == null || TerrainGrid.Instance == null)
            {
                point = LegacySpawnPoint(species);
                return true;
            }

            int bases = parcels.BaseCount;
            var weights = new float[bases];
            float total = 0f;
            for (int b = 0; b < bases; b++)
            {
                if (!BiomeGround.BaseHasLand(b)) continue;
                weights[b] = BiomeGround.SpawnWeight(species, b);
                total += weights[b];
            }

            while (total > 0f)
            {
                float roll = Random.value * total;
                int pick = -1;
                for (int b = 0; b < bases; b++)
                {
                    if (weights[b] <= 0f) continue;
                    pick = b;
                    roll -= weights[b];
                    if (roll <= 0f) break;
                }
                if (pick < 0) break;

                for (int i = 0; i < SpawnPointTries; i++)
                {
                    Vector3 p = RandomBorderPoint();
                    if (BiomeGround.BaseAt(p) == pick) { point = p; return true; }
                }

                total -= weights[pick]; // no border sample landed there; try the next-best base
                weights[pick] = 0f;
            }

            point = default;
            return false;
        }

        /// <summary>Pre-affinity rule: swamp-native species at the swamp, the rest elsewhere.</summary>
        private static Vector3 LegacySpawnPoint(SpiritSpeciesDefinition species)
        {
            bool wantSwamp = BiomeAffinity.NativeBiome(species) == BiomeType.Swamp;
            for (int i = 0; i < SpawnPointTries; i++)
            {
                Vector3 p = RandomBorderPoint();
                if (FrontierGeometry.InSwamp(p) == wantSwamp) return p;
            }
            return RandomBorderPoint(); // degrade: wherever there is usable edge
        }
    }
}

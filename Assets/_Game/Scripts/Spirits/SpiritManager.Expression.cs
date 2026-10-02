using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>Muscle 03 console helpers (kept out of the main manager file).</summary>
    public partial class SpiritManager
    {
        /// <summary>
        /// Console cheat: spawns a Silhouette ~5 units from the shepherd and
        /// pins it (it ignores its Appear/Visit gates, so it neither despawns
        /// nor turns into a visitor) - for testing the shy fade-back.
        /// </summary>
        public SpiritAgent ForceSpawnSilhouette(string speciesId)
        {
            var species = FindSpecies(speciesId);
            if (species == null)
            {
                Debug.LogWarning($"[Spirits] ForceSpawnSilhouette: unknown species '{speciesId}'.");
                return null;
            }

            Vector3 pos = Vector3.zero;
            var player = GameObject.FindWithTag("Player");
            if (player != null)
                pos = player.transform.position + (Vector3)(Random.insideUnitCircle.normalized * 5f);
            pos.z = 0f;

            var agent = Spawn(species, SpiritState.Silhouette, pos);
            agent.Debug_PinSilhouette();
            return agent;
        }
    }
}

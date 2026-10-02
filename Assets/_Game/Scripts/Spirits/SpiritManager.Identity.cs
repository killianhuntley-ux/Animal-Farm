using System.Collections.Generic;
using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Identity half of the SpiritManager (muscle 05): the authored trait pool
    /// and the explicit-identity spawn path the weaving-inheritance work calls.
    /// </summary>
    public partial class SpiritManager
    {
        [Header("Identity (muscle 05)")]
        [Tooltip("Trait pool (ScriptableObjects). Empty = the in-code default pool (SpiritTraits.DefaultSpecs).")]
        [SerializeField] private SpiritTraitDefinition[] traitPool;

        /// <summary>The authored trait assets, or null/empty when the scene predates the wiring.</summary>
        public IReadOnlyList<SpiritTraitDefinition> TraitPool => traitPool;

        /// <summary>
        /// Spawns a fully-converted named Resident with an explicit identity:
        /// <paramref name="traitIds"/> (null/empty = roll them normally; 1 id =
        /// that trait kept and the rest rolled around it; max 2) and a
        /// <paramref name="bias"/> on the individual stat rolls inside the
        /// species band. The hook for weaving inheritance (one trait per parent,
        /// stats biased toward the parents).
        /// </summary>
        public SpiritAgent SpawnResidentWithIdentity(SpiritSpeciesDefinition species, string givenName,
            IReadOnlyList<string> traitIds, SpiritStatBias bias, Vector3 pos, float spirit = 70f)
        {
            if (species == null) return null;

            var agent = Spawn(species, SpiritState.Resident, pos);
            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;
            string name = string.IsNullOrWhiteSpace(givenName) ? species.displayName : givenName.Trim();
            agent.ApplyLoadedState(new SpiritSaveRecord
            {
                givenName = name,
                spirit = spirit,
                hunger01 = 0f,
                lastFed = now,
                residentSince = now
                // vigor/grace/gleam stay 0 -> ApplyLoadedState rolls fresh in-band stats
            });
            agent.ApplyIdentity(traitIds, bias);
            BumpDiscovery(species.id, DiscoveryLevel.Resident);
            return agent;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Weaving inheritance math (muscle 06, verdict 3): stat bias + trait
    /// pass-through. Static and side-effect free (reads the parents, rolls
    /// dice); SpiritManager.Weave applies the result.
    /// </summary>
    public static class WeaveInheritance
    {
        /// <summary>
        /// Stat bias: each parent's roll as a 0..1 position inside ITS OWN
        /// species band, averaged over the two parents and mapped to -1..1
        /// (0.5 = neutral). Strong parents, strong thread.
        /// </summary>
        public static SpiritStatBias StatBias(SpiritAgent a, SpiritAgent b)
        {
            return new SpiritStatBias(
                BiasFor(a, b, SpiritStat.Vigor),
                BiasFor(a, b, SpiritStat.Grace),
                BiasFor(a, b, SpiritStat.Gleam));
        }

        private static float BiasFor(SpiritAgent a, SpiritAgent b, SpiritStat stat)
        {
            float avg = (BandPosition(a, stat) + BandPosition(b, stat)) * 0.5f;
            return Mathf.Clamp((avg - 0.5f) * 2f, -1f, 1f);
        }

        /// <summary>0..1 position of a spirit's stat inside its species band (0.5 for a flat band).</summary>
        public static float BandPosition(SpiritAgent agent, SpiritStat stat)
        {
            if (agent == null) return 0.5f;
            agent.GetStatBand(stat, out int min, out int max);
            if (max <= min) return 0.5f;
            return Mathf.Clamp01((agent.GetStat(stat) - min) / (float)(max - min));
        }

        /// <summary>
        /// Final trait ids for a woven cryptid: one random trait from each
        /// parent (filtered by what the cryptid species allows, deduped, never
        /// a conflicting pair), the remainder rolled and filtered the same
        /// way. Returns 1..SpiritTraits.MaxTraits ids (0 only if the pool is
        /// exhausted by the species filter).
        /// </summary>
        public static List<string> PickTraits(SpiritAgent a, SpiritAgent b, SpiritSpeciesDefinition result)
        {
            var chosen = new List<SpiritTraitDefinition>(SpiritTraits.MaxTraits);

            TryInherit(a, result, chosen);
            TryInherit(b, result, chosen);

            // Fill to the normal 1-or-2 roll (never below what was inherited), species-filtered.
            int want = Mathf.Max(chosen.Count, Random.value < 0.6f ? 1 : 2);
            for (int attempt = 0; attempt < 8 && chosen.Count < want; attempt++)
            {
                var rolled = SpiritTraits.Roll(want, chosen);
                for (int i = 0; i < rolled.Count && chosen.Count < want; i++)
                {
                    var t = rolled[i];
                    if (t == null || chosen.Contains(t)) continue;
                    if (result != null && !result.AllowsTrait(t.id)) continue;
                    chosen.Add(t);
                }
            }

            var ids = new List<string>(chosen.Count);
            for (int i = 0; i < chosen.Count; i++) ids.Add(chosen[i].id);
            return ids;
        }

        /// <summary>Random pick among the parent's traits the cryptid may carry and that fit the set so far.</summary>
        private static void TryInherit(SpiritAgent parent, SpiritSpeciesDefinition result,
            List<SpiritTraitDefinition> chosen)
        {
            if (parent == null || chosen.Count >= SpiritTraits.MaxTraits) return;

            var pool = new List<SpiritTraitDefinition>(SpiritTraits.MaxTraits);
            var traits = parent.Traits;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (t == null || chosen.Contains(t)) continue;
                if (result != null && !result.AllowsTrait(t.id)) continue;
                if (Conflicts(chosen, t)) continue;
                pool.Add(t);
            }
            if (pool.Count == 0) return;
            chosen.Add(pool[Random.Range(0, pool.Count)]);
        }

        private static bool Conflicts(List<SpiritTraitDefinition> have, SpiritTraitDefinition t)
        {
            for (int i = 0; i < have.Count; i++)
                if (have[i].ConflictsWithId(t.id) || t.ConflictsWithId(have[i].id)) return true;
            return false;
        }
    }
}

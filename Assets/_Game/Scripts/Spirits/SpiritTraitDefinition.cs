using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// One individual trait as pure data (muscle 05). A spirit rolls 1-2 at
    /// spawn; traits weight idle-quirk frequencies (SpiritQuirks.Pick),
    /// training-building appetite (TrainingBuilding) and journal flavor text.
    /// Authored by ContentBootstrapper from SpiritTraits.DefaultSpecs; the
    /// runtime pool lives on SpiritManager (with an in-code fallback).
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Spirit Trait")]
    public class SpiritTraitDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea, Tooltip("Journal / inspect flavor line.")]
        public string flavor;

        [Header("Rolling")]
        [Tooltip("Relative chance of being picked at spawn.")]
        public float rollWeight = 1f;
        [Tooltip("Trait ids this one never co-exists with (checked in both directions).")]
        public string[] conflictsWith;

        [Header("Idle quirk weight multipliers (1 = species default)")]
        public float napMul = 1f;
        public float stretchMul = 1f;
        public float hopMul = 1f;
        public float leafChaseMul = 1f;

        [Header("Training appetite multipliers (1 = neutral)")]
        [Tooltip("How readily this spirit walks to any training building.")]
        public float trainingAppetite = 1f;
        public float vigorAppetite = 1f;
        public float graceAppetite = 1f;
        public float gleamAppetite = 1f;
        [Tooltip("Extra pull from a matching food bait on a training building.")]
        public float baitAppetite = 1f;

        /// <summary>Per-stat appetite on top of the general one.</summary>
        public float StatAppetite(SpiritStat stat) =>
            stat == SpiritStat.Vigor ? vigorAppetite
            : stat == SpiritStat.Grace ? graceAppetite : gleamAppetite;

        public bool ConflictsWithId(string otherId)
        {
            if (conflictsWith == null || string.IsNullOrEmpty(otherId)) return false;
            for (int i = 0; i < conflictsWith.Length; i++)
                if (conflictsWith[i] == otherId) return true;
            return false;
        }
    }
}

using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// Met when at least minCount spirits of a species are resident in the garden.
    /// Slice 03 wires the counting via <see cref="ResidentCounter"/>; until then
    /// every count is 0 and the condition is simply unmet.
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Resident Present", fileName = "ResidentPresentCondition")]
    public class ResidentPresentCondition : ConditionAsset
    {
        /// <summary>Hook for slice 03: speciesId -> current resident count. Null = no spirits system yet.</summary>
        public static System.Func<string, int> ResidentCounter;

        [SerializeField] private string speciesId = "";
        [SerializeField, Min(1)] private int minCount = 1;

        public override bool Evaluate()
        {
            var counter = ResidentCounter;
            int now = counter != null ? counter(speciesId) : 0;
            return now >= minCount;
        }

        public override string Describe()
        {
            var counter = ResidentCounter;
            if (counter == null)
                return $"Resident {speciesId} x{minCount} (no spirits yet)";
            return $"Resident {speciesId} x{minCount} (now {counter(speciesId)})";
        }
    }
}

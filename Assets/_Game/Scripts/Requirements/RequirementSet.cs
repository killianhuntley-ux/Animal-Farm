using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// An AND-group of conditions. Empty or null array evaluates true;
    /// null entries (empty inspector slots) are skipped.
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Requirements/Requirement Set", fileName = "RequirementSet")]
    public class RequirementSet : ScriptableObject
    {
        [SerializeField] private ConditionAsset[] conditions;

        /// <summary>Exposed for the debug overlay.</summary>
        public ConditionAsset[] Conditions => conditions;

        /// <summary>True when every condition is met (vacuously true when empty).</summary>
        public bool Evaluate()
        {
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Length; i++)
            {
                var c = conditions[i];
                if (c != null && !c.Evaluate()) return false;
            }
            return true;
        }
    }
}

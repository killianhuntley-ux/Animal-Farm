using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// What a condition is ABOUT, in player terms. Lets the want bubble of a
    /// visitor (muscle 11: "what would it need to stay?") pick an icon for the
    /// first unmet Stay condition without knowing every condition type.
    /// </summary>
    public enum ConditionTopic { Other, Plant, Water, Ground, Time }

    /// <summary>
    /// Base of the requirement engine: every gate condition is a composable
    /// ScriptableObject asset, never hardcoded logic. Implementations must be
    /// null-safe against missing singletons (Evaluate returns false).
    /// </summary>
    public abstract class ConditionAsset : ScriptableObject
    {
        /// <summary>True when the condition is currently met. Never throws.</summary>
        public abstract bool Evaluate();

        /// <summary>
        /// Short human-readable text including the current value,
        /// e.g. "Grass >= 10% (now 4.2%)". Used by the debug overlay.
        /// </summary>
        public abstract string Describe();

        /// <summary>What this condition is about (want-bubble icon hint). Default Other.</summary>
        public virtual ConditionTopic Topic => ConditionTopic.Other;
    }
}

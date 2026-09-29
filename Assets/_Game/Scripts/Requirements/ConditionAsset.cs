using UnityEngine;

namespace AnimalFarm.Requirements
{
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
    }
}

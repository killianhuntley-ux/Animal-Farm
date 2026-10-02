using System.Collections.Generic;
using AnimalFarm.Requirements;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Read helpers for a species' STAY gate (muscle 11): the RequirementSet in
    /// GateChain.resident that a visitor must see met before it may decide to
    /// join the flock on its own. Journal ("Stays when"), want bubbles, the
    /// visitor prompt and the console all read it through here so the
    /// escalation appear -> visit -> stay is described one way everywhere.
    /// Pure reads; nothing to save.
    /// </summary>
    public static class StayGate
    {
        /// <summary>The Stay requirement set, or null when the species has none (closed).</summary>
        public static RequirementSet SetOf(SpiritSpeciesDefinition species) =>
            species != null && species.gateChain != null ? species.gateChain.resident : null;

        /// <summary>Is the Stay gate open right now (the evaluator's cached 4 Hz answer)?</summary>
        public static bool IsMet(SpiritSpeciesDefinition species)
        {
            var evaluator = RequirementEvaluator.Instance;
            return evaluator != null && species != null && species.gateChain != null
                && evaluator.IsGateOpen(species.gateChain.chainId, Gate.Resident);
        }

        /// <summary>The first condition of the Stay set that is not met, or null (all met / no set).</summary>
        public static ConditionAsset FirstUnmet(SpiritSpeciesDefinition species)
        {
            var set = SetOf(species);
            var conditions = set != null ? set.Conditions : null;
            if (conditions == null) return null;
            for (int i = 0; i < conditions.Length; i++)
            {
                var c = conditions[i];
                if (c != null && !c.Evaluate()) return c;
            }
            return null;
        }

        /// <summary>One "[x] text" / "[ ] text" line per condition, for the console.</summary>
        public static List<string> StatusLines(SpiritSpeciesDefinition species)
        {
            var lines = new List<string>();
            var set = SetOf(species);
            if (set == null)
            {
                lines.Add("(no Stay gate authored - this species never decides to stay)");
                return lines;
            }

            var conditions = set.Conditions;
            int shown = 0;
            if (conditions != null)
            {
                for (int i = 0; i < conditions.Length; i++)
                {
                    var c = conditions[i];
                    if (c == null) continue;
                    lines.Add((c.Evaluate() ? "[x] " : "[ ] ") + c.Describe());
                    shown++;
                }
            }
            if (shown == 0) lines.Add("(empty set: always met)");
            return lines;
        }

        /// <summary>Chance multiplier for one stay roll from the ground it stands on (HardNo never rolls).</summary>
        public static float GroundChanceMul(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return 0f;
                case Affinity.Dislike: return 0.5f;
                case Affinity.Like: return 1.1f;
                case Affinity.Love: return 1.25f;
                default: return 1f;
            }
        }
    }
}

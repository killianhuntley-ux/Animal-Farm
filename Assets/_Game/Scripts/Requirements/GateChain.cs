using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>The four progression gates every spirit chain moves through.
    /// Appear = silhouette shows up, Visit = it walks in as a visitor, Resident = the
    /// STAY gate (while it is met a visitor may decide to join the flock - muscle 11),
    /// Fulfil = reserved.</summary>
    public enum Gate
    {
        Appear,
        Visit,
        Resident,
        Fulfil
    }

    /// <summary>
    /// One spirit's progression: the requirement set behind each gate.
    /// A null set means that gate is CLOSED (never open) — gates must be
    /// authored deliberately; callers treat GetSet() == null as closed.
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Requirements/Gate Chain", fileName = "GateChain")]
    public class GateChain : ScriptableObject
    {
        public string chainId;

        public RequirementSet appear;
        public RequirementSet visit;
        public RequirementSet resident;
        public RequirementSet fulfil;

        /// <summary>The set guarding a gate. May be null = the gate is closed.</summary>
        public RequirementSet GetSet(Gate g)
        {
            switch (g)
            {
                case Gate.Appear: return appear;
                case Gate.Visit: return visit;
                case Gate.Resident: return resident;
                case Gate.Fulfil: return fulfil;
                default: return null;
            }
        }
    }
}

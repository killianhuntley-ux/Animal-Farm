using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// One weaving recipe (slice 06): two base species, both at MAX Spirit,
    /// consumed to create a cryptid. Order-independent. Recipes are never shown
    /// in the journal — discovery is the experimentation layer (GDD 2.7).
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Weave Recipe")]
    public class WeaveRecipe : ScriptableObject
    {
        public SpiritSpeciesDefinition parentA;
        public SpiritSpeciesDefinition parentB;
        public SpiritSpeciesDefinition result;

        [Tooltip("Essence consumed by the ritual (owner decision: essence is the weave currency; deeper weaves cost more).")]
        public int essenceCost = 6;

        public bool Matches(SpiritSpeciesDefinition x, SpiritSpeciesDefinition y)
        {
            if (x == null || y == null || parentA == null || parentB == null) return false;
            return (x.id == parentA.id && y.id == parentB.id)
                || (x.id == parentB.id && y.id == parentA.id);
        }
    }
}

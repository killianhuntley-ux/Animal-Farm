using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>Met when a world object with the given id is registered as present.</summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Object Present", fileName = "ObjectPresentCondition")]
    public class ObjectPresentCondition : ConditionAsset
    {
        [SerializeField] private string objectId = "";

        public override bool Evaluate() => WorldObjectRegistry.IsPresent(objectId);

        public override string Describe()
        {
            bool present = WorldObjectRegistry.IsPresent(objectId);
            return $"Object '{objectId}' present (now {(present ? "yes" : "no")})";
        }
    }
}

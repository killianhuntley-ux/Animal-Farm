using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>Met when at least minCount plants of a species have reached minStage.</summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Plant Count", fileName = "PlantCountCondition")]
    public class PlantCountCondition : ConditionAsset
    {
        [SerializeField] private string speciesId = "";
        [SerializeField, Min(0)] private int minStage;
        [SerializeField, Min(1)] private int minCount = 1;

        public override bool Evaluate()
        {
            var plants = PlantManager.Instance;
            return plants != null && plants.CountPlants(speciesId, minStage) >= minCount;
        }

        public override ConditionTopic Topic => ConditionTopic.Plant;

        public override string Describe()
        {
            var plants = PlantManager.Instance;
            int now = plants != null ? plants.CountPlants(speciesId, minStage) : 0;
            return $"{speciesId} x{minCount} at stage {minStage}+ (now {now})";
        }
    }
}

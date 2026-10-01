using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>Met when a base's censused biome matches, optionally at a
    /// minimum quality score (muscle 02: `BiomeIs` / `BiomeScore >=` atoms).</summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Biome Is", fileName = "BiomeIsCondition")]
    public class BiomeIsCondition : ConditionAsset
    {
        [Tooltip("Which base (parcel cluster): 0 = home, 1 = swamp.")]
        [SerializeField] private int baseId = ParcelManager.HomeBaseId;
        [SerializeField] private BiomeType biome = BiomeType.Grassland;
        [Tooltip("0 = type match alone is enough.")]
        [SerializeField, Min(0f)] private float minScore = 0f;

        public override bool Evaluate()
        {
            var scorer = BiomeScorer.Instance;
            return scorer != null
                && scorer.GetBiome(baseId) == biome
                && scorer.GetBiomeScore(baseId) >= minScore;
        }

        public override string Describe()
        {
            var scorer = BiomeScorer.Instance;
            var now = scorer != null ? scorer.GetBiome(baseId) : BiomeType.Barren;
            float score = scorer != null ? scorer.GetBiomeScore(baseId) : 0f;
            string want = minScore > 0f ? $"{biome} >= {minScore:0}" : biome.ToString();
            return $"Base {baseId} biome is {want} (now {now}, score {score:0})";
        }
    }
}

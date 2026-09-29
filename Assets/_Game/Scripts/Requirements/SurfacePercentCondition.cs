using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>Met when a surface type covers at least a given percentage of the field.</summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Surface Percent", fileName = "SurfacePercentCondition")]
    public class SurfacePercentCondition : ConditionAsset
    {
        [SerializeField] private Surface surface = Surface.Grass;
        [SerializeField, Min(0f)] private float minPercent = 10f;

        public override bool Evaluate()
        {
            var grid = TerrainGrid.Instance;
            return grid != null && grid.SurfacePercent(surface) >= minPercent;
        }

        public override string Describe()
        {
            var grid = TerrainGrid.Instance;
            float now = grid != null ? grid.SurfacePercent(surface) : 0f;
            return $"{surface} >= {minPercent:0.#}% (now {now:0.#}%)";
        }
    }
}

using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Data asset for one plantable species (slice 02). Sprites run
    /// seedling..mature; stage count = stageSprites.Length.
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Plant Species")]
    public class PlantSpecies : ScriptableObject
    {
        public string id;
        public string displayName;

        [Tooltip("Seedling..mature; length = stage count.")]
        public Sprite[] stageSprites;

        [Tooltip("Game-hours per growth stage.")]
        public float hoursPerStage = 8f;

        public Surface requiredSurface = Surface.Dirt;

        public string produceId;
        public int produceAmount = 1;

        public Color tint = Color.white;

        [Header("Muscle 02")]
        [Tooltip("Berry-bush style: harvesting drops the plant back to regrowStage instead of removing it.")]
        public bool regrows;

        [Tooltip("Stage index a regrowing plant returns to after harvest.")]
        public int regrowStage = 1;

        [Tooltip("Water species only (requiredSurface = Water): plantable on shallow-rim water cells only.")]
        public bool shallowOnly;

        /// <summary>True if this species can root on the given ground: its required
        /// surface, plus rich Mud for every tilled-soil (Dirt) crop.</summary>
        public bool GrowsOn(Surface s) =>
            s == requiredSurface || (requiredSurface == Surface.Dirt && s == Surface.Mud);
    }
}

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
    }
}

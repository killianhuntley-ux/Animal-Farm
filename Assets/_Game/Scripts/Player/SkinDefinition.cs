using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Pure-data cosmetic skin: sprites plus a tint. Animation lives elsewhere
    /// (ShepherdVisual), so skins swap without touching movement or feel.
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Skin Definition")]
    public class SkinDefinition : ScriptableObject
    {
        public Sprite body;
        public Sprite head;
        public Color tint = Color.white;
    }
}

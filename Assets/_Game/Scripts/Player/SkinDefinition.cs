using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Pure-data cosmetic skin: sprites plus a tint. Animation lives elsewhere
    /// (ShepherdVisual), so skins swap without touching movement or feel.
    /// Directional slots (muscle 01): up/down body+head sprites are optional --
    /// any null slot falls back to the side sprites below, so old skins keep
    /// working unchanged (side pose + flipX only).
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Skin Definition")]
    public class SkinDefinition : ScriptableObject
    {
        [Header("Side pose (required; also the fallback for up/down)")]
        public Sprite body;
        public Sprite head;

        [Header("Up pose (optional; null -> side sprites)")]
        public Sprite bodyUp;
        public Sprite headUp;

        [Header("Down pose (optional; null -> side sprites)")]
        public Sprite bodyDown;
        public Sprite headDown;

        public Color tint = Color.white;

        /// <summary>Body sprite for a facing bucket, with side fallback.</summary>
        public Sprite BodyFor(FacingPose pose)
        {
            switch (pose)
            {
                case FacingPose.Up: return bodyUp != null ? bodyUp : body;
                case FacingPose.Down: return bodyDown != null ? bodyDown : body;
                default: return body;
            }
        }

        /// <summary>Head sprite for a facing bucket, with side fallback.</summary>
        public Sprite HeadFor(FacingPose pose)
        {
            switch (pose)
            {
                case FacingPose.Up: return headUp != null ? headUp : head;
                case FacingPose.Down: return headDown != null ? headDown : head;
                default: return head;
            }
        }
    }

    /// <summary>Dominant-axis facing bucket used to pick a skin pose.</summary>
    public enum FacingPose { Side, Up, Down }
}

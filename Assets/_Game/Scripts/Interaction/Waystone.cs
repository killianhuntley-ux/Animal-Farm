using UnityEngine;

namespace AnimalFarm.Interaction
{
    /// <summary>
    /// Test interactable for the vertical slice: inspecting it cycles the sprite
    /// through a color palette and logs a flavor line, proving the interaction
    /// loop (focus highlight, prompt, interact) end to end.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Waystone : MonoBehaviour, IInteractable
    {
        [SerializeField] private Color[] palette =
        {
            new Color(0.78f, 0.83f, 0.72f), // weathered sage
            new Color(0.62f, 0.71f, 0.80f), // dusk blue
            new Color(0.87f, 0.76f, 0.58f), // dry ochre
            new Color(0.80f, 0.66f, 0.68f)  // faded rose
        };

        private static readonly string[] FlavorLines =
        {
            "The stone hums. It remembers being a mountain.",
            "It is faintly warm. You decide not to ask why.",
            "Nothing happens, which the stone considers a great success.",
            "The stone approves of you the way stones do: silently, and forever."
        };

        private SpriteRenderer _renderer;
        private Vector3 _initialScale;
        private int _colorIndex = -1;

        public string PromptText => "Inspect";

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _initialScale = transform.localScale;
            AnimalFarm.UI.WorldLabel.Attach(gameObject, "Waystone", -0.75f);
        }

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor)
        {
            if (palette == null || palette.Length == 0) return;

            _colorIndex = (_colorIndex + 1) % palette.Length;
            _renderer.color = palette[_colorIndex];
            Debug.Log(FlavorLines[_colorIndex % FlavorLines.Length], this);
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.9f,
                FlavorLines[_colorIndex % FlavorLines.Length],
                new Color(0.95f, 0.95f, 0.85f));
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _initialScale * 1.08f : _initialScale;
        }
    }
}

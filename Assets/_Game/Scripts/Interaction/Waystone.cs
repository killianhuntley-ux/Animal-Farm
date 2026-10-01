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

        /// <summary>
        /// Runtime factory for the buildable Waystone. The SpriteRenderer MUST
        /// be added (and filled) before AddComponent&lt;Waystone&gt;: AddComponent
        /// runs Awake immediately, and Awake reads GetComponent&lt;SpriteRenderer&gt;.
        /// Multiple waystones may coexist.
        /// </summary>
        public static Waystone Create(Vector3 pos, Sprite sprite, Material mat)
        {
            var go = new GameObject("Waystone");
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (mat != null) sr.sharedMaterial = mat;

            // Trigger volume for the InteractionSensor (matches the bootstrapped one).
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.2f, 1.4f);

            return go.AddComponent<Waystone>();
        }

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

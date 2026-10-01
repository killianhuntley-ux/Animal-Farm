using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// RETIRED (muscle 04, verdict 3): the ascension site is a BUILDABLE now
    /// (<see cref="AscensionPad"/> + <see cref="StyxCrossingCeremony"/>), so
    /// this fixed altar no longer runs any ceremony — there is exactly one
    /// crossing path, and it is the pad. The class survives only so scenes
    /// baked with the old bootstrapped altar still load: it renders as a
    /// quiet relic whose interaction points the player at the pad. The main
    /// agent removes the altar's creation from SceneBootstrapper; once the
    /// scene is rebuilt this component never spawns again and the file can
    /// be deleted outright.
    /// </summary>
    public class AscensionAltar : MonoBehaviour, IInteractable
    {
        private static readonly Color IdleGlow = new Color(0.75f, 0.85f, 1f, 0.3f);
        private static readonly Color PaleGreyBlue = new Color(0.70f, 0.75f, 0.85f, 1f);

        // Field names kept verbatim: the baked scene serialized them.
        [Header("Visuals (assigned by bootstrapper)")]
        [SerializeField] private Sprite platformSprite;
        [SerializeField] private Sprite glowSprite;
        [SerializeField] private Sprite columnSprite; // unused since the Styx crossing
        [SerializeField] private Material spriteMaterial;

        private SpriteRenderer _platform;
        private SpriteRenderer _glow;
        private bool _focused;

        private void Start()
        {
            _platform = MakeChild("Platform", platformSprite, -5);
            _glow = MakeChild("Glow", glowSprite, -4);

            WorldLabel.Attach(gameObject, "Old Altar", -1.0f);

            // Trigger volume for the InteractionSensor.
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(2.2f, 1.6f);
        }

        private SpriteRenderer MakeChild(string childName, Sprite sprite, int order)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }

        private void Update()
        {
            // The relic only breathes faintly now — no gold charge, no column.
            if (_glow == null) return;
            float wave = (Mathf.Sin(Time.time * 1.6f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.10f, 0.22f, wave);
            if (_focused) alpha = Mathf.Min(1f, alpha + 0.05f);
            _glow.color = new Color(IdleGlow.r, IdleGlow.g, IdleGlow.b, alpha);
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText => "An old, quiet altar";

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor)
        {
            bool padKnown = AscensionPadManager.Instance != null
                && AscensionPadManager.Instance.FulfilmentSeen;
            FloatingText.Show(transform.position + Vector3.up * 1.5f,
                padKnown
                    ? "(the altar sleeps. The crossing happens at an Ascension Pad now)"
                    : "(the altar sleeps. Something else will carry them across)",
                PaleGreyBlue);
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
        }
    }
}

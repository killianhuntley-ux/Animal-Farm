using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// The vendor's stall (slice 09 economy). A fixed world fixture, like the
    /// CompetitionBoard: walk up and press Interact (or click "Trade") to open
    /// the VendorUI modal where produce and essence are sold for coins and
    /// treats are bought.
    /// </summary>
    public class VendorStall : MonoBehaviour, IInteractable, ISelectable
    {
        private const float FocusScale = 1.06f;

        [SerializeField] private Sprite stallSprite;
        [SerializeField] private Material spriteMaterial;

        private Vector3 _baseScale = Vector3.one;

        private void Start()
        {
            transform.localScale = Vector3.one * 1.6f; // greybox readability (spirit-sized fixtures)
            _baseScale = transform.localScale;

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = stallSprite;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.6f);

            WorldLabel.Attach(gameObject, "Vendor", -1.0f);
        }

        // ------------------------------------------------------- IInteractable

        public string PromptText => "Trade";

        public bool CanInteract(GameObject actor) => VendorUI.Instance != null;

        public void Interact(GameObject actor)
        {
            var ui = VendorUI.Instance;
            if (ui != null) ui.Open();
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "Vendor";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Trade", () => Interact(gameObject)));
        }
    }
}

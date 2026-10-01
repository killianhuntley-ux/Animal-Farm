using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// The Ferryman's land office stall. A fixed world fixture, like the
    /// VendorStall: walk up and press Interact (or click "Land deeds") to open
    /// the LandOfficeUI modal -- the parcel overview where land expansions are
    /// bought for coins (obols, the ferryman's toll).
    /// </summary>
    public class FerrymanStall : MonoBehaviour, IInteractable, ISelectable
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

            WorldLabel.Attach(gameObject, "The Ferryman", -1.0f);
        }

        // ------------------------------------------------------- IInteractable

        public string PromptText => "Land deeds";

        public bool CanInteract(GameObject actor) => LandOfficeUI.Instance != null;

        public void Interact(GameObject actor)
        {
            LandOfficeUI.Instance?.Open();
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "The Ferryman";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Land deeds", () =>
            {
                LandOfficeUI.Instance?.Open();
            }));
        }
    }
}

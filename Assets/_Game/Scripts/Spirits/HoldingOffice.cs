using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The Repo-man's holding office (slice 07): a fixed building where
    /// repossessed spirits wait behind the counter until the shepherd pays
    /// the reclaim fee. Walk-up interact or click-select opens the
    /// HoldingOfficeUI; reclaimed spirits are released at DropPoint.
    /// </summary>
    public class HoldingOffice : MonoBehaviour, IInteractable, ISelectable
    {
        public static HoldingOffice Instance { get; private set; }

        private const float BuildingScale = 1.8f;
        private const float FocusScale = 1.08f;

        [SerializeField] private Sprite officeSprite;
        [Tooltip("Shared sprite material (e.g. the URP 2D lit material the other buildings use).")]
        [SerializeField] private Material spriteMaterial;

        /// <summary>Where reclaimed spirits reappear, just in front of the door.</summary>
        public Vector3 DropPoint => transform.position + Vector3.down * 1.4f;

        private Vector3 _baseScale = Vector3.one;
        private bool _focused;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            transform.localScale = Vector3.one * BuildingScale;
            _baseScale = transform.localScale;

            var renderer = GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<SpriteRenderer>();
            if (officeSprite != null) renderer.sprite = officeSprite;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;
            renderer.sortingOrder = 0;

            var col = GetComponent<BoxCollider2D>();
            if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.6f);

            WorldLabel.Attach(gameObject, "Holding Office");
        }

        private static void OpenUI()
        {
            var ui = HoldingOfficeUI.Instance;
            if (ui != null) ui.Open();
        }

        // ---- IInteractable ---------------------------------------------------

        public string PromptText => "Holding Office";

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor) => OpenUI();

        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ---- ISelectable -----------------------------------------------------

        public string SelectableTitle => "Holding Office";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Enter", OpenUI));
        }
    }
}

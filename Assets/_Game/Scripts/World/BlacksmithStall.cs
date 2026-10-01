using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// The Blacksmith's stall (muscle 01, verdict 1): a forge-spirit vendor
    /// selling tool TIER upgrades for farmer level + obols. Not bootstrapped --
    /// it moves into town only when VendorArrivals notices enough tilling
    /// (staggered move-in, verdict 3), spawned entirely at runtime.
    ///
    /// A fixed world fixture like the VendorStall: walk up and press Interact
    /// (or click "Talk") to open the BlacksmithShopUI modal. The sprite is the
    /// existing vendor stall's, tinted forge-dark; placement is a few units
    /// west of the VendorStall (found by name), with a sane fixed fallback if
    /// the town was never built.
    /// </summary>
    public class BlacksmithStall : MonoBehaviour, IInteractable, ISelectable
    {
        public static BlacksmithStall Instance { get; private set; }

        private const float FocusScale = 1.06f;

        /// <summary>West of the VendorStall, same market row.</summary>
        private static readonly Vector3 VendorOffset = new Vector3(-4f, 0f, 0f);

        /// <summary>Town plaza market row (bootstrapper geometry) if the
        /// VendorStall is missing -- degrade gracefully, never refuse to exist.</summary>
        private static readonly Vector3 FallbackPos = new Vector3(25f, 4.5f, 0f);

        /// <summary>Soot-and-ember tint over the borrowed stall sprite.</summary>
        private static readonly Color ForgeTint = new Color(0.55f, 0.42f, 0.45f, 1f);

        private Vector3 _baseScale = Vector3.one;

        /// <summary>
        /// Spawns the stall into the town (idempotent -- a second call while
        /// one stands is a no-op). Position + sprite borrow from the existing
        /// VendorStall when present.
        /// </summary>
        public static BlacksmithStall Spawn()
        {
            if (Instance != null) return Instance;

            Vector3 pos = FallbackPos;
            Sprite sprite = null;
            Material material = null;

            var vendorGo = GameObject.Find("VendorStall");
            if (vendorGo != null)
            {
                pos = vendorGo.transform.position + VendorOffset;
                var vendorSr = vendorGo.GetComponent<SpriteRenderer>();
                if (vendorSr != null)
                {
                    sprite = vendorSr.sprite;
                    material = vendorSr.sharedMaterial;
                }
            }

            var go = new GameObject("BlacksmithStall (runtime)");
            go.transform.position = pos;
            var stall = go.AddComponent<BlacksmithStall>();
            stall.BuildVisual(sprite, material);
            return stall;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Renderer + trigger + label (VendorStall's Start, inlined so
        /// Spawn can pass the borrowed sprite before any frame renders).</summary>
        private void BuildVisual(Sprite sprite, Material material)
        {
            transform.localScale = Vector3.one * 1.6f; // greybox readability (spirit-sized fixtures)
            _baseScale = transform.localScale;

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : MakeFallbackSprite();
            renderer.color = ForgeTint;
            if (material != null) renderer.sharedMaterial = material;

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.6f);

            WorldLabel.Attach(gameObject, "The Blacksmith", -1.0f);
        }

        /// <summary>Flat square so the stall reads even with no art to borrow.</summary>
        private static Sprite MakeFallbackSprite()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var pixels = new Color32[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        }

        // ------------------------------------------------------- IInteractable

        public string PromptText => "Talk";

        public bool CanInteract(GameObject actor) => true; // the UI self-creates on demand

        public void Interact(GameObject actor)
        {
            BlacksmithShopUI.GetOrCreate()?.Open();
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "The Blacksmith";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Talk", () => Interact(gameObject)));
        }
    }
}

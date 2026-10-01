using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// The traveling merchant (muscle 08/09, verdict 4): a calendar-driven
    /// visitor. Every 5th day of each season (day 5, 10, 15) a stall appears
    /// just inside the farm's town gate for that day only, then packs up at
    /// the next dawn. Subscribes to GameCalendar.DayChanged; nothing is saved
    /// -- presence is derived purely from the calendar, so it can never drift.
    ///
    /// Runtime-spawned via GetOrCreate + AfterSceneLoad bootstrap (GameCalendar
    /// pattern -- no scene setup, no bootstrapper edits).
    /// </summary>
    public class TravelingMerchantManager : MonoBehaviour
    {
        public static TravelingMerchantManager Instance { get; private set; }

        /// <summary>Day-of-season cadence: 5, 10, 15 in a 15-day season.</summary>
        private const int VisitEveryDays = 5;

        /// <summary>Just inside the town gate (home-cluster east gate sits at
        /// x=15, y=0; the plaza runs x 15..37.5), south of the foot traffic.</summary>
        private static readonly Vector3 StallPos = new Vector3(17.5f, -2.5f, 0f);

        private static readonly Color AnnounceGold = new Color(1f, 0.84f, 0.25f, 1f);

        private MerchantStall _stall;
        private bool _subscribed;
        private bool _settled; // initial presence check done (first Update)

        /// <summary>
        /// Returns the live manager, creating one on the fly -- it needs no
        /// scene setup, so runtime creation is safe (CompetitionManager pattern).
        /// </summary>
        public static TravelingMerchantManager GetOrCreate()
        {
            if (Instance == null)
                new GameObject("TravelingMerchant (runtime)").AddComponent<TravelingMerchantManager>();
            return Instance;
        }

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            var calendar = GameCalendar.GetOrCreate();
            if (calendar != null)
            {
                calendar.DayChanged += OnDayChanged;
                _subscribed = true;
            }
        }

        private void Update()
        {
            // Scene start / load lands mid-day: settle presence silently, on
            // the first Update -- after every scene Start, so the VendorStall
            // has built its SpriteRenderer for the stall to borrow.
            if (!_settled)
            {
                _settled = true;
                Refresh(announce: false);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && GameCalendar.Instance != null)
                GameCalendar.Instance.DayChanged -= OnDayChanged;

            if (Instance == this) Instance = null;
        }

        private void OnDayChanged(int day)
        {
            Refresh(announce: true);
        }

        /// <summary>True on the days the merchant bothers to show up.</summary>
        public static bool IsMerchantDay()
        {
            var calendar = GameCalendar.Instance;
            if (calendar == null) return false;
            return calendar.DayOfSeason % VisitEveryDays == 0;
        }

        /// <summary>Spawns/despawns the stall to match the calendar.</summary>
        private void Refresh(bool announce)
        {
            bool shouldBeHere = IsMerchantDay();

            if (shouldBeHere && _stall == null)
            {
                _stall = MerchantStall.Spawn(StallPos);
                if (announce && _stall != null)
                {
                    var player = GameObject.FindWithTag("Player");
                    Vector3 pos = player != null ? player.transform.position : Vector3.zero;
                    FloatingText.Show(pos + Vector3.up * 1.2f,
                        "A traveling merchant has pitched up by the gate.", AnnounceGold);
                    Bleeps.Play(BleepKind.Coin, 0.5f);
                }
            }
            else if (!shouldBeHere && _stall != null)
            {
                // Never strand the player mid-haggle when the day rolls over.
                MerchantShopUI.Instance?.CloseIfOpen();
                Destroy(_stall.gameObject);
                _stall = null;
            }
        }
    }

    /// <summary>
    /// The merchant's pop-up stall: a one-day world fixture like the
    /// VendorStall (sprite borrowed from it, road-dust tint; flat-square
    /// fallback if the town was never built). Interact opens MerchantShopUI,
    /// whose stock rotates deterministically with the day number.
    /// </summary>
    public class MerchantStall : MonoBehaviour, IInteractable, ISelectable
    {
        private const float FocusScale = 1.06f;

        /// <summary>Faded canvas-and-road-dust tint over the borrowed sprite.</summary>
        private static readonly Color WagonTint = new Color(0.72f, 0.60f, 0.78f, 1f);

        private Vector3 _baseScale = Vector3.one;

        /// <summary>Spawns the stall at a world position, borrowing the
        /// VendorStall's sprite/material when present.</summary>
        public static MerchantStall Spawn(Vector3 pos)
        {
            Sprite sprite = null;
            Material material = null;

            var vendorGo = GameObject.Find("VendorStall");
            if (vendorGo != null)
            {
                var vendorSr = vendorGo.GetComponent<SpriteRenderer>();
                if (vendorSr != null)
                {
                    sprite = vendorSr.sprite;
                    material = vendorSr.sharedMaterial;
                }
            }

            var go = new GameObject("MerchantStall (runtime)");
            go.transform.position = pos;
            var stall = go.AddComponent<MerchantStall>();
            stall.BuildVisual(sprite, material);
            return stall;
        }

        private void BuildVisual(Sprite sprite, Material material)
        {
            transform.localScale = Vector3.one * 1.6f; // greybox readability (spirit-sized fixtures)
            _baseScale = transform.localScale;

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : MakeFallbackSprite();
            renderer.color = WagonTint;
            if (material != null) renderer.sharedMaterial = material;

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.6f);

            WorldLabel.Attach(gameObject, "Traveling Merchant", -1.0f);
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

        public string PromptText => "Browse";

        public bool CanInteract(GameObject actor) => true; // the UI self-creates on demand

        public void Interact(GameObject actor)
        {
            MerchantShopUI.GetOrCreate()?.Open();
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "Traveling Merchant";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Browse", () => Interact(gameObject)));
        }
    }
}

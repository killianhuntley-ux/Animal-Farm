using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The vendor's trade modal (slice 09 economy). Opened by the VendorStall.
    /// SELL: one row per sellable item the player holds (hardcoded price
    /// table); BUY: seed packets (seeds are limited items now - one packet,
    /// one planting; the SeedPicker consumes them), the Follow Treat, the
    /// Watchlight (weed ward), plus a disabled Mystery Seed teaser. Currency
    /// reads as "obols" everywhere the player sees it, but remains Inventory
    /// item "coin". Gameplay input is blocked while open (SeedPicker pattern);
    /// the shell is built once, rows rebuilt on every open and after every
    /// transaction. Seed items surface in the pouch HUD automatically
    /// (Inventory-driven).
    /// </summary>
    public class VendorUI : MonoBehaviour
    {
        public static VendorUI Instance { get; private set; }

        private const int TreatPrice = 5;
        private const int MysterySeedPrice = 12;
        private const int WatchlightPrice = 30;
        private const int SandPackPrice = 8;   // terrain material: sand, by the load
        private const int SandPackLoads = 4;
        public const string SandLoadId = "sand_load";

        private static readonly Color CoinGold = new Color(1f, 0.9f, 0.5f);

        /// <summary>What the vendor buys, and for how much per unit. Items not
        /// listed here (coin, treat, ...) are not sellable.</summary>
        // "fiber" = scrap from pulled weeds (muscle 07 useful weeds): 1 obol,
        // so even weeding pays a little.
        private static readonly string[] SellIds = { "wheat", "berry", "bloom", "essence", "fiber" };
        private static readonly int[] SellPrices = { 3, 4, 5, 8, 1 };

        /// <summary>Seed packets the vendor sells (one packet = one planting).</summary>
        private static readonly string[] SeedIds =
            { "seed_grass", "seed_palewheat", "seed_gravebloom", "seed_murkberry", "seed_reed", "seed_glowcaplily" };
        private static readonly string[] SeedNames =
            { "Grass Seed", "Palewheat Seed", "Gravebloom Seed", "Murkberry Seed", "Reed Seed", "Glowcap Lily Seed" };
        private static readonly int[] SeedPrices = { 2, 3, 4, 4, 3, 5 };
        private static readonly string[] SeedSubs =
        {
            "one patch of living green",
            "a pale crop; spirits hunger for it",
            "a flower for the mournful",
            "a dark berry for darker tastes",
            "plant it on a pond's shallow rim; regrows",
            "floats on deep water; swamp-lovers swoon"
        };

        // Muscle 02 crop depth: water-crop produce plus the Fine / Gleaming tiers
        // of every crop sell here (Normal wheat/berry/bloom are in SellIds above).
        private static readonly string[] CropBaseIds = { "wheat", "berry", "bloom", "reed", "glowcap" };
        private static readonly int[] CropBasePrices = { 3, 4, 5, 3, 7 };
        private const int NormalCropsInSellIds = 3; // wheat, berry, bloom

        private GameObject _panel;
        private Text _coinsText;
        private RectTransform _content; // rows rebuilt on open + after trades
        private bool _open;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the trade modal.</summary>
        public void Open()
        {
            if (_open) return;
            if (_panel == null) BuildPanel();

            RebuildContent();

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling(); // render above the HUD
            _open = true;
            UIInputLock.ModalOpen = true;

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            UIInputLock.ModalOpen = false;
            if (_panel != null) _panel.SetActive(false);

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ------------------------------------------------------- transactions

        private void SellAll(string id, int unitPrice)
        {
            var inv = Inventory.Instance;
            if (inv == null) return;

            int count = inv.Count(id);
            if (count <= 0 || !inv.Consume(id, count)) { RebuildContent(); return; }

            int total = count * unitPrice;
            inv.Add("coin", total);
            FloatingText.Show(PlayerPos() + Vector3.up * 0.8f, "+" + total + " obols", CoinGold);

            RebuildContent();
        }

        /// <summary>Buys one seed packet (index into the Seed* tables).</summary>
        private void BuySeed(int index)
        {
            var inv = Inventory.Instance;
            if (inv == null || index < 0 || index >= SeedIds.Length) return;

            int price = SeedPrices[index];
            if (inv.Consume("coin", price))
            {
                inv.Add(SeedIds[index], 1);
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "+1 " + SeedNames[index].ToLowerInvariant(), CoinGold);
            }
            else
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs " + price + " obols)", UIStyle.Danger);
            }

            RebuildContent();
        }

        /// <summary>Terrain material goods (muscle 02): a pack of sand loads, one load
        /// paints one cell via the contextual picker (Interact on open ground).</summary>
        private void BuySand()
        {
            var inv = Inventory.Instance;
            if (inv == null) return;

            if (inv.Consume("coin", SandPackPrice))
            {
                inv.Add(SandLoadId, SandPackLoads);
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f, "+" + SandPackLoads + " sand loads", CoinGold);
            }
            else
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs " + SandPackPrice + " obols)", UIStyle.Danger);
            }

            RebuildContent();
        }

        private void BuyTreat()
        {
            var inv = Inventory.Instance;
            if (inv == null) return;

            if (inv.Consume("coin", TreatPrice))
            {
                inv.Add("treat", 1);
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f, "+1 treat", CoinGold);
            }
            else
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs " + TreatPrice + " obols)", UIStyle.Danger);
            }

            RebuildContent();
        }

        private void BuyWatchlight()
        {
            var inv = Inventory.Instance;
            if (inv == null) return;

            if (!inv.Consume("coin", WatchlightPrice))
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs " + WatchlightPrice + " obols)", UIStyle.Danger);
                RebuildContent();
                return;
            }

            // The vendor UI holds no sprite refs; WeedManager does the placing.
            var placed = WeedManager.Instance != null
                ? WeedManager.Instance.PlaceWatchlightNearPlayer()
                : null;

            if (placed == null)
            {
                // Owner law: never charge for nothing. Refund and say why.
                inv.Add("coin", WatchlightPrice);
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(no room here)", UIStyle.Danger);
            }
            else
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "watchlight set", CoinGold);
            }

            RebuildContent();
        }

        private static Vector3 PlayerPos()
        {
            var player = GameObject.FindWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------ UI

        private void BuildPanel()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("VendorUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(440f, 120f); // height grows via ContentSizeFitter

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true;

            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = UIRoot.MakeText(panelRt, "Title", 34, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Vendor";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var subtitle = UIRoot.MakeText(panelRt, "Subtitle", 19, TextAnchor.MiddleCenter, UIStyle.Grey);
            subtitle.text = "Everything has a price. Even for the dead.";
            subtitle.rectTransform.sizeDelta = new Vector2(0f, 28f);

            _coinsText = UIRoot.MakeText(panelRt, "Coins", 24, TextAnchor.MiddleCenter, UIStyle.Gold);
            _coinsText.rectTransform.sizeDelta = new Vector2(0f, 32f);

            // Rows live in a nested column so the shell survives rebuilds.
            _content = new GameObject("Content").AddComponent<RectTransform>();
            _content.SetParent(panelRt, false);

            var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentFitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _panel.SetActive(false);
        }

        private void RebuildContent()
        {
            if (_content == null) return;

            if (_coinsText != null && Inventory.Instance != null)
                _coinsText.text = "Obols: " + Inventory.Instance.Count("coin");

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            // ---- SELL ----
            MakeHeader(_content, "Sell");

            int sellable = 0;
            var inv = Inventory.Instance;
            if (inv != null)
            {
                for (int i = 0; i < SellIds.Length; i++)
                {
                    string id = SellIds[i];
                    int price = SellPrices[i];
                    int count = inv.Count(id);
                    if (count <= 0) continue;

                    string capturedId = id;    // capture for the click closure
                    int capturedPrice = price;
                    MakeButton(_content, "Sell all " + id + " (+" + (count * price) + " obols)",
                        56f, () => SellAll(capturedId, capturedPrice));
                    sellable++;
                }

                // Quality tiers (and water-crop produce): better stock, better price.
                for (int c = 0; c < CropBaseIds.Length; c++)
                for (int t = 0; t <= (int)CropTier.Gleaming; t++)
                {
                    if (t == (int)CropTier.Normal && c < NormalCropsInSellIds) continue;
                    var tier = (CropTier)t;
                    string id = CropQuality.ItemId(CropBaseIds[c], tier);
                    int count = inv.Count(id);
                    if (count <= 0) continue;

                    int unit = CropQuality.SellPrice(CropBasePrices[c], tier);
                    string capturedId = id;
                    MakeButton(_content, "Sell all " + CropQuality.DisplayName(id) + " (+" + (count * unit) + " obols)",
                        56f, () => SellAll(capturedId, unit));
                    sellable++;
                }
            }

            if (sellable == 0)
            {
                var empty = UIRoot.MakeText(_content, "Empty", 20, TextAnchor.MiddleCenter, UIStyle.Grey);
                empty.text = "(nothing the vendor wants)";
                empty.rectTransform.sizeDelta = new Vector2(0f, 32f);
            }

            UIStyle.MakeDivider(_content);

            // ---- BUY ----
            MakeHeader(_content, "Buy");

            for (int i = 0; i < SeedIds.Length; i++)
            {
                int captured = i; // capture for the click closure
                MakeTwoLineButton(_content, SeedNames[i] + " - " + SeedPrices[i] + " obols",
                    SeedSubs[i], () => BuySeed(captured), enabled: true);
            }

            MakeTwoLineButton(_content, "Follow Treat - " + TreatPrice + " obols",
                "makes any spirit tag along for a while", BuyTreat, enabled: true);

            MakeTwoLineButton(_content, "Watchlight - " + WatchlightPrice + " obols",
                "wards off weeds; place it where you wander least", BuyWatchlight, enabled: true);

            // Road gear (muscle 08): the lantern + per-villain ward charms.
            for (int g = 0; g < RoadGoods.TownStock.Length; g++)
            {
                var good = RoadGoods.TownStock[g]; // capture for the click closure
                MakeTwoLineButton(_content, good.label + " - " + good.price + " obols", good.sub,
                    () => { RoadGoods.Buy(good); RebuildContent(); }, enabled: true);
            }

            MakeTwoLineButton(_content, "Sand (" + SandPackLoads + " loads) - " + SandPackPrice + " obols",
                "one load paints one cell of open ground (Interact); arid biomes", BuySand, enabled: true);

            MakeTwoLineButton(_content, "Mystery Seed - " + MysterySeedPrice + " obols",
                "(soon)", null, enabled: false);

            UIStyle.MakeDivider(_content);

            MakeButton(_content, "Leave", 56f, Close);
        }

        private static void MakeHeader(Transform parent, string label)
        {
            var text = UIRoot.MakeText(parent, "Header_" + label, 20, TextAnchor.MiddleLeft, UIStyle.Grey);
            text.text = label;
            text.rectTransform.sizeDelta = new Vector2(0f, 28f);
        }

        private static void MakeButton(Transform parent, string label, float height,
            UnityEngine.Events.UnityAction onClick)
        {
            var button = MakeButtonBase(parent, label, height, onClick, interactable: true);

            var text = UIRoot.MakeText(button.transform, "Label", 24, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Two-line row (label + small grey sub-line); disabled rows
        /// render grey and ignore clicks.</summary>
        private static void MakeTwoLineButton(Transform parent, string label, string subLabel,
            UnityEngine.Events.UnityAction onClick, bool enabled)
        {
            var button = MakeButtonBase(parent, label, 66f, onClick, enabled);
            var rt = (RectTransform)button.transform;

            var text = UIRoot.MakeText(rt, "Label", 24, TextAnchor.MiddleCenter,
                enabled ? UIStyle.Cream : UIStyle.Grey);
            text.text = label;
            var textRt = text.rectTransform;
            textRt.anchorMin = new Vector2(0f, 0.4f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var sub = UIRoot.MakeText(rt, "SubLabel", 17, TextAnchor.MiddleCenter, UIStyle.Grey);
            sub.text = subLabel;
            var subRt = sub.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0f);
            subRt.anchorMax = new Vector2(1f, 0.4f);
            subRt.offsetMin = Vector2.zero;
            subRt.offsetMax = Vector2.zero;
        }

        private static Button MakeButtonBase(Transform parent, string name, float height,
            UnityEngine.Events.UnityAction onClick, bool interactable)
        {
            var go = new GameObject("Button_" + name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, height);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);
            button.interactable = interactable;

            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }
    }
}

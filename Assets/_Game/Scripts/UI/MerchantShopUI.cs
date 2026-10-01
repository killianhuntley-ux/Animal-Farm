using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The traveling merchant's shop modal (muscle 08/09, verdict 4). Opened
    /// by the one-day MerchantStall. Stock is 3 rows drawn DETERMINISTICALLY
    /// from the absolute day number (seeded System.Random -- same day, same
    /// stock, across saves and reopens): a seed packet at a small discount, a
    /// treat deal, and sometimes the rare curio row ("a jar of authentic
    /// underworld mist" -- flavor-only for now; buying grants a "curio" item).
    /// The stock pools are plain arrays, easy to extend.
    ///
    /// Modal pattern copied from VendorUI: UIInputLock.ModalOpen +
    /// SetGameplayBlocked with the pause-respecting restore, UIStyle look,
    /// shell built once, rows rebuilt on open and after every purchase.
    /// Self-creates on demand (the stall is runtime-spawned).
    /// </summary>
    public class MerchantShopUI : MonoBehaviour
    {
        public static MerchantShopUI Instance { get; private set; }

        private static readonly Color CoinGold = new Color(1f, 0.9f, 0.5f);

        /// <summary>One purchasable row: inventory id, shown label (price is
        /// appended), grey sub-line, obol price, granted quantity.</summary>
        private struct StockEntry
        {
            public string id;
            public string label;
            public string sub;
            public int price;
            public int quantity;

            public StockEntry(string id, string label, string sub, int price, int quantity = 1)
            {
                this.id = id;
                this.label = label;
                this.sub = sub;
                this.price = price;
                this.quantity = quantity;
            }
        }

        // ---- stock pools (data-driven; append to extend) --------------------
        // Seed packets a notch under the town vendor's 2/3/4/4.
        private static readonly StockEntry[] SeedPool =
        {
            new StockEntry("seed_grass", "Grass Seed", "road price - the vendor would weep", 1),
            new StockEntry("seed_palewheat", "Palewheat Seed", "fell off a cart, allegedly", 2),
            new StockEntry("seed_gravebloom", "Gravebloom Seed", "picked up cheap at a funeral", 3),
            new StockEntry("seed_murkberry", "Murkberry Seed", "don't ask which murk", 3),
        };

        // Treats a notch under the town vendor's 5.
        private static readonly StockEntry[] TreatPool =
        {
            new StockEntry("treat", "Follow Treat", "makes any spirit tag along for a while", 4),
            new StockEntry("treat", "Treat Bundle (3)", "three treats, one knowing wink", 11, 3),
        };

        // The rare row: flavor-only for now -- buying grants a "curio" item.
        private static readonly StockEntry CurioEntry =
            new StockEntry("curio", "A jar of authentic underworld mist", "contents: mist (authenticity implied)", 15);

        /// <summary>Chance the third row is the curio instead of more stock.</summary>
        private const float CurioChance = 0.35f;

        private GameObject _panel;
        private Text _coinsText;
        private RectTransform _content; // rows rebuilt on open + after trades
        private bool _open;

        /// <summary>Returns the live modal, creating it on the fly -- the stall
        /// is runtime-spawned, so its UI self-creates too (no scene setup).</summary>
        public static MerchantShopUI GetOrCreate()
        {
            if (Instance == null)
                new GameObject("MerchantShopUI (runtime)").AddComponent<MerchantShopUI>();
            return Instance;
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

        /// <summary>Opens the shop modal.</summary>
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
                if (!paused) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        /// <summary>Safety hatch for the manager: the stall packs up at dawn
        /// even if the player is mid-haggle.</summary>
        public void CloseIfOpen()
        {
            if (_open) Close();
        }

        // ---- today's stock --------------------------------------------------

        /// <summary>
        /// Deterministic 3-row stock for the absolute day: one seed deal, one
        /// treat deal, and a third slot that is the rare curio on some visits
        /// and a second (distinct) seed otherwise. Seeded by day -- NOT
        /// UnityEngine.Random -- so reopening or reloading never reshuffles.
        /// </summary>
        private static StockEntry[] TodaysStock()
        {
            int day = GameClock.Instance != null ? GameClock.Instance.Day : 1;
            var rng = new System.Random(day * 7919 + 31);

            int seedIndex = rng.Next(SeedPool.Length);
            var rows = new StockEntry[3];
            rows[0] = SeedPool[seedIndex];
            rows[1] = TreatPool[rng.Next(TreatPool.Length)];

            if (rng.NextDouble() < CurioChance)
            {
                rows[2] = CurioEntry;
            }
            else
            {
                // A second seed, never the same packet twice.
                int second = rng.Next(SeedPool.Length - 1);
                if (second >= seedIndex) second++;
                rows[2] = SeedPool[second];
            }

            return rows;
        }

        // ------------------------------------------------------- transactions

        private void Buy(StockEntry entry)
        {
            var inv = Inventory.Instance;
            if (inv == null) return;

            if (inv.Consume("coin", entry.price))
            {
                inv.Add(entry.id, entry.quantity);
                // "seed_grass" reads as "seed grass" in the pickup toast.
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "+" + entry.quantity + " " + entry.id.Replace('_', ' '), CoinGold);
                Bleeps.Play(BleepKind.Coin, 0.6f);
            }
            else
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs " + entry.price + " obols)", UIStyle.Danger);
                Bleeps.Play(BleepKind.Denied, 0.8f);
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

            _panel = new GameObject("MerchantShopUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(460f, 120f); // height grows via ContentSizeFitter

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
            title.text = "Traveling Merchant";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var subtitle = UIRoot.MakeText(panelRt, "Subtitle", 19, TextAnchor.MiddleCenter, UIStyle.Grey);
            subtitle.text = "Today only. Tomorrow I am somebody else's problem.";
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

            var stock = TodaysStock();
            for (int i = 0; i < stock.Length; i++)
            {
                var captured = stock[i]; // capture for the click closure
                MakeTwoLineButton(_content, captured.label + " - " + captured.price + " obols",
                    captured.sub, () => Buy(captured), enabled: true);
            }

            UIStyle.MakeDivider(_content);

            MakeButton(_content, "Leave", 56f, Close);
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

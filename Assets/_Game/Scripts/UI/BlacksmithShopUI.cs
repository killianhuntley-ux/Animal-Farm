using AnimalFarm.Core;
using AnimalFarm.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The Blacksmith's upgrade modal (muscle 01, verdict 1). Opened by the
    /// BlacksmithStall. One row per upgradable tool (Hoe, Water Pail, Shovel)
    /// showing current tier, the next tier's price + farmer-level requirement
    /// and a one-line flavor ("Hoe II - swings quicker"). Buying checks
    /// ShepherdProgress.Level and Inventory "coin" (reads as obols), then
    /// calls ToolController.SetToolTier. Maxed rows grey out "(mastered)";
    /// under-leveled rows grey out with "needs level N".
    ///
    /// Modal pattern copied from VendorUI: UIInputLock.ModalOpen +
    /// SetGameplayBlocked with the pause-respecting restore, UIStyle look,
    /// shell built once, rows rebuilt on open and after every purchase.
    /// Self-creates on demand (the stall is runtime-spawned, so no
    /// bootstrapper wires this up).
    /// </summary>
    public class BlacksmithShopUI : MonoBehaviour
    {
        public static BlacksmithShopUI Instance { get; private set; }

        private static readonly Color CoinGold = new Color(1f, 0.9f, 0.5f);

        // ---- price table (muscle 01 verdicts: level + obols combo) ----------
        // Index by NEXT tier: tier 2 = 40 obols + level 3; tier 3 = 100 + 6.
        private static readonly int[] TierPrices = { 0, 0, 40, 100 };
        private static readonly int[] TierLevels = { 0, 0, 3, 6 };

        /// <summary>Tools the forge improves, in row order.</summary>
        private static readonly string[] UpgradableTools =
            { ToolController.ToolHoe, ToolController.ToolWaterPail, ToolController.ToolShovel };

        /// <summary>Flavor per tool per NEXT tier (index 0 = tier 2, 1 = tier 3).</summary>
        private static readonly string[][] TierFlavor =
        {
            new[] { "swings quicker", "tills a whole row" },           // Hoe
            new[] { "pours faster", "soaks a whole row" },             // Water Pail
            new[] { "digs quicker", "digs quicker still" },            // Shovel
        };

        private GameObject _panel;
        private Text _coinsText;
        private RectTransform _content; // rows rebuilt on open + after purchases
        private ToolController _tools;  // found at open; the one shepherd
        private bool _open;

        /// <summary>Returns the live modal, creating it on the fly -- the stall
        /// is runtime-spawned, so its UI self-creates too (no scene setup).</summary>
        public static BlacksmithShopUI GetOrCreate()
        {
            if (Instance == null)
                new GameObject("BlacksmithShopUI (runtime)").AddComponent<BlacksmithShopUI>();
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

        /// <summary>Opens the upgrade modal.</summary>
        public void Open()
        {
            if (_open) return;
            if (_panel == null) BuildPanel();

            // Find the player's ToolController at open (muscle spec) -- the
            // shepherd is the only one, and rows degrade if it's missing.
            _tools = FindFirstObjectByType<ToolController>();

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

        /// <summary>Buys the next tier for a tool. Everything re-checked at
        /// click time -- the panel may be stale by a level or a purchase.</summary>
        private void BuyUpgrade(string toolName)
        {
            if (_tools == null) return;

            int tier = _tools.GetToolTier(toolName);
            int next = tier + 1;
            if (next > ToolController.MaxToolTier) { RebuildContent(); return; }

            int price = TierPrices[next];
            int level = TierLevels[next];

            var progress = ShepherdProgress.GetOrCreate();
            if (progress == null || progress.Level < level)
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs level " + level + ")", UIStyle.Danger);
                Bleeps.Play(BleepKind.Denied, 0.8f);
                RebuildContent();
                return;
            }

            var inv = Inventory.Instance;
            if (inv == null || !inv.Consume("coin", price))
            {
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(needs " + price + " obols)", UIStyle.Danger);
                Bleeps.Play(BleepKind.Denied, 0.8f);
                RebuildContent();
                return;
            }

            if (!_tools.SetToolTier(toolName, next))
            {
                // Owner law: never charge for nothing. Refund and say why.
                inv.Add("coin", price);
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                    "(the forge refuses)", UIStyle.Danger);
                RebuildContent();
                return;
            }

            FloatingText.Show(PlayerPos() + Vector3.up * 0.8f,
                toolName + " " + Numeral(next) + "!", CoinGold);
            Bleeps.Play(BleepKind.Coin, 0.7f);

            RebuildContent();
        }

        private static Vector3 PlayerPos()
        {
            var player = GameObject.FindWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }

        private static string Numeral(int tier) =>
            tier >= 3 ? "III" : tier == 2 ? "II" : "I";

        // ------------------------------------------------------------------ UI

        private void BuildPanel()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("BlacksmithShopUI");
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
            title.text = "The Blacksmith";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var subtitle = UIRoot.MakeText(panelRt, "Subtitle", 19, TextAnchor.MiddleCenter, UIStyle.Grey);
            subtitle.text = "The forge remembers every obol. So do I.";
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

            if (_tools == null)
            {
                var empty = UIRoot.MakeText(_content, "Empty", 20, TextAnchor.MiddleCenter, UIStyle.Grey);
                empty.text = "(the forge-spirit squints... no shepherd to outfit)";
                empty.rectTransform.sizeDelta = new Vector2(0f, 32f);
            }
            else
            {
                int shepherdLevel = ShepherdProgress.Instance != null
                    ? ShepherdProgress.Instance.Level : 1;

                for (int i = 0; i < UpgradableTools.Length; i++)
                    MakeToolRow(UpgradableTools[i], TierFlavor[i], shepherdLevel);
            }

            UIStyle.MakeDivider(_content);

            MakeButton(_content, "Leave", 56f, Close);
        }

        /// <summary>One upgrade row: enabled buy, grey "needs level N",
        /// grey "(mastered)", or grey "(not yet acquired)".</summary>
        private void MakeToolRow(string toolName, string[] flavor, int shepherdLevel)
        {
            // A tool you don't own yet can't be improved -- the forge waits.
            if (!_tools.IsToolUnlocked(toolName))
            {
                MakeTwoLineButton(_content, toolName + " - (not yet yours)",
                    "come back once you carry one", null, enabled: false);
                return;
            }

            int tier = _tools.GetToolTier(toolName);
            if (tier >= ToolController.MaxToolTier)
            {
                MakeTwoLineButton(_content, toolName + " " + Numeral(tier) + " (mastered)",
                    "it cannot be improved, only admired", null, enabled: false);
                return;
            }

            int next = tier + 1;
            int price = TierPrices[next];
            int level = TierLevels[next];
            string label = toolName + " " + Numeral(next) + " - " + price + " obols";
            string sub = toolName + " " + Numeral(next) + " - " + flavor[next - 2];

            if (shepherdLevel < level)
            {
                MakeTwoLineButton(_content, label, "needs level " + level, null, enabled: false);
                return;
            }

            string captured = toolName; // capture for the click closure
            MakeTwoLineButton(_content, label, sub, () => BuyUpgrade(captured), enabled: true);
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

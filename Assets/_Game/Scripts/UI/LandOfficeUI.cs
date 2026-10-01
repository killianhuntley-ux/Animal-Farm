using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The Ferryman's LAND OFFICE modal: a Cities-Skylines-style parcel
    /// overview. One row-card per ParcelManager parcel -- name, blurb, size,
    /// and either an OWNED stamp or a "Buy - N obols" button (coins are
    /// Inventory item "coin"; "obols" is the fiction word). Opened by the
    /// FerrymanStall or by any parcel gate sign's "About this land".
    /// VendorUI pattern: gameplay input blocked while open, shell built once,
    /// content rebuilt on every open and after every transaction.
    /// TODO: muscle phase: mini-map preview + terrain stats per parcel.
    /// </summary>
    public class LandOfficeUI : MonoBehaviour
    {
        public static LandOfficeUI Instance { get; private set; }

        private GameObject _panel;
        private Text _obolsText;
        private RectTransform _content; // rows rebuilt on open + after purchases
        private ScrollRect _scroll;     // 14 deeds no longer fit a flat column
        private Text _statusText;       // transient Danger line, lives OUTSIDE the scroll
        private bool _open;
        private string _status; // transient Danger line under the list, e.g. "(needs 120 obols)"

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the parcel overview modal.</summary>
        public void Open()
        {
            if (_open) return;
            if (_panel == null) BuildPanel();

            _status = null;
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

        // ------------------------------------------------------- transactions

        private void BuyParcel(int index)
        {
            var manager = ParcelManager.Instance;
            if (manager == null) return;

            var info = manager.GetInfo(index);
            if (manager.TryPurchase(index))
            {
                _status = null;
                FloatingText.Show(PlayerPos() + Vector3.up * 0.8f, "deed stamped", UIStyle.Gold);
            }
            else
            {
                _status = "(needs " + info.coinCost + " obols)";
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

            _panel = new GameObject("LandOfficeUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(560f, 120f); // height grows via ContentSizeFitter

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
            title.text = "The Ferryman";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var subtitle = UIRoot.MakeText(panelRt, "Subtitle", 19, TextAnchor.MiddleCenter, UIStyle.Grey);
            subtitle.text = "Passage costs. It always has.";
            subtitle.rectTransform.sizeDelta = new Vector2(0f, 28f);

            _obolsText = UIRoot.MakeText(panelRt, "Obols", 24, TextAnchor.MiddleCenter, UIStyle.Gold);
            _obolsText.rectTransform.sizeDelta = new Vector2(0f, 32f);

            // The deed list scrolls: 14 rows would blow past any screen, so a
            // fixed-height viewport holds the rebuilt column (mouse wheel +
            // drag; the shell, status line and Leave button stay put).
            var scrollGo = new GameObject("DeedScroll");
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.SetParent(panelRt, false);
            scrollRt.sizeDelta = new Vector2(0f, 440f); // fixed viewport height
            var scrollLayout = scrollGo.AddComponent<LayoutElement>();
            scrollLayout.preferredHeight = 440f;
            scrollLayout.flexibleHeight = 0f;

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            var viewport = new GameObject("Viewport").AddComponent<RectTransform>();
            viewport.SetParent(scrollRt, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            // An (invisible) raycastable image so drags over empty row gaps scroll too.
            var viewportImg = viewport.gameObject.AddComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.001f);

            // Rows live in a nested column so the shell survives rebuilds.
            _content = new GameObject("Content").AddComponent<RectTransform>();
            _content.SetParent(viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(0f, 0f);
            _content.offsetMax = new Vector2(0f, 0f);

            var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentFitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewport;
            _scroll.content = _content;

            // Shell furniture under the scroll: status line, divider, Leave.
            _statusText = UIRoot.MakeText(panelRt, "Status", 20, TextAnchor.MiddleCenter, UIStyle.Danger);
            _statusText.text = "";
            _statusText.rectTransform.sizeDelta = new Vector2(0f, 28f);

            UIStyle.MakeDivider(panelRt);

            var leave = UIStyle.MakeButton(panelRt, "Leave", Close, 24);
            ((RectTransform)leave.transform).sizeDelta = new Vector2(0f, 56f);

            _panel.SetActive(false);
        }

        private void RebuildContent()
        {
            if (_content == null) return;

            if (_obolsText != null && Inventory.Instance != null)
                _obolsText.text = "Obols: " + Inventory.Instance.Count("coin");

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            // ---- parcel overview: one row-card per parcel ----
            var manager = ParcelManager.Instance;
            if (manager != null && manager.ParcelCount > 0)
            {
                for (int i = 0; i < manager.ParcelCount; i++)
                    MakeParcelCard(manager.GetInfo(i));
            }
            else
            {
                var empty = UIRoot.MakeText(_content, "Empty", 20, TextAnchor.MiddleCenter, UIStyle.Grey);
                empty.text = "(no land on offer)";
                empty.rectTransform.sizeDelta = new Vector2(0f, 32f);
            }

            // Transient status line (purchase failures) lives in the shell,
            // under the scroll view -- always visible without scrolling.
            if (_statusText != null) _statusText.text = _status ?? "";
        }

        /// <summary>One row-card: left = name + blurb + size, right = OWNED
        /// stamp or buy button.</summary>
        private void MakeParcelCard(ParcelManager.ParcelInfo info)
        {
            if (info.index < 0) return;

            var card = UIStyle.MakePanel(_content, "Parcel_" + info.index, UIStyle.PanelBgLight);
            card.sizeDelta = new Vector2(0f, 100f);

            // -- left column (name / blurb / size)
            var name = UIRoot.MakeText(card, "Name", 24, TextAnchor.MiddleLeft, UIStyle.Cream);
            name.text = info.name;
            SetAnchors(name.rectTransform, new Vector2(0f, 0.62f), new Vector2(0.62f, 1f),
                new Vector2(16f, 0f), new Vector2(0f, -6f));

            var blurb = UIRoot.MakeText(card, "Blurb", 18, TextAnchor.MiddleLeft, UIStyle.Grey);
            blurb.text = info.blurb;
            SetAnchors(blurb.rectTransform, new Vector2(0f, 0.31f), new Vector2(0.62f, 0.62f),
                new Vector2(16f, 0f), Vector2.zero);

            var size = UIRoot.MakeText(card, "Size", 18, TextAnchor.MiddleLeft, UIStyle.Grey);
            size.text = "Size: " + Mathf.RoundToInt(info.size.x) + " x " + Mathf.RoundToInt(info.size.y);
            SetAnchors(size.rectTransform, new Vector2(0f, 0f), new Vector2(0.62f, 0.31f),
                new Vector2(16f, 6f), Vector2.zero);

            // -- right column (status)
            if (info.unlocked)
            {
                var owned = UIRoot.MakeText(card, "Owned", 24, TextAnchor.MiddleCenter, UIStyle.Gold);
                owned.text = "OWNED";
                SetAnchors(owned.rectTransform, new Vector2(0.62f, 0f), new Vector2(1f, 1f),
                    Vector2.zero, new Vector2(-12f, 0f));
            }
            else
            {
                int captured = info.index; // capture for the click closure
                var buy = UIStyle.MakeButton(card, "Buy - " + info.coinCost + " obols",
                    () => BuyParcel(captured), 19);
                SetAnchors((RectTransform)buy.transform, new Vector2(0.64f, 0.26f),
                    new Vector2(1f, 0.74f), Vector2.zero, new Vector2(-14f, 0f));
            }
        }

        private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rt == null) return;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}

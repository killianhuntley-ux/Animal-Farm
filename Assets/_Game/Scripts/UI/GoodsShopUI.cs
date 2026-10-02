using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Generic buy-only shop modal for small themed vendors (muscle 08/09): the
    /// swamp's Mire Peddler today, future biome-exclusive vendors tomorrow.
    /// Give it a title, a subtitle line and a stock list of
    /// <see cref="RoadGoods.Good"/> rows; purchases route through
    /// RoadGoods.Buy (obols, durable-owned check, toasts). Modal pattern copied
    /// from MerchantShopUI / VendorUI (UIInputLock.ModalOpen + SetGameplayBlocked
    /// with the pause-respecting restore, UIStyle look, shell built once, rows
    /// rebuilt on open and after every purchase). Self-creates on demand.
    /// </summary>
    public class GoodsShopUI : MonoBehaviour
    {
        public static GoodsShopUI Instance { get; private set; }

        private GameObject _panel;
        private Text _title, _subtitle, _coinsText;
        private RectTransform _content;
        private bool _open;
        private IList<RoadGoods.Good> _stock;

        public bool IsOpen => _open;

        public static GoodsShopUI GetOrCreate()
        {
            if (Instance == null)
                new GameObject("GoodsShopUI (runtime)").AddComponent<GoodsShopUI>();
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

        /// <summary>Opens the modal for a stock list.</summary>
        public void Open(string title, string subtitle, IList<RoadGoods.Good> stock)
        {
            if (_open) return;
            if (_panel == null) BuildPanel();

            _stock = stock;
            _title.text = title;
            _subtitle.text = subtitle;
            RebuildContent();

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
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
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        public void CloseIfOpen()
        {
            if (_open) Close();
        }

        private void BuyRow(RoadGoods.Good good)
        {
            RoadGoods.Buy(good);
            RebuildContent();
        }

        // ------------------------------------------------------------------ UI

        private void BuildPanel()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("GoodsShopUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(480f, 120f); // height grows via ContentSizeFitter

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

            _title = UIRoot.MakeText(panelRt, "Title", 34, TextAnchor.MiddleCenter, UIStyle.Cream);
            _title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            _subtitle = UIRoot.MakeText(panelRt, "Subtitle", 19, TextAnchor.MiddleCenter, UIStyle.Grey);
            _subtitle.rectTransform.sizeDelta = new Vector2(0f, 28f);

            _coinsText = UIRoot.MakeText(panelRt, "Coins", 24, TextAnchor.MiddleCenter, UIStyle.Gold);
            _coinsText.rectTransform.sizeDelta = new Vector2(0f, 32f);

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

            if (_stock != null)
            {
                for (int i = 0; i < _stock.Count; i++)
                {
                    var good = _stock[i]; // capture for the click closure
                    string label = good.label + " - " + good.price + " obols";
                    MakeTwoLineButton(_content, label, good.sub, () => BuyRow(good), enabled: true);
                }
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
            bg.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);
            button.interactable = interactable;

            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }
    }
}

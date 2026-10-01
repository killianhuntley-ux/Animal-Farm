using System.Text;
using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Debug-grade always-visible inventory list, top-left. Updates on
    /// Inventory.OnChanged; hidden entirely when the inventory is empty.
    /// </summary>
    public class InventoryHUD : MonoBehaviour
    {
        private Text _text;
        private GameObject _panel;
        private bool _dirty = true;
        private bool _subscribed;

        private void Start()
        {
            var root = UIRoot.GetRoot();

            // Small rounded backing panel; hidden together with the text when empty.
            var panelBg = UIStyle.PanelBg;
            panelBg.a = 0.7f;
            var panelRt = UIStyle.MakePanel(root, "InventoryHUD", panelBg);
            _panel = panelRt.gameObject;
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(24f, -20f);
            panelRt.sizeDelta = new Vector2(240f, 100f); // height follows content
            panelRt.GetComponent<Image>().raycastTarget = false;

            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 10);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _text = UIRoot.MakeText(panelRt, "Text", 22, TextAnchor.UpperLeft, UIStyle.Cream);

            var shadow = _text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            _panel.SetActive(false);
        }

        private void Update()
        {
            if (!_subscribed && Inventory.Instance != null)
            {
                Inventory.Instance.OnChanged += OnInventoryChanged;
                _subscribed = true;
            }

            if (!_dirty || _text == null || Inventory.Instance == null) return;
            _dirty = false;

            var items = Inventory.Instance.All;
            if (items.Count == 0)
            {
                _text.text = "";
                if (_panel != null) _panel.SetActive(false);
                return;
            }

            // Coins pinned first, on their own line (slice 09 economy).
            var sb = new StringBuilder();
            int coins = Inventory.Instance.Count("coin");
            if (coins > 0) sb.Append("Obols: ").Append(coins).Append('\n'); // the ferryman's toll

            bool hasPouchItems = items.Count > (coins > 0 ? 1 : 0);
            if (hasPouchItems)
            {
                sb.Append("Pouch:\n");
                foreach (var kv in items)
                {
                    if (kv.Key == "coin") continue; // rendered above
                    sb.Append("  ").Append(kv.Key).Append(" x").Append(kv.Value).Append('\n');
                }
            }
            _text.text = sb.ToString();
            if (_panel != null) _panel.SetActive(true);
        }

        private void OnInventoryChanged(string id, int count) => _dirty = true;

        private void OnDestroy()
        {
            if (_subscribed && Inventory.Instance != null)
                Inventory.Instance.OnChanged -= OnInventoryChanged;
        }
    }
}

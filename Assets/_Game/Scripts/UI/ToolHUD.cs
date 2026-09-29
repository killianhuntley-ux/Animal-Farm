using AnimalFarm.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Bottom-left HUD showing the currently selected tool, e.g.
    ///   Tool: Sow Grass   [Tab / Y]
    /// Built entirely in code in Start; updates via ToolController.OnToolChanged,
    /// with a poll-fallback in Update if the controller wasn't up yet.
    /// </summary>
    public class ToolHUD : MonoBehaviour
    {
        private Text _text;
        private ToolController _tools;

        private void Start()
        {
            var root = UIRoot.GetRoot();

            var holder = new GameObject("ToolHUD").AddComponent<RectTransform>();
            holder.SetParent(root, false);
            holder.anchorMin = new Vector2(0f, 0f);
            holder.anchorMax = new Vector2(0f, 0f);
            holder.pivot = new Vector2(0f, 0f);
            holder.anchoredPosition = new Vector2(24f, 20f);
            holder.sizeDelta = new Vector2(640f, 34f);

            var nearWhite = new Color(0.95f, 0.95f, 0.92f, 1f);

            _text = UIRoot.MakeText(holder, "ToolText", 26, TextAnchor.LowerLeft, nearWhite);
            var rt = _text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;

            var shadow = _text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            TryBindController();
        }

        private void Update()
        {
            // Poll-fallback: the controller may spawn after this HUD.
            if (_tools == null)
                TryBindController();
        }

        private void OnDestroy()
        {
            if (_tools != null)
                _tools.OnToolChanged -= HandleToolChanged;
        }

        private void TryBindController()
        {
            _tools = FindFirstObjectByType<ToolController>();
            if (_tools == null) return;

            _tools.OnToolChanged += HandleToolChanged;
            HandleToolChanged(_tools.CurrentToolName);
        }

        private void HandleToolChanged(string toolName)
        {
            if (_text != null)
                _text.text = "Tool: " + toolName + "   [Tab / Y]";
        }
    }
}

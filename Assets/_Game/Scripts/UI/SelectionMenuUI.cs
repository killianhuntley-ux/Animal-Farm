using System.Collections.Generic;
using AnimalFarm.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Lightweight context menu opened by SelectionController when the player
    /// clicks an ISelectable. One reusable popup panel on the shared UI canvas:
    /// title plus one button per SelectAction. Does NOT block gameplay input;
    /// clicking away closes it (SelectionController handles that).
    /// </summary>
    public class SelectionMenuUI : MonoBehaviour
    {
        public static SelectionMenuUI Instance { get; private set; }

        private const float PanelWidth = 260f;
        private const float Pad = 12f;
        private const float TitleHeight = 32f;
        private const float ButtonHeight = 40f;
        private const float ButtonSpacing = 6f;
        private const int ButtonFontSize = 22;

        private RectTransform _panel;
        private Text _title;

        private ISelectable _target;
        private Vector2 _lastScreenPos;
        private readonly List<SelectAction> _actions = new List<SelectAction>();
        private readonly List<GameObject> _buttons = new List<GameObject>();

        /// <summary>True while the popup is showing.</summary>
        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- public API ----------------------------------------------------

        /// <summary>Opens (or re-targets) the menu near a screen position.</summary>
        public void Open(ISelectable target, Vector2 screenPos)
        {
            if (target == null) return;

            BuildOnce();
            if (_panel == null) return;

            _target = target;
            _lastScreenPos = screenPos;

            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling(); // keep above the rest of the HUD
            RebuildActions();
        }

        public void Close()
        {
            _target = null;
            _actions.Clear();
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        // ---- build -----------------------------------------------------------

        private void BuildOnce()
        {
            if (_panel != null) return;

            var root = UIRoot.GetRoot();
            if (root == null) return;

            _panel = UIStyle.MakePanel(root, "SelectionMenu");
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0f, 1f); // top-left; opens down-right of the click
            _panel.sizeDelta = new Vector2(PanelWidth, 100f);

            _title = UIRoot.MakeText(_panel, "Title", 24, TextAnchor.MiddleLeft, UIStyle.Cream);
            _title.fontStyle = FontStyle.Bold;
            var trt = _title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(Pad, 0f);
            trt.offsetMax = new Vector2(-Pad, 0f);
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, TitleHeight);
            trt.anchoredPosition = new Vector2(0f, -Pad);

            _panel.gameObject.SetActive(false);
        }

        /// <summary>Re-queries the target's actions and rebuilds the buttons.</summary>
        private void RebuildActions()
        {
            if (_panel == null) return;

            // A destroyed MonoBehaviour target reads as null via Unity's operator.
            if (_target == null || (_target is MonoBehaviour mb && mb == null))
            {
                Close();
                return;
            }

            for (int i = 0; i < _buttons.Count; i++)
                if (_buttons[i] != null) Destroy(_buttons[i]);
            _buttons.Clear();

            _title.text = _target.SelectableTitle;

            _actions.Clear();
            _target.GetSelectActions(_actions);

            float y = -(Pad + TitleHeight + ButtonSpacing);
            for (int i = 0; i < _actions.Count; i++)
            {
                int index = i; // capture per-button
                var button = UIStyle.MakeButton(_panel, _actions[i].label,
                    () => OnActionClicked(index), ButtonFontSize);

                var rt = (RectTransform)button.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(Pad, 0f);
                rt.offsetMax = new Vector2(-Pad, 0f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, ButtonHeight);
                rt.anchoredPosition = new Vector2(0f, y);
                y -= ButtonHeight + ButtonSpacing;

                _buttons.Add(button.gameObject);
            }

            float height = Pad + TitleHeight + ButtonSpacing
                + _actions.Count * (ButtonHeight + ButtonSpacing) + Pad - ButtonSpacing;
            _panel.sizeDelta = new Vector2(PanelWidth, Mathf.Max(height, TitleHeight + Pad * 2f));

            PositionAt(_lastScreenPos);
        }

        private void OnActionClicked(int index)
        {
            if (index < 0 || index >= _actions.Count) return;

            var act = _actions[index];
            if (act.action != null) act.action();

            if (act.closeOnRun)
            {
                Close();
            }
            else if (IsOpen)
            {
                // Two-step confirms: re-query so relabeled actions show up.
                RebuildActions();
            }
        }

        // ---- positioning -----------------------------------------------------

        /// <summary>Places the panel near a screen point, clamped on-screen.</summary>
        private void PositionAt(Vector2 screenPos)
        {
            var root = UIRoot.GetRoot();
            if (root == null || _panel == null) return;

            // Overlay canvas: camera argument is null.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, null, out Vector2 local);

            Rect canvasRect = root.rect;
            Vector2 size = _panel.sizeDelta;

            // Pivot is top-left: panel spans [x, x+w] by [y-h, y].
            float x = Mathf.Clamp(local.x, canvasRect.xMin, canvasRect.xMax - size.x);
            float yPos = Mathf.Clamp(local.y, canvasRect.yMin + size.y, canvasRect.yMax);

            _panel.anchoredPosition = new Vector2(x, yPos);
        }
    }
}

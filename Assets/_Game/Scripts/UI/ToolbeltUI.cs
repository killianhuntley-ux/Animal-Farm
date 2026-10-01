using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Toolbelt picker: the Toolbelt key (T) toggles a bottom-center bar with
    /// one button per tool. Clicking a button or pressing 1-9 selects that tool
    /// and closes; pressing T again closes without changing the selection.
    /// Gameplay input is blocked while open (DebugConsole pattern) — the
    /// Toolbelt action itself stays live, which is how T can still close it.
    /// Built entirely in code; buttons are rebuilt on every open.
    /// </summary>
    public class ToolbeltUI : MonoBehaviour
    {
        private const float FollowPixelsUp = 90f;   // bar floats this far above the shepherd
        private const float GrowSeconds = 0.15f;    // 0.4 -> 1.06
        private const float SettleSeconds = 0.07f;  // 1.06 -> 1.0 (total ~0.22s)
        private const float StartScale = 0.4f;
        private const float OvershootScale = 1.06f;

        private GameObject _panel;
        private RectTransform _panelRt;
        private readonly List<Button> _buttons = new List<Button>();

        private ToolController _tools;
        private Transform _shepherd;
        private float _animTime;
        private bool _open;
        private bool _inputSubscribed;

        private void Start()
        {
            BuildPanel();

            if (GameInput.Instance != null)
            {
                GameInput.Instance.ToolbeltPressed += OnToolbeltPressed;
                _inputSubscribed = true;
            }

            TryBindController();
        }

        private void Update()
        {
            // Poll-fallback: the controller may spawn after this UI.
            if (_tools == null)
                TryBindController();

            if (_open)
            {
                PollNumberKeys();
                FollowShepherd();
                TickOpenAnimation();
            }
        }

        private void OnDestroy()
        {
            if (_inputSubscribed && GameInput.Instance != null)
                GameInput.Instance.ToolbeltPressed -= OnToolbeltPressed;

            if (_tools != null)
                _tools.OnToolChanged -= HandleToolChanged;
        }

        private void TryBindController()
        {
            _tools = FindFirstObjectByType<ToolController>();
            if (_tools == null) return;

            _tools.OnToolChanged += HandleToolChanged;
        }

        private void HandleToolChanged(string toolName)
        {
            if (_open) RefreshTints();

            // Never linger over other systems: close if a text field grabbed input
            // or a competition kicked off underneath us.
            if (_open && (AnimalFarm.Core.UIInputLock.TextInputActive
                || (AnimalFarm.Competitions.CompetitionManager.Instance != null
                    && AnimalFarm.Competitions.CompetitionManager.Instance.EventRunning)))
                Close();
        }

        // ---- open / close -----------------------------------------------------

        private void OnToolbeltPressed()
        {
            if (AnimalFarm.Core.UIInputLock.TextInputActive) return; // typing in console
            if (_open) Close();
            else Open();
        }

        private void Open()
        {
            if (_open || _tools == null || _panel == null) return;

            RebuildButtons();
            _panel.SetActive(true);
            _open = true;

            // Grow-and-settle entrance, anchored over the shepherd.
            _animTime = 0f;
            _panelRt.localScale = new Vector3(StartScale, StartScale, 1f);
            FollowShepherd();

            AnimalFarm.Core.UIInputLock.ModalOpen = true;

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            AnimalFarm.Core.UIInputLock.ModalOpen = false;
            if (_panel != null) _panel.SetActive(false);

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        private void SelectAndClose(int index)
        {
            if (_tools != null) _tools.SelectTool(index);
            Close();
        }

        private void PollNumberKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || AnimalFarm.Core.UIInputLock.TextInputActive) return;

            for (int i = 0; i < _buttons.Count && i < 9; i++)
            {
                // Key.Digit1..Digit9 are contiguous, so index off Digit1.
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    SelectAndClose(i);
                    return;
                }
            }
        }

        // ---- over-the-shepherd placement + entrance ---------------------------

        /// <summary>Keeps the bar hovering ~90 px above the shepherd while open.
        /// Overlay canvas, so the camera param to the rect conversion is null.</summary>
        private void FollowShepherd()
        {
            if (_panelRt == null) return;

            if (_shepherd == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) _shepherd = player.transform;
            }

            var cam = Camera.main;
            if (_shepherd == null || cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(_shepherd.position);
            screen.y += FollowPixelsUp;

            var root = UIRoot.GetRoot();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    root, screen, null, out Vector2 local))
                _panelRt.anchoredPosition = local;
        }

        /// <summary>Two-phase unscaled-time pop: 0.4 -> 1.06 -> 1.0 over ~0.22s.</summary>
        private void TickOpenAnimation()
        {
            if (_panelRt == null) return;

            _animTime += Time.unscaledDeltaTime;
            float s;
            if (_animTime < GrowSeconds)
                s = Mathf.Lerp(StartScale, OvershootScale, _animTime / GrowSeconds);
            else if (_animTime < GrowSeconds + SettleSeconds)
                s = Mathf.Lerp(OvershootScale, 1f, (_animTime - GrowSeconds) / SettleSeconds);
            else
                s = 1f;

            _panelRt.localScale = new Vector3(s, s, 1f);
        }

        // ------------------------------------------------------------------ UI

        private void BuildPanel()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("ToolbeltUI");
            _panelRt = _panel.AddComponent<RectTransform>();
            _panelRt.SetParent(root, false);
            // Center anchors so anchoredPosition can track the shepherd's
            // screen position; pivot at bottom-center so the bar grows upward
            // from just above the shepherd's head.
            _panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRt.pivot = new Vector2(0.5f, 0f);
            _panelRt.anchoredPosition = Vector2.zero;    // repositioned on open
            _panelRt.sizeDelta = new Vector2(200f, 76f); // grows via ContentSizeFitter

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true;

            var layout = _panel.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _panel.SetActive(false);
        }

        private void RebuildButtons()
        {
            if (_panelRt == null || _tools == null) return;

            for (int i = _panelRt.childCount - 1; i >= 0; i--)
                Destroy(_panelRt.GetChild(i).gameObject);
            _buttons.Clear();

            var names = _tools.ToolNames;
            for (int i = 0; i < names.Count; i++)
            {
                int index = i; // capture for the click closure
                var button = MakeToolButton(_panelRt, (i + 1) + ". " + names[i],
                    () => SelectAndClose(index));
                _buttons.Add(button);
            }

            RefreshTints();
        }

        private void RefreshTints()
        {
            if (_tools == null) return;

            int current = _tools.CurrentToolIndex;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i];
                if (button == null) continue;

                // Current tool gets the gold base; the rest keep the standard grey.
                UIStyle.StyleButton(button, i == current ? UIStyle.Gold : (Color?)null);
            }
        }

        private static Button MakeToolButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(190f, 56f);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", 22, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;
            Stretch(text.rectTransform);

            return button;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}

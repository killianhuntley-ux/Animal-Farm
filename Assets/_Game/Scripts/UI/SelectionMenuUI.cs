using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Context menu for an ISelectable. Opened by SelectionController on a mouse
    /// click, or by InteractionSensor (via InteractMenu) when the player presses
    /// Interact on the focused target. One reusable popup panel on the shared UI
    /// canvas: title plus one button per SelectAction.
    ///
    /// Modal (like the toolbelt): while open it holds UIInputLock.ModalOpen and
    /// blocks gameplay input, so it reads the keyboard / gamepad DIRECTLY:
    ///   Up / Down (W S arrows, d-pad, left stick) move the highlighted row;
    ///   Interact (E, rebinds honoured), Enter or gamepad South confirm;
    ///   Esc / gamepad Start (via PausePressed, the pause toggle is undone),
    ///   Backspace, gamepad East or right click close. Mouse hover + click work.
    /// Rows whose label is wrapped in parentheses are info rows: shown dimmed,
    /// skipped by navigation. The press that OPENS the menu is spent: input is
    /// ignored on the opening frame (SelectionController.ConsumedClickFrame /
    /// PoutyMount.DismountFrame pattern).
    /// </summary>
    public class SelectionMenuUI : MonoBehaviour
    {
        public static SelectionMenuUI Instance { get; private set; }

        /// <summary>Frame the menu last closed (a confirm press can re-enable Interact the same frame).</summary>
        public static int ClosedFrame = -1;

        private const float PanelWidth = 260f;
        private const float Pad = 12f;
        private const float TitleHeight = 32f;
        private const float ButtonHeight = 40f;
        private const float ButtonSpacing = 6f;
        private const int ButtonFontSize = 22;
        private const float StickThreshold = 0.6f;

        private static readonly Color HighlightText = new Color(0.1f, 0.1f, 0.12f, 1f);

        private RectTransform _panel;
        private Text _title;

        private ISelectable _target;
        private Component _anchor;           // world anchor (keyboard open); null = fixed screen point
        private Vector2 _lastScreenPos;
        private readonly List<SelectAction> _actions = new List<SelectAction>();
        private readonly List<Button> _buttons = new List<Button>();

        private int _index = -1;             // highlighted row (never an info row)
        private int _openedFrame = -1;
        private int _stickDir;
        private bool _holdingLock;           // we set ModalOpen + blocked gameplay
        private bool _pauseHooked;
        private int _pausePressFrame = -1;   // frame a pause press arrived while open (handled in Update)

        /// <summary>True while the popup is showing.</summary>
        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            TryHookPause();
        }

        private void OnDestroy()
        {
            if (_pauseHooked && GameInput.Instance != null)
                GameInput.Instance.PausePressed -= OnPausePressed;
            _pauseHooked = false;

            ReleaseLock(); // never leave global locks dangling
            if (Instance == this) Instance = null;
        }

        /// <summary>The shared instance, created on demand.</summary>
        public static SelectionMenuUI GetOrCreate()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SelectionMenuUI");
            return go.AddComponent<SelectionMenuUI>();
        }

        // ---- public API ----------------------------------------------------

        /// <summary>Opens (or re-targets) the menu near a screen position (mouse click).</summary>
        public void Open(ISelectable target, Vector2 screenPos)
        {
            OpenInternal(target, null, screenPos);
        }

        /// <summary>Opens (or re-targets) the menu anchored beside a world object (Interact key).</summary>
        public void Open(ISelectable target, Component anchor)
        {
            Vector2 pos = anchor != null
                ? AnchorScreenPos(anchor)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            OpenInternal(target, anchor, pos);
        }

        public void Close()
        {
            _target = null;
            _anchor = null;
            _actions.Clear();
            _index = -1;
            if (_panel != null) _panel.gameObject.SetActive(false);
            ReleaseLock();
        }

        // ---- modal ownership ---------------------------------------------------

        private void OpenInternal(ISelectable target, Component anchor, Vector2 screenPos)
        {
            if (target == null) return;

            if (!IsOpen)
            {
                // Never stack on another modal (or the pause menu).
                if (UIInputLock.BlockDirectKeys) return;
                if (GameManager.Instance != null && GameManager.Instance.IsPaused) return;
            }

            BuildOnce();
            if (_panel == null) return;

            _target = target;
            _anchor = anchor;
            _lastScreenPos = screenPos;
            _openedFrame = Time.frameCount;
            _index = -1;

            TryHookPause();
            AcquireLock();

            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling(); // keep above the rest of the HUD
            RebuildActions();
        }

        private void AcquireLock()
        {
            if (_holdingLock) return;
            _holdingLock = true;
            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        /// <summary>Gives input back, unless the pause menu, a ceremony or a text field still needs it blocked.</summary>
        private void ReleaseLock()
        {
            if (!_holdingLock) return;
            _holdingLock = false;
            ClosedFrame = Time.frameCount;

            // A running ceremony owns the modal flag and the input block.
            if (!UIInputLock.CeremonyActive) UIInputLock.ModalOpen = false;

            if (GameInput.Instance != null)
            {
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.AnyOwnerHolds) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        private void TryHookPause()
        {
            if (_pauseHooked || GameInput.Instance == null) return;
            GameInput.Instance.PausePressed += OnPausePressed;
            _pauseHooked = true;
        }

        /// <summary>
        /// Esc / gamepad Start closes the menu. Subscription order against
        /// GameManager's TogglePause is not guaranteed, so this only records the
        /// frame; Update (always after the input events) undoes a pause that the
        /// toggle applied this frame, then closes. A press while the menu is
        /// closed is ignored here and keeps its normal pause meaning.
        /// </summary>
        private void OnPausePressed()
        {
            if (!IsOpen || UIInputLock.TextInputActive) return;
            _pausePressFrame = Time.frameCount;
        }

        // ---- input -------------------------------------------------------------

        private void Update()
        {
            if (!_pauseHooked) TryHookPause();
            if (!IsOpen) return;

            // Esc / Start pressed this frame: cancel the menu, not the game.
            if (_pausePressFrame == Time.frameCount)
            {
                _pausePressFrame = -1;
                if (GameManager.Instance != null && GameManager.Instance.IsPaused)
                    GameManager.Instance.SetPaused(false); // the toggle just paused it
                Close();
                return;
            }

            // A ceremony (naming, Styx, weave) owns the screen: the menu steps aside.
            if (UIInputLock.CeremonyActive)
            {
                Close();
                return;
            }

            // Target destroyed under us (spirit despawned, weed pulled): close cleanly.
            if (_target == null || (_target is MonoBehaviour mb && mb == null))
            {
                Close();
                return;
            }

            // A competition starting underneath us ends the menu (no menus mid-event).
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning)
            {
                Close();
                return;
            }

            FollowAnchor();

            // Buttons must never hold the EventSystem selection: Enter / South would
            // "submit" it on top of our own confirm and run the action twice.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null
                && es.currentSelectedGameObject.transform.IsChildOf(_panel))
                es.SetSelectedGameObject(null);

            if (UIInputLock.TextInputActive) return; // a text field owns the keys

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            int stick = 0;
            if (pad != null)
            {
                float y = pad.leftStick.y.ReadValue();
                stick = y > StickThreshold ? -1 : (y < -StickThreshold ? 1 : 0); // up = previous row
            }

            // The press that opened the menu is spent.
            if (Time.frameCount <= _openedFrame)
            {
                _stickDir = stick;
                return;
            }

            // ---- close ----
            bool cancel = (kb != null && kb.backspaceKey.wasPressedThisFrame)
                || (pad != null && pad.buttonEast.wasPressedThisFrame) // Rest is disabled while we are modal
                || (mouse != null && mouse.rightButton.wasPressedThisFrame);
            if (cancel)
            {
                Close();
                return;
            }

            // ---- move ----
            int step = 0;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) step--;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) step++;
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) step--;
                if (pad.dpad.down.wasPressedThisFrame) step++;
                if (stick != 0 && stick != _stickDir) step += stick;
            }
            _stickDir = stick;
            if (step != 0) MoveSelection(step > 0 ? 1 : -1);

            if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f)
                HoverSelect(mouse.position.ReadValue());

            // ---- confirm ----
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame)
                || (GameInput.Instance != null && GameInput.Instance.InteractPressedThisFrame());
            if (confirm) OnActionClicked(_index);
        }

        private void MoveSelection(int dir)
        {
            int n = _actions.Count;
            if (n == 0) return;

            int i = _index;
            for (int tries = 0; tries < n; tries++)
            {
                i = ((i + dir) % n + n) % n;
                if (i < 0 || i >= n) break;
                if (InteractMenu.IsInfoRow(_actions[i].label)) continue;
                _index = i;
                ApplyHighlight();
                return;
            }
        }

        private void HoverSelect(Vector2 screenPos)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (b == null || !b.interactable) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, screenPos, null))
                    continue;
                if (_index != i)
                {
                    _index = i;
                    ApplyHighlight();
                }
                return;
            }
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
            {
                if (_buttons[i] == null) continue;
                _buttons[i].gameObject.SetActive(false); // gone this frame, not at end of frame
                Destroy(_buttons[i].gameObject);
            }
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

                button.navigation = new Navigation { mode = Navigation.Mode.None };
                if (InteractMenu.IsInfoRow(_actions[i].label)) button.interactable = false;

                _buttons.Add(button);
            }

            float height = Pad + TitleHeight + ButtonSpacing
                + _actions.Count * (ButtonHeight + ButtonSpacing) + Pad - ButtonSpacing;
            _panel.sizeDelta = new Vector2(PanelWidth, Mathf.Max(height, TitleHeight + Pad * 2f));

            // Keep the highlight on a real row (two-step confirms re-query in place).
            if (_index < 0 || _index >= _actions.Count || InteractMenu.IsInfoRow(_actions[_index].label))
            {
                InteractMenu.CountActionable(_actions, out int first);
                _index = first;
            }
            ApplyHighlight();

            PositionAt(_lastScreenPos);
        }

        /// <summary>Gold base + dark text on the highlighted row; info rows dimmed.</summary>
        private void ApplyHighlight()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (b == null) continue;

                bool hot = i == _index;
                UIStyle.StyleButton(b, hot ? UIStyle.GoldDim : (Color?)null);

                var label = b.GetComponentInChildren<Text>();
                if (label != null)
                    label.color = hot ? HighlightText : (b.interactable ? UIStyle.Cream : UIStyle.Grey);
            }
        }

        private void OnActionClicked(int index)
        {
            if (!IsOpen || index < 0 || index >= _actions.Count) return;

            var act = _actions[index];
            if (InteractMenu.IsInfoRow(act.label)) return;

            if (act.closeOnRun)
            {
                // Close FIRST: an action that opens another modal (shop, inspect panel,
                // ghost placement) must find input released, and its own lock then stands.
                Close();
                if (act.action != null) act.action();
            }
            else
            {
                if (act.action != null) act.action();
                // Two-step confirms: re-query so relabeled actions show up.
                if (IsOpen) RebuildActions();
            }
        }

        // ---- positioning -----------------------------------------------------

        private void FollowAnchor()
        {
            if (_anchor == null) return;
            _lastScreenPos = AnchorScreenPos(_anchor);
            PositionAt(_lastScreenPos);
        }

        /// <summary>Screen point just right of the target's top edge (menu pivot is top-left).</summary>
        private static Vector2 AnchorScreenPos(Component anchor)
        {
            var cam = Camera.main;
            if (anchor == null || cam == null)
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Vector3 world;
            var col = anchor.GetComponent<Collider2D>();
            if (col != null)
            {
                Bounds b = col.bounds;
                world = new Vector3(b.max.x + 0.15f, b.max.y, 0f);
            }
            else
            {
                world = anchor.transform.position + new Vector3(0.6f, 0.6f, 0f);
            }

            Vector3 sp = cam.WorldToScreenPoint(world);
            return new Vector2(sp.x, sp.y);
        }

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

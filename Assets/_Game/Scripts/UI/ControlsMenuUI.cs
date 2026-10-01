using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Controls remapping page, opened from the Options menu. One row per
    /// gameplay binding (keyboard+mouse scheme only - gamepad keeps its
    /// defaults this pass): click a row, press the new key or mouse button,
    /// Esc cancels. Overrides persist via SaveBindingOverridesAsJson in
    /// PlayerPrefs and are re-applied on boot by this component's Start.
    ///
    /// While a rebind listen is active, UIInputLock.TextInputActive is set
    /// (console precedent) and the Pause/Console actions are disabled, so
    /// the pressed key can't toggle anything underneath the listen.
    ///
    /// This page only ever opens NESTED under OptionsMenuUI, which owns
    /// UIInputLock.ModalOpen and the gameplay-input block - so it touches
    /// neither. Built once, labels refreshed in place. Self-spawns.
    /// </summary>
    public class ControlsMenuUI : MonoBehaviour
    {
        public static ControlsMenuUI Instance { get; private set; }

        private const string BindingsPrefKey = "af_bindings";

        /// <summary>One rebindable row: action name + optional composite part.</summary>
        private struct Target
        {
            public string label;
            public string action;
            public string part; // composite part name ("up"...) or null

            public Target(string label, string action, string part = null)
            {
                this.label = label;
                this.action = action;
                this.part = part;
            }
        }

        // Move is a WASD composite, so it gets four rows (its first composite's
        // parts). Journal/Calendar/Feedback are direct Keyboard.current reads,
        // not actions - they are not rebindable here.
        private static readonly Target[] Targets =
        {
            new Target("Move Up", "Move", "up"),
            new Target("Move Down", "Move", "down"),
            new Target("Move Left", "Move", "left"),
            new Target("Move Right", "Move", "right"),
            new Target("Sprint", "Sprint"),
            new Target("Interact", "Interact"),
            new Target("Use Tool", "UseTool"),
            new Target("Cycle Tool", "CycleTool"),
            new Target("Toolbelt", "Toolbelt"),
            new Target("Build", "Build"),
            new Target("Pause", "Pause"),
        };

        private GameObject _panel;
        private Text[] _bindingLabels;

        private bool _open;
        private int _listeningRow = -1;
        private InputActionRebindingExtensions.RebindingOperation _rebindOp;
        private InputAction _rebindAction;
        private bool _rebindActionWasEnabled;
        private bool _pauseWasEnabled;
        private bool _consoleWasEnabled;

        // ------------------------------------------------------- Lifecycle

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        public static ControlsMenuUI GetOrCreate()
        {
            if (Instance == null)
                new GameObject("ControlsMenuUI (runtime)").AddComponent<ControlsMenuUI>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            ApplySavedOverrides();
        }

        private void OnDestroy()
        {
            CancelListen();
            if (Instance == this) Instance = null;
        }

        /// <summary>Boot-time restore of saved binding overrides onto the shared asset.</summary>
        private void ApplySavedOverrides()
        {
            if (GameInput.Instance == null || GameInput.Instance.Actions == null) return;

            string json = PlayerPrefs.GetString(BindingsPrefKey, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                GameInput.Instance.Actions.RemoveAllBindingOverrides();
                GameInput.Instance.Actions.LoadBindingOverridesFromJson(json);
            }
            catch (System.Exception e)
            {
                // A stale/garbled pref must never brick input: fall back to defaults.
                Debug.LogWarning("ControlsMenuUI: could not apply saved bindings - " + e.Message);
                GameInput.Instance.Actions.RemoveAllBindingOverrides();
                PlayerPrefs.DeleteKey(BindingsPrefKey);
            }
        }

        // ----------------------------------------------------- Open / close

        /// <summary>Opens the controls page (Options menu's Controls button).</summary>
        public static void Open()
        {
            GetOrCreate().OpenInternal();
        }

        /// <summary>Closed by OptionsMenuUI when it closes (or unpauses) underneath.</summary>
        public static void CloseIfOpen()
        {
            if (Instance != null && Instance._open) Instance.Close();
        }

        private void OpenInternal()
        {
            if (_open) return;

            if (_panel == null) BuildUI();

            RefreshRows();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling(); // render above the options menu
            _open = true;
        }

        private void Close()
        {
            if (!_open) return;

            CancelListen(); // never leave a rebind op dangling

            _open = false;
            if (_panel != null) _panel.SetActive(false);
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            var root = UIRoot.GetRoot();

            // Dim backdrop covering the whole screen.
            _panel = new GameObject("ControlsMenu");
            var overlayRt = _panel.AddComponent<RectTransform>();
            overlayRt.SetParent(root, false);
            Stretch(overlayRt);

            var dim = _panel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true; // swallow clicks behind the modal

            // Centered vertical panel.
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(overlayRt, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(560f, 860f);

            var panelImg = panel.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(panelImg, UIStyle.PanelBg);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Title + hint.
            var title = UIRoot.MakeText(panel, "Title", 32, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Controls";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var hint = UIRoot.MakeText(panel, "Hint", 18, TextAnchor.MiddleCenter, UIStyle.Grey);
            hint.text = "Click a binding, then press a key or mouse button. Esc cancels.";
            hint.rectTransform.sizeDelta = new Vector2(0f, 24f);

            UIStyle.MakeDivider(panel);

            // One row per rebind target: name left, binding button right.
            _bindingLabels = new Text[Targets.Length];
            for (int i = 0; i < Targets.Length; i++)
            {
                var row = new GameObject("Row_" + Targets[i].label).AddComponent<RectTransform>();
                row.SetParent(panel, false);
                row.sizeDelta = new Vector2(0f, 46f);

                var name = UIRoot.MakeText(row, "Name", 24, TextAnchor.MiddleLeft, UIStyle.Cream);
                name.text = Targets[i].label;
                var nameRt = name.rectTransform;
                nameRt.anchorMin = new Vector2(0f, 0f);
                nameRt.anchorMax = new Vector2(0f, 1f);
                nameRt.pivot = new Vector2(0f, 0.5f);
                nameRt.sizeDelta = new Vector2(240f, 0f);
                nameRt.anchoredPosition = Vector2.zero;

                int rowIndex = i; // capture for the closure
                var button = UIStyle.MakeButton(row, "", () => StartListen(rowIndex), 22,
                    UIStyle.PanelBgLight);
                var btnRt = (RectTransform)button.transform;
                btnRt.anchorMin = new Vector2(1f, 0.5f);
                btnRt.anchorMax = new Vector2(1f, 0.5f);
                btnRt.pivot = new Vector2(1f, 0.5f);
                btnRt.sizeDelta = new Vector2(250f, 40f);
                btnRt.anchoredPosition = Vector2.zero;

                _bindingLabels[i] = button.GetComponentInChildren<Text>();
            }

            UIStyle.MakeDivider(panel);

            // Reset + back.
            var reset = UIStyle.MakeButton(panel, "Reset to defaults", OnResetDefaults, 22);
            ((RectTransform)reset.transform).sizeDelta = new Vector2(0f, 48f);

            var back = UIStyle.MakeButton(panel, "Back", Close, 22);
            ((RectTransform)back.transform).sizeDelta = new Vector2(0f, 48f);

            _panel.SetActive(false);
        }

        /// <summary>Rewrites every row's binding display string in place.</summary>
        private void RefreshRows()
        {
            if (_bindingLabels == null) return;

            for (int i = 0; i < Targets.Length; i++)
            {
                if (_bindingLabels[i] == null) continue;

                if (i == _listeningRow)
                {
                    _bindingLabels[i].text = "<press a key>";
                    _bindingLabels[i].color = UIStyle.Gold;
                    continue;
                }

                _bindingLabels[i].color = UIStyle.Cream;

                var action = FindAction(Targets[i].action);
                int index = action != null ? FindBindingIndex(action, Targets[i].part) : -1;
                _bindingLabels[i].text = index >= 0
                    ? action.GetBindingDisplayString(index)
                    : "(missing)";
            }
        }

        // ------------------------------------------------------- Rebinding

        private static InputAction FindAction(string name)
        {
            if (GameInput.Instance == null || GameInput.Instance.Actions == null) return null;
            var map = GameInput.Instance.Actions.FindActionMap("Player");
            return map != null ? map.FindAction(name) : null;
        }

        /// <summary>
        /// Index of the row's keyboard+mouse binding: for a composite part,
        /// the FIRST matching part (WASD, not the arrow twin); otherwise the
        /// first plain KeyboardMouse-group binding (UseTool's Space, not its
        /// LeftButton twin - the second binding keeps its default).
        /// </summary>
        private static int FindBindingIndex(InputAction action, string part)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                bool kbm = b.groups != null && b.groups.Contains("KeyboardMouse");

                if (part == null)
                {
                    if (!b.isComposite && !b.isPartOfComposite && kbm) return i;
                }
                else
                {
                    if (b.isPartOfComposite && kbm
                        && string.Equals(b.name, part, System.StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        private void StartListen(int rowIndex)
        {
            if (_rebindOp != null) return; // one listen at a time

            var action = FindAction(Targets[rowIndex].action);
            int index = action != null ? FindBindingIndex(action, Targets[rowIndex].part) : -1;
            if (action == null || index < 0) return;

            _listeningRow = rowIndex;
            _rebindAction = action;

            // The pressed key must not reach gameplay, the console toggle, or
            // the pause toggle (direct readers check TextInputActive too).
            UIInputLock.TextInputActive = true;

            _rebindActionWasEnabled = action.enabled;
            if (_rebindActionWasEnabled) action.Disable(); // required by the rebind API

            var pause = FindAction("Pause");
            var console = FindAction("Console");
            _pauseWasEnabled = pause != null && pause.enabled;
            _consoleWasEnabled = console != null && console.enabled;
            if (_pauseWasEnabled) pause.Disable();
            if (_consoleWasEnabled) console.Disable();

            _rebindOp = action.PerformInteractiveRebinding(index)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Keyboard>/escape")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(_ => FinishListen(true))
                .OnCancel(_ => FinishListen(false))
                .Start();

            RefreshRows();
        }

        private void FinishListen(bool completed)
        {
            if (_rebindOp != null)
            {
                _rebindOp.Dispose();
                _rebindOp = null;
            }

            // Restore only what we disabled - gameplay actions that the pause
            // block keeps off must stay off.
            if (_rebindAction != null && _rebindActionWasEnabled) _rebindAction.Enable();
            _rebindAction = null;

            var pause = FindAction("Pause");
            var console = FindAction("Console");
            if (_pauseWasEnabled && pause != null) pause.Enable();
            if (_consoleWasEnabled && console != null) console.Enable();

            UIInputLock.TextInputActive = false;
            _listeningRow = -1;

            if (completed) SaveOverrides();
            RefreshRows();
        }

        /// <summary>Cancels a live listen without saving (close, destroy).</summary>
        private void CancelListen()
        {
            if (_rebindOp != null)
                _rebindOp.Cancel(); // fires OnCancel -> FinishListen(false)
        }

        private static void SaveOverrides()
        {
            if (GameInput.Instance == null || GameInput.Instance.Actions == null) return;
            PlayerPrefs.SetString(BindingsPrefKey,
                GameInput.Instance.Actions.SaveBindingOverridesAsJson());
        }

        private void OnResetDefaults()
        {
            CancelListen();

            if (GameInput.Instance != null && GameInput.Instance.Actions != null)
                GameInput.Instance.Actions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(BindingsPrefKey);

            RefreshRows();
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

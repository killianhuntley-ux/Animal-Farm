using System;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The name field of the naming ceremony (muscle 03 "Light descends").
    /// SpiritManager.NamingRequested is bridged into NamingCeremony.Begin,
    /// which choreographs the scene and calls <see cref="Prompt"/> at the bow:
    /// a light backdrop (the spirit and the guide-light stay visible), a
    /// prefilled, editable name InputField and a confirm button. Gameplay
    /// input is blocked while open; uGUI keeps its own input path so typing
    /// works. One prompt at a time - the ceremony queues requests.
    /// </summary>
    public class NamePromptUI : MonoBehaviour
    {
        private static readonly string[] NamePool =
        {
            "Pip", "Morsel", "Dregs", "Bramble", "Tallow", "Wick",
            "Sorrel", "Nettle", "Fig", "Cinder", "Mote", "Burr"
        };

        private const float FadeInSeconds = 0.5f;

        public static NamePromptUI Instance { get; private set; }

        private GameObject _overlay;
        private CanvasGroup _group;
        private Text _title;
        private Text _subtitle;
        private InputField _input;
        private string _suggestion = "Pip";

        private SpiritAgent _current;
        private Action<string> _onConfirm;
        private bool _holdInputOnClose;
        private bool _subscribed;
        private bool _holdingText;   // this prompt set UIInputLock.TextInputActive (and so must clear it)

        /// <summary>True while the name field is on screen.</summary>
        public bool IsOpen => _current != null;

        /// <summary>True while the name field is open and focused (it holds TextInputActive).</summary>
        public bool IsTyping => _current != null && _input != null && _input.isFocused;

        /// <summary>A random pool name (used when a request carries no suggestion).</summary>
        public static string RandomSuggestion() => NamePool[UnityEngine.Random.Range(0, NamePool.Length)];

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void Start()
        {
            SpiritManager.NamingRequested += OnNamingRequested;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed)
                SpiritManager.NamingRequested -= OnNamingRequested;
            if (_holdingText) { _holdingText = false; UIInputLock.TextInputActive = false; } // we held it: release on teardown
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_group != null && _overlay != null && _overlay.activeSelf && _group.alpha < 1f)
                _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, Time.unscaledDeltaTime / FadeInSeconds);

            // The spirit vanished under the prompt (despawn, ascend): close quietly.
            if (_current == null && _overlay != null && _overlay.activeSelf && _onConfirm != null)
                Cancel();
        }

        // ------------------------------------------------------------- Requests

        /// <summary>A visitor decided to stay: run the ceremony (it queues overlapping requests).</summary>
        private void OnNamingRequested(SpiritAgent agent)
        {
            if (agent == null) return;
            NamingCeremony.Begin(agent);
        }

        /// <summary>
        /// Shows the name field for <paramref name="agent"/>. The suggestion
        /// (blank = a random pool name) is pre-filled and stays editable; an
        /// emptied field falls back to it. <paramref name="onConfirm"/> gets
        /// the final trimmed name AFTER the field has closed. With
        /// <paramref name="holdInputOnClose"/> the gameplay block is left to
        /// the caller (the ceremony keeps it until its tail finishes).
        /// </summary>
        public void Prompt(SpiritAgent agent, string suggestedName, Action<string> onConfirm,
            string title = null, bool holdInputOnClose = false)
        {
            if (agent == null) return;
            if (_overlay == null) BuildUI();

            _current = agent;
            _onConfirm = onConfirm;
            _holdInputOnClose = holdInputOnClose;

            _title.text = string.IsNullOrEmpty(title) ? "A spirit wishes to stay!" : title;

            string species = agent.Species != null && !string.IsNullOrEmpty(agent.Species.displayName)
                ? agent.Species.displayName
                : "A spirit";
            _subtitle.text = species + " - give it a name:";

            _suggestion = string.IsNullOrWhiteSpace(suggestedName) ? RandomSuggestion() : suggestedName.Trim();
            _input.text = _suggestion;

            _group.alpha = 0f;
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            UIInputLock.TextInputActive = true; // name typing must not hit gameplay keys
            _holdingText = true;

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);

            _input.Select();
            _input.ActivateInputField();
        }

        /// <summary>Closes the field without naming anyone (the agent went away).</summary>
        public void Cancel()
        {
            _current = null;
            _onConfirm = null;
            Hide();
        }

        private void OnConfirm()
        {
            if (_current == null) { Cancel(); return; }

            string name = _input != null && _input.text != null ? _input.text.Trim() : "";
            if (name.Length == 0) name = _suggestion;

            var callback = _onConfirm;
            _current = null;
            _onConfirm = null;

            Hide();
            callback?.Invoke(name);
        }

        private void Hide()
        {
            if (_overlay != null) _overlay.SetActive(false);
            if (_holdingText) // only the holder clears it (the console may own the flag)
            {
                _holdingText = false;
                UIInputLock.TextInputActive = false;
            }

            if (!_holdInputOnClose && GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
            _holdInputOnClose = false;
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            var root = UIRoot.GetRoot();

            // Light backdrop: the world (spirit, guide-light) must stay visible.
            _overlay = new GameObject("NamePrompt");
            var overlayRt = _overlay.AddComponent<RectTransform>();
            overlayRt.SetParent(root, false);
            Stretch(overlayRt);

            _group = _overlay.AddComponent<CanvasGroup>();

            var dim = _overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.3f);
            dim.raycastTarget = true; // swallow clicks behind the modal

            // Panel sits in the lower third so the bow stays in view.
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(overlayRt, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.3f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(480f, 300f);

            var panelImg = panel.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(panelImg, UIStyle.PanelBg);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Title + subtitle.
            _title = UIRoot.MakeText(panel, "Title", 34, TextAnchor.MiddleCenter,
                UIStyle.Cream);
            _title.text = "A spirit wishes to stay!";
            _title.rectTransform.sizeDelta = new Vector2(0f, 48f);

            _subtitle = UIRoot.MakeText(panel, "Subtitle", 22, TextAnchor.MiddleCenter,
                UIStyle.Grey);
            _subtitle.text = "";
            _subtitle.rectTransform.sizeDelta = new Vector2(0f, 30f);

            // Name input (legacy InputField: bg image + child text).
            var inputGo = new GameObject("NameInput");
            var inputRt = inputGo.AddComponent<RectTransform>();
            inputRt.SetParent(panel, false);
            inputRt.sizeDelta = new Vector2(0f, 48f);

            var inputBg = inputGo.AddComponent<Image>();
            UIStyle.ApplyPanel(inputBg, UIStyle.PanelBgLight);

            var inputText = UIRoot.MakeText(inputRt, "Text", 26, TextAnchor.MiddleLeft, UIStyle.Cream);
            inputText.supportRichText = false;
            inputText.raycastTarget = true;
            var textRt = inputText.rectTransform;
            Stretch(textRt);
            textRt.offsetMin = new Vector2(12f, 6f);
            textRt.offsetMax = new Vector2(-12f, -6f);

            _input = inputGo.AddComponent<InputField>();
            _input.targetGraphic = inputBg;
            _input.textComponent = inputText;
            _input.characterLimit = 16;
            _input.onSubmit.AddListener(_ => OnConfirm()); // Enter confirms
            // uGUI reads keys through the EventSystem's input module, which
            // SetGameplayBlocked doesn't touch - typing works while blocked.

            // Confirm button.
            MakeButton(panel, "Welcome them", OnConfirm);

            _overlay.SetActive(false);
        }

        private static void MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 56f);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", 26, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;
            Stretch(text.rectTransform);
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

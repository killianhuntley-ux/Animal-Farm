using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Modal that pops when a visitor decides to stay (SpiritManager.NamingRequested):
    /// dim backdrop, a prefilled name InputField and a confirm button. Gameplay
    /// input is blocked while open; uGUI keeps its own input path so typing works.
    /// Requests arriving while the modal is open are queued.
    /// </summary>
    public class NamePromptUI : MonoBehaviour
    {
        private static readonly string[] NamePool =
        {
            "Pip", "Morsel", "Dregs", "Bramble", "Tallow", "Wick",
            "Sorrel", "Nettle", "Fig", "Cinder", "Mote", "Burr"
        };

        private GameObject _overlay;
        private Text _subtitle;
        private InputField _input;
        private string _suggestion = "Pip";

        private SpiritAgent _current;
        private readonly Queue<SpiritAgent> _queue = new Queue<SpiritAgent>();
        private bool _subscribed;

        private void Start()
        {
            SpiritManager.NamingRequested += OnNamingRequested;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed)
                SpiritManager.NamingRequested -= OnNamingRequested;
        }

        // ------------------------------------------------------------- Requests

        private void OnNamingRequested(SpiritAgent agent)
        {
            if (agent == null) return;

            if (_current != null)
            {
                _queue.Enqueue(agent); // one at a time; shown after confirm
                return;
            }

            ShowFor(agent);
        }

        private void ShowFor(SpiritAgent agent)
        {
            if (_overlay == null) BuildUI();

            _current = agent;

            string species = agent.Species != null && !string.IsNullOrEmpty(agent.Species.displayName)
                ? agent.Species.displayName
                : "A spirit";
            _subtitle.text = species + " - give it a name:";

            _suggestion = NamePool[Random.Range(0, NamePool.Length)];
            _input.text = _suggestion;

            _overlay.SetActive(true);
            AnimalFarm.Core.UIInputLock.TextInputActive = true; // name typing must not hit gameplay keys

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);

            _input.Select();
            _input.ActivateInputField();
        }

        private void OnConfirm()
        {
            if (_current == null) { Hide(); return; }

            string name = _input != null && _input.text != null ? _input.text.Trim() : "";
            if (name.Length == 0) name = _suggestion;

            var agent = _current;
            _current = null;

            agent.SetGivenName(name);
            FloatingText.Show(agent.transform.position + Vector3.up * 0.9f, name + "!", UIStyle.Gold);

            Hide();

            // Show the next queued request, if any (skipping destroyed agents).
            while (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                if (next != null) { ShowFor(next); return; }
            }
        }

        private void Hide()
        {
            if (_overlay != null) _overlay.SetActive(false);
            AnimalFarm.Core.UIInputLock.TextInputActive = false;

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            var root = UIRoot.GetRoot();

            // Dim backdrop covering the whole screen.
            _overlay = new GameObject("NamePrompt");
            var overlayRt = _overlay.AddComponent<RectTransform>();
            overlayRt.SetParent(root, false);
            Stretch(overlayRt);

            var dim = _overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true; // swallow clicks behind the modal

            // Centered panel.
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(overlayRt, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
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
            var title = UIRoot.MakeText(panel, "Title", 34, TextAnchor.MiddleCenter,
                UIStyle.Cream);
            title.text = "A spirit wishes to stay!";
            title.rectTransform.sizeDelta = new Vector2(0f, 48f);

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
            // SetGameplayBlocked doesn't touch — typing works while blocked.

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

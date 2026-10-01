using System.Text;
using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The playtest feedback tool - the owner's re-examination mechanism
    /// (Muscle 10 verdict 4). F8 opens a modal checklist of every muscle
    /// aspect: prompt + 1-5 rating buttons + a multiline note box per row.
    /// "Save responses" writes a markdown file into
    /// Application.persistentDataPath (playtest_feedback_day&lt;D&gt;_&lt;stamp&gt;.md)
    /// with date, in-game day/season and session length; untouched rows are
    /// skipped. Every save writes a NEW file - nothing is ever overwritten -
    /// and the full path lands in the Unity log so it can be read back.
    ///
    /// Modal pattern (CalendarUI): direct F8 read checks BlockDirectKeys
    /// when closed; open, only a live text field may eat the key. While any
    /// note box is focused, UIInputLock.TextInputActive is held (console
    /// precedent) so typing never reaches gameplay or other direct readers.
    /// Built once, refreshed in place. Self-spawns - no scene setup.
    /// </summary>
    public class FeedbackUI : MonoBehaviour
    {
        public static FeedbackUI Instance { get; private set; }

        private const int NoteCharacterLimit = 1000;

        /// <summary>
        /// The checklist: { title, prompt }. Data-only - extend by adding
        /// rows here; everything else (UI, save file) follows automatically.
        /// </summary>
        private static readonly string[,] Aspects =
        {
            { "Movement feel", "Does walking and sprinting feel good?" },
            { "Tool weight", "Do tool actions feel weighty, with a good rhythm?" },
            { "Tool tiers", "Do the upgrade tiers change how the tools feel?" },
            { "XP and leveling", "Is shepherd progression paced right?" },
            { "Camera and clamp", "Does the camera follow and edge clamp feel right?" },
            { "Sit and rest", "Sitting down to rest (when built) - worth doing?" },
            { "Calendar and seasons", "Do the 15-day seasons read clearly and matter?" },
            { "Rain", "Does rain look right and change your plans?" },
            { "Biome sculpting", "Is sculpting biomes to lure spirits satisfying?" },
            { "Parcel and road purchase", "Buying land from the Ferryman - clear and fair?" },
            { "Spirit charm", "Bubbles, reactions, idles - do the spirits charm you?" },
            { "Naming ceremony", "Does naming a new resident land emotionally?" },
            { "Styx crossing", "The Styx crossing (when built) - how does it feel?" },
            { "Weave rite", "The weave rite (when built) - how does it feel?" },
            { "Vendors", "Blacksmith and merchant - useful and in character?" },
            { "Weeds and villains", "Are the weeds pressure or just a chore?" },
            { "Audio", "Bleeps, voices, music - endearing or annoying?" },
            { "UI readability", "Is every panel readable at a glance?" },
            { "Overall fun", "Did you have fun? What pulled you in or pushed you out?" },
        };

        // ---- UI (built once) ----
        private GameObject _panel;
        private InputField[] _inputs;
        private Button[][] _ratingButtons;
        private int[] _ratings; // 0 = untouched, 1-5 otherwise
        private Text _savedLine;

        private bool _open;
        private bool _textLockHeld;
        private float _sessionStart;

        // ------------------------------------------------------- Lifecycle

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance == null)
                new GameObject("FeedbackUI (runtime)").AddComponent<FeedbackUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _sessionStart = Time.realtimeSinceStartup;
            _ratings = new int[Aspects.GetLength(0)];
        }

        private void OnDestroy()
        {
            if (_textLockHeld)
            {
                UIInputLock.TextInputActive = false;
                _textLockHeld = false;
            }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // F8 toggles. Direct device read: closed -> full BlockDirectKeys
            // check; open -> we ARE the modal, so only a live text field may
            // eat the key (CalendarUI precedent).
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame)
            {
                if (_open)
                {
                    if (!UIInputLock.TextInputActive) Close();
                }
                else if (!UIInputLock.BlockDirectKeys)
                {
                    Open();
                }
            }

            if (!_open) return;

            // Hold TextInputActive exactly while one of our note boxes is
            // focused (console precedent - typing must never hit gameplay).
            bool anyFocused = false;
            if (_inputs != null)
            {
                for (int i = 0; i < _inputs.Length && !anyFocused; i++)
                    anyFocused = _inputs[i] != null && _inputs[i].isFocused;
            }
            if (anyFocused != _textLockHeld)
            {
                _textLockHeld = anyFocused;
                UIInputLock.TextInputActive = anyFocused;
            }
        }

        // ----------------------------------------------------- Open / close

        private void Open()
        {
            if (_open) return;

            if (_panel == null) BuildUI();

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
            if (_textLockHeld)
            {
                _textLockHeld = false;
                UIInputLock.TextInputActive = false;
            }
            if (_panel != null) _panel.SetActive(false);

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

            // Near-fullscreen dark panel (JournalUI proportions).
            _panel = new GameObject("FeedbackPanel");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = new Vector2(180f, 40f);
            panelRt.offsetMax = new Vector2(-180f, -40f);

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true; // swallow clicks behind the panel

            // Title + subtitle.
            var title = UIRoot.MakeText(panelRt, "Title", 32, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Playtest Feedback  [F8]";
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -12f);
            titleRt.sizeDelta = new Vector2(0f, 40f);

            var sub = UIRoot.MakeText(panelRt, "Subtitle", 18, TextAnchor.MiddleCenter, UIStyle.Grey);
            sub.text = "Rate 1-5, write what you felt. Untouched rows are skipped.";
            var subRt = sub.rectTransform;
            subRt.anchorMin = new Vector2(0f, 1f);
            subRt.anchorMax = new Vector2(1f, 1f);
            subRt.pivot = new Vector2(0.5f, 1f);
            subRt.anchoredPosition = new Vector2(0f, -52f);
            subRt.sizeDelta = new Vector2(0f, 24f);

            // Scrollable checklist between header and footer.
            var scrollGo = new GameObject("Scroll");
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.SetParent(panelRt, false);
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(20f, 118f);
            scrollRt.offsetMax = new Vector2(-20f, -84f);

            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0.25f);
            scrollBg.raycastTarget = true; // scroll wheel needs a target

            var scroll = scrollGo.AddComponent<ScrollRect>();

            var viewportGo = new GameObject("Viewport");
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.SetParent(scrollRt, false);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8f, 8f);
            viewportRt.offsetMax = new Vector2(-8f, -8f);
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero;

            var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.spacing = 14f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRt;
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // Aspect rows.
            int count = Aspects.GetLength(0);
            _inputs = new InputField[count];
            _ratingButtons = new Button[count][];
            for (int i = 0; i < count; i++)
                BuildAspectRow(contentRt, i);

            // Footer: saved-path line + Save / Close.
            _savedLine = UIRoot.MakeText(panelRt, "SavedLine", 16, TextAnchor.MiddleCenter, UIStyle.Grey);
            _savedLine.text = "";
            var savedRt = _savedLine.rectTransform;
            savedRt.anchorMin = new Vector2(0f, 0f);
            savedRt.anchorMax = new Vector2(1f, 0f);
            savedRt.pivot = new Vector2(0.5f, 0f);
            savedRt.anchoredPosition = new Vector2(0f, 88f);
            savedRt.sizeDelta = new Vector2(0f, 24f);

            var save = UIStyle.MakeButton(panelRt, "Save responses", OnSave, 24, UIStyle.GoldDim);
            var saveRt = (RectTransform)save.transform;
            saveRt.anchorMin = new Vector2(0.5f, 0f);
            saveRt.anchorMax = new Vector2(0.5f, 0f);
            saveRt.pivot = new Vector2(1f, 0f);
            saveRt.anchoredPosition = new Vector2(-10f, 20f);
            saveRt.sizeDelta = new Vector2(280f, 56f);

            var close = UIStyle.MakeButton(panelRt, "Close  [F8]", Close, 24);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = new Vector2(0.5f, 0f);
            closeRt.anchorMax = new Vector2(0.5f, 0f);
            closeRt.pivot = new Vector2(0f, 0f);
            closeRt.anchoredPosition = new Vector2(10f, 20f);
            closeRt.sizeDelta = new Vector2(220f, 56f);

            _panel.SetActive(false);
        }

        /// <summary>One checklist row: title, prompt, 1-5 buttons, note box.</summary>
        private void BuildAspectRow(RectTransform parent, int index)
        {
            var row = new GameObject("Aspect_" + Aspects[index, 0]).AddComponent<RectTransform>();
            row.SetParent(parent, false);
            row.sizeDelta = new Vector2(0f, 188f);

            var title = UIRoot.MakeText(row, "Title", 24, TextAnchor.MiddleLeft, UIStyle.Gold);
            title.text = (index + 1) + ". " + Aspects[index, 0];
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, 0f);
            titleRt.sizeDelta = new Vector2(0f, 28f);

            var prompt = UIRoot.MakeText(row, "Prompt", 19, TextAnchor.MiddleLeft, UIStyle.Grey);
            prompt.text = Aspects[index, 1];
            var promptRt = prompt.rectTransform;
            promptRt.anchorMin = new Vector2(0f, 1f);
            promptRt.anchorMax = new Vector2(1f, 1f);
            promptRt.pivot = new Vector2(0.5f, 1f);
            promptRt.anchoredPosition = new Vector2(0f, -28f);
            promptRt.sizeDelta = new Vector2(0f, 24f);

            // Five small rating buttons.
            _ratingButtons[index] = new Button[5];
            for (int k = 0; k < 5; k++)
            {
                int aspect = index, rating = k + 1; // capture for the closure
                var button = UIStyle.MakeButton(row, (k + 1).ToString(),
                    () => OnRate(aspect, rating), 20, UIStyle.PanelBgLight);
                var btnRt = (RectTransform)button.transform;
                btnRt.anchorMin = new Vector2(0f, 1f);
                btnRt.anchorMax = new Vector2(0f, 1f);
                btnRt.pivot = new Vector2(0f, 1f);
                btnRt.anchoredPosition = new Vector2(k * 52f, -56f);
                btnRt.sizeDelta = new Vector2(44f, 30f);
                _ratingButtons[index][k] = button;
            }

            // Multiline note box (legacy InputField: bg image + child text).
            var inputGo = new GameObject("Notes");
            var inputRt = inputGo.AddComponent<RectTransform>();
            inputRt.SetParent(row, false);
            inputRt.anchorMin = new Vector2(0f, 0f);
            inputRt.anchorMax = new Vector2(1f, 0f);
            inputRt.pivot = new Vector2(0.5f, 0f);
            inputRt.anchoredPosition = new Vector2(0f, 4f);
            inputRt.sizeDelta = new Vector2(0f, 88f);

            var inputBg = inputGo.AddComponent<Image>();
            UIStyle.ApplyPanel(inputBg, UIStyle.PanelBgLight);

            var inputText = UIRoot.MakeText(inputRt, "Text", 19, TextAnchor.UpperLeft, UIStyle.Cream);
            inputText.supportRichText = false;
            inputText.raycastTarget = true;
            inputText.horizontalOverflow = HorizontalWrapMode.Wrap;
            inputText.verticalOverflow = VerticalWrapMode.Truncate;
            var textRt = inputText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 6f);
            textRt.offsetMax = new Vector2(-10f, -6f);

            var placeholder = UIRoot.MakeText(inputRt, "Placeholder", 19, TextAnchor.UpperLeft,
                new Color(0.62f, 0.63f, 0.66f, 0.5f));
            placeholder.text = "(notes...)";
            placeholder.fontStyle = FontStyle.Italic;
            var phRt = placeholder.rectTransform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(10f, 6f);
            phRt.offsetMax = new Vector2(-10f, -6f);

            var input = inputGo.AddComponent<InputField>();
            input.targetGraphic = inputBg;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.characterLimit = NoteCharacterLimit;
            input.lineType = InputField.LineType.MultiLineNewline;
            // uGUI reads keys through the EventSystem's input module, which
            // SetGameplayBlocked doesn't touch - typing works while blocked.
            _inputs[index] = input;
        }

        // ------------------------------------------------------------- Actions

        /// <summary>Clicking the current rating again clears it back to untouched.</summary>
        private void OnRate(int aspect, int rating)
        {
            _ratings[aspect] = _ratings[aspect] == rating ? 0 : rating;
            RefreshRatingRow(aspect);
        }

        /// <summary>Restyles one row's five buttons: the chosen one goes gold.</summary>
        private void RefreshRatingRow(int aspect)
        {
            var buttons = _ratingButtons[aspect];
            if (buttons == null) return;

            for (int k = 0; k < buttons.Length; k++)
            {
                if (buttons[k] == null) continue;
                bool chosen = _ratings[aspect] == k + 1;
                UIStyle.StyleButton(buttons[k], chosen ? UIStyle.Gold : UIStyle.PanelBgLight);
                var label = buttons[k].GetComponentInChildren<Text>();
                if (label != null)
                    label.color = chosen ? new Color(0.18f, 0.15f, 0.06f, 1f) : UIStyle.Cream;
            }
        }

        // ---------------------------------------------------------------- Save

        private void OnSave()
        {
            // Collect touched rows first - an empty session writes nothing.
            var body = new StringBuilder();
            int touched = 0;
            for (int i = 0; i < Aspects.GetLength(0); i++)
            {
                string notes = _inputs[i] != null && _inputs[i].text != null
                    ? _inputs[i].text.Trim() : "";
                int rating = _ratings[i];
                if (rating == 0 && notes.Length == 0) continue;

                touched++;
                body.AppendLine("## " + Aspects[i, 0]
                    + (rating > 0 ? " (" + rating + "/5)" : " (unrated)"));
                body.AppendLine();
                body.AppendLine(notes.Length > 0 ? notes : "(no notes)");
                body.AppendLine();
            }

            if (touched == 0)
            {
                if (_savedLine != null)
                    _savedLine.text = "Nothing to save yet - rate or write something first.";
                return;
            }

            // Header: real date + in-game context + session length.
            var clock = GameClock.Instance;
            var calendar = GameCalendar.Instance;
            var now = System.DateTime.Now;

            float seconds = Time.realtimeSinceStartup - _sessionStart;
            int minutes = Mathf.FloorToInt(seconds / 60f);
            string length = (minutes / 60) + "h " + (minutes % 60) + "m";

            var sb = new StringBuilder();
            sb.AppendLine("# Playtest feedback");
            sb.AppendLine();
            sb.AppendLine("- Real date: " + now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("- In-game: "
                + (clock != null ? "Day " + clock.Day : "Day ?")
                + (calendar != null ? " (" + calendar.DateLine + ")" : "")
                + (clock != null ? ", " + clock.TimeString : ""));
            sb.AppendLine("- Session length: " + length);
            sb.AppendLine();
            sb.Append(body);

            // New file every save, never overwrite: day + timestamp, plus a
            // numeric suffix in the (sub-second) collision case.
            string stem = "playtest_feedback_day" + (clock != null ? clock.Day : 0)
                + "_" + now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = System.IO.Path.Combine(Application.persistentDataPath, stem + ".md");
            for (int n = 2; System.IO.File.Exists(path); n++)
                path = System.IO.Path.Combine(Application.persistentDataPath,
                    stem + "_" + n + ".md");

            try
            {
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogError("FeedbackUI: could not write feedback file - " + e.Message);
                if (_savedLine != null) _savedLine.text = "Save FAILED: " + e.Message;
                return;
            }

            // Make the path impossible to lose: log, panel line, world toast.
            Debug.Log("Playtest feedback saved to: " + path);
            if (_savedLine != null) _savedLine.text = "Saved: " + path;

            var player = GameObject.FindWithTag("Player");
            Vector3 pos;
            if (player != null) pos = player.transform.position + Vector3.up * 0.9f;
            else if (Camera.main != null)
                pos = new Vector3(Camera.main.transform.position.x,
                    Camera.main.transform.position.y, 0f);
            else pos = Vector3.zero;
            FloatingText.Show(pos, "Feedback saved", UIStyle.Gold);

            Bleeps.Play(BleepKind.Coin, 0.5f);
        }
    }
}

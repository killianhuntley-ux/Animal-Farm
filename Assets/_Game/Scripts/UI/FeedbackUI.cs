using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AnimalFarm.Core;
using AnimalFarm.Onboarding;
using AnimalFarm.Spirits;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Kv = System.Collections.Generic.KeyValuePair<string, string>;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The development QA tool (F8). The developer playtests their own build and
    /// hands the saved report to an AI coding assistant, so this is a concrete
    /// test checklist, not a sentiment survey: every row (FeedbackChecklist) has
    /// an id, test steps with debug-console shortcuts, and an expected result;
    /// the tester marks Untested / Pass / Issue / Broken / Blocked / N/A, adds a
    /// severity and a repro note. A "New bug" form at the top logs free-form bugs
    /// (title, steps, expected, actual, severity), each stamped with an auto
    /// context snapshot; "Screenshot" hides the panel, captures a timestamped png
    /// into Application.persistentDataPath and links the path into the active
    /// bug/note.
    ///
    /// Every report carries auto-captured context (real time, session length,
    /// build/Unity version, in-game day/season/hour, inventory, level, spirits by
    /// state, parcels, position, FPS avg/min/hitches, and the last ~40
    /// warnings/errors/exceptions of the whole session via
    /// Application.logMessageReceived, hooked before the scene loads).
    ///
    /// Persistence: statuses, notes, bugs, filter and expanded sections
    /// auto-save (debounced) to persistentDataPath/feedback_draft.json, on
    /// close and on quit, and restore on the next run - nothing is lost across
    /// play sessions. "Export report" writes a NEW markdown file every time
    /// (playtest_feedback_yyyyMMdd_HHmmss.md, never overwritten): summary,
    /// bugs by severity, Issue/Broken/Blocked checks, Pass, then context + error
    /// log; untouched rows are omitted. "Reset statuses" is a two-step in-panel
    /// confirm (no dialog APIs) and backs the draft up first.
    ///
    /// Modal pattern (CalendarUI): direct F8 read checks BlockDirectKeys when
    /// closed; open, only a live text field may eat the key. While ANY text box
    /// (notes or the bug form) is focused, UIInputLock.TextInputActive is held
    /// (console precedent) so typing never reaches gameplay or other direct
    /// readers. Like the other modals it does NOT pause the game (it blocks
    /// gameplay input and restores it pause-aware on close). Rows are built
    /// lazily per section on first expand. Self-spawns - no scene setup.
    /// </summary>
    public class FeedbackUI : MonoBehaviour
    {
        public static FeedbackUI Instance { get; private set; }

        private const int NoteCharacterLimit = 2000;
        private const int SingleLineLimit = 160;
        private const int MaxLogEntries = 40;
        private const float SaveDebounceSeconds = 1.5f;
        private const float FpsWarmupSeconds = 3f;
        private const string DraftFileName = "feedback_draft.json";

        // ------------------------------------------------ status / severity

        private const int StUntested = 0, StPass = 1, StIssue = 2, StBroken = 3, StBlocked = 4, StNA = 5;
        private const int StatusCount = 6;
        private static readonly string[] StatusNames = { "Untested", "Pass", "Issue", "Broken", "Blocked", "N/A" };
        private static readonly Color[] StatusColors =
        {
            new Color(0.30f, 0.31f, 0.38f, 1f),   // Untested
            new Color(0.27f, 0.58f, 0.34f, 1f),   // Pass
            new Color(0.80f, 0.62f, 0.22f, 1f),   // Issue
            new Color(0.78f, 0.30f, 0.28f, 1f),   // Broken
            new Color(0.52f, 0.38f, 0.70f, 1f),   // Blocked
            new Color(0.42f, 0.43f, 0.47f, 1f),   // N/A
        };

        private const int SevNone = 0, SevLow = 1, SevMed = 2, SevHigh = 3, SevCrash = 4;
        private static readonly string[] SeverityNames = { "None", "Low", "Med", "High", "Crash" };
        private static readonly Color[] SeverityColors =
        {
            new Color(0.30f, 0.31f, 0.38f, 1f),
            new Color(0.40f, 0.60f, 0.78f, 1f),   // Low
            new Color(0.80f, 0.62f, 0.22f, 1f),   // Med
            new Color(0.86f, 0.42f, 0.24f, 1f),   // High
            new Color(0.80f, 0.20f, 0.25f, 1f),   // Crash
        };

        private static readonly Color DarkLabel = new Color(0.12f, 0.10f, 0.04f, 1f);
        private static readonly Color PassGreen = new Color(0.55f, 0.85f, 0.60f, 1f);
        private static readonly Color BadRed = new Color(0.95f, 0.50f, 0.45f, 1f);
        private static readonly Color WarnAmber = new Color(0.95f, 0.78f, 0.40f, 1f);

        // ---------------------------------------------------- saved data

        [System.Serializable]
        private class CheckState
        {
            public string id;
            public int status;
            public int severity;
            public string note = "";
            public bool touched;
            public string when = "";
            public string build = "";
        }

        [System.Serializable]
        private class Bug
        {
            public string id;
            public string title = "";
            public string steps = "";
            public string expected = "";
            public string actual = "";
            public int severity = SevMed;
            public string when = "";
            public string build = "";
            public string context = "";
            public List<string> shots = new List<string>();
        }

        [System.Serializable]
        private class Draft
        {
            public int version = 1;
            public string created = "";
            public string lastSaved = "";
            public List<CheckState> checks = new List<CheckState>();
            public List<Bug> bugs = new List<Bug>();
            public int nextBugNo = 1;
            public string formTitle = "";
            public string formSteps = "";
            public string formExpected = "";
            public string formActual = "";
            public int formSeverity = SevMed;
            public List<string> formShots = new List<string>();
            public int filter;                                  // 0 all, 1 untested, 2 issues only
            public List<string> expanded = new List<string>();  // expanded section names
            public List<string> sessions = new List<string>();  // one line per play session that loaded this draft
        }

        // ------------------------------------------------- log capture
        // Hooked before the scene loads (static, thread-safe) so errors raised
        // in scene Awake are in the buffer too.

        private struct LogEntry
        {
            public string time;
            public LogType type;
            public string message;
            public string stack;
            public int count;
        }

        private static readonly object LogLock = new object();
        private static readonly List<LogEntry> LogBuffer = new List<LogEntry>();
        private static int _errorCount, _warningCount, _exceptionCount;
        private static System.Diagnostics.Stopwatch _sessionClock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void HookLogs()
        {
            lock (LogLock)
            {
                LogBuffer.Clear();
                _errorCount = _warningCount = _exceptionCount = 0;
            }
            _sessionClock = System.Diagnostics.Stopwatch.StartNew();
            Application.logMessageReceivedThreaded -= OnLogMessage;
            Application.logMessageReceivedThreaded += OnLogMessage;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;

            string msg = FlattenLine(message, 400);
            string stack = FirstStackLine(stackTrace);

            lock (LogLock)
            {
                if (type == LogType.Warning) _warningCount++;
                else if (type == LogType.Exception) _exceptionCount++;
                else _errorCount++;

                // Collapse an immediate repeat into a count so spam cannot evict real history.
                if (LogBuffer.Count > 0)
                {
                    var last = LogBuffer[LogBuffer.Count - 1];
                    if (last.type == type && last.message == msg)
                    {
                        last.count++;
                        LogBuffer[LogBuffer.Count - 1] = last;
                        return;
                    }
                }

                string t = System.DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                if (_sessionClock != null)
                    t += " (t+" + FormatDuration(_sessionClock.Elapsed.TotalSeconds) + ")";

                LogBuffer.Add(new LogEntry { time = t, type = type, message = msg, stack = stack, count = 1 });
                if (LogBuffer.Count > MaxLogEntries) LogBuffer.RemoveAt(0);
            }
        }

        // ------------------------------------------------ views (UI side)

        private struct InputTarget
        {
            public int kind;   // 0 = new-bug form, 1 = check note, 2 = logged bug
            public string id;
        }

        private sealed class CheckView
        {
            public FeedbackCheck check;
            public CheckState state;
            public RectTransform root;
            public Text title;
            public Button[] statusButtons;
            public GameObject severityRow;
            public Button[] severityButtons;
            public GameObject noteGo;
            public InputField note;
        }

        private sealed class SectionView
        {
            public string name;
            public List<FeedbackCheck> checks = new List<FeedbackCheck>();
            public List<CheckView> views = new List<CheckView>();
            public Button header;
            public Image headerImage;
            public Text headerText;
            public RectTransform body;
            public bool built;
            public bool expanded;
        }

        private struct Counts
        {
            public int total, untested, pass, issue, broken, blocked, na;
            public int Tested => pass + issue + broken + blocked;
            public int Issues => issue + broken;
        }

        // ---- state ----
        private Draft _draft;
        private Dictionary<string, CheckState> _states;
        private readonly List<SectionView> _sections = new List<SectionView>();

        // ---- UI (built once) ----
        private GameObject _panel;
        private ScrollRect _scroll;
        private RectTransform _content;
        private Text _summaryText;
        private Text _statusLine;
        private Button[] _filterButtons;
        private Button _resetButton;
        private InputField _bugTitle, _bugSteps, _bugExpected, _bugActual;
        private Button[] _formSeverityButtons;
        private Text _formShotsText;
        private Text _bugListHeader;
        private RectTransform _bugListRoot;

        private readonly List<InputField> _inputs = new List<InputField>();
        private readonly List<InputTarget> _inputTargets = new List<InputTarget>();
        private InputTarget _lastTarget; // where a toolbar Screenshot links to (last focused field)

        private bool _open;
        private bool _textLockHeld;
        private bool _shotBusy;

        private bool _dirty;
        private float _saveAt;
        private float _summaryTimer;

        private bool _confirmReset;
        private float _confirmResetUntil;
        private string _confirmBugId;
        private float _confirmBugUntil;

        // ---- FPS (sampled while the panel is CLOSED so UI work never skews it) ----
        private int _fpsFrames;
        private float _fpsTime;
        private float _winTime;
        private int _winFrames;
        private float _minWindowFps = float.MaxValue;
        private float _worstFrameMs;
        private int _hitches;

        private string DraftPath => System.IO.Path.Combine(Application.persistentDataPath, DraftFileName);

        private static double SessionSeconds =>
            _sessionClock != null ? _sessionClock.Elapsed.TotalSeconds : Time.realtimeSinceStartup;

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
            LoadDraft();
        }

        private void OnDestroy()
        {
            if (_dirty) SaveDraft();
            if (_textLockHeld)
            {
                UIInputLock.TextInputActive = false;
                _textLockHeld = false;
            }
            if (_open) UIInputLock.ModalOpen = false;
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            if (_dirty) SaveDraft();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && _dirty) SaveDraft();
        }

        private void Update()
        {
            SampleFps();

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

            // Debounced draft save (runs closed too: a late edit is never lost).
            if (_dirty && Time.unscaledTime >= _saveAt) SaveDraft();

            if (!_open) return;

            // Two-step confirms expire on their own.
            if (_confirmReset && Time.unscaledTime > _confirmResetUntil) CancelResetConfirm();
            if (_confirmBugId != null && Time.unscaledTime > _confirmBugUntil)
            {
                _confirmBugId = null;
                RebuildBugList();
            }

            // Keep the error/FPS numbers in the summary line fresh.
            _summaryTimer += Time.unscaledDeltaTime;
            if (_summaryTimer >= 1f) { _summaryTimer = 0f; RefreshSummary(); }

            // Hold TextInputActive exactly while ANY of our text boxes (notes
            // or the bug form) is focused (console precedent - typing must
            // never hit gameplay).
            bool anyFocused = false;
            for (int i = 0; i < _inputs.Count; i++)
            {
                var input = _inputs[i];
                if (input == null || !input.isFocused) continue;
                anyFocused = true;
                _lastTarget = _inputTargets[i];
                break;
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

            _confirmReset = false;
            _confirmBugId = null;
            UpdateResetLabel();
            RefreshAll();
            RefreshForm();
            RebuildBugList();

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling(); // render above the HUD
            _open = true;
            UIInputLock.ModalOpen = true;

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);

            SetStatusLine("Draft: " + DraftPath);
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
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }

            SaveDraft();
        }

        // ------------------------------------------------------------ Draft

        private void LoadDraft()
        {
            _draft = null;
            string path = DraftPath;
            try
            {
                if (System.IO.File.Exists(path))
                    _draft = JsonUtility.FromJson<Draft>(System.IO.File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("FeedbackUI: could not read the draft (" + e.Message + "); starting a new one.");
                try
                {
                    System.IO.File.Copy(path, System.IO.Path.Combine(Application.persistentDataPath,
                        "feedback_draft_unreadable_" + Stamp() + ".json"), true);
                }
                catch (System.Exception) { /* best effort */ }
            }

            if (_draft == null) _draft = new Draft();
            if (_draft.checks == null) _draft.checks = new List<CheckState>();
            if (_draft.bugs == null) _draft.bugs = new List<Bug>();
            if (_draft.formShots == null) _draft.formShots = new List<string>();
            if (_draft.expanded == null) _draft.expanded = new List<string>();
            if (_draft.sessions == null) _draft.sessions = new List<string>();
            if (string.IsNullOrEmpty(_draft.created)) _draft.created = Now();
            if (_draft.nextBugNo < 1) _draft.nextBugNo = 1;
            if (_draft.filter < 0 || _draft.filter > 2) _draft.filter = 0;
            if (_draft.formSeverity < SevLow || _draft.formSeverity > SevCrash) _draft.formSeverity = SevMed;
            for (int i = 0; i < _draft.bugs.Count; i++)
            {
                if (_draft.bugs[i] == null) { _draft.bugs.RemoveAt(i--); continue; }
                if (_draft.bugs[i].shots == null) _draft.bugs[i].shots = new List<string>();
            }

            // One line per play session that touched this draft (tells the reader
            // which builds the statuses came from).
            _draft.sessions.Add(Now() + "  build " + Application.version + "  unity " + Application.unityVersion
                + (Application.isEditor ? "  editor" : "  player"));
            while (_draft.sessions.Count > 30) _draft.sessions.RemoveAt(0);

            // Index saved states, then make sure every current check has one.
            _states = new Dictionary<string, CheckState>();
            for (int i = 0; i < _draft.checks.Count; i++)
            {
                var s = _draft.checks[i];
                if (s != null && !string.IsNullOrEmpty(s.id)) _states[s.id] = s;
            }

            var all = FeedbackChecklist.All;
            for (int i = 0; i < all.Count; i++)
            {
                var check = all[i];
                CheckState st;
                if (!_states.TryGetValue(check.Id, out st))
                {
                    st = new CheckState { id = check.Id };
                    _draft.checks.Add(st);
                    _states[check.Id] = st;
                }
                if (st.note == null) st.note = "";
                if (st.when == null) st.when = "";
                if (st.build == null) st.build = "";

                // Rows the tester never touched always follow the checklist's
                // current default (a not-built flag flipped later takes effect).
                if (!st.touched && st.note.Length == 0)
                {
                    st.status = DefaultStatus(check);
                    st.severity = SevNone;
                }
                if (st.status < 0 || st.status >= StatusCount) st.status = StUntested;
            }

            MarkDirty(); // persist the session line
        }

        private static int DefaultStatus(FeedbackCheck check) => check.NotBuilt ? StNA : StUntested;

        private void MarkDirty()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + SaveDebounceSeconds;
        }

        private void SaveDraft()
        {
            _dirty = false;
            if (_draft == null) return;

            try
            {
                var expanded = new List<string>();
                for (int i = 0; i < _sections.Count; i++)
                    if (_sections[i].expanded) expanded.Add(_sections[i].name);
                if (_sections.Count > 0) _draft.expanded = expanded;

                _draft.lastSaved = Now();
                string json = JsonUtility.ToJson(_draft, true);
                string path = DraftPath;
                string tmp = path + ".tmp";
                System.IO.File.WriteAllText(tmp, json);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                System.IO.File.Move(tmp, path);

                if (_open && _statusLine != null)
                    _statusLine.text = "Draft saved " + System.DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("FeedbackUI: could not save the draft - " + e.Message);
                if (_open && _statusLine != null) _statusLine.text = "Draft save FAILED: " + e.Message;
            }
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            var root = UIRoot.GetRoot();

            // Near-fullscreen dark panel. Anchored by fraction so it fits 1920x1080
            // and 1280x720 alike (the canvas scales with the screen).
            _panel = new GameObject("QAPanel");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = new Vector2(0.02f, 0.025f);
            panelRt.anchorMax = new Vector2(0.98f, 0.975f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true; // swallow clicks behind the panel

            // Title + summary.
            var title = MakeLabel(panelRt, "Title", "QA Checklist  [F8]", 30, UIStyle.Cream, false, TextAnchor.MiddleLeft);
            Stretch(title.rectTransform, 24f, 8f, 24f, 40f);

            _summaryText = MakeLabel(panelRt, "Summary", "", 18, UIStyle.Grey, false, TextAnchor.MiddleLeft);
            Stretch(_summaryText.rectTransform, 24f, 48f, 24f, 26f);

            BuildToolbar(panelRt);

            _statusLine = MakeLabel(panelRt, "StatusLine", "", 17, UIStyle.Grey, false, TextAnchor.MiddleLeft);
            Stretch(_statusLine.rectTransform, 24f, 126f, 24f, 24f);

            BuildScroll(panelRt);
            BuildBugForm(_content);
            BuildBugListShell(_content);
            BuildSections(_content);

            _panel.SetActive(false);
        }

        private void BuildToolbar(RectTransform panelRt)
        {
            var bar = NewRt("Toolbar", panelRt);
            Stretch(bar, 16f, 78f, 16f, 44f);
            var hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            _filterButtons = new Button[3];
            string[] filterNames = { "All", "Untested", "Issues only" };
            float[] filterWidths = { 80f, 120f, 140f };
            for (int i = 0; i < 3; i++)
            {
                int f = i;
                _filterButtons[i] = MakeButton(bar, filterNames[i], () => SetFilter(f), filterWidths[i], 40f);
            }

            var spacer = NewRt("Spacer", bar);
            Size(spacer, 8f, 40f, 1f);

            MakeButton(bar, "New bug", JumpToNewBug, 130f, 40f, UIStyle.GoldDim);
            MakeButton(bar, "Expand all", () => SetAllExpanded(true), 140f, 40f);
            MakeButton(bar, "Collapse all", () => SetAllExpanded(false), 150f, 40f);
            MakeButton(bar, "Screenshot", () => RequestScreenshot(_lastTarget), 140f, 40f);
            MakeButton(bar, "Export report", OnExport, 170f, 40f, UIStyle.GoldDim);
            _resetButton = MakeButton(bar, "Reset statuses", OnResetClicked, 200f, 40f);
            MakeButton(bar, "Close", Close, 110f, 40f);
        }

        private void BuildScroll(RectTransform panelRt)
        {
            var scrollRt = NewRt("Scroll", panelRt);
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(16f, 16f);
            scrollRt.offsetMax = new Vector2(-16f, -154f);

            var scrollBg = scrollRt.gameObject.AddComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0.25f);
            scrollBg.raycastTarget = true; // scroll wheel needs a target

            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();

            var viewportRt = NewRt("Viewport", scrollRt);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8f, 8f);
            viewportRt.offsetMax = new Vector2(-8f, -8f);
            viewportRt.gameObject.AddComponent<RectMask2D>();

            _content = NewRt("Content", viewportRt);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;

            MakeVLG(_content.gameObject, 8, 8, 8, 40, 10f);
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewportRt;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 50f;
        }

        // ---------------------------------------------------- new-bug form

        private void BuildBugForm(RectTransform content)
        {
            var block = NewRt("NewBug", content);
            var blockBg = block.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(blockBg, new Color(0.20f, 0.17f, 0.12f, 0.98f));
            MakeVLG(block.gameObject, 16, 16, 12, 12, 6f);

            MakeLabel(block, "Header", "NEW BUG", 26, UIStyle.Gold);
            MakeLabel(block, "Hint", "For anything not on the checklist. A context snapshot (time, day, inventory, position, FPS, recent errors) is stamped on automatically.",
                17, UIStyle.Grey);

            var target = new InputTarget { kind = 0, id = "" };
            _lastTarget = target;

            MakeLabel(block, "TitleCaption", "Title", 18, UIStyle.Cream);
            _bugTitle = MakeInput(block, "BugTitle", "One-line summary", false, 44f, 20, target);
            _bugTitle.SetTextWithoutNotify(_draft.formTitle ?? "");
            _bugTitle.onValueChanged.AddListener(v => { _draft.formTitle = v; MarkDirty(); });

            MakeLabel(block, "StepsCaption", "Steps to reproduce", 18, UIStyle.Cream);
            _bugSteps = MakeInput(block, "BugSteps", "1. ...  2. ...", true, 84f, 19, target);
            _bugSteps.SetTextWithoutNotify(_draft.formSteps ?? "");
            _bugSteps.onValueChanged.AddListener(v => { _draft.formSteps = v; MarkDirty(); });

            MakeLabel(block, "ExpectedCaption", "Expected", 18, UIStyle.Cream);
            _bugExpected = MakeInput(block, "BugExpected", "What should have happened", true, 64f, 19, target);
            _bugExpected.SetTextWithoutNotify(_draft.formExpected ?? "");
            _bugExpected.onValueChanged.AddListener(v => { _draft.formExpected = v; MarkDirty(); });

            MakeLabel(block, "ActualCaption", "Actual", 18, UIStyle.Cream);
            _bugActual = MakeInput(block, "BugActual", "What actually happened", true, 64f, 19, target);
            _bugActual.SetTextWithoutNotify(_draft.formActual ?? "");
            _bugActual.onValueChanged.AddListener(v => { _draft.formActual = v; MarkDirty(); });

            // Severity row.
            var sevRow = NewRt("SeverityRow", block);
            MakeHLG(sevRow.gameObject, 8f);
            Size(sevRow, -1f, 38f);
            var sevLabel = MakeLabel(sevRow, "Label", "Severity:", 19, UIStyle.Cream, false, TextAnchor.MiddleLeft);
            Size(sevLabel, 100f, 36f);
            _formSeverityButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int sev = i + 1;
                _formSeverityButtons[i] = MakeButton(sevRow, SeverityNames[sev], () => SetFormSeverity(sev), 100f, 36f);
            }

            _formShotsText = MakeLabel(block, "Shots", "", 16, UIStyle.Grey);

            // Actions.
            var actions = NewRt("Actions", block);
            MakeHLG(actions.gameObject, 8f);
            Size(actions, -1f, 42f);
            MakeButton(actions, "Log bug", OnLogBug, 160f, 40f, UIStyle.GoldDim);
            MakeButton(actions, "Screenshot", () => RequestScreenshot(new InputTarget { kind = 0, id = "" }), 150f, 40f);
            MakeButton(actions, "Clear form", OnClearForm, 140f, 40f);
        }

        private void BuildBugListShell(RectTransform content)
        {
            _bugListHeader = MakeLabel(content, "LoggedBugsHeader", "LOGGED BUGS", 24, UIStyle.Gold);
            _bugListRoot = NewRt("LoggedBugs", content);
            MakeVLG(_bugListRoot.gameObject, 0, 0, 0, 0, 8f);
        }

        private void SetFormSeverity(int sev)
        {
            _draft.formSeverity = sev;
            RefreshForm();
            MarkDirty();
        }

        private void RefreshForm()
        {
            if (_formSeverityButtons == null) return;
            for (int i = 0; i < _formSeverityButtons.Length; i++)
                StyleToggle(_formSeverityButtons[i], _draft.formSeverity == i + 1, SeverityColors[i + 1]);

            if (_formShotsText != null)
                _formShotsText.text = _draft.formShots.Count == 0
                    ? "Screenshots: none attached"
                    : "Screenshots: " + string.Join("  |  ", _draft.formShots);
        }

        private void OnClearForm()
        {
            _draft.formTitle = _draft.formSteps = _draft.formExpected = _draft.formActual = "";
            _draft.formSeverity = SevMed;
            _draft.formShots.Clear();
            _bugTitle.SetTextWithoutNotify("");
            _bugSteps.SetTextWithoutNotify("");
            _bugExpected.SetTextWithoutNotify("");
            _bugActual.SetTextWithoutNotify("");
            RefreshForm();
            MarkDirty();
        }

        private void OnLogBug()
        {
            string title = (_draft.formTitle ?? "").Trim();
            string steps = (_draft.formSteps ?? "").Trim();
            string expected = (_draft.formExpected ?? "").Trim();
            string actual = (_draft.formActual ?? "").Trim();

            if (title.Length == 0 && steps.Length == 0 && actual.Length == 0)
            {
                SetStatusLine("Type a title (or steps / actual) before logging a bug.");
                return;
            }
            if (title.Length == 0)
                title = Truncate(FlattenLine(actual.Length > 0 ? actual : steps, 70), 70);

            var bug = new Bug
            {
                id = "B" + _draft.nextBugNo.ToString("00", CultureInfo.InvariantCulture),
                title = title,
                steps = steps,
                expected = expected,
                actual = actual,
                severity = _draft.formSeverity,
                when = Now(),
                build = Application.version,
                context = CompactContext(),
                shots = new List<string>(_draft.formShots),
            };
            _draft.nextBugNo++;
            _draft.bugs.Add(bug);

            OnClearForm();
            RebuildBugList();
            RefreshSummary();
            SaveDraft();
            SetStatusLine("Logged " + bug.id + " [" + SeverityNames[Mathf.Clamp(bug.severity, 0, 4)] + "] " + bug.title);
        }

        /// <summary>Rebuilds the logged-bug cards (newest first). Small list - cheap.</summary>
        private void RebuildBugList()
        {
            if (_bugListRoot == null) return;

            for (int i = _bugListRoot.childCount - 1; i >= 0; i--)
            {
                var child = _bugListRoot.GetChild(i).gameObject;
                child.SetActive(false); // out of the layout this frame; Destroy lands at end of frame
                Destroy(child);
            }

            _bugListHeader.text = _draft.bugs.Count == 0
                ? "LOGGED BUGS  (none yet)"
                : "LOGGED BUGS  (" + _draft.bugs.Count + ")";

            for (int i = _draft.bugs.Count - 1; i >= 0; i--)
                BuildBugCard(_draft.bugs[i]);

            LayoutRebuilder.MarkLayoutForRebuild(_content);
        }

        private void BuildBugCard(Bug bug)
        {
            int sev = Mathf.Clamp(bug.severity, 0, 4);
            var card = NewRt("Bug_" + bug.id, _bugListRoot);
            var cardBg = card.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(cardBg, Color.Lerp(UIStyle.PanelBgLight, SeverityColors[sev], 0.22f));
            cardBg.raycastTarget = false;
            MakeVLG(card.gameObject, 14, 14, 10, 10, 4f);

            MakeLabel(card, "Head", bug.id + "  [" + SeverityNames[sev] + "]  " + bug.title, 22, UIStyle.Gold);
            MakeLabel(card, "When", "Logged " + bug.when + "  (build " + bug.build + ")", 16, UIStyle.Grey);
            if (!string.IsNullOrEmpty(bug.steps)) MakeLabel(card, "Steps", "Steps: " + bug.steps, 18, UIStyle.Cream);
            if (!string.IsNullOrEmpty(bug.expected)) MakeLabel(card, "Expected", "Expected: " + bug.expected, 18, UIStyle.Cream);
            if (!string.IsNullOrEmpty(bug.actual)) MakeLabel(card, "Actual", "Actual: " + bug.actual, 18, UIStyle.Cream);
            for (int i = 0; i < bug.shots.Count; i++)
                MakeLabel(card, "Shot" + i, "Screenshot: " + bug.shots[i], 16, UIStyle.Grey);
            MakeLabel(card, "Context", "Context: " + bug.context, 15, new Color(0.50f, 0.51f, 0.55f, 1f));

            var row = NewRt("Actions", card);
            MakeHLG(row.gameObject, 8f);
            Size(row, -1f, 38f);

            string id = bug.id;
            MakeButton(row, "Add screenshot", () => RequestScreenshot(new InputTarget { kind = 2, id = id }), 190f, 36f);

            bool confirming = _confirmBugId == id;
            MakeButton(row, confirming ? "Really remove? (click again)" : "Remove",
                () => OnRemoveBug(id), confirming ? 320f : 120f, 36f,
                confirming ? UIStyle.Danger : (Color?)null);
        }

        private void OnRemoveBug(string id)
        {
            if (_confirmBugId != id)
            {
                _confirmBugId = id;
                _confirmBugUntil = Time.unscaledTime + 6f;
                RebuildBugList();
                return;
            }

            _confirmBugId = null;
            for (int i = 0; i < _draft.bugs.Count; i++)
            {
                if (_draft.bugs[i].id != id) continue;
                _draft.bugs.RemoveAt(i);
                break;
            }
            RebuildBugList();
            RefreshSummary();
            MarkDirty();
            SetStatusLine("Removed " + id);
        }

        // ------------------------------------------------------- sections

        private void BuildSections(RectTransform content)
        {
            var sectionNames = FeedbackChecklist.Sections;
            var all = FeedbackChecklist.All;

            for (int s = 0; s < sectionNames.Count; s++)
            {
                var view = new SectionView { name = sectionNames[s] };
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Section == view.name) view.checks.Add(all[i]);
                if (view.checks.Count == 0) continue;

                view.expanded = _draft.expanded.Contains(view.name);

                var captured = view;
                view.header = MakeButton(content, "", () => ToggleSection(captured), -1f, 50f);
                view.headerImage = view.header.GetComponent<Image>();
                view.headerText = view.header.GetComponentInChildren<Text>();
                if (view.headerText != null)
                {
                    view.headerText.alignment = TextAnchor.MiddleLeft;
                    view.headerText.fontSize = 24;
                    view.headerText.rectTransform.offsetMin = new Vector2(18f, 0f);
                }

                view.body = NewRt("Body_" + view.name, content);
                MakeVLG(view.body.gameObject, 18, 0, 0, 6, 8f);

                _sections.Add(view);

                if (view.expanded)
                    BuildSectionRows(view);
                view.body.gameObject.SetActive(view.expanded);
            }
        }

        private void ToggleSection(SectionView s)
        {
            SetExpanded(s, !s.expanded);
            MarkDirty();
        }

        private void SetExpanded(SectionView s, bool expanded)
        {
            s.expanded = expanded;
            if (expanded && !s.built) BuildSectionRows(s);
            s.body.gameObject.SetActive(expanded);
            ApplyFilterToSection(s);
            RefreshSectionHeader(s);
            LayoutRebuilder.MarkLayoutForRebuild(_content);
        }

        private void SetAllExpanded(bool expanded)
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                if (!SectionVisible(_sections[i]) && expanded) continue;
                SetExpanded(_sections[i], expanded);
            }
            MarkDirty();
        }

        private void BuildSectionRows(SectionView s)
        {
            s.built = true;
            for (int i = 0; i < s.checks.Count; i++)
                BuildRow(s, s.checks[i]);
            ApplyFilterToSection(s);
        }

        /// <summary>One checklist row: id+title, steps, expected, status buttons, severity, note.</summary>
        private void BuildRow(SectionView s, FeedbackCheck check)
        {
            var st = _states[check.Id];
            var v = new CheckView { check = check, state = st };

            var rowRt = NewRt("Check_" + check.Id, s.body);
            v.root = rowRt;
            var bg = rowRt.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, check.NotBuilt ? new Color(0.13f, 0.14f, 0.17f, 0.98f) : UIStyle.PanelBgLight);
            bg.raycastTarget = false;
            MakeVLG(rowRt.gameObject, 14, 14, 10, 10, 5f);

            string tag = check.NotBuilt ? "   [NOT BUILT YET]" : (check.Wave2 ? "   [wave 2 - N/A if missing]" : "");
            v.title = MakeLabel(rowRt, "Title", check.Id + "   " + check.Title + tag, 23, UIStyle.Gold);
            MakeLabel(rowRt, "Steps", "TEST:  " + check.Steps, 19, UIStyle.Grey);
            MakeLabel(rowRt, "Expected", "EXPECT:  " + check.Expected, 19, UIStyle.Cream);

            // Status buttons.
            var statusRow = NewRt("Status", rowRt);
            MakeHLG(statusRow.gameObject, 6f);
            Size(statusRow, -1f, 36f);
            v.statusButtons = new Button[StatusCount];
            for (int i = 0; i < StatusCount; i++)
            {
                int status = i;
                v.statusButtons[i] = MakeButton(statusRow, StatusNames[i], () => OnStatusClicked(v, status), 112f, 34f, null, 19);
            }

            // Severity row (only while Issue / Broken).
            var sevRow = NewRt("Severity", rowRt);
            v.severityRow = sevRow.gameObject;
            MakeHLG(sevRow.gameObject, 6f);
            Size(sevRow, -1f, 36f);
            var sevLabel = MakeLabel(sevRow, "Label", "Severity:", 18, UIStyle.Cream, false, TextAnchor.MiddleLeft);
            Size(sevLabel, 96f, 34f);
            v.severityButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int sev = i + 1;
                v.severityButtons[i] = MakeButton(sevRow, SeverityNames[sev], () => OnSeverityClicked(v, sev), 88f, 34f, null, 19);
            }
            var shotGap = NewRt("Gap", sevRow);
            Size(shotGap, 12f, 34f);
            MakeButton(sevRow, "Screenshot", () => RequestScreenshot(new InputTarget { kind = 1, id = check.Id }), 150f, 34f, null, 19);

            // Note box (visible once the row has a status or a note).
            var target = new InputTarget { kind = 1, id = check.Id };
            v.note = MakeInput(rowRt, "Note", "Repro / what you saw / what you expected", true, 78f, 19, target);
            v.noteGo = v.note.gameObject;
            v.note.SetTextWithoutNotify(st.note ?? "");
            v.note.onValueChanged.AddListener(val => { st.note = val; MarkDirty(); });

            s.views.Add(v);
            RefreshRow(v);
        }

        private void OnStatusClicked(CheckView v, int status)
        {
            var st = v.state;
            // Clicking the active status again clears it back to Untested.
            int next = (st.status == status && status != StUntested) ? StUntested : status;

            st.status = next;
            st.touched = true;
            st.when = Now();
            st.build = Application.version;
            if (next == StIssue && st.severity == SevNone) st.severity = SevMed;
            else if (next == StBroken && st.severity == SevNone) st.severity = SevHigh;
            else if (next != StIssue && next != StBroken) st.severity = SevNone;

            RefreshRow(v);
            var section = SectionOf(v.check);
            if (section != null) RefreshSectionHeader(section);
            RefreshSummary();
            LayoutRebuilder.MarkLayoutForRebuild(v.root);
            MarkDirty();
        }

        private void OnSeverityClicked(CheckView v, int sev)
        {
            v.state.severity = v.state.severity == sev ? SevNone : sev;
            v.state.touched = true;
            RefreshRow(v);
            MarkDirty();
        }

        // -------------------------------------------------------- refresh

        private void RefreshRow(CheckView v)
        {
            var st = v.state;
            for (int i = 0; i < v.statusButtons.Length; i++)
                StyleToggle(v.statusButtons[i], st.status == i, StatusColors[i]);

            bool issue = st.status == StIssue || st.status == StBroken;
            v.severityRow.SetActive(issue);
            for (int i = 0; i < v.severityButtons.Length; i++)
                StyleToggle(v.severityButtons[i], st.severity == i + 1, SeverityColors[i + 1]);

            v.noteGo.SetActive(st.status != StUntested || !string.IsNullOrEmpty(st.note));

            switch (st.status)
            {
                case StPass: v.title.color = PassGreen; break;
                case StIssue: v.title.color = WarnAmber; break;
                case StBroken: v.title.color = BadRed; break;
                case StBlocked: v.title.color = new Color(0.75f, 0.65f, 0.95f, 1f); break;
                case StNA: v.title.color = UIStyle.Grey; break;
                default: v.title.color = UIStyle.Gold; break;
            }

            v.root.gameObject.SetActive(Matches(st));
        }

        private void RefreshSectionHeader(SectionView s)
        {
            if (s.headerText == null) return;
            var c = Count(s.checks);
            int applicable = c.total - c.na;

            var sb = new StringBuilder();
            sb.Append(s.expanded ? "[-] " : "[+] ").Append(s.name).Append("   ")
              .Append(c.Tested).Append('/').Append(applicable).Append(" tested");
            if (c.Issues > 0) sb.Append(", ").Append(c.Issues).Append(c.Issues == 1 ? " issue" : " issues");
            if (c.blocked > 0) sb.Append(", ").Append(c.blocked).Append(" blocked");
            if (c.na > 0) sb.Append(", ").Append(c.na).Append(" n/a");
            s.headerText.text = sb.ToString();

            Color tint;
            if (c.Issues > 0) tint = new Color(0.45f, 0.22f, 0.20f, 1f);
            else if (applicable > 0 && c.Tested == applicable) tint = new Color(0.20f, 0.36f, 0.24f, 1f);
            else tint = UIStyle.ButtonBg;
            UIStyle.StyleButton(s.header, tint);
        }

        private void ApplyFilterToSection(SectionView s)
        {
            for (int i = 0; i < s.views.Count; i++)
                s.views[i].root.gameObject.SetActive(Matches(s.views[i].state));
            s.header.gameObject.SetActive(SectionVisible(s));
            s.body.gameObject.SetActive(s.expanded && SectionVisible(s));
        }

        /// <summary>With a filter on, a section with nothing matching hides entirely.</summary>
        private bool SectionVisible(SectionView s)
        {
            if (_draft.filter == 0) return true;
            for (int i = 0; i < s.checks.Count; i++)
                if (Matches(_states[s.checks[i].Id])) return true;
            return false;
        }

        private bool Matches(CheckState st)
        {
            switch (_draft.filter)
            {
                case 1: return st.status == StUntested;
                case 2: return st.status == StIssue || st.status == StBroken || st.status == StBlocked;
                default: return true;
            }
        }

        private void SetFilter(int filter)
        {
            _draft.filter = filter;
            RefreshAll();
            MarkDirty();
        }

        private void RefreshAll()
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                var s = _sections[i];
                for (int k = 0; k < s.views.Count; k++) RefreshRow(s.views[k]);
                ApplyFilterToSection(s);
                RefreshSectionHeader(s);
            }

            for (int i = 0; i < _filterButtons.Length; i++)
                StyleToggle(_filterButtons[i], _draft.filter == i, UIStyle.Gold, true);

            RefreshSummary();
            if (_content != null) LayoutRebuilder.MarkLayoutForRebuild(_content);
        }

        private void RefreshSummary()
        {
            if (_summaryText == null) return;
            var c = Count(FeedbackChecklist.All);
            int applicable = c.total - c.na;
            int errors, warnings, exceptions;
            lock (LogLock) { errors = _errorCount; warnings = _warningCount; exceptions = _exceptionCount; }

            _summaryText.text = "Tested " + c.Tested + "/" + applicable
                + "   Pass " + c.pass + "   Issue " + c.issue + "   Broken " + c.broken
                + "   Blocked " + c.blocked + "   N/A " + c.na + "   Untested " + c.untested
                + "   |   Bugs " + _draft.bugs.Count
                + "   |   Log: " + errors + " err, " + exceptions + " exc, " + warnings + " warn";
        }

        private Counts Count(IReadOnlyList<FeedbackCheck> list)
        {
            var c = new Counts();
            for (int i = 0; i < list.Count; i++) Tally(ref c, list[i]);
            return c;
        }

        private Counts Count(List<FeedbackCheck> list)
        {
            var c = new Counts();
            for (int i = 0; i < list.Count; i++) Tally(ref c, list[i]);
            return c;
        }

        private void Tally(ref Counts c, FeedbackCheck check)
        {
            CheckState st;
            if (!_states.TryGetValue(check.Id, out st)) return;
            c.total++;
            switch (st.status)
            {
                case StPass: c.pass++; break;
                case StIssue: c.issue++; break;
                case StBroken: c.broken++; break;
                case StBlocked: c.blocked++; break;
                case StNA: c.na++; break;
                default: c.untested++; break;
            }
        }

        private SectionView SectionOf(FeedbackCheck check)
        {
            for (int i = 0; i < _sections.Count; i++)
                if (_sections[i].name == check.Section) return _sections[i];
            return null;
        }

        private void JumpToNewBug()
        {
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
            if (_bugTitle != null) _bugTitle.ActivateInputField();
        }

        private void SetStatusLine(string text)
        {
            if (_statusLine != null) _statusLine.text = text;
        }

        // ---------------------------------------------------------- Reset

        private void OnResetClicked()
        {
            if (!_confirmReset)
            {
                _confirmReset = true;
                _confirmResetUntil = Time.unscaledTime + 6f;
                UpdateResetLabel();
                SetStatusLine("Click 'CONFIRM reset' again within 6 seconds to clear every status and note (bugs are kept; the draft is backed up first).");
                return;
            }

            _confirmReset = false;
            UpdateResetLabel();

            // Back the draft up before wiping anything.
            SaveDraft();
            string backup = "(backup failed)";
            try
            {
                backup = System.IO.Path.Combine(Application.persistentDataPath,
                    "feedback_draft_backup_" + Stamp() + ".json");
                System.IO.File.Copy(DraftPath, backup, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("FeedbackUI: draft backup failed - " + e.Message);
            }

            var all = FeedbackChecklist.All;
            for (int i = 0; i < all.Count; i++)
            {
                CheckState st;
                if (!_states.TryGetValue(all[i].Id, out st)) continue;
                st.status = DefaultStatus(all[i]);
                st.severity = SevNone;
                st.note = "";
                st.touched = false;
                st.when = "";
                st.build = "";
            }
            for (int i = 0; i < _sections.Count; i++)
                for (int k = 0; k < _sections[i].views.Count; k++)
                    _sections[i].views[k].note.SetTextWithoutNotify("");

            RefreshAll();
            MarkDirty();
            SetStatusLine("Statuses reset. Backup: " + backup);
        }

        private void CancelResetConfirm()
        {
            _confirmReset = false;
            UpdateResetLabel();
        }

        private void UpdateResetLabel()
        {
            if (_resetButton == null) return;
            var label = _resetButton.GetComponentInChildren<Text>();
            if (label != null) label.text = _confirmReset ? "CONFIRM reset" : "Reset statuses";
            UIStyle.StyleButton(_resetButton, _confirmReset ? UIStyle.Danger : UIStyle.ButtonBg);
            var rt = (RectTransform)_resetButton.transform;
            var le = _resetButton.GetComponent<LayoutElement>();
            if (le != null) le.preferredWidth = _confirmReset ? 210f : 200f;
            LayoutRebuilder.MarkLayoutForRebuild(rt);
        }

        // ------------------------------------------------------ Screenshot

        private void RequestScreenshot(InputTarget target)
        {
            if (_shotBusy) return;
            if (!_open) return;
            StartCoroutine(ShotRoutine(target));
        }

        private IEnumerator ShotRoutine(InputTarget target)
        {
            _shotBusy = true;

            string path = System.IO.Path.Combine(Application.persistentDataPath,
                "feedback_shot_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".png");

            // Hide the panel so the capture shows the game (the HUD stays).
            if (_panel != null) _panel.SetActive(false);
            yield return null;
            yield return new WaitForEndOfFrame();

            bool failed = false;
            try { ScreenCapture.CaptureScreenshot(path); }
            catch (System.Exception e)
            {
                failed = true;
                Debug.LogWarning("FeedbackUI: screenshot failed - " + e.Message);
            }

            // CaptureScreenshot is queued for end of frame; give it two more
            // frames before the panel returns.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            if (_open && _panel != null) _panel.SetActive(true);
            _shotBusy = false;

            if (failed)
            {
                SetStatusLine("Screenshot FAILED - see the console.");
                yield break;
            }

            string where = AttachShot(target, path);
            Debug.Log("Playtest screenshot saved to: " + path);
            SetStatusLine("Screenshot: " + path + "  (linked to " + where + ")");
        }

        /// <summary>Links a screenshot path into the target; returns a short description of where.</summary>
        private string AttachShot(InputTarget target, string path)
        {
            if (target.kind == 1)
            {
                CheckState st;
                if (_states.TryGetValue(target.id, out st))
                {
                    st.note = (string.IsNullOrEmpty(st.note) ? "" : st.note + "\n") + "[screenshot: " + path + "]";
                    st.touched = true;
                    for (int i = 0; i < _sections.Count; i++)
                    {
                        for (int k = 0; k < _sections[i].views.Count; k++)
                        {
                            var v = _sections[i].views[k];
                            if (v.check.Id != target.id) continue;
                            v.note.SetTextWithoutNotify(st.note);
                            RefreshRow(v);
                        }
                    }
                    MarkDirty();
                    return "the note of " + target.id;
                }
            }
            else if (target.kind == 2)
            {
                for (int i = 0; i < _draft.bugs.Count; i++)
                {
                    if (_draft.bugs[i].id != target.id) continue;
                    _draft.bugs[i].shots.Add(path);
                    RebuildBugList();
                    MarkDirty();
                    return "bug " + target.id;
                }
            }

            _draft.formShots.Add(path);
            RefreshForm();
            MarkDirty();
            return "the new-bug form";
        }

        // ---------------------------------------------------------- Export

        private void OnExport()
        {
            SaveDraft();

            int reportable = 0;
            var all = FeedbackChecklist.All;
            for (int i = 0; i < all.Count; i++)
                if (Reportable(_states[all[i].Id])) reportable++;

            if (reportable == 0 && _draft.bugs.Count == 0)
            {
                SetStatusLine("Nothing to export yet - mark a check or log a bug first.");
                return;
            }

            string report = BuildReport();

            string stem = "playtest_feedback_" + Stamp();
            string path = System.IO.Path.Combine(Application.persistentDataPath, stem + ".md");
            for (int n = 2; System.IO.File.Exists(path); n++)
                path = System.IO.Path.Combine(Application.persistentDataPath, stem + "_" + n + ".md");

            try
            {
                System.IO.File.WriteAllText(path, report);
            }
            catch (System.Exception e)
            {
                Debug.LogError("FeedbackUI: could not write the report - " + e.Message);
                SetStatusLine("Export FAILED: " + e.Message);
                return;
            }

            // Make the path impossible to lose: log, panel line, world toast.
            Debug.Log("Playtest feedback report written to: " + path);
            SetStatusLine("Exported: " + path);

            var player = FindPlayer();
            Vector3 pos;
            if (player != null) pos = player.transform.position + Vector3.up * 0.9f;
            else if (Camera.main != null)
                pos = new Vector3(Camera.main.transform.position.x,
                    Camera.main.transform.position.y, 0f);
            else pos = Vector3.zero;
            FloatingText.Show(pos, "Feedback saved", UIStyle.Gold);

            Bleeps.Play(BleepKind.Coin, 0.5f);
        }

        private static bool Reportable(CheckState st) =>
            (st.touched && st.status != StUntested) || !string.IsNullOrWhiteSpace(st.note);

        private string BuildReport()
        {
            var sb = new StringBuilder(16384);
            var all = FeedbackChecklist.All;

            sb.AppendLine("# Playtest feedback report");
            sb.AppendLine();
            sb.AppendLine("- Exported: " + Now());
            sb.AppendLine("- Build: " + Application.version + " | Unity " + Application.unityVersion
                + (Application.isEditor ? " (editor)" : " (player)"));
            sb.AppendLine("- Draft created: " + _draft.created + " | last saved: " + _draft.lastSaved);
            sb.AppendLine("- Play sessions that fed this draft: " + _draft.sessions.Count);
            for (int i = 0; i < _draft.sessions.Count; i++)
                sb.AppendLine("  - " + _draft.sessions[i]);
            sb.AppendLine();

            // ---- summary ----
            var c = Count(all);
            int applicable = c.total - c.na;
            int errors, warnings, exceptions;
            lock (LogLock) { errors = _errorCount; warnings = _warningCount; exceptions = _exceptionCount; }

            int crash = 0, high = 0, med = 0, low = 0;
            for (int i = 0; i < _draft.bugs.Count; i++)
            {
                switch (Mathf.Clamp(_draft.bugs[i].severity, 0, 4))
                {
                    case SevCrash: crash++; break;
                    case SevHigh: high++; break;
                    case SevMed: med++; break;
                    default: low++; break;
                }
            }

            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("- Checks tested: " + c.Tested + " of " + applicable + " applicable (" + c.total + " total)");
            sb.AppendLine("- Pass " + c.pass + " | Issue " + c.issue + " | Broken " + c.broken
                + " | Blocked " + c.blocked + " | N/A " + c.na + " | Untested " + c.untested);
            sb.AppendLine("- Bugs logged: " + _draft.bugs.Count
                + " (Crash " + crash + ", High " + high + ", Med " + med + ", Low " + low + ")");
            sb.AppendLine("- Session log: " + errors + " errors, " + exceptions + " exceptions, " + warnings + " warnings");
            sb.AppendLine();

            // ---- bugs, worst first ----
            sb.AppendLine("## Bugs (" + _draft.bugs.Count + ")");
            sb.AppendLine();
            if (_draft.bugs.Count == 0) sb.AppendLine("(none logged)").AppendLine();
            var bugs = new List<Bug>(_draft.bugs);
            bugs.Sort((a, b) =>
            {
                int bySev = b.severity.CompareTo(a.severity);
                return bySev != 0 ? bySev : string.CompareOrdinal(a.id, b.id);
            });
            for (int i = 0; i < bugs.Count; i++)
            {
                var b = bugs[i];
                sb.AppendLine("### " + b.id + " [" + SeverityNames[Mathf.Clamp(b.severity, 0, 4)] + "] " + b.title);
                sb.AppendLine("- Logged: " + b.when + " (build " + b.build + ")");
                if (!string.IsNullOrEmpty(b.steps)) sb.AppendLine("- Steps: " + Indent(b.steps));
                if (!string.IsNullOrEmpty(b.expected)) sb.AppendLine("- Expected: " + Indent(b.expected));
                if (!string.IsNullOrEmpty(b.actual)) sb.AppendLine("- Actual: " + Indent(b.actual));
                for (int k = 0; k < b.shots.Count; k++) sb.AppendLine("- Screenshot: " + b.shots[k]);
                sb.AppendLine("- Context: " + b.context);
                sb.AppendLine();
            }

            // ---- checks needing attention ----
            var attention = new List<FeedbackCheck>();
            for (int i = 0; i < all.Count; i++)
            {
                var st = _states[all[i].Id];
                if (st.status == StIssue || st.status == StBroken || st.status == StBlocked) attention.Add(all[i]);
            }
            attention.Sort((a, b) =>
            {
                var sa = _states[a.Id]; var sb2 = _states[b.Id];
                int bySev = sb2.severity.CompareTo(sa.severity);
                if (bySev != 0) return bySev;
                int byStatus = AttentionRank(sa.status).CompareTo(AttentionRank(sb2.status));
                return byStatus != 0 ? byStatus : string.CompareOrdinal(a.Id, b.Id);
            });

            sb.AppendLine("## Checks with Issue / Broken / Blocked (" + attention.Count + ")");
            sb.AppendLine();
            if (attention.Count == 0) sb.AppendLine("(none)").AppendLine();
            for (int i = 0; i < attention.Count; i++)
            {
                var check = attention[i];
                var st = _states[check.Id];
                string sev = st.severity > 0 ? " | " + SeverityNames[Mathf.Clamp(st.severity, 0, 4)] : "";
                sb.AppendLine("### [" + StatusNames[st.status] + sev + "] " + check.Id + " - " + check.Title
                    + " (" + check.Section + ")");
                if (!string.IsNullOrEmpty(st.when)) sb.AppendLine("- Tested: " + st.when + " (build " + st.build + ")");
                sb.AppendLine("- How to test: " + check.Steps);
                sb.AppendLine("- Expected: " + check.Expected);
                sb.AppendLine("- Note: " + (string.IsNullOrWhiteSpace(st.note) ? "(none)" : Indent(st.note.Trim())));
                sb.AppendLine();
            }

            // ---- passes ----
            var passes = new List<FeedbackCheck>();
            for (int i = 0; i < all.Count; i++)
                if (_states[all[i].Id].status == StPass && Reportable(_states[all[i].Id])) passes.Add(all[i]);

            sb.AppendLine("## Pass (" + passes.Count + ")");
            sb.AppendLine();
            if (passes.Count == 0) sb.AppendLine("(none)");
            for (int i = 0; i < passes.Count; i++)
            {
                var st = _states[passes[i].Id];
                string note = string.IsNullOrWhiteSpace(st.note) ? "" : " - " + FlattenLine(st.note, 300);
                sb.AppendLine("- " + passes[i].Id + " - " + passes[i].Title + note);
            }
            sb.AppendLine();

            // ---- tester-marked N/A and notes on untested rows (short) ----
            var others = new List<FeedbackCheck>();
            for (int i = 0; i < all.Count; i++)
            {
                var st = _states[all[i].Id];
                if (!Reportable(st)) continue;
                if (st.status == StNA || st.status == StUntested) others.Add(all[i]);
            }
            if (others.Count > 0)
            {
                sb.AppendLine("## N/A or untested with notes (" + others.Count + ")");
                sb.AppendLine();
                for (int i = 0; i < others.Count; i++)
                {
                    var st = _states[others[i].Id];
                    string note = string.IsNullOrWhiteSpace(st.note) ? "" : " - " + FlattenLine(st.note, 300);
                    sb.AppendLine("- [" + StatusNames[st.status] + "] " + others[i].Id + " - " + others[i].Title + note);
                }
                sb.AppendLine();
            }

            // ---- context + error log ----
            sb.AppendLine("## Context at export");
            sb.AppendLine();
            var ctx = CollectContext();
            for (int i = 0; i < ctx.Count; i++)
                sb.AppendLine("- " + ctx[i].Key + ": " + ctx[i].Value);
            sb.AppendLine();

            sb.AppendLine("## Error log (last " + MaxLogEntries + " warnings / errors / exceptions of this session)");
            sb.AppendLine();
            lock (LogLock)
            {
                if (LogBuffer.Count == 0) sb.AppendLine("(nothing captured)");
                for (int i = 0; i < LogBuffer.Count; i++)
                {
                    var e = LogBuffer[i];
                    sb.AppendLine("- " + e.time + " [" + e.type + "]"
                        + (e.count > 1 ? " x" + e.count : "") + " " + e.message);
                    if (!string.IsNullOrEmpty(e.stack)) sb.AppendLine("  at " + e.stack);
                }
            }

            return sb.ToString();
        }

        private static int AttentionRank(int status) =>
            status == StBroken ? 0 : status == StIssue ? 1 : 2;

        // --------------------------------------------------------- Context

        /// <summary>One-line version for the per-bug stamp.</summary>
        private string CompactContext()
        {
            var ctx = CollectContext();
            var sb = new StringBuilder();
            for (int i = 0; i < ctx.Count; i++)
            {
                string key = ctx[i].Key;
                if (key == "residents" || key == "audio diag file" || key == "save file") continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(key).Append('=').Append(ctx[i].Value);
            }

            // The latest captured error rides along: usually the smoking gun.
            lock (LogLock)
            {
                if (LogBuffer.Count > 0)
                {
                    var e = LogBuffer[LogBuffer.Count - 1];
                    sb.Append(" | last log [").Append(e.type).Append("] ").Append(Truncate(e.message, 160));
                }
            }
            return sb.ToString();
        }

        /// <summary>Everything the report knows about the running game. Null-checks everything.</summary>
        private List<Kv> CollectContext()
        {
            var l = new List<Kv>();
            Safe(l, "app", CtxApp);
            Safe(l, "time", CtxClock);
            Safe(l, "progress", CtxProgress);
            Safe(l, "spirits", CtxSpirits);
            Safe(l, "world", CtxWorld);
            Safe(l, "performance", CtxPerformance);
            return l;
        }

        private static void Safe(List<Kv> l, string group, System.Action<List<Kv>> fill)
        {
            try { fill(l); }
            catch (System.Exception e) { l.Add(new Kv(group + " context error", e.GetType().Name + ": " + e.Message)); }
        }

        private static void Put(List<Kv> l, string key, string value) => l.Add(new Kv(key, value));

        private void CtxApp(List<Kv> l)
        {
            Put(l, "real time", Now());
            Put(l, "session length", FormatDuration(SessionSeconds));
            Put(l, "build", Application.version + (Application.isEditor ? " (editor)" : " (player)"));
            Put(l, "unity", Application.unityVersion);
            Put(l, "screen", Screen.width + "x" + Screen.height);
            string savePath = System.IO.Path.Combine(Application.persistentDataPath, "save0.json");
            Put(l, "save file", System.IO.File.Exists(savePath)
                ? "save0.json written " + System.IO.File.GetLastWriteTime(savePath).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "no save0.json");
        }

        private void CtxClock(List<Kv> l)
        {
            var clock = GameClock.Instance;
            var calendar = GameCalendar.Instance;
            var weather = WeatherManager.Instance;

            if (clock != null)
            {
                Put(l, "day", clock.Day + (calendar != null ? " (" + calendar.DateLine + ", season " + calendar.SeasonIndex + ")" : ""));
                Put(l, "hour", clock.TimeString + (clock.IsNight ? " night" : " day") + (clock.FastForward ? " FAST-FORWARD" : ""));
            }
            else
            {
                Put(l, "day", "no GameClock");
            }

            if (weather != null) Put(l, "weather", weather.IsRaining ? "raining" : (weather.IsRainDay ? "rain day (not raining yet)" : "dry"));
            Put(l, "paused", GameManager.Instance != null && GameManager.Instance.IsPaused ? "yes" : "no");
        }

        private void CtxProgress(List<Kv> l)
        {
            var inv = Inventory.Instance;
            if (inv != null)
            {
                var sb = new StringBuilder();
                sb.Append("coin ").Append(inv.Count("coin"));
                foreach (var pair in inv.All)
                {
                    if (pair.Key == "coin" || pair.Value <= 0) continue;
                    sb.Append(", ").Append(pair.Key).Append(' ').Append(pair.Value);
                }
                Put(l, "inventory", sb.ToString());
            }
            else
            {
                Put(l, "inventory", "no Inventory");
            }

            var progress = ShepherdProgress.Instance;
            if (progress != null)
                Put(l, "shepherd level", progress.Level + " (" + progress.XpIntoLevel + "/" + progress.XpForNextLevel
                    + " xp, " + progress.TotalXp + " total)");

            var tools = FindFirstObjectByType<AnimalFarm.Player.ToolController>();
            if (tools != null)
            {
                var sb = new StringBuilder();
                var names = tools.ToolNames;
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(names[i]).Append(" T").Append(tools.GetToolTier(names[i]));
                }
                Put(l, "tools", sb + " (held: " + tools.CurrentToolName + ")");
            }

            var arrivals = VendorArrivals.Instance;
            if (arrivals != null)
                Put(l, "vendor milestones", "tilled " + arrivals.TilesTilled + ", harvested " + arrivals.CropsHarvested
                    + ", built " + arrivals.BuildsPlaced + ", blacksmith " + (arrivals.BlacksmithArrived ? "arrived" : "not yet"));

            var onboarding = OnboardingManager.Instance;
            if (onboarding != null)
                Put(l, "onboarding", onboarding.IsComplete ? "complete" : "step: " + onboarding.CurrentObjective);

            if (GameSettings.Instance != null)
                Put(l, "gentle passage", GameSettings.Instance.GentlePassage ? "on" : "off");
        }

        private void CtxSpirits(List<Kv> l)
        {
            var manager = SpiritManager.Instance;
            if (manager == null) { Put(l, "spirits", "no SpiritManager"); return; }

            int silhouettes = 0, visitors = 0, residents = 0, runaways = 0;
            var names = new StringBuilder();
            var spirits = manager.AllSpirits;
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var a = spirits[i];
                    if (a == null) continue;
                    switch (a.State)
                    {
                        case SpiritState.Silhouette: silhouettes++; break;
                        case SpiritState.Visitor: visitors++; break;
                        case SpiritState.Resident:
                            residents++;
                            break;
                        case SpiritState.Runaway: runaways++; break;
                    }
                    if ((a.State == SpiritState.Resident || a.State == SpiritState.Runaway) && names.Length < 600)
                    {
                        if (names.Length > 0) names.Append(", ");
                        names.Append(string.IsNullOrEmpty(a.GivenName) ? "?" : a.GivenName)
                             .Append('(').Append(a.Species != null ? a.Species.id : "?")
                             .Append(' ').Append(Mathf.RoundToInt(a.Spirit)).Append("%)");
                    }
                }
            }

            Put(l, "spirit counts", "resident " + residents + ", visitor " + visitors
                + ", silhouette " + silhouettes + ", runaway " + runaways);
            if (names.Length > 0) Put(l, "residents", names.ToString());

            var weeds = WeedManager.Instance;
            if (weeds != null && weeds.AllWeeds != null) Put(l, "live weeds", weeds.AllWeeds.Count.ToString());
        }

        private void CtxWorld(List<Kv> l)
        {
            var parcels = ParcelManager.Instance;
            if (parcels != null)
            {
                var owned = new StringBuilder();
                int count = 0;
                for (int i = 0; i < parcels.ParcelCount; i++)
                {
                    if (!parcels.IsUnlocked(i)) continue;
                    count++;
                    if (owned.Length > 0) owned.Append(", ");
                    owned.Append(i).Append(':').Append(parcels.GetInfo(i).name);
                }
                Put(l, "owned parcels", count + "/" + parcels.ParcelCount + " - " + owned);
            }
            else
            {
                Put(l, "owned parcels", "no ParcelManager");
            }

            var biomes = BiomeScorer.Instance;
            if (biomes != null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < biomes.BaseCount; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("base ").Append(i).Append(' ').Append(biomes.GetBiome(i))
                      .Append(' ').Append(biomes.GetBiomeScore(i).ToString("0", CultureInfo.InvariantCulture));
                }
                Put(l, "biomes", sb.ToString());
            }

            var player = FindPlayer();
            if (player != null)
            {
                Vector3 p = player.transform.position;
                Put(l, "player position", "(" + p.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                    + p.y.ToString("0.0", CultureInfo.InvariantCulture) + ")");
            }
            else
            {
                Put(l, "player position", "no Player object");
            }
        }

        private void CtxPerformance(List<Kv> l)
        {
            if (_fpsFrames > 0 && _fpsTime > 0f)
            {
                Put(l, "fps average", (_fpsFrames / _fpsTime).ToString("0.0", CultureInfo.InvariantCulture)
                    + " over " + FormatDuration(_fpsTime) + " (sampled with this panel closed)");
                Put(l, "fps min (1s window)", _minWindowFps < float.MaxValue
                    ? _minWindowFps.ToString("0.0", CultureInfo.InvariantCulture) : "n/a");
                Put(l, "worst frame", _worstFrameMs.ToString("0", CultureInfo.InvariantCulture) + " ms, "
                    + _hitches + " frames over 100 ms");
            }
            else
            {
                Put(l, "fps", "not sampled yet");
            }

            Put(l, "audio", "sfx " + Bleeps.SfxVolume.ToString("0.00", CultureInfo.InvariantCulture)
                + ", music " + AmbientMusic.MusicVolume.ToString("0.00", CultureInfo.InvariantCulture)
                + (Bleeps.Muted ? ", MUTED" : "") + (AudioGuard.Killed ? ", KILLED (Ctrl+M)" : "")
                + ", peak " + AudioLimiter.SessionMaxPeak.ToString("0.00", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(AudioGuard.DiagPath)) Put(l, "audio diag file", AudioGuard.DiagPath);
        }

        private void SampleFps()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // Closed-panel gameplay only: building rows or laying out the
            // panel would otherwise read as a hitch.
            if (_open || _shotBusy)
            {
                _winTime = 0f;
                _winFrames = 0;
                return;
            }
            if (SessionSeconds < FpsWarmupSeconds) return; // scene-load frames are not gameplay
            if (dt > 5f) return;                            // focus loss / debugger break

            _fpsTime += dt;
            _fpsFrames++;
            float ms = dt * 1000f;
            if (ms > _worstFrameMs) _worstFrameMs = ms;
            if (dt > 0.1f) _hitches++;

            _winTime += dt;
            _winFrames++;
            if (_winTime >= 1f)
            {
                float fps = _winFrames / _winTime;
                if (fps < _minWindowFps) _minWindowFps = fps;
                _winTime = 0f;
                _winFrames = 0;
            }
        }

        // ------------------------------------------------------- UI helpers

        private static RectTransform NewRt(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Top-anchored full-width strip: left/right margins, distance from top, height.</summary>
        private static void Stretch(RectTransform rt, float left, float top, float right, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -(top + height));
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static Text MakeLabel(Transform parent, string name, string text, int size, Color color,
            bool wrap = true, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var t = UIRoot.MakeText(parent, name, size, anchor, color);
            t.text = text;
            t.supportRichText = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static LayoutElement Size(Component c, float width, float height, float flexibleWidth = -1f)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) le.preferredWidth = width;
            if (height >= 0f) le.preferredHeight = height;
            if (flexibleWidth >= 0f) le.flexibleWidth = flexibleWidth;
            return le;
        }

        private static VerticalLayoutGroup MakeVLG(GameObject go, int left, int right, int top, int bottom, float spacing)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(left, right, top, bottom);
            v.spacing = spacing;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        private static HorizontalLayoutGroup MakeHLG(GameObject go, float spacing)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        /// <summary>
        /// Styled button with a layout size. width &lt; 0 = stretch (the parent
        /// layout group decides).
        /// </summary>
        private static Button MakeButton(Transform parent, string label, UnityAction onClick,
            float width, float height, Color? bg = null, int fontSize = 20)
        {
            var b = UIStyle.MakeButton(parent, label, onClick, fontSize, bg);
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None; // arrow keys never walk the selection around
            b.navigation = nav;
            Size(b, width, height);
            return b;
        }

        /// <summary>Selected = bright background, unselected = dim.</summary>
        private static void StyleToggle(Button b, bool selected, Color selectedColor, bool darkText = false)
        {
            if (b == null) return;
            UIStyle.StyleButton(b, selected ? selectedColor : UIStyle.ButtonBg);
            var label = b.GetComponentInChildren<Text>();
            if (label != null)
                label.color = selected ? (darkText ? DarkLabel : UIStyle.Cream) : UIStyle.Grey;
        }

        /// <summary>
        /// Legacy InputField (bg image + child text + placeholder) registered
        /// with the focus scan so TextInputActive is held while it is focused.
        /// </summary>
        private InputField MakeInput(Transform parent, string name, string placeholderText, bool multiline,
            float height, int fontSize, InputTarget target)
        {
            var inputRt = NewRt(name, parent);
            var inputBg = inputRt.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(inputBg, new Color(0.08f, 0.09f, 0.12f, 1f));
            Size(inputRt, -1f, height);

            var anchor = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            var inputText = UIRoot.MakeText(inputRt, "Text", fontSize, anchor, UIStyle.Cream);
            inputText.supportRichText = false;
            inputText.raycastTarget = true;
            inputText.horizontalOverflow = multiline ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            inputText.verticalOverflow = VerticalWrapMode.Truncate;
            var textRt = inputText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 6f);
            textRt.offsetMax = new Vector2(-10f, -6f);

            var placeholder = UIRoot.MakeText(inputRt, "Placeholder", fontSize, anchor,
                new Color(0.62f, 0.63f, 0.66f, 0.5f));
            placeholder.text = placeholderText;
            placeholder.fontStyle = FontStyle.Italic;
            var phRt = placeholder.rectTransform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(10f, 6f);
            phRt.offsetMax = new Vector2(-10f, -6f);

            var input = inputRt.gameObject.AddComponent<InputField>();
            input.targetGraphic = inputBg;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.characterLimit = multiline ? NoteCharacterLimit : SingleLineLimit;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            // uGUI reads keys through the EventSystem's input module, which
            // SetGameplayBlocked doesn't touch - typing works while blocked.

            _inputs.Add(input);
            _inputTargets.Add(target);
            return input;
        }

        // ------------------------------------------------------ misc helpers

        private static GameObject FindPlayer()
        {
            try { return GameObject.FindWithTag("Player"); }
            catch (System.Exception) { return null; } // tag not defined in this project state
        }

        private static string Now() =>
            System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static string Stamp() =>
            System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        private static string FormatDuration(double seconds)
        {
            int total = Mathf.Max(0, (int)seconds);
            int h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            return h > 0 ? h + "h" + m.ToString("00") + "m" + s.ToString("00") + "s"
                : m + "m" + s.ToString("00") + "s";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        /// <summary>Collapses newlines/tabs to spaces and limits length (log lines, one-line notes).</summary>
        private static string FlattenLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string flat = s.Replace("\r", "").Replace("\n", " / ").Replace("\t", " ").Trim();
            return Truncate(flat, max);
        }

        private static string FirstStackLine(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var lines = stack.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0) return Truncate(line, 160);
            }
            return "";
        }

        /// <summary>Keeps multi-line text inside one markdown bullet.</summary>
        private static string Indent(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", "").Replace("\n", "\n  ");
        }
    }
}

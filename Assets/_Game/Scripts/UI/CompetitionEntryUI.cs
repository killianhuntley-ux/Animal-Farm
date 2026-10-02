using System.Collections.Generic;
using System.Text;
using AnimalFarm.Competitions;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The competition notice modal. Competitions are HELD (owner verdict
    /// 2026-10-01), so this is a read-only schedule: the next festival events on
    /// the underworld calendar, who is competing (placeholder rival shepherds and
    /// their spirits), and which of the player's residents "could enter". There is
    /// no entry path - the competition code itself stays reachable through the
    /// debug console ('compete'). Gameplay input is blocked while open
    /// (DebugConsole pattern). The panel shell is built once; the rows are
    /// rebuilt on every Open.
    /// </summary>
    public class CompetitionEntryUI : MonoBehaviour
    {
        public static CompetitionEntryUI Instance { get; private set; }

        /// <summary>Difficulty of the most recent debug-console run (0..2). The board reads this for prizes.</summary>
        public static int LastDifficulty;

        private const int UpcomingShown = 3;
        private const int ResidentsShown = 4;

        private static readonly string[] DifficultyNames = { "Gentle Slope", "Proper Hill", "The Mountain" };
        private static readonly string[] SprintDifficultyNames = { "Easy Stroll", "Brisk Dash", "Breakneck" };

        /// <summary>Difficulty labels keyed by the event format ("Sprint" has its own set).</summary>
        private static string[] NamesFor(string format) =>
            format == "Sprint" ? SprintDifficultyNames : DifficultyNames;

        /// <summary>Entry fee in obols per difficulty (slice 09 economy). Same for both formats.</summary>
        private static readonly int[] EntryFees = { 5, 10, 20 };

        /// <summary>The entry fee for a difficulty tier (the board stores it with the entry for refunds).</summary>
        public static int FeeFor(int difficulty) => EntryFees[Mathf.Clamp(difficulty, 0, EntryFees.Length - 1)];

        private GameObject _panel;
        private RectTransform _content; // rows rebuilt each Open
        private Text _title;
        private Text _status;           // transient feedback line ("needs N obols")
        private bool _open;
        private bool _holdingLock;      // we set ModalOpen + blocked gameplay

        // entry mode (festival day)
        private bool _entryMode;
        private SpiritAgent _entrySpirit;
        private int _entryIndex;
        private CompetitionBoard _entryBoard;
        private int _selectedDifficulty;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the read-only notices page (no-op while an event runs).</summary>
        public void Open()
        {
            if (_open) return;
            _entryMode = false;
            Show();
        }

        /// <summary>
        /// Opens the festival entry page for one following spirit: the scheduled event
        /// fixes the format, the player picks a difficulty and pays the fee.
        /// </summary>
        public void OpenEntry(SpiritAgent spirit, int eventIndex, CompetitionBoard board)
        {
            if (_open || spirit == null || board == null) return;
            _entryMode = true;
            _entrySpirit = spirit;
            _entryIndex = eventIndex;
            _entryBoard = board;
            _selectedDifficulty = 0;
            Show();
        }

        private void Show()
        {
            if (CompetitionManager.Instance == null || CompetitionManager.Instance.EventRunning) return;
            if (_panel == null) BuildPanel();

            RebuildContent();

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling(); // render above the HUD
            _open = true;

            if (!_holdingLock)
            {
                _holdingLock = true;
                UIInputLock.ModalOpen = true;
            }
            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            _entrySpirit = null;
            _entryBoard = null;
            if (_panel != null) _panel.SetActive(false);
            ReleaseLock();
        }

        /// <summary>Gives input back, unless the pause menu, a ceremony or a text field still needs it blocked.</summary>
        private void ReleaseLock()
        {
            if (_holdingLock)
            {
                _holdingLock = false;
                // A running ceremony owns the modal flag and the input block.
                if (!UIInputLock.CeremonyActive) UIInputLock.ModalOpen = false;
            }

            if (GameInput.Instance != null)
            {
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.AnyOwnerHolds) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        private void ConfirmEntry()
        {
            var spirit = _entrySpirit;
            var board = _entryBoard;
            int difficulty = _selectedDifficulty;
            int eventIndex = _entryIndex;
            if (spirit == null || board == null || spirit.State != SpiritState.Resident) { Close(); return; }

            // It may have wandered off while the page was open: no fee, keep the page.
            if (!spirit.IsFollowing)
            {
                if (_status != null)
                {
                    _status.text = "(it wandered off - bring it back)";
                    _status.color = UIStyle.Danger;
                }
                return;
            }

            // Entry fee first (slice 09 economy): no obols, no entry.
            int fee = FeeFor(difficulty);
            if (Inventory.Instance == null || !Inventory.Instance.Consume("coin", fee))
            {
                if (_status != null)
                {
                    _status.text = "(needs " + fee + " obols)";
                    _status.color = UIStyle.Danger;
                }
                return; // stay open so the player can pick a cheaper tier
            }

            // Close (and restore input) FIRST - the manager re-blocks it itself.
            Close();
            if (!board.BeginFestivalEntry(spirit, eventIndex, difficulty))
                Inventory.Instance.Add("coin", fee); // could not start: refund
        }

        // ------------------------------------------------------------------ UI

        private void BuildPanel()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("CompetitionEntryUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(760f, 120f); // height grows via ContentSizeFitter

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true;

            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = UIRoot.MakeText(panelRt, "Title", 34, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Competition Notices";
            _title = title;
            title.rectTransform.sizeDelta = new Vector2(0f, 48f);

            // Rows live in a nested column so the shell survives rebuilds.
            _content = new GameObject("Content").AddComponent<RectTransform>();
            _content.SetParent(panelRt, false);

            var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 8f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentFitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _panel.SetActive(false);
        }

        private void RebuildContent()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
            _status = null;

            if (_entryMode)
            {
                RebuildEntry();
                return;
            }

            if (_title != null) _title.text = "Competition Notices";

            // A festival day (entries open, result, or the entry window times) replaces the held line.
            var board = CompetitionBoard.Instance;
            string today = board != null ? board.TodayStatusLine() : null;
            if (!string.IsNullOrEmpty(today))
                AddLine(today, 20, UIStyle.Gold, 52f, TextAnchor.MiddleCenter);
            else
                AddLine(CompetitionSchedule.HeldLine, 20, UIStyle.Gold, 28f, TextAnchor.MiddleCenter);

            var cal = GameCalendar.Instance;
            if (cal != null)
                AddLine("Today: " + cal.DateLine, 18, UIStyle.Grey, 24f, TextAnchor.MiddleCenter);

            BuildUpcoming();
            BuildCouldEnter();
            MakeButton(_content, "Close", 56f, false, Close);
        }

        /// <summary>Festival entry page: event, spirit, difficulty, fee, Enter / Never mind.</summary>
        private void RebuildEntry()
        {
            var ev = CompetitionSchedule.Get(_entryIndex);
            if (_title != null) _title.text = ev.name;

            var cal = GameCalendar.Instance;
            string when = cal != null ? cal.DateLine : "today";
            AddLine(ev.format + " - " + when, 20, UIStyle.Gold, 28f, TextAnchor.MiddleCenter);

            string species = _entrySpirit != null && _entrySpirit.Species != null ? _entrySpirit.Species.displayName : "Spirit";
            string name = _entrySpirit != null && !string.IsNullOrEmpty(_entrySpirit.GivenName) ? _entrySpirit.GivenName : species;
            AddLine("Entering: " + name + " (" + species + ", Spirit "
                + (_entrySpirit != null ? Mathf.RoundToInt(_entrySpirit.Spirit) : 0) + "%)",
                20, UIStyle.Cream, 28f, TextAnchor.MiddleCenter);

            int obols = Inventory.Instance != null ? Inventory.Instance.Count("coin") : 0;
            AddLine("Obols: " + obols, 19, UIStyle.Grey, 26f, TextAnchor.MiddleCenter);

            // difficulty toggles
            var row = new GameObject("DifficultyRow").AddComponent<RectTransform>();
            row.SetParent(_content, false);
            row.sizeDelta = new Vector2(0f, 46f);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            var diffNames = NamesFor(ev.format);
            for (int i = 0; i < diffNames.Length; i++)
            {
                int diff = i; // capture for the click closure
                MakeButton(row, diffNames[i], 46f, diff == _selectedDifficulty,
                    () => { _selectedDifficulty = diff; RebuildContent(); }, 19);
            }

            int fee = EntryFees[Mathf.Clamp(_selectedDifficulty, 0, EntryFees.Length - 1)];
            AddLine("Entry: " + fee + " obols", 19, UIStyle.Grey, 26f, TextAnchor.MiddleCenter);

            _status = UIRoot.MakeText(_content, "Status", 20, TextAnchor.MiddleCenter, UIStyle.Danger);
            _status.text = "";
            _status.rectTransform.sizeDelta = new Vector2(0f, 26f);

            MakeButton(_content, "Enter " + name, 56f, true, ConfirmEntry);
            MakeButton(_content, "Never mind", 56f, false, Close);
        }

        /// <summary>One text row; the caller passes a height that fits its line count.</summary>
        private void AddLine(string text, int size, Color color, float height,
            TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var t = UIRoot.MakeText(_content, "Line", size, anchor, color);
            t.text = text;
            t.rectTransform.sizeDelta = new Vector2(0f, height);
        }

        private void BuildUpcoming()
        {
            var next = CompetitionSchedule.NextEvents(UpcomingShown);
            if (next.Count == 0)
            {
                AddLine("(the festival calendar is not posted yet)", 20, UIStyle.Grey, 30f, TextAnchor.MiddleCenter);
                return;
            }

            AddLine("Coming up", 22, UIStyle.Cream, 30f);

            string gold = ColorUtility.ToHtmlStringRGB(UIStyle.Gold);
            string grey = ColorUtility.ToHtmlStringRGB(UIStyle.Grey);
            for (int i = 0; i < next.Count; i++)
            {
                var ev = next[i];
                var sb = new StringBuilder();
                sb.Append("<color=#").Append(gold).Append('>').Append(ev.festival.name)
                  .Append("</color>  (").Append(ev.festival.format).Append(")\n");
                sb.Append(ev.seasonName).Append(", Day ").Append(ev.festival.dayOfSeason)
                  .Append(" - ").Append(CompetitionSchedule.CountdownText(ev.daysUntil)).Append('\n');
                for (int r = 0; r < CompetitionSchedule.RivalsPerEvent; r++)
                {
                    var rival = CompetitionSchedule.GetRival(ev.index, r);
                    sb.Append("  ").Append(rival.shepherd).Append(" & ").Append(rival.species)
                      .Append(" <color=#").Append(grey).Append(">- ").Append(rival.flavor).Append("</color>");
                    if (r < CompetitionSchedule.RivalsPerEvent - 1) sb.Append('\n');
                }
                AddLine(sb.ToString(), 18, UIStyle.Cream, 24f * (2 + CompetitionSchedule.RivalsPerEvent));
            }
        }

        private void BuildCouldEnter()
        {
            var manager = SpiritManager.Instance;
            var spirits = manager != null ? manager.AllSpirits : null;

            var lines = new List<string>();
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent == null || agent.State != SpiritState.Resident) continue;

                    string species = agent.Species != null ? agent.Species.displayName : "Spirit";
                    string name = !string.IsNullOrEmpty(agent.GivenName) ? agent.GivenName : species;
                    lines.Add(name + " (" + species + ", Spirit " + Mathf.RoundToInt(agent.Spirit) + "%)");
                }
            }

            AddLine("Your residents who could enter", 22, UIStyle.Cream, 30f);

            if (lines.Count == 0)
            {
                AddLine("  (no residents yet)", 18, UIStyle.Grey, 24f);
                return;
            }

            int shown = Mathf.Min(lines.Count, ResidentsShown);
            for (int i = 0; i < shown; i++)
                AddLine("  " + lines[i], 18, UIStyle.Cream, 24f);
            if (lines.Count > shown)
                AddLine("  ...and " + (lines.Count - shown) + " more", 18, UIStyle.Grey, 24f);
        }

        private static void MakeButton(Transform parent, string label, float height, bool gold,
            UnityEngine.Events.UnityAction onClick, int fontSize = 24)
        {
            var go = new GameObject("Button_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, height);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button, gold ? UIStyle.GoldDim : (Color?)null);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", fontSize, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
        }
    }
}

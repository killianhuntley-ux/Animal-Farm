using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Interaction;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Competitions
{
    /// <summary>
    /// The competition notice board (slice 05). Two modes (owner verdicts
    /// 2026-10-01):
    ///   NOTICES - competitions are held, so the board is a read-only schedule
    ///             (CompetitionEntryUI): what is coming up and who is competing.
    ///   ENTRY   - on a scheduled festival day (08:00-18:00, ASSUMPTION) the board
    ///             lets you bring a resident along (the follow mechanic) and enter
    ///             it in that day's event; the scheduled event fixes the format
    ///             (Boulder Trial or Sprint). One entry per event per day, saved,
    ///             and the result is shown on the board for the rest of the day.
    /// The board also listens for finished events (festival and console runs)
    /// and pays out fame, morale and prizes. SAVE ("competitionboard"): the
    /// entry day/slot and the result line.
    /// </summary>
    public class CompetitionBoard : MonoBehaviour, IInteractable, ISelectable, ISaveable
    {
        public static CompetitionBoard Instance { get; private set; }

        private const float FocusScale = 1.06f;

        private static readonly Color PrizeGold = new Color(0.95f, 0.80f, 0.35f, 1f);
        private static readonly Color ResultCream = new Color(0.95f, 0.95f, 0.92f, 1f);

        [SerializeField] private Sprite boardSprite;
        [SerializeField] private Material spriteMaterial;

        private Vector3 _baseScale = Vector3.one;
        private CompetitionManager _subscribedTo; // the instance we hooked (tracked, not a bool)

        // festival entry (saved)
        private int _enteredDay = -1;      // GameClock.Day of the entry, -1 = none
        private int _enteredIndex = -1;    // CompetitionSchedule slot entered
        private string _resultText = "";   // filled when the event finishes
        private int _runningIndex = -1;    // festival run in flight (not saved)
        private int _enteredFee;           // obols paid with the entry (saved; refunded if the run is aborted)
        private bool _abortCheckPending;   // a save was restored with an unfinished entry: judge it next Update

        private enum BoardMode { Notices, Entry, Result }

        private void Awake()
        {
            if (Instance != null && Instance != this) return;
            Instance = this;
        }

        private void Start()
        {
            _baseScale = transform.localScale;

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = boardSprite;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.4f, 1.6f);

            WorldLabel.Attach(gameObject, "Competition Board", -1.0f);

            TrySubscribe();
        }

        private void Update()
        {
            // The manager may awake after us (or be replaced); follow the live instance.
            TrySubscribe();

            // A save restored mid-event: the run is gone, so the entry is void (refund, re-enter).
            // Judged here, not in Restore, because the clock and inventory restore in any order.
            if (_abortCheckPending) ResolveAbortedEntry();
        }

        private void ResolveAbortedEntry()
        {
            _abortCheckPending = false;
            var manager = CompetitionManager.Instance;
            if (manager != null && manager.EventRunning) return; // a real run is in progress
            if (!EnteredToday || !string.IsNullOrEmpty(_resultText)) return;

            if (_enteredFee > 0 && Inventory.Instance != null)
            {
                Inventory.Instance.Add("coin", _enteredFee);
                FloatingText.Show(transform.position + Vector3.up * 0.8f,
                    "(entry fee refunded - the run was interrupted)", ResultCream);
            }
            _enteredDay = -1;
            _enteredIndex = -1;
            _enteredFee = 0;
            _runningIndex = -1;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!ReferenceEquals(_subscribedTo, null)) _subscribedTo.EventFinished -= HandleFinished;
            _subscribedTo = null;
        }

        private void TrySubscribe()
        {
            var mgr = CompetitionManager.Instance;
            if (ReferenceEquals(mgr, _subscribedTo)) return;
            if (!ReferenceEquals(_subscribedTo, null)) _subscribedTo.EventFinished -= HandleFinished;
            _subscribedTo = mgr;
            if (mgr != null) mgr.EventFinished += HandleFinished;
        }

        // ------------------------------------------------------------ Results

        private void HandleFinished(SpiritAgent spirit, CompetitionResult result)
        {
            RecordFestivalResult(spirit, result);
            if (spirit == null) return;

            spirit.RecordCompetition(result.Won);

            // Morale swing. SpiritAgent has no public morale setter besides the
            // debug one, so we lean on Debug_SetSpirit (it clamps internally too).
            if (result.Won)
                spirit.Debug_SetSpirit(Mathf.Min(100f, spirit.Spirit + 25f));
            else
                spirit.Debug_SetSpirit(Mathf.Max(0f, spirit.Spirit - 10f));

            if (result.Won) AwardPrize();

            string who = !string.IsNullOrEmpty(spirit.GivenName) ? spirit.GivenName
                : spirit.Species != null ? spirit.Species.displayName
                : "Spirit";
            string summary = result.Won
                ? who + " won the " + result.eventName + "!"
                : who + " placed " + result.placement + "/" + result.entrants;
            FloatingText.Show(spirit.transform.position + Vector3.up * 0.8f, summary,
                result.Won ? PrizeGold : ResultCream);
        }

        private void AwardPrize()
        {
            if (Inventory.Instance == null) return;

            // Prize purses (slice 09 economy): obols, scaled by difficulty.
            // Same purse for every event; the entry fee was already paid in
            // CompetitionEntryUI.
            int obols;
            switch (Mathf.Clamp(CompetitionEntryUI.LastDifficulty, 0, 2))
            {
                case 0: obols = 15; break;
                case 1: obols = 40; break;
                default: obols = 100; break;
            }

            Inventory.Instance.Add("coin", obols);
            FloatingText.Show(transform.position + Vector3.up * 0.8f,
                "Prize: " + obols + " obols", PrizeGold);
        }

        // ------------------------------------------------------------ Festival

        private static int Today => GameClock.Instance != null ? GameClock.Instance.Day : 1;

        private bool EnteredToday => _enteredDay >= 0 && _enteredDay == Today;

        private BoardMode Mode(out int index)
        {
            index = -1;
            if (EnteredToday) { index = _enteredIndex; return BoardMode.Result; }
            if (CompetitionSchedule.EntriesOpenNow(out index)) return BoardMode.Entry;
            return BoardMode.Notices;
        }

        /// <summary>One line for the notices page about today's festival state, or null.</summary>
        public string TodayStatusLine()
        {
            var mode = Mode(out int idx);
            if (mode == BoardMode.Result)
                return string.IsNullOrEmpty(_resultText)
                    ? "Today: your entry is in the " + CompetitionSchedule.Get(idx).name + "."
                    : "Today's result: " + _resultText;
            if (mode == BoardMode.Entry)
                return CompetitionSchedule.Get(idx).name + " - entries open! Bring a spirit along and use the board.";
            if (CompetitionSchedule.TryGetToday(out int today))
                return "Today: " + CompetitionSchedule.Get(today).name + " - entries "
                    + CompetitionSchedule.WindowOpenHour.ToString("0") + ":00 to "
                    + CompetitionSchedule.WindowCloseHour.ToString("0") + ":00.";
            return null;
        }

        /// <summary>Residents that are currently following the shepherd (the entry candidates).</summary>
        private static List<SpiritAgent> Followers()
        {
            var list = new List<SpiritAgent>();
            var mgr = SpiritManager.Instance;
            if (mgr == null) return list;
            var all = mgr.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a != null && a.State == SpiritState.Resident && a.IsFollowing) list.Add(a);
            }
            return list;
        }

        private static string NameOf(SpiritAgent a) =>
            !string.IsNullOrEmpty(a.GivenName) ? a.GivenName
            : a.Species != null ? a.Species.displayName : "Spirit";

        /// <summary>
        /// Starts today's scheduled event with the spirit (called by the entry modal
        /// once the fee is paid). Marks the entry first so a reload cannot re-enter.
        /// </summary>
        public bool BeginFestivalEntry(SpiritAgent spirit, int eventIndex, int difficulty)
        {
            var manager = CompetitionManager.GetOrCreate();
            if (manager == null || manager.EventRunning || spirit == null) return false;
            if (EnteredToday) return false;

            var ev = CompetitionSchedule.Get(eventIndex);
            _enteredDay = Today;
            _enteredIndex = eventIndex;
            _enteredFee = CompetitionEntryUI.FeeFor(difficulty);
            _resultText = "";
            _runningIndex = eventIndex;
            CompetitionEntryUI.LastDifficulty = Mathf.Clamp(difficulty, 0, 2);

            if (ev.format == "Boulder Trial") manager.StartBoulderTrial(spirit, difficulty);
            else manager.StartCrossing(spirit, difficulty);
            return true;
        }

        private void RecordFestivalResult(SpiritAgent spirit, CompetitionResult result)
        {
            if (_runningIndex < 0) return;
            var ev = CompetitionSchedule.Get(_runningIndex);
            string who = spirit != null ? NameOf(spirit) : "Your spirit";
            if (result.Won)
            {
                _resultText = who + " won the " + ev.name + "!";
            }
            else
            {
                var rival = CompetitionSchedule.GetRival(_runningIndex, 0);
                _resultText = who + " placed " + result.placement + "/" + result.entrants + " in the "
                    + ev.name + "; " + rival.shepherd + " & " + rival.species + " took it.";
            }
            _runningIndex = -1;
            _enteredFee = 0; // the run completed: nothing to refund any more
        }

        // ------------------------------------------------------- IInteractable

        public string PromptText
        {
            get
            {
                var mode = Mode(out int idx);
                if (mode == BoardMode.Entry) return CompetitionSchedule.Get(idx).name + " - entries open!";
                if (mode == BoardMode.Result) return "Today's result";
                return "Read the notices";
            }
        }

        public bool CanInteract(GameObject actor) =>
            CompetitionManager.Instance != null && !CompetitionManager.Instance.EventRunning;

        public void Interact(GameObject actor)
        {
            if (!CanInteract(actor)) return;
            OpenNotices();
        }

        private void OpenNotices()
        {
            if (CanInteract(gameObject) && CompetitionEntryUI.Instance != null)
                CompetitionEntryUI.Instance.Open();
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "Competition Board";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;

            var mode = Mode(out int idx);
            if (mode == BoardMode.Entry)
            {
                var ev = CompetitionSchedule.Get(idx);
                // Parenthesised labels are non-selectable info rows (InteractMenu.IsInfoRow).
                into.Add(new SelectAction("(" + ev.name + " - entries open!)", () => { }, false));

                var followers = Followers();
                if (followers.Count == 0)
                {
                    into.Add(new SelectAction("(bring a spirit along to enter)", () => { }, false));
                }
                else
                {
                    for (int i = 0; i < followers.Count; i++)
                    {
                        var picked = followers[i]; // capture for the closure
                        into.Add(new SelectAction("Enter " + NameOf(picked), () => OpenEntry(picked, idx)));
                    }
                }
            }
            else if (mode == BoardMode.Result)
            {
                string line = string.IsNullOrEmpty(_resultText)
                    ? "your entry is in the " + CompetitionSchedule.Get(idx).name
                    : _resultText;
                into.Add(new SelectAction("(" + line + ")", () => { }, false));
            }

            into.Add(new SelectAction("Read the notices", OpenNotices));
        }

        private void OpenEntry(SpiritAgent spirit, int eventIndex)
        {
            if (!CanInteract(gameObject) || CompetitionEntryUI.Instance == null) return;
            CompetitionEntryUI.Instance.OpenEntry(spirit, eventIndex, this);
        }

        /// <summary>Console: forget the entry so the board can be used again today.</summary>
        public void Debug_ResetEntry() { _enteredDay = -1; _enteredIndex = -1; _enteredFee = 0; _resultText = ""; _runningIndex = -1; }

        // ------------------------------------------------------------ ISaveable

        [Serializable]
        private struct BoardState
        {
            public int enteredDay;
            public int enteredIndex;
            public int enteredFee;
            public string resultText;
        }

        public string SaveKey => "competitionboard";

        public string Capture() => JsonUtility.ToJson(new BoardState
        {
            enteredDay = _enteredDay,
            enteredIndex = _enteredIndex,
            enteredFee = _enteredFee,
            resultText = _resultText ?? ""
        });

        public void Restore(string json)
        {
            _runningIndex = -1;
            if (string.IsNullOrEmpty(json))
            {
                _enteredDay = -1; _enteredIndex = -1; _enteredFee = 0; _resultText = "";
                _abortCheckPending = false;
                return;
            }
            var s = JsonUtility.FromJson<BoardState>(json);
            _enteredDay = s.enteredDay;
            _enteredIndex = s.enteredIndex;
            _enteredFee = Mathf.Max(0, s.enteredFee);
            _resultText = s.resultText ?? "";
            // An entry with no result was saved mid-event; the event cannot resume.
            _abortCheckPending = _enteredDay >= 0 && string.IsNullOrEmpty(_resultText);
        }
    }
}

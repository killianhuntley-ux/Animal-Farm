using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The underworld calendar page plus its always-on HUD date line.
    ///
    /// HUD: a small "Day 7 - The Weep" button under the clock (top-right);
    /// clicking it - or pressing C - opens the calendar page: season name and
    /// flavor line, a 15-day grid with today highlighted, and an upcoming
    /// events list (empty-state until scripted events exist).
    ///
    /// Modal pattern (LandOfficeUI): UIInputLock.ModalOpen + gameplay input
    /// blocked while open, pause-respecting restore on close. Everything is
    /// built ONCE; day/season/rain changes only rewrite texts and tints in
    /// place (cheap signature poll - nothing ever flashes).
    /// Self-spawns at runtime - no scene setup required.
    /// </summary>
    public class CalendarUI : MonoBehaviour
    {
        public static CalendarUI Instance { get; private set; }

        private const int Columns = 5;
        private const int Rows = 3;

        // ---- HUD ----
        private Text _hudLabel;

        // ---- page (built once) ----
        private GameObject _panel;
        private Text _seasonNameText;
        private Text _flavorText;
        private Text _rainText;
        private Image[] _cellBg;
        private Text[] _cellText;

        private bool _open;
        private int _signature = int.MinValue;

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance == null)
                new GameObject("CalendarUI (runtime)").AddComponent<CalendarUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            GameCalendar.GetOrCreate(); // the HUD line needs a calendar to read
            BuildHud();
        }

        private void Update()
        {
            // C toggles the page. Direct device read: closed -> full
            // BlockDirectKeys check; open -> we ARE the modal, so only a live
            // text field may eat the key (SpiritInfoUI precedent).
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame)
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

            // Cheap change signature: refresh HUD (and the open page) only
            // when day, season or rain actually changed.
            var calendar = GameCalendar.Instance;
            var clock = GameClock.Instance;
            if (calendar == null || clock == null) return;

            bool raining = AnimalFarm.World.WeatherManager.Instance != null
                && AnimalFarm.World.WeatherManager.Instance.IsRaining;
            int signature = clock.Day * 8 + calendar.SeasonIndex * 2 + (raining ? 1 : 0);
            if (signature == _signature) return;
            _signature = signature;

            RefreshHud();
            if (_open) RefreshPage();
        }

        // ------------------------------------------------------- open / close

        public void Open()
        {
            if (_open) return;
            // The HUD button stays clickable under side-panel modals: never
            // stack on top of another modal or a live text prompt.
            if (UIInputLock.ModalOpen || UIInputLock.TextInputActive) return;

            if (_panel == null) BuildPage();

            RefreshPage();
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
            if (_panel != null) _panel.SetActive(false);

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ---------------------------------------------------------------- HUD

        /// <summary>Small date button right under the ClockHUD block.</summary>
        private void BuildHud()
        {
            var root = UIRoot.GetRoot();

            var button = UIStyle.MakeButton(root, "", Open, 18, UIStyle.PanelBg);
            button.gameObject.name = "Button_CalendarDate";
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -104f);
            rt.sizeDelta = new Vector2(240f, 34f);

            _hudLabel = button.GetComponentInChildren<Text>();
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (_hudLabel == null || GameCalendar.Instance == null) return;
            _hudLabel.text = GameCalendar.Instance.DateLine;
        }

        // --------------------------------------------------------------- page

        private void BuildPage()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("CalendarPage");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(760f, 640f);

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true; // swallow clicks behind the page

            // Title.
            var title = UIRoot.MakeText(panelRt, "Title", 34, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Calendar  [C]";
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -14f);
            titleRt.sizeDelta = new Vector2(0f, 44f);

            _seasonNameText = MakeHeaderLine(panelRt, "SeasonName", 30, UIStyle.Gold, -62f, 38f);
            _flavorText = MakeHeaderLine(panelRt, "Flavor", 20, UIStyle.Grey, -100f, 26f);
            _rainText = MakeHeaderLine(panelRt, "RainLine", 18, UIStyle.Grey, -128f, 24f);

            // 15-day grid, 5 x 3.
            var grid = new GameObject("DayGrid").AddComponent<RectTransform>();
            grid.SetParent(panelRt, false);
            grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);
            grid.anchoredPosition = new Vector2(0f, -164f);

            const float cellW = 136f;
            const float cellH = 84f;
            const float gap = 8f;
            grid.sizeDelta = new Vector2(Columns * cellW + (Columns - 1) * gap,
                Rows * cellH + (Rows - 1) * gap);

            _cellBg = new Image[GameCalendar.DaysPerSeason];
            _cellText = new Text[GameCalendar.DaysPerSeason];

            for (int i = 0; i < GameCalendar.DaysPerSeason; i++)
            {
                int col = i % Columns;
                int row = i / Columns;

                var cell = UIStyle.MakePanel(grid, "Day_" + (i + 1), UIStyle.PanelBgLight);
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.anchoredPosition = new Vector2(col * (cellW + gap), -row * (cellH + gap));
                cell.sizeDelta = new Vector2(cellW, cellH);
                _cellBg[i] = cell.GetComponent<Image>();
                _cellBg[i].raycastTarget = false;

                var dayText = UIRoot.MakeText(cell, "Num", 26, TextAnchor.MiddleCenter, UIStyle.Cream);
                dayText.text = (i + 1).ToString();
                var dayRt = dayText.rectTransform;
                dayRt.anchorMin = Vector2.zero;
                dayRt.anchorMax = Vector2.one;
                dayRt.offsetMin = Vector2.zero;
                dayRt.offsetMax = Vector2.zero;
                _cellText[i] = dayText;
            }

            // Upcoming events (scripted events slot in here later).
            var upcoming = MakeHeaderLine(panelRt, "UpcomingHeader", 24, UIStyle.Gold, -452f, 30f);
            upcoming.text = "Upcoming";
            var none = MakeHeaderLine(panelRt, "UpcomingEmpty", 20, UIStyle.Grey, -484f, 26f);
            none.text = "Nothing scheduled... yet.";

            // Close.
            var close = UIStyle.MakeButton(panelRt, "Close", Close, 24);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0f);
            closeRt.pivot = new Vector2(0.5f, 0f);
            closeRt.anchoredPosition = new Vector2(0f, 18f);
            closeRt.sizeDelta = new Vector2(220f, 52f);

            _panel.SetActive(false);
        }

        /// <summary>Full-width centered text row at a fixed offset from the top.</summary>
        private static Text MakeHeaderLine(RectTransform parent, string name, int size,
            Color color, float y, float height)
        {
            var text = UIRoot.MakeText(parent, name, size, TextAnchor.MiddleCenter, color);
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(0f, height);
            return text;
        }

        /// <summary>Rewrites texts and tints in place - never rebuilds objects.</summary>
        private void RefreshPage()
        {
            var calendar = GameCalendar.Instance;
            if (_panel == null || calendar == null) return;

            var season = calendar.CurrentSeason;
            _seasonNameText.text = season.name;
            _flavorText.text = season.flavor;

            bool raining = AnimalFarm.World.WeatherManager.Instance != null
                && AnimalFarm.World.WeatherManager.Instance.IsRaining;
            _rainText.text = raining ? "Rain falls today. The dirt drinks free." : "";

            int today = calendar.DayOfSeason;
            var darkOnGold = new Color(0.18f, 0.15f, 0.06f, 1f);
            for (int i = 0; i < _cellBg.Length; i++)
            {
                int dayNum = i + 1;
                bool isToday = dayNum == today;
                bool past = dayNum < today;

                _cellBg[i].color = isToday ? UIStyle.Gold : UIStyle.PanelBgLight;
                _cellText[i].color = isToday ? darkOnGold : (past ? UIStyle.Grey : UIStyle.Cream);
            }
        }
    }
}

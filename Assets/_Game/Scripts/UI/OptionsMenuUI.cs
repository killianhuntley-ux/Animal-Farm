using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Options modal, opened from the pause menu's "Options" button: music and
    /// SFX volume (UIStyle bar + minus/plus steppers - sliders are awkward in
    /// this code-built UI, and the steppers' own click bleep doubles as an SFX
    /// preview), screen shake and text speed toggles, and the Controls page.
    /// All four settings are device preferences, persisted via PlayerPrefs.
    ///
    /// Modal pattern (CalendarUI): UIInputLock.ModalOpen + gameplay input
    /// blocked while open, pause-respecting restore on close. Built once,
    /// refreshed in place. Closes itself (and the nested Controls page) if
    /// the game unpauses underneath it. Self-spawns - no scene setup.
    /// </summary>
    public class OptionsMenuUI : MonoBehaviour
    {
        public static OptionsMenuUI Instance { get; private set; }

        private const float VolumeStep = 0.1f;
        private const string ScreenShakePrefKey = "af_screen_shake";
        private const string TextSpeedPrefKey = "af_text_fast";

        // ---- static settings (PlayerPrefs-backed, for future consumers) ----

        private static bool _screenShake = true;
        private static bool _textFast;
        private static bool _prefsLoaded;

        /// <summary>Screen shake master switch (future camera effects read this).</summary>
        public static bool ScreenShakeEnabled
        {
            get { LoadPrefsOnce(); return _screenShake; }
            set
            {
                LoadPrefsOnce();
                _screenShake = value;
                PlayerPrefs.SetInt(ScreenShakePrefKey, value ? 1 : 0);
            }
        }

        /// <summary>Text speed: false = normal, true = fast (future dialogue reads this).</summary>
        public static bool TextSpeedFast
        {
            get { LoadPrefsOnce(); return _textFast; }
            set
            {
                LoadPrefsOnce();
                _textFast = value;
                PlayerPrefs.SetInt(TextSpeedPrefKey, value ? 1 : 0);
            }
        }

        private static void LoadPrefsOnce()
        {
            if (_prefsLoaded) return;
            _prefsLoaded = true;
            _screenShake = PlayerPrefs.GetInt(ScreenShakePrefKey, 1) != 0;
            _textFast = PlayerPrefs.GetInt(TextSpeedPrefKey, 0) != 0;
        }

        // ---- UI state (built once) ----

        private GameObject _panel;
        private RectTransform _musicFillRt;
        private Image _musicFill;
        private Text _musicValueText;
        private RectTransform _sfxFillRt;
        private Image _sfxFill;
        private Text _sfxValueText;
        private Button _shakeButton;
        private Text _shakeLabel;
        private Button _textSpeedButton;
        private Text _textSpeedLabel;

        private bool _open;
        private bool _subscribed;

        // ------------------------------------------------------- Lifecycle

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        public static OptionsMenuUI GetOrCreate()
        {
            if (Instance == null)
                new GameObject("OptionsMenuUI (runtime)").AddComponent<OptionsMenuUI>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnPauseChanged += OnPauseChanged;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && GameManager.Instance != null)
                GameManager.Instance.OnPauseChanged -= OnPauseChanged;

            if (Instance == this) Instance = null;
        }

        /// <summary>Unpausing underneath the modal (Esc) closes it cleanly.</summary>
        private void OnPauseChanged(bool paused)
        {
            if (!paused && _open) Close();
        }

        // ----------------------------------------------------- Open / close

        /// <summary>Opens the options modal (pause menu's Options button).</summary>
        public static void Open()
        {
            GetOrCreate().OpenInternal();
        }

        private void OpenInternal()
        {
            if (_open) return;

            if (_panel == null) BuildUI();

            Refresh();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling(); // render above the pause menu
            _open = true;
            UIInputLock.ModalOpen = true;

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        private void Close()
        {
            if (!_open) return;

            ControlsMenuUI.CloseIfOpen(); // nested page dies with its parent

            _open = false;
            UIInputLock.ModalOpen = false;
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

            // Dim backdrop covering the whole screen.
            _panel = new GameObject("OptionsMenu");
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
            panel.sizeDelta = new Vector2(460f, 560f);

            var panelImg = panel.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(panelImg, UIStyle.PanelBg);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Title.
            var title = UIRoot.MakeText(panel, "Title", 34, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Options";
            title.rectTransform.sizeDelta = new Vector2(0f, 52f);

            UIStyle.MakeDivider(panel);

            // Volume rows.
            BuildVolumeRow(panel, "Music", out _musicFillRt, out _musicFill, out _musicValueText,
                () => NudgeMusic(-VolumeStep), () => NudgeMusic(VolumeStep));
            BuildVolumeRow(panel, "SFX", out _sfxFillRt, out _sfxFill, out _sfxValueText,
                () => NudgeSfx(-VolumeStep), () => NudgeSfx(VolumeStep));

            UIStyle.MakeDivider(panel);

            // Toggles.
            _shakeButton = UIStyle.MakeButton(panel, "", OnToggleShake, 24);
            ((RectTransform)_shakeButton.transform).sizeDelta = new Vector2(0f, 52f);
            _shakeLabel = _shakeButton.GetComponentInChildren<Text>();

            _textSpeedButton = UIStyle.MakeButton(panel, "", OnToggleTextSpeed, 24);
            ((RectTransform)_textSpeedButton.transform).sizeDelta = new Vector2(0f, 52f);
            _textSpeedLabel = _textSpeedButton.GetComponentInChildren<Text>();

            UIStyle.MakeDivider(panel);

            // Controls page + close.
            var controls = UIStyle.MakeButton(panel, "Controls...", ControlsMenuUI.Open, 24);
            ((RectTransform)controls.transform).sizeDelta = new Vector2(0f, 52f);

            var close = UIStyle.MakeButton(panel, "Back", Close, 24);
            ((RectTransform)close.transform).sizeDelta = new Vector2(0f, 52f);

            _panel.SetActive(false);
        }

        /// <summary>
        /// One volume row: name label, a UIStyle bar showing the level, a
        /// "35%"-style readout, and minus/plus stepper buttons.
        /// </summary>
        private static void BuildVolumeRow(Transform parent, string label,
            out RectTransform fillRt, out Image fill, out Text valueText,
            UnityEngine.Events.UnityAction onMinus, UnityEngine.Events.UnityAction onPlus)
        {
            var row = new GameObject("Row_" + label).AddComponent<RectTransform>();
            row.SetParent(parent, false);
            row.sizeDelta = new Vector2(0f, 48f);

            var name = UIRoot.MakeText(row, "Name", 24, TextAnchor.MiddleLeft, UIStyle.Cream);
            name.text = label;
            var nameRt = name.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(0f, 1f);
            nameRt.pivot = new Vector2(0f, 0.5f);
            nameRt.sizeDelta = new Vector2(90f, 0f);
            nameRt.anchoredPosition = Vector2.zero;

            fill = UIStyle.MakeBar(row, "Bar", out fillRt);
            var barRt = (RectTransform)fill.transform.parent;
            barRt.anchorMin = new Vector2(0f, 0.25f);
            barRt.anchorMax = new Vector2(0f, 0.75f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.sizeDelta = new Vector2(150f, 0f);
            barRt.anchoredPosition = new Vector2(92f, 0f);
            barRt.GetComponent<Image>().raycastTarget = false;
            fill.raycastTarget = false;

            valueText = UIRoot.MakeText(row, "Value", 22, TextAnchor.MiddleCenter, UIStyle.Grey);
            var valueRt = valueText.rectTransform;
            valueRt.anchorMin = new Vector2(0f, 0f);
            valueRt.anchorMax = new Vector2(0f, 1f);
            valueRt.pivot = new Vector2(0f, 0.5f);
            valueRt.sizeDelta = new Vector2(64f, 0f);
            valueRt.anchoredPosition = new Vector2(250f, 0f);

            var minus = UIStyle.MakeButton(row, "-", onMinus, 26);
            var minusRt = (RectTransform)minus.transform;
            minusRt.anchorMin = new Vector2(1f, 0.5f);
            minusRt.anchorMax = new Vector2(1f, 0.5f);
            minusRt.pivot = new Vector2(1f, 0.5f);
            minusRt.sizeDelta = new Vector2(44f, 40f);
            minusRt.anchoredPosition = new Vector2(-50f, 0f);

            var plus = UIStyle.MakeButton(row, "+", onPlus, 26);
            var plusRt = (RectTransform)plus.transform;
            plusRt.anchorMin = new Vector2(1f, 0.5f);
            plusRt.anchorMax = new Vector2(1f, 0.5f);
            plusRt.pivot = new Vector2(1f, 0.5f);
            plusRt.sizeDelta = new Vector2(44f, 40f);
            plusRt.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------- Actions

        private void NudgeMusic(float delta)
        {
            AmbientMusic.MusicVolume = Mathf.Round((AmbientMusic.MusicVolume + delta) * 10f) / 10f;
            Refresh();
        }

        private void NudgeSfx(float delta)
        {
            // The stepper's own click bleep (UIStyle.MakeButton) previews the level.
            Bleeps.SfxVolume = Mathf.Round((Bleeps.SfxVolume + delta) * 10f) / 10f;
            Refresh();
        }

        private void OnToggleShake()
        {
            ScreenShakeEnabled = !ScreenShakeEnabled;
            Refresh();
        }

        private void OnToggleTextSpeed()
        {
            TextSpeedFast = !TextSpeedFast;
            Refresh();
        }

        // ------------------------------------------------------------- Refresh

        /// <summary>Rewrites bars, readouts and toggle styling in place.</summary>
        private void Refresh()
        {
            if (_panel == null) return;

            SetBar(_musicFillRt, _musicFill, _musicValueText, AmbientMusic.MusicVolume);
            SetBar(_sfxFillRt, _sfxFill, _sfxValueText, Bleeps.SfxVolume);

            StyleToggle(_shakeButton, _shakeLabel,
                ScreenShakeEnabled ? "Screen shake: ON" : "Screen shake: OFF", ScreenShakeEnabled);
            StyleToggle(_textSpeedButton, _textSpeedLabel,
                TextSpeedFast ? "Text speed: FAST" : "Text speed: NORMAL", TextSpeedFast);
        }

        private static void SetBar(RectTransform fillRt, Image fill, Text valueText, float frac)
        {
            frac = Mathf.Clamp01(frac);
            if (fillRt != null)
            {
                fillRt.anchorMax = new Vector2(frac, 1f);
                fillRt.offsetMax = new Vector2(frac >= 1f ? -3f : 0f, -3f);
            }
            if (fill != null) fill.color = frac <= 0f ? UIStyle.Grey : UIStyle.Gold;
            if (valueText != null) valueText.text = Mathf.RoundToInt(frac * 100f) + "%";
        }

        /// <summary>Gold background + dark text when ON (gentle-passage styling).</summary>
        private static void StyleToggle(Button button, Text label, string text, bool on)
        {
            if (label != null)
            {
                label.text = text;
                label.color = on ? new Color(0.18f, 0.15f, 0.06f, 1f) : UIStyle.Cream;
            }
            if (button != null)
                UIStyle.StyleButton(button, on ? UIStyle.Gold : (Color?)null);
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

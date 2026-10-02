using AnimalFarm.Core;
using AnimalFarm.Player;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Spirit inspection panel (slice 05) - the per-creature journal page.
    /// Press Inspect while focusing a spirit to open a right-side panel with
    /// its portrait, name (renamable), state, spirit bar, hunger, nature stats,
    /// fame and fulfilment checklist. Press Inspect, gamepad East, or the
    /// pinned "X" button (top-right, outside the layout flow so it can never
    /// fall off-screen) to dismiss. The content column lives inside a
    /// vertical-only ScrollRect so tall content stays reachable on small
    /// screens. Gameplay input is blocked while open (NamePromptUI pattern);
    /// the panel itself listens for the R key directly because
    /// SetGameplayBlocked disables the Inspect action.
    ///
    /// The content STRUCTURE is built once per Open (or target change / rename
    /// commit); the 0.5s tick only writes new values into the kept Text/bar
    /// references so the panel never visibly flashes.
    /// </summary>
    public class SpiritInfoUI : MonoBehaviour
    {
        private const float PanelWidth = 420f;
        private const float RefreshInterval = 0.5f;
        private const int NameCharLimit = 16;

        private static readonly Color TextBright = UIStyle.Cream;
        private static readonly Color TextGrey = UIStyle.Grey;
        private static readonly Color BarRed = UIStyle.Danger;
        private static readonly Color BarGold = UIStyle.Gold;
        private static readonly Color StatBlue = new Color(0.55f, 0.7f, 0.9f, 1f);

        private GameObject _panel;
        private RectTransform _content;
        private ScrollRect _scroll;
        private SpiritAgent _agent;
        private InteractionSensor _sensor;

        private bool _subscribed;
        private bool _renaming;

        /// <summary>True while the rename field holds TextInputActive.</summary>
        public bool IsRenaming => _renaming;
        private float _refreshTimer;

        // ---- kept references into the built content (value-only refresh) ------
        private Text _nameText;
        private Text _stateText;
        private Text _spiritText;
        private RectTransform _spiritFillRt;
        private Image _spiritFill;
        private Text _hungerText;
        private Text _groundText; // muscle 02: current ground + how it feels
        private Text _fameText;
        private Text _checkHomeText;
        private Text _checkSpiritText;
        private Text _checkWishText;

        // Structural signature of the last build; when the live agent drifts
        // away from it (state change, first competition entry, wish granted)
        // we do one full rebuild instead of a value refresh.
        private SpiritState _builtState;
        private bool _builtFameSection;
        private bool _builtWishHint;
        private int _builtStatSig; // stats + traits (training changes them live)

        private bool IsOpen => _panel != null && _panel.activeSelf;

        // ------------------------------------------------------------ lifecycle

        private void Start()
        {
            TrySubscribe();
        }

        private void Update()
        {
            // GameInput may spawn after us; keep retrying until we hook up.
            if (!_subscribed) TrySubscribe();

            if (!IsOpen) return;

            // Agent despawned/ascended under us? Close cleanly.
            if (_agent == null) { Close(); return; }

            // SetGameplayBlocked disables the Inspect action while we're open,
            // so read the key directly (we are the modal, so ModalOpen is ours;
            // only defer to an active text field). Gamepad East also closes.
            if (!UIInputLock.TextInputActive)
            {
                bool keyClose = Keyboard.current != null
                    && Keyboard.current.rKey.wasPressedThisFrame;
                bool padClose = Gamepad.current != null
                    && Gamepad.current.buttonEast.wasPressedThisFrame;
                if (keyClose || padClose)
                {
                    Close();
                    return;
                }
            }

            // Low-frequency value refresh (skip mid-rename so we never touch
            // the live input field's row).
            if (_renaming) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = RefreshInterval;
                RefreshValues();
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && GameInput.Instance != null)
                GameInput.Instance.InspectPressed -= OnInspectPressed;

            if (IsOpen)
            {
                // Never leave global locks dangling.
                UIInputLock.ModalOpen = false;
                if (_renaming) UIInputLock.TextInputActive = false;
            }
        }

        private void TrySubscribe()
        {
            if (GameInput.Instance == null) return;
            GameInput.Instance.InspectPressed += OnInspectPressed;
            _subscribed = true;
        }

        // ---------------------------------------------------------------- input

        private void OnInspectPressed()
        {
            if (IsOpen) { Close(); return; }

            // Never open mid-competition or while some text field is capturing keys.
            if (UIInputLock.TextInputActive) return;
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return;

            if (_sensor == null)
            {
                _sensor = FindFirstObjectByType<InteractionSensor>();
                if (_sensor == null) return;
            }

            if (_sensor.Current is SpiritAgent agent)
                Open(agent);
            // Not a spirit (or nothing focused): do nothing.
        }

        // ----------------------------------------------------------- open/close

        /// <summary>Singleton-ish accessor for external openers (click selection).</summary>
        public static SpiritInfoUI Instance { get; private set; }
        private void Awake() => Instance = this;

        /// <summary>Public entry for the click-selection system.</summary>
        public void OpenFor(SpiritAgent agent)
        {
            if (AnimalFarm.Core.UIInputLock.TextInputActive) return;
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return;
            Open(agent);
        }

        private void Open(SpiritAgent agent)
        {
            if (agent == null) return;

            // Safety net (covers OpenFor and the Inspect key alike): silhouettes
            // are mysteries - no inspecting, and certainly no renaming them.
            if (agent.State == SpiritState.Silhouette) return;

            if (_panel == null) BuildPanel();

            _agent = agent;
            _renaming = false;
            _refreshTimer = RefreshInterval;

            RebuildContent();
            _panel.SetActive(true);

            // Fresh target: start scrolled to the top, with no leftover fling.
            if (_content != null) _content.anchoredPosition = Vector2.zero;
            if (_scroll != null) _scroll.velocity = Vector2.zero;

            // The panel must render above the clock HUD (and anything else
            // already on the canvas).
            _panel.transform.SetAsLastSibling();

            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        private void Close()
        {
            if (_panel != null) _panel.SetActive(false);
            _agent = null;
            _renaming = false;

            UIInputLock.ModalOpen = false;
            UIInputLock.TextInputActive = false;

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ------------------------------------------------------------ panel shell

        private void BuildPanel()
        {
            var root = UIRoot.GetRoot();

            _panel = new GameObject("SpiritInfoPanel");
            var rt = _panel.AddComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, 0f);
            rt.anchoredPosition = Vector2.zero;

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true; // swallow clicks over the panel

            // Scroll container: tall content used to push the old bottom Close
            // button below the screen, so the content column now lives inside
            // a vertical-only elastic ScrollRect (wheel + drag, no scrollbar).
            var scrollGo = new GameObject("Scroll");
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.SetParent(rt, false);
            Stretch(scrollRt);

            // Invisible graphic so wheel/drag raycasts reach the ScrollRect.
            var scrollHit = scrollGo.AddComponent<Image>();
            scrollHit.color = Color.clear;
            scrollGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            _content = contentGo.AddComponent<RectTransform>();
            _content.SetParent(scrollRt, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.viewport = scrollRt;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.scrollSensitivity = 30f;

            // Pinned close button: added to the panel root AFTER the scroll
            // container so it draws on top, and OUTSIDE the layout flow so it
            // is always reachable regardless of content height. R and gamepad
            // East still close (Update()).
            var closeGo = new GameObject("Button_CloseX");
            var closeRt = closeGo.AddComponent<RectTransform>();
            closeRt.SetParent(rt, false);
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-10f, -10f);
            closeRt.sizeDelta = new Vector2(36f, 36f);
            StyleButton(closeGo, "X", 20, Close);

            _panel.SetActive(false);
        }

        // --------------------------------------------------------------- content

        /// <summary>
        /// Full structural (re)build from current agent state. Called ONLY on
        /// Open / target change / rename commit / structural drift - the 0.5s
        /// tick goes through RefreshValues instead.
        /// </summary>
        private void RebuildContent()
        {
            if (_agent == null || _content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            _nameText = null;
            _stateText = null;
            _spiritText = null;
            _spiritFillRt = null;
            _spiritFill = null;
            _hungerText = null;
            _groundText = null;
            _fameText = null;
            _checkHomeText = null;
            _checkSpiritText = null;
            _checkWishText = null;

            var species = _agent.Species;

            // Remember what shape we built so RefreshValues can detect drift.
            _builtState = _agent.State;
            _builtFameSection = _agent.CompetitionEntries > 0;
            _builtWishHint = WantsWishHint();
            _builtStatSig = _agent.StatSignature;

            BuildPortrait(species);
            BuildNameRow();

            // Species + state line (text filled by RefreshValues).
            _stateText = AddText("", 22, TextBright, 28f);
            _stateText.alignment = TextAnchor.MiddleLeft;

            // Flavor text.
            if (species != null && !string.IsNullOrEmpty(species.flavor))
            {
                var flavor = AddText(species.flavor, 20, TextGrey, 0f);
                flavor.horizontalOverflow = HorizontalWrapMode.Wrap;
                AutoHeight(flavor, 3);
            }

            UIStyle.MakeDivider(_content);
            BuildSpiritBar();

            _hungerText = AddText("", 22, TextBright, 28f);

            // Muscle 02: what this species loves / hates, and how its ground feels right now.
            var biomes = AddText("Biomes: " + BiomeAffinity.Describe(species), 20, TextGrey, 0f);
            biomes.horizontalOverflow = HorizontalWrapMode.Wrap;
            AutoHeight(biomes, 2);
            _groundText = AddText("", 22, TextBright, 28f);

            UIStyle.MakeDivider(_content);
            BuildNatureBlock();

            // Fame (hidden until the first entry).
            if (_builtFameSection)
            {
                UIStyle.MakeDivider(_content);
                _fameText = AddText("", 22, TextBright, 28f);
            }

            // Fulfilment checklist (Residents only).
            if (_agent.State == SpiritState.Resident)
            {
                UIStyle.MakeDivider(_content);
                AddText("Fulfilment", 24, BarGold, 30f);
                _checkHomeText = AddText("", 22, TextBright, 26f);
                _checkSpiritText = AddText("", 22, TextBright, 26f);
                _checkWishText = AddText("", 22, TextBright, 26f);

                if (_builtWishHint)
                {
                    var wish = AddText(species.taskDescription, 20, TextGrey, 0f);
                    wish.horizontalOverflow = HorizontalWrapMode.Wrap;
                    AutoHeight(wish, 3);
                }
            }

            RefreshValues();
        }

        /// <summary>
        /// Lightweight per-tick refresh: only rewrites .text, bar rects and
        /// colors on the kept references. Falls back to a full rebuild when
        /// the panel's structure no longer matches the agent.
        /// </summary>
        private void RefreshValues()
        {
            if (_agent == null) return;

            // Structural drift? Rebuild once (RebuildContent re-enters us with
            // a matching signature, so this cannot loop).
            if (_agent.State != _builtState
                || (_agent.CompetitionEntries > 0) != _builtFameSection
                || WantsWishHint() != _builtWishHint
                || _agent.StatSignature != _builtStatSig)
            {
                RebuildContent();
                return;
            }

            var species = _agent.Species;

            if (_nameText != null)
                _nameText.text = ShownName();

            if (_stateText != null)
            {
                string speciesName = species != null && !string.IsNullOrEmpty(species.displayName)
                    ? species.displayName : "Spirit";
                _stateText.text = speciesName + " - " + StateSuffix(_agent);
            }

            int pct = Mathf.RoundToInt(Mathf.Clamp(_agent.Spirit, 0f, 100f));
            float frac = Mathf.Clamp01(_agent.Spirit / 100f);
            if (_spiritText != null)
                _spiritText.text = "Spirit " + pct + "%";
            if (_spiritFillRt != null)
            {
                _spiritFillRt.anchorMax = new Vector2(frac, 1f);
                _spiritFillRt.offsetMax = new Vector2(frac >= 1f ? -3f : 0f, -3f);
            }
            if (_spiritFill != null)
                _spiritFill.color = Color.Lerp(BarRed, BarGold, frac);

            if (_hungerText != null)
                _hungerText.text = "Hunger: " + HungerWord(_agent.Hunger01);

            if (_groundText != null)
                _groundText.text = _agent.GroundBase < 0
                    ? "Ground: beyond the fences"
                    : "Ground: " + BiomeAffinity.BiomeName(_agent.GroundBiome)
                        + " - " + BiomeAffinity.GroundFeeling(_agent.GroundAffinity);

            if (_fameText != null)
                _fameText.text = "Competitions: " + _agent.CompetitionWins + " wins / "
                    + _agent.CompetitionEntries + " entries";

            if (_checkHomeText != null)
                _checkHomeText.text = Check(_agent.HasHome) + " Home";
            if (_checkSpiritText != null)
                _checkSpiritText.text = Check(_agent.Spirit >= 100f) + " Full spirit";
            if (_checkWishText != null)
                _checkWishText.text = Check(_agent.TaskDone) + " Final wish";
        }

        private bool WantsWishHint()
        {
            return _agent != null
                && _agent.State == SpiritState.Resident
                && !_agent.TaskDone
                && _agent.Species != null
                && !string.IsNullOrEmpty(_agent.Species.taskDescription);
        }

        private string ShownName()
        {
            if (!string.IsNullOrEmpty(_agent.GivenName)) return _agent.GivenName;
            return _agent.Species != null && !string.IsNullOrEmpty(_agent.Species.displayName)
                ? _agent.Species.displayName : "Spirit";
        }

        // ------------------------------------------------------------- portrait

        /// <summary>Portrait at the top: body sprite on a small rounded backing.</summary>
        private void BuildPortrait(SpiritSpeciesDefinition species)
        {
            if (species == null || species.bodySprite == null) return;

            var row = MakeRow("PortraitRow", 108f);

            var backing = UIStyle.MakePanel(row, "PortraitBg", UIStyle.PanelBgLight);
            backing.anchorMin = backing.anchorMax = new Vector2(0.5f, 0.5f);
            backing.pivot = new Vector2(0.5f, 0.5f);
            backing.sizeDelta = new Vector2(108f, 104f);
            backing.anchoredPosition = Vector2.zero;
            backing.GetComponent<Image>().raycastTarget = false;

            var imgGo = new GameObject("Portrait");
            var imgRt = imgGo.AddComponent<RectTransform>();
            imgRt.SetParent(backing, false);
            imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.pivot = new Vector2(0.5f, 0.5f);
            imgRt.sizeDelta = new Vector2(96f, 96f);
            imgRt.anchoredPosition = Vector2.zero;

            var img = imgGo.AddComponent<Image>();
            img.sprite = species.bodySprite;
            img.preserveAspect = true;
            img.color = species.tint; // world-render tint (defaults to full color)
            img.raycastTarget = false;
        }

        // ------------------------------------------------------------- name row

        private void BuildNameRow()
        {
            var row = MakeRow("NameRow", 48f);

            var name = UIRoot.MakeText(row, "Name", 32, TextAnchor.MiddleLeft, TextBright);
            name.text = ShownName();
            var nameRt = name.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = new Vector2(-130f, 0f);
            _nameText = name;

            MakeSmallButton(row, "Rename", 120f, BeginRename);
        }

        /// <summary>Swaps the name row for an InputField + OK button.</summary>
        private void BeginRename()
        {
            if (_agent == null || _renaming) return;
            _renaming = true;
            UIInputLock.TextInputActive = true;

            var row = _content.Find("NameRow");
            if (row == null) { EndRename(null); return; }
            _nameText = null; // about to destroy it with the row's children
            for (int i = row.childCount - 1; i >= 0; i--)
                Destroy(row.GetChild(i).gameObject);

            // Legacy InputField: bg image + child text (NamePromptUI pattern).
            var inputGo = new GameObject("NameInput");
            var inputRt = inputGo.AddComponent<RectTransform>();
            inputRt.SetParent(row, false);
            inputRt.anchorMin = new Vector2(0f, 0f);
            inputRt.anchorMax = new Vector2(1f, 1f);
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = new Vector2(-90f, 0f);

            var inputBg = inputGo.AddComponent<Image>();
            UIStyle.ApplyPanel(inputBg, UIStyle.PanelBgLight);

            var inputText = UIRoot.MakeText(inputRt, "Text", 26, TextAnchor.MiddleLeft, UIStyle.Cream);
            inputText.supportRichText = false;
            inputText.raycastTarget = true;
            var textRt = inputText.rectTransform;
            Stretch(textRt);
            textRt.offsetMin = new Vector2(10f, 4f);
            textRt.offsetMax = new Vector2(-10f, -4f);

            var input = inputGo.AddComponent<InputField>();
            input.targetGraphic = inputBg;
            input.textComponent = inputText;
            input.characterLimit = NameCharLimit;
            input.text = !string.IsNullOrEmpty(_agent.GivenName) ? _agent.GivenName : "";
            input.onSubmit.AddListener(_ => EndRename(input)); // Enter confirms

            MakeSmallButton(row, "OK", 80f, () => EndRename(input));

            input.Select();
            input.ActivateInputField();
        }

        /// <summary>Applies the typed name (fallback to the old one) and swaps back.</summary>
        private void EndRename(InputField input)
        {
            _renaming = false;
            UIInputLock.TextInputActive = false;

            if (_agent != null)
            {
                string old = _agent.GivenName;
                string typed = input != null && input.text != null ? input.text.Trim() : "";
                _agent.SetGivenName(typed.Length > 0 ? typed : old);
            }

            _refreshTimer = RefreshInterval;
            RebuildContent(); // rename commit: the one non-Open full rebuild
        }

        // -------------------------------------------------------- content pieces

        private void BuildSpiritBar()
        {
            _spiritText = AddText("", 22, TextBright, 26f);

            // Rounded bar; RefreshValues drives the fill width + red->gold lerp.
            _spiritFill = UIStyle.MakeBar(_content, "SpiritBar", out _spiritFillRt, BarGold);
            var barRt = (RectTransform)_spiritFill.transform.parent;
            barRt.sizeDelta = new Vector2(0f, 20f);
            barRt.GetComponent<Image>().raycastTarget = false;
            _spiritFill.raycastTarget = false;
        }

        private void BuildNatureBlock()
        {
            // Stats roll inside the species band at spawn and climb with
            // training; the structural signature rebuilds this block on change.
            AddText("Nature", 24, BarGold, 30f);
            AddStatRow(SpiritStat.Vigor);
            AddStatRow(SpiritStat.Grace);
            AddStatRow(SpiritStat.Gleam);

            // Traits (muscle 05): names + one flavor line each.
            var traits = _agent.Traits;
            if (traits.Count > 0)
            {
                AddText("Traits: " + SpiritTraits.Describe(traits), 22, BarGold, 28f);
                var lines = AddText(SpiritTraits.FlavorLines(traits), 20, TextGrey, 0f);
                lines.horizontalOverflow = HorizontalWrapMode.Wrap;
                AutoHeight(lines, traits.Count * 2);
            }
            else
            {
                AddText("Every spirit is born different.", 20, TextGrey, 24f);
            }
        }

        private void AddStatRow(SpiritStat stat)
        {
            string label = SpiritStats.Label(stat);
            int value = _agent.GetStat(stat);
            var row = MakeRow("Stat_" + label, 24f);

            // "Vigor 4 (3-7)": the roll plus the species band it came from.
            var text = UIRoot.MakeText(row, "Label", 22, TextAnchor.MiddleLeft, TextBright);
            text.text = SpiritStats.BandText(_agent.Species, stat, value);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var textRt = text.rectTransform;
            textRt.anchorMin = new Vector2(0f, 0f);
            textRt.anchorMax = new Vector2(0f, 1f);
            textRt.pivot = new Vector2(0f, 0.5f);
            textRt.sizeDelta = new Vector2(170f, 0f);
            textRt.anchoredPosition = Vector2.zero;

            const float barMax = 170f;

            // Rounded mini-bar (fixed width, right of the label).
            float frac = Mathf.Clamp01(value / (float)SpiritStats.Ceiling);
            var fill = UIStyle.MakeBar(row, "Bar", out var fillRt, StatBlue);
            var backRt = (RectTransform)fill.transform.parent;
            backRt.anchorMin = new Vector2(0f, 0.15f);
            backRt.anchorMax = new Vector2(0f, 0.85f);
            backRt.pivot = new Vector2(0f, 0.5f);
            backRt.sizeDelta = new Vector2(barMax, 0f);
            backRt.anchoredPosition = new Vector2(180f, 0f);
            backRt.GetComponent<Image>().raycastTarget = false;

            // Fill width: inset 3px each side of the filled fraction.
            fillRt.offsetMax = new Vector2(3f + Mathf.Max(0f, (barMax - 6f) * frac), -3f);
            fill.raycastTarget = false;
        }

        // ------------------------------------------------------------ text logic

        private static string StateSuffix(SpiritAgent agent)
        {
            string s;
            switch (agent.State)
            {
                case SpiritState.Resident: s = "Resident"; break;
                case SpiritState.Visitor: s = "Visitor"; break;
                case SpiritState.Runaway: s = "Runaway"; break;
                default: s = "Unknown"; break;
            }
            if (agent.IsSleeping) s += " (sleeping)";
            if (agent.IsFollowing) s += " (following)";
            if (agent.IsFulfilled) s += " (FULFILLED)";
            return s;
        }

        private static string HungerWord(float hunger01)
        {
            if (hunger01 < 0.25f) return "Fed";
            if (hunger01 < 0.5f) return "Peckish";
            if (hunger01 < 0.75f) return "Hungry";
            return "Starving";
        }

        private static string Check(bool done) => done ? "[x]" : "[ ]";

        // ------------------------------------------------------------ UI helpers

        private Text AddText(string value, int size, Color color, float height)
        {
            var text = UIRoot.MakeText(_content, "Text", size, TextAnchor.UpperLeft, color);
            text.text = value;
            text.rectTransform.sizeDelta = new Vector2(0f, height);
            return text;
        }

        /// <summary>Gives a wrapping text a rough fixed height (~lines of its font).</summary>
        private static void AutoHeight(Text text, int maxLines)
        {
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.rectTransform.sizeDelta =
                new Vector2(0f, (text.fontSize + 6) * maxLines);
        }

        private RectTransform MakeRow(string name, float height)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_content, false);
            rt.sizeDelta = new Vector2(0f, height);
            return rt;
        }

        /// <summary>Small fixed-width button anchored to the row's right edge.</summary>
        private static void MakeSmallButton(Transform row, string label, float width,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(row, false);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            rt.anchoredPosition = Vector2.zero;

            StyleButton(go, label, 22, onClick);
        }

        /// <summary>
        /// Every button here goes through UIStyle.StyleButton: it assigns the
        /// rounded sprite and drives the tint via the Button's ColorBlock (the
        /// Image itself stays white only because the ColorBlock multiplies it
        /// down to ButtonBg every frame - nothing renders as raw white).
        /// </summary>
        private static void StyleButton(GameObject go, string label, int fontSize,
            UnityEngine.Events.UnityAction onClick)
        {
            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(go.transform, "Label", fontSize,
                TextAnchor.MiddleCenter, TextBright);
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

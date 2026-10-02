using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The spirit journal, toggled with J. Species tab shows what the player
    /// has discovered about each species (gated by SpiritManager's
    /// DiscoveryLevel); Residents tab lists the named spirits living here;
    /// Legacy tab remembers those who ascended.
    /// Escape is taken by Pause, so the journal opens AND closes with J only.
    /// Content is rebuilt on open and on tab/selection change. Only the
    /// Residents tab refreshes on a 0.5s timer, and even then it rebuilds only
    /// when a cheap data signature changed - so the panel never flashes.
    /// The Residents tab is a split view like the Species tab: row buttons on
    /// the left, and a read-only detail pane for the selected resident on the
    /// right (a compact mirror of SpiritInfoUI's content - no Rename, no
    /// Close). Clicking a row selects it in place; the journal stays open.
    /// </summary>
    public class JournalUI : MonoBehaviour
    {
        private enum Tab { Species, Residents, Legacy }

        private const float RebuildInterval = 0.5f;

        private static readonly Color Cream = UIStyle.Cream;
        private static readonly Color Muted = UIStyle.Grey;
        private static readonly Color Danger = UIStyle.Danger;
        private static readonly Color Gold = UIStyle.Gold;
        private static readonly Color StatBlue = new Color(0.55f, 0.7f, 0.9f, 1f);

        private GameObject _panel;
        private RectTransform _leftColumn;   // species rows (Species tab)
        private RectTransform _rightPane;    // detail lines (Species tab)
        private GameObject _residentsArea;
        private RectTransform _residentsLeftColumn; // resident rows (Residents tab)
        private RectTransform _residentDetailPane;  // detail pane (Residents tab)
        private RectTransform _legacyPane;           // Legacy tab area (holds the two columns below)
        private RectTransform _legacyListColumn;     // crossed spirits, paged (Legacy tab)
        private RectTransform _legacyDetailPane;     // selected spirit's page + "Woven away"
        private Headstone _selectedStone;
        private int _legacyPage;
        private GameObject _speciesArea;
        private Button _tabSpeciesButton;
        private Button _tabResidentsButton;
        private Button _tabLegacyButton;
        private Text _tabSpeciesLabel;
        private Text _tabResidentsLabel;
        private Text _tabLegacyLabel;

        private bool _open;
        private Tab _tab = Tab.Species;
        private string _selectedSpeciesId;
        private float _rebuildTimer;
        private int _residentsSignature;
        private SpiritAgent _selectedAgent;

        // ---- kept references into the resident detail pane (value refresh) ----
        private Text _resStateText;
        private Text _resSpiritText;
        private RectTransform _resSpiritFillRt;
        private Image _resSpiritFill;
        private Text _resHungerText;
        private Text _resGroundText; // muscle 02: current ground + how it feels
        private Text _resFameText;
        private Text _resCheckHomeText;
        private Text _resCheckSpiritText;
        private Text _resCheckWishText;

        public static JournalUI Instance { get; private set; }

        /// <summary>True while the journal page is showing.</summary>
        public bool IsOpen => _open;

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Naming ceremony (muscle 03): opens straight to this resident's page
        /// on the Residents tab, optionally with a quick page-flip. No-op when
        /// the journal is already open (never toggles it closed).
        /// </summary>
        public void OpenToResident(SpiritAgent agent, bool flip)
        {
            if (_open) return;

            _tab = Tab.Residents;
            _selectedAgent = agent;
            Toggle();
            if (flip && _panel != null) StartCoroutine(PageFlip());
        }

        /// <summary>Horizontal page-turn: the panel unfolds from a thin edge (real time).</summary>
        private System.Collections.IEnumerator PageFlip()
        {
            var rt = _panel != null ? _panel.transform as RectTransform : null;
            if (rt == null) yield break;

            const float duration = 0.35f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                rt.localScale = new Vector3(Mathf.Max(0.04f, k), 1f, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.jKey.wasPressedThisFrame)
            {
                // Closed: full BlockDirectKeys check. Open: we ARE the modal, so only
                // a live text field may eat the key (CalendarUI pattern).
                if (_open)
                {
                    if (!AnimalFarm.Core.UIInputLock.TextInputActive) Toggle();
                }
                else if (!AnimalFarm.Core.UIInputLock.BlockDirectKeys)
                {
                    Toggle();
                }
            }

            if (!_open) return;

            // Timed refresh is Residents-only (its rows genuinely change), and
            // it rebuilds only when the underlying data actually changed so
            // nothing visibly flashes. Species/Legacy rebuild on open and on
            // tab/selection change instead.
            _rebuildTimer -= Time.unscaledDeltaTime;
            if (_rebuildTimer <= 0f)
            {
                _rebuildTimer = RebuildInterval;
                if (_tab == Tab.Residents)
                {
                    int sig = ComputeResidentsSignature();
                    if (sig != _residentsSignature)
                    {
                        _residentsSignature = sig;
                        RebuildResidentsTab();
                    }
                    else
                    {
                        // Nothing structural changed: just rewrite the detail
                        // pane's texts/bar in place (no rebuild, no flash).
                        RefreshResidentDetailValues();
                    }
                }
            }
        }

        private void Toggle()
        {
            _open = !_open;

            if (_panel == null) BuildUI();
            _panel.SetActive(_open);
            if (_open) _panel.transform.SetAsLastSibling(); // render above the HUD

            // A running ceremony owns the modal flag and the input block.
            bool ceremony = AnimalFarm.Core.UIInputLock.CeremonyActive;
            if (_open || !ceremony) AnimalFarm.Core.UIInputLock.ModalOpen = _open;

            if (GameInput.Instance != null)
            {
                if (_open)
                {
                    GameInput.Instance.SetGameplayBlocked(true);
                }
                else
                {
                    // Don't hand input back if the pause menu or a ceremony still needs it blocked.
                    bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                    if (!paused && !ceremony) GameInput.Instance.SetGameplayBlocked(false);
                }
            }

            if (_open)
            {
                _rebuildTimer = RebuildInterval;
                Rebuild();
            }
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            var root = UIRoot.GetRoot();

            // Near-fullscreen dark panel.
            _panel = new GameObject("Journal");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = new Vector2(60f, 40f);
            panelRt.offsetMax = new Vector2(-60f, -40f);

            var bg = _panel.AddComponent<Image>();
            UIStyle.ApplyPanel(bg, UIStyle.PanelBg);
            bg.raycastTarget = true; // swallow clicks behind the journal

            // Title.
            var title = UIRoot.MakeText(panelRt, "Title", 34, TextAnchor.MiddleCenter, Cream);
            title.text = "Journal  [J]";
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -14f);
            titleRt.sizeDelta = new Vector2(0f, 44f);

            // Tab buttons, top-left.
            _tabSpeciesButton = MakeTabButton(panelRt, "Species", new Vector2(24f, -68f),
                () => SelectTab(Tab.Species), out _tabSpeciesLabel);
            _tabResidentsButton = MakeTabButton(panelRt, "Residents", new Vector2(184f, -68f),
                () => SelectTab(Tab.Residents), out _tabResidentsLabel);
            _tabLegacyButton = MakeTabButton(panelRt, "Legacy", new Vector2(344f, -68f),
                () => SelectTab(Tab.Legacy), out _tabLegacyLabel);

            // --- Species tab area: left column + right pane. -----------------
            _speciesArea = new GameObject("SpeciesArea");
            var areaRt = _speciesArea.AddComponent<RectTransform>();
            areaRt.SetParent(panelRt, false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(24f, 20f);
            areaRt.offsetMax = new Vector2(-24f, -120f);

            _leftColumn = MakeColumn(areaRt, "LeftColumn");
            _leftColumn.anchorMin = new Vector2(0f, 0f);
            _leftColumn.anchorMax = new Vector2(0f, 1f);
            _leftColumn.pivot = new Vector2(0f, 1f);
            _leftColumn.offsetMin = new Vector2(0f, 0f);
            _leftColumn.offsetMax = new Vector2(280f, 0f);

            _rightPane = MakeColumn(areaRt, "RightPane");
            _rightPane.anchorMin = new Vector2(0f, 0f);
            _rightPane.anchorMax = new Vector2(1f, 1f);
            _rightPane.pivot = new Vector2(0f, 1f);
            _rightPane.offsetMin = new Vector2(310f, 0f);
            _rightPane.offsetMax = new Vector2(0f, 0f);

            // --- Residents tab area: left rows + right detail pane. -----------
            _residentsArea = new GameObject("ResidentsArea");
            var resAreaRt = _residentsArea.AddComponent<RectTransform>();
            resAreaRt.SetParent(panelRt, false);
            resAreaRt.anchorMin = Vector2.zero;
            resAreaRt.anchorMax = Vector2.one;
            resAreaRt.offsetMin = new Vector2(24f, 20f);
            resAreaRt.offsetMax = new Vector2(-24f, -120f);

            _residentsLeftColumn = MakeColumn(resAreaRt, "LeftColumn");
            _residentsLeftColumn.anchorMin = new Vector2(0f, 0f);
            _residentsLeftColumn.anchorMax = new Vector2(0f, 1f);
            _residentsLeftColumn.pivot = new Vector2(0f, 1f);
            _residentsLeftColumn.offsetMin = new Vector2(0f, 0f);
            _residentsLeftColumn.offsetMax = new Vector2(560f, 0f);

            _residentDetailPane = MakeColumn(resAreaRt, "DetailPane");
            _residentDetailPane.anchorMin = new Vector2(0f, 0f);
            _residentDetailPane.anchorMax = new Vector2(1f, 1f);
            _residentDetailPane.pivot = new Vector2(0f, 1f);
            _residentDetailPane.offsetMin = new Vector2(590f, 0f);
            _residentDetailPane.offsetMax = new Vector2(0f, 0f);

            // --- Legacy tab area. ----------------------------------------------
            // Muscle 04 gallery: a split view (names left, the selected spirit's
            // page right) like the Residents tab; _legacyPane is just the area.
            _legacyPane = new GameObject("LegacyPane").AddComponent<RectTransform>();
            _legacyPane.SetParent(panelRt, false);
            _legacyPane.anchorMin = Vector2.zero;
            _legacyPane.anchorMax = Vector2.one;
            _legacyPane.pivot = new Vector2(0f, 1f);
            _legacyPane.offsetMin = new Vector2(24f, 20f);
            _legacyPane.offsetMax = new Vector2(-24f, -120f);

            _legacyListColumn = MakeColumn(_legacyPane, "LeftColumn");
            _legacyListColumn.anchorMin = new Vector2(0f, 0f);
            _legacyListColumn.anchorMax = new Vector2(0f, 1f);
            _legacyListColumn.pivot = new Vector2(0f, 1f);
            _legacyListColumn.offsetMin = new Vector2(0f, 0f);
            _legacyListColumn.offsetMax = new Vector2(480f, 0f);

            _legacyDetailPane = MakeColumn(_legacyPane, "DetailPane");
            _legacyDetailPane.anchorMin = new Vector2(0f, 0f);
            _legacyDetailPane.anchorMax = new Vector2(1f, 1f);
            _legacyDetailPane.pivot = new Vector2(0f, 1f);
            _legacyDetailPane.offsetMin = new Vector2(510f, 0f);
            _legacyDetailPane.offsetMax = new Vector2(0f, 0f);

            _panel.SetActive(false);
        }

        /// <summary>A vertical-layout container whose children are rebuilt at runtime.</summary>
        private static RectTransform MakeColumn(Transform parent, string name)
        {
            var rt = new GameObject(name).AddComponent<RectTransform>();
            rt.SetParent(parent, false);

            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rt;
        }

        private Button MakeTabButton(Transform parent, string label, Vector2 anchoredPos,
            UnityEngine.Events.UnityAction onClick, out Text labelText)
        {
            var go = new GameObject("Tab_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(150f, 40f);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", 24, TextAnchor.MiddleCenter, Cream);
            text.text = label;
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            labelText = text;
            return button;
        }

        private void SelectTab(Tab tab)
        {
            _tab = tab;
            Rebuild();
        }

        /// <summary>Visual-only: active tab gets the gold base color.</summary>
        private static void RestyleTab(Button button, Text label, bool active)
        {
            if (button != null)
                UIStyle.StyleButton(button, active ? UIStyle.GoldDim : (Color?)null);
            if (label != null)
                label.color = active ? Cream : Muted;
        }

        // ------------------------------------------------------------- Rebuild

        private void Rebuild()
        {
            if (_panel == null) return;

            _speciesArea.SetActive(_tab == Tab.Species);
            _residentsArea.SetActive(_tab == Tab.Residents);
            _legacyPane.gameObject.SetActive(_tab == Tab.Legacy);

            // Highlight the active tab: gold background + bright label.
            RestyleTab(_tabSpeciesButton, _tabSpeciesLabel, _tab == Tab.Species);
            RestyleTab(_tabResidentsButton, _tabResidentsLabel, _tab == Tab.Residents);
            RestyleTab(_tabLegacyButton, _tabLegacyLabel, _tab == Tab.Legacy);

            switch (_tab)
            {
                case Tab.Species: RebuildSpeciesTab(); break;
                case Tab.Residents:
                    _residentsSignature = ComputeResidentsSignature();
                    RebuildResidentsTab();
                    break;
                case Tab.Legacy: RebuildLegacyTab(); break;
            }
        }

        /// <summary>
        /// Cheap signature of everything the Residents tab renders (count,
        /// states, names, spirit, hunger bucket, homes, fame, follow flags,
        /// wish/task done). The timed refresh rebuilds only when this changes;
        /// otherwise the detail pane gets a value-only refresh.
        /// </summary>
        private static int ComputeResidentsSignature()
        {
            var manager = SpiritManager.Instance;
            var spirits = manager != null ? manager.AllSpirits : null;
            if (spirits == null) return 0;

            unchecked
            {
                int h = 17;
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent == null) continue;
                    if (agent.State != SpiritState.Resident
                        && agent.State != SpiritState.Runaway) continue;

                    h = h * 31 + (int)agent.State;
                    h = h * 31 + Mathf.RoundToInt(agent.Spirit);
                    h = h * 31 + (agent.Hunger01 > 0.75f ? 1 : 0);
                    h = h * 31 + agent.CompetitionEntries * 131 + agent.CompetitionWins;
                    h = h * 31 + (agent.HasHome ? 1 : 0)
                        + (agent.IsFulfilled ? 2 : 0)
                        + (agent.IsFollowing ? 4 : 0)
                        + (agent.TaskDone ? 8 : 0);
                    h = h * 31 + (agent.GivenName != null ? agent.GivenName.GetHashCode() : 0);
                    h = h * 31 + agent.StatSignature; // training raises stats live
                }
                return h;
            }
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        private void RebuildSpeciesTab()
        {
            ClearChildren(_leftColumn);
            ClearChildren(_rightPane);

            var manager = SpiritManager.Instance;
            if (manager == null)
            {
                AddLine(_rightPane, "(no spirits stir yet)", 22, Muted);
                return;
            }

            var known = manager.KnownSpecies;
            if (known == null || known.Count == 0)
            {
                AddLine(_rightPane, "(no spirits stir yet)", 22, Muted);
                return;
            }

            // Left column: one row-button per species.
            SpiritSpeciesDefinition selected = null;
            for (int i = 0; i < known.Count; i++)
            {
                var def = known[i];
                if (def == null) continue;

                if (_selectedSpeciesId == null) _selectedSpeciesId = def.id;
                if (def.id == _selectedSpeciesId) selected = def;

                var discovery = manager.GetDiscovery(def.id);
                string label = discovery >= SpiritManager.DiscoveryLevel.Seen
                    ? def.displayName
                    : "???";

                string id = def.id; // capture for the closure
                MakeRowButton(_leftColumn, label, def.id == _selectedSpeciesId,
                    () => { _selectedSpeciesId = id; Rebuild(); });
            }

            // Right pane: the selected species, revealed by discovery level.
            if (selected == null) return;
            var level = manager.GetDiscovery(selected.id);

            if (level == SpiritManager.DiscoveryLevel.Unseen)
            {
                // Muscle 06: an undiscovered cryptid is a "???" silhouette page
                // with a hint slot for the rumors heard so far.
                if (manager.IsWovenSpecies(selected.id))
                {
                    AddPortrait(_rightPane, selected.bodySprite, new Color(0.05f, 0.05f, 0.08f, 1f));
                    AddLine(_rightPane, "???", 30, Cream);
                    AddLine(_rightPane, "A thread nobody has pulled yet. It will take two.", 20, Muted);
                    AddRumorLines(_rightPane, selected.id, true);
                    return;
                }

                AddLine(_rightPane, "??? - nothing is known.", 24, Muted);
                return;
            }

            // Seen and beyond: portrait first. A merely-Seen species stays a
            // near-black silhouette mystery; Visited and beyond show full color.
            AddPortrait(_rightPane, selected.bodySprite,
                level >= SpiritManager.DiscoveryLevel.Visited
                    ? Color.white
                    : new Color(0.12f, 0.12f, 0.16f, 1f));

            AddLine(_rightPane, selected.displayName, 30, Cream);
            if (!string.IsNullOrEmpty(selected.flavor))
                AddLine(_rightPane, selected.flavor, 20, Muted);

            // Muscle 02: biome feelings are learned by meeting the species (Visited and beyond).
            if (level >= SpiritManager.DiscoveryLevel.Visited)
                AddTwoLine(_rightPane, "Biomes: " + BiomeAffinity.Describe(selected), 20, Cream);

            // Muscle 06: a cryptid has no gates - its page tells who it was woven from.
            bool woven = manager.IsWovenSpecies(selected.id);
            if (woven)
            {
                AddWovenLines(_rightPane, selected.id);
            }
            else
            {
                AddLine(_rightPane, "Appears when:", 22, Cream);
                AddConditionLines(_rightPane, selected.gateChain != null ? selected.gateChain.appear : null);
            }

            if (level >= SpiritManager.DiscoveryLevel.Visited && !woven)
            {
                AddLine(_rightPane, "Visits when:", 22, Cream);
                AddConditionLines(_rightPane, selected.gateChain != null ? selected.gateChain.visit : null);

                // Muscle 11: nobody is talked into staying. The Stay gate is the whole list.
                AddLine(_rightPane, "Stays when:", 22, Cream);
                AddConditionLines(_rightPane, StayGate.SetOf(selected));
                AddTwoLine(_rightPane,
                    "It decides on its own. Gifts of " + selected.favoredFoodId + " are welcome, never required.",
                    18, Muted);
            }

            if (level >= SpiritManager.DiscoveryLevel.Resident)
            {
                AddLine(_rightPane,
                    "Resident count: " + manager.CountResidents(selected.id) + "/" + selected.maxResidents,
                    22, Cream);
                AddLine(_rightPane, "Fulfilment: a home, a full spirit, and a final wish", 22, Cream);
                if (!string.IsNullOrEmpty(selected.taskDescription))
                    AddLine(_rightPane, "Their wish: " + selected.taskDescription, 20, Muted);
            }
        }

        /// <summary>Fixed-size species portrait at the top of the detail pane.</summary>
        private static void AddPortrait(RectTransform pane, Sprite sprite, Color tint)
        {
            if (sprite == null) return; // no art yet: just skip the row

            // Wrapper row so the layout group doesn't stretch the image.
            var row = new GameObject("PortraitRow").AddComponent<RectTransform>();
            row.SetParent(pane, false);
            row.sizeDelta = new Vector2(0f, 88f);

            var imgGo = new GameObject("Portrait");
            var imgRt = imgGo.AddComponent<RectTransform>();
            imgRt.SetParent(row, false);
            imgRt.anchorMin = imgRt.anchorMax = new Vector2(0f, 0.5f);
            imgRt.pivot = new Vector2(0f, 0.5f);
            imgRt.sizeDelta = new Vector2(80f, 80f);
            imgRt.anchoredPosition = Vector2.zero;

            var img = imgGo.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.color = tint;
            img.raycastTarget = false;
        }

        /// <summary>
        /// Muscle 06 weave section of a discovered cryptid's species page:
        /// every weave that made one ("Woven from X the wrabbit and Y the
        /// bansheep") plus any rumors heard before it was found.
        /// </summary>
        private static void AddWovenLines(RectTransform pane, string speciesId)
        {
            AddLine(pane, "Woven at the Loom", 22, Cream);

            var archive = WeaveArchive.Instance;
            var weaves = archive != null ? archive.WeavesFor(speciesId) : null;
            if (weaves == null || weaves.Count == 0)
            {
                AddLine(pane, "(no thread remembers how)", 20, Muted);
            }
            else
            {
                int first = Mathf.Max(0, weaves.Count - 4); // latest few
                for (int i = first; i < weaves.Count; i++)
                    AddTwoLine(pane, "Woven from " + WovenFromText(weaves[i]), 20, Muted);
            }

            AddRumorLines(pane, speciesId, false);
        }

        /// <summary>"Pip the wrabbit and Wisp the bansheep".</summary>
        private static string WovenFromText(WeaveArchive.WeaveRecord r) =>
            r.parentAName + " the " + r.parentASpeciesName.ToLowerInvariant()
            + " and " + r.parentBName + " the " + r.parentBSpeciesName.ToLowerInvariant();

        /// <summary>The hint slot: rumors heard about a cryptid (or a "none yet" line when asked to).</summary>
        private static void AddRumorLines(RectTransform pane, string speciesId, bool showEmpty)
        {
            var archive = WeaveArchive.Instance;
            var rumors = archive != null ? archive.RumorsFor(speciesId) : null;
            if (rumors == null || rumors.Count == 0)
            {
                if (showEmpty) AddLine(pane, "Rumors: none heard yet.", 20, Muted);
                return;
            }

            AddLine(pane, "Rumors heard:", 20, Gold);
            for (int i = 0; i < rumors.Count; i++)
                AddTwoLine(pane, "\"" + rumors[i].text + "\" - " + rumors[i].teller, 18, Muted);
        }

        /// <summary>AddLine with room for two wrapped lines.</summary>
        private static Text AddTwoLine(RectTransform parent, string content, int size, Color color)
        {
            var text = AddLine(parent, content, size, color);
            text.rectTransform.sizeDelta = new Vector2(0f, (size + 6f) * 2f + 4f);
            return text;
        }

        private void AddConditionLines(RectTransform pane, Requirements.RequirementSet set)
        {
            if (set == null)
            {
                AddLine(pane, "* (this gate is closed)", 20, Muted);
                return;
            }

            var conditions = set.Conditions;
            int shown = 0;
            if (conditions != null)
            {
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i] == null) continue;
                    AddLine(pane, "* " + conditions[i].Describe(), 20, Muted);
                    shown++;
                }
            }
            if (shown == 0)
                AddLine(pane, "* (always)", 20, Muted);
        }

        private void RebuildResidentsTab()
        {
            ClearChildren(_residentsLeftColumn);

            var manager = SpiritManager.Instance;
            var spirits = manager != null ? manager.AllSpirits : null;
            int listed = 0;
            bool selectedStillListed = false;

            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent == null) continue;

                    string name = !string.IsNullOrEmpty(agent.GivenName)
                        ? agent.GivenName
                        : (agent.Species != null ? agent.Species.displayName : "Spirit");

                    if (agent.State == SpiritState.Resident)
                    {
                        string species = agent.Species != null ? agent.Species.displayName : "?";
                        string mood = agent.Hunger01 > 0.75f ? "Hungry" : "Fed";
                        string line = name + " - " + species + " - Spirit "
                            + Mathf.RoundToInt(agent.Spirit) + "% - " + mood;
                        if (agent.CompetitionEntries > 0)
                            line += " - Wins " + agent.CompetitionWins + "/" + agent.CompetitionEntries;
                        if (!agent.HasHome) line += " (homeless)";

                        Color lineColor = Cream;
                        if (agent.IsFulfilled)
                        {
                            line += " (FULFILLED - lead them to the Ascension Pad!)";
                            lineColor = Gold;
                        }
                        else if (agent.IsFollowing)
                        {
                            line += " (following)";
                        }

                        var picked = agent; // capture for the click closure
                        if (picked == _selectedAgent) selectedStillListed = true;
                        MakeAgentRowButton(_residentsLeftColumn, line, lineColor,
                            picked == _selectedAgent, () => SelectResident(picked));
                        listed++;
                    }
                    else if (agent.State == SpiritState.Runaway)
                    {
                        var picked = agent; // capture for the click closure
                        if (picked == _selectedAgent) selectedStillListed = true;
                        MakeAgentRowButton(_residentsLeftColumn,
                            name + " RAN AWAY - find them at the border!", Danger,
                            picked == _selectedAgent, () => SelectResident(picked));
                        listed++;
                    }
                }
            }

            if (listed == 0)
                AddLine(_residentsLeftColumn, "(no residents yet)", 22, Muted);

            // Selection follows the live agent; a dead/ascended/despawned one
            // clears the detail pane.
            if (!selectedStillListed) _selectedAgent = null;

            RebuildResidentDetail();
        }

        /// <summary>
        /// Row click (Residents tab): select the agent and rebuild the tab in
        /// place (row tints + detail pane). The journal stays open.
        /// </summary>
        private void SelectResident(SpiritAgent agent)
        {
            _selectedAgent = agent;
            RebuildResidentsTab();
        }

        // ---------------------------------------------------- Resident detail

        /// <summary>
        /// Structural rebuild of the right-hand detail pane for the selected
        /// resident: a compact, read-only mirror of SpiritInfoUI's content
        /// (no Rename, no Close - it lives inside the journal). Values are
        /// then kept fresh by RefreshResidentDetailValues on the 0.5s tick.
        /// </summary>
        private void RebuildResidentDetail()
        {
            ClearChildren(_residentDetailPane);

            _resStateText = null;
            _resSpiritText = null;
            _resSpiritFillRt = null;
            _resSpiritFill = null;
            _resHungerText = null;
            _resGroundText = null;
            _resFameText = null;
            _resCheckHomeText = null;
            _resCheckSpiritText = null;
            _resCheckWishText = null;

            var agent = _selectedAgent;
            if (agent == null)
            {
                AddLine(_residentDetailPane, "(select a resident)", 22, Muted);
                return;
            }

            var species = agent.Species;

            // Portrait in the species' own tint (residents are fully known).
            if (species != null)
                AddPortrait(_residentDetailPane, species.bodySprite, species.tint);

            string name = !string.IsNullOrEmpty(agent.GivenName)
                ? agent.GivenName
                : (species != null ? species.displayName : "Spirit");
            AddLine(_residentDetailPane, name, 28, Cream);

            // Species + state line (text filled by the value refresh).
            _resStateText = AddLine(_residentDetailPane, "", 22, Cream);

            if (species != null && !string.IsNullOrEmpty(species.flavor))
            {
                var flavor = AddLine(_residentDetailPane, species.flavor, 20, Muted);
                flavor.verticalOverflow = VerticalWrapMode.Truncate;
                flavor.rectTransform.sizeDelta = new Vector2(0f, 78f); // ~3 lines
            }

            _resSpiritText = AddLine(_residentDetailPane, "", 22, Cream);
            _resSpiritFill = UIStyle.MakeBar(_residentDetailPane, "SpiritBar",
                out _resSpiritFillRt, Gold);
            var barRt = (RectTransform)_resSpiritFill.transform.parent;
            barRt.sizeDelta = new Vector2(0f, 20f);
            barRt.GetComponent<Image>().raycastTarget = false;
            _resSpiritFill.raycastTarget = false;

            _resHungerText = AddLine(_residentDetailPane, "", 22, Cream);

            // Muscle 02: what this species loves / hates, and how its ground feels right now.
            AddTwoLine(_residentDetailPane, "Biomes: " + BiomeAffinity.Describe(species), 20, Muted);
            _resGroundText = AddLine(_residentDetailPane, "", 22, Cream);

            AddLine(_residentDetailPane, "Nature", 24, Gold);
            AddNatureRow(_residentDetailPane, agent, SpiritStat.Vigor);
            AddNatureRow(_residentDetailPane, agent, SpiritStat.Grace);
            AddNatureRow(_residentDetailPane, agent, SpiritStat.Gleam);

            // Traits (muscle 05): names + one flavor line each.
            if (agent.Traits.Count > 0)
            {
                AddLine(_residentDetailPane, "Traits: " + SpiritTraits.Describe(agent.Traits), 22, Gold);
                var traitLines = AddLine(_residentDetailPane, SpiritTraits.FlavorLines(agent.Traits), 20, Muted);
                traitLines.verticalOverflow = VerticalWrapMode.Truncate;
                traitLines.rectTransform.sizeDelta = new Vector2(0f, 26f * agent.Traits.Count * 2f); // ~2 lines each
            }

            // Muscle 06: a woven cryptid's page records the parents it was woven from.
            if (species != null && SpiritManager.Instance != null
                && SpiritManager.Instance.IsWovenSpecies(species.id) && WeaveArchive.Instance != null)
            {
                var weave = WeaveArchive.Instance.RecordFor(agent);
                if (weave != null)
                    AddTwoLine(_residentDetailPane, "Woven from " + WovenFromText(weave), 20, Muted);
            }

            // Competitions line (hidden until the first entry).
            if (agent.CompetitionEntries > 0)
                _resFameText = AddLine(_residentDetailPane, "", 22, Cream);

            // Fulfilment checklist (Residents only).
            if (agent.State == SpiritState.Resident)
            {
                AddLine(_residentDetailPane, "Fulfilment", 24, Gold);
                _resCheckHomeText = AddLine(_residentDetailPane, "", 22, Cream);
                _resCheckSpiritText = AddLine(_residentDetailPane, "", 22, Cream);
                _resCheckWishText = AddLine(_residentDetailPane, "", 22, Cream);

                if (!agent.TaskDone && species != null
                    && !string.IsNullOrEmpty(species.taskDescription))
                {
                    var wish = AddLine(_residentDetailPane, species.taskDescription, 20, Muted);
                    wish.verticalOverflow = VerticalWrapMode.Truncate;
                    wish.rectTransform.sizeDelta = new Vector2(0f, 78f); // ~3 lines
                }
            }

            RefreshResidentDetailValues();
        }

        /// <summary>
        /// Value-only refresh of the kept detail-pane references. Structural
        /// changes (state, first competition entry, wish granted) are covered
        /// by the residents signature, which triggers a full tab rebuild.
        /// </summary>
        private void RefreshResidentDetailValues()
        {
            var agent = _selectedAgent;
            if (agent == null) return;

            if (_resStateText != null)
            {
                string speciesName = agent.Species != null
                    && !string.IsNullOrEmpty(agent.Species.displayName)
                    ? agent.Species.displayName : "Spirit";
                _resStateText.text = speciesName + " - " + StateSuffix(agent);
            }

            int pct = Mathf.RoundToInt(Mathf.Clamp(agent.Spirit, 0f, 100f));
            float frac = Mathf.Clamp01(agent.Spirit / 100f);
            if (_resSpiritText != null)
                _resSpiritText.text = "Spirit " + pct + "%";
            if (_resSpiritFillRt != null)
            {
                _resSpiritFillRt.anchorMax = new Vector2(frac, 1f);
                _resSpiritFillRt.offsetMax = new Vector2(frac >= 1f ? -3f : 0f, -3f);
            }
            if (_resSpiritFill != null)
                _resSpiritFill.color = Color.Lerp(Danger, Gold, frac);

            if (_resHungerText != null)
                _resHungerText.text = "Hunger: " + HungerWord(agent.Hunger01);

            if (_resGroundText != null)
                _resGroundText.text = agent.GroundBase < 0
                    ? "Ground: beyond the fences"
                    : "Ground: " + BiomeAffinity.BiomeName(agent.GroundBiome)
                        + " - " + BiomeAffinity.GroundFeeling(agent.GroundAffinity);

            if (_resFameText != null)
                _resFameText.text = "Competitions: " + agent.CompetitionWins + " wins / "
                    + agent.CompetitionEntries + " entries";

            if (_resCheckHomeText != null)
                _resCheckHomeText.text = Check(agent.HasHome) + " Home";
            if (_resCheckSpiritText != null)
                _resCheckSpiritText.text = Check(agent.Spirit >= 100f) + " Full spirit";
            if (_resCheckWishText != null)
                _resCheckWishText.text = Check(agent.TaskDone) + " Final wish";
        }

        /// <summary>Nature stat row: "Vigor 4 (3-7)" (roll + species band) + fixed-width mini bar.</summary>
        private static void AddNatureRow(RectTransform parent, SpiritAgent agent, SpiritStat stat)
        {
            string label = SpiritStats.Label(stat);
            int value = agent.GetStat(stat);
            var row = new GameObject("Stat_" + label).AddComponent<RectTransform>();
            row.SetParent(parent, false);
            row.sizeDelta = new Vector2(0f, 24f);

            var text = UIRoot.MakeText(row, "Label", 22, TextAnchor.MiddleLeft, Cream);
            text.text = SpiritStats.BandText(agent.Species, stat, value);
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

        private const int LegacyPerPage = 9;

        /// <summary>
        /// Legacy Gallery (muscle 04). Left: the crossed, one row each (paged).
        /// Right: the selected spirit's page - name, species, days on the farm,
        /// how it crossed, its final wish, and whether its stone rests in the
        /// garden or still waits. The "Woven away" section (muscle 06) sits
        /// under the page and stays visible whatever is selected.
        /// </summary>
        private void RebuildLegacyTab()
        {
            ClearChildren(_legacyListColumn);
            ClearChildren(_legacyDetailPane);

            var registry = HeadstoneRegistry.Instance;
            var stones = new System.Collections.Generic.List<Headstone>();
            if (registry != null)
            {
                var all = registry.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null) stones.Add(all[i]);
            }
            int listed = stones.Count;

            // Selection: keep it if still valid, else the most recent crossing.
            int selIndex = _selectedStone != null ? stones.IndexOf(_selectedStone) : -1;
            if (selIndex < 0 && listed > 0)
            {
                selIndex = listed - 1;
                _selectedStone = stones[selIndex];
                _legacyPage = selIndex / LegacyPerPage;
            }
            if (listed == 0) _selectedStone = null;

            int pages = Mathf.Max(1, (listed + LegacyPerPage - 1) / LegacyPerPage);
            _legacyPage = Mathf.Clamp(_legacyPage, 0, pages - 1);

            if (listed > 0)
            {
                int waiting = 0;
                for (int i = 0; i < listed; i++)
                    if (!stones[i].Placed) waiting++;

                AddLine(_legacyListColumn, "The memorial garden", 26, Gold);
                AddLine(_legacyListColumn,
                    listed + (listed == 1 ? " name remembered" : " names remembered")
                        + (waiting > 0 ? " (" + waiting + " stone" + (waiting == 1 ? "" : "s") + " waiting)" : ""),
                    20, Muted);

                int first = _legacyPage * LegacyPerPage;
                int last = Mathf.Min(listed, first + LegacyPerPage);
                for (int i = first; i < last; i++)
                {
                    var picked = stones[i]; // capture for the click closure
                    MakeAgentRowButton(_legacyListColumn,
                        picked.SpiritName + " the " + picked.SpeciesDisplay
                            + " - Day " + picked.AscendedDay,
                        picked.Placed ? Cream : Gold, picked == _selectedStone,
                        () => { _selectedStone = picked; RebuildLegacyTab(); });
                }

                if (pages > 1)
                {
                    AddLine(_legacyListColumn, "Page " + (_legacyPage + 1) + " of " + pages, 20, Muted);
                    if (_legacyPage > 0)
                        MakeRowButton(_legacyListColumn, "< Earlier", false,
                            () => { _legacyPage--; RebuildLegacyTab(); });
                    if (_legacyPage < pages - 1)
                        MakeRowButton(_legacyListColumn, "Later >", false,
                            () => { _legacyPage++; RebuildLegacyTab(); });
                }
            }

            if (_selectedStone != null)
                AddStonePage(_legacyDetailPane, _selectedStone);

            // Muscle 06: the woven-away are remembered here too (garden stones are
            // for the crossed; the woven live on as a cryptid and a banner).
            var archive = WeaveArchive.Instance;
            var weaves = archive != null ? archive.Weaves : null;
            int woven = 0;
            if (weaves != null && weaves.Count > 0)
            {
                if (_selectedStone != null) AddLine(_legacyDetailPane, "", 14, Muted);
                AddLine(_legacyDetailPane, "Woven away", 24, Gold);
                for (int i = 0; i < weaves.Count; i++)
                {
                    var w = weaves[i];
                    string child = !string.IsNullOrEmpty(w.childName) ? w.childName : w.childSpeciesName;
                    AddLine(_legacyDetailPane,
                        w.parentAName + " the " + w.parentASpeciesName + " and "
                            + w.parentBName + " the " + w.parentBSpeciesName
                            + " - woven into " + child + ", Day " + w.day,
                        20, Cream);
                    woven++;
                }
            }

            if (listed == 0 && woven == 0)
                AddLine(_legacyListColumn, "(no one has moved on yet)", 22, Muted);
        }

        /// <summary>One crossed spirit's gallery page (owner rule: the page only
        /// says that they crossed - nothing more about the crossing itself).</summary>
        private static void AddStonePage(RectTransform pane, Headstone stone)
        {
            AddLine(pane, stone.SpiritName, 32, Gold);
            AddLine(pane, "the " + stone.SpeciesDisplay, 24, Cream);
            AddLine(pane, "Days on the farm: " + stone.DaysAmongUs.ToString("0.#"), 22, Cream);
            AddLine(pane, "Fed " + stone.TimesFed + " times.", 22, Cream);
            AddTwoLine(pane, CrossedText(stone), 22, Cream);

            var mgr = SpiritManager.Instance;
            var species = mgr != null ? mgr.FindSpecies(stone.SpeciesId) : null;
            if (species != null && !string.IsNullOrEmpty(species.taskDescription))
                AddTwoLine(pane, "Their last wish, granted: " + species.taskDescription, 20, Muted);

            if (stone.Placed)
            {
                string ground = MemorialGarden.Describe(stone);
                AddTwoLine(pane, "Their stone rests in the garden"
                    + (ground.Length > 0 ? " - " + ground : "") + ".", 20, Muted);
            }
            else
            {
                AddTwoLine(pane,
                    "Their stone has not been placed yet. It waits where it was left - "
                        + "select it and choose Move stone to give it a resting place.",
                    20, Gold);
            }
        }

        private static string CrossedText(Headstone stone)
        {
            string s = "Crossed over on Day " + stone.AscendedDay;
            float h = stone.CrossedHour;
            if (h >= 0f)
            {
                s += h >= 5f && h < 8f ? ", at dawn"
                    : h >= 8f && h < 17f ? ", in the daylight"
                    : h >= 17f && h < 20f ? ", at dusk"
                    : ", in the night";
            }
            if (stone.Witnesses > 0)
                s += ", with " + stone.Witnesses + (stone.Witnesses == 1 ? " neighbour" : " neighbours") + " watching";
            return s + ".";
        }

        // --------------------------------------------------------- List pieces

        private static Text AddLine(RectTransform parent, string content, int size, Color color)
        {
            var text = UIRoot.MakeText(parent, "Line", size, TextAnchor.UpperLeft, color);
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.rectTransform.sizeDelta = new Vector2(0f, size + 10f);
            return text;
        }

        /// <summary>
        /// Clickable resident/runaway row (UIStyle button). The selected row
        /// gets the dim-gold base, like species rows.
        /// </summary>
        private static void MakeAgentRowButton(RectTransform parent, string label,
            Color labelColor, bool selected, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Row_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 40f);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button, selected ? UIStyle.GoldDim : UIStyle.PanelBgLight);
            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", 22, TextAnchor.MiddleLeft, labelColor);
            text.text = label;
            // The row now lives in a fixed-width left column: clip instead of
            // spilling over the detail pane.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 0f);
            textRt.offsetMax = new Vector2(-12f, 0f);
        }

        private static void MakeRowButton(RectTransform parent, string label, bool selected,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Row_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 40f);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            // Selected species row gets the dim-gold base; the rest a darker inset.
            UIStyle.StyleButton(button, selected ? UIStyle.GoldDim : UIStyle.PanelBgLight);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", 22, TextAnchor.MiddleLeft, Cream);
            text.text = label;
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 0f);
            textRt.offsetMax = Vector2.zero;
        }
    }
}

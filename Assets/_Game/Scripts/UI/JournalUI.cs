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
        private RectTransform _legacyPane;
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
        private Text _resFameText;
        private Text _resCheckHomeText;
        private Text _resCheckSpiritText;
        private Text _resCheckWishText;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.jKey.wasPressedThisFrame
                && !AnimalFarm.Core.UIInputLock.TextInputActive)
                Toggle();

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

            if (GameInput.Instance != null)
            {
                if (_open)
                {
                    GameInput.Instance.SetGameplayBlocked(true);
                }
                else
                {
                    // Don't hand input back if the pause menu still needs it blocked.
                    bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                    if (!paused) GameInput.Instance.SetGameplayBlocked(false);
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
            _legacyPane = MakeColumn(panelRt, "LegacyPane");
            _legacyPane.anchorMin = Vector2.zero;
            _legacyPane.anchorMax = Vector2.one;
            _legacyPane.pivot = new Vector2(0f, 1f);
            _legacyPane.offsetMin = new Vector2(24f, 20f);
            _legacyPane.offsetMax = new Vector2(-24f, -120f);

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

            AddLine(_rightPane, "Appears when:", 22, Cream);
            AddConditionLines(_rightPane, selected.gateChain != null ? selected.gateChain.appear : null);

            if (level >= SpiritManager.DiscoveryLevel.Visited)
            {
                AddLine(_rightPane, "Visits when:", 22, Cream);
                AddConditionLines(_rightPane, selected.gateChain != null ? selected.gateChain.visit : null);

                AddLine(_rightPane,
                    "Befriend: feed " + selected.favoredFoodId + " x" + selected.residencyFoodCount,
                    22, Cream);
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

            AddLine(_residentDetailPane, "Nature", 24, Gold);
            AddNatureRow(_residentDetailPane, "Vigor", agent.Vigor);
            AddNatureRow(_residentDetailPane, "Grace", agent.Grace);
            AddNatureRow(_residentDetailPane, "Gleam", agent.Gleam);

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

        /// <summary>Nature stat row: "Vigor 7/9" label + fixed-width mini bar.</summary>
        private static void AddNatureRow(RectTransform parent, string label, int value)
        {
            var row = new GameObject("Stat_" + label).AddComponent<RectTransform>();
            row.SetParent(parent, false);
            row.sizeDelta = new Vector2(0f, 24f);

            var text = UIRoot.MakeText(row, "Label", 22, TextAnchor.MiddleLeft, Cream);
            text.text = label + " " + value + "/9";
            var textRt = text.rectTransform;
            textRt.anchorMin = new Vector2(0f, 0f);
            textRt.anchorMax = new Vector2(0f, 1f);
            textRt.pivot = new Vector2(0f, 0.5f);
            textRt.sizeDelta = new Vector2(150f, 0f);
            textRt.anchoredPosition = Vector2.zero;

            const float barMax = 190f;

            // Rounded mini-bar (fixed width, right of the label).
            float frac = Mathf.Clamp01(value / 9f);
            var fill = UIStyle.MakeBar(row, "Bar", out var fillRt, StatBlue);
            var backRt = (RectTransform)fill.transform.parent;
            backRt.anchorMin = new Vector2(0f, 0.15f);
            backRt.anchorMax = new Vector2(0f, 0.85f);
            backRt.pivot = new Vector2(0f, 0.5f);
            backRt.sizeDelta = new Vector2(barMax, 0f);
            backRt.anchoredPosition = new Vector2(160f, 0f);
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

        private void RebuildLegacyTab()
        {
            ClearChildren(_legacyPane);

            var registry = HeadstoneRegistry.Instance;
            var stones = registry != null ? registry.All : null;
            int listed = 0;

            if (stones != null)
            {
                for (int i = 0; i < stones.Count; i++)
                {
                    var stone = stones[i];
                    if (stone == null) continue;

                    AddLine(_legacyPane,
                        stone.SpiritName + " the " + stone.SpeciesDisplay
                            + " - ascended Day " + stone.AscendedDay,
                        22, Cream);
                    listed++;
                }
            }

            if (listed == 0)
                AddLine(_legacyPane, "(no one has moved on yet)", 22, Muted);
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

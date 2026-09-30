using AnimalFarm.Competitions;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The Boulder Trial entry modal (slice 05). Opened by the CompetitionBoard:
    /// pick a difficulty, pick a resident, and the competition manager takes it
    /// from there. Gameplay input is blocked while open (DebugConsole pattern).
    /// The panel shell is built once; the rows are rebuilt on every Open.
    /// </summary>
    public class CompetitionEntryUI : MonoBehaviour
    {
        public static CompetitionEntryUI Instance { get; private set; }

        /// <summary>Difficulty picked on the most recent entry (0..2). The board reads this for prizes.</summary>
        public static int LastDifficulty;

        private static readonly string[] DifficultyNames = { "Gentle Slope", "Proper Hill", "The Mountain" };

        private GameObject _panel;
        private RectTransform _content; // rows rebuilt each Open
        private bool _open;
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

        /// <summary>Opens the entry modal (no-op while an event runs).</summary>
        public void Open()
        {
            if (_open) return;
            if (CompetitionManager.Instance == null || CompetitionManager.Instance.EventRunning) return;
            if (_panel == null) BuildPanel();

            _selectedDifficulty = 0;
            RebuildContent();

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling(); // render above the HUD
            _open = true;

            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            if (_panel != null) _panel.SetActive(false);

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        private void PickSpirit(SpiritAgent spirit)
        {
            int difficulty = _selectedDifficulty;
            LastDifficulty = difficulty;

            // Close (and restore input) FIRST - the manager re-blocks it itself.
            Close();

            if (spirit == null || CompetitionManager.Instance == null) return;
            CompetitionManager.Instance.StartBoulderTrial(spirit, difficulty);
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
            panelRt.sizeDelta = new Vector2(420f, 120f); // height grows via ContentSizeFitter

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
            title.text = "Boulder Trial";
            title.rectTransform.sizeDelta = new Vector2(0f, 48f);

            // Rows live in a nested column so the shell survives rebuilds.
            _content = new GameObject("Content").AddComponent<RectTransform>();
            _content.SetParent(panelRt, false);

            var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
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

            BuildDifficultyRow();
            BuildSpiritRows();
            MakeButton(_content, "Never mind", 56f, false, Close);
        }

        private void BuildDifficultyRow()
        {
            // Fixed-height row; the layout group splits the width across the
            // three buttons and the smaller font keeps the labels inside them
            // (they were overlapping at 1080p).
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

            for (int i = 0; i < DifficultyNames.Length; i++)
            {
                int diff = i; // capture for the click closure
                MakeButton(row, DifficultyNames[i], 46f, diff == _selectedDifficulty,
                    () => { _selectedDifficulty = diff; RebuildContent(); }, 19);
            }
        }

        private void BuildSpiritRows()
        {
            int listed = 0;
            var manager = SpiritManager.Instance;
            var spirits = manager != null ? manager.AllSpirits : null;

            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var agent = spirits[i];
                    if (agent == null || agent.State != SpiritState.Resident) continue;

                    string name = !string.IsNullOrEmpty(agent.GivenName)
                        ? agent.GivenName
                        : (agent.Species != null ? agent.Species.displayName : "Spirit");
                    string label = name + " - Spirit " + Mathf.RoundToInt(agent.Spirit) + "%";

                    var picked = agent; // capture for the click closure
                    MakeButton(_content, label, 56f, false, () => PickSpirit(picked));
                    listed++;
                }
            }

            if (listed == 0)
            {
                var empty = UIRoot.MakeText(_content, "Empty", 22, TextAnchor.MiddleCenter, UIStyle.Grey);
                empty.text = "(no residents to enter)";
                empty.rectTransform.sizeDelta = new Vector2(0f, 36f);
            }
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

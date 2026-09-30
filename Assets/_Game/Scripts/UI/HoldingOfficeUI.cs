using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Modal ledger for the Repo-man's holding office (slice 07). One button per
    /// held spirit: pay the fee (2x its species' favored food) to reclaim it.
    /// Gameplay input is blocked while open (SeedPickerUI pattern); the content
    /// is rebuilt on every open so the list always reflects RepoManManager.Held.
    /// Closing is via the buttons only - Escape is reserved for Pause.
    /// </summary>
    public class HoldingOfficeUI : MonoBehaviour
    {
        public static HoldingOfficeUI Instance { get; private set; }

        private GameObject _panel;
        private Text _status;
        private bool _open;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the office ledger (rebuilding the list from Held).</summary>
        public void Open()
        {
            if (_open) return;

            _open = true;
            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);

            Rebuild();
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            UIInputLock.ModalOpen = false;
            if (_panel != null) { Destroy(_panel); _panel = null; _status = null; }

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ------------------------------------------------------------------ UI

        /// <summary>Tears down and rebuilds the whole panel from the current Held list.</summary>
        private void Rebuild()
        {
            if (_panel != null) { Destroy(_panel); _panel = null; _status = null; }

            var root = UIRoot.GetRoot();

            _panel = new GameObject("HoldingOfficeUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(480f, 120f); // height grows via ContentSizeFitter

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
            title.text = "Holding Office";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var subtitle = UIRoot.MakeText(panelRt, "Subtitle", 18, TextAnchor.MiddleCenter, UIStyle.Grey);
            subtitle.text = "Repossessed spirits. Fees apply. The Repo-man sends his regards.";
            subtitle.rectTransform.sizeDelta = new Vector2(0f, 26f);

            UIStyle.MakeDivider(panelRt);

            BuildRows(panelRt);

            // Transient error line (filled by a failed reclaim, cleared next click).
            _status = UIRoot.MakeText(panelRt, "Status", 18, TextAnchor.MiddleCenter, UIStyle.Danger);
            _status.text = "";
            _status.rectTransform.sizeDelta = new Vector2(0f, 24f);

            var leave = UIStyle.MakeButton(panelRt, "Leave", Close);
            ((RectTransform)leave.transform).sizeDelta = new Vector2(0f, 56f);
        }

        private void BuildRows(Transform parent)
        {
            var manager = RepoManManager.Instance;
            var held = manager != null ? manager.Held : null;

            if (held == null || held.Count == 0)
            {
                var empty = UIRoot.MakeText(parent, "Empty", 22, TextAnchor.MiddleCenter, UIStyle.Grey);
                empty.text = "(no one is being held)";
                empty.rectTransform.sizeDelta = new Vector2(0f, 40f);
                return;
            }

            for (int i = 0; i < held.Count; i++)
            {
                var rec = held[i];
                string who = !string.IsNullOrEmpty(rec.givenName) ? rec.givenName : rec.speciesId;
                if (string.IsNullOrEmpty(who)) who = "Spirit";

                string food = null;
                if (SpiritManager.Instance != null)
                {
                    var species = SpiritManager.Instance.FindSpecies(rec.speciesId);
                    if (species != null) food = species.favoredFoodId;
                }
                if (string.IsNullOrEmpty(food)) food = "food";

                int index = i; // capture for the click closure
                var button = UIStyle.MakeButton(parent,
                    "Reclaim " + who + " - 2x " + food,
                    () => OnReclaimClicked(index), 24);
                ((RectTransform)button.transform).sizeDelta = new Vector2(0f, 56f);
            }
        }

        private void OnReclaimClicked(int index)
        {
            if (_status != null) _status.text = ""; // clear the previous error

            var manager = RepoManManager.Instance;
            if (manager == null) return;

            if (manager.TryReclaim(index, out string error))
            {
                var held = manager.Held;
                if (held == null || held.Count == 0) Close();
                else Rebuild();
            }
            else if (_status != null)
            {
                _status.text = string.IsNullOrEmpty(error) ? "The Repo-man shakes his head." : error;
            }
        }
    }
}

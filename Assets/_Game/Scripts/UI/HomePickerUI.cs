using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Player;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The BUILD MENU. Opened by the Hammer tool or the Build key (B). Lists
    /// every buildable thing the player has unlocked — currently one entry per
    /// spirit species with a home sprite that has been at least SEEN; future
    /// buildings join this list. Picking an entry closes the menu and enters
    /// placement mode (green/red ghost, click to place, right-click cancels)
    /// via SelectionController.BeginPlaceHome. The class keeps its historical
    /// name because the scene bootstrap references it. Homes are free until
    /// the economy slice.
    /// </summary>
    public class HomePickerUI : MonoBehaviour
    {
        public static HomePickerUI Instance { get; private set; }

        private GameObject _panel;
        private RectTransform _panelRt;
        private readonly List<SpiritSpeciesDefinition> _options = new List<SpiritSpeciesDefinition>();
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

        /// <summary>Opens the build menu (placement cell is chosen afterwards,
        /// in SelectionController's ghost placement mode).</summary>
        public void Open()
        {
            if (_open || SpiritManager.Instance == null) return;

            Rebuild();
            if (_options.Count == 0)
            {
                var player = GameObject.FindWithTag("Player");
                FloatingText.Show(
                    player != null ? player.transform.position + Vector3.up * 0.8f : Vector3.zero,
                    "(nothing to build yet)", UIStyle.Grey);
                return;
            }

            _panel.SetActive(true);
            _panelRt.SetAsLastSibling();
            _open = true;
            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null) GameInput.Instance.SetGameplayBlocked(true);
        }

        /// <summary>Legacy signature (old Build Home tool passed a cell); the
        /// cell is ignored — placement mode picks it now.</summary>
        public void Open(Vector2Int cell) => Open();

        private void Close()
        {
            if (!_open) return;
            _open = false;
            UIInputLock.ModalOpen = false;
            if (_panel != null) _panel.SetActive(false);

            if (GameInput.Instance != null)
            {
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        private void Pick(SpiritSpeciesDefinition species)
        {
            Close();
            if (species == null || SelectionController.Instance == null) return;

            // Hand off to ghost placement mode: highlightable area, click to
            // place, right-click to cancel.
            SelectionController.Instance.BeginPlaceHome(species);
        }

        private void Rebuild()
        {
            if (_panel == null)
            {
                _panelRt = UIStyle.MakePanel(UIRoot.GetRoot(), "BuildMenuUI", UIStyle.PanelBg);
                _panel = _panelRt.gameObject;
                _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
                _panelRt.pivot = new Vector2(0.5f, 0.5f);
                _panelRt.sizeDelta = new Vector2(360f, 120f);

                var layout = _panel.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(24, 24, 20, 20);
                layout.spacing = 10f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                var fitter = _panel.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                _panel.SetActive(false);
            }

            // Rebuild all rows fresh on every open.
            for (int i = _panel.transform.childCount - 1; i >= 0; i--)
                Destroy(_panel.transform.GetChild(i).gameObject);

            var title = UIRoot.MakeText(_panelRt, "Title", 30, TextAnchor.MiddleCenter, UIStyle.Cream);
            title.text = "Build";
            title.rectTransform.sizeDelta = new Vector2(0f, 42f);

            // Unlocked buildables: homes for every species at least SEEN.
            // (Future non-home buildings join this list.)
            _options.Clear();
            foreach (var s in SpiritManager.Instance.KnownSpecies)
            {
                if (s == null || s.homeSprite == null) continue;
                if (SpiritManager.Instance.GetDiscovery(s.id) < SpiritManager.DiscoveryLevel.Seen) continue;
                _options.Add(s);

                var picked = s;
                var b = UIStyle.MakeButton(_panelRt, picked.displayName + " Home", () => Pick(picked), 24);
                ((RectTransform)b.transform).sizeDelta = new Vector2(0f, 46f);
            }

            var cancel = UIStyle.MakeButton(_panelRt, "Never mind", Close, 22);
            ((RectTransform)cancel.transform).sizeDelta = new Vector2(0f, 42f);
        }
    }
}

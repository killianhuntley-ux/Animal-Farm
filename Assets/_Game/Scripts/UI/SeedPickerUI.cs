using System.Collections.Generic;
using System.Globalization;
using AnimalFarm.Core;
using AnimalFarm.Player;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Contextual planting menu ("Plant what here?"). Opened by
    /// PlantingInteractor when the shepherd interacts with an empty dirt cell.
    /// First option is "Grass" (sows the cell green instead of planting), then
    /// one button per seed species (with a small grey growth-time line) plus a
    /// "Nothing" cancel button; number keys 1-9 pick an option too (1 = Grass,
    /// crops from 2). Gameplay input is blocked while open (DebugConsole
    /// pattern). Closing is via the buttons only — Escape is reserved for
    /// Pause. The panel is built once on first open and reused.
    /// </summary>
    public class SeedPickerUI : MonoBehaviour
    {
        public static SeedPickerUI Instance { get; private set; }

        private static readonly Color PlantedColor = new Color(0.55f, 0.9f, 0.55f, 1f); // soft green (world floating text)

        private GameObject _panel;
        private readonly List<PlantSpecies> _options = new List<PlantSpecies>();
        private bool _open;
        private Vector2Int _cell;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_open) PollNumberKeys();
        }

        /// <summary>Opens the picker targeting the given (empty dirt) cell.</summary>
        public void Open(Vector2Int cell)
        {
            if (_open) return;
            if (_panel == null && !BuildPanel()) return;

            _cell = cell;
            _panel.SetActive(true);
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

        private void Pick(PlantSpecies species)
        {
            var cell = _cell;
            Close();

            if (species == null || PlantManager.Instance == null) return;

            // Seeds are FREE this slice — no inventory consumption.
            var plant = PlantManager.Instance.PlantSeed(species, cell);
            if (plant == null) return;

            if (TerrainGrid.Instance != null)
            {
                string label = !string.IsNullOrEmpty(species.displayName) ? species.displayName : species.id;
                FloatingText.Show(TerrainGrid.Instance.CellCenterWorld(cell), "Planted " + label, PlantedColor);
            }
        }

        /// <summary>"Grass" option: sows the (still empty dirt) cell green
        /// instead of planting a crop. Moved here from the old belt tool.</summary>
        private void PickGrass()
        {
            var cell = _cell;
            Close();

            var grid = TerrainGrid.Instance;
            if (grid == null) return;

            // Guards: the cell must still be plain dirt with no plant on it.
            if (grid.GetSurface(cell) != Surface.Dirt) return;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return;

            if (grid.SetSurface(cell, Surface.Grass))
                FloatingText.Show(grid.CellCenterWorld(cell), "Sowed grass", PlantedColor);
        }

        private void PollNumberKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || AnimalFarm.Core.UIInputLock.TextInputActive) return;

            // Index 0 = Grass, then the crop species (shifted down one).
            int total = Mathf.Min(_options.Count + 1, 9);
            for (int i = 0; i < total; i++)
            {
                // Key.Digit1..Digit9 are contiguous, so index off Digit1.
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    if (i == 0) PickGrass();
                    else Pick(_options[i - 1]);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ UI

        /// <summary>Builds the panel once. False if the ToolController isn't up yet.</summary>
        private bool BuildPanel()
        {
            var tools = FindFirstObjectByType<ToolController>();
            if (tools == null) return false;

            var root = UIRoot.GetRoot();

            _panel = new GameObject("SeedPickerUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(340f, 120f); // height grows via ContentSizeFitter

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
            title.text = "Plant what here?";
            title.rectTransform.sizeDelta = new Vector2(0f, 48f);

            _options.Clear();

            // First option: Grass (number key 1) — sows the ground itself.
            MakeOptionButton(panelRt, "Grass", "spreads green underfoot", PickGrass);

            var species = tools.SeedSpecies;
            if (species != null)
            {
                foreach (var s in species)
                {
                    if (s == null) continue;
                    _options.Add(s);

                    var picked = s; // capture for the click closure
                    MakeSeedButton(panelRt, picked, () => Pick(picked));
                }
            }

            MakeCancelButton(panelRt, "Nothing", Close);

            _panel.SetActive(false);
            return true;
        }

        private static void MakeSeedButton(Transform parent, PlantSpecies species, UnityEngine.Events.UnityAction onClick)
        {
            string label = !string.IsNullOrEmpty(species.displayName) ? species.displayName : species.id;

            int stages = species.stageSprites != null ? species.stageSprites.Length - 1 : 0;
            float hours = Mathf.Max(0, stages) * Mathf.Max(0f, species.hoursPerStage);
            string subLabel = hours > 0f
                ? "grows in " + hours.ToString("0.#", CultureInfo.InvariantCulture) + "h"
                : "grows instantly";

            MakeOptionButton(parent, label, subLabel, onClick);
        }

        /// <summary>Two-line option button (label + small grey sub-line).</summary>
        private static void MakeOptionButton(Transform parent, string label, string subLabel, UnityEngine.Events.UnityAction onClick)
        {
            var button = MakeButtonBase(parent, label, 68f, onClick);
            var rt = (RectTransform)button.transform;

            var text = UIRoot.MakeText(rt, "Label", 26, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;
            var textRt = text.rectTransform;
            textRt.anchorMin = new Vector2(0f, 0.4f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var sub = UIRoot.MakeText(rt, "SubLabel", 18, TextAnchor.MiddleCenter, UIStyle.Grey);
            sub.text = subLabel;
            var subRt = sub.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0f);
            subRt.anchorMax = new Vector2(1f, 0.4f);
            subRt.offsetMin = Vector2.zero;
            subRt.offsetMax = Vector2.zero;
        }

        private static void MakeCancelButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var button = MakeButtonBase(parent, label, 56f, onClick);

            var text = UIRoot.MakeText(button.transform, "Label", 26, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;

            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Button MakeButtonBase(Transform parent, string name, float height, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, height);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);

            button.onClick.AddListener(onClick);
            return button;
        }
    }
}

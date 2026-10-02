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
    /// crops from 2). Seeds are LIMITED ITEMS now (owner spec): every option
    /// row shows the owned packet count ("(x N)"), rows with none owned render
    /// disabled-grey with a "(buy at the vendor)" sub-line, and picking
    /// consumes one matching seed item (ids "seed_grass", "seed_&lt;speciesId&gt;")
    /// before planting/sowing. The panel is rebuilt on every open so counts
    /// stay fresh. Gameplay input is blocked while open (DebugConsole
    /// pattern). Closing is via the buttons only - Escape is reserved for
    /// Pause.
    /// </summary>
    public class SeedPickerUI : MonoBehaviour
    {
        public static SeedPickerUI Instance { get; private set; }

        private static readonly Color PlantedColor = new Color(0.55f, 0.9f, 0.55f, 1f); // soft green (world floating text)

        private GameObject _panel;
        private readonly List<UnityEngine.Events.UnityAction> _hotkeys = new List<UnityEngine.Events.UnityAction>(); // number-key actions, in row order
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

        /// <summary>Opens the picker targeting the given (empty dirt) cell.
        /// The panel is rebuilt every time so the seed counts are current.</summary>
        public void Open(Vector2Int cell)
        {
            if (_open) return;
            _cell = cell; // BuildPanel filters the options by this cell's surface
            if (!BuildPanel()) return;

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
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        /// <summary>Inventory item id for one species' seed packet.</summary>
        private static string SeedItemId(PlantSpecies species) => "seed_" + species.id;

        private const string GrassSeedId = "seed_grass";

        private void Pick(PlantSpecies species)
        {
            var cell = _cell;
            Close();

            if (species == null || PlantManager.Instance == null) return;

            var grid = TerrainGrid.Instance;
            Vector3 at = grid != null ? grid.CellCenterWorld(cell) : Vector3.zero;

            // Seeds are limited items: one packet per planting.
            string seedId = SeedItemId(species);
            var inv = Inventory.Instance;
            if (inv == null || !inv.Consume(seedId, 1))
            {
                FloatingText.Show(at, "(no seeds)", UIStyle.Danger);
                return;
            }

            var plant = PlantManager.Instance.PlantSeed(species, cell);
            if (plant == null)
            {
                inv.Add(seedId, 1); // owner law: never charge for nothing
                string why = "(it will not take here)";
                if (grid != null && species.shallowOnly && grid.GetSurface(cell) == Surface.Water
                    && !grid.IsShallowRim(cell))
                    why = "(too deep - try the shallows)";
                else if (PlantManager.Instance.HasPlantAt(cell))
                    why = "(something is already planted here)";
                FloatingText.Show(at, why, UIStyle.Danger);
                return;
            }

            string label = !string.IsNullOrEmpty(species.displayName) ? species.displayName : species.id;
            FloatingText.Show(at, "Planted " + label, PlantedColor);
            AnimalFarm.Core.ShepherdProgress.Grant("plant");
        }

        /// <summary>"Grass" option: sows the (still empty dirt) cell green
        /// instead of planting a crop. Consumes one "seed_grass" packet.</summary>
        private void PickGrass()
        {
            var cell = _cell;
            Close();

            var grid = TerrainGrid.Instance;
            if (grid == null) return;

            // Guards: the cell must still be plain dirt with no plant on it.
            if (grid.GetSurface(cell) != Surface.Dirt) return;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return;

            var inv = Inventory.Instance;
            if (inv == null || !inv.Consume(GrassSeedId, 1))
            {
                FloatingText.Show(grid.CellCenterWorld(cell), "(no seeds)", UIStyle.Danger);
                return;
            }

            if (grid.SetSurface(cell, Surface.Grass))
            {
                FloatingText.Show(grid.CellCenterWorld(cell), "Sowed grass", PlantedColor);
                AnimalFarm.Core.ShepherdProgress.Grant("sow");
            }
            else
            {
                inv.Add(GrassSeedId, 1); // owner law: never charge for nothing
            }
        }

        /// <summary>"Compost soil" option: works compost into the (still empty) dirt cell.</summary>
        private void PickCompost()
        {
            var cell = _cell;
            Close();

            var compost = CompostManager.Instance;
            var grid = TerrainGrid.Instance;
            if (compost == null || grid == null) return;

            if (!compost.TryEnrichSoil(cell))
                FloatingText.Show(grid.CellCenterWorld(cell), "(nothing to compost with)", UIStyle.Danger);
        }

        private void PollNumberKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || AnimalFarm.Core.UIInputLock.TextInputActive) return;

            // Rows in display order (Grass / Compost / crops, as the cell allows).
            int total = Mathf.Min(_hotkeys.Count, 9);
            for (int i = 0; i < total; i++)
            {
                // Key.Digit1..Digit9 are contiguous, so index off Digit1.
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    _hotkeys[i]?.Invoke();
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ UI

        /// <summary>(Re)builds the panel. Called on every open so the owned
        /// seed counts stay fresh. False if the ToolController isn't up yet.</summary>
        private bool BuildPanel()
        {
            var tools = FindFirstObjectByType<ToolController>();
            if (tools == null) return false;

            if (_panel != null)
            {
                Destroy(_panel);
                _panel = null;
            }

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

            _hotkeys.Clear();

            var inv = Inventory.Instance;
            var terrain = TerrainGrid.Instance;
            Surface here = terrain != null ? terrain.GetSurface(_cell) : Surface.Dirt;

            if (here == Surface.Dirt)
            {
                // First option: Grass (number key 1) - sows the ground itself.
                int grassOwned = inv != null ? inv.Count(GrassSeedId) : 0;
                MakeOptionButton(panelRt,
                    "Grass (x " + grassOwned + ")",
                    grassOwned > 0 ? "spreads green underfoot" : "(buy at the vendor)",
                    PickGrass, grassOwned > 0);
                _hotkeys.Add(PickGrass);
            }

            if (here == Surface.Dirt || here == Surface.Mud)
            {
                // Compost the empty soil (tilled dirt or rich mud): the next crop sown here carries it.
                var compost = CompostManager.Instance;
                bool enriched = compost != null && compost.IsEnriched(_cell);
                int compostOwned = compost != null ? compost.Available : 0;
                MakeOptionButton(panelRt,
                    "Compost soil (x " + compostOwned + ")",
                    enriched ? "already enriched"
                        : compostOwned > 0 ? "faster growth, finer crops"
                        : "(happy spirits leave it; weeds give fiber)",
                    PickCompost, compostOwned > 0 && !enriched);
                _hotkeys.Add(PickCompost);
            }

            // Crops: only species that grow on THIS cell's ground (dirt crops on
            // dirt; reeds and lilies on water).
            var species = tools.SeedSpecies;
            if (species != null)
            {
                foreach (var s in species)
                {
                    if (s == null || !s.GrowsOn(here)) continue;

                    var picked = s; // capture for the click closure
                    int owned = inv != null ? inv.Count(SeedItemId(picked)) : 0;
                    MakeSeedButton(panelRt, picked, owned, () => Pick(picked));
                    _hotkeys.Add(() => Pick(picked));
                }
            }

            // Terrain material: a sand load repaints open ground (Dirt / Scrub / Grass).
            int sandOwned = inv != null ? inv.Count(VendorUI.SandLoadId) : 0;
            bool paintable = here == Surface.Dirt || here == Surface.Scrub || here == Surface.Grass;
            if (paintable && sandOwned > 0)
            {
                MakeOptionButton(panelRt, "Sand load (x " + sandOwned + ")",
                    "turns this cell to arid sand", PickSand, true);
                _hotkeys.Add(PickSand);
            }

            // Rich mud (Mire Peddler): same open-ground rule as sand; always-wet swamp soil.
            int mudOwned = inv != null ? inv.Count(SwampVendor.MudLoadId) : 0;
            if (paintable && mudOwned > 0)
            {
                MakeOptionButton(panelRt, "Mud load (x " + mudOwned + ")",
                    "turns this cell to rich swamp mud: always moist, crops grow fine but never gleam", PickMud, true);
                _hotkeys.Add(PickMud);
            }
            if (here == Surface.Scrub || here == Surface.Grass) title.text = "Spread what here?";

            MakeCancelButton(panelRt, "Nothing", Close);

            _panel.SetActive(false);
            return true;
        }

        /// <summary>"Sand load" option: paints the (empty) cell as Sand. Consumes one load.</summary>
        private void PickSand()
        {
            var cell = _cell;
            Close();

            var grid = TerrainGrid.Instance;
            var inv = Inventory.Instance;
            if (grid == null || inv == null || !grid.IsUsable(cell)) return;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return;
            if (AnimalFarm.Spirits.Home.AnyAtCell(cell)) return;

            var surface = grid.GetSurface(cell);
            if (surface != Surface.Dirt && surface != Surface.Scrub && surface != Surface.Grass) return;
            if (!inv.Consume(VendorUI.SandLoadId, 1))
            {
                FloatingText.Show(grid.CellCenterWorld(cell), "(no sand)", UIStyle.Danger);
                return;
            }

            if (grid.SetSurface(cell, Surface.Sand))
                FloatingText.Show(grid.CellCenterWorld(cell), "Spread sand", PlantedColor);
            else
                inv.Add(VendorUI.SandLoadId, 1); // owner law: never charge for nothing
        }

        /// <summary>"Mud load" option: paints the (empty) cell as rich Mud. Consumes one load.</summary>
        private void PickMud()
        {
            var cell = _cell;
            Close();

            var grid = TerrainGrid.Instance;
            var inv = Inventory.Instance;
            if (grid == null || inv == null || !grid.IsUsable(cell)) return;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return;
            if (AnimalFarm.Spirits.Home.AnyAtCell(cell)) return;

            var surface = grid.GetSurface(cell);
            if (surface != Surface.Dirt && surface != Surface.Scrub && surface != Surface.Grass) return;
            if (!inv.Consume(SwampVendor.MudLoadId, 1))
            {
                FloatingText.Show(grid.CellCenterWorld(cell), "(no mud)", UIStyle.Danger);
                return;
            }

            if (grid.SetSurface(cell, Surface.Mud))
            {
                FloatingText.Show(grid.CellCenterWorld(cell), "Spread rich mud", PlantedColor);
                Puffs.Burst(grid.CellCenterWorld(cell), new Color(0.31f, 0.23f, 0.16f, 0.95f), 6, 1.0f);
            }
            else
                inv.Add(SwampVendor.MudLoadId, 1); // owner law: never charge for nothing
        }

        private static void MakeSeedButton(Transform parent, PlantSpecies species, int owned, UnityEngine.Events.UnityAction onClick)
        {
            string name = !string.IsNullOrEmpty(species.displayName) ? species.displayName : species.id;
            string label = name + " (x " + owned + ")";

            int stages = species.stageSprites != null ? species.stageSprites.Length - 1 : 0;
            float hours = Mathf.Max(0, stages) * Mathf.Max(0f, species.hoursPerStage);
            string subLabel = owned <= 0
                ? "(buy at the vendor)"
                : hours > 0f
                    ? "grows in " + hours.ToString("0.#", CultureInfo.InvariantCulture) + "h"
                    : "grows instantly";

            MakeOptionButton(parent, label, subLabel, onClick, owned > 0);
        }

        /// <summary>Two-line option button (label + small grey sub-line).
        /// Disabled rows (no seeds owned) render grey and ignore clicks.</summary>
        private static void MakeOptionButton(Transform parent, string label, string subLabel,
            UnityEngine.Events.UnityAction onClick, bool enabled)
        {
            var button = MakeButtonBase(parent, label, 68f, enabled ? onClick : null);
            button.interactable = enabled;
            var rt = (RectTransform)button.transform;

            var text = UIRoot.MakeText(rt, "Label", 26, TextAnchor.MiddleCenter,
                enabled ? UIStyle.Cream : UIStyle.Grey);
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

            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }
    }
}

using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
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

        private const int LoomCost = 60;      // playtest pricing
        private const int WaystoneCost = 15;  // playtest pricing
        private const int AscensionPadCost = 25; // muscle 04 pricing
        private const string CoinId = "coin"; // obols, in the fiction

        [Header("Structure visuals (assigned by bootstrapper; rows skip when null)")]
        [SerializeField] private Sprite loomSprite;
        [SerializeField] private Sprite waystoneSprite;
        [SerializeField] private Material spriteMaterial;

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
            if (_options.Count == 0 && loomSprite == null && waystoneSprite == null
                && !PadUnlocked && !TrainingBuildingManager.Unlocked && !TapestryBuildRows.AnyCarried)
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
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
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

            BuildStructureRows();
            TapestryBuildRows.Build(_panelRt, Close); // muscle 06: carried tapestry banners

            var cancel = UIStyle.MakeButton(_panelRt, "Never mind", Close, 22);
            ((RectTransform)cancel.transform).sizeDelta = new Vector2(0f, 42f);
        }

        // ---- structures (buildable Loom + Waystone + Ascension Pad) -----------

        /// <summary>Ascension Pad row visibility (muscle 04): only after the
        /// first spirit EVER reaches fulfilment. The manager owns the flag.</summary>
        private static bool PadUnlocked =>
            AscensionPadManager.Instance != null
            && AscensionPadManager.Instance.FulfilmentSeen;

        private void BuildStructureRows()
        {
            bool padUnlocked = PadUnlocked;
            bool trainUnlocked = TrainingBuildingManager.Unlocked;
            if (loomSprite == null && waystoneSprite == null && !padUnlocked && !trainUnlocked)
                return; // nothing structural to offer yet

            UIStyle.MakeDivider(_panelRt);
            var header = UIRoot.MakeText(_panelRt, "StructuresHeader", 20,
                TextAnchor.MiddleCenter, UIStyle.Grey);
            header.text = "Structures";
            header.rectTransform.sizeDelta = new Vector2(0f, 26f);

            if (loomSprite != null)
            {
                bool built = FindFirstObjectByType<TheLoom>() != null;
                MakeStructureRow(
                    built ? "The Loom (built)" : "The Loom - " + LoomCost + " obols",
                    "two become one stranger thing", PickLoom, !built);
            }

            if (waystoneSprite != null)
            {
                MakeStructureRow("Waystone - " + WaystoneCost + " obols",
                    "a stone that remembers", PickWaystone, true);
            }

            // Ascension Pad (muscle 04): unique; sprite is code-generated so
            // the row needs no bootstrapper wiring.
            if (padUnlocked)
            {
                bool built = AscensionPadManager.Instance.HasPad;
                MakeStructureRow(
                    built ? "Ascension Pad (built)" : "Ascension Pad - " + AscensionPadCost + " obols",
                    "where the river comes to meet them", PickAscensionPad, !built);
            }

            // Training grounds (muscle 05): spirits use these on their own.
            // Compact single-line rows - the menu is already tall.
            if (trainUnlocked)
            {
                var trainHeader = UIRoot.MakeText(_panelRt, "TrainingHeader", 20,
                    TextAnchor.MiddleCenter, UIStyle.Grey);
                trainHeader.text = "Training grounds (spirits use them on their own)";
                trainHeader.rectTransform.sizeDelta = new Vector2(0f, 26f);

                for (int i = 0; i < TrainingBuilding.Specs.Length; i++)
                {
                    var spec = TrainingBuilding.Specs[i];
                    bool full = TrainingBuildingManager.CountOf(spec.id) >= TrainingBuildingManager.MaxPerKind;
                    string label = full
                        ? spec.displayName + " (max built)"
                        : spec.displayName + " - " + spec.cost + " obols (" + SpiritStats.Label(spec.stat) + ")";
                    var b = UIStyle.MakeButton(_panelRt, label, () => PickTraining(spec), 20);
                    ((RectTransform)b.transform).sizeDelta = new Vector2(0f, 40f);
                    if (full) b.interactable = false;
                }
            }
        }

        private void PickTraining(TrainingSpec spec)
        {
            Close();
            if (spec == null || SelectionController.Instance == null) return;
            if (TrainingBuildingManager.CountOf(spec.id) >= TrainingBuildingManager.MaxPerKind) return;

            if (!TryPay(spec.cost)) return;

            SelectionController.Instance.BeginPlaceBuilding(TrainingBuilding.GetSprite(spec.id), spec.scale,
                cell => SelectionController.IsPlaceableCell(cell) && !TrainingBuilding.AnyAtCell(cell),
                (cell, world) =>
                {
                    TrainingBuilding.Create(spec, world);
                    Bleeps.Play(BleepKind.Build);
                    FloatingText.Show(world + Vector3.up * 1.2f, spec.displayName + " built", UIStyle.Gold);
                    ShepherdProgress.Grant("build");
                    AnimalFarm.World.VendorArrivals.Note("buildsPlaced"); // hidden vendor move-in milestone
                },
                () => Refund(spec.cost));
        }

        /// <summary>Button row with a small grey flavor sub-line; greyed out
        /// and non-interactable when <paramref name="enabled"/> is false.</summary>
        private void MakeStructureRow(string title, string sub,
            UnityEngine.Events.UnityAction onClick, bool enabled)
        {
            var b = UIStyle.MakeButton(_panelRt, title, onClick, 22);
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(0f, 58f);

            // Lift the main label clear of the sub-line.
            var main = b.GetComponentInChildren<Text>();
            if (main != null)
                main.rectTransform.offsetMin = new Vector2(0f, 16f);

            var subText = UIRoot.MakeText(rt, "Sub", 15, TextAnchor.MiddleCenter, UIStyle.Grey);
            subText.text = sub;
            var srt = subText.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.offsetMin = new Vector2(0f, 6f);
            srt.offsetMax = new Vector2(0f, 22f);

            if (!enabled)
            {
                b.interactable = false;
                if (main != null) main.color = UIStyle.Grey;
            }
        }

        private void PickLoom()
        {
            Close();
            if (loomSprite == null || SelectionController.Instance == null) return;
            if (FindFirstObjectByType<TheLoom>() != null) return; // one Loom only

            if (!TryPay(LoomCost)) return;

            var sprite = loomSprite;
            var mat = spriteMaterial;
            SelectionController.Instance.BeginPlaceBuilding(sprite, 1.6f,
                SelectionController.IsPlaceableCell,
                (cell, world) =>
                {
                    TheLoom.Create(world, sprite, mat);
                    Bleeps.Play(BleepKind.Build);
                    FloatingText.Show(world + Vector3.up * 1.2f, "The Loom is built", UIStyle.Gold);
                    ShepherdProgress.Grant("build");
                    AnimalFarm.World.VendorArrivals.Note("buildsPlaced"); // hidden vendor move-in milestone
                },
                () => Refund(LoomCost));
        }

        private void PickWaystone()
        {
            Close();
            if (waystoneSprite == null || SelectionController.Instance == null) return;

            if (!TryPay(WaystoneCost)) return;

            var sprite = waystoneSprite;
            var mat = spriteMaterial;
            SelectionController.Instance.BeginPlaceBuilding(sprite, 1f,
                SelectionController.IsPlaceableCell,
                (cell, world) =>
                {
                    Waystone.Create(world, sprite, mat);
                    Bleeps.Play(BleepKind.Build);
                    FloatingText.Show(world + Vector3.up * 1.2f, "Waystone raised", UIStyle.Gold);
                    ShepherdProgress.Grant("build");
                    AnimalFarm.World.VendorArrivals.Note("buildsPlaced"); // hidden vendor move-in milestone
                },
                () => Refund(WaystoneCost));
        }

        private void PickAscensionPad()
        {
            Close();
            if (SelectionController.Instance == null) return;
            if (AscensionPadManager.Instance == null
                || AscensionPadManager.Instance.HasPad) return; // one pad only

            if (!TryPay(AscensionPadCost)) return;

            SelectionController.Instance.BeginPlaceBuilding(AscensionPad.PlatformSprite, 1f,
                SelectionController.IsPlaceableCell,
                (cell, world) =>
                {
                    AscensionPad.Create(world);
                    Bleeps.Play(BleepKind.Build);
                    FloatingText.Show(world + Vector3.up * 1.2f,
                        "The pad is laid. The river will know.", UIStyle.Gold);
                    ShepherdProgress.Grant("build");
                    AnimalFarm.World.VendorArrivals.Note("buildsPlaced"); // hidden vendor move-in milestone
                },
                () => Refund(AscensionPadCost));
        }

        private static bool TryPay(int cost)
        {
            if (Inventory.Instance != null && Inventory.Instance.Consume(CoinId, cost))
                return true;

            Bleeps.Play(BleepKind.Denied, 0.8f);
            var player = GameObject.FindWithTag("Player");
            FloatingText.Show(
                player != null ? player.transform.position + Vector3.up * 0.8f : Vector3.zero,
                "(not enough obols)", UIStyle.Grey);
            return false;
        }

        /// <summary>Right-click cancel after paying: give the obols back.</summary>
        private static void Refund(int cost)
        {
            if (Inventory.Instance != null) Inventory.Instance.Add(CoinId, cost);
            var player = GameObject.FindWithTag("Player");
            FloatingText.Show(
                player != null ? player.transform.position + Vector3.up * 0.8f : Vector3.zero,
                "(obols returned)", UIStyle.Grey);
        }
    }
}

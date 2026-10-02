using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The Loom's pairing menu (slice 06). Pick two max-Spirit residents, see
    /// what the recipe would produce (undiscovered results stay mysterious),
    /// then hand the pair to TheLoom.RunWeave. Gameplay input is blocked while
    /// open (DebugConsole pattern); content rebuilds on open and on every
    /// selection change.
    /// </summary>
    public class WeaveUI : MonoBehaviour
    {
        public static WeaveUI Instance { get; private set; }

        private const float PanelWidth = 520f;
        private const float SlotButtonHeight = 40f;
        private static readonly Color SelectedLabel = new Color(0.13f, 0.11f, 0.07f, 1f);

        private GameObject _panel;
        private bool _open;
        private SpiritAgent _pickedA;
        private SpiritAgent _pickedB;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the weave menu as a modal.</summary>
        public void Open()
        {
            if (_open) return;
            _open = true;
            _pickedA = null;
            _pickedB = null;

            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);

            Rebuild();
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;

            if (_panel != null) { Destroy(_panel); _panel = null; }

            UIInputLock.ModalOpen = false;
            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        private void OnWeaveClicked()
        {
            var a = _pickedA;
            var b = _pickedB;
            Close();
            if (a == null || b == null) return;

            var loom = FindFirstObjectByType<TheLoom>();
            if (loom != null) loom.RunWeave(a, b);
        }

        // ------------------------------------------------------------------ data

        /// <summary>Residents at full Spirit -- the only weavable spirits.</summary>
        private static List<SpiritAgent> GatherEligible()
        {
            var list = new List<SpiritAgent>();
            if (SpiritManager.Instance == null) return list;

            var spirits = SpiritManager.Instance.AllSpirits;
            if (spirits == null) return list;

            for (int i = 0; i < spirits.Count; i++)
            {
                var agent = spirits[i];
                if (agent != null && agent.State == SpiritState.Resident && agent.Spirit >= 100f)
                    list.Add(agent);
            }
            return list;
        }

        private static string AgentLabel(SpiritAgent agent)
        {
            if (!string.IsNullOrEmpty(agent.GivenName)) return agent.GivenName;
            return agent.Species != null && !string.IsNullOrEmpty(agent.Species.displayName)
                ? agent.Species.displayName
                : "Spirit";
        }

        /// <summary>Result preview + whether the Weave button should be live.</summary>
        private void EvaluatePair(out bool canWeave, out string text, out Color color)
        {
            canWeave = false;
            text = "(choose two spirits)";
            color = UIStyle.Grey;
            if (_pickedA == null || _pickedB == null || SpiritManager.Instance == null) return;

            var recipe = SpiritManager.Instance.FindRecipe(_pickedA.Species, _pickedB.Species);
            if (recipe == null || recipe.result == null)
            {
                text = "These two will not entwine.";
                color = UIStyle.Danger;
                return;
            }

            // Essence toll (owner decision): the ritual consumes essence.
            int have = AnimalFarm.Core.Inventory.Instance != null
                ? AnimalFarm.Core.Inventory.Instance.Count("essence") : 0;
            string toll = $"  [toll: {recipe.essenceCost} essence, have {have}]";
            if (have < recipe.essenceCost)
            {
                text = "The loom demands more essence." + toll;
                color = UIStyle.Danger;
                return;
            }

            canWeave = true;
            if (SpiritManager.Instance.GetDiscovery(recipe.result.id)
                == SpiritManager.DiscoveryLevel.Unseen)
            {
                text = "Something stirs on the loom..." + toll;
                color = UIStyle.Grey;
            }
            else
            {
                text = (!string.IsNullOrEmpty(recipe.result.displayName)
                    ? recipe.result.displayName
                    : recipe.result.id) + toll;
                color = UIStyle.Gold;
            }
        }

        // ------------------------------------------------------------------ build

        private void Rebuild()
        {
            if (_panel != null) Destroy(_panel);

            var eligible = GatherEligible();

            // Drop stale picks (fed on/ran away/despawned since the last build).
            if (_pickedA == null || !eligible.Contains(_pickedA)) _pickedA = null;
            if (_pickedB == null || !eligible.Contains(_pickedB)) _pickedB = null;

            var root = UIRoot.GetRoot();
            var panelRt = UIStyle.MakePanel(root, "WeaveUI");
            _panel = panelRt.gameObject;
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(PanelWidth, 200f); // height via fitter

            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 18, 18);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = UIRoot.MakeText(panelRt, "Title", 32, TextAnchor.MiddleCenter, UIStyle.Gold);
            title.text = "The Loom";
            title.rectTransform.sizeDelta = new Vector2(0f, 40f);

            var sub = UIRoot.MakeText(panelRt, "Subtitle", 18, TextAnchor.MiddleCenter, UIStyle.Grey);
            sub.text = "Two willing spirits become one stranger thing. Both must be"
                     + " at full spirit. (They have both signed the waiver.)";
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            sub.rectTransform.sizeDelta = new Vector2(0f, 54f);

            UIStyle.MakeDivider(panelRt);

            if (eligible.Count < 2)
            {
                var none = UIRoot.MakeText(panelRt, "NotEnough", 20, TextAnchor.MiddleCenter, UIStyle.Grey);
                none.text = "(two spirits at full spirit are required)";
                none.rectTransform.sizeDelta = new Vector2(0f, 40f);

                BuildButtonsRow(panelRt, false);
                return;
            }

            BuildColumns(panelRt, eligible);
            BuildResultPreview(panelRt);
            BuildButtonsRow(panelRt, true);
        }

        private void BuildColumns(RectTransform parent, List<SpiritAgent> eligible)
        {
            float rowHeight = 30f + eligible.Count * (SlotButtonHeight + 6f);
            var row = new GameObject("Slots");
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.SetParent(parent, false);
            rowRt.sizeDelta = new Vector2(0f, rowHeight);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childAlignment = TextAnchor.UpperCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            BuildSlotColumn(rowRt, "Spirit A", eligible, _pickedA, _pickedB, agent =>
            {
                _pickedA = _pickedA == agent ? null : agent;
                Rebuild();
            });
            BuildSlotColumn(rowRt, "Spirit B", eligible, _pickedB, _pickedA, agent =>
            {
                _pickedB = _pickedB == agent ? null : agent;
                Rebuild();
            });
        }

        private static void BuildSlotColumn(Transform parent, string header,
            List<SpiritAgent> eligible, SpiritAgent picked, SpiritAgent excluded,
            System.Action<SpiritAgent> onPick)
        {
            var col = new GameObject("Column_" + header);
            var colRt = col.AddComponent<RectTransform>();
            colRt.SetParent(parent, false);

            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var head = UIRoot.MakeText(colRt, "Header", 20, TextAnchor.MiddleCenter, UIStyle.Cream);
            head.text = header;
            head.rectTransform.sizeDelta = new Vector2(0f, 24f);

            for (int i = 0; i < eligible.Count; i++)
            {
                var agent = eligible[i];
                if (agent == null || agent == excluded) continue;

                bool isPicked = agent == picked;
                var captured = agent; // for the click closure
                var button = UIStyle.MakeButton(colRt, AgentLabel(agent),
                    () => onPick(captured), 20, isPicked ? UIStyle.Gold : (Color?)null);
                ((RectTransform)button.transform).sizeDelta = new Vector2(0f, SlotButtonHeight);

                if (isPicked)
                {
                    var label = button.GetComponentInChildren<Text>();
                    if (label != null) label.color = SelectedLabel;
                }
            }
        }

        private void BuildResultPreview(RectTransform parent)
        {
            EvaluatePair(out _, out string text, out Color color);
            var preview = UIRoot.MakeText(parent, "ResultPreview", 20, TextAnchor.MiddleCenter, color);
            preview.text = text;
            preview.rectTransform.sizeDelta = new Vector2(0f, 30f);
        }

        private void BuildButtonsRow(RectTransform parent, bool showWeave)
        {
            var row = new GameObject("Buttons");
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.SetParent(parent, false);
            rowRt.sizeDelta = new Vector2(0f, 56f);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            if (showWeave)
            {
                EvaluatePair(out bool canWeave, out _, out _);
                var weave = UIStyle.MakeButton(rowRt, "Weave them", OnWeaveClicked, 24,
                    canWeave ? UIStyle.Gold : (Color?)null);
                weave.interactable = canWeave;
                if (canWeave)
                {
                    var label = weave.GetComponentInChildren<Text>();
                    if (label != null) label.color = SelectedLabel;
                }
            }

            UIStyle.MakeButton(rowRt, "Never mind", Close, 24);
        }
    }
}

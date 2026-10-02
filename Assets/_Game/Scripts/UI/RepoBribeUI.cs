using System;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// The confrontation choice (muscle 07, verdict 1): the Repo-man stands
    /// over a runaway and the shepherd may slip him obols to look the other
    /// way, once. The price (RepoManManager.NextBribePrice) rises every time
    /// it is paid. Two buttons: pay, or let him proceed. Closing is via the
    /// buttons only - Escape is reserved for Pause.
    ///
    /// Modal pattern copied from HoldingOfficeUI/MerchantShopUI:
    /// UIInputLock.ModalOpen + SetGameplayBlocked with the pause-respecting
    /// restore. The panel is rebuilt on every open. Self-creates on demand
    /// (the Repo-man is runtime-spawned, so no scene setup is needed). The
    /// owner supplies callbacks: onBribe returns false when the payment
    /// failed (panel stays open with the shortfall), onRefuse runs after the
    /// panel has closed.
    /// </summary>
    public class RepoBribeUI : MonoBehaviour
    {
        public static RepoBribeUI Instance { get; private set; }

        private GameObject _panel;
        private Text _status;
        private bool _open;
        private int _price;
        private Func<bool> _onBribe;
        private Action _onRefuse;

        public bool IsOpen => _open;

        /// <summary>Returns the live modal, creating it on the fly.</summary>
        public static RepoBribeUI GetOrCreate()
        {
            if (Instance == null)
                new GameObject("RepoBribeUI (runtime)").AddComponent<RepoBribeUI>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Opens the choice. False (nothing changes) if already open.</summary>
        public bool Open(string spiritName, int price, Func<bool> onBribe, Action onRefuse)
        {
            if (_open) return false;

            _open = true;
            _price = price;
            _onBribe = onBribe;
            _onRefuse = onRefuse;

            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null)
                GameInput.Instance.SetGameplayBlocked(true);

            BuildPanel(spiritName);
            return true;
        }

        /// <summary>Safety hatch (save loaded, Repo-man destroyed): closes with no callbacks.</summary>
        public void CloseIfOpen()
        {
            if (_open) Close();
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            _onBribe = null;
            _onRefuse = null;
            UIInputLock.ModalOpen = false;
            if (_panel != null) { Destroy(_panel); _panel = null; _status = null; }

            if (GameInput.Instance != null)
            {
                // Don't hand input back if the pause menu still needs it blocked.
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.CeremonyActive) GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ---- choices -----------------------------------------------------------

        private void OnBribeClicked()
        {
            var inv = Inventory.Instance;
            int have = inv != null ? inv.Count("coin") : 0;
            if (have < _price)
            {
                if (_status != null) _status.text = "(needs " + _price + " obols)";
                Bleeps.Play(BleepKind.Denied, 0.8f);
                return;
            }

            var pay = _onBribe;
            if (pay != null && !pay())
            {
                if (_status != null) _status.text = "The Repo-man shakes his head.";
                Bleeps.Play(BleepKind.Denied, 0.8f);
                return;
            }

            Bleeps.Play(BleepKind.Coin, 0.6f);
            Close();
        }

        private void OnRefuseClicked()
        {
            var refuse = _onRefuse;
            Close();
            if (refuse != null) refuse();
        }

        // ------------------------------------------------------------------ UI

        private void BuildPanel(string spiritName)
        {
            if (_panel != null) { Destroy(_panel); _panel = null; }

            var root = UIRoot.GetRoot();

            _panel = new GameObject("RepoBribeUI");
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.SetParent(root, false);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(540f, 120f); // height grows via ContentSizeFitter

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
            title.text = "The Repo-man";
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);

            string who = string.IsNullOrEmpty(spiritName) ? "this spirit" : spiritName;
            var line1 = UIRoot.MakeText(panelRt, "Line1", 20, TextAnchor.MiddleCenter, UIStyle.Grey);
            line1.text = "Section 12 applies to " + who + ".";
            line1.rectTransform.sizeDelta = new Vector2(0f, 28f);

            var line2 = UIRoot.MakeText(panelRt, "Line2", 18, TextAnchor.MiddleCenter, UIStyle.Grey);
            line2.text = "A modest consideration is also a form of paperwork.";
            line2.rectTransform.sizeDelta = new Vector2(0f, 26f);

            var inv = Inventory.Instance;
            var coins = UIRoot.MakeText(panelRt, "Coins", 24, TextAnchor.MiddleCenter, UIStyle.Gold);
            coins.text = "Obols: " + (inv != null ? inv.Count("coin") : 0);
            coins.rectTransform.sizeDelta = new Vector2(0f, 32f);

            UIStyle.MakeDivider(panelRt);

            var pay = UIStyle.MakeButton(panelRt, "Slip him " + _price + " obols", OnBribeClicked, 24);
            ((RectTransform)pay.transform).sizeDelta = new Vector2(0f, 56f);

            var refuse = UIStyle.MakeButton(panelRt, "Let him proceed", OnRefuseClicked, 24);
            ((RectTransform)refuse.transform).sizeDelta = new Vector2(0f, 56f);

            var note = UIRoot.MakeText(panelRt, "Note", 17, TextAnchor.MiddleCenter, UIStyle.Grey);
            note.text = "He will look away once. The going rate only rises.";
            note.rectTransform.sizeDelta = new Vector2(0f, 24f);

            // Transient error line (filled by a failed payment).
            _status = UIRoot.MakeText(panelRt, "Status", 18, TextAnchor.MiddleCenter, UIStyle.Danger);
            _status.text = "";
            _status.rectTransform.sizeDelta = new Vector2(0f, 24f);

            _panel.transform.SetAsLastSibling(); // render above the HUD
        }
    }
}

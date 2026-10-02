using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Top-center alarm banner (slice 07: the Repo-man). Shown via the static
    /// Show/Hide API from anywhere (e.g. "The Repo-man approaches!"). Danger-red
    /// UIStyle panel with a gentle unscaled alpha pulse while visible. Sits just
    /// below the clock. Hidden by default; a scene bootstrap adds this component
    /// once and everything else goes through the statics.
    /// </summary>
    public class AlarmBannerUI : MonoBehaviour
    {
        private static AlarmBannerUI Instance;

        private const float Width = 700f;
        private const float Height = 54f;
        private const float TopOffset = -70f;    // below the clock
        private const float PulseMin = 0.75f;
        private const float PulseMax = 1f;
        private const float PulseFrequency = 1.2f; // Hz

        private RectTransform _banner;
        private CanvasGroup _group;
        private Text _label;
        private bool _visible;

        // Owner stack: several systems (repo-man, villains, road ambushers) share the
        // one banner. The most recent live owner's text shows; Hide(owner) removes only
        // that owner's entry, so one system ending never clobbers another's alarm.
        private struct Entry
        {
            public object Owner;
            public string Text;
        }

        private static readonly System.Collections.Generic.List<Entry> Owners =
            new System.Collections.Generic.List<Entry>();

        private static readonly object DefaultOwner = new object();

        /// <summary>Shows the banner with the given message for the shared default owner.</summary>
        public static void Show(string message) => Show(DefaultOwner, message);

        /// <summary>Hides the shared default owner's banner entry.</summary>
        public static void Hide() => Hide(DefaultOwner);

        /// <summary>
        /// Shows <paramref name="text"/> on behalf of <paramref name="owner"/> (moved to the
        /// top of the stack if it already had an entry). A null owner means the default one.
        /// </summary>
        public static void Show(object owner, string text)
        {
            owner = owner ?? DefaultOwner;
            RemoveOwner(owner);
            Owners.Add(new Entry { Owner = owner, Text = text ?? "" });
            Refresh();
        }

        /// <summary>Removes only <paramref name="owner"/>'s entry; the next owner's text (if any) shows.</summary>
        public static void Hide(object owner)
        {
            RemoveOwner(owner ?? DefaultOwner);
            Refresh();
        }

        private static void RemoveOwner(object owner)
        {
            for (int i = Owners.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Owners[i].Owner, owner)) Owners.RemoveAt(i);
        }

        /// <summary>Drops destroyed owners, then shows the top entry or hides. No-op without an instance.</summary>
        private static void Refresh()
        {
            for (int i = Owners.Count - 1; i >= 0; i--)
                if (Owners[i].Owner is UnityEngine.Object uo && uo == null) Owners.RemoveAt(i);

            if (Instance == null) return;
            if (Owners.Count > 0) Instance.ShowInternal(Owners[Owners.Count - 1].Text);
            else Instance.HideInternal();
        }

        // Statics survive play-mode entry when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Owners.Clear();
            Instance = null;
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

        private void Update()
        {
            if (!_visible || _group == null) return;

            // Gentle sine pulse, unscaled so it breathes even while paused.
            float t = (Mathf.Sin(Time.unscaledTime * PulseFrequency * 2f * Mathf.PI) + 1f) * 0.5f;
            _group.alpha = Mathf.Lerp(PulseMin, PulseMax, t);
        }

        private void ShowInternal(string message)
        {
            if (_banner == null) Build();
            if (_banner == null) return;

            _label.text = message ?? "";
            _banner.SetAsLastSibling();
            _banner.gameObject.SetActive(true);
            _visible = true;
        }

        private void HideInternal()
        {
            _visible = false;
            if (_banner != null) _banner.gameObject.SetActive(false);
        }

        private void Build()
        {
            var root = UIRoot.GetRoot();
            if (root == null) return;

            var bg = UIStyle.Danger;
            bg.a = 0.9f;
            _banner = UIStyle.MakePanel(root, "AlarmBanner", bg);
            _banner.anchorMin = new Vector2(0.5f, 1f);
            _banner.anchorMax = new Vector2(0.5f, 1f);
            _banner.pivot = new Vector2(0.5f, 1f);
            _banner.anchoredPosition = new Vector2(0f, TopOffset);
            _banner.sizeDelta = new Vector2(Width, Height);

            _group = _banner.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _label = UIRoot.MakeText(_banner, "Message", 28, TextAnchor.MiddleCenter, UIStyle.Cream);
            _label.fontStyle = FontStyle.Bold;
            var lrt = _label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _banner.gameObject.SetActive(false); // hidden by default
        }
    }
}

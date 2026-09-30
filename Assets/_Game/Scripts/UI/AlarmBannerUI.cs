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

        /// <summary>Shows the banner with the given message. No-op if no instance exists.</summary>
        public static void Show(string message)
        {
            if (Instance != null) Instance.ShowInternal(message);
        }

        /// <summary>Hides the banner. No-op if no instance exists.</summary>
        public static void Hide()
        {
            if (Instance != null) Instance.HideInternal();
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

using System.Collections;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Full-screen pause overlay built in code: dim backdrop + centered panel
    /// with Resume / Save / Save &amp; Quit. Shown/hidden via
    /// GameManager.OnPauseChanged. Everything uses unscaled time — the game
    /// pauses with Time.timeScale = 0, and uGUI buttons keep working then.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        private GameObject _overlay;
        private Text _savedLabel;
        private Coroutine _savedFade;
        private bool _subscribed;

        // Gentle Passage settings row (hidden when GameSettings is missing).
        private Button _gentleButton;
        private Text _gentleLabel;
        private GameObject _gentleCaption;

        private void Start()
        {
            BuildUI();

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnPauseChanged += OnPauseChanged;
                _subscribed = true;
                OnPauseChanged(GameManager.Instance.IsPaused);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && GameManager.Instance != null)
                GameManager.Instance.OnPauseChanged -= OnPauseChanged;
        }

        private void OnPauseChanged(bool paused)
        {
            if (_overlay != null) _overlay.SetActive(paused);

            // Re-sync the toggle each time the menu opens (the value may have
            // been changed elsewhere, e.g. restored from a save).
            if (paused) RefreshGentleRow();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            var root = UIRoot.GetRoot();

            // Dim backdrop covering the whole screen.
            _overlay = new GameObject("PauseMenu");
            var overlayRt = _overlay.AddComponent<RectTransform>();
            overlayRt.SetParent(root, false);
            Stretch(overlayRt);

            var dim = _overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true; // swallow clicks behind the menu

            // Centered vertical panel.
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(overlayRt, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(360f, 580f);

            var panelImg = panel.gameObject.AddComponent<Image>();
            UIStyle.ApplyPanel(panelImg, UIStyle.PanelBg);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Title.
            var title = UIRoot.MakeText(panel, "Title", 34, TextAnchor.MiddleCenter,
                UIStyle.Cream);
            title.text = "Paused";
            title.rectTransform.sizeDelta = new Vector2(0f, 56f);

            UIStyle.MakeDivider(panel);

            // Buttons.
            MakeButton(panel, "Resume", OnResume);
            MakeButton(panel, "Options", OnOptions);
            MakeButton(panel, "Save", OnSave);
            MakeButton(panel, "Save & Quit", OnSaveAndQuit);

            // Settings row: Gentle Passage toggle + caption (GDD 4.4 easy mode).
            UIStyle.MakeDivider(panel);

            _gentleButton = MakeButton(panel, "Gentle Passage: OFF", OnToggleGentlePassage);
            _gentleLabel = _gentleButton.GetComponentInChildren<Text>();

            var caption = UIRoot.MakeText(panel, "GentleCaption", 16,
                TextAnchor.MiddleCenter, UIStyle.Grey);
            caption.text = "No repossessions. The Repo-man respects your boundaries.";
            caption.rectTransform.sizeDelta = new Vector2(0f, 24f);
            _gentleCaption = caption.gameObject;

            RefreshGentleRow();

            // Transient "Saved ✓" feedback.
            _savedLabel = UIRoot.MakeText(panel, "SavedLabel", 24, TextAnchor.MiddleCenter,
                UIStyle.Gold);
            _savedLabel.text = "Saved!";
            _savedLabel.rectTransform.sizeDelta = new Vector2(0f, 32f);
            _savedLabel.gameObject.SetActive(false);

            _overlay.SetActive(false);
        }

        private static Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button_" + label);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0f, 56f);

            var bg = go.AddComponent<Image>();
            bg.color = Color.white; // tinted by the Button's ColorBlock

            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            UIStyle.StyleButton(button);

            button.onClick.AddListener(onClick);

            var text = UIRoot.MakeText(rt, "Label", 26, TextAnchor.MiddleCenter, UIStyle.Cream);
            text.text = label;
            Stretch(text.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------- Actions

        private void OnResume()
        {
            if (GameManager.Instance != null) GameManager.Instance.SetPaused(false);
        }

        private void OnOptions()
        {
            OptionsMenuUI.Open(); // modal on top of the pause overlay
        }

        private void OnSave()
        {
            if (SaveSystem.Instance == null) return;
            SaveSystem.Instance.Save();

            if (_savedFade != null) StopCoroutine(_savedFade);
            _savedFade = StartCoroutine(SavedFeedback());
        }

        private void OnSaveAndQuit()
        {
            if (GameManager.Instance != null) GameManager.Instance.SaveAndQuit();
        }

        private void OnToggleGentlePassage()
        {
            var settings = GameSettings.Instance;
            if (settings == null) return;

            settings.GentlePassage = !settings.GentlePassage;
            RefreshGentleRow();
        }

        /// <summary>
        /// Shows/hides the Gentle Passage row (hidden when GameSettings is
        /// missing) and syncs the toggle's label and colors: gold background
        /// with dark text when ON, standard button look when OFF.
        /// </summary>
        private void RefreshGentleRow()
        {
            if (_gentleButton == null) return;

            bool available = GameSettings.Instance != null;
            _gentleButton.gameObject.SetActive(available);
            if (_gentleCaption != null) _gentleCaption.SetActive(available);
            if (!available) return;

            bool on = GameSettings.Instance.GentlePassage;
            if (_gentleLabel != null)
            {
                _gentleLabel.text = on ? "Gentle Passage: ON" : "Gentle Passage: OFF";
                _gentleLabel.color = on
                    ? new Color(0.18f, 0.15f, 0.06f, 1f) // dark text on gold
                    : UIStyle.Cream;
            }
            UIStyle.StyleButton(_gentleButton, on ? UIStyle.Gold : (Color?)null);
        }

        /// <summary>Shows "Saved ✓", holds ~1.5s, then fades — all on unscaled time.</summary>
        private IEnumerator SavedFeedback()
        {
            if (_savedLabel == null) yield break;

            var c = _savedLabel.color;
            _savedLabel.color = new Color(c.r, c.g, c.b, 1f);
            _savedLabel.gameObject.SetActive(true);

            float hold = 1.5f;
            while (hold > 0f)
            {
                hold -= Time.unscaledDeltaTime;
                yield return null;
            }

            float fade = 0.5f;
            float t = fade;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime;
                _savedLabel.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(t / fade));
                yield return null;
            }

            _savedLabel.gameObject.SetActive(false);
            _savedLabel.color = new Color(c.r, c.g, c.b, 1f);
            _savedFade = null;
        }
    }
}

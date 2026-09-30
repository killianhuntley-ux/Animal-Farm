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
            panel.sizeDelta = new Vector2(360f, 400f);

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
            MakeButton(panel, "Save", OnSave);
            MakeButton(panel, "Save & Quit", OnSaveAndQuit);

            // Transient "Saved ✓" feedback.
            _savedLabel = UIRoot.MakeText(panel, "SavedLabel", 24, TextAnchor.MiddleCenter,
                UIStyle.Gold);
            _savedLabel.text = "Saved!";
            _savedLabel.rectTransform.sizeDelta = new Vector2(0f, 32f);
            _savedLabel.gameObject.SetActive(false);

            _overlay.SetActive(false);
        }

        private static void MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
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

using AnimalFarm.Interaction;
using AnimalFarm.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Bottom-center prompt ("[E] Options", "[E] Pull", ...) shown while the
    /// shepherd is focusing an interactable. Driven by InteractionSensor.FocusChanged
    /// and refreshed a few times a second (the menu may gain or lose actions).
    /// Selectable targets read "Options" (E opens their menu) or the label of
    /// their single action (E runs it directly); the rest show their PromptText.
    /// Hidden while the context menu is open.
    /// </summary>
    public class InteractPromptUI : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        private Text _prompt;
        private bool _subscribed;
        private IInteractable _focus;
        private float _refreshTimer;

        private void Start()
        {
            var root = UIRoot.GetRoot();

            _prompt = UIRoot.MakeText(root, "InteractPrompt", 28, TextAnchor.MiddleCenter,
                UIStyle.Cream);

            var rt = _prompt.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 90f);
            rt.sizeDelta = new Vector2(700f, 40f);

            var shadow = _prompt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            _prompt.gameObject.SetActive(false);

            InteractionSensor.FocusChanged += OnFocusChanged;
            _subscribed = true;
        }

        private void OnFocusChanged(IInteractable focus)
        {
            _focus = focus;
            _refreshTimer = 0f;
            Refresh();
        }

        private void Update()
        {
            if (_focus == null || _prompt == null) return;

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshSeconds;
            Refresh();
        }

        private void Refresh()
        {
            if (_prompt == null) return;

            bool dead = _focus == null || (_focus is Object o && o == null);
            var menu = SelectionMenuUI.Instance;
            if (dead || (menu != null && menu.IsOpen))
            {
                _prompt.gameObject.SetActive(false);
                return;
            }

            string text = InteractMenu.PromptFor(_focus);
            if (string.IsNullOrEmpty(text))
            {
                _prompt.gameObject.SetActive(false);
                return;
            }

            _prompt.text = "[E] " + text;
            _prompt.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                InteractionSensor.FocusChanged -= OnFocusChanged;
                _subscribed = false;
            }
        }
    }
}

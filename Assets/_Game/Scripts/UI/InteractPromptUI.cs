using AnimalFarm.Interaction;
using AnimalFarm.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Bottom-center prompt ("[E / Ⓐ] Inspect") shown while the shepherd is
    /// focusing an interactable. Driven by InteractionSensor.FocusChanged.
    /// </summary>
    public class InteractPromptUI : MonoBehaviour
    {
        private Text _prompt;
        private bool _subscribed;

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
            if (_prompt == null) return;

            if (focus == null)
            {
                _prompt.gameObject.SetActive(false);
                return;
            }

            _prompt.text = "[E] " + focus.PromptText;
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

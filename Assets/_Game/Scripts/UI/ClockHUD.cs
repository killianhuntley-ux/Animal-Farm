using AnimalFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Top-right HUD showing the current day and time, e.g.
    ///   Day 3
    ///   08:42 ☀        (☾ at night, "▶▶" appended while fast-forwarding)
    /// Built entirely in code in Start; polls GameClock in Update.
    /// </summary>
    public class ClockHUD : MonoBehaviour
    {
        private Text _dayText;
        private Text _timeText;

        private void Start()
        {
            var root = UIRoot.GetRoot();

            var holder = new GameObject("ClockHUD").AddComponent<RectTransform>();
            holder.SetParent(root, false);
            holder.anchorMin = new Vector2(1f, 1f);
            holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(1f, 1f);
            holder.anchoredPosition = new Vector2(-24f, -20f);
            holder.sizeDelta = new Vector2(260f, 80f);

            var nearWhite = UIStyle.Cream;

            _dayText = UIRoot.MakeText(holder, "DayText", 30, TextAnchor.UpperRight, nearWhite);
            var dayRt = _dayText.rectTransform;
            dayRt.anchorMin = new Vector2(0f, 1f);
            dayRt.anchorMax = new Vector2(1f, 1f);
            dayRt.pivot = new Vector2(1f, 1f);
            dayRt.anchoredPosition = Vector2.zero;
            dayRt.sizeDelta = new Vector2(0f, 36f);
            AddShadow(_dayText);

            _timeText = UIRoot.MakeText(holder, "TimeText", 26, TextAnchor.UpperRight, nearWhite);
            var timeRt = _timeText.rectTransform;
            timeRt.anchorMin = new Vector2(0f, 1f);
            timeRt.anchorMax = new Vector2(1f, 1f);
            timeRt.pivot = new Vector2(1f, 1f);
            timeRt.anchoredPosition = new Vector2(0f, -38f);
            timeRt.sizeDelta = new Vector2(0f, 32f);
            AddShadow(_timeText);
        }

        private void Update()
        {
            var clock = GameClock.Instance;
            if (clock == null || _dayText == null || _timeText == null) return;

            _dayText.text = "Day " + clock.Day;

            string line = clock.TimeString + (clock.IsNight ? " [night]" : "");
            if (clock.FastForward) line += " >>";
            _timeText.text = line;
        }

        private static void AddShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }
}

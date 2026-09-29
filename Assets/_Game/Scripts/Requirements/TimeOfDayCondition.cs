using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// Met when the clock is inside [startHour, endHour). Handles wrap-around
    /// windows: startHour 20, endHour 4 means "night, 20:00 to 04:00".
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Time Of Day", fileName = "TimeOfDayCondition")]
    public class TimeOfDayCondition : ConditionAsset
    {
        [SerializeField, Range(0f, 24f)] private float startHour = 6f;
        [SerializeField, Range(0f, 24f)] private float endHour = 18f;

        public override bool Evaluate()
        {
            var clock = GameClock.Instance;
            if (clock == null) return false;
            return InWindow(clock.Hours);
        }

        private bool InWindow(float hours)
        {
            float start = Mathf.Repeat(startHour, 24f);
            float end = Mathf.Repeat(endHour, 24f);
            if (Mathf.Approximately(start, end)) return true; // degenerate window = always
            return start < end
                ? hours >= start && hours < end
                : hours >= start || hours < end; // wraps past midnight
        }

        public override string Describe()
        {
            var clock = GameClock.Instance;
            string now = clock != null ? clock.TimeString : "--:--";
            return $"Time {Format(startHour)}-{Format(endHour)} (now {now})";
        }

        private static string Format(float h)
        {
            h = Mathf.Repeat(h, 24f);
            int minutes = Mathf.RoundToInt(h * 60f);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }
}

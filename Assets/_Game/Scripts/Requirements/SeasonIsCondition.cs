using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// Met while the underworld calendar sits in a given season (index into
    /// GameCalendar's season table: 0 The Hush, 1 The Weep, 2 The Smolder,
    /// 3 The Long Dim). Lets species gates be season-gated.
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Conditions/Season Is", fileName = "SeasonIsCondition")]
    public class SeasonIsCondition : ConditionAsset
    {
        [SerializeField, Range(0, 3)] private int seasonIndex;

        public override bool Evaluate()
        {
            var calendar = GameCalendar.Instance;
            return calendar != null && calendar.SeasonIndex == seasonIndex;
        }

        public override string Describe()
        {
            var calendar = GameCalendar.Instance;
            string wanted = calendar != null
                ? calendar.GetSeasonName(seasonIndex)
                : "season " + seasonIndex;
            string now = calendar != null
                ? calendar.GetSeasonName(calendar.SeasonIndex)
                : "--";
            return "Season is " + wanted + " (now " + now + ")";
        }
    }
}

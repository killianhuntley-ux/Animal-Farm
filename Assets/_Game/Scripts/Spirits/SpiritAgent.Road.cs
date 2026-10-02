using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Road-travel half of the SpiritAgent (muscle 08 Frontier). Escorting is
    /// the existing follow behaviour ("Come along" / Follow Treat); this file
    /// only adds the hooks RoadTravel needs: a follow-pace multiplier (dark
    /// stretches), a floored mood nudge (strain, tolls, fright), and the
    /// ambush bolt that drops an escort straight into the existing herding
    /// chase (SpiritAgent.HerdTag) -- no new recovery minigame.
    /// </summary>
    public partial class SpiritAgent
    {
        /// <summary>Tags a bolted road spirit needs (ASSUMPTION: always the easy 2, calibration law).</summary>
        private const int RoadBoltTags = 2;

        /// <summary>
        /// Multiplier on follow speed, written every tick by RoadTravel (1 = normal;
        /// below 1 while escorted through an unlit dark stretch).
        /// </summary>
        public float RoadSpeedMul { get; set; } = 1f;

        /// <summary>True while this resident trails the shepherd (a road escort).</summary>
        public bool IsEscort => _following && _state == SpiritState.Resident;

        /// <summary>
        /// Small mood nudge from the road. Negative deltas never push Spirit below
        /// <paramref name="floor"/> (a spirit already under the floor is left alone),
        /// so road strain alone can never cause a runaway; positives clamp at 100.
        /// </summary>
        public void RoadMoodDelta(float delta, float floor)
        {
            if (_state != SpiritState.Resident || Mathf.Approximately(delta, 0f)) return;
            if (delta < 0f)
            {
                if (_spirit <= floor) return;
                _spirit = Mathf.Max(floor, _spirit + delta);
            }
            else
            {
                _spirit = Mathf.Min(100f, _spirit + delta);
            }
        }

        /// <summary>
        /// Ambush bolt: this escort panics and scatters away from
        /// <paramref name="awayFrom"/>. It becomes a Runaway already mid-chase
        /// (2 herding tags to recover, via the normal Interact/soothe on it), so
        /// the existing herding give-up timer and repo clock apply unchanged.
        /// False when it is not a Resident.
        /// </summary>
        public bool RoadBolt(Vector3 awayFrom)
        {
            if (_state != SpiritState.Resident) return false;

            BecomeRunaway(); // drops following; sets a border flee target we override below
            RoadSpeedMul = 1f;
            _herdingTagsLeft = RoadBoltTags;
            _lastHerdTagRealTime = Time.unscaledTime;
            _herdDashing = true;

            Vector3 away = transform.position - awayFrom;
            away.z = 0f;
            if (away.sqrMagnitude < 0.01f) away = new Vector3(Random.value < 0.5f ? -1f : 1f, 0f, 0f);
            away.Normalize();

            // Scatter: mostly away along the road, a little sideways spread.
            Vector3 side = new Vector3(-away.y, away.x, 0f) * Random.Range(-1.2f, 1.2f);
            Vector3 target = transform.position + away * Random.Range(3f, 5.5f) + side;
            target.z = 0f;

            // Stay on the lane while still on the road: ghosts ignore walls,
            // but a dash through the rails would look broken.
            if (FrontierGeometry.IsOnRoad(transform.position))
            {
                var lane = FrontierGeometry.RoadLane;
                target.x = Mathf.Clamp(target.x, lane.xMin, lane.xMax);
                target.y = Mathf.Clamp(target.y, lane.yMin, lane.yMax);
            }

            _wanderTarget = target;
            _hasTarget = true;
            return true;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Identity half of the SpiritAgent (muscle 05): Nature stats rolled inside
    /// the species band, 1-2 traits, and the passive training-building loop.
    /// Kept in its own file so the main agent file stays about behaviour.
    /// </summary>
    public partial class SpiritAgent
    {
        /// <summary>Training ticks needed to raise one stat by one point (slow on purpose).</summary>
        public const float TicksPerStatPoint = 6f;

        private const float TrainConsiderMin = 18f;     // real seconds between "feel like training?" checks
        private const float TrainConsiderMax = 32f;
        private const float TrainRestMin = 25f;         // cooldown after a finished session
        private const float TrainRestMax = 50f;
        private const float TrainWalkTimeout = 45f;     // give up if the building is not reached by then

        private readonly List<SpiritTraitDefinition> _traits =
            new List<SpiritTraitDefinition>(SpiritTraits.MaxTraits);
        private readonly float[] _xp = new float[SpiritStats.Count]; // fractional training progress per stat

        private TrainingBuilding _trainTarget;
        private float _trainTargetUntil;
        private float _nextTrainAt;

        // ---- public surface --------------------------------------------------

        /// <summary>This individual's traits (1-2; never null).</summary>
        public IReadOnlyList<SpiritTraitDefinition> Traits => _traits;

        public bool HasTrait(string id)
        {
            for (int i = 0; i < _traits.Count; i++)
                if (_traits[i] != null && _traits[i].id == id) return true;
            return false;
        }

        public int GetStat(SpiritStat stat) =>
            stat == SpiritStat.Vigor ? _vigor : stat == SpiritStat.Grace ? _grace : _gleam;

        /// <summary>The species (min, max) band for one of this spirit's stats.</summary>
        public void GetStatBand(SpiritStat stat, out int min, out int max) =>
            SpiritStats.Band(_species, stat, out min, out max);

        /// <summary>Fractional training progress toward the next point of a stat.</summary>
        public float GetTrainingTicks(SpiritStat stat) => _xp[(int)stat];

        /// <summary>Cheap change signature (stats + traits) so open UIs know to rebuild.</summary>
        public int StatSignature
        {
            get
            {
                unchecked
                {
                    int h = ((_vigor * 11) + _grace) * 11 + _gleam;
                    for (int i = 0; i < _traits.Count; i++)
                        h = h * 31 + (_traits[i] != null && _traits[i].id != null ? _traits[i].id.GetHashCode() : 0);
                    return h;
                }
            }
        }

        /// <summary>Sets a stat directly (debug / inheritance), clamped into the species band.</summary>
        public void SetStat(SpiritStat stat, int value)
        {
            StoreStat(stat, SpiritStats.ClampToBand(_species, stat, value));
            GetStatBand(stat, out _, out int max);
            if (GetStat(stat) >= max) _xp[(int)stat] = 0f;
        }

        /// <summary>
        /// Adds training ticks; every <see cref="TicksPerStatPoint"/> raises the
        /// stat one point, never past the species max. Returns the points gained.
        /// </summary>
        public int AddTrainingTicks(SpiritStat stat, float ticks)
        {
            if (ticks <= 0f) return 0;

            int i = (int)stat;
            GetStatBand(stat, out _, out int max);
            int cur = GetStat(stat);
            if (cur >= max) { _xp[i] = 0f; return 0; }

            _xp[i] += ticks;
            int gained = 0;
            while (_xp[i] >= TicksPerStatPoint && cur < max)
            {
                _xp[i] -= TicksPerStatPoint;
                cur++;
                gained++;
            }
            if (cur >= max) _xp[i] = 0f;
            if (gained > 0) StoreStat(stat, cur);
            return gained;
        }

        /// <summary>Replaces the traits with exactly these ids (unknown ids dropped, max 2).</summary>
        public void SetTraits(IReadOnlyList<string> ids)
        {
            _traits.Clear();
            _traits.AddRange(SpiritTraits.FromIds(ids));
        }

        /// <summary>
        /// Initialises identity explicitly (weaving-inheritance hook): rerolls
        /// the three stats inside the species band skewed by <paramref name="bias"/>,
        /// and sets the traits from <paramref name="traitIds"/> - a null/empty
        /// list rolls them normally; fewer than two ids keeps those and rolls
        /// the rest around them (no conflicting pairs).
        /// </summary>
        public void ApplyIdentity(IReadOnlyList<string> traitIds, SpiritStatBias bias)
        {
            RollStatsInBand(bias);

            var keep = SpiritTraits.FromIds(traitIds);
            _traits.Clear();
            _traits.AddRange(SpiritTraits.Roll(0, keep));
            for (int i = 0; i < _xp.Length; i++) _xp[i] = 0f;
        }

        // ---- rolling / persistence ---------------------------------------------

        /// <summary>Fresh spawn: in-band stats, and traits if none were set yet.</summary>
        private void RollIdentity()
        {
            RollStatsInBand(SpiritStatBias.None);
            if (_traits.Count == 0) _traits.AddRange(SpiritTraits.Roll());
        }

        private void RollStatsInBand(SpiritStatBias bias)
        {
            _vigor = SpiritStats.RollStat(_species, SpiritStat.Vigor, bias.vigor);
            _grace = SpiritStats.RollStat(_species, SpiritStat.Grace, bias.grace);
            _gleam = SpiritStats.RollStat(_species, SpiritStat.Gleam, bias.gleam);
        }

        private void StoreStat(SpiritStat stat, int value)
        {
            if (stat == SpiritStat.Vigor) _vigor = value;
            else if (stat == SpiritStat.Grace) _grace = value;
            else _gleam = value;
        }

        /// <summary>
        /// Load path: old spirits are clamped into their species band (values
        /// from before the bands existed stay as close as the band allows) and
        /// keep the traits Init rolled when the record carries none.
        /// </summary>
        private void ApplyIdentityRecord(SpiritSaveRecord rec)
        {
            _vigor = SpiritStats.ClampToBand(_species, SpiritStat.Vigor, _vigor);
            _grace = SpiritStats.ClampToBand(_species, SpiritStat.Grace, _grace);
            _gleam = SpiritStats.ClampToBand(_species, SpiritStat.Gleam, _gleam);

            var ids = SpiritTraits.Split(rec.traitIds);
            if (ids.Length > 0)
            {
                var loaded = SpiritTraits.FromIds(ids);
                if (loaded.Count > 0)
                {
                    _traits.Clear();
                    _traits.AddRange(loaded);
                }
            }

            _xp[0] = SanitiseXp(SpiritStat.Vigor, rec.xpVigor);
            _xp[1] = SanitiseXp(SpiritStat.Grace, rec.xpGrace);
            _xp[2] = SanitiseXp(SpiritStat.Gleam, rec.xpGleam);
        }

        private float SanitiseXp(SpiritStat stat, float xp)
        {
            GetStatBand(stat, out _, out int max);
            if (GetStat(stat) >= max) return 0f;
            return Mathf.Clamp(xp, 0f, TicksPerStatPoint - 0.01f);
        }

        // ---- passive training (TrainingBuilding) ----------------------------------

        /// <summary>True while this spirit is mid-session at a training building.</summary>
        public bool IsTraining => _quirk == SpiritQuirks.Kind.Train;

        /// <summary>The training building this spirit is walking to / using, or null.</summary>
        public TrainingBuilding TrainTarget
        {
            get
            {
                if (_trainTarget != null && !IsTraining && Time.time > _trainTargetUntil)
                    _trainTarget = null; // never reached it in time
                return _trainTarget;
            }
        }

        /// <summary>
        /// Personality-chain step: occasionally fancy a workout. Asks the
        /// training manager for a building by trait/species/bait appetite and
        /// returns a spot beside it to walk to.
        /// </summary>
        private bool TryTrainingTarget(out Vector3 target)
        {
            target = default;
            if (_state != SpiritState.Resident || _following) return false;
            if (_trainTarget != null || Time.time < _nextTrainAt) return false;

            _nextTrainAt = Time.time + Random.Range(TrainConsiderMin, TrainConsiderMax);
            if (MoodBand == SpiritMoodBand.Low || _hunger01 > 0.8f) return false; // drooped / starving spirits skip the gym

            var mgr = TrainingBuildingManager.Instance;
            if (mgr == null || !mgr.TryPickBuilding(this, out var building, out target)) return false;

            _trainTarget = building;
            _trainTargetUntil = Time.time + TrainWalkTimeout;
            return true;
        }

        /// <summary>Building callback: the spirit arrived. Owns the quirk slot for the session.</summary>
        public bool BeginTraining(float seconds)
        {
            if (!CanActPersonality || _following) return false;
            if (_restActive || _quirk != SpiritQuirks.Kind.None) return false;
            _hasTarget = false;
            _quirk = SpiritQuirks.Kind.Train;
            _quirkTimer = 0f;
            _quirkDuration = seconds + 1f; // safety; the building ends the session
            return true;
        }

        /// <summary>Ends (or abandons) a session and starts the rest cooldown.</summary>
        public void EndTraining()
        {
            if (_quirk == SpiritQuirks.Kind.Train) EndQuirk();
            _trainTarget = null;
            _nextTrainAt = Time.time + Random.Range(TrainRestMin, TrainRestMax);
        }

        /// <summary>Building callback: a little exercise hop.</summary>
        public void TrainingPulse()
        {
            if (_hopTimer < 0f) _hopTimer = 0f;
        }

        /// <summary>Console hook: skip the training cooldown so the next idle tick can try again.</summary>
        public void Debug_ReadyToTrain() => _nextTrainAt = 0f;
    }
}

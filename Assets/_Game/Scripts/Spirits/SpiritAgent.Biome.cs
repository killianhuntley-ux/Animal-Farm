using AnimalFarm.Core;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Biome half of the SpiritAgent (muscle 02 verdicts 1 + 5). Two jobs, both
    /// deliberately gentle (calibration law: nothing here ever evicts a
    /// spirit or drops it into a runaway):
    ///
    ///  1. GROUND: the spirit's affinity for the biome of the base it lives in
    ///     (its home's base, else the base under its feet). Love adds a slow
    ///     mood trickle, Like/Love/Dislike scale mood GAINS through
    ///     BiomeAffinity.MoodMultiplier, and a Hard No under an existing
    ///     resident (the player re-sculpted the base) drifts mood down to a
    ///     floor with an occasional "(this ground feels wrong)" hint. A
    ///     silhouette or visitor on Hard No ground never stays: it drifts off
    ///     and fades (SpiritAgent.Stay.cs), and homes on Hard No ground are
    ///     never picked for a newcomer.
    ///  2. RAIN: species that Like/Love Swamp enjoy it (hop, happy chirp, a
    ///     hair of Spirit); species that Like/Love Desert hurry to their home
    ///     or the nearest building and wait the rain out. Visual flavor only.
    /// </summary>
    public partial class SpiritAgent
    {
        // ---- ground tuning (ASSUMPTION values; all easy dials) ----------------------
        private const float BiomeTickInterval = 0.5f;
        private const float WrongGroundFloor = 35f;          // Hard No drift never goes below (spec ~35)
        private const float WrongGroundDrainPerHour = 3f;    // Spirit points per GAME hour (a game hour = 50 real s)
        private const float LoveGroundBonusPerHour = 1.2f;   // Love: slow trickle...
        private const float LoveGroundBonusCap = 85f;        // ...that never pushes past this
        private const float HardNoGainMul = 0.5f;            // gains on Hard No ground (MoodMultiplier says 0; residents get half)
        private const float WrongHintMinSeconds = 25f;       // real seconds between "feels wrong" hints
        private const float WrongHintMaxSeconds = 50f;
        private const float WrongHintRange = 14f;            // only bother when the shepherd is around to see

        // ---- rain tuning ---------------------------------------------------------------
        private const float RainEnjoyCap = 80f;              // Enjoy nudges never push past this
        private const float RainEnjoyNudge = 0.25f;          // Spirit per flavor beat
        private const float RainGrumpNudge = -1f;            // one-off when a shelterer finds no cover
        private const float RainGrumpFloor = 50f;
        private const float ShelterMaxDistance = 14f;        // never trek across the map for cover
        private const float ShelterHomeMaxDistance = 25f;    // ...except to its own home
        private const float ShelterSpeedMul = 1.6f;

        private enum RainMood { Indifferent, Enjoys, Shelters }

        // ---- ground state --------------------------------------------------------------
        private float _biomeTimer;
        private float _prevBiomeHours = -1f;
        private float _nextWrongHintAt;
        private bool _wasWrongGround;

        /// <summary>Base id this spirit counts as living in (-1 when off every base).</summary>
        public int GroundBase { get; private set; } = -1;

        /// <summary>Biome of that base (Barren when none).</summary>
        public BiomeType GroundBiome { get; private set; } = BiomeType.Barren;

        /// <summary>This species' feeling about <see cref="GroundBiome"/> (Neutral when none).</summary>
        public Affinity GroundAffinity { get; private set; } = Affinity.Neutral;

        /// <summary>True for a living spirit standing on ground it Hard-No's.</summary>
        public bool OnWrongGround =>
            GroundAffinity == Affinity.HardNo && (_state == SpiritState.Resident || _state == SpiritState.Visitor);

        /// <summary>Multiplier on mood GAINS (feed / soothe / rest) from the ground under it.</summary>
        private float GroundGainMul =>
            GroundAffinity == Affinity.HardNo ? HardNoGainMul : BiomeAffinity.MoodMultiplier(GroundAffinity);

        /// <summary>Re-reads base + biome + affinity now (cheap; no allocation).</summary>
        private void RefreshGround()
        {
            int baseId = -1;
            if (_home != null) baseId = BiomeGround.BaseAt(_home.transform.position); // where it LIVES, not where it strolls
            if (baseId < 0) baseId = BiomeGround.BaseAt(transform.position);

            GroundBase = baseId;
            GroundBiome = baseId >= 0 ? BiomeGround.BiomeOfBase(baseId) : BiomeType.Barren;
            GroundAffinity = baseId >= 0 ? BiomeAffinity.For(_species, GroundBiome) : Affinity.Neutral;
        }

        /// <summary>Is a silhouette's current spot a base whose biome the species Hard No's?</summary>
        private bool StandsOnHardNoGround() =>
            BiomeAffinity.WontLiveIn(_species, BiomeGround.BiomeAt(transform.position));

        private void ShowWrongGroundHint()
        {
            FloatingText.Show(transform.position + Vector3.up * 0.8f,
                "(this ground feels wrong)", HerdGreyBlue);
            if (_bubble != null && !_isSleeping) _bubble.Show(WantKind.Wrong, WantBubbleSeconds);
        }

        /// <summary>
        /// Nearest free home of this species, skipping homes that stand in a
        /// base whose biome the species Hard No's (never settle on wrong ground).
        /// Same nearest-first semantics as Home.FindFree.
        /// </summary>
        private Home FindFreeHomeHere()
        {
            Home best = null;
            float bestSqr = float.MaxValue;
            var all = Home.All;
            for (int i = 0; i < all.Count; i++)
            {
                var h = all[i];
                if (h == null || !h.IsFree || h.SpeciesId != _species.id) continue;
                if (BiomeAffinity.WontLiveIn(_species, BiomeGround.BiomeAt(h.transform.position))) continue;
                float sqr = (h.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = h; }
            }
            return best;
        }

        // ---- per-frame hook (SpiritAgent.Update) ------------------------------------------

        private void TickBiome()
        {
            TickRainReaction();

            _biomeTimer -= Time.deltaTime;
            if (_biomeTimer > 0f) return;
            _biomeTimer = BiomeTickInterval;

            RefreshGround();

            var clock = GameClock.Instance;
            float now = clock != null ? clock.TotalHours : 0f;
            float dh = _prevBiomeHours < 0f ? 0f : Mathf.Clamp(now - _prevBiomeHours, 0f, 1f); // clamp: console day jumps
            _prevBiomeHours = now;

            bool living = _state == SpiritState.Resident || _state == SpiritState.Visitor;
            bool wrong = living && GroundAffinity == Affinity.HardNo;
            if (wrong && !_wasWrongGround)
                _nextWrongHintAt = Time.time + Random.Range(2f, 8f); // first hint soon after the ground turns on it
            _wasWrongGround = wrong;

            if (_state == SpiritState.Resident)
            {
                if (wrong)
                {
                    // Discomfort, not eviction: mood drifts down to the floor and stops.
                    if (_spirit > WrongGroundFloor)
                        _spirit = Mathf.Max(WrongGroundFloor, _spirit - WrongGroundDrainPerHour * dh);
                }
                else if (GroundAffinity == Affinity.Love && _spirit < LoveGroundBonusCap)
                {
                    _spirit = Mathf.Min(LoveGroundBonusCap, _spirit + LoveGroundBonusPerHour * dh);
                }
            }

            if (wrong && !_isSleeping && !_ceremony && Time.time >= _nextWrongHintAt)
            {
                _nextWrongHintAt = Time.time + Random.Range(WrongHintMinSeconds, WrongHintMaxSeconds);
                if (DistanceToPlayer() <= WrongHintRange) ShowWrongGroundHint();
            }
        }

        // ---- rain ------------------------------------------------------------------------

        private RainMood _rainMood;
        private bool _rainMoodKnown;
        private bool _rainSynced;
        private bool _rainRaining;            // what this spirit is currently reacting to
        private bool _rainPending;
        private bool _rainPendingValue;
        private bool _rainPendingSilent;
        private float _rainReactAt;
        private float _nextRainFlavorAt;
        private bool _hasShelter;
        private bool _shelterTried;           // one shelter search per rain spell
        private bool _shelterSettled;
        private Vector3 _shelterPoint;

        private RainMood CurrentRainMood
        {
            get
            {
                if (!_rainMoodKnown && _species != null)
                {
                    _rainMoodKnown = true;
                    bool likesSwamp = BiomeAffinity.For(_species, BiomeType.Swamp) >= Affinity.Like;
                    bool likesDesert = BiomeAffinity.For(_species, BiomeType.Desert) >= Affinity.Like;
                    _rainMood = likesSwamp && !likesDesert ? RainMood.Enjoys
                        : likesDesert && !likesSwamp ? RainMood.Shelters
                        : RainMood.Indifferent;
                }
                return _rainMood;
            }
        }

        /// <summary>True while this spirit is hurrying to / waiting in cover from the rain.</summary>
        public bool IsSheltering => _rainRaining && _hasShelter;

        /// <summary>
        /// Manager callback on WeatherManager.RainChanged. The reaction lands
        /// after <paramref name="delaySeconds"/> (random per spirit, so the
        /// field staggers). Silent reactions skip the chirp and mood nudge
        /// (a spirit spawned or loaded into an already-rainy day).
        /// </summary>
        public void NotifyRain(bool raining, float delaySeconds, bool silent = false)
        {
            _rainPending = true;
            _rainPendingValue = raining;
            _rainPendingSilent = silent;
            _rainReactAt = Time.time + Mathf.Max(0f, delaySeconds);
        }

        private bool CanShowRainFlavor =>
            (_state == SpiritState.Visitor || _state == SpiritState.Resident)
            && !_isSleeping && !_ceremony && !_despawning;

        private void TickRainReaction()
        {
            if (_species == null) return;

            // Late joiner (new spawn / save load into a rain day): catch up quietly once.
            if (!_rainSynced)
            {
                var weather = WeatherManager.Instance;
                if (weather == null) return;
                _rainSynced = true;
                if (weather.IsRaining && !_rainRaining && !_rainPending)
                    NotifyRain(true, Random.Range(0.5f, 6f), true);
            }

            if (_rainPending && Time.time >= _rainReactAt)
            {
                _rainPending = false;
                ApplyRain(_rainPendingValue, _rainPendingSilent);
            }

            // A shelterer that was asleep (or not yet resident) when the rain began
            // still heads for cover once it is up and about.
            if (_rainRaining && !_shelterTried && CurrentRainMood == RainMood.Shelters
                && _state == SpiritState.Resident && CanShowRainFlavor)
            {
                _shelterTried = true;
                TryChooseShelter();
            }

            if (!_rainRaining || CurrentRainMood != RainMood.Enjoys) return;
            if (!CanShowRainFlavor || Time.time < _nextRainFlavorAt) return;

            // Enjoying it: a hop now and then, an occasional happy chirp, a hair of Spirit.
            _nextRainFlavorAt = Time.time + Random.Range(9f, 18f);
            if (_hopTimer < 0f) _hopTimer = 0f;
            if (Random.value < 0.3f) SpiritVoice.Play(_species, VoiceIntent.Happy, 0.5f);
            if (_state == SpiritState.Resident && _spirit < RainEnjoyCap)
                _spirit = Mathf.Min(RainEnjoyCap, _spirit + RainEnjoyNudge);
        }

        private void ApplyRain(bool on, bool silent)
        {
            _rainRaining = on;
            _shelterTried = false;
            if (!on)
            {
                if (_hasShelter)
                {
                    // Cover is over: wander out again.
                    _hasShelter = false;
                    _shelterSettled = false;
                    _hasTarget = false;
                    _idleTimer = Random.Range(1f, 3f);
                }
                return;
            }

            _nextRainFlavorAt = Time.time + Random.Range(3f, 8f);
            if (!CanShowRainFlavor) return;

            switch (CurrentRainMood)
            {
                case RainMood.Enjoys:
                    if (_hopTimer < 0f) _hopTimer = 0f;
                    if (!silent)
                    {
                        if (Random.value < 0.6f) SpiritVoice.Play(_species, VoiceIntent.Happy, 0.6f);
                        if (_state == SpiritState.Resident && _spirit < RainEnjoyCap)
                            _spirit = Mathf.Min(RainEnjoyCap, _spirit + RainEnjoyNudge);
                    }
                    break;

                case RainMood.Shelters:
                    if (!silent && Random.value < 0.7f) SpiritVoice.Play(_species, VoiceIntent.Grumpy, 0.5f);
                    _shelterTried = true;
                    if (!TryChooseShelter() && !silent && _state == SpiritState.Resident && _spirit > RainGrumpFloor)
                        _spirit = Mathf.Max(RainGrumpFloor, _spirit + RainGrumpNudge); // caught out in the open
                    break;
            }
        }

        /// <summary>Picks the spirit's own home, else the nearest building in the same
        /// base, as cover. Residents only; followers stay with the shepherd.</summary>
        private bool TryChooseShelter()
        {
            _hasShelter = false;
            _shelterSettled = false;
            if (_state != SpiritState.Resident || _following) return false;

            Vector3 from = transform.position;
            int myBase = BiomeGround.BaseAt(from);
            bool found = false;
            Vector3 best = default;

            if (_home != null)
            {
                Vector3 hp = _home.transform.position;
                if ((hp - from).sqrMagnitude <= ShelterHomeMaxDistance * ShelterHomeMaxDistance)
                {
                    best = hp;
                    found = true;
                }
            }

            if (!found)
            {
                float bestSqr = ShelterMaxDistance * ShelterMaxDistance;

                var homes = Home.All;
                for (int i = 0; i < homes.Count; i++)
                {
                    var h = homes[i];
                    if (h == null) continue;
                    ConsiderCover(h.transform.position, from, myBase, ref bestSqr, ref best, ref found);
                }

                var gyms = TrainingBuilding.All;
                for (int i = 0; i < gyms.Count; i++)
                {
                    var g = gyms[i];
                    if (g == null) continue;
                    ConsiderCover(g.transform.position, from, myBase, ref bestSqr, ref best, ref found);
                }
            }

            if (!found) return false;

            Vector2 off = Random.insideUnitCircle * 0.7f; // hover at the door, not on the doorstep centre
            _shelterPoint = new Vector3(best.x + off.x, best.y + off.y, 0f);
            _hasShelter = true;
            _hasTarget = false;
            return true;
        }

        private static void ConsiderCover(Vector3 pos, Vector3 from, int myBase,
            ref float bestSqr, ref Vector3 best, ref bool found)
        {
            float sqr = (pos - from).sqrMagnitude;
            if (sqr >= bestSqr) return;
            int theirBase = BiomeGround.BaseAt(pos);
            if (myBase >= 0 && theirBase >= 0 && myBase != theirBase) return; // not across the road
            bestSqr = sqr;
            best = pos;
            found = true;
        }

        /// <summary>
        /// Owns movement while a sheltering spirit hurries to cover and then
        /// waits there (called from TickWander, after sleep/rest checks).
        /// True = movement handled this frame.
        /// </summary>
        private bool TickRainShelter()
        {
            if (!_rainRaining || !_hasShelter) return false;
            if (_state != SpiritState.Resident || _following) return false;

            if (_shelterSettled) return true; // idling under cover; the bob still plays

            transform.position = Vector3.MoveTowards(
                transform.position, _shelterPoint, _species.wanderSpeed * ShelterSpeedMul * Time.deltaTime);
            if ((transform.position - _shelterPoint).sqrMagnitude <= ArriveDistance * ArriveDistance)
                _shelterSettled = true;
            return true;
        }

        // ---- console helpers ----------------------------------------------------------------

        /// <summary>Console: one-line description of this spirit's ground + rain state.</summary>
        public string Debug_BiomeStatus()
        {
            RefreshGround();
            string rain = CurrentRainMood + (_rainRaining ? (_hasShelter ? (_shelterSettled ? " (under cover)" : " (hurrying to cover)") : " (raining)") : "");
            return (string.IsNullOrEmpty(_givenName) ? _species.displayName : _givenName)
                + ": ground base " + GroundBase + " " + GroundBiome + " = " + BiomeAffinity.Label(GroundAffinity)
                + ", mood x" + GroundGainMul.ToString("0.00") + ", rain " + rain;
        }
    }
}

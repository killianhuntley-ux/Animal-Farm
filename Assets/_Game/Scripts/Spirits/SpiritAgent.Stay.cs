using AnimalFarm.Core;
using AnimalFarm.Onboarding;
using AnimalFarm.Requirements;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Muscle 11 - a visitor decides to stay on its OWN (owner verdict
    /// 2026-10-01, core loop: "a creature shows up and decides to join on its
    /// own"; no feeding quota, no trust meter).
    ///
    /// While a visitor is on the land and its STAY gate (GateChain.resident) is
    /// met, game-hours of "being comfortable" accumulate. After a short
    /// settling-in grace it rolls once per game-hour (~30%, scaled by how it
    /// feels about the ground); a success starts the decision beat: it looks
    /// around, hops, then walks to a free home of its species (else over to
    /// the shepherd), waits until the shepherd is close enough to see, and
    /// only then becomes a Resident - which raises the existing naming
    /// ceremony (SpiritManager.NamingRequested). If the Stay gate stays unmet
    /// for a long stretch the visitor simply wanders off again, no penalty.
    ///
    /// Hard-No ground (muscle 02): a silhouette or visitor standing on ground
    /// its species Hard-No's drifts away and fades (it never visits there).
    ///
    /// Persisted: the met-hours counter rides the spirit record (stayHours).
    /// The beat itself, the leave timer and the revisit cooldown are
    /// deliberately in-memory (a reload mid-beat just re-rolls soon).
    /// </summary>
    public partial class SpiritAgent
    {
        // ---- tuning (ASSUMPTION values; all easy dials) -------------------------------
        private const float StayGraceHours = 2f;          // met-time before the first roll (settling in)
        private const float StayRollHours = 1f;           // one roll per game-hour of met-time after that
        private const float StayChance = 0.30f;           // base chance per roll (x ground mood)
        private const float StayTutorialChance = 0.5f;    // while the guide-light tutorial is running the dice are kinder
        private const float StayLeaveHours = 6f;          // Stay gate unmet this long (visit gate open) -> wanders off
        private const float StayRevisitCooldownHours = 3f;// a visitor that wandered off stays a silhouette this long
        private const float StayBeatSeconds = 2.4f;       // look around + hop
        private const float StayApproachSpeedMul = 1.5f;
        private const float StayApproachMaxSeconds = 14f;
        private const float StayArriveHome = 0.35f;
        private const float StayArrivePlayer = 2.2f;
        private const float StayCeremonyRange = 15f;      // the shepherd must be this close for the ceremony
        private const float StaySettleMaxSeconds = 180f;  // never wait forever on a shepherd who is far away
        private const float DriftFadeSeconds = 2.6f;      // Hard-No ground: drift off while fading
        private const float DriftSpeed = 1.1f;

        private enum StayPhase { None, Beat, Approach, Settle }

        private StayPhase _stayPhase = StayPhase.None;
        private float _stayMetHours;        // SAVED (record.stayHours)
        private int _stayRollsDone;         // derived from _stayMetHours on load
        private float _stayUnmetHours;
        private float _stayPrevHours = -1f;
        private float _stayTimer;           // real seconds inside the current phase
        private Home _stayHome;
        private bool _stayLeaving;          // retreating because the Stay gate lapsed
        private bool _stayHopped2;
        private float _revisitNotBeforeHours = -1f;
        private Vector3 _driftDir;

        /// <summary>Game-hours this visitor has spent with its Stay gate met (saved).</summary>
        public float StayMetHours => _stayMetHours;

        /// <summary>True from the "decides to stay" beat until it becomes a resident.</summary>
        public bool IsDecidingToStay => _stayPhase != StayPhase.None;

        private static int RollsFor(float metHours) =>
            Mathf.FloorToInt(Mathf.Max(0f, metHours - StayGraceHours) / StayRollHours);

        private void LoadStayHours(float hours)
        {
            _stayMetHours = Mathf.Max(0f, hours);
            _stayRollsDone = RollsFor(_stayMetHours); // never double-roll hours already rolled
        }

        /// <summary>Is the species already at its resident cap (a visitor then lingers, never joins)?</summary>
        private bool SpeciesFull()
        {
            var mgr = SpiritManager.Instance;
            return mgr != null && _species != null && mgr.CountResidents(_species.id) >= _species.maxResidents;
        }

        // ---- per-frame: the slow "is it comfortable here" accumulator + the roll ---------

        private void TickStay()
        {
            if (_state != SpiritState.Visitor)
            {
                _stayPrevHours = -1f;
                return;
            }
            if (_species == null || _species.gateChain == null || _ceremony || _despawning) return;

            var clock = GameClock.Instance;
            var evaluator = RequirementEvaluator.Instance;
            if (clock == null || evaluator == null) return;

            float now = clock.TotalHours;
            float dh = _stayPrevHours < 0f ? 0f : Mathf.Clamp(now - _stayPrevHours, 0f, 1f); // clamp: console day jumps
            _stayPrevHours = now;

            if (_stayPhase != StayPhase.None) return;     // already decided; the beat owns it
            if (_returningToBorder || _stayLeaving) return; // already on its way out

            string chainId = _species.gateChain.chainId;
            if (!evaluator.IsGateOpen(chainId, Gate.Visit)) return; // the Visit retreat handles this

            bool met = evaluator.IsGateOpen(chainId, Gate.Resident) && !SpeciesFull()
                && GroundAffinity != Affinity.HardNo;

            if (met)
            {
                _stayUnmetHours = 0f;
                _stayMetHours += dh;

                int due = RollsFor(_stayMetHours);
                if (due > _stayRollsDone)
                {
                    _stayRollsDone = due;
                    float chance = StayChance * StayGate.GroundChanceMul(GroundAffinity);
                    var onboarding = OnboardingManager.Instance;
                    if (onboarding != null && !onboarding.IsComplete) chance = Mathf.Max(chance, StayTutorialChance);
                    if (Random.value < chance) BeginStayDecision();
                }
            }
            else
            {
                _stayUnmetHours += dh;
                if (_stayUnmetHours >= StayLeaveHours) WanderOffForNow();
            }
        }

        /// <summary>The Stay gate has been unmet for a long stretch: it drifts back to the border (no penalty).</summary>
        private void WanderOffForNow()
        {
            _stayLeaving = true;
            _returningToBorder = true;
            _wanderTarget = NearestBorderPoint(transform.position);
            _hasTarget = true;
            _idleTimer = 0f;
            _stayMetHours = 0f;
            _stayRollsDone = 0;
            _stayUnmetHours = 0f;
            var clock = GameClock.Instance;
            _revisitNotBeforeHours = (clock != null ? clock.TotalHours : 0f) + StayRevisitCooldownHours;
            if (_quirk != SpiritQuirks.Kind.None) EndQuirk();
            Debug.Log($"[Spirits] {DisplayName} wanders off (the garden is not quite what it wanted).");
        }

        // ---- the decision beat ---------------------------------------------------------

        /// <summary>Starts the "decides to stay" beat. False when it cannot (not a visitor / already deciding).</summary>
        private bool BeginStayDecision()
        {
            if (_state != SpiritState.Visitor || _stayPhase != StayPhase.None || _despawning || _ceremony)
                return false;

            _stayPhase = StayPhase.Beat;
            _stayTimer = 0f;
            _stayHopped2 = false;
            _stayLeaving = false;
            _returningToBorder = false;
            _hasTarget = false;
            if (_quirk != SpiritQuirks.Kind.None) EndQuirk();

            _hopTimer = 0f;
            SpiritVoice.Play(_species, VoiceIntent.Happy, 0.9f);
            FloatingText.Show(transform.position + Vector3.up * 0.9f, "decides to stay!", TaskGold);
            Puffs.Burst(transform.position, RadiantGold, 6, 0.9f, 0.6f, 0.1f);
            if (_bubble != null) _bubble.Show(WantKind.Home, WantBubbleSeconds);
            Debug.Log($"[Spirits] {DisplayName} decides to stay.");
            return true;
        }

        /// <summary>
        /// Owns movement while the decision plays out (called from Update in
        /// the movement chain). True = this frame's movement is handled.
        /// </summary>
        private bool TickStayDecision()
        {
            if (_stayPhase == StayPhase.None) return false;
            if (_state != SpiritState.Visitor || _despawning)
            {
                EndStayBeat();
                return false;
            }

            _stayTimer += Time.deltaTime;

            switch (_stayPhase)
            {
                case StayPhase.Beat:
                {
                    // Look left, look right, a second little hop.
                    float t = _stayTimer;
                    bool flip = (t >= 0.35f && t < 0.8f) || (t >= 1.25f && t < 1.7f);
                    if (Renderer != null) Renderer.flipX = flip;
                    if (!_stayHopped2 && t >= 1.5f)
                    {
                        _stayHopped2 = true;
                        if (_hopTimer < 0f) _hopTimer = 0f;
                    }

                    if (_stayTimer >= StayBeatSeconds)
                    {
                        if (Renderer != null) Renderer.flipX = false;
                        _stayHome = FindFreeHomeHere(); // muscle 02: never a home on Hard No ground
                        _stayPhase = StayPhase.Approach;
                        _stayTimer = 0f;
                    }
                    return true;
                }

                case StayPhase.Approach:
                {
                    Vector3 target;
                    float arrive;
                    if (_stayHome != null) { target = _stayHome.transform.position; arrive = StayArriveHome; }
                    else if (TryPlayerSide(out target)) { arrive = StayArrivePlayer; }
                    else { _stayPhase = StayPhase.Settle; _stayTimer = 0f; return true; }

                    Vector3 to = target - transform.position;
                    to.z = 0f;
                    float dist = to.magnitude;
                    if (dist <= arrive || _stayTimer >= StayApproachMaxSeconds)
                    {
                        _stayPhase = StayPhase.Settle;
                        _stayTimer = 0f;
                        return true;
                    }

                    float step = Mathf.Min(_species.wanderSpeed * StayApproachSpeedMul * Time.deltaTime, dist);
                    transform.position += to / dist * step;
                    return true;
                }

                default: // Settle: hold until the shepherd is close enough to see it, then join
                {
                    float dist = DistanceToPlayer();
                    bool shepherdNear = float.IsPositiveInfinity(dist) || dist <= StayCeremonyRange;
                    bool timedOut = _stayTimer >= StaySettleMaxSeconds;
                    if (shepherdNear || timedOut)
                    {
                        // The timeout only overrides a modal hold; an event or pause is always waited out.
                        if (CanBeginStayCeremonyNow(timedOut))
                        {
                            FinishStayDecision();
                            return true;
                        }
                        // Held back: idle near home, glancing about.
                        if (Renderer != null) Renderer.flipX = ((int)(_stayTimer / 1.4f) & 1) == 1;
                    }
                    return true;
                }
            }
        }

        /// <summary>
        /// The naming ceremony may start now: no competition event, not paused
        /// and (unless <paramref name="ignoreModals"/>, the settle timeout) no
        /// shop / menu / text field / other ceremony holding the input lock.
        /// </summary>
        private static bool CanBeginStayCeremonyNow(bool ignoreModals = false)
        {
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return false;
            var gm = GameManager.Instance;
            if (gm != null && gm.IsPaused) return false;
            if (ignoreModals) return true;
            return !UIInputLock.AnyOwnerHolds;
        }

        /// <summary>A point beside the shepherd to amble over to (no free home to pick).</summary>
        private bool TryPlayerSide(out Vector3 point)
        {
            point = default;
            if (float.IsPositiveInfinity(DistanceToPlayer()) || _playerTransform == null) return false;
            point = _playerTransform.position;
            point.z = 0f;
            return true;
        }

        private void FinishStayDecision()
        {
            if (Renderer != null) Renderer.flipX = false;
            _stayPhase = StayPhase.None;

            // Move into the home it walked to (if it is still free); else the
            // normal slow tick claims one later ("Home?" want bubble until then).
            if (_stayHome != null && _stayHome.IsFree && _stayHome.TryClaim(this))
            {
                _home = _stayHome;
                FloatingText.Show(transform.position + Vector3.up * 0.8f, "Home!", HomePaleBlue);
            }
            _stayHome = null;
            _stayLeaving = false;
            _stayMetHours = 0f;
            _stayRollsDone = 0;

            BecomeResident(); // raises SpiritManager.NamingRequested -> the naming ceremony
        }

        private void EndStayBeat()
        {
            if (Renderer != null) Renderer.flipX = false;
            _stayPhase = StayPhase.None;
            _stayHome = null;
        }

        // ---- Hard-No ground: drift away and fade ------------------------------------------

        /// <summary>
        /// A silhouette or visitor on ground its species Hard-No's drifts toward
        /// the border while fading (about 2.6 s), then despawns - it never
        /// visits there. The manager may respawn a silhouette on kinder ground.
        /// </summary>
        private void BeginDriftAway()
        {
            if (_despawning) return;

            Vector3 to = NearestBorderPoint(transform.position) - transform.position;
            to.z = 0f;
            _driftDir = to.sqrMagnitude > 0.01f ? to.normalized : (Vector3)Random.insideUnitCircle.normalized;
            _despawnFadeSeconds = DriftFadeSeconds;
            _despawning = true;
            _despawnTimer = 0f;
            _hasTarget = false;
            if (_quirk != SpiritQuirks.Kind.None) EndQuirk();
            EndStayBeat();

            if (_state == SpiritState.Visitor && DistanceToPlayer() <= 14f)
                FloatingText.Show(transform.position + Vector3.up * 0.8f, "(it drifts off - wrong ground)", HerdGreyBlue);
            Debug.Log($"[Spirits] {DisplayName} drifts away from ground it cannot abide.");
        }

        /// <summary>A visitor wander point that avoids ground its species Hard-No's (a few tries).</summary>
        private Vector3 PickVisitorPoint()
        {
            Vector3 p = PickInFieldPoint();
            for (int i = 0; i < 6; i++)
            {
                if (!BiomeAffinity.WontLiveIn(_species, BiomeGround.BiomeAt(p))) return p;
                p = PickInFieldPoint();
            }
            return p;
        }

        // ---- want bubble: what would it need to stay? ---------------------------------------

        /// <summary>
        /// The visitor's current want as a _wantActive index (WantFood / WantWater /
        /// WantLand for the first unmet Stay condition by topic, WantHome when the
        /// gate is met and it is considering a roof), or -1 for nothing to show.
        /// </summary>
        private int VisitorWantIndex()
        {
            if (_state != SpiritState.Visitor || _stayPhase != StayPhase.None || _returningToBorder) return -1;

            var set = StayGate.SetOf(_species);
            if (set == null) return -1;

            var conditions = set.Conditions;
            bool anyUnmet = false;
            if (conditions != null)
            {
                for (int i = 0; i < conditions.Length; i++)
                {
                    var c = conditions[i];
                    if (c == null || c.Evaluate()) continue;
                    anyUnmet = true;
                    switch (c.Topic)
                    {
                        case ConditionTopic.Plant: return WantFood;
                        case ConditionTopic.Water: return WantWater;
                        case ConditionTopic.Ground: return WantLand;
                    }
                }
            }
            if (anyUnmet) return -1; // only time / other conditions missing: nothing legible to show
            return SpeciesFull() ? -1 : WantHome;
        }

        // ---- console helpers -------------------------------------------------------------------

        /// <summary>Console: force the stay decision now (visitors only), ignoring gate and dice.</summary>
        public bool Debug_ForceStay() => BeginStayDecision();

        /// <summary>Console: one-line stay progress for this spirit.</summary>
        public string Debug_StayStatus()
        {
            string who = DisplayName + " (" + _state + ")";
            if (_state != SpiritState.Visitor) return who + ": not a visitor.";
            if (_stayPhase != StayPhase.None) return who + ": deciding to stay (" + _stayPhase + ").";

            string gate = StayGate.IsMet(_species) ? "stay gate MET" : "stay gate unmet";
            string rolls = _stayMetHours < StayGraceHours
                ? "settling in " + _stayMetHours.ToString("0.0") + "/" + StayGraceHours.ToString("0.#") + "h"
                : _stayRollsDone + " roll(s) so far";
            string leave = _stayUnmetHours > 0f
                ? ", unmet " + _stayUnmetHours.ToString("0.0") + "/" + StayLeaveHours.ToString("0.#") + "h before it wanders off"
                : "";
            return who + ": " + gate + ", met " + _stayMetHours.ToString("0.0") + "h (" + rolls + ")" + leave
                + ", chance/roll " + Mathf.RoundToInt(StayChance * StayGate.GroundChanceMul(GroundAffinity) * 100f) + "%"
                + (SpeciesFull() ? ", species FULL (will not join)" : "");
        }
    }
}

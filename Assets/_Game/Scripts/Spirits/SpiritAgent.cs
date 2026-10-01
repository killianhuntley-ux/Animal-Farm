using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.Requirements;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>Lifecycle stage of one spirit individual.</summary>
    public enum SpiritState { Silhouette, Visitor, Resident, Runaway }

    /// <summary>Three-band mood read (muscle 03): mood as body language.</summary>
    public enum SpiritMoodBand { Happy, Neutral, Low }

    /// <summary>One spirit's persisted fields (JsonUtility-friendly).</summary>
    [System.Serializable]
    public struct SpiritSaveRecord
    {
        public string speciesId;
        public int state;
        public string givenName;
        public float spirit;
        public float hunger01;
        public int fedCount;
        public float x, y;
        public int timesFed;
        public int taskProgress;
        public bool taskDone;
        public float residentSince;
        public int compEntries;
        public int compWins;
        public int vigor, grace, gleam;
        public float lastFed;
    }

    /// <summary>
    /// One spirit in the field (slice 03). Spirits are ghosts: no Rigidbody2D,
    /// no blocking colliders — pure transform drift, water is not an obstacle.
    /// The only collider is a trigger circle so the InteractionSensor's
    /// OverlapCircle can find them.
    /// </summary>
    public class SpiritAgent : MonoBehaviour, IInteractable, ISelectable
    {
        // ---- tuning constants ------------------------------------------------

        private const float BorderInset = 1.5f;        // spawn/wander distance inside the walls
        private const float BobAmplitude = 0.06f;
        private const float BobFrequency = 0.7f;       // Hz
        private const float RunawayBobFrequency = 0.35f; // sad, slower hover
        private const float HerdingBobFrequency = 1.4f;  // nervous in-place hop while waiting mid-chase
        private const float HerdDashSpeedMultiplier = 3.2f;
        private const int HerdDashResamples = 5;          // away-from-player bias attempts
        private const float HerdTagTimeoutRealSeconds = 45f; // give-up window between tags
        private const int HerdClumsyGraceMax = 4;         // Grace <= this = easier chase (2 tags)
        private const float ShimmerAmplitude = 0.05f;
        private const float ShimmerFrequency = 1.7f;   // Hz
        private const float SilhouetteAlpha = 0.6f;
        private const float SleepingAlpha = 0.45f;
        private const float ArriveDistance = 0.08f;
        private const float StateCheckInterval = 0.5f; // gate polling (evaluator caches anyway)
        private const float RunawayGraceRealSeconds = 30f;
        private const float SootheCooldownRealSeconds = 20f;
        private const float DespawnFadeSeconds = 1f;
        private const float HungryPromptThreshold = 0.5f; // Hunger01 at which "Feed" replaces "Soothe"
        private const float FocusScale = 1.08f;
        private const float HopDuration = 0.35f;
        private const float HopHeight = 0.18f;
        private const float HomeTickInterval = 2f;      // slow tick: home claiming + task scans
        private const float FollowSpiritThreshold = 60f;
        private const float FollowStopDistance = 1.1f;
        private const float TendDistance = 2.6f;         // click-menu feed/soothe reach
        private const float CeremonyDriftSpeed = 1.5f;
        private const float RadiantPulseSeconds = 1.5f;

        // muscle 03: personality tuning
        private const float MoodHappyThreshold = 70f;    // Spirit >= this = happy band
        private const float MoodLowThreshold = 35f;      // Spirit < this = low band
        private const float LowMoodSpeedMul = 0.75f;     // drooped spirits drag
        private const float NocturnalNightSpeedMul = 1.3f;
        private const float WantBubbleSeconds = 3f;
        private const float WantProximityRadius = 2f;    // walk-by re-show range
        private const float WantProximityCooldown = 12f; // real seconds between re-shows
        private const float WantCheckInterval = 0.5f;
        private const float GreetDistance = 2.5f;        // first-meet-of-day range
        private const float StartleDistance = 1.5f;
        private const float StartleSprintSpeed = 5.6f;   // above walk cap (4.5), below sprint cap (7.2)
        private const float StartleCooldown = 4f;        // real seconds between flinches
        private const float FlinchDuration = 0.45f;
        private const float QuirkChance = 0.35f;         // per idle-timer expiry
        private const float DriftNoticeDistance = 6f;    // mood drift only reacts within this

        private static readonly Color SilhouetteColor = new Color(0.18f, 0.18f, 0.28f, 1f); // lightened: readable at night
        private static readonly Color RadiantGold = new Color(1f, 0.95f, 0.65f);
        private static readonly Color TaskGold = new Color(1f, 0.9f, 0.4f);
        private static readonly Color HomePaleBlue = new Color(0.7f, 0.85f, 1f);
        private static readonly Color HerdGreyBlue = new Color(0.62f, 0.7f, 0.85f);

        // ---- public surface --------------------------------------------------

        public SpiritState State => _state;
        public SpiritSpeciesDefinition Species => _species;
        public string GivenName => _givenName;
        /// <summary>Morale, 0..100.</summary>
        public float Spirit => _spirit;
        /// <summary>0 = just fed, 1 = starving.</summary>
        public float Hunger01 => _hunger01;
        public bool IsSleeping => _isSleeping;
        public int FedCount => _fedCount;

        /// <summary>True while this resident trails the shepherd.</summary>
        public bool IsFollowing => _following;
        /// <summary>The home this resident has claimed, or null.</summary>
        public Home CurrentHome => _home;
        public bool HasHome => CurrentHome != null;
        /// <summary>Final-wish state (slice 04).</summary>
        public bool TaskDone => _taskDone;
        public int TaskProgress => _taskProgress;
        /// <summary>Fulfilment: resident, housed, spirit maxed, wish granted.</summary>
        public bool IsFulfilled => _state == SpiritState.Resident && HasHome && _spirit >= 100f && _taskDone;
        /// <summary>Total feeds ever (visitor + resident + task offers).</summary>
        public int TimesFed => _timesFed;
        /// <summary>GameClock.TotalHours stamped when this spirit became a resident.</summary>
        public float ResidentSinceTotalHours => _residentSinceTotalHours;
        /// <summary>Fame (slice 05): competitions entered / won.</summary>
        public int CompetitionEntries => _compEntries;
        public int CompetitionWins => _compWins;

        /// <summary>
        /// Per-individual stats, rolled 2-9 once at spawn (Pokemon-IV-style: two
        /// spirits of the same species roll differently, so collecting multiples
        /// pays off). Vigor = strength/stamina (boulder), Grace = speed/agility
        /// (future races), Gleam = charm/shine (future shows + essence).
        /// </summary>
        public int Vigor => _vigor;
        public int Grace => _grace;
        public int Gleam => _gleam;

        /// <summary>Records one competition entry (and the win, if any).</summary>
        public void RecordCompetition(bool won)
        {
            _compEntries++;
            if (won) _compWins++;
        }

        /// <summary>
        /// Mood read at a glance (muscle 03). Species-specific expression can
        /// override the procedural posture later; today every species shares
        /// the grammar (happy = bouncy, low = drooped + dragging).
        /// </summary>
        public SpiritMoodBand MoodBand =>
            _spirit >= MoodHappyThreshold ? SpiritMoodBand.Happy
            : _spirit < MoodLowThreshold ? SpiritMoodBand.Low
            : SpiritMoodBand.Neutral;

        /// <summary>True when free for a pair interaction (SpiritSocialManager).</summary>
        public bool IsSocialIdle =>
            CanActPersonality && !_following
            && _quirk == SpiritQuirks.Kind.None && _flinchTimer < 0f;

        /// <summary>Body renderer; the manager assigns the shared sprite material after Init.</summary>
        public SpriteRenderer Renderer { get; private set; }

        /// <summary>Toggles follow mode. Refused unless a resident with Spirit >= 60.</summary>
        public void SetFollowing(bool on)
        {
            if (on && (_state != SpiritState.Resident || _spirit < FollowSpiritThreshold)) return;
            _following = on;
            if (on)
            {
                _hasTarget = false; // drop the wander target; TickFollow takes over
                if (_quirk != SpiritQuirks.Kind.None) EndQuirk();
            }
        }

        /// <summary>Called by Home when it despawns under us.</summary>
        public void NotifyHomeLost(Home h)
        {
            if (_home == h) _home = null; // homeless again; no mood hit this slice
        }

        /// <summary>
        /// Follow Treat (economy slice): consumes one "treat" and makes this
        /// resident follow for 2 game-hours regardless of the trust threshold.
        /// Timer is not saved — a loaded treat simply wears off.
        /// </summary>
        public void GiveTreat()
        {
            if (_state != SpiritState.Resident || _following) return;
            if (Inventory.Instance == null || !Inventory.Instance.Consume("treat", 1)) return;

            _following = true;
            _hasTarget = false;
            _treatUntilTotalHours =
                (GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f) + 2f;
            _spirit = Mathf.Clamp(_spirit + 5f, 0f, 100f);
            AnimalFarm.UI.FloatingText.Show(transform.position + Vector3.up * 0.8f,
                "snack-bribed!", new Color(1f, 0.9f, 0.4f));
        }

        private float _treatUntilTotalHours = -1f;

        // Muscle 07 (villains): a Scarer's fright can never drop Spirit below
        // this floor - a Scarer alone must never cause a runaway (calibration
        // law: villain damage stays small, visible, recoverable).
        private const float FrightFloor = 25f;

        /// <summary>
        /// One-shot villain fright (muscle 07, Scarer): a small Spirit hit,
        /// floored at <see cref="FrightFloor"/>, plus the flinch recoil and a
        /// startle squeak. A spirit already below the floor is left where it
        /// is (frights never heal, never runaway-spiral).
        /// </summary>
        public void ApplyFright(float amount)
        {
            if (amount <= 0f) return;
            if (_state != SpiritState.Resident && _state != SpiritState.Visitor) return;

            float floor = Mathf.Min(_spirit, FrightFloor);
            _spirit = Mathf.Clamp(Mathf.Max(_spirit - amount, floor), 0f, 100f);

            _flinchTimer = 0f;
            SpiritVoice.Play(_species, VoiceIntent.Startle, 0.9f);
        }

        public void Debug_SetSpirit(float v) => _spirit = Mathf.Clamp(v, 0f, 100f);
        public void Debug_CompleteTask() => CompleteTask();

        /// <summary>
        /// Locks all behaviour (no wander/follow/sleep/needs/gates, CanInteract
        /// false) and drifts to the platform center. Tiny bob continues.
        /// </summary>
        public void EnterCeremony(Vector3 platformCenter)
        {
            _ceremony = true;
            _ceremonyCenter = new Vector3(platformCenter.x, platformCenter.y, 0f);
            _hasTarget = false;
            _isSleeping = false;
            if (_quirk != SpiritQuirks.Kind.None) EndQuirk();
            if (_bubble != null) _bubble.HideNow();
        }

        /// <summary>Releases the ceremony lock.</summary>
        public void ExitCeremony()
        {
            if (!_ceremony) return;
            _ceremony = false;
            _hasTarget = false;
            _idleTimer = Random.Range(1f, 3f);
            // Don't decay spirit retroactively for the locked stretch.
            if (GameClock.Instance != null) _prevTotalHours = GameClock.Instance.TotalHours;
        }

        // ---- state -----------------------------------------------------------

        private SpiritSpeciesDefinition _species;
        private SpiritState _state = SpiritState.Silhouette;
        private string _givenName = "";
        private float _spirit = 60f;
        private float _hunger01;
        private int _fedCount;
        private bool _isSleeping;

        private float _lastFedTotalHours;
        private float _prevTotalHours;

        // slice 04: fulfilment / follow / ascension
        private Home _home;
        private bool _following;
        private Transform _playerTransform; // cached FindWithTag("Player")
        private bool _taskDone;
        private int _taskProgress;
        private int _timesFed;
        private float _residentSinceTotalHours;
        private int _compEntries;
        private int _compWins;

        // slice 05: per-individual stats (see Vigor/Grace/Gleam)
        private int _vigor;
        private int _grace;
        private int _gleam;
        private float _homeTickTimer;
        private bool _ceremony;
        private Vector3 _ceremonyCenter;

        // wander
        private Vector3 _wanderTarget;
        private bool _hasTarget;
        private float _idleTimer;
        private bool _returningToBorder; // visitor walking back to become a silhouette

        // herding recovery (slice 05 leftover): catching a Runaway is a short
        // chase, not an instant soothe. 0 = not herding; > 0 = tags still
        // needed. State stays Runaway for the whole chase ON PURPOSE:
        // RepoManManager watches State == Runaway, so the repo clock keeps
        // ticking while you chase - that urgency IS the minigame.
        private int _herdingTagsLeft;
        private bool _herdDashing;            // sprinting to the current dash point
        private float _lastHerdTagRealTime;   // unscaled; give-up timer base

        // timers / effects
        private float _stateCheckTimer;
        private float _lowSpiritSeconds;
        private float _lastSootheRealTime = -999f;
        private float _hopTimer = -1f;
        private bool _despawning;
        private float _despawnTimer;
        private Vector3 _baseScale = Vector3.one;
        private bool _focused;

        // muscle 03: personality state. All in-memory only - daily greeting
        // flags and quirk timers deliberately do not save (standing law:
        // ISaveable only if truly needed).
        private AnimalFarm.UI.WantBubble _bubble;
        private const int WantFood = 0, WantWater = 1, WantLonely = 2, WantHome = 3; // mirror AnimalFarm.UI.WantKind
        private readonly bool[] _wantActive = new bool[4];
        private float _wantCheckTimer;
        private float _wantProximityReadyAt;   // unscaled
        private bool _playerInBubbleRange;
        private int _lastGreetedDay = -1;      // first-meet-of-day flag, per game-day
        private float _startleReadyAt;
        private float _flinchTimer = -1f;
        private AnimalFarm.Player.ShepherdController _shepherdCtrl;
        private float _nextHappyHopAt;
        private float _nextZzzAt;
        private float _nextHabitatAt;
        private bool _lingerOnArrive;          // longer idle after mood-drift/habitat walks

        private SpiritQuirks.Kind _quirk = SpiritQuirks.Kind.None;
        private float _quirkTimer;
        private float _quirkDuration;
        private Vector3 _quirkPoint;           // LeafChase dash / Squabble retreat target
        private Vector3 _quirkCenter;          // CirclePlay midpoint
        private float _quirkAngle;
        private float _quirkRadius;
        private float _quirkSpin;              // radians/sec, signed
        private int _quirkDashesLeft;

        // ---- setup -----------------------------------------------------------

        /// <summary>
        /// Configures a freshly spawned (or freshly restored) spirit. The manager
        /// assigns <see cref="Renderer"/>.sharedMaterial afterwards.
        /// </summary>
        public void Init(SpiritSpeciesDefinition species, SpiritState state, Vector3 pos)
        {
            _species = species;
            _state = state;
            transform.position = pos;
            transform.localScale = Vector3.one * 1.6f; // greybox readability: spirits were too small vs shepherd
            _baseScale = transform.localScale;

            // Body renderer on a child so the y-bob never fights root movement.
            if (Renderer == null)
            {
                var body = new GameObject("Body");
                body.transform.SetParent(transform, false);
                Renderer = body.AddComponent<SpriteRenderer>();
            }
            Renderer.sortingOrder = 0;
            if (species != null)
            {
                Renderer.sprite = species.bodySprite;
                Renderer.color = state == SpiritState.Silhouette ? SilhouetteColor : species.tint;
            }

            // Trigger-only collider so the InteractionSensor's OverlapCircle finds us.
            var col = GetComponent<CircleCollider2D>();
            if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;

            gameObject.name = species != null && !string.IsNullOrEmpty(species.displayName)
                ? $"Spirit ({species.displayName})"
                : "Spirit";

            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;
            _lastFedTotalHours = now;
            _prevTotalHours = now;

            RollStats();

            // Muscle 03: want bubble child (build-once, refresh-in-place) and
            // the pair-interaction coordinator (runtime GetOrCreate).
            if (_bubble == null)
                _bubble = AnimalFarm.UI.WantBubble.Attach(transform, 0.72f);
            _nextHappyHopAt = Time.time + Random.Range(4f, 10f);
            _nextHabitatAt = Time.time + Random.Range(10f, 25f);
            SpiritSocialManager.Ensure();
        }

        /// <summary>Rolls the per-individual stats (2-9 each), once per spawn.</summary>
        private void RollStats()
        {
            _vigor = Random.Range(2, 10);
            _grace = Random.Range(2, 10);
            _gleam = Random.Range(2, 10);
        }

        /// <summary>Restores per-individual fields from a save record (after Init;
        /// position and state stay with Init).</summary>
        public void ApplyLoadedState(SpiritSaveRecord rec)
        {
            _spirit = Mathf.Clamp(rec.spirit, 0f, 100f);
            _hunger01 = Mathf.Clamp01(rec.hunger01);
            _fedCount = Mathf.Max(0, rec.fedCount);
            _lastFedTotalHours = rec.lastFed;
            _timesFed = Mathf.Max(0, rec.timesFed);
            _taskProgress = Mathf.Max(0, rec.taskProgress);
            _taskDone = rec.taskDone;
            _residentSinceTotalHours = rec.residentSince;
            _compEntries = Mathf.Max(0, rec.compEntries);
            _compWins = Mathf.Max(0, rec.compWins);

            // Stats floor: a pre-stats save has vigor 0 -- reroll so it stays
            // valid, and rebuild the fed-timestamp it also lacks from hunger01.
            if (rec.vigor == 0)
            {
                RollStats();
                if (GameClock.Instance != null && _species != null)
                    _lastFedTotalHours = GameClock.Instance.TotalHours
                        - _hunger01 * Mathf.Max(0.01f, _species.hungerHours);
            }
            else
            {
                _vigor = rec.vigor;
                _grace = rec.grace;
                _gleam = rec.gleam;
            }

            if (!string.IsNullOrEmpty(rec.givenName)) SetGivenName(rec.givenName);
        }

        public void SetGivenName(string n)
        {
            _givenName = n ?? "";
            if (!string.IsNullOrEmpty(_givenName))
                gameObject.name = $"Spirit {_givenName}";
        }

        public SpiritSaveRecord ToRecord() => new SpiritSaveRecord
        {
            speciesId = _species != null ? _species.id : "",
            state = (int)_state,
            givenName = _givenName,
            spirit = _spirit,
            hunger01 = _hunger01,
            fedCount = _fedCount,
            x = transform.position.x,
            y = transform.position.y,
            timesFed = _timesFed,
            taskProgress = _taskProgress,
            taskDone = _taskDone,
            residentSince = _residentSinceTotalHours,
            compEntries = _compEntries,
            compWins = _compWins,
            vigor = _vigor,
            grace = _grace,
            gleam = _gleam,
            lastFed = _lastFedTotalHours
        };

        // ---- per-frame -------------------------------------------------------

        private void Update()
        {
            if (_species == null) return;

            if (_despawning)
            {
                TickDespawnFade();
                return;
            }

            if (_ceremony)
            {
                // Locked: no sleep/needs/gates/wander/follow — just drift and bob.
                TickCeremony();
                TickVisuals();
                return;
            }

            TickSleep();
            TickNeeds();
            TickStateGates();
            TickHerding();
            TickHomeAndTask();
            TickPresence();
            TickWantBubble();
            if (!TickQuirk())
            {
                if (_following && _state == SpiritState.Resident) TickFollow();
                else TickWander();
            }
            TickVisuals();
        }

        private void TickCeremony()
        {
            transform.position = Vector3.MoveTowards(
                transform.position, _ceremonyCenter, CeremonyDriftSpeed * Time.deltaTime);
        }

        private void TickDespawnFade()
        {
            _despawnTimer += Time.deltaTime;
            float fade = Mathf.Clamp01(1f - _despawnTimer / DespawnFadeSeconds);
            if (Renderer != null)
            {
                var c = Renderer.color;
                c.a = SilhouetteAlpha * fade;
                Renderer.color = c;
            }
            if (_despawnTimer >= DespawnFadeSeconds)
            {
                if (SpiritManager.Instance != null) SpiritManager.Instance.Despawn(this);
                else Destroy(gameObject);
            }
        }

        private void TickSleep()
        {
            bool wasSleeping = _isSleeping;

            _isSleeping = false;
            if (_state != SpiritState.Resident || GameClock.Instance == null) return;

            float h = GameClock.Instance.Hours;
            bool isDayWindow = h >= 6f && h < 18f;
            switch (_species.activity)
            {
                case ActivityWindow.Day: _isSleeping = !isDayWindow; break;
                case ActivityWindow.Night: _isSleeping = isDayWindow; break;
                default: _isSleeping = false; break;
            }

            // Settling down: cancel whatever it was doing, murmur, start zzz.
            if (_isSleeping && !wasSleeping)
            {
                if (_quirk != SpiritQuirks.Kind.None) EndQuirk();
                if (_bubble != null) _bubble.HideNow();
                _nextZzzAt = Time.time + Random.Range(2f, 6f);
                SpiritVoice.Play(_species, VoiceIntent.Sleep, 0.5f);
            }
        }

        private void TickNeeds()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;

            float now = clock.TotalHours;
            float deltaHours = Mathf.Max(0f, now - _prevTotalHours);
            _prevTotalHours = now;

            if (_state != SpiritState.Resident && _state != SpiritState.Runaway)
            {
                _hunger01 = 0f;
                _lowSpiritSeconds = 0f;
                return;
            }

            // Herding pause: don't punish the chase - slide the fed stamp
            // forward so hunger freezes while herding tags are pending.
            if (_state == SpiritState.Runaway && _herdingTagsLeft > 0)
                _lastFedTotalHours += deltaHours;

            // Hunger accrues for residents (and runaways, so they come back hungry).
            float hungerHours = Mathf.Max(0.01f, _species.hungerHours);
            _hunger01 = Mathf.Clamp01((now - _lastFedTotalHours) / hungerHours);

            if (_state == SpiritState.Resident)
            {
                if (_hunger01 >= 1f)
                    _spirit = Mathf.Clamp(_spirit - _species.spiritDecayPerHungryHour * deltaHours, 0f, 100f);

                // Runaway trigger: sustained low spirit (paused/sleeping doesn't count).
                // Following pauses the grace accumulation — they're with you.
                if (!_isSleeping && _spirit < _species.runawayThreshold)
                {
                    if (!_following)
                    {
                        _lowSpiritSeconds += Time.deltaTime;
                        if (_lowSpiritSeconds >= RunawayGraceRealSeconds)
                            BecomeRunaway();
                    }
                }
                else
                {
                    _lowSpiritSeconds = 0f;
                }
            }
        }

        private void TickStateGates()
        {
            _stateCheckTimer -= Time.deltaTime;
            if (_stateCheckTimer > 0f) return;
            _stateCheckTimer = StateCheckInterval;

            var evaluator = RequirementEvaluator.Instance;
            if (evaluator == null || _species.gateChain == null) return;
            string chainId = _species.gateChain.chainId;

            switch (_state)
            {
                case SpiritState.Silhouette:
                    if (evaluator.IsGateOpen(chainId, Gate.Visit))
                    {
                        BecomeVisitor();
                    }
                    else if (!evaluator.IsGateOpen(chainId, Gate.Appear))
                    {
                        _despawning = true;
                        _despawnTimer = 0f;
                    }
                    break;

                case SpiritState.Visitor:
                    if (!evaluator.IsGateOpen(chainId, Gate.Visit))
                    {
                        if (!_returningToBorder)
                        {
                            _returningToBorder = true;
                            _wanderTarget = NearestBorderPoint(transform.position);
                            _hasTarget = true;
                            _idleTimer = 0f;
                        }
                    }
                    else
                    {
                        _returningToBorder = false; // gate reopened mid-retreat
                    }
                    break;
            }
        }

        private void TickWander()
        {
            if (_isSleeping) { _hasTarget = false; return; }

            // Runaways flee to the border (or finish a herding dash), then
            // just wait in place until tagged, settled, or recovered.
            if (_state == SpiritState.Runaway && !_hasTarget) return;

            if (!_hasTarget)
            {
                _idleTimer -= Time.deltaTime;
                if (_idleTimer > 0f) return;

                // Personality layer (muscle 03): quirk > mood drift > habitat
                // habit > plain wander. Quirks take over movement entirely.
                if (CanActPersonality)
                {
                    if (TryStartQuirk()) return;
                    if (TryMoodDriftTarget(out var drift)) { _wanderTarget = drift; _hasTarget = true; }
                    else if (TryHabitatTarget(out var hangout)) { _wanderTarget = hangout; _hasTarget = true; }
                }
                if (!_hasTarget)
                {
                    _wanderTarget = PickWanderTarget();
                    _hasTarget = true;
                }
            }

            float speed = _species.wanderSpeed * (_herdDashing ? HerdDashSpeedMultiplier : 1f);
            if (_state == SpiritState.Visitor || _state == SpiritState.Resident)
            {
                // Posture pace: low mood drags; nocturnal species perk up after dark.
                if (MoodBand == SpiritMoodBand.Low) speed *= LowMoodSpeedMul;
                if (_species.activity == ActivityWindow.Night
                    && GameClock.Instance != null && GameClock.Instance.IsNight)
                    speed *= NocturnalNightSpeedMul;
            }
            transform.position = Vector3.MoveTowards(
                transform.position, _wanderTarget, speed * Time.deltaTime);

            if ((transform.position - _wanderTarget).sqrMagnitude <= ArriveDistance * ArriveDistance)
            {
                _hasTarget = false;
                _herdDashing = false; // arrived: hop nervously in place, waiting
                _idleTimer = _lingerOnArrive ? Random.Range(3f, 6f) : Random.Range(1f, 3f);
                _lingerOnArrive = false;
                if (_species.activity == ActivityWindow.Night
                    && GameClock.Instance != null && GameClock.Instance.IsNight)
                    _idleTimer *= 0.55f; // night shift: noticeably busier after dark

                if (_state == SpiritState.Visitor && _returningToBorder)
                {
                    _returningToBorder = false;
                    BecomeSilhouette();
                }
            }
        }

        /// <summary>Herding give-up: no tag for HerdTagTimeoutRealSeconds sends
        /// the runaway back to the fence and cancels the chase (tags reset).</summary>
        private void TickHerding()
        {
            if (_state != SpiritState.Runaway || _herdingTagsLeft <= 0) return;
            if (Time.unscaledTime - _lastHerdTagRealTime < HerdTagTimeoutRealSeconds) return;

            _herdingTagsLeft = 0;
            _herdDashing = false;
            _wanderTarget = NearestBorderPoint(transform.position);
            _hasTarget = true;
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.8f,
                "(it settles warily at the fence)", HerdGreyBlue);
        }

        /// <summary>Slow tick (~2s, Residents only, awake): claim a free home, then
        /// watch for WaterNearHome wish completion.</summary>
        private void TickHomeAndTask()
        {
            _homeTickTimer -= Time.deltaTime;
            if (_homeTickTimer > 0f) return;
            _homeTickTimer = HomeTickInterval;

            if (_state != SpiritState.Resident || _isSleeping) return;

            if (!HasHome)
            {
                var home = Home.FindFree(_species.id, transform.position);
                if (home != null && home.TryClaim(this))
                {
                    _home = home;
                    AnimalFarm.UI.FloatingText.Show(
                        transform.position + Vector3.up * 0.8f, "Home!", HomePaleBlue);
                }
            }
            else if (!_taskDone && _species.taskKind == FinalTaskKind.WaterNearHome)
            {
                if (WaterNearHome()) CompleteTask();
            }
        }

        /// <summary>Any water cell within taskRadius (Chebyshev) of the home cell?</summary>
        private bool WaterNearHome()
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || _home == null) return false;

            Vector2Int c = _home.Cell;
            int r = Mathf.Max(0, _species.taskRadius);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (grid.GetSurface(new Vector2Int(c.x + dx, c.y + dy)) == Surface.Water)
                        return true;
            return false;
        }

        /// <summary>Marks the final wish done (permanent) with the gold blessing text.</summary>
        private void CompleteTask()
        {
            if (_taskDone) return;
            _taskDone = true;
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.8f, "Their wish is granted.", TaskGold);
        }

        /// <summary>Is the clock inside [taskWindowStartHour, taskWindowEndHour)? Wraps past midnight.</summary>
        private bool IsInTaskWindow()
        {
            var clock = GameClock.Instance;
            if (clock == null) return false;

            float start = Mathf.Repeat(_species.taskWindowStartHour, 24f);
            float end = Mathf.Repeat(_species.taskWindowEndHour, 24f);
            if (Mathf.Approximately(start, end)) return true; // degenerate window = always

            float h = clock.Hours;
            return start < end
                ? h >= start && h < end
                : h >= start || h < end; // wraps past midnight
        }

        /// <summary>Trails the shepherd, stopping FollowStopDistance away.</summary>
        private void TickFollow()
        {
            _hasTarget = false; // wander resumes fresh when following ends
            if (_isSleeping) return;

            // Treat wears off: low-trust followers stop; trusted ones carry on.
            if (_treatUntilTotalHours > 0f && GameClock.Instance != null
                && GameClock.Instance.TotalHours >= _treatUntilTotalHours)
            {
                _treatUntilTotalHours = -1f;
                if (_spirit < FollowSpiritThreshold) { _following = false; return; }
            }

            if (_playerTransform == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player == null) return;
                _playerTransform = player.transform;
            }

            Vector3 to = _playerTransform.position - transform.position;
            to.z = 0f;
            float dist = to.magnitude;
            if (dist <= FollowStopDistance) return;

            float speed = Mathf.Max(_species.wanderSpeed * 2.2f, 3.9f);
            float step = Mathf.Min(speed * Time.deltaTime, dist - FollowStopDistance);
            transform.position += to / dist * step;
        }

        private void TickVisuals()
        {
            if (Renderer == null) return;

            var mood = MoodBand;
            bool expressive = _state == SpiritState.Visitor || _state == SpiritState.Resident;

            // Procedural ghost float: sin y-bob + a feed hop on the body only.
            // Mid-herd (arrived, waiting to be tagged) it hops nervously fast.
            // Mood posture (muscle 03): happy bobs bouncier, low mood drags.
            float freq = BobFrequency;
            float amp = BobAmplitude;
            if (_state == SpiritState.Runaway)
            {
                freq = _herdingTagsLeft > 0 && !_herdDashing ? HerdingBobFrequency : RunawayBobFrequency;
            }
            else if (expressive)
            {
                if (mood == SpiritMoodBand.Happy) { freq *= 1.35f; amp *= 1.3f; }
                else if (mood == SpiritMoodBand.Low) { freq *= 0.6f; amp *= 0.7f; }
            }
            if (_isSleeping) { freq *= 0.3f; amp *= 0.5f; }
            if (_quirk == SpiritQuirks.Kind.Nap) amp *= 0.35f;

            float y = Mathf.Sin(Time.time * freq * 2f * Mathf.PI) * amp;

            // Happy spirits toss in a spontaneous hop now and then.
            if (expressive && !_isSleeping && mood == SpiritMoodBand.Happy
                && _quirk == SpiritQuirks.Kind.None && Time.time >= _nextHappyHopAt)
            {
                _nextHappyHopAt = Time.time + Random.Range(6f, 14f);
                if (_hopTimer < 0f) _hopTimer = 0f;
            }

            if (_hopTimer >= 0f)
            {
                _hopTimer += Time.deltaTime;
                float t = _hopTimer / HopDuration;
                if (t >= 1f) _hopTimer = -1f;
                else y += Mathf.Sin(t * Mathf.PI) * HopHeight;
            }

            // Low-mood droop: the whole body rides a little lower.
            if (expressive && !_isSleeping && mood == SpiritMoodBand.Low) y -= 0.08f;

            Renderer.transform.localPosition = new Vector3(0f, y, 0f);

            // Body-local scale: sleep curl, quirk squash/stretch, startle flinch.
            // (Root scale stays owned by Init/SetFocused; these never fight it.)
            Vector3 bodyScale = Vector3.one;
            if (_isSleeping)
            {
                // Curled up small, with a slow breathing swell + drifting zzz.
                float breathe = 1f + 0.035f * Mathf.Sin(Time.time * 2f * Mathf.PI * 0.25f);
                bodyScale = new Vector3(0.8f * breathe, 0.66f * breathe, 1f);

                if (Time.time >= _nextZzzAt)
                {
                    _nextZzzAt = Time.time + Random.Range(6f, 12f);
                    AnimalFarm.UI.FloatingText.Show(transform.position + Vector3.up * 0.9f,
                        "zzz", new Color(0.75f, 0.8f, 1f, 0.8f));
                }
            }
            else if (_quirk == SpiritQuirks.Kind.Nap)
            {
                bodyScale = new Vector3(1.06f, 0.8f, 1f); // lying low
            }
            else if (_quirk == SpiritQuirks.Kind.Stretch)
            {
                float t = Mathf.Clamp01(_quirkTimer / Mathf.Max(0.01f, _quirkDuration));
                float k = Mathf.Sin(t * Mathf.PI);
                bodyScale = new Vector3(1f - 0.12f * k, 1f + 0.22f * k, 1f);
            }

            if (_flinchTimer >= 0f)
            {
                _flinchTimer += Time.deltaTime;
                float t = _flinchTimer / FlinchDuration;
                if (t >= 1f) _flinchTimer = -1f;
                else bodyScale *= 1f - 0.22f * Mathf.Sin(t * Mathf.PI); // quick shrink-recoil
            }
            Renderer.transform.localScale = bodyScale;

            // Colour + alpha shimmer.
            Color baseColor = _state == SpiritState.Silhouette ? SilhouetteColor : _species.tint;
            float baseAlpha = _state == SpiritState.Silhouette ? SilhouetteAlpha
                : _isSleeping ? SleepingAlpha
                : baseColor.a;

            // Low-mood desaturation (drooped + washed out); naps tint-darken.
            if (expressive && !_isSleeping && mood == SpiritMoodBand.Low)
            {
                float grey = baseColor.r * 0.3f + baseColor.g * 0.59f + baseColor.b * 0.11f;
                baseColor = Color.Lerp(baseColor, new Color(grey, grey, grey, baseColor.a), 0.4f);
            }
            if (_quirk == SpiritQuirks.Kind.Nap)
            {
                baseColor.r *= 0.72f; baseColor.g *= 0.72f; baseColor.b *= 0.72f;
            }

            // Radiant: a fulfilled spirit pulses gently toward warm gold.
            if (IsFulfilled)
            {
                float pulse = (Mathf.Sin(Time.time * 2f * Mathf.PI / RadiantPulseSeconds) + 1f) * 0.5f;
                baseColor = Color.Lerp(baseColor, RadiantGold, pulse);
            }

            float shimmer = Mathf.Sin(Time.time * ShimmerFrequency * 2f * Mathf.PI) * ShimmerAmplitude;
            baseColor.a = Mathf.Clamp01(baseAlpha + shimmer);
            Renderer.color = baseColor;
        }

        // ---- personality (muscle 03) ---------------------------------------------

        /// <summary>Gate for quirks / drift / habitat habits / pair play.</summary>
        private bool CanActPersonality =>
            (_state == SpiritState.Visitor || _state == SpiritState.Resident)
            && !_returningToBorder && !_isSleeping && !_ceremony && !_despawning;

        /// <summary>Shepherd-presence reactions: daily greeting + sprint startle.</summary>
        private void TickPresence()
        {
            if (_state != SpiritState.Visitor && _state != SpiritState.Resident) return;
            if (_isSleeping || _ceremony || _despawning) return;

            float dist = DistanceToPlayer();
            if (float.IsPositiveInfinity(dist)) return;

            // First meet of the day (residents): small hop + greeting chirp.
            // In-memory per-day flag - resets naturally on reload, which is fine.
            var clock = GameClock.Instance;
            if (clock != null && _state == SpiritState.Resident
                && clock.Day != _lastGreetedDay && dist <= GreetDistance)
            {
                _lastGreetedDay = clock.Day;
                if (_hopTimer < 0f) _hopTimer = 0f;
                SpiritVoice.Play(_species, VoiceIntent.Greet);
            }

            // Sprint startle: a flinch and an eep, and NOTHING else - mood is
            // deliberately untouched so sprinting is never discouraged.
            if (Time.time >= _startleReadyAt && dist <= StartleDistance)
            {
                if (_shepherdCtrl == null && _playerTransform != null)
                    _shepherdCtrl = _playerTransform.GetComponent<AnimalFarm.Player.ShepherdController>();
                if (_shepherdCtrl != null
                    && _shepherdCtrl.Velocity.magnitude >= StartleSprintSpeed)
                {
                    _flinchTimer = 0f;
                    _startleReadyAt = Time.time + StartleCooldown;
                    SpiritVoice.Play(_species, VoiceIntent.Startle, 0.8f);
                }
            }
        }

        /// <summary>
        /// Event-driven want bubble: fires ~3s on want onset and again when
        /// the shepherd walks by (cooldown keeps it from spamming).
        /// </summary>
        private void TickWantBubble()
        {
            if (_bubble == null) return;

            bool eligible = (_state == SpiritState.Visitor || _state == SpiritState.Resident)
                && !_isSleeping && !_ceremony && !_despawning;
            if (!eligible)
            {
                // Clear the flags so a still-true want re-fires its onset
                // bubble when the spirit becomes expressive again.
                _playerInBubbleRange = false;
                for (int i = 0; i < _wantActive.Length; i++) _wantActive[i] = false;
                _bubble.HideNow();
                return;
            }

            _wantCheckTimer -= Time.deltaTime;
            if (_wantCheckTimer <= 0f)
            {
                _wantCheckTimer = WantCheckInterval;

                bool food = _state == SpiritState.Visitor
                    || (_state == SpiritState.Resident && _hunger01 >= HungryPromptThreshold);
                bool home = _state == SpiritState.Resident && !HasHome;
                bool water = _state == SpiritState.Resident && HasHome && !_taskDone
                    && _species.taskKind == FinalTaskKind.WaterNearHome;
                bool lonely = _state == SpiritState.Resident && _spirit < MoodLowThreshold;

                // Reverse priority order: when several wants onset together,
                // the LAST Show() wins, so food (highest) goes last.
                UpdateWant(WantLonely, lonely);
                UpdateWant(WantWater, water);
                UpdateWant(WantHome, home);
                UpdateWant(WantFood, food);
            }

            // Walk-by re-show: ENTERING ~2 units shows the top want again.
            float dist = DistanceToPlayer();
            bool near = dist <= WantProximityRadius;
            if (near && !_playerInBubbleRange && Time.unscaledTime >= _wantProximityReadyAt)
            {
                int top = TopActiveWant();
                if (top >= 0)
                {
                    _bubble.Show((AnimalFarm.UI.WantKind)top, WantBubbleSeconds);
                    _wantProximityReadyAt = Time.unscaledTime + WantProximityCooldown;
                }
            }
            _playerInBubbleRange = near;
        }

        /// <summary>Tracks one want flag; a false-to-true flip shows the onset bubble.</summary>
        private void UpdateWant(int index, bool active)
        {
            if (active && !_wantActive[index])
                _bubble.Show((AnimalFarm.UI.WantKind)index, WantBubbleSeconds);
            _wantActive[index] = active;
        }

        /// <summary>Highest-priority active want, or -1. Food > home > water > lonely.</summary>
        private int TopActiveWant()
        {
            if (_wantActive[WantFood]) return WantFood;
            if (_wantActive[WantHome]) return WantHome;
            if (_wantActive[WantWater]) return WantWater;
            if (_wantActive[WantLonely]) return WantLonely;
            return -1;
        }

        /// <summary>Runs the active quirk / pair move; true while it owns movement.</summary>
        private bool TickQuirk()
        {
            if (_quirk == SpiritQuirks.Kind.None) return false;

            // Hard interrupts: sleep onset handles itself; runaway/despawn cancel.
            if (_isSleeping || _despawning || _state == SpiritState.Runaway)
            {
                EndQuirk();
                return false;
            }

            _quirkTimer += Time.deltaTime;
            float dt = Time.deltaTime;

            switch (_quirk)
            {
                case SpiritQuirks.Kind.LeafChase:
                {
                    // Short erratic dashes after an invisible leaf.
                    float speed = _species.wanderSpeed * 2.6f;
                    transform.position = Vector3.MoveTowards(transform.position, _quirkPoint, speed * dt);
                    if ((transform.position - _quirkPoint).sqrMagnitude <= ArriveDistance * ArriveDistance)
                    {
                        _quirkDashesLeft--;
                        if (_quirkDashesLeft <= 0) { EndQuirk(); return true; }
                        _quirkPoint = LeafPoint();
                    }
                    break;
                }
                case SpiritQuirks.Kind.CirclePlay:
                {
                    _quirkAngle += _quirkSpin * dt;
                    Vector3 p = _quirkCenter + new Vector3(
                        Mathf.Cos(_quirkAngle), Mathf.Sin(_quirkAngle), 0f) * _quirkRadius;
                    transform.position = Vector3.MoveTowards(
                        transform.position, p, _species.wanderSpeed * 2.2f * dt);
                    break;
                }
                case SpiritQuirks.Kind.Squabble:
                {
                    float speed = _species.wanderSpeed * 2.8f;
                    transform.position = Vector3.MoveTowards(transform.position, _quirkPoint, speed * dt);
                    if ((transform.position - _quirkPoint).sqrMagnitude <= ArriveDistance * ArriveDistance)
                    {
                        EndQuirk();
                        return true;
                    }
                    break;
                }
                // Nap / Stretch: stationary; TickVisuals draws them.
            }

            if (_quirkTimer >= _quirkDuration) EndQuirk();
            return true;
        }

        private void EndQuirk()
        {
            _quirk = SpiritQuirks.Kind.None;
            _hasTarget = false;
            _idleTimer = Random.Range(0.8f, 2f);

            // A retreating visitor must not lose its way home mid-quirk.
            if (_returningToBorder)
            {
                _wanderTarget = NearestBorderPoint(transform.position);
                _hasTarget = true;
                _idleTimer = 0f;
            }
        }

        /// <summary>Rolls for an idle micro-moment; true when one started
        /// (or a hop consumed the idle slot).</summary>
        private bool TryStartQuirk()
        {
            float chance = QuirkChance;
            if (_species.activity == ActivityWindow.Night
                && GameClock.Instance != null && GameClock.Instance.IsNight)
                chance = 0.5f; // night shift: livelier after dark
            if (Random.value > chance) return false;

            switch (SpiritQuirks.Pick(_species))
            {
                case SpiritQuirks.Kind.Hop:
                    if (_hopTimer < 0f) _hopTimer = 0f;
                    _idleTimer = Random.Range(1f, 2.5f);
                    return true;
                case SpiritQuirks.Kind.Nap:
                    _quirk = SpiritQuirks.Kind.Nap;
                    _quirkTimer = 0f;
                    _quirkDuration = Random.Range(3.5f, 6f);
                    SpiritVoice.Play(_species, VoiceIntent.Sleep, 0.45f);
                    return true;
                case SpiritQuirks.Kind.Stretch:
                    _quirk = SpiritQuirks.Kind.Stretch;
                    _quirkTimer = 0f;
                    _quirkDuration = 0.8f;
                    return true;
                case SpiritQuirks.Kind.LeafChase:
                    _quirk = SpiritQuirks.Kind.LeafChase;
                    _quirkTimer = 0f;
                    _quirkDuration = 4f; // safety cap; usually ends on the last dash
                    _quirkDashesLeft = 3;
                    _quirkPoint = LeafPoint();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A nearby dash point for the invisible leaf.</summary>
        private Vector3 LeafPoint()
        {
            Vector3 p = transform.position + (Vector3)(Random.insideUnitCircle * 1.6f);
            p.z = 0f;
            return p;
        }

        /// <summary>
        /// Mood-based drift: happy spirits sidle toward the shepherd and
        /// linger; low-mood spirits edge away. Mood as body language.
        /// </summary>
        private bool TryMoodDriftTarget(out Vector3 target)
        {
            target = default;
            float dist = DistanceToPlayer();
            if (float.IsPositiveInfinity(dist) || dist > DriftNoticeDistance) return false;

            var mood = MoodBand;
            if (mood == SpiritMoodBand.Happy && dist > 1.4f && Random.value < 0.35f)
            {
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(1.0f, 1.6f);
                target = _playerTransform.position + (Vector3)off;
                target.z = 0f;
                _lingerOnArrive = true; // tag along, then hang around a moment
                return true;
            }
            if (mood == SpiritMoodBand.Low && dist < 3.5f && Random.value < 0.5f)
            {
                Vector3 away = transform.position - _playerTransform.position;
                away.z = 0f;
                if (away.sqrMagnitude < 0.0001f)
                    away = (Vector3)Random.insideUnitCircle.normalized;
                target = transform.position + away.normalized * Random.Range(2f, 3f)
                    + (Vector3)(Random.insideUnitCircle * 0.5f);
                target.z = 0f;
                return true;
            }
            return false;
        }

        /// <summary>Habitat habit: periodically drift near the species' anchor.</summary>
        private bool TryHabitatTarget(out Vector3 target)
        {
            target = default;
            if (_species.habitatPreference == HabitatPreference.None) return false;
            if (Time.time < _nextHabitatAt) return false;

            if (SpiritQuirks.TryFindHabitatPoint(_species.habitatPreference, transform.position, out target))
            {
                _nextHabitatAt = Time.time + Random.Range(25f, 50f);
                _lingerOnArrive = true; // hang around the hangout
                return true;
            }
            _nextHabitatAt = Time.time + 15f; // no matching anchor yet; retry later
            return false;
        }

        /// <summary>Pair play (SpiritSocialManager): circle a shared midpoint,
        /// happy chirp, and a tiny Spirit bump.</summary>
        public void BeginPairPlay(Vector3 center, bool clockwise)
        {
            if (!IsSocialIdle) return;
            _hasTarget = false;
            _quirk = SpiritQuirks.Kind.CirclePlay;
            _quirkTimer = 0f;
            _quirkDuration = 2.6f;
            _quirkCenter = new Vector3(center.x, center.y, 0f);
            Vector3 from = transform.position - _quirkCenter;
            from.z = 0f;
            _quirkRadius = Mathf.Clamp(from.magnitude, 0.45f, 1.2f);
            _quirkAngle = Mathf.Atan2(from.y, from.x);
            _quirkSpin = (clockwise ? -1f : 1f) * 2.6f;
            _spirit = Mathf.Clamp(_spirit + 2f, 0f, 100f); // tiny social glow
            SpiritVoice.Play(_species, VoiceIntent.Happy, 0.8f);
        }

        /// <summary>Pair squabble: quick back-off + grumpy blip. Purely visual
        /// flavor - deliberately NO mood loss.</summary>
        public void BeginPairSquabble(Vector3 otherPos)
        {
            if (!IsSocialIdle) return;
            _hasTarget = false;
            _quirk = SpiritQuirks.Kind.Squabble;
            _quirkTimer = 0f;
            _quirkDuration = 1.2f;
            Vector3 away = transform.position - otherPos;
            away.z = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = (Vector3)Random.insideUnitCircle.normalized;
            _quirkPoint = transform.position + away.normalized * 1.2f;
            SpiritVoice.Play(_species, VoiceIntent.Grumpy, 0.8f);
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.8f, "hmph!", HerdGreyBlue);
        }

        // ---- state transitions -------------------------------------------------

        private void BecomeVisitor()
        {
            _state = SpiritState.Visitor;
            _returningToBorder = false;
            _wanderTarget = PickInFieldPoint(); // drift into the field
            _hasTarget = true;
            Debug.Log($"[Spirits] {DisplayName} drifts into the garden...");
            if (SpiritManager.Instance != null) SpiritManager.Instance.NotifyBecameVisitor(this);
        }

        private void BecomeSilhouette()
        {
            _state = SpiritState.Silhouette; // FedCount preserved
            _hasTarget = false;
            _idleTimer = Random.Range(1f, 3f);
        }

        private void BecomeResident()
        {
            _state = SpiritState.Resident;
            _spirit = 60f;
            _hunger01 = 0f;
            _lowSpiritSeconds = 0f;
            if (GameClock.Instance != null)
            {
                _lastFedTotalHours = GameClock.Instance.TotalHours;
                _residentSinceTotalHours = GameClock.Instance.TotalHours;
            }
            if (SpiritManager.Instance != null) SpiritManager.Instance.NotifyBecameResident(this);
        }

        private void BecomeRunaway()
        {
            _quirk = SpiritQuirks.Kind.None; // drop any quirk WITHOUT EndQuirk: the flee target below must survive
            _state = SpiritState.Runaway;
            _following = false; // running away cancels following
            _lowSpiritSeconds = 0f;
            _herdingTagsLeft = 0; // fresh runaway: chase hasn't started yet
            _herdDashing = false;
            _wanderTarget = NearestBorderPoint(transform.position);
            _hasTarget = true;
            Debug.Log($"[Spirits] {DisplayName} ran away!");
        }

        // ---- ISelectable -------------------------------------------------------

        public string SelectableTitle => _state == SpiritState.Silhouette ? "???" : DisplayName;

        /// <summary>
        /// Interact menu on click. Tending actions (feed/soothe) are
        /// proximity-gated: the shepherd must be within TendDistance.
        /// </summary>
        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;

            // A silhouette is an unmet mystery — no inspecting, no renaming.
            if (_state == SpiritState.Silhouette)
            {
                into.Add(new SelectAction("(it keeps its distance)", () => { }, closeOnRun: true));
                return;
            }

            if (DistanceToPlayer() <= TendDistance)
            {
                // Feed — only when the walk-up feed rules would apply: a
                // visitor working toward residency, or a hungry resident, and
                // the favored food is in the satchel.
                bool hasFood = _species != null && Inventory.Instance != null
                    && Inventory.Instance.Count(_species.favoredFoodId) > 0;
                bool feedApplies = hasFood
                    && (_state == SpiritState.Visitor
                        || (_state == SpiritState.Resident && _hunger01 >= HungryPromptThreshold));
                if (feedApplies)
                    into.Add(new SelectAction($"Feed ({_species.favoredFoodId})", () => TryFeedAction()));

                into.Add(new SelectAction("Soothe", () => TrySootheAction()));

                if (_state == SpiritState.Resident && _spirit >= FollowSpiritThreshold)
                {
                    string label = _following ? "Stay here" : "Come along";
                    into.Add(new SelectAction(label, () => SetFollowing(!IsFollowing)));
                }
                else if (_state == SpiritState.Resident && !_following
                    && Inventory.Instance != null && Inventory.Instance.Count("treat") > 0)
                {
                    // Low-trust resident + a Follow Treat: bribery works on the dead.
                    into.Add(new SelectAction("Give treat (follow)", () => GiveTreat()));
                }

                into.Add(new SelectAction("Inspect", () =>
                {
                    var info = AnimalFarm.UI.SpiritInfoUI.Instance;
                    if (info != null) info.OpenFor(this);
                }));
            }
            else
            {
                into.Add(new SelectAction("Inspect", () =>
                {
                    var info = AnimalFarm.UI.SpiritInfoUI.Instance;
                    if (info != null) info.OpenFor(this);
                }));
                into.Add(new SelectAction("(too far to tend)", () => { }, closeOnRun: false));
            }
        }

        /// <summary>Planar distance to the shepherd (tag "Player"); +inf if absent.</summary>
        private float DistanceToPlayer()
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player == null) return float.PositiveInfinity;
                _playerTransform = player.transform;
            }

            Vector3 to = _playerTransform.position - transform.position;
            to.z = 0f;
            return to.magnitude;
        }

        // ---- IInteractable -----------------------------------------------------

        private string DisplayName =>
            !string.IsNullOrEmpty(_givenName) ? _givenName
            : _species != null && !string.IsNullOrEmpty(_species.displayName) ? _species.displayName
            : "Spirit";

        public string PromptText
        {
            get
            {
                if (_species == null || _state == SpiritState.Silhouette) return "";

                string food = _species.favoredFoodId;
                bool hasFood = Inventory.Instance != null && Inventory.Instance.Count(food) > 0;

                switch (_state)
                {
                    case SpiritState.Visitor:
                        return hasFood
                            ? $"Feed {food} ({_fedCount}/{_species.residencyFoodCount})"
                            : $"Needs {food} ({_fedCount}/{_species.residencyFoodCount})";
                    case SpiritState.Resident:
                        // Mirrors the Interact ladder: offer > feed > follow > soothe.
                        if (CanOfferTaskItem)
                            return $"Offer {_species.taskItemId} ({_taskProgress}/{_species.taskItemCount})";
                        if (_hunger01 >= HungryPromptThreshold && hasFood)
                            return $"Feed {DisplayName}";
                        if (_spirit >= FollowSpiritThreshold)
                            return _following ? $"Stay here, {DisplayName}" : $"Come along, {DisplayName}";
                        return $"Soothe {DisplayName}";
                    case SpiritState.Runaway:
                        return _herdingTagsLeft > 0
                            ? $"Herd {DisplayName} ({_herdingTagsLeft} left)"
                            : $"Soothe {DisplayName}";
                    default:
                        return "";
                }
            }
        }

        /// <summary>GiveItem wish pending and the offering is in the satchel?</summary>
        private bool CanOfferTaskItem =>
            !_taskDone && _species.taskKind == FinalTaskKind.GiveItem
            && Inventory.Instance != null && Inventory.Instance.Count(_species.taskItemId) > 0;

        public bool CanInteract(GameObject actor) =>
            _species != null && !_despawning && !_ceremony && _state != SpiritState.Silhouette;

        public void Interact(GameObject actor)
        {
            if (!CanInteract(actor)) return;

            bool hasFood = Inventory.Instance != null && Inventory.Instance.Count(_species.favoredFoodId) > 0;

            switch (_state)
            {
                case SpiritState.Visitor:
                    if (hasFood) TryFeed();
                    break;
                case SpiritState.Resident:
                    // Priority ladder: offer the wish item > feed > follow toggle > soothe.
                    if (CanOfferTaskItem) TryOfferTaskItem();
                    else if (_hunger01 >= HungryPromptThreshold && hasFood) TryFeed();
                    else if (_spirit >= FollowSpiritThreshold) SetFollowing(!_following);
                    else TrySoothe();
                    break;
                case SpiritState.Runaway:
                    TrySoothe();
                    break;
            }
        }

        private void TryOfferTaskItem()
        {
            if (Inventory.Instance == null || !Inventory.Instance.Consume(_species.taskItemId, 1))
                return;

            _taskProgress++;
            _timesFed++;
            _hopTimer = 0f;

            if (_taskProgress >= _species.taskItemCount)
                CompleteTask();
            else
                AnimalFarm.UI.FloatingText.Show(
                    transform.position + Vector3.up * 0.8f,
                    $"{_taskProgress}/{_species.taskItemCount}", TaskGold);
        }

        /// <summary>
        /// Public wrapper for menus: feeds with exactly the walk-up rules.
        /// False when not applicable (silhouette/ceremony/despawning) or when
        /// the favored food is not in the satchel.
        /// </summary>
        public bool TryFeedAction()
        {
            if (!CanInteract(gameObject)) return false;
            return TryFeed();
        }

        /// <summary>Public wrapper for menus: soothes with exactly the walk-up
        /// rules (cooldown included; on a Runaway this routes through the same
        /// herding-tag logic as walk-up Interact). False when refused.</summary>
        public bool TrySootheAction()
        {
            if (!CanInteract(gameObject)) return false;
            return TrySoothe();
        }

        private bool TryFeed()
        {
            if (Inventory.Instance == null || !Inventory.Instance.Consume(_species.favoredFoodId, 1))
                return false;

            _spirit = Mathf.Clamp(_spirit + _species.feedSpiritBoost, 0f, 100f);
            _hopTimer = 0f;
            _timesFed++;

            if (_state == SpiritState.Visitor)
            {
                _fedCount++;
                AnimalFarm.UI.FloatingText.Show(
                    transform.position + Vector3.up * 0.8f,
                    $"{_fedCount}/{_species.residencyFoodCount}", new Color(1f, 0.9f, 0.4f));

                if (_fedCount >= _species.residencyFoodCount)
                    BecomeResident();
            }
            else // Resident
            {
                _hunger01 = 0f;
                if (GameClock.Instance != null) _lastFedTotalHours = GameClock.Instance.TotalHours;
                AnimalFarm.UI.FloatingText.Show(
                    transform.position + Vector3.up * 0.8f, "Yum!", new Color(1f, 0.9f, 0.4f));
            }
            ShepherdProgress.Grant("feed");
            return true;
        }

        private bool TrySoothe()
        {
            // Herding recovery (slice 05 leftover): soothing a Runaway is a
            // chase, not an instant fix. Tags are instant on purpose - the
            // 20s soothe cooldown applies to calm soothes only. TrySoothe is
            // the ONLY runaway-recovery path (debug 'blessing' etc. use
            // Debug_SetSpirit, which never touches state), so routing the
            // Runaway branch here covers Interact AND TrySootheAction.
            if (_state == SpiritState.Runaway) return HerdTag();

            if (Time.unscaledTime - _lastSootheRealTime < SootheCooldownRealSeconds) return false;
            _lastSootheRealTime = Time.unscaledTime;

            _spirit = Mathf.Clamp(_spirit + _species.sootheSpiritBoost, 0f, 100f);

            _hopTimer = 0f;
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.8f, "<3", new Color(1f, 0.5f, 0.7f));
            ShepherdProgress.Grant("soothe");

            // SootheInWindow wish: a soothe landing inside the hour window grants it.
            if (_state == SpiritState.Resident && !_taskDone
                && _species.taskKind == FinalTaskKind.SootheInWindow && IsInTaskWindow())
                CompleteTask();

            return true;
        }

        // ---- herding recovery (runaway chase minigame) -------------------------

        /// <summary>
        /// One herding tag on a Runaway. The first tag starts the chase (it
        /// bolts), later tags count it down, and the last one recovers the
        /// spirit. State stays Runaway until that last tag - deliberate:
        /// RepoManManager watches State == Runaway, so the repo clock keeps
        /// ticking through the chase.
        /// </summary>
        private bool HerdTag()
        {
            _lastHerdTagRealTime = Time.unscaledTime;

            if (_herdingTagsLeft <= 0)
            {
                // First soothe-interact: it bolts. Clumsy spirits (low Grace)
                // are easier to run down.
                _herdingTagsLeft = _grace <= HerdClumsyGraceMax ? 2 : 3;
                AnimalFarm.UI.FloatingText.Show(
                    transform.position + Vector3.up * 0.8f,
                    $"(it bolts! herd it down - {_herdingTagsLeft} more)", HerdGreyBlue);
                StartHerdDash();
                return true;
            }

            _herdingTagsLeft--;
            if (_herdingTagsLeft > 0)
            {
                AnimalFarm.UI.FloatingText.Show(
                    transform.position + Vector3.up * 0.8f,
                    $"({_herdingTagsLeft} more!)", HerdGreyBlue);
                StartHerdDash();
                return true;
            }

            // Tagged down: exactly the old runaway-soothe recovery (Resident,
            // Spirit 40, timers reset) plus a +3 Spirit bonus for the effort.
            _herdDashing = false;
            _state = SpiritState.Resident;
            _spirit = 43f; // 40 (old recovery) + 3 (shepherding bonus)
            _lowSpiritSeconds = 0f;
            _hasTarget = false;
            _idleTimer = Random.Range(1f, 3f);
            _hopTimer = 0f;
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.8f,
                "shepherded home!", new Color(1f, 0.9f, 0.4f));
            ShepherdProgress.Grant("herd");

            // Parity with the old soothe path: a recovery that lands inside
            // the SootheInWindow hour window still grants the wish.
            if (!_taskDone && _species.taskKind == FinalTaskKind.SootheInWindow && IsInTaskWindow())
                CompleteTask();

            return true;
        }

        /// <summary>
        /// Dash to a random usable interior point, biased away from the
        /// player: resample up to HerdDashResamples times until the target
        /// direction points away from the shepherd, else keep the last pick.
        /// </summary>
        private void StartHerdDash()
        {
            Vector3 target = PickInFieldPoint();

            if (_playerTransform == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) _playerTransform = player.transform;
            }
            if (_playerTransform != null)
            {
                Vector3 away = transform.position - _playerTransform.position;
                away.z = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    away.Normalize();
                    for (int i = 0; i < HerdDashResamples; i++)
                    {
                        Vector3 dir = target - transform.position;
                        dir.z = 0f;
                        if (Vector3.Dot(dir.normalized, away) > 0f) break;
                        target = PickInFieldPoint();
                    }
                }
            }

            _wanderTarget = target;
            _hasTarget = true;
            _herdDashing = true;
        }

        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ---- geometry ----------------------------------------------------------

        // Slice 08b: all field geometry now comes from TerrainGrid's usable
        // region (grows with parcel purchases; handles L-shapes exactly).

        private Vector3 PickWanderTarget() =>
            _state == SpiritState.Silhouette ? PickBorderDriftPoint() : PickInFieldPoint();

        /// <summary>Random point anywhere in the unlocked field.</summary>
        private Vector3 PickInFieldPoint()
        {
            var grid = TerrainGrid.Instance;
            if (grid != null) return grid.RandomUsableInteriorPointNear(transform.position, 12f);
            return transform.position + (Vector3)(Random.insideUnitCircle * 3f);
        }

        /// <summary>Silhouettes pace along the usable region's edge ring.</summary>
        private Vector3 PickBorderDriftPoint()
        {
            var grid = TerrainGrid.Instance;
            if (grid != null) return grid.DriftAlongBorder(transform.position, 2f, 5f);
            return transform.position + (Vector3)(Random.insideUnitCircle * 2f);
        }

        /// <summary>Closest edge-ring point to a world position.</summary>
        private static Vector3 NearestBorderPoint(Vector3 from)
        {
            var grid = TerrainGrid.Instance;
            return grid != null ? grid.NearestUsableBorderPoint(from) : from;
        }
    }
}

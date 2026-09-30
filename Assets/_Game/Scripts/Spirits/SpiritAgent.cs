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

        private static readonly Color SilhouetteColor = new Color(0.18f, 0.18f, 0.28f, 1f); // lightened: readable at night
        private static readonly Color RadiantGold = new Color(1f, 0.95f, 0.65f);
        private static readonly Color TaskGold = new Color(1f, 0.9f, 0.4f);
        private static readonly Color HomePaleBlue = new Color(0.7f, 0.85f, 1f);

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

        /// <summary>Body renderer; the manager assigns the shared sprite material after Init.</summary>
        public SpriteRenderer Renderer { get; private set; }

        /// <summary>Toggles follow mode. Refused unless a resident with Spirit >= 60.</summary>
        public void SetFollowing(bool on)
        {
            if (on && (_state != SpiritState.Resident || _spirit < FollowSpiritThreshold)) return;
            _following = on;
            if (on) _hasTarget = false; // drop the wander target; TickFollow takes over
        }

        /// <summary>Called by Home when it despawns under us.</summary>
        public void NotifyHomeLost(Home h)
        {
            if (_home == h) _home = null; // homeless again; no mood hit this slice
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

        // timers / effects
        private float _stateCheckTimer;
        private float _lowSpiritSeconds;
        private float _lastSootheRealTime = -999f;
        private float _hopTimer = -1f;
        private bool _despawning;
        private float _despawnTimer;
        private Vector3 _baseScale = Vector3.one;
        private bool _focused;

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
            TickHomeAndTask();
            if (_following && _state == SpiritState.Resident) TickFollow();
            else TickWander();
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

            // Runaways flee to the border, then just wait there.
            if (_state == SpiritState.Runaway && !_hasTarget) return;

            if (!_hasTarget)
            {
                _idleTimer -= Time.deltaTime;
                if (_idleTimer > 0f) return;
                _wanderTarget = PickWanderTarget();
                _hasTarget = true;
            }

            transform.position = Vector3.MoveTowards(
                transform.position, _wanderTarget, _species.wanderSpeed * Time.deltaTime);

            if ((transform.position - _wanderTarget).sqrMagnitude <= ArriveDistance * ArriveDistance)
            {
                _hasTarget = false;
                _idleTimer = Random.Range(1f, 3f);

                if (_state == SpiritState.Visitor && _returningToBorder)
                {
                    _returningToBorder = false;
                    BecomeSilhouette();
                }
            }
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

            // Procedural ghost float: sin y-bob + a feed hop on the body only.
            float freq = _state == SpiritState.Runaway ? RunawayBobFrequency : BobFrequency;
            float y = Mathf.Sin(Time.time * freq * 2f * Mathf.PI) * BobAmplitude;

            if (_hopTimer >= 0f)
            {
                _hopTimer += Time.deltaTime;
                float t = _hopTimer / HopDuration;
                if (t >= 1f) _hopTimer = -1f;
                else y += Mathf.Sin(t * Mathf.PI) * HopHeight;
            }
            Renderer.transform.localPosition = new Vector3(0f, y, 0f);

            // Colour + alpha shimmer.
            Color baseColor = _state == SpiritState.Silhouette ? SilhouetteColor : _species.tint;
            float baseAlpha = _state == SpiritState.Silhouette ? SilhouetteAlpha
                : _isSleeping ? SleepingAlpha
                : baseColor.a;

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
            _state = SpiritState.Runaway;
            _following = false; // running away cancels following
            _lowSpiritSeconds = 0f;
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
                        return $"Soothe {DisplayName}";
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
        /// rules (cooldown included). False when refused.</summary>
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
            return true;
        }

        private bool TrySoothe()
        {
            if (Time.unscaledTime - _lastSootheRealTime < SootheCooldownRealSeconds) return false;
            _lastSootheRealTime = Time.unscaledTime;

            if (_state == SpiritState.Runaway)
            {
                _state = SpiritState.Resident;
                _spirit = 40f;
                _lowSpiritSeconds = 0f;
                _hasTarget = false;
                _idleTimer = Random.Range(1f, 3f);
            }
            else
            {
                _spirit = Mathf.Clamp(_spirit + _species.sootheSpiritBoost, 0f, 100f);
            }

            _hopTimer = 0f;
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.8f, "<3", new Color(1f, 0.5f, 0.7f));

            // SootheInWindow wish: a soothe landing inside the hour window grants it.
            if (_state == SpiritState.Resident && !_taskDone
                && _species.taskKind == FinalTaskKind.SootheInWindow && IsInTaskWindow())
                CompleteTask();

            return true;
        }

        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ---- geometry ----------------------------------------------------------

        private static void FieldHalfExtents(out float halfW, out float halfH)
        {
            var grid = TerrainGrid.Instance;
            halfW = (grid != null ? grid.Width : 80) * 0.5f;
            halfH = (grid != null ? grid.Height : 50) * 0.5f;
        }

        private Vector3 PickWanderTarget() =>
            _state == SpiritState.Silhouette ? PickBorderDriftPoint() : PickInFieldPoint();

        /// <summary>Random point anywhere in the field, kept off the walls.</summary>
        private Vector3 PickInFieldPoint()
        {
            FieldHalfExtents(out float hw, out float hh);
            return new Vector3(
                Random.Range(-hw + BorderInset, hw - BorderInset),
                Random.Range(-hh + BorderInset, hh - BorderInset), 0f);
        }

        /// <summary>Silhouettes drift sideways along the border ring, 1.5 inside the walls.</summary>
        private Vector3 PickBorderDriftPoint()
        {
            FieldHalfExtents(out float hw, out float hh);
            float ix = hw - BorderInset, iy = hh - BorderInset;

            Vector3 p = transform.position;
            float step = Random.Range(2f, 5f) * (Random.value < 0.5f ? -1f : 1f);

            // Which wall are we hugging? Drift along its tangent, clamped to the ring.
            float dxWall = ix - Mathf.Abs(p.x);
            float dyWall = iy - Mathf.Abs(p.y);
            if (dxWall < dyWall)
            {
                float x = Mathf.Sign(p.x == 0f ? 1f : p.x) * ix;
                return new Vector3(x, Mathf.Clamp(p.y + step, -iy, iy), 0f);
            }
            float ySide = Mathf.Sign(p.y == 0f ? 1f : p.y) * iy;
            return new Vector3(Mathf.Clamp(p.x + step, -ix, ix), ySide, 0f);
        }

        /// <summary>Closest point on the inset border ring to a world position.</summary>
        private static Vector3 NearestBorderPoint(Vector3 from)
        {
            FieldHalfExtents(out float hw, out float hh);
            float ix = hw - BorderInset, iy = hh - BorderInset;

            float x = Mathf.Clamp(from.x, -ix, ix);
            float y = Mathf.Clamp(from.y, -iy, iy);

            // Snap the nearer coordinate to its wall.
            float toX = ix - Mathf.Abs(x);
            float toY = iy - Mathf.Abs(y);
            if (toX < toY) x = Mathf.Sign(x == 0f ? 1f : x) * ix;
            else y = Mathf.Sign(y == 0f ? 1f : y) * iy;

            return new Vector3(x, y, 0f);
        }
    }
}

using System;
using System.Collections.Generic;
using AnimalFarm.Competitions;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Interaction;
using AnimalFarm.Onboarding;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace AnimalFarm.Player
{
    /// <summary>
    /// THE POUTY MOUNT (muscle 08 verdict 1): once the road rights are owned and the
    /// road has been crossed a few times, a horse-like spirit with a sulky, characterful personality turns up, joins
    /// you for good and becomes a rideable mount. It NEVER wants to ascend: it
    /// is deliberately NOT a SpiritAgent and is never registered with
    /// SpiritManager, so it can't appear in ascension / Styx-crossing /
    /// weave / repo / competition lists -- there is no flag to forget.
    ///
    /// JOIN (owner verdict 2026-10-01): "once you purchase the rights to a second
    /// area, where you're having to cross the roads to and fro, after some time it
    /// shows up to join you." Trigger = road rights owned (ParcelManager.RoadRightsOwned)
    /// AND 6 road crossings (RoadTravel.Crossings; ASSUMPTION: ~3 round trips) AND
    /// at least 1 game-day since the rights were first seen owned (ASSUMPTION),
    /// then it appears on the road on the next crossing. A short scene -- it trots
    /// toward you, stops a pace away, sighs, and decides to allow this. A mount
    /// that already joined (old saves) stays joined.
    ///
    /// COMPANION: trails the shepherd like an escort (a little behind). Ride
    /// toggle (the "Ride" input action, default V / gamepad left trigger,
    /// rebindable): mount within 4 units; riding gives a speed boost x1.5 (x1.75
    /// while perked; half of it stacks on sprint), blocks tools, the build menu
    /// and Interact (pressing Interact dismounts), and excludes sitting to rest
    /// (ShepherdRest) both ways.
    ///
    /// PERSONALITY (contentment 0..100, start 65):
    ///   IGNORED  - after 4 game-hours with no ride/pet/feed it drains 2/hour; under
    ///              30 it POUTS: sulks 5.5 units back, hangs its head, tuts
    ///              "hmph", and REFUSES a ride until tended (ASSUMPTION).
    ///   FED      - berries are its favourite (+40, perked 3 game-hours); any other
    ///              crop is +15 and perked 1 hour. PERKED: ears up, hops, rides faster.
    ///   PET      - +25, 20 s cooldown (the cheap way out of a pout).
    ///
    /// Self-spawning singleton (AfterSceneLoad; no scene setup). SAVE
    /// ("poutymount"): joined, contentment, perk hours left, last-tended stamp,
    /// position. Riding is never saved (you load dismounted).
    /// </summary>
    public class PoutyMount : MonoBehaviour, ISaveable, IInteractable, ISelectable
    {
        public static PoutyMount Instance { get; private set; }

        /// <summary>True while the shepherd is on the mount.</summary>
        public static bool IsRiding { get; private set; }

        /// <summary>Time.frameCount of the last Interact-press dismount (other Interact handlers ignore that press).</summary>
        public static int DismountFrame = -1;

        // ---- tuning (ASSUMPTIONS where the doc is silent) ----------------------------
        private const int JoinCrossings = 6;             // ~3 round trips on the road (owner verdict 2026-10-01)
        private const float JoinWaitHours = 24f;         // at least 1 game-day after buying road rights
        private const float MountReach = 4f;
        private const float FollowDist = 2.0f;
        private const float PoutFollowDist = 5.5f;
        private const float FollowSpeed = 5.0f;
        private const float PoutSpeed = 3.0f;
        private const float JoinSpeed = 6.5f;
        private const float TeleportDist = 22f;
        private const float StartContentment = 65f;
        private const float PoutThreshold = 30f;
        private const float IgnoreGraceHours = 4f;
        private const float IgnoreDecayPerHour = 2f;
        private const float PetGain = 25f;
        private const float PetCooldown = 20f;
        private const float RideSpeed = 1.5f;
        private const float PerkRideSpeed = 1.75f;
        private const float RideLift = 0.42f;        // shepherd sits this far above the ground
        private const string FavoriteFood = "berry"; // berry-bush crop
        private static readonly string[] FeedIds = { "berry", "wheat", "bloom", "reed", "glowcap" };

        private static readonly Color PoutTint = new Color(0.62f, 0.66f, 0.80f);
        private static readonly Color HeartPink = new Color(1f, 0.62f, 0.75f);

        private static readonly string[] PoutLines =
        {
            "hmph.", "(it turns its back on you)", "(pointedly not looking)", "*dramatic sigh*"
        };

        private enum JoinPhase { None, Arrive, Sigh }

        // ---- state ---------------------------------------------------------------------
        private bool _joined;
        private float _contentment = StartContentment;
        private float _perkUntilHours = -1f;
        private float _lastTendedHours;
        private float _prevHours;
        private float _petReadyAt;
        private float _nextPoutLineAt;
        private float _nextJoinCheck;
        private bool _rightsStamped;     // road rights first seen owned (saved)
        private float _rightsHours;      // GameClock.TotalHours of that moment (saved)
        private JoinPhase _join = JoinPhase.None;
        private float _joinPhaseStart;
        private bool _facingRight = true;

        private Transform _player;
        private ShepherdController _playerCtrl;
        private Transform _riderVisual;
        private Vector3 _riderVisualBase;
        private bool _riderVisualCached;

        private SpriteRenderer _body;
        private CircleCollider2D _col;
        private Text _hint;
        private Vector3 _baseScale = Vector3.one;
        private bool _focused;
        private float _bobPhase;
        private float _hopT = -1f;
        private float _lift01;
        private bool _subscribed;

        // ---- public surface ----------------------------------------------------------------

        public bool Joined => _joined;
        public float Contentment => _contentment;
        public bool IsPouting => _joined && _contentment < PoutThreshold;
        public bool IsPerked => _joined && GameClockHours() < _perkUntilHours;
        public bool JoinSceneRunning => _join != JoinPhase.None;

        /// <summary>
        /// Speed factor ShepherdController multiplies into its cap: 1 on foot; the
        /// mount's boost while riding (half of it when sprinting, so sprint + ride
        /// can't outrun the camera).
        /// </summary>
        public static float SpeedFactor(bool sprinting)
        {
            if (!IsRiding) return 1f;
            float f = Instance != null && Instance.IsPerked ? PerkRideSpeed : RideSpeed;
            return sprinting ? 1f + (f - 1f) * 0.5f : f;
        }

        // ---- lifecycle ---------------------------------------------------------------------------

        public static PoutyMount GetOrCreate()
        {
            if (Instance == null)
                new GameObject("PoutyMount (runtime)").AddComponent<PoutyMount>();
            return Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsRiding = false;
            DismountFrame = -1;
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            IsRiding = false;
        }

        private void Start()
        {
            TrySubscribe();
            _prevHours = GameClockHours();
        }

        private void TrySubscribe()
        {
            if (_subscribed || GameInput.Instance == null) return;
            GameInput.Instance.RidePressed += HandleRidePressed;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && GameInput.Instance != null)
                GameInput.Instance.RidePressed -= HandleRidePressed;
            if (Instance == this)
            {
                if (IsRiding) EndRideState();
                IsRiding = false;
                Instance = null;
            }
            if (_hint != null) Destroy(_hint.gameObject);
        }

        private void OnDisable()
        {
            if (IsRiding && Instance == this) Dismount(quiet: true);
        }

        private static float GameClockHours() =>
            GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f;

        private bool ResolvePlayer()
        {
            if (_player != null) return true;
            var go = GameObject.FindWithTag("Player");
            if (go == null) return false;
            _player = go.transform;
            _playerCtrl = go.GetComponent<ShepherdController>();
            return true;
        }

        // ---- per frame ------------------------------------------------------------------------------

        private void Update()
        {
            TrySubscribe();
            if (!ResolvePlayer()) return;

            if (!_joined && _join == JoinPhase.None)
            {
                TickJoinTrigger();
                return;
            }

            if (_join != JoinPhase.None)
            {
                TickJoinScene();
                return;
            }

            // ---- joined ----
            TickMood();

            var comp = CompetitionManager.Instance;
            if (IsRiding && comp != null && comp.EventRunning) Dismount(quiet: false);

            if (!IsRiding) TickCompanion();
            TickPoutLines();
        }

        private void LateUpdate()
        {
            if (_player == null) return;

            // riding: glue the mount under the shepherd and lift the rider onto it
            if (IsRiding)
            {
                transform.position = _player.position + new Vector3(0f, -0.05f, 0f);
                Vector2 f = _playerCtrl != null ? _playerCtrl.FacingDir : Vector2.right;
                if (Mathf.Abs(f.x) > 0.1f) _facingRight = f.x > 0f;
            }

            UpdateLift();
            UpdateBodyVisual();
        }

        // ---- join -----------------------------------------------------------------------------------------

        private void TickJoinTrigger()
        {
            if (Time.unscaledTime < _nextJoinCheck) return;
            _nextJoinCheck = Time.unscaledTime + 1f;

            // Owner verdict 2026-10-01: once you own the road rights (a second area to
            // reach) and keep crossing the road to and fro, after some time it shows
            // up on the road to join you. Crossings are counted by RoadTravel.
            var parcels = ParcelManager.Instance;
            if (parcels == null || !parcels.RoadRightsOwned)
            {
                _rightsStamped = false;
                return;
            }
            if (!_rightsStamped)
            {
                _rightsStamped = true;
                _rightsHours = GameClockHours();
            }

            var road = RoadTravel.Instance;
            if (road == null || road.Crossings < JoinCrossings) return;
            if (GameClockHours() - _rightsHours < JoinWaitHours) return;

            // it turns up ON the road, on the next crossing after the conditions are met
            if (!FrontierGeometry.IsOnRoad(_player.position)) return;

            if (UIInputLock.BlockDirectKeys || Time.timeScale <= 0f) return;
            var comp = CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return;

            BeginJoinScene(true);
        }

        private void BeginJoinScene(bool onRoad = false)
        {
            if (!ResolvePlayer()) return;
            EnsureVisual();

            Vector3 pp = _player.position;
            Vector3 start;
            if (onRoad)
            {
                // ahead of the shepherd along the road, inside the lane (flip if the lane ends first)
                var lane = FrontierGeometry.RoadLane;
                float fx = _playerCtrl != null ? _playerCtrl.FacingDir.x : -1f;
                float rdir = fx >= 0.2f ? 1f : -1f;
                float sx = pp.x + rdir * 9f;
                if (sx < lane.xMin || sx > lane.xMax) sx = pp.x - rdir * 9f;
                sx = Mathf.Clamp(sx, lane.xMin, lane.xMax);
                start = new Vector3(sx, Random.Range(lane.yMin + 0.3f, lane.yMax - 0.3f), 0f);
            }
            else
            {
                // arrive from the side of the owned land with more room
                var parcels = ParcelManager.Instance;
                Rect b = parcels != null ? parcels.OwnedBoundsWorld : new Rect(pp.x - 12f, pp.y - 8f, 24f, 16f);
                float dir = pp.x >= b.center.x ? -1f : 1f;
                start = pp + new Vector3(dir * 13f, Random.Range(-2.5f, 2.5f), 0f);
                start.x = Mathf.Clamp(start.x, b.xMin - 1f, b.xMax + 1f);
                start.y = Mathf.Clamp(start.y, b.yMin - 1f, b.yMax + 1f);
            }

            transform.position = start;
            _facingRight = start.x < pp.x; // trots toward the player
            _body.enabled = true;
            _col.enabled = false;
            _join = JoinPhase.Arrive;
            _joinPhaseStart = Time.time;

            GuideMoments.Announce(onRoad
                ? "Something horse-shaped stands in the road ahead, pretending not to look at you."
                : "Something horse-shaped stands at the edge of your land, pretending not to look at you.");
        }

        private void TickJoinScene()
        {
            Vector3 pp = _player.position;
            Vector3 to = pp - transform.position;
            to.z = 0f;
            float dist = to.magnitude;

            if (_join == JoinPhase.Arrive)
            {
                if (dist > 2.3f)
                {
                    _facingRight = to.x >= 0f;
                    transform.position += to / dist * Mathf.Min(JoinSpeed * Time.deltaTime, dist - 2.3f);
                    return;
                }

                _join = JoinPhase.Sigh;
                _joinPhaseStart = Time.time;
                FloatingText.Show(transform.position + Vector3.up * 1.3f, "*sigh*", UIStyle.Cream);
                Puffs.Burst(transform.position + Vector3.down * 0.2f, new Color(0.76f, 0.68f, 0.54f, 0.6f), 5, 0.8f, 0.3f, 0.07f);
                return;
            }

            // Sigh: a beat, then it decides to allow this
            if (Time.time - _joinPhaseStart < 1.6f) return;

            _join = JoinPhase.None;
            _joined = true;
            _contentment = StartContentment;
            _lastTendedHours = GameClockHours();
            _prevHours = _lastTendedHours;
            _col.enabled = true;
            _hopT = 0f;

            FloatingText.Show(transform.position + Vector3.up * 1.3f, "(it has decided to allow this)", UIStyle.Gold);
            Bleeps.Play(BleepKind.Soothe, 0.5f);
            GuideMoments.Announce("Grudge, a pouty horse-spirit, has joined you. It will carry you, if it feels like it. Press "
                + RideKeyLabel() + " beside it to ride.");
        }

        // ---- mood / companion --------------------------------------------------------------------------------

        private void TickMood()
        {
            float now = GameClockHours();
            float dh = Mathf.Max(0f, now - _prevHours);
            _prevHours = now;

            // Riding counts as attention; otherwise ignoring it past the grace window sours it.
            if (IsRiding) _lastTendedHours = now;
            else if (now - _lastTendedHours > IgnoreGraceHours)
                _contentment = Mathf.Max(0f, _contentment - IgnoreDecayPerHour * dh);
        }

        private void TickPoutLines()
        {
            if (!IsPouting || IsRiding) { return; }
            if (Time.time < _nextPoutLineAt) return;
            _nextPoutLineAt = Time.time + Random.Range(18f, 32f);
            FloatingText.Show(transform.position + Vector3.up * 1.2f,
                PoutLines[Random.Range(0, PoutLines.Length)], PoutTint);
        }

        private void TickCompanion()
        {
            Vector3 pp = _player.position;
            Vector3 to = pp - transform.position;
            to.z = 0f;
            float dist = to.magnitude;

            if (dist > TeleportDist)
            {
                // loaded far away / player teleported: reappear a pace behind
                transform.position = pp + new Vector3(_facingRight ? -2.5f : 2.5f, 0f, 0f);
                return;
            }

            bool pout = IsPouting;
            bool resting = ShepherdRest.IsSeated;
            float stop = pout ? PoutFollowDist : (resting ? 1.8f : FollowDist);
            float speed = pout ? PoutSpeed : FollowSpeed;
            if (IsPerked && !pout) speed *= 1.2f;

            if (dist > stop)
            {
                float step = Mathf.Min(speed * Time.deltaTime, dist - stop);
                transform.position += to / dist * step;
                _facingRight = to.x >= 0f;
            }
            else if (pout)
            {
                // sulking: turned away from you, even standing still
                _facingRight = to.x < 0f;
            }
            else if (Mathf.Abs(to.x) > 0.3f)
            {
                _facingRight = to.x >= 0f;
            }
        }

        // ---- ride / dismount ----------------------------------------------------------------------------------

        private void HandleRidePressed()
        {
            if (!_joined || _join != JoinPhase.None) return;
            if (UIInputLock.BlockDirectKeys) return;
            if (IsRiding) Dismount(quiet: false);
            else TryMount();
        }

        /// <summary>Gets on if the mount allows it. False (with a toast) when refused.</summary>
        public bool TryMount()
        {
            if (!_joined || IsRiding || _join != JoinPhase.None) return false;
            if (!ResolvePlayer()) return false;

            Vector3 at = transform.position + Vector3.up * 1.2f;

            if (ShepherdRest.IsSeated)
            {
                FloatingText.Show(_player.position + Vector3.up * 1.4f, "(stand up first)", UIStyle.Grey);
                return false;
            }
            if (ToolController.MovementLocked) return false;

            var comp = CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return false;

            if (Vector2.Distance(_player.position, transform.position) > MountReach)
            {
                FloatingText.Show(_player.position + Vector3.up * 1.4f, "(too far to mount)", UIStyle.Grey);
                return false;
            }

            if (IsPouting)
            {
                FloatingText.Show(at, "(it sulks and won't be ridden - feed or pet it)", PoutTint);
                Bleeps.Play(BleepKind.Denied, 0.35f);
                return false;
            }

            IsRiding = true;
            _lastTendedHours = GameClockHours();
            _col.enabled = false;
            _body.sortingOrder = -1; // under the rider's body (0) and head (1)
            _hopT = 0f;
            Bleeps.Play(BleepKind.Click, 0.4f);
            ShowHint(true);
            return true;
        }

        /// <summary>Gets off beside the mount's spot (public: Interact-while-riding dismounts).</summary>
        public void Dismount() => Dismount(quiet: false);

        private void Dismount(bool quiet)
        {
            if (!IsRiding) return;
            EndRideState();

            if (_player != null)
            {
                Vector2 f = _playerCtrl != null ? _playerCtrl.FacingDir : Vector2.right;
                transform.position = _player.position + (Vector3)(f.normalized * 1.3f);
            }
            if (!quiet) Bleeps.Play(BleepKind.Click, 0.3f);
        }

        private void EndRideState()
        {
            IsRiding = false;
            if (_col != null) _col.enabled = _joined;
            if (_body != null) _body.sortingOrder = 0;
            ShowHint(false);
        }

        // ---- feeding / petting ----------------------------------------------------------------------------------

        private bool HasFeed(out string id)
        {
            id = null;
            var inv = Inventory.Instance;
            if (inv == null) return false;
            // favourite first, then anything else
            if (CropQuality.CountAny(inv, FavoriteFood) > 0) { id = FavoriteFood; return true; }
            for (int i = 0; i < FeedIds.Length; i++)
                if (CropQuality.CountAny(inv, FeedIds[i]) > 0) { id = FeedIds[i]; return true; }
            return false;
        }

        /// <summary>Feeds one unit of a crop (best tier first). False when none is carried.</summary>
        public bool TryFeed(string id)
        {
            var inv = Inventory.Instance;
            if (!_joined || inv == null || string.IsNullOrEmpty(id)) return false;
            if (!CropQuality.ConsumeBest(inv, id, out var tier)) return false;

            bool fav = id == FavoriteFood;
            float gain = (fav ? 40f : 15f) + (tier == CropTier.Gleaming ? 10f : tier == CropTier.Fine ? 5f : 0f);
            float perkHours = fav ? 3f : 1f;

            _contentment = Mathf.Min(100f, _contentment + gain);
            _perkUntilHours = Mathf.Max(_perkUntilHours, GameClockHours() + perkHours);
            _lastTendedHours = GameClockHours();
            _hopT = 0f;

            Vector3 at = transform.position + Vector3.up * 1.2f;
            FloatingText.Show(at, fav ? "it perks right up!" : "(it deigns to eat)", fav ? UIStyle.Gold : UIStyle.Cream);
            FloatingText.Show(at + Vector3.up * 0.4f, "<3", HeartPink);
            Bleeps.Play(BleepKind.Feed, 0.5f);
            return true;
        }

        /// <summary>A scratch behind the ears: the cheap way out of a pout (20 s cooldown).</summary>
        public bool TryPet()
        {
            if (!_joined || IsRiding) return false;
            Vector3 at = transform.position + Vector3.up * 1.2f;
            if (Time.time < _petReadyAt)
            {
                FloatingText.Show(at, "(it has had enough fuss for now)", UIStyle.Grey);
                return false;
            }
            _petReadyAt = Time.time + PetCooldown;
            _contentment = Mathf.Min(100f, _contentment + PetGain);
            _lastTendedHours = GameClockHours();
            _hopT = 0f;
            FloatingText.Show(at, IsPouting ? "(it thaws, slightly)" : "<3", IsPouting ? PoutTint : HeartPink);
            Bleeps.Play(BleepKind.Soothe, 0.4f);
            return true;
        }

        // ---- IInteractable ---------------------------------------------------------------------------------------

        public string PromptText
        {
            get
            {
                if (HasFeed(out string id) && (_contentment < 85f || IsPouting)) return "Feed " + id;
                return IsPouting ? "Pet Grudge" : "Ride Grudge";
            }
        }

        public bool CanInteract(GameObject actor) => _joined && !IsRiding && _join == JoinPhase.None;

        public void Interact(GameObject actor)
        {
            if (HasFeed(out string id) && (_contentment < 85f || IsPouting)) { TryFeed(id); return; }
            if (IsPouting) { TryPet(); return; }
            TryMount();
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
        }

        // ---- ISelectable -------------------------------------------------------------------------------------------

        public string SelectableTitle => "Grudge, the Pouty Mount";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null || !_joined) return;

            into.Add(new SelectAction("(" + MoodWord() + ")", () => { }, false));
            into.Add(new SelectAction(IsRiding ? "Dismount" : "Ride", () =>
            {
                if (IsRiding) Dismount(quiet: false); else TryMount();
            }));

            for (int i = 0; i < FeedIds.Length; i++)
            {
                string id = FeedIds[i];
                if (Inventory.Instance == null || CropQuality.CountAny(Inventory.Instance, id) <= 0) continue;
                string captured = id;
                into.Add(new SelectAction("Feed (" + id + (id == FavoriteFood ? ", favourite" : "") + ")",
                    () => TryFeed(captured)));
            }
            into.Add(new SelectAction("Pet", () => TryPet()));
        }

        private string MoodWord() =>
            IsPerked ? "perked up" : IsPouting ? "pouting" : _contentment >= 70f ? "content" : "unimpressed";

        // ---- visuals ------------------------------------------------------------------------------------------------

        private void EnsureVisual()
        {
            if (_body != null) return;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, false);
            _body = FrontierArt.AddSprite(bodyGo, FrontierArt.Horse, 0);
            _baseScale = Vector3.one * 1.25f;
            bodyGo.transform.localScale = _baseScale;

            _col = gameObject.AddComponent<CircleCollider2D>();
            _col.isTrigger = true;
            _col.radius = 0.9f;
            _col.enabled = false;

            WorldLabel.Attach(gameObject, "Grudge", -0.55f);
        }

        private void UpdateBodyVisual()
        {
            if (_body == null) return;
            bool show = _joined || _join != JoinPhase.None;
            if (_body.enabled != show) _body.enabled = show;
            if (!show) return;

            var t = _body.transform;
            _bobPhase += Time.deltaTime * (IsPerked ? 7f : IsPouting ? 1.6f : 3.2f);

            bool moving = IsRiding
                ? (_playerCtrl != null && _playerCtrl.IsMoving)
                : (_player != null && Vector2.Distance(_player.position, transform.position)
                    > (IsPouting ? PoutFollowDist : FollowDist) + 0.2f);

            float amp = IsPerked ? 0.07f : IsPouting ? 0.015f : 0.03f;
            float y = Mathf.Sin(_bobPhase) * amp * (moving ? 1.8f : 1f);

            if (_hopT >= 0f)
            {
                _hopT += Time.deltaTime;
                float u = _hopT / 0.4f;
                if (u >= 1f) _hopT = -1f; else y += Mathf.Sin(u * Mathf.PI) * 0.22f;
            }

            t.localPosition = new Vector3(0f, y, 0f);

            float s = _focused && !IsRiding ? 1.08f : 1f;
            // pouting hangs its head: a slight forward droop and squash
            var scale = _baseScale * s;
            if (IsPouting) scale = new Vector3(scale.x * 1.04f, scale.y * 0.94f, 1f);
            scale.x *= _facingRight ? 1f : -1f; // sprite faces right; mirror via scale (keeps flipX free)
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(0f, 0f, IsPouting ? -3f : 0f);

            _body.color = IsPouting ? PoutTint : Color.white;
        }

        /// <summary>Eases the rider up onto / down off the mount (lifts the shepherd's "Visual" child).</summary>
        private void UpdateLift()
        {
            if (!_riderVisualCached)
            {
                _riderVisualCached = true;
                _riderVisual = _player != null ? _player.Find("Visual") : null;
                if (_riderVisual != null) _riderVisualBase = _riderVisual.localPosition;
            }
            if (_riderVisual == null) return;

            _lift01 = Mathf.MoveTowards(_lift01, IsRiding ? 1f : 0f, Time.deltaTime * 5f);
            _riderVisual.localPosition = _riderVisualBase + Vector3.up * (RideLift * _lift01);
        }

        // ---- hint + key label -----------------------------------------------------------------------------------------------

        private void ShowHint(bool show)
        {
            if (show && _hint == null)
            {
                var root = UIRoot.GetRoot();
                if (root != null)
                {
                    _hint = UIRoot.MakeText(root, "RideHint", 22, TextAnchor.MiddleCenter, UIStyle.Cream);
                    var rt = _hint.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, 150f);
                    rt.sizeDelta = new Vector2(900f, 34f);
                }
            }
            if (_hint == null) return;
            if (show) _hint.text = "Riding. Tools are put away. " + RideKeyLabel() + " to dismount.";
            _hint.gameObject.SetActive(show);
        }

        private static string RideKeyLabel()
        {
            var gi = GameInput.Instance;
            var map = gi != null && gi.Actions != null ? gi.Actions.FindActionMap("Player") : null;
            var action = map != null ? map.FindAction("Ride") : null;
            if (action == null) return "V";
            string s = action.GetBindingDisplayString(default(InputBinding.DisplayStringOptions), "KeyboardMouse");
            return string.IsNullOrEmpty(s) ? "V" : s;
        }

        // ---- debug -------------------------------------------------------------------------------------------------------------

        /// <summary>Console: the mount joins instantly (no scene), beside the shepherd.</summary>
        public void Debug_Grant()
        {
            ResolvePlayer();
            EnsureVisual();
            _join = JoinPhase.None;
            _joined = true;
            _contentment = StartContentment;
            _lastTendedHours = GameClockHours();
            _prevHours = _lastTendedHours;
            if (_player != null) transform.position = _player.position + new Vector3(-2.2f, 0f, 0f);
            _col.enabled = !IsRiding;
        }

        /// <summary>Console: forget the mount so the join scene can be replayed.</summary>
        public void Debug_Revoke()
        {
            if (IsRiding) Dismount(quiet: true);
            _joined = false;
            _join = JoinPhase.None;
            if (_body != null) _body.enabled = false;
            if (_col != null) _col.enabled = false;
            _nextJoinCheck = 0f;
        }

        /// <summary>Console: play the join scene now, ignoring the road-rights / crossings trigger.</summary>
        public void Debug_PlayJoin()
        {
            if (_joined || _join != JoinPhase.None) return;
            BeginJoinScene();
        }

        public void Debug_SetContentment(float v)
        {
            _contentment = Mathf.Clamp(v, 0f, 100f);
            _lastTendedHours = GameClockHours();
        }

        public string Debug_Status() =>
            _joined
                ? "mount joined, " + MoodWord() + " (" + _contentment.ToString("0") + "/100)"
                    + (IsPerked ? ", perked " + (_perkUntilHours - GameClockHours()).ToString("0.0") + "h" : "")
                    + (IsRiding ? ", RIDING" : "")
                    + ", ignored " + (GameClockHours() - _lastTendedHours).ToString("0.0") + "h"
                : (_join != JoinPhase.None ? "join scene playing" : "mount not joined (" + JoinProgress() + ")");

        /// <summary>Console: road rights / crossings / wait progress toward the join.</summary>
        private string JoinProgress()
        {
            bool rights = ParcelManager.Instance != null && ParcelManager.Instance.RoadRightsOwned;
            int crossings = RoadTravel.Instance != null ? RoadTravel.Instance.Crossings : 0;
            string wait = !rights ? "n/a"
                : !_rightsStamped ? "starting"
                : Mathf.Max(0f, JoinWaitHours - (GameClockHours() - _rightsHours)).ToString("0.0") + "h left";
            return "road rights " + (rights ? "owned" : "NOT owned") + ", crossings "
                + crossings + "/" + JoinCrossings + ", wait " + wait
                + "; it appears on the road on the next crossing once all three are met";
        }

        /// <summary>Console: skip the 1-game-day wait after buying road rights (crossings still count).</summary>
        public void Debug_SkipRightsWait()
        {
            _rightsStamped = true;
            _rightsHours = GameClockHours() - JoinWaitHours - 1f;
        }

        public int Debug_JoinCrossings => JoinCrossings;

        // ---- ISaveable -----------------------------------------------------------------------------------------------------------------

        [Serializable]
        private struct MountState
        {
            public bool joined;
            public float contentment;
            public float perkHoursLeft;
            public float lastTended;
            public float x, y;
            public bool rightsStamped;
            public float rightsHours;
        }

        public string SaveKey => "poutymount";

        public string Capture() => JsonUtility.ToJson(new MountState
        {
            joined = _joined,
            contentment = _contentment,
            perkHoursLeft = Mathf.Max(0f, _perkUntilHours - GameClockHours()),
            lastTended = _lastTendedHours,
            x = transform.position.x,
            y = transform.position.y,
            rightsStamped = _rightsStamped,
            rightsHours = _rightsHours
        });

        public void Restore(string json)
        {
            if (IsRiding) Dismount(quiet: true); // loads always start dismounted
            _join = JoinPhase.None;

            if (string.IsNullOrEmpty(json))
            {
                _joined = false;
                _rightsStamped = false;
                if (_body != null) _body.enabled = false;
                if (_col != null) _col.enabled = false;
                return;
            }

            var s = JsonUtility.FromJson<MountState>(json);
            _joined = s.joined;
            _contentment = Mathf.Clamp(s.contentment <= 0f && s.joined ? 0f : s.contentment, 0f, 100f);
            float now = GameClockHours();
            _perkUntilHours = s.perkHoursLeft > 0f ? now + s.perkHoursLeft : -1f;
            _lastTendedHours = s.lastTended > now ? now : s.lastTended; // clock may have rolled back on load
            _prevHours = now;
            // Old saves have no rights stamp: it is taken the first time road rights are seen owned.
            _rightsStamped = s.rightsStamped;
            _rightsHours = s.rightsHours > now ? now : s.rightsHours;

            if (_joined)
            {
                EnsureVisual();
                transform.position = new Vector3(s.x, s.y, 0f);
                _col.enabled = true;
            }
            else
            {
                if (_body != null) _body.enabled = false;
                if (_col != null) _col.enabled = false;
            }
        }
    }
}

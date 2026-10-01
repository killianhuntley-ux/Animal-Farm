using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Interaction;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>
    /// VILLAIN SPIRITS (muscle 07, verdict 3): untamable chaos agents that
    /// invade, do one small kind of harm, and slink off. NOT redeemable -
    /// they are driven off (player chase, Watchlight wards), never befriended.
    ///
    /// CALIBRATION LAW (owner, verbatim intent): villain damage must be small,
    /// visible, and RECOVERABLE - never large. Every cap below exists for it:
    ///   Digger   - max 4 holes per visit; holes never spread or worsen.
    ///   Devourer - max 2 plants per visit; prefers immature plants.
    ///   Scarer   - max 4 frights per visit at -6 Spirit, floored at 25.
    ///
    /// SCHEDULING: villains only appear once the farm MATTERS - at least
    /// 2 residents AND day >= 4. At most ONE visit per game-day, rolled at
    /// day start (GameCalendar.DayChanged, with a GameClock.Day poll as the
    /// no-calendar fallback): 25% base chance +3% per resident beyond the
    /// second, capped at 40% so big farms are never under siege. A rolled
    /// visit arrives at a random hour (8:00-20:00) and lasts 1-2 game-hours.
    ///
    /// Watchlights ward villains exactly like weeds: a villain never steps
    /// inside a ward radius, and when everything it wants is warded it paces,
    /// grumbles, and leaves early - the defense visibly pays off.
    ///
    /// SAVE: active villains do NOT persist (a loaded game starts villain-free
    /// that day); the Digger's holes DO ("villainholes"). Self-spawns at
    /// runtime (GameCalendar GetOrCreate pattern) - no scene setup required.
    /// Degrades gracefully: no clock/calendar/spirits means no scheduled
    /// villains (the console command still works).
    /// </summary>
    public class VillainManager : MonoBehaviour, ISaveable
    {
        public static VillainManager Instance { get; private set; }

        // ---- scheduling tuning (documented above) -----------------------------
        private const int MinResidents = 2;
        private const int MinDay = 4;
        private const float BaseVisitChance = 0.25f;
        private const float ChancePerExtraResident = 0.03f;
        private const float MaxVisitChance = 0.40f;
        private const float EarliestArrivalHour = 8f;
        private const float LatestArrivalHour = 20f;

        private readonly List<VillainHole> _holes = new List<VillainHole>();

        private VillainAgent _activeAgent;
        private bool _subscribedCalendar;
        private int _polledDay = -1;         // no-calendar fallback day tracker

        // One pending (rolled, not yet arrived) visit at most.
        private int _pendingDay = -1;        // -1 = nothing pending
        private float _pendingHour;
        private VillainKind _pendingKind;

        /// <summary>True while a villain is on the farm (visits are one at a time).</summary>
        public bool VisitActive => _activeAgent != null;

        /// <summary>Every live hole (console/debug listing).</summary>
        public IReadOnlyList<VillainHole> AllHoles => _holes;

        // ---- lifecycle --------------------------------------------------------

        /// <summary>Returns the live manager, creating one on the fly - it needs
        /// no scene setup, so runtime creation is safe (GameCalendar pattern).</summary>
        public static VillainManager GetOrCreate()
        {
            if (Instance == null)
                new GameObject("VillainManager (runtime)").AddComponent<VillainManager>();
            return Instance;
        }

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            TrySubscribeCalendar();
        }

        private void OnDestroy()
        {
            if (_subscribedCalendar && GameCalendar.Instance != null)
                GameCalendar.Instance.DayChanged -= OnDayStart;

            if (Instance == this) Instance = null;
        }

        private void TrySubscribeCalendar()
        {
            if (_subscribedCalendar || GameCalendar.Instance == null) return;
            GameCalendar.Instance.DayChanged += OnDayStart;
            _subscribedCalendar = true;
        }

        // ---- scheduling tick ----------------------------------------------------

        private void Update()
        {
            TrySubscribeCalendar();

            var clock = GameClock.Instance;
            if (clock == null) return; // no clock = no scheduled villains (graceful)

            // No-calendar fallback: poll the clock's day counter directly.
            if (!_subscribedCalendar)
            {
                if (_polledDay < 0) _polledDay = clock.Day;
                else if (clock.Day != _polledDay)
                {
                    _polledDay = clock.Day;
                    OnDayStart(clock.Day);
                }
            }

            // Pending visit: arrive once the rolled hour passes (same day only -
            // a console day-jump simply voids the visit).
            if (_pendingDay < 0) return;
            if (clock.Day != _pendingDay) { _pendingDay = -1; return; }
            if (clock.Hours < _pendingHour) return;

            _pendingDay = -1;
            SpawnVisit(_pendingKind);
        }

        /// <summary>Day-start roll: at most one villain visit per game-day.</summary>
        private void OnDayStart(int day)
        {
            _pendingDay = -1; // yesterday's unspent roll is void

            if (day < MinDay) return;
            int residents = CountResidents();
            if (residents < MinResidents) return; // the farm doesn't matter yet

            float chance = Mathf.Min(MaxVisitChance,
                BaseVisitChance + ChancePerExtraResident * (residents - MinResidents));
            if (Random.value > chance) return;

            _pendingDay = day;
            _pendingHour = Random.Range(EarliestArrivalHour, LatestArrivalHour);
            _pendingKind = (VillainKind)Random.Range(0, 3);
        }

        private static int CountResidents()
        {
            var spirits = SpiritManager.Instance;
            if (spirits == null) return 0; // no spirits = no villains (graceful)

            int count = 0;
            var all = spirits.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].State == SpiritState.Resident) count++;
            }
            return count;
        }

        // ---- visits ---------------------------------------------------------------

        /// <summary>
        /// Spawns a villain at the farm fence with the arrival drama: a low
        /// warning sting, a whisper at the entry point, and the alarm banner.
        /// False if a villain is already visiting or there is no terrain.
        /// </summary>
        private bool SpawnVisit(VillainKind kind)
        {
            if (_activeAgent != null) return false;
            var grid = TerrainGrid.Instance;
            if (grid == null) return false;

            Vector3 entry = grid.RandomUsableBorderPoint();
            _activeAgent = VillainAgent.Spawn(this, kind, entry);

            Bleeps.Play(BleepKind.Alarm, 0.35f); // low, uneasy - not a siren
            FloatingText.Show(entry + Vector3.up * 0.8f,
                "something unpleasant slips in...", UIStyle.Grey);
            AlarmBannerUI.Show(BannerLine(kind));
            return true;
        }

        private static string BannerLine(VillainKind kind)
        {
            switch (kind)
            {
                case VillainKind.Digger:   return "SOMETHING IS BURROWING ON YOUR LAND";
                case VillainKind.Devourer: return "SOMETHING HUNGRY PROWLS THE CROPS";
                default:                   return "A COLD DREAD DRIFTS AMONG YOUR SPIRITS";
            }
        }

        /// <summary>Console cheat: force a visit now, bypassing the day roll
        /// (still one villain at a time).</summary>
        public bool Debug_ForceVisit(VillainKind kind) => SpawnVisit(kind);

        /// <summary>
        /// Called by the agent as it exits at the fence: the relieved beat.
        /// Driven-off exits (chased, or warded out) read as a win.
        /// </summary>
        public void OnVillainGone(VillainAgent agent, Vector3 exitPos, bool drivenOff)
        {
            if (_activeAgent == agent) _activeAgent = null;
            AlarmBannerUI.Hide();
            Bleeps.Play(BleepKind.Soothe, 0.6f);
            FloatingText.Show(exitPos + Vector3.up * 0.8f,
                drivenOff ? "driven off!" : "(the air clears)",
                drivenOff ? UIStyle.Gold : UIStyle.Grey);
        }

        /// <summary>Silent cleanup path (teardown / save-restore): no drama,
        /// just drop the reference and the banner.</summary>
        public void OnVillainVanished(VillainAgent agent)
        {
            if (_activeAgent != agent) return;
            _activeAgent = null;
            AlarmBannerUI.Hide();
        }

        // ---- holes ------------------------------------------------------------------

        /// <summary>
        /// Drops a Digger hole at a cell. Null if one is already there. Holes
        /// block planting/building (via <see cref="VillainHoles.BlocksCell"/>)
        /// until the player tamps them flat - they never expire on their own,
        /// but never spread either (visible, recoverable).
        /// </summary>
        public VillainHole SpawnHole(Vector2Int cell)
        {
            if (VillainHole.AnyAt(cell)) return null;

            var grid = TerrainGrid.Instance;
            Vector3 pos = grid != null ? grid.CellCenterWorld(cell)
                : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);

            var go = new GameObject("VillainHole");
            go.transform.position = pos;
            var hole = go.AddComponent<VillainHole>();
            hole.Init(cell);
            _holes.Add(hole);
            return hole;
        }

        public void RemoveHole(VillainHole hole)
        {
            if (hole == null) return;
            _holes.Remove(hole);
            Destroy(hole.gameObject);
        }

        // ---- warding -----------------------------------------------------------------

        /// <summary>Watchlights ward villains exactly like weeds: true when the
        /// position sits inside any live Watchlight's ward radius.</summary>
        public static bool IsWarded(Vector3 pos)
        {
            var lights = Watchlight.All;
            for (int i = 0; i < lights.Count; i++)
            {
                var light = lights[i];
                if (light == null) continue;
                if (Vector2.Distance(light.transform.position, pos) <= Watchlight.WardRadius)
                    return true;
            }
            return false;
        }

        // ---- ISaveable ------------------------------------------------------------------

        [Serializable]
        private struct HoleRecord
        {
            public int cx, cy;
        }

        [Serializable]
        private class VillainState
        {
            public List<HoleRecord> holes = new List<HoleRecord>();
        }

        public string SaveKey => "villainholes";

        public string Capture()
        {
            var state = new VillainState();
            for (int i = 0; i < _holes.Count; i++)
            {
                var h = _holes[i];
                if (h == null) continue;
                state.holes.Add(new HoleRecord { cx = h.Cell.x, cy = h.Cell.y });
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            // Active villains never persist: a loaded game starts villain-free
            // that day. Pending rolls are dropped too.
            if (_activeAgent != null) _activeAgent.CancelSilently();
            _activeAgent = null;
            _pendingDay = -1;
            AlarmBannerUI.Hide();

            for (int i = _holes.Count - 1; i >= 0; i--)
                if (_holes[i] != null) Destroy(_holes[i].gameObject);
            _holes.Clear();

            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<VillainState>(json);
            if (state == null || state.holes == null) return;

            for (int i = 0; i < state.holes.Count; i++)
            {
                var rec = state.holes[i];
                var cell = new Vector2Int(rec.cx, rec.cy);
                if (TerrainGrid.Instance != null && !TerrainGrid.Instance.InBounds(cell)) continue;
                SpawnHole(cell);
            }
        }
    }

    /// <summary>
    /// Static query surface for placement validators: does a Digger hole block
    /// this cell? PlantManager.PlantSeed and the build validators
    /// (HomeManager.PlaceHome, Watchlight.PlaceAt) should consult this before
    /// accepting a cell - wiring those call sites belongs to their owners.
    /// </summary>
    public static class VillainHoles
    {
        public static bool BlocksCell(Vector2Int cell) => VillainHole.AnyAt(cell);
    }

    /// <summary>
    /// One Digger hole: a small dark blemish that blocks planting/building on
    /// its cell until tamped flat with two quick interacts ("Tamp the earth").
    /// Capped at 4 per visit, never spreads, never expires on its own -
    /// damage that is small, visible, and recoverable (calibration law).
    /// Persisted by VillainManager ("villainholes").
    /// </summary>
    public class VillainHole : MonoBehaviour, IInteractable, ISelectable
    {
        private const int TampsToFix = 2;
        private const float MinTampGapSeconds = 0.3f;
        private const float FocusScale = 1.08f;

        private static readonly List<VillainHole> _all = new List<VillainHole>();
        private static readonly Color HoleTint = new Color(0.16f, 0.11f, 0.08f, 0.95f);
        private static readonly Color DirtBrown = new Color(0.45f, 0.33f, 0.22f);

        private static Sprite _splotch;

        public Vector2Int Cell { get; private set; }

        private Vector3 _baseScale = Vector3.one;
        private int _tamps;
        private float _lastTampTime = float.NegativeInfinity;

        /// <summary>Any live hole on this cell? (See <see cref="VillainHoles.BlocksCell"/>.)</summary>
        public static bool AnyAt(Vector2Int cell)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i].Cell == cell) return true;
            return false;
        }

        /// <summary>Shared dark-splotch sprite, generated once (Puffs pattern -
        /// villains self-spawn, so no bootstrapper hands them art).</summary>
        private static Sprite Splotch
        {
            get
            {
                if (_splotch == null)
                {
                    const int size = 16;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var px = new Color32[size * size];
                    Vector2 c = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 d = new Vector2(x, y) - c;
                        d.y *= 1.6f; // squashed: a pit seen from above
                        float ang = Mathf.Atan2(d.y, d.x);
                        float edge = 1f + 0.22f * Mathf.Sin(ang * 5f)
                                        + 0.12f * Mathf.Sin(ang * 3f + 1.3f);
                        float r = d.magnitude / (size * 0.36f * edge);
                        byte a = r > 1f ? (byte)0 : r < 0.55f ? (byte)255 : (byte)190;
                        px[x + y * size] = new Color32(255, 255, 255, a);
                    }
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.filterMode = FilterMode.Point;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _splotch = Sprite.Create(tex, new Rect(0, 0, size, size),
                        new Vector2(0.5f, 0.5f), size);
                    _splotch.hideFlags = HideFlags.HideAndDontSave;
                }
                return _splotch;
            }
        }

        public void Init(Vector2Int cell)
        {
            Cell = cell;

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = Splotch;
            renderer.color = HoleTint;
            renderer.sortingOrder = 1; // above terrain, below floating text

            _baseScale = Vector3.one * 1.1f;
            transform.localScale = _baseScale;

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.45f;

            WorldLabel.Attach(gameObject, "Hole", -0.5f);
        }

        // ---- tamping ------------------------------------------------------------

        private void Tamp()
        {
            if (Time.time - _lastTampTime < MinTampGapSeconds) return; // quick taps, not holds
            _lastTampTime = Time.time;
            _tamps++;

            Puffs.Burst(transform.position, DirtBrown, 5, 1.1f, 0.3f, 0.09f);

            if (_tamps >= TampsToFix)
            {
                Bleeps.Play(BleepKind.Build, 0.6f); // a satisfying thump
                FloatingText.Show(transform.position + Vector3.up * 0.4f,
                    "tamped flat!", UIStyle.Gold);
                if (VillainManager.Instance != null) VillainManager.Instance.RemoveHole(this);
                else Destroy(gameObject);
            }
            else
            {
                FloatingText.Show(transform.position + Vector3.up * 0.4f,
                    "tamp..", UIStyle.Grey);
            }
        }

        // ---- IInteractable ---------------------------------------------------------

        public string PromptText => "Tamp the earth";

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor) => Tamp();

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ---- ISelectable -------------------------------------------------------------

        public string SelectableTitle => "Burrow hole";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            // Label-only no-op (keeps the menu open), then one tamp per click.
            into.Add(new SelectAction("(blocks planting/building)", () => { }, false));
            into.Add(new SelectAction("Tamp", Tamp, false));
        }

        private void OnEnable() => _all.Add(this);

        private void OnDisable() => _all.Remove(this);
    }
}

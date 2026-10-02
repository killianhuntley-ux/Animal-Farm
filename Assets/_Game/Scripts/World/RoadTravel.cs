using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Player;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>
    /// Road travel (muscle 08 build item 2): the dangers of walking the west
    /// road to Reedmire with spirits in tow. ESCORTING is the existing follow
    /// behaviour ("Come along", Follow Treat); this manager only watches the
    /// escorts (SpiritAgent.IsEscort) and the shepherd while they are on the
    /// road corridor (FrontierGeometry) and layers four dangers on top:
    ///
    ///   DARK STRETCH   - a pitch-dark range of the road. A vignette overlay
    ///                    closes in on the shepherd; WITHOUT a lantern in the
    ///                    pouch (RoadGoods.HasLantern) escorts crawl (x0.55) and
    ///                    their mood dips (-1.1 Spirit/s, floored at 35).
    ///   BIOME STRAIN   - each road segment has a territory biome; an escort
    ///                    whose species dislikes it (BiomeAffinity) loses Spirit
    ///                    per second (floored at 30): route planning matters.
    ///   TOLL IMPS      - on ~40% of days a checkpoint imp (TollImp) camps at
    ///                    the road's head and wants a few obols.
    ///   AMBUSH         - at most once per day, ~35% per crossing with escorts:
    ///                    a Scarer/Devourer bursts out (RoadAmbusher). Escorts may
    ///                    bolt (max 2) straight into the existing herding chase.
    ///                    A matching ward charm (RoadGoods) turns it away.
    ///
    /// CALIBRATION (same law as muscle 07 villains): every penalty is small,
    /// floored, visible and recoverable -- the road alone can never cause a
    /// runaway except a bolt, which is exactly what herding recovers.
    ///
    /// Self-spawning (AfterSceneLoad GetOrCreate, GameCalendar pattern; no scene
    /// setup). SAVE ("roadtravel"): the per-day settle stamps and the road-crossing
    /// count (the pouty mount's join trigger); presence of
    /// imps / dangers is derived from the calendar and the player's position.
    /// </summary>
    public class RoadTravel : MonoBehaviour, ISaveable
    {
        public static RoadTravel Instance { get; private set; }

        // ---- tuning (ASSUMPTIONS where the doc is silent) -----------------------
        private const float StrainFloor = 30f;
        private const float DarkSlowMul = 0.55f;
        private const float DarkMoodPerSecond = 1.1f;
        private const float DarkMoodFloor = 35f;
        private const float AmbushChance = 0.35f;         // per road crossing with escorts
        private const float TollDayChance = 0.40f;
        private const float TollOpenHour = 6f, TollCloseHour = 20f;
        private const int TollBase = 2, TollPerEscort = 1, TollMax = 6;
        private const float RefusalMoodHit = 5f;
        private const float RefusalMoodFloor = 35f;
        private const float ToastCooldown = 7f;           // per spirit per message kind
        private const float DarkFadeSpeed = 2.5f;         // dark01 per second

        // darkness overlay: sprite scale sets the lit radius (see FrontierArt.Vignette)
        private const float UnlitScale = 40f, LanternScale = 100f;
        private const float UnlitAlpha = 0.93f, LanternAlpha = 0.5f;

        private static readonly Color StrainGrey = new Color(0.72f, 0.66f, 0.55f);
        private static readonly Color DarkBlue = new Color(0.55f, 0.62f, 0.85f);

        private readonly List<SpiritAgent> _escorts = new List<SpiritAgent>();
        private readonly Dictionary<SpiritAgent, float> _lastToast = new Dictionary<SpiritAgent, float>();

        private Transform _player;
        private ShepherdController _playerCtrl;

        // darkness overlay
        private SpriteRenderer _overlay;
        private float _dark01;
        private bool _darkHintShown;

        // ambush scheduling
        private bool _crossingRolled;
        private bool _ambushPending;
        private float _ambushTriggerX;
        private RoadAmbusher _ambusher;
        private int _lastAmbushDay = -1;

        // toll imp
        private TollImp _imp;
        private int _tollSettledDay = -1;
        private bool _tollSettledPaid;
        private float _nextTollCheck;

        // debug overrides
        private bool _forceDark;
        private bool _forceTollDay;

        private bool _dressed;

        // road crossings (PoutyMount's join trigger reads the count)
        private const float CrossingEndMargin = 1.5f;
        private int _crossings;
        private int _lastEnd = -1;            // 0 = home end, 1 = swamp end, -1 = unknown

        /// <summary>
        /// Full traversals of the road corridor (home end to swamp end, or back)
        /// made with road rights owned. A half-way turn-around never counts.
        /// </summary>
        public int Crossings => _crossings;

        /// <summary>Escorts seen on the last tick (read-only, for debug/UI).</summary>
        public IReadOnlyList<SpiritAgent> Escorts => _escorts;

        public bool AmbushActive => _ambusher != null;
        public bool IsDarkNow => _dark01 > 0.5f;
        public bool ForceDark => _forceDark;

        // ---- lifecycle -----------------------------------------------------------

        public static RoadTravel GetOrCreate()
        {
            if (Instance == null)
                new GameObject("RoadTravel (runtime)").AddComponent<RoadTravel>();
            return Instance;
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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (!_dressed)
            {
                _dressed = true;
                RoadDressing.Build();
            }
            BuildOverlay();
        }

        // ---- per-frame ---------------------------------------------------------------

        private void Update()
        {
            if (!ResolvePlayer()) return;

            Vector2 pos = _player.position;
            bool onRoad = FrontierGeometry.IsOnRoad(pos);

            TickCrossings(pos, onRoad);
            CollectEscorts();
            TickDark(pos);
            TickStrain();
            TickAmbushSchedule(pos, onRoad);
            if (Time.time >= _nextTollCheck)
            {
                _nextTollCheck = Time.time + 1f;
                RefreshTollImp();
            }
        }

        private void LateUpdate()
        {
            UpdateOverlay();
        }

        private bool ResolvePlayer()
        {
            if (_player != null) return true;
            var go = GameObject.FindWithTag("Player");
            if (go == null) return false;
            _player = go.transform;
            _playerCtrl = go.GetComponent<ShepherdController>();
            return true;
        }

        private void CollectEscorts()
        {
            _escorts.Clear();
            var mgr = SpiritManager.Instance;
            if (mgr == null) return;

            var all = mgr.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;
                if (a.IsEscort) _escorts.Add(a);
                else if (!Mathf.Approximately(a.RoadSpeedMul, 1f)) a.RoadSpeedMul = 1f; // no longer escorted
            }

            // keep the toast table from growing without bound
            if (_lastToast.Count > 64) _lastToast.Clear();
        }

        // ---- crossings ---------------------------------------------------------------------

        /// <summary>Which end of the corridor a point counts as: 0 home, 1 swamp, -1 neither.</summary>
        private static int EndAt(Vector2 p, bool onRoad)
        {
            if (onRoad)
            {
                if (p.x >= FrontierGeometry.RoadRect.xMax - CrossingEndMargin) return 0;
                if (p.x <= FrontierGeometry.RoadRect.xMin + CrossingEndMargin) return 1;
                return -1;
            }
            if (FrontierGeometry.InSwamp(p)) return 1;
            if (FrontierGeometry.HomeRect.Contains(p)) return 0;
            return -1;
        }

        /// <summary>
        /// Counts one crossing each time the shepherd reaches the OPPOSITE end of
        /// the corridor from the last end they touched (so home -> swamp -> home is
        /// two). Only counted while road rights are owned.
        /// </summary>
        private void TickCrossings(Vector2 pos, bool onRoad)
        {
            int end = EndAt(pos, onRoad);
            if (end < 0 || end == _lastEnd) return;

            bool counts = _lastEnd >= 0
                && ParcelManager.Instance != null && ParcelManager.Instance.RoadRightsOwned;
            _lastEnd = end;
            if (counts) _crossings++;
        }

        // ---- dark stretch ---------------------------------------------------------------

        private bool DarkAt(Vector2 p) => _forceDark || FrontierGeometry.IsDark(p);

        private void TickDark(Vector2 playerPos)
        {
            bool playerDark = DarkAt(playerPos);
            _dark01 = Mathf.MoveTowards(_dark01, playerDark ? 1f : 0f, DarkFadeSpeed * Time.deltaTime);

            bool lantern = RoadGoods.HasLantern;
            if (playerDark && !lantern && !_darkHintShown)
            {
                _darkHintShown = true;
                FloatingText.Show(playerPos + Vector2.up * 1.4f,
                    "(the dark swallows the road - a lantern would help)", DarkBlue);
            }

            // Escorts are judged by THEIR OWN position: they slow where the dark is.
            for (int i = 0; i < _escorts.Count; i++)
            {
                var a = _escorts[i];
                bool inDark = DarkAt(a.transform.position);
                if (inDark && !lantern)
                {
                    a.RoadSpeedMul = DarkSlowMul;
                    a.RoadMoodDelta(-DarkMoodPerSecond * Time.deltaTime, DarkMoodFloor);
                    Toast(a, "dark", "(it hates the dark)", DarkBlue);
                }
                else
                {
                    a.RoadSpeedMul = 1f;
                }
            }
        }

        // ---- biome strain -------------------------------------------------------------------

        private void TickStrain()
        {
            for (int i = 0; i < _escorts.Count; i++)
            {
                var a = _escorts[i];
                Vector2 p = a.transform.position;
                if (!FrontierGeometry.IsOnRoad(p)) continue;

                var territory = EffectiveTerritory(p);
                float rate = BiomeAffinity.StrainPerSecond(BiomeAffinity.For(a.Species, territory));
                if (rate <= 0f) continue;

                a.RoadMoodDelta(-rate * Time.deltaTime, StrainFloor);
                Toast(a, "strain", "(" + NameOf(a) + " hates this ground)", StrainGrey);
            }
        }

        /// <summary>
        /// Territory biome under a road point. The middle stretch is the fixed
        /// wilds (FrontierGeometry); the two ends borrow the CENSUSED biome of the
        /// base they lead out of (BiomeScorer), so how you sculpt your land shapes
        /// who is comfortable at its doorstep. Barren / no scorer = the default.
        /// </summary>
        public static BiomeType EffectiveTerritory(Vector2 p)
        {
            if (!FrontierGeometry.IsOnRoad(p)) return BiomeType.Barren;
            int seg = FrontierGeometry.SegmentIndexAt(p.x);
            if (seg < 0) return BiomeType.Barren;

            var territory = FrontierGeometry.Segments[seg].territory;
            var scorer = BiomeScorer.Instance;
            if (scorer == null) return territory;

            int last = FrontierGeometry.Segments.Length - 1;
            int baseId = seg == 0 ? ParcelManager.HomeBaseId : seg == last ? ParcelManager.SwampBaseId : -1;
            if (baseId < 0) return territory;

            var census = scorer.GetBiome(baseId);
            return census != BiomeType.Barren ? census : territory;
        }

        private void Toast(SpiritAgent a, string kind, string text, Color color)
        {
            if (a == null) return;
            // one toast per (spirit, kind) per cooldown: the dictionary keys the spirit,
            // so strain and dark share a slot -- fine, they rarely overlap.
            if (_lastToast.TryGetValue(a, out float last) && Time.time - last < ToastCooldown) return;
            _lastToast[a] = Time.time;
            FloatingText.Show(a.transform.position + Vector3.up * 0.9f, text, color);
        }

        private static string NameOf(SpiritAgent a) =>
            !string.IsNullOrEmpty(a.GivenName) ? a.GivenName
            : a.Species != null && !string.IsNullOrEmpty(a.Species.displayName) ? a.Species.displayName
            : "it";

        // ---- ambush ---------------------------------------------------------------------------

        private void TickAmbushSchedule(Vector2 pos, bool onRoad)
        {
            if (!onRoad)
            {
                // left the corridor altogether: next entry is a fresh crossing
                _crossingRolled = false;
                _ambushPending = false;
                return;
            }

            if (!_crossingRolled && _escorts.Count > 0)
            {
                _crossingRolled = true;
                int day = GameClock.Instance != null ? GameClock.Instance.Day : 1;
                bool allowed = day != _lastAmbushDay && _ambusher == null;
                _ambushPending = allowed && Random.value < AmbushChance;
                if (_ambushPending)
                {
                    var lane = FrontierGeometry.RoadLane;
                    _ambushTriggerX = Random.Range(lane.xMin + 2.5f, lane.xMax - 3.5f);
                }
            }

            if (_ambushPending && _escorts.Count > 0 && Mathf.Abs(pos.x - _ambushTriggerX) < 0.8f)
            {
                _ambushPending = false;
                StartAmbush(PickAmbushKind(), pos);
            }
        }

        private static VillainKind PickAmbushKind() =>
            Random.value < 0.5f ? VillainKind.Scarer : VillainKind.Devourer;

        /// <summary>
        /// Starts a road ambush NOW at the player (debug console and the
        /// scheduler). The ambusher bursts out ahead of the shepherd. A carried
        /// ward charm of the right kind, or a Watchlight ward over the spot,
        /// turns it away. Digger is not a road ambusher (it only burrows).
        /// </summary>
        public bool StartAmbush(VillainKind kind, Vector2 playerPos)
        {
            if (_ambusher != null) return false;
            if (kind == VillainKind.Digger) kind = VillainKind.Scarer;

            Vector2 dir = _playerCtrl != null ? _playerCtrl.FacingDir : Vector2.left;
            if (Mathf.Abs(dir.x) < 0.2f) dir = new Vector2(Mathf.Sign(dir.x == 0f ? -1f : dir.x), 0f);
            Vector2 spot = playerPos + new Vector2(Mathf.Sign(dir.x) * 3.2f, Random.Range(-0.8f, 0.8f));
            var lane = FrontierGeometry.RoadLane;
            if (FrontierGeometry.IsOnRoad(playerPos))
            {
                spot.x = Mathf.Clamp(spot.x, lane.xMin, lane.xMax);
                spot.y = Mathf.Clamp(spot.y, lane.yMin, lane.yMax);
            }

            _lastAmbushDay = GameClock.Instance != null ? GameClock.Instance.Day : 1;

            bool fended = VillainManager.IsWarded(spot);
            if (!fended) fended = RoadGoods.TryConsumeWard(kind, spot);

            _ambusher = RoadAmbusher.Spawn(kind, spot, fended, this);
            return _ambusher != null;
        }

        /// <summary>Called by the ambusher when it leaves (driven off or slunk away).</summary>
        public void OnAmbusherGone(RoadAmbusher who)
        {
            if (_ambusher == who) _ambusher = null;
        }

        // ---- toll imp -----------------------------------------------------------------------------

        private int Today => GameClock.Instance != null ? GameClock.Instance.Day : 1;

        /// <summary>Deterministic per-day roll: same day, same answer, across saves.</summary>
        public static bool IsTollDay(int day)
        {
            var rng = new System.Random(day * 104729 + 17);
            return rng.NextDouble() < TollDayChance;
        }

        public bool TollSettledToday => _tollSettledDay == Today;
        public bool TollPaidToday => _tollSettledDay == Today && _tollSettledPaid;
        public bool TollImpPresent => _imp != null;

        /// <summary>Current toll: small, grows a little with the party you drag along.</summary>
        public int CurrentToll => Mathf.Min(TollMax, TollBase + TollPerEscort * _escorts.Count);

        private void RefreshTollImp()
        {
            var parcels = ParcelManager.Instance;
            bool roadOpen = parcels != null && parcels.RoadRightsOwned;
            var clock = GameClock.Instance;
            bool inHours = clock != null && clock.Hours >= TollOpenHour && clock.Hours < TollCloseHour;
            bool should = _forceTollDay || (roadOpen && inHours && clock != null && IsTollDay(clock.Day));

            if (should && _imp == null)
            {
                _imp = TollImp.Spawn(FrontierGeometry.TollPost, this);
            }
            else if (!should && _imp != null)
            {
                Destroy(_imp.gameObject);
                _imp = null;
            }
        }

        /// <summary>The imp reports a settled toll (paid or refused) for today.</summary>
        public void SettleToll(bool paid)
        {
            _tollSettledDay = Today;
            _tollSettledPaid = paid;
        }

        /// <summary>The refusal consequence: escorts are insulted (small, floored, recoverable).</summary>
        public void ApplyTollRefusal()
        {
            for (int i = 0; i < _escorts.Count; i++)
            {
                var a = _escorts[i];
                if (a == null) continue;
                a.RoadMoodDelta(-RefusalMoodHit, RefusalMoodFloor);
                FloatingText.Show(a.transform.position + Vector3.up * 0.9f, "(insulted)", StrainGrey);
            }
        }

        /// <summary>Number of escorts right now (the imp prices its toll from it).</summary>
        public int EscortCount => _escorts.Count;

        // ---- darkness overlay -----------------------------------------------------------------------

        private void BuildOverlay()
        {
            if (_overlay != null) return;
            var go = new GameObject("RoadDarkness");
            go.transform.SetParent(transform, false);
            _overlay = go.AddComponent<SpriteRenderer>();
            _overlay.sprite = FrontierArt.Vignette;
            _overlay.sortingOrder = 400; // over the world, under floating text (500)
            _overlay.color = new Color(1f, 1f, 1f, 0f);
            _overlay.enabled = false;
        }

        private void UpdateOverlay()
        {
            if (_overlay == null || _player == null) return;

            if (_dark01 <= 0.005f)
            {
                if (_overlay.enabled) _overlay.enabled = false;
                return;
            }

            bool lantern = RoadGoods.HasLantern;
            float scale = lantern ? LanternScale : UnlitScale;
            float alpha = (lantern ? LanternAlpha : UnlitAlpha) * _dark01;

            // the lit circle stays on the shepherd; the quad is huge so coverage is never an issue
            Vector3 focus = _player.position;
            _overlay.transform.position = new Vector3(focus.x, focus.y + 0.3f, 0f);
            _overlay.transform.localScale = new Vector3(scale, scale, 1f);
            _overlay.color = new Color(1f, 1f, 1f, alpha);
            _overlay.enabled = true;
        }

        // ---- debug -------------------------------------------------------------------------------------

        /// <summary>Console: toggle forced darkness (applies to the road AND anywhere else).</summary>
        public bool Debug_ToggleDark(bool? set = null)
        {
            _forceDark = set ?? !_forceDark;
            if (!_forceDark) _darkHintShown = false;
            return _forceDark;
        }

        /// <summary>Console: toggle the toll imp regardless of day / hour / road rights.</summary>
        public bool Debug_ToggleTollDay(bool? set = null)
        {
            _forceTollDay = set ?? !_forceTollDay;
            _nextTollCheck = 0f;
            return _forceTollDay;
        }

        /// <summary>Console: forget today's toll so the imp asks again.</summary>
        public void Debug_ResetToll() { _tollSettledDay = -1; _tollSettledPaid = false; }

        /// <summary>Console: set the road-crossing count (the pouty mount joins at its threshold).</summary>
        public void Debug_SetCrossings(int n) { _crossings = Mathf.Max(0, n); }

        /// <summary>Console: one-line status.</summary>
        public string Debug_Status()
        {
            string where = "off-road";
            if (_player != null && FrontierGeometry.IsOnRoad(_player.position))
            {
                int s = FrontierGeometry.SegmentIndexAt(_player.position.x);
                where = s >= 0 ? FrontierGeometry.Segments[s].name + " (" + EffectiveTerritory(_player.position) + ")" : "road";
            }
            int day = Today;
            return "road: " + where + ", escorts " + _escorts.Count
                + ", dark " + (DarkAt(_player != null ? (Vector2)_player.position : Vector2.zero) ? "yes" : "no")
                + (_forceDark ? " [forced]" : "")
                + ", lantern " + (RoadGoods.HasLantern ? "yes" : "no")
                + ", tollDay " + (IsTollDay(day) || _forceTollDay ? "yes" : "no")
                + (_imp != null ? " (imp out" + (TollSettledToday ? ", settled)" : ")") : "")
                + ", ambush " + (_ambusher != null ? "ACTIVE" : _ambushPending ? "armed@" + _ambushTriggerX.ToString("0.0") : "none")
                + ", lastAmbushDay " + _lastAmbushDay
                + ", crossings " + _crossings;
        }

        // ---- ISaveable -------------------------------------------------------------------------------------

        [Serializable]
        private struct RoadState
        {
            public int lastAmbushDay;
            public int tollSettledDay;
            public bool tollSettledPaid;
            public int crossings;
            public int lastEnd;
        }

        public string SaveKey => "roadtravel";

        public string Capture() => JsonUtility.ToJson(new RoadState
        {
            lastAmbushDay = _lastAmbushDay,
            tollSettledDay = _tollSettledDay,
            tollSettledPaid = _tollSettledPaid,
            crossings = _crossings,
            lastEnd = _lastEnd
        });

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var s = JsonUtility.FromJson<RoadState>(json);
            _lastAmbushDay = s.lastAmbushDay;
            _tollSettledDay = s.tollSettledDay;
            _tollSettledPaid = s.tollSettledPaid;
            _crossings = Mathf.Max(0, s.crossings);
            _lastEnd = s.crossings > 0 ? Mathf.Clamp(s.lastEnd, -1, 1) : -1;
        }
    }
}

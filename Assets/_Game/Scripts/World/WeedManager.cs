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
    /// WEEDS (absence-pressure skeleton). Weeds sprout where the shepherd
    /// ISN'T - far from them and outside every Watchlight ward - and sour the
    /// moods of nearby residents while they stand. Friction compounds if
    /// ignored (weeds mature, harm more) but NEVER erases player work: weeds
    /// never destroy terrain, plants, homes, or items (owner law: "players
    /// must not feel they did work for nothing"). Pulling one takes three
    /// quick tugs. Persisted; Watchlights are not (this slice).
    /// </summary>
    public class WeedManager : MonoBehaviour, ISaveable
    {
        public static WeedManager Instance { get; private set; }

        private const int MaxLiveWeeds = 4;
        private const float SpawnIntervalSeconds = 60f;    // scaled time
        private const float ShepherdClearRadius = 5f;      // no sprouting near the player
        private const float AlertCooldownGameHours = 2f;   // soft one-liner throttle

        [Tooltip("Dark thistle sprite; assigned by bootstrapper.")]
        [SerializeField] private Sprite weedSprite;
        [Tooltip("Lantern post sprite for vendor-bought Watchlights; assigned by bootstrapper.")]
        [SerializeField] private Sprite watchlightSprite;
        [Tooltip("Assigned by bootstrapper (Sprite-Lit-Default); null is fine.")]
        [SerializeField] private Material spriteMaterial;

        private readonly List<Weed> _weeds = new List<Weed>();
        private float _spawnTimer;
        private float _lastAlertAtTotalHours = float.NegativeInfinity;

        public IReadOnlyList<Weed> AllWeeds => _weeds;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- spawn tick -------------------------------------------------------

        private void Update()
        {
            _spawnTimer += Time.deltaTime;
            if (_spawnTimer < SpawnIntervalSeconds) return;
            _spawnTimer = 0f;

            TrySpawnOne();
        }

        private void TrySpawnOne()
        {
            _weeds.RemoveAll(w => w == null);
            if (_weeds.Count >= MaxLiveWeeds) return;

            var grid = TerrainGrid.Instance;
            if (grid == null) return;

            Vector3 pos = grid.RandomUsableInteriorPoint();
            if (!grid.TryWorldToCell(pos, out var cell)) return;
            if (!IsCellSproutable(cell, pos)) return; // one candidate per tick; scarcity is fine

            Spawn(cell, GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f);
            SoftAlert();
        }

        /// <summary>Weeds sprout where you AREN'T: rejects Water, plants, homes,
        /// occupied cells, Watchlight wards, and anywhere near the shepherd.</summary>
        private bool IsCellSproutable(Vector2Int cell, Vector3 pos)
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.IsUsable(cell)) return false;
            if (grid.GetSurface(cell) == Surface.Water) return false;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return false;
            if (Home.AnyAtCell(cell)) return false;
            if (HasWeedAt(cell)) return false;

            // Watchlight wards.
            var lights = Watchlight.All;
            for (int i = 0; i < lights.Count; i++)
            {
                var light = lights[i];
                if (light == null) continue;
                if (Vector2.Distance(light.transform.position, pos) <= Watchlight.WardRadius)
                    return false;
            }

            // The shepherd's presence keeps the ground honest.
            var player = GameObject.FindWithTag("Player");
            if (player != null && Vector2.Distance(player.transform.position, pos) <= ShepherdClearRadius)
                return false;

            return true;
        }

        private bool HasWeedAt(Vector2Int cell)
        {
            for (int i = 0; i < _weeds.Count; i++)
                if (_weeds[i] != null && _weeds[i].Cell == cell) return true;
            return false;
        }

        /// <summary>One grey line at the shepherd, throttled to once per 2 game-hours.
        /// (A journal entry belongs to a later slice.)</summary>
        private void SoftAlert()
        {
            if (GameClock.Instance == null) return;
            float now = GameClock.Instance.TotalHours;
            if (now - _lastAlertAtTotalHours < AlertCooldownGameHours) return;
            _lastAlertAtTotalHours = now;

            var player = GameObject.FindWithTag("Player");
            if (player == null) return;
            FloatingText.Show(player.transform.position + Vector3.up * 1.0f,
                "(something takes root, far off)", UIStyle.Grey);
        }

        private Weed Spawn(Vector2Int cell, float spawnedAtTotalHours)
        {
            var grid = TerrainGrid.Instance;
            Vector3 pos = grid != null ? grid.CellCenterWorld(cell)
                : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);

            var go = new GameObject("Weed");
            go.transform.position = pos;
            var weed = go.AddComponent<Weed>();
            weed.Init(cell, spawnedAtTotalHours, weedSprite, spriteMaterial);
            _weeds.Add(weed);
            return weed;
        }

        // ---- removal ------------------------------------------------------------

        public void Remove(Weed w)
        {
            if (w == null) return;
            _weeds.Remove(w);
            Destroy(w.gameObject);
        }

        // ---- vendor / console helpers --------------------------------------------

        /// <summary>
        /// Places a Watchlight 1.5 units right of the shepherd (consume-free;
        /// VendorUI pays the obols first and refunds if this returns null).
        /// </summary>
        public Watchlight PlaceWatchlightNearPlayer()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) return null;
            return Watchlight.PlaceAt(player.transform.position + Vector3.right * 1.5f,
                watchlightSprite, spriteMaterial);
        }

        /// <summary>Console cheat: force-sprouts a weed near the player, skipping
        /// the absence rules (ward/shepherd distance) but not cell validity.</summary>
        public Weed ForceSpawnNearPlayer()
        {
            var grid = TerrainGrid.Instance;
            var player = GameObject.FindWithTag("Player");
            if (grid == null || player == null) return null;

            Vector3 basePos = player.transform.position;
            for (int tries = 0; tries < 12; tries++)
            {
                Vector3 candidate = basePos + (Vector3)(Random.insideUnitCircle.normalized
                    * Random.Range(1.5f, 3f));
                if (!grid.TryWorldToCell(candidate, out var cell)) continue;
                if (!grid.IsUsable(cell)) continue;
                if (grid.GetSurface(cell) == Surface.Water) continue;
                if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) continue;
                if (Home.AnyAtCell(cell)) continue;
                if (HasWeedAt(cell)) continue;

                return Spawn(cell, GameClock.Instance != null ? GameClock.Instance.TotalHours : 0f);
            }
            return null;
        }

        // ---- ISaveable ------------------------------------------------------------

        [Serializable]
        private struct WeedRecord
        {
            public int cx, cy;
            public float spawnedAt;
        }

        [Serializable]
        private class WeedsState
        {
            public List<WeedRecord> weeds = new List<WeedRecord>();
        }

        public string SaveKey => "weeds";

        public string Capture()
        {
            var state = new WeedsState();
            for (int i = 0; i < _weeds.Count; i++)
            {
                var w = _weeds[i];
                if (w == null) continue;
                state.weeds.Add(new WeedRecord
                {
                    cx = w.Cell.x,
                    cy = w.Cell.y,
                    spawnedAt = w.SpawnedAtTotalHours
                });
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            for (int i = _weeds.Count - 1; i >= 0; i--)
                if (_weeds[i] != null) Destroy(_weeds[i].gameObject);
            _weeds.Clear();

            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<WeedsState>(json);
            if (state == null || state.weeds == null) return;

            for (int i = 0; i < state.weeds.Count; i++)
            {
                var rec = state.weeds[i];
                var cell = new Vector2Int(rec.cx, rec.cy);
                if (TerrainGrid.Instance != null && !TerrainGrid.Instance.InBounds(cell)) continue;
                if (HasWeedAt(cell)) continue;
                Spawn(cell, rec.spawnedAt);
            }
        }
    }

    /// <summary>
    /// One dark thistle. Sours the Spirit of nearby residents on a slow tick;
    /// matures after 48 game-hours (wider reach, deeper sour). Never touches
    /// terrain, plants, or homes - mood pressure only (owner law). Pulled out
    /// with three quick interact tugs, or click-select "Pull" one tug at a time.
    /// </summary>
    public class Weed : MonoBehaviour, IInteractable, ISelectable
    {
        private const float HarmIntervalSeconds = 10f;   // scaled time
        private const float YoungHarmRadius = 3f;
        private const float YoungSpiritLoss = 2f;
        private const float MatureHarmRadius = 4f;
        private const float MatureSpiritLoss = 3f;
        private const float MatureAfterGameHours = 48f;
        private const float VictimTextCooldown = 30f;    // scaled seconds, per victim
        private const int TugsToPull = 3;
        private const float MinTugGapSeconds = 0.3f;
        private const float FocusScale = 1.08f;

        private static readonly Color YoungTint = new Color(0.45f, 0.3f, 0.5f);
        private static readonly Color MatureTint = new Color(0.3f, 0.19f, 0.36f);
        private static readonly Color SpiritLossPurple = new Color(0.72f, 0.5f, 0.85f);

        public Vector2Int Cell { get; private set; }
        public float SpawnedAtTotalHours { get; private set; }
        public bool IsMature { get; private set; }

        private SpriteRenderer _renderer;
        private Vector3 _baseScale = Vector3.one * 1.1f;
        private float _swayPhase;
        private bool _focused;

        private float _harmTimer;
        private readonly Dictionary<SpiritAgent, float> _lastVictimText =
            new Dictionary<SpiritAgent, float>();

        private int _tugs;
        private float _lastTugTime = float.NegativeInfinity;

        public void Init(Vector2Int cell, float spawnedAtTotalHours, Sprite sprite, Material mat)
        {
            Cell = cell;
            SpawnedAtTotalHours = spawnedAtTotalHours;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.color = YoungTint;
            _renderer.sortingOrder = 1;
            if (mat != null) _renderer.sharedMaterial = mat;

            _baseScale = Vector3.one * 1.1f;
            transform.localScale = _baseScale;
            _swayPhase = Random.Range(0f, Mathf.PI * 2f);

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.45f;

            WorldLabel.Attach(gameObject, "Weed", -0.6f);

            RefreshMaturity();
        }

        private void Update()
        {
            // Slight sway.
            float angle = Mathf.Sin(Time.time * 1.7f + _swayPhase) * 5f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            RefreshMaturity();

            _harmTimer += Time.deltaTime;
            if (_harmTimer >= HarmIntervalSeconds)
            {
                _harmTimer = 0f;
                SourNearbyResidents();
            }
        }

        private void RefreshMaturity()
        {
            if (IsMature || GameClock.Instance == null) return;
            if (GameClock.Instance.TotalHours - SpawnedAtTotalHours < MatureAfterGameHours) return;

            IsMature = true;
            _baseScale = Vector3.one * 1.3f;
            transform.localScale = _focused ? _baseScale * FocusScale : _baseScale;
            if (_renderer != null) _renderer.color = MatureTint;
        }

        /// <summary>Compounding friction, never erasure: nearby residents lose a
        /// little Spirit. Terrain, plants, and homes are untouched.</summary>
        private void SourNearbyResidents()
        {
            if (SpiritManager.Instance == null) return;

            float radius = IsMature ? MatureHarmRadius : YoungHarmRadius;
            float loss = IsMature ? MatureSpiritLoss : YoungSpiritLoss;

            var spirits = SpiritManager.Instance.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null || a.State != SpiritState.Resident) continue;
                if (Vector2.Distance(a.transform.position, transform.position) > radius) continue;

                // No dedicated harm API yet (muscle phase); the debug setter is
                // the one sanctioned clamp-safe write.
                a.Debug_SetSpirit(a.Spirit - loss);

                if (!_lastVictimText.TryGetValue(a, out float last)
                    || Time.time - last >= VictimTextCooldown)
                {
                    _lastVictimText[a] = Time.time;
                    FloatingText.Show(a.transform.position + Vector3.up * 0.6f,
                        "-spirit", SpiritLossPurple);
                }
            }
        }

        // ---- pulling -------------------------------------------------------------

        private void Tug()
        {
            if (Time.time - _lastTugTime < MinTugGapSeconds) return; // quick tugs, not holds
            _lastTugTime = Time.time;
            _tugs++;

            if (_tugs >= TugsToPull)
            {
                // Useful weeds (muscle 07): even the enemy pays out - a scrap
                // of fiber for compost or vendor trade. Work is never wasted.
                if (AnimalFarm.Core.Inventory.Instance != null)
                    AnimalFarm.Core.Inventory.Instance.Add("fiber", 1);
                FloatingText.Show(transform.position + Vector3.up * 0.4f,
                    "ripped it out! +1 fiber", UIStyle.Gold);
                if (WeedManager.Instance != null) WeedManager.Instance.Remove(this);
                else Destroy(gameObject);
            }
            else
            {
                FloatingText.Show(transform.position + Vector3.up * 0.4f, "tug..", UIStyle.Grey);
            }
        }

        // ---- IInteractable ---------------------------------------------------------

        public string PromptText => "Pull weed";

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor) => Tug();

        public void SetFocused(bool focused)
        {
            _focused = focused;
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ---- ISelectable -------------------------------------------------------------

        public string SelectableTitle => IsMature ? "Weed (grown wild)" : "Weed";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            // One tug per click; the menu stays open for the next tug.
            into.Add(new SelectAction("Pull", Tug, false));
        }

        private void OnDestroy()
        {
            _lastVictimText.Clear();
        }
    }
}

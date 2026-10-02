using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Spirits;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>
    /// Muscle 02 compost loop: spirits feed the land that feeds the spirits.
    /// SOURCE: every ~40 scaled seconds each happy Resident (Spirit >= 70, the
    /// same bar as essence motes) may leave an essence-rich LEAVING on the
    /// ground; the shepherd collects it by walking over it (+1 "compost").
    /// Weed fiber (WeedManager) is the humble second source: 3 fiber stand in
    /// for 1 compost when none is held.
    /// USE: compost is worked into TILLED soil -- either an empty Dirt cell
    /// (seed picker, "Compost soil": the cell stays enriched until a crop is
    /// sown there, which then carries it) or straight onto a growing crop
    /// (Interact). A composted crop grows 25% faster and its quality score gets
    /// a nudge (Plant.ApplyCompost). Enrichment never expires on its own and is
    /// only lost if the ground itself is changed deliberately.
    /// Enriched cells persist ("compost" save key); uncollected leavings do not
    /// (the EssenceSpawner trade-off: cap + respawn from happy residents).
    /// Self-spawns at runtime (WeatherManager pattern) - no scene setup.
    /// </summary>
    public class CompostManager : MonoBehaviour, ISaveable
    {
        public static CompostManager Instance { get; private set; }

        public const string CompostId = "compost";
        public const string FiberId = "fiber";
        public const int FiberPerCompost = 3;

        private const float TickSeconds = 40f;     // scaled, so it pauses with the game
        private const float SpiritThreshold = 70f;
        private const float DropChance = 0.25f;
        private const int MaxLiveLeavings = 6;     // cap on uncollected leavings in the world
        private const float ScatterRadius = 0.7f;
        private const float AuditSeconds = 5f;

        private static readonly Color SoilRich = new Color(0.16f, 0.10f, 0.05f, 0.55f);

        private readonly Dictionary<Vector2Int, GameObject> _enriched = new Dictionary<Vector2Int, GameObject>();
        private readonly List<CompostLeaving> _live = new List<CompostLeaving>();
        private readonly List<Vector2Int> _scratch = new List<Vector2Int>();
        private float _timer;
        private float _auditTimer;
        private bool _subscribed;
        private Material _overlayMat;

        /// <summary>Returns the live manager, creating one if the scene predates compost.</summary>
        public static CompostManager GetOrCreate()
        {
            if (Instance == null)
                new GameObject("CompostManager (runtime)").AddComponent<CompostManager>();
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
            if (TerrainGrid.Instance != null)
            {
                TerrainGrid.Instance.OnSurfaceChanged += HandleSurfaceChanged;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && TerrainGrid.Instance != null)
                TerrainGrid.Instance.OnSurfaceChanged -= HandleSurfaceChanged;
            if (Instance == this) Instance = null;
        }

        // ---- material ---------------------------------------------------------

        /// <summary>Compost units on hand, counting weed fiber at FiberPerCompost each.</summary>
        public int Available
        {
            get
            {
                var inv = Inventory.Instance;
                if (inv == null) return 0;
                return inv.Count(CompostId) + inv.Count(FiberId) / FiberPerCompost;
            }
        }

        /// <summary>Spends one compost, or FiberPerCompost fiber when none is held.</summary>
        private bool ConsumeMaterial()
        {
            var inv = Inventory.Instance;
            if (inv == null) return false;
            if (inv.Consume(CompostId, 1)) return true;
            return inv.Count(FiberId) >= FiberPerCompost && inv.Consume(FiberId, FiberPerCompost);
        }

        // ---- applying ---------------------------------------------------------

        public bool IsEnriched(Vector2Int cell) => _enriched.ContainsKey(cell);

        /// <summary>Works compost into an EMPTY tilled cell; the next crop sown there inherits it.</summary>
        public bool TryEnrichSoil(Vector2Int cell)
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.IsUsable(cell) || !IsSoil(grid.GetSurface(cell))) return false;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return false;
            if (_enriched.ContainsKey(cell)) return false;
            if (!ConsumeMaterial()) return false;

            AddEnriched(cell);
            Vector3 at = grid.CellCenterWorld(cell);
            AnimalFarm.UI.FloatingText.Show(at + Vector3.up * 0.3f, "soil enriched", CropQuality.GleamingColor);
            Puffs.Burst(at, new Color(0.45f, 0.32f, 0.18f, 0.9f), 6, 1.0f);
            return true;
        }

        /// <summary>Works compost into a growing crop's soil (growth + quality boost).</summary>
        public bool TryApplyToPlant(Plant plant)
        {
            if (plant == null || !plant.CanCompost) return false;
            if (!ConsumeMaterial()) return false;

            plant.ApplyCompost();
            Vector3 at = plant.transform.position;
            AnimalFarm.UI.FloatingText.Show(at + Vector3.up * 0.7f, "composted", CropQuality.GleamingColor);
            Puffs.Burst(at, new Color(0.45f, 0.32f, 0.18f, 0.9f), 6, 1.0f);
            return true;
        }

        /// <summary>PlantManager calls this when a seed lands: true (and the cell is
        /// spent) if the soil was enriched.</summary>
        public bool TakeEnrichment(Vector2Int cell)
        {
            if (!_enriched.TryGetValue(cell, out var overlay)) return false;
            _enriched.Remove(cell);
            if (overlay != null) Destroy(overlay);
            return true;
        }

        private void HandleSurfaceChanged(Vector2Int cell, Surface surface)
        {
            if (!IsSoil(surface)) TakeEnrichment(cell);
        }

        /// <summary>Ground compost can be worked into: tilled Dirt or rich Mud.</summary>
        private static bool IsSoil(Surface s) => s == Surface.Dirt || s == Surface.Mud;

        // ---- overlay ----------------------------------------------------------

        private void AddEnriched(Vector2Int cell)
        {
            var grid = TerrainGrid.Instance;
            if (grid == null) return;

            var go = new GameObject("RichSoil");
            go.transform.SetParent(transform, false);
            go.transform.position = grid.CellCenterWorld(cell);
            go.transform.localScale = new Vector3(0.86f, 0.86f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.color = SoilRich;
            sr.sortingOrder = -900; // above the terrain tilemap (-1000), below plants
            if (_overlayMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null) _overlayMat = new Material(shader);
            }
            if (_overlayMat != null) sr.sharedMaterial = _overlayMat;

            _enriched[cell] = go;
        }

        private static Sprite _whiteSprite;

        internal static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    // 4 pixels per unit -> the 4x4 texture is exactly 1x1 world units.
                    _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                return _whiteSprite;
            }
        }

        // ---- leavings ---------------------------------------------------------

        private void Update()
        {
            _timer += Time.deltaTime; // scaled: no leavings while paused
            if (_timer >= TickSeconds)
            {
                _timer = 0f;
                DropLeavings();
            }

            _auditTimer += Time.deltaTime;
            if (_auditTimer >= AuditSeconds)
            {
                _auditTimer = 0f;
                AuditEnriched();
            }
        }

        /// <summary>Drops enrichment whose soil is no longer tilled (e.g. a restored save
        /// that landed before the terrain did).</summary>
        private void AuditEnriched()
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || _enriched.Count == 0) return;

            _scratch.Clear();
            foreach (var pair in _enriched)
                if (!IsSoil(grid.GetSurface(pair.Key))) _scratch.Add(pair.Key);
            for (int i = 0; i < _scratch.Count; i++) TakeEnrichment(_scratch[i]);
        }

        private void DropLeavings()
        {
            _live.RemoveAll(p => p == null);

            var manager = SpiritManager.Instance;
            if (manager == null) return;

            var spirits = manager.AllSpirits;
            if (spirits == null) return;

            for (int i = 0; i < spirits.Count; i++)
            {
                if (_live.Count >= MaxLiveLeavings) return;

                var agent = spirits[i];
                if (agent == null || agent.State != SpiritState.Resident) continue;
                if (agent.Spirit < SpiritThreshold) continue;
                if (Random.value >= DropChance) continue;

                SpawnLeaving(agent.transform.position + (Vector3)(Random.insideUnitCircle * ScatterRadius));
            }
        }

        /// <summary>Spawns one leaving at a world position (also the console hook).</summary>
        public void SpawnLeaving(Vector3 pos)
        {
            var go = new GameObject("CompostLeaving");
            go.transform.position = new Vector3(pos.x, pos.y, 0f);

            var leaving = go.AddComponent<CompostLeaving>();
            leaving.Init(WhiteSprite, _overlayMat);
            _live.Add(leaving);
        }

        // ---- ISaveable --------------------------------------------------------

        [Serializable]
        private struct CompostState
        {
            public int[] xs;
            public int[] ys;
        }

        public string SaveKey => "compost";

        public string Capture()
        {
            var xs = new int[_enriched.Count];
            var ys = new int[_enriched.Count];
            int i = 0;
            foreach (var pair in _enriched)
            {
                xs[i] = pair.Key.x;
                ys[i] = pair.Key.y;
                i++;
            }
            return JsonUtility.ToJson(new CompostState { xs = xs, ys = ys });
        }

        public void Restore(string json)
        {
            foreach (var pair in _enriched)
                if (pair.Value != null) Destroy(pair.Value);
            _enriched.Clear();

            if (string.IsNullOrEmpty(json)) return;
            var state = JsonUtility.FromJson<CompostState>(json);
            if (state.xs == null || state.ys == null) return;

            int n = Mathf.Min(state.xs.Length, state.ys.Length);
            for (int i = 0; i < n; i++)
            {
                var cell = new Vector2Int(state.xs[i], state.ys[i]);
                if (!_enriched.ContainsKey(cell)) AddEnriched(cell);
            }
        }
    }

    /// <summary>
    /// One essence-rich leaving. Bobs gently; collected when the SHEPHERD (tag
    /// "Player") walks over its trigger: +1 compost. Persists until collected
    /// (the manager's world cap handles litter). Not saved - see CompostManager.
    /// </summary>
    public class CompostLeaving : MonoBehaviour
    {
        private const float TriggerRadius = 0.45f;
        private const float BaseScale = 0.32f;
        private const float BobAmplitude = 0.05f;
        private const float BobFrequency = 0.7f; // Hz

        private static readonly Color LeavingColor = new Color(0.62f, 0.50f, 0.22f);

        private Vector3 _basePos;
        private float _phase;
        private bool _collected;

        public void Init(Sprite sprite, Material material)
        {
            _basePos = transform.position;
            _phase = Random.value * 10f;

            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = LeavingColor;
            sr.sortingOrder = 3;
            if (material != null) sr.sharedMaterial = material;

            transform.localScale = Vector3.one * BaseScale;

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = TriggerRadius;
        }

        private void Update()
        {
            float y = Mathf.Sin((Time.time + _phase) * BobFrequency * 2f * Mathf.PI) * BobAmplitude;
            transform.position = _basePos + new Vector3(0f, y, 0f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_collected || other == null || !other.CompareTag("Player")) return;
            _collected = true;

            if (Inventory.Instance != null) Inventory.Instance.Add(CompostManager.CompostId, 1);
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.5f, "+1 compost", LeavingColor);

            Destroy(gameObject);
        }
    }
}

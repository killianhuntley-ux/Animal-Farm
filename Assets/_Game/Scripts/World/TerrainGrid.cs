using System;
using System.Collections.Generic;
using AnimalFarm.Core.Saving;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

namespace AnimalFarm.World
{
    /// <summary>Surface type of one terrain cell. Byte-sized for cheap saves.</summary>
    public enum Surface : byte
    {
        Scrub = 0, // untended wild ground (the "barren" of barren->lush)
        Dirt = 1,  // tilled, plantable
        Grass = 2, // sown, lush
        Water = 3, // dug pond; deep cells are impassable, the shallow RIM (auto-derived, not a surface) is wadeable
        Sand = 4,  // arid ground (desert biomes; bought by the load from vendors later)
        Mud = 5    // rich swamp soil: plantable like Dirt, ALWAYS wet, counts toward Swamp (APPENDED: saves store this byte)
    }

    /// <summary>
    /// Authoritative land-state model. Muscle 02/08 world restructure: the grid
    /// now covers the HOME CLUSTER (3x3 parcels; the centre one owned at game
    /// start), the unbuyable scrub BUFFER around it, the west ROAD corridor,
    /// and the SWAMP satellite cluster (2x2) at the road's far end. Cells carry
    /// a USABLE mask -- locked cells refuse tools and sit outside every
    /// percentage/border query -- plus a render-only ZONE byte (buffer /
    /// cluster / road). ParcelManager flips parcel rects usable on purchase.
    ///
    /// The Tilemap stays a pure view: locked cluster cells draw their real
    /// ground dimmed (you can see the land you are buying), buffer draws
    /// dressed scrub, roads draw packed earth, water cells that touch land
    /// draw a pale SHALLOW rim, and every surface boundary gets a soft
    /// colour-lerp edge blend (SetColor, no extra sprites).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class TerrainGrid : MonoBehaviour, ISaveable
    {
        public static TerrainGrid Instance { get; private set; }

        [Header("Dimensions (1x1 cells; world origin = bottom-left corner)")]
        [SerializeField] private int width = 78;
        [SerializeField] private int height = 40;
        [SerializeField] private float worldOriginX = -56f;
        [SerializeField] private float worldOriginY = -20f;
        [Tooltip("Cell-rect usable at game start -- the home cluster's centre parcel.")]
        [SerializeField] private int initialUsableX = 51;
        [SerializeField] private int initialUsableY = 16;
        [SerializeField] private int initialUsableW = 10;
        [SerializeField] private int initialUsableH = 8;

        [Header("Zoning (cell rects; assigned by the bootstrapper)")]
        [Tooltip("Purchasable parcel clusters. Locked cells inside draw their ground dimmed.")]
        [SerializeField] private RectInt[] clusterRects;
        [Tooltip("Walkable dressed road corridors. Never usable; drawn as packed earth.")]
        [SerializeField] private RectInt[] roadRects;
        [Tooltip("Murky-ground tint region (the swamp satellite cluster).")]
        [SerializeField] private RectInt swampRect;
        [Tooltip("Starter pools stamped into a FRESH world (saved worlds keep their own cells).")]
        [SerializeField] private RectInt[] seedWaterRects;

        private const float WateredHours = 24f; // one game-day per soaking
        public const int SurfaceTypeCount = 6; // one slot per Surface value (census arrays size off this)
        private static readonly Color WateredTint = new Color(0.70f, 0.68f, 0.88f);

        // Approximate on-screen colour of each surface's placeholder sprite --
        // the basis for edge blending and the flat (sprite-less) tiles.
        private static readonly Color[] SurfaceBaseColor =
        {
            new Color(0.38f, 0.37f, 0.26f), // Scrub
            new Color(0.42f, 0.31f, 0.22f), // Dirt
            new Color(0.30f, 0.50f, 0.26f), // Grass
            new Color(0.22f, 0.38f, 0.55f), // Water
            new Color(0.80f, 0.72f, 0.48f), // Sand (flat runtime tile)
            new Color(0.31f, 0.23f, 0.16f)  // Mud (flat runtime tile, speckled grey detail)
        };
        private static readonly Color ShallowWaterColor = new Color(0.40f, 0.58f, 0.66f); // sun-lit rim
        private static readonly Color RoadColor = new Color(0.39f, 0.30f, 0.21f);         // packed earth (kept under the dirt sprite base -- multiply tints cannot brighten)
        private static readonly Color BufferTint = new Color(0.88f, 0.88f, 0.84f);        // dressed scrub, faded
        private static readonly Color LockedDim = new Color(0.55f, 0.55f, 0.55f);         // unpurchased cluster land
        private static readonly Color MurkTint = new Color(0.80f, 0.84f, 0.74f);          // swamp ground cast
        private const float EdgeBlend = 0.25f; // how far a cell's colour leans into differing neighbours
        private const float FloodBlend = 0.7f; // how far a flooded cell reads toward shallow-water colour

        // zones (render-only; gameplay reads the usable mask)
        private const byte ZoneBuffer = 0, ZoneCluster = 1, ZoneRoad = 2;

        // Bounded grass spread (muscle 02): sown grass occasionally claims one
        // wild SCRUB cell adjacent to the patch, but only within SpreadRadius
        // of a PLAYER-sown cell. Spread cells never become anchors themselves,
        // so a patch can only round itself out -- never march across the map.
        // Tilled (Dirt), watered, planted, and non-scrub ground is never taken.
        private const float SpreadIntervalHours = 3f; // game-hours between conversions
        private const int SpreadRadius = 2;           // Chebyshev cells from a sown anchor

        [Header("View")]
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private TileBase scrubTile;
        [SerializeField] private TileBase dirtTile;
        [SerializeField] private TileBase grassTile;
        [SerializeField] private TileBase waterTile;
        [Tooltip("Optional. When null, Sand renders as a runtime flat-colour tile.")]
        [SerializeField] private TileBase sandTile;

        public int Width => width;
        public int Height => height;

        /// <summary>Fired after a cell's surface actually changes.</summary>
        public event Action<Vector2Int, Surface> OnSurfaceChanged;

        /// <summary>Fired after a region's usable mask changes (parcel unlock).</summary>
        public event Action OnUsableChanged;

        private byte[] _cells;              // index = x + y * width
        private bool[] _usable;
        private byte[] _zone;               // ZoneBuffer / ZoneCluster / ZoneRoad
        private bool[] _sownAnchor;         // player-sown grass cells (spread anchors)
        private float[] _wateredUntil;      // TotalHours at which the soaking dries; 0 = dry
        private float _nextDryScan;
        private float _nextSpreadHours = -1f;
        private int _usableCount;
        private readonly int[] _counts = new int[SurfaceTypeCount]; // usable cells only
        private readonly List<Vector2Int> _borderCells = new List<Vector2Int>();
        private bool _borderDirty = true;
        private Tile _flatMudTile;          // runtime speckled mud tile (grey detail x SetColor)
        private Transform _playerTf;        // cached for the deep-water nudge (rare path)
        private Rigidbody2D _playerRb;
        private Tile _flatSandTile;         // runtime white tiles coloured via SetColor --
        private Tile _flatShallowTile;      // multiply-tints can't BRIGHTEN sprite tiles
        private Tile _flatFloodTile;        // temporary rain-flood shallows (walkable, view-only)
        private bool[] _flooded;            // transient (NOT saved): PondFlood owns the on/off state
        private bool _floodActive;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            int n = width * height;
            _cells = new byte[n];
            _usable = new bool[n];
            _zone = new byte[n];
            _sownAnchor = new bool[n];
            _flooded = new bool[n];
            _wateredUntil = new float[n];

            StampZones();
            SeedStartingWater();

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                _usable[x + y * width] =
                    x >= initialUsableX && x < initialUsableX + initialUsableW &&
                    y >= initialUsableY && y < initialUsableY + initialUsableH;

            _flatSandTile = MakeFlatTile(Tile.ColliderType.None, "FlatSand");
            _flatShallowTile = MakeFlatTile(Tile.ColliderType.None, "FlatShallow"); // the rim is WADEABLE (deep Tile_Water keeps its Grid collider)
            _flatMudTile = MakeMudTile();
            _flatFloodTile = MakeFlatTile(Tile.ColliderType.None, "FlatFlood");      // flood never blocks anyone

            RecountAll();
            RepaintAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Marks cluster/road cells so the view can tell buyable land
        /// and dressed corridors from the plain buffer scrub.</summary>
        private void StampZones()
        {
            void Stamp(RectInt[] rects, byte zone)
            {
                if (rects == null) return;
                for (int i = 0; i < rects.Length; i++)
                {
                    var r = rects[i];
                    for (int y = r.yMin; y < r.yMax; y++)
                    for (int x = r.xMin; x < r.xMax; x++)
                        if (x >= 0 && x < width && y >= 0 && y < height)
                            _zone[x + y * width] = zone;
                }
            }
            Stamp(clusterRects, ZoneCluster);
            Stamp(roadRects, ZoneRoad);
        }

        /// <summary>Starter pools (the swamp's standing water). Runs before any
        /// Restore -- a loaded save replaces the cell array wholesale anyway.</summary>
        private void SeedStartingWater()
        {
            if (seedWaterRects == null) return;
            for (int i = 0; i < seedWaterRects.Length; i++)
            {
                var r = seedWaterRects[i];
                for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (x >= 0 && x < width && y >= 0 && y < height)
                        _cells[x + y * width] = (byte)Surface.Water;
            }
        }

        private static Tile MakeFlatTile(Tile.ColliderType collider, string tileName)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255)
            });
            tex.Apply();

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = tileName;
            tile.sprite = UnityEngine.Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f), 2f); // 2 px at 2 ppu = exactly one cell
            tile.colliderType = collider;
            tile.color = Color.white;
            return tile;
        }

        /// <summary>Rich-mud tile (runtime art): a 16x16 grey speckle (dark clumps,
        /// a few wet glints) that SetColor multiplies into the mud brown. Seeded,
        /// so every run draws the same ground. Never blocks walking.</summary>
        private static Tile MakeMudTile()
        {
            const int N = 16;
            var rng = new System.Random(4242);
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                float v = 0.86f + (float)(rng.NextDouble() - 0.5) * 0.12f;
                double roll = rng.NextDouble();
                if (roll < 0.07) v -= 0.16f;      // dark clump
                else if (roll > 0.975) v += 0.14f; // wet glint
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                px[i] = new Color32(b, b, b, 255);
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(px);
            tex.Apply();

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "FlatMud";
            tile.sprite = UnityEngine.Sprite.Create(tex, new Rect(0f, 0f, N, N),
                new Vector2(0.5f, 0.5f), N); // 16 px at 16 ppu = exactly one cell
            tile.colliderType = Tile.ColliderType.None;
            tile.color = Color.white;
            return tile;
        }

        // ---- mapping --------------------------------------------------------

        public bool InBounds(Vector2Int cell) =>
            cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;

        public bool TryWorldToCell(Vector3 world, out Vector2Int cell)
        {
            int x = Mathf.FloorToInt(world.x - worldOriginX);
            int y = Mathf.FloorToInt(world.y - worldOriginY);
            cell = new Vector2Int(x, y);
            return InBounds(cell);
        }

        public Vector3 CellCenterWorld(Vector2Int cell) =>
            new Vector3(worldOriginX + cell.x + 0.5f, worldOriginY + cell.y + 0.5f, 0f);

        // ---- queries --------------------------------------------------------

        public bool IsUsable(Vector2Int cell) =>
            InBounds(cell) && _usable[cell.x + cell.y * width];

        public bool IsWorldUsable(Vector3 world) =>
            TryWorldToCell(world, out var cell) && IsUsable(cell);

        public Surface GetSurface(Vector2Int cell) =>
            IsUsable(cell) ? (Surface)_cells[cell.x + cell.y * width] : Surface.Scrub;

        /// <summary>True for a Water cell that touches non-water ground -- the
        /// auto-derived shallow rim (render state; the cell is still Water).</summary>
        public bool IsShallowRim(Vector2Int cell) =>
            InBounds(cell) && IsShallowRimRaw(cell.x, cell.y);

        /// <summary>True for a Water cell with NO land neighbour: the solid centre
        /// of a pond (its tile keeps a collider; the shallow rim does not).</summary>
        public bool IsDeepWater(Vector2Int cell) =>
            InBounds(cell) && IsDeepWaterRaw(cell.x, cell.y);

        /// <summary>True when a world point sits over a wadeable shallow-rim cell
        /// (the shepherd slows here; footsteps slosh and splash).</summary>
        public bool IsWadingAt(Vector3 world) =>
            TryWorldToCell(world, out var cell) && IsShallowRimRaw(cell.x, cell.y);

        /// <summary>Percentage (0..100) of USABLE land held by a surface type.
        /// NOTE: unlocking a parcel dilutes percentages -- bigger land is harder
        /// to keep lush. Flagged as a tuning question for the owner.</summary>
        public float SurfacePercent(Surface s) =>
            _usableCount == 0 ? 0f : _counts[(int)s] * 100f / _usableCount;

        // ---- mutation -------------------------------------------------------

        /// <summary>Sets a cell's surface. False if locked, out of bounds, or unchanged.</summary>
        public bool SetSurface(Vector2Int cell, Surface s) =>
            SetSurfaceInternal(cell, s, sownByHand: s == Surface.Grass);

        /// <summary>Shared path for tools (anchor-marking) and grass spread (not).</summary>
        private bool SetSurfaceInternal(Vector2Int cell, Surface s, bool sownByHand)
        {
            if (!IsUsable(cell)) return false;

            int idx = cell.x + cell.y * width;
            var old = (Surface)_cells[idx];
            if (old == s) return false;

            _cells[idx] = (byte)s;
            _counts[(int)old]--;
            _counts[(int)s]++;
            _wateredUntil[idx] = 0f; // changing the ground dries it
            _sownAnchor[idx] = s == Surface.Grass && sownByHand;

            RepaintAround(cell); // neighbours re-blend; rims may flip
            if (_floodActive) RecomputeFlood(); // the pond edge moved: so does the flood
            if (s == Surface.Water || old == Surface.Water) NudgeShepherdFromDeepWater(); // a rim cell may have just gone deep
            OnSurfaceChanged?.Invoke(cell, s);
            return true;
        }

        /// <summary>
        /// The rim is walkable but deep water is solid. When a surface change turns
        /// the cell UNDER the shepherd deep (e.g. the last land beside it was
        /// dug), move them to the nearest walkable cell so nobody is entombed.
        /// Rare path: the player lookup is cached and runs only on water edits.
        /// </summary>
        private void NudgeShepherdFromDeepWater()
        {
            if (_playerTf == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p == null) return;
                _playerTf = p.transform;
                _playerRb = p.GetComponent<Rigidbody2D>();
            }

            Vector3 pos = _playerRb != null ? (Vector3)_playerRb.position : _playerTf.position;
            if (!TryWorldToCell(pos, out var here) || !IsDeepWaterRaw(here.x, here.y)) return;

            bool found = false;
            Vector2Int best = here;
            float bestSqr = float.MaxValue;
            for (int r = 1; r <= 8 && !found; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue; // this ring only
                    int nx = here.x + dx, ny = here.y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int idx = nx + ny * width;
                    if (IsDeepWaterRaw(nx, ny)) continue;
                    if (!_usable[idx] && _zone[idx] == ZoneCluster) continue; // never into a locked parcel
                    float sqr = ((Vector2)CellCenterWorld(new Vector2Int(nx, ny)) - (Vector2)pos).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = new Vector2Int(nx, ny); found = true; }
                }
            }
            if (!found) return;

            Vector3 to = CellCenterWorld(best);
            if (_playerRb != null) { _playerRb.position = to; _playerRb.linearVelocity = Vector2.zero; }
            _playerTf.position = to;
            AnimalFarm.Core.Puffs.Burst(to, new Color(0.55f, 0.72f, 0.82f, 0.9f), 6, 1.2f);
        }

        // ---- rain flood (muscle 02: ponds swell a little, then recede) -------
        // View + soak only: flooded cells are Scrub/Grass/Sand ground within one
        // cell of a pond, drawn as shallows. The surface census, plants, homes and
        // walking are untouched -- nothing is ever lost to a flood.

        public bool FloodActive => _floodActive;

        public bool IsFlooded(Vector2Int cell) =>
            InBounds(cell) && _flooded[cell.x + cell.y * width];

        /// <summary>Raises or recedes the pond-edge flood (PondFlood drives this).</summary>
        public void SetFloodActive(bool on)
        {
            if (_floodActive == on) return;
            _floodActive = on;
            RecomputeFlood();
        }

        private void RecomputeFlood()
        {
            var changed = new List<Vector2Int>();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = x + y * width;
                bool want = _floodActive && FloodEligible(x, y);
                if (_flooded[idx] == want) continue;
                _flooded[idx] = want;
                changed.Add(new Vector2Int(x, y));
            }
            for (int i = 0; i < changed.Count; i++) RepaintAround(changed[i]);
        }

        private bool FloodEligible(int x, int y)
        {
            int idx = x + y * width;
            if (!_usable[idx] || _zone[idx] == ZoneRoad) return false;
            var s = (Surface)_cells[idx];
            if (s == Surface.Water || s == Surface.Dirt || s == Surface.Mud) return false; // tilled soil is just soaked, never drowned
            return HasWaterWithinOne(x, y);
        }

        private bool HasWaterWithinOne(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if ((Surface)_cells[nx + ny * width] == Surface.Water) return true;
            }
            return false;
        }

        /// <summary>Waterlogged pond edges: soaks every usable Dirt cell touching a
        /// pond (the flood's free watering; SetWatered refreshes a day's soak).</summary>
        public void SoakWaterEdges()
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = x + y * width;
                if (!_usable[idx] || (Surface)_cells[idx] != Surface.Dirt) continue;
                if (HasWaterWithinOne(x, y)) SetWatered(new Vector2Int(x, y));
            }
        }

        // ---- watering (Water Pail; plants grow at full speed on wet soil) ----

        /// <summary>Waters a usable Dirt cell for ~one game-day. False otherwise.</summary>
        public bool SetWatered(Vector2Int cell)
        {
            if (!IsUsable(cell) || GetSurface(cell) != Surface.Dirt) return false;
            float now = AnimalFarm.Core.GameClock.Instance != null
                ? AnimalFarm.Core.GameClock.Instance.TotalHours : 0f;
            bool wasWet = IsWatered(cell); // rain re-soaks whole fields: skip the repaint when nothing reads differently
            _wateredUntil[cell.x + cell.y * width] = now + WateredHours;
            if (!wasWet) RepaintAround(cell);
            return true;
        }

        public bool IsWatered(Vector2Int cell)
        {
            if (!InBounds(cell)) return false;
            if ((Surface)_cells[cell.x + cell.y * width] == Surface.Mud) return true; // rich mud is ALWAYS wet
            float until = _wateredUntil[cell.x + cell.y * width];
            if (until <= 0f) return false;
            float now = AnimalFarm.Core.GameClock.Instance != null
                ? AnimalFarm.Core.GameClock.Instance.TotalHours : 0f;
            return now < until;
        }

        private void Update()
        {
            // Dry-out repaint sweep (visual only; IsWatered is already time-true)
            // plus the grass-spread timer -- both on the same lazy 5 s cadence.
            if (Time.time < _nextDryScan) return;
            _nextDryScan = Time.time + 5f;

            float now = AnimalFarm.Core.GameClock.Instance != null
                ? AnimalFarm.Core.GameClock.Instance.TotalHours : 0f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = x + y * width;
                if (_wateredUntil[idx] > 0f && now >= _wateredUntil[idx])
                {
                    _wateredUntil[idx] = 0f;
                    RepaintAround(new Vector2Int(x, y));
                }
            }

            TickGrassSpread(now);
        }

        /// <summary>Unlocks (or relocks) a rect of cells -- parcel purchases.</summary>
        public void SetUsable(RectInt cellRect, bool usable)
        {
            bool changed = false;
            for (int y = cellRect.yMin; y < cellRect.yMax; y++)
            for (int x = cellRect.xMin; x < cellRect.xMax; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!InBounds(cell)) continue;
                int idx = x + y * width;
                if (_usable[idx] == usable) continue;
                _usable[idx] = usable;
                changed = true;
            }

            if (!changed) return;
            RecountAll();
            _borderDirty = true;
            RepaintAll(); // dim lifts off the whole parcel + edges re-blend
            OnUsableChanged?.Invoke();
        }

        // ---- bounded grass spread --------------------------------------------

        private void TickGrassSpread(float nowHours)
        {
            if (_nextSpreadHours <= 0f) { _nextSpreadHours = nowHours + SpreadIntervalHours; return; }
            if (nowHours < _nextSpreadHours) return;
            _nextSpreadHours = nowHours + SpreadIntervalHours * Random.Range(0.8f, 1.6f);

            // Reservoir-pick ONE eligible cell: usable wild scrub, 4-adjacent
            // to grass, within SpreadRadius of a player-sown anchor, no plant.
            var plants = PlantManager.Instance;
            Vector2Int pick = default;
            int found = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = x + y * width;
                if (!_usable[idx] || (Surface)_cells[idx] != Surface.Scrub) continue;
                if (!IsGrassAt(x - 1, y) && !IsGrassAt(x + 1, y) &&
                    !IsGrassAt(x, y - 1) && !IsGrassAt(x, y + 1)) continue;
                if (!NearSownAnchor(x, y)) continue;
                var cell = new Vector2Int(x, y);
                if (plants != null && plants.HasPlantAt(cell)) continue;
                found++;
                if (Random.Range(0, found) == 0) pick = cell; // reservoir sample
            }

            if (found > 0) SetSurfaceInternal(pick, Surface.Grass, sownByHand: false);
        }

        private bool IsGrassAt(int x, int y) =>
            x >= 0 && x < width && y >= 0 && y < height
            && _usable[x + y * width]
            && (Surface)_cells[x + y * width] == Surface.Grass;

        private bool NearSownAnchor(int x, int y)
        {
            for (int dy = -SpreadRadius; dy <= SpreadRadius; dy++)
            for (int dx = -SpreadRadius; dx <= SpreadRadius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if (_sownAnchor[nx + ny * width]) return true;
            }
            return false;
        }

        // ---- usable-region geometry (spirit borders & wandering) -------------
        // Border cell = usable cell with at least one non-usable/out-of-bounds
        // 4-neighbour. Exact even for L-shaped unlocked regions.

        private void RebuildBorderCache()
        {
            _borderDirty = false;
            _borderCells.Clear();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = x + y * width;
                if (!_usable[idx]) continue;
                bool edge =
                    !IsUsable(new Vector2Int(x - 1, y)) || !IsUsable(new Vector2Int(x + 1, y)) ||
                    !IsUsable(new Vector2Int(x, y - 1)) || !IsUsable(new Vector2Int(x, y + 1));
                if (edge) _borderCells.Add(new Vector2Int(x, y));
            }
        }

        private List<Vector2Int> BorderCells()
        {
            if (_borderDirty) RebuildBorderCache();
            return _borderCells;
        }

        /// <summary>Random cell-center on the usable region's edge ring.</summary>
        public Vector3 RandomUsableBorderPoint()
        {
            var border = BorderCells();
            if (border.Count == 0) return Vector3.zero;
            return CellCenterWorld(border[Random.Range(0, border.Count)]);
        }

        /// <summary>Nearest edge-ring cell-center to a world position.</summary>
        public Vector3 NearestUsableBorderPoint(Vector3 from)
        {
            var border = BorderCells();
            Vector3 best = Vector3.zero;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < border.Count; i++)
            {
                Vector3 p = CellCenterWorld(border[i]);
                float sqr = (p - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return border.Count > 0 ? best : from;
        }

        /// <summary>Edge-ring point a short drift away from 'from' (silhouette pacing).</summary>
        public Vector3 DriftAlongBorder(Vector3 from, float minStep, float maxStep)
        {
            var border = BorderCells();
            if (border.Count == 0) return from;

            // Collect border cells inside the distance band; fall back to nearest.
            Vector3 pick = Vector3.zero;
            int found = 0;
            float minSqr = minStep * minStep, maxSqr = maxStep * maxStep;
            for (int i = 0; i < border.Count; i++)
            {
                Vector3 p = CellCenterWorld(border[i]);
                float sqr = (p - from).sqrMagnitude;
                if (sqr < minSqr || sqr > maxSqr) continue;
                found++;
                if (Random.Range(0, found) == 0) pick = p; // reservoir sample
            }
            return found > 0 ? pick : NearestUsableBorderPoint(from);
        }

        /// <summary>Random usable cell-center (in-field wandering).</summary>
        public Vector3 RandomUsableInteriorPoint()
        {
            // Rejection-sample; the usable region is always a large fraction.
            for (int tries = 0; tries < 40; tries++)
            {
                int x = Random.Range(0, width);
                int y = Random.Range(0, height);
                if (_usable[x + y * width]) return CellCenterWorld(new Vector2Int(x, y));
            }
            var border = BorderCells();
            return border.Count > 0 ? CellCenterWorld(border[0]) : Vector3.zero;
        }

        /// <summary>
        /// Random usable cell-center within maxDist of a point. With two owned
        /// BASES (home + swamp) a global sample would send home spirits on a
        /// doomed march at the satellite's fence -- locality keeps each spirit
        /// wandering its own cluster. Falls back to the global sample when the
        /// neighbourhood holds no usable cells.
        /// </summary>
        public Vector3 RandomUsableInteriorPointNear(Vector3 from, float maxDist)
        {
            if (TryWorldToCell(from, out var center))
            {
                int r = Mathf.Max(1, Mathf.CeilToInt(maxDist));
                for (int tries = 0; tries < 30; tries++)
                {
                    int x = center.x + Random.Range(-r, r + 1);
                    int y = center.y + Random.Range(-r, r + 1);
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;
                    if (_usable[x + y * width]) return CellCenterWorld(new Vector2Int(x, y));
                }
            }
            return RandomUsableInteriorPoint();
        }

        // ---- view -----------------------------------------------------------

        private bool IsDeepWaterRaw(int x, int y) =>
            (Surface)_cells[x + y * width] == Surface.Water && !IsShallowRimRaw(x, y);

        private bool IsShallowRimRaw(int x, int y)
        {
            if ((Surface)_cells[x + y * width] != Surface.Water) return false;
            return IsLandAt(x - 1, y) || IsLandAt(x + 1, y) ||
                   IsLandAt(x, y - 1) || IsLandAt(x, y + 1);
        }

        private bool IsLandAt(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return false;
            return (Surface)_cells[x + y * width] != Surface.Water;
        }

        private bool InSwamp(int x, int y) =>
            x >= swampRect.xMin && x < swampRect.xMax &&
            y >= swampRect.yMin && y < swampRect.yMax;

        /// <summary>The cell's PRE-BLEND absolute colour: surface base, shallow
        /// rim, road earth, watered/murk casts, locked dim, buffer fade.</summary>
        private Color CellBaseColor(int x, int y)
        {
            int idx = x + y * width;
            var s = (Surface)_cells[idx];

            Color abs;
            if (_zone[idx] == ZoneRoad && s != Surface.Water) abs = RoadColor;
            else if (s == Surface.Water && IsShallowRimRaw(x, y)) abs = ShallowWaterColor;
            else if (_flooded[idx]) abs = Color.Lerp(SurfaceBaseColor[(int)s], ShallowWaterColor, FloodBlend);
            else abs = SurfaceBaseColor[(int)s];

            if (_usable[idx])
            {
                if (s == Surface.Dirt && IsWatered(new Vector2Int(x, y))) abs *= WateredTint;
            }
            else if (_zone[idx] == ZoneCluster) abs *= LockedDim;
            else if (_zone[idx] == ZoneBuffer) abs *= BufferTint;

            if (InSwamp(x, y)) abs *= MurkTint;
            abs.a = 1f;
            return abs;
        }

        /// <summary>Base colour leaned toward differing 4-neighbours -- the
        /// cheap autotile edge blend (grass laps into dirt, banks soften).</summary>
        private Color BlendedCellColor(int x, int y)
        {
            Color own = CellBaseColor(x, y);
            Color sum = Color.clear;
            int n = 0;
            AccumulateIfDiffers(x - 1, y, own, ref sum, ref n);
            AccumulateIfDiffers(x + 1, y, own, ref sum, ref n);
            AccumulateIfDiffers(x, y - 1, own, ref sum, ref n);
            AccumulateIfDiffers(x, y + 1, own, ref sum, ref n);
            if (n == 0) return own;

            Color avg = sum / n;
            var blended = Color.Lerp(own, avg, EdgeBlend);
            blended.a = 1f;
            return blended;
        }

        private void AccumulateIfDiffers(int x, int y, Color own, ref Color sum, ref int n)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            Color c = CellBaseColor(x, y);
            float dr = c.r - own.r, dg = c.g - own.g, db = c.b - own.b;
            if (dr * dr + dg * dg + db * db < 0.0009f) return; // same-looking ground: no pull
            sum += c;
            n++;
        }

        /// <summary>Picks the drawn tile. Flat runtime tiles (sand, shallow rim)
        /// take their colour verbatim; sprite tiles get a multiply tint of
        /// blended-colour over the sprite's known base colour.</summary>
        private void SelectTile(int x, int y, out TileBase tile, out Color spriteBase, out bool flat)
        {
            int idx = x + y * width;
            var s = (Surface)_cells[idx];

            if (_zone[idx] == ZoneRoad && s != Surface.Water)
            {
                tile = dirtTile;
                spriteBase = SurfaceBaseColor[(int)Surface.Dirt];
                flat = false;
                return;
            }
            if (s == Surface.Water && IsShallowRimRaw(x, y))
            {
                tile = _flatShallowTile;
                spriteBase = Color.white;
                flat = true;
                return;
            }
            if (_flooded[idx])
            {
                tile = _flatFloodTile;
                spriteBase = Color.white;
                flat = true;
                return;
            }
            if (s == Surface.Sand && sandTile == null)
            {
                tile = _flatSandTile;
                spriteBase = Color.white;
                flat = true;
                return;
            }

            if (s == Surface.Mud)
            {
                tile = _flatMudTile;
                spriteBase = Color.white; // flat: the blended colour is drawn verbatim over the grey speckle
                flat = true;
                return;
            }

            switch (s)
            {
                case Surface.Dirt: tile = dirtTile; break;
                case Surface.Grass: tile = grassTile; break;
                case Surface.Water: tile = waterTile; break;
                case Surface.Sand: tile = sandTile; break;
                default: tile = scrubTile; break;
            }
            spriteBase = SurfaceBaseColor[(int)s];
            flat = false;
        }

        private Vector3Int CellToTilePos(Vector2Int cell) =>
            new Vector3Int(cell.x + Mathf.RoundToInt(worldOriginX), cell.y + Mathf.RoundToInt(worldOriginY), 0);

        private void PaintCell(Vector2Int cell)
        {
            if (tilemap == null || _zone == null) return;

            SelectTile(cell.x, cell.y, out var tile, out var spriteBase, out bool flat);
            Color abs = BlendedCellColor(cell.x, cell.y);

            var pos = CellToTilePos(cell);
            tilemap.SetTile(pos, tile);
            tilemap.SetTileFlags(pos, TileFlags.None);
            if (flat)
            {
                tilemap.SetColor(pos, abs);
            }
            else
            {
                // tint = target colour / sprite base, clamped (tints can't brighten)
                tilemap.SetColor(pos, new Color(
                    Mathf.Clamp01(abs.r / Mathf.Max(spriteBase.r, 0.001f)),
                    Mathf.Clamp01(abs.g / Mathf.Max(spriteBase.g, 0.001f)),
                    Mathf.Clamp01(abs.b / Mathf.Max(spriteBase.b, 0.001f)),
                    1f));
            }
        }

        /// <summary>Repaints a 5x5 block: a changed cell shifts its neighbours'
        /// blends, and can flip a neighbouring water cell's rim state -- which
        /// in turn shifts THAT cell's neighbours.</summary>
        private void RepaintAround(Vector2Int cell)
        {
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var c = new Vector2Int(cell.x + dx, cell.y + dy);
                if (InBounds(c)) PaintCell(c);
            }
        }

        private void RepaintAll()
        {
            if (tilemap == null) return;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                PaintCell(new Vector2Int(x, y));
        }

        private void RecountAll()
        {
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
            _usableCount = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_usable[i]) continue;
                _usableCount++;
                _counts[Mathf.Clamp(_cells[i], 0, SurfaceTypeCount - 1)]++;
            }
        }

        // ---- ISaveable ------------------------------------------------------

        [Serializable]
        private struct TerrainState
        {
            public int width, height;
            public string cells;   // base64
            public string usable;  // base64 (1 byte per cell)
            public string watered; // base64 (4 bytes per cell, float dry-at hours)
            public string sown;    // base64 (1 byte per cell; grass-spread anchors)
            public float nextSpreadHours;
        }

        public string SaveKey => "terrain";

        public string Capture()
        {
            var usableBytes = new byte[_usable.Length];
            for (int i = 0; i < _usable.Length; i++) usableBytes[i] = _usable[i] ? (byte)1 : (byte)0;
            var sownBytes = new byte[_sownAnchor.Length];
            for (int i = 0; i < _sownAnchor.Length; i++) sownBytes[i] = _sownAnchor[i] ? (byte)1 : (byte)0;
            var wateredBytes = new byte[_wateredUntil.Length * 4];
            Buffer.BlockCopy(_wateredUntil, 0, wateredBytes, 0, wateredBytes.Length);
            return JsonUtility.ToJson(new TerrainState
            {
                width = width,
                height = height,
                cells = Convert.ToBase64String(_cells),
                usable = Convert.ToBase64String(usableBytes),
                watered = Convert.ToBase64String(wateredBytes),
                sown = Convert.ToBase64String(sownBytes),
                nextSpreadHours = _nextSpreadHours
            });
        }

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var state = JsonUtility.FromJson<TerrainState>(json);
            if (state.width != width || state.height != height || string.IsNullOrEmpty(state.cells))
            {
                Debug.LogWarning("[TerrainGrid] Save dimensions mismatch; keeping fresh field.");
                return;
            }

            byte[] cells;
            try { cells = Convert.FromBase64String(state.cells); }
            catch { Debug.LogWarning("[TerrainGrid] Corrupt terrain save; keeping fresh field."); return; }
            if (cells.Length != _cells.Length) return;
            _cells = cells;

            if (!string.IsNullOrEmpty(state.usable))
            {
                try
                {
                    var usableBytes = Convert.FromBase64String(state.usable);
                    if (usableBytes.Length == _usable.Length)
                        for (int i = 0; i < _usable.Length; i++) _usable[i] = usableBytes[i] != 0;
                }
                catch { /* keep initial mask */ }
            }

            if (!string.IsNullOrEmpty(state.watered))
            {
                try
                {
                    var wateredBytes = Convert.FromBase64String(state.watered);
                    if (wateredBytes.Length == _wateredUntil.Length * 4)
                        Buffer.BlockCopy(wateredBytes, 0, _wateredUntil, 0, wateredBytes.Length);
                }
                catch { /* dry is a fine default */ }
            }

            if (!string.IsNullOrEmpty(state.sown))
            {
                try
                {
                    var sownBytes = Convert.FromBase64String(state.sown);
                    if (sownBytes.Length == _sownAnchor.Length)
                        for (int i = 0; i < _sownAnchor.Length; i++) _sownAnchor[i] = sownBytes[i] != 0;
                }
                catch { /* no anchors = no spread; harmless */ }
            }
            _nextSpreadHours = state.nextSpreadHours;

            RecountAll();
            _borderDirty = true;
            if (_floodActive) RecomputeFlood(); // mask follows the restored cells
            RepaintAll();
            OnUsableChanged?.Invoke();
        }
    }
}

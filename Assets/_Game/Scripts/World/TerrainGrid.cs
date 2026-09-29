using System;
using AnimalFarm.Core.Saving;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AnimalFarm.World
{
    /// <summary>Surface type of one terrain cell. Byte-sized for cheap saves.</summary>
    public enum Surface : byte
    {
        Scrub = 0, // untended wild ground (the "barren" of barren→lush)
        Dirt = 1,  // tilled, plantable
        Grass = 2, // sown, lush
        Water = 3  // dug pond; impassable
    }

    /// <summary>
    /// Authoritative land-state model (slice 02). The Tilemap is only the view;
    /// every query (surface %, cell lookups) goes through this grid. The
    /// requirement engine and tools read/write cells here, never the Tilemap.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class TerrainGrid : MonoBehaviour, ISaveable
    {
        public static TerrainGrid Instance { get; private set; }

        [Header("Dimensions (cells are 1x1 world units, origin centered)")]
        [SerializeField] private int width = 80;
        [SerializeField] private int height = 50;

        [Header("View")]
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private TileBase scrubTile;
        [SerializeField] private TileBase dirtTile;
        [SerializeField] private TileBase grassTile;
        [SerializeField] private TileBase waterTile;

        public int Width => width;
        public int Height => height;

        /// <summary>Fired after a cell's surface actually changes.</summary>
        public event Action<Vector2Int, Surface> OnSurfaceChanged;

        private byte[] _cells;          // index = x + y * width
        private readonly int[] _counts = new int[4];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _cells = new byte[width * height];
            _counts[(int)Surface.Scrub] = _cells.Length;
            RepaintAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- queries --------------------------------------------------------

        public bool InBounds(Vector2Int cell) =>
            cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;

        /// <summary>World position → cell. False if outside the field.</summary>
        public bool TryWorldToCell(Vector3 world, out Vector2Int cell)
        {
            int x = Mathf.FloorToInt(world.x + width * 0.5f);
            int y = Mathf.FloorToInt(world.y + height * 0.5f);
            cell = new Vector2Int(x, y);
            return InBounds(cell);
        }

        public Vector3 CellCenterWorld(Vector2Int cell) =>
            new Vector3(cell.x - width * 0.5f + 0.5f, cell.y - height * 0.5f + 0.5f, 0f);

        public Surface GetSurface(Vector2Int cell) =>
            InBounds(cell) ? (Surface)_cells[cell.x + cell.y * width] : Surface.Scrub;

        /// <summary>Whole-field percentage (0..100) held by a surface type.</summary>
        public float SurfacePercent(Surface s) =>
            _cells == null || _cells.Length == 0 ? 0f : _counts[(int)s] * 100f / _cells.Length;

        // ---- mutation -------------------------------------------------------

        /// <summary>Sets a cell's surface. Returns false if out of bounds or unchanged.</summary>
        public bool SetSurface(Vector2Int cell, Surface s)
        {
            if (!InBounds(cell)) return false;

            int idx = cell.x + cell.y * width;
            var old = (Surface)_cells[idx];
            if (old == s) return false;

            _cells[idx] = (byte)s;
            _counts[(int)old]--;
            _counts[(int)s]++;

            PaintCell(cell, s);
            OnSurfaceChanged?.Invoke(cell, s);
            return true;
        }

        // ---- view -----------------------------------------------------------

        private TileBase TileFor(Surface s) => s switch
        {
            Surface.Dirt => dirtTile,
            Surface.Grass => grassTile,
            Surface.Water => waterTile,
            _ => scrubTile
        };

        private Vector3Int CellToTilePos(Vector2Int cell) =>
            new Vector3Int(cell.x - width / 2, cell.y - height / 2, 0);

        private void PaintCell(Vector2Int cell, Surface s)
        {
            if (tilemap != null)
                tilemap.SetTile(CellToTilePos(cell), TileFor(s));
        }

        private void RepaintAll()
        {
            if (tilemap == null) return;

            var positions = new Vector3Int[_cells.Length];
            var tiles = new TileBase[_cells.Length];
            int i = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++, i++)
            {
                positions[i] = CellToTilePos(new Vector2Int(x, y));
                tiles[i] = TileFor((Surface)_cells[i]);
            }
            tilemap.SetTiles(positions, tiles);
        }

        // ---- ISaveable ------------------------------------------------------

        [Serializable]
        private struct TerrainState
        {
            public int width, height;
            public string cells; // base64
        }

        public string SaveKey => "terrain";

        public string Capture() => JsonUtility.ToJson(new TerrainState
        {
            width = width,
            height = height,
            cells = Convert.ToBase64String(_cells)
        });

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
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
            foreach (byte b in _cells) _counts[Mathf.Clamp(b, 0, 3)]++;
            RepaintAll();
        }
    }
}

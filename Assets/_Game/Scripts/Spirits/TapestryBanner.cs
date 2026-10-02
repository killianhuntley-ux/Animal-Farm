using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// A hung tapestry banner (muscle 06, verdict 4): the placeable memorial a
    /// weave leaves behind, woven from the two parents' thread colors. World
    /// presence only - the data (parents, colors, position, carried/hung)
    /// lives in the WeaveArchive record, which is what persists. Garden stones
    /// stay exclusive to Styx-crossed spirits; banners are for the woven.
    /// </summary>
    public class TapestryBanner : MonoBehaviour, IInteractable, ISelectable
    {
        private const float FocusScale = 1.06f;

        private static readonly Color BannerText = new Color(0.85f, 0.80f, 0.92f, 1f);
        private static readonly List<TapestryBanner> All = new List<TapestryBanner>();

        public int WeaveId { get; private set; }

        private WeaveArchive.WeaveRecord _record;
        private int _lineIndex;
        private Vector3 _baseScale = Vector3.one;

        /// <summary>Builds the world object for a record at a position (archive-owned).</summary>
        public static TapestryBanner Create(WeaveArchive.WeaveRecord record, Vector3 pos)
        {
            if (record == null) return null;

            var go = new GameObject("TapestryBanner_" + record.id);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TapestrySprites.ForRecord(record);
            sr.sortingOrder = 0;
            var mat = TapestrySprites.SharedMaterial();
            if (mat != null) sr.sharedMaterial = mat;

            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(0.95f, 1.4f);

            var banner = go.AddComponent<TapestryBanner>();
            banner._record = record;
            banner.WeaveId = record.id;
            WorldLabel.Attach(go, record.parentAName + " & " + record.parentBName, -0.95f);
            return banner;
        }

        /// <summary>True when a banner already hangs in this grid cell (one per cell).</summary>
        public static bool AnyAtCell(Vector2Int cell, TapestryBanner except = null)
        {
            var grid = AnimalFarm.World.TerrainGrid.Instance;
            if (grid == null) return false;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var b = All[i];
                if (b == null) { All.RemoveAt(i); continue; }
                if (b == except) continue;
                if (grid.TryWorldToCell(b.transform.position, out var c) && c == cell) return true;
            }
            return false;
        }

        private void Awake() => _baseScale = transform.localScale;
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        // ---- IInteractable ----------------------------------------------------

        public string PromptText => "Read the weave";

        public bool CanInteract(GameObject actor) => _record != null;

        public void Interact(GameObject actor) => ShowNextLine();

        public void SetFocused(bool focused) =>
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;

        private void ShowNextLine()
        {
            if (_record == null) return;
            string line;
            switch (_lineIndex % 4)
            {
                case 0: line = _record.parentAName + " the " + _record.parentASpeciesName; break;
                case 1: line = _record.parentBName + " the " + _record.parentBSpeciesName; break;
                case 2:
                    line = "Woven into " + (!string.IsNullOrEmpty(_record.childName)
                        ? _record.childName : _record.childSpeciesName); break;
                default: line = "Woven on Day " + _record.day + "."; break;
            }
            _lineIndex++;
            FloatingText.Show(transform.position + Vector3.up * 1.1f, line, BannerText);
        }

        // ---- ISelectable ------------------------------------------------------

        public string SelectableTitle => _record != null
            ? _record.parentAName + " & " + _record.parentBName : "Tapestry";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            // Keeps the menu open so repeated clicks cycle the lines.
            into.Add(new SelectAction("Read", ShowNextLine, false));
            into.Add(new SelectAction("Move banner", BeginMove));
            into.Add(new SelectAction("Take down", TakeDown));
        }

        private bool _takenDown;

        private void TakeDown()
        {
            if (_takenDown) return; // two clicks in one frame must not refund the banner twice
            _takenDown = true;
            if (WeaveArchive.Instance != null) WeaveArchive.Instance.TakeDownBanner(this);
            else Destroy(gameObject);
        }

        /// <summary>Free ghost placement (Headstone pattern): right-click leaves the banner where it is.</summary>
        public void BeginMove()
        {
            var controller = AnimalFarm.Player.SelectionController.Instance;
            var sr = GetComponent<SpriteRenderer>();
            if (controller == null || sr == null || sr.sprite == null) return;

            var self = this;
            controller.BeginPlaceBuilding(sr.sprite, 1f,
                cell => WeaveArchive.CanHangAt(cell, self),
                (cell, world) =>
                {
                    if (self == null) return;
                    self.transform.position = world;
                    Bleeps.Play(BleepKind.Build, 0.6f);
                });
        }
    }

    /// <summary>
    /// Procedural banner art: a 24x36 hanging cloth (rod, woven body in the two
    /// thread colors, fringe), generated in code like the other structures.
    /// The same generator draws the big cloth the weave rite hangs in the air.
    /// </summary>
    public static class TapestrySprites
    {
        private const int W = 24, H = 36;
        private const float Ppu = 24f; // 1.0 x 1.5 world units at scale 1

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        /// <summary>Forgets every cached banner sprite (a load re-keys record ids: stale art must not survive it).</summary>
        public static void ClearCache() => Cache.Clear();

        /// <summary>Cached sprite for a record (pivot centre).</summary>
        public static Sprite ForRecord(WeaveArchive.WeaveRecord r)
        {
            if (r == null) return null;
            if (Cache.TryGetValue(r.id, out var s) && s != null) return s;
            s = Make(r.colorA, r.colorB, r.id, false);
            Cache[r.id] = s;
            return s;
        }

        /// <summary>
        /// Builds a banner sprite. <paramref name="pivotTop"/> anchors it at the
        /// rod (the rite scales its Y from zero to unroll the cloth).
        /// </summary>
        public static Sprite Make(Color a, Color b, int pattern, bool pivotTop)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var px = new Color32[W * H];

            Color wood = new Color(0.36f, 0.26f, 0.19f, 1f);
            Color woodLight = new Color(0.52f, 0.40f, 0.28f, 1f);
            int pat = Mathf.Abs(pattern) % 3;

            // Cloth body: x 3..20, y 6..33.
            for (int y = 6; y <= 33; y++)
            {
                for (int x = 3; x <= 20; x++)
                {
                    Color c = ClothColor(a, b, x, y, pat);
                    float grain = 0.9f + 0.1f * ((x + y) & 1);
                    if (x == 3 || x == 20) grain *= 0.75f;
                    if (y >= 31) grain *= 0.85f; // shadow under the rod
                    px[x + y * W] = new Color(c.r * grain, c.g * grain, c.b * grain, 1f);
                }
            }

            // Fringe: alternating thread ends of the two colors.
            for (int x = 3; x <= 20; x++)
            {
                if ((x & 1) != 0) continue;
                int len = (x % 4 == 0) ? 4 : 3;
                Color fc = ((x / 2) & 1) == 0 ? a : b;
                for (int y = 5; y > 5 - len; y--)
                    px[x + y * W] = new Color(fc.r * 0.9f, fc.g * 0.9f, fc.b * 0.9f, 1f);
            }

            // Rod + knobs across the top.
            for (int x = 0; x < W; x++)
            {
                px[x + 34 * W] = wood;
                px[x + 35 * W] = wood;
            }
            px[0 + 34 * W] = woodLight; px[0 + 35 * W] = woodLight;
            px[(W - 1) + 34 * W] = woodLight; px[(W - 1) + 35 * W] = woodLight;

            tex.SetPixels32(px);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;

            var sprite = Sprite.Create(tex, new Rect(0, 0, W, H),
                new Vector2(0.5f, pivotTop ? 1f : 0.5f), Ppu);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Color ClothColor(Color a, Color b, int x, int y, int pat)
        {
            switch (pat)
            {
                case 0: // twill
                    return ((x + y / 2) % 4) < 2 ? a : b;
                case 1: // checks
                    return (((x / 3) + (y / 3)) & 1) == 0 ? a : b;
                default: // stripes with a diamond where the threads cross
                {
                    if (Mathf.Abs(x - 11.5f) + Mathf.Abs(y - 19.5f) < 6f)
                        return Color.Lerp(Color.Lerp(a, b, 0.5f), Color.white, 0.25f);
                    return ((x / 3) & 1) == 0 ? a : b;
                }
            }
        }

        /// <summary>The lit sprite material the headstones / spirits use, when one can be found.</summary>
        public static Material SharedMaterial()
        {
            var reg = HeadstoneRegistry.Instance;
            if (reg != null)
            {
                var stones = reg.All;
                for (int i = 0; i < stones.Count; i++)
                {
                    if (stones[i] == null) continue;
                    var sr = stones[i].GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sharedMaterial != null) return sr.sharedMaterial;
                }
            }
            var mgr = SpiritManager.Instance;
            if (mgr != null)
            {
                var agents = mgr.AllSpirits;
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i] != null && agents[i].Renderer != null && agents[i].Renderer.sharedMaterial != null)
                        return agents[i].Renderer.sharedMaterial;
            }
            return null;
        }
    }
}

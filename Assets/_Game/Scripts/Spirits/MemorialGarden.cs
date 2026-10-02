using System.Collections.Generic;
using System.Text;
using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The memorial garden (muscle 04, verdict 1: the place, the names, NO
    /// buffs - purely cosmetic). Slow ambient beautification around PLACED
    /// headstones: flowers creep in over game-days, stones that stand near
    /// each other bloom fuller, a soft glow wakes at night and pale wisps
    /// (moth-lights) drift among the clustered stones after dark.
    ///
    /// DERIVED, NOT SAVED: everything here is a pure function of each stone's
    /// position, its Placed flag/PlacedHours (already saved by
    /// HeadstoneRegistry) and the clock. Flower positions come from a stable
    /// hash of the stone's identity, so a reload paints the identical garden.
    /// Created at runtime by HeadstoneRegistry (no scene wiring). Visuals are
    /// runtime-painted placeholders; the remaining parts (sorting, tints) are
    /// children of each stone so they follow it when the player moves it.
    /// </summary>
    public class MemorialGarden : MonoBehaviour
    {
        public static MemorialGarden Instance { get; private set; }

        /// <summary>Game-days from placement to a stone's fullest bloom.</summary>
        public const float FullBloomDays = 8f;
        /// <summary>Other placed stones within this many units count as neighbours.</summary>
        public const float NeighbourRadius = 2.4f;

        private const int MaxNeighbours = 5;
        private const int MaxFlowers = 16;
        private const int MaxWisps = 4;
        private const float RefreshSeconds = 1.5f;

        private const int OrderGlow = -2;
        private const int OrderFlower = -1;
        private const int OrderWisp = 2;

        private static readonly Color GlowCool = new Color(0.72f, 0.86f, 1f, 1f);
        private static readonly Color WispWarm = new Color(1f, 0.93f, 0.65f, 1f);
        private static readonly Color WispCool = new Color(0.75f, 0.95f, 1f, 1f);

        private class Flower
        {
            public SpriteRenderer Renderer;
            public float Scale;   // current, eased
            public float Target;  // 0 until it has grown in
        }

        private class Wisp
        {
            public Transform Root;
            public SpriteRenderer Renderer;
            public float Phase, Speed, Radius, Lift, Blink;
        }

        private class StoneGarden
        {
            public Headstone Stone;
            public Transform Root;
            public SpriteRenderer Glow;
            public readonly List<Flower> Flowers = new List<Flower>();
            public readonly List<Wisp> Wisps = new List<Wisp>();
            public int Seed;
            public int Neighbours;
            public float Growth;
            public float GlowPhase;
        }

        private readonly Dictionary<Headstone, StoneGarden> _gardens =
            new Dictionary<Headstone, StoneGarden>();
        private readonly List<Headstone> _scratch = new List<Headstone>();
        private float _refreshTimer;

        // ---- lifecycle ---------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _refreshTimer = RefreshSeconds; // first refresh on the first Update
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= RefreshSeconds)
            {
                _refreshTimer = 0f;
                Refresh();
            }
            Animate(Time.deltaTime);
        }

        // ---- derived queries (shared with the journal + console) ----------------

        /// <summary>0..1 bloom of the ground around one stone; 0 until placed.</summary>
        public static float GrowthOf(Headstone stone)
        {
            if (stone == null || !stone.Placed) return 0f;
            var clock = GameClock.Instance;
            if (clock == null) return 0f;
            float ageDays = Mathf.Max(0f, (clock.TotalHours - stone.PlacedHours) / 24f);
            return Mathf.Clamp01(ageDays / FullBloomDays);
        }

        /// <summary>Other placed stones within <see cref="NeighbourRadius"/>.</summary>
        public static int NeighboursOf(Headstone stone)
        {
            var reg = HeadstoneRegistry.Instance;
            if (stone == null || reg == null) return 0;
            return CountNeighbours(stone, reg.All);
        }

        private static int CountNeighbours(Headstone stone, IReadOnlyList<Headstone> all)
        {
            if (stone == null || !stone.Placed) return 0;
            int n = 0;
            float r2 = NeighbourRadius * NeighbourRadius;
            Vector3 p = stone.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o == stone || !o.Placed) continue;
                if ((o.transform.position - p).sqrMagnitude <= r2) n++;
            }
            return Mathf.Min(n, MaxNeighbours);
        }

        /// <summary>Short journal phrase for the ground around a stone.</summary>
        public static string Describe(Headstone stone)
        {
            if (stone == null) return "";
            if (!stone.Placed) return "";
            float g = GrowthOf(stone);
            string word = g < 0.15f ? "bare earth, just beginning to stir"
                : g < 0.5f ? "the first flowers are creeping in"
                : g < 0.9f ? "flowering"
                : "in full bloom";
            int n = NeighboursOf(stone);
            if (n > 0) word += ", keeping " + n + (n == 1 ? " neighbour" : " neighbours") + " company";
            return word;
        }

        /// <summary>0..1 how night-like it is (smooth dusk and dawn).</summary>
        public static float NightFactor()
        {
            var clock = GameClock.Instance;
            if (clock == null) return 0f;
            float h = clock.Hours;
            float n;
            if (h >= 20f || h < 5f) n = 1f;
            else if (h >= 17f) n = (h - 17f) / 3f;
            else if (h < 7f) n = 1f - (h - 5f) / 2f;
            else n = 0f;
            return Mathf.SmoothStep(0f, 1f, n);
        }

        // ---- refresh (cheap, every ~1.5s): who is gardened, how many flowers ----

        private void Refresh()
        {
            var reg = HeadstoneRegistry.Instance;
            var all = reg != null ? reg.All : null;

            // Drop gardens whose stone is gone (the root dies with it).
            _scratch.Clear();
            foreach (var kv in _gardens)
                if (kv.Key == null) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++) _gardens.Remove(_scratch[i]);
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                var stone = all[i];
                if (stone == null || !stone.Placed) continue;

                if (!_gardens.TryGetValue(stone, out var g))
                {
                    g = BuildGarden(stone);
                    if (g == null) continue;
                    _gardens[stone] = g;
                }

                g.Neighbours = CountNeighbours(stone, all);
                g.Growth = GrowthOf(stone);
                Layout(g);
            }
        }

        private StoneGarden BuildGarden(Headstone stone)
        {
            var root = new GameObject("MemorialGarden");
            root.transform.SetParent(stone.transform, false);

            var g = new StoneGarden
            {
                Stone = stone,
                Root = root.transform,
                Seed = SeedOf(stone)
            };
            g.GlowPhase = Rand01(g.Seed, 9001) * Mathf.PI * 2f;

            var glowGo = new GameObject("NightGlow");
            glowGo.transform.SetParent(root.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            g.Glow = glowGo.AddComponent<SpriteRenderer>();
            g.Glow.sprite = AscensionPad.GlowSprite;
            g.Glow.sortingOrder = OrderGlow;
            g.Glow.color = new Color(GlowCool.r, GlowCool.g, GlowCool.b, 0f);
            return g;
        }

        /// <summary>Grows/shrinks the flower + wisp lists and places them (all deterministic).</summary>
        private void Layout(StoneGarden g)
        {
            int n = g.Neighbours;
            int cap = Mathf.Min(MaxFlowers, 4 + 2 * n);
            // Ceil: the first bloom shows the moment the stone is placed.
            int want = g.Growth > 0f ? Mathf.Clamp(Mathf.CeilToInt(g.Growth * cap), 1, cap) : 0;

            var mat = StoneMaterial(g.Stone);
            while (g.Flowers.Count < want)
            {
                var go = new GameObject("Bloom");
                go.transform.SetParent(g.Root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = OrderFlower;
                if (mat != null) sr.sharedMaterial = mat;
                go.transform.localScale = Vector3.zero;
                g.Flowers.Add(new Flower { Renderer = sr, Scale = 0f, Target = 1f });
            }
            // Fewer wanted (a neighbour was carried away): ease the extras out.
            for (int j = 0; j < g.Flowers.Count; j++)
                g.Flowers[j].Target = j < want ? 1f : 0f;
            for (int j = g.Flowers.Count - 1; j >= 0 && j >= want; j--)
            {
                if (g.Flowers[j].Scale > 0.02f) break; // let it shrink first
                Destroy(g.Flowers[j].Renderer.gameObject);
                g.Flowers.RemoveAt(j);
            }

            // Flower j keeps the same angle/colour forever; only the ring widens with n.
            float ringSpan = 0.45f + 0.12f * n;
            for (int j = 0; j < g.Flowers.Count; j++)
            {
                var f = g.Flowers[j];
                float ang = Rand01(g.Seed, j * 7 + 1) * Mathf.PI * 2f;
                float rad = 0.26f + Mathf.Sqrt(Rand01(g.Seed, j * 7 + 2)) * ringSpan;
                f.Renderer.transform.localPosition =
                    new Vector3(Mathf.Cos(ang) * rad, -0.12f + Mathf.Sin(ang) * rad * 0.72f, 0f);
                f.Renderer.sprite = j % 4 == 3
                    ? TuftSprite
                    : FlowerSprites[(int)(Rand01(g.Seed, j * 7 + 3) * FlowerSprites.Length) % FlowerSprites.Length];
                f.Renderer.flipX = Rand01(g.Seed, j * 7 + 4) > 0.5f;
            }

            // Night wisps: one for a seasoned lone stone, more as stones cluster.
            int wispWant = g.Growth >= 0.35f ? Mathf.Min(MaxWisps, 1 + n) : 0;
            while (g.Wisps.Count < wispWant)
            {
                int k = g.Wisps.Count;
                var go = new GameObject("Wisp");
                go.transform.SetParent(g.Root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = AscensionPad.GlowSprite;
                sr.sortingOrder = OrderWisp;
                go.transform.localScale = Vector3.one * 0.11f;
                g.Wisps.Add(new Wisp
                {
                    Root = go.transform,
                    Renderer = sr,
                    Phase = Rand01(g.Seed, 500 + k) * Mathf.PI * 2f,
                    Speed = 0.35f + Rand01(g.Seed, 520 + k) * 0.35f,
                    Radius = 0.45f + Rand01(g.Seed, 540 + k) * 0.5f,
                    Lift = 0.35f + Rand01(g.Seed, 560 + k) * 0.5f,
                    Blink = 1.1f + Rand01(g.Seed, 580 + k) * 1.4f
                });
                sr.color = k % 2 == 0 ? WispWarm : WispCool;
            }
            while (g.Wisps.Count > wispWant)
            {
                Destroy(g.Wisps[g.Wisps.Count - 1].Root.gameObject);
                g.Wisps.RemoveAt(g.Wisps.Count - 1);
            }
        }

        // ---- per-frame life: bloom easing, night glow, drifting wisps ------------

        private void Animate(float dt)
        {
            float night = NightFactor();
            float t = Time.time;

            foreach (var kv in _gardens)
            {
                var g = kv.Value;
                if (g == null || g.Stone == null) continue;

                // Flowers ease toward their target scale (creep in / ease out).
                for (int j = 0; j < g.Flowers.Count; j++)
                {
                    var f = g.Flowers[j];
                    if (f.Renderer == null) continue;
                    float goal = f.Target * (0.95f + 0.4f * Rand01(g.Seed, j * 7 + 5));
                    if (Mathf.Abs(f.Scale - goal) > 0.002f)
                    {
                        f.Scale = Mathf.MoveTowards(f.Scale, goal, dt * 0.6f);
                        f.Renderer.transform.localScale = Vector3.one * f.Scale;
                    }
                }

                // Soft night glow: stronger when the ground is established and crowded.
                if (g.Glow != null)
                {
                    float strength = Mathf.Sqrt(g.Growth) * (0.22f + 0.05f * g.Neighbours);
                    float pulse = 0.85f + 0.15f * Mathf.Sin(t * 0.9f + g.GlowPhase);
                    float scale = 0.9f + 0.22f * g.Neighbours + 0.5f * g.Growth;
                    g.Glow.transform.localScale = new Vector3(scale, scale * 0.8f, 1f);
                    g.Glow.color = new Color(GlowCool.r, GlowCool.g, GlowCool.b,
                        night * strength * pulse);
                }

                // Wisps: lazy figure-eights above the stones, blinking like fireflies.
                for (int k = 0; k < g.Wisps.Count; k++)
                {
                    var w = g.Wisps[k];
                    if (w.Root == null) continue;
                    bool on = night > 0.02f;
                    if (w.Root.gameObject.activeSelf != on) w.Root.gameObject.SetActive(on);
                    if (!on) continue;

                    float a = t * w.Speed + w.Phase;
                    w.Root.localPosition = new Vector3(
                        Mathf.Sin(a) * w.Radius,
                        w.Lift + Mathf.Sin(a * 2f) * 0.18f,
                        0f);
                    float blink = Mathf.Clamp01(0.25f + 0.85f * Mathf.Sin(t * w.Blink + w.Phase * 3f));
                    var c = w.Renderer.color;
                    w.Renderer.color = new Color(c.r, c.g, c.b, night * blink * 0.9f);
                }
            }
        }

        // ---- helpers -----------------------------------------------------------------

        private static Material StoneMaterial(Headstone stone)
        {
            var sr = stone != null ? stone.GetComponent<SpriteRenderer>() : null;
            return sr != null ? sr.sharedMaterial : null;
        }

        /// <summary>Stable identity seed (FNV-1a; string.GetHashCode is not stable across runs).</summary>
        private static int SeedOf(Headstone stone)
        {
            string key = (stone.SpiritName ?? "") + "|" + (stone.SpeciesId ?? "") + "|" + stone.AscendedDay;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < key.Length; i++)
                    h = (h ^ key[i]) * 16777619u;
                return (int)h;
            }
        }

        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352du;
                x ^= x >> 15; x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>Deterministic 0..1 value for (seed, salt).</summary>
        private static float Rand01(int seed, int salt)
        {
            unchecked
            {
                uint h = Hash((uint)seed * 2654435761u + (uint)salt * 40503u + 977u);
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }

        // ---- procedural placeholder sprites (cached; swap for authored art later) -----

        private static Sprite[] _flowerSprites;
        private static Sprite _tuft;

        private static Sprite[] FlowerSprites
        {
            get
            {
                if (_flowerSprites != null) return _flowerSprites;
                _flowerSprites = new[]
                {
                    MakeFlower(new Color(0.93f, 0.72f, 0.80f, 1f), new Color(1f, 0.90f, 0.55f, 1f)), // dusk pink
                    MakeFlower(new Color(0.76f, 0.70f, 0.95f, 1f), new Color(1f, 0.92f, 0.65f, 1f)), // lavender
                    MakeFlower(new Color(0.96f, 0.96f, 0.92f, 1f), new Color(0.98f, 0.82f, 0.40f, 1f)), // moon white
                    MakeFlower(new Color(0.62f, 0.78f, 0.96f, 1f), new Color(1f, 0.95f, 0.75f, 1f)), // forget-me-not
                    MakeFlower(new Color(0.98f, 0.84f, 0.50f, 1f), new Color(0.80f, 0.55f, 0.30f, 1f))  // honey gold
                };
                return _flowerSprites;
            }
        }

        private static Sprite TuftSprite
        {
            get
            {
                if (_tuft != null) return _tuft;

                const int w = 9, h = 7;
                var tex = NewTex(w, h);
                var dark = new Color(0.26f, 0.45f, 0.30f, 1f);
                var light = new Color(0.40f, 0.62f, 0.38f, 1f);
                int[] xs = { 1, 2, 4, 6, 7 };
                int[] hs = { 3, 4, 6, 5, 3 };
                for (int i = 0; i < xs.Length; i++)
                    for (int y = 0; y < hs[i]; y++)
                        tex.SetPixel(xs[i], y, y >= hs[i] - 2 ? light : dark);
                tex.Apply();
                _tuft = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 64f);
                _tuft.hideFlags = HideFlags.HideAndDontSave;
                return _tuft;
            }
        }

        private static Sprite MakeFlower(Color petal, Color centre)
        {
            const int w = 9, h = 10;
            var tex = NewTex(w, h);
            var stem = new Color(0.30f, 0.52f, 0.32f, 1f);

            for (int y = 0; y <= 4; y++) tex.SetPixel(4, y, stem);   // stem
            tex.SetPixel(3, 2, stem); tex.SetPixel(5, 1, stem);       // two leaves

            // Four-petal bloom around (4,6), bright centre.
            tex.SetPixel(4, 8, petal); tex.SetPixel(4, 4, petal);
            tex.SetPixel(2, 6, petal); tex.SetPixel(6, 6, petal);
            tex.SetPixel(3, 7, petal); tex.SetPixel(5, 7, petal);
            tex.SetPixel(3, 5, petal); tex.SetPixel(5, 5, petal);
            tex.SetPixel(3, 6, petal); tex.SetPixel(5, 6, petal);
            tex.SetPixel(4, 7, petal); tex.SetPixel(4, 5, petal);
            tex.SetPixel(4, 6, centre);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 64f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Texture2D NewTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var clear = new Color32[w * h];
            tex.SetPixels32(clear);
            return tex;
        }

        // ---- debug (console 'garden') ----------------------------------------------

        /// <summary>Shifts every placed stone's clock back so the garden looks days older.</summary>
        public int Debug_AgeAll(float days)
        {
            var reg = HeadstoneRegistry.Instance;
            if (reg == null) return 0;
            int n = 0;
            foreach (var s in reg.All)
            {
                if (s == null || !s.Placed) continue;
                s.PlacedHours -= days * 24f;
                n++;
            }
            _refreshTimer = RefreshSeconds; // repaint next frame
            return n;
        }

        /// <summary>Marks every waiting stone as placed where it stands.</summary>
        public int Debug_PlaceAll()
        {
            var reg = HeadstoneRegistry.Instance;
            if (reg == null) return 0;
            int n = 0;
            foreach (var s in reg.All)
            {
                if (s == null || s.Placed) continue;
                s.MarkPlaced();
                n++;
            }
            _refreshTimer = RefreshSeconds;
            return n;
        }

        public string Debug_Status()
        {
            var reg = HeadstoneRegistry.Instance;
            if (reg == null) return "No headstone registry in this scene.";
            var sb = new StringBuilder();
            int placed = 0, waiting = 0;
            foreach (var s in reg.All)
            {
                if (s == null) continue;
                if (s.Placed) placed++; else waiting++;
            }
            sb.Append("Stones: ").Append(placed).Append(" placed, ").Append(waiting)
              .Append(" waiting. Night factor ").Append(NightFactor().ToString("0.00",
                  System.Globalization.CultureInfo.InvariantCulture)).Append('.');
            foreach (var s in reg.All)
            {
                if (s == null) continue;
                sb.Append("\n  ").Append(s.SpiritName).Append(": ");
                if (!s.Placed) { sb.Append("waiting to be placed"); continue; }
                sb.Append("bloom ").Append((GrowthOf(s) * 100f).ToString("0",
                      System.Globalization.CultureInfo.InvariantCulture)).Append("%, ")
                  .Append(NeighboursOf(s)).Append(" neighbour(s)");
                if (_gardens.TryGetValue(s, out var g))
                    sb.Append(", ").Append(g.Flowers.Count).Append(" blooms, ")
                      .Append(g.Wisps.Count).Append(" wisps");
            }
            return sb.ToString();
        }
    }
}

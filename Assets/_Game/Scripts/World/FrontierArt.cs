using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Runtime-generated placeholder art for the frontier systems (road props,
    /// toll imp, ambusher, mount, darkness vignette). These objects self-spawn
    /// (no bootstrapper hands them sprites), so like Puffs / VillainAgent they
    /// paint their own tiny textures once, cached and hidden from the project.
    /// Pure flat-colour primitives -- the art pass replaces every call site
    /// with real sprites. Nothing here touches the asset database.
    /// </summary>
    public static class FrontierArt
    {
        private const float Ppu = 32f;

        private static Sprite _white, _imp, _horse, _lamp, _milestone, _deadTree, _puddle, _reeds,
            _signpost, _stall, _bones, _vignette, _wraith;

        // ---- shared tiny canvas ------------------------------------------------

        private sealed class Canvas
        {
            public readonly int w, h;
            private readonly Color32[] _px;

            public Canvas(int w, int h) { this.w = w; this.h = h; _px = new Color32[w * h]; }

            public void Box(int x0, int y0, int x1, int y1, Color c)
            {
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(h - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(w - 1, x1); x++)
                    _px[x + y * w] = c;
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color c)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) _px[x + y * w] = c;
                }
            }

            public void Dot(int x, int y, Color c)
            {
                if (x >= 0 && x < w && y >= 0 && y < h) _px[x + y * w] = c;
            }

            public void Set(int x, int y, Color32 c) { _px[x + y * w] = c; }

            public Sprite ToSprite(float pivotX = 0.5f, float pivotY = 0.5f)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(_px);
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                tex.hideFlags = HideFlags.HideAndDontSave;
                var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(pivotX, pivotY), Ppu);
                s.hideFlags = HideFlags.HideAndDontSave;
                return s;
            }
        }


        // ---- sprites -------------------------------------------------------------

        /// <summary>8x8 white square, exactly 0.25 units at scale 1 (scale it for rects).</summary>
        public static Sprite White
        {
            get
            {
                if (_white == null)
                {
                    var c = new Canvas(8, 8);
                    c.Box(0, 0, 7, 7, Color.white);
                    _white = c.ToSprite();
                }
                return _white;
            }
        }

        /// <summary>Cheeky toll imp: squat red body, two horns, a wide grin.</summary>
        public static Sprite Imp
        {
            get
            {
                if (_imp == null)
                {
                    var body = new Color(0.78f, 0.28f, 0.22f);
                    var dark = new Color(0.32f, 0.10f, 0.10f);
                    var c = new Canvas(24, 24);
                    c.Ellipse(12, 9, 8, 7, body);          // belly
                    c.Ellipse(12, 16, 6, 5, body);         // head
                    c.Box(6, 19, 7, 23, dark);            // horns
                    c.Box(16, 19, 17, 23, dark);
                    c.Box(5, 2, 8, 4, dark);              // feet
                    c.Box(15, 2, 18, 4, dark);
                    c.Box(4, 8, 5, 12, body);             // arms
                    c.Box(18, 8, 19, 12, body);
                    c.Dot(10, 17, Color.white); c.Dot(14, 17, Color.white);
                    c.Box(9, 13, 15, 13, new Color(1f, 0.92f, 0.7f)); // grin
                    c.Box(8, 14, 8, 14, new Color(1f, 0.92f, 0.7f));
                    c.Box(16, 14, 16, 14, new Color(1f, 0.92f, 0.7f));
                    _imp = c.ToSprite(0.5f, 0.1f);
                }
                return _imp;
            }
        }

        /// <summary>The pouty mount, side view facing RIGHT; head carried low (sulking posture).</summary>
        public static Sprite Horse
        {
            get
            {
                if (_horse == null)
                {
                    var coat = new Color(0.42f, 0.40f, 0.52f);
                    var mane = new Color(0.22f, 0.18f, 0.32f);
                    var hoof = new Color(0.14f, 0.12f, 0.18f);
                    var c = new Canvas(48, 40);
                    c.Ellipse(22, 20, 14, 7, coat);        // barrel
                    c.Box(12, 5, 14, 15, coat);           // legs
                    c.Box(18, 5, 20, 15, coat);
                    c.Box(26, 5, 28, 15, coat);
                    c.Box(31, 5, 33, 15, coat);
                    c.Box(12, 3, 14, 5, hoof); c.Box(18, 3, 20, 5, hoof);
                    c.Box(26, 3, 28, 5, hoof); c.Box(31, 3, 33, 5, hoof);
                    c.Ellipse(34, 25, 4, 7, coat);         // neck
                    c.Ellipse(39, 20, 6, 4, coat);         // head, hung low
                    c.Box(43, 18, 45, 20, new Color(0.55f, 0.50f, 0.62f)); // muzzle
                    c.Box(33, 28, 36, 34, mane);          // mane
                    c.Box(35, 22, 37, 30, mane);
                    c.Ellipse(8, 22, 5, 3, mane);          // tail
                    c.Box(3, 12, 6, 22, mane);
                    c.Dot(40, 21, new Color(0.08f, 0.06f, 0.12f)); // half-lidded eye
                    c.Box(39, 22, 41, 22, mane);
                    _horse = c.ToSprite(0.5f, 0.1f);
                }
                return _horse;
            }
        }

        /// <summary>Road lamp post: dark pole with a small warm lamp head.</summary>
        public static Sprite LampPost
        {
            get
            {
                if (_lamp == null)
                {
                    var c = new Canvas(12, 40);
                    c.Box(5, 0, 6, 30, new Color(0.30f, 0.26f, 0.24f));
                    c.Box(3, 30, 8, 37, new Color(0.24f, 0.22f, 0.22f));
                    c.Box(4, 31, 7, 36, new Color(1f, 0.82f, 0.45f));
                    c.Box(2, 37, 9, 38, new Color(0.24f, 0.22f, 0.22f));
                    _lamp = c.ToSprite(0.5f, 0.05f);
                }
                return _lamp;
            }
        }

        /// <summary>Little stone milestone.</summary>
        public static Sprite Milestone
        {
            get
            {
                if (_milestone == null)
                {
                    var c = new Canvas(14, 18);
                    c.Box(2, 0, 11, 12, new Color(0.55f, 0.55f, 0.58f));
                    c.Ellipse(6.5f, 12, 4.5f, 4, new Color(0.60f, 0.60f, 0.63f));
                    c.Box(5, 5, 8, 6, new Color(0.30f, 0.30f, 0.34f));
                    c.Box(5, 8, 8, 9, new Color(0.30f, 0.30f, 0.34f));
                    _milestone = c.ToSprite(0.5f, 0.05f);
                }
                return _milestone;
            }
        }

        /// <summary>Bare dead tree for the dry stretch.</summary>
        public static Sprite DeadTree
        {
            get
            {
                if (_deadTree == null)
                {
                    var bark = new Color(0.28f, 0.22f, 0.20f);
                    var c = new Canvas(28, 40);
                    c.Box(13, 0, 15, 24, bark);
                    c.Box(8, 18, 12, 19, bark); c.Box(7, 19, 8, 26, bark);
                    c.Box(16, 22, 21, 23, bark); c.Box(21, 23, 22, 31, bark);
                    c.Box(12, 24, 14, 34, bark); c.Box(10, 30, 11, 36, bark);
                    _deadTree = c.ToSprite(0.5f, 0.02f);
                }
                return _deadTree;
            }
        }

        /// <summary>Murky puddle (flat ellipse).</summary>
        public static Sprite Puddle
        {
            get
            {
                if (_puddle == null)
                {
                    var c = new Canvas(30, 12);
                    c.Ellipse(15, 6, 14, 5, new Color(0.22f, 0.30f, 0.30f, 0.9f));
                    c.Ellipse(12, 7, 5, 2, new Color(0.34f, 0.44f, 0.44f, 0.9f));
                    _puddle = c.ToSprite();
                }
                return _puddle;
            }
        }

        /// <summary>Reed clump for the swamp fringe.</summary>
        public static Sprite Reeds
        {
            get
            {
                if (_reeds == null)
                {
                    var g = new Color(0.40f, 0.56f, 0.34f);
                    var c = new Canvas(20, 26);
                    c.Box(4, 0, 5, 18, g); c.Box(8, 0, 9, 24, g);
                    c.Box(12, 0, 13, 20, g); c.Box(15, 0, 16, 15, g);
                    c.Box(8, 22, 9, 25, new Color(0.45f, 0.30f, 0.22f));
                    c.Box(12, 18, 13, 21, new Color(0.45f, 0.30f, 0.22f));
                    _reeds = c.ToSprite(0.5f, 0.05f);
                }
                return _reeds;
            }
        }

        /// <summary>Wooden sign on a post (arrow-less; the label does the talking).</summary>
        public static Sprite Signpost
        {
            get
            {
                if (_signpost == null)
                {
                    var wood = new Color(0.50f, 0.37f, 0.25f);
                    var c = new Canvas(20, 30);
                    c.Box(9, 0, 10, 20, wood);
                    c.Box(2, 16, 17, 26, wood);
                    c.Box(3, 17, 16, 25, new Color(0.62f, 0.48f, 0.33f));
                    _signpost = c.ToSprite(0.5f, 0.02f);
                }
                return _signpost;
            }
        }

        /// <summary>Plain market stall for the swamp vendor (awning over a counter).</summary>
        public static Sprite Stall
        {
            get
            {
                if (_stall == null)
                {
                    var c = new Canvas(40, 36);
                    c.Box(3, 0, 5, 24, new Color(0.32f, 0.28f, 0.22f));
                    c.Box(34, 0, 36, 24, new Color(0.32f, 0.28f, 0.22f));
                    c.Box(2, 8, 37, 14, new Color(0.40f, 0.34f, 0.26f));
                    for (int x = 0; x < 40; x++)
                    {
                        var stripe = (x / 4) % 2 == 0 ? new Color(0.30f, 0.50f, 0.42f) : new Color(0.74f, 0.70f, 0.52f);
                        c.Box(x, 25, x, 33, stripe);
                    }
                    _stall = c.ToSprite(0.5f, 0.02f);
                }
                return _stall;
            }
        }

        /// <summary>Scattered bones for the dry stretch.</summary>
        public static Sprite Bones
        {
            get
            {
                if (_bones == null)
                {
                    var b = new Color(0.86f, 0.84f, 0.76f);
                    var c = new Canvas(20, 10);
                    c.Box(3, 4, 15, 5, b); c.Box(2, 3, 3, 6, b); c.Box(15, 3, 16, 6, b);
                    c.Ellipse(15, 7, 3, 2, b);
                    _bones = c.ToSprite();
                }
                return _bones;
            }
        }

        /// <summary>
        /// Darkness vignette: transparent in the middle, opaque beyond. Alpha is
        /// smoothstep(0.08, 0.22, distance / halfSize) -- the overlay's SCALE sets
        /// how big the lit circle is (lantern = bigger). 128x128, bilinear.
        /// </summary>
        public static Sprite Vignette
        {
            get
            {
                if (_vignette == null)
                {
                    const int n = 128;
                    var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                    var px = new Color32[n * n];
                    for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f;
                        float dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.22f, d));
                        px[x + y * n] = new Color32(6, 6, 14, (byte)Mathf.RoundToInt(a * 255f));
                    }
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    // 1 unit across at scale 1 (ppu = n): the overlay scales it up.
                    _vignette = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
                    _vignette.hideFlags = HideFlags.HideAndDontSave;
                }
                return _vignette;
            }
        }

        /// <summary>Road-ambusher body: an eerie white blob with a frayed bottom (tinted per kind).</summary>
        public static Sprite Wraith
        {
            get
            {
                if (_wraith == null)
                {
                    var c = new Canvas(24, 24);
                    c.Ellipse(12, 13, 9, 8, Color.white);
                    c.Ellipse(12, 8, 7, 6, Color.white);
                    for (int x = 4; x < 20; x++)
                        if ((x % 3) != 0) c.Box(x, 0, x, 5, Color.white); // ragged tail
                    c.Dot(9, 14, new Color(0.1f, 0.05f, 0.12f)); c.Dot(10, 14, new Color(0.1f, 0.05f, 0.12f));
                    c.Dot(14, 14, new Color(0.1f, 0.05f, 0.12f)); c.Dot(15, 14, new Color(0.1f, 0.05f, 0.12f));
                    c.Box(10, 10, 14, 10, new Color(0.1f, 0.05f, 0.12f));
                    _wraith = c.ToSprite(0.5f, 0.4f);
                }
                return _wraith;
            }
        }

        /// <summary>Drops cached sprites (enter-play-mode without domain reload keeps statics alive).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _white = _imp = _horse = _lamp = _milestone = _deadTree = _puddle = _reeds =
                _signpost = _stall = _bones = _vignette = _wraith = null;
        }

        /// <summary>Adds a sprite child/object with sensible defaults. Returns the renderer.</summary>
        public static SpriteRenderer AddSprite(GameObject go, Sprite sprite, int order, Color? tint = null)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (tint.HasValue) sr.color = tint.Value;
            return sr;
        }

    }
}

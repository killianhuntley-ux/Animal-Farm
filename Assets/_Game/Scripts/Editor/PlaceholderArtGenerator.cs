using System.IO;
using UnityEditor;
using UnityEngine;

namespace AnimalFarm.EditorTools
{
    /// <summary>
    /// Generates flat placeholder sprite PNGs so no slice ever waits on art
    /// (slice roadmap rule 2). Deterministic — safe to re-run; overwrites.
    /// </summary>
    public static class PlaceholderArtGenerator
    {
        private const string Dir = "Assets/_Game/Art/Placeholder";
        private const string UIDir = "Assets/_Game/Resources/UIPlaceholder";
        private const int PPU = 64;

        // 9-slice borders (pixels) for UI sprites, keyed by file name.
        private static readonly System.Collections.Generic.Dictionary<string, Vector4> UIBorders =
            new System.Collections.Generic.Dictionary<string, Vector4>
            {
                { "ui_panel", new Vector4(20, 20, 20, 20) },
                { "ui_button", new Vector4(18, 18, 18, 18) },
                { "ui_bar", new Vector4(8, 8, 8, 8) }
            };

        [MenuItem("AnimalFarm/Generate Placeholder Art")]
        public static void Generate()
        {
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(UIDir);

            // UI chrome (beauty pass): rounded 9-slice panels, loaded at runtime
            // via Resources/UIPlaceholder (see UIStyle).
            WriteUISoftRect("ui_panel", 64, 64, Color.white, roundness: 0.38f);
            // Button: gentler corners than the border (0.45 radius vs 14px border
            // produced washed-out pill artifacts when sliced).
            WriteUISoftRect("ui_button", 64, 64, Color.white, roundness: 0.28f);
            WriteUISoftRect("ui_bar", 24, 24, Color.white, roundness: 0.9f);

            // name, width, height, base color, style
            WriteSoftRect("shepherd_body", 40, 56, new Color(0.45f, 0.40f, 0.55f), roundness: 0.45f);
            WriteCircle("shepherd_head", 30, new Color(0.93f, 0.83f, 0.72f));
            WriteSoftRect("shepherd_body_alt", 40, 56, new Color(0.30f, 0.50f, 0.45f), roundness: 0.45f);
            WriteCircle("shepherd_head_alt", 30, new Color(0.85f, 0.88f, 0.95f));

            WriteNoiseRect("ground_grass", 64, 64, new Color(0.32f, 0.45f, 0.28f), noise: 0.035f);

            // terrain tiles (slice 02) — scrub is deliberately dull: barren→lush readout
            WriteNoiseRect("tile_scrub", 64, 64, new Color(0.38f, 0.37f, 0.26f), noise: 0.045f);
            WriteNoiseRect("tile_dirt", 64, 64, new Color(0.42f, 0.31f, 0.22f), noise: 0.04f);
            WriteNoiseRect("tile_grass", 64, 64, new Color(0.30f, 0.50f, 0.26f), noise: 0.035f);
            WriteNoiseRect("tile_water", 64, 64, new Color(0.22f, 0.38f, 0.55f), noise: 0.03f);

            // spirit bodies (slice 03): pale ghost blobs, one silhouette-friendly sprite each
            WriteMouseSpirit("mausoleum_body", 32, new Color(0.78f, 0.74f, 0.85f));
            WriteSheepSpirit("bansheep_body", 40, new Color(0.92f, 0.90f, 0.86f));
            WriteRabbitSpirit("wrabbit_body", 36, new Color(0.72f, 0.86f, 0.78f));
            WriteMothSpirit("phantomoth_body", 36, new Color(0.70f, 0.62f, 0.82f));

            // the Repo-man + holding office (slice 07)
            WriteRepoMan("repoman_body", 44);
            WriteHome("holding_office", 46, new Color(0.45f, 0.42f, 0.50f), new Color(0.28f, 0.26f, 0.34f));

            // competition board (slice 05)
            WriteNoticeBoard("notice_board", 44, new Color(0.52f, 0.38f, 0.26f));

            // farm fence (UX pass)
            WriteFencePost("fence_post", 12, 40, new Color(0.48f, 0.36f, 0.26f));

            // ascension & homes (slice 04)
            WriteSoftRect("altar_platform", 72, 40, new Color(0.55f, 0.56f, 0.62f), roundness: 0.5f);
            WriteRadialGlow("altar_glow", 96, new Color(1f, 0.92f, 0.6f));
            WriteColumn("altar_column", 40, 128, new Color(1f, 0.97f, 0.8f));
            WriteHeadstone("headstone", 24, 30, new Color(0.62f, 0.63f, 0.68f));
            WriteHome("home_mausoleum", 34, new Color(0.70f, 0.66f, 0.78f), new Color(0.45f, 0.40f, 0.55f));
            WriteHome("home_bansheep", 38, new Color(0.85f, 0.82f, 0.74f), new Color(0.55f, 0.45f, 0.35f));
            WriteHome("home_wrabbit", 34, new Color(0.62f, 0.76f, 0.66f), new Color(0.35f, 0.52f, 0.40f));
            WriteHome("home_phantomoth", 34, new Color(0.60f, 0.54f, 0.72f), new Color(0.36f, 0.30f, 0.50f));

            // woven cryptids + the Loom (slice 06)
            WriteWailpertinger("wailpertinger_body", 40, new Color(0.80f, 0.82f, 0.88f));
            WriteMothmaus("mothmaus_body", 40, new Color(0.72f, 0.66f, 0.84f));
            WriteLoom("loom", 56, new Color(0.42f, 0.32f, 0.24f));

            // plant growth stages (slice 02): sprout shared, mid/mature per species
            WriteSprout("plant_sprout", 24, new Color(0.45f, 0.62f, 0.35f));
            WriteStalks("palewheat_mid", 32, 20, new Color(0.75f, 0.72f, 0.50f));
            WriteStalks("palewheat_ripe", 32, 30, new Color(0.88f, 0.80f, 0.52f));
            WriteFlower("gravebloom_mid", 32, new Color(0.55f, 0.40f, 0.65f), open: false);
            WriteFlower("gravebloom_ripe", 36, new Color(0.62f, 0.42f, 0.75f), open: true);
            WriteBush("murkberry_mid", 30, new Color(0.25f, 0.35f, 0.28f), berries: false);
            WriteBush("murkberry_ripe", 34, new Color(0.25f, 0.35f, 0.28f), berries: true);
            WriteCircle("pebble", 12, new Color(0.55f, 0.53f, 0.50f));
            WriteTuft("grass_tuft", 20, new Color(0.26f, 0.40f, 0.23f));
            WriteSoftRect("rock", 44, 34, new Color(0.45f, 0.44f, 0.42f), roundness: 0.6f);
            WriteSoftRect("waystone", 36, 58, new Color(0.60f, 0.60f, 0.66f), roundness: 0.35f);
            WriteCircle("white_circle", 32, Color.white);
            WriteSoftRect("white_rect", 32, 32, Color.white, roundness: 0.2f);

            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Dir, "*.png"))
                ConfigureImporter(path.Replace('\\', '/'));
            foreach (var path in Directory.GetFiles(UIDir, "*.png"))
                ConfigureImporter(path.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            Debug.Log("[PlaceholderArt] Generated placeholder sprites at " + Dir);
        }

        private static void ConfigureImporter(string assetPath)
        {
            var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spritePixelsPerUnit = PPU;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;

            // FullRect mesh so tiled draw mode (fence rails etc.) tiles correctly.
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);

            // UI chrome gets 9-slice borders.
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (UIBorders.TryGetValue(fileName, out var border))
                imp.spriteBorder = border;

            imp.SaveAndReimport();
        }

        // ---- shape writers ------------------------------------------------

        private static void WriteCircle(string name, int size, Color c)
        {
            var tex = NewTex(size, size);
            float r = size * 0.5f - 0.5f;
            Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float a = Mathf.Clamp01(r - d + 0.5f); // 1px antialias
                // subtle top-light shading for readable volume
                float shade = Mathf.Lerp(0.85f, 1.1f, (y / (float)size));
                tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, a));
            }
            Save(tex, name);
        }

        private static void WriteSoftRect(string name, int w, int h, Color c, float roundness)
        {
            var tex = NewTex(w, h);
            float rad = Mathf.Min(w, h) * 0.5f * Mathf.Clamp01(roundness);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // rounded-rect signed distance
                float qx = Mathf.Abs(x - (w - 1) * 0.5f) - ((w - 1) * 0.5f - rad);
                float qy = Mathf.Abs(y - (h - 1) * 0.5f) - ((h - 1) * 0.5f - rad);
                float dist = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude
                             + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
                float a = Mathf.Clamp01(-dist + 0.5f);
                float shade = Mathf.Lerp(0.88f, 1.08f, y / (float)h);
                tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, a));
            }
            Save(tex, name);
        }

        private static void WriteNoiseRect(string name, int w, int h, Color c, float noise)
        {
            var tex = NewTex(w, h);
            var rng = new System.Random(1234); // deterministic
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = 1f + ((float)rng.NextDouble() * 2f - 1f) * noise;
                tex.SetPixel(x, y, new Color(c.r * n, c.g * n, c.b * n, 1f));
            }
            Save(tex, name);
        }

        private static void WriteTuft(string name, int size, Color c)
        {
            var tex = NewTex(size, size);
            // three simple blades
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, Color.clear);
            int mid = size / 2;
            for (int blade = -1; blade <= 1; blade++)
            {
                int bx = mid + blade * (size / 4);
                int height = size - 4 - Mathf.Abs(blade) * 4;
                for (int y = 0; y < height; y++)
                {
                    int sway = Mathf.RoundToInt(blade * (y / (float)height) * 2f);
                    int px = Mathf.Clamp(bx + sway, 0, size - 1);
                    float shade = Mathf.Lerp(0.8f, 1.15f, y / (float)height);
                    tex.SetPixel(px, y, new Color(c.r * shade, c.g * shade, c.b * shade, 1f));
                }
            }
            Save(tex, name);
        }

        private static void WriteSprout(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            int mid = size / 2;
            for (int y = 0; y < size / 2; y++) tex.SetPixel(mid, y, c); // stem
            // two leaves
            for (int i = 1; i <= size / 5; i++)
            {
                tex.SetPixel(mid - i, size / 3 + i / 2, c);
                tex.SetPixel(mid + i, size / 4 + i / 2, c);
            }
            Save(tex, name);
        }

        private static void WriteStalks(string name, int size, int stalkHeight, Color c)
        {
            var tex = ClearTex(size, size);
            var rng = new System.Random(name.GetHashCode());
            for (int s = 0; s < 5; s++)
            {
                int x = 4 + s * (size - 8) / 4;
                int h = Mathf.Min(size - 2, stalkHeight - rng.Next(0, 5));
                for (int y = 0; y < h; y++)
                {
                    float shade = Mathf.Lerp(0.8f, 1.15f, y / (float)h);
                    tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, 1f));
                }
                // seed head: little cluster at the top
                tex.SetPixel(x - 1, h - 2, c);
                tex.SetPixel(x + 1, h - 2, c);
                tex.SetPixel(x, Mathf.Min(size - 1, h), c);
            }
            Save(tex, name);
        }

        private static void WriteFlower(string name, int size, Color petal, bool open)
        {
            var tex = ClearTex(size, size);
            int mid = size / 2;
            var stem = new Color(0.35f, 0.48f, 0.30f);
            for (int y = 0; y < size / 2; y++) tex.SetPixel(mid, y, stem);
            Vector2 center = new Vector2(mid, size * 0.68f);
            float r = open ? size * 0.26f : size * 0.15f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                if (d <= r)
                {
                    float shade = Mathf.Lerp(1.1f, 0.85f, d / r);
                    tex.SetPixel(x, y, new Color(petal.r * shade, petal.g * shade, petal.b * shade, 1f));
                }
            }
            if (open) // pale core
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                    if (x * x + y * y <= 4)
                        tex.SetPixel(mid + x, (int)(size * 0.68f) + y, new Color(0.95f, 0.92f, 0.75f, 1f));
            Save(tex, name);
        }

        private static void WriteBush(string name, int size, Color leaf, bool berries)
        {
            var tex = ClearTex(size, size);
            Vector2 center = new Vector2(size * 0.5f, size * 0.42f);
            float r = size * 0.4f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                if (d <= r)
                {
                    float shade = Mathf.Lerp(1.15f, 0.8f, d / r);
                    tex.SetPixel(x, y, new Color(leaf.r * shade, leaf.g * shade, leaf.b * shade, 1f));
                }
            }
            if (berries)
            {
                var berry = new Color(0.35f, 0.15f, 0.4f);
                var rng = new System.Random(7);
                for (int i = 0; i < 7; i++)
                {
                    int bx = (int)(center.x + rng.Next(-(int)(r * 0.7f), (int)(r * 0.7f)));
                    int by = (int)(center.y + rng.Next(-(int)(r * 0.6f), (int)(r * 0.6f)));
                    for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                        if (x * x + y * y <= 1) tex.SetPixel(bx + x, by + y, berry);
                }
            }
            Save(tex, name);
        }

        /// <summary>Soft rect written into the UI Resources dir (white; tinted at runtime).</summary>
        private static void WriteUISoftRect(string name, int w, int h, Color c, float roundness)
        {
            var tex = NewTex(w, h);
            float rad = Mathf.Min(w, h) * 0.5f * Mathf.Clamp01(roundness);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float qx = Mathf.Abs(x - (w - 1) * 0.5f) - ((w - 1) * 0.5f - rad);
                float qy = Mathf.Abs(y - (h - 1) * 0.5f) - ((h - 1) * 0.5f - rad);
                float dist = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude
                             + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
                float a = Mathf.Clamp01(-dist + 0.5f);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
            tex.Apply();
            File.WriteAllBytes(Path.Combine(UIDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static void WriteNoticeBoard(string name, int size, Color wood)
        {
            var tex = ClearTex(size, size);
            // two legs
            var darkWood = new Color(wood.r * 0.7f, wood.g * 0.7f, wood.b * 0.7f, 1f);
            for (int y = 0; y < size / 2; y++)
            {
                for (int x = size / 5; x < size / 5 + 3; x++) tex.SetPixel(x, y, darkWood);
                for (int x = size - size / 5 - 3; x < size - size / 5; x++) tex.SetPixel(x, y, darkWood);
            }
            // panel
            for (int y = size / 3; y < size - 2; y++)
            for (int x = 2; x < size - 2; x++)
            {
                float shade = Mathf.Lerp(0.85f, 1.1f, y / (float)size);
                tex.SetPixel(x, y, new Color(wood.r * shade, wood.g * shade, wood.b * shade, 1f));
            }
            // pinned paper
            var paper = new Color(0.92f, 0.90f, 0.82f, 1f);
            for (int y = size / 2; y < size - 6; y++)
            for (int x = size / 4; x < size - size / 4; x++)
                tex.SetPixel(x, y, paper);
            Save(tex, name);
        }

        private static void WriteFencePost(string name, int w, int h, Color c)
        {
            var tex = ClearTex(w, h);
            int mid = w / 2;
            for (int y = 0; y < h; y++)
            {
                int half = y > h - w ? Mathf.Max(1, (h - y)) / 2 : w / 2 - 2; // pointed top
                for (int x = mid - half; x <= mid + half; x++)
                {
                    if (x < 0 || x >= w) continue;
                    float shade = Mathf.Lerp(0.8f, 1.1f, y / (float)h);
                    tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, 1f));
                }
            }
            Save(tex, name);
        }

        // ---- slice 04 writers ----------------------------------------------

        private static void WriteRadialGlow(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / r;
                if (d > 1f) continue;
                float a = Mathf.Pow(1f - d, 2.2f);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
            Save(tex, name);
        }

        private static void WriteColumn(string name, int w, int h, Color c)
        {
            var tex = ClearTex(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x - (w - 1) * 0.5f) / (w * 0.5f);      // horizontal falloff
                float vy = Mathf.Min(y / (h * 0.15f), (h - 1f - y) / (h * 0.25f)); // soft ends
                float a = Mathf.Clamp01(1f - dx) * Mathf.Clamp01(vy) * 0.9f;
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a * a));
            }
            Save(tex, name);
        }

        private static void WriteHeadstone(string name, int w, int h, Color c)
        {
            var tex = ClearTex(w, h);
            int archTop = h - w / 2;
            Vector2 archCenter = new Vector2((w - 1) * 0.5f, archTop);
            float archR = (w - 2) * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 1; x < w - 1; x++)
            {
                bool inBody = y <= archTop;
                bool inArch = !inBody && Vector2.Distance(new Vector2(x, y), archCenter) <= archR;
                if (!inBody && !inArch) continue;
                float shade = Mathf.Lerp(0.85f, 1.1f, y / (float)h);
                tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, 1f));
            }
            // engraved lines
            var dark = new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 1f);
            for (int x = w / 4; x < w - w / 4; x++) { tex.SetPixel(x, h / 2, dark); tex.SetPixel(x, h / 2 - 4, dark); }
            Save(tex, name);
        }

        private static void WriteHome(string name, int size, Color body, Color roof)
        {
            var tex = ClearTex(size, size);
            int wallTop = (int)(size * 0.55f);
            // walls
            for (int y = 2; y <= wallTop; y++)
            for (int x = 3; x < size - 3; x++)
            {
                float shade = Mathf.Lerp(0.85f, 1.08f, y / (float)wallTop);
                tex.SetPixel(x, y, new Color(body.r * shade, body.g * shade, body.b * shade, 1f));
            }
            // roof triangle
            int roofH = size - 2 - wallTop;
            for (int i = 0; i < roofH; i++)
            {
                int inset = 1 + (int)(i * (size * 0.5f - 2) / roofH);
                for (int x = inset; x < size - inset; x++)
                    tex.SetPixel(x, wallTop + 1 + i, roof);
            }
            // door
            var dark = new Color(0.15f, 0.12f, 0.18f, 1f);
            int mid = size / 2;
            for (int y = 2; y <= size / 4 + 2; y++)
            for (int x = mid - 2; x <= mid + 2; x++)
                tex.SetPixel(x, y, dark);
            Save(tex, name);
        }

        private static void WriteRepoMan(string name, int size)
        {
            // Tall dark coat, pale bureaucrat face, a clipboard. Comically officious.
            var tex = ClearTex(size, size);
            var coat = new Color(0.16f, 0.15f, 0.20f);
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.36f), size * 0.24f, size * 0.36f, coat); // coat
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.78f), size * 0.13f, size * 0.13f,
                new Color(0.85f, 0.83f, 0.78f)); // face
            var brim = new Color(0.10f, 0.09f, 0.14f, 1f); // hat brim
            for (int x = (int)(size * 0.30f); x < (int)(size * 0.70f); x++)
            for (int y = (int)(size * 0.86f); y < (int)(size * 0.89f); y++)
                tex.SetPixel(x, y, brim);
            var clip = new Color(0.75f, 0.68f, 0.5f, 1f); // clipboard
            for (int x = (int)(size * 0.66f); x < (int)(size * 0.82f); x++)
            for (int y = (int)(size * 0.30f); y < (int)(size * 0.52f); y++)
                tex.SetPixel(x, y, clip);
            var paper = new Color(0.92f, 0.90f, 0.84f, 1f);
            for (int x = (int)(size * 0.68f); x < (int)(size * 0.80f); x++)
            for (int y = (int)(size * 0.33f); y < (int)(size * 0.49f); y++)
                tex.SetPixel(x, y, paper);
            DrawEyes(tex, new Vector2(size * 0.5f, size * 0.78f), size * 0.05f);
            Save(tex, name);
        }

        // ---- cryptid writers (slice 06) --------------------------------------

        private static void WriteWailpertinger(string name, int size, Color c)
        {
            // Horned, winged rabbit: rabbit blob + antler prongs + side wings.
            var tex = ClearTex(size, size);
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.34f), size * 0.26f, size * 0.24f, c);   // body
            DrawBlob(tex, new Vector2(size * 0.40f, size * 0.68f), size * 0.07f, size * 0.20f, c);  // ears
            DrawBlob(tex, new Vector2(size * 0.60f, size * 0.70f), size * 0.07f, size * 0.20f, c);
            var antler = new Color(0.82f, 0.72f, 0.5f, 1f);
            for (int i = 0; i < 8; i++) // two small antler prongs
            {
                tex.SetPixel((int)(size * 0.30f) - i / 3, (int)(size * 0.78f) + i, antler);
                tex.SetPixel((int)(size * 0.70f) + i / 3, (int)(size * 0.78f) + i, antler);
            }
            var wing = new Color(c.r * 0.85f, c.g * 0.88f, c.b, 0.9f);
            DrawBlob(tex, new Vector2(size * 0.16f, size * 0.40f), size * 0.11f, size * 0.16f, wing);
            DrawBlob(tex, new Vector2(size * 0.84f, size * 0.40f), size * 0.11f, size * 0.16f, wing);
            DrawEyes(tex, new Vector2(size * 0.5f, size * 0.40f), size * 0.10f);
            Save(tex, name);
        }

        private static void WriteMothmaus(string name, int size, Color c)
        {
            // Mothman-mouse: mouse body, big moth wings, glowing eyes.
            var tex = ClearTex(size, size);
            var wing = new Color(c.r * 0.8f, c.g * 0.75f, c.b, 0.95f);
            DrawBlob(tex, new Vector2(size * 0.24f, size * 0.52f), size * 0.20f, size * 0.30f, wing);
            DrawBlob(tex, new Vector2(size * 0.76f, size * 0.52f), size * 0.20f, size * 0.30f, wing);
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.42f), size * 0.16f, size * 0.24f, c);   // body
            DrawBlob(tex, new Vector2(size * 0.40f, size * 0.72f), size * 0.07f, size * 0.07f, c);  // ears
            DrawBlob(tex, new Vector2(size * 0.60f, size * 0.72f), size * 0.07f, size * 0.07f, c);
            var glow = new Color(1f, 0.35f, 0.25f, 1f); // the famous red eyes
            for (int s = -1; s <= 1; s += 2)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                if (x * x + y * y <= 1)
                    tex.SetPixel((int)(size * 0.5f + s * size * 0.07f) + x, (int)(size * 0.58f) + y, glow);
            Save(tex, name);
        }

        private static void WriteLoom(string name, int size, Color wood)
        {
            // Two upright posts, crossbar, hanging threads.
            var tex = ClearTex(size, size);
            var darkWood = new Color(wood.r * 0.75f, wood.g * 0.75f, wood.b * 0.75f, 1f);
            for (int y = 2; y < size - 4; y++)
            {
                for (int x = 6; x < 10; x++) tex.SetPixel(x, y, wood);
                for (int x = size - 10; x < size - 6; x++) tex.SetPixel(x, y, wood);
            }
            for (int x = 4; x < size - 4; x++) // crossbar
            for (int y = size - 8; y < size - 4; y++)
                tex.SetPixel(x, y, darkWood);
            var thread = new Color(0.85f, 0.88f, 0.95f, 0.85f); // ghost-thread
            var rng = new System.Random(11);
            for (int t = 0; t < 6; t++)
            {
                int x = 12 + t * (size - 24) / 5;
                int len = size - 14 - rng.Next(0, 10);
                for (int y = size - 9; y > size - 9 - len && y > 2; y--)
                    tex.SetPixel(x, y, thread);
            }
            Save(tex, name);
        }

        // ---- spirit blob writers (slice 03) --------------------------------

        private static void DrawBlob(Texture2D tex, Vector2 center, float rx, float ry, Color c)
        {
            int w = tex.width, h = tex.height;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - center.x) / rx, dy = (y - center.y) / ry;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 1.08f) continue;
                float a = Mathf.Clamp01((1.08f - d) * 6f);
                float shade = Mathf.Lerp(0.85f, 1.12f, Mathf.InverseLerp(center.y - ry, center.y + ry, y));
                var px = tex.GetPixel(x, y);
                var nc = new Color(c.r * shade, c.g * shade, c.b * shade, Mathf.Max(px.a, a));
                if (a >= px.a) tex.SetPixel(x, y, nc);
            }
        }

        private static void DrawEyes(Texture2D tex, Vector2 center, float spread)
        {
            var dark = new Color(0.12f, 0.10f, 0.18f, 1f);
            for (int s = -1; s <= 1; s += 2)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                if (x * x + y * y <= 1)
                    tex.SetPixel((int)(center.x + s * spread) + x, (int)center.y + y, dark);
        }

        private static void WriteMouseSpirit(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.38f), size * 0.32f, size * 0.30f, c); // body
            DrawBlob(tex, new Vector2(size * 0.32f, size * 0.68f), size * 0.13f, size * 0.13f, c); // ears
            DrawBlob(tex, new Vector2(size * 0.68f, size * 0.68f), size * 0.13f, size * 0.13f, c);
            DrawEyes(tex, new Vector2(size * 0.5f, size * 0.44f), size * 0.12f);
            Save(tex, name);
        }

        private static void WriteSheepSpirit(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            DrawBlob(tex, new Vector2(size * 0.38f, size * 0.42f), size * 0.24f, size * 0.20f, c); // wool
            DrawBlob(tex, new Vector2(size * 0.60f, size * 0.40f), size * 0.24f, size * 0.20f, c);
            DrawBlob(tex, new Vector2(size * 0.50f, size * 0.52f), size * 0.24f, size * 0.20f, c);
            DrawBlob(tex, new Vector2(size * 0.78f, size * 0.52f), size * 0.12f, size * 0.11f,
                new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.60f)); // dark head
            DrawEyes(tex, new Vector2(size * 0.78f, size * 0.54f), size * 0.05f);
            Save(tex, name);
        }

        private static void WriteRabbitSpirit(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.34f), size * 0.28f, size * 0.26f, c);  // body
            DrawBlob(tex, new Vector2(size * 0.38f, size * 0.74f), size * 0.08f, size * 0.24f, c); // long ears
            DrawBlob(tex, new Vector2(size * 0.62f, size * 0.76f), size * 0.08f, size * 0.24f, c);
            DrawEyes(tex, new Vector2(size * 0.5f, size * 0.40f), size * 0.11f);
            Save(tex, name);
        }

        private static void WriteMothSpirit(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            DrawBlob(tex, new Vector2(size * 0.28f, size * 0.55f), size * 0.20f, size * 0.28f, c); // wings
            DrawBlob(tex, new Vector2(size * 0.72f, size * 0.55f), size * 0.20f, size * 0.28f, c);
            DrawBlob(tex, new Vector2(size * 0.5f, size * 0.48f), size * 0.10f, size * 0.24f,
                new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.7f)); // body
            DrawEyes(tex, new Vector2(size * 0.5f, size * 0.66f), size * 0.06f);
            Save(tex, name);
        }

        private static Texture2D ClearTex(int w, int h)
        {
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, Color.clear);
            return tex;
        }

        private static Texture2D NewTex(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false);

        private static void Save(Texture2D tex, string name)
        {
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}

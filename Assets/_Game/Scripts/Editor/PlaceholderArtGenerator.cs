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
        private const int PPU = 64;

        [MenuItem("AnimalFarm/Generate Placeholder Art")]
        public static void Generate()
        {
            Directory.CreateDirectory(Dir);

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

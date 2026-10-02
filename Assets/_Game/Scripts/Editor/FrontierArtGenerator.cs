using System.IO;
using UnityEngine;

namespace AnimalFarm.EditorTools
{
    /// <summary>
    /// Placeholder PNGs for the frontier content (muscle 08 item 3): the three
    /// swamp spirit species, the desert species (muscle 11) and their homes. Called from
    /// PlaceholderArtGenerator.Generate (before its importer pass, so these
    /// files are configured with the rest). Self-contained helpers on purpose:
    /// PlaceholderArtGenerator's painters are private and that file is shared.
    /// Road props, the toll imp, the ambusher and the mount are painted at
    /// runtime instead (World/FrontierArt.cs).
    /// </summary>
    public static class FrontierArtGenerator
    {
        private const string Dir = "Assets/_Game/Art/Placeholder";

        public static void Generate()
        {
            Directory.CreateDirectory(Dir);

            WriteBogwick("bogwick_body", 36, new Color(0.62f, 0.86f, 0.74f));
            WriteReedhen("reedhen_body", 40, new Color(0.80f, 0.84f, 0.72f));
            WriteSloughling("sloughling_body", 36, new Color(0.56f, 0.66f, 0.52f));
            WriteScorpse("scorpse_body", 38, new Color(0.90f, 0.86f, 0.74f));

            WriteHome("home_bogwick", 34, new Color(0.46f, 0.62f, 0.56f), new Color(0.26f, 0.40f, 0.38f));
            WriteHome("home_reedhen", 36, new Color(0.70f, 0.70f, 0.56f), new Color(0.44f, 0.42f, 0.30f));
            WriteHome("home_sloughling", 34, new Color(0.46f, 0.52f, 0.40f), new Color(0.28f, 0.34f, 0.24f));
            WriteHome("home_scorpse", 34, new Color(0.80f, 0.70f, 0.50f), new Color(0.56f, 0.44f, 0.30f));
        }

        // ---- spirit bodies ---------------------------------------------------------

        /// <summary>Candle-wick wisp: a teardrop glow with a flame lick on top.</summary>
        private static void WriteBogwick(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            Blob(tex, size * 0.5f, size * 0.38f, size * 0.26f, size * 0.26f, c);   // body
            Blob(tex, size * 0.5f, size * 0.66f, size * 0.12f, size * 0.20f,
                new Color(c.r * 1.05f, c.g * 1.08f, c.b * 0.9f));                  // flame
            Blob(tex, size * 0.5f, size * 0.82f, size * 0.05f, size * 0.10f,
                new Color(0.95f, 0.98f, 0.80f));                                    // tip
            Eyes(tex, size * 0.5f, size * 0.40f, size * 0.10f);
            Save(tex, name);
        }

        /// <summary>Wading bird: round body, long neck and a pointed beak.</summary>
        private static void WriteReedhen(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            Blob(tex, size * 0.42f, size * 0.36f, size * 0.26f, size * 0.20f, c);   // body
            Blob(tex, size * 0.66f, size * 0.56f, size * 0.07f, size * 0.22f, c);   // neck
            Blob(tex, size * 0.72f, size * 0.78f, size * 0.10f, size * 0.09f, c);   // head
            Blob(tex, size * 0.86f, size * 0.76f, size * 0.07f, size * 0.03f,
                new Color(0.86f, 0.70f, 0.40f));                                    // beak
            Blob(tex, size * 0.36f, size * 0.12f, size * 0.025f, size * 0.12f,
                new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.6f));                     // legs
            Blob(tex, size * 0.50f, size * 0.12f, size * 0.025f, size * 0.12f,
                new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.6f));
            Eyes(tex, size * 0.74f, size * 0.80f, size * 0.0f);
            Save(tex, name);
        }

        /// <summary>Squat mud-toad: a wide low body with bulging eyes.</summary>
        private static void WriteSloughling(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            Blob(tex, size * 0.5f, size * 0.34f, size * 0.34f, size * 0.24f, c);    // body
            Blob(tex, size * 0.34f, size * 0.56f, size * 0.10f, size * 0.10f, c);   // eye bumps
            Blob(tex, size * 0.66f, size * 0.56f, size * 0.10f, size * 0.10f, c);
            Eyes(tex, size * 0.5f, size * 0.57f, size * 0.16f);
            Save(tex, name);
        }

        /// <summary>Bone-white scorpion: a low round body, a tail arching up and over, two little claws.</summary>
        private static void WriteScorpse(string name, int size, Color c)
        {
            var tex = ClearTex(size, size);
            var bone = new Color(c.r * 0.92f, c.g * 0.90f, c.b * 0.84f);
            Blob(tex, size * 0.44f, size * 0.30f, size * 0.26f, size * 0.17f, c);       // body
            Blob(tex, size * 0.74f, size * 0.30f, size * 0.09f, size * 0.07f, bone);    // left-front claw knob
            Blob(tex, size * 0.80f, size * 0.42f, size * 0.07f, size * 0.07f, bone);    // claw tip
            Blob(tex, size * 0.22f, size * 0.46f, size * 0.07f, size * 0.09f, bone);    // tail base
            Blob(tex, size * 0.18f, size * 0.62f, size * 0.06f, size * 0.09f, bone);    // tail rise
            Blob(tex, size * 0.26f, size * 0.77f, size * 0.07f, size * 0.06f, bone);    // tail curl
            Blob(tex, size * 0.38f, size * 0.79f, size * 0.04f, size * 0.05f,
                new Color(0.96f, 0.92f, 0.78f));                                         // stinger
            Blob(tex, size * 0.34f, size * 0.10f, size * 0.025f, size * 0.07f, bone);   // legs
            Blob(tex, size * 0.46f, size * 0.10f, size * 0.025f, size * 0.07f, bone);
            Blob(tex, size * 0.58f, size * 0.10f, size * 0.025f, size * 0.07f, bone);
            Eyes(tex, size * 0.56f, size * 0.36f, size * 0.07f);
            Save(tex, name);
        }

        /// <summary>Little house: walls, triangular roof, dark door (same slot layout as the prairie homes).</summary>
        private static void WriteHome(string name, int size, Color body, Color roof)
        {
            var tex = ClearTex(size, size);
            int wallTop = (int)(size * 0.55f);
            for (int y = 2; y <= wallTop; y++)
            for (int x = 3; x < size - 3; x++)
            {
                float shade = Mathf.Lerp(0.85f, 1.08f, y / (float)wallTop);
                tex.SetPixel(x, y, new Color(body.r * shade, body.g * shade, body.b * shade, 1f));
            }
            int roofH = size - 2 - wallTop;
            for (int i = 0; i < roofH; i++)
            {
                int inset = 1 + (int)(i * (size * 0.5f - 2) / roofH);
                for (int x = inset; x < size - inset; x++)
                    tex.SetPixel(x, wallTop + 1 + i, roof);
            }
            var dark = new Color(0.15f, 0.12f, 0.18f, 1f);
            int mid = size / 2;
            for (int y = 2; y <= size / 4 + 2; y++)
            for (int x = mid - 2; x <= mid + 2; x++)
                tex.SetPixel(x, y, dark);
            Save(tex, name);
        }

        // ---- painting helpers (mirrors of PlaceholderArtGenerator's blob writers) ------

        private static void Blob(Texture2D tex, float cx, float cy, float rx, float ry, Color c)
        {
            int w = tex.width, h = tex.height;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - cx) / Mathf.Max(0.01f, rx), dy = (y - cy) / Mathf.Max(0.01f, ry);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 1.08f) continue;
                float a = Mathf.Clamp01((1.08f - d) * 6f);
                float shade = Mathf.Lerp(0.85f, 1.12f, Mathf.InverseLerp(cy - ry, cy + ry, y));
                var px = tex.GetPixel(x, y);
                if (a >= px.a) tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, Mathf.Max(px.a, a)));
            }
        }

        private static void Eyes(Texture2D tex, float cx, float cy, float spread)
        {
            var dark = new Color(0.12f, 0.10f, 0.18f, 1f);
            for (int s = -1; s <= 1; s += 2)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x * x + y * y > 1) continue;
                int px = (int)(cx + s * spread) + x, py = (int)cy + y;
                if (px >= 0 && px < tex.width && py >= 0 && py < tex.height) tex.SetPixel(px, py, dark);
            }
        }

        private static Texture2D ClearTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, Color.clear);
            return tex;
        }

        private static void Save(Texture2D tex, string name)
        {
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}

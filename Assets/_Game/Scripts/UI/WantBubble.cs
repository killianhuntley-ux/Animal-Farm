using UnityEngine;

namespace AnimalFarm.UI
{
    /// <summary>The four legible spirit wants a bubble can show.</summary>
    public enum WantKind { Food = 0, Water = 1, Lonely = 2, Home = 3 }

    /// <summary>
    /// Small world-space thought bubble above a spirit (muscle 03). Built once
    /// per spirit and refreshed in place: Show() swaps the icon sprite and
    /// restarts the fade envelope; nothing is re-instantiated. Icons are tiny
    /// procedural sprites (circle-on-stem = food, droplet = water, two dots =
    /// lonely, roofed hut = home) generated once and shared by every bubble -
    /// greybox placeholders until the art pass.
    /// </summary>
    public class WantBubble : MonoBehaviour
    {
        private const float FadeInSeconds = 0.15f;
        private const float FadeOutSeconds = 0.35f;
        private const float BubbleAlpha = 0.92f;
        private const float IconAlpha = 0.95f;
        private const int Tex = 32; // icon/bubble texture size in pixels

        private static Sprite _bubbleSprite;
        private static readonly Sprite[] _iconSprites = new Sprite[4];

        private SpriteRenderer _bg;
        private SpriteRenderer _icon;
        private float _timer = -1f; // -1 = hidden
        private float _duration;
        private bool _visible;

        /// <summary>
        /// Builds (once) a bubble under the given parent, floating yOffset
        /// local units above its origin. Returns the existing instance when
        /// one is already attached.
        /// </summary>
        public static WantBubble Attach(Transform parent, float yOffset)
        {
            if (parent == null) return null;

            var existing = parent.Find("WantBubble");
            if (existing != null)
            {
                var b = existing.GetComponent<WantBubble>();
                if (b != null) return b;
            }

            var go = new GameObject("WantBubble");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);
            go.transform.localScale = Vector3.one * 0.5f;

            var bubble = go.AddComponent<WantBubble>();
            bubble._bg = go.AddComponent<SpriteRenderer>();
            bubble._bg.sprite = BubbleSprite();
            bubble._bg.sortingOrder = 400; // above spirits, below FloatingText (500)
            bubble._bg.color = new Color(0.96f, 0.96f, 0.9f, 0f);

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            iconGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            iconGo.transform.localScale = Vector3.one * 0.62f;
            bubble._icon = iconGo.AddComponent<SpriteRenderer>();
            bubble._icon.sortingOrder = 401;
            bubble._icon.color = new Color(0.22f, 0.24f, 0.35f, 0f);

            return bubble;
        }

        /// <summary>Shows (or re-shows) the bubble with the given want icon.</summary>
        public void Show(WantKind kind, float seconds)
        {
            if (_icon != null) _icon.sprite = IconSprite(kind);
            _duration = Mathf.Max(0.5f, seconds);
            _timer = 0f;
            _visible = true;
        }

        /// <summary>Hides immediately (sleep, ceremony, state changes).</summary>
        public void HideNow()
        {
            if (!_visible) return;
            _visible = false;
            _timer = -1f;
            ApplyAlpha(0f);
        }

        private void Update()
        {
            if (_timer < 0f) return;
            _timer += Time.deltaTime;

            float a;
            if (_timer >= _duration)
            {
                _timer = -1f;
                _visible = false;
                a = 0f;
            }
            else
            {
                float fadeIn = Mathf.Clamp01(_timer / FadeInSeconds);
                float fadeOut = Mathf.Clamp01((_duration - _timer) / FadeOutSeconds);
                a = Mathf.Min(fadeIn, fadeOut);
            }
            ApplyAlpha(a);
        }

        private void ApplyAlpha(float a)
        {
            if (_bg != null)
            {
                var c = _bg.color; c.a = BubbleAlpha * a; _bg.color = c;
            }
            if (_icon != null)
            {
                var c = _icon.color; c.a = IconAlpha * a; _icon.color = c;
            }
        }

        // ---- procedural sprites (generated once, shared) ----------------------

        private static Sprite BubbleSprite()
        {
            if (_bubbleSprite == null)
                _bubbleSprite = MakeSprite((x, y) => Disc(x, y, 15.5f, 15.5f, 14.5f));
            return _bubbleSprite;
        }

        private static Sprite IconSprite(WantKind kind)
        {
            int i = Mathf.Clamp((int)kind, 0, _iconSprites.Length - 1);
            if (_iconSprites[i] != null) return _iconSprites[i];

            System.Func<float, float, float> f;
            switch ((WantKind)i)
            {
                case WantKind.Food: // round fruit on a little stem
                    f = (x, y) => Mathf.Max(
                        Disc(x, y, 15.5f, 13f, 7f),
                        Box(x, y, 14.7f, 19.5f, 16.3f, 25f));
                    break;
                case WantKind.Water: // droplet: round belly, point up
                    f = (x, y) => Mathf.Max(
                        Disc(x, y, 15.5f, 12f, 6.5f),
                        TriUp(x, y, 15.5f, 24.5f, 13f, 5.5f));
                    break;
                case WantKind.Lonely: // two dots, apart
                    f = (x, y) => Mathf.Max(
                        Disc(x, y, 10f, 16f, 3.2f),
                        Disc(x, y, 21f, 16f, 3.2f));
                    break;
                default: // WantKind.Home: triangle roof over a little wall
                    f = (x, y) => Mathf.Max(
                        TriUp(x, y, 15.5f, 25f, 14f, 10f),
                        Box(x, y, 10f, 7f, 21f, 14f));
                    break;
            }

            _iconSprites[i] = MakeSprite(f);
            return _iconSprites[i];
        }

        /// <summary>White sprite whose alpha comes from a coverage function
        /// (values clamp to 0..1); the renderer's color supplies the tint.</summary>
        private static Sprite MakeSprite(System.Func<float, float, float> coverage)
        {
            var tex = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var px = new Color[Tex * Tex];
            for (int y = 0; y < Tex; y++)
                for (int x = 0; x < Tex; x++)
                    px[x + y * Tex] = new Color(1f, 1f, 1f, Mathf.Clamp01(coverage(x, y)));
            tex.SetPixels(px);
            tex.Apply(false, true);

            return Sprite.Create(tex, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), Tex);
        }

        // Soft-edged shape coverage helpers (+0.5 gives a ~1px antialias rim).

        private static float Disc(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f;
        }

        private static float Box(float x, float y, float x0, float y0, float x1, float y1) =>
            Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(y - y0, y1 - y)) + 0.5f;

        /// <summary>Triangle pointing up: apex at (apexX, apexY), halfBase wide at baseY.</summary>
        private static float TriUp(float x, float y, float apexX, float apexY, float baseY, float halfBase)
        {
            if (y > apexY + 0.5f || y < baseY - 0.5f) return 0f;
            float t = Mathf.Clamp01((apexY - y) / Mathf.Max(0.01f, apexY - baseY));
            return halfBase * t - Mathf.Abs(x - apexX) + 0.5f;
        }
    }
}

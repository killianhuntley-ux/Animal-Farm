using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The buildable ascension site (muscle 04, verdict 3): you build the spot
    /// when you unlock the moment, place it where it feels right, and decorate
    /// around it. Replaces the fixed bootstrapped altar as the ceremony anchor.
    /// Lead a fulfilled resident here (following) and interact to begin the
    /// Styx crossing (verdict 4). Unfulfilled attempts fail softly with a hint
    /// about what is still missing. Sprites are GENERATED in code, so the pad
    /// needs no bootstrapper wiring and works in any scene.
    /// </summary>
    public class AscensionPad : MonoBehaviour, IInteractable
    {
        private const float NearRadius = 2.6f;

        private static readonly Color IdleGlow = new Color(0.75f, 0.85f, 1f, 0.3f);
        private static readonly Color GoldGlow = new Color(1f, 0.84f, 0.45f, 0.9f);
        private static readonly Color PaleGreyBlue = new Color(0.70f, 0.75f, 0.85f, 1f);

        private SpriteRenderer _platform;
        private SpriteRenderer _glow;
        private SpiritAgent _nearSpirit;
        private float _flickerUntil;
        private bool _focused;

        // ---- generated sprites ------------------------------------------------

        private static Sprite _platformSprite;
        private static Sprite _glowSprite;

        /// <summary>Pale stone slab, ~2.2x1.2 world units (also the build ghost).</summary>
        public static Sprite PlatformSprite
        {
            get
            {
                if (_platformSprite == null)
                    _platformSprite = MakePlatformSprite();
                return _platformSprite;
            }
        }

        /// <summary>Soft radial glow, ~2 world units, white (tint at the renderer).</summary>
        public static Sprite GlowSprite
        {
            get
            {
                if (_glowSprite == null)
                    _glowSprite = MakeRadialGlowSprite(64, 32f);
                return _glowSprite;
            }
        }

        private static Sprite MakePlatformSprite()
        {
            const int w = 56, h = 30;
            var baseCol = new Color(0.62f, 0.66f, 0.76f, 1f); // pale river-stone
            var rimCol = new Color(0.45f, 0.49f, 0.60f, 1f);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            float radius = 9f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Rounded-rect SDF: distance outside the inset box corners.
                    float dx = Mathf.Max(Mathf.Abs(x - (w - 1) * 0.5f) - ((w - 1) * 0.5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y - (h - 1) * 0.5f) - ((h - 1) * 0.5f - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01((radius - d) / 2f);       // 2px soft edge
                    float rim = Mathf.Clamp01((radius - d - 2.5f) / 2f);  // darker ring inside
                    var c = Color.Lerp(rimCol, baseCol, rim);
                    c.a = alpha;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 25f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Shared radial-glow generator (pad glow + ceremony light pools).</summary>
        public static Sprite MakeRadialGlowSprite(int size, float ppu)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Clamp01(1f - r);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- lifecycle ----------------------------------------------------------

        /// <summary>Runtime factory (Waystone pattern): build menu + save restore.</summary>
        public static AscensionPad Create(Vector3 pos)
        {
            var go = new GameObject("AscensionPad");
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            return go.AddComponent<AscensionPad>();
        }

        private void Awake()
        {
            // Register in Awake, not Start: the manager's Restore respawns
            // pads and must be able to Capture them again the same frame.
            if (AscensionPadManager.Instance != null)
                AscensionPadManager.Instance.Register(this);
        }

        private void Start()
        {
            _platform = MakeChild("Platform", PlatformSprite, -5);
            _glow = MakeChild("Glow", GlowSprite, -4);
            _glow.transform.localScale = new Vector3(1.4f, 1.1f, 1f);

            WorldLabel.Attach(gameObject, "Ascension Pad", -1.0f);

            // Trigger volume for the InteractionSensor.
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(2.2f, 1.6f);
        }

        private void OnDestroy()
        {
            if (AscensionPadManager.Instance != null)
                AscensionPadManager.Instance.Unregister(this);
        }

        private SpriteRenderer MakeChild(string childName, Sprite sprite, int order)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        private void Update()
        {
            RefreshNearSpirit();
            UpdateGlow();
        }

        /// <summary>Nearest FOLLOWING spirit within range, or null.</summary>
        private void RefreshNearSpirit()
        {
            _nearSpirit = null;

            var manager = SpiritManager.Instance;
            if (manager == null) return;

            var spirits = manager.AllSpirits;
            if (spirits == null) return;

            float bestSqr = NearRadius * NearRadius;
            for (int i = 0; i < spirits.Count; i++)
            {
                var agent = spirits[i];
                if (agent == null || !agent.IsFollowing) continue;

                float sqr = (agent.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; _nearSpirit = agent; }
            }
        }

        /// <summary>
        /// Idle: soft slow pulse. Fulfilled spirit near (or crossing running):
        /// strong gold pulse. A fail beat flickers the glow briefly.
        /// </summary>
        private void UpdateGlow()
        {
            if (_glow == null) return;

            float alpha;
            Color color;
            bool charged = StyxCrossingCeremony.Running
                || (_nearSpirit != null && _nearSpirit.IsFulfilled);
            if (charged)
            {
                float wave = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;
                alpha = Mathf.Lerp(0.5f, 0.9f, wave);
                color = GoldGlow;
            }
            else
            {
                float wave = (Mathf.Sin(Time.time * 1.6f) + 1f) * 0.5f;
                alpha = Mathf.Lerp(0.15f, 0.3f, wave);
                color = IdleGlow;
            }

            if (Time.time < _flickerUntil)
                alpha *= 0.2f + 0.7f * Mathf.PingPong(Time.time * 14f, 1f);

            if (_focused) alpha = Mathf.Min(1f, alpha + 0.05f);

            _glow.color = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>Where the departing spirit stands (and the stone later drops).</summary>
        public Vector3 PlatformCenter =>
            _platform != null ? _platform.transform.position : transform.position;

        /// <summary>
        /// Ceremony sorting lift: hoists the pad's own renderers above the
        /// darkness overlay (order 300) so the pad stays lit while the world
        /// goes dark. The crossing toggles this.
        /// </summary>
        public void SetCeremonyLift(bool on)
        {
            if (_platform != null) _platform.sortingOrder = on ? 304 : -5;
            if (_glow != null) _glow.sortingOrder = on ? 305 : -4;
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText
        {
            get
            {
                if (_nearSpirit == null) return "The pad waits";
                return _nearSpirit.IsFulfilled ? "Begin the crossing" : "Attempt the crossing";
            }
        }

        public bool CanInteract(GameObject actor) =>
            _nearSpirit != null && !StyxCrossingCeremony.Running;

        public void Interact(GameObject actor)
        {
            if (StyxCrossingCeremony.Running || _nearSpirit == null) return;

            if (_nearSpirit.IsFulfilled)
                StyxCrossingCeremony.Begin(this, _nearSpirit);
            else
                FailBeat(_nearSpirit);
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
        }

        /// <summary>Soft failure: name what's missing, flicker; no state change.</summary>
        private void FailBeat(SpiritAgent spirit)
        {
            string spiritName = !string.IsNullOrEmpty(spirit.GivenName)
                ? spirit.GivenName
                : (spirit.Species != null ? spirit.Species.displayName : "The spirit");

            string hint;
            if (!spirit.HasHome)
                hint = "(" + spiritName + " needs a home)";
            else if (spirit.Spirit < 100f)
                hint = "(" + spiritName + "'s spirit is not yet full)";
            else if (!spirit.TaskDone)
                hint = spirit.Species != null && !string.IsNullOrEmpty(spirit.Species.taskHint)
                    ? spirit.Species.taskHint
                    : "(an unfinished wish lingers)";
            else
                hint = "(the river does not come)";

            FloatingText.Show(PlatformCenter + Vector3.up * 1.5f, hint, PaleGreyBlue);
            _flickerUntil = Time.time + 0.6f;
        }
    }
}

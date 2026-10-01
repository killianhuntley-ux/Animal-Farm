using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Procedural tool-swing readability pass (muscle 01). During a weighty
    /// tool action a small generated placeholder sprite (a tinted stick per
    /// tool -- no art assets) arcs over the shepherd toward the target cell,
    /// then fades out just after the contact beat and vanishes.
    ///
    /// Driven entirely by ToolController: Play() at action start with the
    /// matching duration, Cancel() if the action is ever aborted. Added at
    /// runtime next to ToolController -- no scene setup needed.
    /// </summary>
    public class ToolSwingVisual : MonoBehaviour
    {
        private const float ArcHeight = 0.45f;       // extra lift at mid-swing
        private const float StartAngle = 55f;        // degrees, wound up behind
        private const float EndAngle = -75f;         // degrees, swung through
        private const float FadeStart = 0.7f;        // fade just after contact (0.6)

        private static Sprite _square;

        private SpriteRenderer _renderer;
        private Transform _swingT;

        private bool _playing;
        private float _elapsed;
        private float _duration;
        private Vector3 _from;
        private Vector3 _to;
        private float _side;      // +1 swings right-handed, -1 mirrored
        private Color _tint;

        /// <summary>Shared 1x1-world-unit white square, tinted per tool.</summary>
        private static Sprite Square
        {
            get
            {
                if (_square == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    // 4 pixels per unit -> the 4x4 texture is exactly 1x1 world units.
                    _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    _square.hideFlags = HideFlags.HideAndDontSave;
                }
                return _square;
            }
        }

        /// <summary>Starts a swing from over the shepherd to the target cell.</summary>
        public void Play(string toolName, Vector3 from, Vector3 to, float duration)
        {
            EnsureRenderer();

            _from = from;
            _to = to;
            _duration = Mathf.Max(duration, 0.05f);
            _elapsed = 0f;
            _side = to.x < from.x ? -1f : 1f;
            _tint = TintFor(toolName);
            _playing = true;

            _renderer.color = _tint;
            _renderer.enabled = true;
            Step(0f);
        }

        /// <summary>Hides the swing immediately (aborted action).</summary>
        public void Cancel()
        {
            _playing = false;
            if (_renderer != null) _renderer.enabled = false;
        }

        private void Update()
        {
            if (!_playing) return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            Step(t);

            if (t >= 1f) Cancel();
        }

        /// <summary>Positions, rotates and fades the sprite at swing time t (0..1).</summary>
        private void Step(float t)
        {
            // Ease-in: the wind-up lingers, the swing itself snaps through.
            float eased = t * t * (3f - 2f * t);

            Vector3 pos = Vector3.Lerp(_from, _to, eased);
            pos.y += Mathf.Sin(eased * Mathf.PI) * ArcHeight;
            _swingT.position = pos;

            float angle = Mathf.Lerp(StartAngle, EndAngle, eased) * _side;
            _swingT.rotation = Quaternion.Euler(0f, 0f, angle);

            if (t > FadeStart)
            {
                var c = _tint;
                c.a = _tint.a * (1f - (t - FadeStart) / (1f - FadeStart));
                _renderer.color = c;
            }
        }

        /// <summary>Lazy child sprite: a thin stick shape (scaled unit square).</summary>
        private void EnsureRenderer()
        {
            if (_renderer != null) return;

            var go = new GameObject("ToolSwing");
            _swingT = go.transform;
            _swingT.SetParent(transform, false);
            _swingT.localScale = new Vector3(0.12f, 0.42f, 1f);

            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sprite = Square;
            _renderer.sortingOrder = 210; // above the shepherd, below floating text
            _renderer.enabled = false;
        }

        /// <summary>Placeholder tint per tool name (generated shape, no assets).</summary>
        private static Color TintFor(string toolName)
        {
            switch (toolName)
            {
                case ToolController.ToolHoe: return new Color(0.62f, 0.42f, 0.22f, 1f);  // wood
                case ToolController.ToolWaterPail: return new Color(0.45f, 0.75f, 1f, 1f); // water blue
                case ToolController.ToolShovel: return new Color(0.66f, 0.68f, 0.73f, 1f); // steel
                default: return new Color(0.95f, 0.94f, 0.88f, 1f);  // cream
            }
        }
    }
}

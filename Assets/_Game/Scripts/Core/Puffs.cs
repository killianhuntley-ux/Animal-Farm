using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Fire-and-forget procedural particle puffs (muscle 01). The game ships no
    /// particle assets, so each burst is a handful of tiny generated-sprite
    /// quads that scatter, slow down, shrink and fade over ~a third of a
    /// second. Used for tool-contact dirt/splash puffs and sprint dust.
    ///
    /// Usage: Puffs.Burst(worldPos, color); from anywhere, any time.
    /// </summary>
    public static class Puffs
    {
        private const int MaxLiveBursts = 10;
        private static int _liveBursts;

        private static Sprite _square;

        /// <summary>Shared 1x1-world-unit white square (tinted per particle).</summary>
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

        /// <summary>
        /// Spawns a small scatter of fading squares at a world position.
        /// Cheap and capped -- bursts beyond the live cap are silently dropped.
        /// </summary>
        public static void Burst(Vector3 worldPos, Color color, int count = 6,
            float speed = 1.4f, float life = 0.35f, float size = 0.1f)
        {
            if (!Application.isPlaying || _liveBursts >= MaxLiveBursts) return;
            _liveBursts++;

            var go = new GameObject("Puff");
            go.transform.position = worldPos;
            var runner = go.AddComponent<Runner>();
            runner.Init(color, Mathf.Clamp(count, 1, 16), speed, life, size);
        }

        /// <summary>Owns one burst: moves, shrinks and fades its quads, then dies.</summary>
        private class Runner : MonoBehaviour
        {
            private Transform[] _parts;
            private SpriteRenderer[] _renderers;
            private Vector3[] _velocities;
            private Color _color;
            private float _life;
            private float _size;
            private float _elapsed;

            public void Init(Color color, int count, float speed, float life, float size)
            {
                _color = color;
                _life = Mathf.Max(life, 0.05f);
                _size = size;

                _parts = new Transform[count];
                _renderers = new SpriteRenderer[count];
                _velocities = new Vector3[count];

                for (int i = 0; i < count; i++)
                {
                    var part = new GameObject("p");
                    part.transform.SetParent(transform, false);

                    var sr = part.AddComponent<SpriteRenderer>();
                    sr.sprite = Square;
                    sr.color = color;
                    sr.sortingOrder = 240; // above terrain/plants, below floating text (500)

                    Vector2 dir = Random.insideUnitCircle.normalized;
                    // Slight upward bias so puffs read as kicked-up dust.
                    Vector3 vel = (Vector3)(dir * speed * Random.Range(0.45f, 1f))
                                  + Vector3.up * speed * 0.25f;

                    part.transform.localScale = Vector3.one * (size * Random.Range(0.7f, 1.2f));
                    _parts[i] = part.transform;
                    _renderers[i] = sr;
                    _velocities[i] = vel;
                }
            }

            private void Update()
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / _life);
                float damping = 1f - 5f * Time.deltaTime;
                if (damping < 0f) damping = 0f;

                for (int i = 0; i < _parts.Length; i++)
                {
                    if (_parts[i] == null) continue;
                    _velocities[i] *= damping;
                    _parts[i].position += _velocities[i] * Time.deltaTime;
                    _parts[i].localScale = Vector3.one * (_size * (1f - t * 0.6f));

                    var c = _color;
                    c.a = _color.a * (1f - t);
                    _renderers[i].color = c;
                }

                if (_elapsed >= _life) Destroy(gameObject);
            }

            private void OnDestroy()
            {
                _liveBursts = Mathf.Max(0, _liveBursts - 1);
            }
        }
    }
}

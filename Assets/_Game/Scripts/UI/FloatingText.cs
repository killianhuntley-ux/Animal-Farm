using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Fire-and-forget floating world text ("Wheat +1", "Pip!") built on the
    /// legacy 3D TextMesh so it lives in world space, above the sprites.
    /// Runs on scaled time, so it freezes with the game while paused.
    /// </summary>
    public static class FloatingText
    {
        private const int MaxLive = 12;
        private const float RiseDistance = 1.0f;
        private const float Lifetime = 1.1f;

        private static readonly List<GameObject> Live = new List<GameObject>();

        /// <summary>Spawns rising, fading text at a world position.</summary>
        public static void Show(Vector3 worldPos, string text, Color color)
        {
            // Cap the number of simultaneous texts; drop the oldest first.
            Live.RemoveAll(go => go == null);
            while (Live.Count >= MaxLive)
            {
                var oldest = Live[0];
                Live.RemoveAt(0);
                if (oldest != null) Object.Destroy(oldest);
            }

            var go = new GameObject("FloatingText");
            go.transform.position = worldPos;

            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.text = text;
            mesh.characterSize = 0.08f;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;

            // TextMesh renders via a MeshRenderer; hoist it above the sprites.
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = mesh.font.material;
                renderer.sortingOrder = 500;
            }

            var runner = go.AddComponent<Runner>();
            runner.Init(mesh, color);

            Live.Add(go);
        }

        /// <summary>Rises 1 unit over the lifetime while fading out, then dies.</summary>
        private class Runner : MonoBehaviour
        {
            private TextMesh _mesh;
            private Color _baseColor;
            private Vector3 _start;
            private float _elapsed;

            public void Init(TextMesh mesh, Color color)
            {
                _mesh = mesh;
                _baseColor = color;
                _start = transform.position;
            }

            private void Update()
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / Lifetime);

                transform.position = _start + Vector3.up * (RiseDistance * t);

                if (_mesh != null)
                {
                    float alpha = _baseColor.a * (1f - t);
                    _mesh.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
                }

                if (_elapsed >= Lifetime) Destroy(gameObject);
            }

            private void OnDestroy()
            {
                Live.Remove(gameObject);
            }
        }
    }
}

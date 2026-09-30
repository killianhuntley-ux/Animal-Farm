using UnityEngine;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Small always-on world-space name label under important objects (altar,
    /// homes, headstones, waystone) so greybox shapes are identifiable.
    /// </summary>
    public static class WorldLabel
    {
        public static void Attach(GameObject target, string text, float yOffset = -0.8f)
        {
            if (target == null || string.IsNullOrEmpty(text)) return;

            // Don't double-attach.
            var existing = target.transform.Find("WorldLabel");
            if (existing != null)
            {
                var existingMesh = existing.GetComponent<TextMesh>();
                if (existingMesh != null) existingMesh.text = text;
                return;
            }

            var go = new GameObject("WorldLabel");
            go.transform.SetParent(target.transform, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.fontSize = 48;
            mesh.characterSize = 0.045f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.92f, 0.92f, 0.85f, 0.85f);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 200;
            if (mesh.font != null) renderer.sharedMaterial = mesh.font.material;

            go.AddComponent<ProximityFade>();
        }

        /// <summary>
        /// Labels fade in as the shepherd approaches (UX fix: clustered homes
        /// produced overlapping label soup). Fully visible within 4 units,
        /// gone beyond 8.
        /// </summary>
        private class ProximityFade : MonoBehaviour
        {
            private const float NearDist = 4f, FarDist = 8f;
            private TextMesh _mesh;
            private Transform _player;
            private float _baseAlpha;

            private void Start()
            {
                _mesh = GetComponent<TextMesh>();
                _baseAlpha = _mesh != null ? _mesh.color.a : 1f;
                var p = GameObject.FindWithTag("Player");
                if (p != null) _player = p.transform;
            }

            private void LateUpdate()
            {
                if (_mesh == null) return;
                if (_player == null)
                {
                    var p = GameObject.FindWithTag("Player");
                    if (p == null) return;
                    _player = p.transform;
                }

                float d = Vector2.Distance(_player.position, transform.position);
                float a = _baseAlpha * Mathf.Clamp01(Mathf.InverseLerp(FarDist, NearDist, d));
                var c = _mesh.color;
                if (!Mathf.Approximately(c.a, a)) _mesh.color = new Color(c.r, c.g, c.b, a);
            }
        }
    }
}

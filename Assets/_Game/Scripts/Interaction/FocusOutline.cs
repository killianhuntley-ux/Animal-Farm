using UnityEngine;

namespace AnimalFarm.Interaction
{
    /// <summary>
    /// Shared focus outline (muscle 01 item 11). InteractionSensor calls
    /// FocusOutline.Set(target, true/false) on the focused IInteractable, so no
    /// implementor needs to know about it. The outline is four offset
    /// silhouettes (up / down / left / right) of the target's main sprite,
    /// tinted bright and drawn one order behind it. They live on a hidden child
    /// object under the sprite's transform (so they inherit scale bumps, bob,
    /// flip and tilt) and re-sync sprite / flip / alpha / sorting every
    /// LateUpdate, which keeps them correct through animation and growth stages.
    /// The silhouettes reuse the source renderer's material (no shader lookups).
    /// </summary>
    public class FocusOutline : MonoBehaviour
    {
        public static readonly Color OutlineColor = new Color(1f, 0.9f, 0.4f, 1f);

        private const string RootName = "FocusOutline";
        private const float Thickness = 0.04f; // world units per silhouette offset

        private static readonly Vector2[] Dirs =
        {
            new Vector2(0f, 1f), new Vector2(0f, -1f), new Vector2(-1f, 0f), new Vector2(1f, 0f)
        };

        private SpriteRenderer _source;
        private readonly SpriteRenderer[] _parts = new SpriteRenderer[4];

        // ---- public API ----------------------------------------------------

        /// <summary>Shows or hides the outline on an interactable's main sprite.
        /// Safe on destroyed targets, targets without a sprite, and repeated calls.</summary>
        public static void Set(IInteractable target, bool on)
        {
            if (target == null) return;
            var comp = target as Component;
            if (comp == null) return; // not a Unity object, or destroyed

            var source = FindMainRenderer(comp.gameObject);
            if (source == null) return;

            var existing = FindExisting(source.transform);
            if (existing != null)
            {
                existing.gameObject.SetActive(on);
                return;
            }

            if (!on) return;

            var go = new GameObject(RootName);
            go.transform.SetParent(source.transform, false);
            var outline = go.AddComponent<FocusOutline>();
            outline.Init(source);
        }

        // ---- internals -------------------------------------------------------

        private static FocusOutline FindExisting(Transform sourceTransform)
        {
            var child = sourceTransform.Find(RootName);
            return child != null ? child.GetComponent<FocusOutline>() : null;
        }

        private static bool IsOutlinePart(SpriteRenderer sr) =>
            sr.transform.name == RootName
            || (sr.transform.parent != null && sr.transform.parent.name == RootName);

        /// <summary>Largest visible body sprite under the object (glows and shadows skipped).</summary>
        private static SpriteRenderer FindMainRenderer(GameObject root)
        {
            var own = root.GetComponent<SpriteRenderer>();
            if (own != null && own.sprite != null) return own;

            SpriteRenderer best = null;
            float bestArea = 0f;
            var all = root.GetComponentsInChildren<SpriteRenderer>(false);
            for (int i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null || sr.sprite == null || !sr.enabled || IsOutlinePart(sr)) continue;
                string n = sr.gameObject.name.ToLowerInvariant();
                if (n.Contains("shadow") || n.Contains("glow")) continue;

                Vector3 size = sr.bounds.size;
                float area = size.x * size.y;
                if (area > bestArea) { bestArea = area; best = sr; }
            }
            return best;
        }

        private void Init(SpriteRenderer source)
        {
            _source = source;
            for (int i = 0; i < _parts.Length; i++)
            {
                var go = new GameObject("Part" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = source.sharedMaterial;
                _parts[i] = sr;
            }
            Sync();
        }

        private void OnEnable()
        {
            if (_source != null) Sync(); // re-shown after a hide: never flash a stale sprite
        }

        private void LateUpdate()
        {
            if (_source == null) { Destroy(gameObject); return; }
            Sync();
        }

        private void Sync()
        {
            var parent = transform.parent;
            for (int i = 0; i < _parts.Length; i++)
            {
                var sr = _parts[i];
                if (sr == null) continue;

                sr.sprite = _source.sprite;
                sr.flipX = _source.flipX;
                sr.flipY = _source.flipY;
                sr.sortingLayerID = _source.sortingLayerID;
                sr.sortingOrder = _source.sortingOrder - 1;
                sr.enabled = _source.enabled && _source.sprite != null;

                Color c = OutlineColor;
                c.a = _source.color.a; // fading spirits fade their outline too
                sr.color = c;

                // World-space offset expressed in the sprite's local space, so
                // scale bumps and tilt never change the visual thickness.
                Vector3 world = Dirs[i] * Thickness;
                sr.transform.localPosition = parent != null
                    ? parent.InverseTransformVector(world)
                    : (Vector3)world;
            }
        }
    }
}

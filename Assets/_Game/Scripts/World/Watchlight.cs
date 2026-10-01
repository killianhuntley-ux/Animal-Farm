using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// A warded lantern post (WEEDS slice): suppresses weed spawns within
    /// <see cref="WardRadius"/> units. Bought at the vendor, placed near the
    /// shepherd. Click-selectable: ward-range label + two-step Destroy (Home
    /// pattern).
    /// NOT saved this slice - the muscle phase persists placements; losing a
    /// 30-obol post on reload is a cheap, acceptable loss for the skeleton.
    /// </summary>
    public class Watchlight : MonoBehaviour, ISelectable
    {
        /// <summary>Weeds refuse to sprout within this many units.</summary>
        public const float WardRadius = 6f;

        private static readonly List<Watchlight> _all = new List<Watchlight>();

        /// <summary>Every live, active watchlight (OnEnable/OnDisable registry).</summary>
        public static IReadOnlyList<Watchlight> All => _all;

        [SerializeField] private Sprite sprite;         // set by the spawner (PlaceAt)
        [SerializeField] private Material spriteMaterial;

        private SpriteRenderer _glow;
        private float _glowPhase;
        private bool _confirmingDestroy;

        /// <summary>
        /// Spawns a watchlight at a world position, grid-snapped when the
        /// TerrainGrid exists. Returns null (placed nothing) if the cell is
        /// locked/out of bounds, Water, or already holds a plant or a home.
        /// </summary>
        public static Watchlight PlaceAt(Vector3 pos, Sprite sprite, Material mat)
        {
            var grid = TerrainGrid.Instance;
            if (grid != null)
            {
                if (!grid.TryWorldToCell(pos, out var cell)) return null;
                if (!grid.IsUsable(cell)) return null;
                if (grid.GetSurface(cell) == Surface.Water) return null;
                if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return null;
                if (AnimalFarm.Spirits.Home.AnyAtCell(cell)) return null;
                if (VillainHoles.BlocksCell(cell)) return null;
                pos = grid.CellCenterWorld(cell);
            }

            var go = new GameObject("Watchlight");
            go.transform.position = pos;
            var light = go.AddComponent<Watchlight>();
            light.Init(sprite, mat);
            return light;
        }

        private void Init(Sprite s, Material mat)
        {
            sprite = s;
            spriteMaterial = mat;

            transform.localScale = Vector3.one * 1.6f; // fixture-sized, like the stall

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 1;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;

            // Warm glow child: same sprite, bigger, tinted amber, alpha-pulsed.
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localScale = Vector3.one * 1.5f;
            _glow = glowGo.AddComponent<SpriteRenderer>();
            _glow.sprite = sprite;
            _glow.sortingOrder = 0; // behind the post
            _glow.color = new Color(1f, 0.82f, 0.45f, 0.25f);
            if (spriteMaterial != null) _glow.sharedMaterial = spriteMaterial;
            _glowPhase = Random.Range(0f, Mathf.PI * 2f);

            WorldLabel.Attach(gameObject, "Watchlight", -0.7f);
        }

        private void Update()
        {
            if (_glow == null) return;
            float a = 0.18f + 0.12f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f + _glowPhase));
            var c = _glow.color;
            _glow.color = new Color(c.r, c.g, c.b, a);
        }

        // ---- ISelectable -------------------------------------------------------

        public string SelectableTitle => "Watchlight";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;

            // Label-only no-op (keeps the menu open).
            into.Add(new SelectAction("(wards 6 paces)", () => { }, false));

            // Two-step destroy: first click relabels, second click commits.
            if (!_confirmingDestroy)
            {
                into.Add(new SelectAction("Destroy", () => { _confirmingDestroy = true; }, false));
            }
            else
            {
                into.Add(new SelectAction("Really destroy?", () =>
                {
                    _confirmingDestroy = false;
                    Destroy(gameObject);
                }));
            }
        }

        private void OnEnable() => _all.Add(this);

        private void OnDisable() => _all.Remove(this);
    }
}

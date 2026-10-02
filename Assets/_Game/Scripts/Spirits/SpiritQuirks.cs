using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Shared idle micro-moment library + habitat-anchor lookups (muscle 03).
    /// Pure static helpers: SpiritAgent asks Pick() for a weighted quirk and
    /// TryFindHabitatPoint() for a species hangout; execution lives on the
    /// agent. Anchors are discovered defensively by name/registry - a scene
    /// without rocks, watchlights, or ponds simply skips the habit.
    /// </summary>
    public static class SpiritQuirks
    {
        /// <summary>
        /// Micro-moment kinds. Pick() returns only the four self-quirks;
        /// CirclePlay/Squabble are pair moves started by the
        /// SpiritSocialManager and share the agent's single quirk slot.
        /// </summary>
        public enum Kind { None, Nap, Stretch, Hop, LeafChase, CirclePlay, Squabble, Train }

        private const float AnchorRescanSeconds = 12f;

        private static readonly List<Transform> _rocks = new List<Transform>();
        private static float _nextRockScan;
        private static readonly List<Vector2Int> _waterCells = new List<Vector2Int>();
        private static float _nextWaterScan;

        // ---- quirk picking ---------------------------------------------------

        /// <summary>Weighted pick from the species' quirk frequency fields.</summary>
        public static Kind Pick(SpiritSpeciesDefinition species) => Pick(species, null);

        /// <summary>Weighted pick: species weights x the individual's trait multipliers (muscle 05).</summary>
        public static Kind Pick(SpiritSpeciesDefinition species, IReadOnlyList<SpiritTraitDefinition> traits)
        {
            float nap = (species != null ? Mathf.Max(0f, species.napWeight) : 1f)
                * SpiritTraits.QuirkMul(traits, Kind.Nap);
            float stretch = (species != null ? Mathf.Max(0f, species.stretchWeight) : 1f)
                * SpiritTraits.QuirkMul(traits, Kind.Stretch);
            float hop = (species != null ? Mathf.Max(0f, species.hopWeight) : 1f)
                * SpiritTraits.QuirkMul(traits, Kind.Hop);
            float leaf = (species != null ? Mathf.Max(0f, species.leafChaseWeight) : 1f)
                * SpiritTraits.QuirkMul(traits, Kind.LeafChase);

            float total = nap + stretch + hop + leaf;
            if (total <= 0f) return Kind.None;

            float roll = Random.value * total;
            if ((roll -= nap) < 0f) return Kind.Nap;
            if ((roll -= stretch) < 0f) return Kind.Stretch;
            if ((roll -= hop) < 0f) return Kind.Hop;
            return Kind.LeafChase;
        }

        // ---- habitat anchors ---------------------------------------------------

        /// <summary>
        /// Finds a point near a matching habitat anchor, or false when the
        /// scene has none (or the preference only applies at night and it is
        /// day). Anchor sets are cached and rescanned every ~12 seconds.
        /// </summary>
        public static bool TryFindHabitatPoint(HabitatPreference pref, Vector3 near, out Vector3 point)
        {
            point = default;
            switch (pref)
            {
                case HabitatPreference.Rocks: return TryRockPoint(near, out point);
                case HabitatPreference.Water: return TryWaterPoint(near, out point);
                case HabitatPreference.LightsAtNight: return TryLightPoint(near, out point);
                default: return false;
            }
        }

        private static bool TryRockPoint(Vector3 near, out Vector3 point)
        {
            point = default;

            if (Time.unscaledTime >= _nextRockScan)
            {
                _nextRockScan = Time.unscaledTime + AnchorRescanSeconds;
                _rocks.Clear();
                // SceneBootstrapper scatters rocks under a "Rocks" group; find
                // it by name so a rebuilt/missing scene degrades gracefully.
                var parent = GameObject.Find("Rocks");
                if (parent != null)
                    foreach (Transform child in parent.transform)
                        if (child != null) _rocks.Add(child);
            }
            if (_rocks.Count == 0) return false;

            // Sample a few and keep the nearest - avoids cross-map treks.
            Transform best = null;
            float bestSqr = float.MaxValue;
            int samples = Mathf.Min(4, _rocks.Count);
            for (int i = 0; i < samples; i++)
            {
                var t = _rocks[Random.Range(0, _rocks.Count)];
                if (t == null) continue; // destroyed since the scan
                float sqr = (t.position - near).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = t; }
            }
            if (best == null) return false;

            point = best.position + (Vector3)(Random.insideUnitCircle * 0.9f);
            point.z = 0f;
            return true;
        }

        private static bool TryWaterPoint(Vector3 near, out Vector3 point)
        {
            point = default;
            var grid = TerrainGrid.Instance;
            if (grid == null) return false;

            if (Time.unscaledTime >= _nextWaterScan)
            {
                _nextWaterScan = Time.unscaledTime + AnchorRescanSeconds;
                _waterCells.Clear();
                for (int y = 0; y < grid.Height; y++)
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (grid.GetSurface(cell) == Surface.Water)
                            _waterCells.Add(cell);
                    }
            }
            if (_waterCells.Count == 0) return false;

            Vector2Int bestCell = default;
            bool found = false;
            float bestSqr = float.MaxValue;
            int samples = Mathf.Min(6, _waterCells.Count);
            for (int i = 0; i < samples; i++)
            {
                var cell = _waterCells[Random.Range(0, _waterCells.Count)];
                float sqr = (grid.CellCenterWorld(cell) - near).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; bestCell = cell; found = true; }
            }
            if (!found) return false;

            // Spirits are ghosts - hovering over the pond edge is the charm.
            point = grid.CellCenterWorld(bestCell) + (Vector3)(Random.insideUnitCircle * 0.6f);
            point.z = 0f;
            return true;
        }

        private static bool TryLightPoint(Vector3 near, out Vector3 point)
        {
            point = default;
            if (GameClock.Instance == null || !GameClock.Instance.IsNight) return false;

            var lights = Watchlight.All;
            Watchlight best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                float sqr = (l.transform.position - near).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = l; }
            }
            if (best == null) return false;

            // Orbit the lamplight like any self-respecting moth-adjacent ghost.
            point = best.transform.position
                + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(0.7f, 1.2f));
            point.z = 0f;
            return true;
        }
    }
}

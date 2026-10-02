using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Shared map constants for the frontier systems (road travel, toll imps,
    /// swamp vendor, pouty mount). MIRRORS the geometry authored in
    /// SceneBootstrapper / ParcelManager -- keep the three in step when the
    /// map changes:
    ///   home cluster x[-15,15] y[-12,12]; road corridor x[-27,-15] y[-2,2];
    ///   swamp cluster x[-47,-27] y[-8,8]. The road runs west from the home
    ///   cluster's gate (x=-15) to the swamp's gate (x=-27).
    /// The road is short, so its "territory" is stylised (ASSUMPTION): the
    /// home verge reads as Grassland, the middle as a dry Desert stretch
    /// ("Dust Reach", the hostile ground a swamp spirit hates) and the far end
    /// as Swamp fringe. A real hell region later just adds list entries.
    /// </summary>
    public static class FrontierGeometry
    {
        public static readonly Rect HomeRect = Rect.MinMaxRect(-15f, -12f, 15f, 12f);
        public static readonly Rect RoadRect = Rect.MinMaxRect(-27f, -2f, -15f, 2f);
        public static readonly Rect SwampRect = Rect.MinMaxRect(-47f, -8f, -27f, 8f);

        /// <summary>Walkable lane inside the rails (props and spawns stay inside it).</summary>
        public static readonly Rect RoadLane = Rect.MinMaxRect(-26.4f, -1.5f, -15.6f, 1.5f);

        /// <summary>One stretch of the road with a territory biome (strain source).</summary>
        public struct RoadSegment
        {
            public string name;
            public float xMin, xMax;
            public BiomeType territory;
        }

        // Ordered home -> swamp (descending x). Territory drives biome strain.
        public static readonly RoadSegment[] Segments =
        {
            new RoadSegment { name = "Hearth Verge", xMin = -20.5f, xMax = -15f, territory = BiomeType.Grassland },
            new RoadSegment { name = "Dust Reach",   xMin = -24.5f, xMax = -20.5f, territory = BiomeType.Desert },
            new RoadSegment { name = "Mire Fringe",  xMin = -27f,   xMax = -24.5f, territory = BiomeType.Swamp }
        };

        /// <summary>Pitch-dark ranges (x extents) the lantern is for.</summary>
        public static readonly Vector2[] DarkRanges =
        {
            new Vector2(-25.2f, -19.0f)
        };

        /// <summary>Toll-imp checkpoint: just past the home gate, before the dark.</summary>
        public static readonly Vector3 TollPost = new Vector3(-17.4f, 0.9f, 0f);

        /// <summary>Swamp vendor stall: just inside the swamp gate, on the north side.</summary>
        public static readonly Vector3 SwampVendorPos = new Vector3(-29.4f, 5.4f, 0f);

        /// <summary>True when the point is inside the road corridor (generous in y: the rails are walls).</summary>
        public static bool IsOnRoad(Vector2 p) =>
            p.x >= RoadRect.xMin && p.x <= RoadRect.xMax && p.y >= RoadRect.yMin - 0.4f && p.y <= RoadRect.yMax + 0.4f;

        /// <summary>The road segment under an x coordinate, or -1 off the road.</summary>
        public static int SegmentIndexAt(float x)
        {
            for (int i = 0; i < Segments.Length; i++)
                if (x >= Segments[i].xMin && x <= Segments[i].xMax) return i;
            return -1;
        }

        /// <summary>The territory biome under a road point (Barren off the road).</summary>
        public static BiomeType TerritoryAt(Vector2 p)
        {
            if (!IsOnRoad(p)) return BiomeType.Barren;
            int i = SegmentIndexAt(p.x);
            return i >= 0 ? Segments[i].territory : BiomeType.Barren;
        }

        /// <summary>True when the point is inside a pitch-dark stretch of the road.</summary>
        public static bool IsDark(Vector2 p)
        {
            if (!IsOnRoad(p)) return false;
            for (int i = 0; i < DarkRanges.Length; i++)
                if (p.x >= DarkRanges[i].x && p.x <= DarkRanges[i].y) return true;
            return false;
        }

        public static bool InSwamp(Vector2 p) => SwampRect.Contains(p);
    }
}

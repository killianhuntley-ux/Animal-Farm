using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Corridor dressing for the west road (muscle 08: "roads are real dressed
    /// corridors you walk"). Built once at scene start by RoadTravel from
    /// runtime-painted placeholder props (FrontierArt) so no scene regen is
    /// needed: translucent ground washes that mark each territory
    /// (Hearth Verge green, Dust Reach sand, Mire Fringe teal), lamp posts at
    /// the lit ends, milestones, signposts that warn about the dark, dead trees
    /// and bones in the dry stretch, puddles and reeds at the mire. Props hug
    /// the rails (|y| >= 1.0) so the lane stays clear; none has a collider.
    /// The art pass replaces the generated sprites with real ones; positions
    /// and the segment table live in FrontierGeometry.
    /// </summary>
    public static class RoadDressing
    {
        private static bool _built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _built = false; }

        public static void Build()
        {
            if (_built) return;
            _built = true;

            var root = new GameObject("RoadDressing");

            // ---- territory ground washes (above the tilemap at -1000, below actors) ----
            var wash = new Color[]
            {
                new Color(0.35f, 0.60f, 0.30f, 0.28f), // Hearth Verge: grass-green
                new Color(0.86f, 0.74f, 0.42f, 0.42f), // Dust Reach: dry sand
                new Color(0.20f, 0.45f, 0.45f, 0.38f)  // Mire Fringe: murky teal
            };
            for (int i = 0; i < FrontierGeometry.Segments.Length; i++)
            {
                var seg = FrontierGeometry.Segments[i];
                float w = seg.xMax - seg.xMin;
                var go = new GameObject("Wash_" + seg.name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3((seg.xMin + seg.xMax) * 0.5f, 0f, 0f);
                // White is 0.25 units at scale 1
                go.transform.localScale = new Vector3(w / 0.25f, FrontierGeometry.RoadRect.height / 0.25f, 1f);
                FrontierArt.AddSprite(go, FrontierArt.White, -900, wash[Mathf.Min(i, wash.Length - 1)]);
            }

            // ---- lit ends: lamp posts either side of each gate ----
            Prop(root, FrontierArt.LampPost, -15.9f, 1.25f, 1.4f);
            Prop(root, FrontierArt.LampPost, -15.9f, -1.25f, 1.4f);
            Prop(root, FrontierArt.LampPost, -26.2f, 1.25f, 1.4f);
            Prop(root, FrontierArt.LampPost, -26.2f, -1.25f, 1.4f);

            // ---- signposts: name the road, warn about the dark ----
            Sign(root, -16.6f, -1.2f, "West Road\nto Reedmire");
            Sign(root, -18.6f, 1.2f, "Dust Reach ahead\nbring a lantern");
            Sign(root, -25.4f, -1.2f, "Reedmire\nmind the mud");

            // ---- milestones ----
            Prop(root, FrontierArt.Milestone, -19.8f, -1.3f, 1.3f);
            Prop(root, FrontierArt.Milestone, -22.9f, 1.3f, 1.3f);

            // ---- Dust Reach: dead trees, bones ----
            Prop(root, FrontierArt.DeadTree, -20.9f, 1.25f, 1.5f);
            Prop(root, FrontierArt.DeadTree, -22.4f, -1.2f, 1.7f);
            Prop(root, FrontierArt.DeadTree, -23.9f, 1.3f, 1.4f);
            Prop(root, FrontierArt.Bones, -21.6f, 0.2f, 1.2f, order: -5);
            Prop(root, FrontierArt.Bones, -23.4f, -0.5f, 1.1f, order: -5, flip: true);

            // ---- Mire Fringe: puddles, reeds ----
            Prop(root, FrontierArt.Puddle, -25.6f, 0.4f, 1.6f, order: -5);
            Prop(root, FrontierArt.Puddle, -26.1f, -0.7f, 1.3f, order: -5, flip: true);
            Prop(root, FrontierArt.Reeds, -25.1f, 1.3f, 1.4f);
            Prop(root, FrontierArt.Reeds, -26.0f, -1.3f, 1.5f);
            Prop(root, FrontierArt.Reeds, -24.9f, -1.2f, 1.2f);

            // ---- Hearth Verge: the last milestone before the wilds ----
            Prop(root, FrontierArt.Milestone, -17.2f, 1.3f, 1.0f);
        }

        private static GameObject Prop(GameObject root, Sprite sprite, float x, float y, float scale,
            int order = 0, bool flip = false)
        {
            var go = new GameObject(sprite != null ? sprite.name : "Prop");
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = FrontierArt.AddSprite(go, sprite, order);
            sr.flipX = flip;
            return go;
        }

        private static void Sign(GameObject root, float x, float y, string text)
        {
            var go = Prop(root, FrontierArt.Signpost, x, y, 1.4f);
            go.name = "Sign";
            WorldLabel.Attach(go, text, -0.9f);
        }
    }
}

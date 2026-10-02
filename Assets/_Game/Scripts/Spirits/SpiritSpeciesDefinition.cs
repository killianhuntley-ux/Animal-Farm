using AnimalFarm.Requirements;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    public enum ActivityWindow { Always, Day, Night }

    /// <summary>Optional hangout anchor a species drifts near (muscle 03).</summary>
    public enum HabitatPreference { None, Rocks, Water, LightsAtNight }

    /// <summary>Shape of a species' bespoke final wish (slice 04).</summary>
    public enum FinalTaskKind
    {
        GiveItem,       // offer taskItemId x taskItemCount to the spirit
        WaterNearHome,  // have >= 1 water cell within taskRadius of its home
        SootheInWindow  // soothe it during [taskWindowStartHour, taskWindowEndHour)
    }

    /// <summary>
    /// One spirit species as pure data (slice 03). Garden-state gates (Appear /
    /// Visit / Stay) come from the species' GateChain. A visitor becomes a
    /// resident on its OWN: while the Stay gate (GateChain.resident) is met it
    /// periodically rolls to decide to stay (muscle 11, owner verdict
    /// 2026-10-01: no feeding quota, no trust meter).
    /// </summary>
    [CreateAssetMenu(menuName = "AnimalFarm/Spirit Species")]
    public class SpiritSpeciesDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea] public string flavor;

        [Header("Visuals (single sprite + procedural float; rigs later)")]
        public Sprite bodySprite;
        public Color tint = Color.white;

        [Header("Gates (Appear = silhouette, Visit = visitor, Resident = STAY gate: met = it may decide to join)")]
        public GateChain gateChain;

        [Header("Residency (visitors decide on their own; favoured food is only a gift)")]
        public string favoredFoodId = "wheat";
        [HideInInspector, Tooltip("DEPRECATED (muscle 11): feeding no longer converts a visitor. Kept so old assets deserialize.")]
        public int residencyFoodCount = 2;
        public int maxResidents = 3;

        [Header("Behaviour")]
        public ActivityWindow activity = ActivityWindow.Always;
        public float wanderSpeed = 1.6f;

        [Header("Personality (muscle 03 - idle quirks + habitat habit)")]
        [Tooltip("Optional anchor this species periodically hangs out near. Degrades to plain wandering when the scene has none.")]
        public HabitatPreference habitatPreference = HabitatPreference.None;
        [Tooltip("Idle micro-moment weights (relative frequency; 0 disables one).")]
        public float napWeight = 1f;
        public float stretchWeight = 1f;
        public float hopWeight = 1f;
        public float leafChaseWeight = 1f;

        [Header("Nature bands (muscle 05: x = base/min, y = max; individuals roll inside, training climbs to max)")]
        public Vector2Int vigorBand = new Vector2Int(2, 9);
        public Vector2Int graceBand = new Vector2Int(2, 9);
        public Vector2Int gleamBand = new Vector2Int(2, 9);

        [Tooltip("Muscle 06: trait ids a WOVEN cryptid of this species may inherit/roll. Empty = all allowed.")]
        public string[] allowedTraitIds;

        /// <summary>True when this species may carry the trait (empty list = any).</summary>
        public bool AllowsTrait(string traitId)
        {
            if (allowedTraitIds == null || allowedTraitIds.Length == 0) return true;
            for (int i = 0; i < allowedTraitIds.Length; i++)
                if (allowedTraitIds[i] == traitId) return true;
            return false;
        }

        /// <summary>The authored (min, max) of one Nature stat, sanitised to 1..SpiritStats.Ceiling.</summary>
        public void GetBand(SpiritStat stat, out int min, out int max)
        {
            Vector2Int b = stat == SpiritStat.Vigor ? vigorBand
                : stat == SpiritStat.Grace ? graceBand : gleamBand;
            min = Mathf.Clamp(b.x, 1, SpiritStats.Ceiling);
            max = Mathf.Clamp(b.y, min, SpiritStats.Ceiling);
        }

        [Header("Voice (synth chirps; owner mic gibberish replaces at skin phase)")]
        [Tooltip("Base chirp frequency in Hz.")]
        public float voiceBasePitch = 520f;
        [Tooltip("-1..1: chirps slide down (negative) or up (positive); magnitude = up to half an octave.")]
        public float voiceContour = 0.5f;
        [Tooltip("Length of one chirp in seconds.")]
        public float voiceChirpSeconds = 0.09f;

        [Header("Mood animation (muscle 03 - happy / neutral / sad-sick posture, bob style, pace)")]
        public SpiritAnimProfile animProfile = new SpiritAnimProfile();

        [Header("Needs & Spirit (morale, 0..100)")]
        [Tooltip("Game-hours from fully fed back to hungry.")]
        public float hungerHours = 12f;
        [Tooltip("Spirit lost per game-hour while hungry.")]
        public float spiritDecayPerHungryHour = 8f;
        public float feedSpiritBoost = 15f;
        public float sootheSpiritBoost = 8f;
        [Tooltip("Below this Spirit, sustained, the spirit runs away to the border.")]
        public float runawayThreshold = 15f;

        [Header("Home (slice 04 — required for fulfilment)")]
        public Sprite homeSprite;

        [Header("Final Wish (slice 04 — the bespoke ascension task)")]
        public FinalTaskKind taskKind = FinalTaskKind.GiveItem;
        [Tooltip("GiveItem: inventory id to offer.")]
        public string taskItemId;
        public int taskItemCount = 1;
        [Tooltip("WaterNearHome: radius in cells around its home.")]
        public int taskRadius = 3;
        [Tooltip("SootheInWindow: hour window (wraps past midnight).")]
        public float taskWindowStartHour;
        public float taskWindowEndHour = 1f;
        [TextArea, Tooltip("Journal line describing the wish (revealed when fulfilment is close).")]
        public string taskDescription;
        [Tooltip("Hint shown when an ascension attempt fails on the unfinished task.")]
        public string taskHint;

        [Header("Biome affinity (muscle 02: Hard No / Dislike / Neutral / Like / Love; missing row = Neutral)")]
        public BiomeAffinityEntry[] biomeAffinities;
    }
}

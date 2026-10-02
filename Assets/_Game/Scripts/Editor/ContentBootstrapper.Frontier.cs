using UnityEditor;
using UnityEngine;
using AnimalFarm.Requirements;
using AnimalFarm.Spirits;
using AnimalFarm.World;

namespace AnimalFarm.EditorTools
{
    /// <summary>
    /// Frontier half of the content bootstrapper (muscle 08 item 3): the three
    /// SWAMP spirit species (data + gate chains keyed on the swamp base's biome
    /// census and the water plants), the one DESERT species (muscle 11: Scorpse,
    /// keyed on sand and the home base's Desert census) and the 5-step
    /// biome-affinity table for EVERY species (muscle 02 verdict 1). Every chain
    /// authors the three-step escalation Appear -> Visit -> Stay (the Stay set
    /// rides GateChain.resident, see SpiritAgent.Stay.cs). Partial so it can reuse the shared
    /// private helpers (Cond / Set / Chain / MakeSpirit ...) without touching
    /// the main file beyond three hook lines.
    /// </summary>
    public static partial class ContentBootstrapper
    {
        private static readonly string[] FrontierSpeciesAssets = { "Bogwick", "Reedhen", "Sloughling", "Scorpse" };
        private static readonly string[] FrontierChainAssets = { "chain_bogwick", "chain_reedhen", "chain_sloughling", "chain_scorpse" };

        private static void GenerateFrontier()
        {
            GenerateSwampGates();
            GenerateSwampSpirits();
            GenerateDesertGates();
            GenerateDesertSpirits();
            GenerateBiomeAffinities();
        }

        // ---- swamp gates ------------------------------------------------------------

        private static void GenerateSwampGates()
        {
            // The swamp base's census (BiomeScorer, base 1): >= 12% water and 2+ wet plants.
            var swamp = Cond<BiomeIsCondition>("cond_swamp_biome",
                ("baseId", ParcelManager.SwampBaseId), ("biome", BiomeType.Swamp), ("minScore", 0f));
            var swamp30 = Cond<BiomeIsCondition>("cond_swamp_biome_30",
                ("baseId", ParcelManager.SwampBaseId), ("biome", BiomeType.Swamp), ("minScore", 30f));

            var lily1any = Cond<PlantCountCondition>("cond_glowcaplily_1any",
                ("speciesId", "glowcaplily"), ("minStage", 0), ("minCount", 1));
            var lily1ripe = Cond<PlantCountCondition>("cond_glowcaplily_1ripe",
                ("speciesId", "glowcaplily"), ("minStage", 2), ("minCount", 1));
            var reed2any = Cond<PlantCountCondition>("cond_reed_2any",
                ("speciesId", "reed"), ("minStage", 0), ("minCount", 2));
            var reed3ripe = Cond<PlantCountCondition>("cond_reed_3ripe",
                ("speciesId", "reed"), ("minStage", 2), ("minCount", 3));
            var daytime = Cond<TimeOfDayCondition>("cond_daytime", ("startHour", 6f), ("endHour", 18f));
            var night = Cond<TimeOfDayCondition>("cond_night", ("startHour", 20f), ("endHour", 4f));

            // Stay-gate atoms (muscle 11).
            var swamp50 = Cond<BiomeIsCondition>("cond_swamp_biome_50",
                ("baseId", ParcelManager.SwampBaseId), ("biome", BiomeType.Swamp), ("minScore", 50f));
            var lily2ripe = Cond<PlantCountCondition>("cond_glowcaplily_2ripe",
                ("speciesId", "glowcaplily"), ("minStage", 2), ("minCount", 2));
            var reed5ripe = Cond<PlantCountCondition>("cond_reed_5ripe",
                ("speciesId", "reed"), ("minStage", 2), ("minCount", 5));
            var berry1ripe = Cond<PlantCountCondition>("cond_murkberry_1ripe",
                ("speciesId", "murkberry"), ("minStage", 2), ("minCount", 1));

            // Bogwick: a swamp with a glowcap lily gets a visitor after dark; a ripe lily keeps it.
            var bogAppear = Set("set_bogwick_appear", swamp, lily1any, night);
            var bogVisit = Set("set_bogwick_visit", swamp, lily1ripe, night);
            // Reedhen: a reed bed notices you; a stand of ripe reeds and daylight makes it stay.
            var henAppear = Set("set_reedhen_appear", swamp, reed2any);
            var henVisit = Set("set_reedhen_visit", swamp, reed3ripe, daytime);
            // Sloughling: just needs a real swamp (a good one to move in).
            var slAppear = Set("set_sloughling_appear", swamp);
            var slVisit = Set("set_sloughling_visit", swamp30);

            // Stay (muscle 11): the favoured plant in numbers, and a swamp that is properly swampy.
            var bogStay = Set("set_bogwick_stay", swamp30, lily2ripe, night);
            var henStay = Set("set_reedhen_stay", swamp30, reed5ripe);
            var slStay = Set("set_sloughling_stay", swamp50, berry1ripe);

            Chain("chain_bogwick", "bogwick", bogAppear, bogVisit, bogStay, null);
            Chain("chain_reedhen", "reedhen", henAppear, henVisit, henStay, null);
            Chain("chain_sloughling", "sloughling", slAppear, slVisit, slStay, null);
        }

        // ---- swamp spirits ------------------------------------------------------------

        private static void GenerateSwampSpirits()
        {
            MakeSpirit("bogwick", "Bogwick", "bogwick_body",
                "A candle-wick of a spirit. It flickers when it lies, and it lies often.",
                "chain_bogwick", favoredFood: "glowcap", maxResidents: 2,
                activity: ActivityWindow.Night, hungerHours: 12f);
            MakeSpirit("reedhen", "Reedhen", "reedhen_body",
                "Stands motionless in the shallows for hours. It insists this is a hobby.",
                "chain_reedhen", favoredFood: "reed", maxResidents: 2,
                activity: ActivityWindow.Day, hungerHours: 12f);
            MakeSpirit("sloughling", "Sloughling", "sloughling_body",
                "A mud-toad's memory of being a mud-toad. It glorps in the past tense.",
                "chain_sloughling", favoredFood: "berry", maxResidents: 3,
                activity: ActivityWindow.Always, hungerHours: 14f);

            var bog = LoadFrontierSpecies("Bogwick");
            var hen = LoadFrontierSpecies("Reedhen");
            var slough = LoadFrontierSpecies("Sloughling");

            // Final wishes (skeleton text, owner rewrites like the prairie species).
            if (bog != null)
            {
                bog.taskKind = FinalTaskKind.GiveItem;
                bog.taskItemId = "glowcap"; bog.taskItemCount = 1;
                bog.taskDescription = "To be lit once more by something that glows for its own reasons.";
                bog.taskHint = "(it wants a glowcap bloom)";
                Personality(bog, HabitatPreference.LightsAtNight, 0.6f, 0.6f, 0.8f, 1.2f, 700f, 0.4f, 0.09f);
                Bands(bog, 2, 4, 4, 8, 5, 10);
                bog.animProfile = new SpiritAnimProfile
                {
                    happy = MakePose(SpiritBobStyle.Flutter, 1.3f, 1.3f, 1.15f, 0.03f, 1.05f, 1.04f, 0f, 5f, 1f),
                    neutral = MakePose(SpiritBobStyle.Float, 0.9f, 1.1f, 1f, 0f, 1f, 1f, 0f, 0f, 1f),
                    sadSick = MakePose(SpiritBobStyle.Float, 0.4f, 0.5f, 0.6f, -0.12f, 1f, 0.86f, 0f, 0f, 0.5f)
                };
                EditorUtility.SetDirty(bog);
            }
            if (hen != null)
            {
                hen.taskKind = FinalTaskKind.WaterNearHome;
                hen.taskRadius = 2;
                hen.taskDescription = "It wants to wade at its own front door.";
                hen.taskHint = "(dig water near its home)";
                Personality(hen, HabitatPreference.Water, 0.5f, 1.4f, 0.5f, 0.4f, 380f, -0.3f, 0.12f);
                Bands(hen, 3, 6, 5, 9, 3, 7);
                hen.animProfile = new SpiritAnimProfile
                {
                    happy = MakePose(SpiritBobStyle.Sway, 1.1f, 1.1f, 1.1f, 0f, 1f, 1.06f, 0f, 4f, 1f),
                    neutral = MakePose(SpiritBobStyle.Float, 0.8f, 0.8f, 1f, 0f, 1f, 1f, 0f, 0f, 1f),
                    sadSick = MakePose(SpiritBobStyle.Float, 0.4f, 0.5f, 0.6f, -0.1f, 1.05f, 0.88f, -6f, 0f, 0.55f)
                };
                EditorUtility.SetDirty(hen);
            }
            if (slough != null)
            {
                slough.taskKind = FinalTaskKind.GiveItem;
                slough.taskItemId = "berry"; slough.taskItemCount = 2;
                slough.taskDescription = "A feast of murky fruit, eaten loudly.";
                slough.taskHint = "(it wants two more berries)";
                Personality(slough, HabitatPreference.Water, 1.6f, 0.6f, 1.2f, 0.4f, 240f, -0.5f, 0.15f);
                Bands(slough, 4, 8, 2, 5, 2, 6);
                slough.animProfile = new SpiritAnimProfile
                {
                    happy = MakePose(SpiritBobStyle.Bounce, 1.2f, 1.2f, 1.1f, 0f, 1.04f, 0.98f, 0f, 0f, 1f),
                    neutral = MakePose(SpiritBobStyle.Float, 0.7f, 0.8f, 1f, 0f, 1f, 1f, 0f, 0f, 1f),
                    sadSick = MakePose(SpiritBobStyle.Float, 0.35f, 0.5f, 0.55f, -0.08f, 1.08f, 0.84f, 4f, 0f, 0.5f)
                };
                EditorUtility.SetDirty(slough);
            }
        }

        // ---- desert gates (muscle 11) ----------------------------------------------------------

        /// <summary>
        /// Scorpse's escalation, keyed on SAND (bought by the load at the vendor) and
        /// the one hardy plant the graveyard soil takes to, the gravebloom:
        /// appear = sand starts to spread and a gravebloom is growing; visit = the
        /// home base has become Desert (sand 40%+) and a bloom is ripe; stay = a real
        /// desert (sand 50%+), two ripe blooms and a little water (an oasis) in the field.
        /// ASSUMPTION: only the HOME base counts (the swamp base cannot be sanded for it).
        /// </summary>
        private static void GenerateDesertGates()
        {
            var sand15 = Cond<SurfacePercentCondition>("cond_sand_15",
                ("surface", Surface.Sand), ("minPercent", 15f));
            var homeDesert = Cond<BiomeIsCondition>("cond_home_desert",
                ("baseId", ParcelManager.HomeBaseId), ("biome", BiomeType.Desert), ("minScore", 0f));
            var homeDesert50 = Cond<BiomeIsCondition>("cond_home_desert_50",
                ("baseId", ParcelManager.HomeBaseId), ("biome", BiomeType.Desert), ("minScore", 50f));
            var bloom1any = Cond<PlantCountCondition>("cond_gravebloom_1any",
                ("speciesId", "gravebloom"), ("minStage", 0), ("minCount", 1));
            var bloom1ripe = Cond<PlantCountCondition>("cond_gravebloom_1ripe",
                ("speciesId", "gravebloom"), ("minStage", 2), ("minCount", 1));
            var bloom2ripe = Cond<PlantCountCondition>("cond_gravebloom_2ripe",
                ("speciesId", "gravebloom"), ("minStage", 2), ("minCount", 2));
            var water3 = Cond<SurfacePercentCondition>("cond_water_3",
                ("surface", Surface.Water), ("minPercent", 3f));

            var scAppear = Set("set_scorpse_appear", sand15, bloom1any);
            var scVisit = Set("set_scorpse_visit", homeDesert, bloom1ripe);
            var scStay = Set("set_scorpse_stay", homeDesert50, bloom2ripe, water3);
            Chain("chain_scorpse", "scorpse", scAppear, scVisit, scStay, null);
        }

        // ---- desert spirit (muscle 11) ----------------------------------------------------------

        /// <summary>
        /// The one desert-native species (owner approved; the name and look are an
        /// ASSUMPTION): a bone-white scorpion's memory. It Loves Desert and dislikes
        /// Swamp, so the rain reaction (muscle 02) makes it shelter from rain at its
        /// home or the nearest building.
        /// </summary>
        private static void GenerateDesertSpirits()
        {
            MakeSpirit("scorpse", "Scorpse", "scorpse_body",
                "Bleached clean by a desert that never finished the job. It carries the stinger out of habit.",
                "chain_scorpse", favoredFood: "bloom", maxResidents: 2,
                activity: ActivityWindow.Always, hungerHours: 16f);

            var sc = LoadFrontierSpecies("Scorpse");
            if (sc == null) return;

            sc.taskKind = FinalTaskKind.WaterNearHome;
            sc.taskRadius = 3;
            sc.taskDescription = "It has waited a very long time to see an oasis. It will pretend not to cry.";
            sc.taskHint = "(dig water near its home)";
            sc.tint = new Color(1f, 0.97f, 0.88f, 1f);
            Personality(sc, HabitatPreference.Rocks, 1.4f, 0.6f, 0.7f, 0.5f, 470f, -0.2f, 0.055f);
            Bands(sc, 3, 7, 3, 6, 2, 6);
            sc.animProfile = new SpiritAnimProfile
            {
                happy = MakePose(SpiritBobStyle.Waddle, 1.3f, 1.1f, 1.15f, 0f, 1.02f, 1.05f, 0f, 4f, 1f),
                neutral = MakePose(SpiritBobStyle.Float, 0.8f, 0.8f, 1f, 0f, 1f, 1f, 0f, 0f, 1f),
                sadSick = MakePose(SpiritBobStyle.Float, 0.4f, 0.5f, 0.6f, -0.1f, 1.06f, 0.86f, -7f, 0f, 0.5f)
            };
            EditorUtility.SetDirty(sc);
        }

        private static SpiritSpeciesDefinition LoadFrontierSpecies(string displayName) =>
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{displayName}.asset");

        // ---- biome affinity (every species) --------------------------------------------

        /// <summary>
        /// ASSUMPTION table (the docs fix the 5-step scale, not the values):
        /// prairie species like or love Grassland; swamp species love Swamp and
        /// dislike the dry Desert stretch of the road (the frail Bogwick flat-out
        /// refuses it); woven cryptids inherit a lean toward the mire; the desert
        /// species (Scorpse) loves Desert and dislikes Swamp.
        /// Barren is Neutral for everyone (unauthored = Neutral).
        /// </summary>
        private static void GenerateBiomeAffinities()
        {
            SetAffinities("Mausoleum", (BiomeType.Grassland, Affinity.Like), (BiomeType.Swamp, Affinity.Dislike), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Bansheep", (BiomeType.Grassland, Affinity.Love), (BiomeType.Swamp, Affinity.Dislike), (BiomeType.Desert, Affinity.HardNo));
            SetAffinities("Wrabbit", (BiomeType.Grassland, Affinity.Like), (BiomeType.Swamp, Affinity.Like), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Phantomoth", (BiomeType.Grassland, Affinity.Neutral), (BiomeType.Swamp, Affinity.Like), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Wailpertinger", (BiomeType.Grassland, Affinity.Neutral), (BiomeType.Swamp, Affinity.Love), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Mothmaus", (BiomeType.Grassland, Affinity.Neutral), (BiomeType.Swamp, Affinity.Like), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Bogwick", (BiomeType.Grassland, Affinity.Neutral), (BiomeType.Swamp, Affinity.Love), (BiomeType.Desert, Affinity.HardNo));
            SetAffinities("Reedhen", (BiomeType.Grassland, Affinity.Like), (BiomeType.Swamp, Affinity.Love), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Sloughling", (BiomeType.Grassland, Affinity.Dislike), (BiomeType.Swamp, Affinity.Love), (BiomeType.Desert, Affinity.Dislike));
            SetAffinities("Scorpse", (BiomeType.Grassland, Affinity.Neutral), (BiomeType.Swamp, Affinity.Dislike), (BiomeType.Desert, Affinity.Love));
        }

        private static void SetAffinities(string displayName, params (BiomeType biome, Affinity affinity)[] rows)
        {
            var def = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{displayName}.asset");
            if (def == null) return;

            var list = new BiomeAffinityEntry[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                list[i] = new BiomeAffinityEntry { biome = rows[i].biome, affinity = rows[i].affinity };
            def.biomeAffinities = list;
            EditorUtility.SetDirty(def);
        }

        // ---- load helpers (used by LoadAllSpiritSpecies / LoadAllChains) ------------------

        private static SpiritSpeciesDefinition[] WithFrontierSpecies(SpiritSpeciesDefinition[] baseSpecies)
        {
            var result = new SpiritSpeciesDefinition[baseSpecies.Length + FrontierSpeciesAssets.Length];
            for (int i = 0; i < baseSpecies.Length; i++) result[i] = baseSpecies[i];
            for (int i = 0; i < FrontierSpeciesAssets.Length; i++)
                result[baseSpecies.Length + i] = LoadFrontierSpecies(FrontierSpeciesAssets[i]);
            return result;
        }

        private static GateChain[] WithFrontierChains(GateChain[] baseChains)
        {
            var result = new GateChain[baseChains.Length + FrontierChainAssets.Length];
            for (int i = 0; i < baseChains.Length; i++) result[i] = baseChains[i];
            for (int i = 0; i < FrontierChainAssets.Length; i++)
                result[baseChains.Length + i] =
                    AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/{FrontierChainAssets[i]}.asset");
            return result;
        }
    }
}

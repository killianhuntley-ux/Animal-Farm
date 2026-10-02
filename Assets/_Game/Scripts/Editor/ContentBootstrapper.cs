using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using AnimalFarm.Requirements;
using AnimalFarm.Spirits;
using AnimalFarm.World;

namespace AnimalFarm.EditorTools
{
    /// <summary>
    /// Generates slice-02 content as data assets: terrain tiles, plant species,
    /// and the dummy gate chains that light the requirement-engine debug lamps.
    /// Deterministic and re-runnable (get-or-create by path).
    /// </summary>
    public static partial class ContentBootstrapper
    {
        private const string ArtDir = "Assets/_Game/Art/Placeholder/";
        private const string TileDir = "Assets/_Game/Art/Tiles";
        private const string PlantDir = "Assets/_Game/Data/Plants";
        private const string GateDir = "Assets/_Game/Data/Gates";
        private const string SpiritDir = "Assets/_Game/Data/Spirits";
        private const string TraitDir = "Assets/_Game/Data/Traits";

        [MenuItem("AnimalFarm/Generate Content Assets")]
        public static void Generate()
        {
            EnsureFolder("Assets/_Game/Data");
            EnsureFolder(TileDir);
            EnsureFolder(PlantDir);
            EnsureFolder(GateDir);
            EnsureFolder(SpiritDir);

            DeleteSlice02Dummies();
            GenerateTiles();
            GeneratePlants();
            GenerateGates();
            GenerateSpirits();
            GenerateRecipes();
            GenerateTraits();
            GenerateFrontier(); // swamp species + biome affinity (muscle 08; ContentBootstrapper.Frontier.cs)

            AssetDatabase.SaveAssets();
            Debug.Log("[Content] Tiles, plants, gates and spirit species generated.");
        }

        /// <summary>The slice-02 dummy chains are superseded by real species chains.</summary>
        private static void DeleteSlice02Dummies()
        {
            string[] dead =
            {
                $"{GateDir}/chain_meadow_visitor.asset", $"{GateDir}/chain_pond_lurker.asset",
                $"{GateDir}/set_meadow_appear.asset", $"{GateDir}/set_meadow_visit.asset",
                $"{GateDir}/set_meadow_resident.asset", $"{GateDir}/set_meadow_fulfil.asset",
                $"{GateDir}/set_pond_appear.asset", $"{GateDir}/set_pond_visit.asset",
                $"{GateDir}/set_pond_resident.asset", $"{GateDir}/set_pond_fulfil.asset",
                $"{GateDir}/cond_res_meadow.asset", $"{GateDir}/cond_res_pond.asset"
            };
            foreach (var path in dead)
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);
        }

        // ---- tiles -----------------------------------------------------------

        private static void GenerateTiles()
        {
            MakeTile("Tile_Scrub", "tile_scrub", Tile.ColliderType.None);
            MakeTile("Tile_Dirt", "tile_dirt", Tile.ColliderType.None);
            MakeTile("Tile_Grass", "tile_grass", Tile.ColliderType.None);
            MakeTile("Tile_Water", "tile_water", Tile.ColliderType.Grid); // impassable
            MakeTile("Tile_Locked", "tile_locked", Tile.ColliderType.None); // unpurchased parcel (walls block, not tiles)
        }

        private static void MakeTile(string name, string spriteName, Tile.ColliderType collider)
        {
            var tile = GetOrCreate<Tile>($"{TileDir}/{name}.asset");
            tile.sprite = LoadSprite(spriteName);
            tile.colliderType = collider;
            tile.color = Color.white;
            EditorUtility.SetDirty(tile);
        }

        // ---- plants ----------------------------------------------------------

        private static void GeneratePlants()
        {
            MakePlant("palewheat", "Palewheat", new[] { "plant_sprout", "palewheat_mid", "palewheat_ripe" },
                hoursPerStage: 6f, produceId: "wheat", produceAmount: 2);
            MakePlant("gravebloom", "Gravebloom", new[] { "plant_sprout", "gravebloom_mid", "gravebloom_ripe" },
                hoursPerStage: 8f, produceId: "bloom", produceAmount: 1);
            // Berry-bush style: harvest drops it back to its mid stage (stage 1).
            MakePlant("murkberry", "Murkberry", new[] { "plant_sprout", "murkberry_mid", "murkberry_ripe" },
                hoursPerStage: 10f, produceId: "berry", produceAmount: 3, regrows: true);

            // Water species (muscle 02): planted ON Water cells from the bank, feed swamp scoring.
            // Reeds: shallow rim only, regrow. Glowcap lilies: any water cell, one-shot.
            MakePlant("reed", "Reed", new[] { "plant_sprout", "reed_mid", "reed_ripe" },
                hoursPerStage: 5f, produceId: "reed", produceAmount: 2, regrows: true,
                surface: Surface.Water, shallowOnly: true);
            MakePlant("glowcaplily", "Glowcap Lily", new[] { "plant_sprout", "glowcap_mid", "glowcap_ripe" },
                hoursPerStage: 9f, produceId: "glowcap", produceAmount: 1, regrows: false,
                surface: Surface.Water, shallowOnly: false);
        }

        private static void MakePlant(string id, string displayName, string[] stageSprites,
            float hoursPerStage, string produceId, int produceAmount,
            bool regrows = false, Surface surface = Surface.Dirt, bool shallowOnly = false)
        {
            var species = GetOrCreate<PlantSpecies>($"{PlantDir}/{displayName}.asset");
            var sprites = new Sprite[stageSprites.Length];
            for (int i = 0; i < stageSprites.Length; i++) sprites[i] = LoadSprite(stageSprites[i]);

            SetField(species, "id", id);
            SetField(species, "displayName", displayName);
            SetField(species, "stageSprites", sprites);
            SetField(species, "hoursPerStage", hoursPerStage);
            SetField(species, "requiredSurface", surface);
            SetField(species, "produceId", produceId);
            SetField(species, "produceAmount", produceAmount);
            SetField(species, "regrows", regrows);
            SetField(species, "regrowStage", 1);
            SetField(species, "shallowOnly", shallowOnly);
            EditorUtility.SetDirty(species);
        }

        public static PlantSpecies[] LoadAllPlantSpecies() => new[]
        {
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Palewheat.asset"),
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Gravebloom.asset"),
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Murkberry.asset"),
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Reed.asset"),
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Glowcap Lily.asset")
        };

        // ---- gates -----------------------------------------------------------

        private static void GenerateGates()
        {
            // conditions (general library — reused across species)
            var grass8 = Cond<SurfacePercentCondition>("cond_grass_8",
                ("surface", Surface.Grass), ("minPercent", 8f));
            var grass15 = Cond<SurfacePercentCondition>("cond_grass_15",
                ("surface", Surface.Grass), ("minPercent", 15f));
            var grass20 = Cond<SurfacePercentCondition>("cond_grass_20",
                ("surface", Surface.Grass), ("minPercent", 20f));
            var daytime = Cond<TimeOfDayCondition>("cond_daytime",
                ("startHour", 6f), ("endHour", 18f));
            var night = Cond<TimeOfDayCondition>("cond_night",
                ("startHour", 20f), ("endHour", 4f));
            var wheat2Any = Cond<PlantCountCondition>("cond_palewheat_2any",
                ("speciesId", "palewheat"), ("minStage", 0), ("minCount", 2));
            var wheat2Ripe = Cond<PlantCountCondition>("cond_palewheat_2ripe",
                ("speciesId", "palewheat"), ("minStage", 2), ("minCount", 2));
            var bloom1Any = Cond<PlantCountCondition>("cond_gravebloom_1any",
                ("speciesId", "gravebloom"), ("minStage", 0), ("minCount", 1));
            var bloom2Ripe = Cond<PlantCountCondition>("cond_gravebloom_2ripe",
                ("speciesId", "gravebloom"), ("minStage", 2), ("minCount", 2));
            var resMausoleum = Cond<ResidentPresentCondition>("cond_res_mausoleum",
                ("speciesId", "mausoleum"), ("minCount", 1));

            // STAY-gate atoms (muscle 11): what makes a visitor decide to join on its own.
            var wheat1Ripe = Cond<PlantCountCondition>("cond_palewheat_1ripe",
                ("speciesId", "palewheat"), ("minStage", 2), ("minCount", 1));
            var wheat3Ripe = Cond<PlantCountCondition>("cond_palewheat_3ripe",
                ("speciesId", "palewheat"), ("minStage", 2), ("minCount", 3));
            var bloom3Ripe = Cond<PlantCountCondition>("cond_gravebloom_3ripe",
                ("speciesId", "gravebloom"), ("minStage", 2), ("minCount", 3));
            var berry2Ripe = Cond<PlantCountCondition>("cond_murkberry_2ripe",
                ("speciesId", "murkberry"), ("minStage", 2), ("minCount", 2));
            var water3 = Cond<SurfacePercentCondition>("cond_water_3",
                ("surface", Surface.Water), ("minPercent", 3f));
            var homeGrassland = Cond<BiomeIsCondition>("cond_home_grassland",
                ("baseId", ParcelManager.HomeBaseId), ("biome", BiomeType.Grassland), ("minScore", 0f));

            // Water-plant atoms (muscle 02) for swamp-spirit chains to wire later.
            Cond<PlantCountCondition>("cond_reed_2any",
                ("speciesId", "reed"), ("minStage", 0), ("minCount", 2));
            Cond<PlantCountCondition>("cond_reed_3ripe",
                ("speciesId", "reed"), ("minStage", 2), ("minCount", 3));
            Cond<PlantCountCondition>("cond_glowcaplily_1any",
                ("speciesId", "glowcaplily"), ("minStage", 0), ("minCount", 1));

            // species gate sets: Appear (silhouette) -> Visit (visitor) -> Resident = the
            // STAY gate (muscle 11): while it is met a visitor rolls to decide to join on
            // its own - no feeding quota. Fulfil = slice 04.
            var mausAppear = Set("set_mausoleum_appear", grass8);
            var mausVisit = Set("set_mausoleum_visit", grass15);
            var shepAppear = Set("set_bansheep_appear", wheat2Any);
            var shepVisit = Set("set_bansheep_visit", wheat2Ripe, daytime);
            var wrabAppear = Set("set_wrabbit_appear", resMausoleum);
            var wrabVisit = Set("set_wrabbit_visit", grass20, resMausoleum);
            var mothAppear = Set("set_phantomoth_appear", night, bloom1Any);
            var mothVisit = Set("set_phantomoth_visit", night, bloom2Ripe);

            // Stay = the favoured crop fully grown, plus the land feeling right for the species.
            var mausStay = Set("set_mausoleum_stay", grass15, wheat1Ripe);
            var shepStay = Set("set_bansheep_stay", wheat3Ripe, homeGrassland);
            var wrabStay = Set("set_wrabbit_stay", berry2Ripe, water3);
            var mothStay = Set("set_phantomoth_stay", night, bloom3Ripe);

            Chain("chain_mausoleum", "mausoleum", mausAppear, mausVisit, mausStay, null);
            Chain("chain_bansheep", "bansheep", shepAppear, shepVisit, shepStay, null);
            Chain("chain_wrabbit", "wrabbit", wrabAppear, wrabVisit, wrabStay, null);
            Chain("chain_phantomoth", "phantomoth", mothAppear, mothVisit, mothStay, null);
        }

        // ---- spirits (slice 03) -----------------------------------------------

        private static void GenerateSpirits()
        {
            // Woven cryptids (slice 06): no gate chain — they never spawn wild;
            // the Loom is their only origin (GDD 2.7: cryptids are MADE).
            MakeCryptid("wailpertinger", "Wailpertinger", "wailpertinger_body",
                "Horns from one parent, the wail from the other. Folklore, freshly manufactured.",
                favoredFood: "berry", taskItem: "berry", taskCount: 2,
                taskDesc: "A hoard of berries befitting a proper legend.",
                taskHint: "(legends demand two more berries)");
            MakeCryptid("mothmaus", "Mothmaus", "mothmaus_body",
                "Seen only at the edge of lamplight. Squeaks portents nobody asked for.",
                favoredFood: "bloom", taskItem: "", taskCount: 0,
                taskDesc: "One last portent, whispered at the witching hour.",
                taskHint: "(sit with it in the small hours)");

            MakeSpirit("mausoleum", "Mausoleum", "mausoleum_body",
                "A mouse-shaped memory. It squeaks in past tense.",
                "chain_mausoleum", favoredFood: "wheat", maxResidents: 3,
                activity: ActivityWindow.Always, hungerHours: 14f);
            MakeSpirit("bansheep", "Bansheep", "bansheep_body",
                "It wails at shearing time. Nobody has ever sheared it.",
                "chain_bansheep", favoredFood: "wheat", maxResidents: 2,
                activity: ActivityWindow.Day, hungerHours: 12f);
            MakeSpirit("wrabbit", "Wrabbit", "wrabbit_body",
                "Quick in life. Quicker now.",
                "chain_wrabbit", favoredFood: "berry", maxResidents: 2,
                activity: ActivityWindow.Always, hungerHours: 10f);
            MakeSpirit("phantomoth", "Phantomoth", "phantomoth_body",
                "Drawn to lights it can no longer feel.",
                "chain_phantomoth", favoredFood: "bloom", maxResidents: 2,
                activity: ActivityWindow.Night, hungerHours: 12f);
        }

        private static void MakeSpirit(string id, string displayName, string spriteName, string flavor,
            string chainAsset, string favoredFood, int maxResidents,
            ActivityWindow activity, float hungerHours)
        {
            var def = GetOrCreate<SpiritSpeciesDefinition>($"{SpiritDir}/{displayName}.asset");
            def.id = id;
            def.displayName = displayName;
            def.flavor = flavor;
            def.bodySprite = LoadSprite(spriteName);
            def.gateChain = AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/{chainAsset}.asset");
            def.favoredFoodId = favoredFood;
            def.maxResidents = maxResidents;
            def.activity = activity;
            def.hungerHours = hungerHours;
            def.homeSprite = LoadSprite("home_" + id);
            ApplyFinalTask(def);
            ApplyPersonality(def);
            EditorUtility.SetDirty(def);
        }

        /// <summary>
        /// Muscle 03: per-species idle-quirk weights, habitat habit and synth
        /// voice profile. Weights are relative (nap/stretch/hop/leafChase);
        /// voice = base pitch Hz, contour -1..1 (down..up), chirp seconds.
        /// </summary>
        private static void ApplyPersonality(SpiritSpeciesDefinition def)
        {
            switch (def.id)
            {
                case "mausoleum": // hides near rocks; tiny up-squeaks
                    Personality(def, HabitatPreference.Rocks, 1.2f, 0.8f, 0.6f, 0.6f, 740f, 0.6f, 0.06f);
                    break;
                case "bansheep": // placid napper; low falling wail
                    Personality(def, HabitatPreference.None, 1.5f, 1.0f, 0.8f, 0.3f, 300f, -0.4f, 0.16f);
                    break;
                case "wrabbit": // pond-side zoomies; quick rising squeak
                    Personality(def, HabitatPreference.Water, 0.4f, 0.8f, 1.8f, 1.6f, 560f, 0.8f, 0.05f);
                    break;
                case "phantomoth": // orbits lights at night; thin high flutter
                    Personality(def, HabitatPreference.LightsAtNight, 0.6f, 0.6f, 0.7f, 1.2f, 880f, 0.3f, 0.08f);
                    break;
                case "wailpertinger": // waterside wailer; low falling horn-note
                    Personality(def, HabitatPreference.Water, 0.6f, 0.9f, 1.4f, 1.0f, 420f, -0.6f, 0.14f);
                    break;
                case "mothmaus": // lamplight lurker; small rising portent
                    Personality(def, HabitatPreference.LightsAtNight, 0.8f, 0.7f, 0.8f, 1.0f, 820f, 0.5f, 0.07f);
                    break;
            }
            ApplyStatBands(def);
            ApplyAnimProfile(def);
        }

        /// <summary>
        /// Muscle 03 verdict 8: per-species happy / neutral / sad-sick poses.
        /// Same grammar for all (upright + bouncy vs drooped + dragging) with
        /// species-dependent expression: bob style, tempo, squash, lean, pace.
        /// </summary>
        private static void ApplyAnimProfile(SpiritSpeciesDefinition def)
        {
            var p = new SpiritAnimProfile();
            switch (def.id)
            {
                case "mausoleum": // skittish mouse: quick little hops; sad = huddled and tilted
                    p.happy = MakePose(SpiritBobStyle.Bounce, 1.6f, 1.0f, 1.15f, 0f, 0.95f, 1.06f, 0f, 3f, 1f);
                    p.neutral = MakePose(SpiritBobStyle.Float, 1.15f, 0.9f, 1f, 0f, 1f, 1f, 0f, 0f, 1f);
                    p.sadSick = MakePose(SpiritBobStyle.Float, 0.7f, 0.5f, 0.7f, -0.1f, 1.1f, 0.82f, 12f, 0f, 0.55f);
                    break;
                case "bansheep": // placid sheep: slow sway; sad = head hung low and heavy
                    p.happy = MakePose(SpiritBobStyle.Sway, 1.0f, 1.1f, 1.05f, 0f, 1f, 1.06f, 0f, 5f, 1f);
                    p.neutral = MakePose(SpiritBobStyle.Float, 0.8f, 1.0f, 1f, 0f, 1f, 1f, 0f, 0f, 1f);
                    p.sadSick = MakePose(SpiritBobStyle.Float, 0.4f, 0.6f, 0.6f, -0.1f, 1.08f, 0.88f, -8f, 0f, 0.6f);
                    break;
                case "wrabbit": // zoomy rabbit: big springy bounces; sad = ears-down flop
                    p.happy = MakePose(SpiritBobStyle.Bounce, 1.9f, 1.4f, 1.3f, 0f, 0.96f, 1.08f, 0f, 0f, 1f);
                    p.neutral = MakePose(SpiritBobStyle.Bounce, 1.0f, 0.6f, 1f, 0f, 1f, 1f, 0f, 0f, 1f);
                    p.sadSick = MakePose(SpiritBobStyle.Float, 0.5f, 0.5f, 0.65f, -0.1f, 1.05f, 0.88f, 6f, 0f, 0.55f);
                    break;
                case "phantomoth": // moth: fluttery and wing-wide; sad = wings folded, low and still
                    p.happy = MakePose(SpiritBobStyle.Flutter, 1.4f, 1.5f, 1.2f, 0.03f, 1.08f, 1f, 0f, 6f, 1f);
                    p.neutral = MakePose(SpiritBobStyle.Flutter, 0.9f, 1.0f, 1f, 0f, 1f, 1f, 0f, 0f, 1f);
                    p.sadSick = MakePose(SpiritBobStyle.Float, 0.35f, 0.5f, 0.6f, -0.15f, 0.95f, 0.88f, 0f, 0f, 0.5f);
                    break;
                case "wailpertinger": // lumbering cryptid: waddling gait; sad = slumped lean
                    p.happy = MakePose(SpiritBobStyle.Waddle, 1.2f, 1.1f, 1.1f, 0f, 1f, 1.05f, 0f, 0f, 1f);
                    p.neutral = MakePose(SpiritBobStyle.Float, 0.8f, 1.0f, 1f, 0f, 1f, 1f, 0f, 0f, 1f);
                    p.sadSick = MakePose(SpiritBobStyle.Float, 0.4f, 0.5f, 0.6f, -0.1f, 1.06f, 0.88f, -10f, 0f, 0.55f);
                    break;
                case "mothmaus": // omen-squeaker: eerie sway, flutters when pleased; sad = drooped, ashen
                    p.happy = MakePose(SpiritBobStyle.Flutter, 1.3f, 1.2f, 1.15f, 0.02f, 1.04f, 1.02f, 0f, 4f, 1f);
                    p.neutral = MakePose(SpiritBobStyle.Sway, 0.9f, 0.9f, 1f, 0f, 1f, 1f, 0f, 0f, 1f);
                    p.sadSick = MakePose(SpiritBobStyle.Float, 0.4f, 0.5f, 0.6f, -0.12f, 1f, 0.86f, 5f, 0f, 0.5f);
                    break;
            }
            def.animProfile = p;
        }

        private static SpiritPose MakePose(SpiritBobStyle style, float freqMul, float ampMul, float speedMul,
            float yOffset, float scaleX, float scaleY, float tilt, float sway, float saturation)
        {
            return new SpiritPose
            {
                bobStyle = style,
                bobFreqMul = freqMul,
                bobAmpMul = ampMul,
                speedMul = speedMul,
                bodyYOffset = yOffset,
                bodyScale = new Vector2(scaleX, scaleY),
                tiltDegrees = tilt,
                swayDegrees = sway,
                saturation = saturation
            };
        }

        private static void Personality(SpiritSpeciesDefinition def, HabitatPreference habitat,
            float nap, float stretch, float hop, float leaf,
            float pitch, float contour, float chirpSeconds)
        {
            def.habitatPreference = habitat;
            def.napWeight = nap;
            def.stretchWeight = stretch;
            def.hopWeight = hop;
            def.leafChaseWeight = leaf;
            def.voiceBasePitch = pitch;
            def.voiceContour = contour;
            def.voiceChirpSeconds = chirpSeconds;
        }

        /// <summary>Slice 04: each species' authored final wish (skeleton text — owner rewrites).</summary>
        private static void ApplyFinalTask(SpiritSpeciesDefinition def)
        {
            switch (def.id)
            {
                case "mausoleum":
                    def.taskKind = FinalTaskKind.GiveItem;
                    def.taskItemId = "berry"; def.taskItemCount = 1;
                    def.taskDescription = "A last taste of something it never dared try.";
                    def.taskHint = "(it hungers for one perfect murkberry)";
                    break;
                case "bansheep":
                    def.taskKind = FinalTaskKind.GiveItem;
                    def.taskItemId = "bloom"; def.taskItemCount = 3;
                    def.taskDescription = "Flowers for its own funeral. It insists on attending.";
                    def.taskHint = "(it wants gravebloom blossoms - three, for symmetry)";
                    break;
                case "wrabbit":
                    def.taskKind = FinalTaskKind.WaterNearHome;
                    def.taskRadius = 3;
                    def.taskDescription = "It always wanted waterfront property.";
                    def.taskHint = "(dig water near its home)";
                    break;
                case "phantomoth":
                    def.taskKind = FinalTaskKind.SootheInWindow;
                    def.taskWindowStartHour = 0f; def.taskWindowEndHour = 1f;
                    def.taskDescription = "One last moonlit dance, at the stroke of midnight.";
                    def.taskHint = "(sit with it at midnight)";
                    break;
            }
        }

        private static void MakeCryptid(string id, string displayName, string spriteName, string flavor,
            string favoredFood, string taskItem, int taskCount, string taskDesc, string taskHint)
        {
            var def = GetOrCreate<SpiritSpeciesDefinition>($"{SpiritDir}/{displayName}.asset");
            def.id = id;
            def.displayName = displayName;
            def.flavor = flavor;
            def.bodySprite = LoadSprite(spriteName);
            def.gateChain = null; // never appears wild — loom-only
            def.favoredFoodId = favoredFood;
            def.maxResidents = 1;
            def.activity = ActivityWindow.Always;
            def.hungerHours = 16f;
            def.homeSprite = LoadSprite("home_" + (id == "wailpertinger" ? "wrabbit" : "phantomoth")); // parent-style homes for now
            if (!string.IsNullOrEmpty(taskItem))
            {
                def.taskKind = FinalTaskKind.GiveItem;
                def.taskItemId = taskItem;
                def.taskItemCount = taskCount;
            }
            else
            {
                def.taskKind = FinalTaskKind.SootheInWindow;
                def.taskWindowStartHour = 0f;
                def.taskWindowEndHour = 2f;
            }
            def.taskDescription = taskDesc;
            def.taskHint = taskHint;
            ApplyPersonality(def);
            EditorUtility.SetDirty(def);
        }

        private static void GenerateRecipes()
        {
            MakeRecipe("recipe_wailpertinger", "Wrabbit", "Bansheep", "Wailpertinger", essenceCost: 6);
            MakeRecipe("recipe_mothmaus", "Mausoleum", "Phantomoth", "Mothmaus", essenceCost: 6);
        }

        private static void MakeRecipe(string assetName, string a, string b, string result, int essenceCost)
        {
            var recipe = GetOrCreate<WeaveRecipe>($"{SpiritDir}/{assetName}.asset");
            recipe.parentA = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{a}.asset");
            recipe.parentB = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{b}.asset");
            recipe.result = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{result}.asset");
            recipe.essenceCost = essenceCost;
            EditorUtility.SetDirty(recipe);
        }

        // ---- identity (muscle 05) -----------------------------------------------

        /// <summary>
        /// Species Nature bands: (min, max) per stat, individuals roll inside.
        /// Species identity lives here - the mouse is quick and slight, the
        /// sheep is a heavy lifter, the moths shine. Bands sit inside 1..10.
        /// </summary>
        private static void ApplyStatBands(SpiritSpeciesDefinition def)
        {
            switch (def.id)
            {
                case "mausoleum": // small, quick, modest shine
                    Bands(def, 2, 5, 4, 8, 3, 7);
                    break;
                case "bansheep": // big and sturdy, slow on its feet
                    Bands(def, 5, 9, 2, 5, 3, 7);
                    break;
                case "wrabbit": // all legs
                    Bands(def, 3, 6, 6, 10, 2, 6);
                    break;
                case "phantomoth": // frail, graceful, glowing
                    Bands(def, 2, 4, 5, 9, 5, 10);
                    break;
                case "wailpertinger": // woven: horn strength + rabbit legs
                    Bands(def, 5, 9, 5, 9, 4, 8);
                    break;
                case "mothmaus": // woven: the shiniest thing in the dark
                    Bands(def, 2, 5, 4, 8, 6, 10);
                    break;
            }
        }

        private static void Bands(SpiritSpeciesDefinition def,
            int vMin, int vMax, int gMin, int gMax, int lMin, int lMax)
        {
            def.vigorBand = new Vector2Int(vMin, vMax);
            def.graceBand = new Vector2Int(gMin, gMax);
            def.gleamBand = new Vector2Int(lMin, lMax);
        }

        /// <summary>Trait pool assets, authored from the single in-code spec table.</summary>
        private static void GenerateTraits()
        {
            EnsureFolder(TraitDir);
            foreach (var spec in SpiritTraits.DefaultSpecs)
            {
                var trait = GetOrCreate<SpiritTraitDefinition>($"{TraitDir}/Trait_{spec.id}.asset");
                SpiritTraits.Apply(spec, trait);
                EditorUtility.SetDirty(trait);
            }
        }

        public static SpiritTraitDefinition[] LoadAllTraits()
        {
            var specs = SpiritTraits.DefaultSpecs;
            var result = new SpiritTraitDefinition[specs.Length];
            for (int i = 0; i < specs.Length; i++)
                result[i] = AssetDatabase.LoadAssetAtPath<SpiritTraitDefinition>($"{TraitDir}/Trait_{specs[i].id}.asset");
            return result;
        }

        public static WeaveRecipe[] LoadAllRecipes() => new[]
        {
            AssetDatabase.LoadAssetAtPath<WeaveRecipe>($"{SpiritDir}/recipe_wailpertinger.asset"),
            AssetDatabase.LoadAssetAtPath<WeaveRecipe>($"{SpiritDir}/recipe_mothmaus.asset")
        };

        public static SpiritSpeciesDefinition[] LoadAllSpiritSpecies() => WithFrontierSpecies(new[]
        {
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Mausoleum.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Bansheep.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Wrabbit.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Phantomoth.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Wailpertinger.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Mothmaus.asset")
        });

        private static ConditionAsset Cond<T>(string name, params (string field, object value)[] fields)
            where T : ConditionAsset
        {
            var asset = GetOrCreate<T>($"{GateDir}/{name}.asset");
            foreach (var (field, value) in fields) SetField(asset, field, value);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static RequirementSet Set(string name, params ConditionAsset[] conditions)
        {
            var set = GetOrCreate<RequirementSet>($"{GateDir}/{name}.asset");
            SetField(set, "conditions", conditions);
            EditorUtility.SetDirty(set);
            return set;
        }

        private static void Chain(string assetName, string chainId,
            RequirementSet appear, RequirementSet visit, RequirementSet resident, RequirementSet fulfil)
        {
            var chain = GetOrCreate<GateChain>($"{GateDir}/{assetName}.asset");
            SetField(chain, "chainId", chainId);
            SetField(chain, "appear", appear);
            SetField(chain, "visit", visit);
            SetField(chain, "resident", resident);
            SetField(chain, "fulfil", fulfil);
            EditorUtility.SetDirty(chain);
        }

        public static GateChain[] LoadAllChains() => WithFrontierChains(new[]
        {
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_mausoleum.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_bansheep.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_wrabbit.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_phantomoth.asset")
        });

        public static Tile LoadTile(string name) =>
            AssetDatabase.LoadAssetAtPath<Tile>($"{TileDir}/{name}.asset");

        // ---- plumbing --------------------------------------------------------

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                int slash = path.LastIndexOf('/');
                AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
            }
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static Sprite LoadSprite(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + name + ".png");
            if (s == null) Debug.LogError("[Content] Missing sprite: " + name);
            return s;
        }

        /// <summary>Sets a field by name regardless of visibility (works for [SerializeField] privates and publics).</summary>
        public static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var f = type.GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(target, value); return; }
                type = type.BaseType;
            }
            Debug.LogError($"[Content] Field '{fieldName}' not found on {target.GetType().Name}");
        }
    }
}

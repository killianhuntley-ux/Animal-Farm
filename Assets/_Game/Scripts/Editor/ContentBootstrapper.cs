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
    public static class ContentBootstrapper
    {
        private const string ArtDir = "Assets/_Game/Art/Placeholder/";
        private const string TileDir = "Assets/_Game/Art/Tiles";
        private const string PlantDir = "Assets/_Game/Data/Plants";
        private const string GateDir = "Assets/_Game/Data/Gates";
        private const string SpiritDir = "Assets/_Game/Data/Spirits";

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
            MakePlant("murkberry", "Murkberry", new[] { "plant_sprout", "murkberry_mid", "murkberry_ripe" },
                hoursPerStage: 10f, produceId: "berry", produceAmount: 3);
        }

        private static void MakePlant(string id, string displayName, string[] stageSprites,
            float hoursPerStage, string produceId, int produceAmount)
        {
            var species = GetOrCreate<PlantSpecies>($"{PlantDir}/{displayName}.asset");
            var sprites = new Sprite[stageSprites.Length];
            for (int i = 0; i < stageSprites.Length; i++) sprites[i] = LoadSprite(stageSprites[i]);

            SetField(species, "id", id);
            SetField(species, "displayName", displayName);
            SetField(species, "stageSprites", sprites);
            SetField(species, "hoursPerStage", hoursPerStage);
            SetField(species, "requiredSurface", Surface.Dirt);
            SetField(species, "produceId", produceId);
            SetField(species, "produceAmount", produceAmount);
            EditorUtility.SetDirty(species);
        }

        public static PlantSpecies[] LoadAllPlantSpecies() => new[]
        {
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Palewheat.asset"),
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Gravebloom.asset"),
            AssetDatabase.LoadAssetAtPath<PlantSpecies>($"{PlantDir}/Murkberry.asset")
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

            // species gate sets — resident mirrors visit (residency itself is
            // per-individual feeding, handled by SpiritAgent); fulfil = slice 04.
            var mausAppear = Set("set_mausoleum_appear", grass8);
            var mausVisit = Set("set_mausoleum_visit", grass15);
            var shepAppear = Set("set_bansheep_appear", wheat2Any);
            var shepVisit = Set("set_bansheep_visit", wheat2Ripe, daytime);
            var wrabAppear = Set("set_wrabbit_appear", resMausoleum);
            var wrabVisit = Set("set_wrabbit_visit", grass20, resMausoleum);
            var mothAppear = Set("set_phantomoth_appear", night, bloom1Any);
            var mothVisit = Set("set_phantomoth_visit", night, bloom2Ripe);

            Chain("chain_mausoleum", "mausoleum", mausAppear, mausVisit, mausVisit, null);
            Chain("chain_bansheep", "bansheep", shepAppear, shepVisit, shepVisit, null);
            Chain("chain_wrabbit", "wrabbit", wrabAppear, wrabVisit, wrabVisit, null);
            Chain("chain_phantomoth", "phantomoth", mothAppear, mothVisit, mothVisit, null);
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
                "chain_mausoleum", favoredFood: "wheat", foodCount: 1, maxResidents: 3,
                activity: ActivityWindow.Always, hungerHours: 14f);
            MakeSpirit("bansheep", "Bansheep", "bansheep_body",
                "It wails at shearing time. Nobody has ever sheared it.",
                "chain_bansheep", favoredFood: "wheat", foodCount: 2, maxResidents: 2,
                activity: ActivityWindow.Day, hungerHours: 12f);
            MakeSpirit("wrabbit", "Wrabbit", "wrabbit_body",
                "Quick in life. Quicker now.",
                "chain_wrabbit", favoredFood: "berry", foodCount: 2, maxResidents: 2,
                activity: ActivityWindow.Always, hungerHours: 10f);
            MakeSpirit("phantomoth", "Phantomoth", "phantomoth_body",
                "Drawn to lights it can no longer feel.",
                "chain_phantomoth", favoredFood: "bloom", foodCount: 2, maxResidents: 2,
                activity: ActivityWindow.Night, hungerHours: 12f);
        }

        private static void MakeSpirit(string id, string displayName, string spriteName, string flavor,
            string chainAsset, string favoredFood, int foodCount, int maxResidents,
            ActivityWindow activity, float hungerHours)
        {
            var def = GetOrCreate<SpiritSpeciesDefinition>($"{SpiritDir}/{displayName}.asset");
            def.id = id;
            def.displayName = displayName;
            def.flavor = flavor;
            def.bodySprite = LoadSprite(spriteName);
            def.gateChain = AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/{chainAsset}.asset");
            def.favoredFoodId = favoredFood;
            def.residencyFoodCount = foodCount;
            def.maxResidents = maxResidents;
            def.activity = activity;
            def.hungerHours = hungerHours;
            def.homeSprite = LoadSprite("home_" + id);
            ApplyFinalTask(def);
            EditorUtility.SetDirty(def);
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
            def.residencyFoodCount = 1;
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
            EditorUtility.SetDirty(def);
        }

        private static void GenerateRecipes()
        {
            MakeRecipe("recipe_wailpertinger", "Wrabbit", "Bansheep", "Wailpertinger");
            MakeRecipe("recipe_mothmaus", "Mausoleum", "Phantomoth", "Mothmaus");
        }

        private static void MakeRecipe(string assetName, string a, string b, string result)
        {
            var recipe = GetOrCreate<WeaveRecipe>($"{SpiritDir}/{assetName}.asset");
            recipe.parentA = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{a}.asset");
            recipe.parentB = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{b}.asset");
            recipe.result = AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/{result}.asset");
            EditorUtility.SetDirty(recipe);
        }

        public static WeaveRecipe[] LoadAllRecipes() => new[]
        {
            AssetDatabase.LoadAssetAtPath<WeaveRecipe>($"{SpiritDir}/recipe_wailpertinger.asset"),
            AssetDatabase.LoadAssetAtPath<WeaveRecipe>($"{SpiritDir}/recipe_mothmaus.asset")
        };

        public static SpiritSpeciesDefinition[] LoadAllSpiritSpecies() => new[]
        {
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Mausoleum.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Bansheep.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Wrabbit.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Phantomoth.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Wailpertinger.asset"),
            AssetDatabase.LoadAssetAtPath<SpiritSpeciesDefinition>($"{SpiritDir}/Mothmaus.asset")
        };

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

        public static GateChain[] LoadAllChains() => new[]
        {
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_mausoleum.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_bansheep.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_wrabbit.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_phantomoth.asset")
        };

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

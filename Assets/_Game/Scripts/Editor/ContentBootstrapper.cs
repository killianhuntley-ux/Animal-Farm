using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using AnimalFarm.Requirements;
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

        [MenuItem("AnimalFarm/Generate Content Assets")]
        public static void Generate()
        {
            EnsureFolder("Assets/_Game/Data");
            EnsureFolder(TileDir);
            EnsureFolder(PlantDir);
            EnsureFolder(GateDir);

            GenerateTiles();
            GeneratePlants();
            GenerateGates();

            AssetDatabase.SaveAssets();
            Debug.Log("[Content] Tiles, plant species and gate chains generated.");
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
            // conditions
            var grass10 = Cond<SurfacePercentCondition>("cond_grass_10",
                ("surface", Surface.Grass), ("minPercent", 10f));
            var grass20 = Cond<SurfacePercentCondition>("cond_grass_20",
                ("surface", Surface.Grass), ("minPercent", 20f));
            var water3 = Cond<SurfacePercentCondition>("cond_water_3",
                ("surface", Surface.Water), ("minPercent", 3f));
            var water6 = Cond<SurfacePercentCondition>("cond_water_6",
                ("surface", Surface.Water), ("minPercent", 6f));
            var daytime = Cond<TimeOfDayCondition>("cond_daytime",
                ("startHour", 6f), ("endHour", 18f));
            var night = Cond<TimeOfDayCondition>("cond_night",
                ("startHour", 20f), ("endHour", 4f));
            var wheat3 = Cond<PlantCountCondition>("cond_palewheat_3ripe",
                ("speciesId", "palewheat"), ("minStage", 2), ("minCount", 3));
            var berry2 = Cond<PlantCountCondition>("cond_murkberry_2ripe",
                ("speciesId", "murkberry"), ("minStage", 2), ("minCount", 2));
            var resMeadow = Cond<ResidentPresentCondition>("cond_res_meadow",
                ("speciesId", "meadow_visitor"), ("minCount", 1));
            var resPond = Cond<ResidentPresentCondition>("cond_res_pond",
                ("speciesId", "pond_lurker"), ("minCount", 1));

            // sets
            var meadowAppear = Set("set_meadow_appear", grass10);
            var meadowVisit = Set("set_meadow_visit", grass20, daytime);
            var meadowResident = Set("set_meadow_resident", wheat3);
            var meadowFulfil = Set("set_meadow_fulfil", resMeadow);
            var pondAppear = Set("set_pond_appear", water3);
            var pondVisit = Set("set_pond_visit", water6, night);
            var pondResident = Set("set_pond_resident", berry2);
            var pondFulfil = Set("set_pond_fulfil", resPond);

            // chains
            Chain("chain_meadow_visitor", "meadow_visitor", meadowAppear, meadowVisit, meadowResident, meadowFulfil);
            Chain("chain_pond_lurker", "pond_lurker", pondAppear, pondVisit, pondResident, pondFulfil);
        }

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
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_meadow_visitor.asset"),
            AssetDatabase.LoadAssetAtPath<GateChain>($"{GateDir}/chain_pond_lurker.asset")
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

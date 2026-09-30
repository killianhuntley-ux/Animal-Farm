using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using AnimalFarm.Competitions;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Debugging;
using AnimalFarm.Interaction;
using AnimalFarm.Player;
using AnimalFarm.Requirements;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using AnimalFarm.World;

namespace AnimalFarm.EditorTools
{
    /// <summary>
    /// Builds the greybox Prairie scene from code — repeatable, no manual editor
    /// work (slice 01). Safe to re-run: overwrites Prairie.unity.
    /// Run via menu or: -batchmode -executeMethod AnimalFarm.EditorTools.SceneBootstrapper.Generate
    /// </summary>
    public static class SceneBootstrapper
    {
        private const string ScenePath = "Assets/_Game/Scenes/Prairie.unity";
        private const string ArtDir = "Assets/_Game/Art/Placeholder/";
        // UX pass: 80x50 was a camera-test size; the farm is a fenced 40x26 plot
        // so border silhouettes are always discoverable from the middle.
        private const float FieldW = 40f, FieldH = 26f;

        [MenuItem("AnimalFarm/Build Prairie Scene")]
        public static void Generate()
        {
            PlaceholderArtGenerator.Generate();
            ContentBootstrapper.Generate();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material litMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat");
            if (litMat == null) Debug.LogWarning("[Bootstrap] Sprite-Lit-Default not found; sprites will be unlit.");

            // --- Managers -------------------------------------------------
            var managers = new GameObject("_Managers");
            var input = managers.AddComponent<GameInput>();
            AssignPrivateField(input, "actions",
                AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Game/Input/Controls.inputactions"));
            managers.AddComponent<GameClock>();
            managers.AddComponent<GameManager>();
            managers.AddComponent<SaveSystem>();
            managers.AddComponent<DebugConsole>();
            managers.AddComponent<Inventory>();
            managers.AddComponent<GameSettings>();

            // --- Global light + day/night ---------------------------------
            var lightGo = new GameObject("GlobalLight2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            lightGo.AddComponent<DayNightLight>();

            // --- Terrain (slice 02: authoritative grid + tilemap view) ------
            var terrainRoot = new GameObject("Terrain");
            terrainRoot.AddComponent<Grid>(); // 1x1 cells at origin
            var tilemapGo = new GameObject("Tilemap");
            tilemapGo.transform.SetParent(terrainRoot.transform, false);
            var tilemap = tilemapGo.AddComponent<Tilemap>();
            var tilemapRenderer = tilemapGo.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = -1000;
            if (litMat) tilemapRenderer.sharedMaterial = litMat;
            var tilemapCollider = tilemapGo.AddComponent<TilemapCollider2D>(); // water blocks walking

            var terrainGrid = terrainRoot.AddComponent<TerrainGrid>();
            AssignPrivateField(terrainGrid, "width", (int)FieldW);
            AssignPrivateField(terrainGrid, "height", (int)FieldH);
            AssignPrivateField(terrainGrid, "tilemap", tilemap);
            AssignPrivateField(terrainGrid, "scrubTile", ContentBootstrapper.LoadTile("Tile_Scrub"));
            AssignPrivateField(terrainGrid, "dirtTile", ContentBootstrapper.LoadTile("Tile_Dirt"));
            AssignPrivateField(terrainGrid, "grassTile", ContentBootstrapper.LoadTile("Tile_Grass"));
            AssignPrivateField(terrainGrid, "waterTile", ContentBootstrapper.LoadTile("Tile_Water"));

            // --- World systems ----------------------------------------------
            var plantSpecies = ContentBootstrapper.LoadAllPlantSpecies();
            var worldSystems = new GameObject("_WorldSystems");
            var plantManager = worldSystems.AddComponent<PlantManager>();
            AssignPrivateField(plantManager, "knownSpecies", plantSpecies);
            AssignPrivateField(plantManager, "spriteMaterial", litMat);
            var evaluator = worldSystems.AddComponent<RequirementEvaluator>();
            AssignPrivateField(evaluator, "chains", ContentBootstrapper.LoadAllChains());
            var spiritManager = worldSystems.AddComponent<SpiritManager>();
            var spiritSpecies = ContentBootstrapper.LoadAllSpiritSpecies();
            AssignPrivateField(spiritManager, "knownSpecies", spiritSpecies);
            AssignPrivateField(spiritManager, "spriteMaterial", litMat);
            AssignPrivateField(spiritManager, "recipes", ContentBootstrapper.LoadAllRecipes());
            var homeManager = worldSystems.AddComponent<HomeManager>();
            AssignPrivateField(homeManager, "spriteMaterial", litMat);
            var headstones = worldSystems.AddComponent<HeadstoneRegistry>();
            AssignPrivateField(headstones, "headstoneSprite", Sprite(ArtDir + "headstone.png"));
            AssignPrivateField(headstones, "spriteMaterial", litMat);

            var compManager = worldSystems.AddComponent<CompetitionManager>();
            AssignPrivateField(compManager, "spriteMaterial", litMat);
            var repoManager = worldSystems.AddComponent<RepoManManager>();
            AssignPrivateField(repoManager, "repoSprite", Sprite(ArtDir + "repoman_body.png"));
            AssignPrivateField(repoManager, "spriteMaterial", litMat);
            var parcelManager = worldSystems.AddComponent<ParcelManager>();
            AssignPrivateField(parcelManager, "spriteMaterial", litMat);
            AssignPrivateField(parcelManager, "groundSprite", Sprite(ArtDir + "ground_grass.png"));
            AssignPrivateField(parcelManager, "fencePostSprite", Sprite(ArtDir + "fence_post.png"));
            AssignPrivateField(parcelManager, "whiteRect", Sprite(ArtDir + "white_rect.png"));

            // --- Competition Board (slice 05; lives in TOWN per owner decision) --
            var boardGo = new GameObject("CompetitionBoard");
            boardGo.transform.position = new Vector3(37f, 0f, 0f);
            boardGo.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            var board = boardGo.AddComponent<CompetitionBoard>();
            AssignPrivateField(board, "boardSprite", Sprite(ArtDir + "notice_board.png"));
            AssignPrivateField(board, "spriteMaterial", litMat);

            // --- The Loom (slice 06: weaving ritual site, west side) -----------
            var loomGo = new GameObject("TheLoom");
            loomGo.transform.position = new Vector3(-12f, 6f, 0f);
            var loom = loomGo.AddComponent<TheLoom>();
            AssignPrivateField(loom, "loomSprite", Sprite(ArtDir + "loom.png"));
            AssignPrivateField(loom, "spriteMaterial", litMat);

            // --- Ascension Altar (slice 04) ----------------------------------
            var altarGo = new GameObject("AscensionAltar");
            altarGo.transform.position = new Vector3(0f, 8f, 0f);
            var altar = altarGo.AddComponent<AscensionAltar>();
            AssignPrivateField(altar, "platformSprite", Sprite(ArtDir + "altar_platform.png"));
            AssignPrivateField(altar, "glowSprite", Sprite(ArtDir + "altar_glow.png"));
            AssignPrivateField(altar, "columnSprite", Sprite(ArtDir + "altar_column.png"));
            AssignPrivateField(altar, "spriteMaterial", litMat);

            // --- Farm fence (visual border on the collider walls) -------------
            var fenceParent = new GameObject("Fence");
            var postSprite = Sprite(ArtDir + "fence_post.png");
            var railTint = new Color(0.42f, 0.31f, 0.22f);
            const float postSpacing = 1.6f;
            void PlacePost(float x, float y)
            {
                var post = new GameObject("Post");
                post.transform.SetParent(fenceParent.transform);
                post.transform.position = new Vector3(x, y, 0f);
                var psr = post.AddComponent<SpriteRenderer>();
                psr.sprite = postSprite;
                psr.sortingOrder = 20;
                if (litMat) psr.sharedMaterial = litMat;
            }
            for (float x = -FieldW / 2; x <= FieldW / 2 + 0.01f; x += postSpacing)
            {
                PlacePost(x, FieldH / 2);
                PlacePost(x, -FieldH / 2);
            }
            for (float y = -FieldH / 2 + postSpacing; y < FieldH / 2; y += postSpacing)
            {
                PlacePost(-FieldW / 2, y);
                if (Mathf.Abs(y) > 2.4f) PlacePost(FieldW / 2, y); // leave the town gate open
            }
            void PlaceRail(Vector2 pos, Vector2 size)
            {
                var rail = new GameObject("Rail");
                rail.transform.SetParent(fenceParent.transform);
                rail.transform.position = pos;
                var rsr = rail.AddComponent<SpriteRenderer>();
                rsr.sprite = Sprite(ArtDir + "white_rect.png");
                rsr.drawMode = SpriteDrawMode.Tiled;
                rsr.size = size;
                rsr.color = railTint;
                rsr.sortingOrder = 19;
                if (litMat) rsr.sharedMaterial = litMat;
            }
            PlaceRail(new Vector2(0, FieldH / 2 + 0.1f), new Vector2(FieldW, 0.12f));
            PlaceRail(new Vector2(0, -FieldH / 2 + 0.1f), new Vector2(FieldW, 0.12f));
            PlaceRail(new Vector2(-FieldW / 2, 0.1f), new Vector2(0.12f, FieldH));
            // east rail splits around the town gate
            PlaceRail(new Vector2(FieldW / 2, (FieldH / 2 + 2.4f) / 2f), new Vector2(0.12f, FieldH / 2 - 2.4f));
            PlaceRail(new Vector2(FieldW / 2, -(FieldH / 2 + 2.4f) / 2f), new Vector2(0.12f, FieldH / 2 - 2.4f));

            // --- Scatter detail (pebbles only — tufts clash with tile look) --
            var scatterParent = new GameObject("Scatter");
            var rng = new System.Random(42);
            var pebble = Sprite(ArtDir + "pebble.png");
            for (int i = 0; i < 26; i++)
            {
                var go = new GameObject("Pebble");
                go.transform.SetParent(scatterParent.transform);
                go.transform.position = RandomInField(rng, margin: 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = pebble;
                sr.sortingOrder = -10; // below actors, above ground
                if (litMat) sr.sharedMaterial = litMat;
                float s = 0.7f + (float)rng.NextDouble() * 0.6f;
                go.transform.localScale = new Vector3(s, s, 1f);
            }

            // --- Bounds (east wall has a gate at y in [-2, 2] leading to town) --
            var bounds = new GameObject("Bounds");
            // North wall in two NAMED halves — ParcelManager destroys them by
            // name when the matching parcel is purchased (slice 08 contract).
            var northW = new GameObject("NorthWall_P0");
            northW.transform.SetParent(bounds.transform);
            northW.transform.position = new Vector2(-FieldW / 4, FieldH / 2 + 0.5f);
            northW.AddComponent<BoxCollider2D>().size = new Vector2(FieldW / 2, 1);
            var northE = new GameObject("NorthWall_P1");
            northE.transform.SetParent(bounds.transform);
            northE.transform.position = new Vector2(FieldW / 4, FieldH / 2 + 0.5f);
            northE.AddComponent<BoxCollider2D>().size = new Vector2(FieldW / 2, 1);
            AddWall(bounds, new Vector2(0, -FieldH / 2 - 0.5f), new Vector2(FieldW, 1));
            AddWall(bounds, new Vector2(-FieldW / 2 - 0.5f, 0), new Vector2(1, FieldH));
            float gateHalf = 2f;
            float eastSegH = FieldH / 2 - gateHalf;
            AddWall(bounds, new Vector2(FieldW / 2 + 0.5f, gateHalf + eastSegH / 2), new Vector2(1, eastSegH));
            AddWall(bounds, new Vector2(FieldW / 2 + 0.5f, -gateHalf - eastSegH / 2), new Vector2(1, eastSegH));

            // --- Town (slice 09 pulled forward: competitions live in town) -----
            // Contained plaza east of the farm gate: x in [20.5, 46], y in [-9, 9].
            AddWall(bounds, new Vector2(33.25f, 9.5f), new Vector2(26f, 1f));
            AddWall(bounds, new Vector2(33.25f, -9.5f), new Vector2(26f, 1f));
            AddWall(bounds, new Vector2(46.5f, 0f), new Vector2(1f, 20f));

            var town = new GameObject("Town");
            var townGround = new GameObject("TownGround");
            townGround.transform.SetParent(town.transform);
            townGround.transform.position = new Vector3(33.5f, 0f, 0f);
            var tgSr = townGround.AddComponent<SpriteRenderer>();
            tgSr.sprite = Sprite(ArtDir + "ground_grass.png");
            tgSr.drawMode = SpriteDrawMode.Tiled;
            tgSr.size = new Vector2(25f, 18f);
            tgSr.color = new Color(0.78f, 0.70f, 0.55f); // packed-dirt plaza
            tgSr.sortingOrder = -950;
            if (litMat) tgSr.sharedMaterial = litMat;

            void PlaceStall(Vector2 pos, string label, Color tint)
            {
                var stall = new GameObject("Stall");
                stall.transform.SetParent(town.transform);
                stall.transform.position = pos;
                var ssr = stall.AddComponent<SpriteRenderer>();
                ssr.sprite = Sprite(ArtDir + "notice_board.png");
                ssr.color = tint;
                ssr.sortingOrder = 0;
                if (litMat) ssr.sharedMaterial = litMat;
                stall.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                AnimalFarm.UI.WorldLabel.Attach(stall, label, -0.9f);
            }
            PlaceStall(new Vector2(30f, 4.5f), "Vendor (coming soon)", new Color(0.8f, 0.75f, 0.9f));
            PlaceStall(new Vector2(30f, -4.5f), "Ferryman (coming soon)", new Color(0.7f, 0.85f, 0.9f));

            // Holding Office (slice 07): where repossessed spirits await fees.
            var officeGo = new GameObject("HoldingOffice");
            officeGo.transform.SetParent(town.transform);
            officeGo.transform.position = new Vector3(42f, 5f, 0f);
            var office = officeGo.AddComponent<HoldingOffice>();
            AssignPrivateField(office, "officeSprite", Sprite(ArtDir + "holding_office.png"));
            AssignPrivateField(office, "spriteMaterial", litMat);

            // --- Rocks (obstacles) -----------------------------------------
            var rocksParent = new GameObject("Rocks");
            var rockSprite = Sprite(ArtDir + "rock.png");
            for (int i = 0; i < 4; i++)
            {
                var rock = new GameObject("Rock");
                rock.transform.SetParent(rocksParent.transform);
                rock.transform.position = RandomInField(rng, margin: 6f);
                var sr = rock.AddComponent<SpriteRenderer>();
                sr.sprite = rockSprite;
                if (litMat) sr.sharedMaterial = litMat;
                var col = rock.AddComponent<CircleCollider2D>();
                col.radius = 0.28f;
                col.offset = new Vector2(0, -0.05f);
                float s = 1f + (float)rng.NextDouble() * 1.4f;
                rock.transform.localScale = new Vector3(s, s, 1f);
            }

            // --- Skins ------------------------------------------------------
            var skinA = MakeSkin("Skin_Default", ArtDir + "shepherd_body.png", ArtDir + "shepherd_head.png");
            var skinB = MakeSkin("Skin_Alt", ArtDir + "shepherd_body_alt.png", ArtDir + "shepherd_head_alt.png");

            // --- Shepherd ---------------------------------------------------
            var shepherd = new GameObject("Shepherd") { tag = "Player" };
            var rb = shepherd.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var capsule = shepherd.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.55f, 0.45f);
            capsule.offset = new Vector2(0f, -0.35f); // feet, for 3/4 depth feel
            capsule.direction = CapsuleDirection2D.Horizontal;
            shepherd.AddComponent<ShepherdController>();
            shepherd.AddComponent<InteractionSensor>();
            shepherd.AddComponent<PlantingInteractor>();

            // Tool system: reticle child + controller (slice 02)
            var reticleGo = new GameObject("ToolReticle");
            reticleGo.transform.SetParent(shepherd.transform, false);
            var reticleSr = reticleGo.AddComponent<SpriteRenderer>();
            reticleSr.sprite = Sprite(ArtDir + "white_rect.png");
            reticleSr.sortingOrder = 50;
            reticleSr.color = new Color(1f, 1f, 1f, 0.5f);
            reticleGo.transform.localScale = new Vector3(2f, 2f, 1f); // 32px @ 64ppu -> 1 unit
            var toolController = shepherd.AddComponent<ToolController>();
            AssignPrivateField(toolController, "seedSpecies", plantSpecies);
            AssignPrivateField(toolController, "homeSpecies", spiritSpecies);
            AssignPrivateField(toolController, "reticle", reticleSr);

            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(shepherd.transform, false);
            var body = NewChildSprite(visualRoot.transform, "Body", null, litMat, order: 0);
            body.transform.localPosition = new Vector3(0, 0.1f, 0);
            var head = NewChildSprite(visualRoot.transform, "Head", null, litMat, order: 1);
            head.transform.localPosition = new Vector3(0, 0.62f, 0);
            var visual = visualRoot.AddComponent<ShepherdVisual>();
            AssignPrivateField(visual, "skin", skinA);
            AssignPrivateField(visual, "alternateSkin", skinB);
            AssignPrivateField(visual, "bodyRenderer", body.GetComponent<SpriteRenderer>());
            AssignPrivateField(visual, "headRenderer", head.GetComponent<SpriteRenderer>());

            // --- Waystone (interaction test object) ------------------------
            var waystone = new GameObject("Waystone");
            waystone.transform.position = new Vector3(4f, 2f, 0);
            var wsSr = waystone.AddComponent<SpriteRenderer>();
            wsSr.sprite = Sprite(ArtDir + "waystone.png");
            if (litMat) wsSr.sharedMaterial = litMat;
            var wsCol = waystone.AddComponent<BoxCollider2D>();
            wsCol.isTrigger = true;
            wsCol.size = new Vector2(1.2f, 1.4f);
            waystone.AddComponent<Waystone>();

            // --- Camera -----------------------------------------------------
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.backgroundColor = new Color(0.16f, 0.20f, 0.14f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0, 1, 0); // 3/4-view y-sort
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var follow = camGo.AddComponent<CameraFollow>();
            AssignPrivateField(follow, "target", shepherd.transform);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<SelectionController>();

            // --- UI + EventSystem -------------------------------------------
            var ui = new GameObject("_UI");
            ui.AddComponent<ClockHUD>();
            ui.AddComponent<InteractPromptUI>();
            ui.AddComponent<PauseMenu>();
            ui.AddComponent<ToolHUD>();
            ui.AddComponent<RequirementDebugOverlay>();
            ui.AddComponent<NamePromptUI>();
            ui.AddComponent<JournalUI>();
            ui.AddComponent<InventoryHUD>();
            ui.AddComponent<ToolbeltUI>();
            ui.AddComponent<SeedPickerUI>();
            ui.AddComponent<CompetitionEntryUI>();
            ui.AddComponent<SpiritInfoUI>();
            ui.AddComponent<SelectionMenuUI>();
            ui.AddComponent<WeaveUI>();
            ui.AddComponent<AlarmBannerUI>();
            ui.AddComponent<HoldingOfficeUI>();
            ui.AddComponent<HomePickerUI>();

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            // --- Save scene, register in build settings ---------------------
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] Prairie scene generated at " + ScenePath);
        }

        // ---- helpers -------------------------------------------------------

        private static Sprite Sprite(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) Debug.LogError("[Bootstrap] Missing sprite: " + path);
            return s;
        }

        private static Vector3 RandomInField(System.Random rng, float margin)
        {
            float x = Mathf.Lerp(-FieldW / 2 + margin, FieldW / 2 - margin, (float)rng.NextDouble());
            float y = Mathf.Lerp(-FieldH / 2 + margin, FieldH / 2 - margin, (float)rng.NextDouble());
            // keep spawn area near origin clear
            if (Mathf.Abs(x) < 3f && Mathf.Abs(y) < 3f) x += 6f;
            return new Vector3(x, y, 0);
        }

        private static void AddWall(GameObject parent, Vector2 pos, Vector2 size)
        {
            var wall = new GameObject("Wall");
            wall.transform.SetParent(parent.transform);
            wall.transform.position = pos;
            var col = wall.AddComponent<BoxCollider2D>();
            col.size = size;
        }

        private static GameObject NewChildSprite(Transform parent, string name, UnityEngine.Sprite sprite, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            if (sprite != null) sr.sprite = sprite;
            sr.sortingOrder = order;
            if (mat) sr.sharedMaterial = mat;
            return go;
        }

        private static SkinDefinition MakeSkin(string assetName, string bodyPath, string headPath)
        {
            string path = "Assets/_Game/Art/" + assetName + ".asset";
            var skin = AssetDatabase.LoadAssetAtPath<SkinDefinition>(path);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<SkinDefinition>();
                AssetDatabase.CreateAsset(skin, path);
            }
            skin.body = Sprite(bodyPath);
            skin.head = Sprite(headPath);
            EditorUtility.SetDirty(skin);
            return skin;
        }

        private static void AssignPrivateField(object target, string fieldName, object value)
        {
            var f = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (f == null) { Debug.LogError($"[Bootstrap] Field '{fieldName}' not found on {target.GetType().Name}"); return; }
            f.SetValue(target, value);
        }
    }
}

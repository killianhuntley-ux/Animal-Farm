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
        // Muscle 02/08 world restructure (owner): the HOME base is a 3x3
        // cluster of 10x8 parcels (centre owned at start) inside one perimeter
        // fence; unbuyable scrub buffer all around; a gated ROAD corridor runs
        // west to the 2x2 SWAMP satellite cluster; the town stays east through
        // a gate. Parcel deeds live in ParcelManager -- keep geometry in step.
        private const float ParcelW = 10f, ParcelH = 8f;
        // home cluster x[-15,15] y[-12,12]; swamp x[-47,-27] y[-8,8]
        private const float HomeMinX = -15f, HomeMinY = -12f;
        private const float HomeMaxX = 15f, HomeMaxY = 12f;
        private const float SwampMinX = -47f, SwampMinY = -8f;
        private const float SwampMaxX = -27f, SwampMaxY = 8f;
        // road corridor x[-27,-15] y[-2,2] (walkable, never purchasable land)
        private const float RoadMinX = -27f, RoadMaxX = -15f;
        private const float RoadMinY = -2f, RoadMaxY = 2f;
        // terrain grid: swamp + road + home cluster + scrub buffer ring
        private const int GridW = 78, GridH = 40;
        private const float GridOriginX = -56f, GridOriginY = -20f;
        // town plaza east of the home cluster's gate: x[15,37.5] y[-7.5,7.5]
        private const float TownGateHalf = 1.5f;  // east gate half-height
        private const float RoadGateHalf = 2f;    // west (road) gate half-height

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
            // Grid covers swamp + road + home cluster + buffer; the home
            // cluster's CENTRE parcel starts usable. Cell = world - origin.
            AssignPrivateField(terrainGrid, "width", GridW);
            AssignPrivateField(terrainGrid, "height", GridH);
            AssignPrivateField(terrainGrid, "worldOriginX", GridOriginX);
            AssignPrivateField(terrainGrid, "worldOriginY", GridOriginY);
            AssignPrivateField(terrainGrid, "initialUsableX", (int)(-5f - GridOriginX)); // 51
            AssignPrivateField(terrainGrid, "initialUsableY", (int)(-4f - GridOriginY)); // 16
            AssignPrivateField(terrainGrid, "initialUsableW", (int)ParcelW);
            AssignPrivateField(terrainGrid, "initialUsableH", (int)ParcelH);
            // zoning (cell space): clusters draw dimmed-until-bought; the road
            // draws packed earth; the swamp rect carries the murky ground cast
            // and two starter pools (stamped only into fresh worlds).
            var homeCells = new RectInt((int)(HomeMinX - GridOriginX), (int)(HomeMinY - GridOriginY), 30, 24);
            var swampCells = new RectInt((int)(SwampMinX - GridOriginX), (int)(SwampMinY - GridOriginY), 20, 16);
            var roadCells = new RectInt((int)(RoadMinX - GridOriginX), (int)(RoadMinY - GridOriginY), 12, 4);
            AssignPrivateField(terrainGrid, "clusterRects", new[] { homeCells, swampCells });
            AssignPrivateField(terrainGrid, "roadRects", new[] { roadCells });
            AssignPrivateField(terrainGrid, "swampRect", swampCells);
            AssignPrivateField(terrainGrid, "seedWaterRects", new[]
            {
                new RectInt((int)(-44f - GridOriginX), (int)(3f - GridOriginY), 3, 2),  // Reedmire Hollow pool
                new RectInt((int)(-33f - GridOriginX), (int)(-5f - GridOriginY), 3, 2)  // Reedmire Deep pool
            });
            AssignPrivateField(terrainGrid, "tilemap", tilemap);
            AssignPrivateField(terrainGrid, "scrubTile", ContentBootstrapper.LoadTile("Tile_Scrub"));
            AssignPrivateField(terrainGrid, "dirtTile", ContentBootstrapper.LoadTile("Tile_Dirt"));
            AssignPrivateField(terrainGrid, "grassTile", ContentBootstrapper.LoadTile("Tile_Grass"));
            AssignPrivateField(terrainGrid, "waterTile", ContentBootstrapper.LoadTile("Tile_Water"));
            // sandTile stays null: Sand renders via the grid's runtime flat tile.

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
            AssignPrivateField(headstones, "gravePlotOrigin", new Vector2(-4.2f, -3.2f)); // hearth parcel, SW

            var compManager = worldSystems.AddComponent<CompetitionManager>();
            AssignPrivateField(compManager, "spriteMaterial", litMat);
            var repoManager = worldSystems.AddComponent<RepoManManager>();
            AssignPrivateField(repoManager, "repoSprite", Sprite(ArtDir + "repoman_body.png"));
            AssignPrivateField(repoManager, "spriteMaterial", litMat);
            worldSystems.AddComponent<AnimalFarm.Core.BleepsWireup>();
            var essence = worldSystems.AddComponent<EssenceSpawner>();
            AssignPrivateField(essence, "moteSprite", Sprite(ArtDir + "white_circle.png"));
            AssignPrivateField(essence, "spriteMaterial", litMat);
            var onboarding = worldSystems.AddComponent<AnimalFarm.Onboarding.OnboardingManager>();
            AssignPrivateField(onboarding, "glowSprite", Sprite(ArtDir + "altar_glow.png"));
            AssignPrivateField(onboarding, "spriteMaterial", litMat);
            var weedManager = worldSystems.AddComponent<WeedManager>();
            AssignPrivateField(weedManager, "weedSprite", Sprite(ArtDir + "weed_thistle.png"));
            AssignPrivateField(weedManager, "watchlightSprite", Sprite(ArtDir + "watchlight_post.png"));
            AssignPrivateField(weedManager, "spriteMaterial", litMat);
            var parcelManager = worldSystems.AddComponent<ParcelManager>();
            AssignPrivateField(parcelManager, "spriteMaterial", litMat);
            AssignPrivateField(parcelManager, "whiteRect", Sprite(ArtDir + "white_rect.png"));
            AssignPrivateField(parcelManager, "alwaysInCameraBounds",
                Rect.MinMaxRect(HomeMaxX, -7.5f, 37.5f, 7.5f)); // the town plaza
            worldSystems.AddComponent<BiomeScorer>(); // per-base terrain census (muscle 02)

            // --- Competition Board (slice 05; lives in TOWN per owner decision) --
            var boardGo = new GameObject("CompetitionBoard");
            boardGo.transform.position = new Vector3(19.5f, 4.5f, 0f); // just inside the town gate
            boardGo.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            var board = boardGo.AddComponent<CompetitionBoard>();
            AssignPrivateField(board, "boardSprite", Sprite(ArtDir + "notice_board.png"));
            AssignPrivateField(board, "spriteMaterial", litMat);

            // The Loom and Waystone are BUILDABLE now (hammer menu) — no longer
            // pre-placed. HomePickerUI carries their sprites.

            // --- Perimeter fences (visuals; matching collider walls below) ----
            // One fence around the WHOLE home cluster (locked parcels inside
            // are walkable -- TerrainGrid draws them dim), one around the
            // swamp cluster, and rails down both sides of the road corridor.
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
            // Posts every 1.6 along a straight run + one rail over its length.
            void FenceRun(Vector2 a, Vector2 b)
            {
                bool horizontal = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
                float len = horizontal ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.y - a.y);
                int posts = Mathf.Max(2, Mathf.RoundToInt(len / postSpacing) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float t = i / (float)(posts - 1);
                    PlacePost(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t));
                }
                Vector2 mid = (a + b) * 0.5f + new Vector2(0f, 0.1f);
                PlaceRail(mid, horizontal ? new Vector2(len, 0.12f) : new Vector2(0.12f, len));
            }

            // home cluster perimeter: gates east (to town) + west (the road)
            FenceRun(new Vector2(HomeMinX, HomeMaxY), new Vector2(HomeMaxX, HomeMaxY));
            FenceRun(new Vector2(HomeMinX, HomeMinY), new Vector2(HomeMaxX, HomeMinY));
            FenceRun(new Vector2(HomeMinX, HomeMinY), new Vector2(HomeMinX, -RoadGateHalf));
            FenceRun(new Vector2(HomeMinX, RoadGateHalf), new Vector2(HomeMinX, HomeMaxY));
            FenceRun(new Vector2(HomeMaxX, HomeMinY), new Vector2(HomeMaxX, -TownGateHalf));
            FenceRun(new Vector2(HomeMaxX, TownGateHalf), new Vector2(HomeMaxX, HomeMaxY));
            // road corridor sides
            FenceRun(new Vector2(RoadMinX, RoadMaxY), new Vector2(RoadMaxX, RoadMaxY));
            FenceRun(new Vector2(RoadMinX, RoadMinY), new Vector2(RoadMaxX, RoadMinY));
            // swamp cluster perimeter: one gate east, onto the road
            FenceRun(new Vector2(SwampMinX, SwampMaxY), new Vector2(SwampMaxX, SwampMaxY));
            FenceRun(new Vector2(SwampMinX, SwampMinY), new Vector2(SwampMaxX, SwampMinY));
            FenceRun(new Vector2(SwampMinX, SwampMinY), new Vector2(SwampMinX, SwampMaxY));
            FenceRun(new Vector2(SwampMaxX, SwampMinY), new Vector2(SwampMaxX, -RoadGateHalf));
            FenceRun(new Vector2(SwampMaxX, RoadGateHalf), new Vector2(SwampMaxX, SwampMaxY));

            // --- Scatter detail (pebbles only — tufts clash with tile look) --
            var scatterParent = new GameObject("Scatter");
            var rng = new System.Random(42);
            var pebble = Sprite(ArtDir + "pebble.png");
            for (int i = 0; i < 10; i++)
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

            // --- Bounds (collider walls mirroring the fence runs) --------------
            // Straight wall along a fence line, 0.8 thick, centred on the line.
            var bounds = new GameObject("Bounds");
            void WallRun(Vector2 a, Vector2 b)
            {
                bool horizontal = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
                float len = horizontal ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.y - a.y);
                AddWall(bounds, (a + b) * 0.5f,
                    horizontal ? new Vector2(len, 0.8f) : new Vector2(0.8f, len));
            }
            // home cluster (gates east + west; the west gap is sealed by
            // ParcelManager's road blocker until road rights are bought)
            WallRun(new Vector2(HomeMinX, HomeMaxY), new Vector2(HomeMaxX, HomeMaxY));
            WallRun(new Vector2(HomeMinX, HomeMinY), new Vector2(HomeMaxX, HomeMinY));
            WallRun(new Vector2(HomeMinX, HomeMinY), new Vector2(HomeMinX, -RoadGateHalf));
            WallRun(new Vector2(HomeMinX, RoadGateHalf), new Vector2(HomeMinX, HomeMaxY));
            WallRun(new Vector2(HomeMaxX, HomeMinY), new Vector2(HomeMaxX, -TownGateHalf));
            WallRun(new Vector2(HomeMaxX, TownGateHalf), new Vector2(HomeMaxX, HomeMaxY));
            // road corridor
            WallRun(new Vector2(RoadMinX, RoadMaxY), new Vector2(RoadMaxX, RoadMaxY));
            WallRun(new Vector2(RoadMinX, RoadMinY), new Vector2(RoadMaxX, RoadMinY));
            // swamp cluster
            WallRun(new Vector2(SwampMinX, SwampMaxY), new Vector2(SwampMaxX, SwampMaxY));
            WallRun(new Vector2(SwampMinX, SwampMinY), new Vector2(SwampMaxX, SwampMinY));
            WallRun(new Vector2(SwampMinX, SwampMinY), new Vector2(SwampMinX, SwampMaxY));
            WallRun(new Vector2(SwampMaxX, SwampMinY), new Vector2(SwampMaxX, -RoadGateHalf));
            WallRun(new Vector2(SwampMaxX, RoadGateHalf), new Vector2(SwampMaxX, SwampMaxY));

            // --- Town (slice 09: competitions + vendors live in town) ----------
            // Contained plaza east of the home gate: x in [15, 37.5], y in [-7.5, 7.5].
            AddWall(bounds, new Vector2(26.25f, 7.5f), new Vector2(22.5f, 1f));
            AddWall(bounds, new Vector2(26.25f, -7.5f), new Vector2(22.5f, 1f));
            AddWall(bounds, new Vector2(37.5f, 0f), new Vector2(1f, 16f));

            var town = new GameObject("Town");
            var townGround = new GameObject("TownGround");
            townGround.transform.SetParent(town.transform);
            townGround.transform.position = new Vector3(26.25f, 0f, 0f);
            var tgSr = townGround.AddComponent<SpriteRenderer>();
            tgSr.sprite = Sprite(ArtDir + "ground_grass.png");
            tgSr.drawMode = SpriteDrawMode.Tiled;
            tgSr.size = new Vector2(22.5f, 15f);
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
            // Vendor is REAL now (slice 09 economy); Ferryman still a placeholder.
            var vendorGo = new GameObject("VendorStall");
            vendorGo.transform.SetParent(town.transform);
            vendorGo.transform.position = new Vector3(29f, 4.5f, 0f);
            var vendor = vendorGo.AddComponent<VendorStall>();
            AssignPrivateField(vendor, "stallSprite", Sprite(ArtDir + "notice_board.png"));
            AssignPrivateField(vendor, "spriteMaterial", litMat);
            // Ferryman is REAL now: land deeds via the parcel overview.
            var ferryGo = new GameObject("FerrymanStall");
            ferryGo.transform.SetParent(town.transform);
            ferryGo.transform.position = new Vector3(29f, -4.5f, 0f);
            var ferry = ferryGo.AddComponent<FerrymanStall>();
            AssignPrivateField(ferry, "stallSprite", Sprite(ArtDir + "notice_board.png"));
            AssignPrivateField(ferry, "spriteMaterial", litMat);

            // Holding Office (slice 07): where repossessed spirits await fees.
            // INSIDE the town walls (owner rule) -- the old 42,5 sat outside.
            var officeGo = new GameObject("HoldingOffice");
            officeGo.transform.SetParent(town.transform);
            officeGo.transform.position = new Vector3(34f, 4.5f, 0f);
            var office = officeGo.AddComponent<HoldingOffice>();
            AssignPrivateField(office, "officeSprite", Sprite(ArtDir + "holding_office.png"));
            AssignPrivateField(office, "spriteMaterial", litMat);

            // --- Rocks (obstacles) -----------------------------------------
            var rocksParent = new GameObject("Rocks");
            var rockSprite = Sprite(ArtDir + "rock.png");
            for (int i = 0; i < 2; i++)
            {
                var rock = new GameObject("Rock");
                rock.transform.SetParent(rocksParent.transform);
                rock.transform.position = RandomInField(rng, margin: 1.5f); // 10x8 hearth parcel
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

            // Waystone is buildable via the hammer menu now — not pre-placed.

            // --- Camera -----------------------------------------------------
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.5f;
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
            var homePicker = ui.AddComponent<HomePickerUI>();
            AssignPrivateField(homePicker, "loomSprite", Sprite(ArtDir + "loom.png"));
            AssignPrivateField(homePicker, "waystoneSprite", Sprite(ArtDir + "waystone.png"));
            AssignPrivateField(homePicker, "spriteMaterial", litMat);
            ui.AddComponent<VendorUI>();
            ui.AddComponent<LandOfficeUI>();

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

        /// <summary>Random point inside the HEARTH parcel (10x8, centred on origin).</summary>
        private static Vector3 RandomInField(System.Random rng, float margin)
        {
            float x = Mathf.Lerp(-ParcelW / 2 + margin, ParcelW / 2 - margin, (float)rng.NextDouble());
            float y = Mathf.Lerp(-ParcelH / 2 + margin, ParcelH / 2 - margin, (float)rng.NextDouble());
            // keep spawn area near origin clear
            if (Mathf.Abs(x) < 1.5f && Mathf.Abs(y) < 1.5f) x += 2.5f;
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

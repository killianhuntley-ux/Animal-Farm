using System;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Hidden play-milestone tracker driving staggered vendor move-ins
    /// (muscle 01, verdict 3). The town grows as a RESPONSE to how you play:
    /// the game quietly counts tiles tilled, crops harvested and builds
    /// placed, and when a counter crosses a vendor's threshold that vendor
    /// moves in -- stall appears, small announcement beat, flag persisted so
    /// the stall exists from then on.
    ///
    /// First arrival: THE BLACKSMITH (forge-spirit, tool upgrades) at
    /// 40 tiles tilled -- right about when the base hoe starts feeling slow.
    ///
    /// Counters feed from two directions:
    ///  - tilesTilled: subscribed to TerrainGrid.OnSurfaceChanged (only the
    ///    Hoe ever produces Dirt, and Restore never fires the event, so the
    ///    signal is clean -- no ToolController edits needed).
    ///  - cropsHarvested / buildsPlaced: call sites ping the static
    ///    VendorArrivals.Note("cropsHarvested" / "buildsPlaced") convenience
    ///    (Plant.Interact, SelectionController.BeginPlaceHome, HomePickerUI).
    ///
    /// Runtime-spawned via GetOrCreate + AfterSceneLoad bootstrap
    /// (GameCalendar pattern -- no scene setup, no bootstrapper edits), so it
    /// exists before SaveSystem's Start-time scan picks up "vendorarrivals".
    /// </summary>
    public class VendorArrivals : MonoBehaviour, ISaveable
    {
        public static VendorArrivals Instance { get; private set; }

        /// <summary>Tiles tilled before the Blacksmith decides you're serious.</summary>
        public const int BlacksmithTilledThreshold = 40;

        /// <summary>Read-only peeks (debug console / future vendor gates).</summary>
        public int TilesTilled { get; private set; }
        public int CropsHarvested { get; private set; }
        public int BuildsPlaced { get; private set; }
        public bool BlacksmithArrived { get; private set; }

        private static readonly Color AnnounceGold = new Color(1f, 0.84f, 0.25f, 1f);
        private static readonly Color AnnounceGrey = new Color(0.78f, 0.78f, 0.74f, 1f);

        private bool _subscribedGrid;
        private float _nextGridSearch;

        /// <summary>
        /// Returns the live tracker, creating one on the fly -- it needs no
        /// scene setup, so runtime creation is safe (CompetitionManager pattern).
        /// </summary>
        public static VendorArrivals GetOrCreate()
        {
            if (Instance == null)
                new GameObject("VendorArrivals (runtime)").AddComponent<VendorArrivals>();
            return Instance;
        }

        /// <summary>Self-spawn: runs after scene Awakes, before any Start.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        /// <summary>
        /// Static convenience for call sites that should not care whether the
        /// tracker exists yet. Known counters: "tilesTilled" (normally fed by
        /// the TerrainGrid event instead), "cropsHarvested", "buildsPlaced".
        /// Unknown names are a logged no-op, never a throw.
        /// </summary>
        public static void Note(string counter)
        {
            var arrivals = GetOrCreate();
            if (arrivals == null || string.IsNullOrEmpty(counter)) return;

            switch (counter.ToLowerInvariant())
            {
                case "tilestilled": arrivals.TilesTilled++; break;
                case "cropsharvested": arrivals.CropsHarvested++; break;
                case "buildsplaced": arrivals.BuildsPlaced++; break;
                default:
                    Debug.LogWarning("[VendorArrivals] Note: unknown counter '" + counter + "'.");
                    return;
            }

            arrivals.CheckMilestones(announce: true);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_subscribedGrid && TerrainGrid.Instance != null)
                TerrainGrid.Instance.OnSurfaceChanged -= OnSurfaceChanged;

            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // The grid is a scene object; latch on as soon as it exists (and
            // re-latch cheaply if a scene reload ever replaced it).
            if (!_subscribedGrid && Time.unscaledTime >= _nextGridSearch)
            {
                _nextGridSearch = Time.unscaledTime + 2f;
                if (TerrainGrid.Instance != null)
                {
                    TerrainGrid.Instance.OnSurfaceChanged += OnSurfaceChanged;
                    _subscribedGrid = true;
                }
            }

            // A restored "arrived" flag spawns its stall here, silently --
            // Restore order vs other Starts is not guaranteed, and the spawn
            // is idempotent, so the lazy ensure is the safe path.
            if (BlacksmithArrived && BlacksmithStall.Instance == null)
                BlacksmithStall.Spawn();
        }

        /// <summary>Only the Hoe produces Dirt, so Dirt == a till landing.</summary>
        private void OnSurfaceChanged(Vector2Int cell, Surface surface)
        {
            if (surface != Surface.Dirt) return;
            TilesTilled++;
            CheckMilestones(announce: true);
        }

        // ---- milestones -----------------------------------------------------

        private void CheckMilestones(bool announce)
        {
            if (!BlacksmithArrived && TilesTilled >= BlacksmithTilledThreshold)
            {
                // The flag is the arrival; Update's ensure spawns the stall a
                // frame later -- after every scene Start, so the VendorStall's
                // sprite exists to borrow (same path live and on restore).
                BlacksmithArrived = true;
                if (announce) AnnounceBlacksmith();
            }
        }

        /// <summary>Guide-style arrival beat: floating text over the player,
        /// a bleep, and one dark-funny-kind line.</summary>
        private void AnnounceBlacksmith()
        {
            var player = GameObject.FindWithTag("Player");
            Vector3 pos = player != null ? player.transform.position : Vector3.zero;

            AnimalFarm.UI.FloatingText.Show(pos + Vector3.up * 1.5f,
                "The Blacksmith has moved into town!", AnnounceGold);
            AnimalFarm.Onboarding.GuideMoments.Announce(
                "A forge-spirit opens shop in town. It heard you sighing at your hoe.");
            Bleeps.Play(BleepKind.Ascend, 0.6f);

            Debug.Log("[VendorArrivals] The Blacksmith moved in (tilesTilled="
                      + TilesTilled + ").");
        }

        // ---- ISaveable ------------------------------------------------------

        [Serializable]
        private struct ArrivalsState
        {
            public int tilesTilled;
            public int cropsHarvested;
            public int buildsPlaced;
            public bool blacksmithArrived;
        }

        public string SaveKey => "vendorarrivals";

        public string Capture() => JsonUtility.ToJson(new ArrivalsState
        {
            tilesTilled = TilesTilled,
            cropsHarvested = CropsHarvested,
            buildsPlaced = BuildsPlaced,
            blacksmithArrived = BlacksmithArrived
        });

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<ArrivalsState>(json);
            TilesTilled = Mathf.Max(0, state.tilesTilled);
            CropsHarvested = Mathf.Max(0, state.cropsHarvested);
            BuildsPlaced = Mathf.Max(0, state.buildsPlaced);
            BlacksmithArrived = state.blacksmithArrived;

            // A save from mid-milestone (counter crossed, flag not yet set --
            // shouldn't happen, but saves are forever) arrives silently.
            CheckMilestones(announce: false);
        }
    }
}

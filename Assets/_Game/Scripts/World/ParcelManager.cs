using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Land ownership (muscle 02/08 world restructure). The world is PARCELS
    /// grouped into BASES (parcel clusters -- the biome unit for BiomeScorer):
    ///
    ///   - HOME base: a 3x3 cluster of 10x8 fields. The centre field is owned
    ///     from the start; the other eight are bought one at a time at the
    ///     Registrar's Land Office. The whole cluster sits inside ONE perimeter
    ///     fence (built by SceneBootstrapper), so locked fields are walkable
    ///     but refuse tools -- TerrainGrid draws them dimmed until bought.
    ///     The data model is cluster-agnostic (name + rect + baseId), so the
    ///     4x4 expansion later is just more list entries.
    ///   - WEST ROAD RIGHTS: not land but passage -- buying it tears down the
    ///     physical gate bar in the cluster's west fence, opening the dressed
    ///     road corridor to the swamp. Road cells stay usable=false forever.
    ///   - SWAMP base ("Reedmire"): a 2x2 satellite cluster at the road's far
    ///     end, bought parcel by parcel AFTER road rights (prereqIndex).
    ///
    /// Locked parcels show an info-only sign (ParcelGate) that points at the
    /// Land Office. Unlock flips the parcel's TerrainGrid cells usable; no
    /// runtime fences are built any more -- cluster perimeters come from the
    /// bootstrapper, and the old named-north-wall contract is retired.
    /// CameraFollow clamps to <see cref="OwnedBoundsWorld"/>.
    /// </summary>
    public class ParcelManager : MonoBehaviour, ISaveable
    {
        public static ParcelManager Instance { get; private set; }

        /// <summary>Base (parcel-cluster) ids -- the biome unit (BiomeScorer).</summary>
        public const int HomeBaseId = 0;
        public const int SwampBaseId = 1;

        [Header("Art (assigned by the bootstrapper)")]
        [SerializeField] private Material spriteMaterial;
        [SerializeField] private Sprite whiteRect;

        [Header("Camera bounds")]
        [Tooltip("World region always inside the camera clamp (the town plaza).")]
        [SerializeField] private Rect alwaysInCameraBounds = Rect.MinMaxRect(15f, -7.5f, 37.5f, 7.5f);

        private static readonly Color WoodBrown = new Color(0.42f, 0.31f, 0.22f);

        // ---- world geometry. Parcels are authored HERE; the matching fences,
        // walls and TerrainGrid zoning live in SceneBootstrapper -- keep the
        // two in step when the map changes. ---------------------------------
        private const float ParcelW = 10f, ParcelH = 8f;
        private static readonly Vector2 HomeOrigin = new Vector2(-15f, -12f);  // 3x3 cluster -> x[-15,15] y[-12,12]
        private static readonly Vector2 SwampOrigin = new Vector2(-47f, -8f);  // 2x2 cluster -> x[-47,-27] y[-8,8]
        private static readonly Rect RoadRect = Rect.MinMaxRect(-27f, -2f, -15f, 2f);
        // Road rights also hand the camera clamp the whole swamp enclosure --
        // you can WALK the mire before you own an inch of it.
        private static readonly Rect RoadRevealRect = Rect.MinMaxRect(-48f, -9f, -15f, 9f);

        private enum ParcelKind : byte { Field, Road }

        /// <summary>One purchasable thing at the Land Office: a field or a road right.</summary>
        private class Parcel
        {
            public string name;
            public ParcelKind kind;
            public int baseId;           // HomeBaseId / SwampBaseId; -1 for road rights
            public Rect rect;            // world-space area (field ground / road corridor)
            public Rect revealRect;      // area the camera clamp gains when unlocked
            public int coinCost;         // obols
            public string blurb;         // flavor line for the Land Office overview
            public int prereqIndex = -1; // parcel that must be owned first (-1 = none)
            public Vector3 signPos;      // where the gate sign stands while locked
            public bool unlocked;
            public GameObject gate;      // sign + label + trigger while locked
            public GameObject blocker;   // physical gate-bar collider while locked (road only)
        }

        private readonly List<Parcel> _parcels = new List<Parcel>();
        private int _roadIndex = -1;

        /// <summary>Fired after any unlock (purchase, debug, or save restore).</summary>
        public event Action OnOwnershipChanged;

        private Rect _ownedBounds;
        private bool _boundsDirty = true;

        public int ParcelCount => _parcels.Count;

        /// <summary>True once the West Road Rights deed is bought (frontier systems wake up).</summary>
        public bool RoadRightsOwned => _roadIndex >= 0 && IsUnlocked(_roadIndex);

        /// <summary>Owned FIELD parcels (road rights excluded; the free hearth field counts).
        /// The pouty mount's join trigger reads this.</summary>
        public int UnlockedFieldCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _parcels.Count; i++)
                    if (_parcels[i].kind == ParcelKind.Field && _parcels[i].unlocked) n++;
                return n;
            }
        }

        public bool IsUnlocked(int index) =>
            index >= 0 && index < _parcels.Count && _parcels[index].unlocked;

        /// <summary>Read-only snapshot of one parcel for the Land Office UI.</summary>
        public struct ParcelInfo
        {
            public int index;
            public string name;
            public bool unlocked;
            public int coinCost;
            public Vector2 size;
            public string blurb;
        }

        /// <summary>Snapshot for the UI; index -1 if <paramref name="i"/> is out of range.</summary>
        public ParcelInfo GetInfo(int i)
        {
            if (i < 0 || i >= _parcels.Count) return new ParcelInfo { index = -1 };
            var p = _parcels[i];

            // Surface the prerequisite in the blurb -- the Land Office row
            // renders blurbs verbatim, so this is the player's one warning.
            string blurb = p.blurb;
            if (!p.unlocked && p.prereqIndex >= 0 && !_parcels[p.prereqIndex].unlocked)
                blurb += " [needs " + _parcels[p.prereqIndex].name + "]";

            return new ParcelInfo
            {
                index = i,
                name = p.name,
                unlocked = p.unlocked,
                coinCost = p.coinCost,
                size = p.rect.size,
                blurb = blurb
            };
        }

        // ---- lifecycle --------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildParcelList();

            // Build locked-state scaffolding in Awake so it exists before
            // SaveSystem.Start() calls Restore() on load.
            for (int i = 0; i < _parcels.Count; i++)
            {
                if (_parcels[i].unlocked) continue;
                BuildGate(i);
                if (_parcels[i].kind == ParcelKind.Road) BuildRoadBlocker(i);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>All deeds on the Registrar's books. Costs are obols; they
        /// climb with distance from the hearth, and the mire costs mire money.</summary>
        private void BuildParcelList()
        {
            // -- home cluster: 3x3 fields, centre owned from the first breath.
            Field("Hearth Field", 1, 1, 0, HomeBaseId, HomeOrigin,
                "Where you started. The soil already answers to you.", startOwned: true);
            Field("West Field", 0, 1, 120, HomeBaseId, HomeOrigin,
                "The road side. Buy toward the mire and the mire comes closer.");
            Field("North Field", 1, 2, 140, HomeBaseId, HomeOrigin,
                "Soft ground. The grass remembers being walked on.");
            Field("East Field", 2, 1, 150, HomeBaseId, HomeOrigin,
                "Town noise carries over this fence. Spirits pretend not to listen.");
            Field("South Field", 1, 0, 160, HomeBaseId, HomeOrigin,
                "Low and quiet. Things pool here: water, mist, memory.");
            Field("Northwest Field", 0, 2, 220, HomeBaseId, HomeOrigin,
                "A corner the wind argues over.");
            Field("Northeast Field", 2, 2, 240, HomeBaseId, HomeOrigin,
                "Morning light lands here first.");
            Field("Southwest Field", 0, 0, 260, HomeBaseId, HomeOrigin,
                "Stubborn scrub. It will hold a grudge, then hold seed.");
            Field("Southeast Field", 2, 0, 280, HomeBaseId, HomeOrigin,
                "The far corner. Good bones under bad weeds.");

            // -- road rights: opens the west gate and the corridor to the mire.
            _roadIndex = _parcels.Count;
            _parcels.Add(new Parcel
            {
                name = "West Road Rights",
                kind = ParcelKind.Road,
                baseId = -1,
                rect = RoadRect,
                revealRect = RoadRevealRect,
                coinCost = 200,
                blurb = "Passage west to Reedmire. The Registrar keeps a file on every road.",
                signPos = new Vector3(-13.4f, 0f, 0f) // just inside the cluster's west gate
            });

            // -- swamp satellite: 2x2 fields past the road, bought one by one.
            Field("Reedmire Hollow", 0, 1, 240, SwampBaseId, SwampOrigin,
                "Murky ground, already pooling. Reeds would approve.", prereq: _roadIndex);
            Field("Reedmire Bank", 1, 1, 260, SwampBaseId, SwampOrigin,
                "The drier lip of the mire. Relatively speaking.", prereq: _roadIndex);
            Field("Reedmire Shallows", 0, 0, 280, SwampBaseId, SwampOrigin,
                "Standing water with opinions.", prereq: _roadIndex);
            Field("Reedmire Deep", 1, 0, 300, SwampBaseId, SwampOrigin,
                "The mire keeps its secrets here. Buy them.", prereq: _roadIndex);
        }

        private void Field(string name, int col, int row, int cost, int baseId, Vector2 origin,
            string blurb, bool startOwned = false, int prereq = -1)
        {
            var rect = new Rect(origin.x + col * ParcelW, origin.y + row * ParcelH, ParcelW, ParcelH);
            _parcels.Add(new Parcel
            {
                name = name,
                kind = ParcelKind.Field,
                baseId = baseId,
                rect = rect,
                revealRect = rect,
                coinCost = cost,
                blurb = blurb,
                prereqIndex = prereq,
                signPos = new Vector3(rect.center.x, rect.center.y, 0f),
                unlocked = startOwned
            });
        }

        // ---- purchase / unlock ------------------------------------------------

        /// <summary>Charges the parcel's coin cost (obols); on success, opens it.</summary>
        public bool TryPurchase(int index)
        {
            if (index < 0 || index >= _parcels.Count) return false;
            var p = _parcels[index];
            if (p.unlocked) return false;

            Vector3 pos = PopupAnchor(p);

            if (p.prereqIndex >= 0 && !_parcels[p.prereqIndex].unlocked)
            {
                FloatingText.Show(pos, "(needs " + _parcels[p.prereqIndex].name + ")", UIStyle.Grey);
                return false;
            }

            if (Inventory.Instance == null || !Inventory.Instance.Consume("coin", p.coinCost))
            {
                FloatingText.Show(pos, "(needs " + p.coinCost + " obols)", UIStyle.Grey);
                return false;
            }

            Unlock(index);
            FloatingText.Show(pos, p.name + (p.kind == ParcelKind.Road ? " granted!" : " opened!"), UIStyle.Gold);
            return true;
        }

        /// <summary>Purchases happen at the Land Office, so float the text at
        /// the player, not at a sign that may be half a map away.</summary>
        private static Vector3 PopupAnchor(Parcel p)
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) return player.transform.position + Vector3.up * 0.8f;
            return p.signPos + Vector3.up * 0.8f;
        }

        /// <summary>Force-open a parcel with no cost or prereq (debug console: "parcel &lt;i&gt;").</summary>
        public bool Debug_Unlock(int index)
        {
            if (index < 0 || index >= _parcels.Count || _parcels[index].unlocked) return false;
            Unlock(index);
            return true;
        }

        /// <summary>
        /// Opens a parcel WITHOUT charging (callers charge). Fields become real
        /// terrain (usable cells); road rights only tear down the gate bar --
        /// the corridor's cells stay usable=false (dressed, walkable, untillable).
        /// One-way this slice.
        /// </summary>
        private void Unlock(int index)
        {
            if (index < 0 || index >= _parcels.Count) return;
            var p = _parcels[index];
            if (p.unlocked) return;
            p.unlocked = true;

            if (p.blocker != null) { Destroy(p.blocker); p.blocker = null; }
            if (p.gate != null) { Destroy(p.gate); p.gate = null; }

            if (p.kind == ParcelKind.Field && TerrainGrid.Instance != null
                && TerrainGrid.Instance.TryWorldToCell(
                    new Vector3(p.rect.xMin + 0.5f, p.rect.yMin + 0.5f), out var min)
                && TerrainGrid.Instance.TryWorldToCell(
                    new Vector3(p.rect.xMax - 0.5f, p.rect.yMax - 0.5f), out var max))
            {
                TerrainGrid.Instance.SetUsable(
                    new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1), true);
            }

            _boundsDirty = true;
            OnOwnershipChanged?.Invoke();
        }

        // ---- camera bounds ------------------------------------------------------

        /// <summary>
        /// Bounding box of everywhere the camera may roam: owned parcel rects,
        /// the reveal of any opened road (corridor + its satellite enclosure),
        /// and the town plaza (always reachable through the east gate).
        /// CameraFollow clamps to this plus its own margin, and the box grows
        /// the moment a purchase lands (OnOwnershipChanged fires after).
        /// </summary>
        public Rect OwnedBoundsWorld
        {
            get
            {
                if (_boundsDirty) RecomputeOwnedBounds();
                return _ownedBounds;
            }
        }

        private void RecomputeOwnedBounds()
        {
            _boundsDirty = false;
            float xMin = alwaysInCameraBounds.xMin, xMax = alwaysInCameraBounds.xMax;
            float yMin = alwaysInCameraBounds.yMin, yMax = alwaysInCameraBounds.yMax;
            for (int i = 0; i < _parcels.Count; i++)
            {
                if (!_parcels[i].unlocked) continue;
                var r = _parcels[i].revealRect;
                if (r.xMin < xMin) xMin = r.xMin;
                if (r.xMax > xMax) xMax = r.xMax;
                if (r.yMin < yMin) yMin = r.yMin;
                if (r.yMax > yMax) yMax = r.yMax;
            }
            _ownedBounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        // ---- base queries (BiomeScorer) ----------------------------------------

        /// <summary>Number of bases (parcel clusters) in the world.</summary>
        public int BaseCount => 2;

        /// <summary>
        /// World rects of every FIELD parcel in a base, owned or not -- callers
        /// (BiomeScorer) filter by TerrainGrid.IsUsable, so locked land never
        /// skews a census. Clears <paramref name="into"/> first.
        /// </summary>
        public void GetBaseParcelRects(int baseId, List<Rect> into)
        {
            if (into == null) return;
            into.Clear();
            for (int i = 0; i < _parcels.Count; i++)
            {
                if (_parcels[i].kind != ParcelKind.Field) continue;
                if (_parcels[i].baseId == baseId) into.Add(_parcels[i].rect);
            }
        }

        // ---- construction -----------------------------------------------------

        /// <summary>Sign + label + interaction trigger. Field signs stand at the
        /// parcel's centre (locked cluster land is walkable); the road sign
        /// stands just inside the gate it would open.</summary>
        private void BuildGate(int index)
        {
            var p = _parcels[index];

            var gate = new GameObject("ParcelGate_" + index);
            gate.transform.SetParent(transform, false);
            gate.transform.position = p.signPos;

            // Small wooden sign (tinted white rect).
            var sign = new GameObject("Sign");
            sign.transform.SetParent(gate.transform, false);
            sign.transform.localScale = new Vector3(1.2f, 1.5f, 1f);
            var sr = sign.AddComponent<SpriteRenderer>();
            sr.sprite = whiteRect;
            sr.color = WoodBrown;
            sr.sortingOrder = 21; // just above the fence posts (20)
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            WorldLabel.Attach(gate, p.name + "\nSee the Land Office", -1.3f);

            // Trigger for the walk-up prompt and the click raycast.
            var col = gate.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(2.4f, 2.2f);

            var gateComp = gate.AddComponent<ParcelGate>();
            gateComp.Init(this, index, sign.transform);
            gateComp.SetTexts(p.name, "About this land");

            p.gate = gate;
        }

        /// <summary>
        /// Physical bar sealing the home cluster's west gate until road rights
        /// are bought: a collider across the fence gap plus a visible wooden
        /// bar. Fields get no blockers -- locked cluster land is walkable.
        /// </summary>
        private void BuildRoadBlocker(int index)
        {
            var p = _parcels[index];

            var blocker = new GameObject("RoadBlocker_" + index);
            blocker.transform.SetParent(transform, false);
            blocker.transform.position = new Vector3(p.rect.xMax - 0.5f, p.rect.center.y, 0f);
            blocker.AddComponent<BoxCollider2D>().size = new Vector2(1f, p.rect.height);

            var bar = new GameObject("Bar");
            bar.transform.SetParent(blocker.transform, false);
            var sr = bar.AddComponent<SpriteRenderer>();
            sr.sprite = whiteRect;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(0.18f, p.rect.height);
            sr.color = WoodBrown;
            sr.sortingOrder = 20;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            p.blocker = blocker;
        }

        // ---- ISaveable --------------------------------------------------------

        [Serializable]
        private struct ParcelState
        {
            public bool[] unlocked;
        }

        public string SaveKey => "parcels";

        public string Capture()
        {
            var unlocked = new bool[_parcels.Count];
            for (int i = 0; i < _parcels.Count; i++) unlocked[i] = _parcels[i].unlocked;
            return JsonUtility.ToJson(new ParcelState { unlocked = unlocked });
        }

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<ParcelState>(json);
            if (state.unlocked == null) return;

            // Re-apply unlocks WITHOUT charging: Unlock never touches the
            // inventory (TryPurchase does the charging). Unlock is one-way
            // this slice, so a saved "locked" never re-locks an open parcel.
            int n = Mathf.Min(state.unlocked.Length, _parcels.Count);
            for (int i = 0; i < n; i++)
            {
                if (state.unlocked[i] && !_parcels[i].unlocked)
                    Unlock(i);
            }
        }
    }

    /// <summary>
    /// INFO-ONLY gate sign for one locked parcel. Spawned by ParcelManager at
    /// runtime; dies when the parcel opens. Purchasing moved to the Registrar's
    /// Land Office -- the gate just points the player there.
    /// </summary>
    public class ParcelGate : MonoBehaviour, IInteractable, ISelectable
    {
        private ParcelManager _manager;
        private int _index;
        private Transform _sign;
        private Vector3 _signBaseScale = Vector3.one;
        private string _prompt = "About this land";
        private string _title = "Parcel";

        public void Init(ParcelManager manager, int index, Transform sign)
        {
            _manager = manager;
            _index = index;
            _sign = sign;
            if (sign != null) _signBaseScale = sign.localScale;
        }

        /// <summary>Title/prompt strings, e.g. "North Field" / "About this land".</summary>
        public void SetTexts(string title, string prompt)
        {
            if (!string.IsNullOrEmpty(title)) _title = title;
            if (!string.IsNullOrEmpty(prompt)) _prompt = prompt;
        }

        // ---- IInteractable ----

        public string PromptText => _prompt;

        public bool CanInteract(GameObject actor) =>
            _manager != null && !_manager.IsUnlocked(_index);

        public void Interact(GameObject actor)
        {
            LandOfficeUI.Instance?.Open();
        }

        public void SetFocused(bool focused)
        {
            if (_sign != null)
                _sign.localScale = focused ? _signBaseScale * 1.08f : _signBaseScale;
        }

        // ---- ISelectable ----

        public string SelectableTitle => _title;

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("About this land", () =>
            {
                LandOfficeUI.Instance?.Open();
            }));
        }
    }
}

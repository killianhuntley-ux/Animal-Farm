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
    /// Land expansion (slice 08 core): purchasable PARCELS -- fenced annex
    /// fields attached to the farm's north side. Each locked parcel shows a
    /// gate sign on the farm's north fence line; paying its produce cost
    /// opens the wall segment behind it and builds the parcel's ground,
    /// fence, and containment walls at runtime (mirroring the greybox style
    /// of SceneBootstrapper, which we cannot run at play time).
    ///
    /// ==================== TECH DEBT (slice 08b) ====================
    /// slice 08b: extend TerrainGrid to parcels.
    /// TerrainGrid is a FIXED 40x26 array covering the core farm only and is
    /// NOT resized here. A purchased parcel is VISUAL + TRAVERSAL land only
    /// (own tiled ground sprite + fence + wall removal) -- it is NOT tillable
    /// terrain yet. Farming stays inside the core field until 08b.
    /// ===============================================================
    ///
    /// Wall-removal contract: SceneBootstrapper builds the farm's north
    /// bounds wall in two NAMED halves, "NorthWall_P0" (west, x[-20,0]) and
    /// "NorthWall_P1" (east, x[0,20]). Unlock destroys the matching half by
    /// name -- null-safe, because on a load-restore (or a stale scene that
    /// predates the split) the object may already be gone or never existed.
    /// As a belt to that suspender, each LOCKED parcel also spawns its own
    /// runtime blocker collider across its south edge, so containment never
    /// depends on the scene file's wall naming alone.
    /// </summary>
    public class ParcelManager : MonoBehaviour, ISaveable
    {
        public static ParcelManager Instance { get; private set; }

        [Header("Art (assigned by the bootstrapper)")]
        [SerializeField] private Material spriteMaterial;
        [SerializeField] private Sprite groundSprite;
        [SerializeField] private Sprite fencePostSprite;
        [SerializeField] private Sprite whiteRect;

        private const float FencePostSpacing = 1.6f;
        private const float RailThickness = 0.12f;
        private static readonly Color WoodBrown = new Color(0.42f, 0.31f, 0.22f);

        /// <summary>One annex field. Hardcoded this slice; data-driven later.</summary>
        private class Parcel
        {
            public string name;
            public Rect rect;        // world-space area (farm is x[-20,20], y[-13,13])
            public string costItemId;
            public int costCount;
            public Color groundTint;
            public bool unlocked;
            public GameObject gate;     // sign + label + trigger while locked
            public GameObject blocker;  // runtime south-edge collider while locked
        }

        private readonly List<Parcel> _parcels = new List<Parcel>();

        public int ParcelCount => _parcels.Count;

        public bool IsUnlocked(int index) =>
            index >= 0 && index < _parcels.Count && _parcels[index].unlocked;

        // ---- lifecycle --------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Costs are produce this slice: coins arrive with the economy slice;
            // produce is the proto-currency.
            _parcels.Add(new Parcel
            {
                name = "North Meadow",
                rect = Rect.MinMaxRect(-20f, 13f, 0f, 25f),
                costItemId = "wheat",
                costCount = 6,
                groundTint = new Color(0.62f, 0.75f, 0.50f) // meadow greenish
            });
            _parcels.Add(new Parcel
            {
                name = "North Rise",
                rect = Rect.MinMaxRect(0f, 13f, 20f, 25f),
                costItemId = "berry",
                costCount = 10,
                groundTint = new Color(0.80f, 0.71f, 0.53f) // dusty rise
            });

            // Build locked-state scaffolding in Awake so it exists before
            // SaveSystem.Start() calls Restore() on load.
            for (int i = 0; i < _parcels.Count; i++)
            {
                BuildGate(i);
                BuildBlocker(i);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- purchase / unlock ------------------------------------------------

        private static string CostText(Parcel p) => p.costCount + "x " + p.costItemId;

        /// <summary>Charges the parcel's produce cost; on success, opens it.</summary>
        public bool TryPurchase(int index)
        {
            if (index < 0 || index >= _parcels.Count) return false;
            var p = _parcels[index];
            if (p.unlocked) return false;

            // Capture the popup anchor before Unlock destroys the gate.
            Vector3 pos = p.gate != null
                ? p.gate.transform.position + Vector3.up * 0.8f
                : new Vector3(p.rect.center.x, p.rect.yMin + 0.8f, 0f);

            if (Inventory.Instance == null || !Inventory.Instance.Consume(p.costItemId, p.costCount))
            {
                FloatingText.Show(pos, "(needs " + CostText(p) + ")", UIStyle.Grey);
                return false;
            }

            Unlock(index);
            FloatingText.Show(pos, p.name + " opened!", UIStyle.Gold);
            return true;
        }

        /// <summary>Force-open a parcel with no cost (debug console: "parcel &lt;i&gt;").</summary>
        public bool Debug_Unlock(int index)
        {
            if (index < 0 || index >= _parcels.Count || _parcels[index].unlocked) return false;
            Unlock(index);
            return true;
        }

        /// <summary>
        /// Opens a parcel WITHOUT charging (callers charge): removes the farm's
        /// north-wall segment + our blocker, builds the annex visuals and its
        /// containment walls, and retires the gate sign. One-way this slice.
        /// </summary>
        private void Unlock(int index)
        {
            if (index < 0 || index >= _parcels.Count) return;
            var p = _parcels[index];
            if (p.unlocked) return;
            p.unlocked = true;

            // (1) Open the south edge: destroy our runtime blocker and the
            // bootstrapper's named wall half. Both null-safe -- the named wall
            // is already gone on a save-load re-apply, and may not exist at
            // all in a scene built before the north wall was split.
            if (p.blocker != null) { Destroy(p.blocker); p.blocker = null; }
            var bootstrapWall = GameObject.Find("NorthWall_P" + index);
            if (bootstrapWall != null) Destroy(bootstrapWall);

            // (2) Ground, fence, and containment for the annex.
            BuildParcelField(index, p);

            // (3) The gate has done its job.
            if (p.gate != null) { Destroy(p.gate); p.gate = null; }
        }

        // ---- construction -----------------------------------------------------

        /// <summary>Sign + label + interaction trigger on the north fence line.</summary>
        private void BuildGate(int index)
        {
            var p = _parcels[index];

            var gate = new GameObject("ParcelGate_" + index);
            gate.transform.SetParent(transform, false);
            gate.transform.position = new Vector3(p.rect.center.x, p.rect.yMin, 0f);

            // Small wooden sign (tinted white rect).
            var sign = new GameObject("Sign");
            sign.transform.SetParent(gate.transform, false);
            sign.transform.localScale = new Vector3(1.2f, 1.5f, 1f);
            var sr = sign.AddComponent<SpriteRenderer>();
            sr.sprite = whiteRect;
            sr.color = WoodBrown;
            sr.sortingOrder = 21; // just above the fence posts (20)
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            WorldLabel.Attach(gate, p.name + "\n" + CostText(p) + " to open", -1.3f);

            // Trigger for the walk-up prompt and the click raycast. Reaches a
            // little into the farm so the player can use it from inside.
            var col = gate.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(2.4f, 2.2f);
            col.offset = new Vector2(0f, -0.4f);

            var gateComp = gate.AddComponent<ParcelGate>();
            gateComp.Init(this, index, sign.transform);
            gateComp.SetTexts(p.name, "Open " + p.name + " (" + CostText(p) + ")");

            p.gate = gate;
        }

        /// <summary>
        /// Runtime collider sealing a LOCKED parcel's south edge. Redundant
        /// with the bootstrapper's NorthWall_P halves on purpose -- see the
        /// wall-removal contract in the class comment.
        /// </summary>
        private void BuildBlocker(int index)
        {
            var p = _parcels[index];

            var blocker = new GameObject("ParcelBlocker_" + index);
            blocker.transform.SetParent(transform, false);
            blocker.transform.position = new Vector3(p.rect.center.x, p.rect.yMin + 0.5f, 0f);
            blocker.AddComponent<BoxCollider2D>().size = new Vector2(p.rect.width, 1f);

            p.blocker = blocker;
        }

        /// <summary>
        /// Ground sprite + perimeter fence + containment walls on the parcel's
        /// three OUTER edges (north/east/west; the south edge opens onto the
        /// farm). Mirrors the bootstrapper's fence style: posts every 1.6
        /// units at sorting order 20, thin tiled rail strips at 19.
        /// </summary>
        private void BuildParcelField(int index, Parcel p)
        {
            var root = new GameObject("Parcel_" + index);
            root.transform.SetParent(transform, false);

            // -- ground (tiled, tinted per parcel; above the farm tilemap at -1000)
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = new Vector3(p.rect.center.x, p.rect.center.y, 0f);
            var gsr = ground.AddComponent<SpriteRenderer>();
            gsr.sprite = groundSprite;
            gsr.drawMode = SpriteDrawMode.Tiled;
            gsr.size = new Vector2(p.rect.width, p.rect.height);
            gsr.color = p.groundTint;
            gsr.sortingOrder = -950;
            if (spriteMaterial != null) gsr.sharedMaterial = spriteMaterial;

            // -- fence posts
            void PlacePost(float x, float y)
            {
                var post = new GameObject("Post");
                post.transform.SetParent(root.transform);
                post.transform.position = new Vector3(x, y, 0f);
                var psr = post.AddComponent<SpriteRenderer>();
                psr.sprite = fencePostSprite;
                psr.sortingOrder = 20;
                if (spriteMaterial != null) psr.sharedMaterial = spriteMaterial;
            }
            // North edge (corner to corner).
            for (float x = p.rect.xMin; x <= p.rect.xMax + 0.01f; x += FencePostSpacing)
                PlacePost(x, p.rect.yMax);
            // East + west edges. Start one spacing up: the farm's own north
            // fence already has posts on the y = rect.yMin line.
            for (float y = p.rect.yMin + FencePostSpacing; y < p.rect.yMax; y += FencePostSpacing)
            {
                PlacePost(p.rect.xMin, y);
                PlacePost(p.rect.xMax, y);
            }

            // -- rail strips (thin tiled white_rect, wood tinted)
            void PlaceRail(Vector2 pos, Vector2 size)
            {
                var rail = new GameObject("Rail");
                rail.transform.SetParent(root.transform);
                rail.transform.position = pos;
                var rsr = rail.AddComponent<SpriteRenderer>();
                rsr.sprite = whiteRect;
                rsr.drawMode = SpriteDrawMode.Tiled;
                rsr.size = size;
                rsr.color = WoodBrown;
                rsr.sortingOrder = 19;
                if (spriteMaterial != null) rsr.sharedMaterial = spriteMaterial;
            }
            PlaceRail(new Vector2(p.rect.center.x, p.rect.yMax + 0.1f),
                new Vector2(p.rect.width, RailThickness));
            PlaceRail(new Vector2(p.rect.xMin, p.rect.center.y + 0.1f),
                new Vector2(RailThickness, p.rect.height));
            PlaceRail(new Vector2(p.rect.xMax, p.rect.center.y + 0.1f),
                new Vector2(RailThickness, p.rect.height));

            // -- containment walls (player must stay inside the opened land)
            void PlaceWall(Vector2 pos, Vector2 size)
            {
                var wall = new GameObject("Wall");
                wall.transform.SetParent(root.transform);
                wall.transform.position = pos;
                wall.AddComponent<BoxCollider2D>().size = size;
            }
            PlaceWall(new Vector2(p.rect.center.x, p.rect.yMax + 0.5f), new Vector2(p.rect.width, 1f));
            PlaceWall(new Vector2(p.rect.xMin - 0.5f, p.rect.center.y), new Vector2(1f, p.rect.height));
            PlaceWall(new Vector2(p.rect.xMax + 0.5f, p.rect.center.y), new Vector2(1f, p.rect.height));
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
    /// The interactable/selectable gate sign for one locked parcel. Spawned by
    /// ParcelManager at runtime; dies when the parcel opens.
    /// </summary>
    public class ParcelGate : MonoBehaviour, IInteractable, ISelectable
    {
        private ParcelManager _manager;
        private int _index;
        private Transform _sign;
        private Vector3 _signBaseScale = Vector3.one;
        private string _prompt = "Open parcel";
        private string _title = "Parcel";

        public void Init(ParcelManager manager, int index, Transform sign)
        {
            _manager = manager;
            _index = index;
            _sign = sign;
            if (sign != null) _signBaseScale = sign.localScale;
        }

        /// <summary>Prompt/title strings, e.g. "Open North Meadow (6x wheat)".</summary>
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
            if (_manager != null) _manager.TryPurchase(_index);
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
            into.Add(new SelectAction("Purchase", () =>
            {
                if (_manager != null) _manager.TryPurchase(_index);
            }));
        }
    }
}

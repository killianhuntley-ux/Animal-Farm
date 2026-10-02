using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// The Mire Peddler: the swamp base's BIOME-EXCLUSIVE vendor (muscle 08/09
    /// verdict 4). He only ever sets up in Reedmire, and his stock solves
    /// OTHER biomes' problems -- the cross-biome shopping hook:
    ///   Peat Compost x3 - mire peat that lifts crop quality back on the prairie
    ///                     farm (applied to tilled soil like any compost);
    ///   Mud-Stone       - turns away one Digger visit at the farm (the prairie's
    ///                     burrowing problem); sold nowhere else.
    /// (ASSUMPTION: which problems he solves -- the doc only fixes the shape.)
    /// Self-spawns at the swamp gate (AfterSceneLoad), no scene setup; always
    /// present, never saved. Stall art is runtime-painted (FrontierArt.Stall).
    /// </summary>
    public class SwampVendor : MonoBehaviour, IInteractable, ISelectable
    {
        private const float FocusScale = 1.06f;
        private const string Title = "The Mire Peddler";
        private const string Subtitle = "Mud has uses. Come back from the dry end with questions.";

        public static SwampVendor Instance { get; private set; }

        /// <summary>Inventory id of one rich-mud load (spread via the seed picker like a sand load).</summary>
        public const string MudLoadId = "mud_load";

        /// <summary>The peddler's stock (data-driven; append to extend).</summary>
        public static readonly RoadGoods.Good[] Stock =
        {
            new RoadGoods.Good(AnimalFarm.World.CompostManager.CompostId, "Peat Compost (3)",
                "dark mire peat; spread it on dry-land crops for finer harvests", 12, 3),
            RoadGoods.MudStone,
            // ASSUMPTION (owner: ~10 obols, 4 loads): swamp soil by the load.
            new RoadGoods.Good(MudLoadId, "Rich Mud (4 loads)",
                "one load turns one cell to swamp soil: always moist, crops grow fine but never gleam (Interact on open ground)", 10, 4)
        };

        private Vector3 _baseScale = Vector3.one;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject("SwampVendor").AddComponent<SwampVendor>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            transform.position = FrontierGeometry.SwampVendorPos;
            transform.localScale = Vector3.one * 1.8f;
            _baseScale = transform.localScale;

            var sr = FrontierArt.AddSprite(gameObject, FrontierArt.Stall, 0);
            sr.color = new Color(0.9f, 0.95f, 0.9f);

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 1.2f);

            WorldLabel.Attach(gameObject, "Mire Peddler", -0.9f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static void OpenShop()
        {
            GoodsShopUI.GetOrCreate()?.Open(Title, Subtitle, Stock);
        }

        // ------------------------------------------------------- IInteractable

        public string PromptText => "Trade";

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor) => OpenShop();

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "Mire Peddler";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Trade", OpenShop));
        }
    }
}

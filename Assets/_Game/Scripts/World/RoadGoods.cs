using AnimalFarm.Core;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Road gear catalog (muscle 08 items 2 + 6): the lantern and the
    /// per-villain ward charms, plus the shared buy / spend logic. Stock rows
    /// are rendered by whichever vendor sells them (VendorUI for the town
    /// vendor, SwampVendor for the mire peddler), both calling
    /// <see cref="Buy"/>. All gear is plain Inventory ids, so saving is free.
    ///
    /// WARD DESIGN (ASSUMPTION -- the doc says "consumable or durable, decide in
    /// build"): wards are CONSUMABLE one-shot charms carried in the pouch. A
    /// charm is auto-spent the first time its villain type would strike you:
    /// as a road ambush (RoadTravel) or as a scheduled farm visit
    /// (VillainManager), and the strike is turned away with no damage. They sit
    /// beside the Watchlight, which stays the DURABLE, placed, all-kinds farm
    /// defense: charms are the portable, specific, you-gear-up-for-the-trip
    /// tool. The LANTERN is DURABLE (a road tool, never consumed; carrying one
    /// is enough -- no equip key, ASSUMPTION).
    ///   Bell Charm   -> Scarer   (sold by the town vendor)
    ///   Bitter Bait  -> Devourer (sold by the town vendor)
    ///   Mud-Stone    -> Digger   (sold ONLY at the swamp: dug from ground
    ///                  nothing can dig -- the biome-exclusive vendor hook)
    /// </summary>
    public static class RoadGoods
    {
        public const string LanternId = "lantern";
        public const string BellCharmId = "bell_charm";
        public const string BitterBaitId = "bitter_bait";
        public const string MudStoneId = "mud_stone";

        private static readonly Color CoinGold = new Color(1f, 0.9f, 0.5f);

        /// <summary>One purchasable road good.</summary>
        public struct Good
        {
            public string id;
            public string label;
            public string sub;
            public int price;
            public int quantity;
            public bool durable; // true = you only ever need one

            public Good(string id, string label, string sub, int price, int quantity = 1, bool durable = false)
            {
                this.id = id;
                this.label = label;
                this.sub = sub;
                this.price = price;
                this.quantity = quantity;
                this.durable = durable;
            }
        }

        public static readonly Good Lantern = new Good(LanternId, "Road Lantern",
            "keeps escorts brisk and cheerful in the dark stretch; never used up", 25, 1, durable: true);

        public static readonly Good BellCharm = new Good(BellCharmId, "Bell Charm",
            "one ambush turned away: Scarers loathe a little bell", 8);

        public static readonly Good BitterBait = new Good(BitterBaitId, "Bitter Bait",
            "one ambush turned away: Devourers spit it out and sulk", 8);

        public static readonly Good MudStone = new Good(MudStoneId, "Mud-Stone",
            "one Digger turned away: it will not dig where this was dug", 10);

        /// <summary>Goods the TOWN vendor stocks (lantern + the road-ambusher wards).</summary>
        public static readonly Good[] TownStock = { Lantern, BellCharm, BitterBait };

        public static bool HasLantern =>
            Inventory.Instance != null && Inventory.Instance.Count(LanternId) > 0;

        public static string WardIdFor(VillainKind kind)
        {
            switch (kind)
            {
                case VillainKind.Scarer: return BellCharmId;
                case VillainKind.Devourer: return BitterBaitId;
                default: return MudStoneId;
            }
        }

        public static string WardNameFor(VillainKind kind)
        {
            switch (kind)
            {
                case VillainKind.Scarer: return "Bell Charm";
                case VillainKind.Devourer: return "Bitter Bait";
                default: return "Mud-Stone";
            }
        }

        public static bool HasWardFor(VillainKind kind) =>
            Inventory.Instance != null && Inventory.Instance.Count(WardIdFor(kind)) > 0;

        /// <summary>
        /// Spends one ward charm of the matching kind, if carried. True = the
        /// strike is turned away (caller skips all damage). Shows the toast.
        /// </summary>
        public static bool TryConsumeWard(VillainKind kind, Vector3 at)
        {
            var inv = Inventory.Instance;
            if (inv == null || !inv.Consume(WardIdFor(kind), 1)) return false;

            FloatingText.Show(at + Vector3.up * 1.0f, WardNameFor(kind) + " spent", UIStyle.Gold);
            Bleeps.Play(BleepKind.Soothe, 0.5f);
            return true;
        }

        /// <summary>
        /// Pays for and grants a good. Refuses (no charge) when the player is
        /// short, or already carries the durable lantern. Returns true on a sale.
        /// </summary>
        public static bool Buy(Good good)
        {
            var inv = Inventory.Instance;
            if (inv == null) return false;

            Vector3 at = PlayerPos() + Vector3.up * 0.8f;

            if (good.durable && inv.Count(good.id) > 0)
            {
                FloatingText.Show(at, "(you already carry one)", UIStyle.Grey);
                Bleeps.Play(BleepKind.Denied, 0.6f);
                return false;
            }

            if (!inv.Consume("coin", good.price))
            {
                FloatingText.Show(at, "(needs " + good.price + " obols)", UIStyle.Danger);
                Bleeps.Play(BleepKind.Denied, 0.8f);
                return false;
            }

            inv.Add(good.id, good.quantity);
            FloatingText.Show(at, "+" + good.quantity + " " + good.label.ToLowerInvariant(), CoinGold);
            Bleeps.Play(BleepKind.Coin, 0.6f);
            return true;
        }

        private static Vector3 PlayerPos()
        {
            var player = GameObject.FindWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }
    }
}

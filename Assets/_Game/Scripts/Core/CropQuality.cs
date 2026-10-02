using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>Crop quality tier (muscle 02): Normal / Fine / Gleaming.</summary>
    public enum CropTier { Normal = 0, Fine = 1, Gleaming = 2 }

    /// <summary>
    /// Quality tiers ride on plain inventory ids: Normal is the bare produce id
    /// ("wheat"), higher tiers are suffixed ("wheat_fine", "wheat_gleaming"), so
    /// the id->count Inventory, saves and every Normal-only consumer keep
    /// working untouched. This static helper owns the id scheme, the score ->
    /// tier thresholds, sell/feed multipliers and best-first consumption.
    /// </summary>
    public static class CropQuality
    {
        /// <summary>Quality score (watered share of grow time + compost nudge) needed for Fine.</summary>
        public const float FineThreshold = 0.55f;

        /// <summary>Quality score needed for Gleaming.</summary>
        public const float GleamingThreshold = 0.90f;

        /// <summary>Score ceiling for crops grown on rich Mud: always Fine, never Gleaming.</summary>
        public const float MudScoreCap = GleamingThreshold - 0.01f;

        /// <summary>Compost nudges the score up by this much.</summary>
        public const float CompostBonus = 0.15f;

        private const string FineSuffix = "_fine";
        private const string GleamingSuffix = "_gleaming";

        public static readonly Color FineColor = new Color(0.65f, 0.85f, 1f, 1f);
        public static readonly Color GleamingColor = new Color(1f, 0.88f, 0.4f, 1f);

        public static CropTier FromScore(float score) =>
            score >= GleamingThreshold ? CropTier.Gleaming
            : score >= FineThreshold ? CropTier.Fine
            : CropTier.Normal;

        /// <summary>Inventory id for a produce at a tier.</summary>
        public static string ItemId(string baseId, CropTier tier)
        {
            if (string.IsNullOrEmpty(baseId)) return baseId;
            switch (tier)
            {
                case CropTier.Fine: return baseId + FineSuffix;
                case CropTier.Gleaming: return baseId + GleamingSuffix;
                default: return baseId;
            }
        }

        /// <summary>Strips a tier suffix ("wheat_fine" -> "wheat").</summary>
        public static string BaseId(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (id.EndsWith(GleamingSuffix)) return id.Substring(0, id.Length - GleamingSuffix.Length);
            if (id.EndsWith(FineSuffix)) return id.Substring(0, id.Length - FineSuffix.Length);
            return id;
        }

        public static CropTier TierOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return CropTier.Normal;
            if (id.EndsWith(GleamingSuffix)) return CropTier.Gleaming;
            if (id.EndsWith(FineSuffix)) return CropTier.Fine;
            return CropTier.Normal;
        }

        /// <summary>Star badge text (ASCII): none / "*" / "**".</summary>
        public static string Stars(CropTier tier) =>
            tier == CropTier.Gleaming ? "**" : tier == CropTier.Fine ? "*" : "";

        public static Color TierColor(CropTier tier) =>
            tier == CropTier.Gleaming ? GleamingColor : tier == CropTier.Fine ? FineColor : Color.white;

        /// <summary>Sell-price multiplier: Normal x1, Fine x1.5, Gleaming x2.5.</summary>
        public static float SellMultiplier(CropTier tier) =>
            tier == CropTier.Gleaming ? 2.5f : tier == CropTier.Fine ? 1.5f : 1f;

        /// <summary>Per-unit sell price for a tier, rounded to whole obols.</summary>
        public static int SellPrice(int basePrice, CropTier tier) =>
            Mathf.Max(1, Mathf.RoundToInt(basePrice * SellMultiplier(tier)));

        /// <summary>Spirit-contentment multiplier when fed this tier (they prefer the good stuff).</summary>
        public static float FeedMultiplier(CropTier tier) =>
            tier == CropTier.Gleaming ? 2f : tier == CropTier.Fine ? 1.5f : 1f;

        /// <summary>Readable pouch/shop name: "wheat *" / "wheat **".</summary>
        public static string DisplayName(string id)
        {
            // Muscle 06: tapestry banners ride the pouch as "tapestry_<n>" items.
            if (AnimalFarm.Spirits.WeaveArchive.IsBannerItem(id))
                return AnimalFarm.Spirits.WeaveArchive.BannerLabel(id);

            var tier = TierOf(id);
            return tier == CropTier.Normal ? id : BaseId(id) + " " + Stars(tier);
        }

        /// <summary>Owned count of a produce across all three tiers.</summary>
        public static int CountAny(Inventory inv, string baseId)
        {
            if (inv == null || string.IsNullOrEmpty(baseId)) return 0;
            return inv.Count(baseId)
                 + inv.Count(ItemId(baseId, CropTier.Fine))
                 + inv.Count(ItemId(baseId, CropTier.Gleaming));
        }

        /// <summary>Consumes one unit, best tier first. False (no change) if none owned.</summary>
        public static bool ConsumeBest(Inventory inv, string baseId, out CropTier tier)
        {
            tier = CropTier.Normal;
            if (inv == null || string.IsNullOrEmpty(baseId)) return false;
            for (int t = (int)CropTier.Gleaming; t >= 0; t--)
            {
                if (inv.Consume(ItemId(baseId, (CropTier)t), 1)) { tier = (CropTier)t; return true; }
            }
            return false;
        }

        /// <summary>Consumes one unit, lowest tier first (keeps the good stock for feeding/selling).</summary>
        public static bool ConsumeWorst(Inventory inv, string baseId)
        {
            if (inv == null || string.IsNullOrEmpty(baseId)) return false;
            for (int t = 0; t <= (int)CropTier.Gleaming; t++)
                if (inv.Consume(ItemId(baseId, (CropTier)t), 1)) return true;
            return false;
        }

        /// <summary>True when at least n units are owned across all tiers.</summary>
        public static bool HasAny(Inventory inv, string baseId, int n) => CountAny(inv, baseId) >= n;

        /// <summary>
        /// Consumes n units across tiers, lowest tier first (plain, fine, gleaming).
        /// All-or-nothing: false (no change) if fewer than n are owned in total.
        /// </summary>
        public static bool ConsumeWorst(Inventory inv, string baseId, int n)
        {
            if (inv == null || string.IsNullOrEmpty(baseId)) return false;
            if (n <= 0) return true;
            if (CountAny(inv, baseId) < n) return false;
            int left = n;
            for (int t = 0; t <= (int)CropTier.Gleaming && left > 0; t++)
            {
                string id = ItemId(baseId, (CropTier)t);
                int take = Mathf.Min(left, inv.Count(id));
                if (take > 0 && inv.Consume(id, take)) left -= take;
            }
            return left <= 0;
        }
    }
}

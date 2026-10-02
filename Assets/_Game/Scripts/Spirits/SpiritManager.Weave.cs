using System.Collections.Generic;
using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Weaving half of the SpiritManager (muscle 06): validation, the single
    /// commit block the Loom rite calls (essence toll, inheritance, the
    /// cryptid, the archive record + banner), and recipe/discovery queries the
    /// journal and rumor system share.
    /// </summary>
    public partial class SpiritManager
    {
        // ---- queries ----------------------------------------------------------

        /// <summary>Why a pair can or cannot be woven right now (the rite and the debug console share this).</summary>
        public bool CanWeave(SpiritAgent a, SpiritAgent b, out WeaveRecipe recipe, out string reason)
        {
            recipe = null;
            reason = null;
            if (a == null || b == null || a == b) { reason = "(choose two spirits)"; return false; }
            if (a.State != SpiritState.Resident || b.State != SpiritState.Resident)
            { reason = "Both must be residents."; return false; }
            // "a sad spirit won't ascend to a higher creature plane" - GDD
            if (a.Spirit < 100f || b.Spirit < 100f)
            { reason = "Both must be at full spirit."; return false; }

            recipe = FindRecipe(a.Species, b.Species);
            if (recipe == null || recipe.result == null)
            { recipe = null; reason = "These two will not entwine."; return false; }

            // Owner decision: essence is the weave currency (deeper weaves cost more).
            int have = Inventory.Instance != null ? Inventory.Instance.Count("essence") : 0;
            if (have < recipe.essenceCost)
            { reason = "The loom demands more essence."; return false; }
            return true;
        }

        /// <summary>True for any species that some recipe produces (a cryptid).</summary>
        public bool IsWovenSpecies(string speciesId)
        {
            if (recipes == null || string.IsNullOrEmpty(speciesId)) return false;
            for (int i = 0; i < recipes.Length; i++)
                if (recipes[i] != null && recipes[i].result != null && recipes[i].result.id == speciesId)
                    return true;
            return false;
        }

        /// <summary>Given name, falling back to the species name for the unnamed.</summary>
        public static string WeaveParentName(SpiritAgent a)
        {
            if (a == null) return "Spirit";
            if (!string.IsNullOrEmpty(a.GivenName)) return a.GivenName;
            if (a.Species != null && !string.IsNullOrEmpty(a.Species.displayName))
                return a.Species.displayName;
            return "Spirit";
        }

        /// <summary>A species' thread color for the rite and its banner: its own palette tint, opaque.</summary>
        public static Color ThreadColor(SpiritSpeciesDefinition species)
        {
            Color c = species != null ? species.tint : Color.white;
            c.a = 1f;
            return c;
        }

        // ---- the commit -----------------------------------------------------------

        public SpiritAgent Weave(SpiritAgent a, SpiritAgent b, Vector3 spawnPos) =>
            Weave(a, b, spawnPos, out _);

        /// <summary>
        /// THE weave commit (muscle 06; SAVE POLICY "not started": the rite never
        /// saves, every permanent change lands here in one block). Takes the
        /// essence toll, spawns the cryptid as a fresh Resident whose stat rolls
        /// are biased by the parents and whose traits are one from each parent
        /// (species-filtered), logs the weave + banner in the WeaveArchive, then
        /// consumes both parents. The cryptid is left UNNAMED (species name) -
        /// the rite hands it to the naming ceremony with a blended suggestion.
        /// Returns null (nothing changed) if the pair is invalid.
        /// </summary>
        public SpiritAgent Weave(SpiritAgent a, SpiritAgent b, Vector3 spawnPos,
            out WeaveArchive.WeaveRecord record)
        {
            record = null;
            if (!CanWeave(a, b, out var recipe, out _)) return null;

            if (recipe.essenceCost > 0)
            {
                if (Inventory.Instance == null
                    || !Inventory.Instance.Consume("essence", recipe.essenceCost))
                    return null; // UI validates first; this is the hard gate
            }

            var result = recipe.result;
            string nameA = WeaveParentName(a);
            string nameB = WeaveParentName(b);
            Color colorA = ThreadColor(a.Species);
            Color colorB = ThreadColor(b.Species);
            var speciesA = a.Species;
            var speciesB = b.Species;

            // Inheritance: strong parents, strong thread; one trait from each.
            // The toll is already taken: a throw before the cryptid exists must
            // refund it (nothing woven, nothing lost).
            SpiritStatBias bias;
            List<string> traitIds;
            SpiritAgent woven;
            try
            {
                bias = WeaveInheritance.StatBias(a, b);
                traitIds = WeaveInheritance.PickTraits(a, b, result);
                woven = SpawnResidentWithIdentity(result, null, traitIds, bias, spawnPos, 80f);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (Inventory.Instance != null)
                    Inventory.Instance.Add("essence", recipe.essenceCost); // toll refunded
                return null;
            }
            if (woven == null)
            {
                if (Inventory.Instance != null)
                    Inventory.Instance.Add("essence", recipe.essenceCost); // nothing woven: toll refunded
                return null;
            }
            // Explicit, so the species filter also holds for the rolled filler.
            if (traitIds.Count > 0) woven.SetTraits(traitIds);

            WeaveArchive.Ensure();
            if (WeaveArchive.Instance != null)
                record = WeaveArchive.Instance.AddWeave(nameA, speciesA, nameB, speciesB,
                    woven.GivenName, result, colorA, colorB);

            // The parents are consumed: free their homes, then remove them.
            ReleaseHomeOf(a);
            ReleaseHomeOf(b);
            Despawn(a);
            Despawn(b);

            BumpDiscovery(result.id, DiscoveryLevel.Resident);

            string resultName = !string.IsNullOrEmpty(result.displayName) ? result.displayName : result.id;
            Debug.Log($"[Weave] {nameA} and {nameB} were woven into a {resultName}. "
                + $"Traits: {SpiritTraits.Describe(woven.Traits)}; "
                + $"Vigor {woven.Vigor} Grace {woven.Grace} Gleam {woven.Gleam}.");
            return woven;
        }

        // ---- debug helpers --------------------------------------------------------

        /// <summary>Console: set a species' discovery level directly (may LOWER it - monotonic rule bypassed).</summary>
        public void Debug_SetDiscovery(string speciesId, DiscoveryLevel level)
        {
            if (string.IsNullOrEmpty(speciesId)) return;
            if (level == DiscoveryLevel.Unseen) _discovery.Remove(speciesId);
            else _discovery[speciesId] = level;
        }
    }
}

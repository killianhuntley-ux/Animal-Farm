using System;
using System.Collections.Generic;
using System.Globalization;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;

namespace AnimalFarm.Debugging
{
    /// <summary>
    /// Console commands for weaving (muscle 06): force the rite, weave without
    /// the Loom, recipe discovery, rumors, banners, and previews of the name
    /// blend and inheritance. Lives in its own file so DebugConsole only
    /// forwards unknown commands here (TryRun returns false when the command
    /// is not ours).
    /// </summary>
    public static class WeaveDebugCommands
    {
        public static readonly string[] HelpLines =
        {
            "weaveforce <a> <b> - run the loom rite on two residents by name/species (sets spirit 100, grants missing essence)",
            "weavenow <a> <b>   - weave instantly with no Loom or rite (still names the result + logs the banner)",
            "recipes            - list weave recipes: discovered?, essence cost, resident counts",
            "discover <id|all>  - mark a cryptid species discovered;  forget <id|all> - back to ??? (journal silhouette)",
            "rumor [id|reset|clear] [guide] - fire a weave rumor now (vendor bubble at the player, or guide-light); reset = refund today's budget",
            "banner / banners   - add a test tapestry banner to the pouch / list every weave + banner state",
            "blend <a> <b>      - preview the name-blend suggestions for two names",
            "inherit <a> <b>    - preview stat bias + inherited traits for two residents (no weave)"
        };

        public static bool TryRun(string cmd, string[] args, Action<string> print)
        {
            switch (cmd)
            {
                case "weaveforce": CmdWeaveForce(args, print); return true;
                case "weavenow": CmdWeaveNow(args, print); return true;
                case "recipes": CmdRecipes(print); return true;
                case "discover": CmdDiscover(args, true, print); return true;
                case "forget": CmdDiscover(args, false, print); return true;
                case "rumor": CmdRumor(args, print); return true;
                case "banner": CmdBanner(print); return true;
                case "banners": CmdBanners(print); return true;
                case "blend": CmdBlend(args, print); return true;
                case "inherit": CmdInherit(args, print); return true;
            }
            return false;
        }

        // ---- lookup ----------------------------------------------------------------

        private static string NameOf(SpiritAgent a) =>
            !string.IsNullOrEmpty(a.GivenName) ? a.GivenName
            : a.Species != null ? a.Species.displayName : "?";

        /// <summary>First resident matching a given name / species name / species id, other than <paramref name="except"/>.</summary>
        private static SpiritAgent FindResident(string key, SpiritAgent except)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null || string.IsNullOrEmpty(key)) return null;
            var spirits = mgr.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null || a == except || a.Species == null || a.State != SpiritState.Resident) continue;
                if (string.Equals(a.GivenName, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.Species.displayName, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.Species.id, key, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }

        private static bool PickPair(string[] args, Action<string> print, out SpiritAgent a, out SpiritAgent b)
        {
            a = null;
            b = null;
            if (SpiritManager.Instance == null) { print("SpiritManager not available."); return false; }
            if (args.Length < 3) { print("Usage: " + args[0] + " <a> <b>  (given name, species name or id)"); return false; }
            a = FindResident(args[1], null);
            b = FindResident(args[2], a);
            if (a == null) { print("No resident matches '" + args[1] + "'."); return false; }
            if (b == null) { print("No (other) resident matches '" + args[2] + "'."); return false; }
            return true;
        }

        private static WeaveRecipe FindRecipeByKey(string key)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null || string.IsNullOrEmpty(key)) return null;
            var recipes = mgr.Recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r == null || r.result == null) continue;
                if (string.Equals(r.result.id, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.result.displayName, key, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return null;
        }

        private static Vector3 PlayerPos()
        {
            var player = GameObject.FindWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }

        // ---- weaving ----------------------------------------------------------------------

        private static void CmdWeaveForce(string[] args, Action<string> print)
        {
            if (!PickPair(args, print, out var a, out var b)) return;

            var loom = UnityEngine.Object.FindFirstObjectByType<TheLoom>();
            if (loom == null) { print("No Loom in the scene (build one, or use 'weavenow')."); return; }

            var recipe = SpiritManager.Instance.FindRecipe(a.Species, b.Species);
            if (recipe == null || recipe.result == null)
            {
                print(NameOf(a) + " + " + NameOf(b) + ": these two will not entwine.");
                return;
            }

            a.Debug_SetSpirit(100f);
            b.Debug_SetSpirit(100f);
            GrantEssence(recipe, print);

            loom.RunWeave(a, b);
            print("Rite started: " + NameOf(a) + " + " + NameOf(b) + " -> " + recipe.result.displayName + ".");
        }

        private static void CmdWeaveNow(string[] args, Action<string> print)
        {
            if (!PickPair(args, print, out var a, out var b)) return;

            var recipe = SpiritManager.Instance.FindRecipe(a.Species, b.Species);
            if (recipe == null || recipe.result == null)
            {
                print(NameOf(a) + " + " + NameOf(b) + ": these two will not entwine.");
                return;
            }
            if (NamingCeremony.Running || WeaveRiteCeremony.Running)
            {
                print("A ceremony is already playing.");
                return;
            }

            a.Debug_SetSpirit(100f);
            b.Debug_SetSpirit(100f);
            GrantEssence(recipe, print);

            string givenA = a.GivenName, givenB = b.GivenName;
            var child = SpiritManager.Instance.Weave(a, b, PlayerPos() + new Vector3(0f, -1.5f, 0f),
                out var record);
            if (child == null) { print("The weave failed (nothing changed)."); return; }

            NamingCeremony.Begin(child, WeaveNames.Blend(givenA, givenB), "A legend is woven!");
            if (record != null && WeaveArchive.Instance != null)
                WeaveArchive.Instance.WatchNaming(record, child);

            print("Wove " + record?.parentAName + " + " + record?.parentBName + " into a "
                + recipe.result.displayName + ". Banner id " + (record != null ? record.id : 0) + " is in the pouch.");
        }

        private static void GrantEssence(WeaveRecipe recipe, Action<string> print)
        {
            var inv = Inventory.Instance;
            if (inv == null) return;
            int have = inv.Count("essence");
            if (have >= recipe.essenceCost) return;
            inv.Add("essence", recipe.essenceCost - have);
            print("Granted " + (recipe.essenceCost - have) + " essence (toll " + recipe.essenceCost + ").");
        }

        // ---- recipes + discovery -----------------------------------------------------------

        private static void CmdRecipes(Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }
            var recipes = mgr.Recipes;
            if (recipes.Count == 0) { print("No weave recipes defined."); return; }

            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r == null || r.result == null || r.parentA == null || r.parentB == null) continue;
                bool found = WeaveArchive.IsRecipeDiscovered(r);
                print(r.result.id + ": " + r.parentA.displayName + " + " + r.parentB.displayName
                    + " -> " + r.result.displayName
                    + " | " + (found ? "discovered" : "UNDISCOVERED")
                    + " | toll " + r.essenceCost
                    + " | residents " + mgr.CountResidents(r.parentA.id) + "/" + mgr.CountResidents(r.parentB.id)
                    + " | rumors heard " + (WeaveArchive.Instance != null ? WeaveArchive.Instance.HeardCount(r.result.id) : 0));
            }
        }

        private static void CmdDiscover(string[] args, bool discover, Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }
            if (args.Length < 2) { print("Usage: " + args[0] + " <cryptid id|all>"); return; }

            var level = discover ? SpiritManager.DiscoveryLevel.Resident : SpiritManager.DiscoveryLevel.Unseen;
            int n = 0;
            if (args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var recipes = mgr.Recipes;
                for (int i = 0; i < recipes.Count; i++)
                {
                    if (recipes[i] == null || recipes[i].result == null) continue;
                    mgr.Debug_SetDiscovery(recipes[i].result.id, level);
                    n++;
                }
            }
            else
            {
                var r = FindRecipeByKey(args[1]);
                if (r == null) { print("No recipe produces '" + args[1] + "' (try 'recipes')."); return; }
                mgr.Debug_SetDiscovery(r.result.id, level);
                n = 1;
            }
            print((discover ? "Discovered " : "Forgot ") + n + " cryptid(s). Open the journal (J) to see the page.");
        }

        // ---- rumors ----------------------------------------------------------------------------

        private static void CmdRumor(string[] args, Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }
            WeaveArchive.Ensure();
            var archive = WeaveArchive.Instance;
            if (archive == null) { print("WeaveArchive not available."); return; }

            if (args.Length >= 2 && args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                archive.Debug_ResetRumorBudget();
                print("Today's rumor budget refunded.");
                return;
            }
            if (args.Length >= 2 && args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                archive.Debug_ClearRumors();
                print("All heard rumors forgotten.");
                return;
            }

            bool guide = false;
            WeaveRecipe recipe = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("guide", StringComparison.OrdinalIgnoreCase)) guide = true;
                else if (args[i].Equals("vendor", StringComparison.OrdinalIgnoreCase)) guide = false;
                else recipe = FindRecipeByKey(args[i]);
            }

            if (recipe == null)
            {
                // Prefer a genuinely hint-worthy recipe, else any undiscovered one, else the first.
                var ready = WeaveArchive.RumorCandidates();
                if (ready.Count > 0) recipe = ready[0];
                var recipes = mgr.Recipes;
                for (int i = 0; recipe == null && i < recipes.Count; i++)
                    if (recipes[i] != null && recipes[i].result != null && !WeaveArchive.IsRecipeDiscovered(recipes[i]))
                        recipe = recipes[i];
                for (int i = 0; recipe == null && i < recipes.Count; i++)
                    if (recipes[i] != null && recipes[i].result != null) recipe = recipes[i];
            }
            if (recipe == null) { print("No weave recipes defined."); return; }

            string said = archive.FireRumor(recipe, guide, PlayerPos(), guide ? "The light" : "The vendor");
            print((guide ? "[guide] " : "[vendor] ") + said);
        }

        // ---- banners ------------------------------------------------------------------------------

        private static void CmdBanner(Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            WeaveArchive.Ensure();
            var archive = WeaveArchive.Instance;
            if (mgr == null || archive == null) { print("Not available."); return; }

            var recipes = mgr.Recipes;
            WeaveRecipe recipe = null;
            for (int i = 0; recipe == null && i < recipes.Count; i++)
                if (recipes[i] != null && recipes[i].result != null
                    && recipes[i].parentA != null && recipes[i].parentB != null) recipe = recipes[i];
            if (recipe == null) { print("No weave recipes defined."); return; }

            var r = archive.AddWeave("Pip", recipe.parentA, "Wisp", recipe.parentB, "Pipwisp", recipe.result,
                SpiritManager.ThreadColor(recipe.parentA), SpiritManager.ThreadColor(recipe.parentB));
            print("Added test banner #" + r.id + " (" + WeaveArchive.ItemId(r.id)
                + ") to the pouch. Build menu (B) -> Hang.");
        }

        private static void CmdBanners(Action<string> print)
        {
            var archive = WeaveArchive.Instance;
            if (archive == null || archive.Weaves.Count == 0) { print("(no weaves recorded)"); return; }
            for (int i = 0; i < archive.Weaves.Count; i++)
            {
                var w = archive.Weaves[i];
                print("#" + w.id + " Day " + w.day + ": " + w.parentAName + " the " + w.parentASpeciesName
                    + " + " + w.parentBName + " the " + w.parentBSpeciesName + " -> " + w.childName
                    + " the " + w.childSpeciesName + " | banner " + (w.hung
                        ? "HUNG at " + w.x.ToString("0.#", CultureInfo.InvariantCulture) + "," + w.y.ToString("0.#", CultureInfo.InvariantCulture)
                        : "carried"));
            }
        }

        // ---- previews -------------------------------------------------------------------------------

        private static void CmdBlend(string[] args, Action<string> print)
        {
            if (args.Length < 3) { print("Usage: blend <nameA> <nameB>  (use '-' for an unnamed parent)"); return; }
            string a = args[1] == "-" ? "" : args[1];
            string b = args[2] == "-" ? "" : args[2];
            var samples = new List<string>();
            for (int i = 0; i < 6; i++) samples.Add(WeaveNames.Blend(a, b) ?? "(null: ceremony picks a random name)");
            print("Blend suggestions: " + string.Join(", ", samples));
        }

        private static void CmdInherit(string[] args, Action<string> print)
        {
            if (!PickPair(args, print, out var a, out var b)) return;

            var recipe = SpiritManager.Instance.FindRecipe(a.Species, b.Species);
            if (recipe == null || recipe.result == null)
            {
                print(NameOf(a) + " + " + NameOf(b) + ": these two will not entwine.");
                return;
            }

            var bias = WeaveInheritance.StatBias(a, b);
            print(NameOf(a) + " [" + SpiritTraits.Describe(a.Traits) + "] + " + NameOf(b) + " ["
                + SpiritTraits.Describe(b.Traits) + "] -> " + recipe.result.displayName);
            print("Stat bias (-1..1): vigor " + bias.vigor.ToString("0.00", CultureInfo.InvariantCulture)
                + ", grace " + bias.grace.ToString("0.00", CultureInfo.InvariantCulture)
                + ", gleam " + bias.gleam.ToString("0.00", CultureInfo.InvariantCulture));
            for (int i = 0; i < 5; i++)
            {
                var ids = WeaveInheritance.PickTraits(a, b, recipe.result);
                print("  trait roll " + (i + 1) + ": " + string.Join(", ", ids));
            }
        }
    }
}

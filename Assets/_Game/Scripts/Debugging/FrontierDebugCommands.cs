using System;
using System.Globalization;
using AnimalFarm.Core;
using AnimalFarm.Player;
using AnimalFarm.Spirits;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Debugging
{
    /// <summary>
    /// Console commands for the frontier layer (muscle 08/09): road travel,
    /// toll imps, ambushes, dark stretches, ward gear, the pouty mount and
    /// biome affinity. Own file so DebugConsole only forwards unknown commands
    /// here (TryRun returns false when the command is not ours).
    /// </summary>
    public static class FrontierDebugCommands
    {
        public static readonly string[] HelpLines =
        {
            "road            - road status: segment, escorts, dark, lantern, toll, ambush",
            "dark [on|off]   - force/toggle a dark stretch under the shepherd (escorts slow without a lantern)",
            "toll [on|off|reset] - force the toll imp to appear (on/off) or forget today's toll (reset)",
            "ambush [scarer|devourer] - force a road ambush ahead of the shepherd (escorts may bolt; herd them back)",
            "gear [lantern|bell|bait|mud|all|clear] [n] - grant road gear (bell=Scarer ward, bait=Devourer, mud=Digger)",
            "mount [grant|join|revoke|status|content <0-100>|crossings [n]|skipwait] - pouty mount: instant join / play join scene / reset; crossings shows or sets the road-crossing count (joins at 6 with road rights + 1 game-day)",
            "affinity [name|id] - show a spirit species' 5-step biome affinities (and the road territories)",
            "spawntable [base] - affinity-weighted silhouette table: per species, its weight and share of spawns at each owned base",
            "biome [base] [grassland|swamp|desert|barren|clear] - list bases' biomes; pin a base's biome for testing ('biome clear' frees all)",
            "ground - every living spirit's ground base/biome/affinity, mood multiplier and rain behaviour"
        };

        public static bool TryRun(string cmd, string[] args, Action<string> print)
        {
            switch (cmd)
            {
                case "road": CmdRoad(print); return true;
                case "dark": CmdDark(args, print); return true;
                case "toll": CmdToll(args, print); return true;
                case "ambush": CmdAmbush(args, print); return true;
                case "gear": CmdGear(args, print); return true;
                case "mount": CmdMount(args, print); return true;
                case "affinity": CmdAffinity(args, print); return true;
                case "spawntable": CmdSpawnTable(args, print); return true;
                case "biome": CmdBiome(args, print); return true;
                case "ground": CmdGround(print); return true;
            }
            return false;
        }

        private static void CmdRoad(Action<string> print)
        {
            var road = RoadTravel.Instance;
            if (road == null) { print("RoadTravel not available."); return; }
            print(road.Debug_Status());
            var parcels = ParcelManager.Instance;
            if (parcels != null && !parcels.RoadRightsOwned)
                print("(road rights not owned yet - toll imps wait for them; 'parcel 9' opens them)");
        }

        private static void CmdDark(string[] args, Action<string> print)
        {
            var road = RoadTravel.Instance;
            if (road == null) { print("RoadTravel not available."); return; }
            bool? set = ParseOnOff(args);
            bool on = road.Debug_ToggleDark(set);
            print("Forced darkness " + (on ? "ON" : "OFF")
                + (on && !RoadGoods.HasLantern ? " (no lantern: escorts will slow and sulk)" : "") + ".");
        }

        private static void CmdToll(string[] args, Action<string> print)
        {
            var road = RoadTravel.Instance;
            if (road == null) { print("RoadTravel not available."); return; }
            if (args.Length >= 2 && args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                road.Debug_ResetToll();
                print("Today's toll forgotten; the imp will ask again.");
                return;
            }
            bool on = road.Debug_ToggleTollDay(ParseOnOff(args));
            print("Toll imp forced " + (on ? "ON (it appears within a second at the road's head)" : "OFF (normal calendar rules)") + ".");
        }

        private static void CmdAmbush(string[] args, Action<string> print)
        {
            var road = RoadTravel.Instance;
            var player = GameObject.FindWithTag("Player");
            if (road == null || player == null) { print("RoadTravel / player not available."); return; }

            var kind = VillainKind.Scarer;
            if (args.Length >= 2)
            {
                string k = args[1].ToLowerInvariant();
                if (k == "devourer") kind = VillainKind.Devourer;
                else if (k != "scarer") { print("Usage: ambush [scarer|devourer]"); return; }
            }

            if (road.AmbushActive) { print("An ambush is already live."); return; }
            if (road.EscortCount == 0)
                print("(no escorts right now - 'Come along' a resident first to see the bolt)");
            print(road.StartAmbush(kind, player.transform.position)
                ? kind + " ambush started (a matching ward charm turns it away)."
                : "Could not start an ambush.");
        }

        private static void CmdGear(string[] args, Action<string> print)
        {
            var inv = Inventory.Instance;
            if (inv == null) { print("Inventory not available."); return; }

            string what = args.Length >= 2 ? args[1].ToLowerInvariant() : "all";
            int n = 1;
            if (args.Length >= 3) int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            n = Mathf.Clamp(n, 1, 99);

            switch (what)
            {
                case "lantern": inv.Add(RoadGoods.LanternId, 1); print("Lantern granted (durable; one is plenty)."); return;
                case "bell": inv.Add(RoadGoods.BellCharmId, n); print("+" + n + " Bell Charm (Scarer ward)."); return;
                case "bait": inv.Add(RoadGoods.BitterBaitId, n); print("+" + n + " Bitter Bait (Devourer ward)."); return;
                case "mud": inv.Add(RoadGoods.MudStoneId, n); print("+" + n + " Mud-Stone (Digger ward)."); return;
                case "all":
                    inv.Add(RoadGoods.LanternId, inv.Count(RoadGoods.LanternId) > 0 ? 0 : 1);
                    inv.Add(RoadGoods.BellCharmId, n);
                    inv.Add(RoadGoods.BitterBaitId, n);
                    inv.Add(RoadGoods.MudStoneId, n);
                    print("Granted lantern + " + n + " of each ward charm.");
                    return;
                case "clear":
                    inv.Consume(RoadGoods.LanternId, inv.Count(RoadGoods.LanternId));
                    inv.Consume(RoadGoods.BellCharmId, inv.Count(RoadGoods.BellCharmId));
                    inv.Consume(RoadGoods.BitterBaitId, inv.Count(RoadGoods.BitterBaitId));
                    inv.Consume(RoadGoods.MudStoneId, inv.Count(RoadGoods.MudStoneId));
                    print("Road gear cleared.");
                    return;
            }
            print("Usage: gear [lantern|bell|bait|mud|all|clear] [n]");
        }

        private static void CmdMount(string[] args, Action<string> print)
        {
            var mount = PoutyMount.Instance;
            if (mount == null) { print("PoutyMount not available."); return; }

            string sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "status";
            switch (sub)
            {
                case "grant":
                    mount.Debug_Grant();
                    print("Mount granted (no join scene). " + mount.Debug_Status());
                    return;
                case "join":
                    if (mount.Joined) { print("Already joined (use 'mount revoke' first)."); return; }
                    mount.Debug_PlayJoin();
                    print("Join scene started.");
                    return;
                case "revoke":
                    mount.Debug_Revoke();
                    print("Mount revoked; it will turn up again on the road once road rights, crossings and the wait are met ('mount' shows progress, 'mount join' plays the scene now).");
                    return;
                case "crossings":
                {
                    var road = RoadTravel.Instance;
                    if (road == null) { print("RoadTravel not available."); return; }
                    if (args.Length >= 3)
                    {
                        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                        { print("Usage: mount crossings [n]"); return; }
                        road.Debug_SetCrossings(n);
                    }
                    print("Road crossings: " + road.Crossings + "/" + mount.Debug_JoinCrossings
                        + " (counted with road rights owned; a crossing is reaching the far end of the road). " + mount.Debug_Status());
                    return;
                }
                case "skipwait":
                    mount.Debug_SkipRightsWait();
                    print("The 1-game-day wait after road rights is skipped. " + mount.Debug_Status());
                    return;
                case "content":
                    if (args.Length < 3
                        || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    { print("Usage: mount content <0-100>  (under 30 it pouts and refuses rides)"); return; }
                    mount.Debug_SetContentment(v);
                    print(mount.Debug_Status());
                    return;
                default:
                    print(mount.Debug_Status());
                    return;
            }
        }

        private static void CmdAffinity(string[] args, Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }

            string key = args.Length >= 2 ? args[1] : null;
            var species = mgr.KnownSpecies;
            int shown = 0;
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                if (s == null) continue;
                if (!string.IsNullOrEmpty(key)
                    && !string.Equals(s.id, key, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(s.displayName, key, StringComparison.OrdinalIgnoreCase)) continue;

                string line = s.id + ": ";
                foreach (BiomeType b in new[] { BiomeType.Grassland, BiomeType.Swamp, BiomeType.Desert, BiomeType.Barren })
                    line += b + "=" + BiomeAffinity.Label(BiomeAffinity.For(s, b)) + " ";
                var native = BiomeAffinity.NativeBiome(s);
                if (native != BiomeType.Barren) line += "| native " + native;
                print(line);
                shown++;
            }
            if (shown == 0) print("(no matching species)");

            var segs = FrontierGeometry.Segments;
            string road = "Road territories (home -> swamp): ";
            for (int i = 0; i < segs.Length; i++) road += segs[i].name + "=" + segs[i].territory + (i < segs.Length - 1 ? ", " : "");
            print(road);
        }

        private static void CmdSpawnTable(string[] args, Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            var parcels = ParcelManager.Instance;
            if (mgr == null || parcels == null) { print("SpiritManager / ParcelManager not available."); return; }

            int only = -1;
            if (args.Length >= 2
                && (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out only)
                    || only < 0 || only >= parcels.BaseCount))
            { print("Usage: spawntable [base 0.." + (parcels.BaseCount - 1) + "]"); return; }

            var evaluator = AnimalFarm.Requirements.RequirementEvaluator.Instance;
            var species = mgr.KnownSpecies;
            for (int b = 0; b < parcels.BaseCount; b++)
            {
                if (only >= 0 && b != only) continue;

                var biome = BiomeGround.BiomeOfBase(b);
                bool land = BiomeGround.BaseHasLand(b);
                var scorer = BiomeScorer.Instance;
                print("Base " + b + ": " + biome + (scorer != null && scorer.IsForced(b) ? " (pinned)" : "")
                    + (land ? "" : " - no owned land, nothing spawns here"));
                if (!land) continue;

                for (int i = 0; i < species.Count; i++)
                {
                    var s = species[i];
                    if (s == null || string.IsNullOrEmpty(s.id)) continue;

                    float w = BiomeGround.SpawnWeight(s, b);
                    float total = 0f;
                    for (int o = 0; o < parcels.BaseCount; o++)
                        if (BiomeGround.BaseHasLand(o)) total += BiomeGround.SpawnWeight(s, o);

                    string line = "  " + s.id + ": " + BiomeAffinity.Label(BiomeAffinity.For(s, biome))
                        + " w" + w.ToString("0.0", CultureInfo.InvariantCulture);
                    if (w <= 0f) line += " -> never appears here";
                    else if (total > 0f) line += " -> " + Mathf.RoundToInt(w / total * 100f) + "% of its silhouettes";

                    bool gateOpen = evaluator != null && s.gateChain != null
                        && evaluator.IsGateOpen(s.gateChain.chainId, AnimalFarm.Requirements.Gate.Appear);
                    if (!gateOpen) line += " [Appear gate closed]";
                    print(line);
                }
            }
        }

        private static void CmdBiome(string[] args, Action<string> print)
        {
            var scorer = BiomeScorer.Instance;
            if (scorer == null) { print("BiomeScorer not available."); return; }

            // "biome clear" frees every base; "biome <base> clear" frees one.
            if (args.Length == 2 && args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                scorer.Debug_ClearForcedBiome(-1);
                print("All biome pins cleared; the census is back in charge.");
                return;
            }

            if (args.Length >= 3)
            {
                if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int baseId)
                    || baseId < 0 || baseId >= scorer.BaseCount)
                { print("Usage: biome <base 0.." + (scorer.BaseCount - 1) + "> <grassland|swamp|desert|barren|clear>"); return; }

                string what = args[2].ToLowerInvariant();
                if (what == "clear")
                {
                    scorer.Debug_ClearForcedBiome(baseId);
                    print("Base " + baseId + " unpinned (now " + scorer.GetBiome(baseId) + ").");
                    return;
                }

                BiomeType type;
                switch (what)
                {
                    case "grassland": case "grass": type = BiomeType.Grassland; break;
                    case "swamp": type = BiomeType.Swamp; break;
                    case "desert": type = BiomeType.Desert; break;
                    case "barren": type = BiomeType.Barren; break;
                    default:
                        print("Usage: biome <base> <grassland|swamp|desert|barren|clear>");
                        return;
                }
                scorer.Debug_ForceBiome(baseId, type);
                print("Base " + baseId + " pinned to " + type + " (gates and spirit affinity follow it; 'biome " + baseId + " clear' frees it).");
                return;
            }

            for (int b = 0; b < scorer.BaseCount; b++)
                print("Base " + b + ": " + scorer.GetBiome(b) + " score " + scorer.GetBiomeScore(b).ToString("0")
                    + (scorer.IsForced(b) ? " (pinned)" : ""));
        }

        private static void CmdGround(Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }

            var weather = WeatherManager.Instance;
            print("Rain: " + (weather != null && weather.IsRaining ? "falling" : "dry"));

            int shown = 0;
            var all = mgr.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a.Species == null) continue;
                if (a.State != SpiritState.Resident && a.State != SpiritState.Visitor) continue;
                print("  " + a.State + " " + a.Debug_BiomeStatus());
                shown++;
            }
            if (shown == 0) print("(no visitors or residents)");
        }

        private static bool? ParseOnOff(string[] args)
        {
            if (args.Length < 2) return null;
            string a = args[1].ToLowerInvariant();
            if (a == "on" || a == "1" || a == "true") return true;
            if (a == "off" || a == "0" || a == "false") return false;
            return null;
        }
    }
}

using System;
using System.Globalization;
using AnimalFarm.Spirits;
using UnityEngine;

namespace AnimalFarm.Debugging
{
    /// <summary>
    /// Console commands for the memorial garden (muscle 04): spawn clustered
    /// placed headstones, drop an unplaced one, age the garden, place the
    /// waiting ones. Own file so DebugConsole only forwards unknown commands
    /// here (TryRun returns false when the command is not ours).
    /// </summary>
    public static class GardenDebugCommands
    {
        public static readonly string[] HelpLines =
        {
            "garden [status]  - memorial garden: stones placed/waiting, bloom %, neighbours, blooms/wisps, night factor",
            "garden stones [n] - lay n PLACED headstones (default 4, max 30) in a tight cluster near the shepherd",
            "garden drop      - lay one UNPLACED headstone beside the shepherd (then select it > Move stone)",
            "garden age <days> - age every placed stone by <days> game-days (flowers creep in; full bloom = 8)",
            "garden place     - mark every waiting stone as placed where it stands (starts its garden clock)"
        };

        private static readonly string[] Names =
        {
            "Pip", "Moss", "Tansy", "Bram", "Nettle", "Wick", "Sorrel", "Fenn", "Quill", "Marrow"
        };

        public static bool TryRun(string cmd, string[] args, Action<string> print)
        {
            if (cmd != "garden") return false;
            CmdGarden(args, print);
            return true;
        }

        private static void CmdGarden(string[] args, Action<string> print)
        {
            var reg = HeadstoneRegistry.Instance;
            var garden = MemorialGarden.Instance;
            if (reg == null || garden == null)
            {
                print("No memorial garden in this scene (HeadstoneRegistry missing).");
                return;
            }

            string sub = args != null && args.Length >= 2 ? args[1].ToLowerInvariant() : "status";
            switch (sub)
            {
                case "status":
                    print(garden.Debug_Status());
                    break;

                case "stones":
                {
                    int n = 4;
                    if (args.Length > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        n = Mathf.Clamp(parsed, 1, 30);

                    Vector3 origin = PlayerPos() + new Vector3(2f, 1f, 0f);
                    int made = 0;
                    for (int i = 0; i < n; i++)
                    {
                        // 4 per row, 0.9 apart: every stone has several neighbours inside 2.4 units.
                        Vector3 pos = origin + new Vector3((i % 4) * 0.9f, (i / 4) * 0.9f, 0f);
                        string name = Names[(reg.All.Count + i) % Names.Length];
                        var stone = reg.Debug_CreateStone(name, SpeciesIdFor(i), pos, true);
                        if (stone != null) made++;
                    }
                    print("Laid " + made + " placed stone(s) near you. Try 'garden age 4' and 'time 22'.");
                    break;
                }

                case "drop":
                {
                    string name = Names[reg.All.Count % Names.Length];
                    var stone = reg.Debug_CreateStone(name, SpeciesIdFor(reg.All.Count),
                        PlayerPos() + new Vector3(1.5f, 0f, 0f), false);
                    print(stone != null
                        ? "Dropped an unplaced stone for " + name + ". Select it > Move stone to place it."
                        : "Could not lay a stone.");
                    break;
                }

                case "age":
                {
                    if (args.Length < 3
                        || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float days))
                    {
                        print("Usage: garden age <days>");
                        break;
                    }
                    int n = garden.Debug_AgeAll(days);
                    print("Aged " + n + " placed stone(s) by " + days.ToString("0.#", CultureInfo.InvariantCulture)
                        + " day(s).");
                    break;
                }

                case "place":
                    print("Placed " + garden.Debug_PlaceAll() + " waiting stone(s) where they stand.");
                    break;

                default:
                    print("Unknown garden command (try 'help').");
                    break;
            }
        }

        private static Vector3 PlayerPos()
        {
            var player = GameObject.FindWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }

        /// <summary>Cycles the known species so test stones show varied epitaphs/wishes.</summary>
        private static string SpeciesIdFor(int i)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null || mgr.KnownSpecies.Count == 0) return "";
            var s = mgr.KnownSpecies[i % mgr.KnownSpecies.Count];
            return s != null ? s.id : "";
        }
    }
}

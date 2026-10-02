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
    /// Console commands for the living-land polish: shallow wading, plant sway
    /// and rich mud. Own file so DebugConsole only forwards unknown commands
    /// here (TryRun returns false when the command is not ours).
    /// </summary>
    public static class LandDebugCommands
    {
        public static readonly string[] HelpLines =
        {
            "mudload [n]     - add n rich-mud loads (default 4); spread them via the seed picker on open ground",
            "mudpatch [r]    - turn the open ground within r cells of the shepherd to rich mud (default 1, max 4)",
            "wade            - shepherd wading status: feet cell, surface, rim/deep, speed factor",
            "gust [0-1|auto] - pin the global wind gust for plant sway (auto = natural wind); no argument prints it"
        };

        public static bool TryRun(string cmd, string[] args, Action<string> print)
        {
            switch (cmd)
            {
                case "mudload": CmdMudLoad(args, print); return true;
                case "mudpatch": CmdMudPatch(args, print); return true;
                case "wade": CmdWade(print); return true;
                case "gust": CmdGust(args, print); return true;
                default: return false;
            }
        }

        private static void CmdMudLoad(string[] args, Action<string> print)
        {
            var inv = Inventory.Instance;
            if (inv == null) { print("No inventory."); return; }

            int n = 4;
            if (args != null && args.Length > 1
                && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                n = Mathf.Clamp(parsed, 1, 99);

            inv.Add(SwampVendor.MudLoadId, n);
            print("+" + n + " rich-mud load(s). Total: " + inv.Count(SwampVendor.MudLoadId) + ".");
        }

        private static void CmdMudPatch(string[] args, Action<string> print)
        {
            var grid = TerrainGrid.Instance;
            var player = GameObject.FindWithTag("Player");
            if (grid == null || player == null) { print("No terrain or player."); return; }

            int r = 1;
            if (args != null && args.Length > 1
                && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                r = Mathf.Clamp(parsed, 0, 4);

            if (!grid.TryWorldToCell(player.transform.position, out var center)) { print("Off the map."); return; }

            int painted = 0;
            var plants = PlantManager.Instance;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var cell = new Vector2Int(center.x + dx, center.y + dy);
                var surface = grid.GetSurface(cell);
                if (surface == Surface.Water) continue;
                if (plants != null && plants.HasPlantAt(cell)) continue;
                if (Home.AnyAtCell(cell)) continue;
                if (grid.SetSurface(cell, Surface.Mud)) painted++;
            }
            print("Painted " + painted + " cell(s) as rich mud.");
        }

        private static void CmdWade(Action<string> print)
        {
            var grid = TerrainGrid.Instance;
            var player = GameObject.FindWithTag("Player");
            if (grid == null || player == null) { print("No terrain or player."); return; }

            Vector3 feet = player.transform.position + Vector3.down * 0.3f;
            if (!grid.TryWorldToCell(feet, out var cell)) { print("Off the map."); return; }

            var ctrl = player.GetComponent<ShepherdController>();
            string where = grid.IsDeepWater(cell) ? "DEEP water" : grid.IsShallowRim(cell) ? "shallow rim" : grid.GetSurface(cell).ToString();
            print("Feet cell " + cell.x + "," + cell.y + ": " + where
                  + (ctrl != null ? "; wading=" + ctrl.IsWading + " speed=" + ctrl.Velocity.magnitude.ToString("0.0", CultureInfo.InvariantCulture) : "")
                  + ". Wade factor 0.6 on foot, 0.8 mounted.");
        }

        private static void CmdGust(string[] args, Action<string> print)
        {
            if (args != null && args.Length > 1)
            {
                string a = args[1].ToLowerInvariant();
                if (a == "auto" || a == "off") Plant.DebugGust = -1f;
                else if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    Plant.DebugGust = Mathf.Clamp01(v);
                else { print("Usage: gust [0-1|auto]"); return; }
            }
            print("Gust " + Plant.CurrentGust.ToString("0.00", CultureInfo.InvariantCulture)
                  + (Plant.DebugGust >= 0f ? " (pinned)" : " (natural)") + ".");
        }
    }
}

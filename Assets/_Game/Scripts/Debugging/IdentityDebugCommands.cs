using System;
using System.Collections.Generic;
using System.Globalization;
using AnimalFarm.Spirits;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Debugging
{
    /// <summary>
    /// Console commands for the identity layer (muscle 05): stats / traits /
    /// training. Lives in its own file so DebugConsole only forwards unknown
    /// commands here (TryRun returns false when the command is not ours).
    /// </summary>
    public static class IdentityDebugCommands
    {
        public static readonly string[] HelpLines =
        {
            "stats [name]    - show Nature stats (with species band), traits, training progress",
            "traits [name id [id2]] - list the trait pool, or set a spirit's traits (id 'roll' rerolls)",
            "train <name|all> <vigor|grace|gleam> [ticks] - grant training ticks (default 6 = one point)",
            "trainnow [name] - clear training cooldown so spirits head for a gym on their next idle",
            "gym <stones|hurdles|mirror> - place a free training building near the player",
            "bait <food|none> - fill/clear the bait slot of the nearest training building",
            "reroll <name>   - reroll a spirit's stats (inside band) and traits"
        };

        public static bool TryRun(string cmd, string[] args, Action<string> print)
        {
            switch (cmd)
            {
                case "stats": CmdStats(args, print); return true;
                case "traits": CmdTraits(args, print); return true;
                case "train": CmdTrain(args, print); return true;
                case "trainnow": CmdTrainNow(args, print); return true;
                case "gym": CmdGym(args, print); return true;
                case "bait": CmdBait(args, print); return true;
                case "reroll": CmdReroll(args, print); return true;
            }
            return false;
        }

        // ---- lookup ------------------------------------------------------------------

        private static string NameOf(SpiritAgent a) =>
            !string.IsNullOrEmpty(a.GivenName) ? a.GivenName
            : a.Species != null ? a.Species.displayName : "?";

        /// <summary>Spirits matching a name (given name, species name or id); null/empty/'all' = every spirit.</summary>
        private static List<SpiritAgent> Find(string key)
        {
            var result = new List<SpiritAgent>();
            var mgr = SpiritManager.Instance;
            if (mgr == null) return result;

            bool all = string.IsNullOrEmpty(key) || key.Equals("all", StringComparison.OrdinalIgnoreCase);
            var spirits = mgr.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null || a.Species == null) continue;
                if (all
                    || string.Equals(a.GivenName, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.Species.displayName, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.Species.id, key, StringComparison.OrdinalIgnoreCase))
                    result.Add(a);
            }
            return result;
        }

        private static string Describe(SpiritAgent a)
        {
            string traits = SpiritTraits.Describe(a.Traits);
            string s = NameOf(a) + " [" + (a.Species != null ? a.Species.id : "?") + "] "
                + SpiritStats.BandText(a.Species, SpiritStat.Vigor, a.Vigor) + ", "
                + SpiritStats.BandText(a.Species, SpiritStat.Grace, a.Grace) + ", "
                + SpiritStats.BandText(a.Species, SpiritStat.Gleam, a.Gleam)
                + " | traits: " + (traits.Length > 0 ? traits : "(none)")
                + " | train xp V" + a.GetTrainingTicks(SpiritStat.Vigor).ToString("0.0", CultureInfo.InvariantCulture)
                + " G" + a.GetTrainingTicks(SpiritStat.Grace).ToString("0.0", CultureInfo.InvariantCulture)
                + " L" + a.GetTrainingTicks(SpiritStat.Gleam).ToString("0.0", CultureInfo.InvariantCulture)
                + "/" + SpiritAgent.TicksPerStatPoint.ToString("0", CultureInfo.InvariantCulture);
            if (a.IsTraining) s += " | TRAINING";
            else if (a.TrainTarget != null) s += " | heading to " + a.TrainTarget.SelectableTitle;
            return s;
        }

        // ---- commands ------------------------------------------------------------------

        private static void CmdStats(string[] args, Action<string> print)
        {
            if (SpiritManager.Instance == null) { print("SpiritManager not available."); return; }
            var found = Find(args.Length >= 2 ? args[1] : null);
            if (found.Count == 0) { print("(no matching spirits)"); return; }
            for (int i = 0; i < found.Count; i++) print(Describe(found[i]));
        }

        private static void CmdTraits(string[] args, Action<string> print)
        {
            if (args.Length < 3)
            {
                var pool = SpiritTraits.All;
                var names = new List<string>();
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] != null) names.Add(pool[i].id);
                print("Trait pool: " + string.Join(", ", names));
                print("Usage: traits <name> <id> [id2]  (or 'roll')");
                return;
            }

            var found = Find(args[1]);
            if (found.Count == 0) { print("(no matching spirits)"); return; }

            bool roll = args[2].Equals("roll", StringComparison.OrdinalIgnoreCase);
            var ids = new List<string>();
            for (int i = 2; i < args.Length; i++) ids.Add(args[i]);

            if (!roll)
            {
                var parsed = SpiritTraits.FromIds(ids);
                if (parsed.Count == 0) { print("No known trait in: " + string.Join(", ", ids)); return; }
            }

            for (int i = 0; i < found.Count; i++)
            {
                if (roll) found[i].SetTraits(SpiritTraits.Join(SpiritTraits.Roll()).Split(','));
                else found[i].SetTraits(ids);
                print(Describe(found[i]));
            }
        }

        private static void CmdTrain(string[] args, Action<string> print)
        {
            if (args.Length < 3 || !SpiritStats.TryParse(args[2], out var stat))
            {
                print("Usage: train <name|all> <vigor|grace|gleam> [ticks]");
                return;
            }

            float ticks = SpiritAgent.TicksPerStatPoint;
            if (args.Length >= 4
                && (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out ticks) || ticks <= 0f))
            {
                print("Ticks must be a positive number.");
                return;
            }

            var found = Find(args[1]);
            if (found.Count == 0) { print("(no matching spirits)"); return; }
            for (int i = 0; i < found.Count; i++)
            {
                int before = found[i].GetStat(stat);
                int gained = found[i].AddTrainingTicks(stat, ticks);
                found[i].GetStatBand(stat, out _, out int max);
                print(NameOf(found[i]) + ": " + SpiritStats.Label(stat) + " " + before + " -> "
                    + found[i].GetStat(stat) + " (max " + max + ")" + (gained == 0 && before >= max ? " at cap" : ""));
            }
        }

        private static void CmdTrainNow(string[] args, Action<string> print)
        {
            var found = Find(args.Length >= 2 ? args[1] : null);
            for (int i = 0; i < found.Count; i++) found[i].Debug_ReadyToTrain();
            print("Cleared the training cooldown on " + found.Count + " spirit(s).");
        }

        private static void CmdGym(string[] args, Action<string> print)
        {
            var spec = args.Length >= 2 ? TrainingBuilding.FindSpec(args[1].ToLowerInvariant()) : null;
            if (spec == null)
            {
                var ids = new List<string>();
                foreach (var s in TrainingBuilding.Specs) ids.Add(s.id);
                print("Usage: gym <" + string.Join("|", ids) + ">");
                return;
            }

            Vector3 pos = Vector3.zero;
            var player = GameObject.FindWithTag("Player");
            if (player != null)
                pos = player.transform.position + new Vector3(2.5f, 0f, 0f);

            var grid = TerrainGrid.Instance;
            if (grid != null && grid.TryWorldToCell(pos, out var cell))
                pos = grid.CellCenterWorld(cell);

            TrainingBuilding.Create(spec, pos);
            print("Placed " + spec.displayName + " (free).");
        }

        private static void CmdBait(string[] args, Action<string> print)
        {
            if (args.Length < 2) { print("Usage: bait <food id|none>  (wheat, berry, bloom)"); return; }

            TrainingBuilding nearest = null;
            float best = float.MaxValue;
            var player = GameObject.FindWithTag("Player");
            Vector3 from = player != null ? player.transform.position : Vector3.zero;
            var all = TrainingBuilding.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                float d = (all[i].transform.position - from).sqrMagnitude;
                if (d < best) { best = d; nearest = all[i]; }
            }
            if (nearest == null) { print("No training buildings (try 'gym stones')."); return; }

            bool none = args[1].Equals("none", StringComparison.OrdinalIgnoreCase);
            nearest.Debug_SetBait(none ? "" : args[1].ToLowerInvariant(), TrainingBuilding.MaxBaitCharges);
            print(nearest.SelectableTitle + ": bait " + (none ? "cleared" : args[1] + " x" + TrainingBuilding.MaxBaitCharges));
        }

        private static void CmdReroll(string[] args, Action<string> print)
        {
            if (args.Length < 2) { print("Usage: reroll <name|all>"); return; }
            var found = Find(args[1]);
            if (found.Count == 0) { print("(no matching spirits)"); return; }
            for (int i = 0; i < found.Count; i++)
            {
                found[i].ApplyIdentity(null, SpiritStatBias.None);
                print(Describe(found[i]));
            }
        }
    }
}

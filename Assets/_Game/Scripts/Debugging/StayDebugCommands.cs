using System;
using AnimalFarm.Requirements;
using AnimalFarm.Spirits;

namespace AnimalFarm.Debugging
{
    /// <summary>
    /// Console commands for the stay decision (muscle 11): visitors decide to
    /// join the flock on their own once the species' Stay gate is met. Own file
    /// so DebugConsole only forwards unknown commands here (TryRun returns
    /// false when the command is not ours).
    /// </summary>
    public static class StayDebugCommands
    {
        public static readonly string[] HelpLines =
        {
            "stay [name|id]  - force a visitor's 'decides to stay' decision now (skips the gate and the dice; then the naming ceremony)",
            "staygate [id]   - show a species' Appear/Visit/Stay gate state with [x]/[ ] per Stay condition, plus its visitors' progress (no id = every species, one line each)"
        };

        public static bool TryRun(string cmd, string[] args, Action<string> print)
        {
            switch (cmd)
            {
                case "stay": CmdStay(args, print); return true;
                case "staygate": CmdStayGate(args, print); return true;
            }
            return false;
        }

        private static string NameOf(SpiritAgent a) =>
            !string.IsNullOrEmpty(a.GivenName) ? a.GivenName
            : a.Species != null ? a.Species.displayName : "?";

        private static bool Matches(SpiritAgent a, string key) =>
            string.IsNullOrEmpty(key)
            || string.Equals(NameOf(a), key, StringComparison.OrdinalIgnoreCase)
            || (a.Species != null && (string.Equals(a.Species.id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Species.displayName, key, StringComparison.OrdinalIgnoreCase)));

        private static void CmdStay(string[] args, Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }

            string key = args.Length >= 2 ? args[1] : null;
            SpiritAgent target = null;
            int visitors = 0;
            var all = mgr.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a.Species == null || a.State != SpiritState.Visitor) continue;
                visitors++;
                if (target == null && Matches(a, key)) target = a;
            }

            if (target == null)
            {
                print(visitors == 0
                    ? "No visitor on the land ('spawn <id>' makes one; 'staygate' shows what a species needs)."
                    : "No visitor matches '" + key + "'.");
                return;
            }

            print(target.Debug_ForceStay()
                ? NameOf(target) + " decides to stay - close the console to watch (it may wait for you to come near before the naming)."
                : NameOf(target) + " cannot decide right now (already deciding, or leaving).");
        }

        private static void CmdStayGate(string[] args, Action<string> print)
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { print("SpiritManager not available."); return; }

            var evaluator = RequirementEvaluator.Instance;
            string key = args.Length >= 2 ? args[1] : null;
            var species = mgr.KnownSpecies;
            int shown = 0;

            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                if (s == null || s.gateChain == null) continue; // woven cryptids have no gates
                if (!string.IsNullOrEmpty(key)
                    && !string.Equals(s.id, key, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(s.displayName, key, StringComparison.OrdinalIgnoreCase)) continue;

                string chain = s.gateChain.chainId;
                string gates = evaluator == null ? "(no evaluator)"
                    : "Appear " + (evaluator.IsGateOpen(chain, Gate.Appear) ? "OPEN" : "closed")
                    + ", Visit " + (evaluator.IsGateOpen(chain, Gate.Visit) ? "OPEN" : "closed")
                    + ", Stay " + (evaluator.IsGateOpen(chain, Gate.Resident) ? "OPEN" : "closed");
                print(s.id + ": " + gates + "  residents " + mgr.CountResidents(s.id) + "/" + s.maxResidents);
                shown++;

                if (string.IsNullOrEmpty(key)) continue; // overview: one line per species

                var lines = StayGate.StatusLines(s);
                for (int l = 0; l < lines.Count; l++) print("   Stay: " + lines[l]);

                var all = mgr.AllSpirits;
                int visitors = 0;
                for (int v = 0; v < all.Count; v++)
                {
                    var a = all[v];
                    if (a == null || a.Species != s || a.State != SpiritState.Visitor) continue;
                    print("   " + a.Debug_StayStatus());
                    visitors++;
                }
                if (visitors == 0) print("   (no visitor of this species on the land)");
            }

            if (shown == 0) print("(no matching species with gates)");
        }
    }
}

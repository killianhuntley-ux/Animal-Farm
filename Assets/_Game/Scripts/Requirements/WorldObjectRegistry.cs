using System.Collections.Generic;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// Lightweight presence registry for placed world objects (benches, ponds,
    /// mushroom logs, ...). Placed garden objects register themselves in later
    /// slices; conditions only ask "is it there right now?".
    /// </summary>
    public static class WorldObjectRegistry
    {
        private static readonly HashSet<string> _present = new HashSet<string>();

        public static void Register(string id)
        {
            if (!string.IsNullOrEmpty(id)) _present.Add(id);
        }

        public static void Unregister(string id)
        {
            if (!string.IsNullOrEmpty(id)) _present.Remove(id);
        }

        public static bool IsPresent(string id) =>
            !string.IsNullOrEmpty(id) && _present.Contains(id);
    }
}

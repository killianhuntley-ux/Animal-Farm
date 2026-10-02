using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>The three Nature stats (muscle 05: each a species band + individual roll).</summary>
    public enum SpiritStat { Vigor, Grace, Gleam }

    /// <summary>
    /// Per-stat roll bias for spawning a spirit with a skewed individual roll
    /// (weaving inheritance hook). Each value is -1..1: -1 pins the roll to the
    /// species minimum, 0 is a uniform roll inside the band, +1 pins it to the max.
    /// </summary>
    [System.Serializable]
    public struct SpiritStatBias
    {
        public float vigor, grace, gleam;

        public SpiritStatBias(float vigor, float grace, float gleam)
        {
            this.vigor = vigor;
            this.grace = grace;
            this.gleam = gleam;
        }

        public static SpiritStatBias None => default;

        /// <summary>The same bias on all three stats.</summary>
        public static SpiritStatBias Uniform(float bias) => new SpiritStatBias(bias, bias, bias);

        public float Get(SpiritStat stat) =>
            stat == SpiritStat.Vigor ? vigor : stat == SpiritStat.Grace ? grace : gleam;
    }

    /// <summary>Stat-band helpers shared by spawning, loading, training and the UI.</summary>
    public static class SpiritStats
    {
        /// <summary>Hard upper limit for any band.</summary>
        public const int Ceiling = 10;
        public const int Count = 3;

        public static string Label(SpiritStat stat) =>
            stat == SpiritStat.Vigor ? "Vigor" : stat == SpiritStat.Grace ? "Grace" : "Gleam";

        public static bool TryParse(string s, out SpiritStat stat)
        {
            stat = SpiritStat.Vigor;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "vigor": stat = SpiritStat.Vigor; return true;
                case "grace": stat = SpiritStat.Grace; return true;
                case "gleam": stat = SpiritStat.Gleam; return true;
            }
            return false;
        }

        /// <summary>(min, max) for a species stat; the legacy 2-9 when species is null.</summary>
        public static void Band(SpiritSpeciesDefinition species, SpiritStat stat, out int min, out int max)
        {
            if (species == null) { min = 2; max = 9; return; }
            species.GetBand(stat, out min, out max);
        }

        /// <summary>
        /// Individual roll inside the species band. <paramref name="bias"/> -1..1
        /// skews the roll toward the min (negative) or max (positive).
        /// </summary>
        public static int RollStat(SpiritSpeciesDefinition species, SpiritStat stat, float bias = 0f)
        {
            Band(species, stat, out int min, out int max);
            float t = Random.value;
            if (bias > 0f) t = Mathf.Lerp(t, 1f, Mathf.Clamp01(bias));
            else if (bias < 0f) t = Mathf.Lerp(t, 0f, Mathf.Clamp01(-bias));
            int v = min + Mathf.FloorToInt(t * (max - min + 1));
            return Mathf.Clamp(v, min, max);
        }

        public static int ClampToBand(SpiritSpeciesDefinition species, SpiritStat stat, int value)
        {
            Band(species, stat, out int min, out int max);
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>"Vigor 4 (3-7)" - the legible roll-in-band readout.</summary>
        public static string BandText(SpiritSpeciesDefinition species, SpiritStat stat, int value)
        {
            Band(species, stat, out int min, out int max);
            return Label(stat) + " " + value + " (" + min + "-" + max + ")";
        }
    }

    /// <summary>
    /// Trait pool access + rolling + the quirk/appetite weight lookups (muscle 05).
    /// The pool is the SpiritManager's authored assets; when a scene predates
    /// the wiring, an equivalent in-memory pool is built from
    /// <see cref="DefaultSpecs"/> so traits always work.
    /// </summary>
    public static class SpiritTraits
    {
        /// <summary>A spirit carries at most this many traits.</summary>
        public const int MaxTraits = 2;

        /// <summary>Plain-data trait definition: the single source for the asset authoring AND the fallback pool.</summary>
        public sealed class TraitSpec
        {
            public string id, name, flavor;
            public float weight = 1f;
            public string[] conflicts;
            public float nap = 1f, stretch = 1f, hop = 1f, leaf = 1f;
            public float train = 1f, vigor = 1f, grace = 1f, gleam = 1f, bait = 1f;
        }

        public static readonly TraitSpec[] DefaultSpecs =
        {
            new TraitSpec { id = "brave", name = "Brave",
                flavor = "Marches toward whatever just squeaked.",
                conflicts = new[] { "skittish" },
                nap = 0.8f, leaf = 1.2f, train = 1.2f, vigor = 1.6f },
            new TraitSpec { id = "lazy", name = "Lazy",
                flavor = "Would nap through the end of the underworld.",
                conflicts = new[] { "rowdy", "curious" },
                nap = 2.2f, stretch = 1.4f, hop = 0.5f, leaf = 0.5f, train = 0.45f, bait = 1.3f },
            new TraitSpec { id = "greedy", name = "Greedy",
                flavor = "Knows exactly where the snacks are kept.",
                bait = 2.5f },
            new TraitSpec { id = "skittish", name = "Skittish",
                flavor = "Startles at its own shadow, and trains to outrun it.",
                conflicts = new[] { "brave" },
                nap = 0.7f, hop = 1.3f, leaf = 1.3f, train = 0.9f, vigor = 0.7f, grace = 1.6f },
            new TraitSpec { id = "showoff", name = "Show-off",
                flavor = "Practises its best hop wherever you happen to be looking.",
                nap = 0.7f, stretch = 1.3f, hop = 1.8f, train = 1.1f, gleam = 1.9f },
            new TraitSpec { id = "curious", name = "Curious",
                flavor = "Investigates every leaf. Twice.",
                conflicts = new[] { "dreamy", "lazy" },
                nap = 0.8f, leaf = 1.9f, train = 1.3f },
            new TraitSpec { id = "dreamy", name = "Dreamy",
                flavor = "Drifts off mid-thought. Mid-hop, even.",
                conflicts = new[] { "curious" },
                nap = 1.5f, stretch = 1.2f, leaf = 0.7f, train = 0.8f, gleam = 1.3f },
            new TraitSpec { id = "rowdy", name = "Rowdy",
                flavor = "Cannot pass a heavy rock without shoving it.",
                conflicts = new[] { "lazy", "gentle" },
                nap = 0.6f, hop = 1.4f, leaf = 1.4f, train = 1.4f, vigor = 1.3f, grace = 1.2f },
            new TraitSpec { id = "gentle", name = "Gentle",
                flavor = "Moves as if apologising to the grass.",
                conflicts = new[] { "rowdy" },
                nap = 1.2f, stretch = 1.5f, hop = 0.7f, train = 0.9f, grace = 1.3f }
        };

        /// <summary>Copies a spec into an asset/instance (editor authoring + runtime fallback).</summary>
        public static void Apply(TraitSpec s, SpiritTraitDefinition d)
        {
            d.id = s.id;
            d.displayName = s.name;
            d.flavor = s.flavor;
            d.rollWeight = s.weight;
            d.conflictsWith = s.conflicts;
            d.napMul = s.nap;
            d.stretchMul = s.stretch;
            d.hopMul = s.hop;
            d.leafChaseMul = s.leaf;
            d.trainingAppetite = s.train;
            d.vigorAppetite = s.vigor;
            d.graceAppetite = s.grace;
            d.gleamAppetite = s.gleam;
            d.baitAppetite = s.bait;
        }

        // ---- pool ----------------------------------------------------------------

        private static List<SpiritTraitDefinition> _fallback;

        /// <summary>The live trait pool: manager assets, else the in-memory defaults.</summary>
        public static IReadOnlyList<SpiritTraitDefinition> All
        {
            get
            {
                var mgr = SpiritManager.Instance;
                var pool = mgr != null ? mgr.TraitPool : null;
                if (pool != null && pool.Count > 0) return pool;

                if (_fallback == null || _fallback.Count == 0 || _fallback[0] == null)
                {
                    _fallback = new List<SpiritTraitDefinition>(DefaultSpecs.Length);
                    for (int i = 0; i < DefaultSpecs.Length; i++)
                    {
                        var d = ScriptableObject.CreateInstance<SpiritTraitDefinition>();
                        d.hideFlags = HideFlags.HideAndDontSave;
                        Apply(DefaultSpecs[i], d);
                        _fallback.Add(d);
                    }
                }
                return _fallback;
            }
        }

        public static SpiritTraitDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        // ---- rolling -------------------------------------------------------------

        /// <summary>
        /// Rolls a trait set. <paramref name="total"/> = how many traits the
        /// result should hold (0 = the normal spawn roll: 1 trait 60%, 2 traits
        /// 40%; capped at <see cref="MaxTraits"/>). <paramref name="keep"/> are
        /// traits already decided (e.g. inherited) - they stay, and the rest is
        /// rolled around them, never creating a conflicting pair.
        /// </summary>
        public static List<SpiritTraitDefinition> Roll(int total = 0,
            IReadOnlyList<SpiritTraitDefinition> keep = null)
        {
            var result = new List<SpiritTraitDefinition>(MaxTraits);
            if (keep != null)
            {
                for (int i = 0; i < keep.Count && result.Count < MaxTraits; i++)
                    if (keep[i] != null && !result.Contains(keep[i]) && !ConflictsAny(result, keep[i]))
                        result.Add(keep[i]);
            }

            int want = total <= 0 ? (Random.value < 0.6f ? 1 : 2) : Mathf.Min(total, MaxTraits);
            var all = All;
            while (result.Count < want)
            {
                float sum = 0f;
                for (int i = 0; i < all.Count; i++)
                    if (IsCandidate(result, all[i])) sum += Mathf.Max(0f, all[i].rollWeight);
                if (sum <= 0f) break;

                float roll = Random.value * sum;
                SpiritTraitDefinition picked = null;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!IsCandidate(result, all[i])) continue;
                    picked = all[i];
                    roll -= Mathf.Max(0f, all[i].rollWeight);
                    if (roll < 0f) break;
                }
                if (picked == null) break;
                result.Add(picked);
            }
            return result;
        }

        private static bool IsCandidate(List<SpiritTraitDefinition> have, SpiritTraitDefinition t) =>
            t != null && !string.IsNullOrEmpty(t.id) && t.rollWeight > 0f
            && !have.Contains(t) && !ConflictsAny(have, t);

        private static bool ConflictsAny(List<SpiritTraitDefinition> have, SpiritTraitDefinition t)
        {
            for (int i = 0; i < have.Count; i++)
                if (have[i].ConflictsWithId(t.id) || t.ConflictsWithId(have[i].id)) return true;
            return false;
        }

        /// <summary>Ids to definitions (unknown ids dropped, duplicates dropped, capped at MaxTraits).</summary>
        public static List<SpiritTraitDefinition> FromIds(IEnumerable<string> ids)
        {
            var result = new List<SpiritTraitDefinition>(MaxTraits);
            if (ids == null) return result;
            foreach (var id in ids)
            {
                var t = Find(id != null ? id.Trim() : null);
                if (t != null && !result.Contains(t)) result.Add(t);
                if (result.Count >= MaxTraits) break;
            }
            return result;
        }

        // ---- save strings ----------------------------------------------------------

        public static string Join(IReadOnlyList<SpiritTraitDefinition> traits)
        {
            if (traits == null || traits.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < traits.Count; i++)
            {
                if (traits[i] == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(traits[i].id);
            }
            return sb.ToString();
        }

        public static string[] Split(string joined) =>
            string.IsNullOrEmpty(joined)
                ? System.Array.Empty<string>()
                : joined.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);

        // ---- weight lookups ----------------------------------------------------------

        /// <summary>Combined multiplier the spirit's traits apply to one quirk's pick weight.</summary>
        public static float QuirkMul(IReadOnlyList<SpiritTraitDefinition> traits, SpiritQuirks.Kind kind)
        {
            float m = 1f;
            if (traits == null) return m;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (t == null) continue;
                switch (kind)
                {
                    case SpiritQuirks.Kind.Nap: m *= t.napMul; break;
                    case SpiritQuirks.Kind.Stretch: m *= t.stretchMul; break;
                    case SpiritQuirks.Kind.Hop: m *= t.hopMul; break;
                    case SpiritQuirks.Kind.LeafChase: m *= t.leafChaseMul; break;
                }
            }
            return m;
        }

        /// <summary>Combined general x per-stat training appetite multiplier.</summary>
        public static float Appetite(IReadOnlyList<SpiritTraitDefinition> traits, SpiritStat stat)
        {
            float m = 1f;
            if (traits == null) return m;
            for (int i = 0; i < traits.Count; i++)
                if (traits[i] != null) m *= traits[i].trainingAppetite * traits[i].StatAppetite(stat);
            return m;
        }

        public static float BaitAppetite(IReadOnlyList<SpiritTraitDefinition> traits)
        {
            float m = 1f;
            if (traits == null) return m;
            for (int i = 0; i < traits.Count; i++)
                if (traits[i] != null) m *= traits[i].baitAppetite;
            return m;
        }

        // ---- display ---------------------------------------------------------------

        /// <summary>"Brave, Lazy" (empty when none).</summary>
        public static string Describe(IReadOnlyList<SpiritTraitDefinition> traits)
        {
            if (traits == null || traits.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < traits.Count; i++)
            {
                if (traits[i] == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(traits[i].displayName);
            }
            return sb.ToString();
        }

        /// <summary>One flavor line per trait, newline-separated: "Brave - Marches toward...".</summary>
        public static string FlavorLines(IReadOnlyList<SpiritTraitDefinition> traits)
        {
            if (traits == null || traits.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (t == null) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(t.displayName).Append(" - ").Append(t.flavor);
            }
            return sb.ToString();
        }
    }
}

using System;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Muscle 02 verdict 1: the 5-step spirit biome affinity scale. Hard No
    /// (won't live there) / Dislike (will live, unhappy) / Neutral / Like /
    /// Love. Values are ordered so a higher number is a warmer feeling.
    /// </summary>
    public enum Affinity
    {
        HardNo = 0,
        Dislike = 1,
        Neutral = 2,
        Like = 3,
        Love = 4
    }

    /// <summary>One authored row on a species: how it feels about one biome.</summary>
    [Serializable]
    public struct BiomeAffinityEntry
    {
        public BiomeType biome;
        public Affinity affinity;
    }

    /// <summary>
    /// Read side of the affinity table authored on
    /// <see cref="SpiritSpeciesDefinition.biomeAffinities"/>. Biomes with no
    /// row read as Neutral, so un-authored species stay unaffected. First
    /// consumer is road travel (RoadTravel: biome-mismatch strain); later
    /// agents can wire residency / lure weighting through the same helpers.
    /// </summary>
    public static class BiomeAffinity
    {
        /// <summary>The species' feeling about a biome (Neutral when unauthored).</summary>
        public static Affinity For(SpiritSpeciesDefinition species, BiomeType biome)
        {
            if (species == null || species.biomeAffinities == null) return Affinity.Neutral;
            var rows = species.biomeAffinities;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].biome == biome) return rows[i].affinity;
            return Affinity.Neutral;
        }

        /// <summary>
        /// Mood multiplier for GAINS while standing in a biome (comfort scales
        /// with affinity): HardNo 0, Dislike 0.5, Neutral 1, Like 1.25, Love 1.5.
        /// </summary>
        public static float MoodMultiplier(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return 0f;
                case Affinity.Dislike: return 0.5f;
                case Affinity.Like: return 1.25f;
                case Affinity.Love: return 1.5f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Spirit points drained per REAL second while a spirit is walked
        /// through a biome it dislikes (road strain). Like/Neutral/Love drain
        /// nothing. ASSUMPTION: tuned for a ~12-unit road crossing of several
        /// seconds, so a Dislike crossing costs a few points, a Hard No more.
        /// </summary>
        public static float StrainPerSecond(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return 2.2f;
                case Affinity.Dislike: return 0.9f;
                default: return 0f;
            }
        }

        /// <summary>True when the species would refuse to live in this biome.</summary>
        public static bool WontLiveIn(SpiritSpeciesDefinition species, BiomeType biome) =>
            For(species, biome) == Affinity.HardNo;

        /// <summary>
        /// The biome a species loves most (its natural home), or Barren when it
        /// loves nothing. Used to pick where wild silhouettes of the species
        /// appear (swamp species at the swamp base).
        /// </summary>
        public static BiomeType NativeBiome(SpiritSpeciesDefinition species)
        {
            if (species == null || species.biomeAffinities == null) return BiomeType.Barren;
            var best = BiomeType.Barren;
            int bestScore = (int)Affinity.Like; // must beat "Like" to count as native: only Love
            var rows = species.biomeAffinities;
            for (int i = 0; i < rows.Length; i++)
            {
                if ((int)rows[i].affinity > bestScore)
                {
                    bestScore = (int)rows[i].affinity;
                    best = rows[i].biome;
                }
            }
            return best;
        }

        /// <summary>
        /// Relative chance weight for a silhouette to appear at a base whose
        /// biome the species feels this way about. HardNo never appears there.
        /// ASSUMPTION: Love 6 / Like 3 / Neutral 1 / Dislike 0.2 / HardNo 0.
        /// </summary>
        public static float SpawnWeight(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return 0f;
                case Affinity.Dislike: return 0.2f;
                case Affinity.Like: return 3f;
                case Affinity.Love: return 6f;
                default: return 1f;
            }
        }

        /// <summary>Player-facing biome name ("Swamp", "Desert", ...).</summary>
        public static string BiomeName(BiomeType b)
        {
            switch (b)
            {
                case BiomeType.Grassland: return "Grassland";
                case BiomeType.Swamp: return "Swamp";
                case BiomeType.Desert: return "Desert";
                default: return "Barren ground";
            }
        }

        /// <summary>
        /// One-line journal summary of a species' feelings, warmest first:
        /// "Loves Swamp, Likes Grassland, Hates Desert". Neutral and Barren
        /// rows are skipped; an empty table reads as easygoing.
        /// </summary>
        public static string Describe(SpiritSpeciesDefinition species)
        {
            if (species == null || species.biomeAffinities == null || species.biomeAffinities.Length == 0)
                return "Easygoing about every biome";

            string text = "";
            for (int step = (int)Affinity.Love; step >= (int)Affinity.HardNo; step--)
            {
                var a = (Affinity)step;
                if (a == Affinity.Neutral) continue;

                var rows = species.biomeAffinities;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i].affinity != a || rows[i].biome == BiomeType.Barren) continue;
                    if (text.Length > 0) text += ", ";
                    text += Verb(a) + " " + BiomeName(rows[i].biome);
                }
            }
            return text.Length > 0 ? text : "Easygoing about every biome";
        }

        /// <summary>"Loves" / "Likes" / "Dislikes" / "Hates" (Neutral: "Shrugs at").</summary>
        public static string Verb(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return "Hates";
                case Affinity.Dislike: return "Dislikes";
                case Affinity.Like: return "Likes";
                case Affinity.Love: return "Loves";
                default: return "Shrugs at";
            }
        }

        /// <summary>How the ground itself feels to a spirit standing on it (inspect panel).</summary>
        public static string GroundFeeling(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return "this ground feels wrong";
                case Affinity.Dislike: return "uneasy here";
                case Affinity.Like: return "comfortable here";
                case Affinity.Love: return "at home here";
                default: return "fine here";
            }
        }

        /// <summary>Short label for UI / debug output.</summary>
        public static string Label(Affinity a)
        {
            switch (a)
            {
                case Affinity.HardNo: return "hard no";
                case Affinity.Dislike: return "dislike";
                case Affinity.Like: return "like";
                case Affinity.Love: return "love";
                default: return "neutral";
            }
        }
    }
}
